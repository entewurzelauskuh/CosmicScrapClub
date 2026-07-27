# VFX Phase B-3a — Cube-Death Enhancement — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: use superpowers:executing-plans to implement this plan task-by-task (inline — this work drives the single shared Unity Editor on the main project root, so it is NOT suitable for isolated subagents/worktrees). Steps use checkbox (`- [ ]`) syntax.

**Goal:** Make a dying cube read as *destroyed* — a one-frame flash, a spark burst, ~4 particle debris chunks, and a lingering flame/smoke trail — hooked into the single `CubeDeath.BeginDeath` funnel, toggleable from the Settings → Debug tab.

**Architecture:** Installer-generated additive burst prefab (flash + spark + Mesh-mode debris) instantiated at the cube on death; a `TrailRenderer` child on the dying cube for the drift; both gated by two new `VfxSettings` toggles and wired to `CubeDeath` via a static `ConfigureVfx` called from `FlyController.Awake` (mirrors B-2's `ProjectileHit.ConfigureImpactPrefabs`). `LingeringTrail` relocates Fly→Core so `CubeDeath` (Core) can use it without a layer inversion.

**Tech stack:** Unity 6.3 LTS / URP 17.3, `Assembly-CSharp` (no asmdefs, no test framework), MCP-for-Unity. No pytest/EditMode tests exist — **per-task verification = `read_console` shows zero compile errors** (poll `editor_state.is_compiling == false` first); **feature verification = the Play-mode fly+shoot gate (Task 8).**

**Branch:** `vfx/b3a-cube-death` (already created, spec committed at `a34b214`).

**Spec:** `docs/superpowers/specs/2026-07-27-vfx-phase-b3a-cube-death-design.md`

---

## File structure

| File | Change | Responsibility |
| --- | --- | --- |
| `Assets/Scripts/Core/VfxSettings.cs` | modify | + 2 toggle keys/properties (`CubeDeathBurst`, `CubeDeathTrail`) |
| `Assets/Scripts/Core/SettingsMenu.cs` | modify | + 2 Debug-tab toggle rows |
| `Assets/Scripts/Fly/LingeringTrail.cs` → `Assets/Scripts/Core/LingeringTrail.cs` | move | relocate to Core so `CubeDeath` may use it |
| `Assets/Scripts/Core/CubeDeath.cs` | modify | static `BurstPrefab`/`TrailMaterial` + `ConfigureVfx`; spawn burst + trail in `BeginDeath` |
| `Assets/Scripts/Fly/FlyController.cs` | modify | + 2 serialized refs, `CubeDeath.ConfigureVfx(...)` in `Awake` |
| `Assets/Scripts/Editor/VfxAssetsInstaller.cs` | modify | generate `CubeDebrisMat` + `CubeDeathBurst.prefab` |
| `Assets/VFX/Materials/CubeDebrisMat.mat` | create (installer) | opaque dark-metal material for debris mesh particles |
| `Assets/VFX/Prefabs/CubeDeathBurst.prefab` | create (installer) | one-shot flash + spark + debris ParticleSystem |
| `Assets/Scenes/FlyScene.unity` | modify | `FlyController`'s 2 new refs assigned |

---

### Task 0: Pre-flight

**Files:** none.

- [ ] **Step 1: Confirm branch + clean tree scope**

Run:
```bash
cd "/Users/anon/My project" && git branch --show-current && git log --oneline -1
```
Expected: `vfx/b3a-cube-death`, HEAD = `a34b214` (spec commit). (The 5 ship `.mat` + probuilder file remain unstaged pre-existing dirt — never touch.)

- [ ] **Step 2: Confirm Unity is connected, idle, not playing**

Read resource `mcpforunity://editor/state`. Expected: `play_mode.is_playing == false`, `compilation.is_compiling == false`, `advice.ready_for_tools == true`. If the maintainer is in Play mode, stop and wait — do NOT `refresh_unity`-compile during their Play session.

- [ ] **Step 3: Baseline console**

`read_console(types=["error"], count=10)` → expect no `CS` compile errors (MCP transport noise is fine).

---

### Task 1: VfxSettings — 2 toggle keys

**Files:** Modify `Assets/Scripts/Core/VfxSettings.cs`

- [ ] **Step 1: Add the keys + properties**

Alongside the existing B-2 keys (e.g. after `KRocketSmokePuff`), add — mirroring the existing `MuzzleFlashPyramid` property verbatim so the default-on behaviour matches:

```csharp
const string KCubeDeathBurst = "VfxCubeDeathBurst";
const string KCubeDeathTrail = "VfxCubeDeathTrail";
```
and with the other bool properties:
```csharp
public static bool CubeDeathBurst { get => Get(KCubeDeathBurst); set => Set(KCubeDeathBurst, value); }
public static bool CubeDeathTrail { get => Get(KCubeDeathTrail); set => Set(KCubeDeathTrail, value); }
```

- [ ] **Step 2: Verify compile**

Poll `editor_state` until `is_compiling == false`, then `read_console(types=["error"])`. Expected: no `CS` errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Core/VfxSettings.cs
git commit -m "feat(vfx): add CubeDeathBurst/CubeDeathTrail toggle keys (B-3a)"
```

---

### Task 2: SettingsMenu — 2 Debug rows

**Files:** Modify `Assets/Scripts/Core/SettingsMenu.cs`

- [ ] **Step 1: Add two rows to the VFX toggle-tuple list**

After the `("Rocket smoke puffs", …)` tuple (the last entry before the list closes), add:

```csharp
                ("Cube death burst",
                    "Flash + spark burst + debris chunks thrown off a cube when it's destroyed.",
                    () => VfxSettings.CubeDeathBurst, v => VfxSettings.CubeDeathBurst = v),
                ("Cube death trail",
                    "Grey flame/smoke ribbon trailing a destroyed cube as it drifts away.",
                    () => VfxSettings.CubeDeathTrail, v => VfxSettings.CubeDeathTrail = v),
```

(The list uses column-major fill: 14 → 16 rows stays balanced across the two columns.)

- [ ] **Step 2: Verify compile** — poll `editor_state`, `read_console(types=["error"])`, expect none.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Core/SettingsMenu.cs
git commit -m "feat(vfx): surface Cube-death toggles in Settings Debug tab (B-3a)"
```

---

### Task 3: Relocate `LingeringTrail` Fly → Core

**Files:** Move `Assets/Scripts/Fly/LingeringTrail.cs` → `Assets/Scripts/Core/LingeringTrail.cs`; verify `Bullet.cs` / `Rocket.cs`.

Rationale: `CubeDeath` (Core) needs the lingering trail; the helper is generic (only depends on `TrailRenderer`). Moving keeps the dependency direction correct (Fly→Core).

- [ ] **Step 1: Move the file + its .meta (preserve GUID)**

```bash
git mv Assets/Scripts/Fly/LingeringTrail.cs Assets/Scripts/Core/LingeringTrail.cs
git mv Assets/Scripts/Fly/LingeringTrail.cs.meta Assets/Scripts/Core/LingeringTrail.cs.meta
```

- [ ] **Step 2: Change the namespace**

In the moved file, `namespace CubeFly.Fly` → `namespace CubeFly.Core`. (No other change; `[RequireComponent(typeof(TrailRenderer))]` + `DetachAndFade()` stay.)

- [ ] **Step 3: Ensure consumers resolve it**

`Bullet.cs` and `Rocket.cs` reference `LingeringTrail`. Confirm each has `using CubeFly.Core;` at the top (they already use Core types like `HitContext`); add it if missing. Grep to confirm they still reference the type by simple name:
```bash
grep -n "LingeringTrail" Assets/Scripts/Fly/Bullet.cs Assets/Scripts/Fly/Rocket.cs
```

- [ ] **Step 4: Refresh + verify compile**

`refresh_unity(scope="scripts", compile="request", wait_for_ready=true)`, then `read_console(types=["error"])`. Expected: no `CS` errors (a namespace move with same-assembly consumers compiles clean once `using CubeFly.Core;` is present).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Core/LingeringTrail.cs Assets/Scripts/Core/LingeringTrail.cs.meta Assets/Scripts/Fly/Bullet.cs Assets/Scripts/Fly/Rocket.cs
git commit -m "refactor: relocate LingeringTrail Fly->Core for cross-scene reuse (B-3a)"
```

---

### Task 4: `CubeDeath` — VFX hook

**Files:** Modify `Assets/Scripts/Core/CubeDeath.cs`

- [ ] **Step 1: Add static config + a trail-child field**

Below the `CubeDied` event region, add:

```csharp
        // Cube-death VFX, configured once per FlyScene load by
        // FlyController.Awake (mirrors ProjectileHit.ConfigureImpactPrefabs).
        // Static because CubeDeath is lazily AddComponent'd and every death
        // path shares one config. Null in unconfigured scenes (menus) →
        // no VFX, drift unchanged.
        public static GameObject BurstPrefab;
        public static Material TrailMaterial;

        public static void ConfigureVfx(GameObject burst, Material trail)
        {
            BurstPrefab = burst;
            TrailMaterial = trail;
        }
```
and next to `bool _dying;`:
```csharp
        LingeringTrail _trailChild;
```

- [ ] **Step 2: Spawn the VFX in `BeginDeath`**

In `BeginDeath`, after the collider-disable `foreach` loop and before `StartCoroutine(DriftAndDespawn(driftDir));`, insert:

```csharp
            SpawnDeathVfx(driftDir);
```

Add the method (below `BeginDeath`):

```csharp
        void SpawnDeathVfx(Vector3 driftDir)
        {
            if (VfxSettings.CubeDeathBurst && BurstPrefab != null)
            {
                // World-space one-shot at the cube; oriented so the debris
                // cone (local +Z) throws along the drift direction. The
                // prefab self-destroys via main.stopAction = Destroy.
                Instantiate(BurstPrefab, transform.position, Quaternion.LookRotation(driftDir));
            }

            if (VfxSettings.CubeDeathTrail && TrailMaterial != null)
            {
                // TrailRenderer on a dedicated child so it can detach and
                // fade past the cube's despawn (same pattern as Bullet's
                // tracer). Child follows the cube through the drift.
                GameObject trailGo = new GameObject("DeathTrail");
                trailGo.transform.SetParent(transform, false);
                trailGo.transform.localPosition = Vector3.zero;

                TrailRenderer trail = trailGo.AddComponent<TrailRenderer>();
                trail.time = DriftDuration;      // 2 s — matches the drift
                trail.startWidth = 0.3f;
                trail.endWidth = 0f;
                trail.minVertexDistance = 0.1f;
                trail.sharedMaterial = TrailMaterial;   // no per-cube clone
                trail.emitting = true;

                Gradient grad = new Gradient();
                grad.SetKeys(
                    new[]
                    {
                        new GradientColorKey(Color.white, 0f),
                        new GradientColorKey(new Color(0.8f, 0.8f, 0.8f), 1f),
                    },
                    new[]
                    {
                        new GradientAlphaKey(0.6f, 0f),
                        new GradientAlphaKey(0f, 1f),
                    });
                trail.colorGradient = grad;

                _trailChild = trailGo.AddComponent<LingeringTrail>();
            }
        }
```

- [ ] **Step 3: Detach the trail before despawn**

In `DriftAndDespawn`, replace the final `Destroy(gameObject);` with:

```csharp
            if (_trailChild != null) _trailChild.DetachAndFade();
            Destroy(gameObject);
```

- [ ] **Step 4: Verify compile** — poll `editor_state`, `read_console(types=["error"])`, expect none. (`LingeringTrail` is now in `CubeFly.Core`, same namespace as `CubeDeath` — no `using` needed.)

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Core/CubeDeath.cs
git commit -m "feat(vfx): spawn death burst + lingering trail in CubeDeath (B-3a)"
```

---

### Task 5: `FlyController` — wire the config

**Files:** Modify `Assets/Scripts/Fly/FlyController.cs`

- [ ] **Step 1: Add serialized refs**

Next to the existing B-2 VFX fields (e.g. after `bulletImpactDustPrefab`):

```csharp
        [Tooltip("CubeDeathBurst.prefab (Assets/VFX/Prefabs/). Passed to CubeDeath.ConfigureVfx in Awake. If null, no cube-death burst fires.")]
        [SerializeField] GameObject cubeDeathBurstPrefab;
        [Tooltip("Trail material for the dying-cube flame/smoke trail (RocketSmokeTrailMat to start). Passed to CubeDeath.ConfigureVfx. If null, no death trail.")]
        [SerializeField] Material cubeDeathTrailMaterial;
```

- [ ] **Step 2: Configure in `Awake`**

Immediately after the existing `ProjectileHit.ConfigureImpactPrefabs(bulletImpactSparkPrefab, bulletImpactDustPrefab);` line:

```csharp
            CubeDeath.ConfigureVfx(cubeDeathBurstPrefab, cubeDeathTrailMaterial);
```

(`FlyController` already `using CubeFly.Core;` — `CubeDeath` resolves.)

- [ ] **Step 3: Verify compile** — poll `editor_state`, `read_console(types=["error"])`, expect none.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Fly/FlyController.cs
git commit -m "feat(vfx): FlyController configures CubeDeath VFX in Awake (B-3a)"
```

---

### Task 6: Installer — generate `CubeDebrisMat` + `CubeDeathBurst.prefab`

**Files:** Modify `Assets/Scripts/Editor/VfxAssetsInstaller.cs`

> The exact ParticleSystem module code is authored against the live Unity API during execution (ParticleSystem config is highly verbose and version-sensitive). Mirror the existing `EnsureEnginePlumePrefab` structure (root PS + sub-emitters, `PrefabUtility.SaveAsPrefabAsset`, stable GUID, convergent regen). Parameters below are the contract.

- [ ] **Step 1: Add `EnsureOpaqueMeshDebrisMaterial(path, tint)`**

New helper mirroring `EnsureAdditiveParticleMaterial` but **opaque**: URP `Universal Render Pipeline/Unlit` (or `Lit`), `_Surface = 0` (Opaque), no blend override, `_BaseColor` = dark rust/metal tint `new Color(0.22f, 0.16f, 0.13f)`. Save to `Assets/VFX/Materials/CubeDebrisMat.mat`.

- [ ] **Step 2: Add `EnsureCubeDeathBurstPrefab(Material debrisMat)`**

Build a root GameObject "CubeDeathBurst" with a root ParticleSystem (`main.stopAction = Destroy`, `playOnAwake = true`, `duration` ≈ 0.1 s, `looping = false`) and three sub-systems (child ParticleSystems), per the spec tuning table:

- **Flash** — `startColor` HDR warm-white `(3,2.88,2.25, α3)`, `startSize` ≈ 1.0, `startLifetime` ≈ 0.08, emission = burst of 1, shape disabled (point), renderer = Billboard + **additive** material (reuse `MuzzleStarburstMat` or a shared additive glow material from the installer).
- **Spark** — `startColor` HDR warm `(3,2.5,1.5, α3)`, `startSize` ≈ 0.08, `startLifetime` ≈ 0.3, emission = burst of ≈ 20, shape = Sphere, `startSpeed` ≈ 6, renderer = Stretch/Billboard + additive tracer material (reuse `BulletTracerMat`).
- **Debris** — renderer `renderMode = Mesh` with the **built-in Cube mesh** (`Resources.GetBuiltinResource<Mesh>("Cube.fbx")`) + `debrisMat`; emission = burst of ≈ 4; shape = Cone along local +Z, `angle` ≈ 35°; `startSpeed` ≈ 4; `startSize` ≈ 0.15–0.25 (random between two constants); `startLifetime` ≈ 2; `gravityModifier` ≈ 0.7; `startRotation` random + `rotationOverLifetime` on.

Save via `PrefabUtility.SaveAsPrefabAsset` to `Assets/VFX/Prefabs/CubeDeathBurst.prefab` (mirror the engine-plume save; preserve GUID on regen).

- [ ] **Step 3: Call them from `Apply()`**

In the installer's `Apply()` (alongside the B-1/B-2 asset generation), add:
```csharp
Material cubeDebris = EnsureOpaqueMeshDebrisMaterial(CubeDebrisMatPath, new Color(0.22f, 0.16f, 0.13f));
GameObject cubeDeathBurst = EnsureCubeDeathBurstPrefab(cubeDebris);
```
and extend the final `Debug.Log("VfxAssetsInstaller: applied … VFX assets …")` summary to mention B-3a.

- [ ] **Step 4: Verify compile** — poll `editor_state`, `read_console(types=["error"])`, expect none. (The prefab/material aren't generated yet — that's Task 7 — this step only compiles the installer.)

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Editor/VfxAssetsInstaller.cs
git commit -m "feat(vfx): installer generates CubeDeathBurst prefab + CubeDebrisMat (B-3a)"
```

---

### Task 7: Run installer + wire `FlyController` refs in FlyScene

**Files:** creates `Assets/VFX/Prefabs/CubeDeathBurst.prefab`, `Assets/VFX/Materials/CubeDebrisMat.mat`; modifies `Assets/Scenes/FlyScene.unity`.

- [ ] **Step 1: Run the installer**

`execute_menu_item("Tools/CubeFly/Generate VFX assets")` (or the installer's actual menu path — confirm from `[MenuItem]` in `VfxAssetsInstaller.cs`). Then `read_console` → expect the "applied … VFX assets" log and no errors. Confirm the two assets now exist on disk.

- [ ] **Step 2: Assign the refs on FlyController in FlyScene**

Via `execute_code` (edit mode): find the `FlyController` in the open FlyScene, `SerializedObject` set `cubeDeathBurstPrefab` = the generated `CubeDeathBurst.prefab` and `cubeDeathTrailMaterial` = `RocketSmokeTrailMat.mat`, `ApplyModifiedPropertiesWithoutUndo()`, `EditorSceneManager.MarkSceneDirty` + `SaveScene`. (Mirrors how B-2's impact-prefab refs sit on FlyController in the scene.)

- [ ] **Step 3: Verify** — `read_console(types=["error"])` none; `git status` shows `CubeDeathBurst.prefab(.meta)`, `CubeDebrisMat.mat(.meta)`, `FlyScene.unity` changed.

- [ ] **Step 4: Commit**

```bash
git add Assets/VFX/Prefabs/CubeDeathBurst.prefab Assets/VFX/Prefabs/CubeDeathBurst.prefab.meta \
        Assets/VFX/Materials/CubeDebrisMat.mat Assets/VFX/Materials/CubeDebrisMat.mat.meta \
        Assets/Scenes/FlyScene.unity
git commit -m "feat(vfx): generate CubeDeathBurst assets + wire FlyController (B-3a)"
```

---

### Task 8: Play-mode gate + tune

**Files:** possibly re-tunes the prefab (Task 7 assets) if the gate flags it.

- [ ] **Step 1: Enter Play, force a cube death, capture**

`manage_editor(play)`; wait for `is_playing`. Via `execute_code`, find a `DesertTarget`/world cube and drive it to 0 HP (call `CubeStats.TakeRawDamage(9999)` through `CubeDamage.ApplyAndLog`, or directly `AddComponent<CubeDeath>().BeginDeath(center)`), `Time.timeScale=0` a beat later, then offscreen-render `Camera.main` via `RenderPipeline.StandardRequest` (game-view may be a sliver — see `project_vfx_arc` memory) and Read the PNG. Confirm flash + spark + debris + trail fire and read against the sand; additive burst blooms without blow-out.

- [ ] **Step 2: Tune if needed** — adjust the burst prefab params (counts, sizes, speeds, gravity, trail width/time) via the installer or directly, re-run, re-capture. Iterate until it reads well.

- [ ] **Step 3: Multi-cube sanity** — kill a small cluster in one frame; confirm no obvious perf spike (watch `read_console` / frame hitching). No pooling planned; note if the profiler flags it.

- [ ] **Step 4: Toggle check** — in Play, flip `VfxSettings.CubeDeathBurst` / `CubeDeathTrail` off (or via Settings → Debug) and confirm each effect disappears independently; the cube still drifts + despawns.

- [ ] **Step 5: Stop Play**, `manage_editor(stop)`. Commit any tuning:
```bash
git add Assets/VFX/Prefabs/CubeDeathBurst.prefab Assets/VFX/Materials/CubeDebrisMat.mat
git commit -m "tune(vfx): B-3a cube-death burst timings/scales from Play gate"
```

- [ ] **Step 6: Maintainer fly+shoot gate** — hand off to the maintainer to fly + shoot in the desert and confirm it reads well in real combat (the project's standard VFX gate). Wait for confirmation before landing.

---

### Task 9: Docs + land

**Files:** `ROADMAP.md`, `docs/full_architecture.md`.

- [ ] **Step 1: Docs** — mark B-3a shipped in `ROADMAP.md` (under Phase B); add the `CubeDeathBurst`/`CubeDebrisMat` assets + the `CubeDeath` VFX hook to `docs/full_architecture.md` (installer entry + `CubeDeath` row). Commit.

- [ ] **Step 2: Final verify** — `git status` clean except the intended files (+ pre-existing non-ours dirt); `read_console` no errors.

- [ ] **Step 3: Land** — per maintainer's choice (direct fast-forward to `main`, or PR). Then proceed to **B-3b** (low-HP feedback).

---

## Self-review

**Spec coverage:** flash/spark/debris/trail → Tasks 6 (assets) + 4 (spawn); hook in `BeginDeath` → Task 4; installer-generated → Task 6/7; static `ConfigureVfx` wiring → Tasks 4/5/7; 2 Debug toggles → Tasks 1/2; null-safe/decoupled → Task 4 guards; particle-only debris → Task 6 (Mesh burst, no rigidbodies); verification gate → Task 8; docs → Task 9. All spec sections covered.

**Placeholder scan:** installer PS module code is intentionally spec-by-parameter (Task 6 note) rather than verbatim — a deliberate adaptation to ParticleSystem verbosity, not a TODO; every other code step is complete.

**Type consistency:** `CubeDeathBurst`/`CubeDeathTrail` (properties), `BurstPrefab`/`TrailMaterial`/`ConfigureVfx` (CubeDeath), `cubeDeathBurstPrefab`/`cubeDeathTrailMaterial` (FlyController serialized), `_trailChild : LingeringTrail`, `DetachAndFade()` — names consistent across Tasks 1/4/5/7.
