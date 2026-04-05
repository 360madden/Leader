using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using LeaderDecoder.Models;
using LeaderDecoder.Services;

// ────────────────────────────────────────────────────────────────────────────
// Leader Telemetry Roundtrip Tests
// Run with: dotnet run --project LeaderDecoder -- --test
// Validates Encoder.lua → TelemetryService decode pipeline.
// ────────────────────────────────────────────────────────────────────────────

namespace LeaderDecoder
{
    internal static class RoundtripTests
    {
        private static int _pass = 0, _fail = 0;

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

            // ── Coord encode/decode ──────────────────────────────────────
            TestCoord("Zero position",     0f);
            TestCoord("Positive X",        1234.5f);
            TestCoord("Negative Z",       -987.6f);
            TestCoord("Large coord",       50000.1f);
            TestCoord("Negative large",   -50000.1f);
            TestCoord("Sub-unit prec",     0.1f);
            TestCoord("Sub-unit prec 2",   0.09f);  // below 0.1 precision — expect ~0

            // ── Heading encode/decode ────────────────────────────────────
            TestHeading("Zero radians",    0f);
            TestHeading("PI/2 (East)",     (float)(Math.PI / 2));
            TestHeading("PI (South)",      (float)Math.PI);
            TestHeading("3PI/2 (West)",    (float)(3 * Math.PI / 2));
            TestHeading("2PI (North)",     (float)(2 * Math.PI));
            TestHeading("Typical angle",   2.3456f);
            TestHeading("Negative wraps", -0.7500f);

            // ── Zone hash passthrough ────────────────────────────────────
            TestZoneHash("Zero hash",   0);
            TestZoneHash("Max hash",    255);
            TestZoneHash("Mid hash",    128);

            // ── Player tag passthrough ───────────────────────────────────
            TestPlayerTag("Exact tag", "BOB1", "BOB1");
            TestPlayerTag("Normalized tag", "ab-cd", "ABCD");

            // ── Flag bitfield ────────────────────────────────────────────
            TestFlags("All clear",     false, false, false, false, false);
            TestFlags("Combat only",   true,  false, false, true,  false);
            TestFlags("All set",       true,  true,  true,  true,  true);
            TestFlags("Mounted+Alive", false, false, false, true,  true);
            TestMotionEstimatorUsesCoordinateDeltas();
            TestBreadcrumbFollowTargetTrailsPath();
            TestFollowBootstrapsForwardProbe();
            TestFollowProjectsIntoStrafe();
            TestFollowBackpedalsWhenGoalIsBehind();
            TestFollowUsesBreadcrumbTrailInsteadOfBodyChase();
            TestEmergencyStopResetsBasisState();
            TestMountSyncUsesConfiguredKey();
            TestAssistUsesConfiguredKey();
            TestZoneChangeClearsHeadingHistory();
            TestZoneMismatchStopsFollow();
            TestWindowFilterProcessIdOrder();
            TestWindowFilterHwndOrder();
            TestWindowSlotsPreserveMissingProcessIdEntries();
            TestWindowSlotsPreserveMissingHwndEntries();

            // ── Full state roundtrip ─────────────────────────────────────
            TestFullState("Typical moving combat state", new GameState
            {
                CoordX    = 5432.1f, CoordY = 23.4f, CoordZ = -1234.5f,
                RawFacing = 1.5708f, ZoneHash = 42,
                PlayerHP  = 200,     TargetHP = 127,
                IsCombat  = true,    HasTarget = true, IsMoving = false,
                IsAlive   = true,    IsMounted = false
            });

            // ── Summary ──────────────────────────────────────────────────
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

        // ── Test helpers ──────────────────────────────────────────────────

        private static void TestCoord(string name, float value)
        {
            var sim = new TelemetrySimulator();
            var svc = new TelemetryService();

            float round = value < -838860f || value > 838860f ? 0 : (float)Math.Round(value * 10) / 10;
            if (Math.Abs(value * 10 - Math.Floor(value * 10)) > 0.5f && Math.Abs(value) >= 0.1f)
                round = (float)Math.Round(value, 1);

            var state = BuildState();
            state.CoordX = value;
            var decoded = svc.Decode(sim.Generate(state));

            float expected = (float)Math.Round(value * 10) / 10f;
            float actual   = (float)Math.Round(decoded.CoordX * 10) / 10f;
            Assert($"Coord  [{name}] ({value}→{expected})", Math.Abs(actual - expected) < 0.11f, $"got {actual}");
        }

        private static void TestHeading(string name, float rad)
        {
            var sim = new TelemetrySimulator();
            var svc = new TelemetryService();
            var state = BuildState();
            state.RawFacing = NormalizeProtocolHeading(rad);
            var decoded = svc.Decode(sim.Generate(state));
            float expected = state.RawFacing;
            float actual   = decoded.RawFacing;
            Assert($"Heading[{name}] ({rad:F4})", Math.Abs(actual - expected) < 0.0002f, $"got {actual:F5}");
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

        private static void TestZoneHash(string name, byte hash)
        {
            var sim = new TelemetrySimulator();
            var svc = new TelemetryService();
            var state = BuildState(); state.ZoneHash = hash;
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
            state.IsCombat = combat; state.HasTarget = target; state.IsMoving = moving;
            state.IsAlive  = alive;  state.IsMounted = mounted;
            var decoded = svc.Decode(sim.Generate(state));
            bool ok = decoded.IsCombat  == combat
                   && decoded.HasTarget == target
                   && decoded.IsMoving  == moving
                   && decoded.IsAlive   == alive
                   && decoded.IsMounted == mounted;
            Assert($"Flags  [{name}]", ok, $"got CB:{decoded.IsCombat} TG:{decoded.HasTarget} MV:{decoded.IsMoving} AL:{decoded.IsAlive} MT:{decoded.IsMounted}");
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

        private static void TestEmergencyStopResetsFollowState()
        {
            var controller = new FollowController(new InputEngine(), new NavigationKernel(), new BridgeSettings());
            var type = typeof(FollowController);

            var movingField = type.GetField("_isMovingForward", BindingFlags.NonPublic | BindingFlags.Instance);
            var lastErrorField = type.GetField("_lastError", BindingFlags.NonPublic | BindingFlags.Instance);
            var wSinceField = type.GetField("_wForwardSince", BindingFlags.NonPublic | BindingFlags.Instance);
            var distField = type.GetField("_distanceAtPress", BindingFlags.NonPublic | BindingFlags.Instance);
            var headingField = type.GetField("_controlHeading", BindingFlags.NonPublic | BindingFlags.Instance);
            var knownField = type.GetField("_controlHeadingKnown", BindingFlags.NonPublic | BindingFlags.Instance);
            var settleField = type.GetField("_turnSettledUntil", BindingFlags.NonPublic | BindingFlags.Instance);

            if (movingField == null || lastErrorField == null || wSinceField == null || distField == null
                || headingField == null || knownField == null || settleField == null)
            {
                Assert("Follow [EmergencyStop resets slot state]", false, "reflection lookup failed");
                return;
            }

            var moving = (bool[])movingField.GetValue(controller)!;
            var lastError = (float[])lastErrorField.GetValue(controller)!;
            var wSince = (DateTime[])wSinceField.GetValue(controller)!;
            var dist = (float[])distField.GetValue(controller)!;
            var heading = (float[])headingField.GetValue(controller)!;
            var known = (bool[])knownField.GetValue(controller)!;
            var settle = (DateTime[])settleField.GetValue(controller)!;

            moving[1] = true;
            lastError[1] = 1.25f;
            wSince[1] = DateTime.Now;
            dist[1] = 9.5f;
            heading[1] = 1.75f;
            known[1] = true;
            settle[1] = DateTime.Now.AddSeconds(2);

            controller.EmergencyStop(1, IntPtr.Zero);

            bool ok = !moving[1]
                   && Math.Abs(lastError[1]) < 0.0001f
                   && wSince[1] == DateTime.MinValue
                   && Math.Abs(dist[1] - float.MaxValue) < 0.0001f
                   && Math.Abs(heading[1]) < 0.0001f
                   && !known[1]
                   && settle[1] == DateTime.MinValue;

            Assert("Follow [EmergencyStop resets slot state]", ok,
                $"moving={moving[1]} err={lastError[1]} since={wSince[1]:O} dist={dist[1]} heading={heading[1]} known={known[1]} settle={settle[1]:O}");
        }

        private static void TestUnknownHeadingTurnsFirst()
        {
            var input = new RecordingInputEngine();
            var settings = new BridgeSettings
            {
                KeyTurnLeft = (byte)InputEngine.RiftKey.Num3,
                KeyTurnRight = (byte)InputEngine.RiftKey.Num4
            };
            var controller = new FollowController(input, new NavigationKernel(), settings);

            var leader = BuildState();
            leader.CoordX = 10f;
            leader.CoordZ = 10f;

            var follower = BuildState();
            follower.CoordX = 0f;
            follower.CoordZ = 0f;
            follower.IsHeadingLocked = false;
            follower.EstimatedHeading = 0f;

            controller.Update(1, follower, leader, IntPtr.Zero);

            bool ok = input.DownScanCodes.Count == 0
                && input.TappedScanCodes.Count == 1
                && input.TappedScanCodes[0] == settings.KeyTurnRight;

            Assert("Follow [Unknown heading turns first]", ok,
                $"downs=[{string.Join(",", input.DownScanCodes)}] taps=[{string.Join(",", input.TappedScanCodes)}]");
        }

        private static void TestStationaryFollowerTurnsBeforeMove()
        {
            var input = new RecordingInputEngine();
            var settings = new BridgeSettings
            {
                KeyTurnLeft = (byte)InputEngine.RiftKey.Num3,
                KeyTurnRight = (byte)InputEngine.RiftKey.Num4
            };
            var controller = new FollowController(input, new NavigationKernel(), settings);

            var leader = BuildState();
            leader.CoordX = 10f;
            leader.CoordZ = 0f;

            var follower = BuildState();
            follower.CoordX = 0f;
            follower.CoordZ = 0f;
            follower.IsHeadingLocked = true;
            follower.IsMoving = false;
            follower.EstimatedHeading = 0f;

            controller.Update(1, follower, leader, IntPtr.Zero);

            bool ok = input.DownScanCodes.Count == 0
                && input.TappedScanCodes.Count == 1
                && input.TappedScanCodes[0] == settings.KeyTurnRight;

            Assert("Follow [Stationary follower turns before move]", ok,
                $"downs=[{string.Join(",", input.DownScanCodes)}] taps=[{string.Join(",", input.TappedScanCodes)}]");
        }

        private static void TestTurnCommandSeedsControlHeading()
        {
            var input = new RecordingInputEngine();
            var settings = new BridgeSettings { TurnRateRadiansPerSecond = 3.0f };
            var controller = new FollowController(input, new NavigationKernel(), settings);
            var headingField = typeof(FollowController).GetField("_controlHeading", BindingFlags.NonPublic | BindingFlags.Instance);
            var knownField = typeof(FollowController).GetField("_controlHeadingKnown", BindingFlags.NonPublic | BindingFlags.Instance);

            if (headingField == null || knownField == null)
            {
                Assert("Follow [Turn command seeds control heading]", false, "reflection lookup failed");
                return;
            }

            var leader = BuildState();
            leader.CoordX = 10f;
            leader.CoordZ = 0f;

            var follower = BuildState();
            follower.CoordX = 0f;
            follower.CoordZ = 0f;
            follower.IsHeadingLocked = false;
            follower.EstimatedHeading = 0f;

            controller.Update(1, follower, leader, IntPtr.Zero);

            var headings = (float[])headingField.GetValue(controller)!;
            var known = (bool[])knownField.GetValue(controller)!;
            bool ok = known[1] && headings[1] > 0f;

            Assert("Follow [Turn command seeds control heading]", ok,
                $"known={known[1]} heading={headings[1]:F4}");
        }

        private static void TestTurnSettleBlocksForwardAdvance()
        {
            var input = new RecordingInputEngine();
            var settings = new BridgeSettings();
            var controller = new FollowController(input, new NavigationKernel(), settings);
            var settleField = typeof(FollowController).GetField("_turnSettledUntil", BindingFlags.NonPublic | BindingFlags.Instance);

            if (settleField == null)
            {
                Assert("Follow [Turn settle blocks forward advance]", false, "reflection lookup failed");
                return;
            }

            var settle = (DateTime[])settleField.GetValue(controller)!;
            settle[1] = DateTime.Now.AddSeconds(1);

            var leader = BuildState();
            leader.CoordZ = 10f;

            var follower = BuildState();
            follower.IsHeadingLocked = true;
            follower.IsMoving = true;
            follower.EstimatedHeading = 0f;

            controller.Update(1, follower, leader, IntPtr.Zero);

            bool ok = !input.DownScanCodes.Contains(settings.KeyForward)
                && !input.TappedScanCodes.Contains(settings.KeyForward);

            Assert("Follow [Turn settle blocks forward advance]", ok,
                $"downs=[{string.Join(",", input.DownScanCodes)}] taps=[{string.Join(",", input.TappedScanCodes)}]");
        }

        private static void TestFollowReleasesWithinBand()
        {
            var controller = new FollowController(new InputEngine(), new NavigationKernel(), new BridgeSettings());
            var movingField = typeof(FollowController).GetField("_isMovingForward", BindingFlags.NonPublic | BindingFlags.Instance);

            if (movingField == null)
            {
                Assert("Follow [Re-entering band releases W]", false, "reflection lookup failed");
                return;
            }

            var leader = BuildState();
            leader.CoordZ = 10f;

            var follower = BuildState();
            follower.IsHeadingLocked = false;

            controller.Update(1, follower, leader, IntPtr.Zero);

            follower.CoordZ = 7f;
            controller.Update(1, follower, leader, IntPtr.Zero);

            var moving = (bool[])movingField.GetValue(controller)!;
            Assert("Follow [Re-entering band releases W]", !moving[1], $"moving={moving[1]}");
        }

        private static void TestWithinBandDoesNotTurnSpam()
        {
            var input = new RecordingInputEngine();
            var controller = new FollowController(input, new NavigationKernel(), new BridgeSettings());

            var leader = BuildState();
            leader.CoordX = 2f;
            leader.CoordZ = 0f;

            var follower = BuildState();
            follower.CoordX = 0f;
            follower.CoordZ = 0f;
            follower.IsHeadingLocked = true;
            follower.IsMoving = true;
            follower.EstimatedHeading = 0f;

            controller.Update(1, follower, leader, IntPtr.Zero);

            bool ok = input.TappedScanCodes.Count == 0;
            Assert("Follow [Within band suppresses steering]", ok,
                $"taps=[{string.Join(",", input.TappedScanCodes)}]");
        }

        private static void TestLargeAngleTurnsBeforeForwardHold()
        {
            var input = new RecordingInputEngine();
            var settings = new BridgeSettings
            {
                KeyTurnLeft = (byte)InputEngine.RiftKey.Num3,
                KeyTurnRight = (byte)InputEngine.RiftKey.Num4
            };
            var controller = new FollowController(input, new NavigationKernel(), settings);

            var leader = BuildState();
            leader.CoordX = 10f;
            leader.CoordZ = 0f;

            var follower = BuildState();
            follower.CoordX = 0f;
            follower.CoordZ = 0f;
            follower.IsHeadingLocked = true;
            follower.IsMoving = true;
            follower.EstimatedHeading = 0f;

            controller.Update(1, follower, leader, IntPtr.Zero);

            bool ok = !input.DownScanCodes.Contains(settings.KeyForward)
                && input.TappedScanCodes.Count == 1
                && input.TappedScanCodes[0] == settings.KeyTurnRight;

            Assert("Follow [Large angle turns before W hold]", ok,
                $"downs=[{string.Join(",", input.DownScanCodes)}] taps=[{string.Join(",", input.TappedScanCodes)}]");
        }

        private static void TestTrailingAnchorStopsOverchase()
        {
            var input = new RecordingInputEngine();
            var settings = new BridgeSettings();
            var controller = new FollowController(input, new NavigationKernel(), settings);
            var movingField = typeof(FollowController).GetField("_isMovingForward", BindingFlags.NonPublic | BindingFlags.Instance);

            if (movingField == null)
            {
                Assert("Follow [Trailing anchor avoids overchase]", false, "reflection lookup failed");
                return;
            }

            var leader = BuildState();
            leader.CoordZ = 10f;
            leader.IsHeadingLocked = true;
            leader.EstimatedHeading = 0f;
            leader.IsMoving = true;

            var follower = BuildState();
            follower.CoordZ = 6.2f;
            follower.IsHeadingLocked = true;
            follower.IsMoving = true;
            follower.EstimatedHeading = 0f;

            controller.Update(1, follower, leader, IntPtr.Zero);

            var moving = (bool[])movingField.GetValue(controller)!;
            bool ok = !moving[1]
                && !input.DownScanCodes.Contains(settings.KeyForward)
                && !input.TappedScanCodes.Contains(settings.KeyForward);

            Assert("Follow [Trailing anchor avoids overchase]", ok,
                $"moving={moving[1]} downs=[{string.Join(",", input.DownScanCodes)}] taps=[{string.Join(",", input.TappedScanCodes)}]");
        }

        private static void TestApproachBandUsesForwardPulse()
        {
            var input = new RecordingInputEngine();
            var settings = new BridgeSettings();
            var controller = new FollowController(input, new NavigationKernel(), settings);
            var movingField = typeof(FollowController).GetField("_isMovingForward", BindingFlags.NonPublic | BindingFlags.Instance);

            if (movingField == null)
            {
                Assert("Follow [Approach band uses W pulse]", false, "reflection lookup failed");
                return;
            }

            var leader = BuildState();
            leader.CoordZ = 10f;
            leader.IsHeadingLocked = true;
            leader.EstimatedHeading = 0f;
            leader.IsMoving = true;

            var follower = BuildState();
            follower.CoordZ = 4.8f;
            follower.IsHeadingLocked = true;
            follower.IsMoving = true;
            follower.EstimatedHeading = 0f;

            controller.Update(1, follower, leader, IntPtr.Zero);

            var moving = (bool[])movingField.GetValue(controller)!;
            bool ok = !moving[1]
                && input.TappedScanCodes.Contains(settings.KeyForward)
                && !input.DownScanCodes.Contains(settings.KeyForward);

            Assert("Follow [Approach band uses W pulse]", ok,
                $"moving={moving[1]} downs=[{string.Join(",", input.DownScanCodes)}] taps=[{string.Join(",", input.TappedScanCodes)}]");
        }

        private static void TestSteeringTapScalesWithError()
        {
            var input = new RecordingInputEngine();
            var settings = new BridgeSettings
            {
                KeyTurnLeft = (byte)InputEngine.RiftKey.Num3,
                KeyTurnRight = (byte)InputEngine.RiftKey.Num4
            };
            var controller = new FollowController(input, new NavigationKernel(), settings);

            var leader = BuildState();
            leader.CoordX = 10f;
            leader.CoordZ = 0f;

            var follower = BuildState();
            follower.IsHeadingLocked = true;
            follower.IsMoving = true;
            follower.EstimatedHeading = 0f;

            controller.Update(1, follower, leader, IntPtr.Zero);
            int largeErrorIndex = input.TappedScanCodes.IndexOf(settings.KeyTurnRight);
            if (largeErrorIndex < 0)
            {
                Assert("Follow [Steering scales with error]", false, $"tapCodes=[{string.Join(",", input.TappedScanCodes)}]");
                return;
            }

            int largeErrorTap = input.TapDurations[largeErrorIndex];

            var inputSmall = new RecordingInputEngine();
            var controllerSmall = new FollowController(inputSmall, new NavigationKernel(), settings);
            var leaderSmall = BuildState();
            leaderSmall.CoordX = 3f;
            leaderSmall.CoordZ = 10f;

            var followerSmall = BuildState();
            followerSmall.IsHeadingLocked = true;
            followerSmall.IsMoving = true;
            followerSmall.EstimatedHeading = 0f;

            controllerSmall.Update(1, followerSmall, leaderSmall, IntPtr.Zero);
            int smallErrorIndex = inputSmall.TappedScanCodes.IndexOf(settings.KeyTurnRight);
            if (smallErrorIndex < 0)
            {
                Assert("Follow [Steering scales with error]", false, $"smallTapCodes=[{string.Join(",", inputSmall.TappedScanCodes)}]");
                return;
            }

            int smallErrorTap = inputSmall.TapDurations[smallErrorIndex];
            bool ok = largeErrorTap > smallErrorTap;
            Assert("Follow [Steering scales with error]", ok,
                $"large={largeErrorTap} small={smallErrorTap}");
        }

        private static void TestAntiStuckClearsForwardState()
        {
            var controller = new FollowController(new InputEngine(), new NavigationKernel(), new BridgeSettings());
            var type = typeof(FollowController);

            var movingField = type.GetField("_isMovingForward", BindingFlags.NonPublic | BindingFlags.Instance);
            var lastErrorField = type.GetField("_lastError", BindingFlags.NonPublic | BindingFlags.Instance);
            var wSinceField = type.GetField("_wForwardSince", BindingFlags.NonPublic | BindingFlags.Instance);
            var distField = type.GetField("_distanceAtPress", BindingFlags.NonPublic | BindingFlags.Instance);

            if (movingField == null || lastErrorField == null || wSinceField == null || distField == null)
            {
                Assert("Follow [Anti-stuck clears forward state]", false, "reflection lookup failed");
                return;
            }

            var moving = (bool[])movingField.GetValue(controller)!;
            var lastError = (float[])lastErrorField.GetValue(controller)!;
            var wSince = (DateTime[])wSinceField.GetValue(controller)!;
            var dist = (float[])distField.GetValue(controller)!;

            moving[1] = true;
            lastError[1] = 0.75f;
            wSince[1] = DateTime.Now.AddSeconds(-6);
            dist[1] = 10f;

            var leader = BuildState();
            leader.CoordZ = 10f;
            var follower = BuildState();
            follower.IsHeadingLocked = true;
            follower.EstimatedHeading = 0f;

            controller.Update(1, follower, leader, IntPtr.Zero);

            bool ok = !moving[1]
                   && Math.Abs(lastError[1]) < 0.0001f
                   && wSince[1] == DateTime.MinValue
                   && Math.Abs(dist[1] - float.MaxValue) < 0.0001f;

            Assert("Follow [Anti-stuck clears forward state]", ok,
                $"moving={moving[1]} err={lastError[1]} since={wSince[1]:O} dist={dist[1]}");
        }

        private static void TestAntiStuckDoesNotJump()
        {
            var input = new RecordingInputEngine();
            var controller = new FollowController(input, new NavigationKernel(), new BridgeSettings());
            var type = typeof(FollowController);

            var movingField = type.GetField("_isMovingForward", BindingFlags.NonPublic | BindingFlags.Instance);
            var wSinceField = type.GetField("_wForwardSince", BindingFlags.NonPublic | BindingFlags.Instance);
            var distField = type.GetField("_distanceAtPress", BindingFlags.NonPublic | BindingFlags.Instance);

            if (movingField == null || wSinceField == null || distField == null)
            {
                Assert("Follow [Anti-stuck does not jump]", false, "reflection lookup failed");
                return;
            }

            var moving = (bool[])movingField.GetValue(controller)!;
            var wSince = (DateTime[])wSinceField.GetValue(controller)!;
            var dist = (float[])distField.GetValue(controller)!;

            moving[1] = true;
            wSince[1] = DateTime.Now.AddSeconds(-6);
            dist[1] = 10f;

            var leader = BuildState();
            leader.CoordZ = 10f;
            var follower = BuildState();
            follower.IsHeadingLocked = true;
            follower.IsMoving = true;
            follower.EstimatedHeading = 0f;

            controller.Update(1, follower, leader, IntPtr.Zero);

            bool ok = input.TappedScanCodes.Count == 0;
            Assert("Follow [Anti-stuck does not jump]", ok,
                $"taps=[{string.Join(",", input.TappedScanCodes)}]");
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

            bool ok = input.TappedScanCodes.Count == 1
                && input.TappedScanCodes[0] == settings.KeyMount;
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

            bool ok = input.TappedScanCodes.Count == 1
                && input.TappedScanCodes[0] == settings.KeyInteract;
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
            third.RawFacing = 0f;
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
            SeedBasis(controller, 1, new Vector2(0f, 1f), new Vector2(-1f, 0f));

            var leader = BuildState();
            leader.ZoneHash = 10;
            leader.CoordZ = 20f;

            var follower = BuildState();
            follower.ZoneHash = 20;

            controller.Update(1, follower, leader, IntPtr.Zero);

            (bool hasForward, bool hasLeft) = ReadBasisFlags(controller, 1);
            bool ok = !hasForward
                && !hasLeft
                && input.UpScanCodes.Contains(settings.KeyForward)
                && input.UpScanCodes.Contains(settings.KeyLeft)
                && input.UpScanCodes.Contains(settings.KeyBack)
                && input.UpScanCodes.Contains(settings.KeyRight);

            Assert("Follow [Zone mismatch stops follow]", ok,
                $"hasForward={hasForward} hasLeft={hasLeft} ups=[{string.Join(",", input.UpScanCodes)}]");
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

        private static void TestFollowBootstrapsForwardProbe()
        {
            var input = new RecordingInputEngine();
            var settings = new BridgeSettings();
            var controller = new FollowController(input, new NavigationKernel(), settings);

            var leader = BuildState();
            leader.CoordZ = 10f;

            var follower = BuildState();

            controller.Update(1, follower, leader, IntPtr.Zero);

            bool ok = input.TappedScanCodes.Count == 1
                && input.TappedScanCodes[0] == settings.KeyForward
                && !input.TappedScanCodes.Contains(settings.KeyTurnLeft)
                && !input.TappedScanCodes.Contains(settings.KeyTurnRight);

            Assert("Follow [Bootstrap uses forward probe]", ok,
                $"taps=[{string.Join(",", input.TappedScanCodes)}]");
        }

        private static void TestFollowProjectsIntoStrafe()
        {
            var input = new RecordingInputEngine();
            var settings = new BridgeSettings();
            var controller = new FollowController(input, new NavigationKernel(), settings);
            SeedBasis(controller, 1, new Vector2(0f, 1f), new Vector2(-1f, 0f));

            var leader = BuildState();
            leader.CoordX = -5f;
            leader.CoordZ = 0f;

            var follower = BuildState();

            controller.Update(1, follower, leader, IntPtr.Zero);

            bool ok = input.TappedScanCodes.Count == 1 && input.TappedScanCodes[0] == settings.KeyLeft;
            Assert("Follow [Projected error strafes]", ok,
                $"taps=[{string.Join(",", input.TappedScanCodes)}]");
        }

        private static void TestFollowBackpedalsWhenGoalIsBehind()
        {
            var input = new RecordingInputEngine();
            var settings = new BridgeSettings();
            var controller = new FollowController(input, new NavigationKernel(), settings);
            SeedBasis(controller, 1, new Vector2(0f, 1f), new Vector2(-1f, 0f));

            var leader = BuildState();
            leader.CoordX = 0f;
            leader.CoordZ = -5f;

            var follower = BuildState();

            controller.Update(1, follower, leader, IntPtr.Zero);

            bool ok = input.TappedScanCodes.Count == 1 && input.TappedScanCodes[0] == settings.KeyBack;
            Assert("Follow [Projected error backpedals]", ok,
                $"taps=[{string.Join(",", input.TappedScanCodes)}]");
        }

        private static void TestFollowUsesBreadcrumbTrailInsteadOfBodyChase()
        {
            var input = new RecordingInputEngine();
            var nav = new NavigationKernel();
            var settings = new BridgeSettings();
            var controller = new FollowController(input, nav, settings);
            SeedBasis(controller, 1, new Vector2(0f, 1f), new Vector2(-1f, 0f));

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
            follower.CoordX = 0f;
            follower.CoordZ = 2f;

            controller.Update(1, follower, leader2, IntPtr.Zero);

            bool ok = input.TappedScanCodes.Count == 1 && input.TappedScanCodes[0] == settings.KeyForward;
            Assert("Follow [Breadcrumb trail beats body chase]", ok,
                $"taps=[{string.Join(",", input.TappedScanCodes)}]");
        }

        private static void TestEmergencyStopResetsBasisState()
        {
            var controller = new FollowController(new RecordingInputEngine(), new NavigationKernel(), new BridgeSettings());
            SeedBasis(controller, 1, new Vector2(0f, 1f), new Vector2(-1f, 0f));

            controller.EmergencyStop(1, IntPtr.Zero);

            (bool hasForward, bool hasLeft) = ReadBasisFlags(controller, 1);
            bool ok = !hasForward && !hasLeft;
            Assert("Follow [EmergencyStop resets basis]", ok,
                $"hasForward={hasForward} hasLeft={hasLeft}");
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

            bool ok = filtered.Count == 2
                && filtered[0].ProcessId == 303
                && filtered[1].ProcessId == 101;

            Assert("Window [PID list preserves order]", ok,
                $"got [{string.Join(",", filtered.Select(window => window.ProcessId))}]");
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

            bool ok = filtered.Count == 2
                && filtered[0].Hwnd == (IntPtr)0x1002
                && filtered[1].Hwnd == (IntPtr)0x1001;

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

        private static void SeedBasis(FollowController controller, int slot, Vector2 forward, Vector2 left)
        {
            var basisField = typeof(FollowController).GetField("_basisStates", BindingFlags.NonPublic | BindingFlags.Instance);
            if (basisField == null)
            {
                throw new InvalidOperationException("Could not find _basisStates field.");
            }

            var basisStates = (Array)basisField.GetValue(controller)!;
            object basis = basisStates.GetValue(slot)!;
            Type basisType = basis.GetType();

            basisType.GetField("Forward", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(basis, forward);
            basisType.GetField("Left", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(basis, left);
            basisType.GetField("HasForward", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(basis, true);
            basisType.GetField("HasLeft", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(basis, true);
        }

        private static (bool HasForward, bool HasLeft) ReadBasisFlags(FollowController controller, int slot)
        {
            var basisField = typeof(FollowController).GetField("_basisStates", BindingFlags.NonPublic | BindingFlags.Instance);
            if (basisField == null)
            {
                throw new InvalidOperationException("Could not find _basisStates field.");
            }

            var basisStates = (Array)basisField.GetValue(controller)!;
            object basis = basisStates.GetValue(slot)!;
            Type basisType = basis.GetType();

            bool hasForward = (bool)basisType.GetField("HasForward", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(basis)!;
            bool hasLeft = (bool)basisType.GetField("HasLeft", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(basis)!;
            return (hasForward, hasLeft);
        }

        private static GameState BuildState() => new GameState
        {
            IsValid = true, IsAlive = true, PlayerHP = 255, TargetHP = 0,
            CoordX = 0, CoordY = 0, CoordZ = 0, RawFacing = 0, ZoneHash = 0
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
