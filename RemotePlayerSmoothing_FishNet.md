# Remote Player Smoothing & Reconciliation (FishNet, 20 Tick Server)

## Context

- Server tick rate: **20 Hz**
- Client FPS: **~120 FPS**
- Networking: **FishNet**
- Movement: **PredictedRigidbody** (server authoritative)
- Goal: **Apex-like smoothness**
  - No visible tick stepping
  - Smooth remote player motion
  - Server authority preserved
  - No camera coupling to physics

This document defines the **architecture, rules, and implementation plan**
for smoothing **remote players only**.

---

## High-Level Principles

1. Never render authoritative physics directly
2. Never render remote players at “now”
3. Smooth visuals, not simulation
4. Correctness beats invented motion
5. Small lies are better than visible truth

---

## Explicit Decisions

### What we WILL do
- Snapshot buffering for remote players
- Past-time interpolation (100–150 ms)
- Frame-rate–driven visuals
- Optional velocity-assisted interpolation (Hermite, clamped)
- Animation-driven visuals

### What we will NOT do
- NetworkTickSmoother for remote players
- Bézier curves or splines for FPS characters
- Camera attached to Rigidbody
- Rendering authoritative transforms directly
- Tick-driven visuals

---

## Object Hierarchy

```
Player (NetworkObject)
├── AuthorityRoot
│   ├── PredictedRigidbody
│   └── TickNetworkBehaviour
└── VisualRoot
    ├── Mesh / SkinnedMesh
    ├── Animator
    └── Visual-only scripts
```

Rules:
- AuthorityRoot is never rendered
- VisualRoot contains no physics
- All smoothing happens on VisualRoot

---

## Snapshot Data Model

```csharp
struct PlayerSnapshot
{
    public double serverTime;
    public Vector3 position;
    public Vector3 velocity;
    public float yAxisRotation;
}
```

---

## Snapshot Buffer

- Stored per remote player
- Size: 6–10 snapshots
- Interpolation delay: 0.1–0.15 seconds

```csharp
const double InterpolationDelay = 0.12;
const int MaxSnapshots = 10;
```

---

## Rendering Time Rule

Render remote players at:

```
renderTime = NetworkTime - InterpolationDelay
```

---

## Interpolation Strategy

### Required: Linear

```csharp
Vector3 position = Vector3.Lerp(a.position, b.position, t);
Quaternion rotation = Quaternion.Slerp(a.rotation, b.rotation, t);
```

### Optional: Hermite (clamped and pseudo code see SplineSection for example implementation)

```csharp
Vector3 HermiteInterpolate(PlayerSnapshot a, PlayerSnapshot b, float t)
{
    float dt = (float)(b.serverTime - a.serverTime);
    Vector3 v0 = Vector3.ClampMagnitude(a.velocity * dt, MaxHermiteVelocity);
    Vector3 v1 = Vector3.ClampMagnitude(b.velocity * dt, MaxHermiteVelocity);

    return Vector3.Hermite(a.position, v0, b.position, v1, t);
}
```

Fallback to Lerp on sharp direction changes.

---

## Extrapolation Rules

- Only if future snapshot missing
- Max 50–100 ms
- Linear only
- Freeze before snap

---

## Reconciliation Philosophy

Reconciliation corrects simulation, not visuals.

```csharp
VisualRoot.position = Vector3.SmoothDamp(
    VisualRoot.position,
    authorityPosition,
    ref correctionVelocity,
    0.08f
);
```

---

## Distance Error Thresholds

| Error | Action |
|------|--------|
| < 0.05 m | Ignore |
| 0.05–0.3 m | Smooth |
| > 0.3 m | Snap |
| > 1.0 m | Teleport |

---

## Animation Guidelines

- Animator driven by velocity & direction
- Root motion hides corrections
- Never network bones directly

---

## Responsibilities

### Server
- Authoritative physics (20 Hz)

### Client – Local
- Prediction
- Frame-based camera

### Client – Remote
- Snapshot buffer
- Past-time interpolation

---

## Implementation TODO

- Disable NetworkTickSmoother for remote players
- Implement snapshot buffer
- Past-time interpolation
- Optional Hermite interpolation
- Tune thresholds

---

## Unity Implementation (RoachRace)

Added runtime component:

- `RemotePlayerVisualSmoother` (Packages/com.roachrace.networking/Runtime/Smoothing/RemotePlayerVisualSmoother.cs)

Optional remote animation helper:

- `SurvivorRemoteAnimator` (Packages/com.roachrace.networking/Runtime/Animation/SurvivorRemoteAnimator.cs)

### How to wire it

Attach `RemotePlayerVisualSmoother` to the **Player (NetworkObject)** root.

Inspector fields:

- **Authority Root**: the transform that receives authoritative updates (usually the Rigidbody root / predicted root)
- **Authority Rigidbody** (optional): the Rigidbody on Authority Root (used to read velocity for Hermite)
- **Visual Root**: the visual-only transform to smooth (no physics)

Runtime behavior:

- Runs **only for remote objects** (`!IsOwner`) on clients
- Samples snapshots on FishNet `TimeManager.OnPostTick`
- Renders visuals at `renderTime = (approxServerTime - interpolationDelay)`
- Uses **linear** by default; optional **Hermite** (clamped) when enabled
- Disables any `NetworkTickSmoother` found in children for remotes

### Remote Survivor Animation (Option 2)

`SurvivorRemoteAnimator` is a lightweight bridge to drive remote animation:

- Locomotion is derived from smoothed `VisualRoot` motion each frame (MoveX/MoveY/Gait/IsMoving).
- Crouch is replicated as a persistent state (`SyncVar<bool>`).
- Fire/Reload/UseItem are replicated as events (RPC → trigger parameters).

This keeps CAS/Animator visuals decoupled from simulation and avoids networking bones or root motion.

---

## Guiding Mantra

Authority is real  
Visuals are fake  
Fake feels better
