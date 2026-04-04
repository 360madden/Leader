using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using LeaderDecoder.Models;

namespace LeaderDecoder.Services
{
    /// <summary>
    /// LEADER DIAGNOSTIC SERVICE
    /// Handles visual snapshots and telemetry logging for system audits.
    /// </summary>
    public class DiagnosticService
    {
        private const string LogFolder = "debug";
        private const string CsvPath = "debug/telemetry_log.csv";

        public DiagnosticService()
        {
            EnsureDirectories();
        }

        private void EnsureDirectories()
        {
            if (!Directory.Exists(LogFolder))
            {
                Directory.CreateDirectory(LogFolder);
            }

            if (!File.Exists(CsvPath))
            {
                File.WriteAllText(CsvPath, "Timestamp,Slot,X,Y,Z,Facing,HP,Flags\n");
            }
        }

        /// <summary>
        /// Saves a telemetry bitmap fragment to the debug folder.
        /// </summary>
        public void SaveSnapshot(Bitmap bmp, string label)
        {
            try
            {
                string filename = Path.Combine(LogFolder, $"{label}_{DateTime.Now:HHmmss_fff}.png");
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
                string entry = $"{DateTime.Now:HH:mm:ss.fff},{slot+1}," +
                               $"{state.CoordX:F2},{state.CoordY:F2},{state.CoordZ:F2}," +
                               $"{state.RawFacing:F2},{state.PlayerHP}," +
                               $"{(state.IsCombat ? 1 : 0)}{(state.IsMounted ? 1 : 0)}{(state.IsAlive ? 1 : 0)}\n";
                
                File.AppendAllText(CsvPath, entry);
            }
            catch (Exception ex)
            {
                // Silently fail logging to prevent loop stutters
            }
        }
    }
}
