using System;

namespace LeaderDecoder.Models
{
    /// <summary>
    /// LEADER BRIDGE SETTINGS
    /// Externalized configuration for the telemetry and pursuit engine.
    /// Values are loaded from settings.json at runtime.
    /// </summary>
    public class BridgeSettings
    {
        // 🧬 Optics & Sampling
        public int CaptureWidth { get; set; } = 28;
        public int CaptureHeight { get; set; } = 4;
        public int TargetFPS { get; set; } = 30;

        // 🧭 Pursuit Parameters
        public float FollowDistanceMin { get; set; } = 1.5f;
        public float FollowDistanceMax { get; set; } = 3.5f;
        public float AngleTolerance { get; set; } = 0.15f; 
        
        // PID-Lite Steering
        public float TurnP { get; set; } = 0.8f;      // Turn intensity
        public float TurnD { get; set; } = 0.2f;      // Damping
        public float AssistDistance { get; set; } = 5.0f; // Max distance to trigger interact

        // ⌨️ Keybindings (ScanCodes)
        public byte KeyForward { get; set; } = 0x11; // W
        public byte KeyLeft { get; set; } = 0x1E;    // A
        public byte KeyBack { get; set; } = 0x1F;    // S
        public byte KeyRight { get; set; } = 0x20;   // D
        public byte KeyJump { get; set; } = 0x39;    // Space
        public byte KeyMount { get; set; } = 0x32;   // M
        public byte KeyInteract { get; set; } = 0x21; // F
    }
}
