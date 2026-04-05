using System;
using System.Numerics;
using LeaderDecoder.Models;

namespace LeaderDecoder.Services
{
    /// <summary>
    /// LEADER FOLLOW CONTROLLER v1.5
    ///
    /// This controller is deliberately coordinate-only. It never assumes a readable unit-facing value.
    /// Instead it does four things in a tight loop:
    ///
    /// 1) Resolve a pursuit goal in world space using the leader's smoothed motion and breadcrumb trail.
    /// 2) Learn the follower's forward axis from observed forward locomotion, because RIFT forces facing to
    ///    align with straight-ahead movement.
    /// 3) Build the local left axis as the perpendicular of that learned forward axis.
    /// 4) Project goal error into that local basis and emit short forward/back/strafe pulses.
    ///
    /// Progress is measured against the goal distance itself. If repeated commands stall or make distance
    /// worse, the learned forward basis is invalidated and reacquired with a fresh forward probe.
    /// </summary>
    public class FollowController
    {
        private readonly InputEngine _input;
        private readonly NavigationKernel _nav;
        private BridgeSettings _settings;

        private readonly SlotState[] _slotStates = new SlotState[5];
        private readonly DateTime[] _lastNavPulseAt = new DateTime[5];
        private readonly DateTime[] _lastMountAttempt = new DateTime[5];
        private readonly DateTime[] _lastAssistAttempt = new DateTime[5];

        private const double AssistCooldownSec = 3.0;
        private const double NavPulseCooldownSec = 0.11;

        private const float PredictionSeconds = 0.20f;
        private const float StopRadiusFloor = 0.60f;
        private const float HoldGoalRadius = 1.10f;
        private const float HoldLateralRadius = 0.55f;
        private const float ForwardDeadzone = 0.35f;
        private const float LateralDeadzone = 0.35f;
        private const float BackwardDeadzone = 0.45f;
        private const float LateralPriorityRatio = 0.85f;
        private const float ThetaStrafeThreshold = 0.55f;

        private const float ForwardGain = 0.45f;
        private const float BackwardGain = 0.35f;
        private const float LateralGain = 0.55f;
        private const float ForwardAngleDamping = 0.90f;
        private const float MinimumDrive = 0.08f;

        private const int ForwardCalibrationPulseMs = 80;
        private const int NavigationPulseMinMs = 55;
        private const int NavigationPulseMaxMs = 145;
        private const int CalibrationObserveWindowMs = 320;
        private const float BasisLearnDistance = 0.12f;
        private const float BasisBlendAlpha = 0.35f;

        private const float ProgressEpsilon = 0.12f;
        private const int MaxWorseningTicks = 3;
        private const int MaxStallTicks = 6;

        private sealed class SlotState
        {
            public Vector2 Forward = Vector2.Zero;
            public bool HasForwardBasis;
            public ForwardCalibration? PendingForwardCalibration;
            public float LastGoalDistance = float.MaxValue;
            public int ConsecutiveWorseningTicks;
            public int ConsecutiveStallTicks;
        }

        private sealed class ForwardCalibration
        {
            public required Vector2 StartPosition { get; init; }
            public required DateTime ExpiresAt { get; init; }
        }

        private enum DriveAxis
        {
            None,
            Forward,
            Backward,
            StrafeLeft,
            StrafeRight,
        }

        private enum ProgressState
        {
            Unknown,
            Improved,
            Neutral,
            Worsening,
            Recalibrate,
        }

        private readonly record struct DriveCommand(DriveAxis Axis, int DurationMs, bool RefreshForwardBasis);

        public FollowController(InputEngine input, NavigationKernel nav, BridgeSettings settings)
        {
            _input = input;
            _nav = nav;
            _settings = settings;

            for (int i = 0; i < _slotStates.Length; i++)
            {
                _slotStates[i] = new SlotState();
                _lastNavPulseAt[i] = DateTime.MinValue;
                _lastMountAttempt[i] = DateTime.MinValue;
                _lastAssistAttempt[i] = DateTime.MinValue;
            }
        }

        public void ApplySettings(BridgeSettings settings) => _settings = settings;

        public void Update(int slot, GameState follower, GameState leader, IntPtr hwnd)
        {
            if (!follower.IsValid || !leader.IsValid || slot <= 0 || slot >= _slotStates.Length)
            {
                return;
            }

            ObserveForwardCalibration(slot, follower);

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
            (float goalX, float goalZ) = _nav.ResolveFollowTarget(0, leader, desiredTrailDistance, PredictionSeconds);

            Vector2 followerPosition = new(follower.CoordX, follower.CoordZ);
            Vector2 goal = new(goalX, goalZ);
            Vector2 error = goal - followerPosition;
            float goalDistance = error.Length();
            float stopRadius = Math.Max(StopRadiusFloor, desiredTrailDistance * 0.35f);
            float holdRadius = Math.Max(stopRadius, HoldGoalRadius);
            bool withinLeaderBand = leaderDistance <= _settings.FollowDistanceMax;
            SlotState state = _slotStates[slot];

            if (goalDistance <= stopRadius)
            {
                ResetProgress(slot);
                HandleSupportActions(slot, follower, leader, leaderDistance, hwnd);
                return;
            }

            if (!state.HasForwardBasis)
            {
                if (withinLeaderBand && goalDistance <= holdRadius)
                {
                    ResetProgress(slot);
                    HandleSupportActions(slot, follower, leader, leaderDistance, hwnd);
                    return;
                }

                ResetProgress(slot);
                TryStartForwardCalibration(slot, followerPosition, hwnd);
                HandleSupportActions(slot, follower, leader, leaderDistance, hwnd);
                return;
            }

            Vector2 h = state.Forward;
            Vector2 l = PerpendicularLeft(h);

            float eForward = Vector2.Dot(error, h);
            float eLateral = Vector2.Dot(error, l);
            float theta = MathF.Atan2(eLateral, eForward);

            if (withinLeaderBand && goalDistance <= holdRadius && Math.Abs(eLateral) <= HoldLateralRadius)
            {
                ResetProgress(slot);
                HandleSupportActions(slot, follower, leader, leaderDistance, hwnd);
                return;
            }

            ProgressState progress = UpdateProgress(slot, goalDistance);
            if (progress == ProgressState.Recalibrate)
            {
                ResetForwardBasis(slot);
                ResetProgress(slot);
                TryStartForwardCalibration(slot, followerPosition, hwnd);
                HandleSupportActions(slot, follower, leader, leaderDistance, hwnd);
                return;
            }

            if (CanIssueNavPulse(slot))
            {
                DriveCommand command = SelectDriveCommand(eForward, eLateral, theta);
                if (command.Axis != DriveAxis.None)
                {
                    IssueDriveCommand(slot, followerPosition, hwnd, command);
                }
            }

            HandleSupportActions(slot, follower, leader, leaderDistance, hwnd);
        }

        public void EmergencyStop(int slot, IntPtr hwnd)
        {
            _input.SendScanCodeUp(hwnd, _settings.KeyForward);
            _input.SendScanCodeUp(hwnd, _settings.KeyLeft);
            _input.SendScanCodeUp(hwnd, _settings.KeyBack);
            _input.SendScanCodeUp(hwnd, _settings.KeyRight);
            _input.SendScanCodeUp(hwnd, _settings.KeyTurnLeft);
            _input.SendScanCodeUp(hwnd, _settings.KeyTurnRight);

            if (slot > 0 && slot < _slotStates.Length)
            {
                ResetForwardBasis(slot);
                ResetProgress(slot);
                _lastNavPulseAt[slot] = DateTime.MinValue;
            }
        }

        public void EmergencyStop(IntPtr hwnd) => EmergencyStop(-1, hwnd);

        private void ObserveForwardCalibration(int slot, GameState follower)
        {
            SlotState state = _slotStates[slot];
            if (state.PendingForwardCalibration is null)
            {
                return;
            }

            Vector2 currentPosition = new(follower.CoordX, follower.CoordZ);
            Vector2 displacement = currentPosition - state.PendingForwardCalibration.StartPosition;

            if (displacement.Length() >= BasisLearnDistance)
            {
                LearnForwardBasis(slot, displacement);
                state.PendingForwardCalibration = null;
                return;
            }

            if (DateTime.Now >= state.PendingForwardCalibration.ExpiresAt)
            {
                state.PendingForwardCalibration = null;
            }
        }

        private void LearnForwardBasis(int slot, Vector2 displacement)
        {
            if (!TryNormalize(displacement, out Vector2 sampleForward))
            {
                return;
            }

            SlotState state = _slotStates[slot];
            state.Forward = state.HasForwardBasis
                ? BlendDirection(state.Forward, sampleForward)
                : sampleForward;
            state.HasForwardBasis = true;
        }

        private void TryStartForwardCalibration(int slot, Vector2 followerPosition, IntPtr hwnd)
        {
            if (!CanIssueNavPulse(slot))
            {
                return;
            }

            _input.TapScanCode(hwnd, _settings.KeyForward, ForwardCalibrationPulseMs);
            _lastNavPulseAt[slot] = DateTime.Now;

            SlotState state = _slotStates[slot];
            state.PendingForwardCalibration ??= new ForwardCalibration
            {
                StartPosition = followerPosition,
                ExpiresAt = DateTime.Now.AddMilliseconds(ForwardCalibrationPulseMs + CalibrationObserveWindowMs),
            };
        }

        private ProgressState UpdateProgress(int slot, float goalDistance)
        {
            SlotState state = _slotStates[slot];
            if (state.LastGoalDistance == float.MaxValue)
            {
                state.LastGoalDistance = goalDistance;
                return ProgressState.Unknown;
            }

            float delta = state.LastGoalDistance - goalDistance;
            state.LastGoalDistance = goalDistance;

            if (delta > ProgressEpsilon)
            {
                state.ConsecutiveWorseningTicks = 0;
                state.ConsecutiveStallTicks = 0;
                return ProgressState.Improved;
            }

            if (delta < -ProgressEpsilon)
            {
                state.ConsecutiveWorseningTicks++;
                state.ConsecutiveStallTicks = 0;
                return state.ConsecutiveWorseningTicks >= MaxWorseningTicks
                    ? ProgressState.Recalibrate
                    : ProgressState.Worsening;
            }

            state.ConsecutiveStallTicks++;
            return state.ConsecutiveStallTicks >= MaxStallTicks
                ? ProgressState.Recalibrate
                : ProgressState.Neutral;
        }

        private void ResetProgress(int slot)
        {
            if (slot <= 0 || slot >= _slotStates.Length)
            {
                return;
            }

            SlotState state = _slotStates[slot];
            state.LastGoalDistance = float.MaxValue;
            state.ConsecutiveWorseningTicks = 0;
            state.ConsecutiveStallTicks = 0;
        }

        private void ResetForwardBasis(int slot)
        {
            if (slot <= 0 || slot >= _slotStates.Length)
            {
                return;
            }

            SlotState state = _slotStates[slot];
            state.Forward = Vector2.Zero;
            state.HasForwardBasis = false;
            state.PendingForwardCalibration = null;
        }

        private DriveCommand SelectDriveCommand(float eForward, float eLateral, float theta)
        {
            float absForward = Math.Abs(eForward);
            float absLateral = Math.Abs(eLateral);
            float absTheta = Math.Abs(theta);

            bool preferStrafe = absLateral > LateralDeadzone
                && (absTheta > ThetaStrafeThreshold || absLateral >= absForward * LateralPriorityRatio);

            if (preferStrafe)
            {
                bool goLeft = eLateral > 0f;
                float drive = MathF.Tanh(LateralGain * absLateral);
                return new DriveCommand(
                    goLeft ? DriveAxis.StrafeLeft : DriveAxis.StrafeRight,
                    ResolvePulseDurationMs(drive),
                    false);
            }

            if (eForward > ForwardDeadzone)
            {
                float drive = MathF.Tanh(ForwardGain * eForward) * MathF.Exp(-ForwardAngleDamping * absTheta);
                if (drive >= MinimumDrive)
                {
                    return new DriveCommand(DriveAxis.Forward, ResolvePulseDurationMs(drive), true);
                }
            }

            if (eForward < -BackwardDeadzone)
            {
                float drive = MathF.Tanh(BackwardGain * absForward);
                return new DriveCommand(DriveAxis.Backward, ResolvePulseDurationMs(drive), false);
            }

            if (absLateral > LateralDeadzone)
            {
                bool goLeft = eLateral > 0f;
                float drive = MathF.Tanh(LateralGain * absLateral);
                return new DriveCommand(
                    goLeft ? DriveAxis.StrafeLeft : DriveAxis.StrafeRight,
                    ResolvePulseDurationMs(drive),
                    false);
            }

            return new DriveCommand(DriveAxis.None, 0, false);
        }

        private void IssueDriveCommand(int slot, Vector2 followerPosition, IntPtr hwnd, DriveCommand command)
        {
            byte key = command.Axis switch
            {
                DriveAxis.Forward => _settings.KeyForward,
                DriveAxis.Backward => _settings.KeyBack,
                DriveAxis.StrafeLeft => _settings.KeyLeft,
                DriveAxis.StrafeRight => _settings.KeyRight,
                _ => 0,
            };

            if (key == 0)
            {
                return;
            }

            _input.TapScanCode(hwnd, key, command.DurationMs);
            _lastNavPulseAt[slot] = DateTime.Now;

            if (command.RefreshForwardBasis)
            {
                SlotState state = _slotStates[slot];
                state.PendingForwardCalibration ??= new ForwardCalibration
                {
                    StartPosition = followerPosition,
                    ExpiresAt = DateTime.Now.AddMilliseconds(command.DurationMs + CalibrationObserveWindowMs),
                };
            }
        }

        private void HandleSupportActions(int slot, GameState follower, GameState leader, float leaderDistance, IntPtr hwnd)
        {
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

        private bool CanIssueNavPulse(int slot)
        {
            return (DateTime.Now - _lastNavPulseAt[slot]).TotalSeconds >= NavPulseCooldownSec;
        }

        private float ResolveTrailDistance()
        {
            float averageBand = (_settings.FollowDistanceMin + _settings.FollowDistanceMax) * 0.5f;
            return Math.Clamp(averageBand, _settings.FollowDistanceMin + 0.4f, _settings.FollowDistanceMax);
        }

        private static Vector2 BlendDirection(Vector2 current, Vector2 sample)
        {
            Vector2 blended = current * (1f - BasisBlendAlpha) + sample * BasisBlendAlpha;
            return TryNormalize(blended, out Vector2 normalized) ? normalized : sample;
        }

        private static int ResolvePulseDurationMs(float drive)
        {
            float clamped = Math.Clamp(drive, 0f, 1f);
            float duration = NavigationPulseMinMs + clamped * (NavigationPulseMaxMs - NavigationPulseMinMs);
            return (int)Math.Round(duration);
        }

        private static Vector2 PerpendicularLeft(Vector2 forward)
        {
            return TryNormalize(forward, out Vector2 normalized)
                ? new Vector2(-normalized.Y, normalized.X)
                : Vector2.Zero;
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
