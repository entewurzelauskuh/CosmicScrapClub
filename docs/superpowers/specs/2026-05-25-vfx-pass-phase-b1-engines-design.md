# VFX Pass — Phase B-1 (Engines + Boost Flare + RCS Puffs) — Design Spec

**Status:** Approved design, ready for implementation planning
**Date:** 2026-05-25
**Branch:** `feat/vfx-phase-b1-engines` off `main`
**ROADMAP item:** Up Next #1 — Phase B sub-PR 1 (of several)
**Sibling PR (parallel, separate spec):** AA Settings (Phase 1.5 — Graphics tab dropdown). Not blocking this PR; either can land first.

## Overview

First slice of Phase B from `docs/vfx_pass_ideas.md`. Three coordinated
visual additions tied to construct propulsion:

1. **Engine plume per thruster cube** — a `ParticleSystem` emitting
   along the thruster's exhaust direction (`-LocalThrustAxis`).
   Length / brightness scale with how hard that thruster is being
   commanded.
2. **Boost flare** — when Left-Ctrl boost is active *and* the
   thruster contributes to that axis, the plume gets hotter, longer,
   and brighter. Includes an inner shock-diamond sprite for the
   supersonic-jet feel.
3. **RCS puffs** — brief one-shot bursts at the construct corners
   when strafe / yaw / roll input is applied but no thruster covers
   that axis. Sells the "tiny attitude jets firing" idea on
   unthrustered axes.

Three new toggles join the Settings → Debug tab (8 total, still
two-column friendly). Default ON. All needed assets (particle
texture, materials, prefabs) are authored procedurally via an Editor
`MenuItem` installer following the Phase 1 `VfxOverridesInstaller`
precedent — idempotent, reproducible, git-friendly.

This PR also establishes the **`Assets/VFX/{Textures,Materials,Prefabs}/`
folder convention** that subsequent Phase B / C / D PRs will follow.

## Background — current systems

**Thrusters.** `ShapeUtilityThruster` cubes are placeable Utility
shapes. Each instance carries a `ThrusterBehavior` component
exposing `LocalThrustAxis` — a clean unit axis (±X / ±Y / ±Z in the
construct's local frame) along which thrust is applied. Today
`ThrusterBehavior` is a passive descriptor: `FlyController` reads
its axes and applies forces.

**Flight forces.** `FlyController` is the construct-level driver.
Each `FixedUpdate` it reads the player's WASD/Space/C input,
identifies which thrusters contribute to each commanded axis, and
calls `Rigidbody.AddForce` accordingly. Boost is gated by a
construct-level boost meter (`ThrusterBehavior`-owned resource +
overboost lockout). When `LeftCtrl` is held and the active thrust
axis has at least one contributing thruster, force is multiplied
by ~1.3 and the max-speed cap on that axis lifts by ~1.3.

**Attitude (strafe / yaw / roll).** Same `FlyController` reads the
attitude input (Q / E / arrows etc.) and applies torque via
`AddTorque`. Some attitude axes are covered by thrusters (e.g. side
thrusters provide yaw torque if placed off-centre); others are
not, and the construct still rotates via the Rigidbody's natural
response to torque. RCS puffs visualise the *unthrustered* attitude
inputs — selling them as "attitude jets" even though no jet exists.

**Bloom.** Phase 1 enabled URP `Bloom` with intensity 0.6 / threshold
1.0 / scatter 0.7 on `DefaultVolumeProfile`. HDR-bright particle
colours (intensity > 1) naturally bloom — no per-particle work.

**Tooltip + Debug tab.** Phase 1 landed the `TooltipHud` /
`TooltipTrigger` pair and the two-column Debug tab. New toggles
slot in as one-line appends to the `effects` array in
`SettingsMenu.BuildDebugPanel` — no plumbing changes.

## Scope

| In | Out |
|---|---|
| Per-thruster engine plume `ParticleSystem` (`ThrusterVfx`) | Per-construct-material plume tint variants (A/B/C/D) — single cool-blue palette this PR |
| Plume intensity tied to thruster input level (0-1) | Damage-flicker / sputter on low-HP thrusters (Phase B-3 destruction slice) |
| Boost flare amplification + shock-diamond sprite | Engine startup flame / shutdown afterglow tween |
| RCS puffs at four construct corners (`RcsPuffVfx`) | Heat-distortion / refraction shader behind plumes (Phase C) |
| Three new Debug-tab toggles + persistence | Engine VFX in BuildScene (FlyScene only — BuildScene shows static placement) |
| `Assets/VFX/{Textures,Materials,Prefabs}/` folder convention | Texture-painting tool / sprite picker UI |
| Procedural texture / material / prefab installer | Other Phase B effects (weapons, destruction, HUD) — separate PRs |
| `ThrusterBehavior.CurrentInputLevel` + `IsBoosting` API | A generic `VfxPool` pooled-particle infrastructure — YAGNI for ~10 thrusters per construct |
| `FlyController.CurrentAttitudeInput` property | AA settings — separate Phase 1.5 PR (Graphics tab dropdown) |

## Visual treatment — Elite Dangerous / Star Citizen direction

Cube Fly's blocky cube-stack aesthetic pairs best with a **clean,
crisp** plume — not the industrial-smoky look (Space Engineers) or
the cartoony-saturated look (Everspace). Phase 1 bloom is live, so
emissive plumes pop naturally.

| Aspect | Value | Why |
|---|---|---|
| **Plume core colour** | Cool blue at HDR intensity ~2.5 (`#80C0FF` × 2.5) | Reads as fusion exhaust; bloom pulls a halo |
| **Plume edge colour** | Lighter blue fading to transparent at the tip | Soft falloff, no harsh edge |
| **Particle render mode** | `StretchedBillboard`, velocity scale ~1.5 | Particles elongate along travel direction → reads as fluid streak, not discrete dots |
| **Plume length** | Lerps with `CurrentInputLevel`: 0 → 0 emission; 1 → ~3-unit streak from a 1-unit thruster cube | Length is the primary visual feedback for thrust amount |
| **Plume emission rate** | 60 particles / sec at full input | Smooth stream; cheap |
| **Boost flare emission** | ×1.5 emission, ×1.4 lifetime | More volume, longer tail |
| **Boost flare colour** | Shifts hotter — white-blue at HDR intensity ~4 | "Afterburner ignited" feel |
| **Shock-diamond sprite** | A single small inner-billboard particle at ×0.4 size, brighter than the main plume | Classic supersonic-jet visual; sells the "boost engaged" beat |
| **RCS puff** | 6-particle radial burst, 0.2 s lifetime, same cool-blue palette but small (×0.3 scale) | Reads as "tiny jet pop" without competing with main plumes |

**Reference:** Elite Dangerous Sidewinder main engines + RCS — soft
blue cones at idle, brighter and longer under boost, tiny puffs on
attitude jets. The shock-diamond reference is also from ED's larger
ships under Frame Shift boost.

## Architecture

```
ThrusterBehavior  (existing, on each PlacedThruster cube)
├── CurrentInputLevel : float  [0..1]  (NEW)
├── IsBoosting       : bool             (NEW)
└── ThrusterVfx  (NEW sibling component, added at construct-build time)
    └── ParticleSystem (instantiated from EnginePlume.prefab as a child)
        ├── Default: 60 pps, blue, stretched billboards
        └── Boosting: rate / lifetime / colour scaled up

CubeConstruct  (existing, on construct root)
├── CurrentAttitudeInput : Vector3  (NEW property on FlyController)
└── RcsPuffVfx  (NEW sibling component, added at construct-build time)
    └── 4 child ParticleSystem one-shot emitters at construct corners

VfxSettings  (existing, append three new keys)
├── EnginePlume  : bool
├── BoostFlare   : bool
└── RcsPuff      : bool
```

### `ThrusterVfx`

`Assets/Scripts/Fly/ThrusterVfx.cs`, ~90 lines.

- `Awake` instantiates `EnginePlume.prefab` as a child. Orients its
  Z axis along `-LocalThrustAxis` (so particles emit along the
  exhaust direction).
- `LateUpdate` reads `ThrusterBehavior.CurrentInputLevel` and
  `IsBoosting`, then sets:
  - `ParticleSystem.EmissionModule.rateOverTime` = `BaseRate * input * (boosting ? BoostRateMul : 1f)`
  - `ParticleSystem.MainModule.startLifetime` = `BaseLifetime * (boosting ? BoostLifetimeMul : 1f)`
  - `ParticleSystem.MainModule.startColor` = blend between default and hot-boost colour
  - Shock-diamond child emitter active only if `boosting`
- On `VfxSettings.Changed`: re-evaluate enable/disable of the prefab
  root based on `VfxSettings.EnginePlume`. (`BoostFlare` toggle gates
  the boost-time amplification only — when OFF, plumes never amplify
  on boost but still emit normally.)
- On `Destroy`: clean up the instantiated child.

### `RcsPuffVfx`

`Assets/Scripts/Fly/RcsPuffVfx.cs`, ~70 lines.

- `Awake` instantiates 4 `RcsPuff.prefab` instances at the construct's
  4 bounds corners (using the same `Rigidbody`/collider bounds the
  flight system already computes).
- `Update` reads `FlyController.CurrentAttitudeInput` (a `Vector3`:
  strafe / yaw / roll components). For each non-zero axis component
  whose contribution is **not** already covered by a thruster (the
  set of "missing" axes the construct doesn't have jets for), fire
  a `Particles.Emit(burst)` call on the appropriate corner emitter.
- Per-axis burst rate is throttled (no more than one burst every 0.15 s)
  so a sustained yaw input becomes a series of rhythmic puffs, not a
  continuous stream — sells "small jets firing in pulses".
- Disabled root when `VfxSettings.RcsPuff == false`.

### `VfxApplier`

Phase 1's `VfxApplier` already exists and listens to
`VfxSettings.Changed`. We extend its `Apply()` minimally — actually
nothing: engine and RCS VFX are owned by per-construct components,
not the URP volume profile. Toggle changes notify those components
directly via the existing static `VfxSettings.Changed` event. No
change to `VfxApplier`.

## Asset authoring

**Folder convention (NEW):**

```
Assets/VFX/
├── Textures/
│   └── Glow_64.png        — 64×64 soft radial gradient (procedural)
├── Materials/
│   ├── EnginePlumeMat.mat — URP particle additive, references Glow_64
│   ├── BoostShockMat.mat  — URP particle additive, slightly tighter falloff
│   └── RcsPuffMat.mat     — URP particle additive, smaller
└── Prefabs/
    ├── EnginePlume.prefab — ParticleSystem with default tuning
    └── RcsPuff.prefab     — ParticleSystem one-shot burst
```

This convention keeps VFX assets out of `Assets/Materials/`
(per-cube placement materials) and `Assets/Prefabs/` (placeable cube
prefabs). Subsequent VFX PRs append here.

**`VfxAssetsInstaller` Editor MenuItem** (`Tools/CubeFly/Generate VFX
assets`) — ~150 lines, `Assets/Scripts/Editor/VfxAssetsInstaller.cs`:

- **Step 1: Procedurally generate `Glow_64.png`.** A 64×64 `Texture2D`
  with a gaussian-falloff radial gradient (white core, transparent
  edges). Encoded as PNG, written to disk, re-imported via
  `AssetDatabase.ImportAsset` with the right texture-import settings
  (Sprite/Default, sRGB on, no mipmaps, point or bilinear filter).
- **Step 2: Create / update materials.** Each material loads a known
  URP particle shader (e.g. `Universal Render Pipeline/Particles/Unlit`
  in additive mode), sets the main texture to `Glow_64`, configures
  tint / blend mode. Saved to `Assets/VFX/Materials/`.
- **Step 3: Create / update prefabs.** Build the `ParticleSystem`
  GameObject hierarchy in code, configure every module (`MainModule`,
  `EmissionModule`, `ShapeModule`, `VelocityOverLifetime`,
  `ColorOverLifetime`, `SizeOverLifetime`, etc.) to the spec values,
  attach the right `Material`, save as prefab via
  `PrefabUtility.SaveAsPrefabAsset`.

The installer is **idempotent** — TryGet-style checks before each
write; re-running only overwrites what's drifted. Stays in the repo
as insurance for re-application after any accidental delete.

(The Editor folder convention excludes the installer from runtime
builds.)

## Particle budget

Conservative estimates with default Phase 1 + Phase B-1 in flight:

| Source | Active per frame | Worst-case (50-cube / 10-thruster ship boosting + 4 RCS firing) |
|---|---|---|
| Engine plume idle (per thruster, no input) | 0 | 0 |
| Engine plume full input (per thruster) | ~30 | 300 |
| Boost flare amplification (per thruster boosting) | +20 | +200 |
| Shock-diamond sprite (boosting) | 1 | 10 |
| RCS puff (per burst, decays in 0.2 s) | ~6 | ~24 (4 corners × 6) |
| **Worst case total** | — | **~530 alive** |

URP comfortably handles 5k+ particles on integrated GPUs. We're at
~10% of that ceiling. No pooling needed.

## Hooks added to existing code

### `ThrusterBehavior.cs`

```csharp
// New fields + properties
[Range(0f, 1f)] float _currentInputLevel;
bool _isBoosting;

public float CurrentInputLevel => _currentInputLevel;
public bool  IsBoosting => _isBoosting;

// Internal setters called by FlyController each FixedUpdate
internal void SetInputLevel(float level) => _currentInputLevel = level;
internal void SetBoosting(bool boosting) => _isBoosting = boosting;
```

`internal` (not `public`) — only FlyController in the same
`CubeFly.Fly` namespace drives these.

### `FlyController.cs`

- In the existing FixedUpdate force loop, after computing each
  thruster's contribution to the commanded axis, call
  `thruster.SetInputLevel(contribution)` and
  `thruster.SetBoosting(isBoostingThisAxis)`. ~5 lines added.
- New public property:
  ```csharp
  public Vector3 CurrentAttitudeInput { get; private set; }
  ```
  Updated each Update from the player's strafe / yaw / roll input
  (already computed inline today — just exposing it).
- In `BuildConstruct(...)`, after instantiating thruster cubes,
  add `cube.AddComponent<ThrusterVfx>()` on each. ~3 lines.
- Same method, after the construct root is built, add
  `_constructRoot.AddComponent<RcsPuffVfx>()`. ~2 lines.

### `VfxSettings.cs`

Three new keys following the existing pattern:

```csharp
const string KEnginePlume = "VfxEnginePlume";
const string KBoostFlare  = "VfxBoostFlare";
const string KRcsPuff     = "VfxRcsPuff";

public static bool EnginePlume { get => Get(KEnginePlume); set => Set(KEnginePlume, value); }
public static bool BoostFlare  { get => Get(KBoostFlare);  set => Set(KBoostFlare,  value); }
public static bool RcsPuff     { get => Get(KRcsPuff);     set => Set(KRcsPuff,     value); }
```

Default ON for each (`PlayerPrefs.GetInt(key, 1) != 0`).

## Settings → Debug tab integration

Three lines appended to the `effects` array in
`SettingsMenu.BuildDebugPanel`:

```csharp
("Engine plume",
    "Per-thruster particle stream along the exhaust direction. Length and brightness scale with thrust input.",
    () => VfxSettings.EnginePlume, v => VfxSettings.EnginePlume = v),
("Boost flare",
    "Brighter, longer plume with an inner shock-diamond when Left-Ctrl boost is active on a thrustered axis.",
    () => VfxSettings.BoostFlare,  v => VfxSettings.BoostFlare  = v),
("RCS puffs",
    "Tiny attitude-jet puffs at construct corners when strafe / yaw / roll is commanded on an axis without a dedicated thruster.",
    () => VfxSettings.RcsPuff,     v => VfxSettings.RcsPuff     = v),
```

The two-column layout auto-rebalances to 4 left / 4 right (8 toggles
total — well within capacity).

## Files

**New:**
- `Assets/Scripts/Fly/ThrusterVfx.cs` (~90 lines)
- `Assets/Scripts/Fly/RcsPuffVfx.cs` (~70 lines)
- `Assets/Scripts/Editor/VfxAssetsInstaller.cs` (~150 lines)
- `Assets/VFX/Textures/Glow_64.png` (generated, ~2 KB)
- `Assets/VFX/Materials/EnginePlumeMat.mat` (generated)
- `Assets/VFX/Materials/BoostShockMat.mat` (generated)
- `Assets/VFX/Materials/RcsPuffMat.mat` (generated)
- `Assets/VFX/Prefabs/EnginePlume.prefab` (generated)
- `Assets/VFX/Prefabs/RcsPuff.prefab` (generated)

**Modified:**
- `Assets/Scripts/Fly/ThrusterBehavior.cs` — add `CurrentInputLevel`
  / `IsBoosting` + internal setters (~10 lines).
- `Assets/Scripts/Fly/FlyController.cs` — call setters each
  FixedUpdate; expose `CurrentAttitudeInput`; instantiate
  `ThrusterVfx` on each thruster + `RcsPuffVfx` on the construct in
  `BuildConstruct` (~20 lines).
- `Assets/Scripts/Core/VfxSettings.cs` — 3 new keys + properties
  (~10 lines).
- `Assets/Scripts/Core/SettingsMenu.cs` — 3-line append to the
  `effects` array.
- `README.md` — "What's In Here" engine VFX bullet + Debug-tab line.
- `ROADMAP.md` — Phase B sub-PR 1 marked in-flight.

## Explicitly out of scope

- **AA settings.** Phase 1.5 sibling PR. Graphics-tab dropdown
  (MSAA / FXAA / SMAA / TAA). Not blocking; can land before or
  after this PR.
- **Per-material thruster tints.** All thrusters share one cool-blue
  palette in this PR. A future polish PR could swap palettes per
  construct material (A/B/C/D) — small follow-up if you want it.
- **Engine-damage flicker.** Sputtering / dropouts when a thruster's
  HP drops — that's Phase B-3 (destruction + crash).
- **Heat distortion.** Refraction shader behind the plume — Phase C.
- **Engine startup / shutdown tween.** A brief flame on FlyScene
  load and a fade on input release. Marginal payoff for the
  complexity — deferred.
- **BuildScene preview of engine VFX.** Out of scope. BuildScene
  shows static placement; thrusters there are inert.
- **Other Phase B effects.** Weapons + impacts, destruction + crash,
  HUD feedback — each is its own PR with its own brainstorm.

## References

- `docs/vfx_pass_ideas.md` — Phase B engine plume / boost flare /
  RCS puff bullet entries; quality references.
- `docs/superpowers/specs/2026-05-24-vfx-pass-phase-1-design.md` —
  Phase 1 spec; bloom + VfxSettings + VfxApplier + Debug tab patterns.
- `Assets/Scripts/Fly/ThrusterBehavior.cs` — `LocalThrustAxis`
  property; the construct-local exhaust direction the plume aligns
  to.
- `Assets/Scripts/Fly/FlyController.cs` — FixedUpdate force loop;
  boost gating; attitude input processing.
- `Assets/Scripts/Core/SettingsMenu.cs` — Debug-tab `effects` array;
  the one-line append pattern.
- `Assets/Scripts/Editor/VfxOverridesInstaller.cs` — Phase 1's
  `MenuItem` installer; the pattern `VfxAssetsInstaller` mirrors.
- Elite Dangerous (Sidewinder main engines, Frame Shift boost
  shock-diamond) — visual reference for plume / boost / RCS aesthetics.
