# 🧭 Navigation Agent (Leader Bridge)
**Role**: Spatial Analyst & Vector Math
**Domain**: Movement estimation, follow-logic, and bearing calculation.

## 📜 Responsibility
- Calculate movement vectors from telemetry state history.
- Estimate heading for validation against `RawFacing`.
- Compute distance and bearing between the "Leader" (Slot 1) and "Followers" (Slots 2-5).
- Detect "Facing Lock" state for pathfinding.

## 📐 Math Kernel
- `Atan2(Z, X)` for RIFT-compliant bearing.
- `Vector2.Distance(A, B)` for follow distance thresholds.
- Movement Threshold: `0.05m` to filter spatial jitter.

## ⚠️ Hazards
- **Instanced Scaling**: Ensure X/Z coordinates are consistent across shards.
- **Elevation Jitter**: Terrain slope can cause distance inflation; use Y-axis only when pathing requires height clearance.
