using System.Globalization;
using System.Text.Json;
using LeaderDecoder.Models;
using LeaderDecoder.Services;

namespace LeaderInputProbe;

internal static class Program
{
    private const int DefaultHoldMs = 250;
    private const int TapHoldMs = 60;
    private const float PlanarMovementThreshold = 0.10f;
    private const float VerticalMovementThreshold = 0.10f;
    private const string ResultLogName = "input_probe_results.csv";

    private static readonly Dictionary<string, InputEngine.RiftKey> KeyMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Q"] = InputEngine.RiftKey.Q,
            ["W"] = InputEngine.RiftKey.W,
            ["FORWARD"] = InputEngine.RiftKey.W,
            ["E"] = InputEngine.RiftKey.E,
            ["A"] = InputEngine.RiftKey.A,
            ["LEFT"] = InputEngine.RiftKey.A,
            ["S"] = InputEngine.RiftKey.S,
            ["BACK"] = InputEngine.RiftKey.S,
            ["BACKWARD"] = InputEngine.RiftKey.S,
            ["D"] = InputEngine.RiftKey.D,
            ["RIGHT"] = InputEngine.RiftKey.D,
            ["STRAFELEFT"] = InputEngine.RiftKey.Q,
            ["STRAFERIGHT"] = InputEngine.RiftKey.E,
            ["TURNLEFT"] = InputEngine.RiftKey.A,
            ["TURNRIGHT"] = InputEngine.RiftKey.D,
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

    private static readonly Dictionary<string, Func<BridgeSettings, byte>> ActionMap =
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
            ["assist"] = s => s.KeyInteract,
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

            string repoRoot = FindRepoRoot();
            string resultsPath = ResolveResultsPath(repoRoot, options.ResultsPath);

            if (options.ShowSummary)
            {
                return PrintSummary(resultsPath);
            }

            string? resultsDirectory = Path.GetDirectoryName(resultsPath);
            if (!string.IsNullOrWhiteSpace(resultsDirectory))
            {
                Directory.CreateDirectory(resultsDirectory);
            }

            EnsureResultLog(resultsPath);

            if (!options.ShowHelp && !options.ListOnly && !options.HasProbeIntent)
            {
                Console.WriteLine();
                Console.WriteLine("No probe action was supplied.");
                PrintUsage();
                return 1;
            }

            ProbeTarget? probe = null;
            string? settingsSource = null;
            if (options.HasProbeIntent && !TryResolveProbe(options, repoRoot, diag, out probe, out error, out settingsSource))
            {
                Console.Error.WriteLine(error);
                return 1;
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
                if (probe is not null)
                {
                    Console.WriteLine();
                    Console.WriteLine(BuildIntentSummary(options, probe, 0, options.Tap ? TapHoldMs : options.HoldMs, settingsSource, resultsPath));
                }
                return 0;
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
            Console.WriteLine(BuildIntentSummary(options, probe!, selected.Count, holdMs, settingsSource, resultsPath));

            for (int index = 0; index < selected.Count; index++)
            {
                var window = selected[index];
                Console.WriteLine();
                Console.WriteLine($"[{index + 1}] {RiftWindowService.FormatIdentity(window)}");
                Console.WriteLine($"    Selectors: {RiftWindowService.FormatSelectorHints(window)}");

                for (int attempt = 0; attempt < options.Repeat; attempt++)
                {
                    ProbeCapture? before = capture is not null ? Capture(capture, window.Hwnd) : null;
                    if (before is not null)
                    {
                        PrintInspection("Before", before);
                    }

                    Console.WriteLine(
                        $"    Probe {attempt + 1}/{options.Repeat}: {(probe!.SourceType == "action" ? "action" : "key")}={probe.RequestedLabel} duration={holdMs}ms");
                    ExecutePress(input, window.Hwnd, probe.ScanCode, holdMs);

                    if (capture is not null && options.WaitMs > 0)
                    {
                        Thread.Sleep(options.WaitMs);
                    }

                    ProbeCapture? after = capture is not null ? Capture(capture, window.Hwnd) : null;
                    if (after is not null)
                    {
                        PrintInspection("After", after);
                    }

                    ProbeResult result = Classify(probe, before, after);
                    AppendResult(resultsPath, BuildResultRow(window, probe, attempt + 1, holdMs, before, after, result));

                    Console.WriteLine(
                        $"      Result: {result.Status} | ΔXZ={result.PlanarDelta:F2} | ΔY={result.VerticalDelta:F2} | Δ3D={result.Distance3D:F2} | Notes={result.Notes}");

                    if (attempt < options.Repeat - 1 && options.IntervalMs > 0)
                    {
                        Thread.Sleep(options.IntervalMs);
                    }
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

    private static string BuildIntentSummary(Options options, ProbeTarget probe, int selectedCount, int holdMs, string? settingsSource, string resultsPath)
    {
        var parts = new List<string>
        {
            $"Probe={probe.SourceType}:{probe.RequestedLabel}",
            $"ScanCode=0x{probe.ScanCode:X2}",
            options.Tap ? "Mode=Tap" : "Mode=Press",
            $"HoldMs={holdMs}",
            selectedCount == 1 ? "Target=1 window" : $"Target={selectedCount} windows",
            $"Results={resultsPath}"
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

        if (!string.IsNullOrWhiteSpace(settingsSource))
        {
            parts.Add($"Settings={settingsSource}");
        }

        return string.Join(" | ", parts);
    }

    private static void PrintInspection(string label, ProbeCapture capture)
    {
        Console.WriteLine(
            $"    {label}: Profile={capture.Analysis.ProfileName} | Strip={capture.Analysis.Width}x{capture.Analysis.Height} | Client={capture.WindowSnapshot.ClientWidth ?? 0}x{capture.WindowSnapshot.ClientHeight ?? 0} | Minimized={capture.WindowSnapshot.IsMinimized}");

        foreach (string line in StripInspector.FormatStateSummary(capture.Analysis.State).Split(Environment.NewLine))
        {
            Console.WriteLine($"      {line}");
        }
    }

    private static ProbeCapture Capture(CaptureEngine capture, IntPtr hwnd)
    {
        using var bmp = capture.CaptureRegion(hwnd, StripInspector.StripWidth, StripInspector.StripHeight);
        var analysis = StripInspector.Analyze(bmp);
        return new ProbeCapture
        {
            Analysis = analysis,
            WindowSnapshot = RiftWindowService.GetWindowSnapshot(hwnd),
        };
    }

    private static void ExecutePress(InputEngine input, IntPtr hwnd, byte scanCode, int holdMs)
    {
        input.SendScanCodeDown(hwnd, scanCode);
        Thread.Sleep(holdMs);
        input.SendScanCodeUp(hwnd, scanCode);
    }

    private static bool TryResolveProbe(Options options, string repoRoot, DiagnosticService diag, out ProbeTarget probe, out string? error, out string? settingsSource)
    {
        probe = ProbeTarget.Empty;
        error = null;
        settingsSource = null;

        if (!string.IsNullOrWhiteSpace(options.ActionName))
        {
            string action = options.ActionName.Trim();
            if (!ActionMap.TryGetValue(action, out var selector))
            {
                error = $"Unknown action '{options.ActionName}'. Supported actions: {string.Join(", ", ActionMap.Keys.OrderBy(static k => k, StringComparer.OrdinalIgnoreCase))}";
                return false;
            }

            BridgeSettings settings = LoadSettings(repoRoot, options.SettingsPath, diag, out settingsSource);
            byte scanCode = selector(settings);
            if (scanCode == 0)
            {
                error = $"Action '{options.ActionName}' resolved to scan code 0 in the current settings.";
                return false;
            }

            probe = new ProbeTarget
            {
                RequestedLabel = action.ToLowerInvariant(),
                SourceType = "action",
                ScanCode = scanCode,
                Kind = ResolveActionKind(action)
            };

            return true;
        }

        if (!string.IsNullOrWhiteSpace(options.KeyName))
        {
            string keyName = options.KeyName.Trim();
            if (!KeyMap.TryGetValue(keyName, out InputEngine.RiftKey key))
            {
                error = $"Unknown key '{options.KeyName}'. Supported keys: {string.Join(", ", KeyMap.Keys.OrderBy(static k => k, StringComparer.OrdinalIgnoreCase))}";
                return false;
            }

            probe = new ProbeTarget
            {
                RequestedLabel = keyName.ToUpperInvariant(),
                SourceType = "key",
                ScanCode = (byte)key,
                Kind = ResolveKeyKind(keyName)
            };

            return true;
        }

        error = "No probe target supplied.";
        return false;
    }

    private static ProbeKind ResolveActionKind(string action)
    {
        return action.Trim().ToLowerInvariant() switch
        {
            "forward" or "backward" or "back" or "left" or "strafeleft" or "right" or "straferight" => ProbeKind.PlanarMovement,
            "jump" => ProbeKind.VerticalMovement,
            "mount" or "interact" or "assist" => ProbeKind.StateChange,
            _ => ProbeKind.Generic,
        };
    }

    private static ProbeKind ResolveKeyKind(string key)
    {
        return key.Trim().ToUpperInvariant() switch
        {
            "W" or "FORWARD" or "A" or "LEFT" or "S" or "BACK" or "BACKWARD" or "D" or "RIGHT" or "Q" or "E" or "STRAFELEFT" or "STRAFERIGHT" or "TURNLEFT" or "TURNRIGHT" => ProbeKind.PlanarMovement,
            "SPACE" or "SPACEBAR" or "JUMP" => ProbeKind.VerticalMovement,
            "F" or "INTERACT" or "ASSIST" or "M" or "MOUNT" => ProbeKind.StateChange,
            _ => ProbeKind.Generic,
        };
    }

    private static ProbeResult Classify(ProbeTarget probe, ProbeCapture? before, ProbeCapture? after)
    {
        if (before is null || after is null)
        {
            return new ProbeResult
            {
                Status = "sent_without_inspection",
                Notes = "probe was sent without before/after telemetry capture"
            };
        }

        GameState beforeState = before.Analysis.State;
        GameState afterState = after.Analysis.State;

        float deltaX = afterState.CoordX - beforeState.CoordX;
        float deltaY = afterState.CoordY - beforeState.CoordY;
        float deltaZ = afterState.CoordZ - beforeState.CoordZ;
        float planarDelta = MathF.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
        float distance3D = MathF.Sqrt(planarDelta * planarDelta + deltaY * deltaY);

        if (!beforeState.IsValid || !afterState.IsValid)
        {
            return new ProbeResult
            {
                Status = "invalid_strip",
                Notes = !beforeState.IsValid && !afterState.IsValid
                    ? "telemetry strip invalid before and after the probe"
                    : !beforeState.IsValid
                        ? "telemetry strip invalid before the probe"
                        : "telemetry strip invalid after the probe",
                PlanarDelta = planarDelta,
                VerticalDelta = MathF.Abs(deltaY),
                Distance3D = distance3D,
            };
        }

        bool movingChanged = beforeState.IsMoving != afterState.IsMoving;
        bool mountedChanged = beforeState.IsMounted != afterState.IsMounted;
        bool targetChanged = beforeState.HasTarget != afterState.HasTarget || beforeState.TargetHP != afterState.TargetHP;
        bool hpChanged = beforeState.PlayerHP != afterState.PlayerHP;
        bool zoneChanged = beforeState.ZoneHash != afterState.ZoneHash;
        bool tagChanged = !string.Equals(beforeState.PlayerTag, afterState.PlayerTag, StringComparison.Ordinal);

        return probe.Kind switch
        {
            ProbeKind.PlanarMovement => (planarDelta >= PlanarMovementThreshold || distance3D >= PlanarMovementThreshold || movingChanged)
                ? new ProbeResult
                {
                    Status = "accepted_movement",
                    Notes = "position or moving-state changed after the probe",
                    PlanarDelta = planarDelta,
                    VerticalDelta = MathF.Abs(deltaY),
                    Distance3D = distance3D,
                }
                : new ProbeResult
                {
                    Status = "ignored_movement",
                    Notes = "no coordinate or moving-state change detected",
                    PlanarDelta = planarDelta,
                    VerticalDelta = MathF.Abs(deltaY),
                    Distance3D = distance3D,
                },

            ProbeKind.VerticalMovement => (MathF.Abs(deltaY) >= VerticalMovementThreshold || distance3D >= VerticalMovementThreshold || movingChanged)
                ? new ProbeResult
                {
                    Status = "accepted_movement",
                    Notes = "vertical position or moving-state changed after the probe",
                    PlanarDelta = planarDelta,
                    VerticalDelta = MathF.Abs(deltaY),
                    Distance3D = distance3D,
                }
                : new ProbeResult
                {
                    Status = "ignored_movement",
                    Notes = "no vertical or moving-state change detected",
                    PlanarDelta = planarDelta,
                    VerticalDelta = MathF.Abs(deltaY),
                    Distance3D = distance3D,
                },

            ProbeKind.StateChange => (mountedChanged || targetChanged || hpChanged || zoneChanged || tagChanged || movingChanged)
                ? new ProbeResult
                {
                    Status = "accepted_state_change",
                    Notes = "telemetry flags, target, health, or identity changed after the probe",
                    PlanarDelta = planarDelta,
                    VerticalDelta = MathF.Abs(deltaY),
                    Distance3D = distance3D,
                }
                : new ProbeResult
                {
                    Status = "no_visible_change",
                    Notes = "no visible telemetry change detected after the probe",
                    PlanarDelta = planarDelta,
                    VerticalDelta = MathF.Abs(deltaY),
                    Distance3D = distance3D,
                },

            _ => (distance3D >= PlanarMovementThreshold || movingChanged || mountedChanged || targetChanged || hpChanged || zoneChanged || tagChanged)
                ? new ProbeResult
                {
                    Status = "accepted_state_change",
                    Notes = "some visible telemetry change followed the probe",
                    PlanarDelta = planarDelta,
                    VerticalDelta = MathF.Abs(deltaY),
                    Distance3D = distance3D,
                }
                : new ProbeResult
                {
                    Status = "no_visible_change",
                    Notes = "no visible telemetry change detected after the probe",
                    PlanarDelta = planarDelta,
                    VerticalDelta = MathF.Abs(deltaY),
                    Distance3D = distance3D,
                },
        };
    }

    private static ProbeLogRow BuildResultRow(
        RiftWindowInfo window,
        ProbeTarget probe,
        int attempt,
        int holdMs,
        ProbeCapture? before,
        ProbeCapture? after,
        ProbeResult result)
    {
        RiftWindowSnapshot snapshot = after?.WindowSnapshot ?? before?.WindowSnapshot ?? RiftWindowService.GetWindowSnapshot(window.Hwnd);
        GameState? beforeState = before?.Analysis.State;
        GameState? afterState = after?.Analysis.State;

        return new ProbeLogRow
        {
            Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
            Window = RiftWindowService.FormatIdentity(window),
            ProcessId = window.ProcessId,
            Hwnd = RiftWindowService.FormatHwnd(window.Hwnd),
            Probe = probe.RequestedLabel,
            SourceType = probe.SourceType,
            ScanCode = $"0x{probe.ScanCode:X2}",
            Attempt = attempt,
            HoldMs = holdMs,
            ClientWidth = snapshot.ClientWidth,
            ClientHeight = snapshot.ClientHeight,
            IsMinimized = snapshot.IsMinimized,
            BeforeProfile = before?.Analysis.ProfileName,
            AfterProfile = after?.Analysis.ProfileName,
            BeforeValid = beforeState?.IsValid,
            AfterValid = afterState?.IsValid,
            BeforeX = beforeState?.CoordX,
            BeforeY = beforeState?.CoordY,
            BeforeZ = beforeState?.CoordZ,
            AfterX = afterState?.CoordX,
            AfterY = afterState?.CoordY,
            AfterZ = afterState?.CoordZ,
            PlanarDelta = result.PlanarDelta,
            VerticalDelta = result.VerticalDelta,
            Distance3D = result.Distance3D,
            BeforeMoving = beforeState?.IsMoving,
            AfterMoving = afterState?.IsMoving,
            BeforeMounted = beforeState?.IsMounted,
            AfterMounted = afterState?.IsMounted,
            BeforeHasTarget = beforeState?.HasTarget,
            AfterHasTarget = afterState?.HasTarget,
            BeforeHP = beforeState?.PlayerHP,
            AfterHP = afterState?.PlayerHP,
            BeforeTargetHP = beforeState?.TargetHP,
            AfterTargetHP = afterState?.TargetHP,
            BeforeFacing = beforeState?.RawFacing,
            AfterFacing = afterState?.RawFacing,
            BeforeZoneHash = beforeState?.ZoneHash,
            AfterZoneHash = afterState?.ZoneHash,
            BeforePlayerTag = beforeState?.PlayerTag,
            AfterPlayerTag = afterState?.PlayerTag,
            Result = result.Status,
            Notes = result.Notes,
        };
    }

    private static void AppendResult(string path, ProbeLogRow row)
    {
        string line = string.Join(",",
            Csv(row.Timestamp),
            Csv(row.Window),
            Csv(row.ProcessId.ToString(CultureInfo.InvariantCulture)),
            Csv(row.Hwnd),
            Csv(row.Probe),
            Csv(row.SourceType),
            Csv(row.ScanCode),
            Csv(row.Attempt.ToString(CultureInfo.InvariantCulture)),
            Csv(row.HoldMs.ToString(CultureInfo.InvariantCulture)),
            Csv(NullableInt(row.ClientWidth)),
            Csv(NullableInt(row.ClientHeight)),
            Csv(Bool(row.IsMinimized)),
            Csv(row.BeforeProfile),
            Csv(row.AfterProfile),
            Csv(NullableBool(row.BeforeValid)),
            Csv(NullableBool(row.AfterValid)),
            Csv(NullableFloat(row.BeforeX)),
            Csv(NullableFloat(row.BeforeY)),
            Csv(NullableFloat(row.BeforeZ)),
            Csv(NullableFloat(row.AfterX)),
            Csv(NullableFloat(row.AfterY)),
            Csv(NullableFloat(row.AfterZ)),
            Csv(NullableFloat(row.PlanarDelta)),
            Csv(NullableFloat(row.VerticalDelta)),
            Csv(NullableFloat(row.Distance3D)),
            Csv(NullableBool(row.BeforeMoving)),
            Csv(NullableBool(row.AfterMoving)),
            Csv(NullableBool(row.BeforeMounted)),
            Csv(NullableBool(row.AfterMounted)),
            Csv(NullableBool(row.BeforeHasTarget)),
            Csv(NullableBool(row.AfterHasTarget)),
            Csv(NullableInt(row.BeforeHP)),
            Csv(NullableInt(row.AfterHP)),
            Csv(NullableInt(row.BeforeTargetHP)),
            Csv(NullableInt(row.AfterTargetHP)),
            Csv(NullableFloat(row.BeforeFacing)),
            Csv(NullableFloat(row.AfterFacing)),
            Csv(NullableByte(row.BeforeZoneHash)),
            Csv(NullableByte(row.AfterZoneHash)),
            Csv(row.BeforePlayerTag),
            Csv(row.AfterPlayerTag),
            Csv(row.Result),
            Csv(row.Notes));

        File.AppendAllText(path, line + Environment.NewLine);
    }

    private static void EnsureResultLog(string path)
    {
        if (!File.Exists(path))
        {
            File.WriteAllText(path, "Timestamp,Window,ProcessId,Hwnd,Probe,SourceType,ScanCode,Attempt,HoldMs,ClientWidth,ClientHeight,IsMinimized,BeforeProfile,AfterProfile,BeforeValid,AfterValid,BeforeX,BeforeY,BeforeZ,AfterX,AfterY,AfterZ,PlanarDelta,VerticalDelta,Distance3D,BeforeMoving,AfterMoving,BeforeMounted,AfterMounted,BeforeHasTarget,AfterHasTarget,BeforeHP,AfterHP,BeforeTargetHP,AfterTargetHP,BeforeFacing,AfterFacing,BeforeZoneHash,AfterZoneHash,BeforePlayerTag,AfterPlayerTag,Result,Notes\n");
        }
    }

    private static string ResolveResultsPath(string repoRoot, string? explicitPath)
    {
        return string.IsNullOrWhiteSpace(explicitPath)
            ? Path.Combine(repoRoot, "LeaderInputProbe", "debug", ResultLogName)
            : ResolvePath(repoRoot, explicitPath);
    }

    private static int PrintSummary(string path)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Probe results file was not found: {path}");
            return 1;
        }

        var rows = LoadSummaryRows(path, out int skippedRows);
        Console.WriteLine("============================================================");
        Console.WriteLine("Leader Input Probe Summary");
        Console.WriteLine("============================================================");
        Console.WriteLine($"Results: {path}");

        if (rows.Count == 0)
        {
            Console.WriteLine("No probe result entries were found.");
            if (skippedRows > 0)
            {
                Console.WriteLine($"Skipped malformed rows: {skippedRows}");
            }
            return 0;
        }

        int accepted = rows.Count(static row => row.IsAccepted);
        int ignored = rows.Count(static row => row.IsIgnored);
        int invalid = rows.Count(static row => string.Equals(row.Result, "invalid_strip", StringComparison.OrdinalIgnoreCase));
        int noInspection = rows.Count(static row => string.Equals(row.Result, "sent_without_inspection", StringComparison.OrdinalIgnoreCase));

        Console.WriteLine($"Rows: {rows.Count}");
        Console.WriteLine($"Accepted: {accepted}");
        Console.WriteLine($"Ignored/NoChange: {ignored}");
        Console.WriteLine($"Invalid strips: {invalid}");
        Console.WriteLine($"Without inspection: {noInspection}");
        if (skippedRows > 0)
        {
            Console.WriteLine($"Skipped malformed rows: {skippedRows}");
        }

        Console.WriteLine();
        Console.WriteLine("By probe:");
        Console.WriteLine("  Probe                 Total  Accepted  Ignored  Invalid  AvgΔ3D  LastResult");

        foreach (var group in rows
            .GroupBy(static row => $"{row.SourceType}:{row.Probe}", StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            string key = group.Key.Length > 20 ? group.Key[..20] : group.Key;
            int total = group.Count();
            int groupAccepted = group.Count(static row => row.IsAccepted);
            int groupIgnored = group.Count(static row => row.IsIgnored);
            int groupInvalid = group.Count(static row => string.Equals(row.Result, "invalid_strip", StringComparison.OrdinalIgnoreCase));
            double averageDistance = group.Average(static row => (double)row.Distance3D);
            string lastResult = group
                .OrderByDescending(static row => row.TimestampText, StringComparer.OrdinalIgnoreCase)
                .Select(static row => row.Result)
                .FirstOrDefault() ?? string.Empty;

            Console.WriteLine($"  {key,-20} {total,5} {groupAccepted,9} {groupIgnored,8} {groupInvalid,8} {averageDistance,7:F2}  {lastResult}");
        }

        return 0;
    }

    private static List<ProbeSummaryRow> LoadSummaryRows(string path, out int skippedRows)
    {
        skippedRows = 0;
        using var reader = new StreamReader(path);

        string? headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            return new List<ProbeSummaryRow>();
        }

        string[] headers = ParseCsvLine(headerLine);
        var headerIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < headers.Length; index++)
        {
            headerIndex[headers[index]] = index;
        }

        if (!headerIndex.ContainsKey("Probe")
            || !headerIndex.ContainsKey("SourceType")
            || !headerIndex.ContainsKey("Result"))
        {
            skippedRows = 1;
            return new List<ProbeSummaryRow>();
        }

        var rows = new List<ProbeSummaryRow>();
        while (!reader.EndOfStream)
        {
            string? line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] fields = ParseCsvLine(line);
            if (!TryGetCsvField(fields, headerIndex, "Probe", out string probe)
                || !TryGetCsvField(fields, headerIndex, "SourceType", out string sourceType)
                || !TryGetCsvField(fields, headerIndex, "Result", out string result))
            {
                skippedRows++;
                continue;
            }

            rows.Add(new ProbeSummaryRow
            {
                TimestampText = GetCsvField(fields, headerIndex, "Timestamp"),
                Probe = probe,
                SourceType = sourceType,
                Result = result,
                Distance3D = ParseCsvFloat(GetCsvField(fields, headerIndex, "Distance3D")),
            });
        }

        return rows;
    }

    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int index = 0; index < line.Length; index++)
        {
            char ch = line[index];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (index + 1 < line.Length && line[index + 1] == '"')
                    {
                        current.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(ch);
                }
            }
            else if (ch == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else if (ch == '"')
            {
                inQuotes = true;
            }
            else
            {
                current.Append(ch);
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }

    private static bool TryGetCsvField(string[] fields, Dictionary<string, int> headerIndex, string name, out string value)
    {
        value = string.Empty;
        if (!headerIndex.TryGetValue(name, out int index) || index < 0 || index >= fields.Length)
        {
            return false;
        }

        value = fields[index];
        return true;
    }

    private static string GetCsvField(string[] fields, Dictionary<string, int> headerIndex, string name)
    {
        return TryGetCsvField(fields, headerIndex, name, out string value)
            ? value
            : string.Empty;
    }

    private static float ParseCsvFloat(string text)
    {
        return float.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float value)
            ? value
            : 0f;
    }

    private static BridgeSettings LoadSettings(string repoRoot, string? explicitPath, DiagnosticService diag, out string source)
    {
        string path = string.IsNullOrWhiteSpace(explicitPath)
            ? Path.Combine(repoRoot, "LeaderDecoder", "settings.json")
            : ResolvePath(repoRoot, explicitPath);

        if (!File.Exists(path))
        {
            source = "defaults (settings.json not found)";
            return new BridgeSettings();
        }

        try
        {
            source = path;
            return JsonSerializer.Deserialize<BridgeSettings>(File.ReadAllText(path)) ?? new BridgeSettings();
        }
        catch (Exception ex)
        {
            diag.LogToolFailure(
                source: "LeaderInputProbe",
                operation: "LoadSettings",
                detail: "Failed to load bridge settings, falling back to defaults.",
                context: path,
                ex: ex,
                dedupeKey: $"input-probe-settings|{path}",
                throttleSeconds: 60.0);
            source = $"defaults (failed to load {path})";
            return new BridgeSettings();
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Environment.CurrentDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "LeaderDecoder"))
                && Directory.Exists(Path.Combine(dir.FullName, "LeaderInputProbe")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Environment.CurrentDirectory;
    }

    private static string ResolvePath(string root, string path) =>
        Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(root, path));

    private static string Csv(string? value)
    {
        string text = value ?? string.Empty;
        return text.Contains('"') || text.Contains(',') || text.Contains('\n') || text.Contains('\r')
            ? $"\"{text.Replace("\"", "\"\"")}\""
            : text;
    }

    private static string NullableFloat(float? value) => value?.ToString("F3", CultureInfo.InvariantCulture) ?? string.Empty;
    private static string NullableInt(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    private static string NullableByte(byte? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    private static string NullableBool(bool? value) => value.HasValue ? (value.Value ? "true" : "false") : string.Empty;
    private static string Bool(bool value) => value ? "true" : "false";

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

                case "--summary":
                    options.ShowSummary = true;
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

                case "--action":
                    if (!TryReadString(args, ref i, out string? actionName, out error))
                    {
                        return false;
                    }

                    options.ActionName = actionName;
                    break;

                case "--settings":
                    if (!TryReadString(args, ref i, out string? settingsPath, out error))
                    {
                        return false;
                    }

                    options.SettingsPath = settingsPath;
                    break;

                case "--results":
                    if (!TryReadString(args, ref i, out string? resultsPath, out error))
                    {
                        return false;
                    }

                    options.ResultsPath = resultsPath;
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

        if (!string.IsNullOrWhiteSpace(options.KeyName) && !string.IsNullOrWhiteSpace(options.ActionName))
        {
            error = "--key and --action cannot be combined.";
            return false;
        }

        if (options.Tap && options.HoldMs != DefaultHoldMs)
        {
            error = "--tap cannot be combined with --hold-ms.";
            return false;
        }

        if (!options.ShowHelp && !options.ListOnly && !options.ShowSummary && !options.HasProbeIntent)
        {
            error = "--key or --action is required unless you only use --list.";
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
        Console.WriteLine("  LeaderInputProbe.exe --summary");
        Console.WriteLine("  LeaderInputProbe.exe --summary --results .\\LeaderInputProbe\\debug\\input_probe_results.csv");
        Console.WriteLine("  LeaderInputProbe.exe --pid 127928 --key W --hold-ms 3000 --inspect");
        Console.WriteLine("  LeaderInputProbe.exe --pid 127928 --action forward --inspect");
        Console.WriteLine("  LeaderInputProbe.exe --hwnd 0x351350 --key SPACE --tap");
        Console.WriteLine("  LeaderInputProbe.exe --pids 127928,133228 --action interact --tap --settings ..\\LeaderDecoder\\settings.json");
        Console.WriteLine("  LeaderInputProbe.exe --title-contains RIFT --index 2 --key A --hold-ms 500");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --list                 List detected or filtered RIFT windows and exit if no probe action is given");
        Console.WriteLine("  --summary              Summarize an existing input_probe_results.csv without sending input");
        Console.WriteLine("  --index N              Target the Nth filtered RIFT window");
        Console.WriteLine("  --all                  Probe all filtered RIFT windows");
        Console.WriteLine("  --pid N                Filter to a specific process id");
        Console.WriteLine("  --pids N1,N2           Filter to multiple process ids in the given order");
        Console.WriteLine("  --hwnd HEX             Filter to a specific HWND, e.g. 0x351350");
        Console.WriteLine("  --hwnds A,B            Filter to multiple HWNDs in the given order");
        Console.WriteLine("  --title-contains TEXT  Filter to window titles containing TEXT");
        Console.WriteLine("  --key NAME             Required probe key: Q, W, E, A, S, D, F, M, SPACE, 1-5");
        Console.WriteLine("  --action NAME          Probe action resolved from settings.json: forward, backward, left, right, jump, mount, interact");
        Console.WriteLine("  --settings PATH        Optional settings.json used with --action");
        Console.WriteLine("  --results PATH         Optional results CSV path used with --summary");
        Console.WriteLine("  --tap                  Use a 60 ms press instead of --hold-ms");
        Console.WriteLine("  --hold-ms N            Press duration in milliseconds (default: 250)");
        Console.WriteLine("  --repeat N             Repeat the probe N times (default: 1)");
        Console.WriteLine("  --interval-ms N        Delay between repeated probes (default: 250)");
        Console.WriteLine("  --inspect              Capture and decode the telemetry strip before and after the probe");
        Console.WriteLine("  --wait-ms N            Delay before post-probe inspection (default: 250)");
        Console.WriteLine("  --help                 Show this help");
    }

    private enum ProbeKind
    {
        Generic,
        PlanarMovement,
        VerticalMovement,
        StateChange,
    }

    private sealed class ProbeTarget
    {
        public static ProbeTarget Empty { get; } = new();
        public string RequestedLabel { get; init; } = string.Empty;
        public string SourceType { get; init; } = string.Empty;
        public byte ScanCode { get; init; }
        public ProbeKind Kind { get; init; }
    }

    private sealed class ProbeCapture
    {
        public required StripAnalysis Analysis { get; init; }
        public required RiftWindowSnapshot WindowSnapshot { get; init; }
    }

    private sealed class ProbeResult
    {
        public string Status { get; init; } = string.Empty;
        public string Notes { get; init; } = string.Empty;
        public float PlanarDelta { get; init; }
        public float VerticalDelta { get; init; }
        public float Distance3D { get; init; }
    }

    private sealed class ProbeLogRow
    {
        public required string Timestamp { get; init; }
        public required string Window { get; init; }
        public required int ProcessId { get; init; }
        public required string Hwnd { get; init; }
        public required string Probe { get; init; }
        public required string SourceType { get; init; }
        public required string ScanCode { get; init; }
        public required int Attempt { get; init; }
        public required int HoldMs { get; init; }
        public int? ClientWidth { get; init; }
        public int? ClientHeight { get; init; }
        public required bool IsMinimized { get; init; }
        public string? BeforeProfile { get; init; }
        public string? AfterProfile { get; init; }
        public bool? BeforeValid { get; init; }
        public bool? AfterValid { get; init; }
        public float? BeforeX { get; init; }
        public float? BeforeY { get; init; }
        public float? BeforeZ { get; init; }
        public float? AfterX { get; init; }
        public float? AfterY { get; init; }
        public float? AfterZ { get; init; }
        public float? PlanarDelta { get; init; }
        public float? VerticalDelta { get; init; }
        public float? Distance3D { get; init; }
        public bool? BeforeMoving { get; init; }
        public bool? AfterMoving { get; init; }
        public bool? BeforeMounted { get; init; }
        public bool? AfterMounted { get; init; }
        public bool? BeforeHasTarget { get; init; }
        public bool? AfterHasTarget { get; init; }
        public int? BeforeHP { get; init; }
        public int? AfterHP { get; init; }
        public int? BeforeTargetHP { get; init; }
        public int? AfterTargetHP { get; init; }
        public float? BeforeFacing { get; init; }
        public float? AfterFacing { get; init; }
        public byte? BeforeZoneHash { get; init; }
        public byte? AfterZoneHash { get; init; }
        public string? BeforePlayerTag { get; init; }
        public string? AfterPlayerTag { get; init; }
        public required string Result { get; init; }
        public required string Notes { get; init; }
    }

    private sealed class ProbeSummaryRow
    {
        public required string TimestampText { get; init; }
        public required string Probe { get; init; }
        public required string SourceType { get; init; }
        public required string Result { get; init; }
        public required float Distance3D { get; init; }
        public bool IsAccepted =>
            string.Equals(Result, "accepted_movement", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Result, "accepted_state_change", StringComparison.OrdinalIgnoreCase);
        public bool IsIgnored =>
            string.Equals(Result, "ignored_movement", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Result, "no_visible_change", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Options
    {
        public bool ShowHelp { get; set; }
        public bool ListOnly { get; set; }
        public bool ShowSummary { get; set; }
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
        public string? ActionName { get; set; }
        public string? SettingsPath { get; set; }
        public string? ResultsPath { get; set; }
        public bool HasProbeIntent => !string.IsNullOrWhiteSpace(KeyName) || !string.IsNullOrWhiteSpace(ActionName);
    }
}
