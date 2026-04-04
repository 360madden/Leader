using System;
using LeaderDecoder.Models;

namespace LeaderDecoder.Services
{
    /// <summary>
    /// LEADER FOLLOW CONTROLLER v1.0
    /// Orchestrates character pursuit vectors based on optical telemetry.
    /// Manages deadzones, rotation thresholds, and mount synchronization.
    /// </summary>
    public class FollowController
    {
        private readonly InputEngine _input;
        private readonly NavigationKernel _nav;
        private readonly bool[] _isMovingForward = new bool[5];
        private DateTime[] _lastMountAttempt = new DateTime[5];
        private float[] _lastError = new float[5];
        private BridgeSettings _settings;

        public FollowController(InputEngine input, NavigationKernel nav, BridgeSettings settings)
        {
            _input = input;
            _nav = nav;
            _settings = settings;
            for (int i = 0; i < 5; i++) 
            {
                _lastMountAttempt[i] = DateTime.MinValue;
                _lastError[i] = 0;
            }
        }

        /// <summary>
        /// Updates the movement state of a follower window.
        /// </summary>
        public void Update(int slot, GameState follower, GameState leader, IntPtr hwnd)
        {
            if (!follower.IsValid || !leader.IsValid || slot == 0) return;

            // 0. Safety Check
            if (!follower.IsAlive)
            {
                EmergencyStop(hwnd);
                _isMovingForward[slot] = false;
                return;
            }

            float distance = _nav.CalculateDistance(follower, leader);
            float targetBearing = _nav.CalculateBearingToLeader(follower, leader);
            float angleDelta = NormalizeAngle(targetBearing - follower.EstimatedHeading);

            // 1. PD-Lite Steering
            float error = angleDelta;
            float dError = error - _lastError[slot];
            _lastError[slot] = error;

            float steeringPower = (error * _settings.TurnP) + (dError * _settings.TurnD);

            if (Math.Abs(steeringPower) > _settings.AngleTolerance)
            {
                if (steeringPower > 0)
                    _input.TapKey(hwnd, InputEngine.RiftKey.D);
                else
                    _input.TapKey(hwnd, InputEngine.RiftKey.A);
            }

            // 2. Movement Management
            if (distance > _settings.FollowDistanceMax && !_isMovingForward[slot])
            {
                if (Math.Abs(error) < _settings.AngleTolerance * 2.5f) 
                {
                    _input.SendKeyDown(hwnd, InputEngine.RiftKey.W);
                    _isMovingForward[slot] = true;
                }
            }
            else if (distance < _settings.FollowDistanceMin && _isMovingForward[slot])
            {
                _input.SendKeyUp(hwnd, InputEngine.RiftKey.W);
                _isMovingForward[slot] = false;
            }

            // 3. Mount & Assist Logic
            if (leader.IsMounted && !follower.IsMounted && !_isMovingForward[slot])
            {
                if ((DateTime.Now - _lastMountAttempt[slot]).TotalSeconds > 5)
                {
                    _input.TapKey(hwnd, InputEngine.RiftKey.M);
                    _lastMountAttempt[slot] = DateTime.Now;
                }
            }

            // Simple Target Assist (F Key)
            if (leader.HasTarget && !follower.HasTarget && distance < _settings.AssistDistance)
            {
                _input.TapKey(hwnd, InputEngine.RiftKey.F);
            }
        }

        private float NormalizeAngle(float angle)
        {
            while (angle > Math.PI) angle -= (float)(2 * Math.PI);
            while (angle < -Math.PI) angle += (float)(2 * Math.PI);
            return angle;
        }

        /// <summary>
        /// Force release all keys (emergency stop).
        /// </summary>
        public void EmergencyStop(IntPtr hwnd)
        {
            _input.SendKeyUp(hwnd, InputEngine.RiftKey.W);
            _input.SendKeyUp(hwnd, InputEngine.RiftKey.A);
            _input.SendKeyUp(hwnd, InputEngine.RiftKey.S);
            _input.SendKeyUp(hwnd, InputEngine.RiftKey.D);
        }
    }
}
