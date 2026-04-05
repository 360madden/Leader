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
                if (!Console.IsInputRedirected && !Console.IsOutputRedirected)
                {
                    Console.WriteLine("\nPress any key to exit...");
                    Console.ReadKey(true);
                }
                return;
            }

            if (options.ListOnly)
            {
                PrintWindowList(RiftWindowService.FilterWindows(RiftWindowService.FindRiftWindows(), BuildFilter(options)));
                return;
            }

            if (options.Help)
            {
                PrintUsage();
                return;
            }

            TryPrepareConsole();
            var filter = BuildFilter(options);

            // 🧱 Bootstrapping (Hardening Agent)
            var config = new ConfigManager();
            var settings = config.Settings;

            Console.WriteLine("================================================================================");
            Console.WriteLine("🛰️  LEADER: OPTICAL TELEMETRY BRIDGE (v1.1)");
            Console.WriteLine($"   Mode: {(options.SimMode ? "SIMULATION" : "LIVE SCAN")}");
            Console.WriteLine("   Keys: [T]=Toggle Follow  [ScrollLock]=Global Toggle  [L]=Log  [S]=Snap  [R]=Reload Config");
            Console.WriteLine("================================================================================");

            // Initialize Modular Services
            var diag = new DiagnosticService();
            var capture = new CaptureEngine(diag);
            var telemetry = new TelemetryService();
            var nav = new NavigationKernel();
            var input = new InputEngine(diag);
            var follow = new FollowController(input, nav, settings, diag);
            var memory = new MemoryEngine(diag);

            bool isSimMode = options.SimMode;
            bool isFollowEnabled = false;
            bool isLoggingEnabled = false;
            bool toggleFollowRequested = false;
            List<RiftLockedRoleAssignment>? lockedRoles = null;

            // Global hotkey — ScrollLock toggles follow even inside RIFT
            using var hotkey = new GlobalHotkeyService(diag);
            hotkey.OnToggleFollow = () =>
            {
                toggleFollowRequested = true;
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
                var allWindows = RiftWindowService.FindRiftWindows();
                var activeFilter = ResolveActiveFilter(filter, lockedRoles);
                var slots = RiftWindowService.BuildWindowSlots(allWindows, activeFilter);
                int detectedCount = slots.Count(slot => slot.Window is not null);
                int displayTargetCount = GetDisplayTargetCount(options, lockedRoles);

                // 1. Global Input Handling
                if (TryReadConsoleKey(out ConsoleKey? key))
                {
                    if (key == ConsoleKey.T)
                    {
                        toggleFollowRequested = true;
                    }
                    if (key == ConsoleKey.L)
                    {
                        isLoggingEnabled = !isLoggingEnabled;
                        Console.Beep(1000, 100);
                    }
                    if (key == ConsoleKey.S)
                    {
                        var primaryWindow = slots.FirstOrDefault(slot => slot.Window is not null)?.Window;
                        if (!isSimMode && primaryWindow is not null)
                        {
                            using (var bmp = capture.CaptureRegion(primaryWindow.Hwnd, settings.CaptureWidth, settings.CaptureHeight))
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

                if (toggleFollowRequested)
                {
                    bool toggled = ToggleFollowState(ref isFollowEnabled, ref lockedRoles, slots, gameStates, filter);
                    Console.Beep(toggled && isFollowEnabled ? 880 : 440, toggled ? 120 : 220);
                    toggleFollowRequested = false;

                    activeFilter = ResolveActiveFilter(filter, lockedRoles);
                    slots = RiftWindowService.BuildWindowSlots(allWindows, activeFilter);
                    detectedCount = slots.Count(slot => slot.Window is not null);
                    displayTargetCount = GetDisplayTargetCount(options, lockedRoles);
                }

                // 0. Standby Logic
                if (detectedCount == 0 && !isSimMode)
                {
                    Console.SetCursorPosition(0, 5);
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[STANDBY] Waiting for matching RIFT game windows... (use --list / --pid / --pids / --hwnd / --hwnds / --title-contains)      ");
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Thread.Sleep(1000); // Low-power polling
                    continue;
                }

                // UI Cleanup (Dashboard Mode)
                Console.SetCursorPosition(0, 5);
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"[BRIDGE_STATUS] Detected: {detectedCount}/{displayTargetCount} | Latency: {cycleSw.ElapsedMilliseconds,2}ms | ");

                Console.ForegroundColor = isFollowEnabled ? ConsoleColor.Green : ConsoleColor.Yellow;
                Console.Write($"PURSUIT: {(isFollowEnabled ? "ACTIVE " : "DISABLED")} ");

                Console.ForegroundColor = lockedRoles is { Count: > 0 } ? ConsoleColor.Green : ConsoleColor.DarkGray;
                Console.Write($"| ROLES: {(lockedRoles is { Count: > 0 } ? "LOCKED" : "DYNAMIC")} ");

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
                    else if (i < slots.Count && slots[i].Window is not null)
                    {
                        var window = slots[i].Window!;
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
                        bool roleMatched = lockedRoles is null
                            || (i < lockedRoles.Count && RiftWindowService.MatchesLockedRole(slots.ElementAtOrDefault(i), state, lockedRoles[i]));

                        if (!roleMatched)
                        {
                            gameStates[i] = new GameState();
                            if (i > 0 && i < slots.Count && slots[i].Window is not null)
                            {
                                follow.EmergencyStop(i, slots[i].Window!.Hwnd);
                                LogControllerIdle(
                                    diag,
                                    isLoggingEnabled && isFollowEnabled,
                                    i,
                                    "role_mismatch",
                                    roleMatched: false,
                                    leaderDistance: gameStates[0].IsValid ? nav.CalculateDistance(state, gameStates[0]) : null,
                                    withinLeaderBand: gameStates[0].IsValid ? nav.CalculateDistance(state, gameStates[0]) <= settings.FollowDistanceMax : null);
                            }

                            string mismatchIdentity = (i < slots.Count && slots[i].Window is not null)
                                ? $"{state.PlayerTag}@{RiftWindowService.FormatCompactIdentity(slots[i].Window!)}"
                                : RiftWindowService.FormatExpectedIdentity(i < slots.Count ? slots[i] : null);
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($" SLOT[{i + 1}] {mismatchIdentity,-31} | -- ROLE MISMATCH --                                                  ");
                            Console.ForegroundColor = ConsoleColor.Gray;
                            continue;
                        }

                        bool zoneChanged = nav.UpdateHeading(i, state);
                        gameStates[i] = state;

                        // Logging logic
                        if (isLoggingEnabled) diag.LogTelemetry(i, state);

                        // Follow logic for followers (Slots 2-5)
                        if (i > 0 && i < slots.Count && slots[i].Window is not null && gameStates[0].IsValid && isFollowEnabled)
                        {
                            if (zoneChanged)
                            {
                                // Zone transition — stop all keys; resume next frame
                                follow.EmergencyStop(i, slots[i].Window!.Hwnd);
                                LogControllerIdle(
                                    diag,
                                    isLoggingEnabled,
                                    i,
                                    "zone_transition_reset",
                                    roleMatched: true,
                                    leaderDistance: nav.CalculateDistance(state, gameStates[0]),
                                    withinLeaderBand: nav.CalculateDistance(state, gameStates[0]) <= settings.FollowDistanceMax);
                            }
                            else
                            {
                                follow.Update(i, state, gameStates[0], slots[i].Window!.Hwnd, isLoggingEnabled);
                            }
                        }
                        else if (i > 0 && i < slots.Count && slots[i].Window is not null && (!gameStates[0].IsValid || !isFollowEnabled))
                        {
                            follow.EmergencyStop(i, slots[i].Window!.Hwnd);
                            if (isFollowEnabled && isLoggingEnabled && !gameStates[0].IsValid)
                            {
                                LogControllerIdle(
                                    diag,
                                    true,
                                    i,
                                    "leader_invalid",
                                    roleMatched: true);
                            }
                        }

                        // UI Dashboard
                        string zoneFlag = zoneChanged ? " [ZONE!]" : "       ";
                        string headingText = state.IsHeadingLocked ? $"{state.EstimatedHeading,5:F2}" : " --- ";
                        string status = $"HP:{state.PlayerHP,3:D3} THP:{state.TargetHP,5:D5} | X:{state.CoordX,7:F1} Y:{state.CoordY,6:F1} Z:{state.CoordZ,7:F1} | MH:{state.RawFacing,5:F2}({headingText})";
                        string flagStr = $"{(state.IsCombat ? "[CB]" : "    ")} {(state.IsMounted ? "[MT]" : "    ")} {(state.IsAlive ? "    " : "[DEAD]")}{zoneFlag}";
                        Console.ForegroundColor = zoneChanged ? ConsoleColor.Yellow : ConsoleColor.Gray;
                        string identity = (i < slots.Count && slots[i].Window is not null)
                            ? $"{state.PlayerTag}@{RiftWindowService.FormatCompactIdentity(slots[i].Window!)}"
                            : "SIM_LEAD";
                        Console.WriteLine($" SLOT[{i + 1}] {identity,-31} | {status} | {flagStr}");
                        Console.ForegroundColor = ConsoleColor.Gray;
                    }
                    else
                    {
                        if (processIds[i] != 0 || memoryHandles[i] != IntPtr.Zero)
                        {
                            memory.Detach(memoryHandles[i]);
                            memoryHandles[i] = IntPtr.Zero;
                            processIds[i] = 0;
                        }

                        gameStates[i] = new GameState();
                        if (i > 0 && i < slots.Count && slots[i].Window is not null)
                        {
                            follow.EmergencyStop(i, slots[i].Window!.Hwnd);
                            if (isFollowEnabled && isLoggingEnabled)
                            {
                                LogControllerIdle(
                                    diag,
                                    true,
                                    i,
                                    "telemetry_invalid",
                                    roleMatched: true);
                            }
                        }

                        string identity = RiftWindowService.FormatExpectedIdentity(i < slots.Count ? slots[i] : null);
                        Console.WriteLine($" SLOT[{i + 1}] {identity,-31} | -- STANDBY --                                                         ");
                    }
                }

                // 3. Maintain stable frame timing
                while (cycleSw.ElapsedMilliseconds < (1000 / settings.TargetFPS)) Thread.Sleep(1);
            }
        }

        private static RiftWindowFilter? ResolveActiveFilter(RiftWindowFilter? filter, List<RiftLockedRoleAssignment>? lockedRoles)
        {
            if (lockedRoles is { Count: > 0 })
            {
                return new RiftWindowFilter
                {
                    ProcessIds = RiftWindowService.ExtractProcessIds(lockedRoles)
                };
            }

            return filter;
        }

        private static void LogControllerIdle(
            DiagnosticService diag,
            bool isLoggingEnabled,
            int slotIndex,
            string idleReason,
            bool roleMatched,
            float? leaderDistance = null,
            bool? withinLeaderBand = null)
        {
            if (!isLoggingEnabled)
            {
                return;
            }

            diag.LogControllerAction(new ControllerActionLogEntry
            {
                Slot = slotIndex + 1,
                LeaderDistance = leaderDistance,
                SelectedAxis = "None",
                IdleReason = idleReason,
                RoleMatched = roleMatched,
                ProgressState = "Unknown",
                WithinLeaderBand = withinLeaderBand,
            });
        }

        private static bool ToggleFollowState(
            ref bool isFollowEnabled,
            ref List<RiftLockedRoleAssignment>? lockedRoles,
            List<RiftWindowSlot> slots,
            GameState[] gameStates,
            RiftWindowFilter? baseFilter)
        {
            if (!isFollowEnabled)
            {
                IntPtr preferredLeaderHwnd = RiftWindowService.GetForegroundWindowHandle();
                bool hasForegroundRiftWindow = preferredLeaderHwnd != IntPtr.Zero
                    && slots.Any(slot => slot.Window is not null && slot.Window.Hwnd == preferredLeaderHwnd);
                bool hasExplicitRoleOrder = baseFilter?.ProcessIds is { Length: > 0 }
                    || baseFilter?.Hwnds is { Length: > 0 };

                if (!hasForegroundRiftWindow && !hasExplicitRoleOrder)
                {
                    return false;
                }

                List<RiftLockedRoleAssignment> capturedRoles = RiftWindowService.CaptureRoleAssignments(
                    slots,
                    gameStates,
                    hasForegroundRiftWindow ? preferredLeaderHwnd : IntPtr.Zero);
                if (capturedRoles.Count == 0)
                {
                    return false;
                }

                lockedRoles = capturedRoles;
                isFollowEnabled = true;
                return true;
            }

            isFollowEnabled = false;
            lockedRoles = null;
            return true;
        }

        private static int GetDisplayTargetCount(BridgeOptions options, List<RiftLockedRoleAssignment>? lockedRoles)
        {
            if (lockedRoles is { Count: > 0 })
            {
                return Math.Min(lockedRoles.Count, 5);
            }

            if (options.ProcessIds is { Length: > 0 })
            {
                return Math.Min(options.ProcessIds.Length, 5);
            }

            if (options.Hwnds is { Length: > 0 })
            {
                return Math.Min(options.Hwnds.Length, 5);
            }

            return 5;
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
            Console.WriteLine();
            WriteHeader("USAGE");
            WriteUsage("LeaderDecoder.exe --list [--pid N | --pids N1,N2 | --hwnd 0xHEX | --hwnds 0xA,0xB | --title-contains TEXT]");
            WriteUsage("LeaderDecoder.exe --sim");
            WriteUsage("LeaderDecoder.exe --test");
            Console.WriteLine();
            WriteHeader("OPTIONS");
            WriteOption("--list", "List detected/filtered RIFT windows and exit");
            WriteOption("--pid N", "Target a specific process id");
            WriteOption("--pids N1,N2", "Target multiple process ids in the given order");
            WriteOption("--hwnd HEX", "Target a specific HWND, e.g. 0x351350");
            WriteOption("--hwnds A,B", "Target multiple HWNDs in the given order");
            WriteOption("--title-contains TEXT", "Filter windows by title substring");
            WriteOption("--sim", "Run simulation mode");
            WriteOption("--test", "Run encode/decode roundtrip tests");
            WriteOption("--help", "Show this help");
        }

        private static void WriteHeader(string text)
        {
            var prev = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(text);
            Console.ForegroundColor = prev;
        }

        private static void WriteUsage(string text)
        {
            var prev = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("  ");
            Console.WriteLine(text);
            Console.ForegroundColor = prev;
        }

        private static void WriteOption(string flag, string description)
        {
            var prev = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"  {flag.PadRight(24)}");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(description);
            Console.ForegroundColor = prev;
        }

        private static void TryPrepareConsole()
        {
            try
            {
                Console.Clear();
            }
            catch (IOException)
            {
            }
            catch (InvalidOperationException)
            {
            }

            try
            {
                Console.Title = "🛰️ Leader: Multi-Agent Bridge v1.2";
            }
            catch (IOException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        private static bool TryReadConsoleKey(out ConsoleKey? key)
        {
            key = null;

            try
            {
                if (!Console.KeyAvailable)
                {
                    return false;
                }

                key = Console.ReadKey(true).Key;
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
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
