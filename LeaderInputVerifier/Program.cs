using System.Globalization;
using System.IO;
using System.Text.Json;
using LeaderDecoder.Models;
using LeaderDecoder.Services;

namespace LeaderInputVerifier;

internal static class Program
{
    private const int DefaultHoldMs = 120;
    private const int DefaultWaitMs = 250;
    private const float MovementThreshold = 0.12f;
    private const string ResultLogName = "input_verifier_results.csv";

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

        Console.Title = "Leader Input Verifier";
        Console.WriteLine("============================================================");
        Console.WriteLine("Leader Input Verifier");
        Console.WriteLine("Verifies whether configured controller actions produce detectable telemetry changes.");
        Console.WriteLine("============================================================");

        var settings = LoadSettings(options.SettingsPath, out string settingsSource);
        var capture = new CaptureEngine();
        var input = new InputEngine();
        string debugDir = Path.Combine(Environment.CurrentDirectory, "debug");
        string resultLogPath = Path.Combine(debugDir, ResultLogName);
        EnsureResultLog(debugDir, resultLogPath);

        var allWindows = RiftWindowService.FindRiftWindows();
        var filteredWindows = RiftWindowService.FilterWindows(allWindows, BuildFilter(options));

        Console.WriteLine($"Detected RIFT windows: {allWindows.Count}");
        Console.WriteLine($"Settings source: {settingsSource}");
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

        if (options.ListOnly && !options.HasActionIntent)
        {
            return 0;
        }

        if (!options.HasActionIntent)
        {
            Console.WriteLine();
            Console.WriteLine("No action was supplied.");
            PrintUsage();
            return 1;
        }

        var selectedWindows = SelectWindows(filteredWindows, options, out error);
        if (!string.IsNullOrWhiteSpace(error))
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        var actions = BuildActionPlan(options, settings, out error);
        if (!string.IsNullOrWhiteSpace(error))
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine(BuildIntentSummary(options, selectedWindows.Count, actions, settingsSource));

        bool anyFailure = false;
        int rowIndex = 0;

        foreach (var window in selectedWindows)
        {
            Console.WriteLine();
            Console.WriteLine($"[{window.ProcessId}] {RiftWindowService.FormatIdentity(window)}");
            Console.WriteLine($"    Selectors: {RiftWindowService.FormatSelectorHints(window)}");

            foreach (var action in actions)
            {
                rowIndex++;
                var before = CaptureState(capture, window.Hwnd);
                ExecuteAction(input, window.Hwnd, action, options.HoldMs);

                if (options.WaitMs > 0)
                {
                    Thread.Sleep(options.WaitMs);
                }

                var after = CaptureState(capture, window.Hwnd);
                var result = Classify(before, after, action, MovementThreshold);

                AppendResult(resultLogPath, new ResultRow
                {
                    Timestamp = DateTime.Now,
                    WindowLabel = RiftWindowService.FormatCompactIdentity(window),
                    ActionLabel = action.Label,
                    ScanCode = action.ScanCode,
                    BeforeValid = before.State.IsValid,
                    AfterValid = after.State.IsValid,
                    BeforeX = before.State.CoordX,
                    BeforeZ = before.State.CoordZ,
                    AfterX = after.State.CoordX,
                    AfterZ = after.State.CoordZ,
                    Delta2D = result.Delta2D,
                    Status = result.Status,
                    Notes = result.Notes,
                });

                Console.WriteLine(
                    $"    {rowIndex,2}. {action.Label,-10} | {result.Status,-15} | " +
                    $"Δ={result.Delta2D,5:F2} | Before={FormatStateShort(before)} | After={FormatStateShort(after)}");

                if (result.Status == "InvalidStrip")
                {
                    anyFailure = true;
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Result log: {resultLogPath}");
        return anyFailure ? 1 : 0;
    }

    private static CaptureSnapshot CaptureState(CaptureEngine capture, IntPtr hwnd)
    {
        using var bmp = capture.CaptureRegion(hwnd, StripInspector.StripWidth, StripInspector.StripHeight);
        var analysis = StripInspector.Analyze(bmp);
        return new CaptureSnapshot
        {
            Analysis = analysis,
        };
    }

    private static void ExecuteAction(InputEngine input, IntPtr hwnd, VerifierAction action, int holdMs)
    {
        input.TapScanCode(hwnd, action.ScanCode, holdMs);
    }

    private static ActionResult Classify(CaptureSnapshot before, CaptureSnapshot after, VerifierAction action, float movementThreshold)
    {
        if (!before.State.IsValid || !after.State.IsValid)
        {
            return new ActionResult
            {
                Delta2D = null,
                Status = "InvalidStrip",
                Notes = !before.State.IsValid && !after.State.IsValid
                    ? "before_and_after_invalid"
                    : !before.State.IsValid
                        ? "before_invalid"
                        : "after_invalid",
            };
        }

        float dx = after.State.CoordX - before.State.CoordX;
        float dz = after.State.CoordZ - before.State.CoordZ;
        float delta2D = MathF.Sqrt(dx * dx + dz * dz);

        if (delta2D >= movementThreshold)
        {
            return new ActionResult
            {
                Delta2D = delta2D,
                Status = "MovementDetected",
                Notes = action.IsMovementAction ? "expected_movement" : "unexpected_movement",
            };
        }

        return new ActionResult
        {
            Delta2D = delta2D,
            Status = "NoChange",
            Notes = action.IsMovementAction ? "no_detectable_movement" : "expected_no_movement",
        };
    }

    private static string FormatStateShort(CaptureSnapshot snapshot)
    {
        return snapshot.State.IsValid
            ? $"ok X={snapshot.State.CoordX:F1} Z={snapshot.State.CoordZ:F1}"
            : "invalid";
    }

    private static void AppendResult(string path, ResultRow row)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(fullPath))
        {
            File.WriteAllText(fullPath, "Timestamp,Window,Action,ScanCode,BeforeValid,AfterValid,BeforeX,BeforeZ,AfterX,AfterZ,Delta2D,Status,Notes\n");
        }

        string line = string.Join(",",
            Csv(row.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)),
            Csv(row.WindowLabel),
            Csv(row.ActionLabel),
            Csv(row.ScanCode.ToString(CultureInfo.InvariantCulture)),
            Csv(Bool(row.BeforeValid)),
            Csv(Bool(row.AfterValid)),
            Csv(Float(row.BeforeX)),
            Csv(Float(row.BeforeZ)),
            Csv(Float(row.AfterX)),
            Csv(Float(row.AfterZ)),
            Csv(Float(row.Delta2D)),
            Csv(row.Status),
            Csv(row.Notes));

        File.AppendAllText(fullPath, line + Environment.NewLine);
    }

    private static string BuildIntentSummary(Options options, int selectedWindowCount, IReadOnlyList<VerifierAction> actions, string settingsSource)
    {
        var parts = new List<string>
        {
            selectedWindowCount == 1 ? "Target=1 window" : $"Target={selectedWindowCount} windows",
            actions.Count == 1 ? $"Action={actions[0].Label}" : $"Sequence={string.Join(">", actions.Select(action => action.Label))}",
            $"HoldMs={options.HoldMs}",
            $"WaitMs={options.WaitMs}",
            $"MovementThreshold={MovementThreshold.ToString("0.###", CultureInfo.InvariantCulture)}",
            $"Settings={settingsSource}",
        };

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

        if (options.Index.HasValue)
        {
            parts.Add($"Index={options.Index.Value}");
        }

        if (!string.IsNullOrWhiteSpace(options.SettingsPath))
        {
            parts.Add($"SettingsPath={options.SettingsPath}");
        }

        return string.Join(" | ", parts);
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

        if (options.Index.HasValue)
        {
            int index = options.Index.Value;
            if (index <= 0 || index > windows.Count)
            {
                error = $"--index {index} is out of range for {windows.Count} filtered window(s).";
                return new List<RiftWindowInfo>();
            }

            return new List<RiftWindowInfo> { windows[index - 1] };
        }

        if (windows.Count == 1)
        {
            return new List<RiftWindowInfo> { windows[0] };
        }

        error = "Multiple RIFT windows matched. Use --pid, --hwnd, --index, --all, --pids, or --hwnds to select explicit target(s).";
        return new List<RiftWindowInfo>();
    }

    private static IReadOnlyList<VerifierAction> BuildActionPlan(Options options, BridgeSettings settings, out string? error)
    {
        error = null;

        if (!string.IsNullOrWhiteSpace(options.ActionName))
        {
            if (!TryResolveAction(options.ActionName, settings, out var action, out error))
            {
                return Array.Empty<VerifierAction>();
            }

            return new[] { action };
        }

        if (!string.IsNullOrWhiteSpace(options.MovementSequence))
        {
            var tokens = options.MovementSequence
                .Split(new[] { ',', ';', '+', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var actions = new List<VerifierAction>(tokens.Length);
            foreach (string token in tokens)
            {
                if (!TryResolveAction(token, settings, out var action, out error))
                {
                    return Array.Empty<VerifierAction>();
                }

                actions.Add(action);
            }

            if (actions.Count == 0)
            {
                error = "No valid actions were found in --movement-sequence.";
                return Array.Empty<VerifierAction>();
            }

            return actions;
        }

        error = "Either --action or --movement-sequence must be supplied.";
        return Array.Empty<VerifierAction>();
    }

    private static bool TryResolveAction(string value, BridgeSettings settings, out VerifierAction action, out string? error)
    {
        action = default!;
        error = null;

        string normalized = value.Trim().ToLowerInvariant();
        byte scanCode;
        bool isMovementAction;

        switch (normalized)
        {
            case "forward":
                scanCode = settings.KeyForward;
                isMovementAction = true;
                break;
            case "backward":
            case "back":
                scanCode = settings.KeyBack;
                isMovementAction = true;
                break;
            case "left":
            case "strafeleft":
                scanCode = settings.KeyLeft;
                isMovementAction = true;
                break;
            case "right":
            case "straferight":
                scanCode = settings.KeyRight;
                isMovementAction = true;
                break;
            case "jump":
                scanCode = settings.KeyJump;
                isMovementAction = true;
                break;
            case "mount":
                scanCode = settings.KeyMount;
                isMovementAction = false;
                break;
            case "interact":
            case "assist":
                scanCode = settings.KeyInteract;
                isMovementAction = false;
                break;
            default:
                error = $"Unknown action '{value}'. Supported actions: forward, backward, left, right, jump, mount, interact.";
                return false;
        }

        if (scanCode == 0)
        {
            error = $"Action '{value}' resolved to scan code 0 in the current settings.";
            return false;
        }

        action = new VerifierAction
        {
            Label = normalized,
            ScanCode = scanCode,
            IsMovementAction = isMovementAction,
        };

        return true;
    }

    private static BridgeSettings LoadSettings(string? explicitPath, out string source)
    {
        var candidatePaths = new List<string>();

        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            candidatePaths.Add(Path.GetFullPath(explicitPath));
        }

        string defaultPath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "LeaderDecoder", "settings.json"));
        candidatePaths.Add(defaultPath);

        foreach (string path in candidatePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                string json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<BridgeSettings>(json) ?? new BridgeSettings();
                source = path;
                return settings;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SETTINGS] Failed to load '{path}': {ex.Message}. Falling back to defaults.");
            }
        }

        source = "defaults";
        return new BridgeSettings();
    }

    private static void EnsureResultLog(string debugDir, string resultLogPath)
    {
        Directory.CreateDirectory(Path.GetFullPath(debugDir));
    }

    private static string Csv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Float(float? value) => value.HasValue ? value.Value.ToString("F3", CultureInfo.InvariantCulture) : string.Empty;

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  LeaderInputVerifier.exe --list");
        Console.WriteLine("  LeaderInputVerifier.exe --pid 127928 --action forward --wait-ms 250");
        Console.WriteLine("  LeaderInputVerifier.exe --hwnd 0x351350 --movement-sequence forward,left,right");
        Console.WriteLine("  LeaderInputVerifier.exe --pids 127928,133228 --action interact --settings ..\\LeaderDecoder\\settings.json");
        Console.WriteLine("  LeaderInputVerifier.exe --title-contains RIFT --index 2 --action mount");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --list                 List detected or filtered RIFT windows and exit if no action is given");
        Console.WriteLine("  --index N              Target the Nth filtered RIFT window");
        Console.WriteLine("  --all                  Run the selected action(s) on all filtered RIFT windows");
        Console.WriteLine("  --pid N                Filter to a specific process id");
        Console.WriteLine("  --pids N1,N2           Filter to multiple process ids in the given order");
        Console.WriteLine("  --hwnd HEX             Filter to a specific HWND, e.g. 0x351350");
        Console.WriteLine("  --hwnds A,B            Filter to multiple HWNDs in the given order");
        Console.WriteLine("  --title-contains TEXT  Filter to window titles containing TEXT");
        Console.WriteLine("  --action NAME          forward, backward, left, right, jump, mount, interact");
        Console.WriteLine("  --movement-sequence    Comma/semicolon/plus separated list of actions");
        Console.WriteLine("  --settings PATH        Explicit settings.json path");
        Console.WriteLine("  --hold-ms N           Tap duration in milliseconds (default: 120)");
        Console.WriteLine("  --wait-ms N           Delay after action before post-capture (default: 250)");
        Console.WriteLine("  --help                Show this help");
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
                case "--index":
                    if (!TryReadInt(args, ref i, out int index, out error))
                    {
                        return false;
                    }
                    options.Index = index;
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
                        error = $"Invalid --pids value '{pidListText}'. Expected comma-separated integers.";
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
                        error = $"Invalid --hwnd value '{hwndText}'.";
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
                        error = $"Invalid --hwnds value '{hwndListText}'.";
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
                case "--action":
                    if (!TryReadString(args, ref i, out string? actionName, out error))
                    {
                        return false;
                    }
                    options.ActionName = actionName;
                    break;
                case "--movement-sequence":
                    if (!TryReadString(args, ref i, out string? sequence, out error))
                    {
                        return false;
                    }
                    options.MovementSequence = sequence;
                    break;
                case "--settings":
                    if (!TryReadString(args, ref i, out string? settingsPath, out error))
                    {
                        return false;
                    }
                    options.SettingsPath = settingsPath;
                    break;
                case "--hold-ms":
                    if (!TryReadInt(args, ref i, out int holdMs, out error))
                    {
                        return false;
                    }
                    options.HoldMs = Math.Max(1, holdMs);
                    break;
                case "--wait-ms":
                    if (!TryReadInt(args, ref i, out int waitMs, out error))
                    {
                        return false;
                    }
                    options.WaitMs = Math.Max(0, waitMs);
                    break;
                default:
                    error = $"Unknown option '{arg}'.";
                    return false;
            }
        }

        if (options.HoldMs <= 0)
        {
            options.HoldMs = DefaultHoldMs;
        }

        if (options.WaitMs < 0)
        {
            options.WaitMs = DefaultWaitMs;
        }

        return true;
    }

    private static bool TryReadString(string[] args, ref int i, out string? value, out string? error)
    {
        value = null;
        error = null;

        if (i + 1 >= args.Length)
        {
            error = $"Missing value for {args[i]}.";
            return false;
        }

        value = args[++i];
        return true;
    }

    private static bool TryReadInt(string[] args, ref int i, out int value, out string? error)
    {
        value = 0;
        error = null;

        if (!TryReadString(args, ref i, out string? text, out error))
        {
            return false;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            error = $"Invalid integer value '{text}'.";
            return false;
        }

        return true;
    }

    private sealed class Options
    {
        public bool ShowHelp { get; set; }
        public bool ListOnly { get; set; }
        public bool AllWindows { get; set; }
        public int? Index { get; set; }
        public int? ProcessId { get; set; }
        public int[]? ProcessIds { get; set; }
        public IntPtr? Hwnd { get; set; }
        public IntPtr[]? Hwnds { get; set; }
        public string? TitleContains { get; set; }
        public string? ActionName { get; set; }
        public string? MovementSequence { get; set; }
        public string? SettingsPath { get; set; }
        public int HoldMs { get; set; } = DefaultHoldMs;
        public int WaitMs { get; set; } = DefaultWaitMs;

        public bool HasActionIntent => !string.IsNullOrWhiteSpace(ActionName) || !string.IsNullOrWhiteSpace(MovementSequence);
    }

    private sealed class VerifierAction
    {
        public required string Label { get; init; }
        public required byte ScanCode { get; init; }
        public required bool IsMovementAction { get; init; }
    }

    private sealed class CaptureSnapshot
    {
        public required StripAnalysis Analysis { get; init; }
        public GameState State => Analysis.State;
    }

    private sealed class ActionResult
    {
        public float? Delta2D { get; init; }
        public required string Status { get; init; }
        public required string Notes { get; init; }
    }

    private sealed class ResultRow
    {
        public required DateTime Timestamp { get; init; }
        public required string WindowLabel { get; init; }
        public required string ActionLabel { get; init; }
        public required byte ScanCode { get; init; }
        public required bool BeforeValid { get; init; }
        public required bool AfterValid { get; init; }
        public required float BeforeX { get; init; }
        public required float BeforeZ { get; init; }
        public required float AfterX { get; init; }
        public required float AfterZ { get; init; }
        public required float? Delta2D { get; init; }
        public required string Status { get; init; }
        public required string Notes { get; init; }
    }
}
