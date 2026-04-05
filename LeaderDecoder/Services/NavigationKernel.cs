using System;
using System.Collections.Generic;
using System.Numerics;
using LeaderDecoder.Models;

namespace LeaderDecoder.Services
{
    /// <summary>
    /// LEADER NAVIGATION KERNEL v1.3
    /// Motion-first spatial analysis for coordinate-only following.
    ///
    /// Core responsibilities:
    /// 1) Estimate target travel direction from coordinate deltas.
    /// 2) Smooth that travel direction with an EMA so idle jitter does not whip the controller.
    /// 3) Preserve a breadcrumb history so followers trail the leader's path, not the leader's body.
    /// 4) Reset all derived motion state on zone transitions, teleports, and long sampling gaps.
    /// </summary>
    public class NavigationKernel
    {
        private const int MaxHistory = 48;
        private const int FollowerMatchHistorySamples = 6;
        private const float FollowerTrailLookahead = 2.0f;
        private const float TrailProjectionMatchRadius = 1.25f;
        private const float MinMovementThreshold = 0.08f;
        private const float MinTravelSpeed = 0.35f;
        private const float MaxTeleportDistance = 35.0f;
        private const double MaxSegmentGapSeconds = 0.75;
        private const float VelocityEmaBeta = 0.55f;
        private const float VelocityDecay = 0.60f;
        private const float DefaultPredictionSeconds = 0.20f;

        private readonly Dictionary<int, List<MotionSample>> _history = new();
        private readonly Dictionary<int, Vector2> _smoothedVelocity = new();
        private readonly Dictionary<int, byte> _lastZoneHash = new();

        private readonly record struct MotionSample(Vector2 Position, DateTime Timestamp);
        private readonly record struct TrailProjection(Vector2 Point, float DistanceFromAnchor, float DistanceToTrail);

        /// <summary>
        /// Updates per-slot motion state.
        /// Returns true when the slot appears to have crossed a zone boundary and the caller should abort pursuit.
        /// </summary>
        public bool UpdateHeading(int slot, GameState state)
        {
            if (!state.IsValid)
            {
                return false;
            }

            bool zoneChanged = DetectAndResetZoneTransition(slot, state.ZoneHash);
            List<MotionSample> history = GetHistory(slot);
            Vector2 currentPosition = new(state.CoordX, state.CoordZ);
            DateTime now = DateTime.UtcNow;

            bool hasMeasuredVelocity = false;
            Vector2 measuredVelocity = Vector2.Zero;

            if (history.Count > 0)
            {
                MotionSample previous = history[^1];
                double dt = (now - previous.Timestamp).TotalSeconds;
                float displacement = Vector2.Distance(previous.Position, currentPosition);

                if (dt <= 0 || dt > MaxSegmentGapSeconds || displacement >= MaxTeleportDistance)
                {
                    history.Clear();
                    _smoothedVelocity.Remove(slot);
                }
                else if (displacement >= MinMovementThreshold)
                {
                    measuredVelocity = (currentPosition - previous.Position) / (float)dt;
                    hasMeasuredVelocity = true;
                }
            }

            state.VelocityX = hasMeasuredVelocity ? measuredVelocity.X : 0f;
            state.VelocityZ = hasMeasuredVelocity ? measuredVelocity.Y : 0f;

            UpdateSmoothedVelocity(slot, measuredVelocity, hasMeasuredVelocity);

            history.Add(new MotionSample(currentPosition, now));
            if (history.Count > MaxHistory)
            {
                history.RemoveAt(0);
            }

            Vector2 smoothedVelocity = _smoothedVelocity.TryGetValue(slot, out Vector2 cached)
                ? cached
                : Vector2.Zero;

            state.SmoothedVelocityX = smoothedVelocity.X;
            state.SmoothedVelocityZ = smoothedVelocity.Y;
            state.TravelSpeed = smoothedVelocity.Length();
            state.HasTravelVector = state.TravelSpeed >= MinTravelSpeed;

            if (state.HasTravelVector)
            {
                state.EstimatedHeading = NormalizeAngle((float)Math.Atan2(smoothedVelocity.X, smoothedVelocity.Y));
                state.IsHeadingLocked = true;
            }
            else
            {
                state.EstimatedHeading = 0f;
                state.IsHeadingLocked = false;
            }

            return zoneChanged;
        }

        /// <summary>
        /// Resolves the world-space pursuit goal for a leader slot.
        ///
        /// The anchor point starts at the leader's current position, optionally nudged forward by a short
        /// dead-reckoning prediction horizon. We then walk backwards along the leader's breadcrumb trail to
        /// find the point that sits the requested trailing distance behind the anchor.
        /// </summary>
        public (float X, float Z) ResolveFollowTarget(
            int slot,
            GameState leader,
            float trailingDistance,
            float predictionSeconds = DefaultPredictionSeconds)
        {
            return ResolveFollowTarget(slot, null, leader, null, trailingDistance, predictionSeconds);
        }

        /// <summary>
        /// Resolves a pursuit goal that is still anchored to the leader's breadcrumb trail, but can bias the
        /// target toward the follower's matched progress on that same trail. This keeps far-behind followers
        /// chasing a short local carrot along the path instead of cutting directly toward the final trailing
        /// point behind the leader.
        /// </summary>
        public (float X, float Z) ResolveFollowTarget(
            int leaderSlot,
            int? followerSlot,
            GameState leader,
            GameState? follower,
            float trailingDistance,
            float predictionSeconds = DefaultPredictionSeconds)
        {
            Vector2 anchor = ResolveLeaderAnchor(leader, predictionSeconds);
            List<Vector2> trail = BuildTrailPoints(leaderSlot, anchor);

            if (trail.Count == 0 || trailingDistance <= 0f)
            {
                return (anchor.X, anchor.Y);
            }

            float targetDistanceFromAnchor = trailingDistance;
            TrailProjection? followerProjection = TryResolveFollowerProjection(followerSlot, follower, trail);
            if (followerProjection is TrailProjection projection
                && projection.DistanceToTrail <= TrailProjectionMatchRadius
                && projection.DistanceFromAnchor > trailingDistance + FollowerTrailLookahead)
            {
                targetDistanceFromAnchor = Math.Max(
                    trailingDistance,
                    projection.DistanceFromAnchor - FollowerTrailLookahead);
            }

            Vector2 point = ResolvePointAlongTrailFromAnchor(trail, targetDistanceFromAnchor);
            return (point.X, point.Y);
        }

        /// <summary>
        /// Flat (2D) distance between two states — ignores elevation.
        /// </summary>
        public float CalculateDistance(GameState a, GameState b)
        {
            float dx = a.CoordX - b.CoordX;
            float dz = a.CoordZ - b.CoordZ;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// Full 3D distance — includes elevation (Y axis). Use for terrain checks.
        /// </summary>
        public float Calculate3DDistance(GameState a, GameState b)
        {
            float dx = a.CoordX - b.CoordX;
            float dy = a.CoordY - b.CoordY;
            float dz = a.CoordZ - b.CoordZ;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>
        /// Checks if the elevation difference between two states exceeds a threshold.
        /// Useful for detecting walls/ledges that would require Z-axis navigation.
        /// </summary>
        public bool IsElevationBlocked(GameState a, GameState b, float maxDelta = 3.0f)
        {
            return Math.Abs(a.CoordY - b.CoordY) > maxDelta;
        }

        private bool DetectAndResetZoneTransition(int slot, byte zoneHash)
        {
            bool zoneChanged = false;
            if (_lastZoneHash.TryGetValue(slot, out byte previousZone) && previousZone != zoneHash)
            {
                zoneChanged = true;
                if (_history.TryGetValue(slot, out List<MotionSample>? history))
                {
                    history.Clear();
                }
                _smoothedVelocity.Remove(slot);
            }

            _lastZoneHash[slot] = zoneHash;
            return zoneChanged;
        }

        private void UpdateSmoothedVelocity(int slot, Vector2 measuredVelocity, bool hasMeasuredVelocity)
        {
            if (hasMeasuredVelocity)
            {
                if (_smoothedVelocity.TryGetValue(slot, out Vector2 previous))
                {
                    _smoothedVelocity[slot] = Vector2.Lerp(previous, measuredVelocity, VelocityEmaBeta);
                }
                else
                {
                    _smoothedVelocity[slot] = measuredVelocity;
                }

                return;
            }

            if (_smoothedVelocity.TryGetValue(slot, out Vector2 cached))
            {
                Vector2 decayed = cached * VelocityDecay;
                if (decayed.Length() < MinTravelSpeed * 0.5f)
                {
                    _smoothedVelocity.Remove(slot);
                }
                else
                {
                    _smoothedVelocity[slot] = decayed;
                }
            }
        }

        private List<MotionSample> GetHistory(int slot)
        {
            if (!_history.TryGetValue(slot, out List<MotionSample>? history))
            {
                history = new List<MotionSample>(MaxHistory);
                _history[slot] = history;
            }

            return history;
        }

        private static Vector2 ResolveLeaderAnchor(GameState leader, float predictionSeconds)
        {
            Vector2 anchor = new(leader.CoordX, leader.CoordZ);
            if (leader.HasTravelVector)
            {
                Vector2 predictedOffset = new(leader.SmoothedVelocityX, leader.SmoothedVelocityZ);
                anchor += predictedOffset * Math.Max(0f, predictionSeconds);
            }

            return anchor;
        }

        private List<Vector2> BuildTrailPoints(int slot, Vector2 anchor)
        {
            var points = new List<Vector2>(MaxHistory + 1);
            if (_history.TryGetValue(slot, out List<MotionSample>? history))
            {
                foreach (MotionSample sample in history)
                {
                    points.Add(sample.Position);
                }
            }

            if (points.Count == 0 || Vector2.Distance(points[^1], anchor) > 0.001f)
            {
                points.Add(anchor);
            }

            return points;
        }

        private TrailProjection? TryResolveFollowerProjection(int? followerSlot, GameState? follower, List<Vector2> trail)
        {
            if (follower is null)
            {
                return null;
            }

            TrailProjection bestProjection = ProjectOntoTrail(trail, new Vector2(follower.CoordX, follower.CoordZ));
            if (!followerSlot.HasValue || !_history.TryGetValue(followerSlot.Value, out List<MotionSample>? followerHistory))
            {
                return bestProjection;
            }

            int sampleCount = Math.Min(FollowerMatchHistorySamples, followerHistory.Count);
            for (int i = followerHistory.Count - 1; i >= followerHistory.Count - sampleCount; i--)
            {
                TrailProjection candidate = ProjectOntoTrail(trail, followerHistory[i].Position);
                if (candidate.DistanceToTrail <= TrailProjectionMatchRadius
                    && candidate.DistanceFromAnchor < bestProjection.DistanceFromAnchor)
                {
                    bestProjection = candidate;
                }
            }

            return bestProjection;
        }

        private static Vector2 ResolvePointAlongTrailFromAnchor(List<Vector2> trail, float distanceFromAnchor)
        {
            if (trail.Count == 0)
            {
                return Vector2.Zero;
            }

            if (trail.Count == 1)
            {
                return trail[0];
            }

            Vector2 current = trail[^1];
            float remaining = Math.Max(0f, distanceFromAnchor);

            for (int index = trail.Count - 2; index >= 0; index--)
            {
                Vector2 older = trail[index];
                float segmentLength = Vector2.Distance(current, older);

                if (segmentLength < 0.001f)
                {
                    current = older;
                    continue;
                }

                if (segmentLength >= remaining)
                {
                    float t = remaining / segmentLength;
                    return Vector2.Lerp(current, older, t);
                }

                remaining -= segmentLength;
                current = older;
            }

            return trail[0];
        }

        private static TrailProjection ProjectOntoTrail(List<Vector2> trail, Vector2 sample)
        {
            if (trail.Count == 0)
            {
                return new TrailProjection(sample, 0f, float.MaxValue);
            }

            if (trail.Count == 1)
            {
                float distance = Vector2.Distance(sample, trail[0]);
                return new TrailProjection(trail[0], 0f, distance);
            }

            float totalLength = 0f;
            for (int i = 1; i < trail.Count; i++)
            {
                totalLength += Vector2.Distance(trail[i - 1], trail[i]);
            }

            TrailProjection best = new(trail[^1], 0f, Vector2.Distance(sample, trail[^1]));
            float traversed = 0f;

            for (int i = 0; i < trail.Count - 1; i++)
            {
                Vector2 start = trail[i];
                Vector2 end = trail[i + 1];
                Vector2 delta = end - start;
                float segmentLength = delta.Length();
                if (segmentLength < 0.001f)
                {
                    continue;
                }

                float t = Math.Clamp(Vector2.Dot(sample - start, delta) / delta.LengthSquared(), 0f, 1f);
                Vector2 point = Vector2.Lerp(start, end, t);
                float projectionDistance = Vector2.Distance(sample, point);
                float distanceFromStart = traversed + (segmentLength * t);
                float distanceFromAnchor = Math.Max(0f, totalLength - distanceFromStart);

                if (projectionDistance < best.DistanceToTrail - 0.0001f
                    || (Math.Abs(projectionDistance - best.DistanceToTrail) <= 0.0001f
                        && distanceFromAnchor < best.DistanceFromAnchor))
                {
                    best = new TrailProjection(point, distanceFromAnchor, projectionDistance);
                }

                traversed += segmentLength;
            }

            return best;
        }

        private static float NormalizeAngle(float angle)
        {
            while (angle > Math.PI) angle -= (float)(2 * Math.PI);
            while (angle < -Math.PI) angle += (float)(2 * Math.PI);
            return angle;
        }
    }
}
