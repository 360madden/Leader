# Coordinate-Only Follow Controller Plan and Implementation

## Purpose

This document records the final design for the Leader bridge follow controller after the navigation rewrite in:

- `C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\Leader\LeaderDecoder\Services\NavigationKernel.cs`
- `C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\Leader\LeaderDecoder\Services\FollowController.cs`

It is intentionally verbose so future changes do not drift back toward undocumented heading assumptions or body-chasing behavior.

---

## Problem Statement

The bridge must follow a friendly moving target using only public, coordinate-based telemetry.

What we **do** have reliably:

- unit coordinates
- coordinate change over time
- movement / alive / combat / target / mounted flags
- line-of-sight style state can be layered in later

What we **do not** assume:

- a documented unit yaw / pitch / camera heading field
- `/follow`
- camera-derived orientation
- a trustworthy stationary facing signal

The controller therefore has to solve navigation from geometry and feedback alone.

---

## Core Observability Rules

### 1. Leader motion direction is observable while the leader is moving

If the leader moves from `t_(k-1)` to `t_k`, then instantaneous travel velocity is:

```text
v_measured = (t_k - t_(k-1)) / Δt
```

That gives a travel direction even if no explicit facing value exists.

### 2. A stationary unit's true facing is not observable from coordinates alone

If position does not change, then direction is underdetermined.

### 3. The follower's forward axis becomes observable after true forward locomotion

RIFT forces the character to face the direction of straight forward walking/running. Therefore, after a short forward probe:

```text
h = normalize(s_1 - s_0)
```

is a valid estimate of the follower's forward axis, provided the probe actually caused measurable movement.

This is the key practical observability fact that makes the whole controller viable without a direct facing field.

### 4. Progress is observable from distance change

If the current pursuit goal is `g` and the follower is at `s`, then:

```text
d_k = ||g_k - s_k||
Δd_k = d_(k-1) - d_k
```

Interpretation:

- `Δd > ε` → progress improved
- `Δd < -ε` → progress worsened
- `|Δd| <= ε` → stalled / noisy / blocked

This is the controller's feedback signal.

---

## Final Architecture

The controller is split into four layers.

### Layer 1 — Leader Motion Estimation

Implemented in `NavigationKernel`.

Responsibilities:

1. Compute measured velocity from leader coordinate deltas.
2. Smooth that velocity with an exponential moving average.
3. Derive travel heading from the smoothed velocity.
4. Store breadcrumb history for path trailing and corner recovery.
5. Reset motion state on zone transitions, teleports, and long sampling gaps.

Mathematically:

```text
v_t = β * ((t_k - t_(k-1)) / Δt) + (1 - β) * v_(t-1)
u_t = normalize(v_t)     when ||v_t|| is large enough
```

In code, this is represented by:

- `VelocityX`, `VelocityZ` → instantaneous measured velocity
- `SmoothedVelocityX`, `SmoothedVelocityZ` → EMA velocity
- `EstimatedHeading` → heading derived from smoothed velocity
- `HasTravelVector` / `TravelSpeed` → confidence gates

### Layer 2 — Goal Point Resolution

Also implemented in `NavigationKernel`.

The controller does **not** chase the leader's exact current position every tick.

Instead it forms a goal point using:

1. current leader position
2. a short predictive lead based on smoothed leader velocity
3. a backward walk along the breadcrumb trail to enforce trailing distance

Conceptually:

```text
g = t_k + τ v_t - d u_t
```

But instead of subtracting only a raw travel vector, the implementation walks backward along the stored path. That is stronger around corners because it follows where the leader **went**, not merely where the leader is pointed right now.

### Layer 3 — Follower Basis Learning

Implemented in `FollowController`.

The follower does not begin with a trusted local frame. It creates one from an observed forward probe.

Procedure:

1. If no forward basis exists, send a short forward pulse.
2. Observe the resulting displacement over a small time window.
3. If the displacement exceeds the minimum learn distance, accept it as the forward axis.

Mathematically:

```text
h = normalize(s_1 - s_0)
l = (-h_y, h_x)
```

Important design choice:

- The controller now learns **forward only**.
- The left axis is derived by perpendicular rotation.
- It does **not** require a separate left/right calibration probe to start operating.

That is simpler, more robust, and aligns with the actual observability available in-game.

### Layer 4 — Local-Frame Control and Progress Recovery

Implemented in `FollowController`.

Given the current goal `g` and follower position `s`:

```text
e = g - s
e_f = e · h
e_l = e · l
θ = atan2(e_l, e_f)
```

Interpretation:

- `e_f` = longitudinal error in the follower's forward frame
- `e_l` = lateral error in the follower's local left/right frame
- `θ` = angular form of that local error

Command policy:

- prefer strafe when lateral error dominates
- go forward when longitudinal error is positive and sufficiently aligned
- backpedal when longitudinal error is negative
- stop issuing movement when inside the stop band

Forward pulse strength is shaped with the same structure as the math sketch:

```text
u_fwd = tanh(k_f * e_f) * exp(-λ * |θ|)
```

Because keyboard input is not analog, `u_fwd` is mapped to **pulse duration**, not speed.

Lateral movement uses a similar saturating pulse scale:

```text
u_lat = tanh(k_l * |e_l|)
```

---

## Progress Logic

The controller explicitly tracks whether it is improving the distance to the **goal point** rather than the raw leader body distance.

This is crucial because a moving leader can make raw body distance fluctuate even while pursuit is correct.

Each update computes:

```text
d_k = ||g_k - s_k||
Δd_k = d_(k-1) - d_k
```

Then it classifies the result with a small epsilon band.

### Improvement

If `Δd > ε`:

- keep the current basis
- clear stall counters
- continue pursuit

### Worsening

If `Δd < -ε`:

- increment the worsening counter
- if worsening persists for multiple updates, invalidate the learned basis
- reacquire the forward basis with a fresh forward probe

### Stall

If `|Δd| <= ε` repeatedly:

- treat the controller as stuck / noisy / blocked
- invalidate the learned basis after enough stall ticks
- reacquire the forward basis with a fresh forward probe

This is the bridge's closed-loop safety mechanism. It prevents the system from blindly trusting a stale or corrupted local frame.

---

## Why This Design Is Better Than Body-Chasing or Facing-First Control

### It follows the path, not the model

Body-chasing tends to:

- zig-zag
- overshoot
- cut inside corners
- collapse into oscillation near the target

Breadcrumb trailing is much more stable.

### It avoids the impossible problem

Trying to find a stationary unit's exact facing from coordinates is not generally solvable.

This controller avoids that trap by only demanding what can actually be observed.

### It uses game behavior, not undocumented state

The follower basis is learned from the fact that straight forward movement causes the avatar to face that direction.

That is much safer than inventing or assuming a hidden facing field exists.

### It is self-correcting

Distance-to-goal feedback lets the controller detect:

- stale basis estimates
- obstacle stalls
- pulses that are making things worse

and respond by recalibrating.

---

## Implementation Mapping

### `NavigationKernel.cs`

Key behaviors implemented there:

- EMA velocity smoothing
- dead-reckoning decay when motion briefly drops out
- breadcrumb storage
- trailing-goal resolution from breadcrumb walkback
- zone/teleport reset handling

### `FollowController.cs`

Key behaviors implemented there:

- forward-only basis calibration
- perpendicular left-axis derivation
- local-frame error projection
- pulse-based forward/back/strafe command selection
- distance-to-goal progress scoring
- automatic basis invalidation and forward recalibration
- mount sync and target assist preservation

### `RoundtripTests.cs`

Tests were rewritten to cover the new controller model rather than the older turn-first / heading-seeding model.

Covered cases include:

- motion direction derived from coordinates
- breadcrumb trailing target selection
- bootstrap forward calibration
- learned forward basis producing correct lateral motion
- backpedal selection when the goal is behind
- breadcrumb path preference over body chase
- worsening-progress recalibration
- emergency stop basis reset

---

## Tuning Notes

The current implementation keeps the most sensitive controller constants in code rather than exposing them all in `settings.json`. That keeps the public config stable while the navigation model settles.

Most important constants live in `FollowController.cs`:

- `ForwardDeadzone`
- `LateralDeadzone`
- `ThetaStrafeThreshold`
- `ForwardGain`
- `LateralGain`
- `ForwardAngleDamping`
- `ProgressEpsilon`
- `MaxWorseningTicks`
- `MaxStallTicks`
- `ForwardCalibrationPulseMs`
- `NavigationPulseMinMs`
- `NavigationPulseMaxMs`

Most important motion constants live in `NavigationKernel.cs`:

- `VelocityEmaBeta`
- `VelocityDecay`
- `MinMovementThreshold`
- `MinTravelSpeed`
- `MaxTeleportDistance`
- `PredictionSeconds` / `DefaultPredictionSeconds`

---

## Assumptions and Limits

This design assumes:

1. Straight forward locomotion causes the avatar to face the same direction.
2. The configured movement keys are true movement keys, specifically that `Q` / `E` are the strafe inputs and `A` / `D` remain turn keys.
3. Coordinate jitter is small relative to the configured thresholds.

This design does **not** yet do all of the following:

- explicit LOS-driven fallback modes
- breadcrumb index matching against follower history
- obstacle-aware repathing
- terrain-classified movement policy

Those are reasonable future extensions, but they are not required for a robust first-principles coordinate controller.

---

## Bottom Line

The implemented system now follows this rule set:

1. infer leader motion from coordinate deltas
2. smooth it
3. build a trailing goal from prediction + breadcrumbs
4. learn follower forward from actual forward displacement
5. derive left by perpendicular rotation
6. project goal error into that basis
7. drive forward/back/strafe pulses from local-frame error
8. use distance-to-goal change to detect improvement, failure, and recalibration needs

That is the intended long-term navigation model for this bridge.
