using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
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
            Console.Clear();
            Console.Title = "🛰️ Leader: Multi-Agent Bridge v1.0";
            
            // 🧱 Bootstrapping (Hardening Agent)
            var config = new ConfigManager();
            var settings = config.Settings;
            
            Console.WriteLine("================================================================================");
            Console.WriteLine("🛰️  LEADER: OPTICAL TELEMETRY BRIDGE (v1.1)");
            Console.WriteLine($"   Mode: {(args.Length > 0 && args[0] == "--sim" ? "SIMULATION" : "LIVE SCAN")}");
            Console.WriteLine("   Keys: [T]=Toggle Follow  [ScrollLock]=Global Toggle  [L]=Log  [S]=Snap  [R]=Reload Config");
            Console.WriteLine("================================================================================");

            // Initialize Modular Services
            var capture  = new CaptureEngine();
            var telemetry = new TelemetryService();
            var nav      = new NavigationKernel();
            var input    = new InputEngine();
            var follow   = new FollowController(input, nav, settings);
            var diag     = new DiagnosticService();
            
            bool isSimMode       = args.Length > 0 && args[0] == "--sim";
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
            for (int i = 0; i < 5; i++) gameStates[i] = new GameState();

            // Mock Leader for SIM mode
            var mockLeader = new GameState { IsValid = true, PlayerHP = 255, CoordX = 1000, CoordY = 20, CoordZ = 1000, RawFacing = 0, IsAlive = true };

            // Main Telemetry Loop (30Hz Target)
            while (true)
            {
                var cycleSw = Stopwatch.StartNew();
                var windows = FindRiftWindows();

                // 0. Standby Logic
                if (windows.Count == 0 && !isSimMode)
                {
                    Console.SetCursorPosition(0, 5);
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[STANDBY] Waiting for RIFT game windows... (Ensure titles start with 'RIFT')      ");
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
                        var (name, hwnd) = windows[i];
                        using (var bmp = capture.CaptureRegion(hwnd, settings.CaptureWidth, settings.CaptureHeight))
                        {
                            state = telemetry.Decode(bmp);
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
                                follow.EmergencyStop(windows[i].Hwnd);
                            }
                            else
                            {
                                follow.Update(i, state, gameStates[0], windows[i].Hwnd);
                            }
                        }
                        else if (i > 0 && i < windows.Count && !isFollowEnabled)
                        {
                            follow.EmergencyStop(windows[i].Hwnd);
                        }

                        // UI Dashboard
                        string zoneFlag   = zoneChanged ? " [ZONE!]" : "       ";
                        string headingText = state.IsHeadingLocked ? $"{state.EstimatedHeading,5:F2}" : " --- ";
                        string status = $"HP:{state.PlayerHP,3:D3} | X:{state.CoordX,7:F1} Y:{state.CoordY,6:F1} Z:{state.CoordZ,7:F1} | F:{state.RawFacing,5:F2}({headingText})";
                        string flagStr = $"{(state.IsCombat ? "[CB]" : "    ")} {(state.IsMounted ? "[MT]" : "    ")} {(state.IsAlive ? "    " : "[DEAD]")}{zoneFlag}";
                        Console.ForegroundColor = zoneChanged ? ConsoleColor.Yellow : ConsoleColor.Gray;
                        Console.WriteLine($" SLOT[{i+1}] {((i < windows.Count) ? windows[i].Name : "SIM_LEAD"),-12} | {status} | {flagStr}");
                        Console.ForegroundColor = ConsoleColor.Gray;
                    }
                    else
                    {
                        Console.WriteLine($" SLOT[{i+1}] {(i < windows.Count ? windows[i].Name : "UNASSIGNED"),-12} | -- STANDBY --                                                                      ");
                    }
                }

                // 3. Maintain stable frame timing
                while (cycleSw.ElapsedMilliseconds < (1000 / settings.TargetFPS)) Thread.Sleep(1);
            }
        }

        /// <summary>
        /// Finds and enumerates active RIFT window handles.
        /// </summary>
        static List<(string Name, IntPtr Hwnd)> FindRiftWindows()
        {
            var results = new List<(string, IntPtr)>();
            var processes = Process.GetProcessesByName("RIFT");
            foreach (var p in processes)
            {
                if (p.MainWindowHandle != IntPtr.Zero)
                {
                    results.Add((p.MainWindowTitle, p.MainWindowHandle));
                }
            }
            // Sort by Title (RIFT1, RIFT2, etc.) for stable slot mapping
            results.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return results;
        }
    }
}
