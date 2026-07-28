# VFX Phase B-3b — Low-HP Feedback — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: use superpowers:executing-plans to implement this plan task-by-task (inline — drives the single shared Unity Editor on the main project root; NOT suitable for isolated subagents/worktrees). Steps use checkbox (`- [ ]`) syntax.

**Goal:** A cube below 25% HP emits sustained smoke (all cubes) and — for player-construct cubes — pulses a red emissive alarm, both toggleable in Settings → Debug and standing down cleanly on death.

**Architecture:** A lazily-captured `MaxHealthPoints` on `CubeStats` gives a well-defined `HealthFraction`; `CubeDamage.ApplyAndLog` attaches one `LowHpVfx` per cube when it crosses <25% alive, passing `isPlayer = (energy != null)`. `LowHpVfx` (Core) spawns a looping installer-generated smoke child and pulses `_EmissionColor` via a `MaterialPropertyBlock` (player only), both live-toggle-polled, self-cleaning on death. Smoke prefab wired via a static `ConfigureVfx` from `FlyController.Awake` (mirrors `CubeDeath`).

**Tech stack:** Unity 6.3 LTS / URP 17.3, `Assembly-CSharp` (no asmdefs, no test framework). **Per-task verification = `read_console` shows zero CS compile errors** (poll `editor_state.is_compiling == false` first); **feature verification = the Play-mode fly+shoot gate (Task 8).**

**Branch:** `vfx/b3b-low-hp` (created; spec committed at `1f5a7da`).

**Spec:** `docs/superpowers/specs/2026-07-28-vfx-phase-b3b-low-hp-feedback-design.md`

---

## File structure

| File | Change | Responsibility |
| --- | --- | --- |
| `Assets/Scripts/Core/CubeStats.cs` | modify | + `MaxHealthPoints` (lazy capture) + `HealthFraction` |
| `Assets/Scripts/Core/VfxSettings.cs` | modify | + `LowHpSmoke` / `LowHpFlicker` keys/properties |
| `Assets/Scripts/Core/SettingsMenu.cs` | modify | + 2 Debug-tab rows |
| `Assets/Scripts/Core/LowHpVfx.cs` | **create** | per-cube smoke + player-only emissive flicker |
| `Assets/Scripts/Fly/CubeDamage.cs` | modify | attach `LowHpVfx` on the <25% alive path |
| `Assets/Scripts/Fly/FlyController.cs` | modify | + serialized `lowHpSmokePrefab`, `LowHpVfx.ConfigureVfx` in `Awake` |
| `Assets/Scripts/Editor/VfxAssetsInstaller.cs` | modify | generate `LowHpSmokeMat` + `LowHpSmoke.prefab` |
| `Assets/VFX/Materials/LowHpSmokeMat.mat` | create (installer) | alpha-blended dark smoke material |
| `Assets/VFX/Prefabs/LowHpSmoke.prefab` | create (installer) | looping dark smoke ParticleSystem |
| `Assets/Scenes/FlyScene.unity` | modify | `FlyController.lowHpSmokePrefab` assigned |

---

### Task 0: Pre-flight

- [ ] **Step 1: Branch + Unity idle**

Run:
```bash
cd "/Users/anon/My project" && git branch --show-current && git log --oneline -1
```
Expected: `vfx/b3b-low-hp`, HEAD = `1f5a7da`. Read `mcpforunity://editor/state`: `is_playing == false`, `is_compiling == false`, `ready_for_tools == true`. If the maintainer is in Play, wait. `read_console(types=["error"])` → no `CS` errors (MCP transport noise is fine).

---

### Task 1: `CubeStats` — max-HP baseline

**Files:** Modify `Assets/Scripts/Core/CubeStats.cs`

- [ ] **Step 1: Add the property + fraction**

After the `mass` field (before `TakeDamage`):

```csharp
        // Peak HP, captured lazily as the pre-damage HP on the first
        // HP-reducing hit. HP is monotonic (never heals), so the first real
        // hit always sees the peak — this needs no build-time wiring and
        // covers player cubes, world targets, and turrets uniformly. (B-3b)
        public float MaxHealthPoints { get; private set; }

        // Fraction of max HP remaining (1 before any damage). Used for the
        // <25% low-HP feedback trigger.
        public float HealthFraction => MaxHealthPoints > 0f ? healthPoints / MaxHealthPoints : 1f;
```

- [ ] **Step 2: Capture in `TakeDamage`**

In `TakeDamage`, replace:
```csharp
            float hpBefore = healthPoints;
            healthPoints = Mathf.Max(0f, healthPoints - effective);
```
with:
```csharp
            float hpBefore = healthPoints;
            if (MaxHealthPoints <= 0f) MaxHealthPoints = hpBefore;
            healthPoints = Mathf.Max(0f, healthPoints - effective);
```

- [ ] **Step 3: Capture in `TakeRawDamage`**

In `TakeRawDamage`, replace:
```csharp
            float hpBefore = healthPoints;
            healthPoints = Mathf.Max(0f, healthPoints - incoming);
```
with:
```csharp
            float hpBefore = healthPoints;
            if (MaxHealthPoints <= 0f) MaxHealthPoints = hpBefore;
            healthPoints = Mathf.Max(0f, healthPoints - incoming);
```

- [ ] **Step 4: Verify compile** — poll `editor_state`, `read_console(types=["error"])`, expect none.

- [ ] **Step 5: Commit**
```bash
git add Assets/Scripts/Core/CubeStats.cs
git commit -m "feat(vfx): CubeStats MaxHealthPoints + HealthFraction (B-3b)"
```

---

### Task 2: `VfxSettings` — 2 toggle keys

**Files:** Modify `Assets/Scripts/Core/VfxSettings.cs`

- [ ] **Step 1: Add keys + properties**

After `KCubeDeathTrail`:
```csharp
        const string KLowHpSmoke   = "VfxLowHpSmoke";
        const string KLowHpFlicker = "VfxLowHpFlicker";
```
After the `CubeDeathTrail` property:
```csharp
        public static bool LowHpSmoke   { get => Get(KLowHpSmoke);   set => Set(KLowHpSmoke,   value); }
        public static bool LowHpFlicker { get => Get(KLowHpFlicker); set => Set(KLowHpFlicker, value); }
```

- [ ] **Step 2: Verify compile** — poll, `read_console`, expect none.

- [ ] **Step 3: Commit**
```bash
git add Assets/Scripts/Core/VfxSettings.cs
git commit -m "feat(vfx): add LowHpSmoke/LowHpFlicker toggle keys (B-3b)"
```

---

### Task 3: `SettingsMenu` — 2 Debug rows

**Files:** Modify `Assets/Scripts/Core/SettingsMenu.cs`

- [ ] **Step 1: Add rows**

After the `("Cube death trail", …)` tuple (added in B-3a), before the closing `};`:
```csharp
                ("Low-HP smoke",
                    "Sustained dark smoke rising off any cube below 25% HP.",
                    () => VfxSettings.LowHpSmoke,   v => VfxSettings.LowHpSmoke   = v),
                ("Low-HP flicker",
                    "Pulsing red emissive alarm on your own construct's cubes below 25% HP.",
                    () => VfxSettings.LowHpFlicker, v => VfxSettings.LowHpFlicker = v),
```

- [ ] **Step 2: Verify compile** — poll, `read_console`, expect none.

- [ ] **Step 3: Commit**
```bash
git add Assets/Scripts/Core/SettingsMenu.cs
git commit -m "feat(vfx): surface Low-HP toggles in Settings Debug tab (B-3b)"
```

---

### Task 4: `LowHpVfx` component

**Files:** Create `Assets/Scripts/Core/LowHpVfx.cs`

- [ ] **Step 1: Create the script** (via `create_script` or Write) with:

```csharp
using UnityEngine;

namespace CubeFly.Core
{
    // Sustained low-HP feedback on a cube below 25% HP: a looping smoke
    // ParticleSystem (all cubes) + a pulsing red emissive alarm (player
    // construct cubes only, set via Configure). Lazily AddComponent'd by
    // CubeDamage when a cube crosses the threshold — one per cube. Reads its
    // toggles each frame so the Debug A/B works live, and self-cleans on
    // death so the B-3a death burst + drift aren't fighting a live smoke +
    // red tint. (B-3b)
    public class LowHpVfx : MonoBehaviour
    {
        // Configured once per FlyScene load by FlyController.Awake (mirrors
        // CubeDeath.ConfigureVfx). Null in unconfigured scenes → no smoke.
        public static GameObject SmokePrefab;
        public static void ConfigureVfx(GameObject smoke) => SmokePrefab = smoke;

        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        // Red the flicker pulses toward, HDR-scaled so bloom picks it up.
        static readonly Color FlickerColor = new Color(1f, 0.15f, 0.1f) * 2f;
        const float FlickerHz = 3.5f;

        bool _canFlicker;
        CubeStats _stats;
        ParticleSystem _smoke;
        Renderer _renderer;
        MaterialPropertyBlock _mpb;
        bool _flickering;
        bool _done;

        // isPlayer = the cube belongs to the player construct (from CubeDamage's
        // ConstructEnergySystem lookup). Only player cubes flicker.
        public void Configure(bool isPlayer) => _canFlicker = isPlayer;

        void Awake()
        {
            _stats = GetComponent<CubeStats>();
            _renderer = GetComponentInChildren<Renderer>();
            if (SmokePrefab != null)
            {
                GameObject go = Instantiate(SmokePrefab, transform);
                go.transform.localPosition = Vector3.zero;
                _smoke = go.GetComponentInChildren<ParticleSystem>();
            }
        }

        void Update()
        {
            if (_done) return;

            // Death cleanup: stand down so the death burst/drift take over.
            if (_stats == null || _stats.healthPoints <= 0f)
            {
                if (_smoke != null)
                {
                    var em = _smoke.emission; em.enabled = false;
                    _smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
                ClearFlicker();
                _done = true;
                return;
            }

            // Smoke (all cubes) — live toggle.
            if (_smoke != null)
            {
                var em = _smoke.emission;
                em.enabled = VfxSettings.LowHpSmoke;
            }

            // Flicker (player cubes only) — live toggle.
            if (_canFlicker && _renderer != null)
            {
                if (VfxSettings.LowHpFlicker)
                {
                    if (_mpb == null) _mpb = new MaterialPropertyBlock();
                    float t = (Mathf.Sin(Time.time * FlickerHz * Mathf.PI * 2f) + 1f) * 0.5f;
                    _renderer.GetPropertyBlock(_mpb);
                    _mpb.SetColor(EmissionColorId, FlickerColor * t);
                    _renderer.SetPropertyBlock(_mpb);
                    _flickering = true;
                }
                else if (_flickering)
                {
                    ClearFlicker();
                }
            }
        }

        // Restore the cube's baseline emissive (mirror the delete tool's
        // clear-on-un-hover). SetPropertyBlock(null) drops the override.
        void ClearFlicker()
        {
            if (_flickering && _renderer != null) _renderer.SetPropertyBlock(null);
            _flickering = false;
        }

        void OnDisable() => ClearFlicker();
    }
}
```

- [ ] **Step 2: Verify compile** — poll `editor_state`, `read_console(types=["error"])`, expect none.

- [ ] **Step 3: Commit**
```bash
git add Assets/Scripts/Core/LowHpVfx.cs Assets/Scripts/Core/LowHpVfx.cs.meta
git commit -m "feat(vfx): LowHpVfx component (smoke + player emissive flicker) (B-3b)"
```

---

### Task 5: `CubeDamage` — attach on the <25% alive path

**Files:** Modify `Assets/Scripts/Fly/CubeDamage.cs`

- [ ] **Step 1: Add the threshold const**

Inside `class CubeDamage`, above `ApplyAndLog`:
```csharp
        // Fraction of max HP below which a cube shows low-HP feedback (B-3b).
        const float LowHpThreshold = 0.25f;
```

- [ ] **Step 2: Attach in the surviving-cube branch**

Replace:
```csharp
            if (stats.healthPoints > 0f) return applied;
```
with:
```csharp
            if (stats.healthPoints > 0f)
            {
                // B-3b: low-HP feedback. Smoke for any cube; the flicker is
                // gated player-only inside LowHpVfx via isPlayer (energy != null
                // ⇔ player construct — world targets/turrets have no energy
                // system). Attach at most one per cube.
                if (stats.HealthFraction < LowHpThreshold
                    && stats.GetComponent<LowHpVfx>() == null)
                {
                    stats.gameObject.AddComponent<LowHpVfx>().Configure(energy != null);
                }
                return applied;
            }
```

(`energy` is the `ConstructEnergySystem` already resolved earlier in the method for shield interception; `LowHpVfx` is in `CubeFly.Core`, which `CubeDamage` already `using`s.)

- [ ] **Step 3: Verify compile** — poll, `read_console`, expect none.

- [ ] **Step 4: Commit**
```bash
git add Assets/Scripts/Fly/CubeDamage.cs
git commit -m "feat(vfx): attach LowHpVfx on the <25% alive path (B-3b)"
```

---

### Task 6: `FlyController` — wire the config

**Files:** Modify `Assets/Scripts/Fly/FlyController.cs`

- [ ] **Step 1: Serialized ref**

After the B-3a `cubeDeathTrailMaterial` field (in the Phase B-3a VFX header block):
```csharp
        [Tooltip("LowHpSmoke.prefab (Assets/VFX/Prefabs/). Passed to LowHpVfx.ConfigureVfx in Awake. If null, no low-HP smoke.")]
        [SerializeField] GameObject lowHpSmokePrefab;
```

- [ ] **Step 2: Configure in `Awake`**

After the B-3a `CubeDeath.ConfigureVfx(...)` line:
```csharp
            LowHpVfx.ConfigureVfx(lowHpSmokePrefab);
```

- [ ] **Step 3: Verify compile** — poll, `read_console`, expect none.

- [ ] **Step 4: Commit**
```bash
git add Assets/Scripts/Fly/FlyController.cs
git commit -m "feat(vfx): FlyController configures LowHpVfx smoke in Awake (B-3b)"
```

---

### Task 7: Installer — `LowHpSmokeMat` + `LowHpSmoke.prefab`; run + wire

**Files:** Modify `Assets/Scripts/Editor/VfxAssetsInstaller.cs`; creates the two assets + modifies `FlyScene.unity`.

> ParticleSystem module code authored live (mirror the single-PS builders like `EnsureRocketSmokePuffPrefab`). Parameters below are the contract.

- [ ] **Step 1: Path consts** — after `CubeDeathBurstPrefabPath`:
```csharp
        const string LowHpSmokeMatPath    = MaterialsDir + "/LowHpSmokeMat.mat";
        const string LowHpSmokePrefabPath = PrefabsDir + "/LowHpSmoke.prefab";
```

- [ ] **Step 2: `EnsureLowHpSmokePrefab(Material smokeMat)`** — a single **looping** ParticleSystem "LowHpSmoke":
  - `main`: `loop = true`, `startLifetime` ≈ 1.2, `startSpeed` ≈ 1, `startSize` ≈ 0.3, `startColor` dark grey `(0.15,0.15,0.15, 0.5)`, `simulationSpace = World`, `playOnAwake = true`, `maxParticles` ≈ 16, `gravityModifier` ≈ -0.05 (gentle rise) or use velocity (below).
  - `emission`: `rateOverTime` ≈ 8 (steady wisp; **not** a burst).
  - `shape`: Cone, small `radius` ≈ 0.1, `angle` ≈ 15, oriented up — OR Sphere radius 0.1 + `velocityOverLifetime.y` ≈ 1 for the rise.
  - `sizeOverLifetime`: grow slightly then fade; `colorOverLifetime`: alpha in→out.
  - renderer: Billboard, `sharedMaterial = smokeMat`.
  - Save via `PrefabUtility.SaveAsPrefabAsset(root, LowHpSmokePrefabPath)` in the try/finally + `DestroyImmediate(root)` pattern.

- [ ] **Step 3: `Apply()` calls** — after the B-3a `EnsureCubeDeathBurstPrefab(...)`:
```csharp
            Material lowHpSmoke = EnsureAlphaBlendedParticleMaterial(
                LowHpSmokeMatPath, glow, new Color(0.15f, 0.15f, 0.15f, 1f));
            EnsureLowHpSmokePrefab(lowHpSmoke);
```
and extend the final summary log to mention `LowHpSmokeMat` / `LowHpSmoke.prefab [B-3b]`.

- [ ] **Step 4: Compile-check the installer** — poll, `read_console`, expect none.

- [ ] **Step 5: Commit installer code**
```bash
git add Assets/Scripts/Editor/VfxAssetsInstaller.cs
git commit -m "feat(vfx): installer generates LowHpSmoke prefab + material (B-3b)"
```

- [ ] **Step 6: Run installer** — `execute_menu_item("Tools/CubeFly/Generate VFX assets")`; `read_console` → no errors; confirm the two assets exist. NOTE: re-running regenerates all VFX assets (convergent) — verify via `git status` that only `LowHpSmoke.prefab(.meta)` + `LowHpSmokeMat.mat(.meta)` are new and no existing prefab/material drifted (the additive mats are already canonical from B-3a — expect no diff).

- [ ] **Step 7: Wire FlyScene** — `execute_code` (edit mode): `OpenScene(FlyScene, Single)`; `FindAnyObjectByType<CubeFly.Fly.FlyController>()`; `SerializedObject` set `lowHpSmokePrefab` = the generated `LowHpSmoke.prefab`; `ApplyModifiedPropertiesWithoutUndo`; `MarkSceneDirty` + `SaveScene`. Confirm the FlyScene diff is just the one ref (+ any benign orphan-prune).

- [ ] **Step 8: Commit assets + scene**
```bash
git add Assets/VFX/Prefabs/LowHpSmoke.prefab Assets/VFX/Prefabs/LowHpSmoke.prefab.meta \
        Assets/VFX/Materials/LowHpSmokeMat.mat Assets/VFX/Materials/LowHpSmokeMat.mat.meta \
        Assets/Scenes/FlyScene.unity
git commit -m "feat(vfx): generate LowHpSmoke assets + wire FlyController (B-3b)"
```

---

### Task 8: Play-mode gate + tune

- [ ] **Step 1: Enter Play**, wait for `is_playing`. Confirm `LowHpVfx.SmokePrefab != null` (configured).

- [ ] **Step 2: Drive a player cube low.** With a construct present (or via `execute_code` damage a construct cube to ~20% via `CubeDamage.ApplyAndLog` with a player-construct `HitContext`), confirm a `LowHpVfx` attaches, a smoke child spawns, and (player) the emissive pulses red. Offscreen-render `Camera.main` (StandardRequest) → Read the PNG.

- [ ] **Step 3: Drive a target low.** Damage a `DesertTarget` to <25% → confirm smoke, **no** flicker (`_canFlicker == false`).

- [ ] **Step 4: Death handoff.** Kill a low-HP cube → confirm smoke stops + emissive clears + the B-3a burst/drift take over (no red/smoke on the drifting wreck).

- [ ] **Step 5: Toggles.** Flip `LowHpSmoke` / `LowHpFlicker` in Settings → Debug (or via `VfxSettings`) → each responds live.

- [ ] **Step 6: Emissive keyword check.** If the flicker doesn't show, the armour material's `_EMISSION` keyword is off — enable it on the shared cube material (installer or material asset) and re-verify. (Delete tool proves the MPB pattern works, so likely fine.)

- [ ] **Step 7: Stop Play**; commit any tuning to the prefab/material.

- [ ] **Step 8: Maintainer fly+shoot gate** — hand off: take fire until your ship smokes + flickers; shoot a target to <25% (smoke only); watch a low-HP cube die. Fine-tune smoke density/colour + flicker rate/intensity per feedback. Wait for confirmation.

---

### Task 9: Docs + land

- [ ] **Step 1: Docs** — mark B-3b shipped under Phase B-3 in `ROADMAP.md`; add `LowHpVfx` + `CubeStats.MaxHealthPoints`/`HealthFraction` + the installer `LowHpSmoke` asset to `docs/full_architecture.md`. Commit.

- [ ] **Step 2: Final verify** — `git status` clean except pre-existing non-ours dirt; `read_console` no errors.

- [ ] **Step 3: Land** — per maintainer (direct fast-forward to `main`, or PR). Then B-3c (camera shake).

---

## Self-review

**Spec coverage:** max-HP → Task 1; smoke (all) + flicker (player) → Task 4 + assets Task 7; trigger/attach → Task 5; player detection (`energy != null`) → Task 5; static wiring → Tasks 4/6/7; toggles → Tasks 2/3; death cleanup → Task 4; installer asset → Task 7; gate → Task 8; docs → Task 9. All spec sections covered.

**Placeholder scan:** installer PS module code is spec-by-parameter (Task 7 note), consistent with B-3a; every other code step is complete.

**Type consistency:** `MaxHealthPoints`/`HealthFraction` (CubeStats), `LowHpSmoke`/`LowHpFlicker` (VfxSettings), `LowHpVfx.SmokePrefab`/`ConfigureVfx`/`Configure(bool)` (component), `lowHpSmokePrefab` (FlyController serialized), `LowHpThreshold` (CubeDamage), `EmissionColorId`/`FlickerColor` — consistent across Tasks 1/2/4/5/6.
