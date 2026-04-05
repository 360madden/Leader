using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
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

            // ── Flag bitfield ────────────────────────────────────────────
            TestFlags("All clear",     false, false, false, false, false);
            TestFlags("Combat only",   true,  false, false, true,  false);
            TestFlags("All set",       true,  true,  true,  true,  true);
            TestFlags("Mounted+Alive", false, false, false, true,  true);
            TestEmergencyStopResetsFollowState();
            TestHeadingBootstrapStartsForwardMotion();
            TestStationaryFollowerBypassesAngleGate();
            TestFollowReleasesWithinBand();
            TestAntiStuckClearsForwardState();
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

            if (movingField == null || lastErrorField == null || wSinceField == null || distField == null)
            {
                Assert("Follow [EmergencyStop resets slot state]", false, "reflection lookup failed");
                return;
            }

            var moving = (bool[])movingField.GetValue(controller)!;
            var lastError = (float[])lastErrorField.GetValue(controller)!;
            var wSince = (DateTime[])wSinceField.GetValue(controller)!;
            var dist = (float[])distField.GetValue(controller)!;

            moving[1] = true;
            lastError[1] = 1.25f;
            wSince[1] = DateTime.Now;
            dist[1] = 9.5f;

            controller.EmergencyStop(1, IntPtr.Zero);

            bool ok = !moving[1]
                   && Math.Abs(lastError[1]) < 0.0001f
                   && wSince[1] == DateTime.MinValue
                   && Math.Abs(dist[1] - float.MaxValue) < 0.0001f;

            Assert("Follow [EmergencyStop resets slot state]", ok,
                $"moving={moving[1]} err={lastError[1]} since={wSince[1]:O} dist={dist[1]}");
        }

        private static void TestHeadingBootstrapStartsForwardMotion()
        {
            var controller = new FollowController(new InputEngine(), new NavigationKernel(), new BridgeSettings());
            var movingField = typeof(FollowController).GetField("_isMovingForward", BindingFlags.NonPublic | BindingFlags.Instance);

            if (movingField == null)
            {
                Assert("Follow [Unknown heading bootstraps W]", false, "reflection lookup failed");
                return;
            }

            var leader = BuildState();
            leader.CoordX = 10f;
            leader.CoordZ = 10f;

            var follower = BuildState();
            follower.CoordX = 0f;
            follower.CoordZ = 0f;
            follower.IsHeadingLocked = false;
            follower.EstimatedHeading = 0f;

            controller.Update(1, follower, leader, IntPtr.Zero);

            var moving = (bool[])movingField.GetValue(controller)!;
            Assert("Follow [Unknown heading bootstraps W]", moving[1], $"moving={moving[1]}");
        }

        private static void TestStationaryFollowerBypassesAngleGate()
        {
            var controller = new FollowController(new InputEngine(), new NavigationKernel(), new BridgeSettings());
            var movingField = typeof(FollowController).GetField("_isMovingForward", BindingFlags.NonPublic | BindingFlags.Instance);

            if (movingField == null)
            {
                Assert("Follow [Stationary follower bypasses angle gate]", false, "reflection lookup failed");
                return;
            }

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

            var moving = (bool[])movingField.GetValue(controller)!;
            Assert("Follow [Stationary follower bypasses angle gate]", moving[1], $"moving={moving[1]}");
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

        private static GameState BuildState() => new GameState
        {
            IsValid = true, IsAlive = true, PlayerHP = 255, TargetHP = 0,
            CoordX = 0, CoordY = 0, CoordZ = 0, RawFacing = 0, ZoneHash = 0
        };

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
