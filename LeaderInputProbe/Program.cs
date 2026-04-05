using System.Globalization;
using LeaderDecoder.Services;

namespace LeaderInputProbe;

internal static class Program
{
    private const int DefaultHoldMs = 250;
    private const int TapHoldMs = 60;

    private static readonly Dictionary<string, InputEngine.RiftKey> KeyMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["W"] = InputEngine.RiftKey.W,
            ["FORWARD"] = InputEngine.RiftKey.W,
            ["A"] = InputEngine.RiftKey.A,
            ["LEFT"] = InputEngine.RiftKey.A,
            ["S"] = InputEngine.RiftKey.S,
            ["BACK"] = InputEngine.RiftKey.S,
            ["BACKWARD"] = InputEngine.RiftKey.S,
            ["D"] = InputEngine.RiftKey.D,
            ["RIGHT"] = InputEngine.RiftKey.D,
            ["F"] = InputEngine.RiftKey.F,
            ["INTERACT"] = InputEngine.RiftKey.F,
            ["ASSIST"] = InputEngine.RiftKey.F,
            ["M"] = InputEngine.RiftKey.M,
            ["MOUNT"] = InputEngine.RiftKey.M,
            ["SPACE"] = InputEngine.RiftKey.Space,
            ["SPACEBAR"] = InputEngine.RiftKey.Space,
            ["JUMP"] = InputEngine.RiftKey.Space,
            ["1"] = InputEngine.RiftKey.Num1,
            ["NUM1"] = InputEngine.RiftKey.Num1,
            ["2"] = InputEngine.RiftKey.Num2,
            ["NUM2"] = InputEngine.RiftKey.Num2,
            ["3"] = InputEngine.RiftKey.Num3,
            ["NUM3"] = InputEngine.RiftKey.Num3,
            ["4"] = InputEngine.RiftKey.Num4,
            ["NUM4"] = InputEngine.RiftKey.Num4,
            ["5"] = InputEngine.RiftKey.Num5,
            ["NUM5"] = InputEngine.RiftKey.Num5,
        };

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

            Console.Title = "Leader Input Probe";
            Console.WriteLine("============================================================");
            Console.WriteLine("Leader Input Probe");
            Console.WriteLine("Sends deterministic background key probes to selected RIFT windows.");
            Console.WriteLine("============================================================");

            var allWindows = RiftWindowService.FindRiftWindows();
            var filteredWindows = RiftWindowService.FilterWindows(allWindows, BuildFilter(options));

            Console.WriteLine($"Detected RIFT windows: {allWindows.Count}");
            if (HasExplicitFilter(options))
            {
                Console.WriteLine($"Filtered RIFT windows: {filteredWindows.Count}");
            }

            PrintWindowList(filteredWindows);

            if (filteredWindows.Count == 0)
            {
                Console.Error.WriteLine("No matching live RIFT windows with a main handle were found.");
                return 1;
            }

            if (options.ListOnly && !options.HasProbeIntent)
            {
                return 0;
            }

            if (!options.HasProbeIntent)
            {
                Console.WriteLine();
                Console.WriteLine("No probe action was supplied.");
                PrintUsage();
                return 1;
            }

            if (!TryResolveKey(options.KeyName!, out var key, out error))
            {
                Console.Error.WriteLine(error);
                return 1;
            }

            var selected = SelectWindows(filteredWindows, options, out error);
            if (!string.IsNullOrWhiteSpace(error))
            {
                Console.Error.WriteLine(error);
                return 1;
            }

            int holdMs = options.Tap ? TapHoldMs : options.HoldMs;
            var input = new InputEngine(diag);
            CaptureEngine? capture = options.Inspect ? new CaptureEngine(diag) : null;

            Console.WriteLine();
            Console.WriteLine(BuildIntentSummary(options, key, selected.Count, holdMs));

            for (int index = 0; index < selected.Count; index++)
            {
                var window = selected[index];
                Console.WriteLine();
                Console.WriteLine($"[{index + 1}] {RiftWindowService.FormatIdentity(window)}");
                Console.WriteLine($"    Selectors: {RiftWindowService.FormatSelectorHints(window)}");

                if (capture is not null)
                {
                    PrintInspection("Before", capture, window.Hwnd);
                }

                for (int attempt = 0; attempt < options.Repeat; attempt++)
                {
                    Console.WriteLine(
                        $"    Probe {attempt + 1}/{options.Repeat}: key={options.KeyName} duration={holdMs}ms");
                    ExecutePress(input, window.Hwnd, key, holdMs);

                    if (attempt < options.Repeat - 1 && options.IntervalMs > 0)
                    {
                        Thread.Sleep(options.IntervalMs);
                    }
                }

                if (capture is not null)
                {
                    if (options.WaitMs > 0)
                    {
                        Thread.Sleep(options.WaitMs);
                    }

                    PrintInspection("After", capture, window.Hwnd);
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            diag.LogToolFailure(
                source: "LeaderInputProbe",
                operation: "UnhandledException",
                detail: "Input probe crashed.",
                context: string.Join(" ", args),
                ex: ex,
                dedupeKey: "input-probe-unhandled",
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
        if (windows.Count == 0)
        {
            return;
        }

        for (int index = 0; index < windows.Count; index++)
        {
            var window = windows[index];
            var snapshot = RiftWindowService.GetWindowSnapshot(window.Hwnd);
            Console.WriteLine(
                $"[{index + 1}] {RiftWindowService.FormatIdentity(window)} | " +
                $"Client={snapshot.ClientWidth ?? 0}x{snapshot.ClientHeight ?? 0} | " +
                $"Minimized={snapshot.IsMinimized}");
            Console.WriteLine($"    Selectors: {RiftWindowService.FormatSelectorHints(window)}");
        }
    }

    private static List<RiftWindowInfo> SelectWindows(List<RiftWindowInfo> windows, Options options, out string? error)
    {
        error = null;

        if (options.AllWindows || options.ProcessIds is { Length: > 0 } || options.Hwnds is { Length: > 0 })
        {
            return windows;
        }

        if (options.WindowIndex.HasValue)
        {
            int targetIndex = options.WindowIndex.Value;
            if (targetIndex <= 0 || targetIndex > windows.Count)
            {
                error = $"--index {targetIndex} is out of range for {windows.Count} filtered window(s).";
                return new List<RiftWindowInfo>();
            }

            return new List<RiftWindowInfo> { windows[targetIndex - 1] };
        }

        if (windows.Count == 1)
        {
            return new List<RiftWindowInfo> { windows[0] };
        }

        error = "Multiple RIFT windows matched. Use --pid, --hwnd, --index, --all, --pids, or --hwnds to select explicit target(s).";
        return new List<RiftWindowInfo>();
    }

    private static string BuildIntentSummary(Options options, InputEngine.RiftKey key, int selectedCount, int holdMs)
    {
        var parts = new List<string>
        {
            $"Key={key}",
            options.Tap ? "Mode=Tap" : "Mode=Press",
            $"HoldMs={holdMs}",
            selectedCount == 1 ? "Target=1 window" : $"Target={selectedCount} windows"
        };

        if (options.Repeat > 1)
        {
            parts.Add($"Repeat={options.Repeat}");
            parts.Add($"IntervalMs={options.IntervalMs}");
        }

        if (options.ProcessId.HasValue)
        {
            parts.Add($"PID={options.ProcessId.Value}");
        }
        else if (options.ProcessIds is { Length: > 0 })
        {
            parts.Add($"PIDs={string.Join(",", options.ProcessIds)}");
        }

        if (options.Hwnd.HasValue)
        {
            parts.Add($"HWND={RiftWindowService.FormatHwnd(options.Hwnd.Value)}");
        }
        else if (options.Hwnds is { Length: > 0 })
        {
            parts.Add($"HWNDs={string.Join(",", options.Hwnds.Select(RiftWindowService.FormatHwnd))}");
        }

        if (!string.IsNullOrWhiteSpace(options.TitleContains))
        {
            parts.Add($"TitleContains={options.TitleContains}");
        }

        if (options.WindowIndex.HasValue)
        {
            parts.Add($"Index={options.WindowIndex.Value}");
        }

        if (options.Inspect)
        {
            parts.Add("Inspect=True");
            parts.Add($"WaitMs={options.WaitMs}");
        }

        return string.Join(" | ", parts);
    }

    private static void PrintInspection(string label, CaptureEngine capture, IntPtr hwnd)
    {
        using var bmp = capture.CaptureRegion(hwnd, StripInspector.StripWidth, StripInspector.StripHeight);
        var analysis = StripInspector.Analyze(bmp);

        Console.WriteLine($"    {label}:");
        foreach (string line in StripInspector.FormatStateSummary(analysis.State).Split(Environment.NewLine))
        {
            Console.WriteLine($"      {line}");
        }
    }

    private static void ExecutePress(InputEngine input, IntPtr hwnd, InputEngine.RiftKey key, int holdMs)
    {
        input.SendKeyDown(hwnd, key);
        Thread.Sleep(holdMs);
        input.SendKeyUp(hwnd, key);
    }

    private static bool TryResolveKey(string value, out InputEngine.RiftKey key, out string? error)
    {
        key = default;
        error = null;

        if (KeyMap.TryGetValue(value.Trim(), out key))
        {
            return true;
        }

        error = $"Unknown key '{value}'. Supported keys: {string.Join(", ", KeyMap.Keys.OrderBy(static k => k, StringComparer.OrdinalIgnoreCase))}";
        return false;
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

                case "--tap":
                    options.Tap = true;
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

                case "--hold-ms":
                    if (!TryReadInt(args, ref i, out int holdMs, out error))
                    {
                        return false;
                    }

                    if (holdMs <= 0)
                    {
                        error = "--hold-ms must be greater than zero.";
                        return false;
                    }

                    options.HoldMs = holdMs;
                    break;

                case "--repeat":
                    if (!TryReadInt(args, ref i, out int repeat, out error))
                    {
                        return false;
                    }

                    if (repeat <= 0)
                    {
                        error = "--repeat must be 1 or greater.";
                        return false;
                    }

                    options.Repeat = repeat;
                    break;

                case "--interval-ms":
                    if (!TryReadInt(args, ref i, out int intervalMs, out error))
                    {
                        return false;
                    }

                    if (intervalMs < 0)
                    {
                        error = "--interval-ms cannot be negative.";
                        return false;
                    }

                    options.IntervalMs = intervalMs;
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

                case "--key":
                    if (!TryReadString(args, ref i, out string? keyName, out error))
                    {
                        return false;
                    }

                    options.KeyName = keyName;
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

        if (options.Tap && options.HoldMs != DefaultHoldMs)
        {
            error = "--tap cannot be combined with --hold-ms.";
            return false;
        }

        if (!options.ListOnly && string.IsNullOrWhiteSpace(options.KeyName))
        {
            error = "--key is required unless you only use --list.";
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
        Console.WriteLine("  LeaderInputProbe.exe --list");
        Console.WriteLine("  LeaderInputProbe.exe --pid 127928 --key W --hold-ms 3000 --inspect");
        Console.WriteLine("  LeaderInputProbe.exe --hwnd 0x351350 --key SPACE --tap");
        Console.WriteLine("  LeaderInputProbe.exe --pids 127928,133228 --key F --tap");
        Console.WriteLine("  LeaderInputProbe.exe --title-contains RIFT --index 2 --key A --hold-ms 500");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --list                 List detected or filtered RIFT windows and exit if no probe action is given");
        Console.WriteLine("  --index N              Target the Nth filtered RIFT window");
        Console.WriteLine("  --all                  Probe all filtered RIFT windows");
        Console.WriteLine("  --pid N                Filter to a specific process id");
        Console.WriteLine("  --pids N1,N2           Filter to multiple process ids in the given order");
        Console.WriteLine("  --hwnd HEX             Filter to a specific HWND, e.g. 0x351350");
        Console.WriteLine("  --hwnds A,B            Filter to multiple HWNDs in the given order");
        Console.WriteLine("  --title-contains TEXT  Filter to window titles containing TEXT");
        Console.WriteLine("  --key NAME             Required probe key: W, A, S, D, F, M, SPACE, 1-5");
        Console.WriteLine("  --tap                  Use a 60 ms press instead of --hold-ms");
        Console.WriteLine("  --hold-ms N            Press duration in milliseconds (default: 250)");
        Console.WriteLine("  --repeat N             Repeat the probe N times (default: 1)");
        Console.WriteLine("  --interval-ms N        Delay between repeated probes (default: 250)");
        Console.WriteLine("  --inspect              Capture and decode the telemetry strip before and after the probe");
        Console.WriteLine("  --wait-ms N            Delay before post-probe inspection (default: 250)");
        Console.WriteLine("  --help                 Show this help");
    }

    private sealed class Options
    {
        public bool ShowHelp { get; set; }
        public bool ListOnly { get; set; }
        public bool AllWindows { get; set; }
        public bool Inspect { get; set; }
        public bool Tap { get; set; }
        public int WaitMs { get; set; } = 250;
        public int HoldMs { get; set; } = DefaultHoldMs;
        public int Repeat { get; set; } = 1;
        public int IntervalMs { get; set; } = 250;
        public int? WindowIndex { get; set; }
        public int? ProcessId { get; set; }
        public int[]? ProcessIds { get; set; }
        public IntPtr? Hwnd { get; set; }
        public IntPtr[]? Hwnds { get; set; }
        public string? TitleContains { get; set; }
        public string? KeyName { get; set; }
        public bool HasProbeIntent => !string.IsNullOrWhiteSpace(KeyName);
    }
}
