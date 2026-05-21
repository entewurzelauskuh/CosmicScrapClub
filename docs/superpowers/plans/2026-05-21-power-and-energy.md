# Power & Energy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a construct-wide power/shield system — reactor cubes produce power, shield cubes draw power and add a shared damage-absorbing pool, with a net-rate balance and a consumer cascade.

**Architecture:** One `ConstructEnergySystem` MonoBehaviour on `CubeConstruct` (sibling to `FlyController`) owns the power balance, shared shield pool, regen, and cascade. `FlyController.BuildConstruct` collects new `ReactorBehavior`/`ShieldBehavior` cubes (the passive `ThrusterBehavior` pattern) and hands them over via `RegisterCubes`. `CubeDamage.ApplyAndLog` intercepts each hit through the shield before HP. A `FlyShieldIndicator` adds the shield bar + power readout to `FlyHud`.

**Tech Stack:** Unity 6.3 LTS / URP 17.3, MonoBehaviour C#, UnityEngine.UI (legacy uGUI), no DOTS. No automated test framework (deferred with F5) — per-task verification is the Unity compile-check (`refresh_unity` + `read_console` filtered to `Assets/Scripts`) plus a final manual play-test.

**Spec:** `docs/superpowers/specs/2026-05-21-power-and-energy-design.md`
**Branch:** `feat/power-and-energy` (already created off `main`; spec committed at `d65f0e8`).

**Delivery:** 3 commits in dependency order, then one PR with Copilot review.

---

## File structure

**Create (scripts):**
- `Assets/Scripts/Fly/ReactorBehavior.cs` — passive descriptor: `Output` + `IsAlive`.
- `Assets/Scripts/Fly/ShieldBehavior.cs` — passive descriptor: `Draw`, `Contribution`, `IsAlive`.
- `Assets/Scripts/Fly/ConstructEnergySystem.cs` — the central power/shield system.
- `Assets/Scripts/Core/ReactorMeshAuthor.cs` — assigns the solid-cylinder mesh.
- `Assets/Scripts/Fly/FlyShieldIndicator.cs` — HUD shield bar + power readout.

**Modify (scripts):**
- `Assets/Scripts/Core/PrimitiveMeshes.cs` — add `SolidCylinder`.
- `Assets/Scripts/Fly/FlyController.cs` — collect cubes, resolve + register the energy system, recompute on death.
- `Assets/Scripts/Fly/CubeDamage.cs` — shield interception step.

**Create (assets, via Unity MCP):**
- `Assets/Materials/ReactorMat.mat`, `Assets/Materials/ShieldMat.mat`
- `Assets/Materials/Defs/ReactorMatDef.asset`, `ShieldMatDef.asset`
- `Assets/Prefabs/PlacedReactor.prefab`, `PlacedShield.prefab`
- `Assets/Shapes/ShapeUtilityReactor.asset`, `ShapeUtilityShield.asset`

**Modify (assets):**
- `Assets/Scenes/FlyScene.unity` — add `ConstructEnergySystem` to `CubeConstruct`, add `FlyShieldIndicator` to `FlyHUD`.
- `Assets/Shapes/ShapeRegistry.asset` — append the two new shapes.

**Reference templates** (existing analogues): `ThrusterBehavior.cs`, `WeaponBehavior.cs` (the `IsAlive` cache), `CylinderMeshAuthor.cs`, `PlacedThruster.prefab`, `ThrusterMatDef.asset`, `ShapeUtilityThruster.asset`, `FlyHpIndicator.cs` / `FlyBoostBar.cs` (HUD).

**Key existing GUIDs** (for hand-authored YAML + prefab wiring):
- `CubeStats` script: `a0a0a0a000000000000000000000010e`
- `PlacedCubeData` script: `a0a0a0a0000000030000000000000003`
- `ShapeDefinition` script: `1b2a4f1c8e3a4d6db5f1eecf4a0a1b01`
- `MaterialDefinition` script: `1b2a4f1c8e3a4d6db5f1eecf4a0a1b03`
- Built-in Unity Cube mesh: `{fileID: 10202, guid: 0000000000000000e000000000000000, type: 0}`
- Layer 6 = `PlacedCube`.

**New script GUIDs** (assign in each `.cs.meta`, continuing the HUD sequence 0020–0022):
- `ReactorBehavior` → `a0a0a0a0000000230000000000000023` (executionOrder 0)
- `ShieldBehavior` → `a0a0a0a0000000240000000000000024` (executionOrder 0)
- `ConstructEnergySystem` → `a0a0a0a0000000250000000000000025` (executionOrder 0)
- `ReactorMeshAuthor` → `a0a0a0a0000000260000000000000026` (executionOrder 0)
- `FlyShieldIndicator` → `a0a0a0a0000000270000000000000027` (executionOrder 100)

---

## Commit 1 — Power core

### Task 1: Reactor + Shield behaviours

**Files:**
- Create: `Assets/Scripts/Fly/ReactorBehavior.cs` (+ `.cs.meta`)
- Create: `Assets/Scripts/Fly/ShieldBehavior.cs` (+ `.cs.meta`)

- [ ] **Step 1: Write `ReactorBehavior.cs`**

```csharp
using CubeFly.Core;
using UnityEngine;

namespace CubeFly.Fly
{
    // A placed Reactor in flight — produces power for the construct's
    // ConstructEnergySystem. Passive descriptor (no Update), the
    // ThrusterBehavior pattern: FlyController.BuildConstruct collects every
    // ReactorBehavior into a list and hands them to the energy system,
    // which sums the alive ones' Output each RecomputePower.
    public class ReactorBehavior : MonoBehaviour
    {
        [Tooltip("Power produced while this reactor is alive. Feeds the construct's net-rate power balance.")]
        [SerializeField] float output = 10f;

        public float Output => output;

        // True while alive (HP > 0). Lazy-cached sibling CubeStats —
        // copied from WeaponBehavior.IsAlive (the construct is rigid for a
        // Fly session, so resolving once is safe).
        public bool IsAlive
        {
            get
            {
                if (!_statsResolved)
                {
                    _stats = GetComponent<CubeStats>();
                    _statsResolved = true;
                }
                return _stats != null && _stats.healthPoints > 0f;
            }
        }

        CubeStats _stats;
        bool _statsResolved;
    }
}
```

- [ ] **Step 2: Write `ReactorBehavior.cs.meta`**

```yaml
fileFormatVersion: 2
guid: a0a0a0a0000000230000000000000023
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

- [ ] **Step 3: Write `ShieldBehavior.cs`**

```csharp
using CubeFly.Core;
using UnityEngine;

namespace CubeFly.Fly
{
    // A placed Shield generator in flight. Passive descriptor (the
    // ThrusterBehavior pattern). FlyController.BuildConstruct collects
    // every ShieldBehavior; ConstructEnergySystem sums the alive ones'
    // Draw (power consumed) and Contribution (shield points added to the
    // shared pool) each RecomputePower.
    public class ShieldBehavior : MonoBehaviour
    {
        [Tooltip("Power consumed while this shield is alive and powered.")]
        [SerializeField] float draw = 20f;
        [Tooltip("Shield points this cube adds to the construct's shared shield pool.")]
        [SerializeField] float contribution = 50f;

        public float Draw => draw;
        public float Contribution => contribution;

        public bool IsAlive
        {
            get
            {
                if (!_statsResolved)
                {
                    _stats = GetComponent<CubeStats>();
                    _statsResolved = true;
                }
                return _stats != null && _stats.healthPoints > 0f;
            }
        }

        CubeStats _stats;
        bool _statsResolved;
    }
}
```

- [ ] **Step 4: Write `ShieldBehavior.cs.meta`** (same block, guid `a0a0a0a0000000240000000000000024`, executionOrder 0).

- [ ] **Step 5: Compile-check**

`refresh_unity(scope="scripts", compile="request", wait_for_ready=true)`, then `read_console(types=["error"], filter_text="Assets/Scripts")`. Expected: 0 errors. (If a stale-cache "type does not exist" error appears, re-run with `mode="force"` — observed throughout the HUD work.)

---

### Task 2: `ConstructEnergySystem`

**Files:**
- Create: `Assets/Scripts/Fly/ConstructEnergySystem.cs` (+ `.cs.meta`, guid `a0a0a0a0000000250000000000000025`, executionOrder 0)

- [ ] **Step 1: Write `ConstructEnergySystem.cs`**

```csharp
using System.Collections.Generic;
using CubeFly.Core;
using UnityEngine;

namespace CubeFly.Fly
{
    // Construct-wide power + shield system. One per construct, on the
    // CubeConstruct GameObject (sibling to FlyController). FlyController
    // collects ReactorBehavior + ShieldBehavior instances during
    // BuildConstruct and hands them over via RegisterCubes; this system
    // owns the instantaneous net-rate power balance, the single shared
    // shield pool, regen, and the consumer-priority cascade.
    //
    // Power model: NetPower (the player-facing demand balance) =
    // Σ(alive reactor Output) − Σ(alive shield Draw). The shield is a
    // single all-or-nothing consumer that claims power first (so the
    // laser, a later lower-priority consumer, is what gets cut first
    // under contention): powered iff total output >= total shield draw.
    //
    // Damage interception: CubeDamage.ApplyAndLog resolves this system via
    // GetComponentInParent on the hit cube and calls ApplyToShield, which
    // absorbs against the pool (type-scaled) before the overflow reaches HP.
    public class ConstructEnergySystem : MonoBehaviour
    {
        [Header("Shield regen")]
        [Tooltip("Shield points regenerated per second once the regen delay has elapsed.")]
        [SerializeField] float regenRate = 20f;
        [Tooltip("Seconds without taking damage before the shield starts regenerating.")]
        [SerializeField] float regenDelaySeconds = 5f;

        [Header("Shield damage-type modifiers")]
        [Tooltip("Multiplier on projectile damage while the shield absorbs it. <1 = shield resists projectiles.")]
        [SerializeField] float projectileModifier = 0.9f;
        [Tooltip("Multiplier on energy damage while the shield absorbs it. >1 = shield is weak to energy.")]
        [SerializeField] float energyModifier = 1.1f;
        [Tooltip("Multiplier on kinetic (crash) damage while the shield absorbs it.")]
        [SerializeField] float kineticModifier = 1f;

        readonly List<ReactorBehavior> _reactors = new();
        readonly List<ShieldBehavior> _shields = new();

        float _shieldPoints;
        float _shieldMax;
        float _shieldDraw;
        float _totalOutput;
        bool _shieldPowered;
        float _timeSinceDamage;

        const string TAG = "Energy";

        // --- HUD read-only surface ---
        public float ShieldPoints => _shieldPoints;
        public float ShieldMax => _shieldMax;
        // Player-facing demand balance: output − total nominal shield draw
        // (later also − active laser draw). Negative = under-powered.
        public float NetPower => _totalOutput - _shieldDraw;
        public bool ShieldActive => _shieldPowered;
        public bool HasShieldCubes => _shields.Count > 0;
        public bool HasPowerCubes => _reactors.Count > 0 || _shields.Count > 0;

        // Called once by FlyController.Start after BuildConstruct.
        public void RegisterCubes(IEnumerable<ReactorBehavior> reactors, IEnumerable<ShieldBehavior> shields)
        {
            _reactors.Clear();
            _shields.Clear();
            _reactors.AddRange(reactors);
            _shields.AddRange(shields);
            RecomputePower();
            // Seed the pool full so a freshly-built powered construct flies
            // in with shields up.
            _shieldPoints = _shieldPowered ? _shieldMax : 0f;
            Debug.unityLogger.Log(TAG,
                $"Registered {_reactors.Count} reactor(s), {_shields.Count} shield(s). " +
                $"Output {_totalOutput:F0}, shield draw {_shieldDraw:F0}, net {NetPower:F0}, " +
                $"shield {(_shieldPowered ? "ONLINE" : "OFFLINE")} (max {_shieldMax:F0}).");
        }

        // Recompute power balance + shield ceiling. Public so
        // FlyController.OnCubeDied can call it after the disconnect cascade
        // settles (a reactor/shield may have died or been orphaned).
        public void RecomputePower()
        {
            _totalOutput = 0f;
            for (int i = 0; i < _reactors.Count; i++)
                if (_reactors[i] != null && _reactors[i].IsAlive) _totalOutput += _reactors[i].Output;

            _shieldDraw = 0f;
            _shieldMax = 0f;
            for (int i = 0; i < _shields.Count; i++)
            {
                ShieldBehavior s = _shields[i];
                if (s != null && s.IsAlive) { _shieldDraw += s.Draw; _shieldMax += s.Contribution; }
            }

            // Shield is highest-priority consumer: powered iff output covers
            // its full draw.
            _shieldPowered = _shieldMax > 0f && _totalOutput >= _shieldDraw;

            if (!_shieldPowered) _shieldPoints = 0f;                    // field collapses
            else _shieldPoints = Mathf.Min(_shieldPoints, _shieldMax);  // clamp to (maybe reduced) ceiling
        }

        void Update()
        {
            _timeSinceDamage += Time.deltaTime;
            if (_shieldPowered && _shieldPoints < _shieldMax && _timeSinceDamage >= regenDelaySeconds)
                _shieldPoints = Mathf.Min(_shieldMax, _shieldPoints + regenRate * Time.deltaTime);
        }

        // Called from CubeDamage.ApplyAndLog for any hit on a construct
        // cube. Resets the regen timer (the construct was hit), absorbs
        // against the pool if powered, and returns the overflow that should
        // continue to HP. When the shield is down, returns the amount
        // unchanged (full overflow, no type modifier — the modifier is a
        // shield property).
        public float ApplyToShield(float amount, DamageType type)
        {
            _timeSinceDamage = 0f;
            if (!_shieldPowered || _shieldPoints <= 0f) return amount;

            float scaled = amount * TypeModifier(type);
            float absorbed = Mathf.Min(scaled, _shieldPoints);
            _shieldPoints -= absorbed;
            return scaled - absorbed;
        }

        float TypeModifier(DamageType type)
        {
            switch (type)
            {
                case DamageType.Projectile: return projectileModifier;
                case DamageType.Energy:     return energyModifier;
                default:                    return kineticModifier; // Kinetic
            }
        }
    }
}
```

- [ ] **Step 2: Compile-check** — `refresh_unity` + `read_console` filtered to `Assets/Scripts`. Expected: 0 errors.

---

### Task 3: `FlyController` wiring + scene component

**Files:**
- Modify: `Assets/Scripts/Fly/FlyController.cs`
- Modify: `Assets/Scenes/FlyScene.unity` (add `ConstructEnergySystem` to `CubeConstruct`)

- [ ] **Step 1: Add the collection lists + system reference fields**

After the `_spawnedThrusters` field declaration (`readonly List<ThrusterBehavior> _spawnedThrusters = new();`), add:

```csharp
        // Collected during BuildConstruct — every ReactorBehavior /
        // ShieldBehavior on the spawned construct, handed to the
        // ConstructEnergySystem in Start (same pattern as _spawnedWeapons).
        readonly List<ReactorBehavior> _spawnedReactors = new();
        readonly List<ShieldBehavior> _spawnedShields = new();

        // The construct-wide power/shield system (sibling on CubeConstruct).
        // Resolved in Start; RecomputePower() is called after each cube
        // death so power reflects lost reactors/shields.
        ConstructEnergySystem _energySystem;
```

- [ ] **Step 2: Collect reactor/shield cubes in `BuildConstruct`**

In the placement loop, immediately after the existing thruster-collection block (the `ThrusterBehavior thruster = go.GetComponent<ThrusterBehavior>();` … `_spawnedThrusters.Add(thruster);` block), add:

```csharp
                // Collect any ReactorBehavior / ShieldBehavior — same
                // collected-into-a-list pattern as weapons + thrusters.
                ReactorBehavior reactor = go.GetComponent<ReactorBehavior>();
                if (reactor != null) _spawnedReactors.Add(reactor);

                ShieldBehavior shield = go.GetComponent<ShieldBehavior>();
                if (shield != null) _spawnedShields.Add(shield);
```

- [ ] **Step 3: Resolve + register the energy system in `Start`**

In `Start`, immediately after the `ResolveRigidbody();` call, add:

```csharp
            // Resolve the construct-wide power/shield system (a scene
            // component on CubeConstruct; AddComponent fallback so
            // direct-Play without it still works) and hand it the collected
            // reactor/shield cubes.
            _energySystem = construct.GetComponent<ConstructEnergySystem>();
            if (_energySystem == null) _energySystem = construct.gameObject.AddComponent<ConstructEnergySystem>();
            _energySystem.RegisterCubes(_spawnedReactors, _spawnedShields);
```

- [ ] **Step 4: Recompute power after a cube death**

In `OnCubeDied`, after the `ResolveRigidbody();` call, add:

```csharp
            // Reactors/shields may have died (or been orphaned by the
            // cascade) — recompute the power balance + shield ceiling.
            if (_energySystem != null) _energySystem.RecomputePower();
```

- [ ] **Step 5: Add `ConstructEnergySystem` to `CubeConstruct` in FlyScene**

Via Unity MCP (load the scene, add the component, save). Load the `manage_components` schema first if needed.

```python
manage_scene(action="load", path="Assets/Scenes/FlyScene.unity")
# CubeConstruct is the Rigidbody/FlyCrashHandler owner (fileID 200000 in the scene).
manage_components(action="add", target="CubeConstruct",
    component_type="CubeFly.Fly.ConstructEnergySystem")
manage_scene(action="save")
```

Verify: `grep -n "ConstructEnergySystem" Assets/Scenes/FlyScene.unity` shows a MonoBehaviour record on CubeConstruct.

- [ ] **Step 6: Compile-check** — `refresh_unity` + `read_console`. Expected: 0 errors.

---

### Task 4: Shield interception in `CubeDamage` + commit

**Files:**
- Modify: `Assets/Scripts/Fly/CubeDamage.cs`

- [ ] **Step 1: Insert the shield step + route overflow to HP**

Replace this block at the top of `ApplyAndLog` (lines ~30-37):

```csharp
            CubeStats stats = context.Target;
            if (stats == null) return 0f;

            float hpBefore = stats.healthPoints;
            bool bypassArmour = (context.Flags & HitFlags.BypassArmour) != 0;
            float applied = bypassArmour
                ? stats.TakeRawDamage(context.Amount)
                : stats.TakeDamage(context.Amount);
```

with:

```csharp
            CubeStats stats = context.Target;
            if (stats == null) return 0f;

            // Shield interception: if the struck cube belongs to a construct
            // with a ConstructEnergySystem, route the hit through its shield
            // pool first. The shield absorbs (type-scaled) and returns the
            // overflow that continues to HP. World target cubes have no
            // energy system → full damage to HP, exactly as before.
            float toHp = context.Amount;
            ConstructEnergySystem energy = stats.GetComponentInParent<ConstructEnergySystem>();
            if (energy != null) toHp = energy.ApplyToShield(context.Amount, context.Type);

            float hpBefore = stats.healthPoints;
            bool bypassArmour = (context.Flags & HitFlags.BypassArmour) != 0;
            float applied = bypassArmour
                ? stats.TakeRawDamage(toHp)
                : stats.TakeDamage(toHp);
```

- [ ] **Step 2: Add shield info to the two log lines**

In both `Debug.unityLogger.Log(...)` calls, change the leading text from
`$"Hit '{stats.name}' for {applied:F1} damage "` to include the shield
overflow. Replace the bypassArmour branch's log with:

```csharp
                Debug.unityLogger.Log(context.SourceTag,
                    $"Hit '{stats.name}' for {applied:F1} damage " +
                    $"(raw {context.Amount:F1}, to-HP {toHp:F1}, type {context.Type}, armour bypassed). " +
                    $"HP: {hpBefore:F1} → {stats.healthPoints:F1}.");
```

and the armour branch's log with:

```csharp
                Debug.unityLogger.Log(context.SourceTag,
                    $"Hit '{stats.name}' for {applied:F1} damage " +
                    $"(raw {context.Amount:F1}, to-HP {toHp:F1}, type {context.Type}, AV {stats.armourValue:F1}). " +
                    $"HP: {hpBefore:F1} → {stats.healthPoints:F1}.");
```

(When no shield is present `toHp == context.Amount`, so the log reads
naturally for world targets too.)

- [ ] **Step 3: Compile-check** — `refresh_unity` + `read_console`. Expected: 0 errors.

- [ ] **Step 4: Commit 1**

```bash
cd "/Users/anon/My project"
git add Assets/Scripts/Fly/ReactorBehavior.cs Assets/Scripts/Fly/ReactorBehavior.cs.meta \
        Assets/Scripts/Fly/ShieldBehavior.cs Assets/Scripts/Fly/ShieldBehavior.cs.meta \
        Assets/Scripts/Fly/ConstructEnergySystem.cs Assets/Scripts/Fly/ConstructEnergySystem.cs.meta \
        Assets/Scripts/Fly/FlyController.cs \
        Assets/Scripts/Fly/CubeDamage.cs \
        Assets/Scenes/FlyScene.unity
git commit -m "$(cat <<'EOF'
Add ConstructEnergySystem power/shield core

ReactorBehavior + ShieldBehavior passive descriptors (the
ThrusterBehavior pattern); a ConstructEnergySystem on CubeConstruct
owning the net-rate power balance, the single shared shield pool,
regen, and the all-or-nothing shield consumer. FlyController collects
the new cubes in BuildConstruct, registers them in Start, and
recomputes power after each cube death. CubeDamage.ApplyAndLog routes
every construct hit through the shield (type-scaled, overflow to HP)
before applying to HP. No cubes exist yet — verified once the Reactor
/ Shield shapes land in commit 2.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Commit 2 — Reactor + Shield cubes

### Task 5: Solid-cylinder mesh + `ReactorMeshAuthor`

**Files:**
- Modify: `Assets/Scripts/Core/PrimitiveMeshes.cs`
- Create: `Assets/Scripts/Core/ReactorMeshAuthor.cs` (+ `.cs.meta`, guid `a0a0a0a0000000260000000000000026`, executionOrder 0)

- [ ] **Step 1: Add `SolidCylinder` to `PrimitiveMeshes`**

Add the `_solidCylinder` cache field next to the others (`static Mesh _cone;`):

```csharp
        static Mesh _solidCylinder;
```

Add this property + builder after the `Cone` builder (`BuildCone`), before the closing brace of the class:

```csharp
        // 1×1×1 solid cylinder centred at the origin. Axis along +Y;
        // radius 0.5 (fills the cell horizontally), height 1 (fills it
        // vertically). Same outer dimensions and the same single valid
        // attachment face (-Y) as the hollow cylinder weapon, but solid:
        // capped top + bottom discs, no inner wall. Used by the Reactor
        // cube. Wall winding matches HollowCylinder's outer wall; cap
        // windings match Cone's base (-Y) / its reverse (+Y) for outward
        // normals in Unity's left-handed CW-front convention.
        public static Mesh SolidCylinder
        {
            get
            {
                if (_solidCylinder == null) _solidCylinder = BuildSolidCylinder();
                return _solidCylinder;
            }
        }

        static Mesh BuildSolidCylinder()
        {
            const int N = 32;
            const float h = 0.5f;
            const float r = 0.5f;

            // Vertex layout (4N + 2):
            //   [0 .. N-1]     wall top ring     (smooth radial)
            //   [N .. 2N-1]    wall bottom ring  (smooth radial)
            //   [2N .. 3N-1]   top cap ring      (flat +Y)
            //   [3N .. 4N-1]   bottom cap ring   (flat -Y)
            //   [4N]           top centre        (flat +Y)
            //   [4N+1]         bottom centre     (flat -Y)
            Vector3[] verts = new Vector3[4 * N + 2];
            for (int i = 0; i < N; i++)
            {
                float theta = i * (2f * Mathf.PI / N);
                float c = Mathf.Cos(theta);
                float s = Mathf.Sin(theta);
                Vector3 top = new Vector3(r * c,  h, r * s);
                Vector3 bot = new Vector3(r * c, -h, r * s);
                verts[i]         = top;
                verts[N + i]     = bot;
                verts[2 * N + i] = top;
                verts[3 * N + i] = bot;
            }
            verts[4 * N]     = new Vector3(0f,  h, 0f);
            verts[4 * N + 1] = new Vector3(0f, -h, 0f);

            // 4 tris/segment: 2 wall + 1 top cap + 1 bottom cap.
            int[] tris = new int[12 * N];
            int t = 0;
            for (int i = 0; i < N; i++)
            {
                int j = (i + 1) % N;

                // Outer wall — normal radially outward.
                tris[t++] = i;          tris[t++] = j;          tris[t++] = N + j;
                tris[t++] = i;          tris[t++] = N + j;      tris[t++] = N + i;

                // Top cap (+Y) — fan from top centre, reversed vs the -Y base.
                tris[t++] = 4 * N;      tris[t++] = 2 * N + j;  tris[t++] = 2 * N + i;

                // Bottom cap (-Y) — fan from bottom centre.
                tris[t++] = 4 * N + 1;  tris[t++] = 3 * N + i;  tris[t++] = 3 * N + j;
            }

            Mesh m = new Mesh { name = "SolidCylinder" };
            m.vertices = verts;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
```

- [ ] **Step 2: Write `ReactorMeshAuthor.cs`**

```csharp
using UnityEngine;

namespace CubeFly.Core
{
    // Assigns the runtime-generated solid-cylinder mesh to this
    // GameObject's MeshFilter (and MeshCollider, if present) on Awake,
    // only when the slot is empty. Mirror of CylinderMeshAuthor for the
    // Reactor cube's solid cylinder.
    [RequireComponent(typeof(MeshFilter))]
    public class ReactorMeshAuthor : MonoBehaviour
    {
        void Awake()
        {
            Mesh mesh = PrimitiveMeshes.SolidCylinder;

            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh == null) mf.sharedMesh = mesh;

            MeshCollider mc = GetComponent<MeshCollider>();
            if (mc != null && mc.sharedMesh == null) mc.sharedMesh = mesh;
        }
    }
}
```

- [ ] **Step 3: Write `ReactorMeshAuthor.cs.meta`** (standard block, guid `a0a0a0a0000000260000000000000026`, executionOrder 0).

- [ ] **Step 4: Compile-check** — `refresh_unity` + `read_console`. Expected: 0 errors. (The new behaviour scripts from commit 1 + these must all compile before authoring prefabs that reference them by type name.)

---

### Task 6: Materials + MaterialDefinitions

**Files:**
- Create: `Assets/Materials/ReactorMat.mat`, `Assets/Materials/ShieldMat.mat`
- Create: `Assets/Materials/Defs/ReactorMatDef.asset`, `Assets/Materials/Defs/ShieldMatDef.asset`

- [ ] **Step 1: Create the two URP/Lit materials via Unity MCP**

Load the `manage_material` schema, then create both with a distinct base + emission colour (reactor warm amber, shield cyan). Match the project's existing URP/Lit materials (the Reactor reuses the look of the cylinder weapon's material family).

```python
manage_material(action="create", path="Assets/Materials/ReactorMat.mat", shader="Universal Render Pipeline/Lit")
manage_material(action="set_property", path="Assets/Materials/ReactorMat.mat",
    properties={"_BaseColor": [1.0, 0.62, 0.2, 1.0], "_EmissionColor": [1.0, 0.5, 0.1, 1.0], "_EmissionEnabled": true})

manage_material(action="create", path="Assets/Materials/ShieldMat.mat", shader="Universal Render Pipeline/Lit")
manage_material(action="set_property", path="Assets/Materials/ShieldMat.mat",
    properties={"_BaseColor": [0.3, 0.8, 1.0, 1.0], "_EmissionColor": [0.2, 0.7, 1.0, 1.0], "_EmissionEnabled": true})
```

(Exact property keys vary by Unity/URP version — if `set_property` rejects a key, inspect an existing `.mat` under `Assets/Materials/` via the components/material resource and match its property names. The colours are the design intent; the renderer just needs to read distinct base colours since `MaterialDefinition.SwatchColor` returns `material.color`.)

- [ ] **Step 2: Create the two `MaterialDefinition` ScriptableObjects via Unity MCP**

Load the `manage_scriptable_object` schema. Create each as a `MaterialDefinition` and set its fields + the `material` reference by asset path. Template: `Assets/Materials/Defs/ThrusterMatDef.asset`.

```python
manage_scriptable_object(action="create", path="Assets/Materials/Defs/ReactorMatDef.asset",
    script_class="CubeFly.Core.MaterialDefinition")
manage_scriptable_object(action="set_property", path="Assets/Materials/Defs/ReactorMatDef.asset",
    properties={"displayName": "Reactor", "healthPoints": 60, "armourValue": 5, "mass": 10,
                "material": {"path": "Assets/Materials/ReactorMat.mat"}})

manage_scriptable_object(action="create", path="Assets/Materials/Defs/ShieldMatDef.asset",
    script_class="CubeFly.Core.MaterialDefinition")
manage_scriptable_object(action="set_property", path="Assets/Materials/Defs/ShieldMatDef.asset",
    properties={"displayName": "Shield", "healthPoints": 50, "armourValue": 5, "mass": 5,
                "material": {"path": "Assets/Materials/ShieldMat.mat"}})
```

**Note on stat duplication:** `MaterialDefinition.ApplyTo` writes HP/AV/mass into the cube's `CubeStats` at spawn, so these coupled-material stat values (reactor 60/5/10, shield 50/5/5) are the source of truth for the spawned cubes' stats. The prefab `CubeStats` defaults in Task 7 should match but will be overwritten by `ApplyTo` regardless.

- [ ] **Step 3: Verify** — `refresh_unity` + `read_console`. Both `.asset` files exist and reference their `.mat`. No errors.

---

### Task 7: Reactor + Shield prefabs

**Files:**
- Create: `Assets/Prefabs/PlacedReactor.prefab`, `Assets/Prefabs/PlacedShield.prefab`

Both are authored via Unity MCP (create GameObject(s) in the open scene, add components, save as prefab). Template: `Assets/Prefabs/PlacedThruster.prefab` (layer 6, components: Transform, MeshFilter, MeshRenderer, BoxCollider, PlacedCubeData, CubeStats, mesh author, behaviour).

- [ ] **Step 1: Author `PlacedReactor` (solid cylinder, full-cell)**

```python
manage_scene(action="load", path="Assets/Scenes/FlyScene.unity")   # scratch host scene
manage_gameobject(action="create", name="PlacedReactor")
manage_gameobject(action="modify", target="PlacedReactor", layer=6)   # PlacedCube layer
# Components: MeshFilter (mesh left empty — ReactorMeshAuthor fills it), MeshRenderer (ReactorMat),
# BoxCollider 1×1×1, CubeStats, PlacedCubeData, ReactorMeshAuthor, ReactorBehavior.
manage_components(action="add", target="PlacedReactor", component_type="MeshFilter")
manage_components(action="add", target="PlacedReactor", component_type="MeshRenderer")
manage_components(action="set_property", target="PlacedReactor", component_type="MeshRenderer",
    property="material", value={"path": "Assets/Materials/ReactorMat.mat"})
manage_components(action="add", target="PlacedReactor", component_type="BoxCollider",
    properties={"size": [1, 1, 1], "center": [0, 0, 0]})
manage_components(action="add", target="PlacedReactor", component_type="CubeFly.Core.CubeStats",
    properties={"healthPoints": 60, "armourValue": 5, "mass": 10})
manage_components(action="add", target="PlacedReactor", component_type="CubeFly.Build.PlacedCubeData")
manage_components(action="add", target="PlacedReactor", component_type="CubeFly.Core.ReactorMeshAuthor")
manage_components(action="add", target="PlacedReactor", component_type="CubeFly.Fly.ReactorBehavior",
    properties={"output": 10})
manage_gameobject(action="modify", target="PlacedReactor", save_as_prefab=true,
    prefab_path="Assets/Prefabs/PlacedReactor.prefab")
# Remove the scratch instance from the scene afterward (do NOT save the scene).
manage_gameobject(action="delete", target="PlacedReactor")
```

- [ ] **Step 2: Author `PlacedShield` (0.5 cube, grounded + centred on −Y)**

The shield's visible mesh + its collider are offset to sit flush against
the cell's −Y face and centred in X/Z. Use a child for the scaled mesh so
the root stays at cell-centre scale 1; author the BoxCollider on the root
at the offset.

```python
manage_gameobject(action="create", name="PlacedShield")
manage_gameobject(action="modify", target="PlacedShield", layer=6)
# Root collider: half-size, offset to the -Y half of the cell.
manage_components(action="add", target="PlacedShield", component_type="BoxCollider",
    properties={"size": [0.5, 0.5, 0.5], "center": [0, -0.25, 0]})
manage_components(action="add", target="PlacedShield", component_type="CubeFly.Core.CubeStats",
    properties={"healthPoints": 50, "armourValue": 5, "mass": 5})
manage_components(action="add", target="PlacedShield", component_type="CubeFly.Build.PlacedCubeData")
manage_components(action="add", target="PlacedShield", component_type="CubeFly.Fly.ShieldBehavior",
    properties={"draw": 20, "contribution": 50})
# Child holds the scaled + offset built-in cube mesh.
manage_gameobject(action="create", name="ShieldMesh", parent="PlacedShield")
manage_gameobject(action="modify", target="ShieldMesh",
    position=[0, -0.25, 0], scale=[0.5, 0.5, 0.5])   # local; flush against -Y, centred X/Z
manage_components(action="add", target="ShieldMesh", component_type="MeshFilter")
# Assign the built-in Unity cube mesh to the MeshFilter:
manage_components(action="set_property", target="ShieldMesh", component_type="MeshFilter",
    property="sharedMesh", value={"guid": "0000000000000000e000000000000000", "fileID": 10202})
manage_components(action="add", target="ShieldMesh", component_type="MeshRenderer")
manage_components(action="set_property", target="ShieldMesh", component_type="MeshRenderer",
    property="material", value={"path": "Assets/Materials/ShieldMat.mat"})
manage_gameobject(action="modify", target="PlacedShield", save_as_prefab=true,
    prefab_path="Assets/Prefabs/PlacedShield.prefab")
manage_gameobject(action="delete", target="PlacedShield")
```

(If `set_property` for the built-in cube mesh reference is awkward through
MCP, an alternative is a small `ShieldMeshAuthor`-free approach: assign
Unity's primitive cube by creating the child as `manage_gameobject(action="create",
primitive_type="Cube")` then reparenting + scaling/offsetting it — the
primitive already carries the cube MeshFilter/MeshRenderer/BoxCollider; if
so, delete that child's BoxCollider since the root carries the gameplay
collider.)

- [ ] **Step 3: Verify both prefabs**

`refresh_unity` + `read_console` (0 errors). Confirm both `.prefab` files
exist under `Assets/Prefabs/`. Open each in the Editor or screenshot to
confirm: reactor renders as a solid cylinder filling its cell; shield
renders as a small cube grounded at the bottom of its cell, centred.

---

### Task 8: Shape definitions + registry append + commit

**Files:**
- Create: `Assets/Shapes/ShapeUtilityReactor.asset`, `Assets/Shapes/ShapeUtilityShield.asset`
- Modify: `Assets/Shapes/ShapeRegistry.asset`

- [ ] **Step 1: Create the two `ShapeDefinition`s via Unity MCP**

Both are **Utility** category (`category: 2`), single valid face `−Y`
(`faceNegY` true, all others false), coupled to their MatDef + prefab.
Template: `Assets/Shapes/ShapeUtilityThruster.asset`.

```python
manage_scriptable_object(action="create", path="Assets/Shapes/ShapeUtilityReactor.asset",
    script_class="CubeFly.Core.ShapeDefinition")
manage_scriptable_object(action="set_property", path="Assets/Shapes/ShapeUtilityReactor.asset",
    properties={"displayName": "Reactor", "category": 2,
                "prefab": {"path": "Assets/Prefabs/PlacedReactor.prefab"},
                "coupledMaterial": {"path": "Assets/Materials/Defs/ReactorMatDef.asset"},
                "faceNegX": false, "facePosX": false, "faceNegY": true,
                "facePosY": false, "faceNegZ": false, "facePosZ": false})

manage_scriptable_object(action="create", path="Assets/Shapes/ShapeUtilityShield.asset",
    script_class="CubeFly.Core.ShapeDefinition")
manage_scriptable_object(action="set_property", path="Assets/Shapes/ShapeUtilityShield.asset",
    properties={"displayName": "Shield", "category": 2,
                "prefab": {"path": "Assets/Prefabs/PlacedShield.prefab"},
                "coupledMaterial": {"path": "Assets/Materials/Defs/ShieldMatDef.asset"},
                "faceNegX": false, "facePosX": false, "faceNegY": true,
                "facePosY": false, "faceNegZ": false, "facePosZ": false})
```

- [ ] **Step 2: Append both shapes to `ShapeRegistry`**

Read the new shapes' GUIDs from their `.meta` files:

```bash
cd "/Users/anon/My project"
grep -h guid Assets/Shapes/ShapeUtilityReactor.asset.meta Assets/Shapes/ShapeUtilityShield.asset.meta
```

Then append two entries to the `shapes:` list at the end of
`Assets/Shapes/ShapeRegistry.asset` (after the existing 5th entry, the
thruster `af89f47bd56184526a01562fb4b87d29`):

```yaml
  - {fileID: 11400000, guid: <ShapeUtilityReactor guid>, type: 2}
  - {fileID: 11400000, guid: <ShapeUtilityShield guid>, type: 2}
```

(The two GUIDs come from the grep above — fill them in exactly.)

- [ ] **Step 3: Verify** — `refresh_unity` + `read_console` (0 errors). The
Build toolbar's **Utilities** flyout should now list Thruster + Reactor +
Shield (verified at play-test).

- [ ] **Step 4: Commit 2**

```bash
cd "/Users/anon/My project"
git add Assets/Scripts/Core/PrimitiveMeshes.cs \
        Assets/Scripts/Core/ReactorMeshAuthor.cs Assets/Scripts/Core/ReactorMeshAuthor.cs.meta \
        Assets/Materials/ReactorMat.mat Assets/Materials/ReactorMat.mat.meta \
        Assets/Materials/ShieldMat.mat Assets/Materials/ShieldMat.mat.meta \
        Assets/Materials/Defs/ReactorMatDef.asset Assets/Materials/Defs/ReactorMatDef.asset.meta \
        Assets/Materials/Defs/ShieldMatDef.asset Assets/Materials/Defs/ShieldMatDef.asset.meta \
        Assets/Prefabs/PlacedReactor.prefab Assets/Prefabs/PlacedReactor.prefab.meta \
        Assets/Prefabs/PlacedShield.prefab Assets/Prefabs/PlacedShield.prefab.meta \
        Assets/Shapes/ShapeUtilityReactor.asset Assets/Shapes/ShapeUtilityReactor.asset.meta \
        Assets/Shapes/ShapeUtilityShield.asset Assets/Shapes/ShapeUtilityShield.asset.meta \
        Assets/Shapes/ShapeRegistry.asset
git commit -m "$(cat <<'EOF'
Add Reactor + Shield cubes

Reactor: a solid-cylinder Utility shape (new PrimitiveMeshes.SolidCylinder
+ ReactorMeshAuthor), output 10 / mass 10. Shield: a half-size cube
grounded + centred on its single -Y mount face, draw 20 / mass 5 /
+50 pool. Both append to ShapeRegistry's Utilities flyout with coupled
materials. Two reactors power one shield — a deliberately heavy build
decision.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Commit 3 — HUD

### Task 9: `FlyShieldIndicator` + scene attach + commit

**Files:**
- Create: `Assets/Scripts/Fly/FlyShieldIndicator.cs` (+ `.cs.meta`, guid `a0a0a0a0000000270000000000000027`, executionOrder 100)
- Modify: `Assets/Scenes/FlyScene.unity` (add `FlyShieldIndicator` to the `FlyHUD` GameObject)

- [ ] **Step 1: Write `FlyShieldIndicator.cs`**

```csharp
using CubeFly.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CubeFly.Fly
{
    // Bottom-left HUD: a shield bar (cyan fill = ShieldPoints / ShieldMax)
    // stacked above the HP label, plus a "Power: +N / −N" readout. Both
    // read the construct's ConstructEnergySystem. The bar is hidden when
    // the construct has no shield cubes; the readout is hidden when it has
    // no reactor/shield cubes. Built under FlyHud.Instance.Root.
    //
    // DefaultExecutionOrder(100) so Start runs after FlyController.Start
    // (which builds the construct + registers the energy system), mirroring
    // FlyHpIndicator.
    [DefaultExecutionOrder(100)]
    public class FlyShieldIndicator : MonoBehaviour
    {
        [Header("Layout (bottom-left, above HP)")]
        [SerializeField] Vector2 barAnchoredPosition = new Vector2(20f, 100f);
        [SerializeField] Vector2 barSize = new Vector2(180f, 16f);
        [SerializeField] Vector2 powerLabelAnchoredPosition = new Vector2(20f, 122f);
        [SerializeField] int powerFontSize = 18;

        [Header("Colours")]
        [SerializeField] Color shieldFillColor = new Color(0.3f, 0.8f, 1f, 0.95f);
        [SerializeField] Color shieldFrameColor = new Color(0.05f, 0.12f, 0.16f, 0.85f);
        [SerializeField] Color shieldDownColor = new Color(0.4f, 0.4f, 0.45f, 0.6f);
        [SerializeField] Color powerPositiveColor = new Color(0.4f, 1f, 0.5f, 1f);
        [SerializeField] Color powerNegativeColor = new Color(1f, 0.4f, 0.35f, 1f);

        ConstructEnergySystem _energy;
        RectTransform _frame;
        RectTransform _fill;
        Image _fillImage;
        Text _powerLabel;

        const string TAG = "FlyShield";

        void Awake() => BuildUI();

        void Start()
        {
            _energy = FindAnyObjectByType<ConstructEnergySystem>();
            if (_energy == null)
                Debug.unityLogger.Log(TAG, "No ConstructEnergySystem in scene; shield HUD stays hidden.");
        }

        void Update()
        {
            bool hasShield = _energy != null && _energy.HasShieldCubes;
            bool hasPower  = _energy != null && _energy.HasPowerCubes;

            if (_frame != null && _frame.gameObject.activeSelf != hasShield)
                _frame.gameObject.SetActive(hasShield);
            if (_powerLabel != null) _powerLabel.enabled = hasPower;
            if (!hasPower) return;

            if (hasShield)
            {
                float frac = _energy.ShieldMax > 0f
                    ? Mathf.Clamp01(_energy.ShieldPoints / _energy.ShieldMax) : 0f;
                _fill.sizeDelta = new Vector2(barSize.x * frac, barSize.y);
                _fillImage.color = _energy.ShieldActive ? shieldFillColor : shieldDownColor;
            }

            float net = _energy.NetPower;
            _powerLabel.text = $"Power: {(net >= 0f ? "+" : "")}{net:F0}";
            _powerLabel.color = net >= 0f ? powerPositiveColor : powerNegativeColor;
        }

        void BuildUI()
        {
            RectTransform root = FlyHud.Instance.Root;
            int uiLayer = LayerMask.NameToLayer("UI");

            // Bar frame (background), bottom-left anchored.
            GameObject frameGO = new GameObject("ShieldBarFrame",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            if (uiLayer >= 0) frameGO.layer = uiLayer;
            frameGO.transform.SetParent(root, false);
            _frame = (RectTransform)frameGO.transform;
            _frame.anchorMin = _frame.anchorMax = _frame.pivot = new Vector2(0f, 0f);
            _frame.sizeDelta = barSize;
            _frame.anchoredPosition = barAnchoredPosition;
            Image frameImg = frameGO.GetComponent<Image>();
            frameImg.color = shieldFrameColor;
            frameImg.raycastTarget = false;

            // Fill — left-anchored child so width = fraction shrinks from the right.
            GameObject fillGO = new GameObject("ShieldBarFill",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            if (uiLayer >= 0) fillGO.layer = uiLayer;
            fillGO.transform.SetParent(frameGO.transform, false);
            _fill = (RectTransform)fillGO.transform;
            _fill.anchorMin = _fill.anchorMax = _fill.pivot = new Vector2(0f, 0f);
            _fill.sizeDelta = barSize;
            _fill.anchoredPosition = Vector2.zero;
            _fillImage = fillGO.GetComponent<Image>();
            _fillImage.color = shieldFillColor;
            _fillImage.raycastTarget = false;

            // Power readout label.
            _powerLabel = UIStyle.BuildLabel(root, "Power: +0", fontSize: powerFontSize);
            _powerLabel.alignment = TextAnchor.LowerLeft;
            RectTransform plRT = (RectTransform)_powerLabel.transform;
            plRT.anchorMin = plRT.anchorMax = plRT.pivot = new Vector2(0f, 0f);
            plRT.sizeDelta = new Vector2(220f, 28f);
            plRT.anchoredPosition = powerLabelAnchoredPosition;
        }
    }
}
```

- [ ] **Step 2: Write `FlyShieldIndicator.cs.meta`** (standard block, guid `a0a0a0a0000000270000000000000027`, **executionOrder 100**).

- [ ] **Step 3: Compile-check** — `refresh_unity` + `read_console`. Expected: 0 errors.

- [ ] **Step 4: Add `FlyShieldIndicator` to the `FlyHUD` GameObject**

```python
manage_scene(action="load", path="Assets/Scenes/FlyScene.unity")
manage_components(action="add", target="FlyHUD", component_type="CubeFly.Fly.FlyShieldIndicator")
manage_scene(action="save")
```

Verify: `grep -n "FlyShieldIndicator" Assets/Scenes/FlyScene.unity` shows a MonoBehaviour record on the `FlyHUD` GameObject.

- [ ] **Step 5: Compile-check + commit 3**

`refresh_unity` + `read_console` (0 errors), then:

```bash
cd "/Users/anon/My project"
git add Assets/Scripts/Fly/FlyShieldIndicator.cs Assets/Scripts/Fly/FlyShieldIndicator.cs.meta \
        Assets/Scenes/FlyScene.unity
git commit -m "$(cat <<'EOF'
Add FlyShieldIndicator HUD (shield bar + power readout)

Bottom-left shield bar (cyan fill = ShieldPoints / ShieldMax, greyed
when the field is collapsed) above the HP label, plus a Power: +N / −N
readout (green when net ≥ 0, red when under-powered). Both read the
construct's ConstructEnergySystem and hide themselves when the
construct has no shield / power cubes. Built under FlyHud.Instance.Root
on the FlyHUD GameObject.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 10: Push, open PR, request Copilot review

- [ ] **Step 1: Push + PR**

```bash
cd "/Users/anon/My project"
git push -u origin feat/power-and-energy
gh pr create --title "Power & Energy (reactor + shield)" --body "$(cat <<'EOF'
## Summary

- New `ConstructEnergySystem` on `CubeConstruct` — instantaneous net-rate power balance, single shared shield pool, regen, all-or-nothing shield consumer (laser slots in as a lower-priority consumer later).
- `ReactorBehavior` / `ShieldBehavior` passive descriptors (the `ThrusterBehavior` pattern); `FlyController` collects them, registers them, and recomputes power on cube death.
- `CubeDamage.ApplyAndLog` routes every construct hit through the shield (type-scaled: projectile ×0.9, energy ×1.1, kinetic ×1.0; overflow spills to HP) before applying to HP. World targets unaffected.
- New cubes: **Reactor** (solid cylinder via `PrimitiveMeshes.SolidCylinder`, output 10 / mass 10) and **Shield** (half-size grounded+centred cube, draw 20 / mass 5 / +50 pool). Two reactors power one shield.
- `FlyShieldIndicator` adds a shield bar + power readout to `FlyHud`.

Three commits in dependency order: power core → reactor/shield cubes → HUD. Backward compatible — a construct with no reactor/shield cubes has no power system and flies as before; old saves load unchanged (no schema bump).

Spec: `docs/superpowers/specs/2026-05-21-power-and-energy-design.md`
Plan: `docs/superpowers/plans/2026-05-21-power-and-energy.md`

## Test plan

- [ ] Compile clean per commit (verified during implementation).
- [ ] Build: Reactor + Shield appear in the Utilities flyout; placement obeys the single −Y mount face (mount on a surface, nothing stacks on top); reactor = solid cylinder, shield = small grounded+centred cube; mass cap counts them; selected-stats readout shows their stats.
- [ ] Power: 1 reactor + 1 shield reads `Power: −10`, shield bar stays empty/greyed (offline). Add a 2nd reactor → `Power: +0`, shield bar fills to +50.
- [ ] Shield absorption: bullets (projectile ×0.9) drain the bar; a big hit overflows to HP; crash damage (kinetic) drains it too; the bar refills 5 s after the last hit at ~20/s.
- [ ] Cascade: destroy a reactor mid-flight so net goes negative → shield bar collapses to 0, damage flows to HP.
- [ ] Backward compat: a no-reactor/no-shield construct shows no shield bar / power readout and flies as before; an old save loads unchanged.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
PR_NUM=$(gh pr view --json number -q .number)
gh api repos/entewurzelauskuh/CosmicScrapClub/pulls/$PR_NUM/requested_reviewers \
    -X POST -f "reviewers[]=copilot-pull-request-reviewer[bot]" 2>/dev/null || true
```

- [ ] **Step 2: Manual play-test (user)** — the user runs the Test-plan checklist on the main project root, then triages Copilot review before merge.

---

## Self-review

- **Spec coverage:** ConstructEnergySystem (Tasks 2-4) ✓; reactor/shield behaviours (Task 1) ✓; net-rate power + cascade (Task 2) ✓; shield mechanics + type modifiers + overflow + collapse + regen (Task 2) ✓; damage interception (Task 4) ✓; solid-cylinder reactor (Tasks 5,7,8) ✓; half-cube shield (Tasks 7,8) ✓; Utility category + −Y face (Task 8) ✓; coupled materials (Task 6) ✓; HUD shield bar + power readout (Task 9) ✓; cube-death recompute (Task 3) ✓; backward-compat (no scene/save changes for power-less constructs) ✓.
- **Placeholder scan:** the only `<...>` markers are the two ShapeRegistry GUIDs in Task 8 Step 2, which are filled by the immediately-preceding grep — an actionable read-back, not a vague TBD. MCP property-key caveats are noted where the API may vary by version.
- **Type consistency:** `Output`, `Draw`, `Contribution`, `IsAlive`, `RegisterCubes(IEnumerable<ReactorBehavior>, IEnumerable<ShieldBehavior>)`, `RecomputePower()`, `ApplyToShield(float, DamageType)`, `ShieldPoints`/`ShieldMax`/`NetPower`/`ShieldActive`/`HasShieldCubes`/`HasPowerCubes` — used identically across Tasks 1-4 and 9. `PrimitiveMeshes.SolidCylinder` defined in Task 5, consumed by `ReactorMeshAuthor` (Task 5) and the reactor prefab (Task 7).
