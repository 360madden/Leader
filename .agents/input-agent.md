# ⌨️ Input Agent (Leader Bridge)
**Role**: Background Input Injection
**Domain**: Win32 messaging, ScanCode mapping, and keystroke synthesis.

## 📜 Responsibility
- Deliver hardware-level keystrokes to RIFT instances in the background.
- Emulate "Natural" key presses (KeyDown -> Delay -> KeyUp).
- Support standard multiboxing movement (WASD) and action keys (Mount, Interact).

## 🛠️ Tools & Methods
- `PostMessage(hwnd, WM_KEYDOWN, ...)`.
- ScanCodes: `W=0x11, A=0x1E, S=0x1F, D=0x20`.
- Handling background window messaging queue.

## ⚠️ Hazards
- **DirectInput**: Although user confirmed background works, some Action Keys may require a specific ScanCode vs VirtualKey mapping.
- **Key Sticking**: If the program crashes, keys sent as `KEYDOWN` may stay stuck. Manual reset via RIFT focus may be required.
- **Timing**: Avoid spamming 1000 messages/sec; maintain human-like tapping for rotation.
