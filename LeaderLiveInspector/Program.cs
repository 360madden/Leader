using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using LeaderDecoder.Models;
using LeaderDecoder.Services;

namespace LeaderLiveInspector
{
    internal static class Program
    {
        private const int DefaultWatchIntervalMs = 1000;
        private const string SampleLogName = "live_inspector_samples.csv";
        private const string EventLogName = "live_inspector_events.csv";

        private static int Main(string[] args)
        {
            var diag = new DiagnosticService();

            try
            {
                if (!TryParseOptions(args, out var options, out string? error))
                {
                    Console.Error.WriteLine(error);
                    Console.Error.WriteLine();
                    PrintUsage();
                    return 1;
                }

                if (options.ShowHelp)
                {
                    PrintUsage();
                    return 0;
                }

                string repoRoot = FindRepoRoot();
                string debugDir = Path.Combine(repoRoot, "LeaderLiveInspector", "debug");
                Directory.CreateDirectory(debugDir);
                string sampleLogPath = Path.Combine(debugDir, SampleLogName);
                string eventLogPath = Path.Combine(debugDir, EventLogName);
                EnsureSampleLog(sampleLogPath);
                EnsureEventLog(eventLogPath);

                Console.Title = "Leader Live Inspector";
                Console.WriteLine("============================================================");
                Console.WriteLine("Leader Live Inspector (non-invasive)");
                Console.WriteLine($"Captures only the top-left {StripInspector.StripWidth}x{StripInspector.StripHeight} client-area strip.");
                Console.WriteLine("============================================================");

                var capture = new CaptureEngine(diag);
                var previousSnapshots = new Dictionary<string, InspectionSnapshot>(StringComparer.OrdinalIgnoreCase);
                int? previousFilteredCount = null;
                int iteration = 0;

                do
                {
                    if (options.Watch)
                    {
                        TryClearConsole();
                        Console.WriteLine("============================================================");
                        Console.WriteLine("Leader Live Inspector (non-invasive)");
                        Console.WriteLine($"Captures only the top-left {StripInspector.StripWidth}x{StripInspector.StripHeight} client-area strip.");
                        Console.WriteLine("============================================================");
                    }

                    var allWindows = RiftWindowService.FindRiftWindows();
                    var filteredWindows = RiftWindowService.FilterWindows(allWindows, BuildFilter(options));
                    var savedConfig = TryLoadSavedRiftConfig();

                    Console.WriteLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    Console.WriteLine($"Detected RIFT windows: {allWindows.Count}");
                    if (HasExplicitFilter(options))
                    {
                        Console.WriteLine($"Filtered RIFT windows: {filteredWindows.Count}");
                    }

                    Console.WriteLine(BuildSavedConfigSummary(savedConfig));

                    if (filteredWindows.Count == 0)
                    {
                        Console.WriteLine("No matching live RIFT windows with a main handle were found.");
                    }

                    if (options.ListOnly && !options.Watch)
                    {
                        PrintWindowList(filteredWindows);
                        return 0;
                    }

                    if (options.Watch && previousFilteredCount.HasValue && previousFilteredCount.Value != filteredWindows.Count)
                    {
                        AppendEvent(eventLogPath, new LiveInspectorEventRow
                        {
                            Timestamp = DateTime.Now,
                            Window = "watch",
                            ProcessId = null,
                            Hwnd = string.Empty,
                            EventType = "filtered_count_changed",
                            Before = previousFilteredCount.Value.ToString(CultureInfo.InvariantCulture),
                            After = filteredWindows.Count.ToString(CultureInfo.InvariantCulture),
                            Details = $"Filtered window count changed from {previousFilteredCount.Value} to {filteredWindows.Count}.",
                        });
                    }

                    var currentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    for (int index = 0; index < filteredWindows.Count; index++)
                    {
                        var window = filteredWindows[index];
                        var snapshot = RiftWindowService.GetWindowSnapshot(window.Hwnd);

                        Console.WriteLine();
                        Console.WriteLine($"[{index + 1}] {RiftWindowService.FormatIdentity(window)}");
                        Console.WriteLine($"Selectors: {RiftWindowService.FormatSelectorHints(window)}");
                        Console.WriteLine(BuildWindowSummary(snapshot, savedConfig));

                        using var bmp = capture.CaptureRegion(window.Hwnd, StripInspector.StripWidth, StripInspector.StripHeight);
                        var analysis = StripInspector.Analyze(bmp);
                        var inspection = new InspectionSnapshot
                        {
                            Window = window,
                            Snapshot = snapshot,
                            Analysis = analysis,
                            SavedResolutionMatchesLiveClient = savedConfig?.ResolutionX is int savedX && savedConfig.ResolutionY is int savedY
                                ? snapshot.ClientWidth == savedX && snapshot.ClientHeight == savedY
                                : null
                        };

                        currentKeys.Add(BuildSnapshotKey(window));
                        AppendSample(sampleLogPath, inspection);

                        if (options.Watch)
                        {
                            string key = BuildSnapshotKey(window);
                            if (previousSnapshots.TryGetValue(key, out InspectionSnapshot? previous))
                            {
                                LogSnapshotEvents(eventLogPath, previous, inspection);
                            }
                            else
                            {
                                AppendEvent(eventLogPath, new LiveInspectorEventRow
                                {
                                    Timestamp = DateTime.Now,
                                    Window = RiftWindowService.FormatIdentity(window),
                                    ProcessId = window.ProcessId,
                                    Hwnd = RiftWindowService.FormatHwnd(window.Hwnd),
                                    EventType = "window_appeared",
                                    Before = string.Empty,
                                    After = analysis.State.IsValid ? "valid" : "invalid",
                                    Details = "Window appeared in the filtered watch set.",
                                });
                            }

                            previousSnapshots[key] = inspection;
                        }

                        Console.WriteLine(StripInspector.FormatStateSummary(analysis.State));
                        Console.WriteLine(StripInspector.FormatSampleTable(analysis));

                        if (!string.IsNullOrWhiteSpace(options.SaveDir))
                        {
                            Directory.CreateDirectory(options.SaveDir);
                            string safeTitle = SanitizeFileName(window.Title);
                            string outputPath = Path.Combine(
                                options.SaveDir,
                                $"{DateTime.Now:yyyyMMdd_HHmmssfff}_{safeTitle}_{window.ProcessId}_{RiftWindowService.FormatHwnd(window.Hwnd).Replace("0x", string.Empty)}.png");
                            string actualOutputPath = SaveBitmapCopy(bmp, outputPath);
                            Console.WriteLine($"Saved capture: {actualOutputPath}");
                        }
                    }

                    if (options.Watch)
                    {
                        var missingKeys = previousSnapshots.Keys.Where(key => !currentKeys.Contains(key)).ToList();
                        foreach (string missingKey in missingKeys)
                        {
                            InspectionSnapshot previous = previousSnapshots[missingKey];
                            AppendEvent(eventLogPath, new LiveInspectorEventRow
                            {
                                Timestamp = DateTime.Now,
                                Window = RiftWindowService.FormatIdentity(previous.Window),
                                ProcessId = previous.Window.ProcessId,
                                Hwnd = RiftWindowService.FormatHwnd(previous.Window.Hwnd),
                                EventType = "window_disappeared",
                                Before = previous.Analysis.State.IsValid ? "valid" : "invalid",
                                After = string.Empty,
                                Details = "Window disappeared from the filtered watch set.",
                            });
                            previousSnapshots.Remove(missingKey);
                        }
                    }

                    previousFilteredCount = filteredWindows.Count;
                    iteration++;

                    if (options.Watch)
                    {
                        Console.WriteLine();
                        if (options.WatchCount > 0 && iteration >= options.WatchCount)
                        {
                            break;
                        }

                        Console.WriteLine($"Press Ctrl+C to stop. Refreshing in {options.IntervalMs} ms...");
                        Thread.Sleep(options.IntervalMs);
                    }
                }
                while (options.Watch);

                return 0;
            }
            catch (Exception ex)
            {
                diag.LogToolFailure(
                    source: "LeaderLiveInspector",
                    operation: "UnhandledException",
                    detail: "Live inspector crashed.",
                    context: string.Join(" ", args),
                    ex: ex,
                    dedupeKey: "live-inspector-unhandled",
                    throttleSeconds: 1.0);
                Console.Error.WriteLine($"Unhandled error: {ex.Message}");
                return 1;
            }
        }

        private static RiftWindowFilter? BuildFilter(Options options)
        {
            if (!HasExplicitFilter(options))
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

        private static bool HasExplicitFilter(Options options)
        {
            return options.ProcessId.HasValue
                || options.ProcessIds is { Length: > 0 }
                || options.Hwnd.HasValue
                || options.Hwnds is { Length: > 0 }
                || !string.IsNullOrWhiteSpace(options.TitleContains);
        }

        private static void PrintWindowList(List<RiftWindowInfo> windows)
        {
            for (int index = 0; index < windows.Count; index++)
            {
                var window = windows[index];
                var snapshot = RiftWindowService.GetWindowSnapshot(window.Hwnd);
                Console.WriteLine($"[{index + 1}] {RiftWindowService.FormatIdentity(window)} | Client={snapshot.ClientWidth ?? 0}x{snapshot.ClientHeight ?? 0}");
                Console.WriteLine($"    Selectors: {RiftWindowService.FormatSelectorHints(window)}");
            }
        }

        private static string BuildSavedConfigSummary(SavedRiftConfig? config)
        {
            if (config is null)
            {
                return "SavedConfig: missing (%APPDATA%\\RIFT\\rift.cfg)";
            }

            var parts = new List<string>
            {
                $"SavedConfig: {config.Path}"
            };

            if (config.ResolutionX.HasValue && config.ResolutionY.HasValue)
            {
                parts.Add($"SavedResolution={config.ResolutionX.Value}x{config.ResolutionY.Value}");
            }

            if (config.WindowMode.HasValue)
            {
                parts.Add($"WindowMode={config.WindowMode.Value}");
            }

            if (config.Maximized.HasValue)
            {
                parts.Add($"Maximized={config.Maximized.Value}");
            }

            if (!string.IsNullOrWhiteSpace(config.PlayInBackground))
            {
                parts.Add($"PlayInBackground={config.PlayInBackground}");
            }

            return string.Join(" | ", parts);
        }

        private static void TryClearConsole()
        {
            try
            {
                if (!Console.IsOutputRedirected)
                {
                    Console.Clear();
                }
            }
            catch (IOException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        private static string BuildWindowSummary(RiftWindowSnapshot snapshot, SavedRiftConfig? config)
        {
            var parts = new List<string>
            {
                $"LiveWindow={snapshot.WindowLeft},{snapshot.WindowTop} {snapshot.WindowWidth}x{snapshot.WindowHeight}",
                $"LiveClient={snapshot.ClientLeft},{snapshot.ClientTop} {snapshot.ClientWidth}x{snapshot.ClientHeight}",
                $"Minimized={snapshot.IsMinimized}"
            };

            if (config?.ResolutionX is int savedX && config.ResolutionY is int savedY)
            {
                bool matches = snapshot.ClientWidth == savedX && snapshot.ClientHeight == savedY;
                parts.Add($"SavedResolutionMatchesLiveClient={matches}");
            }

            return string.Join(" | ", parts);
        }

        private static string BuildSnapshotKey(RiftWindowInfo window)
        {
            return $"{window.ProcessId}|{RiftWindowService.FormatHwnd(window.Hwnd)}";
        }

        private static void LogSnapshotEvents(string path, InspectionSnapshot previous, InspectionSnapshot current)
        {
            if (!string.Equals(previous.Analysis.ProfileName, current.Analysis.ProfileName, StringComparison.OrdinalIgnoreCase))
            {
                AppendEvent(path, BuildEvent(current, "profile_changed", previous.Analysis.ProfileName, current.Analysis.ProfileName, "Sample profile changed."));
            }

            if (previous.Analysis.State.IsValid != current.Analysis.State.IsValid)
            {
                AppendEvent(
                    path,
                    BuildEvent(
                        current,
                        "strip_validity_changed",
                        previous.Analysis.State.IsValid ? "valid" : "invalid",
                        current.Analysis.State.IsValid ? "valid" : "invalid",
                        "Telemetry strip validity changed."));
            }

            string previousClient = $"{previous.Snapshot.ClientWidth}x{previous.Snapshot.ClientHeight}";
            string currentClient = $"{current.Snapshot.ClientWidth}x{current.Snapshot.ClientHeight}";
            if (!string.Equals(previousClient, currentClient, StringComparison.Ordinal))
            {
                AppendEvent(path, BuildEvent(current, "client_size_changed", previousClient, currentClient, "Live client size changed."));
            }

            if (previous.Snapshot.IsMinimized != current.Snapshot.IsMinimized)
            {
                AppendEvent(
                    path,
                    BuildEvent(
                        current,
                        "minimized_changed",
                        previous.Snapshot.IsMinimized ? "true" : "false",
                        current.Snapshot.IsMinimized ? "true" : "false",
                        "Window minimized state changed."));
            }

            if (previous.SavedResolutionMatchesLiveClient != current.SavedResolutionMatchesLiveClient)
            {
                AppendEvent(
                    path,
                    BuildEvent(
                        current,
                        "saved_resolution_match_changed",
                        NullableBool(previous.SavedResolutionMatchesLiveClient),
                        NullableBool(current.SavedResolutionMatchesLiveClient),
                        "Saved resolution match state changed."));
            }
        }

        private static LiveInspectorEventRow BuildEvent(InspectionSnapshot inspection, string eventType, string before, string after, string details)
        {
            return new LiveInspectorEventRow
            {
                Timestamp = DateTime.Now,
                Window = RiftWindowService.FormatIdentity(inspection.Window),
                ProcessId = inspection.Window.ProcessId,
                Hwnd = RiftWindowService.FormatHwnd(inspection.Window.Hwnd),
                EventType = eventType,
                Before = before,
                After = after,
                Details = details,
            };
        }

        private static void EnsureSampleLog(string path)
        {
            if (!File.Exists(path))
            {
                File.WriteAllText(path, "Timestamp,Window,ProcessId,Hwnd,Profile,StripWidth,StripHeight,IsValid,CoordX,CoordY,CoordZ,IsMoving,IsMounted,HasTarget,PlayerHP,TargetHP,RawFacing,ZoneHash,PlayerTag,ClientWidth,ClientHeight,IsMinimized,SavedResolutionMatchesLiveClient\n");
            }
        }

        private static void EnsureEventLog(string path)
        {
            if (!File.Exists(path))
            {
                File.WriteAllText(path, "Timestamp,Window,ProcessId,Hwnd,EventType,Before,After,Details\n");
            }
        }

        private static void AppendSample(string path, InspectionSnapshot inspection)
        {
            GameState state = inspection.Analysis.State;
            string line = string.Join(",",
                Csv(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)),
                Csv(RiftWindowService.FormatIdentity(inspection.Window)),
                Csv(inspection.Window.ProcessId.ToString(CultureInfo.InvariantCulture)),
                Csv(RiftWindowService.FormatHwnd(inspection.Window.Hwnd)),
                Csv(inspection.Analysis.ProfileName),
                Csv(inspection.Analysis.Width.ToString(CultureInfo.InvariantCulture)),
                Csv(inspection.Analysis.Height.ToString(CultureInfo.InvariantCulture)),
                Csv(Bool(state.IsValid)),
                Csv(Float(state.CoordX)),
                Csv(Float(state.CoordY)),
                Csv(Float(state.CoordZ)),
                Csv(Bool(state.IsMoving)),
                Csv(Bool(state.IsMounted)),
                Csv(Bool(state.HasTarget)),
                Csv(state.PlayerHP.ToString(CultureInfo.InvariantCulture)),
                Csv(state.TargetHP.ToString(CultureInfo.InvariantCulture)),
                Csv(Float(state.RawFacing)),
                Csv(state.ZoneHash.ToString(CultureInfo.InvariantCulture)),
                Csv(state.PlayerTag),
                Csv(NullableInt(inspection.Snapshot.ClientWidth)),
                Csv(NullableInt(inspection.Snapshot.ClientHeight)),
                Csv(Bool(inspection.Snapshot.IsMinimized)),
                Csv(NullableBool(inspection.SavedResolutionMatchesLiveClient)));

            File.AppendAllText(path, line + Environment.NewLine);
        }

        private static void AppendEvent(string path, LiveInspectorEventRow row)
        {
            string line = string.Join(",",
                Csv(row.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)),
                Csv(row.Window),
                Csv(row.ProcessId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                Csv(row.Hwnd),
                Csv(row.EventType),
                Csv(row.Before),
                Csv(row.After),
                Csv(row.Details));

            File.AppendAllText(path, line + Environment.NewLine);
        }

        private static SavedRiftConfig? TryLoadSavedRiftConfig()
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RIFT",
                "rift.cfg");

            if (!File.Exists(path))
            {
                return null;
            }

            var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            string currentSection = string.Empty;

            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";") || line.StartsWith("#"))
                {
                    continue;
                }

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    currentSection = line[1..^1];
                    if (!sections.ContainsKey(currentSection))
                    {
                        sections[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    }

                    continue;
                }

                int separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                if (!sections.ContainsKey(currentSection))
                {
                    sections[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                string key = line[..separator].Trim();
                string value = line[(separator + 1)..].Trim();
                sections[currentSection][key] = value;
            }

            sections.TryGetValue("Client", out var clientSection);
            sections.TryGetValue("Video", out var videoSection);
            sections.TryGetValue("Window", out var windowSection);

            return new SavedRiftConfig
            {
                Path = path,
                DocumentsDirectory = TryGetValue(clientSection, "DocumentsDirectory"),
                PlayInBackground = TryGetValue(clientSection, "PlayInBackground"),
                ResolutionX = TryParseInt(TryGetValue(videoSection, "ResolutionX")),
                ResolutionY = TryParseInt(TryGetValue(videoSection, "ResolutionY")),
                WindowMode = TryParseInt(TryGetValue(videoSection, "WindowMode")),
                Maximized = TryParseBool(TryGetValue(windowSection, "Maximized")),
                TopX = TryParseInt(TryGetValue(windowSection, "TopX")),
                TopY = TryParseInt(TryGetValue(windowSection, "TopY"))
            };
        }

        private static bool TryParseOptions(string[] args, out Options options, out string? error)
        {
            options = new Options();
            error = null;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                switch (arg.ToLowerInvariant())
                {
                    case "--help":
                    case "-h":
                    case "/?":
                        options.ShowHelp = true;
                        break;

                    case "--watch":
                        options.Watch = true;
                        break;

                    case "--watch-count":
                        if (!TryReadInt(args, ref i, out int watchCount, out error))
                        {
                            return false;
                        }

                        if (watchCount < 0)
                        {
                            error = "--watch-count cannot be negative.";
                            return false;
                        }

                        options.WatchCount = watchCount;
                        break;

                    case "--interval-ms":
                        if (!TryReadInt(args, ref i, out int intervalMs, out error))
                        {
                            return false;
                        }

                        if (intervalMs <= 0)
                        {
                            error = "--interval-ms must be greater than zero.";
                            return false;
                        }

                        options.IntervalMs = intervalMs;
                        break;

                    case "--list":
                        options.ListOnly = true;
                        break;

                    case "--save-dir":
                        if (!TryReadString(args, ref i, out string? saveDir, out error))
                        {
                            return false;
                        }

                        options.SaveDir = saveDir;
                        break;

                    case "--pid":
                        if (!TryReadInt(args, ref i, out int pidValue, out error))
                        {
                            return false;
                        }

                        options.ProcessId = pidValue;
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
                        if (!TryReadString(args, ref i, out string? titleText, out error))
                        {
                            return false;
                        }

                        options.TitleContains = titleText;
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

            if (!int.TryParse(raw, out value))
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

        private static void WriteColored(ConsoleColor color, string text)
        {
            var previous = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ForegroundColor = previous;
        }

        private static void PrintUsage()
        {
            WriteColored(ConsoleColor.Cyan, "Leader Live Inspector — strip capture and decode");
            Console.WriteLine();

            WriteColored(ConsoleColor.Yellow, "Usage:");
            WriteColored(ConsoleColor.White, "  LeaderLiveInspector.exe --list");
            WriteColored(ConsoleColor.White, "  LeaderLiveInspector.exe --pid 127928");
            WriteColored(ConsoleColor.White, "  LeaderLiveInspector.exe --pids 127928,128104 --watch");
            WriteColored(ConsoleColor.White, "  LeaderLiveInspector.exe --pids 127928,128104 --watch --watch-count 5 --interval-ms 500");
            WriteColored(ConsoleColor.White, "  LeaderLiveInspector.exe --hwnd 0x123456 --save-dir C:\\temp");
            WriteColored(ConsoleColor.White, "  LeaderLiveInspector.exe --hwnds 0x351350,0x123456 --save-dir C:\\temp");
            WriteColored(ConsoleColor.White, "  LeaderLiveInspector.exe --title-contains RIFT --watch");
            Console.WriteLine();

            WriteColored(ConsoleColor.Yellow, "Target selection:");
            WriteColored(ConsoleColor.White, "  --list                 List matching RIFT windows");
            WriteColored(ConsoleColor.White, "  --pid N                Filter to a specific process id");
            WriteColored(ConsoleColor.White, "  --pids N1,N2           Filter to multiple process ids in the given order");
            WriteColored(ConsoleColor.White, "  --hwnd HEX             Filter to a specific HWND, e.g. 0x123456");
            WriteColored(ConsoleColor.White, "  --hwnds A,B            Filter to multiple HWNDs in the given order");
            WriteColored(ConsoleColor.White, "  --title-contains TEXT  Filter to window titles containing TEXT");
            Console.WriteLine();

            WriteColored(ConsoleColor.Yellow, "Capture mode:");
            WriteColored(ConsoleColor.White, "  --watch                Refresh repeatedly");
            WriteColored(ConsoleColor.White, "  --watch-count N        Limit watch mode to N refreshes (0 = unlimited)");
            WriteColored(ConsoleColor.White, "  --interval-ms N        Refresh interval in milliseconds (default: 1000)");
            WriteColored(ConsoleColor.White, "  --save-dir PATH        Save captured strips to PATH");
            Console.WriteLine();

            WriteColored(ConsoleColor.Yellow, "General:");
            WriteColored(ConsoleColor.White, "  --help                 Show this help");
            Console.WriteLine();
            WriteColored(ConsoleColor.DarkGray, "Best practice: use --save-dir when you need artifacts, and prefer PID/ HWND selection over title matching when possible.");
        }

        private static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        }

        private static string SaveBitmapCopy(Bitmap source, string outputPath)
        {
            using var copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(copy);
            g.DrawImage(source, 0, 0, source.Width, source.Height);

            try
            {
                copy.Save(outputPath, ImageFormat.Png);
                return outputPath;
            }
            catch
            {
                string bmpFallbackPath = Path.ChangeExtension(outputPath, ".bmp");
                copy.Save(bmpFallbackPath, ImageFormat.Bmp);
                return bmpFallbackPath;
            }
        }

        private static string? TryGetValue(Dictionary<string, string>? section, string key)
        {
            return section is not null && section.TryGetValue(key, out var value) ? value : null;
        }

        private static int? TryParseInt(string? value)
        {
            return int.TryParse(value, out int parsed) ? parsed : null;
        }

        private static bool? TryParseBool(string? value)
        {
            return bool.TryParse(value, out bool parsed) ? parsed : null;
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(Environment.CurrentDirectory);
            while (dir is not null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "LeaderDecoder"))
                    && Directory.Exists(Path.Combine(dir.FullName, "LeaderLiveInspector")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            return Environment.CurrentDirectory;
        }

        private static string Csv(string? value)
        {
            string text = value ?? string.Empty;
            return text.Contains('"') || text.Contains(',') || text.Contains('\n') || text.Contains('\r')
                ? $"\"{text.Replace("\"", "\"\"")}\""
                : text;
        }

        private static string Bool(bool value) => value ? "true" : "false";

        private static string Float(float value) => value.ToString("F3", CultureInfo.InvariantCulture);

        private static string NullableInt(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

        private static string NullableBool(bool? value) => value.HasValue ? Bool(value.Value) : string.Empty;

        private sealed class SavedRiftConfig
        {
            public required string Path { get; init; }
            public string? DocumentsDirectory { get; init; }
            public string? PlayInBackground { get; init; }
            public int? ResolutionX { get; init; }
            public int? ResolutionY { get; init; }
            public int? WindowMode { get; init; }
            public bool? Maximized { get; init; }
            public int? TopX { get; init; }
            public int? TopY { get; init; }
        }

        private sealed class Options
        {
            public bool ShowHelp { get; set; }
            public bool Watch { get; set; }
            public int WatchCount { get; set; }
            public int IntervalMs { get; set; } = DefaultWatchIntervalMs;
            public bool ListOnly { get; set; }
            public string? SaveDir { get; set; }
            public int? ProcessId { get; set; }
            public int[]? ProcessIds { get; set; }
            public IntPtr? Hwnd { get; set; }
            public IntPtr[]? Hwnds { get; set; }
            public string? TitleContains { get; set; }
        }

        private sealed class InspectionSnapshot
        {
            public required RiftWindowInfo Window { get; init; }
            public required RiftWindowSnapshot Snapshot { get; init; }
            public required StripAnalysis Analysis { get; init; }
            public bool? SavedResolutionMatchesLiveClient { get; init; }
        }

        private sealed class LiveInspectorEventRow
        {
            public required DateTime Timestamp { get; init; }
            public required string Window { get; init; }
            public int? ProcessId { get; init; }
            public required string Hwnd { get; init; }
            public required string EventType { get; init; }
            public required string Before { get; init; }
            public required string After { get; init; }
            public required string Details { get; init; }
        }
    }
}
