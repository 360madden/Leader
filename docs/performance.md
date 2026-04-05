# ⏱️ Performance & Profiling (Telemetry Bridge)

Maintaining a minimum of 30Hz telemetry data polling means the entire Decode and Input loop for all 5 windows must complete within a strict **33.3ms budget** (ideally <16ms equivalent to 60fps). 

This document details the optimizations and how to profile the `LeaderDecoder`.

## CPU & Capture Profiling

The costliest function in the pipeline is `BitBlt` via the native `CaptureEngine.cs`. Capturing full windows is too slow, which is why we only sample a 56x8 area exactly at `(0,0)`.

### Profiling Tools Included
The Bridge console automatically profiles every frame using a high-precision `Stopwatch` tied to `cycleSw.ElapsedMilliseconds`.
Look at the Bridge dashboard:
> `[BRIDGE_STATUS] Detected: 5/5 | Latency: 2ms | PURSUIT: ACTIVE`

If your latency metric hits `> 16ms`:
1. **Disable HDR**: Windows HDR causes internal format conversions during GDI bit-blitting, ballooning the cost per capture from ~0.2ms to over 15ms.
2. **Disable Scaling / DWM Overlays**: Ensure that Discord Overlays or similar GPU hooks are not injecting into the RIFT client.
3. **Verify Sleep Timer**: By default, Windows timer resolution is 15.6ms. The Bridge C# pipeline is configured to yield execution cleanly, but if the console is running in the background, you might see scheduling spikes.

## Memory Hardening

Since `LeaderDecoder` runs continuously for hours, GC (Garbage Collection) pauses must be avoided.
- **Bitmap Disposals**: Inside `CaptureEngine.cs`, `Bitmap` and `Graphics` objects are wrapped in `using` statements and immediately explicitly disposed to avoid pushing them to Gen 2 GC.
- **Data Structs**: We use `struct`-like lightweight passing for the `GameState` and reuse them (modifying the existing 5 slots) instead of instantiating new objects every loop.

## In-Game Optimization (Lua)

In `main.lua` and `Gatherer.lua`:
```lua
-- Runs precisely on the game's UI render step
Command.Event.Attach(Event.System.Update.Begin, mainLoop, "LeaderTelemetryUpdate")
```
- **Why Event.System.Update.Begin?** Instead of `Event.System.Update.End` or a generic timer, `.Begin` ensures that our `Renderer.lua` (the color squares) strictly changes *before* that frame is rasterized and sent to the C# Bridge, minimizing latency between a true game-state change and the optical flash on screen.
- **Garbage Generation**: The Lua script limits the instantiation of new tables by reusing a single `_packet` cache memory map for `PlayerHP`, `Targets`, and Coordinates.

If you experience RIFT framerate drops on the Leader account, disable the on-screen diagnostic text via `/leader diag`, or `/leaderbridge diag` if `/leader` is unavailable, to ensure Font Rendering strings are skipped.
