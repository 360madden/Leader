# 🛰️ Leader — Optical Telemetry Bridge

A high-performance, hybrid **Lua + C#** multiboxing bridge for RIFT.  
Transmits real-time 3D spatial telemetry from the Leader game instance to up to 4 Follower instances via an optical pixel-strip encoded in the game UI — no third-party software hooks required.

---

## Architecture

```
┌─ RIFT Client (Leader) ──────────────────────────────────┐
│  Gatherer.lua  →  Encoder.lua  →  Renderer.lua         │
│  [game state]     [pack RGB]     [7-pixel strip @ 0,0] │
└─────────────────────────────────────────────────────────┘
                          │  (screen capture)
┌─ LeaderDecoder.exe ─────▼───────────────────────────────┐
│  CaptureEngine → TelemetryService → NavigationKernel   │
│  [GDI BitBlt]    [decode RGB]       [bearing + EMA]    │
│                                                         │
│  FollowController → InputEngine → RIFT Followers        │
│  [PD steering]     [PostMessage]   [background input]  │
└─────────────────────────────────────────────────────────┘
```

## Telemetry Protocol v1.1

| Pixel | X offset | R | G | B | Description |
|-------|----------|---|---|---|-------------|
| 0 | 0 | 255 | 0 | 255 | **Sync beacon** (magenta) |
| 1 | 4 | playerHP (0-255) | targetHP (0-255) | flags bitfield | **Status** |
| 2 | 8 | CoordX low | CoordX mid | CoordX high | **X coordinate** |
| 3 | 12 | CoordY low | CoordY mid | CoordY high | **Y (elevation)** |
| 4 | 16 | CoordZ low | CoordZ mid | CoordZ high | **Z coordinate** |
| 5 | 20 | facing low | facing high | zone hash | **Heading + Zone** |
| 6 | 24 | target ID R | target ID G | target ID B | **Target hash** |

**Coordinate encoding:** `n = floor(val × 10) + 8388608` — range ±838 860 m, 0.1 m precision  
**Heading encoding:** `n = floor(radian × 10000)` — range 0 → 2π, ~0.0001 rad precision  
**Flags bitfield:** bit 0=Combat, 1=HasTarget, 2=IsMoving, 3=IsAlive, 4=IsMounted

---

## Quick Start

### 1. Install the Addon
Copy the `Leader/` folder to:
```
RIFT\Interface\AddOns\Leader\
```

### 2. Build the Bridge
```bat
run_bridge.bat
```
This will `dotnet build -c Release` and launch `LeaderDecoder.exe`.  
Requires [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

### 3. In-Game Setup
- Load into RIFT with the **Leader** addon enabled.  
- The 28×4 pixel strip will appear in the **top-left corner** of every instance.  
- Type `/leader diag` to toggle the real-time telemetry audit overlay. If `/leader` is unavailable because another addon already registered it, use `/leaderbridge diag` instead.

### 4. Bridge Controls

| Key | Action |
|-----|--------|
| `T` | Toggle **Follow / Pursuit** for all followers |
| `L` | Toggle **CSV telemetry logging** to `debug/telemetry_log.csv` |
| `S` | Save a **diagnostic snapshot** of the current optical strip |

---

## Configuration (`settings.json`)

Auto-generated on first run next to `LeaderDecoder.exe`:

```json
{
  "CaptureWidth": 28,
  "CaptureHeight": 4,
  "TargetFPS": 30,
  "FollowDistanceMin": 1.5,
  "FollowDistanceMax": 3.5,
  "AngleTolerance": 0.15,
  "TurnP": 0.8,
  "TurnD": 0.2,
  "AssistDistance": 5.0,
  "KeyForward": 17,
  "KeyLeft": 30,
  "KeyBack": 31,
  "KeyRight": 32,
  "KeyJump": 57,
  "KeyMount": 50,
  "KeyInteract": 33
}
```

**PD Tuning Reference:**
- `TurnP` — Rotation intensity. Higher = more aggressive turning. Default `0.8`
- `TurnD` — Rotation damping. Higher = less overshoot jitter. Default `0.2`
- `FollowDistanceMin/Max` — Deadzone band in metres. Followers hold inside, chase outside.
- `AssistDistance` — Max range for automatic Interact (F key) combat assist.

---

## Simulation Mode

Test pursuit logic **without a live RIFT server**:
```bat
run_sim.bat
```
Generates a synthetic leader orbiting in a circle. Press `T` to toggle following.

---

## Diagnostics

All diagnostic output goes to `debug/` next to `LeaderDecoder.exe`:

| File | Description |
|------|-------------|
| `debug/telemetry_log.csv` | Per-frame X,Y,Z,Heading log (toggle with `L`) |
| `debug/sync_failure_*.png` | Auto-saved when Pixel 0 sync is lost |
| `debug/manual_snap_*.png`  | Manual snapshots saved with `S` |

### Non-Invasive Inspection Helpers

Use these helper launchers when you want to validate the strip without changing the client:

| Helper | Purpose |
|--------|---------|
| `run_live_inspector.bat` | Captures the live top-left `28x4` region from visible RIFT windows and reports sync/decode status plus saved/live window geometry context; supports targeting by `--pid`, `--hwnd`, and `--title-contains` |
| `run_input_probe.bat` | Sends deterministic background key probes to explicit RIFT clients by `--pid`, `--pids`, `--hwnd`, or `--hwnds`, with optional strip inspection before and after the probe |
| `run_screenshot_inspector.bat` | Inspects the latest RIFT screenshot, or a chosen image path, for the Leader strip |

### Window Resize Helper

Use these helper launchers when you want to move between known-good live client sizes quickly:

| Helper | Purpose |
|--------|---------|
| `run_window_resizer.bat` | Generic client-area resize tool with presets, custom sizes, dynamic `--scale`, targeting by `--pid` / `--hwnd`, optional multi-window offsets, and immediate `--inspect` validation |
| `resize_320x180.bat` | One-click resize to `320x180` plus immediate strip validation |
| `resize_640x360.bat` | One-click resize to `640x360` plus immediate strip validation |
| `resize_1280x720.bat` | One-click resize to `1280x720` plus immediate strip validation |
| `resize_half_current.bat` | One-click dynamic half-size resize from the current live client size |

Current field notes live in:
- `docs/non-invasive-validation.md`
- `docs/window-resizer.md`

---

## Module Reference

### Lua (Leader Client)
| Module | Role |
|--------|------|
| `Gatherer.lua` | Polls RIFT Inspect API; returns typed telemetry packet |
| `Encoder.lua` | Packs float values into 24-bit RGB components |
| `Renderer.lua` | Writes 7 opaque 4×4 colour blocks at (0,0) |
| `DiagnosticUI.lua` | In-game readable overlay for live data validation |
| `main.lua` | 30Hz orchestration loop, slash commands |

### C# (LeaderDecoder)
| Service | Role |
|---------|------|
| `CaptureEngine` | DPI-aware GDI screen capture at client (0,0) |
| `TelemetryService` | RGB → `GameState` inverse-decode |
| `NavigationKernel` | Bearing, EMA heading, zone-change detection |
| `FollowController` | PD-lite steering, mount sync, target assist |
| `InputEngine` | Async Win32 `PostMessage` + VK + ScanCode injection |
| `DiagnosticService` | PNG snapshots, CSV logger |
| `ConfigManager` | `settings.json` load/save |
| `TelemetrySimulator` | Offline mock pixel-strip generator |
| `MemoryEngine` | `ReadProcessMemory` skeleton for future enrichment |

---

## Compliance

This system operates as a **presentation-layer observer** only:
- Reads the RIFT UI the same way a human would see the screen.
- Sends only **one action at a time** per follower (no macros, no burst input).
- Uses the officially published multiboxing standard of "one keystroke = one action".

---

## License

MIT — see `LICENSE`
