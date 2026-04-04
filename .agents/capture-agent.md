# 🛰️ Capture Agent (Leader Bridge)
**Role**: High-performance Win32 GDI Optics
**Domain**: Low-latency screen capture and window management.

## 📜 Responsibility
- Identify and sort RIFT game windows (`RIFT1` through `RIFT5`).
- Perform 0ms-latency `BitBlt` transfers of the top-left 28x4 window region.
- Ensure 1:1 pixel fidelity for machine-vision sampling.

## 🛠️ Tools & Methods
- `user32.dll` / `gdi32.dll` P/Invoke.
- `CaptureRegion(IntPtr hwnd, 28, 4)`
- Threading: 30Hz target capture loop.

## ⚠️ Hazards
- **UI Scaling**: If the game is at non-standard DPI, the sampling might shift. Currently mitigated by Lua `UI.CreateContext`.
- **Z-Order**: Ensure the game window is rendering even if eclipsed (though GDI requires the window to be visible/active for standard `PrintWindow` or `BitBlt` in some window modes).
