using System;
using System.Numerics;
using LeaderDecoder.Models;

namespace LeaderDecoder.Services
{
    /// <summary>
    /// LEADER FOLLOW CONTROLLER v1.4
    /// Coordinate-only pursuit controller:
    /// - follows a breadcrumb-backed trailing point instead of chasing the body
    /// - calibrates the follower's local forward/strafe basis from observed movement probes
    /// - projects world-space error into that learned basis and emits one movement pulse at a time
    /// - preserves mount sync and target assist
    /// </summary>
    public class FollowController
    {
        private readonly InputEngine _input;
        private readonly NavigationKernel _nav;
        private BridgeSettings _settings;

        private readonly MovementBasisState[] _basisStates = new MovementBasisState[5];
        private readonly DateTime[] _lastNavPulseAt = new DateTime[5];
        private readonly DateTime[] _lastMountAttempt = new DateTime[5];
        private readonly DateTime[] _lastAssistAttempt = new DateTime[5];
        private readonly DateTime[] _progressWatchStartedAt = new DateTime[5];
        private readonly float[] _progressWatchDistance = new float[5];

        private const double AssistCooldownSec = 3.0;
        private const double NavPulseCooldownSec = 0.11;
        private const double ProgressResetSec = 2.5;
        private const float ProgressRequiredDelta = 0.35f;
        private const int BootstrapForwardPulseMs = 80;
        private const int BootstrapStrafePulseMs = 75;
        private const int NavPulseMinMs = 55;
        private const int NavPulseMaxMs = 145;
        private const int ProbeObserveWindowMs = 320;
        private const float BasisLearnDistance = 0.12f;
        private const float BasisBlendAlpha = 0.35f;
        private const float StopRadiusFloor = 0.60f;
        private const float LongitudinalDeadzone = 0.35f;
        private const float LateralDeadzone = 0.35f;
        private const float AxisPrioritySlack = 0.85f;
        private const float DefaultPredictionSeconds = 0.20f;

        private sealed class MovementBasisState
        {
            public Vector2 Forward = Vector2.Zero;
            public Vector2 Left = Vector2.Zero;
            public bool HasForward;
            public bool HasLeft;
            public ProbeObservation? PendingObservation;
        }

        private sealed class ProbeObservation
        {
            public required ProbeAxis Axis { get; init; }
            public required Vector2 StartPosition { get; init; }
            public required DateTime ExpiresAt { get; init; }
        }

        private enum ProbeAxis
        {
            Forward,
            Backward,
            Left,
            Right
        }

        public FollowController(InputEngine input, NavigationKernel nav, BridgeSettings settings)
        {
            _input = input;
            _nav = nav;
            _settings = settings;

            for (int i = 0; i < 5; i++)
            {
                _basisStates[i] = new MovementBasisState();
                _lastNavPulseAt[i] = DateTime.MinValue;
                _lastMountAttempt[i] = DateTime.MinValue;
                _lastAssistAttempt[i] = DateTime.MinValue;
                _progressWatchStartedAt[i] = DateTime.MinValue;
                _progressWatchDistance[i] = float.MaxValue;
            }
        }

        public void ApplySettings(BridgeSettings settings) => _settings = settings;

        public void Update(int slot, GameState follower, GameState leader, IntPtr hwnd)
        {
            if (!follower.IsValid || !leader.IsValid || slot <= 0 || slot >= _basisStates.Length)
            {
                return;
            }

            ObserveProbeResult(slot, follower);

            if (follower.ZoneHash != 0 && leader.ZoneHash != 0 && follower.ZoneHash != leader.ZoneHash)
            {
                EmergencyStop(slot, hwnd);
                return;
            }

            if (!follower.IsAlive)
            {
                EmergencyStop(slot, hwnd);
                return;
            }

            float leaderDistance = _nav.CalculateDistance(follower, leader);
            float desiredTrailDistance = ResolveTrailDistance();
            (float goalX, float goalZ) = _nav.ResolveFollowTarget(0, leader, desiredTrailDistance, DefaultPredictionSeconds);

            Vector2 followerPos = new(follower.CoordX, follower.CoordZ);
            Vector2 goal = new(goalX, goalZ);
            Vector2 error = goal - followerPos;
            float distanceToGoal = error.Length();
            float stopRadius = Math.Max(StopRadiusFloor, desiredTrailDistance * 0.35f);

            if (distanceToGoal > stopRadius)
            {
                TrackProgress(slot, distanceToGoal);

                if (CanIssueNavPulse(slot))
                {
                    MovementBasisState basis = _basisStates[slot];

                    if (!basis.HasForward)
                    {
                        IssuePulse(slot, hwnd, followerPos, ProbeAxis.Forward, _settings.KeyForward, BootstrapForwardPulseMs, observe: true);
                    }
                    else if (!basis.HasLeft)
                    {
                        Vector2 guessedLeft = PerpendicularLeft(basis.Forward);
                        float longitudinalGuess = Vector2.Dot(error, basis.Forward);
                        float lateralGuess = Vector2.Dot(error, guessedLeft);

                        if (Math.Abs(longitudinalGuess) >= Math.Abs(lateralGuess) * 1.35f)
                        {
                            bool goForward = longitudinalGuess >= 0f;
                            ProbeAxis axis = goForward ? ProbeAxis.Forward : ProbeAxis.Backward;
                            byte key = goForward ? _settings.KeyForward : _settings.KeyBack;
                            IssuePulse(slot, hwnd, followerPos, axis, key, BootstrapForwardPulseMs, observe: true);
                        }
                        else
                        {
                            bool goLeft = lateralGuess >= 0f;
                            ProbeAxis axis = goLeft ? ProbeAxis.Left : ProbeAxis.Right;
                            byte key = goLeft ? _settings.KeyLeft : _settings.KeyRight;
                            IssuePulse(slot, hwnd, followerPos, axis, key, BootstrapStrafePulseMs, observe: true);
                        }
                    }
                    else
                    {
                        (float longitudinal, float lateral) = ProjectIntoBasis(error, basis.Forward, basis.Left);
                        TryDriveProjectedError(slot, hwnd, followerPos, longitudinal, lateral);
                    }
                }
            }
            else
            {
                ResetProgress(slot);
            }

            if (leader.IsMounted && !follower.IsMounted)
            {
                if ((DateTime.Now - _lastMountAttempt[slot]).TotalSeconds > 5)
                {
                    _input.TapScanCode(hwnd, _settings.KeyMount);
                    _lastMountAttempt[slot] = DateTime.Now;
                }
            }

            if (leader.HasTarget && !follower.HasTarget && leaderDistance < _settings.AssistDistance)
            {
                if ((DateTime.Now - _lastAssistAttempt[slot]).TotalSeconds > AssistCooldownSec)
                {
                    _input.TapScanCode(hwnd, _settings.KeyInteract);
                    _lastAssistAttempt[slot] = DateTime.Now;
                }
            }
        }

        public void EmergencyStop(int slot, IntPtr hwnd)
        {
            _input.SendScanCodeUp(hwnd, _settings.KeyForward);
            _input.SendScanCodeUp(hwnd, _settings.KeyLeft);
            _input.SendScanCodeUp(hwnd, _settings.KeyBack);
            _input.SendScanCodeUp(hwnd, _settings.KeyRight);
            _input.SendScanCodeUp(hwnd, _settings.KeyTurnLeft);
            _input.SendScanCodeUp(hwnd, _settings.KeyTurnRight);

            if (slot > 0 && slot < _basisStates.Length)
            {
                ResetBasisState(slot);
                ResetProgress(slot);
            }
        }

        public void EmergencyStop(IntPtr hwnd) => EmergencyStop(-1, hwnd);

        private bool CanIssueNavPulse(int slot)
        {
            return (DateTime.Now - _lastNavPulseAt[slot]).TotalSeconds >= NavPulseCooldownSec;
        }

        private void ObserveProbeResult(int slot, GameState follower)
        {
            MovementBasisState basis = _basisStates[slot];
            if (basis.PendingObservation is null)
            {
                return;
            }

            Vector2 current = new(follower.CoordX, follower.CoordZ);
            Vector2 displacement = current - basis.PendingObservation.StartPosition;
            if (displacement.Length() >= BasisLearnDistance)
            {
                ApplyProbeObservation(basis, basis.PendingObservation.Axis, displacement);
                basis.PendingObservation = null;
                return;
            }

            if (DateTime.Now >= basis.PendingObservation.ExpiresAt)
            {
                basis.PendingObservation = null;
            }
        }

        private void ApplyProbeObservation(MovementBasisState basis, ProbeAxis axis, Vector2 displacement)
        {
            if (!TryNormalize(displacement, out Vector2 sample))
            {
                return;
            }

            if (axis == ProbeAxis.Backward || axis == ProbeAxis.Right)
            {
                sample = -sample;
            }

            if (axis == ProbeAxis.Forward || axis == ProbeAxis.Backward)
            {
                basis.Forward = BlendDirection(basis.HasForward ? basis.Forward : sample, sample);
                basis.HasForward = true;

                if (basis.HasLeft)
                {
                    Vector2 correctedLeft = Reject(basis.Left, basis.Forward);
                    if (TryNormalize(correctedLeft, out Vector2 normalizedLeft))
                    {
                        basis.Left = normalizedLeft;
                    }
                    else
                    {
                        basis.Left = Vector2.Zero;
                        basis.HasLeft = false;
                    }
                }

                return;
            }

            if (basis.HasForward)
            {
                Vector2 lateralOnly = Reject(sample, basis.Forward);
                if (!TryNormalize(lateralOnly, out sample))
                {
                    return;
                }
            }

            basis.Left = BlendDirection(basis.HasLeft ? basis.Left : sample, sample);
            basis.HasLeft = true;
        }

        private float ResolveTrailDistance()
        {
            float averageBand = (_settings.FollowDistanceMin + _settings.FollowDistanceMax) * 0.5f;
            return Math.Clamp(averageBand, _settings.FollowDistanceMin + 0.4f, _settings.FollowDistanceMax);
        }

        private void TryDriveProjectedError(int slot, IntPtr hwnd, Vector2 followerPos, float longitudinal, float lateral)
        {
            float absLongitudinal = Math.Abs(longitudinal);
            float absLateral = Math.Abs(lateral);

            if (absLongitudinal <= LongitudinalDeadzone && absLateral <= LateralDeadzone)
            {
                ResetProgress(slot);
                return;
            }

            if (absLateral > LateralDeadzone && absLateral >= absLongitudinal * AxisPrioritySlack)
            {
                bool goLeft = lateral > 0f;
                ProbeAxis axis = goLeft ? ProbeAxis.Left : ProbeAxis.Right;
                byte key = goLeft ? _settings.KeyLeft : _settings.KeyRight;
                int duration = ResolvePulseDurationMs(absLateral, LateralDeadzone);
                IssuePulse(slot, hwnd, followerPos, axis, key, duration, observe: false);
                return;
            }

            if (absLongitudinal > LongitudinalDeadzone)
            {
                bool goForward = longitudinal > 0f;
                ProbeAxis axis = goForward ? ProbeAxis.Forward : ProbeAxis.Backward;
                byte key = goForward ? _settings.KeyForward : _settings.KeyBack;
                int duration = ResolvePulseDurationMs(absLongitudinal, LongitudinalDeadzone);
                IssuePulse(slot, hwnd, followerPos, axis, key, duration, observe: false);
                return;
            }

            if (absLateral > LateralDeadzone)
            {
                bool goLeft = lateral > 0f;
                ProbeAxis axis = goLeft ? ProbeAxis.Left : ProbeAxis.Right;
                byte key = goLeft ? _settings.KeyLeft : _settings.KeyRight;
                int duration = ResolvePulseDurationMs(absLateral, LateralDeadzone);
                IssuePulse(slot, hwnd, followerPos, axis, key, duration, observe: false);
            }
        }

        private void IssuePulse(int slot, IntPtr hwnd, Vector2 followerPos, ProbeAxis axis, byte key, int durationMs, bool observe)
        {
            _input.TapScanCode(hwnd, key, durationMs);
            _lastNavPulseAt[slot] = DateTime.Now;

            if (observe)
            {
                _basisStates[slot].PendingObservation = new ProbeObservation
                {
                    Axis = axis,
                    StartPosition = followerPos,
                    ExpiresAt = DateTime.Now.AddMilliseconds(durationMs + ProbeObserveWindowMs)
                };
            }
        }

        private void TrackProgress(int slot, float distanceToGoal)
        {
            if (_progressWatchDistance[slot] == float.MaxValue)
            {
                _progressWatchDistance[slot] = distanceToGoal;
                _progressWatchStartedAt[slot] = DateTime.Now;
                return;
            }

            if (_progressWatchDistance[slot] - distanceToGoal >= ProgressRequiredDelta)
            {
                _progressWatchDistance[slot] = distanceToGoal;
                _progressWatchStartedAt[slot] = DateTime.Now;
                return;
            }

            if ((DateTime.Now - _progressWatchStartedAt[slot]).TotalSeconds >= ProgressResetSec)
            {
                ResetBasisState(slot);
                ResetProgress(slot);
            }
        }

        private void ResetProgress(int slot)
        {
            if (slot <= 0 || slot >= _progressWatchDistance.Length)
            {
                return;
            }

            _progressWatchDistance[slot] = float.MaxValue;
            _progressWatchStartedAt[slot] = DateTime.MinValue;
        }

        private void ResetBasisState(int slot)
        {
            if (slot <= 0 || slot >= _basisStates.Length)
            {
                return;
            }

            _basisStates[slot] = new MovementBasisState();
            _lastNavPulseAt[slot] = DateTime.MinValue;
        }

        private static (float Longitudinal, float Lateral) ProjectIntoBasis(Vector2 error, Vector2 forward, Vector2 left)
        {
            float determinant = forward.X * left.Y - forward.Y * left.X;
            if (Math.Abs(determinant) < 0.10f)
            {
                Vector2 fallbackLeft = PerpendicularLeft(forward);
                return (Vector2.Dot(error, forward), Vector2.Dot(error, fallbackLeft));
            }

            float longitudinal = (error.X * left.Y - error.Y * left.X) / determinant;
            float lateral = (forward.X * error.Y - forward.Y * error.X) / determinant;
            return (longitudinal, lateral);
        }

        private static Vector2 BlendDirection(Vector2 current, Vector2 sample)
        {
            Vector2 blended = current * (1f - BasisBlendAlpha) + sample * BasisBlendAlpha;
            if (TryNormalize(blended, out Vector2 normalized))
            {
                return normalized;
            }

            return sample;
        }

        private static int ResolvePulseDurationMs(float axisMagnitude, float deadzone)
        {
            float normalized = Math.Clamp((axisMagnitude - deadzone) / 3.5f, 0f, 1f);
            float duration = NavPulseMinMs + normalized * (NavPulseMaxMs - NavPulseMinMs);
            return (int)Math.Round(duration);
        }

        private static Vector2 Reject(Vector2 vector, Vector2 onto)
        {
            float lengthSquared = onto.LengthSquared();
            if (lengthSquared <= 0.0001f)
            {
                return vector;
            }

            return vector - onto * (Vector2.Dot(vector, onto) / lengthSquared);
        }

        private static Vector2 PerpendicularLeft(Vector2 forward)
        {
            if (!TryNormalize(forward, out Vector2 normalized))
            {
                return Vector2.Zero;
            }

            return new Vector2(-normalized.Y, normalized.X);
        }

        private static bool TryNormalize(Vector2 vector, out Vector2 normalized)
        {
            if (vector.LengthSquared() <= 0.0001f)
            {
                normalized = Vector2.Zero;
                return false;
            }

            normalized = Vector2.Normalize(vector);
            return true;
        }
    }
}
