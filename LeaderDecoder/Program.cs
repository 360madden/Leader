using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Drawing;
using System.Linq;
using System.Threading;
using LeaderDecoder.Models;
using LeaderDecoder.Services;

namespace LeaderDecoder
{
    /// <summary>
    /// LEADER TELEMETRY ORCHESTRATOR v1.0
    /// High-performance multi-window coordinator for optical telemetry.
    /// Manages Capture, Decode, and Navigation loops for up to 5 game instances.
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            if (!TryParseOptions(args, out var options, out string? parseError))
            {
                Console.Error.WriteLine(parseError);
                Console.Error.WriteLine();
                PrintUsage();
                Environment.ExitCode = 1;
                return;
            }

            // --test mode: run encode/decode validation then exit
            if (options.TestMode)
            {
                RoundtripTests.Run();
                Console.WriteLine("\nPress any key to exit...");
                Console.ReadKey(true);
                return;
            }

            if (options.ListOnly)
            {
                PrintWindowList(RiftWindowService.FilterWindows(RiftWindowService.FindRiftWindows(), BuildFilter(options)));
                return;
            }

            Console.Clear();
            Console.Title = "🛰️ Leader: Multi-Agent Bridge v1.2";

            // 🧱 Bootstrapping (Hardening Agent)
            var config = new ConfigManager();
            var settings = config.Settings;

            Console.WriteLine("================================================================================");
            Console.WriteLine("🛰️  LEADER: OPTICAL TELEMETRY BRIDGE (v1.1)");
            Console.WriteLine($"   Mode: {(options.SimMode ? "SIMULATION" : "LIVE SCAN")}");
            Console.WriteLine("   Keys: [T]=Toggle Follow  [ScrollLock]=Global Toggle  [L]=Log  [S]=Snap  [R]=Reload Config");
            Console.WriteLine("================================================================================");

            // Initialize Modular Services
            var capture = new CaptureEngine();
            var telemetry = new TelemetryService();
            var nav = new NavigationKernel();
            var input = new InputEngine();
            var follow = new FollowController(input, nav, settings);
            var diag = new DiagnosticService();
            var memory = new MemoryEngine();

            bool isSimMode = options.SimMode;
            bool isFollowEnabled = false;
            bool isLoggingEnabled = false;

            // Global hotkey — ScrollLock toggles follow even inside RIFT
            using var hotkey = new GlobalHotkeyService();
            hotkey.OnToggleFollow = () =>
            {
                isFollowEnabled = !isFollowEnabled;
                Console.Beep(isFollowEnabled ? 880 : 440, 120);
            };
            hotkey.Start();

            var gameStates = new GameState[5];
            int[] processIds = new int[5];
            IntPtr[] memoryHandles = new IntPtr[5];
            for (int i = 0; i < 5; i++) gameStates[i] = new GameState();

            // Mock Leader for SIM mode
            var mockLeader = new GameState { IsValid = true, PlayerHP = 255, CoordX = 1000, CoordY = 20, CoordZ = 1000, RawFacing = 0, IsAlive = true };

            // Main Telemetry Loop (30Hz Target)
            while (true)
            {
                var cycleSw = Stopwatch.StartNew();
                var windows = RiftWindowService.FilterWindows(RiftWindowService.FindRiftWindows(), BuildFilter(options));

                // 0. Standby Logic
                if (windows.Count == 0 && !isSimMode)
                {
                    Console.SetCursorPosition(0, 5);
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[STANDBY] Waiting for matching RIFT game windows... (use --list / --pid / --pids / --hwnd / --hwnds / --title-contains)      ");
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Thread.Sleep(1000); // Low-power polling
                    continue;
                }

                // 1. Global Input Handling
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).Key;
                    if (key == ConsoleKey.T)
                    {
                        isFollowEnabled = !isFollowEnabled;
                        Console.Beep(isFollowEnabled ? 800 : 400, 150);
                    }
                    if (key == ConsoleKey.L)
                    {
                        isLoggingEnabled = !isLoggingEnabled;
                        Console.Beep(1000, 100);
                    }
                    if (key == ConsoleKey.S)
                    {
                        if (!isSimMode && windows.Count > 0)
                        {
                            using (var bmp = capture.CaptureRegion(windows[0].Hwnd, settings.CaptureWidth, settings.CaptureHeight))
                                diag.SaveSnapshot(bmp, "manual_snap");
                            Console.Beep(1200, 50);
                        }
                    }
                    if (key == ConsoleKey.R)
                    {
                        config.Reload();
                        settings = config.Settings;
                        follow.ApplySettings(settings);
                        Console.Beep(600, 80);
                        Console.Beep(900, 80);
                    }
                }

                // UI Cleanup (Dashboard Mode)
                Console.SetCursorPosition(0, 5);
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"[BRIDGE_STATUS] Detected: {windows.Count}/5 | Latency: {cycleSw.ElapsedMilliseconds,2}ms | ");

                Console.ForegroundColor = isFollowEnabled ? ConsoleColor.Green : ConsoleColor.Yellow;
                Console.Write($"PURSUIT: {(isFollowEnabled ? "ACTIVE " : "DISABLED")} ");

                Console.ForegroundColor = isLoggingEnabled ? ConsoleColor.Magenta : ConsoleColor.DarkGray;
                Console.WriteLine($"| LOG: {(isLoggingEnabled ? "ON " : "OFF")}      ");

                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine(new string('-', 95));

                // 2. Telemetry Processing & Decision Engine
                for (int i = 0; i < 5; i++)
                {
                    GameState? state = null;

                    if (isSimMode && i == 0) // Leader simulation
                    {
                        mockLeader.CoordX += (float)Math.Cos(DateTime.Now.Ticks / 10000000.0) * 0.5f;
                        mockLeader.CoordZ += (float)Math.Sin(DateTime.Now.Ticks / 10000000.0) * 0.5f;
                        state = mockLeader;
                    }
                    else if (i < windows.Count)
                    {
                        var window = windows[i];
                        var hwnd = window.Hwnd;
                        var pid = window.ProcessId;
                        var baseAddr = window.BaseAddress;

                        // Bind Memory Reader if the process changed
                        if (processIds[i] != pid)
                        {
                            memory.Detach(memoryHandles[i]);
                            memoryHandles[i] = memory.Attach(pid);
                            processIds[i] = pid;
                        }

                        using (var bmp = capture.CaptureRegion(hwnd, settings.CaptureWidth, settings.CaptureHeight))
                        {
                            state = telemetry.Decode(bmp);

                            // 🧠 Hybrid Memory Sweep
                            if (baseAddr > 0 && settings.MemoryOffsets.TryGetValue("TargetHP", out int[]? hpOffset))
                            {
                                long targetHpPtr = memory.ReadMultiLevelPointer(memoryHandles[i], baseAddr, hpOffset);
                                if (targetHpPtr > 0)
                                {
                                    state.TargetHP = memory.ReadInt32(memoryHandles[i], targetHpPtr);
                                }
                            }

                            // Auto-Snapshot on Sync Failure
                            if (!state.IsValid && i == 0) diag.SaveSnapshot(bmp, "sync_failure");
                        }
                    }

                    if (state != null && state.IsValid)
                    {
                        bool zoneChanged = nav.UpdateHeading(i, state);
                        gameStates[i] = state;

                        // Logging logic
                        if (isLoggingEnabled) diag.LogTelemetry(i, state);

                        // Follow logic for followers (Slots 2-5)
                        if (i > 0 && i < windows.Count && gameStates[0].IsValid && isFollowEnabled)
                        {
                            if (zoneChanged)
                            {
                                // Zone transition — stop all keys; resume next frame
                                follow.EmergencyStop(i, windows[i].Hwnd);
                            }
                            else
                            {
                                follow.Update(i, state, gameStates[0], windows[i].Hwnd);
                            }
                        }
                        else if (i > 0 && i < windows.Count && !isFollowEnabled)
                        {
                            follow.EmergencyStop(i, windows[i].Hwnd);
                        }

                        // UI Dashboard
                        string zoneFlag = zoneChanged ? " [ZONE!]" : "       ";
                        string headingText = state.IsHeadingLocked ? $"{state.EstimatedHeading,5:F2}" : " --- ";
                        string status = $"HP:{state.PlayerHP,3:D3} THP:{state.TargetHP,5:D5} | X:{state.CoordX,7:F1} Y:{state.CoordY,6:F1} Z:{state.CoordZ,7:F1} | F:{state.RawFacing,5:F2}({headingText})";
                        string flagStr = $"{(state.IsCombat ? "[CB]" : "    ")} {(state.IsMounted ? "[MT]" : "    ")} {(state.IsAlive ? "    " : "[DEAD]")}{zoneFlag}";
                        Console.ForegroundColor = zoneChanged ? ConsoleColor.Yellow : ConsoleColor.Gray;
                        string identity = (i < windows.Count)
                            ? RiftWindowService.FormatCompactIdentity(windows[i])
                            : "SIM_LEAD";
                        Console.WriteLine($" SLOT[{i + 1}] {identity,-26} | {status} | {flagStr}");
                        Console.ForegroundColor = ConsoleColor.Gray;
                    }
                    else
                    {
                        string identity = i < windows.Count
                            ? RiftWindowService.FormatCompactIdentity(windows[i])
                            : "UNASSIGNED";
                        Console.WriteLine($" SLOT[{i + 1}] {identity,-26} | -- STANDBY --                                                              ");
                    }
                }

                // 3. Maintain stable frame timing
                while (cycleSw.ElapsedMilliseconds < (1000 / settings.TargetFPS)) Thread.Sleep(1);
            }
        }

        private static RiftWindowFilter? BuildFilter(BridgeOptions options)
        {
            if (!options.HasFilter)
            {
                return null;
            }

            return new RiftWindowFilter
            {
                ProcessId = options.ProcessId,
                ProcessIds = options.ProcessIds,
                Hwnd = options.Hwnd,
                Hwnds = options.Hwnds,
                TitleContains = options.TitleContains
            };
        }

        private static void PrintWindowList(List<RiftWindowInfo> windows)
        {
            Console.WriteLine("Detected RIFT windows:");
            if (windows.Count == 0)
            {
                Console.WriteLine("  (none)");
                return;
            }

            for (int i = 0; i < windows.Count; i++)
            {
                var window = windows[i];
                var snapshot = RiftWindowService.GetWindowSnapshot(window.Hwnd);
                Console.WriteLine(
                    $"[{i + 1}] {RiftWindowService.FormatIdentity(window)} | Client={snapshot.ClientWidth ?? 0}x{snapshot.ClientHeight ?? 0}");
                Console.WriteLine($"    Selectors: {RiftWindowService.FormatSelectorHints(window)}");
            }
        }

        private static bool TryParseOptions(string[] args, out BridgeOptions options, out string? error)
        {
            options = new BridgeOptions();
            error = null;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                switch (arg.ToLowerInvariant())
                {
                    case "--help":
                    case "-h":
                    case "/?":
                        options.Help = true;
                        break;

                    case "--test":
                        options.TestMode = true;
                        break;

                    case "--sim":
                        options.SimMode = true;
                        break;

                    case "--list":
                        options.ListOnly = true;
                        break;

                    case "--pid":
                        if (!TryReadInt(args, ref i, out int pid, out error))
                        {
                            return false;
                        }
                        options.ProcessId = pid;
                        break;

                    case "--pids":
                        if (!TryReadString(args, ref i, out string? pidListText, out error))
                        {
                            return false;
                        }
                        if (!RiftWindowService.TryParseProcessIdList(pidListText, out int[] processIds))
                        {
                            error = $"Could not parse PID list '{pidListText}'. Expected comma-separated integers.";
                            return false;
                        }
                        options.ProcessIds = processIds;
                        break;

                    case "--hwnd":
                        if (!TryReadString(args, ref i, out string? hwndText, out error))
                        {
                            return false;
                        }
                        if (!RiftWindowService.TryParseHwnd(hwndText, out IntPtr hwnd))
                        {
                            error = $"Could not parse HWND value '{hwndText}'.";
                            return false;
                        }
                        options.Hwnd = hwnd;
                        break;

                    case "--hwnds":
                        if (!TryReadString(args, ref i, out string? hwndListText, out error))
                        {
                            return false;
                        }
                        if (!RiftWindowService.TryParseHwndList(hwndListText, out IntPtr[] hwnds))
                        {
                            error = $"Could not parse HWND list '{hwndListText}'. Expected comma-separated values such as 0x351350,0x123456.";
                            return false;
                        }
                        options.Hwnds = hwnds;
                        break;

                    case "--title-contains":
                        if (!TryReadString(args, ref i, out string? titleContains, out error))
                        {
                            return false;
                        }
                        options.TitleContains = titleContains;
                        break;

                    default:
                        error = $"Unknown option '{arg}'.";
                        return false;
                }
            }

            if (options.ProcessId.HasValue && options.ProcessIds is { Length: > 0 })
            {
                error = "--pid and --pids cannot be combined.";
                return false;
            }

            if (options.Hwnd.HasValue && options.Hwnds is { Length: > 0 })
            {
                error = "--hwnd and --hwnds cannot be combined.";
                return false;
            }

            if ((options.ProcessId.HasValue || options.ProcessIds is { Length: > 0 })
                && (options.Hwnd.HasValue || options.Hwnds is { Length: > 0 }))
            {
                error = "PID-based filters and HWND-based filters cannot be combined.";
                return false;
            }

            return true;
        }

        private static bool TryReadInt(string[] args, ref int index, out int value, out string? error)
        {
            value = 0;
            error = null;
            if (!TryReadString(args, ref index, out string? raw, out error))
            {
                return false;
            }

            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                error = $"Expected an integer value after '{args[index - 1]}'.";
                return false;
            }

            return true;
        }

        private static bool TryReadString(string[] args, ref int index, out string? value, out string? error)
        {
            value = null;
            error = null;

            if (index + 1 >= args.Length)
            {
                error = $"Missing value after '{args[index]}'.";
                return false;
            }

            value = args[++index];
            return true;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  LeaderDecoder.exe --list [--pid N | --pids N1,N2 | --hwnd 0xHEX | --hwnds 0xA,0xB | --title-contains TEXT]");
            Console.WriteLine("  LeaderDecoder.exe --sim");
            Console.WriteLine("  LeaderDecoder.exe --test");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --list                 List detected/filtered RIFT windows and exit");
            Console.WriteLine("  --pid N                Target a specific process id");
            Console.WriteLine("  --pids N1,N2           Target multiple process ids in the given order");
            Console.WriteLine("  --hwnd HEX             Target a specific HWND, e.g. 0x351350");
            Console.WriteLine("  --hwnds A,B            Target multiple HWNDs in the given order");
            Console.WriteLine("  --title-contains TEXT  Filter windows by title substring");
            Console.WriteLine("  --sim                  Run simulation mode");
            Console.WriteLine("  --test                 Run encode/decode roundtrip tests");
            Console.WriteLine("  --help                 Show this help");
        }

        private sealed class BridgeOptions
        {
            public bool Help { get; set; }
            public bool TestMode { get; set; }
            public bool SimMode { get; set; }
            public bool ListOnly { get; set; }
            public int? ProcessId { get; set; }
            public int[]? ProcessIds { get; set; }
            public IntPtr? Hwnd { get; set; }
            public IntPtr[]? Hwnds { get; set; }
            public string? TitleContains { get; set; }
            public bool HasFilter =>
                ProcessId.HasValue ||
                ProcessIds is { Length: > 0 } ||
                Hwnd.HasValue ||
                Hwnds is { Length: > 0 } ||
                !string.IsNullOrWhiteSpace(TitleContains);
        }
    }
}
