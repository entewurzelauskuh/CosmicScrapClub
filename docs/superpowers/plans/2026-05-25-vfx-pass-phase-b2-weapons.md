# VFX Pass — Phase B-2 (Weapons + Impacts) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` (inline execution pre-selected by the user for this PR) to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship eight projectile-and-impact visual effects (muzzle flashes, bullet tracer, bullet impact spark + dust, rocket exhaust plume, rocket smoke trail + puffs) gated by eight new Debug-tab toggles, following the Phase B-1 architecture pattern.

**Architecture:** Distributed per-component (Approach A from the design spec). Per-cube VFX (muzzle flash) wired by `FlyController.BuildConstruct` into `WeaponBehavior`. Per-projectile VFX (tracer, plume, smoke trail, puffs) wired into projectile prefabs by an extended `VfxAssetsInstaller` and read by `Bullet.Awake` / `Rocket.Awake`. Impact VFX (spark, dust) routed through a new static helper `ProjectileHit.SpawnImpactVfx`, configured once by `FlyController` at scene load. Shared `LingeringTrail` helper for clean TrailRenderer detach on projectile destroy.

**Tech Stack:** Unity 6.3 LTS (`6000.3.11f1`), URP, C# 9 / .NET Standard 2.1, MonoBehaviour-only (no DOTS/ECS), no asmdefs, no automated tests. Verification is via `mcp__unityMCP__refresh_unity` + `mcp__unityMCP__read_console` after each script touch (compile-check) and manual play-test in the Unity Editor (behaviour-check).

**Design spec:** [docs/superpowers/specs/2026-05-25-vfx-pass-phase-b2-weapons-design.md](docs/superpowers/specs/2026-05-25-vfx-pass-phase-b2-weapons-design.md)

**Branch:** `feat/vfx-phase-b2-weapons` off `main` (already created; spec committed at `dd718fa`).

---

## File structure overview

### New files (15)

| Path | Responsibility | Size |
|---|---|---|
| `Assets/Scripts/Fly/LingeringTrail.cs` | Tiny MonoBehaviour exposing `DetachAndFade()` — detaches a TrailRenderer from its parent and sets it to fade-and-self-destruct. Called from `Bullet.OnDestroy` / `Rocket.OnDestroy`. | ~25 lines |
| `Assets/VFX/Textures/MuzzleStarburst_64.png` | 64×64 RGBA procedural starburst sprite: white core + 4 cardinal spikes + 4 fainter 45° diagonals + radial gradient falloff. | ~3 KB |
| `Assets/VFX/Textures/BulletTracerStripe_8x32.png` | 8×32 RGBA cross-section gradient: V=0.5 white core, V=0 / V=1 transparent pink edges. | ~1 KB |
| `Assets/VFX/Materials/MuzzleStarburstMat.mat` | URP Particles/Unlit additive, warm yellow-white tint, uses MuzzleStarburst_64. Shared between Pyramid muzzle + bullet impact spark. | |
| `Assets/VFX/Materials/MuzzleDiscMat.mat` | URP Particles/Unlit additive, warm orange/yellow, uses Glow_64. | |
| `Assets/VFX/Materials/BulletTracerMat.mat` | URP Particles/Unlit additive, white tint, uses BulletTracerStripe_8x32. Used by Bullet TrailRenderer. | |
| `Assets/VFX/Materials/BulletImpactDustMat.mat` | URP Particles/Unlit **alpha-blended** (not additive), warm tan tint, uses Glow_64. | |
| `Assets/VFX/Materials/RocketExhaustMat.mat` | URP Particles/Unlit additive, warm orange/yellow HDR ×2.5, uses Glow_64. | |
| `Assets/VFX/Materials/RocketSmokeTrailMat.mat` | URP Particles/Unlit **alpha-blended**, cool grey-white, uses Glow_64. Used by Rocket TrailRenderer. | |
| `Assets/VFX/Prefabs/MuzzleFlashStarburst.prefab` | One-shot ParticleSystem, single-particle starburst sprite, ~0.06 s lifetime, `stopAction = Destroy`. | |
| `Assets/VFX/Prefabs/MuzzleFlashDisc.prefab` | One-shot ParticleSystem, single-particle disc puff, ~0.10 s lifetime, `stopAction = Destroy`. | |
| `Assets/VFX/Prefabs/BulletImpactSpark.prefab` | One-shot ParticleSystem, 2 layers (core + radial sparks sub-system). Root `stopAction = Destroy`. | |
| `Assets/VFX/Prefabs/BulletImpactDust.prefab` | One-shot ParticleSystem, soft tan puff cluster, `stopAction = Destroy`. | |
| `Assets/VFX/Prefabs/RocketExhaustPlume.prefab` | Continuous looping ParticleSystem, warm yellow-orange flame, mirrors EnginePlume structure. | |
| `Assets/VFX/Prefabs/RocketSmokePuff.prefab` | Continuous looping ParticleSystem, soft white puffs with 10× growth, reuses RcsPuffMat. | |

### Modified files (10)

| Path | Change |
|---|---|
| `Assets/Scripts/Core/VfxSettings.cs` | Append 8 typed-bool properties + 8 PlayerPrefs keys. Fix stale header comment ("Five typed bool properties..."). |
| `Assets/Scripts/Core/SettingsMenu.cs` | Append 8 tuples to the `effects` array in `BuildDebugPanel`. |
| `Assets/Scripts/Fly/WeaponBehavior.cs` | Add `[SerializeField] GameObject muzzlePrefab`, `SetMuzzlePrefab` setter, `PlayMuzzleVfx` protected helper. |
| `Assets/Scripts/Fly/PyramidWeapon.cs` | After `Bullet.Launch(...)`, call `PlayMuzzleVfx(tipPos, Quaternion.LookRotation(fireDir), VfxSettings.MuzzleFlashPyramid)`. |
| `Assets/Scripts/Fly/CylinderWeapon.cs` | After `Rocket.Launch(...)`, call `PlayMuzzleVfx(exitPos, Quaternion.LookRotation(launchDir), VfxSettings.MuzzleFlashCylinder)`. |
| `Assets/Scripts/Fly/Bullet.cs` | Add `[SerializeField] Material tracerMaterial`. `Awake` creates a child GameObject `Tracer` parented to the bullet, then adds TrailRenderer + LingeringTrail to the **child** (toggle/null-guarded). The child-GameObject pattern is required so OnDestroy can detach it from the dying bullet. `Update` polls `VfxSettings.BulletTracer`. After `ApplyAndLog(...)`, call `ProjectileHit.SpawnImpactVfx(hit)`. `OnDestroy` calls `_lingeringTrail?.DetachAndFade()`. |
| `Assets/Scripts/Fly/Rocket.cs` | Add `[SerializeField] GameObject exhaustPlumePrefab; smokePuffPrefab; [SerializeField] Material smokeTrailMaterial;`. `Awake` instantiates plume + puff children (each with `localRotation = Quaternion.Euler(-90f, 0f, 0f)` to orient the Cone +Z emission along the rocket's local -Y = backward) + creates a child GameObject `SmokeTrail` parented to the rocket hosting the TrailRenderer + LingeringTrail (each toggle/null-guarded; trail uses `sharedMaterial` to avoid per-rocket Material allocation). `Update` polls three toggles. After `ApplyAndLog(...)`, call `ProjectileHit.SpawnImpactVfx(hit, scale: 1.20f)` — warhead is +20% bigger than a bullet puncture. `OnDestroy` detaches all child VFX and calls `Stop(ParticleSystemStopBehavior.StopEmitting)` on ParticleSystem children (StopEmitting keeps alive particles; StopEmittingAndClear would kill them). |
| `Assets/Scripts/Fly/ProjectileHit.cs` | Add `public static GameObject SparkPrefab; DustPrefab;` + `ConfigureImpactPrefabs(spark, dust)` setter + `SpawnImpactVfx(in RaycastHit hit, float scale = 1.0f)` static method (optional uniform scale on the spawned prefab GameObject — Bullet uses default 1.0, Rocket passes 1.20). **`ApplyAndLog` itself unchanged.** |
| `Assets/Scripts/Fly/FlyController.cs` | Add 4 new `[SerializeField] GameObject` fields. In `Awake`: `ProjectileHit.ConfigureImpactPrefabs(...)`. In `BuildConstruct`, when a weapon component is found, type-switch on `PyramidWeapon`/`CylinderWeapon` and call `weapon.SetMuzzlePrefab(...)`. |
| `Assets/Scripts/Editor/VfxAssetsInstaller.cs` | Extend `Apply()`. Add `EnsureAlphaBlendedParticleMaterial` helper. Add `EnsureStarburstTexture` + `EnsureTracerStripeTexture` generators. Add 6 material creation calls + 6 prefab creation calls. Add `WireBulletPrefab` + `WireRocketPrefab` methods that use `PrefabUtility.LoadPrefabContents` + `SerializedObject` to set the new SerializeField references on the existing projectile prefabs. |

### Implementation order

Three phases (each phase produces a working intermediate state — partial assets / partial behaviour, but nothing broken):

- **Phase 0 — Code infrastructure (Tasks 1–5):** new types and method hooks exist, behaviour is null-guarded so the game runs unchanged.
- **Phase 1 — Asset generation (Tasks 6–12):** all assets on disk, projectile prefabs wired by the installer.
- **Phase 2 — Runtime wiring (Tasks 13–21):** effects activate in-game.
- **Phase 3 — Final verification (Task 22):** full smoke pass + bugfixes.

---

## Per-task verification recipe (used throughout)

Most tasks share this verification pattern. Where a task lists "Refresh & verify clean compile", the steps are:

1. **Refresh Unity** — invoke the MCP refresh tool:
   ```
   mcp__unityMCP__refresh_unity(
       mode="force",
       compile="request",
       scope="scripts",
       wait_for_ready=true)
   ```
   Wait for the call to return (~2–3 s on this project).

2. **Read console for errors** — only the project's scripts, only errors:
   ```
   mcp__unityMCP__read_console(
       types=["error"],
       count=20,
       filter_text="Assets/Scripts",
       format="detailed")
   ```
   **Expected: zero error entries.** If any appear, fix the code and repeat.

For tasks that also need the installer re-run, use:
```
mcp__unityMCP__execute_menu_item(menu_path="Tools/CubeFly/Generate VFX assets")
```
followed by another refresh + console-check. Expected log line: `VfxAssetsInstaller: applied Phase B-2 VFX assets ...` (after task 9's installer-message update).

For tasks that need play-test, the verification step describes what to look for in the Editor Game view; a human runs Play manually since play-mode verification is faster done by eye than scripted.

---

## Phase 0 — Code infrastructure

### Task 1: VfxSettings — add 8 new keys + update header comment

**Files:**
- Modify: `Assets/Scripts/Core/VfxSettings.cs`

- [ ] **Step 1: Update header doc comment** (lines 6–18, the multi-line summary)

Find:
```csharp
    // PlayerPrefs-backed static facade for the VFX Debug-tab toggles.
    // Five typed bool properties; each Get reads PlayerPrefs (default 1
    // = ON), each Set writes + saves + fires Changed. No batching, no
    // Apply button: changes take effect immediately because the Debug
    // tab is a real-time A/B comparison surface.
```

Replace the first line of the doc with the current count (use "the typed bool properties below" so future additions don't restale it):
```csharp
    // PlayerPrefs-backed static facade for the VFX Debug-tab toggles.
    // The typed bool properties below each Get reads PlayerPrefs
    // (default 1 = ON), each Set writes + saves + fires Changed. No
    // batching, no Apply button: changes take effect immediately
    // because the Debug tab is a real-time A/B comparison surface.
```

- [ ] **Step 2: Append 8 new constants** after line 28 (`const string KRcsPuff = "VfxRcsPuff";`):

```csharp
        const string KMuzzleFlashPyramid  = "VfxMuzzleFlashPyramid";
        const string KMuzzleFlashCylinder = "VfxMuzzleFlashCylinder";
        const string KBulletTracer        = "VfxBulletTracer";
        const string KBulletImpactSpark   = "VfxBulletImpactSpark";
        const string KBulletImpactDust    = "VfxBulletImpactDust";
        const string KRocketExhaust       = "VfxRocketExhaust";
        const string KRocketSmokeTrail    = "VfxRocketSmokeTrail";
        const string KRocketSmokePuff     = "VfxRocketSmokePuff";
```

- [ ] **Step 3: Append 8 new properties** after line 39 (`public static bool RcsPuff = ...`):

```csharp
        public static bool MuzzleFlashPyramid  { get => Get(KMuzzleFlashPyramid);  set => Set(KMuzzleFlashPyramid,  value); }
        public static bool MuzzleFlashCylinder { get => Get(KMuzzleFlashCylinder); set => Set(KMuzzleFlashCylinder, value); }
        public static bool BulletTracer        { get => Get(KBulletTracer);        set => Set(KBulletTracer,        value); }
        public static bool BulletImpactSpark   { get => Get(KBulletImpactSpark);   set => Set(KBulletImpactSpark,   value); }
        public static bool BulletImpactDust    { get => Get(KBulletImpactDust);    set => Set(KBulletImpactDust,    value); }
        public static bool RocketExhaust       { get => Get(KRocketExhaust);       set => Set(KRocketExhaust,       value); }
        public static bool RocketSmokeTrail    { get => Get(KRocketSmokeTrail);    set => Set(KRocketSmokeTrail,    value); }
        public static bool RocketSmokePuff     { get => Get(KRocketSmokePuff);     set => Set(KRocketSmokePuff,     value); }
```

- [ ] **Step 4: Refresh & verify clean compile** (see "Per-task verification recipe").

- [ ] **Step 5: Commit**

```bash
cd "/Users/anon/My project"
git add Assets/Scripts/Core/VfxSettings.cs
git commit -m "vfx-b2: add 8 weapon-VFX toggle keys to VfxSettings

Adds typed bool properties for VfxMuzzleFlash(Pyramid|Cylinder),
BulletTracer, BulletImpact(Spark|Dust), RocketExhaust,
RocketSmoke(Trail|Puff). All default ON via existing PlayerPrefs
fallback. Updates stale header comment ('Five typed bool
properties...') to avoid future drift.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 2: SettingsMenu — add 8 Debug-tab toggle entries

**Files:**
- Modify: `Assets/Scripts/Core/SettingsMenu.cs` (around line 343 — the `effects` array in `BuildDebugPanel`)

- [ ] **Step 1: Locate the `effects` array initialization**

In `BuildDebugPanel`, find the `effects` array declaration around line 343–369. After the last existing entry (`VfxSettings.RcsPuff` setter line, around line 369), before the closing `};` of the array, append 8 new tuples:

```csharp
                ("Pyramid muzzle",
                    "One-frame yellow-white starburst at the Pyramid weapon's tip when it fires.",
                    () => VfxSettings.MuzzleFlashPyramid,  v => VfxSettings.MuzzleFlashPyramid  = v),
                ("Cylinder muzzle",
                    "Soft orange/yellow disc puff at the Cylinder weapon's barrel when it fires.",
                    () => VfxSettings.MuzzleFlashCylinder, v => VfxSettings.MuzzleFlashCylinder = v),
                ("Bullet tracer",
                    "Yellow-white core + pink-fringe trail behind machine-gun bullets in flight.",
                    () => VfxSettings.BulletTracer,        v => VfxSettings.BulletTracer        = v),
                ("Bullet impact spark",
                    "Bright warm spark + radiating streaks where a bullet hits any surface.",
                    () => VfxSettings.BulletImpactSpark,   v => VfxSettings.BulletImpactSpark   = v),
                ("Bullet impact dust",
                    "Soft tan puff cluster where a bullet hits a roughly-upward surface.",
                    () => VfxSettings.BulletImpactDust,    v => VfxSettings.BulletImpactDust    = v),
                ("Rocket exhaust",
                    "Warm orange/yellow flame from the rocket's tail while in flight.",
                    () => VfxSettings.RocketExhaust,       v => VfxSettings.RocketExhaust       = v),
                ("Rocket smoke trail",
                    "Cool grey-white ribbon trailing behind the rocket in flight.",
                    () => VfxSettings.RocketSmokeTrail,    v => VfxSettings.RocketSmokeTrail    = v),
                ("Rocket smoke puffs",
                    "Soft white cloud puffs emitted from the rocket's tail.",
                    () => VfxSettings.RocketSmokePuff,     v => VfxSettings.RocketSmokePuff     = v),
```

The existing `leftCount = (effects.Length + 1) / 2` (around line 377) auto-rebalances: 16 entries → 8 left, 8 right.

- [ ] **Step 2: Refresh & verify clean compile.**

- [ ] **Step 3: Manual verify** — open Unity Editor, Play any scene that exposes Settings (MainMenu works), open Settings → Debug. Confirm 16 toggles visible in two columns, all defaulting to ON. Toggle a couple to confirm persistence-on-write fires (no exception in console).

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Core/SettingsMenu.cs
git commit -m "vfx-b2: surface 8 new VFX toggles in Settings Debug tab

Appends entries for Pyramid muzzle, Cylinder muzzle, bullet tracer,
bullet impact (spark/dust), rocket exhaust, rocket smoke (trail/puff).
Two-column layout auto-rebalances 8/8 via the existing leftCount
formula. Toggles wire to the VfxSettings keys added in the previous
commit; no visual effect yet (Phase 0 infrastructure only).

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 3: LingeringTrail.cs — new shared helper

**Files:**
- Create: `Assets/Scripts/Fly/LingeringTrail.cs`

- [ ] **Step 1: Create the file** with this exact content:

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
    // DetachAndFade() explicitly from its own OnDestroy.
    //
    // After DetachAndFade:
    //  • The TrailRenderer's GameObject is unparented.
    //  • emitting = false  — no new vertices appear.
    //  • autodestruct = true — the GameObject removes itself once
    //    the last surviving trail segment expires per TrailRenderer.time.
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

- [ ] **Step 2: Expand the `.meta` file** (the project hygiene rule — `mcp__unityMCP__create_script` and `Write` both produce minimal metas; Copilot reviews flag them). After saving the .cs, the .meta gets auto-created with just `fileFormatVersion: 2` + `guid: ...`. Replace it with the full MonoImporter format matching another script's meta:

Read an existing meta as a reference:
```
Read: Assets/Scripts/Core/PauseMenu.cs.meta
```

Copy its structure to the new `Assets/Scripts/Fly/LingeringTrail.cs.meta`, keeping ONLY the new file's auto-generated `guid:` value. The rest of the YAML (defaultReferences, executionOrder, icon, userData, assetBundleName, assetBundleVariant) should match.

- [ ] **Step 3: Refresh & verify clean compile.**

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Fly/LingeringTrail.cs Assets/Scripts/Fly/LingeringTrail.cs.meta
git commit -m "vfx-b2: add LingeringTrail helper for clean trail detach

Shared MonoBehaviour for Bullet and Rocket. DetachAndFade()
unparents the TrailRenderer's GameObject, disables emitting, sets
autodestruct=true. Called explicitly from the parent projectile's
OnDestroy so the trail survives long enough to fade per its time
config instead of vanishing with the projectile.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 4: WeaponBehavior — add muzzle hook

**Files:**
- Modify: `Assets/Scripts/Fly/WeaponBehavior.cs`

- [ ] **Step 1: Add muzzle prefab serialize field** in the `[Header("Common")]` block (around line 25, alongside the existing `projectilePrefab` field):

After line 27 (`[SerializeField] protected float reloadSeconds = 0.2f;`), add:
```csharp
        [Tooltip("Wired by FlyController.BuildConstruct after weapon instantiation. Per subclass: Pyramid → MuzzleFlashStarburst.prefab; Cylinder → MuzzleFlashDisc.prefab. If null, no muzzle VFX fires.")]
        [SerializeField] GameObject muzzlePrefab;
```

- [ ] **Step 2: Add public setter** alongside the existing public properties (around line 36–38):

After line 38 (`public bool CanFire => _cooldown <= 0f;`), add:
```csharp

        // Wired by FlyController.BuildConstruct, mirroring ThrusterVfx.SetPlumePrefab.
        public void SetMuzzlePrefab(GameObject prefab) => muzzlePrefab = prefab;
```

- [ ] **Step 3: Add `PlayMuzzleVfx` helper** at the end of the class, just before the closing `}`:

```csharp

        // Spawn a one-shot muzzle-flash GameObject from `muzzlePrefab` at
        // the given world pos/rot. Toggle-gated by `toggle` and null-
        // guarded against missing prefab. The prefab is expected to have
        // a ParticleSystem with `main.stopAction = Destroy` so the
        // instance auto-cleans after its burst finishes.
        protected void PlayMuzzleVfx(Vector3 worldPos, Quaternion worldRot, bool toggle)
        {
            if (!toggle || muzzlePrefab == null) return;
            Instantiate(muzzlePrefab, worldPos, worldRot);
        }
```

- [ ] **Step 4: Refresh & verify clean compile.**

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Fly/WeaponBehavior.cs
git commit -m "vfx-b2: add muzzle-flash hook to WeaponBehavior

New protected PlayMuzzleVfx(pos, rot, toggle) helper that subclasses
call from Fire(). Wired by FlyController.BuildConstruct via
SetMuzzlePrefab() (mirrors ThrusterVfx.SetPlumePrefab pattern). Null-
guarded against missing prefab; toggle-guarded against the off case.

No subclass calls this yet — wiring lands in subsequent commits.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 5: ProjectileHit — add impact spawner static helper

**Files:**
- Modify: `Assets/Scripts/Fly/ProjectileHit.cs`

- [ ] **Step 1: Add `using` for `CubeFly.Core`** — already present at the top of the file (line 1). No new using needed.

- [ ] **Step 2: Add static config + spawn helpers** at the end of the class, just before the closing `}`:

Find the end of `ApplyAndLog` (around line 121) and the closing `}` of the class (around line 122). Insert before that closing `}`:

```csharp

        // ---------- B-2 impact VFX dispatch ----------

        // Configured once per FlyScene load by FlyController.Awake.
        // Static rather than instance because ProjectileHit is itself
        // static (no MonoBehaviour to hang [SerializeField] off), and
        // both projectile types (Bullet, Rocket) need to dispatch to
        // the same prefab references.
        public static GameObject SparkPrefab;
        public static GameObject DustPrefab;

        // Called once by FlyController.Awake before any projectile
        // can possibly spawn. Subsequent calls overwrite; safe to
        // re-call during scene transitions.
        public static void ConfigureImpactPrefabs(GameObject spark, GameObject dust)
        {
            SparkPrefab = spark;
            DustPrefab = dust;
        }

        // Spawn the appropriate impact VFX at the hit point, oriented
        // along the surface normal. Spark fires on any hit (toggle and
        // prefab permitting). Dust additionally fires when the hit
        // surface is roughly upward (matches PyramidWeapon's
        // FrontalDotThreshold = cos 45°). The two are independent
        // toggles — both, either, or neither can fire.
        //
        // Called from Bullet/Rocket right after ApplyAndLog, before
        // the projectile Destroys itself. Kept here (rather than in
        // ApplyAndLog) so damage and presentation stay separately
        // call-sited.
        public static void SpawnImpactVfx(in RaycastHit hit, float scale = 1.0f)
        {
            Quaternion orientation = Quaternion.LookRotation(hit.normal);

            if (VfxSettings.BulletImpactSpark && SparkPrefab != null)
            {
                GameObject go = Object.Instantiate(SparkPrefab, hit.point, orientation);
                if (scale != 1.0f) go.transform.localScale = Vector3.one * scale;
            }

            if (VfxSettings.BulletImpactDust && DustPrefab != null
                && Vector3.Dot(hit.normal, Vector3.up) > 0.7f)
            {
                GameObject go = Object.Instantiate(DustPrefab, hit.point, orientation);
                if (scale != 1.0f) go.transform.localScale = Vector3.one * scale;
            }
        }
```

Optional `scale` (default 1.0) uniform-scales the spawned impact prefabs. Bullet passes the default; Rocket passes 1.20 so the warhead-sized impact reads ~20% bigger.

- [ ] **Step 3: Refresh & verify clean compile.**

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Fly/ProjectileHit.cs
git commit -m "vfx-b2: add SpawnImpactVfx static dispatcher to ProjectileHit

New static SparkPrefab/DustPrefab fields plus ConfigureImpactPrefabs
setter (called once from FlyController.Awake) and SpawnImpactVfx
spawner (called from Bullet/Rocket after ApplyAndLog). Spark fires on
any hit; dust additionally fires when Dot(hit.normal, Vector3.up) >
0.7 (cos 45°, matching PyramidWeapon.FrontalDotThreshold). Both
toggle-guarded and null-guarded. ApplyAndLog itself unchanged —
damage and presentation stay separately call-sited.

No callers yet; wiring lands in subsequent commits.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Phase 1 — Asset generation via extended installer

### Task 6: VfxAssetsInstaller — add alpha-blended material helper

**Files:**
- Modify: `Assets/Scripts/Editor/VfxAssetsInstaller.cs`

- [ ] **Step 1: Add new helper method** alongside the existing `EnsureAdditiveParticleMaterial` (around line 124). Append after `EnsureAdditiveParticleMaterial` closes (around line 155):

```csharp

        // URP Particles/Unlit, alpha-blended (NOT additive). For VFX
        // that should darken or soft-overlay (smoke, dust) rather than
        // pop with bloom. SrcAlpha + OneMinusSrcAlpha standard alpha.
        static Material EnsureAlphaBlendedParticleMaterial(string path, Texture2D texture, Color tint)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            if (texture != null && mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", texture);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", tint);
            if (mat.HasProperty("_MainTex") && texture != null)
                mat.SetTexture("_MainTex", texture);

            // Alpha-blend: _Surface = 1 (Transparent) + _Blend = 0 (Alpha).
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend"))   mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite"))   mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3000;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            EditorUtility.SetDirty(mat);
            return mat;
        }
```

- [ ] **Step 2: Refresh & verify clean compile.** (No installer run yet — helper is unused; later tasks call it.)

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Editor/VfxAssetsInstaller.cs
git commit -m "vfx-b2: add EnsureAlphaBlendedParticleMaterial helper

Mirrors EnsureAdditiveParticleMaterial but with standard alpha blend
(SrcAlpha + OneMinusSrcAlpha) instead of additive (SrcAlpha + One).
For smoke/dust effects that should soft-overlay rather than pop with
bloom. Same idempotent pattern: re-applies tint and blend props on
every run.

Unused for now; callers land in subsequent commits.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 7: VfxAssetsInstaller — add starburst + tracer-stripe texture generators

**Files:**
- Modify: `Assets/Scripts/Editor/VfxAssetsInstaller.cs`

- [ ] **Step 1: Add path constants** at the top of the class (alongside the existing `GlowTexturePath`, around line 40):

```csharp
        const string StarburstTexturePath    = TexturesDir + "/MuzzleStarburst_64.png";
        const string TracerStripeTexturePath = TexturesDir + "/BulletTracerStripe_8x32.png";
```

- [ ] **Step 2: Add the starburst texture generator** alongside `EnsureGlowTexture` (around line 81). Append after `EnsureGlowTexture` closes (around line 120):

```csharp

        // 64×64 RGBA32 starburst sprite: bright white core + 4 cardinal
        // spikes + 4 fainter diagonal spikes + radial gradient falloff.
        // Used by the Pyramid muzzle flash and the bullet impact spark
        // (both share MuzzleStarburstMat). Procedurally generated for
        // git-friendliness; skipped on subsequent runs if the PNG exists.
        static Texture2D EnsureStarburstTexture()
        {
            if (File.Exists(StarburstTexturePath))
                return AssetDatabase.LoadAssetAtPath<Texture2D>(StarburstTexturePath);

            const int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[size * size];
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float maxDist = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 d = new Vector2(x - center.x, y - center.y);
                    float dist = d.magnitude;
                    float radial = Mathf.Clamp01(dist / maxDist);

                    // Core: tight gaussian falloff.
                    float core = Mathf.Exp(-radial * radial * 9f);

                    // Cardinal spikes (0°, 90°, 180°, 270°): tight angular
                    // band perpendicular to each axis, fading along
                    // length proportional to (1 - radial).
                    float spikeH = Mathf.Exp(-((d.y * d.y) / 1.2f));
                    float spikeV = Mathf.Exp(-((d.x * d.x) / 1.2f));
                    float cardinal = Mathf.Max(spikeH, spikeV) * Mathf.Max(0f, 1f - radial) * 0.85f;

                    // Diagonal spikes (45°): rotate coords by 45°, same
                    // gaussian-along-axis treatment, fainter (×0.4).
                    float c = 0.7071f; // cos 45° = sin 45°
                    float dx45 =  d.x * c + d.y * c;
                    float dy45 = -d.x * c + d.y * c;
                    float diagH = Mathf.Exp(-((dy45 * dy45) / 1.2f));
                    float diagV = Mathf.Exp(-((dx45 * dx45) / 1.2f));
                    float diagonal = Mathf.Max(diagH, diagV) * Mathf.Max(0f, 1f - radial) * 0.40f;

                    float alpha = Mathf.Clamp01(Mathf.Max(core, Mathf.Max(cardinal, diagonal)));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(StarburstTexturePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(StarburstTexturePath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(StarburstTexturePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.sRGBTexture = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(StarburstTexturePath);
        }
```

- [ ] **Step 3: Add the tracer-stripe texture generator** immediately after `EnsureStarburstTexture`:

```csharp

        // 8×32 RGBA32 cross-section gradient for the bullet TrailRenderer.
        // V=0.5 → bright white core; V→0 and V→1 → transparent hot pink.
        // U axis is uniform (no pattern); the TrailRenderer's default UV
        // mapping stretches U along the trail length, so the cross-
        // section pink-fringe halo appears across the trail's width.
        static Texture2D EnsureTracerStripeTexture()
        {
            if (File.Exists(TracerStripeTexturePath))
                return AssetDatabase.LoadAssetAtPath<Texture2D>(TracerStripeTexturePath);

            const int w = 8;
            const int h = 32;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[w * h];
            float center = (h - 1) / 2f;
            float maxDist = center;
            Color pink = new Color(1f, 0.4f, 0.75f);
            for (int y = 0; y < h; y++)
            {
                float dist = Mathf.Abs(y - center);
                float t = Mathf.Clamp01(dist / maxDist);
                // Gaussian-shaped cross-section: bright white at center,
                // fading through warm-pink to transparent at edges.
                float coreWeight = Mathf.Exp(-t * t * 4f);    // 1.0 at center, ~0.02 at edge
                Color rgb = Color.Lerp(pink, Color.white, coreWeight);
                float alpha = coreWeight;
                Color c = new Color(rgb.r, rgb.g, rgb.b, alpha);
                for (int x = 0; x < w; x++)
                    pixels[y * w + x] = c;
            }
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(TracerStripeTexturePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(TracerStripeTexturePath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(TracerStripeTexturePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.sRGBTexture = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(TracerStripeTexturePath);
        }
```

- [ ] **Step 4: Call them from `Apply()`** — find the `Apply` method (around line 47–70). After `Texture2D glow = EnsureGlowTexture();` (around line 54), add:

```csharp
            Texture2D starburst    = EnsureStarburstTexture();
            Texture2D tracerStripe = EnsureTracerStripeTexture();
```

These variables will be passed to material generators in the next task — for now they're declared so the compile succeeds. The two `_ = ...` discards prevent "unused variable" warnings if Unity flags them, but in C# unused locals don't warn by default — leave as named variables for the next task to use.

- [ ] **Step 5: Refresh & verify clean compile.**

- [ ] **Step 6: Run installer**

```
mcp__unityMCP__execute_menu_item(menu_path="Tools/CubeFly/Generate VFX assets")
```

Wait ~2 s, then refresh + read console. Look for the existing log line *"VfxAssetsInstaller: applied Phase B-1 VFX assets ..."* (still says "B-1" — will be updated in task 9). No errors expected.

- [ ] **Step 7: Verify textures on disk**

```bash
ls -la "/Users/anon/My project/Assets/VFX/Textures/"
```

Expected: `Glow_64.png` (existing), `MuzzleStarburst_64.png` (new, ~3 KB), `BulletTracerStripe_8x32.png` (new, ~1 KB).

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/Editor/VfxAssetsInstaller.cs \
        Assets/VFX/Textures/MuzzleStarburst_64.png \
        Assets/VFX/Textures/MuzzleStarburst_64.png.meta \
        Assets/VFX/Textures/BulletTracerStripe_8x32.png \
        Assets/VFX/Textures/BulletTracerStripe_8x32.png.meta
git commit -m "vfx-b2: add procedural starburst + tracer-stripe textures

EnsureStarburstTexture generates a 64x64 RGBA white starburst with
4 cardinal + 4 diagonal spikes (gaussian-along-axis bands) over a
radial-gradient core. Used by Pyramid muzzle flash + bullet impact
spark. EnsureTracerStripeTexture generates an 8x32 RGBA cross-section
gradient (white core at V=0.5, pink edges) for the bullet TrailRenderer
material. Both first-creation-only (idempotent — delete file to regen).

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 8: VfxAssetsInstaller — add 6 material generation calls

**Files:**
- Modify: `Assets/Scripts/Editor/VfxAssetsInstaller.cs`

- [ ] **Step 1: Add path constants** alongside the existing material paths (around lines 41–43):

```csharp
        const string MuzzleStarburstMatPath   = MaterialsDir + "/MuzzleStarburstMat.mat";
        const string MuzzleDiscMatPath        = MaterialsDir + "/MuzzleDiscMat.mat";
        const string BulletTracerMatPath      = MaterialsDir + "/BulletTracerMat.mat";
        const string BulletImpactDustMatPath  = MaterialsDir + "/BulletImpactDustMat.mat";
        const string RocketExhaustMatPath     = MaterialsDir + "/RocketExhaustMat.mat";
        const string RocketSmokeTrailMatPath  = MaterialsDir + "/RocketSmokeTrailMat.mat";
```

- [ ] **Step 2: Add 6 material creation calls in `Apply()`** — after the existing material creations (around line 61, after `rcsPuff` assignment), add:

```csharp

            Material muzzleStarburst   = EnsureAdditiveParticleMaterial(
                MuzzleStarburstMatPath,  starburst,    new Color(1f,    0.96f, 0.75f, 1f));
            Material muzzleDisc        = EnsureAdditiveParticleMaterial(
                MuzzleDiscMatPath,       glow,         new Color(1f,    0.70f, 0.30f, 1f));
            Material bulletTracer      = EnsureAdditiveParticleMaterial(
                BulletTracerMatPath,     tracerStripe, new Color(1f,    1f,    1f,    1f));
            Material bulletImpactDust  = EnsureAlphaBlendedParticleMaterial(
                BulletImpactDustMatPath, glow,         new Color(0.92f, 0.82f, 0.60f, 1f));
            Material rocketExhaust     = EnsureAdditiveParticleMaterial(
                RocketExhaustMatPath,    glow,         new Color(1f,    0.70f, 0.30f, 1f));
            Material rocketSmokeTrail  = EnsureAlphaBlendedParticleMaterial(
                RocketSmokeTrailMatPath, glow,         new Color(0.92f, 0.95f, 1f,    1f));
```

These variables get used by the prefab generators (Task 9–11) and prefab wirers (Task 12). For this commit, they're declared and unused — that's fine, C# doesn't error on unused locals.

- [ ] **Step 3: Refresh & verify clean compile.**

- [ ] **Step 4: Run installer**

```
mcp__unityMCP__execute_menu_item(menu_path="Tools/CubeFly/Generate VFX assets")
```

- [ ] **Step 5: Verify materials on disk**

```bash
ls -la "/Users/anon/My project/Assets/VFX/Materials/"
```

Expected: 3 existing (`EnginePlumeMat.mat`, `BoostShockMat.mat`, `RcsPuffMat.mat`) + 6 new (`MuzzleStarburstMat.mat`, `MuzzleDiscMat.mat`, `BulletTracerMat.mat`, `BulletImpactDustMat.mat`, `RocketExhaustMat.mat`, `RocketSmokeTrailMat.mat`).

Optionally open one of the new materials in the Unity Editor inspector to spot-check: BulletImpactDustMat should show `_BaseColor` tan, `_BaseMap` = `Glow_64`, blend mode "Alpha". RocketExhaustMat should be additive (`_DstBlend = One`).

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Editor/VfxAssetsInstaller.cs Assets/VFX/Materials/
git commit -m "vfx-b2: generate 6 weapon-VFX materials

Adds path constants and EnsureAdditive/AlphaBlended calls for
MuzzleStarburstMat, MuzzleDiscMat, BulletTracerMat, BulletImpactDustMat,
RocketExhaustMat, RocketSmokeTrailMat. Additive for muzzle+tracer+
plume (bloom-amplified); alpha-blended for impact-dust and rocket-
smoke-trail (soft overlay). All idempotent — tints and blend params
reapply every run.

Materials not consumed yet; prefab generators and prefab wiring land
in subsequent commits.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 9: VfxAssetsInstaller — add muzzle flash prefab generators (Starburst + Disc)

**Files:**
- Modify: `Assets/Scripts/Editor/VfxAssetsInstaller.cs`

- [ ] **Step 1: Add path constants** alongside the existing prefab paths (around lines 44–45):

```csharp
        const string MuzzleFlashStarburstPrefabPath = PrefabsDir + "/MuzzleFlashStarburst.prefab";
        const string MuzzleFlashDiscPrefabPath      = PrefabsDir + "/MuzzleFlashDisc.prefab";
```

- [ ] **Step 2: Add Starburst prefab generator** — append to the end of the class (just before the closing `}` of `VfxAssetsInstaller`):

```csharp

        // Pyramid muzzle flash. Single-particle one-shot starburst at the
        // weapon's tip. stopAction = Destroy + duration < lifetime ensures
        // the instantiated GameObject auto-cleans after its burst.
        static void EnsureMuzzleFlashStarburstPrefab(Material starburstMat)
        {
            GameObject root = new GameObject("MuzzleFlashStarburst");
            try
            {
                ParticleSystem ps = root.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                var main = ps.main;
                main.duration = 0.10f;
                main.loop = false;
                main.startLifetime = 0.06f;
                main.startSpeed = 0f;
                main.startSize = 0.18f;
                main.startColor = new Color(1f, 0.96f, 0.75f, 1f) * 3f;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.maxParticles = 8;
                main.playOnAwake = true;
                main.stopAction = ParticleSystemStopAction.Destroy;

                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.05f;

                var sz = ps.sizeOverLifetime;
                sz.enabled = true;
                AnimationCurve szCurve = new AnimationCurve(
                    new Keyframe(0f, 1f), new Keyframe(1f, 0.4f));
                sz.size = new ParticleSystem.MinMaxCurve(1f, szCurve);

                var col = ps.colorOverLifetime;
                col.enabled = true;
                Gradient g = new Gradient();
                g.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.10f), new GradientAlphaKey(0f, 1f) });
                col.color = new ParticleSystem.MinMaxGradient(g);

                var renderer = root.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.sharedMaterial = starburstMat;

                PrefabUtility.SaveAsPrefabAsset(root, MuzzleFlashStarburstPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
```

- [ ] **Step 3: Add Disc prefab generator** — append immediately after:

```csharp

        // Cylinder muzzle flash. Single-particle soft disc puff, warmer
        // orange tint, slight outward expansion (startSpeed > 0 +
        // expanding sizeOverLifetime). Reuses Glow_64.png via MuzzleDiscMat.
        static void EnsureMuzzleFlashDiscPrefab(Material discMat)
        {
            GameObject root = new GameObject("MuzzleFlashDisc");
            try
            {
                ParticleSystem ps = root.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                var main = ps.main;
                main.duration = 0.12f;
                main.loop = false;
                main.startLifetime = 0.10f;
                main.startSpeed = 0.5f;
                main.startSize = 0.30f;
                main.startColor = new Color(1f, 0.70f, 0.30f, 1f) * 2.5f;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.maxParticles = 6;
                main.playOnAwake = true;
                main.stopAction = ParticleSystemStopAction.Destroy;

                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.05f;

                var sz = ps.sizeOverLifetime;
                sz.enabled = true;
                AnimationCurve szCurve = new AnimationCurve(
                    new Keyframe(0f, 0.6f), new Keyframe(1f, 1.4f));
                sz.size = new ParticleSystem.MinMaxCurve(1f, szCurve);

                var col = ps.colorOverLifetime;
                col.enabled = true;
                Gradient g = new Gradient();
                g.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
                col.color = new ParticleSystem.MinMaxGradient(g);

                var renderer = root.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.sharedMaterial = discMat;

                PrefabUtility.SaveAsPrefabAsset(root, MuzzleFlashDiscPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
```

- [ ] **Step 4: Call both from `Apply()`** — after the material creations from Task 8, before `AssetDatabase.SaveAssets();` (around line 66), add:

```csharp
            EnsureMuzzleFlashStarburstPrefab(muzzleStarburst);
            EnsureMuzzleFlashDiscPrefab(muzzleDisc);
```

- [ ] **Step 5: Update the final log line** to reflect B-2 assets — find around line 68 (existing: `"VfxAssetsInstaller: applied Phase B-1 VFX assets (Glow_64, ..."`). Replace with:

```csharp
            Debug.Log("VfxAssetsInstaller: applied Phase B-1 + B-2 VFX assets " +
                "(Glow_64, MuzzleStarburst_64, BulletTracerStripe_8x32; " +
                "EnginePlumeMat, BoostShockMat, RcsPuffMat, MuzzleStarburstMat, MuzzleDiscMat, " +
                "BulletTracerMat, BulletImpactDustMat, RocketExhaustMat, RocketSmokeTrailMat; " +
                "EnginePlume.prefab, RcsPuff.prefab, MuzzleFlashStarburst.prefab, MuzzleFlashDisc.prefab).");
```

(Subsequent prefab-generation tasks will extend this string further.)

- [ ] **Step 6: Refresh & verify clean compile, then run installer.**

```
mcp__unityMCP__refresh_unity(mode="force", compile="request", scope="scripts", wait_for_ready=true)
mcp__unityMCP__read_console(types=["error"], count=20, filter_text="Assets/Scripts", format="detailed")
mcp__unityMCP__execute_menu_item(menu_path="Tools/CubeFly/Generate VFX assets")
```

- [ ] **Step 7: Verify prefabs on disk**

```bash
ls -la "/Users/anon/My project/Assets/VFX/Prefabs/"
```

Expected: 2 existing (`EnginePlume.prefab`, `RcsPuff.prefab`) + 2 new (`MuzzleFlashStarburst.prefab`, `MuzzleFlashDisc.prefab`).

Optionally open `MuzzleFlashStarburst.prefab` in the inspector — should show one ParticleSystem with the MuzzleStarburstMat assigned, duration 0.10, stopAction Destroy, 1-burst emission.

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/Editor/VfxAssetsInstaller.cs Assets/VFX/Prefabs/MuzzleFlashStarburst.prefab Assets/VFX/Prefabs/MuzzleFlashStarburst.prefab.meta Assets/VFX/Prefabs/MuzzleFlashDisc.prefab Assets/VFX/Prefabs/MuzzleFlashDisc.prefab.meta
git commit -m "vfx-b2: generate muzzle flash prefabs (Starburst + Disc)

MuzzleFlashStarburst.prefab — single-particle 0.06 s warm yellow-white
burst with the procedural starburst sprite, stopAction Destroy.
MuzzleFlashDisc.prefab — single-particle 0.10 s warm orange/yellow
soft disc puff with Glow_64, slight outward expansion via startSpeed
0.5 + expanding sizeOverLifetime curve. Both Billboard, additive,
unconditionally regenerated each installer run.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 10: VfxAssetsInstaller — add bullet impact prefab generators (Spark + Dust)

**Files:**
- Modify: `Assets/Scripts/Editor/VfxAssetsInstaller.cs`

- [ ] **Step 1: Add path constants:**

```csharp
        const string BulletImpactSparkPrefabPath = PrefabsDir + "/BulletImpactSpark.prefab";
        const string BulletImpactDustPrefabPath  = PrefabsDir + "/BulletImpactDust.prefab";
```

- [ ] **Step 2: Add Spark prefab generator** (2-layer: core single-particle + sparks sub-emitter). Append:

```csharp

        // Bullet impact spark. Two-layer prefab:
        //  Layer 1 (root)  — small bright core, single billboard, white
        //                     starburst at quarter scale of the muzzle.
        //  Layer 2 (child) — 6 radial spark streaks flying outward,
        //                     stretched billboards velocity-aligned.
        // The whole prefab is instantiated with Quaternion.LookRotation(
        // hit.normal) by ProjectileHit.SpawnImpactVfx, so the child
        // hemisphere fires OUTWARD from the surface.
        static void EnsureBulletImpactSparkPrefab(Material sparkMat)
        {
            GameObject root = new GameObject("BulletImpactSpark");
            try
            {
                // Layer 1 — core
                ParticleSystem ps = root.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.duration = 0.10f;
                main.loop = false;
                main.startLifetime = 0.08f;
                main.startSpeed = 0f;
                main.startSize = 0.10f;
                main.startColor = new Color(1f, 0.96f, 0.75f, 1f) * 3f;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.maxParticles = 3;
                main.playOnAwake = true;
                main.stopAction = ParticleSystemStopAction.Destroy;

                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.02f;

                var sz = ps.sizeOverLifetime;
                sz.enabled = true;
                AnimationCurve szCurve = new AnimationCurve(
                    new Keyframe(0f, 1f), new Keyframe(1f, 0.5f));
                sz.size = new ParticleSystem.MinMaxCurve(1f, szCurve);

                var col = ps.colorOverLifetime;
                col.enabled = true;
                Gradient gCore = new Gradient();
                gCore.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.10f), new GradientAlphaKey(0f, 1f) });
                col.color = new ParticleSystem.MinMaxGradient(gCore);

                var renderer = root.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.sharedMaterial = sparkMat;

                // Layer 2 — sparks (child)
                GameObject sparksGo = new GameObject("Sparks");
                sparksGo.transform.SetParent(root.transform, false);
                ParticleSystem sparksPs = sparksGo.AddComponent<ParticleSystem>();
                sparksPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                var sm = sparksPs.main;
                sm.duration = 0.10f;
                sm.loop = false;
                sm.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.18f);
                sm.startSpeed = 2f;
                sm.startSize = 0.04f;
                sm.startColor = new Color(1f, 0.96f, 0.70f, 1f) * 2.5f;
                sm.simulationSpace = ParticleSystemSimulationSpace.World;
                sm.maxParticles = 8;
                sm.playOnAwake = true;
                sm.stopAction = ParticleSystemStopAction.None;

                var se = sparksPs.emission;
                se.enabled = true;
                se.rateOverTime = 0f;
                se.SetBursts(new[] { new ParticleSystem.Burst(0f, 6) });

                var sShape = sparksPs.shape;
                sShape.enabled = true;
                sShape.shapeType = ParticleSystemShapeType.Hemisphere;
                sShape.radius = 0.02f;

                var ssz = sparksPs.sizeOverLifetime;
                ssz.enabled = true;
                AnimationCurve sparkSizeCurve = new AnimationCurve(
                    new Keyframe(0f, 1f), new Keyframe(1f, 0.2f));
                ssz.size = new ParticleSystem.MinMaxCurve(1f, sparkSizeCurve);

                var scol = sparksPs.colorOverLifetime;
                scol.enabled = true;
                Gradient gSparks = new Gradient();
                gSparks.SetKeys(
                    new[]
                    {
                        new GradientColorKey(Color.white, 0f),
                        new GradientColorKey(new Color(1f, 0.4f, 0.75f), 1f),
                    },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                scol.color = new ParticleSystem.MinMaxGradient(gSparks);

                var sRenderer = sparksGo.GetComponent<ParticleSystemRenderer>();
                sRenderer.renderMode = ParticleSystemRenderMode.Stretch;
                sRenderer.lengthScale = 0f;
                sRenderer.velocityScale = 0.4f;
                sRenderer.sharedMaterial = sparkMat;

                PrefabUtility.SaveAsPrefabAsset(root, BulletImpactSparkPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
```

- [ ] **Step 3: Add Dust prefab generator** — append:

```csharp

        // Bullet impact ground dust. Single-layer warm tan puff cluster.
        // Alpha-blended (not additive) so it darkens/soft-overlays
        // instead of glowing. Hemisphere shape aligned with hit.normal
        // at spawn time.
        static void EnsureBulletImpactDustPrefab(Material dustMat)
        {
            GameObject root = new GameObject("BulletImpactDust");
            try
            {
                ParticleSystem ps = root.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                var main = ps.main;
                main.duration = 0.30f;
                main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.40f);
                main.startSpeed = 0.8f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.25f);
                main.startColor = new Color(0.92f, 0.82f, 0.60f, 1f);
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 8;
                main.playOnAwake = true;
                main.stopAction = ParticleSystemStopAction.Destroy;

                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 5) });

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Hemisphere;
                shape.radius = 0.06f;

                var sz = ps.sizeOverLifetime;
                sz.enabled = true;
                AnimationCurve szCurve = new AnimationCurve(
                    new Keyframe(0f, 0.6f), new Keyframe(1f, 1.6f));
                sz.size = new ParticleSystem.MinMaxCurve(1f, szCurve);

                var col = ps.colorOverLifetime;
                col.enabled = true;
                Gradient g = new Gradient();
                g.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.7f, 0.10f), new GradientAlphaKey(0f, 1f) });
                col.color = new ParticleSystem.MinMaxGradient(g);

                var renderer = root.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.sharedMaterial = dustMat;

                PrefabUtility.SaveAsPrefabAsset(root, BulletImpactDustPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
```

- [ ] **Step 4: Call both from `Apply()`** — after the muzzle prefab calls (from Task 9):

```csharp
            EnsureBulletImpactSparkPrefab(muzzleStarburst);   // reuses MuzzleStarburstMat
            EnsureBulletImpactDustPrefab(bulletImpactDust);
```

- [ ] **Step 5: Extend the final log line** to include the two new prefabs (append before the closing `).`):

```
..., BulletImpactSpark.prefab, BulletImpactDust.prefab
```

- [ ] **Step 6: Refresh & verify clean compile, then run installer.**

- [ ] **Step 7: Verify prefabs on disk**

```bash
ls -la "/Users/anon/My project/Assets/VFX/Prefabs/"
```

Expected: 4 existing/new + `BulletImpactSpark.prefab` + `BulletImpactDust.prefab`.

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/Editor/VfxAssetsInstaller.cs Assets/VFX/Prefabs/BulletImpactSpark.prefab Assets/VFX/Prefabs/BulletImpactSpark.prefab.meta Assets/VFX/Prefabs/BulletImpactDust.prefab Assets/VFX/Prefabs/BulletImpactDust.prefab.meta
git commit -m "vfx-b2: generate bullet impact prefabs (Spark + Dust)

BulletImpactSpark.prefab — 2-layer: core (small white starburst) +
sparks child (6 radial stretched billboards from hemisphere, color-
shifts white→hot-pink over lifetime). Root stopAction Destroy cleans
the whole prefab. Reuses MuzzleStarburstMat for both layers.

BulletImpactDust.prefab — single-layer 5-puff warm tan cluster from
hemisphere aligned with hit.normal, alpha-blended (BulletImpactDustMat),
0.6→1.6 growing size, 0.25–0.40 s random lifetime.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 11: VfxAssetsInstaller — add rocket VFX prefab generators (Exhaust Plume + Smoke Puff)

**Files:**
- Modify: `Assets/Scripts/Editor/VfxAssetsInstaller.cs`

- [ ] **Step 1: Add path constants:**

```csharp
        const string RocketExhaustPlumePrefabPath = PrefabsDir + "/RocketExhaustPlume.prefab";
        const string RocketSmokePuffPrefabPath    = PrefabsDir + "/RocketSmokePuff.prefab";
```

- [ ] **Step 2: Add Rocket exhaust plume generator** — append:

```csharp

        // Rocket exhaust plume. Continuous looping ParticleSystem, warm
        // yellow-orange HDR. Mirrors EnginePlume.prefab structure (Stretch
        // billboard, cone shape, world simulation) minus the ShockDiamond
        // child — no boost concept for rockets. Instantiated as a child
        // of Rocket; Rocket.OnDestroy detaches + Stop(KeepParticles) so
        // alive particles finish naturally.
        static void EnsureRocketExhaustPlumePrefab(Material exhaustMat)
        {
            GameObject root = new GameObject("RocketExhaustPlume");
            try
            {
                ParticleSystem ps = root.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                var main = ps.main;
                main.duration = 5f;
                main.loop = true;
                main.startLifetime = 0.18f;
                main.startSpeed = 4f;
                main.startSize = 0.15f;
                main.startColor = new Color(1f, 0.70f, 0.30f, 1f) * 2.5f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 60;
                main.playOnAwake = true;
                main.stopAction = ParticleSystemStopAction.Destroy;

                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 35f;

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 6f;
                shape.radius = 0.04f;

                var sz = ps.sizeOverLifetime;
                sz.enabled = true;
                AnimationCurve szCurve = new AnimationCurve(
                    new Keyframe(0f, 0.6f), new Keyframe(0.4f, 1.0f), new Keyframe(1f, 0.3f));
                sz.size = new ParticleSystem.MinMaxCurve(1f, szCurve);

                var col = ps.colorOverLifetime;
                col.enabled = true;
                Gradient g = new Gradient();
                g.SetKeys(
                    new[]
                    {
                        new GradientColorKey(Color.white, 0f),
                        new GradientColorKey(new Color(1f, 0.45f, 0.10f), 1f),
                    },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
                col.color = new ParticleSystem.MinMaxGradient(g);

                var renderer = root.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.lengthScale = 0f;
                renderer.velocityScale = 1.2f;
                renderer.sharedMaterial = exhaustMat;

                PrefabUtility.SaveAsPrefabAsset(root, RocketExhaustPlumePrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
```

- [ ] **Step 3: Add Rocket smoke puff generator** — append:

```csharp

        // Rocket smoke puff. Continuous looping ParticleSystem of soft
        // white discrete clouds. Reuses RcsPuffMat from B-1 (no new
        // material). 10x growth over 1.5 s lifetime per user spec —
        // start small (0.1), grow large (1.0), alpha-fade throughout.
        static void EnsureRocketSmokePuffPrefab(Material puffMat)
        {
            GameObject root = new GameObject("RocketSmokePuff");
            try
            {
                ParticleSystem ps = root.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                var main = ps.main;
                main.duration = 5f;
                main.loop = true;
                main.startLifetime = 1.5f;
                main.startSpeed = 0.3f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.10f);
                main.startColor = new Color(1f, 1f, 1f, 0.85f);
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 80;
                main.playOnAwake = true;
                main.stopAction = ParticleSystemStopAction.Destroy;

                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 15f;

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 5f;
                shape.radius = 0.06f;

                var sz = ps.sizeOverLifetime;
                sz.enabled = true;
                AnimationCurve szCurve = new AnimationCurve(
                    new Keyframe(0f, 0.1f), new Keyframe(1f, 1f));
                sz.size = new ParticleSystem.MinMaxCurve(1f, szCurve);

                var col = ps.colorOverLifetime;
                col.enabled = true;
                Gradient g = new Gradient();
                g.SetKeys(
                    new[]
                    {
                        new GradientColorKey(Color.white, 0f),
                        new GradientColorKey(new Color(0.92f, 0.95f, 1f), 1f),
                    },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.10f), new GradientAlphaKey(0f, 1f) });
                col.color = new ParticleSystem.MinMaxGradient(g);

                var renderer = root.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.sharedMaterial = puffMat;

                PrefabUtility.SaveAsPrefabAsset(root, RocketSmokePuffPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
```

- [ ] **Step 4: Call both from `Apply()`:**

```csharp
            EnsureRocketExhaustPlumePrefab(rocketExhaust);
            EnsureRocketSmokePuffPrefab(rcsPuff);  // reuses RcsPuffMat from B-1
```

- [ ] **Step 5: Extend the final log line** to include the two new prefabs.

- [ ] **Step 6: Refresh & verify clean compile, then run installer.**

- [ ] **Step 7: Verify prefabs on disk.**

```bash
ls -la "/Users/anon/My project/Assets/VFX/Prefabs/"
```

Expected: `RocketExhaustPlume.prefab` and `RocketSmokePuff.prefab` present.

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/Editor/VfxAssetsInstaller.cs Assets/VFX/Prefabs/RocketExhaustPlume.prefab Assets/VFX/Prefabs/RocketExhaustPlume.prefab.meta Assets/VFX/Prefabs/RocketSmokePuff.prefab Assets/VFX/Prefabs/RocketSmokePuff.prefab.meta
git commit -m "vfx-b2: generate rocket VFX prefabs (Exhaust + SmokePuff)

RocketExhaustPlume.prefab — continuous Stretch billboard plume,
warm yellow-orange HDR x2.5, mirrors EnginePlume.prefab structure
minus ShockDiamond child (no boost for rockets). 35 rate, 0.18s
lifetime, 60 max particles.

RocketSmokePuff.prefab — continuous Billboard puffs, soft white,
0.1->1.0 size-over-lifetime (10x growth per user spec), 1.5s
lifetime, 15 rate, reuses RcsPuffMat from B-1 (no new material).

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 12: VfxAssetsInstaller — wire Bullet.prefab + Rocket.prefab via SerializedObject

**Files:**
- Modify: `Assets/Scripts/Editor/VfxAssetsInstaller.cs`

This is the trickiest installer task — it patches existing projectile prefabs to set new SerializeField references. Required because `Bullet.cs` and `Rocket.cs` will gain new `[SerializeField]` fields (in Phase 2 tasks), and the projectile prefabs need those references populated for the runtime to find them.

- [ ] **Step 1: Add path constants** alongside other paths:

```csharp
        const string BulletPrefabPath = "Assets/Prefabs/Projectiles/Bullet.prefab";
        const string RocketPrefabPath = "Assets/Prefabs/Projectiles/Rocket.prefab";
```

- [ ] **Step 2: Add `WireBulletPrefab` method** — append:

```csharp

        // Patches Bullet.prefab to set the tracerMaterial SerializeField
        // reference added in Phase B-2. Uses PrefabUtility.LoadPrefabContents
        // + SerializedObject so the prefab on disk gets the reference,
        // surviving a fresh clone of the repo. Idempotent — re-running
        // sets the same reference again.
        //
        // Bullet must have a [SerializeField] Material tracerMaterial
        // field at this point (added in the per-projectile wiring task).
        // If the field hasn't been added yet, SerializedObject.FindProperty
        // returns null and this no-ops with a warning — safe.
        static void WireBulletPrefab(Material tracerMat)
        {
            if (!File.Exists(BulletPrefabPath))
            {
                Debug.unityLogger.LogWarning("VfxAssetsInstaller",
                    $"{BulletPrefabPath} not found; skipping bullet prefab wiring.");
                return;
            }
            GameObject instance = PrefabUtility.LoadPrefabContents(BulletPrefabPath);
            try
            {
                var bullet = instance.GetComponent<CubeFly.Fly.Bullet>();
                if (bullet == null)
                {
                    Debug.unityLogger.LogWarning("VfxAssetsInstaller",
                        $"{BulletPrefabPath} has no Bullet component; skipping wiring.");
                    return;
                }
                var so = new SerializedObject(bullet);
                var prop = so.FindProperty("tracerMaterial");
                if (prop == null)
                {
                    Debug.unityLogger.LogWarning("VfxAssetsInstaller",
                        "Bullet has no 'tracerMaterial' SerializeField yet; " +
                        "wiring deferred until that field is added.");
                    return;
                }
                prop.objectReferenceValue = tracerMat;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(instance, BulletPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(instance);
            }
        }
```

- [ ] **Step 3: Add `WireRocketPrefab` method** — append:

```csharp

        // Patches Rocket.prefab to set the exhaustPlumePrefab,
        // smokePuffPrefab, and smokeTrailMaterial SerializeField references
        // added in Phase B-2. Same idempotent SerializedObject path as
        // WireBulletPrefab.
        static void WireRocketPrefab(
            GameObject exhaustPlumePrefab, GameObject smokePuffPrefab, Material smokeTrailMat)
        {
            if (!File.Exists(RocketPrefabPath))
            {
                Debug.unityLogger.LogWarning("VfxAssetsInstaller",
                    $"{RocketPrefabPath} not found; skipping rocket prefab wiring.");
                return;
            }
            GameObject instance = PrefabUtility.LoadPrefabContents(RocketPrefabPath);
            try
            {
                var rocket = instance.GetComponent<CubeFly.Fly.Rocket>();
                if (rocket == null)
                {
                    Debug.unityLogger.LogWarning("VfxAssetsInstaller",
                        $"{RocketPrefabPath} has no Rocket component; skipping wiring.");
                    return;
                }
                var so = new SerializedObject(rocket);
                bool anySet = false;

                var pPlume = so.FindProperty("exhaustPlumePrefab");
                if (pPlume != null) { pPlume.objectReferenceValue = exhaustPlumePrefab; anySet = true; }

                var pPuff = so.FindProperty("smokePuffPrefab");
                if (pPuff != null) { pPuff.objectReferenceValue = smokePuffPrefab; anySet = true; }

                var pTrail = so.FindProperty("smokeTrailMaterial");
                if (pTrail != null) { pTrail.objectReferenceValue = smokeTrailMat; anySet = true; }

                if (!anySet)
                {
                    Debug.unityLogger.LogWarning("VfxAssetsInstaller",
                        "Rocket has no B-2 SerializeFields yet; wiring deferred " +
                        "until exhaustPlumePrefab/smokePuffPrefab/smokeTrailMaterial are added.");
                    return;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(instance, RocketPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(instance);
            }
        }
```

- [ ] **Step 4: Get the prefab references in `Apply()`** — the prefab generators return `void` currently (they save to disk and discard the root). We need the prefab assets to pass as references. After `EnsureRocketSmokePuffPrefab(rcsPuff);` (from Task 11), add:

```csharp

            // Load the just-generated prefabs as assets for wiring.
            GameObject exhaustPlumeAsset = AssetDatabase.LoadAssetAtPath<GameObject>(RocketExhaustPlumePrefabPath);
            GameObject smokePuffAsset    = AssetDatabase.LoadAssetAtPath<GameObject>(RocketSmokePuffPrefabPath);

            WireBulletPrefab(bulletTracer);
            WireRocketPrefab(exhaustPlumeAsset, smokePuffAsset, rocketSmokeTrail);
```

- [ ] **Step 5: Refresh & verify clean compile.**

At this point the wirer will log warnings because `Bullet` / `Rocket` don't yet have the new SerializeFields. That's expected — Phase 2 tasks (16, 18–20) add those fields, and re-running the installer afterward populates them. Don't be alarmed by the warnings now.

- [ ] **Step 6: Run installer**

```
mcp__unityMCP__execute_menu_item(menu_path="Tools/CubeFly/Generate VFX assets")
```

Expected console messages: log line about applied assets + 2 warnings ("Bullet has no 'tracerMaterial'..." and "Rocket has no B-2 SerializeFields..."). The warnings will go away after Phase 2 lands.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Editor/VfxAssetsInstaller.cs
git commit -m "vfx-b2: add WireBulletPrefab + WireRocketPrefab installer steps

PrefabUtility.LoadPrefabContents + SerializedObject pattern to set
the new VFX SerializeField references on Bullet.prefab and
Rocket.prefab idempotently. Survives fresh clones — re-running the
installer restores wiring after manual unwire.

The new SerializeFields on Bullet/Rocket are added in subsequent
commits; until then the wirers log a 'deferred' warning and no-op.
Re-running the installer after the runtime tasks populates the
prefab references.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Phase 2 — Runtime wiring (effects become visible)

### Task 13: PyramidWeapon — call PlayMuzzleVfx in Fire

**Files:**
- Modify: `Assets/Scripts/Fly/PyramidWeapon.cs`

- [ ] **Step 1: Add the `using CubeFly.Core;` import** if not already present. Check the top of the file. If only `using UnityEngine;` is there (line 1), add:

```csharp
using CubeFly.Core;
using UnityEngine;
```

- [ ] **Step 2: Call `PlayMuzzleVfx` at the end of `Fire`** — after the `Bullet.Launch(...)` call (around line 53), but before the `else` branch's fallback path. The muzzle should fire when a real bullet launches, not on the defensive fallback.

Find the inner `if (bullet != null) { ... bullet.Launch(...); }` block (around lines 48–54). Add the muzzle call just inside, immediately after `bullet.Launch(...)`:

```csharp
            if (bullet != null)
            {
                // Pass Construct + damage so the bullet can run self-hit
                // prevention and apply the weapon's damage value on hit.
                // The bullet snapshots both at Launch and never re-queries.
                bullet.Launch(tipPos, fireDir, Construct, damage);
                PlayMuzzleVfx(tipPos, Quaternion.LookRotation(fireDir),
                    VfxSettings.MuzzleFlashPyramid);
            }
```

- [ ] **Step 3: Refresh & verify clean compile.**

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Fly/PyramidWeapon.cs
git commit -m "vfx-b2: spawn muzzle flash from PyramidWeapon.Fire

After Bullet.Launch, call PlayMuzzleVfx(tipPos, LookRotation(fireDir),
VfxSettings.MuzzleFlashPyramid). Toggle-guarded inside the base hook;
null-guarded against unwired muzzlePrefab. No visible effect yet —
FlyController wiring in a subsequent commit will populate the prefab
reference.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 14: CylinderWeapon — call PlayMuzzleVfx in Fire

**Files:**
- Modify: `Assets/Scripts/Fly/CylinderWeapon.cs`

- [ ] **Step 1: Add `using CubeFly.Core;` import** if not already present. (Check top of file.)

```csharp
using CubeFly.Core;
using UnityEngine;
```

- [ ] **Step 2: Call `PlayMuzzleVfx` at the end of `Fire`** — after `rocket.Launch(...)` (around line 41). The muzzle anchors at `exitPos` (the open barrel end), not `spawnPos` (cylinder centre where the rocket internally spawns):

Find the inner `if (rocket != null) { ... rocket.Launch(...); }` block (around lines 36–43). Add the muzzle call immediately after `rocket.Launch(...)`:

```csharp
            if (rocket != null)
            {
                // Pass Construct + damage so the rocket can run self-hit
                // prevention (in both exit and seek phases) and apply the
                // weapon's damage value on hit. Snapshotted at Launch.
                rocket.Launch(spawnPos, launchDir, exitPos, crosshairWorldTarget,
                    Construct, damage);
                PlayMuzzleVfx(exitPos, Quaternion.LookRotation(launchDir),
                    VfxSettings.MuzzleFlashCylinder);
            }
```

- [ ] **Step 3: Refresh & verify clean compile.**

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Fly/CylinderWeapon.cs
git commit -m "vfx-b2: spawn muzzle flash from CylinderWeapon.Fire

After Rocket.Launch, call PlayMuzzleVfx(exitPos, LookRotation(launchDir),
VfxSettings.MuzzleFlashCylinder). Muzzle anchors at the open barrel
end (exitPos = spawnPos + launchDir * launchExitDistance), not the
cylinder centre — that's where the rocket emerges and where the flash
should visually originate.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 15: FlyController — wire muzzle prefabs + ConfigureImpactPrefabs

**Files:**
- Modify: `Assets/Scripts/Fly/FlyController.cs`

- [ ] **Step 1: Add 4 new SerializeField fields** alongside the existing VFX prefab fields (around lines 131–134, near `enginePlumePrefab` and `rcsPuffPrefab`):

```csharp
        [Tooltip("MuzzleFlashStarburst.prefab (Assets/VFX/Prefabs/). Wired to each PyramidWeapon at BuildConstruct time. If null, no Pyramid muzzle VFX fires.")]
        [SerializeField] GameObject muzzleFlashStarburstPrefab;
        [Tooltip("MuzzleFlashDisc.prefab (Assets/VFX/Prefabs/). Wired to each CylinderWeapon at BuildConstruct time. If null, no Cylinder muzzle VFX fires.")]
        [SerializeField] GameObject muzzleFlashDiscPrefab;
        [Tooltip("BulletImpactSpark.prefab (Assets/VFX/Prefabs/). Passed to ProjectileHit.ConfigureImpactPrefabs once in Awake. If null, no spark VFX fires.")]
        [SerializeField] GameObject bulletImpactSparkPrefab;
        [Tooltip("BulletImpactDust.prefab (Assets/VFX/Prefabs/). Passed to ProjectileHit.ConfigureImpactPrefabs once in Awake. If null, no dust VFX fires (even on upward hits).")]
        [SerializeField] GameObject bulletImpactDustPrefab;
```

- [ ] **Step 2: Add the `Awake` impact-prefab configuration** — find FlyController's existing `Awake` method (search for `void Awake()`). If it doesn't exist, add one near the top of the class (after the SerializeField fields, before any other method).

If it exists, add this as the first statement in Awake:
```csharp
            ProjectileHit.ConfigureImpactPrefabs(bulletImpactSparkPrefab, bulletImpactDustPrefab);
```

If it doesn't exist:
```csharp
        void Awake()
        {
            // Configure impact prefabs before any projectile can spawn
            // (projectiles can't spawn until BuildConstruct runs in Start;
            // Awake is one phase earlier, so refs are always set in time).
            ProjectileHit.ConfigureImpactPrefabs(bulletImpactSparkPrefab, bulletImpactDustPrefab);
        }
```

- [ ] **Step 3: Wire the muzzle prefab in `BuildConstruct`** — find the per-cube loop in `BuildConstruct` (around line 489, where `ThrusterVfx` is added). Find the existing `if (enginePlumePrefab != null) { ... }` block.

Locate where weapon components are accessed in that same loop. (The existing code likely does `var weapon = go.GetComponent<WeaponBehavior>();` somewhere or branches on cube type.) Where the weapon is identified, add a type-switch + muzzle wiring:

```csharp
                    WeaponBehavior weapon = go.GetComponent<WeaponBehavior>();
                    if (weapon != null)
                    {
                        if (weapon is PyramidWeapon && muzzleFlashStarburstPrefab != null)
                            weapon.SetMuzzlePrefab(muzzleFlashStarburstPrefab);
                        else if (weapon is CylinderWeapon && muzzleFlashDiscPrefab != null)
                            weapon.SetMuzzlePrefab(muzzleFlashDiscPrefab);
                    }
```

If the existing code already retrieves the weapon component for other reasons (e.g. collecting `_weapons.Add(weapon)`), reuse that local instead of re-fetching. The exact insertion is "after the weapon component is in hand, before the loop iteration ends."

- [ ] **Step 4: Refresh & verify clean compile.**

- [ ] **Step 5: Wire scene-instance fields in Unity Editor** — open `FlyScene.unity` in Unity, select the FlyController GameObject in the Hierarchy, find the new 4 fields in the Inspector. Drag-assign:
- `muzzleFlashStarburstPrefab` ← `Assets/VFX/Prefabs/MuzzleFlashStarburst.prefab`
- `muzzleFlashDiscPrefab` ← `Assets/VFX/Prefabs/MuzzleFlashDisc.prefab`
- `bulletImpactSparkPrefab` ← `Assets/VFX/Prefabs/BulletImpactSpark.prefab`
- `bulletImpactDustPrefab` ← `Assets/VFX/Prefabs/BulletImpactDust.prefab`

Save the scene (`Ctrl/Cmd+S`).

- [ ] **Step 6: Play-test muzzle flashes**

Press Play in Unity. Build or pick a slot with a Pyramid + Cylinder weapon. Fire each:
- Pyramid: warm yellow-white starburst at the tip per shot.
- Cylinder: warm orange/yellow disc puff at the barrel end per shot.

If a flash doesn't appear, check:
- Console for warnings.
- Field references on FlyController in Inspector.
- The prefab itself opens cleanly with the right material.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Fly/FlyController.cs Assets/Scenes/FlyScene.unity
git commit -m "vfx-b2: wire muzzle + impact prefab refs into FlyController

Adds 4 SerializeField GameObject fields (muzzleFlashStarburstPrefab,
muzzleFlashDiscPrefab, bulletImpactSparkPrefab, bulletImpactDustPrefab)
all assigned in FlyScene inspector.

Awake calls ProjectileHit.ConfigureImpactPrefabs(spark, dust) before
any projectile can spawn. BuildConstruct type-switches on
PyramidWeapon/CylinderWeapon and calls SetMuzzlePrefab with the right
prefab (mirrors the existing ThrusterVfx.SetPlumePrefab wiring
pattern). Null-guarded on each prefab field independently.

Muzzle flashes now visible on Pyramid + Cylinder fire. Impact effects
not yet wired into Bullet/Rocket (next commits).

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 16: Bullet — add tracer + polling + impact spawn + LingeringTrail

**Files:**
- Modify: `Assets/Scripts/Fly/Bullet.cs`

This task bundles three small but related Bullet.cs changes: tracer setup (TrailRenderer + LingeringTrail), per-frame polling for the tracer toggle, and the impact-spawn call after `ApplyAndLog`. All three touch the same file in three places.

- [ ] **Step 1: Add `using CubeFly.Core;`** at the top if not present:

```csharp
using CubeFly.Core;
using UnityEngine;
```

- [ ] **Step 2: Add tracer SerializeField + private state** — at the top of the class, after the existing `[SerializeField] float maxRange = 200f;` (line 25):

```csharp
        [Tooltip("BulletTracerMat.mat (Assets/VFX/Materials/). Wired by VfxAssetsInstaller. If null, no tracer is attached.")]
        [SerializeField] Material tracerMaterial;
```

And among the existing private fields (around lines 27–35), add:
```csharp
        TrailRenderer _trail;
        LingeringTrail _lingeringTrail;
```

- [ ] **Step 3: Add `Awake` method** — Bullet currently has no Awake. Add one above `Update` (around line 70):

```csharp
        void Awake()
        {
            // Tracer setup. Toggle-gated + null-guarded. If either fails
            // at Awake, no TrailRenderer is added — flipping the toggle
            // ON later won't retroactively add one (new bullets get
            // tracers, existing don't), which is the intended behaviour.
            //
            // The TrailRenderer lives on a dedicated CHILD GameObject so
            // OnDestroy can detach the child before Unity destroys the
            // bullet's hierarchy — the orphan child then fades naturally
            // per TrailRenderer.time and autodestructs. Hosting the
            // TrailRenderer on the bullet itself would defeat the
            // detach pattern: SetParent(null) on the root being destroyed
            // doesn't preserve it from destruction.
            if (VfxSettings.BulletTracer && tracerMaterial != null)
            {
                GameObject trailGo = new GameObject("Tracer");
                trailGo.transform.SetParent(transform, false);
                trailGo.transform.localPosition = Vector3.zero;
                trailGo.transform.localRotation = Quaternion.identity;

                _trail = trailGo.AddComponent<TrailRenderer>();
                // 0.15 s lifetime — halved from the original 0.30 after
                // play-test feedback that the longer trail felt overly
                // smeared at the bullet's 80 u/s speed.
                _trail.time = 0.15f;
                _trail.startWidth = 0.05f;
                _trail.endWidth = 0.02f;
                _trail.minVertexDistance = 0.10f;
                // sharedMaterial avoids per-bullet material instantiation
                // (TrailRenderer.material clones the asset for write-
                // isolation, allocating + leaking a Material per
                // projectile). Matches LaserWeapon's LineRenderer pattern.
                _trail.sharedMaterial = tracerMaterial;
                _trail.emitting = true;

                Gradient grad = new Gradient();
                grad.SetKeys(
                    new[]
                    {
                        new GradientColorKey(new Color(1.00f, 1.00f, 1.00f), 0f),
                        new GradientColorKey(new Color(1.00f, 0.96f, 0.70f), 0.5f),
                        new GradientColorKey(new Color(1.00f, 0.40f, 0.75f), 1f),
                    },
                    new[]
                    {
                        new GradientAlphaKey(1.00f, 0f),
                        new GradientAlphaKey(0.85f, 0.5f),
                        new GradientAlphaKey(0.00f, 1f),
                    });
                _trail.colorGradient = grad;

                _lingeringTrail = trailGo.AddComponent<LingeringTrail>();
            }
        }
```

- [ ] **Step 4: Poll the tracer toggle in `Update`** — at the top of `Update`, before the `if (!_armed) return;` line (around line 72):

```csharp
            // Poll BulletTracer toggle each frame for live Debug-tab A/B.
            if (_trail != null) _trail.emitting = VfxSettings.BulletTracer;
```

- [ ] **Step 5: Call `SpawnImpactVfx` after `ApplyAndLog`** — find the existing line (around 80):

```csharp
                ProjectileHit.ApplyAndLog(hit, _damage, _firingConstruct, TAG);
                Destroy(gameObject);
```

Insert between them:
```csharp
                ProjectileHit.ApplyAndLog(hit, _damage, _firingConstruct, TAG);
                ProjectileHit.SpawnImpactVfx(hit);
                Destroy(gameObject);
```

- [ ] **Step 6: Add `OnDestroy` for LingeringTrail detach** — at the end of the class, just before the closing `}`:

```csharp

        void OnDestroy()
        {
            // Detach trail before Unity destroys the hierarchy so the
            // remaining trail segments fade per TrailRenderer.time instead
            // of vanishing with the bullet.
            if (_lingeringTrail != null) _lingeringTrail.DetachAndFade();
        }
```

- [ ] **Step 7: Refresh & verify clean compile.**

- [ ] **Step 8: Re-run installer to populate `tracerMaterial` on Bullet.prefab** — now that the SerializeField exists, the WireBulletPrefab call (from Task 12) can find and set it:

```
mcp__unityMCP__execute_menu_item(menu_path="Tools/CubeFly/Generate VFX assets")
```

Check console: the previous "Bullet has no 'tracerMaterial'..." warning should no longer appear; instead the installer silently wires the reference into Bullet.prefab.

- [ ] **Step 9: Verify Bullet.prefab wiring** — in Unity, open `Assets/Prefabs/Projectiles/Bullet.prefab` and confirm `tracerMaterial` shows `BulletTracerMat` in the inspector.

- [ ] **Step 10: Play-test bullet tracer + impact**

- Fire a Pyramid weapon. Bullets leave yellow-white-to-pink TrailRenderer trails that fade behind them.
- Hit a target with a bullet. A bright warm spark appears at the impact point. Hit the top of a target (if accessible) — a soft tan dust puff also appears.
- During sustained fire: open Settings → Debug, toggle "Bullet tracer" OFF — new trail vertices stop appearing on existing bullets within one frame. Toggle back ON — emission resumes.
- Toggle "Bullet impact spark" OFF — next hit shows no spark. Toggle dust off — no dust on upward hits. Independent control.

- [ ] **Step 11: Commit**

```bash
git add Assets/Scripts/Fly/Bullet.cs Assets/Prefabs/Projectiles/Bullet.prefab
git commit -m "vfx-b2: wire bullet tracer + impact effects in Bullet

- [SerializeField] Material tracerMaterial (wired by installer).
- Awake adds TrailRenderer (0.30s time, warm-to-pink colorGradient)
  + LingeringTrail when toggle is on AND material is non-null.
- Update polls VfxSettings.BulletTracer each frame to support live
  Debug-tab A/B (sets _trail.emitting).
- After ApplyAndLog(hit, ...), calls ProjectileHit.SpawnImpactVfx(hit)
  before Destroying. Both toggles + null-guards inside the helper.
- OnDestroy explicitly calls _lingeringTrail.DetachAndFade() so the
  trail survives long enough to fade per its time config.

Re-runs installer to populate the new tracerMaterial reference on
Bullet.prefab via the WireBulletPrefab SerializedObject path.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 17: Rocket — call SpawnImpactVfx after ApplyAndLog

**Files:**
- Modify: `Assets/Scripts/Fly/Rocket.cs`

Smallest of the Rocket tasks — one line added at the existing hit dispatch site. Bundles the symmetric Bullet change from Task 16 for the rocket projectile.

- [ ] **Step 1: Add `using CubeFly.Core;`** at the top if not present.

```csharp
using CubeFly.Core;
using UnityEngine;
```

- [ ] **Step 2: Call `SpawnImpactVfx` after `ApplyAndLog`** — find around line 91 (drifted from 81 after PR #49's MeshAlignment block landed in main):

```csharp
                ProjectileHit.ApplyAndLog(hit, _damage, _firingConstruct, TAG);
                Destroy(gameObject);
```

Insert between them (passing scale=1.20 so the rocket's impact reads ~20% bigger than the bullet's):
```csharp
                ProjectileHit.ApplyAndLog(hit, _damage, _firingConstruct, TAG);
                ProjectileHit.SpawnImpactVfx(hit, scale: 1.20f);
                Destroy(gameObject);
```

- [ ] **Step 3: Refresh & verify clean compile.**

- [ ] **Step 4: Play-test rocket impact** — fire a rocket at a target. On hit, a bright warm spark + (if hit was top-facing) a tan dust puff should appear, both ~10% larger than what bullets produce. No tracer / smoke yet — those are next tasks.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Fly/Rocket.cs
git commit -m "vfx-b2: spawn impact VFX on Rocket hit

After ApplyAndLog(hit, ...) and before Destroy, call
ProjectileHit.SpawnImpactVfx(hit). Same toggle/null-guarded dispatch
as Bullet — spark on any hit, dust additionally when Dot(normal, up)
> 0.7. Rocket and Bullet share impact behaviour by design (both are
'projectile hits a thing').

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 18: Rocket — exhaust plume + smoke puff child instantiation

**Files:**
- Modify: `Assets/Scripts/Fly/Rocket.cs`

- [ ] **Step 1: Add 2 SerializeField fields + private state** — at the top of the class, after the existing `[SerializeField] float maxRange = 200f;` (line 24):

```csharp
        [Tooltip("RocketExhaustPlume.prefab (Assets/VFX/Prefabs/). Wired by VfxAssetsInstaller. Instantiated as a child of the rocket at Awake if VfxRocketExhaust is on.")]
        [SerializeField] GameObject exhaustPlumePrefab;
        [Tooltip("RocketSmokePuff.prefab (Assets/VFX/Prefabs/). Wired by VfxAssetsInstaller. Instantiated as a child of the rocket at Awake if VfxRocketSmokePuff is on.")]
        [SerializeField] GameObject smokePuffPrefab;
```

Among the private fields (after `_armed` field, around line 33), add:
```csharp
        ParticleSystem _exhaustPlumePs;
        ParticleSystem _smokePuffPs;
```

- [ ] **Step 2: Add `Awake` method** — Rocket currently has no Awake. Add one above `Update`:

```csharp
        void Awake()
        {
            // Exhaust plume child — instantiated only if toggle on AND
            // prefab non-null. Plume fires opposite to rocket flight
            // direction so it trails behind the rocket like a flame.
            //
            // Orientation derivation. The Cone particle shape emits along
            // its OWN local +Z (confirmed by B-1's ThrusterVfx, which
            // uses LookRotation to align plume +Z with thruster -Y).
            // After PR #49's MeshAlignment, the rocket's transform.up
            // (= rocket's local +Y) is aligned with launchDir; so the
            // rocket's local -Y = backward in world. We need plume's
            // local +Z to point along the rocket's local -Y after the
            // parent's transform is applied — that's a +90° rotation
            // around the plume's local X axis (Quaternion.Euler(90,0,0)
            // sends local +Z → local -Y in the plume's frame, which
            // resolves to -launchDir in world). A 180° rotation would
            // flip +Z → -Z and emit upward; identity (the degenerate
            // LookRotation(Vector3.down) fallback) leaves +Z = rocket's
            // local +Z = world DOWN.
            if (VfxSettings.RocketExhaust && exhaustPlumePrefab != null)
            {
                GameObject plumeGo = Instantiate(exhaustPlumePrefab, transform);
                plumeGo.transform.localPosition = Vector3.zero;
                plumeGo.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                _exhaustPlumePs = plumeGo.GetComponent<ParticleSystem>();
            }

            // Smoke puff child — same Cone-shape orientation issue as the
            // exhaust plume, same fix.
            if (VfxSettings.RocketSmokePuff && smokePuffPrefab != null)
            {
                GameObject puffGo = Instantiate(smokePuffPrefab, transform);
                puffGo.transform.localPosition = Vector3.zero;
                puffGo.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                _smokePuffPs = puffGo.GetComponent<ParticleSystem>();
            }
        }
```

- [ ] **Step 3: Add `OnDestroy` cleanup for the child VFX** — at the end of the class:

```csharp

        void OnDestroy()
        {
            // Detach child ParticleSystems and stop emission, keeping
            // alive particles. Their stopAction = Destroy auto-cleans
            // the orphan GameObjects once particles finish.
            DetachAndStop(_exhaustPlumePs);
            DetachAndStop(_smokePuffPs);
        }

        static void DetachAndStop(ParticleSystem ps)
        {
            if (ps == null) return;
            ps.transform.SetParent(null, true);   // worldPositionStays
            // StopEmitting (not StopEmittingAndClear) keeps already-
            // alive particles alive to finish their lifetimes; the
            // prefab's main.stopAction = Destroy then auto-cleans the
            // orphan GameObject once the last particle expires.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
```

- [ ] **Step 4: Refresh & verify clean compile.**

- [ ] **Step 5: Re-run installer to populate `exhaustPlumePrefab` + `smokePuffPrefab`** on Rocket.prefab:

```
mcp__unityMCP__execute_menu_item(menu_path="Tools/CubeFly/Generate VFX assets")
```

The deferred warning from Task 12 should now partially clear — only `smokeTrailMaterial` remains unset (until Task 19).

- [ ] **Step 6: Verify Rocket.prefab wiring** — open it in the inspector, confirm `exhaustPlumePrefab` and `smokePuffPrefab` are set to the right assets.

- [ ] **Step 7: Play-test** — fire a rocket. Both effects should appear behind the rocket: a warm orange/yellow flame at the tail (exhaust) AND soft white puff clouds trailing back (puffs). Watch the rocket hit a target — the children detach and finish their particle lifetimes off-rocket instead of vanishing instantly.

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/Fly/Rocket.cs Assets/Prefabs/Projectiles/Rocket.prefab
git commit -m "vfx-b2: instantiate exhaust + smoke puff children on Rocket

Awake conditionally instantiates RocketExhaustPlume.prefab and
RocketSmokePuff.prefab as children of the rocket, each toggle-gated
+ null-guarded. Both oriented via Quaternion.Euler(-90, 0, 0) so the
Cone shape's local -Z emission (this Unity build's actual default
when AddComponent-created without explicit shape.rotation) resolves
to the rocket's local -Y (= backward in world after MeshAlignment
maps rocket local +Y to launchDir). See per-effect spec block for
the full reverse-engineering trail.

OnDestroy detaches the children and calls
Stop(StopEmitting) on each — alive particles finish
naturally, prefab stopAction=Destroy auto-cleans orphan GameObjects.

Re-runs installer to populate exhaustPlumePrefab + smokePuffPrefab
SerializeField references on Rocket.prefab.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 19: Rocket — smoke trail (TrailRenderer + LingeringTrail)

**Files:**
- Modify: `Assets/Scripts/Fly/Rocket.cs`

- [ ] **Step 1: Add SerializeField + private state.** After `[SerializeField] GameObject smokePuffPrefab;` (from Task 18):

```csharp
        [Tooltip("RocketSmokeTrailMat.mat (Assets/VFX/Materials/). Wired by VfxAssetsInstaller. Used by the TrailRenderer added at Awake if VfxRocketSmokeTrail is on.")]
        [SerializeField] Material smokeTrailMaterial;
```

Among private fields, after `_smokePuffPs`:
```csharp
        TrailRenderer _smokeTrail;
        LingeringTrail _smokeTrailLingering;
```

- [ ] **Step 2: Extend `Awake` to add the trail** — at the end of the existing Awake (from Task 18), add:

```csharp

            // Smoke trail (TrailRenderer + LingeringTrail) added in code
            // so the toggle gates whether it exists at all — flipping ON
            // later doesn't retroactively add it.
            //
            // The TrailRenderer lives on a dedicated CHILD GameObject for
            // the same reason as Bullet's tracer: OnDestroy must detach
            // the child BEFORE Unity destroys the rocket's hierarchy, so
            // the orphan child can fade per TrailRenderer.time and then
            // autodestruct. Hosting the trail on the rocket itself would
            // defeat the detach (the root being destroyed cannot be
            // SetParent'd away from its own destruction).
            if (VfxSettings.RocketSmokeTrail && smokeTrailMaterial != null)
            {
                GameObject trailGo = new GameObject("SmokeTrail");
                trailGo.transform.SetParent(transform, false);
                trailGo.transform.localPosition = Vector3.zero;
                trailGo.transform.localRotation = Quaternion.identity;

                _smokeTrail = trailGo.AddComponent<TrailRenderer>();
                _smokeTrail.time = 1.0f;
                _smokeTrail.startWidth = 0.20f;
                _smokeTrail.endWidth = 0.05f;
                _smokeTrail.minVertexDistance = 0.05f;
                // sharedMaterial avoids per-rocket material instantiation
                // (TrailRenderer.material clones the asset for write-
                // isolation, allocating + leaking a Material per rocket).
                // Matches LaserWeapon's LineRenderer pattern.
                _smokeTrail.sharedMaterial = smokeTrailMaterial;
                _smokeTrail.emitting = true;

                Color trailColor = new Color(0.92f, 0.95f, 1.00f);
                Gradient grad = new Gradient();
                grad.SetKeys(
                    new[]
                    {
                        new GradientColorKey(trailColor, 0f),
                        new GradientColorKey(trailColor, 1f),
                    },
                    new[]
                    {
                        new GradientAlphaKey(0.70f, 0f),
                        new GradientAlphaKey(0.00f, 1f),
                    });
                _smokeTrail.colorGradient = grad;

                _smokeTrailLingering = trailGo.AddComponent<LingeringTrail>();
            }
```

- [ ] **Step 3: Extend `OnDestroy` to detach the trail** — add at the top of the existing OnDestroy method body (from Task 18):

```csharp
            if (_smokeTrailLingering != null) _smokeTrailLingering.DetachAndFade();
```

So OnDestroy becomes:
```csharp
        void OnDestroy()
        {
            if (_smokeTrailLingering != null) _smokeTrailLingering.DetachAndFade();
            DetachAndStop(_exhaustPlumePs);
            DetachAndStop(_smokePuffPs);
        }
```

- [ ] **Step 4: Refresh & verify clean compile.**

- [ ] **Step 5: Re-run installer to populate `smokeTrailMaterial`** on Rocket.prefab:

```
mcp__unityMCP__execute_menu_item(menu_path="Tools/CubeFly/Generate VFX assets")
```

The "Rocket has no B-2 SerializeFields..." warning from Task 12 should now be completely gone — all three Rocket fields are wired.

- [ ] **Step 6: Verify Rocket.prefab wiring** — open in inspector, confirm `smokeTrailMaterial` shows `RocketSmokeTrailMat`.

- [ ] **Step 7: Play-test** — fire a rocket. In addition to the exhaust + puffs from Task 18, a cool grey-white ribbon should trail behind. On hit, the ribbon detaches and fades over its 1 s lifetime instead of vanishing.

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/Fly/Rocket.cs Assets/Prefabs/Projectiles/Rocket.prefab
git commit -m "vfx-b2: add cool grey-white smoke trail to Rocket

Awake conditionally adds TrailRenderer (1.0s time, 0.20→0.05 width,
cool grey-white tint, alpha-fading colorGradient) + LingeringTrail
helper. OnDestroy detaches via LingeringTrail.DetachAndFade so the
ribbon fades per its time config instead of popping out with the
rocket.

Re-runs installer to populate smokeTrailMaterial SerializeField on
Rocket.prefab. Closes the deferred-wiring warning from the installer
extension (Task 12).

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 20: Rocket — runtime polling for the three continuous effects

**Files:**
- Modify: `Assets/Scripts/Fly/Rocket.cs`

- [ ] **Step 1: Poll all three toggles at the top of `Update`** — add before the existing `if (!_armed) return;` (around line 81):

```csharp
            // Poll toggles each frame for live Debug-tab A/B comparison.
            // No subscription model — these are short-lived and the read
            // cost is negligible.
            if (_exhaustPlumePs != null)
            {
                var em = _exhaustPlumePs.emission;
                em.enabled = VfxSettings.RocketExhaust;
            }
            if (_smokePuffPs != null)
            {
                var em = _smokePuffPs.emission;
                em.enabled = VfxSettings.RocketSmokePuff;
            }
            if (_smokeTrail != null) _smokeTrail.emitting = VfxSettings.RocketSmokeTrail;
```

- [ ] **Step 2: Refresh & verify clean compile.**

- [ ] **Step 3: Play-test runtime polling** — fire a rocket and let it fly. During flight, open Settings → Debug:
- Toggle "Rocket exhaust" OFF → flame stops immediately on the in-flight rocket. Toggle ON → flame resumes.
- Toggle "Rocket smoke trail" OFF → trail stops emitting new vertices on the in-flight rocket; existing vertices keep fading per `time`. Toggle ON → emission resumes.
- Toggle "Rocket smoke puffs" OFF → puffs stop appearing; existing alive puffs finish their 1.5 s lifetime. Toggle ON → resumes.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Fly/Rocket.cs
git commit -m "vfx-b2: poll rocket VFX toggles in Update for live A/B

At the top of Update, read VfxSettings.RocketExhaust / RocketSmokePuff
/ RocketSmokeTrail and set the corresponding emission.enabled /
emitting on the child ParticleSystems and trail. Matches B-1
ThrusterVfx polling pattern — mid-flight toggle takes visible effect
within one frame, supporting the Debug tab's A/B comparison surface.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Phase 3 — Final verification

### Task 21: Full smoke pass + bugfixes

**Files:** none (verification only, plus any bugfix commits this triggers).

- [ ] **Step 1: Re-run the installer one last time** to confirm full convergence:
```
mcp__unityMCP__execute_menu_item(menu_path="Tools/CubeFly/Generate VFX assets")
```
Console should show the success log line, no warnings, no errors.

- [ ] **Step 2: Run the spec's full Manual Smoke Pass** (from the design spec's "Testing — manual play-test checklist" section). Open FlyScene, build/pick a construct with at least one Pyramid and one Cylinder weapon, fire each, observe rocket behaviour, test all 8 toggles individually and in combination, run stress test (sustained fire 10 s, watch frame rate stay > 60 fps).

- [ ] **Step 3: Run the regression pass** — verify B-1 (engine plume, boost flare, RCS puff) and Phase 1 (Bloom, Vignette, Tonemapping, etc.) all behave as before.

- [ ] **Step 4: Bugfix any deviations** — for each issue found, commit a separate small fix following the same per-task verification recipe (refresh + read_console + play-test). Suggested commit message: `vfx-b2: fix <short description>` with the specifics in the body.

- [ ] **Step 5: Final commit if anything was tweaked** — once the smoke pass passes cleanly, this task is done. No further commits unless bugfixes were needed.

---

## Push & PR

After Task 21 passes:

- [ ] **Step 1: Push the branch**
```bash
git push -u origin feat/vfx-phase-b2-weapons
```

- [ ] **Step 2: Open a PR**
```bash
gh pr create \
  --title "VFX Phase B-2: weapons + impacts (muzzle + tracer + impact + rocket plume/smoke/puffs)" \
  --body "$(cat <<'EOF'
Second slice of the VFX pass (after PR #48). Ships eight projectile-and-impact effects:

- Muzzle flashes — warm yellow-white starburst at Pyramid tip; warm orange/yellow disc at Cylinder barrel.
- Bullet tracer — TrailRenderer, yellow-white head fading to hot-pink tail, pink-fringe cross-section.
- Bullet impact spark + ground dust — warm spark always; soft tan dust when the hit surface is roughly upward.
- Rocket exhaust plume — continuous warm yellow/orange flame.
- Rocket smoke trail + puffs — cool grey-white ribbon + 10×-growth soft white particle puffs.

Eight new Debug-tab toggles (16 total), all defaulting ON. All assets procedurally generated by an extended `VfxAssetsInstaller` (now also wires Bullet.prefab / Rocket.prefab via `SerializedObject` patching, so a fresh clone works without inspector wiring).

**Architecture:** Distributed per-component (Approach A from the spec), matching the B-1 `ThrusterVfx` pattern. Per-cube VFX wired by `FlyController.BuildConstruct`. Per-projectile VFX wired into projectile prefabs by the installer. Impact VFX routed through new static helper `ProjectileHit.SpawnImpactVfx`, configured once by `FlyController.Awake`. Shared `LingeringTrail` helper for clean TrailRenderer detach on projectile destroy.

**Spec:** `docs/superpowers/specs/2026-05-25-vfx-pass-phase-b2-weapons-design.md`
**Plan:** `docs/superpowers/plans/2026-05-25-vfx-pass-phase-b2-weapons.md`

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 3: Notify user** — Copilot review will spin up automatically. The plan's verification gates were Editor-only (no CI to wait on for this project), so the PR is ready for human review immediately.

---

## Implementation summary

- **21 tasks** producing **21 commits** (plus any bugfix commits in Task 21).
- **Phase 0 commits (Tasks 1–5):** infrastructure, no visible behaviour change.
- **Phase 1 commits (Tasks 6–12):** asset generation, no runtime behaviour change.
- **Phase 2 commits (Tasks 13–20):** runtime wiring — effects activate progressively. Task 13 makes Pyramid muzzle visible, Task 14 makes Cylinder muzzle visible, Task 15 wires the FlyController references, Task 16 makes bullet tracer + impact visible, Task 17 makes rocket impact visible, Tasks 18–20 make rocket exhaust/smoke trail/puffs visible with live toggle control.
- **Phase 3 (Task 21):** verification gate, may produce bugfix commits.

Each commit is bite-sized, independently reviewable, and leaves the project in a working state.
