using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using LeaderDecoder.Services;

namespace LeaderWindowResizer;

internal static class Program
{
    private static readonly Dictionary<string, ResolutionPreset> Presets =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["320x180"] = new("320x180", 320, 180),
            ["180p"] = new("320x180", 320, 180),
            ["tiny"] = new("320x180", 320, 180),
            ["485x309"] = new("485x309", 485, 309),
            ["legacy"] = new("485x309", 485, 309),
            ["640x360"] = new("640x360", 640, 360),
            ["360p"] = new("640x360", 640, 360),
            ["small"] = new("640x360", 640, 360),
            ["1280x720"] = new("1280x720", 1280, 720),
            ["720p"] = new("1280x720", 1280, 720),
            ["hd"] = new("1280x720", 1280, 720),
        };

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

        Console.Title = "Leader Window Resizer";
        Console.WriteLine("============================================================");
        Console.WriteLine("Leader Window Resizer");
        Console.WriteLine("Resizes live RIFT client windows by client-area resolution.");
        Console.WriteLine("============================================================");

        var windows = FindRiftWindows();
        if (windows.Count == 0)
        {
            Console.Error.WriteLine("No live RIFT windows with a main handle were found.");
            return 1;
        }

        Console.WriteLine($"Detected RIFT windows: {windows.Count}");
        for (int i = 0; i < windows.Count; i++)
        {
            var snapshot = GetWindowSnapshot(windows[i].Hwnd);
            Console.WriteLine(
                $"[{i + 1}] PID {windows[i].ProcessId} | {windows[i].Title} | " +
                $"Client={snapshot.ClientWidth ?? 0}x{snapshot.ClientHeight ?? 0} | " +
                $"Window={snapshot.WindowLeft ?? 0},{snapshot.WindowTop ?? 0}");
        }

        if (options.ListOnly && !options.HasResizeIntent)
        {
            return 0;
        }

        if (!options.HasResizeIntent)
        {
            Console.WriteLine();
            Console.WriteLine("No resize target was supplied.");
            PrintUsage();
            return 1;
        }

        var selected = SelectWindows(windows, options);
        if (selected.Count == 0)
        {
            Console.Error.WriteLine("No matching RIFT windows were selected.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine(BuildIntentSummary(options, selected.Count));

        CaptureEngine? capture = options.Inspect ? new CaptureEngine() : null;
        bool anyFailure = false;

        for (int index = 0; index < selected.Count; index++)
        {
            var window = selected[index];
            var before = GetWindowSnapshot(window.Hwnd);

            if (before.ClientWidth is null || before.ClientHeight is null)
            {
                Console.WriteLine();
                Console.WriteLine($"[{index + 1}] {window.Title}");
                Console.WriteLine("  Could not read current client geometry.");
                anyFailure = true;
                continue;
            }

            var targetClient = ResolveTargetClientSize(options, before);
            int targetLeft = ResolveCoordinate(options.Left, before.WindowLeft ?? 32, options.StepX, index);
            int targetTop = ResolveCoordinate(options.Top, before.WindowTop ?? 32, options.StepY, index);
            var result = ResizeWindow(window.Hwnd, targetLeft, targetTop, targetClient.Width, targetClient.Height);

            Console.WriteLine();
            Console.WriteLine($"[{index + 1}] PID {window.ProcessId} | {window.Title}");
            Console.WriteLine($"  Before client: {before.ClientWidth}x{before.ClientHeight}");
            Console.WriteLine($"  After client:  {result.After.ClientWidth}x{result.After.ClientHeight}");
            Console.WriteLine($"  Target pos:    {targetLeft},{targetTop}");
            Console.WriteLine($"  Match:         {result.ExactMatch}");

            if (!result.ExactMatch)
            {
                anyFailure = true;
                Console.WriteLine("  Warning: final client size did not exactly match the requested target.");
            }

            if (capture is not null)
            {
                if (options.WaitMs > 0)
                {
                    Thread.Sleep(options.WaitMs);
                }

                using var bmp = capture.CaptureRegion(window.Hwnd, StripInspector.StripWidth, StripInspector.StripHeight);
                var analysis = StripInspector.Analyze(bmp);
                Console.WriteLine($"  {StripInspector.FormatStateSummary(analysis.State)}");
                foreach (string line in StripInspector.FormatSampleTable(analysis).Split(Environment.NewLine))
                {
                    Console.WriteLine($"  {line}");
                }
            }
        }

        return anyFailure ? 1 : 0;
    }

    private static string BuildIntentSummary(Options options, int selectedCount)
    {
        var parts = new List<string>();

        if (options.PresetName is not null)
        {
            parts.Add($"Preset={options.PresetName}");
        }
        else if (options.Scale.HasValue)
        {
            parts.Add($"Scale={options.Scale.Value.ToString("0.###", CultureInfo.InvariantCulture)}");
        }
        else if (options.Width.HasValue && options.Height.HasValue)
        {
            parts.Add($"Size={options.Width.Value}x{options.Height.Value}");
        }

        parts.Add(selectedCount == 1 ? "Target=1 window" : $"Target={selectedCount} windows");

        if (options.Left.HasValue || options.Top.HasValue)
        {
            parts.Add($"Anchor={options.Left?.ToString() ?? "current"},{options.Top?.ToString() ?? "current"}");
        }

        if (options.StepX != 0 || options.StepY != 0)
        {
            parts.Add($"Step={options.StepX},{options.StepY}");
        }

        if (options.Inspect)
        {
            parts.Add("InspectAfterResize=True");
            parts.Add($"WaitMs={options.WaitMs}");
        }

        return string.Join(" | ", parts);
    }

    private static int ResolveCoordinate(int? requested, int current, int step, int index)
    {
        int baseValue = requested ?? current;
        return baseValue + (step * index);
    }

    private static TargetClientSize ResolveTargetClientSize(Options options, WindowSnapshot before)
    {
        if (options.Scale.HasValue)
        {
            int width = Math.Max(1, (int)Math.Round((before.ClientWidth ?? 1) * options.Scale.Value, MidpointRounding.AwayFromZero));
            int height = Math.Max(1, (int)Math.Round((before.ClientHeight ?? 1) * options.Scale.Value, MidpointRounding.AwayFromZero));
            return new TargetClientSize(width, height);
        }

        return new TargetClientSize(options.Width!.Value, options.Height!.Value);
    }

    private static ResizeResult ResizeWindow(IntPtr hwnd, int targetLeft, int targetTop, int clientWidth, int clientHeight)
    {
        var before = GetWindowSnapshot(hwnd);

        if (before.IsMinimized)
        {
            Win32.ShowWindow(hwnd, Win32.SW_RESTORE);
            Thread.Sleep(200);
            before = GetWindowSnapshot(hwnd);
        }

        int style = Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE).ToInt32();
        int exStyle = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt32();
        var outer = GetOuterSize(clientWidth, clientHeight, style, exStyle);

        if (!Win32.SetWindowPos(hwnd, IntPtr.Zero, targetLeft, targetTop, outer.Width, outer.Height, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE))
        {
            throw new InvalidOperationException($"SetWindowPos failed with Win32 error {Marshal.GetLastWin32Error()}.");
        }

        Thread.Sleep(120);
        var after = GetWindowSnapshot(hwnd);
        int widthError = clientWidth - (after.ClientWidth ?? 0);
        int heightError = clientHeight - (after.ClientHeight ?? 0);

        if (widthError != 0 || heightError != 0)
        {
            int correctedOuterWidth = Math.Max((after.WindowWidth ?? outer.Width) + widthError, clientWidth);
            int correctedOuterHeight = Math.Max((after.WindowHeight ?? outer.Height) + heightError, clientHeight);

            if (!Win32.SetWindowPos(hwnd, IntPtr.Zero, targetLeft, targetTop, correctedOuterWidth, correctedOuterHeight, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE))
            {
                throw new InvalidOperationException($"Correction SetWindowPos failed with Win32 error {Marshal.GetLastWin32Error()}.");
            }

            Thread.Sleep(120);
            after = GetWindowSnapshot(hwnd);
        }

        bool exactMatch = after.ClientWidth == clientWidth && after.ClientHeight == clientHeight;
        return new ResizeResult(before, after, exactMatch);
    }

    private static OuterSize GetOuterSize(int clientWidth, int clientHeight, int style, int exStyle)
    {
        var rect = new Win32.Rect
        {
            Left = 0,
            Top = 0,
            Right = clientWidth,
            Bottom = clientHeight,
        };

        if (!Win32.AdjustWindowRectEx(ref rect, style, false, exStyle))
        {
            return new OuterSize(clientWidth, clientHeight);
        }

        return new OuterSize(rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    private static List<RiftWindow> SelectWindows(List<RiftWindow> windows, Options options)
    {
        if (options.AllWindows)
        {
            return windows;
        }

        int targetIndex = Math.Clamp(options.WindowIndex ?? 1, 1, windows.Count);
        return new List<RiftWindow> { windows[targetIndex - 1] };
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

                case "--list":
                    options.ListOnly = true;
                    break;

                case "--all":
                    options.AllWindows = true;
                    break;

                case "--inspect":
                    options.Inspect = true;
                    break;

                case "--wait-ms":
                    if (!TryReadInt(args, ref i, out int waitMs, out error))
                    {
                        return false;
                    }

                    if (waitMs < 0)
                    {
                        error = "--wait-ms cannot be negative.";
                        return false;
                    }

                    options.WaitMs = waitMs;
                    break;

                case "--index":
                    if (!TryReadInt(args, ref i, out int indexValue, out error))
                    {
                        return false;
                    }

                    if (indexValue <= 0)
                    {
                        error = "--index must be 1 or greater.";
                        return false;
                    }

                    options.WindowIndex = indexValue;
                    break;

                case "--left":
                    if (!TryReadInt(args, ref i, out int leftValue, out error))
                    {
                        return false;
                    }

                    options.Left = leftValue;
                    break;

                case "--top":
                    if (!TryReadInt(args, ref i, out int topValue, out error))
                    {
                        return false;
                    }

                    options.Top = topValue;
                    break;

                case "--step-x":
                    if (!TryReadInt(args, ref i, out int stepX, out error))
                    {
                        return false;
                    }

                    options.StepX = stepX;
                    break;

                case "--step-y":
                    if (!TryReadInt(args, ref i, out int stepY, out error))
                    {
                        return false;
                    }

                    options.StepY = stepY;
                    break;

                case "--width":
                    if (!TryReadInt(args, ref i, out int widthValue, out error))
                    {
                        return false;
                    }

                    options.Width = widthValue;
                    break;

                case "--height":
                    if (!TryReadInt(args, ref i, out int heightValue, out error))
                    {
                        return false;
                    }

                    options.Height = heightValue;
                    break;

                case "--size":
                    if (!TryReadString(args, ref i, out string? sizeValue, out error))
                    {
                        return false;
                    }

                    if (!TryParseResolution(sizeValue!, out int sizeWidth, out int sizeHeight))
                    {
                        error = $"Could not parse resolution '{sizeValue}'. Expected WIDTHxHEIGHT, for example 640x360.";
                        return false;
                    }

                    options.Width = sizeWidth;
                    options.Height = sizeHeight;
                    break;

                case "--preset":
                    if (!TryReadString(args, ref i, out string? presetValue, out error))
                    {
                        return false;
                    }

                    if (!TryResolvePreset(presetValue!, out var preset))
                    {
                        error = $"Unknown preset '{presetValue}'.";
                        return false;
                    }

                    options.PresetName = preset.Name;
                    options.Width = preset.Width;
                    options.Height = preset.Height;
                    break;

                case "--scale":
                    if (!TryReadString(args, ref i, out string? scaleText, out error))
                    {
                        return false;
                    }

                    if (!double.TryParse(scaleText, NumberStyles.Float, CultureInfo.InvariantCulture, out double scaleValue) || scaleValue <= 0)
                    {
                        error = $"Invalid --scale value '{scaleText}'.";
                        return false;
                    }

                    options.Scale = scaleValue;
                    break;

                default:
                    error = $"Unknown option '{arg}'.";
                    return false;
            }
        }

        if (options.Scale.HasValue && (options.Width.HasValue || options.Height.HasValue || options.PresetName is not null))
        {
            error = "--scale cannot be combined with --preset, --size, --width, or --height.";
            return false;
        }

        if (options.Width.HasValue ^ options.Height.HasValue)
        {
            error = "--width and --height must be supplied together.";
            return false;
        }

        if (options.Width.HasValue && (options.Width <= 0 || options.Height <= 0))
        {
            error = "Target resolution must be greater than zero.";
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

    private static bool TryResolvePreset(string presetName, out ResolutionPreset preset)
    {
        if (Presets.TryGetValue(presetName, out preset))
        {
            return true;
        }

        if (TryParseResolution(presetName, out int width, out int height))
        {
            preset = new ResolutionPreset($"{width}x{height}", width, height);
            return true;
        }

        preset = default;
        return false;
    }

    private static bool TryParseResolution(string text, out int width, out int height)
    {
        width = 0;
        height = 0;

        string[] parts = text.Split('x', 'X', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        return int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out width)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out height)
            && width > 0
            && height > 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  LeaderWindowResizer.exe --preset 640x360 --inspect");
        Console.WriteLine("  LeaderWindowResizer.exe --size 320x180 --left 45 --top 108");
        Console.WriteLine("  LeaderWindowResizer.exe --scale 0.5 --inspect");
        Console.WriteLine("  LeaderWindowResizer.exe --all --preset 640x360 --left 45 --top 108 --step-x 24 --step-y 24");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --list                 List detected RIFT windows and exit if no resize target is given");
        Console.WriteLine("  --index N              Target the Nth detected RIFT window (default: 1)");
        Console.WriteLine("  --all                  Resize all detected RIFT windows");
        Console.WriteLine("  --preset NAME          Presets: 320x180, 485x309, 640x360, 1280x720, 180p, 360p, 720p");
        Console.WriteLine("  --size WxH             Custom client resolution");
        Console.WriteLine("  --width N --height N   Custom client resolution");
        Console.WriteLine("  --scale F              Dynamic scale from current live client size, e.g. 0.5");
        Console.WriteLine("  --left N --top N       Absolute window position for the first resized window");
        Console.WriteLine("  --step-x N --step-y N  Per-window position offset when resizing multiple windows");
        Console.WriteLine("  --inspect              Run Leader strip decode immediately after resizing");
        Console.WriteLine("  --wait-ms N            Delay before post-resize inspection (default: 1000)");
        Console.WriteLine("  --help                 Show this help");
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

    private static WindowSnapshot GetWindowSnapshot(IntPtr hwnd)
    {
        bool haveWindowRect = Win32.GetWindowRect(hwnd, out var windowRect);
        bool haveClientRect = Win32.GetClientRect(hwnd, out var clientRect);
        var point = new Win32.Point();
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

    private sealed class Options
    {
        public bool ShowHelp { get; set; }
        public bool ListOnly { get; set; }
        public bool AllWindows { get; set; }
        public bool Inspect { get; set; }
        public int WaitMs { get; set; } = 1000;
        public int? WindowIndex { get; set; }
        public int? Left { get; set; }
        public int? Top { get; set; }
        public int StepX { get; set; }
        public int StepY { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public double? Scale { get; set; }
        public string? PresetName { get; set; }

        public bool HasResizeIntent => Scale.HasValue || (Width.HasValue && Height.HasValue);
    }

    private sealed class RiftWindow
    {
        public required int ProcessId { get; init; }
        public required string Title { get; init; }
        public required IntPtr Hwnd { get; init; }
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

    private readonly record struct ResolutionPreset(string Name, int Width, int Height);
    private readonly record struct TargetClientSize(int Width, int Height);
    private readonly record struct OuterSize(int Width, int Height);
    private readonly record struct ResizeResult(WindowSnapshot Before, WindowSnapshot After, bool ExactMatch);

    private static class Win32
    {
        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const int SW_RESTORE = 9;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Point
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool AdjustWindowRectEx(ref Rect rect, int style, bool hasMenu, int exStyle);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ClientToScreen(IntPtr hwnd, ref Point point);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetClientRect(IntPtr hwnd, out Rect rect);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool IsIconic(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ShowWindow(IntPtr hwnd, int command);
    }
}
