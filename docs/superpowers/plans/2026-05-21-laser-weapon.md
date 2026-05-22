# Laser Weapon Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a continuous hitscan **laser** weapon cube — energy-type beam, shared per-type heat, per-cube power draw — riding the existing weapon pipeline.

**Architecture:** `LaserWeapon : WeaponBehavior` (auto-collected by `FlyController.BuildConstruct`'s existing `GetComponent<WeaponBehavior>()` path and dispatched by `FlyShootingController`). Its `Fire` does a per-frame barrel-axis raycast + `LineRenderer` beam + ticked energy damage; `reloadSeconds = 0` so it fires every frame, gated instead by heat (shared per type, owned by `FlyShootingController`) and power (`ConstructEnergySystem.AvailableForWeapons`). A `FlyHeatBar` mirrors the boost bar on the right of the crosshair.

**Tech Stack:** Unity 6.3 LTS / URP 17.3, MonoBehaviour C#, UnityEngine.UI, no DOTS. No automated test framework — per-commit verification is the Unity compile-check (`refresh_unity` + `read_console` filtered to `Assets/Scripts`) plus a final manual play-test.

**Spec:** `docs/superpowers/specs/2026-05-21-laser-weapon-design.md`
**Branch:** `feat/laser-weapon` (already created off `main`; spec committed at `992970b`).

**Delivery:** 3 commits in dependency order, then one PR with Copilot review.

---

## File structure

**Create (scripts):**
- `Assets/Scripts/Fly/LaserWeapon.cs` (+ `.cs.meta`) — the beam weapon.
- `Assets/Scripts/Fly/FlyHeatBar.cs` (+ `.cs.meta`) — heat bar HUD, right of crosshair.

**Modify (scripts):**
- `Assets/Scripts/Fly/ConstructEnergySystem.cs` — add `AvailableForWeapons`.
- `Assets/Scripts/Fly/ProjectileHit.cs` — add optional `DamageType` param to `ApplyAndLog`.
- `Assets/Scripts/Fly/FlyShootingController.cs` — laser dispatch (power-gated) + shared heat tick + `WeaponTypeGroup` heat/`IsLaser`/laser `ReadyFraction`.
- `Assets/Scripts/Core/ReactorMeshAuthor.cs` → **rename** to `SolidCylinderMeshAuthor.cs` (shared by reactor + laser barrel) + update `PlacedReactor.prefab`'s class identifier.

**Create (assets):**
- `Assets/Materials/LaserMat.mat` (+ `.meta`), `Assets/Materials/Defs/LaserMatDef.asset` (+ `.meta`)
- `Assets/Prefabs/PlacedLaser.prefab` (+ `.meta`)
- `Assets/Shapes/ShapeWeaponLaser.asset` (+ `.meta`)

**Modify (assets):**
- `Assets/Shapes/ShapeRegistry.asset` — append the laser shape.
- `Assets/Scenes/FlyScene.unity` — add `FlyHeatBar` to the `FlyHUD` GameObject.

**Key existing GUIDs:**
- `ShapeDefinition` script: `1b2a4f1c8e3a4d6db5f1eecf4a0a1b01`
- `MaterialDefinition` script: `1b2a4f1c8e3a4d6db5f1eecf4a0a1b03`
- `CubeStats` script: `a0a0a0a000000000000000000000010e`
- `PlacedCubeData` script: `a0a0a0a0000000030000000000000003`
- `SolidCylinderMeshAuthor` (was `ReactorMeshAuthor`) script: `a0a0a0a0000000260000000000000026` (preserved through the rename)
- Layer 6 = `PlacedCube`.

**New GUIDs to assign:**
- `LaserWeapon.cs` → `a0a0a0a0000000280000000000000028` (executionOrder 0)
- `FlyHeatBar.cs` → `a0a0a0a0000000290000000000000029` (executionOrder 0)
- `LaserMat.mat` → `b1b1b1b1000000510000000000000051`
- `LaserMatDef.asset` → `2b2a4f1c8e3a4d6db5f1eecf4a0a2c23`
- `ShapeWeaponLaser.asset` → `2b2a4f1c8e3a4d6db5f1eecf4a0a2c24`
- `PlacedLaser.prefab` → `3a3a4f1c8e3a4d6db5f1eecf4a0a5d03`

---

## Commit 1 — Laser core + power hook

### Task 1: `ConstructEnergySystem.AvailableForWeapons`

**Files:** Modify `Assets/Scripts/Fly/ConstructEnergySystem.cs`

- [ ] **Step 1: Add the property** after the `CanEject` property:

```csharp
        // Spare power left for the weapon tier after the shield's
        // higher-priority claim. A shield that is offline because it's
        // unaffordable draws nothing, so its budget is freed for the laser.
        // FlyShootingController allocates this across firing laser cubes
        // (weapons cut first under contention).
        public float AvailableForWeapons =>
            Mathf.Max(0f, _totalOutput - (_shieldPowered ? _shieldDraw : 0f));
```

- [ ] **Step 2: Compile-check** — `refresh_unity(scope="all", compile="request", mode="force")`, then `read_console(types=["error"], filter_text="Assets/Scripts")`. Expect 0 errors.

---

### Task 2: `ProjectileHit.ApplyAndLog` damage-type param

**Files:** Modify `Assets/Scripts/Fly/ProjectileHit.cs`

- [ ] **Step 1: Add an optional `DamageType` parameter** so the laser can route energy damage through the same helper. Replace the `ApplyAndLog` signature + the `type:` line:

Change the signature line:

```csharp
        public static void ApplyAndLog(RaycastHit hit, float damage,
            Transform firingConstruct, string projectileTag)
```

to:

```csharp
        public static void ApplyAndLog(RaycastHit hit, float damage,
            Transform firingConstruct, string projectileTag,
            DamageType damageType = DamageType.Projectile)
```

and change the `HitContext` field:

```csharp
                type: DamageType.Projectile,
```

to:

```csharp
                type: damageType,
```

(Bullet / Rocket callers pass no extra arg → default `Projectile`, unchanged.)

- [ ] **Step 2: Compile-check** — `refresh_unity` + `read_console`. Expect 0 errors.

---

### Task 3: `LaserWeapon`

**Files:** Create `Assets/Scripts/Fly/LaserWeapon.cs` (+ `.cs.meta`)

- [ ] **Step 1: Write `LaserWeapon.cs`**

```csharp
using CubeFly.Core;
using UnityEngine;

namespace CubeFly.Fly
{
    // Continuous hitscan beam weapon. Subclasses WeaponBehavior so it
    // rides the existing FlyShootingController select-and-dispatch loop,
    // but it has no projectile and no reload: reloadSeconds is 0 so the
    // dispatcher fires it every frame LMB is held; shared per-type heat
    // (owned by FlyShootingController) and per-cube power (allocated from
    // ConstructEnergySystem.AvailableForWeapons) gate it instead.
    //
    // Each fire: raycast from the barrel (transform.position along
    // transform.up — the fixed -Y-mount / +Y-barrel convention, NOT
    // crosshair-tracked), draw the LineRenderer barrel->hit (or ->max
    // range), and apply ENERGY damage in fixed ticks to the first cube
    // hit. On any frame the dispatcher does NOT fire it (released /
    // deselected / overheated / unpowered / over the power budget),
    // LateUpdate turns the beam off.
    public class LaserWeapon : WeaponBehavior
    {
        [Header("Laser")]
        [Tooltip("World-space beam range.")]
        [SerializeField] float range = 100f;
        [Tooltip("Seconds between damage ticks. Damage is applied in chunks (not per-frame) so each tick is meaningful against the subtractive-armour formula effective = max(0, raw - AV).")]
        [SerializeField] float tickInterval = 0.1f;
        [Tooltip("Power drawn while firing. FlyShootingController powers floor(AvailableForWeapons / this) of the laser cubes wanting to fire each frame.")]
        [SerializeField] float powerDraw = 5f;
        [Tooltip("Beam colour (tints the LineRenderer).")]
        [SerializeField] Color beamColor = new Color(1f, 0.3f, 0.15f, 1f);
        [SerializeField] float beamWidth = 0.06f;

        public float PowerDraw => powerDraw;

        LineRenderer _line;
        int _hitLayerMask;
        float _tickTimer;
        bool _beamedThisFrame;

        const string TAG = "Laser";

        void Awake()
        {
            // Add + configure the LineRenderer at runtime so the prefab
            // doesn't have to serialize the verbose component.
            _line = GetComponent<LineRenderer>();
            if (_line == null) _line = gameObject.AddComponent<LineRenderer>();
            _line.positionCount = 2;
            _line.useWorldSpace = true;
            _line.startWidth = _line.endWidth = beamWidth;
            _line.startColor = _line.endColor = beamColor;
            // Sprites/Default renders vertex colours and works under URP for
            // a simple unlit beam line. Real beam VFX is the Extended VFX
            // pass (roadmap item 4); this is the v1 placeholder.
            _line.material = new Material(Shader.Find("Sprites/Default"));
            _line.enabled = false;

            // Same target layers + fallback as Bullet/Rocket.
            _hitLayerMask = LayerMask.GetMask("PlacedCube", "AlphaCube");
            if (_hitLayerMask == 0)
                _hitLayerMask = ~(1 << LayerMask.NameToLayer("Ignore Raycast"));
        }

        // Called by the dispatcher each frame this laser is selected, LMB
        // held, not overheated, and powered. crosshairWorldTarget is
        // ignored — the laser fires along its fixed barrel axis.
        protected override void Fire(Vector3 crosshairWorldTarget)
        {
            Vector3 origin = transform.position;
            Vector3 dir = transform.up;

            bool didHit = ProjectileHit.TrySweep(origin, dir, range, _hitLayerMask, Construct, out RaycastHit hit);
            Vector3 end = didHit ? hit.point : origin + dir * range;

            _line.enabled = true;
            _line.SetPosition(0, origin);
            _line.SetPosition(1, end);
            _beamedThisFrame = true;

            // Ticked damage — accumulate real time and apply a chunk each
            // interval to whatever the beam currently hits. While loop so a
            // long frame applies all due ticks (matching elapsed time).
            _tickTimer += Time.deltaTime;
            while (_tickTimer >= tickInterval)
            {
                _tickTimer -= tickInterval;
                if (didHit)
                    ProjectileHit.ApplyAndLog(hit, damage, Construct, TAG, DamageType.Energy);
            }
        }

        // Turn the beam off on any frame the dispatcher didn't fire us, and
        // reset the tick timer so the next burst's first tick is a full
        // interval rather than instant.
        void LateUpdate()
        {
            if (_beamedThisFrame) { _beamedThisFrame = false; return; }
            if (_line != null && _line.enabled) _line.enabled = false;
            _tickTimer = 0f;
        }
    }
}
```

- [ ] **Step 2: Write `LaserWeapon.cs.meta`**

```yaml
fileFormatVersion: 2
guid: a0a0a0a0000000280000000000000028
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:
```

- [ ] **Step 3: Compile-check** — `refresh_unity` + `read_console`. Expect 0 errors. (`damage`, `Construct`, `Shape`, `ReloadSeconds` are inherited from `WeaponBehavior`; `ProjectileHit.TrySweep` / `ApplyAndLog` exist.)

---

### Task 4: `FlyShootingController` laser dispatch + shared heat + commit

**Files:** Modify `Assets/Scripts/Fly/FlyShootingController.cs`

- [ ] **Step 1: Add heat-tuning fields + energy ref + the per-frame fired flag** after the `scrollDeadzone` field:

```csharp
        [Header("Laser heat (shared per laser weapon-type)")]
        [Tooltip("Heat units added per second while a laser of the selected type is firing. 100 = overheated. At 50/s a cold laser overheats after ~2 s of sustained fire.")]
        [SerializeField] float heatRisePerSecond = 50f;
        [Tooltip("Heat units shed per second when not firing (and not overheated).")]
        [SerializeField] float heatFallPerSecond = 30f;
        [Tooltip("Heat units shed per second while overheated — the slow lockout recovery. The laser stays locked until heat returns to 0.")]
        [SerializeField] float heatFallOverheatedPerSecond = 15f;
```

- [ ] **Step 2: Add the energy ref + fired flag** next to the `_lastScrollSign` field:

```csharp
        // Resolved in RegisterWeapons. The laser is the weapon-tier power
        // consumer; FlyShootingController allocates AvailableForWeapons
        // across firing laser cubes.
        ConstructEnergySystem _energy;
        // Set true by HandleFireInput on a frame the SELECTED laser type
        // actually beamed (>=1 cube powered + fired); consumed by
        // TickLaserHeat to decide rise vs cool. Reset each Update.
        bool _selectedLaserFiredThisFrame;
```

- [ ] **Step 3: Resolve the energy system in `RegisterWeapons`.** Add just before the closing `TypesChanged?.Invoke();` line:

```csharp
            _energy = FindAnyObjectByType<ConstructEnergySystem>();
```

- [ ] **Step 4: Restructure `Update`** so heat ticks every frame (cooling even over UI / when not firing). Replace the whole `Update` method:

```csharp
        void Update()
        {
            // Pause + weapon-presence gating.
            if (PauseMenu.Instance != null && PauseMenu.Instance.IsOpen) return;
            if (!HasWeapons) return;

            _selectedLaserFiredThisFrame = false; // HandleFireInput may set it

            // Auto-switch off a fully-dead selected type. Runs before the
            // pointer-over-UI gate — a weapon dying must move selection
            // regardless of where the cursor is.
            AutoSwitchOffDeadType();

            // Selection input (digits, mouse wheel) is allowed even when
            // the cursor is over the weapon toolbar.
            HandleSelectionInputs();

            // Fire (LMB) is gated by pointer-over-UI; heat must still tick
            // (cool) when over UI, so the fire dispatch is conditional but
            // TickLaserHeat below always runs.
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (!overUI) HandleFireInput();

            TickLaserHeat();
        }
```

- [ ] **Step 5: Replace `HandleFireInput`** with the laser-aware version:

```csharp
        void HandleFireInput()
        {
            if (!_input.Fly.Fire.IsPressed()) return;
            if (flyController == null) return;
            Transform construct = flyController.Construct;
            if (construct == null) return;

            WeaponTypeGroup active = _types[_selectedTypeIndex];
            Vector3 target = construct.position + construct.forward * aimRange;

            if (active.IsLaser)
            {
                // Overheated lasers are locked out entirely (heat still
                // cools in TickLaserHeat).
                if (active.Overheated) return;

                // Power-gate: the laser is the weapon-tier consumer. Power
                // floor(available / per-cube draw) of the alive lasers; the
                // rest don't fire this frame and turn their beam off in
                // LaserWeapon.LateUpdate.
                float drawPer = active.LaserPowerDraw;
                float available = _energy != null ? _energy.AvailableForWeapons : 0f;
                int budget = drawPer > 0f ? Mathf.FloorToInt(available / drawPer) : int.MaxValue;

                int fired = 0;
                for (int i = 0; i < active.Instances.Count; i++)
                {
                    WeaponBehavior w = active.Instances[i];
                    if (w == null || !w.IsAlive) continue;
                    if (fired >= budget) continue;
                    w.TryFire(target); // laser ignores target, beams along its barrel
                    fired++;
                }
                _selectedLaserFiredThisFrame = fired > 0;
            }
            else
            {
                for (int i = 0; i < active.Instances.Count; i++)
                {
                    WeaponBehavior w = active.Instances[i];
                    if (w != null && w.IsAlive) w.TryFire(target);
                }
            }
        }

        // Tick shared heat for every laser type each frame: the selected
        // type rises while it's firing, everything else (and the selected
        // type when idle) cools. Overheat latches at 100 and clears at 0.
        void TickLaserHeat()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < _types.Count; i++)
            {
                WeaponTypeGroup t = _types[i];
                if (!t.IsLaser) continue;

                bool rising = (i == _selectedTypeIndex) && _selectedLaserFiredThisFrame;
                if (rising)
                    t.Heat = Mathf.Min(100f, t.Heat + heatRisePerSecond * dt);
                else
                    t.Heat = Mathf.Max(0f, t.Heat -
                        (t.Overheated ? heatFallOverheatedPerSecond : heatFallPerSecond) * dt);

                if (!t.Overheated && t.Heat >= 100f) t.Overheated = true;
                else if (t.Overheated && t.Heat <= 0f) t.Overheated = false;
            }
        }
```

- [ ] **Step 6: Extend `WeaponTypeGroup`** — add the heat state, `IsLaser`, `LaserPowerDraw`, and the laser branch in `ReadyFraction`. Add these members inside the `WeaponTypeGroup` class (after the `Instances` property / constructor):

```csharp
        // Shared heat for a laser type (0..100). Ticked by
        // FlyShootingController; meaningless for non-laser types.
        public float Heat;
        // Latched at heat 100, cleared at 0 — while true the type is fire-
        // locked.
        public bool Overheated;

        bool _isLaserResolved;
        bool _isLaser;
        // True when this type's instances are LaserWeapons. Cached — a
        // type's weapon class never changes for a Fly session.
        public bool IsLaser
        {
            get
            {
                if (!_isLaserResolved)
                {
                    for (int i = 0; i < Instances.Count; i++)
                        if (Instances[i] is LaserWeapon) { _isLaser = true; break; }
                    _isLaserResolved = true;
                }
                return _isLaser;
            }
        }

        // Per-cube power draw of this laser type (0 for non-lasers). Read
        // from a representative LaserWeapon instance.
        public float LaserPowerDraw
        {
            get
            {
                for (int i = 0; i < Instances.Count; i++)
                    if (Instances[i] is LaserWeapon lw) return lw.PowerDraw;
                return 0f;
            }
        }
```

- [ ] **Step 7: Replace `ReadyFraction`** so a laser type drives the toolbar bar from heat (drains as it heats):

```csharp
        // 0 = just fired / fully heated, 1 = ready / cold. Drives the
        // toolbar bar. For a laser the bar shows remaining heat capacity
        // (1 - heat); for a projectile weapon it shows reload progress.
        public float ReadyFraction
        {
            get
            {
                if (IsLaser) return 1f - Mathf.Clamp01(Heat / 100f);
                float r = MaxReloadSeconds;
                if (r <= 0f) return 1f;
                return 1f - Mathf.Clamp01(CooldownRemaining / r);
            }
        }
```

- [ ] **Step 8: Compile-check** — `refresh_unity(mode="force")` + `read_console`. Expect 0 errors. (If a stale-cache "LaserWeapon does not exist" error appears, re-run the force refresh — observed throughout the project.)

- [ ] **Step 9: Commit 1**

```bash
cd "/Users/anon/My project"
git add Assets/Scripts/Fly/ConstructEnergySystem.cs Assets/Scripts/Fly/ProjectileHit.cs \
        Assets/Scripts/Fly/LaserWeapon.cs Assets/Scripts/Fly/LaserWeapon.cs.meta \
        Assets/Scripts/Fly/FlyShootingController.cs
git commit -m "$(cat <<'EOF'
Add laser core: LaserWeapon + heat/power dispatch

LaserWeapon : WeaponBehavior — a continuous hitscan beam (per-frame
barrel-axis raycast + LineRenderer + ticked energy damage), reloadSeconds
0 so the existing FlyShootingController loop fires it every frame. Gated
by shared per-type heat (owned by FlyShootingController: rise 50/s, cool
30/s, overheat lockout recovering at 15/s to 0) and per-cube power
allocated from the new ConstructEnergySystem.AvailableForWeapons (weapons
cut first). WeaponTypeGroup gains heat state + IsLaser + a heat-based
ReadyFraction so the toolbar bar drains as the laser heats.
ProjectileHit.ApplyAndLog takes an optional DamageType so the beam routes
energy damage (shield x1.1) through the shared pipeline. No cube yet —
verified once the laser shape lands in commit 2.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Commit 2 — Laser cube

### Task 5: Rename `ReactorMeshAuthor` → `SolidCylinderMeshAuthor`

**Files:** Rename `Assets/Scripts/Core/ReactorMeshAuthor.cs` (+ `.meta`); modify `Assets/Prefabs/PlacedReactor.prefab`

- [ ] **Step 1: Rename the file (preserving the .meta GUID)**

```bash
cd "/Users/anon/My project"
git mv Assets/Scripts/Core/ReactorMeshAuthor.cs Assets/Scripts/Core/SolidCylinderMeshAuthor.cs
git mv Assets/Scripts/Core/ReactorMeshAuthor.cs.meta Assets/Scripts/Core/SolidCylinderMeshAuthor.cs.meta
```

- [ ] **Step 2: Rename the class + comment** in `Assets/Scripts/Core/SolidCylinderMeshAuthor.cs`. Replace the comment + class line:

```csharp
    // Assigns the runtime-generated solid-cylinder mesh to this
    // GameObject's MeshFilter (and MeshCollider, if present) on Awake,
    // only when the slot is empty. Shared by the Reactor cube and the
    // Laser barrel (both solid cylinders). Mirror of CylinderMeshAuthor.
    [RequireComponent(typeof(MeshFilter))]
    public class SolidCylinderMeshAuthor : MonoBehaviour
```

(The `.cs.meta` keeps GUID `a0a0a0a0000000260000000000000026`, so the prefab reference resolves to the renamed class.)

- [ ] **Step 3: Update `PlacedReactor.prefab`'s class identifier.** In `Assets/Prefabs/PlacedReactor.prefab`, change:

```yaml
  m_EditorClassIdentifier: Assembly-CSharp::CubeFly.Core.ReactorMeshAuthor
```

to:

```yaml
  m_EditorClassIdentifier: Assembly-CSharp::CubeFly.Core.SolidCylinderMeshAuthor
```

- [ ] **Step 4: Compile-check** — `refresh_unity(mode="force")` + `read_console`. Expect 0 errors (and no missing-script warning on the reactor — the GUID is unchanged).

---

### Task 6: Laser material + MaterialDefinition

**Files:** Create `Assets/Materials/LaserMat.mat` (+ `.meta`), `Assets/Materials/Defs/LaserMatDef.asset` (+ `.meta`)

- [ ] **Step 1: Create `LaserMat.mat` by copying `ReactorMat.mat`**

```bash
cd "/Users/anon/My project"
cp Assets/Materials/ReactorMat.mat Assets/Materials/LaserMat.mat
```

Then edit `Assets/Materials/LaserMat.mat`: change `m_Name: ReactorMat` → `m_Name: LaserMat`, and set both colour lines (hot red-orange, both `_BaseColor` and `_Color` so the swatch is correct — the fix from the Power & Energy PR):

```yaml
    - _BaseColor: {r: 1, g: 0.3, b: 0.15, a: 1}
    - _Color: {r: 1, g: 0.3, b: 0.15, a: 1}
```

- [ ] **Step 2: Write `LaserMat.mat.meta`**

```yaml
fileFormatVersion: 2
guid: b1b1b1b1000000510000000000000051
NativeFormatImporter:
  externalObjects: {}
  mainObjectFileID: 2100000
  userData:
  assetBundleName:
  assetBundleVariant:
```

- [ ] **Step 3: Write `LaserMatDef.asset`** (template: `CylinderWeaponMatDef.asset`)

```yaml
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 0}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: 1b2a4f1c8e3a4d6db5f1eecf4a0a1b03, type: 3}
  m_Name: LaserMatDef
  m_EditorClassIdentifier:
  displayName: Laser
  material: {fileID: 2100000, guid: b1b1b1b1000000510000000000000051, type: 2}
  healthPoints: 40
  armourValue: 0
  mass: 2
```

- [ ] **Step 4: Write `LaserMatDef.asset.meta`**

```yaml
fileFormatVersion: 2
guid: 2b2a4f1c8e3a4d6db5f1eecf4a0a2c23
NativeFormatImporter:
  externalObjects: {}
  mainObjectFileID: 11400000
  userData:
  assetBundleName:
  assetBundleVariant:
```

- [ ] **Step 5: Import + verify** — `refresh_unity(mode="force")` + `read_console`. Expect 0 errors. Confirm `LaserMatDef`'s material reference resolves (no "missing material" warning).

---

### Task 7: `PlacedLaser.prefab`

**Files:** Create `Assets/Prefabs/PlacedLaser.prefab` (+ `.meta`)

Two-object prefab (template: `PlacedShield.prefab`): root carries the gameplay components + a full-cell collider; a `LaserBarrel` child holds the `SolidCylinder` mesh scaled thin. The `LineRenderer` is added at runtime by `LaserWeapon.Awake`, so it is NOT in the prefab.

- [ ] **Step 1: Write `PlacedLaser.prefab`**

```yaml
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1 &5600000000000000010
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  serializedVersion: 6
  m_Component:
  - component: {fileID: 5600000000000000011}
  - component: {fileID: 5600000000000000012}
  - component: {fileID: 5600000000000000013}
  - component: {fileID: 5600000000000000014}
  - component: {fileID: 5600000000000000015}
  m_Layer: 6
  m_Name: PlacedLaser
  m_TagString: Untagged
  m_Icon: {fileID: 0}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &5600000000000000011
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 5600000000000000010}
  serializedVersion: 2
  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
  m_LocalPosition: {x: 0, y: 0, z: 0}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_ConstrainProportionsScale: 0
  m_Children:
  - {fileID: 5600000000000000021}
  m_Father: {fileID: 0}
  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}
--- !u!65 &5600000000000000012
BoxCollider:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 5600000000000000010}
  m_Material: {fileID: 0}
  m_IncludeLayers:
    serializedVersion: 2
    m_Bits: 0
  m_ExcludeLayers:
    serializedVersion: 2
    m_Bits: 0
  m_LayerOverridePriority: 0
  m_IsTrigger: 0
  m_ProvidesContacts: 0
  m_Enabled: 1
  serializedVersion: 3
  m_Size: {x: 1, y: 1, z: 1}
  m_Center: {x: 0, y: 0, z: 0}
--- !u!114 &5600000000000000013
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 5600000000000000010}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: a0a0a0a000000000000000000000010e, type: 3}
  m_Name:
  m_EditorClassIdentifier: Assembly-CSharp::CubeFly.Core.CubeStats
  healthPoints: 40
  armourValue: 0
  mass: 2
--- !u!114 &5600000000000000014
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 5600000000000000010}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: a0a0a0a0000000030000000000000003, type: 3}
  m_Name:
  m_EditorClassIdentifier: Assembly-CSharp::CubeFly.Build.PlacedCubeData
  cell: {x: 0, y: 0, z: 0}
--- !u!114 &5600000000000000015
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 5600000000000000010}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: a0a0a0a0000000280000000000000028, type: 3}
  m_Name:
  m_EditorClassIdentifier: Assembly-CSharp::CubeFly.Fly.LaserWeapon
  projectilePrefab: {fileID: 0}
  reloadSeconds: 0
  damage: 6
  armorPenetration: 0
  range: 100
  tickInterval: 0.1
  powerDraw: 5
  beamColor: {r: 1, g: 0.3, b: 0.15, a: 1}
  beamWidth: 0.06
--- !u!1 &5600000000000000020
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  serializedVersion: 6
  m_Component:
  - component: {fileID: 5600000000000000021}
  - component: {fileID: 5600000000000000022}
  - component: {fileID: 5600000000000000023}
  - component: {fileID: 5600000000000000024}
  m_Layer: 6
  m_Name: LaserBarrel
  m_TagString: Untagged
  m_Icon: {fileID: 0}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &5600000000000000021
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 5600000000000000020}
  serializedVersion: 2
  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
  m_LocalPosition: {x: 0, y: 0, z: 0}
  m_LocalScale: {x: 0.3, y: 1, z: 0.3}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {fileID: 5600000000000000011}
  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}
--- !u!33 &5600000000000000022
MeshFilter:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 5600000000000000020}
  m_Mesh: {fileID: 0}
--- !u!23 &5600000000000000023
MeshRenderer:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 5600000000000000020}
  m_Enabled: 1
  m_CastShadows: 1
  m_ReceiveShadows: 1
  m_DynamicOccludee: 1
  m_StaticShadowCaster: 0
  m_MotionVectors: 1
  m_LightProbeUsage: 1
  m_ReflectionProbeUsage: 1
  m_RayTracingMode: 2
  m_RayTraceProcedural: 0
  m_RayTracingAccelStructBuildFlagsOverride: 0
  m_RayTracingAccelStructBuildFlags: 1
  m_SmallMeshCulling: 1
  m_ForceMeshLod: -1
  m_MeshLodSelectionBias: 0
  m_RenderingLayerMask: 1
  m_RendererPriority: 0
  m_Materials:
  - {fileID: 2100000, guid: b1b1b1b1000000510000000000000051, type: 2}
  m_StaticBatchInfo:
    firstSubMesh: 0
    subMeshCount: 0
  m_StaticBatchRoot: {fileID: 0}
  m_ProbeAnchor: {fileID: 0}
  m_LightProbeVolumeOverride: {fileID: 0}
  m_ScaleInLightmap: 1
  m_ReceiveGI: 1
  m_PreserveUVs: 0
  m_IgnoreNormalsForChartDetection: 0
  m_ImportantGI: 0
  m_StitchLightmapSeams: 1
  m_SelectedEditorRenderState: 3
  m_MinimumChartSize: 4
  m_AutoUVMaxDistance: 0.5
  m_AutoUVMaxAngle: 89
  m_LightmapParameters: {fileID: 0}
  m_GlobalIlluminationMeshLod: 0
  m_SortingLayerID: 0
  m_SortingLayer: 0
  m_SortingOrder: 0
  m_MaskInteraction: 0
  m_AdditionalVertexStreams: {fileID: 0}
--- !u!114 &5600000000000000024
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 5600000000000000020}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: a0a0a0a0000000260000000000000026, type: 3}
  m_Name:
  m_EditorClassIdentifier: Assembly-CSharp::CubeFly.Core.SolidCylinderMeshAuthor
```

- [ ] **Step 2: Write `PlacedLaser.prefab.meta`**

```yaml
fileFormatVersion: 2
guid: 3a3a4f1c8e3a4d6db5f1eecf4a0a5d03
PrefabImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
```

- [ ] **Step 3: Import + verify** — `refresh_unity(mode="force")` + `read_console`. Expect 0 errors and no broken-script / missing-reference warnings on `PlacedLaser`.

---

### Task 8: `ShapeWeaponLaser` + registry + commit

**Files:** Create `Assets/Shapes/ShapeWeaponLaser.asset` (+ `.meta`); modify `Assets/Shapes/ShapeRegistry.asset`

- [ ] **Step 1: Write `ShapeWeaponLaser.asset`** (template: `ShapeWeaponCylinder.asset`; Weapon category = 1, `−Y` mount)

```yaml
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 0}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: 1b2a4f1c8e3a4d6db5f1eecf4a0a1b01, type: 3}
  m_Name: ShapeWeaponLaser
  m_EditorClassIdentifier:
  displayName: Laser
  prefab: {fileID: 5600000000000000010, guid: 3a3a4f1c8e3a4d6db5f1eecf4a0a5d03, type: 3}
  category: 1
  coupledMaterial: {fileID: 11400000, guid: 2b2a4f1c8e3a4d6db5f1eecf4a0a2c23, type: 2}
  faceNegX: 0
  facePosX: 0
  faceNegY: 1
  facePosY: 0
  faceNegZ: 0
  facePosZ: 0
```

- [ ] **Step 2: Write `ShapeWeaponLaser.asset.meta`**

```yaml
fileFormatVersion: 2
guid: 2b2a4f1c8e3a4d6db5f1eecf4a0a2c24
NativeFormatImporter:
  externalObjects: {}
  mainObjectFileID: 11400000
  userData:
  assetBundleName:
  assetBundleVariant:
```

- [ ] **Step 3: Append to `ShapeRegistry.asset`.** The `shapes:` list currently ends with the reactor + shield entries (from the Power & Energy PR). Add the laser as the final entry:

```yaml
  - {fileID: 11400000, guid: 2b2a4f1c8e3a4d6db5f1eecf4a0a2c24, type: 2}
```

(Append after the last existing `- {fileID: 11400000, ...}` line in the `shapes:` block.)

- [ ] **Step 4: Compile-check + verify** — `refresh_unity(mode="force")` + `read_console`. Expect 0 errors. The laser now appears in the build toolbar's **Weapons** flyout.

- [ ] **Step 5: Commit 2**

```bash
cd "/Users/anon/My project"
git add Assets/Scripts/Core/SolidCylinderMeshAuthor.cs Assets/Scripts/Core/SolidCylinderMeshAuthor.cs.meta \
        Assets/Prefabs/PlacedReactor.prefab \
        Assets/Materials/LaserMat.mat Assets/Materials/LaserMat.mat.meta \
        Assets/Materials/Defs/LaserMatDef.asset Assets/Materials/Defs/LaserMatDef.asset.meta \
        Assets/Prefabs/PlacedLaser.prefab Assets/Prefabs/PlacedLaser.prefab.meta \
        Assets/Shapes/ShapeWeaponLaser.asset Assets/Shapes/ShapeWeaponLaser.asset.meta \
        Assets/Shapes/ShapeRegistry.asset
git rm Assets/Scripts/Core/ReactorMeshAuthor.cs Assets/Scripts/Core/ReactorMeshAuthor.cs.meta 2>/dev/null || true
git commit -m "$(cat <<'EOF'
Add Laser weapon cube

A thin-barrel Weapon shape: the SolidCylinder mesh on a child scaled
(0.3, 1, 0.3) so it reads as a focused emitter, distinct from the rocket
cylinder; mounted -Y / barrel +Y; full-cell collider. LaserMat /
LaserMatDef (hot red-orange, HP 40 / AV 0 / mass 2). PlacedLaser carries
LaserWeapon (per-tick damage 6, range 100, tick 0.1 s, power draw 5);
the LineRenderer is added at runtime. ReactorMeshAuthor is renamed to
SolidCylinderMeshAuthor (shared by reactor + laser). Appended to the
Weapons flyout via ShapeRegistry.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

(If `git mv` in Task 5 already staged the rename, the `git rm` is a no-op — the `|| true` keeps the commit going.)

---

## Commit 3 — HUD

### Task 9: `FlyHeatBar` + scene attach + commit

**Files:** Create `Assets/Scripts/Fly/FlyHeatBar.cs` (+ `.cs.meta`); modify `Assets/Scenes/FlyScene.unity`

- [ ] **Step 1: Write `FlyHeatBar.cs`** (mirror of `FlyBoostBar`, right of the crosshair, reading the selected laser type from `FlyShootingController`)

```csharp
using System.Collections;
using CubeFly.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CubeFly.Fly
{
    // Heat HUD element — a thin vertical bar to the RIGHT of the crosshair,
    // mirroring FlyBoostBar on the left. Reads the SELECTED weapon type's
    // shared heat from FlyShootingController; the whole element is hidden
    // unless the selected type is a laser.
    //
    // Fill   — bar height = Heat/100 (grows up as the laser heats).
    // Colour — lerps cool->hot with heat; while Overheated it pulses red
    //          (the FlyBoostBar critical-throb pattern).
    // Flash  — "Overheated!" flashes 3x on the lockout edge, above the
    //          crosshair (the FlyBoostBar "Overboosted!" pattern).
    public class FlyHeatBar : MonoBehaviour
    {
        [SerializeField] FlyShootingController shootingController;

        [Header("Bar layout (screen-centre relative)")]
        [Tooltip("Anchored position of the bar centre relative to screen centre. Positive x sits it right of the crosshair (mirror of the boost bar).")]
        [SerializeField] Vector2 anchoredPosition = new Vector2(90f, 0f);
        [SerializeField] Vector2 barSize = new Vector2(12f, 120f);
        [SerializeField] Color coolColor = new Color(1f, 0.6f, 0.2f, 1f);
        [SerializeField] Color hotColor = new Color(1f, 0.2f, 0.1f, 1f);
        [SerializeField] Color frameColor = new Color(0.12f, 0.06f, 0.04f, 1f);

        [Header("Overheated flash")]
        [SerializeField] Vector2 flashAnchoredPosition = new Vector2(0f, 70f);
        [SerializeField] int flashFontSize = 26;
        [SerializeField] Color flashColor = new Color(1f, 0.4f, 0.25f, 1f);
        [SerializeField] int flashCount = 3;
        [SerializeField] float flashOnSeconds = 0.27f;
        [SerializeField] float flashOffSeconds = 0.12f;

        [Header("Overheated throb")]
        [SerializeField] float overheatedPulseSeconds = 0.6f;
        [SerializeField, Range(0f, 1f)] float overheatedAlphaMin = 0.45f;

        RectTransform _frame;
        RectTransform _fill;
        Image _fillImage;
        Image _frameImage;
        Text _flashLabel;

        bool _wasOverheated;
        Coroutine _flashRoutine;

        const string TAG = "FlyHeatBar";

        void Awake() => BuildUI();

        void OnValidate()
        {
            overheatedPulseSeconds = Mathf.Max(0.01f, overheatedPulseSeconds);
            overheatedAlphaMin = Mathf.Clamp01(overheatedAlphaMin);
        }

        void Start()
        {
            if (shootingController == null) shootingController = FindAnyObjectByType<FlyShootingController>();
            if (shootingController == null)
                Debug.unityLogger.Log(TAG, "No FlyShootingController in scene; heat bar stays hidden.");
        }

        void Update()
        {
            if (_frame == null) return;

            WeaponTypeGroup sel = shootingController != null ? shootingController.SelectedType : null;
            bool isLaser = sel != null && sel.IsLaser;

            if (_frame.gameObject.activeSelf != isLaser) _frame.gameObject.SetActive(isLaser);
            if (!isLaser)
            {
                _wasOverheated = false;
                return;
            }

            float fraction = Mathf.Clamp01(sel.Heat / 100f);
            _fill.sizeDelta = new Vector2(barSize.x, barSize.y * fraction);

            if (sel.Overheated)
            {
                float pulse01 = 0.5f * (1f + Mathf.Sin(
                    Time.unscaledTime * (2f * Mathf.PI / overheatedPulseSeconds)));
                float a = Mathf.Lerp(overheatedAlphaMin, 1f, pulse01);
                SetImageAlpha(_fillImage, hotColor, a);
                SetImageAlpha(_frameImage, frameColor, a);
            }
            else
            {
                Color c = Color.Lerp(coolColor, hotColor, fraction);
                SetImageAlpha(_fillImage, c, 1f);
                SetImageAlpha(_frameImage, frameColor, 1f);
            }

            if (sel.Overheated && !_wasOverheated)
            {
                if (_flashRoutine != null) StopCoroutine(_flashRoutine);
                _flashRoutine = StartCoroutine(FlashOverheated());
            }
            _wasOverheated = sel.Overheated;
        }

        static void SetImageAlpha(Image img, Color baseColor, float alpha)
        {
            if (img == null) return;
            img.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
        }

        IEnumerator FlashOverheated()
        {
            if (_flashLabel == null) yield break;
            for (int i = 0; i < flashCount; i++)
            {
                _flashLabel.enabled = true;
                yield return new WaitForSecondsRealtime(flashOnSeconds);
                _flashLabel.enabled = false;
                yield return new WaitForSecondsRealtime(flashOffSeconds);
            }
            _flashLabel.enabled = false;
            _flashRoutine = null;
        }

        void BuildUI()
        {
            RectTransform canvasRoot = FlyHud.Instance.Root;
            int uiLayer = LayerMask.NameToLayer("UI");

            GameObject frameGO = new GameObject("HeatBarFrame",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            if (uiLayer >= 0) frameGO.layer = uiLayer;
            frameGO.transform.SetParent(canvasRoot, false);
            _frame = (RectTransform)frameGO.transform;
            _frame.anchorMin = _frame.anchorMax = _frame.pivot = new Vector2(0.5f, 0.5f);
            _frame.sizeDelta = barSize;
            _frame.anchoredPosition = anchoredPosition;
            _frameImage = frameGO.GetComponent<Image>();
            _frameImage.color = frameColor;
            _frameImage.raycastTarget = false;

            GameObject fillGO = new GameObject("HeatBarFill",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            if (uiLayer >= 0) fillGO.layer = uiLayer;
            fillGO.transform.SetParent(frameGO.transform, false);
            _fill = (RectTransform)fillGO.transform;
            _fill.anchorMin = _fill.anchorMax = _fill.pivot = new Vector2(0.5f, 0f);
            _fill.sizeDelta = new Vector2(barSize.x, 0f);
            _fill.anchoredPosition = Vector2.zero;
            _fillImage = fillGO.GetComponent<Image>();
            _fillImage.color = coolColor;
            _fillImage.raycastTarget = false;

            _flashLabel = UIStyle.BuildLabel(canvasRoot, "Overheated!", fontSize: flashFontSize, style: FontStyle.Bold);
            _flashLabel.color = flashColor;
            RectTransform flashRT = (RectTransform)_flashLabel.transform;
            flashRT.anchorMin = flashRT.anchorMax = flashRT.pivot = new Vector2(0.5f, 0.5f);
            flashRT.sizeDelta = new Vector2(360f, 44f);
            flashRT.anchoredPosition = flashAnchoredPosition;
            _flashLabel.enabled = false;

            _frame.gameObject.SetActive(false);
        }
    }
}
```

- [ ] **Step 2: Write `FlyHeatBar.cs.meta`** (standard block, guid `a0a0a0a0000000290000000000000029`, executionOrder 0).

- [ ] **Step 3: Compile-check** — `refresh_unity(mode="force")` + `read_console`. Expect 0 errors.

- [ ] **Step 4: Add `FlyHeatBar` to the `FlyHUD` GameObject**

```python
manage_scene(action="load", path="Assets/Scenes/FlyScene.unity")
manage_components(action="add", target="FlyHUD", component_type="CubeFly.Fly.FlyHeatBar", search_method="by_name")
manage_scene(action="save")
```

Verify: `grep -n "FlyHeatBar" Assets/Scenes/FlyScene.unity` shows a MonoBehaviour record on `FlyHUD`.

- [ ] **Step 5: Compile-check + commit 3**

`refresh_unity(mode="force")` + `read_console` (0 errors), then:

```bash
cd "/Users/anon/My project"
git add Assets/Scripts/Fly/FlyHeatBar.cs Assets/Scripts/Fly/FlyHeatBar.cs.meta Assets/Scenes/FlyScene.unity
git commit -m "$(cat <<'EOF'
Add FlyHeatBar HUD (heat bar right of crosshair)

A vertical heat bar mirroring FlyBoostBar on the right of the crosshair:
fill grows with the selected laser type's shared heat (0->100), colour
lerps cool->hot and pulses red while overheated, and an "Overheated!"
3x flash fires on the lockout edge. Hidden unless a laser type is
selected. Reads FlyShootingController.SelectedType; built under
FlyHud.Instance.Root on the FlyHUD GameObject.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 10: Push, open PR, request Copilot review

- [ ] **Step 1: Push + PR**

```bash
cd "/Users/anon/My project"
git push -u origin feat/laser-weapon
gh pr create --title "Laser weapon (energy beam)" --body "$(cat <<'EOF'
## Summary

- New `LaserWeapon : WeaponBehavior` — a continuous hitscan beam fired along the cube's fixed barrel axis: per-frame self-filtered raycast + runtime `LineRenderer` + ticked **energy** damage (every 0.1 s) routed through `CubeDamage` so the shield takes it at ×1.1. `reloadSeconds = 0`, so the existing `FlyShootingController` loop fires it every frame; heat + power gate it instead.
- **Shared per-type heat** (on `WeaponTypeGroup`, ticked by `FlyShootingController`): rise 50/s, cool 30/s, overheat lockout recovering at 15/s back to 0, "Overheated!" flash. The toolbar bar reuses `1 − heat` so it drains as the laser heats.
- **Per-cube power** — `ConstructEnergySystem.AvailableForWeapons` (spare after the shield's claim) allocated `floor(available / 5)` across firing lasers; weapons cut first. A laser needs a reactor to fire.
- New cube: thin-barrel Weapon shape (`SolidCylinder` scaled `(0.3,1,0.3)`, `−Y` mount), `LaserMat`/`LaserMatDef` (HP 40 / AV 0 / mass 2). `ReactorMeshAuthor` renamed `SolidCylinderMeshAuthor` (shared).
- `FlyHeatBar` — heat bar right of the crosshair (mirror of the boost bar).

Three commits: laser core + power hook → laser cube → HUD. `ProjectileHit.ApplyAndLog` gained an optional `DamageType` so the beam reuses the shared hit pipeline.

Spec: \`docs/superpowers/specs/2026-05-21-laser-weapon-design.md\`
Plan: \`docs/superpowers/plans/2026-05-21-laser-weapon.md\`

## Test plan

- [ ] Compile clean per commit (verified during implementation).
- [ ] Build: Laser in the **Weapons** flyout; mounts `−Y` only; renders as a thin barrel (distinct from the rocket cylinder); mass 2 counts against the cap.
- [ ] Fire: with a reactor aboard, hold LMB → continuous beam along the barrel axis (not crosshair-tracked); `LineRenderer` connects barrel→hit; AV-0 world cubes take energy DPS and die; beam stops on release / deselect.
- [ ] Heat: the right-of-crosshair bar fills while firing, overheats at ~2 s sustained → "Overheated!" ×3 + lockout, slow cool to 0 before firing again; short bursts never lock out; the toolbar bar mirrors it.
- [ ] Power: with no reactor the laser can't fire; with a reactor it fires; killing the reactor mid-beam cuts the laser (before the shield).
- [ ] Energy vs shield: a laser drains a shielded target faster than projectile fire (×1.1).

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
PR_NUM=$(gh pr view --json number -q .number)
gh api repos/entewurzelauskuh/CosmicScrapClub/pulls/$PR_NUM/requested_reviewers \
    -X POST -f "reviewers[]=copilot-pull-request-reviewer[bot]" 2>/dev/null || true
```

- [ ] **Step 2: Manual play-test (user)** — run the Test-plan checklist on the main project root; triage Copilot before merge.

---

## Self-review

- **Spec coverage:** continuous beam + barrel-axis fire (Task 3) ✓; ticked energy damage + shield ×1.1 (Tasks 2, 3) ✓; shared per-type heat + lockout + flash (Tasks 4, 9) ✓; per-cube power gating + weapons-cut-first (Tasks 1, 4) ✓; thin-barrel model + `−Y` mount (Tasks 5, 7, 8) ✓; `SolidCylinderMeshAuthor` rename (Task 5) ✓; heat bar right of crosshair + toolbar `1−heat` (Tasks 4, 9) ✓; new shape/material/prefab + registry (Tasks 6, 7, 8) ✓; backward compat (no FlyController/save changes) ✓.
- **Placeholder scan:** no TBD/TODO; the `.mat` is a concrete copy-and-edit of `ReactorMat.mat`; every code step has full code.
- **Type consistency:** `PowerDraw` (LaserWeapon ↔ WeaponTypeGroup.LaserPowerDraw), `IsLaser` / `Heat` / `Overheated` / `ReadyFraction` (WeaponTypeGroup ↔ FlyShootingController ↔ FlyHeatBar), `AvailableForWeapons` (ConstructEnergySystem ↔ FlyShootingController), `ApplyAndLog(..., DamageType)` (ProjectileHit ↔ LaserWeapon) — all consistent across tasks. Script GUID `a0a0a0a0000000260000000000000026` is the shared `SolidCylinderMeshAuthor` referenced by both `PlacedReactor` (Task 5) and `PlacedLaser` (Task 7).
