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
            Vector2 anchor = new(leader.CoordX, leader.CoordZ);
            if (leader.HasTravelVector)
            {
                Vector2 predictedOffset = new(leader.SmoothedVelocityX, leader.SmoothedVelocityZ);
                anchor += predictedOffset * Math.Max(0f, predictionSeconds);
            }

            if (!_history.TryGetValue(slot, out List<MotionSample>? history)
                || history.Count == 0
                || trailingDistance <= 0f)
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

        private static float NormalizeAngle(float angle)
        {
            while (angle > Math.PI) angle -= (float)(2 * Math.PI);
            while (angle < -Math.PI) angle += (float)(2 * Math.PI);
            return angle;
        }
    }
}
