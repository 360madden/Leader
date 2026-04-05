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

## Telemetry Protocol v1.2

| Pixel | X offset | R | G | B | Description |
|-------|----------|---|---|---|-------------|
| 0 | 0 | 255 | 0 | 255 | **Sync beacon** (magenta) |
| 1 | 4 | playerHP (0-255) | targetHP (0-255) | flags bitfield | **Status** |
| 2 | 8 | CoordX low | CoordX mid | CoordX high | **X coordinate** |
| 3 | 12 | CoordY low | CoordY mid | CoordY high | **Y (elevation)** |
| 4 | 16 | CoordZ low | CoordZ mid | CoordZ high | **Z coordinate** |
| 5 | 20 | heading low | heading high | zone hash | **Movement heading + Zone** |
| 6 | 24 | player tag R | player tag G | player tag B | **Player identity tag** |

**Coordinate encoding:** `n = floor(val × 10) + 8388608` — range ±838 860 m, 0.1 m precision  
**Heading encoding:** `n = floor(radian × 10000)` — range 0 → 2π, ~0.0001 rad precision. This is the leader's derived travel heading from recent coordinate deltas, not a documented unit-facing field.  
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
- The 56×8 pixel strip will appear in the **top-left corner** of every instance.  
- Type `/leader diag` to toggle the real-time telemetry audit overlay. If `/leader` is unavailable because another addon already registered it, use `/leaderbridge diag` instead.

### 4. Bridge Controls

| Key | Action |
|-----|--------|
| `T` | Toggle **Follow / Pursuit** for all followers |
| `L` | Toggle **decoder-side CSV telemetry logging** to `debug/telemetry_log.csv` |
| `S` | Save a **diagnostic snapshot** of the current optical strip |

---

## Configuration (`settings.json`)

Auto-generated on first run next to `LeaderDecoder.exe`:

```json
{
  "CaptureWidth": 56,
  "CaptureHeight": 8,
  "TargetFPS": 30,
  "FollowDistanceMin": 1.5,
  "FollowDistanceMax": 3.5,
  "FollowEngageDistanceMax": 20.0,
  "AngleTolerance": 0.15,
  "TurnP": 0.8,
  "TurnD": 0.2,
  "AssistDistance": 5.0,
  "KeyForward": 17,
  "KeyLeft": 16,
  "KeyBack": 31,
  "KeyRight": 18,
  "KeyJump": 57,
  "KeyMount": 50,
  "KeyInteract": 33
}
```

**PD Tuning Reference:**
- `FollowEngageDistanceMax` — Followers only attempt coordinate-follow when the leader is within this range. Default `20.0`
- `TurnP` — Rotation intensity. Higher = more aggressive turning. Default `0.8`
- `TurnD` — Rotation damping. Higher = less overshoot jitter. Default `0.2`
- `FollowDistanceMin/Max` — Deadzone band in metres. Followers hold inside, chase outside.
- `AssistDistance` — Max range for automatic Interact (F key) combat assist.

> Note: the current follow controller is now coordinate-only and does not use the legacy turn-tuning values above for active steering. They remain in `settings.json` for compatibility. The follower currently assumes `Q` / `E` are the strafe inputs, while `A` / `D` remain turn keys.

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
| `debug/telemetry_log.csv` | Per-frame X,Y,Z,Heading log written by the decoder (toggle with `L`) |
| `debug/sync_failure_*.png` | Auto-saved when Pixel 0 sync is lost |
| `debug/manual_snap_*.png`  | Manual snapshots saved with `S` |

### Non-Invasive Inspection Helpers

Use these helper launchers when you want to validate the strip without changing the client:

| Helper | Purpose |
|--------|---------|
| `run_live_inspector.bat` | Captures the live top-left `56x8` region from visible RIFT windows and reports sync/decode status plus saved/live window geometry context; supports targeting by `--pid`, `--hwnd`, and `--title-contains`, plus watch-mode CSV sample/event logging for profile, validity, geometry, and minimized-state anomalies |
| `run_input_probe.bat` | Sends deterministic background key probes to explicit RIFT clients by `--pid`, `--pids`, `--hwnd`, or `--hwnds`, with optional settings-resolved `--action` probes, before/after telemetry classification, CSV result logging, and a safe `--summary` mode that highlights accepted vs ignored probes by key/action |
| `run_input_verifier.bat` | Verifies whether configured controller actions such as forward/back/left/right produce detectable strip/coordinate changes, with action-aware before/after classification and CSV result logging |
| `run_screenshot_inspector.bat` | Inspects the latest RIFT screenshot, or a chosen image path, for the Leader strip |

### Addon-Side Dump Logging

The addon can persist a small rolling telemetry buffer into its SavedVariables file for helper-app consumption:

| Control | Purpose |
|---------|---------|
| `/leader dump status` | Show whether dump logging is enabled and how many entries are buffered |
| `/leader dump on` | Enable telemetry dump buffering |
| `/leader dump off` | Disable telemetry dump buffering |
| `/leader dump toggle` | Toggle telemetry dump buffering |
| `/leader dump clear` | Clear buffered dump entries |
| `/leader dump interval <seconds>` | Adjust the dump throttle interval |

Buffered samples are stored in the character SavedVariables file alongside `LeaderConfig` and are intended for offline/helper-app correlation, not live file IO from the addon. Use `/leader dump toggle` to switch buffering on or off.

### Addon-Side Runtime Status

The addon now also maintains a lightweight runtime heartbeat in `LeaderConfig.runtime` so helper tools can tell the difference between:

- addon loaded but waiting for valid packets
- renderer alive but no recent telemetry
- slash command fallback / registration differences
- repeated nil-packet streaks during loading or transition periods

Use:

| Control | Purpose |
|---------|---------|
| `/leader status` | Print the current runtime heartbeat summary |

Runtime status keeps a bounded event log plus fields such as the current player tag, zone hash, last valid packet time, nil-packet streak, dump state, and active slash command.

### Addon-Side Transition State

The addon now also tracks likely loading / transition periods in `LeaderConfig.transition` so helper tools can separate:

- repeated nil-packet periods
- zone-change stabilization
- recovered telemetry after a dead/empty stretch

Use:

| Control | Purpose |
|---------|---------|
| `/leader transition` | Print the current transition/loading summary |

Transition state keeps a bounded history plus counts for transition activations and recoveries, along with the current reason, zone hash, and nil-packet streak.

### Addon-Side Debug Export

The addon now also maintains a compact machine-readable snapshot in `LeaderConfig.debugExport` so external helpers can read one canonical internal state blob instead of stitching together multiple tables.

Use:

| Control | Purpose |
|---------|---------|
| `/leader export` | Print the current debug-export summary |

The export snapshot includes:

- latest packet summary
- runtime heartbeat summary
- transition/loading summary
- dump logging summary

This is intended for offline/helper-app consumption from SavedVariables, not live file output from the addon.

### Addon-Side Renderer Health

The addon now also tracks strip-write health in `LeaderConfig.renderHealth` so you can tell whether the Lua renderer actually completed its 7-pixel write cycle and when layout resyncs occurred.

Use:

| Control | Purpose |
|---------|---------|
| `/leader render` | Print the current renderer-health summary |

Renderer health keeps:

- last render frame sequence
- last pixel-write count
- whether the frame completed the expected strip write
- layout resync count
- current pixel size / client width snapshot
- bounded history for incomplete frames and layout changes

### Addon-Side Capability Status

The addon now tracks which internal subsystems initialized successfully in `LeaderConfig.capabilities`, which helps distinguish partial addon startup from packet/render issues that external helpers cannot see directly.

Use:

| Control | Purpose |
|---------|---------|
| `/leader capabilities` | Print the current addon capability summary |
| `/leader caps` | Short alias for capability status |

Capability status keeps:

- slash registration state and active primary slash command
- runtime / transition / export / render-health module readiness
- mini status-badge readiness
- renderer / diag / dump subsystem readiness
- bounded capability-change history

### Addon-Side Status Badge

The addon now includes a tiny optional in-game badge for quick client-health checks without opening the full diagnostic overlay.

Use:

| Control | Purpose |
|---------|---------|
| `/leader badge` | Toggle the mini status badge |
| `/leader badge on` | Force the badge visible |
| `/leader badge off` | Hide the badge |
| `/leader badge status` | Print whether the badge is currently visible |

The badge shows:

- overall state (`OK`, `WAIT`, or `WARN`)
- packet activity
- render write count
- transition reason
- current player tag

### Addon-Side Session Timeline

The addon now maintains a bounded session timeline in `LeaderConfig.timeline` so external tools can read one chronological stream of high-level addon events instead of stitching together multiple module histories.

Use:

| Control | Purpose |
|---------|---------|
| `/leader timeline` | Print the current timeline summary |

The session timeline records:

- session initialization
- no-packet outages and recovery
- zone-hash changes
- dump / badge command actions
- render-frame failures and layout resyncs

### Addon-Side Session Statistics

The addon now keeps a rolling session summary in `LeaderConfig.sessionStats` for quick post-run analysis without replaying raw packet dumps.

Use:

| Control | Purpose |
|---------|---------|
| `/leader stats` | Print the current session statistics summary |

The session stats track:

- valid-packet and no-packet tick counts
- packet recovery count
- zone-change count
- render failures and layout resync counts
- command usage counts for dump / badge / diag
- last seen player tag and zone hash

### Window Resize Helper

Use these helper launchers when you want to move between known-good live client sizes quickly:

| Helper | Purpose |
|--------|---------|
| `run_window_resizer.bat` | Generic client-area resize tool with presets, custom sizes, dynamic `--scale`, targeting by `--pid` / `--hwnd`, optional multi-window offsets, and immediate `--inspect` validation |
| `resize_320x180.bat` | One-click resize to `320x180` plus immediate strip validation |
| `resize_640x360.bat` | One-click resize to `640x360` plus immediate strip validation |
| `resize_1280x720.bat` | One-click resize to `1280x720` plus immediate strip validation |
| `resize_half_current.bat` | One-click dynamic half-size resize from the current live client size |

### Trace Bundle Helper

Use this helper when you want a single artifact bundle for a bad run:

| Helper | Purpose |
|--------|---------|
| `run_trace_bundle.bat` | Creates a timestamped trace bundle containing current logs, helper debug artifacts, addon saved files such as `AddonSettings.lua` / `SavedVariables\\Leader.lua`, settings snapshots, a live window inventory, and a bounded set of recent images for later review |

Current field notes live in:
- `docs/non-invasive-validation.md`
- `docs/window-resizer.md`
- `docs/navigation-controller.md`

---

## Module Reference

### Lua (Leader Client)
| Module | Role |
|--------|------|
| `Gatherer.lua` | Polls RIFT Inspect API; returns typed telemetry packet |
| `Encoder.lua` | Packs float values into 24-bit RGB components |
| `Renderer.lua` | Writes 7 opaque 8×8 colour blocks at (0,0) |
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
