# 📐 Leader Window Resizer

`C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\Leader\LeaderWindowResizer` is a dedicated live helper for changing the **RIFT client-area resolution** without guessing outer window sizes.

It is optimized for the current Leader workflow:

- detect live `rift_x64` / `RIFT` windows
- identify them explicitly by **PID** and **HWND**
- resize by **client area**, not outer frame
- support common presets like `320x180`, `640x360`, and `1280x720`
- support custom sizes
- support **dynamic scaling** from the current live size
- optionally run an immediate **Leader strip decode** after resize

---

## Launcher

Generic launcher:

- `C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\Leader\run_window_resizer.bat`

One-click preset launchers:

- `C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\Leader\resize_320x180.bat`
- `C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\Leader\resize_640x360.bat`
- `C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\Leader\resize_1280x720.bat`
- `C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\Leader\resize_half_current.bat`

---

## Common Examples

### Resize the first live RIFT client to `320x180`

```bat
run_window_resizer.bat --preset 320x180 --inspect
```

### Resize to `640x360`

```bat
run_window_resizer.bat --preset 640x360 --inspect
```

### Resize to `1280x720`

```bat
run_window_resizer.bat --preset 1280x720 --inspect
```

### Custom resolution

```bat
run_window_resizer.bat --size 960x540 --inspect
```

### Dynamic half-size from the current live client

```bat
run_window_resizer.bat --scale 0.5 --inspect
```

### Resize a specific live window by PID

```bat
run_window_resizer.bat --pid 127928 --preset 640x360 --inspect
```

### Resize a specific live window by HWND

```bat
run_window_resizer.bat --hwnd 0x123456 --size 485x309 --inspect
```

### Resize all detected clients and stagger them

```bat
run_window_resizer.bat --all --preset 640x360 --left 45 --top 108 --step-x 24 --step-y 24 --inspect
```

### Give the client one second to settle before inspecting

```bat
run_window_resizer.bat --preset 485x309 --inspect --wait-ms 1000
```

---

## Supported Options

- `--list`
  - list detected RIFT windows
- `--index N`
  - target the `N`th detected window
- `--all`
  - target all detected windows
- `--pid N`
  - target/filter by process id
- `--hwnd HEX`
  - target/filter by window handle
- `--title-contains TEXT`
  - target/filter by title substring
- `--preset NAME`
  - presets:
    - `320x180`
    - `485x309`
    - `640x360`
    - `1280x720`
    - aliases like `180p`, `360p`, `720p`
- `--size WxH`
  - custom client resolution
- `--width N --height N`
  - custom client resolution
- `--scale F`
  - multiply the current client size by `F`
- `--left N --top N`
  - absolute position for the first resized window
- `--step-x N --step-y N`
  - offset applied per window when resizing multiple clients
- `--inspect`
  - immediately capture and decode the Leader strip after resizing
- `--wait-ms N`
  - wait before the post-resize inspection capture
  - useful because RIFT can need a short settle period after resize

---

## Notes

- This helper changes the **live window size**; it does not patch game memory.
- It targets the **client area**, so `640x360` means the actual drawable RIFT client resolution.
- `--inspect` is useful when testing whether a new resolution still yields a valid Leader strip decode.
