# Tomorrow Workflow

Use this as the fastest safe restart point for the next development session.

## Current Focus

- **Primary area:** helper-app UX and addon reliability
- **Addon work already completed:**
  - stale telemetry clears when no valid packet is available
  - dump toggle is wired in the Lua slash-command flow
  - packet/docs now describe **player tag** on pixel 6
  - dump schema defaults to version `2`
- **Helper-app work currently in progress:**
  - colorized, better-organized `--help` output for the existing C# helper tools

## Current Local State

At the end of today, the main active local changes are in these helper apps:

- `LeaderDecoder/Program.cs`
- `LeaderInputProbe/Program.cs`
- `LeaderInputVerifier/Program.cs`
- `LeaderLiveInspector/Program.cs`
- `LeaderTraceBundle/Program.cs`
- `LeaderWindowResizer/Program.cs`

Those changes are focused on **console help formatting only**, not runtime behavior.

## Best Start Sequence

### 1. Re-orient

Open the repo root:

```powershell
cd C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\Leader
git status --short
```

Confirm the working tree still matches the helper-help pass above.

### 2. Build the touched helper apps first

These are the fastest confidence checks for the current local changes:

```powershell
dotnet build .\LeaderDecoder\LeaderDecoder.csproj -c Release
dotnet build .\LeaderInputProbe\LeaderInputProbe.csproj -c Release
dotnet build .\LeaderInputVerifier\LeaderInputVerifier.csproj -c Release
dotnet build .\LeaderLiveInspector\LeaderLiveInspector.csproj -c Release
dotnet build .\LeaderWindowResizer\LeaderWindowResizer.csproj -c Release
dotnet build .\LeaderTraceBundle\LeaderTraceBundle.csproj -c Release
```

### 3. Check live-client baseline before deeper changes

If RIFT clients are running, use the lowest-risk inspection path first:

```powershell
.\run_live_inspector.bat --list
```

Then only target explicit windows by `--pid`, `--pids`, `--hwnd`, or `--hwnds`.

### 4. Use explicit validation helpers before changing control logic

- `run_live_inspector.bat` → verify strip presence, sync, and geometry
- `run_input_probe.bat` → verify raw background input acceptance
- `run_input_verifier.bat` → verify configured actions produce detectable change
- `run_trace_bundle.bat` → preserve evidence before restarting clients or changing live state

## Best Practices for Tomorrow

- **Prefer explicit window targeting.** Use PID/HWND filters instead of title matching when possible.
- **Preserve evidence before recovery.** If a client crashes or behaves oddly, create a trace bundle first.
- **Keep addon changes small.** The Lua side is already stable enough for small iterative fixes; avoid broad refactors without a concrete runtime bug.
- **Treat helper apps as safety rails.** Validate with the helper tools before assuming addon/controller regressions.

## Highest-Value Next Tasks

Pick one of these, in order:

1. **Finish and review helper help-output polish**
   - confirm style is consistent across all helper apps
   - keep this cosmetic-only unless a real help bug appears
2. **Run one live validation pass**
   - list windows
   - inspect strip
   - confirm one known-good client size
3. **Resume addon/runtime work**
   - only after the validation baseline is good
   - focus on real observed issues, not speculative cleanup

## If Only One Client Is Online

Use this order:

1. `run_trace_bundle.bat`
2. `run_live_inspector.bat --list`
3. target the surviving client explicitly
4. resize/inspect if needed
5. only then restart the missing/crashed client

## Start-Heretomorrow Checklist

- [ ] Run `git status --short`
- [ ] Build the six touched helper apps
- [ ] Run `run_live_inspector.bat --list`
- [ ] Confirm at least one known-good live client
- [ ] Decide whether the day starts with **helper polish** or **runtime validation**
