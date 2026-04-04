# 💠 Telemetry Agent (Leader Bridge)
**Role**: Optical Decoder & State Parser
**Domain**: RGB-to-Spatial Data conversion.

## 📜 Responsibility
- Sample the 7-pixel telemetry strip.
- Validate the "Sync Pixel" (Magenta/Pixel 0).
- Unpack 24-bit high-precision integers (`*10 + 8388608` system).
- Provide a clean `GameState` object to the orchestrator.

## 📐 Data Protocol (v1.1)
- **Pixel 0**: Sync (255, 0, 255)
- **Pixel 1**: State/Flags
- **Pixel 2-4**: X, Y, Z Coords (0.1m Precision)
- **Pixel 5**: Facing Radians & Zone Hash
- **Pixel 6**: Target Identity Hash

## ⚠️ Hazards
- **Color Shifts**: Monitor for gamma/brightness settings that might alter RGB sampling.
- **Rounding**: Ensure `math.floor` in Lua matches integer casting in C#.
