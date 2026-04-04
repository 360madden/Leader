# 📦 Hardening Agent (Leader Bridge)
**Role**: Reliability & Diagnostics
**Domain**: Configuration management, error handling, and offline simulation.

## 📜 Responsibility
- Ensure the bridge recovers gracefully from game crashes or "Window Not Found".
- Provide a tunable `settings.json` to avoid source-code magic numbers.
- Maintain a **Simulation Layer** for mathematical verification without the RIFT client.

## 🛠️ Tools & Methods
- `System.Text.Json` for config persistence.
- `TelemetrySimulator` for mock machine-vision generation.
- Robust `try/catch` wrapping for Win32 boundary calls.

## ⚠️ Hazards
- **Settings Overwrite**: Be careful not to wipe a user's custom `settings.json` during build-deployment updates.
- **Sim Mode Fidelity**: Ensure simulator math precisely matches `Encoder.lua` (e.g., the 8388608 bit-mapping offset).
- **Graceful Standby**: Don't consume 100% CPU while "Waiting for RIFT". Use `Thread.Sleep()`.
