using System;
using System.Collections.Generic;

namespace LeaderDecoder.Models
{
    /// <summary>
    /// LEADER BRIDGE SETTINGS
    /// Externalized configuration for the telemetry and pursuit engine.
    /// Values are loaded from settings.json at runtime.
    /// </summary>
    public class BridgeSettings
    {
        // 🧠 Memory Engine (Offsets)
        public Dictionary<string, int[]> MemoryOffsets { get; set; } = new Dictionary<string, int[]>();

        // 🧬 Optics & Sampling
        public int CaptureWidth { get; set; } = 56;
        public int CaptureHeight { get; set; } = 8;
        public int TargetFPS { get; set; } = 30;

        // 🧭 Pursuit Parameters
        public float FollowDistanceMin { get; set; } = 1.5f;
        public float FollowDistanceMax { get; set; } = 3.5f;
        public float FollowEngageDistanceMax { get; set; } = 20.0f;
        public float AngleTolerance { get; set; } = 0.15f; 
        
        // Legacy turn-steering settings retained for settings.json compatibility.
        public float TurnP { get; set; } = 0.8f;
        public float TurnD { get; set; } = 0.2f;
        public float TurnRateRadiansPerSecond { get; set; } = 3.25f;
        public float AssistDistance { get; set; } = 5.0f; // Max distance to trigger interact

        // ⌨️ Keybindings (ScanCodes)
        public byte KeyForward { get; set; } = 0x11; // W
        public byte KeyLeft { get; set; } = 0x10;    // Q
        public byte KeyBack { get; set; } = 0x1F;    // S
        public byte KeyRight { get; set; } = 0x12;   // E
        public byte KeyTurnLeft { get; set; } = 0x1E;  // Legacy turn-left key; only released during emergency stop
        public byte KeyTurnRight { get; set; } = 0x20; // Legacy turn-right key; only released during emergency stop
        public byte KeyJump { get; set; } = 0x39;    // Space
        public byte KeyMount { get; set; } = 0x32;   // M
        public byte KeyInteract { get; set; } = 0x21; // F
    }
}
