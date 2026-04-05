using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using LeaderDecoder.Models;
using LeaderDecoder.Services;

namespace LeaderDecoder
{
    internal static class RoundtripTests
    {
        private static int _pass;
        private static int _fail;

        public static void Run()
        {
            if (!Console.IsOutputRedirected)
            {
                Console.Clear();
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("═══════════════════════════════════════════════════");
            Console.WriteLine("  🧪 LEADER — TELEMETRY ROUNDTRIP TEST SUITE");
            Console.WriteLine("═══════════════════════════════════════════════════");
            Console.ResetColor();

            TestCoord("Zero position", 0f);
            TestCoord("Positive X", 1234.5f);
            TestCoord("Negative Z", -987.6f);
            TestCoord("Large coord", 50000.1f);
            TestCoord("Negative large", -50000.1f);
            TestCoord("Sub-unit prec", 0.1f);
            TestCoord("Sub-unit prec 2", 0.09f);

            TestHeading("Zero radians", 0f);
            TestHeading("PI/2 (East)", (float)(Math.PI / 2));
            TestHeading("PI (South)", (float)Math.PI);
            TestHeading("3PI/2 (West)", (float)(3 * Math.PI / 2));
            TestHeading("2PI (North)", (float)(2 * Math.PI));
            TestHeading("Typical angle", 2.3456f);
            TestHeading("Negative wraps", -0.75f);

            TestZoneHash("Zero hash", 0);
            TestZoneHash("Max hash", 255);
            TestZoneHash("Mid hash", 128);

            TestPlayerTag("Exact tag", "BOB1", "BOB1");
            TestPlayerTag("Normalized tag", "ab-cd", "ABCD");

            TestFlags("All clear", false, false, false, false, false);
            TestFlags("Combat only", true, false, false, true, false);
            TestFlags("All set", true, true, true, true, true);
            TestFlags("Mounted+Alive", false, false, false, true, true);

            TestMotionEstimatorUsesCoordinateDeltas();
            TestBreadcrumbFollowTargetTrailsPath();
            TestFollowBootstrapsForwardCalibration();
            TestForwardCalibrationLearnsBasisAndDerivesLeftAxis();
            TestStrafeObservationRefinesForwardBasis();
            TestBackwardObservationRefinesForwardBasis();
            TestFollowBackpedalsWhenGoalIsBehind();
            TestFollowDoesNotEngageBeyondMaxDistance();
            TestFollowUsesBreadcrumbTrailInsteadOfBodyChase();
            TestFollowerTrailMatchPrefersLocalBreadcrumbCarrot();
            TestWorseningProgressForcesRecalibration();
            TestEmergencyStopResetsBasisAndReleasesKeys();
            TestMountSyncUsesConfiguredKey();
            TestAssistUsesConfiguredKey();
            TestZoneChangeClearsHeadingHistory();
            TestZoneMismatchStopsFollow();
            TestRoleLockCapturesCurrentAssignments();
            TestRoleLockPrefersForegroundWindowAsLeader();
            TestRoleLockMatchesExpectedWindowAndTag();
            TestWindowFilterProcessIdOrder();
            TestWindowFilterHwndOrder();
            TestWindowSlotsPreserveMissingProcessIdEntries();
            TestWindowSlotsPreserveMissingHwndEntries();

            TestFullState("Typical moving combat state", new GameState
            {
                CoordX = 5432.1f,
                CoordY = 23.4f,
                CoordZ = -1234.5f,
                RawFacing = 1.5708f,
                ZoneHash = 42,
                PlayerHP = 200,
                TargetHP = 127,
                IsCombat = true,
                HasTarget = true,
                IsMoving = false,
                IsAlive = true,
                IsMounted = false,
            });

            Console.WriteLine();
            Console.WriteLine("───────────────────────────────────────────────────");
            if (_fail == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  ✅  ALL {_pass} TESTS PASSED");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  ❌  {_fail}/{_pass + _fail} TESTS FAILED");
            }
            Console.ResetColor();
        }

        private static void TestCoord(string name, float value)
        {
            var sim = new TelemetrySimulator();
            var svc = new TelemetryService();
            var state = BuildState();
            state.CoordX = value;

            var decoded = svc.Decode(sim.Generate(state));
            float expected = (float)Math.Round(value * 10) / 10f;
            float actual = (float)Math.Round(decoded.CoordX * 10) / 10f;

            Assert($"Coord  [{name}] ({value}->{expected})", Math.Abs(actual - expected) < 0.11f, $"got {actual}");
        }

        private static void TestHeading(string name, float radians)
        {
            var sim = new TelemetrySimulator();
            var svc = new TelemetryService();
            var state = BuildState();
            state.RawFacing = NormalizeProtocolHeading(radians);

            var decoded = svc.Decode(sim.Generate(state));
            Assert($"Heading[{name}] ({radians:F4})", Math.Abs(decoded.RawFacing - state.RawFacing) < 0.0002f,
                $"got {decoded.RawFacing:F5}");
        }

        private static void TestZoneHash(string name, byte hash)
        {
            var sim = new TelemetrySimulator();
            var svc = new TelemetryService();
            var state = BuildState();
            state.ZoneHash = hash;

            var decoded = svc.Decode(sim.Generate(state));
            Assert($"Zone   [{name}] ({hash})", decoded.ZoneHash == hash, $"got {decoded.ZoneHash}");
        }

        private static void TestPlayerTag(string name, string tag, string expected)
        {
            var sim = new TelemetrySimulator();
            var svc = new TelemetryService();
            var state = BuildState();
            state.PlayerTag = tag;

            var decoded = svc.Decode(sim.Generate(state));
            Assert($"Tag    [{name}] ({tag}->{expected})", decoded.PlayerTag == expected, $"got {decoded.PlayerTag}");
        }

        private static void TestFlags(string name, bool combat, bool target, bool moving, bool alive, bool mounted)
        {
            var sim = new TelemetrySimulator();
            var svc = new TelemetryService();
            var state = BuildState();
            state.IsCombat = combat;
            state.HasTarget = target;
            state.IsMoving = moving;
            state.IsAlive = alive;
            state.IsMounted = mounted;

            var decoded = svc.Decode(sim.Generate(state));
            bool ok = decoded.IsCombat == combat
                && decoded.HasTarget == target
                && decoded.IsMoving == moving
                && decoded.IsAlive == alive
                && decoded.IsMounted == mounted;

            Assert($"Flags  [{name}]", ok,
                $"got CB:{decoded.IsCombat} TG:{decoded.HasTarget} MV:{decoded.IsMoving} AL:{decoded.IsAlive} MT:{decoded.IsMounted}");
        }

        private static void TestFullState(string name, GameState original)
        {
            var sim = new TelemetrySimulator();
            var svc = new TelemetryService();
            var decoded = svc.Decode(sim.Generate(original));

            bool xOk = Math.Abs(decoded.CoordX - original.CoordX) < 0.11f;
            bool yOk = Math.Abs(decoded.CoordY - original.CoordY) < 0.11f;
            bool zOk = Math.Abs(decoded.CoordZ - original.CoordZ) < 0.11f;
            bool hOk = Math.Abs(decoded.RawFacing - original.RawFacing) < 0.0002f;
            bool fOk = decoded.IsCombat == original.IsCombat && decoded.IsAlive == original.IsAlive;

            Assert($"Full   [{name}]", xOk && yOk && zOk && hOk && fOk,
                $"X:{decoded.CoordX:F1} Y:{decoded.CoordY:F1} Z:{decoded.CoordZ:F1} H:{decoded.RawFacing:F4}");
        }

        private static void TestMotionEstimatorUsesCoordinateDeltas()
        {
            var nav = new NavigationKernel();

            var first = BuildState();
            first.CoordX = 0f;
            first.CoordZ = 0f;
            first.RawFacing = 2.40f;

            var second = BuildState();
            second.CoordX = 0f;
            second.CoordZ = 2f;
            second.RawFacing = 2.40f;
            second.IsMoving = true;

            nav.UpdateHeading(0, first);
            Thread.Sleep(40);
            nav.UpdateHeading(0, second);

            bool ok = second.IsHeadingLocked && Math.Abs(second.EstimatedHeading) < 0.25f;
            Assert("Nav    [Motion estimator uses coordinate deltas]", ok,
                $"locked={second.IsHeadingLocked} heading={second.EstimatedHeading:F3}");
        }

        private static void TestBreadcrumbFollowTargetTrailsPath()
        {
            var nav = new NavigationKernel();

            var leader0 = BuildState();
            leader0.CoordX = 0f;
            leader0.CoordZ = 0f;

            var leader1 = BuildState();
            leader1.CoordX = 0f;
            leader1.CoordZ = 5f;
            leader1.IsMoving = true;

            var leader2 = BuildState();
            leader2.CoordX = 5f;
            leader2.CoordZ = 5f;
            leader2.IsMoving = true;

            nav.UpdateHeading(0, leader0);
            nav.UpdateHeading(0, leader1);
            nav.UpdateHeading(0, leader2);

            (float x, float z) = nav.ResolveFollowTarget(0, leader2, 2.5f, 0f);
            bool ok = Math.Abs(x - 2.5f) < 0.15f && Math.Abs(z - 5f) < 0.15f;

            Assert("Nav    [Breadcrumb target trails path]", ok, $"x={x:F2} z={z:F2}");
        }

        private static void TestFollowBootstrapsForwardCalibration()
        {
            var input = new RecordingInputEngine();
            var settings = new BridgeSettings();
            var controller = new FollowController(input, new NavigationKernel(), settings);

            var leader = BuildState();
            leader.CoordZ = 10f;
            var follower = BuildState();

            controller.Update(1, follower, leader, IntPtr.Zero);

            var debug = ReadSlotDebug(controller, 1);
            bool ok = input.TappedScanCodes.Count == 1
                && input.TappedScanCodes[0] == settings.KeyForward
                && !debug.HasForwardBasis
                && debug.HasPendingCalibration;

            Assert("Follow [Bootstrap uses forward calibration]", ok,
                $"taps=[{string.Join(",", input.TappedScanCodes)}] hasBasis={debug.HasForwardBasis} pending={debug.HasPendingCalibration}");
        }

        private static void TestForwardCalibrationLearnsBasisAndDerivesLeftAxis()
        {
            var input = new RecordingInputEngine();
            var settings = new BridgeSettings();
            var controller = new FollowController(input, new NavigationKernel(), settings);

            var leaderAhead = BuildState();
            leaderAhead.CoordZ = 8f;
            var followerStart = BuildState();

            controller.Update(1, followerStart, leaderAhead, IntPtr.Zero);
            Thread.Sleep(150);

            var leaderLeft = BuildState();
            leaderLeft.CoordX = -5f;
            leaderLeft.CoordZ = 0.8f;
            var followerAfterProbe = BuildState();
            followerAfterProbe.CoordZ = 0.8f;
            followerAfterProbe.IsMoving = true;

            controller.Update(1, followerAfterProbe, leaderLeft, IntPtr.Zero);

            var debug = ReadSlotDebug(controller, 1);
            bool ok = input.TappedScanCodes.Count == 2
                && input.TappedScanCodes[0] == settings.KeyForward
                && input.TappedScanCodes[1] == settings.KeyLeft
                && debug.HasForwardBasis;

            Assert("Follow [Forward probe learns basis and derives left]", ok,
                $"taps=[{string.Join(",", input.TappedScanCodes)}] hasBasis={debug.HasForwardBasis} forward=({debug.Forward.X:F2},{debug.Forward.Y:F2})");
        }

        private static void TestFollowBackpedalsWhenGoalIsBehind()
        {
            var input = new RecordingInputEngine();
            var settings = new BridgeSettings();
            var controller = new FollowController(input, new NavigationKernel(), settings);
            SeedForwardBasis(controller, 1, new Vector2(0f, 1f));

            var leader = BuildState();
            leader.CoordZ = -5f;
            var follower = BuildState();

            controller.Update(1, follower, leader, IntPtr.Zero);

            bool ok = input.TappedScanCodes.Count == 1 && input.TappedScanCodes[0] == settings.KeyBack;
            Assert("Follow [Projected error backpedals]", ok,
                $"taps=[{string.Join(",", input.TappedScanCodes)}]");
        }

        private static void TestStrafeObservationRefinesForwardBasis()
        {
            var input = new RecordingInputEngine();
            var settings = new BridgeSettings();
            var controller = new FollowController(input, new NavigationKernel(), settings);
            SeedForwardBasis(controller, 1, new Vector2(0.8f, 0.6f));

            var leaderLeft = BuildState();
            leaderLeft.CoordX = -5f;
            leaderLeft.CoordZ = 0.25f;

            var followerStart = BuildState();
            controller.Update(1, followerStart, leaderLeft, IntPtr.Zero);

            var followerAfterStrafe = BuildState();
            followerAfterStrafe.CoordX = -0.8f;
            followerAfterStrafe.IsMoving = true;

            controller.Update(1, followerAfterStrafe, leaderLeft, IntPtr.Zero);

            var debug = ReadSlotDebug(controller, 1);
            bool ok = input.TappedScanCodes.Count >= 1
                && input.TappedScanCodes[0] == settings.KeyLeft
                && debug.Forward.Y > debug.Forward.X
                && debug.Forward.Y > 0.75f;

            Assert("Follow [Strafe observation refines forward basis]", ok,
                $"taps=[{string.Join(",", input.TappedScanCodes)}] forward=({debug.Forward.X:F2},{debug.Forward.Y:F2})");
        }

        private static void TestBackwardObservationRefinesForwardBasis()
        {
            var input = new RecordingInputEngine();
            var settings = new BridgeSettings();
            var controller = new FollowController(input, new NavigationKernel(), settings);
            SeedForwardBasis(controller, 1, new Vector2(0.6f, 0.8f));

            var leaderBehind = BuildState();
            leaderBehind.CoordX = -3f;
            leaderBehind.CoordZ = -4f;

            var followerStart = BuildState();
            controller.Update(1, followerStart, leaderBehind, IntPtr.Zero);

            var followerAfterBackpedal = BuildState();
            followerAfterBackpedal.CoordZ = -0.8f;
            followerAfterBackpedal.IsMoving = true;

            controller.Update(1, followerAfterBackpedal, leaderBehind, IntPtr.Zero);

            var debug = ReadSlotDebug(controller, 1);
            bool ok = input.TappedScanCodes.Count >= 1
                && input.TappedScanCodes[0] == settings.KeyBack
                && debug.Forward.Y > 0.90f
                && debug.Forward.X < 0.45f;

            Assert("Follow [Backward observation refines forward basis]", ok,
                $"taps=[{string.Join(",", input.TappedScanCodes)}] forward=({debug.Forward.X:F2},{debug.Forward.Y:F2})");
        }

        private static void TestFollowDoesNotEngageBeyondMaxDistance()
        {
            var input = new RecordingInputEngine();
            var settings = new BridgeSettings { FollowEngageDistanceMax = 20f };
            var controller = new FollowController(input, new NavigationKernel(), settings);
            SeedForwardBasis(controller, 1, new Vector2(0f, 1f));

            var leader = BuildState();
            leader.CoordZ = 25f;
            var follower = BuildState();

            controller.Update(1, follower, leader, IntPtr.Zero);

            var debug = ReadSlotDebug(controller, 1);
            bool ok = input.TappedScanCodes.Count == 0
                && !debug.HasForwardBasis
                && input.UpScanCodes.Contains(settings.KeyForward)
                && input.UpScanCodes.Contains(settings.KeyLeft)
                && input.UpScanCodes.Contains(settings.KeyBack)
                && input.UpScanCodes.Contains(settings.KeyRight);

            Assert("Follow [Does not engage beyond max distance]", ok,
                $"taps=[{string.Join(",", input.TappedScanCodes)}] hasBasis={debug.HasForwardBasis} ups=[{string.Join(",", input.UpScanCodes)}]");
        }

        private static void TestFollowUsesBreadcrumbTrailInsteadOfBodyChase()
        {
            var input = new RecordingInputEngine();
            var nav = new NavigationKernel();
            var settings = new BridgeSettings();
            var controller = new FollowController(input, nav, settings);
            SeedForwardBasis(controller, 1, new Vector2(0f, 1f));

            var leader0 = BuildState();
            leader0.CoordX = 0f;
            leader0.CoordZ = 0f;

            var leader1 = BuildState();
            leader1.CoordX = 0f;
            leader1.CoordZ = 5f;
            leader1.IsMoving = true;

            var leader2 = BuildState();
            leader2.CoordX = 5f;
            leader2.CoordZ = 5f;
            leader2.IsMoving = true;

            nav.UpdateHeading(0, leader0);
            nav.UpdateHeading(0, leader1);
            nav.UpdateHeading(0, leader2);
            leader2.HasTravelVector = false;
            leader2.SmoothedVelocityX = 0f;
            leader2.SmoothedVelocityZ = 0f;

            var follower = BuildState();
            follower.CoordX = 2.5f;
            follower.CoordZ = 2f;

            controller.Update(1, follower, leader2, IntPtr.Zero);

            bool ok = input.TappedScanCodes.Count == 1 && input.TappedScanCodes[0] == settings.KeyForward;
            Assert("Follow [Breadcrumb trail beats body chase]", ok,
                $"taps=[{string.Join(",", input.TappedScanCodes)}]");
        }

        private static void TestFollowerTrailMatchPrefersLocalBreadcrumbCarrot()
        {
            var input = new RecordingInputEngine();
            var nav = new NavigationKernel();
            var settings = new BridgeSettings();
            var controller = new FollowController(input, nav, settings);
            SeedForwardBasis(controller, 1, new Vector2(0f, 1f));

            var leader0 = BuildState();
            leader0.CoordX = 0f;
            leader0.CoordZ = 0f;

            var leader1 = BuildState();
            leader1.CoordX = 0f;
            leader1.CoordZ = 5f;
            leader1.IsMoving = true;

            var leader2 = BuildState();
            leader2.CoordX = 5f;
            leader2.CoordZ = 5f;
            leader2.IsMoving = true;

            nav.UpdateHeading(0, leader0);
            nav.UpdateHeading(0, leader1);
            nav.UpdateHeading(0, leader2);
            leader2.HasTravelVector = false;
            leader2.SmoothedVelocityX = 0f;
            leader2.SmoothedVelocityZ = 0f;

            var follower0 = BuildState();
            follower0.CoordX = 0f;
            follower0.CoordZ = 0f;

            var follower1 = BuildState();
            follower1.CoordX = 0f;
            follower1.CoordZ = 2f;

            nav.UpdateHeading(1, follower0);
            nav.UpdateHeading(1, follower1);

            controller.Update(1, follower1, leader2, IntPtr.Zero);

            bool ok = input.TappedScanCodes.Count == 1 && input.TappedScanCodes[0] == settings.KeyForward;
            Assert("Follow [Follower trail match prefers local breadcrumb carrot]", ok,
                $"taps=[{string.Join(",", input.TappedScanCodes)}]");
        }

        private static void TestWorseningProgressForcesRecalibration()
        {
            var input = new RecordingInputEngine();
            var settings = new BridgeSettings();
            var controller = new FollowController(input, new NavigationKernel(), settings);
            SeedForwardBasis(controller, 1, new Vector2(0f, 1f));
            SeedProgress(controller, 1, lastGoalDistance: 4f, worseningTicks: 2, stallTicks: 0);

            var leader = BuildState();
            leader.CoordZ = 10f;
            var follower = BuildState();

            controller.Update(1, follower, leader, IntPtr.Zero);

            var debug = ReadSlotDebug(controller, 1);
            bool ok = input.TappedScanCodes.Count == 1
                && input.TappedScanCodes[0] == settings.KeyForward
                && !debug.HasForwardBasis
                && debug.HasPendingCalibration;

            Assert("Follow [Worsening progress triggers recalibration]", ok,
                $"taps=[{string.Join(",", input.TappedScanCodes)}] hasBasis={debug.HasForwardBasis} pending={debug.HasPendingCalibration}");
        }

        private static void TestEmergencyStopResetsBasisAndReleasesKeys()
        {
            var input = new RecordingInputEngine();
            var settings = new BridgeSettings();
            var controller = new FollowController(input, new NavigationKernel(), settings);
            SeedForwardBasis(controller, 1, new Vector2(0f, 1f));

            controller.EmergencyStop(1, IntPtr.Zero);

            var debug = ReadSlotDebug(controller, 1);
            bool ok = !debug.HasForwardBasis
                && input.UpScanCodes.Contains(settings.KeyForward)
                && input.UpScanCodes.Contains(settings.KeyLeft)
                && input.UpScanCodes.Contains(settings.KeyBack)
                && input.UpScanCodes.Contains(settings.KeyRight);

            Assert("Follow [EmergencyStop resets basis and releases keys]", ok,
                $"hasBasis={debug.HasForwardBasis} ups=[{string.Join(",", input.UpScanCodes)}]");
        }

        private static void TestMountSyncUsesConfiguredKey()
        {
            var input = new RecordingInputEngine();
            var settings = new BridgeSettings { KeyMount = (byte)InputEngine.RiftKey.Num1 };
            var controller = new FollowController(input, new NavigationKernel(), settings);

            var leader = BuildState();
            leader.IsMounted = true;

            var follower = BuildState();
            follower.IsMounted = false;
            follower.CoordX = leader.CoordX;
            follower.CoordZ = leader.CoordZ;

            controller.Update(1, follower, leader, IntPtr.Zero);

            bool ok = input.TappedScanCodes.Count == 1 && input.TappedScanCodes[0] == settings.KeyMount;
            Assert("Follow [Mount sync uses configured key]", ok,
                $"taps=[{string.Join(",", input.TappedScanCodes)}]");
        }

        private static void TestAssistUsesConfiguredKey()
        {
            var input = new RecordingInputEngine();
            var settings = new BridgeSettings { KeyInteract = (byte)InputEngine.RiftKey.Num2 };
            var controller = new FollowController(input, new NavigationKernel(), settings);

            var leader = BuildState();
            leader.HasTarget = true;

            var follower = BuildState();
            follower.HasTarget = false;
            follower.CoordX = leader.CoordX;
            follower.CoordZ = leader.CoordZ;

            controller.Update(1, follower, leader, IntPtr.Zero);

            bool ok = input.TappedScanCodes.Count == 1 && input.TappedScanCodes[0] == settings.KeyInteract;
            Assert("Follow [Assist uses configured key]", ok,
                $"taps=[{string.Join(",", input.TappedScanCodes)}]");
        }

        private static void TestZoneChangeClearsHeadingHistory()
        {
            var nav = new NavigationKernel();

            var first = BuildState();
            first.ZoneHash = 1;
            first.CoordX = 0f;
            first.CoordZ = 0f;

            var second = BuildState();
            second.ZoneHash = 1;
            second.CoordX = 0f;
            second.CoordZ = 2f;
            second.IsMoving = true;

            var third = BuildState();
            third.ZoneHash = 2;
            third.CoordX = 50f;
            third.CoordZ = 50f;
            third.IsMoving = false;

            nav.UpdateHeading(1, first);
            nav.UpdateHeading(1, second);
            bool zoneChanged = nav.UpdateHeading(1, third);

            bool ok = zoneChanged && !third.IsHeadingLocked && Math.Abs(third.EstimatedHeading) < 0.0001f;
            Assert("Nav    [Zone change clears heading history]", ok,
                $"zoneChanged={zoneChanged} locked={third.IsHeadingLocked} heading={third.EstimatedHeading:F4}");
        }

        private static void TestZoneMismatchStopsFollow()
        {
            var input = new RecordingInputEngine();
            var settings = new BridgeSettings();
            var controller = new FollowController(input, new NavigationKernel(), settings);
            SeedForwardBasis(controller, 1, new Vector2(0f, 1f));

            var leader = BuildState();
            leader.ZoneHash = 10;
            leader.CoordZ = 20f;

            var follower = BuildState();
            follower.ZoneHash = 20;

            controller.Update(1, follower, leader, IntPtr.Zero);

            var debug = ReadSlotDebug(controller, 1);
            bool ok = !debug.HasForwardBasis
                && input.UpScanCodes.Contains(settings.KeyForward)
                && input.UpScanCodes.Contains(settings.KeyLeft)
                && input.UpScanCodes.Contains(settings.KeyBack)
                && input.UpScanCodes.Contains(settings.KeyRight);

            Assert("Follow [Zone mismatch stops follow]", ok,
                $"hasBasis={debug.HasForwardBasis} ups=[{string.Join(",", input.UpScanCodes)}]");
        }

        private static void TestWindowFilterProcessIdOrder()
        {
            var windows = new[]
            {
                new RiftWindowInfo { Title = "RIFT", ProcessName = "rift_x64", Hwnd = (IntPtr)0x1001, ProcessId = 101, BaseAddress = 0 },
                new RiftWindowInfo { Title = "RIFT", ProcessName = "rift_x64", Hwnd = (IntPtr)0x1002, ProcessId = 202, BaseAddress = 0 },
                new RiftWindowInfo { Title = "RIFT", ProcessName = "rift_x64", Hwnd = (IntPtr)0x1003, ProcessId = 303, BaseAddress = 0 },
            };

            var filtered = RiftWindowService.FilterWindows(windows, new RiftWindowFilter
            {
                ProcessIds = new[] { 303, 101 }
            });

            bool ok = filtered.Count == 2 && filtered[0].ProcessId == 303 && filtered[1].ProcessId == 101;
            Assert("Window [PID list preserves order]", ok,
                $"got [{string.Join(",", filtered.Select(window => window.ProcessId))}]");
        }

        private static void TestRoleLockCapturesCurrentAssignments()
        {
            var slots = new[]
            {
                new RiftWindowSlot
                {
                    Window = new RiftWindowInfo { Title = "Leader", ProcessName = "rift_x64", Hwnd = (IntPtr)0x1003, ProcessId = 303, BaseAddress = 0 }
                },
                new RiftWindowSlot
                {
                    Window = null,
                    ExpectedProcessId = 999
                },
                new RiftWindowSlot
                {
                    Window = new RiftWindowInfo { Title = "Follower", ProcessName = "rift_x64", Hwnd = (IntPtr)0x1001, ProcessId = 101, BaseAddress = 0 }
                }
            };

            var states = new[]
            {
                new GameState { IsValid = true, PlayerTag = "LEAD" },
                new GameState(),
                new GameState { IsValid = true, PlayerTag = "FOL1" }
            };

            List<RiftLockedRoleAssignment> captured = RiftWindowService.CaptureRoleAssignments(slots, states);
            bool ok = captured.Count == 2
                && captured[0].ProcessId == 303
                && captured[0].ExpectedPlayerTag == "LEAD"
                && captured[1].ProcessId == 101
                && captured[1].ExpectedPlayerTag == "FOL1";
            Assert("Window [Role lock captures current slot order]", ok,
                $"got [{string.Join(",", captured.Select(role => $"{role.ProcessId}:{role.ExpectedPlayerTag ?? "null"}"))}]");
        }

        private static void TestRoleLockMatchesExpectedWindowAndTag()
        {
            var slot = new RiftWindowSlot
            {
                Window = new RiftWindowInfo { Title = "Follower", ProcessName = "rift_x64", Hwnd = (IntPtr)0x1001, ProcessId = 101, BaseAddress = 0 }
            };

            var state = new GameState
            {
                IsValid = true,
                PlayerTag = "fol1"
            };

            var assignment = new RiftLockedRoleAssignment
            {
                ProcessId = 101,
                ExpectedPlayerTag = "FOL1",
                InitialHwnd = (IntPtr)0x1001,
                InitialTitle = "Follower"
            };

            var wrongTagState = new GameState
            {
                IsValid = true,
                PlayerTag = "OTHR"
            };

            bool ok = RiftWindowService.MatchesLockedRole(slot, state, assignment)
                && !RiftWindowService.MatchesLockedRole(slot, wrongTagState, assignment);

            Assert("Window [Role lock validates expected tag]", ok,
                $"match={RiftWindowService.MatchesLockedRole(slot, state, assignment)} wrongTag={RiftWindowService.MatchesLockedRole(slot, wrongTagState, assignment)}");
        }

        private static void TestRoleLockPrefersForegroundWindowAsLeader()
        {
            var slots = new[]
            {
                new RiftWindowSlot
                {
                    Window = new RiftWindowInfo { Title = "Follower", ProcessName = "rift_x64", Hwnd = (IntPtr)0x1001, ProcessId = 101, BaseAddress = 0 }
                },
                new RiftWindowSlot
                {
                    Window = new RiftWindowInfo { Title = "Leader", ProcessName = "rift_x64", Hwnd = (IntPtr)0x1003, ProcessId = 303, BaseAddress = 0 }
                }
            };

            var states = new[]
            {
                new GameState { IsValid = true, PlayerTag = "FOL1" },
                new GameState { IsValid = true, PlayerTag = "LEAD" }
            };

            List<RiftLockedRoleAssignment> captured = RiftWindowService.CaptureRoleAssignments(
                slots,
                states,
                preferredLeaderHwnd: (IntPtr)0x1003);

            bool ok = captured.Count == 2
                && captured[0].ProcessId == 303
                && captured[0].ExpectedPlayerTag == "LEAD"
                && captured[1].ProcessId == 101
                && captured[1].ExpectedPlayerTag == "FOL1";

            Assert("Window [Role lock prefers foreground leader]", ok,
                $"got [{string.Join(",", captured.Select(role => $"{role.ProcessId}:{role.ExpectedPlayerTag ?? "null"}"))}]");
        }

        private static void TestWindowFilterHwndOrder()
        {
            var windows = new[]
            {
                new RiftWindowInfo { Title = "RIFT", ProcessName = "rift_x64", Hwnd = (IntPtr)0x1001, ProcessId = 101, BaseAddress = 0 },
                new RiftWindowInfo { Title = "RIFT", ProcessName = "rift_x64", Hwnd = (IntPtr)0x1002, ProcessId = 202, BaseAddress = 0 },
                new RiftWindowInfo { Title = "RIFT", ProcessName = "rift_x64", Hwnd = (IntPtr)0x1003, ProcessId = 303, BaseAddress = 0 },
            };

            var filtered = RiftWindowService.FilterWindows(windows, new RiftWindowFilter
            {
                Hwnds = new[] { (IntPtr)0x1002, (IntPtr)0x1001 }
            });

            bool ok = filtered.Count == 2 && filtered[0].Hwnd == (IntPtr)0x1002 && filtered[1].Hwnd == (IntPtr)0x1001;
            Assert("Window [HWND list preserves order]", ok,
                $"got [{string.Join(",", filtered.Select(window => RiftWindowService.FormatHwnd(window.Hwnd)))}]");
        }

        private static void TestWindowSlotsPreserveMissingProcessIdEntries()
        {
            var windows = new[]
            {
                new RiftWindowInfo { Title = "RIFT", ProcessName = "rift_x64", Hwnd = (IntPtr)0x1002, ProcessId = 202, BaseAddress = 0 },
            };

            var slots = RiftWindowService.BuildWindowSlots(windows, new RiftWindowFilter
            {
                ProcessIds = new[] { 101, 202 }
            });

            bool ok = slots.Count == 2
                && slots[0].ExpectedProcessId == 101
                && slots[0].Window is null
                && slots[1].ExpectedProcessId == 202
                && slots[1].Window?.ProcessId == 202;

            Assert("Window [PID slots preserve missing entries]", ok,
                $"got [{string.Join(",", slots.Select(slot => slot.Window?.ProcessId.ToString() ?? $"missing:{slot.ExpectedProcessId}"))}]");
        }

        private static void TestWindowSlotsPreserveMissingHwndEntries()
        {
            var windows = new[]
            {
                new RiftWindowInfo { Title = "RIFT", ProcessName = "rift_x64", Hwnd = (IntPtr)0x1002, ProcessId = 202, BaseAddress = 0 },
            };

            var slots = RiftWindowService.BuildWindowSlots(windows, new RiftWindowFilter
            {
                Hwnds = new[] { (IntPtr)0x1001, (IntPtr)0x1002 }
            });

            bool ok = slots.Count == 2
                && slots[0].ExpectedHwnd == (IntPtr)0x1001
                && slots[0].Window is null
                && slots[1].ExpectedHwnd == (IntPtr)0x1002
                && slots[1].Window?.Hwnd == (IntPtr)0x1002;

            Assert("Window [HWND slots preserve missing entries]", ok,
                $"got [{string.Join(",", slots.Select(slot => slot.Window is not null ? RiftWindowService.FormatHwnd(slot.Window.Hwnd) : $"missing:{RiftWindowService.FormatHwnd(slot.ExpectedHwnd ?? IntPtr.Zero)}"))}]");
        }

        private static void SeedForwardBasis(FollowController controller, int slot, Vector2 forward)
        {
            object slotState = GetSlotState(controller, slot);
            Type slotStateType = slotState.GetType();
            slotStateType.GetField("Forward", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(slotState, forward);
            slotStateType.GetField("HasForwardBasis", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(slotState, true);
        }

        private static void SeedProgress(FollowController controller, int slot, float lastGoalDistance, int worseningTicks, int stallTicks)
        {
            object slotState = GetSlotState(controller, slot);
            Type slotStateType = slotState.GetType();
            slotStateType.GetField("LastGoalDistance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(slotState, lastGoalDistance);
            slotStateType.GetField("ConsecutiveWorseningTicks", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(slotState, worseningTicks);
            slotStateType.GetField("ConsecutiveStallTicks", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(slotState, stallTicks);
        }

        private static SlotDebug ReadSlotDebug(FollowController controller, int slot)
        {
            object slotState = GetSlotState(controller, slot);
            Type slotStateType = slotState.GetType();
            return new SlotDebug
            {
                Forward = (Vector2)slotStateType.GetField("Forward", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(slotState)!,
                HasForwardBasis = (bool)slotStateType.GetField("HasForwardBasis", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(slotState)!,
                HasPendingCalibration = slotStateType.GetField("PendingForwardCalibration", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(slotState) is not null,
                LastGoalDistance = (float)slotStateType.GetField("LastGoalDistance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(slotState)!,
                WorseningTicks = (int)slotStateType.GetField("ConsecutiveWorseningTicks", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(slotState)!,
                StallTicks = (int)slotStateType.GetField("ConsecutiveStallTicks", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(slotState)!,
            };
        }

        private static object GetSlotState(FollowController controller, int slot)
        {
            var slotStatesField = typeof(FollowController).GetField("_slotStates", BindingFlags.NonPublic | BindingFlags.Instance);
            if (slotStatesField == null)
            {
                throw new InvalidOperationException("Could not find _slotStates field.");
            }

            var slotStates = (Array)slotStatesField.GetValue(controller)!;
            return slotStates.GetValue(slot)!;
        }

        private static float NormalizeProtocolHeading(float heading)
        {
            float twoPi = (float)(2 * Math.PI);
            while (heading < 0)
            {
                heading += twoPi;
            }

            while (heading > twoPi)
            {
                heading -= twoPi;
            }

            return heading;
        }

        private static GameState BuildState() => new GameState
        {
            IsValid = true,
            IsAlive = true,
            PlayerHP = 255,
            TargetHP = 0,
            CoordX = 0f,
            CoordY = 0f,
            CoordZ = 0f,
            RawFacing = 0f,
            ZoneHash = 0,
            PlayerTag = "TEST",
        };

        private sealed class RecordingInputEngine : InputEngine
        {
            public List<byte> TappedScanCodes { get; } = new();
            public List<int> TapDurations { get; } = new();
            public List<byte> DownScanCodes { get; } = new();
            public List<byte> UpScanCodes { get; } = new();

            public override void TapScanCode(IntPtr hwnd, byte scanCode, int durationMs = 60)
            {
                TappedScanCodes.Add(scanCode);
                TapDurations.Add(durationMs);
            }

            public override void SendScanCodeDown(IntPtr hwnd, byte scanCode)
            {
                DownScanCodes.Add(scanCode);
            }

            public override void SendScanCodeUp(IntPtr hwnd, byte scanCode)
            {
                UpScanCodes.Add(scanCode);
            }
        }

        private sealed class SlotDebug
        {
            public Vector2 Forward { get; init; }
            public bool HasForwardBasis { get; init; }
            public bool HasPendingCalibration { get; init; }
            public float LastGoalDistance { get; init; }
            public int WorseningTicks { get; init; }
            public int StallTicks { get; init; }
        }

        private static void Assert(string label, bool condition, string failMsg = "")
        {
            if (condition)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  ✅ PASS  {label}");
                _pass++;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  ❌ FAIL  {label}  →  {failMsg}");
                _fail++;
            }

            Console.ResetColor();
        }
    }
}
