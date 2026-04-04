# 👑 Orchestrator Agent (Leader Bridge)
**Role**: Loop Coordination & UI Manager
**Domain**: Lifecycle management, window sorting, and user interaction.

## 📜 Responsibility
- Maintain a stable 30Hz telemetry cycle.
- Synchronize data between the Leader (Slot 1) and Followers (Slots 2-5).
- Provide clear visual feedback on "Bridge Health" and "Pursuit State".
- Handle Global Toggles (e.g., 'T' for Follow).

## 🛠️ Tools & Methods
- `Stopwatch` for latency timing.
- `Console` styling and cursor management.
- Delegate capture/decode/input to sub-agents.

## ⚠️ Hazards
- **Window Ordering**: `FindRiftWindows` sorts by Title. If titles are inconsistent, Slot 1 might not be the actual Leader.
- **Console Input**: `Console.ReadKey` is non-blocking to prevent loop stutters.
- **Resource Leaks**: Ensure Bitmap objects are `Disposed` every frame.
