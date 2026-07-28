# VFX Phase B-3b — Low-HP Feedback — Design

**Date:** 2026-07-28
**Branch:** `vfx/b3b-low-hp`
**Status:** approved (design), pending implementation plan

## Context

Second slice of Phase B-3 (destruction + crash). B-3a (cube-death enhancement)
shipped on `main` (`0df232a`). **B-3b** is the "wear and tear of a sustained
fight" pass from `docs/vfx_pass_ideas.md` §5: when a cube drops below 25% HP it
should *read* as failing — sustained smoke rising off it, and (for the player's
own ship) a pulsing red emissive alarm.

Per the maintainer's scope decision:
- **Sustained smoke → all cubes** (player construct, world targets, turrets) —
  a universal "nearly dead" indicator, incl. "finish him" feedback on targets.
- **Red emissive flicker → player construct cubes only** — a "your ship is
  failing" alarm; enemy targets just smoke.

B-3c (camera shake) is a separate slice, out of scope here.

## Key facts (from exploration)

- `CubeStats` (Core) stores only current `healthPoints` (set at build from
  `MaterialDefinition.healthPoints` for material cubes, `ShipClass.AlphaHealthPoints`
  for the alpha, or the prefab value for world targets). **No max-HP is stored.**
- HP is **monotonic non-increasing**: `TakeDamage` / `TakeRawDamage` only reduce
  it; shields regen a separate pool, HP itself never heals. So "below 25%" is a
  **one-way latch** until death — there is no "recovered above 25%" transition to
  handle.
- `CubeDamage.ApplyAndLog` (Fly) already computes
  `energy = stats.GetComponentInParent<ConstructEnergySystem>()` for shield
  interception. `energy != null` ⇔ the cube is part of the **player construct**
  (world targets / turrets have no energy system). Free player/enemy discriminator.
- The delete tool (`BuildManager`) already tints a hovered cube red via a
  `MaterialPropertyBlock` on `_EmissionColor` (`Shader.PropertyToID("_EmissionColor")`),
  cleared on un-hover — the exact pattern to mirror (animated) for the flicker.

## Goals

- A cube below 25% HP emits sustained dark smoke that rides it through motion.
- A **player** cube below 25% HP additionally pulses red (emissive), as an alarm.
- Fires the instant a cube crosses the threshold; persists until the cube dies.
- Cheap, per-cube, no material instantiation (MaterialPropertyBlock), no idle
  cost on healthy cubes.
- Both effects independently toggleable in Settings → Debug, live (poll each frame).
- On death, the low-HP VFX bows out cleanly so the B-3a death burst + drift take
  over.

## Non-goals

- Camera shake (B-3c).
- Any "heal / recover above 25%" behaviour (HP is monotonic — impossible here).
- Flicker on enemy targets / turrets (smoke only, by scope decision).
- Reworking `CubeStats` HP semantics beyond adding a max-HP baseline.

## Max-HP baseline

`CubeStats` gains:
- `public float MaxHealthPoints { get; private set; }` — captured **lazily as
  `hpBefore` on the first HP-reducing hit** (inside `TakeDamage` / `TakeRawDamage`,
  guarded `if (MaxHealthPoints <= 0f) MaxHealthPoints = hpBefore;`, right before
  the decrement). Bulletproof against Unity lifecycle timing and uniform across
  all cube types, because HP is monotonic — the first real hit always sees peak HP.
- `public float HealthFraction => MaxHealthPoints > 0f ? healthPoints / MaxHealthPoints : 1f;`
  (returns 1 before any damage, so an undamaged cube never reads as "low").

No changes to the build sites; no serialized field; no timing dependency.

## Trigger (`CubeDamage.ApplyAndLog`)

Fold into the existing alive-path early return. After the damage log, before
`return applied;` for a surviving cube:

```
if (stats.healthPoints > 0f)
{
    if (stats.HealthFraction < LowHpThreshold          // 0.25
        && stats.GetComponent<LowHpVfx>() == null)     // attach once
    {
        stats.gameObject.AddComponent<LowHpVfx>().Configure(isPlayer: energy != null);
    }
    return applied;
}
// dead path (alpha end-of-run / CubeDeath) unchanged below
```

`LowHpThreshold = 0.25f` (a const in `CubeDamage` or `LowHpVfx`). Attaches at most
one `LowHpVfx` per cube (idempotent). Dead cubes fall through to the existing
CubeDeath path; a `LowHpVfx` already on a now-dying cube self-cleans (below).

## `LowHpVfx` component (new, `CubeFly.Core`, per-cube)

Attached to the failing cube. Mirrors the `CubeDeath` static-config pattern.

- **Static config:** `static GameObject SmokePrefab;` +
  `static void ConfigureVfx(GameObject smoke)`, called once by
  `FlyController.Awake` (alongside `CubeDeath.ConfigureVfx` /
  `ProjectileHit.ConfigureImpactPrefabs`). Null-safe → unconfigured scenes: no VFX.
- **`Configure(bool isPlayer)`** stores whether this cube may flicker.
- **Smoke (all cubes):** on init, if `SmokePrefab != null`, instantiate it as a
  child (local origin) — a looping dark alpha-blended smoke ParticleSystem. Its
  emission is enabled/disabled each `Update` per `VfxSettings.LowHpSmoke`.
- **Flicker (player only):** if `isPlayer`, each `Update` (when
  `VfxSettings.LowHpFlicker` on) pulse the cube renderer's `_EmissionColor` to a
  red whose intensity follows a sine (e.g. `Mathf.PingPong` / `sin`), written via
  a cached `MaterialPropertyBlock` on the cube's `Renderer`
  (`GetComponentInChildren<Renderer>()`). When the toggle is off, clear the block
  (restore baseline). Never touches `sharedMaterial`.
- **Death cleanup:** each `Update`, if `_stats == null || _stats.healthPoints <= 0f`:
  stop the smoke emitting (let existing puffs fade), clear the emissive block,
  and `enabled = false` (or destroy self). Ensures the B-3a death burst + drift
  aren't fighting a live smoke/flicker. (The cube is destroyed ~2 s later by
  CubeDeath, taking the smoke child with it; detaching the smoke to linger is a
  possible polish, not in this slice.)

Lives in Core (like `CubeDeath`) so `CubeDamage` (Fly) attaching it is a Fly→Core
reference. Reads `VfxSettings` (Core) directly.

## Assets (installer-generated)

- **`LowHpSmoke.prefab`** — a single looping ParticleSystem: slow upward drift,
  low emission rate (a steady wisp, not a burst), soft dark-grey/black puffs,
  short-ish lifetime, small size, `playOnAwake = true`, `loop = true`,
  World simulation space so puffs detach and rise as the cube moves. Alpha-blended
  (reuse `EnsureAlphaBlendedParticleMaterial`), NOT additive.
- **`LowHpSmokeMat`** — alpha-blended dark smoke material (glow texture, dark tint),
  or reuse an existing alpha-blended smoke material if one fits.

Generated in `VfxAssetsInstaller.Apply()` + wired onto `FlyController`'s new
serialized `lowHpSmokePrefab` field (assigned in FlyScene, as with the B-3a burst).

## Settings toggles

Two new `VfxSettings` keys + two Debug-tab rows (default **on**):

| Key | Property | Covers |
| --- | --- | --- |
| `VfxLowHpSmoke` | `LowHpSmoke` | sustained smoke on any <25%-HP cube |
| `VfxLowHpFlicker` | `LowHpFlicker` | red emissive pulse on <25%-HP **player** cubes |

## Tuning defaults (starting points; fine-tuned at the live gate)

| Param | Value |
| --- | --- |
| Low-HP threshold | 0.25 (fraction of max) |
| Smoke emission rate | ~6–10 /s |
| Smoke lifetime / size | ~1.2 s / ~0.3 |
| Smoke rise speed | ~1 u/s upward |
| Smoke colour | dark grey `~(0.15,0.15,0.15)`, alpha ~0.5 |
| Flicker colour | red `~(1, 0.15, 0.1)` (HDR ×~2 for bloom) |
| Flicker rate | ~3–4 Hz sine |

## Edge cases / risks

- **Emissive keyword:** setting `_EmissionColor` via MPB may need the material's
  `_EMISSION` keyword enabled to render. The delete tool proves the pattern works
  on placed cubes; if the flicker doesn't show at the gate, enable the keyword on
  the cube material (or via the renderer). Flagged as a gate check.
- **Many simultaneous smokers:** a dense target cluster shot to <25% spawns
  several looping smoke systems. Each is light, but watch the count at the gate
  (same spirit as B-3a's multi-death check). No pooling this slice.
- **Alpha cube:** it's a player construct cube (`energy != null`), so it flickers
  when <25% — a desirable "anchor failing" alarm. It never runs CubeDeath (end-of-run
  owns it), so its `LowHpVfx` just persists until game-over; acceptable.
- **World targets** have no `MaterialDefinition` path, but the lazy `MaxHealthPoints`
  capture (first hit) covers them uniformly.
- **Flicker restore:** clearing the `MaterialPropertyBlock` (SetPropertyBlock(null)
  or an empty block) must fully restore the cube's baseline emissive — mirror the
  delete tool's clear-on-un-hover exactly.

## Verification & gate

Play-mode fly + shoot in the desert (maintainer gate):
- Crash / take fire until a **player** cube drops <25% → it emits smoke **and**
  pulses red; the alpha cube alarms too.
- Shoot a **target** down to <25% → it smokes, **no** red flicker.
- Watch a cube die from low-HP → smoke/flicker stop and the B-3a death burst +
  drift take over cleanly (no lingering red or smoke on the drifting wreck).
- Toggle `LowHpSmoke` / `LowHpFlicker` in Settings → Debug → each responds live.
- Multi-cube smoke count in a target cluster stays reasonable.
- Fine-tune smoke density/colour + flicker rate/intensity at this gate (deferred
  by the maintainer).

## Implementation outline (for writing-plans)

1. `CubeStats` — add `MaxHealthPoints` (lazy capture) + `HealthFraction`.
2. `VfxSettings` — add `LowHpSmoke` / `LowHpFlicker` keys + properties.
3. `SettingsMenu` Debug tab — add the 2 toggle rows + tooltips.
4. `LowHpVfx` (new, Core) — static `SmokePrefab` + `ConfigureVfx`; `Configure(isPlayer)`;
   smoke child + emissive flicker; live toggle poll; death cleanup.
5. `CubeDamage.ApplyAndLog` — attach `LowHpVfx` on the <25% alive path.
6. `FlyController` — serialized `lowHpSmokePrefab` + `LowHpVfx.ConfigureVfx(...)` in `Awake`.
7. `VfxAssetsInstaller` — generate `LowHpSmokeMat` + `LowHpSmoke.prefab`; run + wire FlyScene.
8. Compile-check each; Play-mode gate + tune; land via ff; then B-3c.
