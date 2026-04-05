using System;
using System.Collections.Generic;
using System.Numerics;
using LeaderDecoder.Models;

namespace LeaderDecoder.Services
{
    /// <summary>
    /// LEADER NAVIGATION KERNEL v1.2
    /// Motion-first spatial analysis:
    /// - derives travel direction from coordinate deltas, not unit facing
    /// - keeps a breadcrumb trail for trailing-point pursuit
    /// - resets motion history cleanly across zone changes / teleports
    /// </summary>
    public class NavigationKernel
    {
        private const int MaxHistory = 48;
        private const int MaxDirectionSegments = 8;
        private const float MinMovementThreshold = 0.08f;
        private const float MinTravelSpeed = 0.35f;
        private const float MaxTeleportDistance = 35.0f;
        private const double MaxSegmentGapSeconds = 0.75;
        private const float DefaultPredictionSeconds = 0.20f;

        private readonly Dictionary<int, List<MotionSample>> _history = new();
        private readonly Dictionary<int, byte> _lastZoneHash = new();

        private readonly record struct MotionSample(Vector2 Position, DateTime Timestamp);

        /// <summary>
        /// Updates motion history and zone-change state for a given slot.
        /// Returns true if a zone-change was detected (caller should abort pursuit).
        /// </summary>
        public bool UpdateHeading(int slot, GameState state)
        {
            if (!state.IsValid)
            {
                return false;
            }

            bool zoneChanged = false;
            if (_lastZoneHash.TryGetValue(slot, out byte prevZone) && prevZone != state.ZoneHash)
            {
                zoneChanged = true;
                if (_history.ContainsKey(slot))
                {
                    _history[slot].Clear();
                }
            }

            _lastZoneHash[slot] = state.ZoneHash;

            Vector2 currentPos = new(state.CoordX, state.CoordZ);
            List<MotionSample> history = GetHistory(slot);
            DateTime now = DateTime.UtcNow;

            if (history.Count > 0)
            {
                MotionSample previous = history[^1];
                double gapSeconds = (now - previous.Timestamp).TotalSeconds;
                float displacement = Vector2.Distance(previous.Position, currentPos);
                if (gapSeconds > MaxSegmentGapSeconds || displacement >= MaxTeleportDistance)
                {
                    history.Clear();
                }
            }

            if (history.Count > 0)
            {
                MotionSample previous = history[^1];
                double dt = (now - previous.Timestamp).TotalSeconds;
                if (dt > 0 && dt <= MaxSegmentGapSeconds)
                {
                    Vector2 velocity = (currentPos - previous.Position) / (float)dt;
                    state.VelocityX = velocity.X;
                    state.VelocityZ = velocity.Y;
                }
                else
                {
                    state.VelocityX = 0f;
                    state.VelocityZ = 0f;
                }
            }
            else
            {
                state.VelocityX = 0f;
                state.VelocityZ = 0f;
            }

            history.Add(new MotionSample(currentPos, now));
            if (history.Count > MaxHistory)
            {
                history.RemoveAt(0);
            }

            Vector2 smoothedVelocity = ComputeSmoothedVelocity(history);
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
        /// Resolves a stable follow point by trailing backwards along the leader's recent path.
        /// Falls back to the leader position when no usable breadcrumb trail exists.
        /// </summary>
        public (float X, float Z) ResolveFollowTarget(
            int slot,
            GameState leader,
            float trailingDistance,
            float predictionSeconds = DefaultPredictionSeconds)
        {
            Vector2 anchor = new(leader.CoordX, leader.CoordZ);
            if (leader.HasTravelVector)
            {
                anchor += new Vector2(leader.SmoothedVelocityX, leader.SmoothedVelocityZ) * Math.Max(0f, predictionSeconds);
            }

            if (!_history.TryGetValue(slot, out List<MotionSample>? history) || history.Count == 0 || trailingDistance <= 0f)
            {
                return (anchor.X, anchor.Y);
            }

            Vector2 current = anchor;
            float remaining = trailingDistance;

            for (int index = history.Count - 1; index >= 0; index--)
            {
                Vector2 older = history[index].Position;
                float segmentLength = Vector2.Distance(current, older);

                if (segmentLength < 0.001f)
                {
                    current = older;
                    continue;
                }

                if (segmentLength >= remaining)
                {
                    float t = remaining / segmentLength;
                    Vector2 point = Vector2.Lerp(current, older, t);
                    return (point.X, point.Y);
                }

                remaining -= segmentLength;
                current = older;
            }

            Vector2 oldest = history[0].Position;
            return (oldest.X, oldest.Y);
        }

        /// <summary>
        /// Returns the bearing angle (radians) a follower must face to move toward the leader.
        /// Accounts for RIFT's coordinate system: X+ = East, Z+ = South.
        /// </summary>
        public float CalculateBearingToLeader(GameState follower, GameState leader)
        {
            float dx = leader.CoordX - follower.CoordX;
            float dz = leader.CoordZ - follower.CoordZ;
            return NormalizeAngle((float)Math.Atan2(dx, dz));
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

        private List<MotionSample> GetHistory(int slot)
        {
            if (!_history.TryGetValue(slot, out List<MotionSample>? history))
            {
                history = new List<MotionSample>(MaxHistory);
                _history[slot] = history;
            }

            return history;
        }

        private static Vector2 ComputeSmoothedVelocity(List<MotionSample> history)
        {
            if (history.Count < 2)
            {
                return Vector2.Zero;
            }

            Vector2 weightedVelocity = Vector2.Zero;
            float totalWeight = 0f;
            int usedSegments = 0;

            for (int index = history.Count - 1; index > 0 && usedSegments < MaxDirectionSegments; index--)
            {
                MotionSample newer = history[index];
                MotionSample older = history[index - 1];

                double dt = (newer.Timestamp - older.Timestamp).TotalSeconds;
                if (dt <= 0 || dt > MaxSegmentGapSeconds)
                {
                    continue;
                }

                Vector2 delta = newer.Position - older.Position;
                if (delta.Length() < MinMovementThreshold)
                {
                    continue;
                }

                float weight = MaxDirectionSegments - usedSegments;
                Vector2 velocity = delta / (float)dt;
                weightedVelocity += velocity * weight;
                totalWeight += weight;
                usedSegments++;
            }

            if (totalWeight <= 0f)
            {
                return Vector2.Zero;
            }

            return weightedVelocity / totalWeight;
        }

        private static float NormalizeAngle(float angle)
        {
            while (angle > Math.PI) angle -= (float)(2 * Math.PI);
            while (angle < -Math.PI) angle += (float)(2 * Math.PI);
            return angle;
        }
    }
}
