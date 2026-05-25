# VFX Pass — Phase B-2 (Weapons + Impacts) — Design Spec

**Status:** Approved design, ready for implementation planning
**Date:** 2026-05-25
**Branch:** `feat/vfx-phase-b2-weapons` off `main`
**ROADMAP item:** Up Next #1 — Phase B sub-PR 2 (of several), following B-1 (engines)
**Predecessor:** `docs/superpowers/specs/2026-05-25-vfx-pass-phase-b1-engines-design.md`
**Brainstorm resume note:** `docs/superpowers/specs/2026-05-25-vfx-pass-phase-b2-resume.md`

## Overview

Second slice of Phase B from `docs/vfx_pass_ideas.md` (§2 Weapons & Projectiles, plus the bullet-impact subset of §5 Damage & Destruction). Seven coordinated visual additions that make weapon fire feel kinetic and impacts feel reactive:

1. **Muzzle flash — Pyramid** (machine gun): warm yellow-white starburst at the pyramid's tip on every shot.
2. **Muzzle flash — Cylinder** (rocket launcher): warm orange/yellow soft-disc puff at the cylinder's barrel on every shot.
3. **Bullet tracer**: head-fading TrailRenderer on bullets in flight — yellow-white core fading through warm yellow to a hot-pink tail, with a pink-fringe cross-section.
4. **Bullet impact spark**: small + snappy radial spike burst + bright core where a bullet hits any surface.
5. **Bullet impact ground dust**: soft tan/beige puff cluster where a bullet hits a roughly-upward surface (`Dot(normal, up) > 0.7`). Coexists with the spark.
6. **Rocket exhaust plume**: continuous bright warm yellow/orange flame at the rocket's tail while in flight.
7. **Rocket smoke trail (a)** + **smoke puffs (b)**: cool grey-white TrailRenderer ribbon **plus** discrete soft-white particle puffs trailing behind the rocket — together they sell "rocket leaves smoke" with both shape (ribbon) and texture (puffs).

Eight new toggles join the Settings → Debug tab (16 total, still two-column friendly — capacity is ~26). Default ON. All needed assets (two new procedural textures, six materials, six prefabs) are authored procedurally via the existing `VfxAssetsInstaller` Editor MenuItem, extended with new generators — idempotent, reproducible, git-friendly. Two existing projectile prefabs (`Bullet.prefab`, `Rocket.prefab`) are also wired by the installer via `SerializedObject` patching so a fresh checkout works out of the box.

## Background — current systems

**Weapons.** `WeaponBehavior` is an abstract MonoBehaviour with a public `TryFire(Vector3 crosshairWorldTarget)` entry point. Subclasses (`PyramidWeapon`, `CylinderWeapon`) override `protected abstract Fire(target)` with type-specific spawn logic. The base class owns reload cooldown and a `[SerializeField] GameObject projectilePrefab` reference. Construct + Shape references are set by `FlyController.BuildConstruct` after the weapon component is added to a placed cube.

- `PyramidWeapon.Fire` spawns `Bullet.prefab` at `transform.TransformPoint(Vector3.up * 0.5f)` (the pyramid's apex) with direction either `transform.up` (off-axis pyramid) or toward the crosshair (frontal pyramid).
- `CylinderWeapon.Fire` spawns `Rocket.prefab` at `transform.position` (cylinder centre) with launch direction `transform.up`. The rocket exits along that direction for `launchExitDistance = 0.5` before redirecting to the crosshair target.

**Projectiles.** Both `Bullet` and `Rocket` move kinematically and detect hits via per-frame swept raycasts (`ProjectileHit.TrySweep`) — no Unity colliders/triggers (per CLAUDE.md). On hit they call `ProjectileHit.ApplyAndLog(hit, damage, firingConstruct, TAG)` then `Destroy(gameObject)`. On max-range they `Destroy(gameObject)` directly. `RaycastHit` carries `point` and `normal` in world space — both available at the call site for VFX spawning.

**`ProjectileHit`** is a static class containing `TrySweep` (sweep + self-hit filter + nearest-hit selection) and `ApplyAndLog` (CubeStats resolution + damage routing through `CubeDamage.ApplyAndLog`). The natural place to add a `SpawnImpactVfx(hit)` static method.

**`FlyController.BuildConstruct`** is the per-Fly-session construct builder. It walks the placements list and `Instantiate`s the correct shape prefab for each cube, calling `AddComponent<ThrusterVfx>` + `tvfx.SetPlumePrefab(enginePlumePrefab)` for each thruster. `enginePlumePrefab` and `rcsPuffPrefab` are `[SerializeField]` fields on `FlyController` itself. **The same pattern extends to weapons in this PR** — new `muzzleFlashStarburstPrefab` / `muzzleFlashDiscPrefab` fields get wired into `PyramidWeapon` / `CylinderWeapon` during the same loop.

**B-1 ThrusterVfx pattern (reused).** `ThrusterVfx` instantiates `EnginePlume.prefab` as a child in `Start()`, polls `VfxSettings.EnginePlume` every `LateUpdate`, and adjusts `emission.rateOverTime` accordingly. The "poll every frame for the Debug-tab A/B comparison" rule is the established convention and is followed here for rocket continuous effects.

**`VfxAssetsInstaller`** is the Phase B-1 Editor MenuItem at `Tools/CubeFly/Generate VFX assets`. It:
- Generates `Glow_64.png` (procedural radial gradient) on first creation; skips if exists.
- Creates/updates three additive materials (`EnginePlumeMat`, `BoostShockMat`, `RcsPuffMat`) idempotently — re-applies tint and additive blend params on every run so manual drift converges back to spec.
- Unconditionally regenerates two prefabs (`EnginePlume.prefab`, `RcsPuff.prefab`) — the prefab GUID is stable across runs (it's the asset's, not the inner GameObjects'), so scene references stay valid.

This PR **extends** `VfxAssetsInstaller` with new generators (two new textures, six new materials, six new prefabs) plus two new prefab-patcher steps that wire `Bullet.prefab` and `Rocket.prefab` via `PrefabUtility.LoadPrefabContents` + `SerializedObject`. Single source of truth for all VFX assets, including projectile prefab wiring.

**`VfxSettings`** is a static PlayerPrefs facade with typed bool properties — currently eight (`Bloom`, `Vignette`, `Tonemapping`, `ColorAdjustments`, `ChromaticAberration`, `EnginePlume`, `BoostFlare`, `RcsPuff`). Default 1 = ON via `PlayerPrefs.GetInt(key, 1)`. Each setter fires `Changed`. B-2 appends eight more keys following the exact same pattern.

**`SettingsMenu.BuildDebugPanel`** builds a two-column toggle layout from an `effects` array of `(label, tooltip, getter, setter)` tuples. Adding a toggle is a one-line append; `leftCount = (effects.Length + 1) / 2` auto-rebalances the columns.

## Scope

| In | Out |
|---|---|
| Pyramid muzzle flash (starburst sprite, warm yellow-white) | Crosshair hit-confirm pulse (§8 HUD-tier) — deferred to Phase B-4 (HUD feedback) |
| Cylinder muzzle flash (disc sprite, warm orange/yellow) | Rocket detonation multi-emitter — deferred to Phase C (shaders + scripted sequences) |
| Bullet TrailRenderer tracer (warm core + pink fringe) | Laser beam glow / impact heat / scorch decal — Phase C |
| Bullet impact spark prefab (small + snappy) | Surface-aware dust dispatch (sand on desert, rock on asteroid, ice on ice world) — Phase C extension; B-2's `SpawnImpactVfx` is structured so this slots in later |
| Bullet impact ground-dust prefab (warm tan, alpha-blended) | Damage-tier impact variation (small hit / big hit / kill shot) — Phase C |
| Rocket exhaust plume prefab (continuous, warm orange/yellow) | Pooled-particle infrastructure — YAGNI at ≤ a few projectiles alive |
| Rocket smoke trail (TrailRenderer, cool grey-white) | VFX in BuildScene (FlyScene only — BuildScene shows static placement) |
| Rocket smoke puffs (continuous particle puffs, soft white, reuses RcsPuffMat) | New `VfxRegistry` ScriptableObject infrastructure — overkill for 6 prefabs + 2 material refs |
| Eight new Debug-tab toggles + persistence | Texture-painting tool / sprite picker UI |
| Procedural texture / material / prefab generators + Bullet/Rocket prefab wiring in extended `VfxAssetsInstaller` | Sibling Phase B-3 (destruction VFX), Phase B-4 (HUD VFX), Phase B-5 (environment VFX) — separate specs |
| `LingeringTrail` helper MonoBehaviour for clean trail detach on projectile destroy | Cross-section gradient texture authoring tool — B-2 ships one procedurally-generated stripe texture |
| `ProjectileHit.SpawnImpactVfx` static spawner + `ConfigureImpactPrefabs` static setter | AA settings — separate Phase 1.5 PR (Graphics tab dropdown) |

## Visual treatment — Freelancer / Squadrons trifecta + warm/cool weapon palette

Cube Fly's blocky-cube-stack aesthetic pairs best with **clean, readable, bloom-friendly** weapon effects — closest reference is **Freelancer's classic muzzle + tracer + impact-spark trifecta** (cheap, timeless, instantly readable), saturated up toward the **Everspace / Squadrons** bloom-amplified arcade look. Elite Dangerous / Star Citizen serve as the clean-sci-fi reference for the rocket plume's HDR warmth.

| Aspect | Value | Why |
|---|---|---|
| **Warm-cool weapon-vs-propulsion split** | Weapons = warm (yellow / orange / pink). Ship engines = cool blue-white (locked B-1). | Players instantly read "rocket plume" vs "engine plume" in chaos. Visual hierarchy via temperature. |
| **Pyramid muzzle palette** | Warm yellow-white HDR ×3 | Most "iconic gunshot" of the three sprite options. |
| **Pyramid muzzle sprite** | 4-spike starburst + soft diagonals + bright core (new procedural texture) | Freelancer/Wing Commander classic. Spikes give kinetic flare. |
| **Cylinder muzzle palette** | Warm orange/yellow HDR ×2.5 | Hotter than Pyramid — reads as "rocket burn lighting up the barrel". |
| **Cylinder muzzle sprite** | Soft radial disc puff (reuses existing `Glow_64.png`) | More restrained / less videogame-y than starburst. Forward-compatible with future rocket splash-damage visual (disc shape suggests area). Zero new texture cost. |
| **Bullet tracer palette** | Yellow-white core, hot-pink tail (head-to-tail) + pink fringe (cross-section) | `vfx_pass_ideas.md` recommendation. Hot pink is bloom-friendly + visually distinct from cool engine plumes, cool rocket smoke, warm muzzle flashes, and any future laser. |
| **Bullet tracer length** | 0.15 s `TrailRenderer.time` | Halved from initial 0.30 s after play-test — the longer trail felt smeared at the bullet's 80 u/s speed. 0.15 s reads as a clean kinetic streak that fades before the next shot in sustained fire. |
| **Bullet impact spark scale** | Small + snappy (core radius ~0.10, spike length ~0.15, ~5–7 streaks, ~0.10 s burst) | User requirement: "reads as weapon hit, not explosion". |
| **Bullet impact ground-dust palette** | Warm tan/beige (`#EBD299`), alpha-blended | Reads as "sand / debris kicked up", future-proof for the desert level (ROADMAP item 10). |
| **Spark/dust co-occurrence rule** | Spark always fires; dust additionally fires when `Dot(normal, up) > 0.7` | Matches `PyramidWeapon.FrontalDotThreshold` (cos 45°) for cross-codebase consistency. Both toggles independent — user can have spark-only, dust-only, both, or neither. |
| **Rocket exhaust plume palette** | Warm yellow-orange HDR ×2.5, fading to deep red | Hollywood rocket burn. Contrasts cool ship engine plume — clearest "rocket, not engine" read. |
| **Rocket smoke trail palette** | Cool grey-white (alpha-blended, not additive) | Neutral "smoke" reading. Sits cleanly against starfield without competing with any other VFX. |
| **Rocket smoke puff palette** | Soft white (reuses `RcsPuffMat` from B-1 at lower emission rate, longer lifetime) | Adds shape texture to the smooth trail ribbon — sells "physical exhaust gases" instead of flat ribbon. Zero new material cost. |
| **Rocket smoke puff growth** | 10× growth across lifetime (start 0.05, end 0.50) | User-specified: "start small, grow fast, lose opaqueness, despawn". |

## Architecture — Approach A (Distributed per-component)

VFX lives next to the thing it visualises — matches the established B-1 pattern (`ThrusterVfx` owns engine plume + boost flare; `RcsPuffVfx` owns corner emitters). No central VFX orchestrator, no event bus, no pool. Three wiring channels:

| VFX category | Prefab/material ref lives on | Wired by |
|---|---|---|
| **Per-cube** — muzzle flash on Pyramid/Cylinder | `WeaponBehavior` (new `[SerializeField] GameObject muzzlePrefab`) | `FlyController.BuildConstruct` calls `weapon.SetMuzzlePrefab(...)` per weapon type — mirrors `ThrusterVfx.SetPlumePrefab` |
| **Per-projectile** — bullet tracer, rocket exhaust plume, rocket smoke trail, rocket smoke puffs | `Bullet` / `Rocket` (new `[SerializeField]` material + prefab fields, **wired into the projectile prefab by the installer**) | `Bullet.Awake` / `Rocket.Awake` read the fields, gate on `VfxSettings.*`, instantiate or `AddComponent` accordingly |
| **Impact** — spark, dust | `ProjectileHit` (static `SparkPrefab` / `DustPrefab` fields) | `FlyController.Start` calls `ProjectileHit.ConfigureImpactPrefabs(spark, dust)` from its own `[SerializeField]` refs |

Why three channels and not one: FlyController already holds prefab references for all of B-1 (engine plume, RCS puff); per-cube weapons get the same treatment. Projectiles are spawned dynamically (not pre-baked into the construct), so their VFX refs live on the projectile prefab itself, set by the installer for clone-friendly correctness. Impact VFX is fired from a static helper used by both projectile types, so it gets a static config slot configured once per FlyScene load.

### Sub-decision defaults baked in

1. **Muzzle**: base-class hook in `WeaponBehavior` with `[SerializeField] GameObject muzzlePrefab` + `protected void PlayMuzzleVfx(pos, rot, toggle)` helper. Subclasses call it from inside `Fire()`. *Not* a separate `WeaponMuzzleVfx` MonoBehaviour added at construct-build (would have parity with `ThrusterVfx` but adds a component per weapon with no benefit for one-shot bursts).
2. **Bullet tracer**: added in `Bullet.Awake()` by creating a dedicated child GameObject (`Tracer`) and `AddComponent<TrailRenderer>` + `AddComponent<LingeringTrail>` on the child. Configured in code. *Not* a prefab edit (fragile + would need a manual second wiring step every time the prefab is touched). The child-GameObject pattern is required: hosting the TrailRenderer on the bullet root would defeat `LingeringTrail.DetachAndFade` — `SetParent(null)` cannot rescue the root from its own destruction.
3. **`LingeringTrail` helper**: shared ~25-line MonoBehaviour for Bullet + Rocket trail detach. *Not* inline destroy-time logic in two places (avoids drift if the detach pattern needs to evolve).
4. **Installer extension**: extend `VfxAssetsInstaller`, not a sibling `VfxWeaponAssetsInstaller`. Single source of truth, idempotent, one MenuItem.

### Hook surfaces (verified against actual code)

- `WeaponBehavior.Fire` is `protected abstract`. Subclasses already own the override.
- `PyramidWeapon.Fire` already computes `tipPos = transform.TransformPoint(Vector3.up * 0.5f)` and `fireDir` — both directly reusable as muzzle position + rotation.
- `CylinderWeapon.Fire` already computes `exitPos = spawnPos + launchDir * launchExitDistance` — this is one barrel-length out, the correct muzzle anchor (not the cylinder centre where the rocket spawns).
- `Bullet.Update` (around line 80) calls `ProjectileHit.ApplyAndLog(hit, _damage, _firingConstruct, TAG)` then `Destroy(gameObject)`. The new `ProjectileHit.SpawnImpactVfx(hit)` slots in between those two calls.
- `Rocket.Update` (around line 81) — same pattern, same insertion point.
- `FlyController.BuildConstruct` already iterates placements and dispatches per type (~line 489 for thrusters). Weapons get a sibling `if/else if` block in the same loop.

## New + modified files

### New files (15)

| Path | Purpose | Notes |
|---|---|---|
| `Assets/Scripts/Fly/LingeringTrail.cs` | Shared MonoBehaviour. `DetachAndFade()` method: `SetParent(null, true)`, `emitting = false`, `autodestruct = true`. Lives on a dedicated child GameObject hosting the TrailRenderer (so detaching the child saves it from parent destruction). Called explicitly by `Bullet.OnDestroy` / `Rocket.OnDestroy` before parent destruction completes. | ~25 lines |
| `Assets/VFX/Textures/MuzzleStarburst_64.png` | Procedural 64×64 RGBA. White core + 4 cardinal spikes + 4 fainter 45° diagonals + radial gradient falloff. | Generated by installer; skipped if exists |
| `Assets/VFX/Textures/BulletTracerStripe_8x32.png` | Procedural 8×32 RGBA. Cross-section gradient: V=0.5 white core, V=0/V=1 transparent pink edges. Used as the BulletTracerMat texture for the pink-fringe-across-width effect. | Generated by installer; skipped if exists |
| `Assets/VFX/Materials/MuzzleStarburstMat.mat` | URP Particles/Unlit additive. Tint `(1.00, 0.96, 0.75)`. **Shared between** Pyramid muzzle flash AND bullet impact spark (both layers). | Generated by installer; idempotent |
| `Assets/VFX/Materials/MuzzleDiscMat.mat` | URP Particles/Unlit additive. Tint `(1.00, 0.70, 0.30)`. Uses `Glow_64.png`. | Generated by installer; idempotent |
| `Assets/VFX/Materials/BulletTracerMat.mat` | URP Particles/Unlit additive. Uses `BulletTracerStripe_8x32.png`. TrailRenderer's `colorGradient` adds the head-to-tail palette shift. | Generated by installer; idempotent |
| `Assets/VFX/Materials/BulletImpactDustMat.mat` | URP Particles/Unlit **alpha-blended** (NOT additive). Tint `(0.92, 0.82, 0.60)`. | Generated by installer; uses the new `EnsureAlphaBlendedParticleMaterial` helper (see `VfxAssetsInstaller.cs` row in Modified files). Idempotent. |
| `Assets/VFX/Materials/RocketExhaustMat.mat` | URP Particles/Unlit additive. Tint `(1.00, 0.70, 0.30) × 2.5` HDR. Uses `Glow_64.png`. | Generated by installer; idempotent |
| `Assets/VFX/Materials/RocketSmokeTrailMat.mat` | URP Particles/Unlit **alpha-blended**. Tint `(0.92, 0.95, 1.00)`. Uses `Glow_64.png`. | Generated by installer; uses the new alpha-blended helper |
| `Assets/VFX/Prefabs/MuzzleFlashStarburst.prefab` | One-shot ParticleSystem, single-particle, `stopAction = Destroy`, 0.06 s lifetime, 0.18 startSize. | Generated by installer; overwritten every run |
| `Assets/VFX/Prefabs/MuzzleFlashDisc.prefab` | One-shot ParticleSystem, single-particle, `stopAction = Destroy`, 0.10 s lifetime, 0.30 startSize, slight outward expansion. | Generated by installer; overwritten every run |
| `Assets/VFX/Prefabs/BulletImpactSpark.prefab` | One-shot ParticleSystem, two layers (core + sub-emitter sparks). Root `stopAction = Destroy`. 0.10–0.18 s lifetime. | Generated by installer; overwritten every run |
| `Assets/VFX/Prefabs/BulletImpactDust.prefab` | One-shot ParticleSystem, single layer, soft tan puff cluster. `stopAction = Destroy`. 0.25–0.40 s lifetime. | Generated by installer; overwritten every run |
| `Assets/VFX/Prefabs/RocketExhaustPlume.prefab` | Continuous looping ParticleSystem. Mirrors `EnginePlume.prefab` structure but with warm palette + no `ShockDiamond` child (no boost concept for rockets). | Generated by installer; overwritten every run |
| `Assets/VFX/Prefabs/RocketSmokePuff.prefab` | Continuous looping ParticleSystem. 1.5 s particle lifetime, 10× growth, soft white. Reuses `RcsPuffMat`. | Generated by installer; overwritten every run |

### Modified files (10)

| Path | Change |
|---|---|
| `Assets/Scripts/Core/VfxSettings.cs` | Append 8 typed-bool properties: `MuzzleFlashPyramid`, `MuzzleFlashCylinder`, `BulletTracer`, `BulletImpactSpark`, `BulletImpactDust`, `RocketExhaust`, `RocketSmokeTrail`, `RocketSmokePuff`. Update stale class header comment ("Five typed bool properties..." → reflects current count). |
| `Assets/Scripts/Core/SettingsMenu.cs` | Append 8 tuples to the `effects` array in `BuildDebugPanel`. Two-column layout auto-rebalances (8 left, 8 right). |
| `Assets/Scripts/Fly/WeaponBehavior.cs` | Add `[SerializeField] GameObject muzzlePrefab` + `public void SetMuzzlePrefab(GameObject)` + `protected void PlayMuzzleVfx(Vector3 pos, Quaternion rot, bool toggle)`. Null-guards prefab and toggle internally. |
| `Assets/Scripts/Fly/PyramidWeapon.cs` | After `Bullet.Launch(...)` in `Fire`: `PlayMuzzleVfx(tipPos, Quaternion.LookRotation(fireDir), VfxSettings.MuzzleFlashPyramid)`. |
| `Assets/Scripts/Fly/CylinderWeapon.cs` | After `Rocket.Launch(...)` in `Fire`: `PlayMuzzleVfx(exitPos, Quaternion.LookRotation(launchDir), VfxSettings.MuzzleFlashCylinder)`. Note: muzzle anchors at the open end (`exitPos`), not the cylinder centre where the rocket spawns. |
| `Assets/Scripts/Fly/Bullet.cs` | Add `[SerializeField] Material tracerMaterial`. In `Awake`: if `VfxSettings.BulletTracer && tracerMaterial != null`, create a child GameObject `Tracer` parented to the bullet (localPosition zero, localRotation identity), then `AddComponent<TrailRenderer>` + `AddComponent<LingeringTrail>` on the **child**. Configure trail params (see section "Per-effect details"). In `Update`: poll `VfxSettings.BulletTracer` and set `_trail.emitting` accordingly each frame. In `OnDestroy`: call `_lingeringTrail?.DetachAndFade()` (detaches the child from the dying bullet). After `ProjectileHit.ApplyAndLog(...)` in `Update`: call `ProjectileHit.SpawnImpactVfx(hit)`. |
| `Assets/Scripts/Fly/Rocket.cs` | Add `[SerializeField] GameObject exhaustPlumePrefab; smokePuffPrefab; [SerializeField] Material smokeTrailMaterial;`. In `Awake`: instantiate exhaust + puff as children if their toggles are on; if `RocketSmokeTrail` is on, create a child GameObject `SmokeTrail` and add TrailRenderer + LingeringTrail on the **child**. In `Update`: poll each toggle and toggle `emission.enabled` / `TrailRenderer.emitting` accordingly. In `OnDestroy`: detach trail via `LingeringTrail.DetachAndFade()`, detach plume + puff and call `ps.Stop(true, ParticleSystemStopBehavior.StopEmitting)` on each (StopEmitting keeps alive particles by default; StopEmittingAndClear would kill them). After `ProjectileHit.ApplyAndLog(...)` in `Update`: call `ProjectileHit.SpawnImpactVfx(hit)`. |
| `Assets/Scripts/Fly/ProjectileHit.cs` | Add `public static GameObject SparkPrefab; DustPrefab;`. Add `public static void ConfigureImpactPrefabs(GameObject spark, GameObject dust)` setter. Add `public static void SpawnImpactVfx(in RaycastHit hit)`: gates on `VfxSettings.BulletImpactSpark` / `BulletImpactDust`, picks spark vs dust by `Vector3.Dot(hit.normal, Vector3.up) > 0.7f` (dust is additive — fires alongside spark, not instead of), instantiates at `hit.point` oriented along `hit.normal`. **`ApplyAndLog` itself is unchanged** — Bullet/Rocket call `SpawnImpactVfx(hit)` separately to keep damage and presentation independently call-sited. |
| `Assets/Scripts/Fly/FlyController.cs` | Add 4 new `[SerializeField] GameObject` fields: `muzzleFlashStarburstPrefab`, `muzzleFlashDiscPrefab`, `bulletImpactSparkPrefab`, `bulletImpactDustPrefab`. In `BuildConstruct`, when a weapon is encountered: type-switch and call `weapon.SetMuzzlePrefab(...)`. In `Awake` (or `Start`, before any projectile can spawn): `ProjectileHit.ConfigureImpactPrefabs(bulletImpactSparkPrefab, bulletImpactDustPrefab)`. |
| `Assets/Scripts/Editor/VfxAssetsInstaller.cs` | Extend `Apply()` with: new `EnsureStarburstTexture()` + `EnsureTracerStripeTexture()` generators; six new material `EnsureAdditiveParticleMaterial` / new `EnsureAlphaBlendedParticleMaterial` calls; six new prefab generators; **two new prefab-patcher steps** (`WireBulletPrefab(tracerMat)`, `WireRocketPrefab(exhaustPrefab, puffPrefab, smokeTrailMat)`) using `PrefabUtility.LoadPrefabContents` + `SerializedObject`. Log line updated to reflect the new assets. |

### Asset wiring touchpoint summary

| Wiring | Where set | Recovery if broken |
|---|---|---|
| FlyController scene-instance fields (6 VFX-prefab refs total: 2 existing — `enginePlumePrefab`, `rcsPuffPrefab` — plus 4 new — `muzzleFlashStarburstPrefab`, `muzzleFlashDiscPrefab`, `bulletImpactSparkPrefab`, `bulletImpactDustPrefab`) | Inspector on FlyController in FlyScene | Hand-rewire; no installer support (matches B-1 status quo) |
| Bullet.prefab `tracerMaterial` field | **Set by extended installer via `SerializedObject`** | Re-run `Tools/CubeFly/Generate VFX assets` |
| Rocket.prefab `exhaustPlumePrefab` / `smokePuffPrefab` / `smokeTrailMaterial` fields | **Set by extended installer via `SerializedObject`** | Re-run `Tools/CubeFly/Generate VFX assets` |
| Material tints / blend flags / particle params | Set by installer | Re-run installer |
| Procedural sprites (`MuzzleStarburst_64.png`, `BulletTracerStripe_8x32.png`) | First-creation only, then skipped | Delete the file + re-run installer |

## Per-effect implementation details

Each spec block is concrete enough that the installer code can be written directly. All numeric values are first-pass defaults pulled from B-1 calibration — tunable in play-test.

### 1 — Pyramid muzzle flash (`MuzzleFlashStarburst.prefab`)

```
main:        duration 0.10, loop false, startLifetime 0.06,
             startSpeed 0, startSize 0.18,
             startColor (1, 0.96, 0.75) × 3.0    # HDR warm yellow-white
             simulationSpace Local, maxParticles 8,
             playOnAwake true, stopAction Destroy
emission:    bursts [(t=0, count=1)]
shape:       Sphere, radius 0.05
sizeOverLifetime:  1.0 → 0.4
colorOverLifetime: alpha 0→1 (0–10%), 1→0 (10–100%)
renderer:    Billboard, MuzzleStarburstMat
```

Spawned by `WeaponBehavior.PlayMuzzleVfx(tipPos, Quaternion.LookRotation(fireDir), VfxSettings.MuzzleFlashPyramid)` from `PyramidWeapon.Fire`. The rotation is along the fire direction — useful for future Stretch-mode flashes; harmless for billboards.

### 2 — Cylinder muzzle flash (`MuzzleFlashDisc.prefab`)

Same structure as Pyramid muzzle, different params:

```
main:        duration 0.12, loop false, startLifetime 0.10,
             startSpeed 0.5, startSize 0.30,
             startColor (1, 0.70, 0.30) × 2.5    # HDR warm orange/yellow
             simulationSpace Local, maxParticles 6,
             playOnAwake true, stopAction Destroy
emission:    bursts [(t=0, count=1)]
shape:       Sphere, radius 0.05
sizeOverLifetime:  0.6 → 1.4   # puff expands outward
colorOverLifetime: alpha 0→1 (0–15%), 1→0 (15–100%)
renderer:    Billboard, MuzzleDiscMat
```

Spawned by `WeaponBehavior.PlayMuzzleVfx(exitPos, Quaternion.LookRotation(launchDir), VfxSettings.MuzzleFlashCylinder)` from `CylinderWeapon.Fire`. Anchors at the barrel's open end (`exitPos = transform.position + transform.up * launchExitDistance`), not the cylinder centre.

### 3 — Bullet tracer (`Bullet.Awake` + `LingeringTrail`)

TrailRenderer added in `Bullet.Awake` when `VfxSettings.BulletTracer && tracerMaterial != null`:

```
trail:
  time:               0.15      # halved from initial 0.30 after play-test (felt smeared at 80 u/s)
  startWidth:         0.05
  endWidth:           0.02
  minVertexDistance:  0.10
  material:           BulletTracerMat   (uses BulletTracerStripe_8x32.png)
  colorGradient:
    t=0%:   (1.00, 1.00, 1.00) α=1.0    # white head
    t=50%:  (1.00, 0.96, 0.70) α=0.85   # warm yellow mid
    t=100%: (1.00, 0.40, 0.75) α=0.0    # pink tail
```

The pink-fringe-across-width effect comes from `BulletTracerStripe_8x32.png` (cross-section gradient). The head-to-tail palette shift comes from `colorGradient`. Two complementary mechanisms, one TrailRenderer.

The `TrailRenderer` and `LingeringTrail` both live on a dedicated child GameObject (`Tracer`) parented to the bullet. `Bullet.OnDestroy` calls `_lingeringTrail.DetachAndFade()` — which `SetParent(null)`'s the **child**, saving it from the bullet's destruction; the orphan child then fades per `TrailRenderer.time` and autodestructs. Hosting the trail on the bullet root would defeat this pattern.

`Bullet.Update` polls `VfxSettings.BulletTracer` each frame and sets `_trail.emitting` accordingly — supports mid-flight toggle changes for the Debug-tab A/B comparison.

### 4 — Bullet impact spark (`BulletImpactSpark.prefab`)

Two-layer prefab. Root system has `stopAction = Destroy`; the whole GameObject self-cleans after both layers finish.

**Layer 1 — Core (root):**
```
main:        duration 0.10, loop false, startLifetime 0.08,
             startSpeed 0, startSize 0.10,
             startColor (1, 0.96, 0.75) × 3.0
             simulationSpace Local, maxParticles 3,
             playOnAwake true, stopAction Destroy
emission:    bursts [(t=0, count=1)]
shape:       Sphere, radius 0.02
sizeOverLifetime:  1.0 → 0.5
colorOverLifetime: alpha 0→1 (0–10%), 1→0 (10–100%)
renderer:    Billboard, MuzzleStarburstMat
```

**Layer 2 — Sparks (child of root):**
```
main:        duration 0.10, loop false, startLifetime [0.12, 0.18] random,
             startSpeed 2.0, startSize 0.04,
             startColor (1, 0.96, 0.70) × 2.5
             simulationSpace World, maxParticles 8,
             playOnAwake true, stopAction None
emission:    bursts [(t=0, count=6)]
shape:       Hemisphere, radius 0.02
             # The whole prefab is instantiated with Quaternion.LookRotation(hit.normal),
             # so the hemisphere fires OUTWARD from the surface.
sizeOverLifetime:  1.0 → 0.2
colorOverLifetime: white → hot pink (1, 0.4, 0.75) with alpha 1→0
renderer:    Stretch, lengthScale 0, velocityScale 0.4, MuzzleStarburstMat
```

Instantiated by `ProjectileHit.SpawnImpactVfx(hit)` at `hit.point` oriented via `Quaternion.LookRotation(hit.normal)`. Toggle-gated on `VfxSettings.BulletImpactSpark`.

### 5 — Bullet impact ground dust (`BulletImpactDust.prefab`)

Single-layer soft tan puff cluster:

```
main:        duration 0.30, loop false, startLifetime [0.25, 0.40] random,
             startSpeed 0.8, startSize [0.15, 0.25] random,
             startColor (0.92, 0.82, 0.60, 1)   # warm tan, alpha-blended
             simulationSpace World, maxParticles 8,
             playOnAwake true, stopAction Destroy
emission:    bursts [(t=0, count=5)]
shape:       Hemisphere, radius 0.06
             # aligned with hit.normal so puffs rise from the surface
sizeOverLifetime:  0.6 → 1.6    # puffs grow as they drift
colorOverLifetime: alpha 0→0.7 (0–10%), 0.7→0 (10–100%)
renderer:    Billboard, BulletImpactDustMat   (alpha-blended, NOT additive)
```

Instantiated by `ProjectileHit.SpawnImpactVfx(hit)` only when `Vector3.Dot(hit.normal, Vector3.up) > 0.7f` (matches `PyramidWeapon.FrontalDotThreshold` = cos 45°). Toggle-gated on `VfxSettings.BulletImpactDust`. Spark and dust are independent toggles and **not mutually exclusive** — both, either, or neither can fire.

### 6 — Rocket exhaust plume (`RocketExhaustPlume.prefab`)

Continuous looping ParticleSystem. Mirrors `EnginePlume.prefab` structure minus the `ShockDiamond` child:

```
main:        duration 5.0, loop true, startLifetime 0.18,
             startSpeed 4.0, startSize 0.15,
             startColor (1, 0.70, 0.30) × 2.5    # HDR warm yellow-orange
             simulationSpace World, maxParticles 60,
             playOnAwake true, stopAction Destroy
emission:    rateOverTime 35
shape:       Cone, angle 6°, radius 0.04
             # Cone emits along its OWN local +Y. The plume's
             # GameObject is parented to the rocket with
             # localRotation = Quaternion.Euler(180, 0, 0) — rotating
             # 180° around X flips the local +Y to point along the
             # rocket's local -Y (= -transform.up = backward in world,
             # since MeshAlignment maps rocket local +Y to launchDir).
colorOverLifetime: white → (1, 0.45, 0.10) with alpha 0→1 (0–15%), 1→0 (15–100%)
sizeOverLifetime: curve (0%, 0.6) → (40%, 1.0) → (100%, 0.3)
renderer:    Stretch, lengthScale 0, velocityScale 1.2, RocketExhaustMat
```

Instantiated as a child of Rocket in `Rocket.Awake` (toggle-gated on `VfxSettings.RocketExhaust`). Positioned at `localPosition = Vector3.zero`, oriented to fire opposite to flight direction. Rocket flies along +Y, so plume cone fires along -Y of rocket's local frame.

`Rocket.Update` polls `VfxSettings.RocketExhaust` each frame and sets `emission.enabled` accordingly.

**Cleanup:** `Rocket.OnDestroy` detaches the plume child via `transform.SetParent(null, true)`, then calls `_plumePs.Stop(true, ParticleSystemStopBehavior.StopEmitting)` (StopEmitting keeps alive particles by default — use `StopEmittingAndClear` only if you want them killed). Alive particles finish their 0.18 s lifetimes naturally; `stopAction = Destroy` then removes the orphan GameObject.

### 7a — Rocket smoke trail (`Rocket.Awake` + `LingeringTrail`)

TrailRenderer added in `Rocket.Awake` when `VfxSettings.RocketSmokeTrail && smokeTrailMaterial != null`:

```
trail:
  time:               1.0
  startWidth:         0.20
  endWidth:           0.05
  minVertexDistance:  0.05
  material:           RocketSmokeTrailMat   (alpha-blended, NOT additive)
  colorGradient:
    t=0%:   (0.92, 0.95, 1.00) α=0.70    # cool grey-white near rocket
    t=100%: (0.92, 0.95, 1.00) α=0.0     # fades to transparent
```

Single-tint ribbon, no head-to-tail palette shift. Like Bullet's tracer, the `TrailRenderer` + `LingeringTrail` live on a dedicated child GameObject (`SmokeTrail`) parented to the rocket. `Rocket.OnDestroy` calls `_smokeTrailLingering.DetachAndFade()` to detach the child before the rocket dies. `Rocket.Update` polls `VfxSettings.RocketSmokeTrail` and sets `_smokeTrail.emitting` accordingly.

### 7b — Rocket smoke puffs (`RocketSmokePuff.prefab`)

Continuous looping ParticleSystem on a child of Rocket. Reuses `RcsPuffMat` from B-1 — no new material needed:

```
main:        duration 5.0, loop true, startLifetime 1.5,
             startSpeed 0.3,                # slow backward drift
             startSize [0.05, 0.10] random,
             startColor (1, 1, 1, 0.85)
             simulationSpace World, maxParticles 80,
             playOnAwake true, stopAction Destroy
emission:    rateOverTime 15
shape:       Cone, angle 5°, radius 0.06
             # Same parent + localRotation = Quaternion.Euler(180, 0, 0)
             # pattern as exhaust plume to flip Cone's local +Y emission
             # onto the rocket's local -Y (backward in world).
colorOverLifetime: white → (0.92, 0.95, 1.0) with alpha 0→0.9 (0–10%), 0.9→0 (10–100%)
sizeOverLifetime: 0.1 → 1.0    # 10× growth (user-specified)
renderer:    Billboard, RcsPuffMat   (REUSED from B-1)
```

Instantiated as a child of Rocket in `Rocket.Awake`, gated on `VfxSettings.RocketSmokePuff`. `Rocket.Update` polls and toggles `emission.enabled`. Cleanup identical to exhaust plume — detach + `Stop(StopEmitting)` + `stopAction = Destroy` self-cleans.

### `LingeringTrail` helper

```csharp
using UnityEngine;

namespace CubeFly.Fly
{
    // Tiny detach helper for TrailRenderers that are children of
    // short-lived projectiles. Without this, killing the projectile
    // vanishes the trail mid-air with a visible pop.
    //
    // The detach must happen BEFORE Unity destroys the parent's
    // hierarchy (not during the child's OnDestroy) — by then the
    // hierarchy cleanup race makes SetParent(null) unreliable across
    // Unity versions. So the parent (Bullet / Rocket) calls
    // DetachAndFade() explicitly from its own OnDestroy before
    // returning.
    [RequireComponent(typeof(TrailRenderer))]
    public class LingeringTrail : MonoBehaviour
    {
        public void DetachAndFade()
        {
            transform.SetParent(null, true);   // worldPositionStays = true
            TrailRenderer trail = GetComponent<TrailRenderer>();
            if (trail == null) return;
            trail.emitting = false;
            trail.autodestruct = true;
        }
    }
}
```

Used by both Bullet and Rocket — each creates a dedicated child GameObject (`Tracer` / `SmokeTrail`) parented to the projectile that hosts the TrailRenderer + LingeringTrail. The child-GameObject pattern is essential: calling `DetachAndFade` on a `LingeringTrail` attached to the bullet/rocket root itself would be a no-op (the root cannot `SetParent(null)` its way out of its own destruction), so the trail would die with the projectile.

## Toggle plumbing

### `VfxSettings.cs` additions

```csharp
const string KMuzzleFlashPyramid  = "VfxMuzzleFlashPyramid";
const string KMuzzleFlashCylinder = "VfxMuzzleFlashCylinder";
const string KBulletTracer        = "VfxBulletTracer";
const string KBulletImpactSpark   = "VfxBulletImpactSpark";
const string KBulletImpactDust    = "VfxBulletImpactDust";
const string KRocketExhaust       = "VfxRocketExhaust";
const string KRocketSmokeTrail    = "VfxRocketSmokeTrail";
const string KRocketSmokePuff     = "VfxRocketSmokePuff";

public static bool MuzzleFlashPyramid  { get => Get(KMuzzleFlashPyramid);  set => Set(KMuzzleFlashPyramid,  value); }
public static bool MuzzleFlashCylinder { get => Get(KMuzzleFlashCylinder); set => Set(KMuzzleFlashCylinder, value); }
public static bool BulletTracer        { get => Get(KBulletTracer);        set => Set(KBulletTracer,        value); }
public static bool BulletImpactSpark   { get => Get(KBulletImpactSpark);   set => Set(KBulletImpactSpark,   value); }
public static bool BulletImpactDust    { get => Get(KBulletImpactDust);    set => Set(KBulletImpactDust,    value); }
public static bool RocketExhaust       { get => Get(KRocketExhaust);       set => Set(KRocketExhaust,       value); }
public static bool RocketSmokeTrail    { get => Get(KRocketSmokeTrail);    set => Set(KRocketSmokeTrail,    value); }
public static bool RocketSmokePuff     { get => Get(KRocketSmokePuff);     set => Set(KRocketSmokePuff,     value); }
```

All default to `true` via the existing `PlayerPrefs.GetInt(key, 1)` rule. The `Changed` event fires on set (the new keys do not need volume re-application since they are not post-processing effects).

Also update the class header doc comment: *"Five typed bool properties..."* is already stale after B-1. Replace with *"the typed bool properties below"* or *"sixteen typed bool properties"* to stop the count from drifting further.

### `SettingsMenu.cs` — `BuildDebugPanel` additions

Append eight tuples to the `effects` array in the existing order (Pyramid muzzle, Cylinder muzzle, bullet tracer, bullet impact spark, bullet impact dust, rocket exhaust, rocket smoke trail, rocket smoke puff). The existing `leftCount = (effects.Length + 1) / 2` auto-rebalances columns. 16 entries → 8 left, 8 right.

Tooltip text suggestion (kept short, descriptive of the visual not the implementation):

| Toggle label | Tooltip |
|---|---|
| Pyramid muzzle | One-frame yellow-white starburst at the Pyramid weapon's tip when it fires. |
| Cylinder muzzle | Soft orange/yellow disc puff at the Cylinder weapon's barrel when it fires. |
| Bullet tracer | Yellow-white core + pink-fringe trail behind machine-gun bullets in flight. |
| Bullet impact spark | Bright warm spark + radiating streaks where a bullet hits any surface. |
| Bullet impact dust | Soft tan puff cluster where a bullet hits a roughly-upward surface. |
| Rocket exhaust | Warm orange/yellow flame from the rocket's tail while in flight. |
| Rocket smoke trail | Cool grey-white ribbon trailing behind the rocket in flight. |
| Rocket smoke puffs | Soft white cloud puffs emitted from the rocket's tail. |

## Runtime behaviour

The Debug tab is an A/B comparison surface — toggles must take visible effect immediately, matching B-1's `ThrusterVfx` polling rule.

| Effect class | Poll? | How |
|---|---|---|
| One-shot bursts (muzzle, impact spark, impact dust) | **No** | Toggle is checked at instantiation time. Already-spawned bursts (≤ 0.4 s lifetime) finish naturally. |
| Continuous effects on long-lived hosts (bullet tracer, rocket exhaust / smoke trail / smoke puffs) | **Yes, every Update** | The host (`Bullet.Update`, `Rocket.Update`) reads the relevant `VfxSettings.*` properties and adjusts `TrailRenderer.emitting` / `ParticleSystem.emission.enabled` accordingly. ~3 lines per effect. Negligible cost (handful of projectiles alive at any time). |

Flipping "Bullet tracer" OFF mid-volley visibly stops new trail vertices on existing bullets within one frame; existing vertices fade per `TrailRenderer.time`. Flipping ON resumes emission. Same for the rocket effects. Live A/B comparison preserved.

## Edge cases & null-guards

1. **Self-construct hits** — already filtered by `ProjectileHit.TrySweep` before `ApplyAndLog`. `SpawnImpactVfx` only sees non-self hits.
2. **Scene unload mid-flight** — VFX GameObjects are scene-rooted; they unload with the scene. No leaks.
3. **`Time.timeScale = 0` (pause menu)** — TrailRenderer and ParticleSystem honour `Time.timeScale` by default. VFX freezes while paused, resumes on unpause. Matches B-1.
4. **Missing prefab / material references** — null-guard every consumer:
   - `WeaponBehavior.PlayMuzzleVfx` early-returns if `muzzlePrefab == null` or `!toggle`.
   - `Bullet.Awake` skips TrailRenderer add if `tracerMaterial == null` or `!VfxSettings.BulletTracer`.
   - `Rocket.Awake` skips each child instantiation / TrailRenderer add independently per missing-ref or toggle-off.
   - `ProjectileHit.SpawnImpactVfx` early-returns per-branch if its respective prefab is null or its toggle is off.
5. **Destroy-ordering for TrailRenderers** — Unity destroys a parent's children alongside it; child `OnDestroy` racing with hierarchy cleanup makes `SetParent(null)` unreliable. **Resolved** by exposing `LingeringTrail.DetachAndFade()` as a public method called explicitly by the parent's `OnDestroy` before returning. Same explicit-call pattern for `ParticleSystem` children: `Rocket.OnDestroy` detaches plume + puff and calls `Stop(true, ParticleSystemStopBehavior.StopEmitting)` on each — `StopEmitting` (unlike `StopEmittingAndClear`) keeps already-alive particles alive to finish their lifetimes.
6. **Performance bound** — at spec emission rates and sustained-fire cadences:
   - Pyramid at 5 shots/s × 0.06 s muzzle lifetime = ~0.3 muzzle-flash GOs alive on average. Trivial.
   - Up to ~5 bullets in flight simultaneously (200 u range ÷ 80 u·s⁻¹ × 5 Hz) = 5 TrailRenderers + occasional impact bursts. Well under any GPU concern.
   - 1 rocket alive at a time typically; up to ~3 with overlap = 3 × (plume 60 + puff 80 + trail). Roughly ~420 alive particles peak across all rockets. URP handles this comfortably on modest hardware. Adds < 1 ms to frame budget.
7. **`VfxSettings.Changed` event subscription** — none required. `VfxApplier` already listens for the post-processing keys; the new weapon-VFX keys are read polled by Update logic, not pushed via event. No new subscribers, no leak risk.
8. **Bullet/Rocket spawn before FlyController.Awake completes** — impossible in practice: weapons cannot fire before `BuildConstruct` completes, which runs in `FlyController.Start`. `ProjectileHit.ConfigureImpactPrefabs` runs in `FlyController.Awake` (one phase earlier), so static refs are always set before any projectile spawns. Defensive null-guard in `SpawnImpactVfx` covers the case anyway.

## Testing — manual play-test checklist

No automated test infrastructure (no asmdefs, no EditMode/PlayMode tests, per CLAUDE.md). Verification is manual via the Unity Editor.

**Setup once per session:**
- Open FlyScene from MainMenu (or direct-Play FlyScene) with a construct that has at least one Pyramid weapon and one Cylinder weapon.
- Open Settings → Debug tab. All 16 toggles default ON; confirm the 8 new ones appear in the two-column layout.

**Smoke pass (defaults ON):**
- [ ] Fire the Pyramid weapon — yellow-white starburst flashes at the tip per shot. Bullet leaves a yellow-pink TrailRenderer trail that fades behind it.
- [ ] Hit a target with a Pyramid bullet — bright warm spark + radiating streaks appear at the impact point.
- [ ] Hit the top of a target (or asteroid top) with a Pyramid bullet — soft tan dust puff appears in addition to the spark per the dot threshold.
- [ ] Fire the Cylinder weapon — warm orange/yellow disc puff flashes at the barrel.
- [ ] Watch a Rocket in flight — bright warm exhaust plume at tail + cool grey-white smoke ribbon + soft white puff cluster trailing.
- [ ] Let a rocket hit a target — VFX detach cleanly, no pop (trail finishes fading, plume/puff particles complete their lifetimes off-rocket).
- [ ] Let a rocket exit max range without hitting — same clean detach.

**Toggle pass (mid-flight):**
- [ ] During sustained Pyramid fire: toggle "Bullet tracer" OFF → new vertices stop appearing on existing trails within one frame; existing vertices fade per `time`. Toggle back ON → emission resumes.
- [ ] During an active rocket flight: toggle "Rocket exhaust" OFF → flame stops immediately; in-flight rocket smoke trail / puffs unaffected. Toggle each rocket subsystem independently.
- [ ] Toggle "Pyramid muzzle" OFF → next Pyramid shot has no muzzle flash; bullet still fires and tracer still emits.
- [ ] Toggle "Bullet impact spark" OFF but leave "Bullet impact dust" ON → vertical-surface hits show nothing; upward-surface hits still puff.

**Stress pass:**
- [ ] Hold fire for 10 s on a construct with multiple Pyramids and Cylinders, with all 16 VFX toggles ON. Frame rate stays comfortably above 60 fps in the Editor's Game view (no GC stutter from particle allocations).
- [ ] Pause (ESC) mid-rocket-flight → VFX freezes; unpause → resumes.

**Persistence pass:**
- [ ] Toggle a few keys OFF in Settings, close the game, reopen → toggles remain OFF.

**Regression pass (B-1 unchanged):**
- [ ] Engine plume, boost flare, RCS puff all behave as before.
- [ ] Post-processing toggles (Bloom, Vignette, etc.) still affect the scene as before.

The implementation plan (`docs/superpowers/plans/2026-05-25-vfx-pass-phase-b2-weapons.md` — produced by the next step) turns these into per-commit verification gates, with `mcp__unityMCP__refresh_unity` + `mcp__unityMCP__read_console(types=["error"])` after each script touch (per the resume-doc hygiene rule).

## Hygiene notes carried over from B-1

- **Material drift on `Assets/Materials/{Bullet,Laser,Reactor,Rocket,Shield}Mat.mat`** is recurring Unity float-precision noise after material re-saves. **Never stage.** Leave unstaged or discard.
- **`CLAUDE.md`** is the user's local file. **Never touch.**
- **Per-script `.meta` files** often start minimal on `Write` (and on `mcp__unityMCP__create_script` since this session). Proactively expand them with the full `MonoImporter` block matching `PauseMenu.cs.meta`'s format to head off Copilot's stock "incomplete meta" comment.
- `mcp__unityMCP__create_script`'s validator has thrown false positives on longer scripts. If it fails, fall back to `Write` for the new file.
- After each script touch, trigger `mcp__unityMCP__refresh_unity(mode="force", compile="request", scope="scripts", wait_for_ready=true)`, wait ~2–3 s, then `mcp__unityMCP__read_console(types=["error"], count=20, filter_text="Assets/Scripts", format="detailed")` — expect zero entries.

## Workflow rhythm

- Brainstorm (this spec) → plan → **inline execution** (user-preselected) → push → Copilot review.
- Branch naming: `feat/vfx-phase-b2-weapons` off `main`.
- Plan path: `docs/superpowers/plans/2026-05-25-vfx-pass-phase-b2-weapons.md`.
- The whole PR is a single brainstorm → single spec → single plan → multiple bite-sized task commits → push → Copilot.

## Related documents

- `docs/vfx_pass_ideas.md` — VFX backlog (§2 Weapons & Projectiles, §5 Damage & Destruction)
- `docs/superpowers/specs/2026-05-25-vfx-pass-phase-b1-engines-design.md` — Phase B-1 (engines + boost flare + RCS puffs); pattern source for `ThrusterVfx`, `VfxAssetsInstaller`, polling rule
- `docs/superpowers/specs/2026-05-24-vfx-pass-phase-1-design.md` — Phase 1 (post-processing + Debug tab); pattern source for `VfxSettings`, `SettingsMenu` two-column layout
- `docs/superpowers/specs/2026-05-25-vfx-pass-phase-b2-resume.md` — brainstorm resume note that bootstrapped this session
- `docs/full_architecture.md` — file-by-file implementation map
- `docs/weapon_shooting_spec.md` — weapon/projectile subsystem deep-dive
