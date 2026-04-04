using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using LeaderDecoder.Services;

namespace LeaderLiveInspector
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            bool watch = args.Any(arg => string.Equals(arg, "--watch", StringComparison.OrdinalIgnoreCase));
            string? saveDir = GetOptionValue(args, "--save-dir");

            Console.Title = "Leader Live Inspector";
            Console.WriteLine("============================================================");
            Console.WriteLine("Leader Live Inspector (non-invasive)");
            Console.WriteLine("Captures only the top-left 28x4 client-area strip.");
            Console.WriteLine("============================================================");

            var capture = new CaptureEngine();

            do
            {
                if (watch)
                {
                    Console.Clear();
                    Console.WriteLine("============================================================");
                    Console.WriteLine("Leader Live Inspector (non-invasive)");
                    Console.WriteLine("Captures only the top-left 28x4 client-area strip.");
                    Console.WriteLine("============================================================");
                }

                var windows = FindRiftWindows();
                var savedConfig = TryLoadSavedRiftConfig();
                Console.WriteLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"Detected RIFT windows: {windows.Count}");
                Console.WriteLine(BuildSavedConfigSummary(savedConfig));

                if (windows.Count == 0)
                {
                    Console.WriteLine("No live RIFT windows with a main handle were found.");
                }

                for (int index = 0; index < windows.Count; index++)
                {
                    var window = windows[index];
                    var snapshot = GetWindowSnapshot(window.Hwnd);
                    Console.WriteLine();
                    Console.WriteLine($"[{index + 1}] PID {window.ProcessId} | {window.Title}");
                    Console.WriteLine(BuildWindowSummary(snapshot, savedConfig));

                    using var bmp = capture.CaptureRegion(window.Hwnd, StripInspector.StripWidth, StripInspector.StripHeight);
                    var analysis = StripInspector.Analyze(bmp);

                    Console.WriteLine(StripInspector.FormatStateSummary(analysis.State));
                    Console.WriteLine(StripInspector.FormatSampleTable(analysis));

                    if (!string.IsNullOrWhiteSpace(saveDir))
                    {
                        Directory.CreateDirectory(saveDir);
                        string safeTitle = SanitizeFileName(window.Title);
                        string outputPath = Path.Combine(saveDir, $"{DateTime.Now:yyyyMMdd_HHmmssfff}_{safeTitle}_{window.ProcessId}.png");
                        string actualOutputPath = SaveBitmapCopy(bmp, outputPath);
                        Console.WriteLine($"Saved capture: {actualOutputPath}");
                    }
                }

                if (watch)
                {
                    Console.WriteLine();
                    Console.WriteLine("Press Ctrl+C to stop. Refreshing in 1000 ms...");
                    Thread.Sleep(1000);
                }
            }
            while (watch);
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

        private static string BuildWindowSummary(WindowSnapshot snapshot, SavedRiftConfig? config)
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

        private static List<RiftWindow> FindRiftWindows()
        {
            var windows = new Dictionary<int, RiftWindow>();

            foreach (string processName in new[] { "rift_x64", "RIFT" })
            {
                foreach (Process process in Process.GetProcessesByName(processName))
                {
                    if (process.MainWindowHandle == IntPtr.Zero)
                    {
                        continue;
                    }

                    windows[process.Id] = new RiftWindow
                    {
                        ProcessId = process.Id,
                        Title = string.IsNullOrWhiteSpace(process.MainWindowTitle) ? process.ProcessName : process.MainWindowTitle,
                        Hwnd = process.MainWindowHandle
                    };
                }
            }

            return windows.Values
                .OrderBy(window => window.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(window => window.ProcessId)
                .ToList();
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

        private static WindowSnapshot GetWindowSnapshot(IntPtr hwnd)
        {
            var windowRect = new Win32.Rect();
            var clientRect = new Win32.Rect();
            var point = new Win32.Point();

            bool haveWindowRect = Win32.GetWindowRect(hwnd, out windowRect);
            bool haveClientRect = Win32.GetClientRect(hwnd, out clientRect);
            bool haveClientPoint = haveClientRect && Win32.ClientToScreen(hwnd, ref point);

            return new WindowSnapshot
            {
                IsMinimized = Win32.IsIconic(hwnd),
                WindowLeft = haveWindowRect ? windowRect.Left : null,
                WindowTop = haveWindowRect ? windowRect.Top : null,
                WindowWidth = haveWindowRect ? windowRect.Right - windowRect.Left : null,
                WindowHeight = haveWindowRect ? windowRect.Bottom - windowRect.Top : null,
                ClientLeft = haveClientPoint ? point.X : null,
                ClientTop = haveClientPoint ? point.Y : null,
                ClientWidth = haveClientRect ? clientRect.Right - clientRect.Left : null,
                ClientHeight = haveClientRect ? clientRect.Bottom - clientRect.Top : null
            };
        }

        private static string? GetOptionValue(string[] args, string optionName)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
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

        private sealed class RiftWindow
        {
            public required string Title { get; init; }
            public required IntPtr Hwnd { get; init; }
            public required int ProcessId { get; init; }
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

        private sealed class WindowSnapshot
        {
            public bool IsMinimized { get; init; }
            public int? WindowLeft { get; init; }
            public int? WindowTop { get; init; }
            public int? WindowWidth { get; init; }
            public int? WindowHeight { get; init; }
            public int? ClientLeft { get; init; }
            public int? ClientTop { get; init; }
            public int? ClientWidth { get; init; }
            public int? ClientHeight { get; init; }
        }

        private static class Win32
        {
            [StructLayout(LayoutKind.Sequential)]
            internal struct Rect
            {
                public int Left;
                public int Top;
                public int Right;
                public int Bottom;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct Point
            {
                public int X;
                public int Y;
            }

            [DllImport("user32.dll", SetLastError = true)]
            internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

            [DllImport("user32.dll", SetLastError = true)]
            internal static extern bool GetClientRect(IntPtr hwnd, out Rect rect);

            [DllImport("user32.dll", SetLastError = true)]
            internal static extern bool ClientToScreen(IntPtr hwnd, ref Point point);

            [DllImport("user32.dll", SetLastError = true)]
            internal static extern bool IsIconic(IntPtr hwnd);
        }
    }
}
