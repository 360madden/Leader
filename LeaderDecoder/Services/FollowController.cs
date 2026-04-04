using System;
using LeaderDecoder.Models;

namespace LeaderDecoder.Services
{
    /// <summary>
    /// LEADER FOLLOW CONTROLLER v1.2
    /// PD-lite steering, target assist with cooldown, anti-stuck watchdog.
    /// </summary>
    public class FollowController
    {
        private readonly InputEngine    _input;
        private readonly NavigationKernel _nav;
        private BridgeSettings          _settings;

        private readonly bool[]     _isMovingForward  = new bool[5];
        private float[]             _lastError        = new float[5];

        // Mount cooldown
        private DateTime[] _lastMountAttempt  = new DateTime[5];

        // Target-assist cooldown — prevents F-tap every 33ms
        private DateTime[] _lastAssistAttempt = new DateTime[5];
        private const double AssistCooldownSec = 3.0;

        // Anti-stuck watchdog
        private DateTime[] _wForwardSince    = new DateTime[5]; // when W was first pressed
        private float[]    _distanceAtPress  = new float[5];    // distance logged when W pressed
        private const double StuckThreshSec   = 5.0;   // seconds before triggering recovery
        private const float  StuckDistDelta   = 0.3f;  // must have closed at least this many metres

        public FollowController(InputEngine input, NavigationKernel nav, BridgeSettings settings)
        {
            _input    = input;
            _nav      = nav;
            _settings = settings;

            for (int i = 0; i < 5; i++)
            {
                _lastMountAttempt[i]  = DateTime.MinValue;
                _lastAssistAttempt[i] = DateTime.MinValue;
                _wForwardSince[i]     = DateTime.MinValue;
                _distanceAtPress[i]   = float.MaxValue;
                _lastError[i]         = 0;
            }
        }

        public void ApplySettings(BridgeSettings settings) => _settings = settings;

        public void Update(int slot, GameState follower, GameState leader, IntPtr hwnd)
        {
            if (!follower.IsValid || !leader.IsValid || slot == 0) return;

            // ── 0. Death guard ────────────────────────────────────────────
            if (!follower.IsAlive)
            {
                EmergencyStop(hwnd);
                _isMovingForward[slot] = false;
                return;
            }

            float distance     = _nav.CalculateDistance(follower, leader);
            float bearing      = _nav.CalculateBearingToLeader(follower, leader);
            float angleDelta   = NormalizeAngle(bearing - follower.EstimatedHeading);

            // ── 1. PD-Lite Steering ───────────────────────────────────────
            float error        = angleDelta;
            float dError       = error - _lastError[slot];
            _lastError[slot]   = error;
            float steerPower   = error * _settings.TurnP + dError * _settings.TurnD;

            if (Math.Abs(steerPower) > _settings.AngleTolerance)
            {
                _input.TapKey(hwnd, steerPower > 0 ? InputEngine.RiftKey.D : InputEngine.RiftKey.A);
            }

            // ── 2. Movement + Anti-Stuck Watchdog ─────────────────────────
            if (distance > _settings.FollowDistanceMax)
            {
                if (!_isMovingForward[slot] && Math.Abs(error) < _settings.AngleTolerance * 2.5f)
                {
                    _input.SendKeyDown(hwnd, InputEngine.RiftKey.W);
                    _isMovingForward[slot]  = true;
                    _wForwardSince[slot]    = DateTime.Now;
                    _distanceAtPress[slot]  = distance;
                }
                else if (_isMovingForward[slot])
                {
                    // Watchdog: if we've been pressing W for StuckThreshSec and barely moved
                    double wDuration = (DateTime.Now - _wForwardSince[slot]).TotalSeconds;
                    if (wDuration > StuckThreshSec
                        && _distanceAtPress[slot] - distance < StuckDistDelta)
                    {
                        // Anti-stuck: jump then reset heading lock
                        _input.TapKey(hwnd, InputEngine.RiftKey.Space);
                        _wForwardSince[slot]   = DateTime.Now;
                        _distanceAtPress[slot] = distance;
                    }
                }
            }
            else if (distance < _settings.FollowDistanceMin && _isMovingForward[slot])
            {
                _input.SendKeyUp(hwnd, InputEngine.RiftKey.W);
                _isMovingForward[slot] = false;
            }

            // ── 3. Mount Sync ─────────────────────────────────────────────
            if (leader.IsMounted && !follower.IsMounted && !_isMovingForward[slot])
            {
                if ((DateTime.Now - _lastMountAttempt[slot]).TotalSeconds > 5)
                {
                    _input.TapKey(hwnd, InputEngine.RiftKey.M);
                    _lastMountAttempt[slot] = DateTime.Now;
                }
            }

            // ── 4. Target Assist (with cooldown) ─────────────────────────
            if (leader.HasTarget && !follower.HasTarget && distance < _settings.AssistDistance)
            {
                if ((DateTime.Now - _lastAssistAttempt[slot]).TotalSeconds > AssistCooldownSec)
                {
                    _input.TapKey(hwnd, InputEngine.RiftKey.F);
                    _lastAssistAttempt[slot] = DateTime.Now;
                }
            }
        }

        public void EmergencyStop(IntPtr hwnd)
        {
            _input.SendKeyUp(hwnd, InputEngine.RiftKey.W);
            _input.SendKeyUp(hwnd, InputEngine.RiftKey.A);
            _input.SendKeyUp(hwnd, InputEngine.RiftKey.S);
            _input.SendKeyUp(hwnd, InputEngine.RiftKey.D);
        }

        private static float NormalizeAngle(float a)
        {
            while (a >  Math.PI) a -= (float)(2 * Math.PI);
            while (a < -Math.PI) a += (float)(2 * Math.PI);
            return a;
        }
    }
}
