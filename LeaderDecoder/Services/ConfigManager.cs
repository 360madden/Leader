using System;
using System.IO;
using System.Text.Json;
using LeaderDecoder.Models;

namespace LeaderDecoder.Services
{
    /// <summary>
    /// LEADER CONFIG MANAGER
    /// Manages the lifecycle of the bridge settings.
    /// </summary>
    public class ConfigManager
    {
        private const string ConfigPath = "settings.json";
        public BridgeSettings Settings { get; private set; }

        public ConfigManager()
        {
            Load();
        }

        /// <summary>
        /// Loads settings from disk or creates defaults if missing.
        /// </summary>
        public void Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    Settings = JsonSerializer.Deserialize<BridgeSettings>(json) ?? new BridgeSettings();
                }
                else
                {
                    Settings = new BridgeSettings();
                    Save(); // Persist defaults
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CONFIG] Load Error: {ex.Message}. Using defaults.");
                Settings = new BridgeSettings();
            }
        }

        /// <summary>
        /// Hot-reloads settings from disk at runtime (bound to 'R' key in Program.cs).
        /// </summary>
        public void Reload()
        {
            Console.WriteLine("[CONFIG] 🔄 Reloading settings.json...");
            Load();
            Console.WriteLine($"[CONFIG] ✅ Reloaded — FPS:{Settings.TargetFPS} P:{Settings.TurnP} D:{Settings.TurnD} Dist:{Settings.FollowDistanceMin}-{Settings.FollowDistanceMax}m");
        }

        /// <summary>
        /// Saves current settings to settings.json.
        /// </summary>
        public void Save()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(Settings, options);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CONFIG] Save Error: {ex.Message}");
            }
        }
    }
}
