# VFX Phase B-3c — Camera Shake — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: use superpowers:executing-plans to implement this plan task-by-task (inline — drives the single shared Unity Editor on the main project root; NOT suitable for isolated subagents/worktrees). Steps use checkbox (`- [ ]`) syntax.

**Goal:** A trauma-based camera shake that kicks the third-person camera on a ship crash (scaled by impact speed) and on a nearby rocket detonation (distance-scaled), toggleable in Settings → Debug.

**Architecture:** A static `CameraShake` trauma accumulator (Fly); `FlyCamera.LateUpdate` reads trauma, applies a Perlin-noise offset scaled by `trauma²` as a *clean additive overlay* (stripped before the follow Lerp/Slerp, re-applied after), and decays trauma. `FlyCrashHandler` and `Rocket` call `CameraShake.Add(...)`; `FlyController.Awake` calls `Reset()`.

**Tech stack:** Unity 6.3 LTS / URP 17.3, `Assembly-CSharp` (no asmdefs, no test framework). Pure code — no assets/prefabs. **Per-task verification = `read_console` shows zero CS compile errors** (poll `editor_state.is_compiling == false` first). **Feature verification:** mechanism autonomously (the MCP editor freezes the game loop, so `Update`-driven shake can't be *animated* headlessly) + the maintainer fly-test for live feel.

**Branch:** `vfx/b3c-camera-shake` (created; spec committed at `7b9db94`).

**Spec:** `docs/superpowers/specs/2026-07-28-vfx-phase-b3c-camera-shake-design.md`

---

## File structure

| File | Change | Responsibility |
| --- | --- | --- |
| `Assets/Scripts/Fly/CameraShake.cs` | **create** | static trauma accumulator (`Add`/`Trauma`/`Decay`/`Reset`) |
| `Assets/Scripts/Core/VfxSettings.cs` | modify | + `CameraShake` key/property |
| `Assets/Scripts/Core/SettingsMenu.cs` | modify | + 1 Debug-tab row |
| `Assets/Scripts/Fly/FlyCamera.cs` | modify | strip/re-apply shake overlay in `LateUpdate` + tuning fields |
| `Assets/Scripts/Fly/FlyCrashHandler.cs` | modify | crash → `CameraShake.Add` (impact-speed-scaled) |
| `Assets/Scripts/Fly/Rocket.cs` | modify | rocket hit → `CameraShake.Add` (distance falloff) |
| `Assets/Scripts/Fly/FlyController.cs` | modify | `CameraShake.Reset()` in `Awake` |

---

### Task 0: Pre-flight

- [ ] **Step 1:** `git branch --show-current` → `vfx/b3c-camera-shake`, HEAD `7b9db94`. Read `mcpforunity://editor/state`: not playing, not compiling, `ready_for_tools`. If maintainer is in Play, wait. `read_console(types=["error"])` → no `CS` errors.

---

### Task 1: `CameraShake` service

**Files:** Create `Assets/Scripts/Fly/CameraShake.cs`

- [ ] **Step 1: Create the script**

```csharp
using UnityEngine;

namespace CubeFly.Fly
{
    // Static trauma accumulator for the third-person camera shake (B-3c).
    // Triggers (crash, rocket detonation) call Add; FlyCamera reads Trauma,
    // applies a noise-driven offset scaled by trauma², and Decays it each
    // frame. Static so triggers need no camera reference (mirrors
    // CubeDeath.CubeDied). Reset on FlyScene load clears leftover trauma
    // (the static outlives scene loads).
    public static class CameraShake
    {
        static float _trauma;

        public static float Trauma => _trauma;
        public static void Add(float amount) => _trauma = Mathf.Clamp01(_trauma + amount);
        public static void Decay(float recoverPerSec, float dt) => _trauma = Mathf.Max(0f, _trauma - recoverPerSec * dt);
        public static void Reset() => _trauma = 0f;
    }
}
```

- [ ] **Step 2: Verify compile** — poll `editor_state`, `read_console(types=["error"])`, expect none.

- [ ] **Step 3: Commit**
```bash
git add Assets/Scripts/Fly/CameraShake.cs Assets/Scripts/Fly/CameraShake.cs.meta
git commit -m "feat(vfx): CameraShake static trauma service (B-3c)"
```

---

### Task 2: `VfxSettings` — toggle key

**Files:** Modify `Assets/Scripts/Core/VfxSettings.cs`

- [ ] **Step 1:** After `KLowHpFlicker`:
```csharp
        const string KCameraShake         = "VfxCameraShake";
```
After the `LowHpFlicker` property:
```csharp
        public static bool CameraShake         { get => Get(KCameraShake);         set => Set(KCameraShake,         value); }
```

- [ ] **Step 2: Verify compile** — expect none.

- [ ] **Step 3: Commit**
```bash
git add Assets/Scripts/Core/VfxSettings.cs
git commit -m "feat(vfx): add CameraShake toggle key (B-3c)"
```

---

### Task 3: `SettingsMenu` — Debug row

**Files:** Modify `Assets/Scripts/Core/SettingsMenu.cs`

- [ ] **Step 1:** After the `("Low-HP flicker", …)` tuple, before the closing `};`:
```csharp
                ("Camera shake",
                    "Camera kick on a ship crash + nearby rocket detonations.",
                    () => VfxSettings.CameraShake,  v => VfxSettings.CameraShake  = v),
```

- [ ] **Step 2: Verify compile** — expect none.

- [ ] **Step 3: Commit**
```bash
git add Assets/Scripts/Core/SettingsMenu.cs
git commit -m "feat(vfx): surface Camera shake toggle in Settings Debug tab (B-3c)"
```

---

### Task 4: `FlyCamera` — shake overlay

**Files:** Modify `Assets/Scripts/Fly/FlyCamera.cs`

- [ ] **Step 1: Add tuning fields + state**

After the existing serialized fields (e.g. after `snapBackSpeed`):
```csharp
        [Header("Camera shake (B-3c)")]
        [Tooltip("Max positional shake offset (world units) at full trauma.")]
        [SerializeField] float maxShakeOffset = 0.35f;
        [Tooltip("Max roll (degrees about the view axis) at full trauma.")]
        [SerializeField] float maxShakeRoll = 2f;
        [Tooltip("Perlin sample rate — higher = buzzier shake.")]
        [SerializeField] float shakeFrequency = 25f;
        [Tooltip("Trauma recovered per second (how fast the shake settles).")]
        [SerializeField] float shakeRecovery = 1.2f;

        Vector3 _shakeOffset;
        float _shakeRoll;
```

- [ ] **Step 2: Strip last frame's shake at the top of `LateUpdate`**

Immediately after `if (construct == null) return;` in `LateUpdate`:
```csharp
            // Strip last frame's shake so the follow Lerp/Slerp operate on the
            // clean pose — shake is a pure additive overlay (ApplyShake, below).
            transform.position -= _shakeOffset;
            if (_shakeRoll != 0f)
                transform.rotation = Quaternion.AngleAxis(-_shakeRoll, transform.forward) * transform.rotation;
```

- [ ] **Step 3: Apply fresh shake at the end of `LateUpdate`**

As the last statement inside `LateUpdate` (after the `transform.rotation = Quaternion.Slerp(...)` line):
```csharp
            ApplyShake();
```

Add the method after `LateUpdate`:
```csharp
        // Trauma-based additive camera shake (B-3c). Offset ∝ trauma² (so a
        // little trauma barely moves), driven by per-axis Perlin noise in
        // screen space + a small roll. Toggle- and trauma-guarded; stores the
        // applied offset/roll so LateUpdate can strip them next frame.
        void ApplyShake()
        {
            float trauma = CameraShake.Trauma;
            if (!VfxSettings.CameraShake || trauma <= 0f)
            {
                _shakeOffset = Vector3.zero;
                _shakeRoll = 0f;
                return;
            }

            float shake = trauma * trauma;
            float t = Time.time * shakeFrequency;
            float nx = Mathf.PerlinNoise(t, 0f) * 2f - 1f;
            float ny = Mathf.PerlinNoise(0f, t) * 2f - 1f;
            float nz = Mathf.PerlinNoise(t, t) * 2f - 1f;

            _shakeOffset = (transform.right * nx + transform.up * ny) * (shake * maxShakeOffset);
            _shakeRoll = nz * shake * maxShakeRoll;

            transform.position += _shakeOffset;
            transform.rotation = Quaternion.AngleAxis(_shakeRoll, transform.forward) * transform.rotation;

            CameraShake.Decay(shakeRecovery, Time.deltaTime);
        }
```

(`FlyCamera` already `using CubeFly.Core;` for `VfxSettings`; `CameraShake` is in the same `CubeFly.Fly` namespace.)

- [ ] **Step 4: Verify compile** — expect none.

- [ ] **Step 5: Commit**
```bash
git add Assets/Scripts/Fly/FlyCamera.cs
git commit -m "feat(vfx): FlyCamera trauma-shake overlay in LateUpdate (B-3c)"
```

---

### Task 5: `FlyCrashHandler` — crash trigger

**Files:** Modify `Assets/Scripts/Fly/FlyCrashHandler.cs`

- [ ] **Step 1: Add tuning consts**

Next to the existing `[SerializeField]` tuning fields (or as consts near `TAG`):
```csharp
        [Tooltip("Trauma per (u/s) of normal-component impact speed → camera shake. 0.03 maps a ~33 u/s crash to full trauma.")]
        [SerializeField] float shakeCrashScale = 0.03f;
```

- [ ] **Step 2: Trigger shake after the impact-speed gate**

In `OnCollisionEnter`, immediately after `if (normalImpactSpeed < minSpeedForDamage) return;`:
```csharp
            // Camera shake scaled by head-on impact speed (B-3c). Reuses the
            // minSpeedForDamage gate above, so gentle landings don't shake.
            if (VfxSettings.CameraShake)
                CameraShake.Add(Mathf.Clamp01(normalImpactSpeed * shakeCrashScale));
```

(`FlyCrashHandler` already `using CubeFly.Core;` for `VfxSettings`; `CameraShake` is same-namespace.)

- [ ] **Step 3: Verify compile** — expect none.

- [ ] **Step 4: Commit**
```bash
git add Assets/Scripts/Fly/FlyCrashHandler.cs
git commit -m "feat(vfx): crash camera shake from impact speed (B-3c)"
```

---

### Task 6: `Rocket` — detonation trigger

**Files:** Modify `Assets/Scripts/Fly/Rocket.cs`

- [ ] **Step 1: Add tuning fields**

Near the top of the class (with the other serialized fields):
```csharp
        [Header("Camera shake (B-3c)")]
        [Tooltip("Trauma added to the camera shake when this rocket detonates point-blank; falls off to 0 by shakeRange.")]
        [SerializeField] float shakeTrauma = 0.4f;
        [Tooltip("Distance (u) beyond which a rocket detonation adds no camera shake.")]
        [SerializeField] float shakeRange = 40f;
```

- [ ] **Step 2: Trigger shake at the hit branch**

In `Update`, in the hit branch — immediately before `Destroy(gameObject);` (right after `ProjectileHit.SpawnImpactVfx(hit, scale: 1.20f);`):
```csharp
                // Nearby-detonation camera shake, distance-scaled (B-3c).
                if (VfxSettings.CameraShake)
                {
                    Camera cam = Camera.main;
                    if (cam != null)
                    {
                        float dist = Vector3.Distance(cam.transform.position, hit.point);
                        float falloff = Mathf.Clamp01(1f - dist / shakeRange);
                        if (falloff > 0f) CameraShake.Add(shakeTrauma * falloff);
                    }
                }
```

(`hit` is the `RaycastHit` from the enclosing `if (ProjectileHit.TrySweep(..., out RaycastHit hit))` block, so `hit.point` is in scope. `Rocket` already `using CubeFly.Core;`; `CameraShake` is same-namespace.)

- [ ] **Step 3: Verify compile** — expect none.

- [ ] **Step 4: Commit**
```bash
git add Assets/Scripts/Fly/Rocket.cs
git commit -m "feat(vfx): nearby-rocket-detonation camera shake (B-3c)"
```

---

### Task 7: `FlyController` — reset on scene load

**Files:** Modify `Assets/Scripts/Fly/FlyController.cs`

- [ ] **Step 1:** In `Awake`, after `LowHpVfx.ConfigureVfx(lowHpSmokePrefab);`:
```csharp
            CameraShake.Reset();   // clear leftover trauma across scene reloads (B-3c)
```

- [ ] **Step 2: Verify compile** — expect none.

- [ ] **Step 3: Commit**
```bash
git add Assets/Scripts/Fly/FlyController.cs
git commit -m "feat(vfx): reset CameraShake trauma on FlyScene load (B-3c)"
```

---

### Task 8: Play-mode mechanism check + maintainer gate

- [ ] **Step 1: Mechanism check (autonomous).** Enter Play. Via `execute_code`: confirm `CameraShake.Trauma == 0` at start; `CameraShake.Add(0.5f)` → `Trauma == 0.5`; `Add(0.8f)` → clamps to `1.0`; `Decay(1.2f, 1f)` lowers it; `Reset()` zeroes it. Confirm `VfxSettings.CameraShake` default true. Confirm the trigger call sites compile-resolve (already proven by Task 5/6 compiles). Stop Play. *(The live shake can't be animated headlessly — the MCP editor freezes the game loop, `Time.frameCount` stuck at 1 — same limitation as the B-3b flicker; that's expected.)*

- [ ] **Step 2: Maintainer fly + shoot gate.** Hand off: crash into terrain at speed (strong shake) vs graze (little/none); fire a rocket into a near target (punch) vs far (nothing); bullets (no shake); toggle **Camera shake** in Settings → Debug. Fine-tune `maxShakeOffset` / `maxShakeRoll` / `shakeRecovery` / `shakeCrashScale` / rocket `shakeTrauma`+`shakeRange` to taste. Wait for confirmation (+ commit any tuning).

---

### Task 9: Docs + land

- [ ] **Step 1: Docs** — mark B-3c shipped in `ROADMAP.md` (Phase B-3 complete); add `CameraShake` + the `FlyCamera` shake overlay + crash/rocket triggers to `docs/full_architecture.md`. Note that **B-3 is now complete**. Commit.

- [ ] **Step 2: Final verify** — `git status` clean except pre-existing non-ours dirt; `read_console` no errors.

- [ ] **Step 3: Land** — per maintainer (fast-forward to `main`, or PR). Phase B-3 done → ROADMAP's next VFX work is **B-4 (HUD feedback)**.

---

## Self-review

**Spec coverage:** `CameraShake` service → Task 1; `FlyCamera` strip/re-apply overlay + trauma² + Perlin + roll → Task 4; crash trigger (impact-speed-scaled, reuses `minSpeedForDamage` gate) → Task 5; rocket trigger (distance falloff, `Camera.main`) → Task 6; `Reset` on load → Task 7; toggle → Tasks 2/3; verification + gate → Task 8; docs → Task 9. All spec sections covered.

**Placeholder scan:** every code step is complete (no installer parameter-specs this phase — it's pure code). No TODOs.

**Type consistency:** `CameraShake.Add`/`Trauma`/`Decay`/`Reset` (Task 1) used identically in Tasks 4/5/6/7; `VfxSettings.CameraShake` (property) vs `CameraShake` (class) are member-access vs type — distinct, no collision. `maxShakeOffset`/`maxShakeRoll`/`shakeFrequency`/`shakeRecovery` (FlyCamera), `shakeCrashScale` (FlyCrashHandler), `shakeTrauma`/`shakeRange` (Rocket), `_shakeOffset`/`_shakeRoll` state — consistent within their files.
