using System;
using System.Collections.Generic;
using System.Numerics;
using LeaderDecoder.Models;

namespace LeaderDecoder.Services
{
    /// <summary>
    /// LEADER NAVIGATION KERNEL v1.1
    /// High-precision spatial analysis, heading estimation, and zone-change detection.
    /// </summary>
    public class NavigationKernel
    {
        private const int   MaxHistory           = 12;
        private const float MinMovementThreshold = 0.08f; // Metres — filters GPS noise

        private readonly Dictionary<int, Queue<Vector2>> _posHistory = new();
        private readonly Dictionary<int, float>          _smoothedHeading = new();
        private readonly Dictionary<int, byte>           _lastZoneHash = new();

        // Heading smoothing weights — exponential moving average
        private const float HeadingAlpha = 0.25f;

        /// <summary>
        /// Updates heading and zone-change state for a given slot.
        /// Returns true if a zone-change was detected (caller should abort pursuit).
        /// </summary>
        public bool UpdateHeading(int slot, GameState state)
        {
            if (!state.IsValid) return false;

            // ── Zone-change detection ──────────────────────────────────────
            bool zoneChanged = false;
            if (_lastZoneHash.TryGetValue(slot, out byte prevZone) && prevZone != state.ZoneHash)
            {
                zoneChanged = true;
                // Clear positional history so stale vectors don't corrupt the new zone heading
                if (_posHistory.ContainsKey(slot)) _posHistory[slot].Clear();
                if (_smoothedHeading.ContainsKey(slot)) _smoothedHeading.Remove(slot);
            }
            _lastZoneHash[slot] = state.ZoneHash;

            // ── Primary: use RawFacing from Lua telemetry (most accurate) ──
            if (!_posHistory.ContainsKey(slot))
                _posHistory[slot] = new Queue<Vector2>();

            var currentPos = new Vector2(state.CoordX, state.CoordZ);
            var history    = _posHistory[slot];

            float rawFacing = state.RawFacing;

            // ── Secondary / Fallback: historical movement vector ───────────
            bool hasGoodVector = false;
            if (history.Count > 0)
            {
                var oldPos    = history.Peek();
                var direction = currentPos - oldPos;

                if (direction.Length() > MinMovementThreshold)
                {
                    // RIFT coordinate system: X+ = East, Z+ = South
                    // Atan2 in standard math: Atan2(y, x). Here our "forward Y" is Z axis.
                    float vectorHeading = (float)Math.Atan2(direction.X, direction.Y);
                    hasGoodVector = true;

                    // Blend: if RawFacing is near-zero AND we have movement, trust vector
                    if (rawFacing < 0.001f && state.IsMoving)
                        rawFacing = vectorHeading;
                }
            }

            // ── Exponential smoothing to suppress jitter ───────────────────
            if (!_smoothedHeading.TryGetValue(slot, out float prevSmoothed))
                prevSmoothed = rawFacing;

            // Angle-aware lerp (handles wrap-around correctly)
            float delta = NormalizeAngle(rawFacing - prevSmoothed);
            float smoothed = prevSmoothed + HeadingAlpha * delta;
            _smoothedHeading[slot] = NormalizeAngle(smoothed);

            state.EstimatedHeading = _smoothedHeading[slot];
            state.IsHeadingLocked  = rawFacing > 0.001f || hasGoodVector;

            // Store position
            history.Enqueue(currentPos);
            if (history.Count > MaxHistory) history.Dequeue();

            return zoneChanged;
        }

        /// <summary>
        /// Returns the bearing angle (radians) a follower must face to move toward the leader.
        /// Accounts for RIFT's coordinate system: X+ = East, Z+ = South.
        /// </summary>
        public float CalculateBearingToLeader(GameState follower, GameState leader)
        {
            float dx = leader.CoordX - follower.CoordX;
            float dz = leader.CoordZ - follower.CoordZ;
            // Atan2(dz, dx) would be standard math; RIFT rotates ~90° so we swap args
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

        private static float NormalizeAngle(float angle)
        {
            while (angle >  Math.PI) angle -= (float)(2 * Math.PI);
            while (angle < -Math.PI) angle += (float)(2 * Math.PI);
            return angle;
        }
    }
}
