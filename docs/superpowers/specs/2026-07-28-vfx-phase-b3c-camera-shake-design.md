# VFX Phase B-3c — Camera Shake — Design

**Date:** 2026-07-28
**Branch:** `vfx/b3c-camera-shake`
**Status:** approved (design), pending implementation plan

## Context

Final slice of Phase B-3 (destruction + crash). B-3a (cube-death enhancement,
`0df232a`) and B-3b (low-HP feedback, `2e6aac2`) shipped. **B-3c** adds the
"camera shake on crash + nearby detonation" item from `docs/vfx_pass_ideas.md`
§5 — a physical kick to the third-person camera when the ship crashes or one of
your rockets detonates nearby, so impacts *feel* impactful.

Per the maintainer's scope decision: **crash + your rocket hits** (distance-scaled).
Bullets never shake; cube-death bursts don't shake; enemy fire doesn't shake.

## Key facts (from exploration)

- **No existing camera-shake code** — greenfield.
- **Crash hook:** `FlyCrashHandler.OnCollisionEnter` already computes
  `normalImpactSpeed = |Vector3.Dot(relativeVelocity, contact.normal)|` and only
  fires for the player construct's collisions — a clean intensity signal, already
  gated to "we crashed."
- **Rocket-detonation hook:** `Rocket.cs` Update, at the hit branch
  (`ProjectileHit.SpawnImpactVfx(hit, scale: 1.20f); Destroy(gameObject);`,
  ~line 216) — rocket-specific, so shaking here never shakes on bullets.
- **Camera:** `FlyCamera.LateUpdate` sets `transform.position` via a follow
  `Vector3.Lerp` (adaptive rate) then `transform.rotation` via `Quaternion.Slerp`.
  Shake layers on after.

## Goals

- A crash kicks the camera proportional to head-on impact speed (a gentle graze
  barely registers; a fast head-on crash shakes hard).
- A rocket detonating near the camera gives a punch that falls off with distance;
  a distant rocket does nothing.
- The shake is a **trauma-based** kick that decays smoothly, not a fixed jolt.
- It's an **additive layer** that never fights or drifts the follow camera.
- Toggleable from Settings → Debug (`VfxCameraShake`).
- Cheap, allocation-free, no per-frame cost when trauma is zero.

## Non-goals

- Shake from bullets, enemy fire, or cube-death bursts (scope decision).
- A physical impulse to the construct Rigidbody (this is camera-only).
- Screen-space post effects (radial blur etc.) — those are Phase D.

## `CameraShake` — static trauma service (`CubeFly.Fly`)

A tiny static accumulator; triggers need no camera reference (mirrors
`CubeDeath.CubeDied`).

- `static float _trauma;`
- `public static void Add(float amount)` → `_trauma = Mathf.Clamp01(_trauma + amount)`.
- `public static float Trauma => _trauma;`
- `public static void Decay(float recoverPerSec, float dt)` →
  `_trauma = Mathf.Max(0f, _trauma - recoverPerSec * dt)`.
- `public static void Reset()` → `_trauma = 0f` (called on FlyScene load so trauma
  never leaks across a scene reload; the static outlives scene loads).

## `FlyCamera` integration

Trauma shake, applied as a clean additive overlay in `LateUpdate`:

- `FlyCamera` keeps `Vector3 _shakeOffset` and `float _shakeRoll` (last frame's
  applied shake).
- **Start of `LateUpdate`:** strip last frame's shake so the follow Lerp/Slerp
  operate on the clean pose (`transform.position -= _shakeOffset;` and undo the
  roll). Then the existing follow code runs on the un-shaken pose.
- **End of `LateUpdate`:** if `VfxSettings.CameraShake` and `CameraShake.Trauma > 0`:
  - `shake = trauma * trauma` (non-linear — small trauma barely moves).
  - Per-axis **Perlin noise** in [−1, 1] sampled at `Time.time * ShakeFrequency`
    (distinct offsets per axis so they don't correlate).
  - `_shakeOffset = (right * nx + up * ny) * (shake * MaxOffset)` (screen-space).
  - `_shakeRoll = nz * shake * MaxRoll` (degrees, roll about `forward`).
  - Apply: `transform.position += _shakeOffset;` and roll about forward.
  - `CameraShake.Decay(Recovery, Time.deltaTime)`.
  - Else (`trauma == 0` or toggle off): `_shakeOffset = Vector3.zero; _shakeRoll = 0`.
- Uses `Time.deltaTime` (scaled) so shake naturally freezes during the ESC pause
  (the follow already does), and `Time.time` for the noise phase.

## Triggers

Both gated by `VfxSettings.CameraShake` at the call site (cheap early-out).

- **Crash** — in `FlyCrashHandler.OnCollisionEnter`, after the `normalImpactSpeed`
  is known (and passes `minSpeedForDamage`, reusing that gate so gentle landings
  don't shake):
  `CameraShake.Add(Mathf.Clamp(normalImpactSpeed * CrashScale, 0f, CrashMax));`
- **Rocket detonation** — in `Rocket.cs` at the hit branch, before `Destroy`:
  ```
  Camera cam = Camera.main;
  if (cam != null) {
      float dist = Vector3.Distance(cam.transform.position, hit.point);
      float falloff = Mathf.Clamp01(1f - dist / RocketRange);
      if (falloff > 0f) CameraShake.Add(RocketTrauma * falloff);
  }
  ```
  `Camera.main` per rocket-hit is fine (hits are infrequent).

## Settings toggle

One new `VfxSettings` key + one Debug-tab row (default **on**):

| Key | Property | Covers |
| --- | --- | --- |
| `VfxCameraShake` | `CameraShake` | crash + nearby-rocket camera shake |

## Tuning defaults (starting points; fine-tuned at the gate)

| Param | Where | Value |
| --- | --- | --- |
| `MaxOffset` | FlyCamera | ~0.35 u |
| `MaxRoll` | FlyCamera | ~2° |
| `ShakeFrequency` | FlyCamera | ~25 |
| `Recovery` | FlyCamera | ~1.2 /s |
| `CrashScale` | FlyCrashHandler | ~0.03 (a ~35 u/s crash → ~1.0 trauma) |
| `CrashMax` | FlyCrashHandler | 1.0 |
| `RocketTrauma` | Rocket | ~0.4 |
| `RocketRange` | Rocket | ~40 u |

## Edge cases / risks

- **Follow feedback:** handled by the strip-before / re-apply-after pattern — the
  follow Lerp/Slerp always see the clean pose, so shake never accumulates or drags
  the camera off its follow target.
- **Scene reload:** `CameraShake.Reset()` on FlyScene load (a `FlyController.Awake`
  call, alongside the other Configure calls) clears leftover trauma.
- **Pause:** with `Time.deltaTime` the shake + decay freeze during the ESC overlay,
  matching the camera body; acceptable.
- **`Camera.main` cost:** only called on rocket hits (rare), not per frame.
- **Crash on a target vs terrain:** `OnCollisionEnter` fires for both; both should
  shake (you hit something hard) — no distinction needed.
- **Multiple rockets / rapid crashes:** trauma clamps at 1.0, so it can't runaway;
  it just stays maxed briefly then decays.

## Verification & gate

Autonomous (mechanism, not live feel — the MCP editor freezes the game loop so
`Update`-driven shake can't be animated headlessly): confirm `CameraShake.Add`
raises `Trauma`, `Decay` lowers it, the toggle gates it, and the trigger call
sites resolve. Maintainer **fly + shoot gate** (the real check):
- Crash into terrain at speed → strong shake; graze gently → little/none.
- Fire a rocket into a **near** target → a punch; a **far** rocket → nothing.
- Bullets → no shake.
- Toggle **Camera shake** in Settings → Debug → shake stops/starts.
- Fine-tune `MaxOffset` / `MaxRoll` / `Recovery` / the trauma scales to taste.

## Implementation outline (for writing-plans)

1. `CameraShake` (new, Fly) — static trauma accumulator (`Add`/`Trauma`/`Decay`/`Reset`).
2. `VfxSettings` — add `CameraShake` key + property.
3. `SettingsMenu` Debug tab — add the toggle row.
4. `FlyCamera` — strip/re-apply shake overlay in `LateUpdate` + tuning consts.
5. `FlyCrashHandler` — `CameraShake.Add` from `normalImpactSpeed` (toggle-gated).
6. `Rocket` — `CameraShake.Add` with distance falloff at the hit branch (toggle-gated).
7. `FlyController.Awake` — `CameraShake.Reset()` on scene load.
8. Compile-check each; Play-mode mechanism check + maintainer fly gate; land ff.
   B-3 complete → ROADMAP moves to B-4.
