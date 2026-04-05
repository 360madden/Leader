# ⌨️ Leader Bridge: Control Keys

This document outlines the default key mappings, scan codes, and Win32 Virtual Keys (VK) utilized by the Leader Bridge to send inputs into background RIFT windows via `PostMessage`.

## Why ScanCodes + VK?
RIFT is a DirectX game, and its input handler expects hardware-level ScanCodes (Set 1) rather than standard Virtual Keys sent by `SendKeys` or basic `PostMessage` scripts. The `InputEngine` uses a composite message format to trick the game into accepting inputs even while minimized or out-of-focus.

> **Technical Detail**: The `PostMessage` `lParam` for `WM_KEYDOWN` must include the hardware ScanCode shifted into bits 16-23.

> **Current navigation assumption**: the coordinate-only follow controller expects `Q` / `E` to function as strafe keys for the follower, while `A` / `D` remain turn keys.

---

## 🎮 Movement Keys

| Action | Virtual Key (VK) | ScanCode (Set 1) | Hex Value |
| :--- | :--- | :--- | :--- |
| **Move Forward** | `W` | `0x11` | `VK: 0x57` |
| **Strafe Left** | `Q` | `0x10` | `VK: 0x51` |
| **Move Backward** | `S` | `0x1F` | `VK: 0x53` |
| **Strafe Right**| `E` | `0x12` | `VK: 0x45` |
| **Turn Left** | `A` | `0x1E` | `VK: 0x41` |
| **Turn Right**| `D` | `0x20` | `VK: 0x44` |
| **Jump** | `Space` | `0x39` | `VK: 0x20` |

---

## ⚔️ Combat & Action Keys

| Action | Key | Virtual Key (VK) | ScanCode (Set 1) | Notes |
| :--- | :--- | :--- | :--- | :--- |
| **Assist / Interact** | `F` | `0x46` | `0x21` | Tapped to assist the Leader's target. Cooldown locked to prevent spam. |
| **Mount / Dismount** | `M` | `0x4D` | `0x32` | Triggered when the FollowController detects a mount state mismatch. |
| **Action Bar 1** | `1` | `0x31` | `0x02` | `Num1` (Top row, not Numpad) |
| **Action Bar 2** | `2` | `0x32` | `0x03` | `Num2` |
| **Action Bar 3** | `3` | `0x33` | `0x04` | `Num3` |
| **Action Bar 4** | `4` | `0x34` | `0x05` | `Num4` |
| **Action Bar 5** | `5` | `0x35` | `0x06` | `Num5` |

---

## ⚙️ Bridge Engine Global Hotkeys
These keys are intercepted globally by the `GlobalHotkeyService.cs` thread, meaning they work regardless of whether the Bridge or the RIFT client has Window Focus.

- `ScrollLock`: **Toggle Pursuit**. Globally pauses/resumes the Follow & Steer logic. (Useful for pausing followers when you need to walk the leader through a tight door).
- `T`: Console-level toggle for Pursuit (Only works if the Bridge terminal is focused).
- `L`: Console-level toggle for CSV Telemetry Logging.
- `S`: Save a manual diagnostic `.PNG` snapshot of the telemetry strip for all active windows.
- `R`: Hot-reload the `settings.json` config variables (allows tuning PD steering rates live).

## Adding Custom Keys
If you need to add obscure keys (like `Ctrl+Shift+Alt+F13`), you must map them in `InputEngine.cs` inside the `RiftKey` Enum, and provide the correct Set 1 ScanCode. 

**Example Custom Action:**
```csharp
// Inside InputEngine.RiftKey enum
F13 = 0x64, // F13 ScanCode

// Inside vkMap initializer
_vkMap[(int)RiftKey.F13] = VK.F13;
```
*(Note: Modifiers like Shift/Ctrl require separate `KEYDOWN` messages ahead of the target key, and `KEYUP` after).*
