using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using LeaderDecoder.Services;

namespace LeaderLiveInspector
{
    internal static class Program
    {
        private static int Main(string[] args)
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

            Console.Title = "Leader Live Inspector";
            Console.WriteLine("============================================================");
            Console.WriteLine("Leader Live Inspector (non-invasive)");
            Console.WriteLine("Captures only the top-left 28x4 client-area strip.");
            Console.WriteLine("============================================================");

            var capture = new CaptureEngine();

            do
            {
                if (options.Watch)
                {
                    Console.Clear();
                    Console.WriteLine("============================================================");
                    Console.WriteLine("Leader Live Inspector (non-invasive)");
                    Console.WriteLine("Captures only the top-left 28x4 client-area strip.");
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

                for (int index = 0; index < filteredWindows.Count; index++)
                {
                    var window = filteredWindows[index];
                    var snapshot = RiftWindowService.GetWindowSnapshot(window.Hwnd);

                    Console.WriteLine();
                    Console.WriteLine(
                        $"[{index + 1}] PID {window.ProcessId} | HWND {RiftWindowService.FormatHwnd(window.Hwnd)} | " +
                        $"{window.ProcessName} | {window.Title}");
                    Console.WriteLine(BuildWindowSummary(snapshot, savedConfig));

                    using var bmp = capture.CaptureRegion(window.Hwnd, StripInspector.StripWidth, StripInspector.StripHeight);
                    var analysis = StripInspector.Analyze(bmp);

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
                    Console.WriteLine();
                    Console.WriteLine("Press Ctrl+C to stop. Refreshing in 1000 ms...");
                    Thread.Sleep(1000);
                }
            }
            while (options.Watch);

            return 0;
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
                Hwnd = options.Hwnd,
                TitleContains = options.TitleContains
            };
        }

        private static bool HasExplicitFilter(Options options)
        {
            return options.ProcessId.HasValue || options.Hwnd.HasValue || !string.IsNullOrWhiteSpace(options.TitleContains);
        }

        private static void PrintWindowList(List<RiftWindowInfo> windows)
        {
            for (int index = 0; index < windows.Count; index++)
            {
                var window = windows[index];
                var snapshot = RiftWindowService.GetWindowSnapshot(window.Hwnd);
                Console.WriteLine(
                    $"[{index + 1}] PID {window.ProcessId} | HWND {RiftWindowService.FormatHwnd(window.Hwnd)} | " +
                    $"{window.ProcessName} | {window.Title} | Client={snapshot.ClientWidth ?? 0}x{snapshot.ClientHeight ?? 0}");
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

        private static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  LeaderLiveInspector.exe --list");
            Console.WriteLine("  LeaderLiveInspector.exe --pid 127928");
            Console.WriteLine("  LeaderLiveInspector.exe --hwnd 0x123456 --save-dir C:\\temp");
            Console.WriteLine("  LeaderLiveInspector.exe --title-contains RIFT --watch");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --list                 List matching RIFT windows");
            Console.WriteLine("  --watch                Refresh repeatedly");
            Console.WriteLine("  --save-dir PATH        Save captured strips to PATH");
            Console.WriteLine("  --pid N                Filter to a specific process id");
            Console.WriteLine("  --hwnd HEX             Filter to a specific HWND, e.g. 0x123456");
            Console.WriteLine("  --title-contains TEXT  Filter to window titles containing TEXT");
            Console.WriteLine("  --help                 Show this help");
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
            public bool ListOnly { get; set; }
            public string? SaveDir { get; set; }
            public int? ProcessId { get; set; }
            public IntPtr? Hwnd { get; set; }
            public string? TitleContains { get; set; }
        }
    }
}
