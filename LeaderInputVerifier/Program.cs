using System.Globalization;
using System.Text.Json;
using LeaderDecoder.Models;
using LeaderDecoder.Services;

namespace LeaderInputVerifier;

internal static class Program
{
    private const int DefaultHoldMs = 120;
    private const int DefaultWaitMs = 250;
    private const string DefaultSequence = "forward,backward,left,right";

    private static readonly Dictionary<string, Func<BridgeSettings, byte>> Actions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["forward"] = s => s.KeyForward,
            ["backward"] = s => s.KeyBack,
            ["back"] = s => s.KeyBack,
            ["left"] = s => s.KeyLeft,
            ["strafeleft"] = s => s.KeyLeft,
            ["right"] = s => s.KeyRight,
            ["straferight"] = s => s.KeyRight,
            ["jump"] = s => s.KeyJump,
            ["mount"] = s => s.KeyMount,
            ["interact"] = s => s.KeyInteract,
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

            if (options.Help)
            {
                PrintUsage();
                return 0;
            }

            string repoRoot = FindRepoRoot();
            BridgeSettings settings = LoadSettings(repoRoot, options.SettingsPath, diag);

            var capture = new CaptureEngine(diag);
            var input = new InputEngine(diag);

            var windows = RiftWindowService.FilterWindows(RiftWindowService.FindRiftWindows(), BuildFilter(options));
            Console.WriteLine("============================================================");
            Console.WriteLine("Leader Input Verifier");
            Console.WriteLine("============================================================");
            Console.WriteLine($"Detected RIFT windows: {windows.Count}");

            if (options.ListOnly)
            {
                PrintWindows(windows);
                return 0;
            }

            if (windows.Count == 0)
            {
                Console.Error.WriteLine("No matching live RIFT windows with a main handle were found.");
                return 1;
            }

            var selected = SelectWindows(windows, options, out error);
            if (!string.IsNullOrWhiteSpace(error))
            {
                Console.Error.WriteLine(error);
                return 1;
            }

            IReadOnlyList<string> actions = ResolveActions(options, out error);
            if (!string.IsNullOrWhiteSpace(error))
            {
                Console.Error.WriteLine(error);
                return 1;
            }

            string debugDir = Path.Combine(repoRoot, "LeaderInputVerifier", "debug");
            Directory.CreateDirectory(debugDir);
            string resultsPath = Path.Combine(debugDir, "input_verifier_results.csv");
            EnsureHeader(resultsPath);

            Console.WriteLine();
            Console.WriteLine($"Target={selected.Count} window(s) | Actions={string.Join("+", actions)} | HoldMs={options.HoldMs} | WaitMs={options.WaitMs}");
            Console.WriteLine($"Settings={options.SettingsPath ?? Path.Combine("..", "LeaderDecoder", "settings.json")}");
            Console.WriteLine($"Results={resultsPath}");

            int verified = 0;
            int changed = 0;
            int invalid = 0;

            foreach (string action in actions)
            {
                byte key = ResolveKey(settings, action);
                if (key == 0)
                {
                    Console.Error.WriteLine($"Action '{action}' resolved to scan code 0 and cannot be sent.");
                    return 1;
                }

                foreach (var window in selected)
                {
                    var before = Capture(capture, window.Hwnd);
                    if (!before.State.IsValid)
                    {
                        invalid++;
                    }

                    Console.WriteLine();
                    Console.WriteLine($"[{window.ProcessId}] {RiftWindowService.FormatCompactIdentity(window)}");
                    Console.WriteLine($"  Action: {action} | Key=0x{key:X2}");
                    Console.WriteLine($"  Before: {StripInspector.FormatStateSummary(before.State)}");

                    input.TapScanCode(window.Hwnd, key, options.HoldMs);
                    Thread.Sleep(options.WaitMs);

                    var after = Capture(capture, window.Hwnd);
                    if (!after.State.IsValid)
                    {
                        invalid++;
                    }

                    var classification = Classify(action, before.State, after.State, out float delta, out string notes);
                    if (classification is "movement_observed" or "state_changed")
                    {
                        changed++;
                    }

                    verified++;
                    Console.WriteLine($"  After:  {StripInspector.FormatStateSummary(after.State)}");
                    Console.WriteLine($"  Result: {classification} | ΔXY={delta:F2} | Notes={notes}");

                    AppendRow(resultsPath, new InputRow
                    {
                        Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                        Window = RiftWindowService.FormatIdentity(window),
                        Action = action,
                        Key = $"0x{key:X2}",
                        BeforeValid = before.State.IsValid,
                        AfterValid = after.State.IsValid,
                        BeforeX = before.State.CoordX,
                        BeforeY = before.State.CoordY,
                        BeforeZ = before.State.CoordZ,
                        AfterX = after.State.CoordX,
                        AfterY = after.State.CoordY,
                        AfterZ = after.State.CoordZ,
                        DistanceDelta = delta,
                        BeforeMoving = before.State.IsMoving,
                        AfterMoving = after.State.IsMoving,
                        BeforeMounted = before.State.IsMounted,
                        AfterMounted = after.State.IsMounted,
                        BeforeHasTarget = before.State.HasTarget,
                        AfterHasTarget = after.State.HasTarget,
                        Result = classification,
                        Notes = notes,
                    });
                }
            }

            Console.WriteLine();
            Console.WriteLine("============================================================");
            Console.WriteLine($"Verified steps: {verified}");
            Console.WriteLine($"Changed steps:   {changed}");
            Console.WriteLine($"Invalid strips:  {invalid}");
            Console.WriteLine("============================================================");
            return 0;
        }
        catch (Exception ex)
        {
            diag.LogToolFailure(
                source: "LeaderInputVerifier",
                operation: "UnhandledException",
                detail: "Input verifier crashed.",
                context: string.Join(" ", args),
                ex: ex,
                dedupeKey: "input-verifier-unhandled",
                throttleSeconds: 1.0);
            Console.Error.WriteLine($"Unhandled error: {ex.Message}");
            return 1;
        }
    }

    private static (StripAnalysis Analysis, GameState State) Capture(CaptureEngine capture, IntPtr hwnd)
    {
        using var bmp = capture.CaptureRegion(hwnd, StripInspector.StripWidth, StripInspector.StripHeight);
        var analysis = StripInspector.Analyze(bmp);
        return (analysis, analysis.State);
    }

    private static string Classify(string action, GameState before, GameState after, out float distanceDelta, out string notes)
    {
        distanceDelta = Distance(before, after);

        if (!before.IsValid || !after.IsValid)
        {
            notes = "telemetry strip was invalid before or after the action";
            return "invalid_strip";
        }

        bool movementAction = IsMovementAction(action);
        bool movementObserved = distanceDelta >= 0.10f || before.IsMoving != after.IsMoving;

        if (movementAction)
        {
            if (movementObserved)
            {
                notes = "position or moving-state changed after input";
                return "movement_observed";
            }

            notes = "no coordinate or moving-state change detected";
            return "no_movement_detected";
        }

        bool stateChanged = before.IsMounted != after.IsMounted || before.HasTarget != after.HasTarget || before.PlayerHP != after.PlayerHP;
        if (stateChanged)
        {
            notes = "mount/target/health state changed after input";
            return "state_changed";
        }

        notes = "no visible state change detected";
        return "no_state_change";
    }

    private static bool IsMovementAction(string action)
    {
        return action.Equals("forward", StringComparison.OrdinalIgnoreCase)
            || action.Equals("backward", StringComparison.OrdinalIgnoreCase)
            || action.Equals("back", StringComparison.OrdinalIgnoreCase)
            || action.Equals("left", StringComparison.OrdinalIgnoreCase)
            || action.Equals("strafeleft", StringComparison.OrdinalIgnoreCase)
            || action.Equals("right", StringComparison.OrdinalIgnoreCase)
            || action.Equals("straferight", StringComparison.OrdinalIgnoreCase);
    }

    private static float Distance(GameState a, GameState b)
    {
        float dx = a.CoordX - b.CoordX;
        float dz = a.CoordZ - b.CoordZ;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    private static void AppendRow(string path, InputRow row)
    {
        string line = string.Join(",",
            Csv(row.Timestamp),
            Csv(row.Window),
            Csv(row.Action),
            Csv(row.Key),
            Csv(Bool(row.BeforeValid)),
            Csv(Bool(row.AfterValid)),
            Csv(row.BeforeX.ToString("F2", CultureInfo.InvariantCulture)),
            Csv(row.BeforeY.ToString("F2", CultureInfo.InvariantCulture)),
            Csv(row.BeforeZ.ToString("F2", CultureInfo.InvariantCulture)),
            Csv(row.AfterX.ToString("F2", CultureInfo.InvariantCulture)),
            Csv(row.AfterY.ToString("F2", CultureInfo.InvariantCulture)),
            Csv(row.AfterZ.ToString("F2", CultureInfo.InvariantCulture)),
            Csv(row.DistanceDelta.ToString("F2", CultureInfo.InvariantCulture)),
            Csv(Bool(row.BeforeMoving)),
            Csv(Bool(row.AfterMoving)),
            Csv(Bool(row.BeforeMounted)),
            Csv(Bool(row.AfterMounted)),
            Csv(Bool(row.BeforeHasTarget)),
            Csv(Bool(row.AfterHasTarget)),
            Csv(row.Result),
            Csv(row.Notes));

        File.AppendAllText(path, line + Environment.NewLine);
    }

    private static void EnsureHeader(string path)
    {
        if (!File.Exists(path))
        {
            File.WriteAllText(path, "Timestamp,Window,Action,Key,BeforeValid,AfterValid,BeforeX,BeforeY,BeforeZ,AfterX,AfterY,AfterZ,DistanceDelta,BeforeMoving,AfterMoving,BeforeMounted,AfterMounted,BeforeHasTarget,AfterHasTarget,Result,Notes\n");
        }
    }

    private static IReadOnlyList<string> ResolveActions(Options options, out string? error)
    {
        error = null;

        if (!string.IsNullOrWhiteSpace(options.MovementSequence))
        {
            var values = options.MovementSequence
                .Split(new[] { ',', ';', '+' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();
            if (values.Length == 0)
            {
                return DefaultSequence.Split(',');
            }

            foreach (string value in values)
            {
                if (!Actions.ContainsKey(value))
                {
                    error = $"Unknown movement sequence action '{value}'.";
                    return Array.Empty<string>();
                }
            }

            return values;
        }

        if (string.IsNullOrWhiteSpace(options.ActionName))
        {
            error = "Either --action or --movement-sequence is required.";
            return Array.Empty<string>();
        }

        if (!Actions.ContainsKey(options.ActionName))
        {
            error = $"Unknown action '{options.ActionName}'. Supported actions: {string.Join(", ", Actions.Keys.OrderBy(static k => k, StringComparer.OrdinalIgnoreCase))}";
            return Array.Empty<string>();
        }

        return new[] { options.ActionName };
    }

    private static byte ResolveKey(BridgeSettings settings, string action)
    {
        return Actions[action](settings);
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
            int index = options.WindowIndex.Value;
            if (index <= 0 || index > windows.Count)
            {
                error = $"--index {index} is out of range for {windows.Count} filtered window(s).";
                return new();
            }

            return new() { windows[index - 1] };
        }

        if (windows.Count == 1)
        {
            return new() { windows[0] };
        }

        error = "Multiple RIFT windows matched. Use --pid, --hwnd, --index, --all, --pids, or --hwnds to select explicit target(s).";
        return new();
    }

    private static RiftWindowFilter? BuildFilter(Options options)
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

    private static BridgeSettings LoadSettings(string repoRoot, string? settingsPath, DiagnosticService diag)
    {
        string path = string.IsNullOrWhiteSpace(settingsPath)
            ? Path.Combine(repoRoot, "LeaderDecoder", "settings.json")
            : ResolvePath(repoRoot, settingsPath);

        if (!File.Exists(path))
        {
            return new BridgeSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<BridgeSettings>(File.ReadAllText(path)) ?? new BridgeSettings();
        }
        catch (Exception ex)
        {
            diag.LogToolFailure(
                source: "LeaderInputVerifier",
                operation: "LoadSettings",
                detail: "Failed to load bridge settings, falling back to defaults.",
                context: path,
                ex: ex,
                dedupeKey: $"settings|{path}",
                throttleSeconds: 60.0);
            return new BridgeSettings();
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Environment.CurrentDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "LeaderDecoder"))
                && Directory.Exists(Path.Combine(dir.FullName, "LeaderInputVerifier")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        return Environment.CurrentDirectory;
    }

    private static string ResolvePath(string root, string path) =>
        Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(root, path));

    private static string Csv(string value) =>
        value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    private static string Bool(bool value) => value ? "true" : "false";

    private static void PrintWindows(List<RiftWindowInfo> windows)
    {
        for (int i = 0; i < windows.Count; i++)
        {
            var window = windows[i];
            var snapshot = RiftWindowService.GetWindowSnapshot(window.Hwnd);
            Console.WriteLine($"[{i + 1}] {RiftWindowService.FormatIdentity(window)} | Client={snapshot.ClientWidth ?? 0}x{snapshot.ClientHeight ?? 0} | Minimized={snapshot.IsMinimized}");
            Console.WriteLine($"    Selectors: {RiftWindowService.FormatSelectorHints(window)}");
        }
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
                    options.Help = true;
                    break;
                case "--list":
                    options.ListOnly = true;
                    break;
                case "--all":
                    options.AllWindows = true;
                    break;
                case "--index":
                    if (!TryReadInt(args, ref i, out int index, out error)) return false;
                    options.WindowIndex = index;
                    break;
                case "--pid":
                    if (!TryReadInt(args, ref i, out int pid, out error)) return false;
                    options.ProcessId = pid;
                    break;
                case "--pids":
                    if (!TryReadString(args, ref i, out string? pids, out error)) return false;
                    if (!RiftWindowService.TryParseProcessIdList(pids, out int[] processIds))
                    {
                        error = $"Invalid --pids value '{pids}'.";
                        return false;
                    }
                    options.ProcessIds = processIds;
                    break;
                case "--hwnd":
                    if (!TryReadString(args, ref i, out string? hwndValue, out error)) return false;
                    if (!RiftWindowService.TryParseHwnd(hwndValue, out IntPtr hwnd))
                    {
                        error = $"Invalid --hwnd value '{hwndValue}'.";
                        return false;
                    }
                    options.Hwnd = hwnd;
                    break;
                case "--hwnds":
                    if (!TryReadString(args, ref i, out string? hwnds, out error)) return false;
                    if (!RiftWindowService.TryParseHwndList(hwnds, out IntPtr[] handles))
                    {
                        error = $"Invalid --hwnds value '{hwnds}'.";
                        return false;
                    }
                    options.Hwnds = handles;
                    break;
                case "--title-contains":
                    if (!TryReadString(args, ref i, out string? title, out error)) return false;
                    options.TitleContains = title;
                    break;
                case "--action":
                    if (!TryReadString(args, ref i, out string? action, out error)) return false;
                    options.ActionName = action;
                    break;
                case "--movement-sequence":
                    if (!TryReadOptionalValue(args, ref i, out string? sequence))
                    {
                        error = "Invalid --movement-sequence value.";
                        return false;
                    }
                    options.MovementSequence = string.IsNullOrWhiteSpace(sequence) ? DefaultSequence : sequence;
                    break;
                case "--settings":
                    if (!TryReadString(args, ref i, out string? settings, out error)) return false;
                    options.SettingsPath = settings;
                    break;
                case "--hold-ms":
                    if (!TryReadInt(args, ref i, out int holdMs, out error)) return false;
                    options.HoldMs = Math.Max(1, holdMs);
                    break;
                case "--wait-ms":
                    if (!TryReadInt(args, ref i, out int waitMs, out error)) return false;
                    options.WaitMs = Math.Max(0, waitMs);
                    break;
                default:
                    error = $"Unknown argument '{arg}'.";
                    return false;
            }
        }

        return true;
    }

    private static bool TryReadString(string[] args, ref int index, out string? value, out string? error)
    {
        value = null;
        error = null;
        if (index + 1 >= args.Length)
        {
            error = $"Missing value for {args[index]}.";
            return false;
        }

        value = args[++index];
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryReadOptionalValue(string[] args, ref int index, out string? value)
    {
        value = null;
        if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = args[++index];
        }

        return true;
    }

    private static bool TryReadInt(string[] args, ref int index, out int value, out string? error)
    {
        value = 0;
        error = null;
        if (!TryReadString(args, ref index, out string? text, out error))
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

    private static void PrintUsage()
    {
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  LeaderInputVerifier.exe --action forward --pid 127928");
        Console.WriteLine("  LeaderInputVerifier.exe --movement-sequence forward,backward,left,right --all");
        Console.WriteLine("  LeaderInputVerifier.exe --hwnd 0x351350 --action interact --settings ..\\LeaderDecoder\\settings.json");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --list                 List detected or filtered RIFT windows and exit");
        Console.WriteLine("  --index N              Target the Nth filtered RIFT window");
        Console.WriteLine("  --all                  Verify all filtered RIFT windows");
        Console.WriteLine("  --pid N                Filter to a specific process id");
        Console.WriteLine("  --pids N1,N2           Filter to multiple process ids in the given order");
        Console.WriteLine("  --hwnd HEX             Filter to a specific HWND, e.g. 0x351350");
        Console.WriteLine("  --hwnds A,B            Filter to multiple HWNDs in the given order");
        Console.WriteLine("  --title-contains TEXT  Filter to window titles containing TEXT");
        Console.WriteLine("  --action NAME          Single action: forward, backward, left, right, jump, mount, interact");
        Console.WriteLine("  --movement-sequence    Comma-separated action sequence, default: forward,backward,left,right");
        Console.WriteLine("  --settings PATH        Optional settings.json path");
        Console.WriteLine("  --hold-ms N            Key hold duration in milliseconds (default: 120)");
        Console.WriteLine("  --wait-ms N            Wait after each action before the after-capture (default: 250)");
        Console.WriteLine("  --help                 Show this help");
    }

    private sealed class Options
    {
        public bool Help { get; set; }
        public bool ListOnly { get; set; }
        public bool AllWindows { get; set; }
        public int? WindowIndex { get; set; }
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
        public bool HasFilter => ProcessId.HasValue || ProcessIds is { Length: > 0 } || Hwnd.HasValue || Hwnds is { Length: > 0 } || !string.IsNullOrWhiteSpace(TitleContains);
    }

    private sealed class InputRow
    {
        public required string Timestamp { get; init; }
        public required string Window { get; init; }
        public required string Action { get; init; }
        public required string Key { get; init; }
        public required bool BeforeValid { get; init; }
        public required bool AfterValid { get; init; }
        public required float BeforeX { get; init; }
        public required float BeforeY { get; init; }
        public required float BeforeZ { get; init; }
        public required float AfterX { get; init; }
        public required float AfterY { get; init; }
        public required float AfterZ { get; init; }
        public required float DistanceDelta { get; init; }
        public required bool BeforeMoving { get; init; }
        public required bool AfterMoving { get; init; }
        public required bool BeforeMounted { get; init; }
        public required bool AfterMounted { get; init; }
        public required bool BeforeHasTarget { get; init; }
        public required bool AfterHasTarget { get; init; }
        public required string Result { get; init; }
        public required string Notes { get; init; }
    }
}
