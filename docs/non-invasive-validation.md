# 🔎 Non-Invasive Validation Notes

This document tracks what we know about the **Leader** addon using only non-invasive evidence:

- saved addon settings
- live top-left strip capture
- in-game screenshots
- helper tools that only read pixels or saved files

No input injection, memory edits, or client modification are part of this workflow.

---

## Helper Apps

### 1. Live Strip Inspector
Project:
- `C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\Leader\LeaderLiveInspector`

Launcher:
- `C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\Leader\run_live_inspector.bat`

Purpose:
- enumerates visible RIFT windows
- captures only the top-left `28x4` client-area strip
- reports sync validity, raw RGB samples, and decoded telemetry fields
- also reports saved `rift.cfg` resolution/window mode and live client geometry so capture mismatches are easier to explain

### 2. Screenshot Inspector
Project:
- `C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\Leader\LeaderScreenshotInspector`

Launcher:
- `C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\Leader\run_screenshot_inspector.bat`

Purpose:
- inspects the latest screenshot, or a chosen screenshot path
- samples the top-left `28x4` region
- reports whether the Leader sync strip is visible in the saved image

---

## What We Know So Far

### Environment observations on April 4, 2026

- A live RIFT client process was observed as:
  - `rift_x64.exe`
  - started at **April 4, 2026 12:37:13 PM**
- The account settings file below had the addon enabled:
  - `C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\Saved\rift315.1@gmail.com\AddonSettings.lua`
  - contained `Leader = "enabled"`
- The saved variables file below existed and had recent write activity:
  - `C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\Saved\rift315.1@gmail.com\Deepwood\Atank\SavedVariables\Leader.lua`
  - last observed write: **April 4, 2026 1:08:47 PM**

### Screenshot evidence

#### Screenshot 1
- File:
  - `C:\Users\mrkoo\OneDrive\Documents\RIFT\Screenshots\2026-04-04_132151.jpg`
- Timestamp:
  - **April 4, 2026 1:21:51 PM**
- Dimensions:
  - `485x309`
- Result:
  - the Leader strip was **not clearly visible**
  - the diagnostic overlay was **not visible**

#### Screenshot 2
- File:
  - `C:\Users\mrkoo\OneDrive\Documents\RIFT\Screenshots\2026-04-04_132344.jpg`
- Timestamp:
  - **April 4, 2026 1:23:44 PM**
- Dimensions:
  - `2560x1369`
- Result:
  - the top-left Leader strip **was visible**
  - sampled sync values were near the expected magenta beacon, for example:
    - `(255, 0, 250)`
    - `(255, 1, 255)`
- Conclusion:
  - by the time of this screenshot, the addon was visibly rendering the strip

### Live capture evidence

- A direct live client-area capture of the top-left `28x4` region taken before the later screenshot did **not** show the expected magenta sync strip.
- After the client was maximized and re-screenshot, the later screenshot **did** show the strip.

### Current interpretation

- The addon is installed.
- It is enabled in at least one saved RIFT profile.
- It has recent saved-variable activity.
- It was visibly rendering the telemetry strip in the later screenshot from **April 4, 2026 1:23:44 PM**.

This is strong evidence that the addon can run live in the client.

---

## Helper App Validation Runs

### Screenshot helper run

Run result observed on **April 4, 2026 1:23:44 PM** screenshot:

- Tool:
  - `LeaderScreenshotInspector`
- Input:
  - `C:\Users\mrkoo\OneDrive\Documents\RIFT\Screenshots\2026-04-04_132344.jpg`
- Result:
  - `VALID`
- Sampled sync block:
  - `P0 = 255, 1, 255`
- Decoder output:
  - the helper accepted the strip as a valid Leader frame

### Live helper run

Run result observed on **April 4, 2026 1:30:07 PM** against the live window:

- Tool:
  - `LeaderLiveInspector`
- Window count:
  - `1`
- Window title:
  - `RIFT`
- Result:
  - `INVALID`
- Sampled sync block:
  - `P0 = 28, 33, 28`
- Decoder output:
  - no magenta sync strip was present in the raw top-left client capture at that moment

### Live helper run with ChromaLink-style window/config context

Run result observed on **April 4, 2026 1:33:28 PM** against the live window:

- Tool:
  - `LeaderLiveInspector`
- Saved config:
  - `C:\Users\mrkoo\AppData\Roaming\RIFT\rift.cfg`
  - saved resolution: `485x309`
  - saved window mode: `0`
  - saved maximized flag: `False`
- Live window:
  - window rect: `45,108 501x348`
  - client rect: `53,139 485x309`
  - minimized: `False`
  - saved resolution matches live client: `True`
- Result:
  - `INVALID`
- Sampled sync block:
  - `P0 = 28, 33, 28`

Interpretation:
- the current failure is **not** explained by a saved-vs-live resolution mismatch
- the client is running at the same `485x309` size recorded in `rift.cfg`
- the strip is still missing from the raw top-left client capture even though the later screenshot showed it

### Raw live capture structure clue

After saving a raw live strip capture and inspecting the full pixel rows:

- the saved live crop still decoded as `INVALID`
- but the **very first pixel** in the live capture was:
  - `x=0, y=0 -> 255,0,255`
- the expected sample centre for block 0 (`x=2, y=2`) was:
  - `25,30,25`

Interpretation:
- the live capture does contain **some strip-like color right at the top-left edge**
- but the strip is **not occupying the expected 4x4 block geometry** inside the raw client capture
- this points more toward **vertical clipping / sampling mismatch / partial rendering at the top edge** than a simple “addon not loaded” failure

This also explains the earlier disagreement:
- screenshot evidence can still show the strip visually
- while the raw `28x4` decoder path fails because the strip is not sitting cleanly inside the expected sampling row/column layout

### Main blocker now

The screenshot helper and the live helper disagree:

- the saved screenshot from **1:23:44 PM** shows a decodable strip
- the direct live capture from **1:30:07 PM** does not

That discrepancy is now the main non-invasive validation problem to solve.

---

## Limits of Current Evidence

- JPEG screenshots are not equivalent to the raw `BitBlt` capture path used by `LeaderDecoder`.
- A screenshot proving the strip is visible does not automatically prove the bridge can decode it reliably every frame.
- The exact relationship between window state (for example maximized vs. non-maximized) and reliable strip capture still needs direct validation with the new helper tools.
- The live client may be rendering the strip in a way that screenshots preserve but raw top-left client capture does not currently reproduce.

---

## Next Recommended Checks

1. Use `run_live_inspector.bat` while the game remains live.
2. Compare live strip samples before and after window-state changes.
3. Confirm whether the live inspector reports a valid sync beacon continuously.
4. Only after that, revisit the full bridge runtime path.
