## Leader Telemetry Protocol v1.1

### Strip Geometry
- **Width**: 56 px  |  **Height**: 8 px  |  **Pixel block size**: 8×8 px  
- **Capture origin**: (0, 0) of the game window client area  
- **Sync**: Pixel 0 must decode as R≥250, G≤5, B≥250 for the frame to be accepted  

### Pixel Map

| Pixel | Client X | Channel | Value | Formula |
|-------|----------|---------|-------|---------|
| 0 | 0–7 | RGB | 255, 0, 255 | Static magenta sync beacon |
| 1 | 8–15 | R | playerHP | `floor(health/healthMax × 255)` |
| 1 | 8–15 | G | targetHP | `floor(health/healthMax × 255)` |
| 1 | 8–15 | B | flags | Bitfield (see below) |
| 2 | 16–23 | RGB | CoordX | 24-bit packed: `floor(x×10)+8388608` |
| 3 | 24–31 | RGB | CoordY | 24-bit packed: `floor(y×10)+8388608` |
| 4 | 32–39 | RGB | CoordZ | 24-bit packed: `floor(z×10)+8388608` |
| 5 | 40–47 | R | facing low byte | `n = floor(radians×10000)` |
| 5 | 40–47 | G | facing high byte | `n >> 8` |
| 5 | 40–47 | B | zone hash | Sum of zone string bytes mod 256 |
| 6 | 48–55 | RGB | target hash | Last 6 hex chars of unit ID |

### Flags Bitfield (Pixel 1, B Channel)
| Bit | Mask | Meaning |
|-----|------|---------|
| 0 | 0x01 | IsCombat |
| 1 | 0x02 | HasTarget |
| 2 | 0x04 | IsMoving |
| 3 | 0x08 | IsAlive |
| 4 | 0x10 | IsMounted |

### Coordinate Decode (C#)
```csharp
float val = (R + G * 256 + B * 65536 - 8388608) / 10.0f;
```

### Heading Decode (C#)
```csharp
float radians = (R + G * 256) / 10000.0f;
```

### Precision
| Field | Precision | Range |
|-------|-----------|-------|
| X / Y / Z | 0.1 m | ±838 860 m |
| Heading | ~0.0001 rad (~0.006°) | 0 → 2π |
| HP | 1/255 (~0.4%) | 0–100% |

### Coordinate System Notes
RIFT uses: **X+ = East**, **Z+ = South**, **Y+ = Up**  
Bearing calculation (C# NavigationKernel):
```csharp
float bearing = Atan2(leader.X - follower.X, leader.Z - follower.Z);
```
This matches RIFT's forward-facing convention where heading `0` = North (−Z direction).
