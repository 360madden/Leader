using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using LeaderDecoder.Models;

namespace LeaderDecoder.Services
{
    /// <summary>
    /// LEADER DIAGNOSTIC SERVICE
    /// Handles visual snapshots and telemetry logging for system audits.
    /// </summary>
    public class DiagnosticService
    {
        private const string LogFolderName = "debug";
        private const string TelemetryFileName = "telemetry_log.csv";
        private const string ControllerFileName = "controller_actions.csv";
        private const string ToolFailureFileName = "tool_failures.csv";

        private readonly object _sync = new();
        private readonly Dictionary<string, DateTime> _lastToolFailureAt = new(StringComparer.Ordinal);
        private readonly string _logFolder;
        private readonly string _telemetryCsvPath;
        private readonly string _controllerCsvPath;
        private readonly string _toolFailureCsvPath;

        public DiagnosticService(string? baseDirectory = null)
        {
            string rootDirectory = string.IsNullOrWhiteSpace(baseDirectory)
                ? Environment.CurrentDirectory
                : Path.GetFullPath(baseDirectory);

            _logFolder = Path.Combine(rootDirectory, LogFolderName);
            _telemetryCsvPath = Path.Combine(_logFolder, TelemetryFileName);
            _controllerCsvPath = Path.Combine(_logFolder, ControllerFileName);
            _toolFailureCsvPath = Path.Combine(_logFolder, ToolFailureFileName);
            EnsureDirectories();
        }

        private void EnsureDirectories()
        {
            try
            {
                if (!Directory.Exists(_logFolder))
                {
                    Directory.CreateDirectory(_logFolder);
                }

                if (!File.Exists(_telemetryCsvPath))
                {
                    File.WriteAllText(_telemetryCsvPath, "Timestamp,Slot,X,Y,Z,MotionHeading,HP,Flags\n");
                }

                if (!File.Exists(_controllerCsvPath))
                {
                    File.WriteAllText(
                        _controllerCsvPath,
                        "Timestamp,Slot,LeaderDistance,GoalDistance,GoalX,GoalZ,SelectedAxis,PulseDurationMs,HasBasis,PendingAxis,IdleReason,RoleMatched,ProgressState,ErrorForward,ErrorLateral,Theta,CanPulse,WithinLeaderBand,StopRadius,HoldRadius,BasisForwardX,BasisForwardZ\n");
                }

                if (!File.Exists(_toolFailureCsvPath))
                {
                    File.WriteAllText(
                        _toolFailureCsvPath,
                        "Timestamp,Source,Operation,Slot,Context,Detail,ExceptionType,ExceptionMessage\n");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DIAG] Setup Error: Could not initialize log folder. {ex.Message}");
            }
        }

        /// <summary>
        /// Saves a telemetry bitmap fragment to the debug folder.
        /// </summary>
        public void SaveSnapshot(Bitmap bmp, string label)
        {
            try
            {
                string filename = Path.Combine(_logFolder, $"{label}_{DateTime.Now:HHmmss_fff}.png");
                bmp.Save(filename, ImageFormat.Png);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DIAG] Snapshot Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Appends a telemetry state snapshot to the CSV log.
        /// </summary>
        public void LogTelemetry(int slot, GameState state)
        {
            try
            {
                AppendCsvLine(
                    _telemetryCsvPath,
                    DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
                    slot + 1,
                    state.CoordX.ToString("F2", CultureInfo.InvariantCulture),
                    state.CoordY.ToString("F2", CultureInfo.InvariantCulture),
                    state.CoordZ.ToString("F2", CultureInfo.InvariantCulture),
                    state.RawFacing.ToString("F2", CultureInfo.InvariantCulture),
                    state.PlayerHP,
                    $"{(state.IsCombat ? 1 : 0)}{(state.IsMounted ? 1 : 0)}{(state.IsAlive ? 1 : 0)}");
            }
            catch (Exception)
            {
                // Silently fail logging to prevent loop stutters
            }
        }

        public void LogControllerAction(ControllerActionLogEntry entry)
        {
            try
            {
                AppendCsvLine(
                    _controllerCsvPath,
                    DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
                    entry.Slot,
                    FormatNullableFloat(entry.LeaderDistance),
                    FormatNullableFloat(entry.GoalDistance),
                    FormatNullableFloat(entry.GoalX),
                    FormatNullableFloat(entry.GoalZ),
                    entry.SelectedAxis,
                    entry.PulseDurationMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    FormatNullableBool(entry.HasBasis),
                    entry.PendingAxis,
                    entry.IdleReason,
                    FormatNullableBool(entry.RoleMatched),
                    entry.ProgressState,
                    FormatNullableFloat(entry.ErrorForward),
                    FormatNullableFloat(entry.ErrorLateral),
                    FormatNullableFloat(entry.Theta),
                    FormatNullableBool(entry.CanPulse),
                    FormatNullableBool(entry.WithinLeaderBand),
                    FormatNullableFloat(entry.StopRadius),
                    FormatNullableFloat(entry.HoldRadius),
                    FormatNullableFloat(entry.BasisForwardX),
                    FormatNullableFloat(entry.BasisForwardZ));
            }
            catch (Exception)
            {
                // Silently fail logging to prevent loop stutters
            }
        }

        public void LogToolFailure(
            string source,
            string operation,
            string detail,
            int? slot = null,
            string? context = null,
            Exception? ex = null,
            string? dedupeKey = null,
            double throttleSeconds = 5.0)
        {
            try
            {
                lock (_sync)
                {
                    string effectiveDedupeKey = dedupeKey ?? $"{source}|{operation}|{detail}|{context}";
                    DateTime now = DateTime.UtcNow;
                    if (_lastToolFailureAt.TryGetValue(effectiveDedupeKey, out DateTime lastLoggedAt)
                        && (now - lastLoggedAt).TotalSeconds < throttleSeconds)
                    {
                        return;
                    }

                    _lastToolFailureAt[effectiveDedupeKey] = now;

                    File.AppendAllText(
                        _toolFailureCsvPath,
                        BuildCsvLine(
                            DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
                            source,
                            operation,
                            slot?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                            context ?? string.Empty,
                            detail,
                            ex?.GetType().FullName ?? string.Empty,
                            ex?.Message ?? string.Empty));
                }
            }
            catch (Exception)
            {
                // Silently fail logging to prevent loop stutters
            }
        }

        private void AppendCsvLine(string path, params object?[] fields)
        {
            lock (_sync)
            {
                File.AppendAllText(path, BuildCsvLine(fields));
            }
        }

        private static string BuildCsvLine(params object?[] fields)
        {
            return string.Join(",", fields.Select(FormatCsvField)) + Environment.NewLine;
        }

        private static string FormatCsvField(object? value)
        {
            string text = value switch
            {
                null => string.Empty,
                bool boolValue => boolValue ? "true" : "false",
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
            };

            if (text.Contains('"') || text.Contains(',') || text.Contains('\n') || text.Contains('\r'))
            {
                return $"\"{text.Replace("\"", "\"\"")}\"";
            }

            return text;
        }

        private static string FormatNullableFloat(float? value)
        {
            return value?.ToString("F3", CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static string FormatNullableBool(bool? value)
        {
            return value.HasValue
                ? (value.Value ? "true" : "false")
                : string.Empty;
        }
    }

    public sealed class ControllerActionLogEntry
    {
        public int Slot { get; init; }
        public float? LeaderDistance { get; init; }
        public float? GoalDistance { get; init; }
        public float? GoalX { get; init; }
        public float? GoalZ { get; init; }
        public string SelectedAxis { get; init; } = "None";
        public int? PulseDurationMs { get; init; }
        public bool? HasBasis { get; init; }
        public string PendingAxis { get; init; } = string.Empty;
        public string IdleReason { get; init; } = string.Empty;
        public bool? RoleMatched { get; init; }
        public string ProgressState { get; init; } = string.Empty;
        public float? ErrorForward { get; init; }
        public float? ErrorLateral { get; init; }
        public float? Theta { get; init; }
        public bool? CanPulse { get; init; }
        public bool? WithinLeaderBand { get; init; }
        public float? StopRadius { get; init; }
        public float? HoldRadius { get; init; }
        public float? BasisForwardX { get; init; }
        public float? BasisForwardZ { get; init; }
    }
}
