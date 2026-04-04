using System;
using System.Drawing;
using System.Drawing.Imaging;
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
            Console.Clear();
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

            // ── Zone hash passthrough ────────────────────────────────────
            TestZoneHash("Zero hash",   0);
            TestZoneHash("Max hash",    255);
            TestZoneHash("Mid hash",    128);

            // ── Flag bitfield ────────────────────────────────────────────
            TestFlags("All clear",     false, false, false, false, false);
            TestFlags("Combat only",   true,  false, false, true,  false);
            TestFlags("All set",       true,  true,  true,  true,  true);
            TestFlags("Mounted+Alive", false, false, false, true,  true);

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
            state.RawFacing = rad % (float)(2 * Math.PI);  // clamp to 0-2π
            var decoded = svc.Decode(sim.Generate(state));
            float expected = state.RawFacing;
            float actual   = decoded.RawFacing;
            Assert($"Heading[{name}] ({rad:F4})", Math.Abs(actual - expected) < 0.0002f, $"got {actual:F5}");
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
