# Thruster PR 1b — Placeable Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Thruster cube placeable in the build scene — a new `Cone` runtime mesh, a `ThrusterMeshAuthor`, the `ShapeCategory.Utility` category with the `weaponMaterial`→`coupledMaterial` refactor, the `GameData` save-path generalization, and the four new assets (`ThrusterMat`, `ThrusterMatDef`, `PlacedThruster.prefab`, `ShapeUtilityThruster.asset`) registered in `ShapeRegistry`. After PR 1b a "Utilities" toolbar category lists "Thruster"; placed thrusters render as a cone, attach only on their circular base, and carry `ThrusterMatDef` stats. They are **inert in flight** — no `ThrusterBehavior` script (that is PR 2).

**Architecture:** The Thruster is the first **Utility** shape — neither armour nor weapon. `ShapeCategory` gains a third member `Utility`; `ShapeDefinition.weaponMaterial` is renamed `coupledMaterial` (Weapon *and* Utility shapes share it). The PR 1a `CategoryFlyout` is already category-agnostic — `BuildToolbarController` builds one `CategoryFlyout` per non-armour `ShapeCategory` in the registry, so the Utilities button + flyout are a **data-only addition**: registering `ShapeUtilityThruster` in `ShapeRegistry` produces the Utilities flyout with no toolbar-code change beyond a one-line `CategoryButtonLabel` case. The cone geometry is a `PrimitiveMeshes.Cone` static cached mesh in the mould of `HollowCylinder` / `SquarePyramid`; `ThrusterMeshAuthor` assigns it on `Awake` exactly as `CylinderMeshAuthor` does. The save layer special-cases coupled-material shapes by `UsesCoupledMaterial` (was `IsWeapon`) so a thruster round-trips by shape name with a `-1` material-index sentinel.

**Tech Stack:** Unity 6.3 LTS (6000.3.x) / URP, MonoBehaviour-only C#, pure C# (no DOTS), legacy `UnityEngine.UI`. Compilation + console checks via Unity MCP; asset creation (material, ScriptableObjects, prefab) via Unity MCP tools; editor build-scene check by hand. No unit-test framework.

---

## Starting state

- Branch: **`feat/thruster-placeable`**, stacked on `feat/thruster-cube` (which carried PR 1a). PR 1a already shipped — it extracted the Weapons-button machinery into `Assets/Scripts/Build/CategoryFlyout.cs`, and `BuildToolbarController` now builds one `CategoryFlyout` per non-armour `ShapeCategory` found in the registry (partitioning logic, `categoryOrder`, `_categoryFlyouts`, `FindCategoryFlyout`, `CloseFlyoutsExcept`, `AnyOtherFlyoutPinned`, `CategoryButtonLabel` are all in place). Do not re-do that refactor.
- **PR 2 (boost mechanic)** — `ThrusterBehavior`, the Boost resource, Left-Ctrl input, flight-force integration, HUD bar — is a separate later plan. **Out of scope here.** PR 1b adds no fly-side code; `PlacedThruster.prefab` gets *no* `ThrusterBehavior` (the script does not exist until PR 2).
- Today the `ShapeRegistry` holds four shapes — `ShapeCube`, `ShapeSlope` (Armour), `ShapeWeaponPyramid`, `ShapeWeaponCylinder` (Weapon). After PR 1b it holds five, with `ShapeUtilityThruster` (Utility) appended last.
- `ShapeDefinition` today exposes `weaponMaterial`, `ResolveMaterial`, `IsWeapon`; `ShapeCategory` is `{ Armour, Weapon }`. `CategoryFlyout.cs`, `BuildToolbarController.cs`, and `GameData.cs` all read `weaponMaterial` / `IsWeapon` and must be updated in lock-step (Task 3 and Task 4).

## Conventions for every task

- **Compile/console check:** after creating or editing any `.cs` file, refresh Unity and wait for the domain reload to finish, then read the console filtered to errors. Concretely: `mcp__UnityMCP__refresh_unity` with `compile="request"`, `mode="force"`, `scope="all"`, `wait_for_ready=true`; poll the `mcpforunity://editor/state` resource until `is_compiling=false` and `ready_for_tools=true`; then `mcp__UnityMCP__read_console` with `action="get"`, `types=["error"]`, `count=50`. **Zero compile errors before proceeding.** (MCP `Client handler exited` lines are infrastructure noise, not errors.)
- **The rename is atomic.** The `weaponMaterial`→`coupledMaterial` field rename and *every* reference update (`ShapeDefinition.cs`, `CategoryFlyout.cs`, `BuildToolbarController.cs`) land in **one task (Task 3)** so the project never sits in a non-compiling state. `GameData.cs`'s `IsWeapon`→`UsesCoupledMaterial` change is Task 4 — `IsWeapon` is *kept* on `ShapeDefinition`, so Task 3 compiling does not depend on Task 4.
- **`.meta` for new scripts (C1/C2 quirk):** Unity's auto-generated `.meta` stub for a new `.cs` file omits the full `MonoImporter` block this project expects. Each new script gets a hand-written canonical `.meta` containing a complete `MonoImporter` block — see Task 2 Step 2 for the exact template (it mirrors `Assets/Scripts/Core/CylinderMeshAuthor.cs.meta`).
- **Asset creation via Unity MCP.** New `.mat` / `.asset` / `.prefab` files are created at execution time with Unity MCP tools (`manage_material`, `manage_asset`, `manage_prefabs`, `manage_scriptable_object`, `execute_code`), not by hand-writing YAML — Unity then writes correct `.meta` GUIDs itself. Each asset task below gives the exact target field values (mirroring the existing cylinder/pyramid weapon assets) and the recommended tool. If a tool cannot set a field, fall back to `execute_code` with an editor-side C# snippet (`AssetDatabase` + `SerializedObject`).
- **Commit** at the end of each task with the exact `git add` paths shown, on branch `feat/thruster-placeable`. Do not amend; each task is a fresh commit. Commit messages are imperative ("Add …", "Rename …"), matching the repo style.

---

## Task 1: Add `PrimitiveMeshes.Cone`

**Files:**
- Modify: `Assets/Scripts/Core/PrimitiveMeshes.cs`

Add a fourth runtime-generated mesh — a cone — alongside `TriangularPrism`, `SquarePyramid`, `HollowCylinder`. The cone fits a 1×1×1 cell: circular base at `y=-0.5` radius `0.5`, apex at `(0,+0.5,0)`, `N=24` segments. Vertices are duplicated per face (base ring distinct from side ring) so `RecalculateNormals` gives the base a flat `−Y` normal and each side triangle its own outward normal.

The winding below is worked out analytically (the engineer cannot derive it):
- **Base fan** — triangle `(centre, rim[i], rim[i+1])`. Verified at θ=0: `(rim[0]−centre) × (rim[1]−centre)` has a negative Y component → `−Y` outward normal (the base faces down, away from the apex). This is the same handedness as `SquarePyramid`'s base `(0,1,2)+(0,2,3)`.
- **Side triangles** — triangle `(rim[i], apex, rim[i+1])`. Verified at θ=0 (the `+X` side): `(apex−rim[0]) × (rim[1]−rim[0])` has a positive X component → outward radial normal. Note this is `(rim[i], apex, rim[i+1])`, **not** `(rim[i], rim[i+1], apex)` — the apex sits in the middle of the index triple.

- [ ] **Step 1: Add the `Cone` property and `BuildCone()` method**

In `Assets/Scripts/Core/PrimitiveMeshes.cs`, add a `_cone` backing field next to the other three. Find:

```csharp
        static Mesh _triangularPrism;
        static Mesh _squarePyramid;
        static Mesh _hollowCylinder;
```

Replace it with:

```csharp
        static Mesh _triangularPrism;
        static Mesh _squarePyramid;
        static Mesh _hollowCylinder;
        static Mesh _cone;
```

Then, immediately before the final closing `}` of the `PrimitiveMeshes` class (after `BuildHollowCylinder`'s closing brace, before the brace that closes the class), insert:

```csharp

        // 1×1×1 circular-base cone. Base circle at y=-0.5 radius 0.5
        // (fills the cell horizontally), apex at y=+0.5. The base is
        // the only valid attachment face per ShapeUtilityThruster —
        // it is the cone's placement face; the apex is the exhaust
        // nozzle. Designed to fully occupy one grid cell so the
        // cell-graph adjacency / face-detection raycasts behave the
        // same as for cubes, slopes and pyramids. Triangle windings
        // produce outward-facing normals in Unity's left-handed
        // CW-front convention; verified analytically at theta=0. The
        // base ring and the side ring are separate vertex copies so
        // RecalculateNormals gives the base a flat -Y normal and each
        // side triangle its own outward normal (no smoothing across
        // the base-to-side seam).
        public static Mesh Cone
        {
            get
            {
                if (_cone == null) _cone = BuildCone();
                return _cone;
            }
        }

        static Mesh BuildCone()
        {
            const int N = 24;          // segments around the circumference
            const float h = 0.5f;      // half-height (fills the unit cell)
            const float r = 0.5f;      // base radius (matches cube half-width)

            // Vertex layout (2N + 2 total):
            //   [0]            base centre              (flat -Y)
            //   [1 .. N]       base rim ring            (flat -Y)
            //   [N+1 .. 2N]    side rim ring (a copy)   (outward radial)
            //   [2N+1]         apex                     (outward radial)
            Vector3[] verts = new Vector3[2 * N + 2];
            verts[0] = new Vector3(0f, -h, 0f);          // base centre
            verts[2 * N + 1] = new Vector3(0f, h, 0f);   // apex

            for (int i = 0; i < N; i++)
            {
                float theta = i * (2f * Mathf.PI / N);
                Vector3 rim = new Vector3(r * Mathf.Cos(theta), -h, r * Mathf.Sin(theta));
                verts[1 + i]     = rim;   // base rim copy
                verts[N + 1 + i] = rim;   // side rim copy
            }

            // Base fan: N triangles, each (centre, rim[i], rim[i+1]).
            // Side: N triangles, each (rim[i], apex, rim[i+1]).
            // 3 indices per triangle x 2N triangles = 6N indices.
            int[] tris = new int[6 * N];
            int t = 0;
            for (int i = 0; i < N; i++)
            {
                int j = (i + 1) % N;

                // Base triangle — normal -Y. Winding (centre, rim[i],
                // rim[i+1]); verified at theta=0 the cross product
                // (rim[0]-centre) x (rim[1]-centre) points -Y.
                tris[t++] = 0;
                tris[t++] = 1 + i;
                tris[t++] = 1 + j;

                // Side triangle — outward radial normal. Winding
                // (rim[i], apex, rim[i+1]); verified at theta=0 the
                // cross product (apex-rim[0]) x (rim[1]-rim[0]) points
                // +X on the +X side of the cone.
                tris[t++] = N + 1 + i;
                tris[t++] = 2 * N + 1;
                tris[t++] = N + 1 + j;
            }

            Mesh m = new Mesh { name = "Cone" };
            m.vertices = verts;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
```

- [ ] **Step 2: Compile + console check**

Refresh Unity (`refresh_unity`, `compile="request"`, `mode="force"`, `scope="all"`, `wait_for_ready=true`), poll `mcpforunity://editor/state` until `is_compiling=false` and `ready_for_tools=true`, then `read_console(action="get", types=["error"], count=50)`.

Expected: zero errors. `Cone` is referenced by nothing yet (it is wired into `ThrusterMeshAuthor` in Task 2), so it must compile purely on its own.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Scripts/Core/PrimitiveMeshes.cs"
git commit -m "Add PrimitiveMeshes.Cone — runtime cone mesh for the Thruster"
```

---

## Task 2: Add `ThrusterMeshAuthor.cs` + its canonical `.meta`

**Files:**
- Create: `Assets/Scripts/Core/ThrusterMeshAuthor.cs`
- Create: `Assets/Scripts/Core/ThrusterMeshAuthor.cs.meta`

A mirror of `CylinderMeshAuthor` / `PyramidMeshAuthor`: on `Awake`, assign `PrimitiveMeshes.Cone` to the `MeshFilter` (and `MeshCollider`, if one is present) — but only when the slot is empty, so a hand-authored mesh would take precedence. `PlacedThruster.prefab` uses a `BoxCollider` (not a `MeshCollider`), so the `MeshCollider` branch is a no-op for the shipped prefab; it is kept for symmetry with the cylinder/pyramid authors.

- [ ] **Step 1: Create `ThrusterMeshAuthor.cs`**

Create `Assets/Scripts/Core/ThrusterMeshAuthor.cs` with this exact content:

```csharp
using UnityEngine;

namespace CubeFly.Core
{
    // Assigns the runtime-generated cone mesh to this GameObject's
    // MeshFilter (and MeshCollider, if present) on Awake — but only
    // when the slot is empty. If the prefab references a baked cone
    // mesh directly, this component becomes a no-op. Mirror of
    // CylinderMeshAuthor / PyramidMeshAuthor.
    [RequireComponent(typeof(MeshFilter))]
    public class ThrusterMeshAuthor : MonoBehaviour
    {
        void Awake()
        {
            Mesh mesh = PrimitiveMeshes.Cone;

            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh == null) mf.sharedMesh = mesh;

            MeshCollider mc = GetComponent<MeshCollider>();
            if (mc != null && mc.sharedMesh == null) mc.sharedMesh = mesh;
        }
    }
}
```

- [ ] **Step 2: Create the canonical `.meta` for `ThrusterMeshAuthor.cs`**

Unity's auto-generated stub `.meta` for a new `.cs` file omits the full `MonoImporter` block this project expects. Create `Assets/Scripts/Core/ThrusterMeshAuthor.cs.meta` with this exact content (a fresh unique 32-hex GUID — the one below is unused in the project; if Unity has already generated a `.meta` for this file, overwrite it with the block below but keep whatever GUID Unity assigned if it already differs and is already referenced anywhere):

```
fileFormatVersion: 2
guid: 4c0d1d8b3e624f1ba4a2c3b0e3d1f022
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

This matches the shape of `Assets/Scripts/Core/CylinderMeshAuthor.cs.meta` (a `MonoImporter` with `externalObjects`, `serializedVersion: 2`, empty `defaultReferences`, `executionOrder: 0`, a zero `icon`, empty `userData` / `assetBundleName` / `assetBundleVariant`). The GUID `4c0d1d8b3e624f1ba4a2c3b0e3d1f022` is one past `CylinderMeshAuthor`'s `…f021` and `PyramidMeshAuthor`'s `…f020`; confirm it is unused with `grep -rl 4c0d1d8b3e624f1ba4a2c3b0e3d1f022 Assets/` before committing.

- [ ] **Step 3: Compile + console check**

Refresh Unity (`refresh_unity`, `compile="request"`, `mode="force"`, `scope="all"`, `wait_for_ready=true`), poll `mcpforunity://editor/state` until `is_compiling=false` and `ready_for_tools=true`, then `read_console(action="get", types=["error"], count=50)`.

Expected: zero errors. `ThrusterMeshAuthor` references `PrimitiveMeshes.Cone` from Task 1, which now exists; it is referenced by nothing else yet (the prefab is created in Task 6).

- [ ] **Step 4: Commit**

```bash
git add "Assets/Scripts/Core/ThrusterMeshAuthor.cs" "Assets/Scripts/Core/ThrusterMeshAuthor.cs.meta"
git commit -m "Add ThrusterMeshAuthor — assigns the cone mesh at Awake"
```

---

## Task 3: `ShapeDefinition` — `Utility` category, `coupledMaterial` rename, helpers + all reference updates

**Files:**
- Modify: `Assets/Scripts/Core/ShapeDefinition.cs`
- Modify: `Assets/Scripts/Build/CategoryFlyout.cs`
- Modify: `Assets/Scripts/Build/BuildToolbarController.cs`

This is the **atomic rename task** — it adds `ShapeCategory.Utility`, renames `ShapeDefinition.weaponMaterial`→`coupledMaterial` with `[FormerlySerializedAs]`, updates `ResolveMaterial`, adds `IsArmour` / `UsesCoupledMaterial`, adds the `CategoryButtonLabel` Utilities case, and updates **every** `weaponMaterial` reference in `Assets/Scripts` — all in one commit so the project compiles. `IsWeapon` is **kept** (still used in this file and in `GameData`/`BuildToolbarController`); `GameData`'s `IsWeapon`→`UsesCoupledMaterial` swap is the *next* task and is independent.

Before starting, confirm the full reference set with `grep -rn "weaponMaterial" Assets/Scripts/`. As of this plan the matches are: `ShapeDefinition.cs` (the field declaration, its `[Tooltip]`, two class-doc comment lines, two `ResolveMaterial` comment lines, the `ResolveMaterial` body), `CategoryFlyout.cs` (lines ~186 and ~275), `BuildToolbarController.cs` (comment only, line ~868). `FlyWeaponToolbarController.cs` lines ~143-144 **also** read `shape.weaponMaterial` — those must be updated too (see Step 5). Comment-only mentions in `MaterialRegistry.cs`, `GameData.cs`, `FlyController.cs`, `BuildManager.cs`, `CubePreview.cs` may be left as-is (they do not affect compilation) but updating them is encouraged for accuracy; this plan updates the load-bearing ones and leaves stale comments to a later cleanup.

- [ ] **Step 1: Add `ShapeCategory.Utility` and update the enum doc comment**

In `Assets/Scripts/Core/ShapeDefinition.cs`, find the enum and its doc comment:

```csharp
    // Shape categories — orthogonal axis to material. Armour shapes
    // (Cube, Slope) pull their material from the MaterialRegistry's
    // A/B/C/D pool, with the per-shape memory dict in BuildManager
    // remembering each shape's last-armed choice. Weapon shapes
    // (Pyramid, future) are 1:1 coupled with their own dedicated
    // MaterialDefinition referenced by `weaponMaterial`; the regular
    // material flyout is suppressed for them.
    public enum ShapeCategory
    {
        Armour,
        Weapon,
    }
```

Replace it with:

```csharp
    // Shape categories — orthogonal axis to material. Armour shapes
    // (Cube, Slope) pull their material from the MaterialRegistry's
    // A/B/C/D pool, with the per-shape memory dict in BuildManager
    // remembering each shape's last-armed choice. Weapon shapes
    // (Pyramid, Cylinder) and Utility shapes (Thruster) are 1:1
    // coupled with their own dedicated MaterialDefinition referenced
    // by `coupledMaterial`; the regular material flyout is suppressed
    // for them. Utility is the non-armour, non-weapon category — the
    // Thruster is its first member.
    public enum ShapeCategory
    {
        Armour,
        Weapon,
        Utility,
    }
```

`Utility` is appended last, so the serialized integer values of `Armour` (0) and `Weapon` (1) are unchanged — `ShapeWeaponPyramid.asset` / `ShapeWeaponCylinder.asset` keep `category: 1`, and `ShapeUtilityThruster.asset` will use `category: 2`.

- [ ] **Step 2: Update the `ShapeDefinition` class doc comment**

Find:

```csharp
    // One placeable shape — geometry + collider only. For armour
    // shapes, stats / colour / gameplay identity come from the chosen
    // MaterialDefinition at spawn time. For weapon shapes, the
    // MaterialDefinition is fixed (`weaponMaterial`) and the toolbar
    // doesn't offer alternatives.
```

Replace it with:

```csharp
    // One placeable shape — geometry + collider only. For armour
    // shapes, stats / colour / gameplay identity come from the chosen
    // MaterialDefinition at spawn time. For weapon and utility shapes,
    // the MaterialDefinition is fixed (`coupledMaterial`) and the
    // toolbar doesn't offer alternatives.
```

- [ ] **Step 3: Rename the field and add `[FormerlySerializedAs]`**

Find:

```csharp
        [Tooltip("Armour shapes use the MaterialRegistry's A/B/C/D pool. Weapon shapes use their own coupled `weaponMaterial`.")]
        public ShapeCategory category = ShapeCategory.Armour;

        [Tooltip("Material applied at spawn time when category == Weapon. Ignored for armour shapes (the chosen MaterialDefinition from MaterialRegistry is used instead).")]
        public MaterialDefinition weaponMaterial;
```

Replace it with:

```csharp
        [Tooltip("Armour shapes use the MaterialRegistry's A/B/C/D pool. Weapon and Utility shapes use their own coupled `coupledMaterial`.")]
        public ShapeCategory category = ShapeCategory.Armour;

        [Tooltip("Material applied at spawn time for non-armour shapes (Weapon, Utility). Ignored for armour shapes (the chosen MaterialDefinition from MaterialRegistry is used instead).")]
        [UnityEngine.Serialization.FormerlySerializedAs("weaponMaterial")]
        public MaterialDefinition coupledMaterial;
```

`[FormerlySerializedAs("weaponMaterial")]` makes Unity, on the next import, read the old serialized `weaponMaterial:` key into the new `coupledMaterial` field for `ShapeWeaponPyramid.asset` and `ShapeWeaponCylinder.asset` — no manual YAML edit, no broken refs. Unity rewrites those two `.asset` files (the YAML key becomes `coupledMaterial:`); **they will show as modified in this task's `git diff`** — that is expected and correct. They are added to this task's commit (Step 6).

- [ ] **Step 4: Update `ResolveMaterial` and the helper properties**

Find:

```csharp
        // Resolves the MaterialDefinition that should be applied to a
        // placement of this shape. Armour shapes consult the supplied
        // registry by index; weapon shapes ignore the registry and
        // return their coupled `weaponMaterial`. Returns null when
        // either the registry is missing or the index is out of range
        // (armour) / weaponMaterial is unassigned (weapon).
        public MaterialDefinition ResolveMaterial(int materialIndex, MaterialRegistry materialRegistry)
        {
            if (category == ShapeCategory.Weapon) return weaponMaterial;
            return materialRegistry != null ? materialRegistry.Get(materialIndex) : null;
        }

        public bool IsWeapon => category == ShapeCategory.Weapon;
```

Replace it with:

```csharp
        // Resolves the MaterialDefinition that should be applied to a
        // placement of this shape. Armour shapes consult the supplied
        // registry by index; non-armour shapes (Weapon, Utility)
        // ignore the registry and return their coupled
        // `coupledMaterial`. Returns null when either the registry is
        // missing or the index is out of range (armour) /
        // coupledMaterial is unassigned (non-armour).
        public MaterialDefinition ResolveMaterial(int materialIndex, MaterialRegistry materialRegistry)
        {
            if (category != ShapeCategory.Armour) return coupledMaterial;
            return materialRegistry != null ? materialRegistry.Get(materialIndex) : null;
        }

        // Category == Weapon. Kept for the toolbar's weapon-specific
        // readout ("(Weapon)" label) and any weapon-only gameplay.
        public bool IsWeapon => category == ShapeCategory.Weapon;

        // Category == Armour — pulls its material from MaterialRegistry.
        public bool IsArmour => category == ShapeCategory.Armour;

        // Category != Armour (Weapon or Utility) — uses the coupled
        // `coupledMaterial` instead of the MaterialRegistry pool. The
        // toolbar (non-armour category flyouts) and the save layer
        // group on this rather than on IsWeapon.
        public bool UsesCoupledMaterial => category != ShapeCategory.Armour;
```

`ResolveMaterial`'s old `category == ShapeCategory.Weapon` becomes `category != ShapeCategory.Armour` — a Utility shape now resolves its coupled material exactly like a weapon, and an Armour shape is unaffected.

- [ ] **Step 5: Update every `weaponMaterial` reference to `coupledMaterial`**

In `Assets/Scripts/Build/CategoryFlyout.cs`, there are two reads. Find (in `BuildFlyout`):

```csharp
                MaterialDefinition wmat = shape != null ? shape.weaponMaterial : null;
```

Replace it with:

```csharp
                MaterialDefinition wmat = shape != null ? shape.coupledMaterial : null;
```

Find (in `RefreshSwatch`):

```csharp
            ShapeDefinition shape = _buildManager.Shapes.Get(swatchShape);
            MaterialDefinition wmat = shape != null ? shape.weaponMaterial : null;
```

Replace it with:

```csharp
            ShapeDefinition shape = _buildManager.Shapes.Get(swatchShape);
            MaterialDefinition wmat = shape != null ? shape.coupledMaterial : null;
```

(The local variable name `wmat` is left as-is — renaming a local is cosmetic and out of scope; only the field reference must change for the rename to compile.)

In `Assets/Scripts/Build/BuildToolbarController.cs`, the only `weaponMaterial` occurrence is a comment in `RefreshSelectedStats`. Find:

```csharp
            // ResolveMaterial picks coupled weaponMaterial for weapons;
            // registry-indexed MaterialDefinition for armour. Single
            // call site keeps the format string symmetric.
```

Replace it with:

```csharp
            // ResolveMaterial picks the coupled coupledMaterial for
            // non-armour shapes; registry-indexed MaterialDefinition
            // for armour. Single call site keeps the format string
            // symmetric.
```

In `Assets/Scripts/Fly/FlyWeaponToolbarController.cs`, find the `weaponMaterial` reads (around lines 143-144):

```csharp
                Color swatchColor = (shape != null && shape.weaponMaterial != null)
                    ? shape.weaponMaterial.SwatchColor
```

Replace it with:

```csharp
                Color swatchColor = (shape != null && shape.coupledMaterial != null)
                    ? shape.coupledMaterial.SwatchColor
```

(Read the surrounding lines first to confirm the exact text — if the ternary spans differently than shown, match the two `shape.weaponMaterial` tokens precisely and change only those.)

- [ ] **Step 6: Add the `CategoryButtonLabel` Utilities case**

In `Assets/Scripts/Build/BuildToolbarController.cs`, find the `CategoryButtonLabel` method:

```csharp
        // Toolbar button label for a non-armour category.
        string CategoryButtonLabel(ShapeCategory category)
        {
            switch (category)
            {
                case ShapeCategory.Weapon: return weaponsButtonLabel;
                default:                   return category.ToString();
            }
        }
```

Replace it with:

```csharp
        // Toolbar button label for a non-armour category.
        string CategoryButtonLabel(ShapeCategory category)
        {
            switch (category)
            {
                case ShapeCategory.Weapon:  return weaponsButtonLabel;
                case ShapeCategory.Utility: return "Utilities";
                default:                    return category.ToString();
            }
        }
```

The `"Utilities"` label is inline (a string literal) — the existing `weaponsButtonLabel` is a `[SerializeField]` for legacy reasons; a serialized field for the Utilities label is not required by the spec, and adding one would need a scene/inspector edit. If a future PR wants it tunable, promote it then.

- [ ] **Step 7: Compile + console check**

Refresh Unity (`refresh_unity`, `compile="request"`, `mode="force"`, `scope="all"`, `wait_for_ready=true`), poll `mcpforunity://editor/state` until `is_compiling=false` and `ready_for_tools=true`, then `read_console(action="get", types=["error"], count=50)`.

Expected: zero errors. If the console reports `'ShapeDefinition' does not contain a definition for 'weaponMaterial'`, a reference in Step 5 was missed — re-run `grep -rn "weaponMaterial" Assets/Scripts/` and fix the remaining non-comment hit. After the refresh, also confirm in the console (or via `git status`) that `ShapeWeaponPyramid.asset` and `ShapeWeaponCylinder.asset` were rewritten by the `[FormerlySerializedAs]` migration — open one and check the YAML now reads `coupledMaterial: {fileID: …}` (the GUID value must be unchanged from the old `weaponMaterial:` line).

- [ ] **Step 8: Verify the auto-migration preserved the weapon material refs**

Read `Assets/Shapes/ShapeWeaponPyramid.asset` and `Assets/Shapes/ShapeWeaponCylinder.asset`. Each must now have a `coupledMaterial:` line (not `weaponMaterial:`) whose `guid` is the matdef it had before — `2b2a4f1c8e3a4d6db5f1eecf4a0a2c21` (PyramidWeaponMatDef) for the pyramid, `2b2a4f1c8e3a4d6db5f1eecf4a0a2c22` (CylinderWeaponMatDef) for the cylinder. If a file still shows `weaponMaterial:`, Unity has not re-imported it — force it with `mcp__UnityMCP__manage_asset` reimport (or another `refresh_unity` with `mode="force"`). The migration is complete only when both `.asset` files reference their matdef under the new key.

- [ ] **Step 9: Commit**

```bash
git add "Assets/Scripts/Core/ShapeDefinition.cs" "Assets/Scripts/Build/CategoryFlyout.cs" "Assets/Scripts/Build/BuildToolbarController.cs" "Assets/Scripts/Fly/FlyWeaponToolbarController.cs" "Assets/Shapes/ShapeWeaponPyramid.asset" "Assets/Shapes/ShapeWeaponCylinder.asset"
git commit -m "Rename ShapeDefinition.weaponMaterial to coupledMaterial; add Utility category"
```

The two `.asset` files are committed here because the `[FormerlySerializedAs]` migration rewrote them as a side effect of this task's field rename — they belong with the change that caused them.

---

## Task 4: `GameData` — save/load coupled-material path keyed on `UsesCoupledMaterial`

**Files:**
- Modify: `Assets/Scripts/Core/GameData.cs`

The save/load path special-cases coupled-material shapes: it skips the `MaterialRegistry` name lookup and resolves the material via the shape, with a `-1` material-index sentinel. That check currently keys on `IsWeapon`; it must key on `UsesCoupledMaterial` so a Utility shape (the Thruster) round-trips identically to a weapon — by shape name, no material lookup.

There is exactly one behavioural check to change (`LoadFromSave`, line ~320). `ToSave` (line ~369) is already correct — it calls `ResolveMaterial`, which Task 3 generalised to all non-armour shapes, so a thruster's `coupledMaterial` is written into the `PlacementRecord.material` name for diagnosability and resolved-by-shape on load. Two stale comments are also corrected.

- [ ] **Step 1: Change the `LoadFromSave` coupled-material branch**

In `Assets/Scripts/Core/GameData.cs`, find this region inside `LoadFromSave`:

```csharp
                    // Weapon shapes have a coupled material that the
                    // load path resolves via the shape, not via name
                    // lookup in MaterialRegistry — the saved material
                    // name is informational for those entries. Set
                    // MaterialIndex to -1 (sentinel "use coupled").
                    ShapeDefinition shape = shapeRegistry.Get(shapeIndex);
                    int materialIndex;
                    if (shape != null && shape.IsWeapon)
                    {
                        materialIndex = -1;
                    }
```

Replace it with:

```csharp
                    // Weapon and Utility shapes have a coupled material
                    // that the load path resolves via the shape, not
                    // via name lookup in MaterialRegistry — the saved
                    // material name is informational for those entries.
                    // Set MaterialIndex to -1 (sentinel "use coupled").
                    ShapeDefinition shape = shapeRegistry.Get(shapeIndex);
                    int materialIndex;
                    if (shape != null && shape.UsesCoupledMaterial)
                    {
                        materialIndex = -1;
                    }
```

- [ ] **Step 2: Correct the `SumPlacedMasses` doc comment**

Find:

```csharp
        // Sum of all placed cubes' masses. Does NOT include the alpha
        // cube — callers add that separately. Resolves each
        // placement's material via ShapeDefinition.ResolveMaterial so
        // weapon shapes pull from their coupled weaponMaterial and
        // armour shapes pull from MaterialRegistry by index.
        // Placements whose shape or material lookup fails (registry
        // null, shape not in registry, weapon with missing
        // weaponMaterial) are silently skipped — this can under-count
        // total mass if the registries are misconfigured. Returns 0
        // when `shapes` is null.
```

Replace it with:

```csharp
        // Sum of all placed cubes' masses. Does NOT include the alpha
        // cube — callers add that separately. Resolves each
        // placement's material via ShapeDefinition.ResolveMaterial so
        // non-armour shapes (Weapon, Utility) pull from their coupled
        // coupledMaterial and armour shapes pull from MaterialRegistry
        // by index. Placements whose shape or material lookup fails
        // (registry null, shape not in registry, non-armour shape with
        // missing coupledMaterial) are silently skipped — this can
        // under-count total mass if the registries are misconfigured.
        // Returns 0 when `shapes` is null.
```

- [ ] **Step 3: Correct the `ToSave` inline comment**

Find:

```csharp
                // For weapon shapes the material is implicit (coupled
                // to the shape) — we still write a non-empty name for
                // diagnosability, but the load path resolves via the
                // shape rather than name-lookup.
```

Replace it with:

```csharp
                // For non-armour shapes (Weapon, Utility) the material
                // is implicit (coupled to the shape) — we still write a
                // non-empty name for diagnosability, but the load path
                // resolves via the shape rather than name-lookup.
```

- [ ] **Step 4: Compile + console check**

Refresh Unity (`refresh_unity`, `compile="request"`, `mode="force"`, `scope="all"`, `wait_for_ready=true`), poll `mcpforunity://editor/state` until `is_compiling=false` and `ready_for_tools=true`, then `read_console(action="get", types=["error"], count=50)`.

Expected: zero errors. `UsesCoupledMaterial` was added to `ShapeDefinition` in Task 3, so the reference resolves.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Core/GameData.cs"
git commit -m "Key the save coupled-material path on UsesCoupledMaterial"
```

---

## Task 5: Create `ThrusterMat.mat` and `ThrusterMatDef.asset`

**Files:**
- Create: `Assets/Materials/ThrusterMat.mat` (+ `.meta` — Unity-generated)
- Create: `Assets/Materials/Defs/ThrusterMatDef.asset` (+ `.meta` — Unity-generated)

Both assets are created with Unity MCP tools at execution time. `ThrusterMat` is a URP/Lit material in a cool blue-grey; `ThrusterMatDef` is a `MaterialDefinition` ScriptableObject with HP/AV/Mass identical to `PyramidWeaponMatDef` and its renderer material pointing at `ThrusterMat`.

- [ ] **Step 1: Create `ThrusterMat.mat` (URP/Lit, cool blue-grey)**

Use `mcp__UnityMCP__manage_material` to create a new material at `Assets/Materials/ThrusterMat.mat`:
- **Shader:** `Universal Render Pipeline/Lit` (URP/Lit — shader GUID `933532a4fcc9baf4fa0491de14d08ed7`, the same shader every other `*Mat.mat` in `Assets/Materials/` uses).
- **Base color (`_BaseColor` / `_Color`):** a cool blue-grey, engine-like, distinct from the rusty weapon reds (`PyramidWeaponMat` is `_BaseColor` ≈ `(0.55, 0.18, 0.20, 1)`) and the A/B/C/D armour palette. Target `_BaseColor: {r: 0.36, g: 0.42, b: 0.50, a: 1}` (a desaturated steel blue). Set `_Color` to the same RGBA if the tool exposes it (URP/Lit keeps both in sync).
- **`_Metallic`:** `0.4` and **`_Smoothness`:** `0.6` — matching `PyramidWeaponMat` so the thruster reads as the same material family of placeable, just a different hue.
- Leave `_EmissionColor` black (`0,0,0,1`) and the `_EMISSION` keyword off — the exhaust glow, if any, is a PR 2 concern.

If `manage_material` cannot set a particular float/color, create the material with the shader assigned, then `execute_code` an editor snippet using `new Material(Shader.Find("Universal Render Pipeline/Lit"))`, `mat.SetColor("_BaseColor", …)`, `mat.SetFloat("_Metallic", 0.4f)`, `mat.SetFloat("_Smoothness", 0.6f)`, and `AssetDatabase.CreateAsset(mat, "Assets/Materials/ThrusterMat.mat")`.

Verify: the file `Assets/Materials/ThrusterMat.mat` exists with a Unity-generated `.meta`, and `read_console` reports no shader / import errors.

- [ ] **Step 2: Create `ThrusterMatDef.asset` (a `MaterialDefinition`)**

Use `mcp__UnityMCP__manage_scriptable_object` (or `manage_asset` to instantiate a `MaterialDefinition`) to create `Assets/Materials/Defs/ThrusterMatDef.asset`. `MaterialDefinition` is `CubeFly.Core.MaterialDefinition` (script GUID `1b2a4f1c8e3a4d6db5f1eecf4a0a1b03`; `CreateAssetMenu` menu `CubeFly/Material`). Set its fields:
- `displayName`: `Thruster`
- `material`: reference `Assets/Materials/ThrusterMat.mat` (the asset from Step 1).
- `healthPoints`: `5`
- `armourValue`: `0`
- `mass`: `0.5`

The HP/AV/Mass values are copied verbatim from `Assets/Materials/Defs/PyramidWeaponMatDef.asset` (`healthPoints: 5`, `armourValue: 0`, `mass: 0.5`) — spec §2.5 specifies "HP / AV / Mass identical to the pyramid weapon's matdef".

Reference YAML shape to aim for (the matdef structure, mirroring `PyramidWeaponMatDef.asset`):

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
  m_Name: ThrusterMatDef
  m_EditorClassIdentifier:
  displayName: Thruster
  material: {fileID: 2100000, guid: <GUID of ThrusterMat.mat>, type: 2}
  healthPoints: 5
  armourValue: 0
  mass: 0.5
```

Do not hand-author this — let the MCP tool / `AssetDatabase` write it so the `material` GUID and the `.meta` are correct. If a tool path is unavailable, `execute_code`: `var def = ScriptableObject.CreateInstance<CubeFly.Core.MaterialDefinition>(); def.displayName = "Thruster"; def.material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/ThrusterMat.mat"); def.healthPoints = 5f; def.armourValue = 0f; def.mass = 0.5f; AssetDatabase.CreateAsset(def, "Assets/Materials/Defs/ThrusterMatDef.asset");`.

Verify: `Assets/Materials/Defs/ThrusterMatDef.asset` exists; opening it shows `material` resolving to `ThrusterMat` (not a missing reference) and the three stat values above.

- [ ] **Step 3: Compile + console check**

Refresh Unity and `read_console(action="get", types=["error"], count=50)`. Expected: zero errors. (No `.cs` changed, but the asset import should be clean — a broken `material` reference or wrong script GUID would surface here.)

- [ ] **Step 4: Commit**

```bash
git add "Assets/Materials/ThrusterMat.mat" "Assets/Materials/ThrusterMat.mat.meta" "Assets/Materials/Defs/ThrusterMatDef.asset" "Assets/Materials/Defs/ThrusterMatDef.asset.meta"
git commit -m "Add ThrusterMat material and ThrusterMatDef definition"
```

---

## Task 6: Create `PlacedThruster.prefab`

**Files:**
- Create: `Assets/Prefabs/PlacedThruster.prefab` (+ `.meta` — Unity-generated)

The placeable prefab, mirroring `PlacedCylinder.prefab` / `PlacedPyramid.prefab` **minus the weapon scripts**. It carries: a `MeshFilter` with an empty mesh slot, a `MeshRenderer`, a cell-bounds `BoxCollider` (`Size 1×1×1`, `Center 0,0,0`), `PlacedCubeData` (the `cell` component — GUID `a0a0a0a0000000030000000000000003`), `CubeStats` (GUID `a0a0a0a000000000000000000000010e`), and `ThrusterMeshAuthor` (from Task 2). **No `ThrusterBehavior`** (PR 2). **No weapon component** (`PlacedCylinder` has a launcher at `5b1d2e3f…f75` and `PlacedPyramid` at `…f74` — the thruster has neither). Layer `PlacedCube` (layer index `6`, as on every `Placed*` prefab).

- [ ] **Step 1: Create the prefab**

Build it with Unity MCP. Recommended approach — `mcp__UnityMCP__execute_code` an editor snippet that constructs the GameObject and saves it as a prefab, so component wiring and `.meta` are correct:

```csharp
// Editor-side, run via execute_code.
var go = new GameObject("PlacedThruster");
go.layer = 6; // PlacedCube

var mf = go.AddComponent<MeshFilter>();
mf.sharedMesh = null; // ThrusterMeshAuthor fills this at Awake.

var mr = go.AddComponent<MeshRenderer>();
mr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/ThrusterMat.mat");

var box = go.AddComponent<BoxCollider>();
box.size = Vector3.one;        // cell bounds
box.center = Vector3.zero;

go.AddComponent<CubeFly.Build.PlacedCubeData>();   // the `cell` component
go.AddComponent<CubeFly.Core.CubeStats>();         // populated at spawn by ThrusterMatDef.ApplyTo
go.AddComponent<CubeFly.Core.ThrusterMeshAuthor>();

PrefabUtility.SaveAsPrefabAsset(go, "Assets/Prefabs/PlacedThruster.prefab");
Object.DestroyImmediate(go);
```

Notes:
- `PlacedCubeData` is the `cell`-bearing component on the existing `Placed*` prefabs (script GUID `a0a0a0a0000000030000000000000003`, source `Assets/Scripts/Build/PlacedCubeData.cs`); confirm the namespace (`CubeFly.Build`) by reading that file before running — if the type is in a different namespace, adjust the `AddComponent` line.
- `CubeStats` defaults (`healthPoints 100`, `armourValue 10`, `mass 1`) are placeholders — `ThrusterMatDef.ApplyTo` overwrites them at spawn with `5 / 0 / 0.5`. Leaving the prefab defaults is fine and matches `PlacedCylinder` (whose serialized `CubeStats` happens to carry `10/0/5`, also overwritten at spawn).
- The `MeshRenderer` material is set to `ThrusterMat` so the prefab previews correctly in the editor; `MaterialDefinition.ApplyTo` re-assigns the same material at spawn anyway.
- No `MeshCollider` — the `BoxCollider` is the cell-bounds collider, cheap and correct for the grid (spec §2.3). `ThrusterMeshAuthor`'s `MeshCollider` branch is therefore inert here.

If you build the prefab interactively with `manage_prefabs` / `manage_gameobject` instead, the component set, layer, `BoxCollider` size/center, and the empty `MeshFilter.sharedMesh` must match the snippet above exactly.

- [ ] **Step 2: Verify the prefab**

Read `Assets/Prefabs/PlacedThruster.prefab` and confirm:
- `m_Layer: 6`.
- A `MeshFilter` with `m_Mesh: {fileID: 0}` (empty slot).
- A `MeshRenderer` whose `m_Materials` references `ThrusterMat`.
- A `BoxCollider` with `m_Size: {x: 1, y: 1, z: 1}` and `m_Center: {x: 0, y: 0, z: 0}`.
- Three `MonoBehaviour` components: `PlacedCubeData` (`a0a0a0a0000000030000000000000003`), `CubeStats` (`a0a0a0a000000000000000000000010e`), `ThrusterMeshAuthor` (the Task 2 GUID `4c0d1d8b3e624f1ba4a2c3b0e3d1f022`).
- **No** weapon-launcher `MonoBehaviour` (no `5b1d2e3f…` / `4c…f74` script), **no** `ThrusterBehavior`.

Refresh Unity and `read_console(action="get", types=["error"], count=50)` — zero errors (a missing script reference on the prefab would surface here).

- [ ] **Step 3: Commit**

```bash
git add "Assets/Prefabs/PlacedThruster.prefab" "Assets/Prefabs/PlacedThruster.prefab.meta"
git commit -m "Add PlacedThruster prefab — inert cone placeable"
```

---

## Task 7: Create `ShapeUtilityThruster.asset` and register it in `ShapeRegistry`

**Files:**
- Create: `Assets/Shapes/ShapeUtilityThruster.asset` (+ `.meta` — Unity-generated)
- Modify: `Assets/Shapes/ShapeRegistry.asset`

The `ShapeDefinition` ScriptableObject for the Thruster, then its registration in `ShapeRegistry` after the two weapon shapes.

- [ ] **Step 1: Create `ShapeUtilityThruster.asset` (a `ShapeDefinition`)**

Create `Assets/Shapes/ShapeUtilityThruster.asset` with Unity MCP (`manage_scriptable_object` / `manage_asset`, falling back to `execute_code`). `ShapeDefinition` is `CubeFly.Core.ShapeDefinition` (script GUID `1b2a4f1c8e3a4d6db5f1eecf4a0a1b01`; `CreateAssetMenu` menu `CubeFly/Shape`). Set its fields:
- `displayName`: `Thruster`
- `category`: `Utility` (enum value `2`).
- `prefab`: reference `Assets/Prefabs/PlacedThruster.prefab` (from Task 6).
- `coupledMaterial`: reference `Assets/Materials/Defs/ThrusterMatDef.asset` (from Task 5). Note the field is `coupledMaterial` — Task 3 renamed it; do **not** write `weaponMaterial`.
- Valid attachment faces — **`faceNegY` only**, identical to `ShapeWeaponPyramid` (apex-up cone, base-down): `faceNegX: 0`, `facePosX: 0`, `faceNegY: 1`, `facePosY: 0`, `faceNegZ: 0`, `facePosZ: 0`.

Reference YAML shape to aim for (mirroring `ShapeWeaponPyramid.asset`, post-rename — i.e. with `category: 2` and a `coupledMaterial:` key):

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
  m_Name: ShapeUtilityThruster
  m_EditorClassIdentifier:
  displayName: Thruster
  prefab: {fileID: 7900000, guid: <GUID of PlacedThruster.prefab>, type: 3}
  category: 2
  coupledMaterial: {fileID: 11400000, guid: <GUID of ThrusterMatDef.asset>, type: 2}
  faceNegX: 0
  facePosX: 0
  faceNegY: 1
  facePosY: 0
  faceNegZ: 0
  facePosZ: 0
```

`execute_code` fallback: `var s = ScriptableObject.CreateInstance<CubeFly.Core.ShapeDefinition>(); s.displayName = "Thruster"; s.category = CubeFly.Core.ShapeCategory.Utility; s.prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlacedThruster.prefab"); s.coupledMaterial = AssetDatabase.LoadAssetAtPath<CubeFly.Core.MaterialDefinition>("Assets/Materials/Defs/ThrusterMatDef.asset"); s.faceNegX = false; s.facePosX = false; s.faceNegY = true; s.facePosY = false; s.faceNegZ = false; s.facePosZ = false; AssetDatabase.CreateAsset(s, "Assets/Shapes/ShapeUtilityThruster.asset");`.

Verify: `Assets/Shapes/ShapeUtilityThruster.asset` exists; opening it shows `category: 2`, `prefab` resolving to `PlacedThruster`, `coupledMaterial` resolving to `ThrusterMatDef`, and exactly `faceNegY: 1` with the other five faces `0`.

- [ ] **Step 2: Register `ShapeUtilityThruster` in `ShapeRegistry.asset`**

`Assets/Shapes/ShapeRegistry.asset` currently lists four shapes under `shapes:`:

```yaml
  shapes:
  - {fileID: 11400000, guid: 2b2a4f1c8e3a4d6db5f1eecf4a0a2c01, type: 2}
  - {fileID: 11400000, guid: 2b2a4f1c8e3a4d6db5f1eecf4a0a2c02, type: 2}
  - {fileID: 11400000, guid: 2b2a4f1c8e3a4d6db5f1eecf4a0a2c04, type: 2}
  - {fileID: 11400000, guid: 2b2a4f1c8e3a4d6db5f1eecf4a0a2c05, type: 2}
```

(That is `ShapeCube`, `ShapeSlope`, `ShapeWeaponPyramid` = `…2c04`, `ShapeWeaponCylinder` = `…2c05`.) Append `ShapeUtilityThruster` as a fifth entry, **after** the weapon shapes:

```yaml
  shapes:
  - {fileID: 11400000, guid: 2b2a4f1c8e3a4d6db5f1eecf4a0a2c01, type: 2}
  - {fileID: 11400000, guid: 2b2a4f1c8e3a4d6db5f1eecf4a0a2c02, type: 2}
  - {fileID: 11400000, guid: 2b2a4f1c8e3a4d6db5f1eecf4a0a2c04, type: 2}
  - {fileID: 11400000, guid: 2b2a4f1c8e3a4d6db5f1eecf4a0a2c05, type: 2}
  - {fileID: 11400000, guid: <GUID of ShapeUtilityThruster.asset>, type: 2}
```

Prefer doing this via Unity MCP — load `ShapeRegistry.asset`, append the `ShapeUtilityThruster` reference to the `shapes` list with a `SerializedObject`/`SerializedProperty` `execute_code` snippet, and save — so the `guid` is filled correctly from the freshly-created asset. (`var reg = AssetDatabase.LoadAssetAtPath<CubeFly.Core.ShapeRegistry>("Assets/Shapes/ShapeRegistry.asset"); var so = new SerializedObject(reg); var arr = so.FindProperty("shapes"); arr.arraySize += 1; arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = AssetDatabase.LoadAssetAtPath<CubeFly.Core.ShapeDefinition>("Assets/Shapes/ShapeUtilityThruster.asset"); so.ApplyModifiedProperties(); AssetDatabase.SaveAssets();` — confirm the serialized field is named `shapes` by reading `Assets/Scripts/Core/ShapeRegistry.cs` first.) Hand-editing the YAML to add the line is acceptable only if the `ShapeUtilityThruster.asset` `.meta` GUID is read first and pasted exactly.

The append order matters: `BuildToolbarController` orders non-armour categories by **first appearance in the registry**, so Weapons (entries 3-4) precedes Utilities (entry 5) → toolbar reads `[Cube] [Slope] [Weapons ▸] [Utilities ▸] [Delete]`, exactly the spec §4 target.

- [ ] **Step 3: Compile + console check**

Refresh Unity and `read_console(action="get", types=["error"], count=50)`. Expected: zero errors. A broken `prefab` / `coupledMaterial` reference on the new shape, or a malformed `ShapeRegistry` list entry, would surface here.

- [ ] **Step 4: Commit**

```bash
git add "Assets/Shapes/ShapeUtilityThruster.asset" "Assets/Shapes/ShapeUtilityThruster.asset.meta" "Assets/Shapes/ShapeRegistry.asset"
git commit -m "Add ShapeUtilityThruster shape and register it in ShapeRegistry"
```

---

## Task 8: Build-scene verification

**Files:**
- No code changes expected. If the check uncovers a defect, fix it in the relevant file from Tasks 1-7 and re-verify, then commit the fix.

This is the acceptance gate for PR 1b — spec §10's "PR 1" test plan. The Thruster is placeable, the Utilities category works, and the PR 1a refactor did not regress weapons.

- [ ] **Step 1: Clean compile**

Refresh Unity (`refresh_unity`, `compile="request"`, `mode="force"`, `scope="all"`, `wait_for_ready=true`), poll `mcpforunity://editor/state` until `is_compiling=false` and `ready_for_tools=true`, then `read_console(action="get", types=["error"], count=50)`. Zero errors required.

- [ ] **Step 2: Editor check — the Utilities category and the Thruster placeable**

Open the Build scene and enter Play mode (via `manage_editor` play control, or run the scene by hand). Verify each item — these are spec §10's PR 1 acceptance criteria:

1. **Toolbar layout** — the toolbar reads `[Cube] [Slope] [Weapons ▸] [Utilities ▸] [Delete]`. The "Utilities" button sits immediately after "Weapons" and before "Delete"; all five slots are evenly spaced and centred.
2. **Utilities flyout** — clicking (or right-clicking, or hover-peeking) the "Utilities" button opens its flyout; the flyout lists exactly one entry, **"Thruster"**, with its stat line (`HP 5 · AV 0 · M 0.5`) and a blue-grey swatch.
3. **Arming the Thruster** — clicking the "Thruster" flyout entry arms it and closes the flyout; the "Utilities" button gets the blue selected highlight; the bottom-left "Selected" line reads `Selected: Thruster …` (the weapon-style `(Weapon)` suffix does **not** appear — `IsWeapon` is false for a Utility shape, so `RefreshSelectedStats` takes the non-weapon branch and prints `Selected: Thruster · Material Thruster`).
4. **Placing a Thruster** — placing one in the hangar renders a **cone** (circular base, pointed apex), shaded blue-grey. The base is flat-shaded with a downward normal; the sides are smoothly lit around the cone. No inverted / black faces (winding is correct).
5. **Attachment face** — the Thruster attaches to the construct only on its circular **base** face (its local `−Y`); placement against any other face is rejected, exactly like the pyramid weapon.
6. **Stats carried** — a placed Thruster carries HP 5 / AV 0 / Mass 0.5 from `ThrusterMatDef` (inspect the placed instance's `CubeStats`, or watch the bottom-left Mass total change by `0.5` per thruster placed).
7. **Save / load round-trip** — build a construct that includes at least one Thruster, save it, reload it. The Thruster reappears at the same cell with the same rotation; the console's `LoadFromSave` log shows it loaded (not skipped). This exercises the Task 4 `UsesCoupledMaterial` path — the thruster round-trips by shape name with the `-1` material sentinel.
8. **Weapons not regressed** — the "Weapons" flyout still opens and lists Pyramid + Cylinder; arming and placing a weapon still works; existing weapon shapes still resolve their coupled material (the `FormerlySerializedAs` migration worked — a placed pyramid/cylinder still shows its rusty-red material and correct HP/AV/Mass).
9. **Armour + shortcuts not regressed** — Cube / Slope buttons, the material flyout, digit keys (1-2 arm armour shapes; Shift+digit set the active armour material), `M` (toggles the active shape's category flyout), `Esc` (closes flyouts), and the Delete button all behave as before.

- [ ] **Step 3: Final commit**

If Step 2 surfaced no defect, Tasks 1-7 already committed everything — `git status` is clean for all touched files; record completion without a new commit. If Step 2 required a fix, commit it with a message naming the defect, e.g.:

```bash
git add <fixed file(s)>
git commit -m "Fix Thruster placeable defect found in build-scene check"
```

Then report PR 1b complete: the Thruster is placeable behind a "Utilities" toolbar category; it renders as a cone, attaches on its base, carries `ThrusterMatDef` stats, and round-trips through save/load. It is inert in flight — `ThrusterBehavior` and the boost mechanic are PR 2.

---

## Notes & risks

- **`FormerlySerializedAs` auto-migration (Task 3, the main risk).** `[FormerlySerializedAs("weaponMaterial")]` is the standard, reliable Unity mechanism for a serialized-field rename — on the next import of an asset that still has the old `weaponMaterial:` key, Unity deserialises it into the new `coupledMaterial` field and rewrites the asset with the new key. The risk is *timing*, not correctness: Unity rewrites the `.asset` lazily (on import / on next save), so `git diff` may not show `ShapeWeaponPyramid.asset` / `ShapeWeaponCylinder.asset` as changed until a forced reimport. Task 3 Step 8 explicitly forces and verifies this. If for any reason the migration does not fire, the fallback is a one-line manual YAML edit per file (`weaponMaterial:` → `coupledMaterial:`, GUID unchanged) — but that should not be necessary. Do **not** delete `[FormerlySerializedAs]` after migrating; it is cheap insurance for any save/asset created before the rename, and any teammate's local un-migrated asset.
- **`IsWeapon` is kept, not removed.** Spec §3.2 keeps `IsWeapon`. Task 3 keeps it; only `GameData` and the toolbar's *coupled-material grouping* move to `UsesCoupledMaterial`. `RefreshSelectedStats` in `BuildToolbarController` still uses `IsWeapon` for the `(Weapon)` label — correct: a Utility shape should not be labelled a weapon. `UpdateButtonStates` / the M-key block also still reference `IsWeapon`; those are about *armour-material* suppression and are unaffected by a Utility shape (which is non-armour, so `_shapeButtons[idx]` is null for it and the armour-only paths skip it anyway). Leaving them on `IsWeapon` is intentional and harmless — re-auditing them is out of scope.
- **Asset-creation approach.** Tasks 5-7 create a `.mat`, two ScriptableObject `.asset`s, and a `.prefab` via Unity MCP at execution time rather than hand-written YAML — Unity then generates correct `.meta` GUIDs and resolves cross-asset references. The plan gives exact target field values (mirroring `PyramidWeaponMatDef` / `ShapeWeaponPyramid` / `PlacedPyramid`) plus an `execute_code` C# fallback for every asset, so the engineer is never blocked if a high-level `manage_*` tool lacks a field setter. The one thing the plan cannot pre-fill is the GUIDs of the freshly-created assets — those are emitted by Unity and must be read back when wiring the next asset (matdef → mat, shape → prefab + matdef, registry → shape). Each asset task ends with a read-back verification step for exactly this reason.
- **`PlacedCubeData` namespace.** The `cell`-bearing component on the `Placed*` prefabs has script GUID `a0a0a0a0000000030000000000000003`, source file `Assets/Scripts/Build/PlacedCubeData.cs`. Task 6's snippet assumes namespace `CubeFly.Build` — the implementer must read that file first and adjust the `AddComponent<…>()` type if the namespace differs. This is the only place the plan relies on a name it did not directly read in full.
- **No `ThrusterBehavior`, no fly-side changes.** PR 1b is build-side only. `PlacedThruster.prefab` deliberately omits `ThrusterBehavior` (the script is created in PR 2). If a step or tool tries to add a thrust/boost component, that is a mistake — PR 1b's thruster is inert in flight.
- **Cone winding is pre-derived.** Task 1's `BuildCone` has the base fan as `(centre, rim[i], rim[i+1])` and the sides as `(rim[i], apex, rim[i+1])` — both verified analytically at θ=0 (base cross product → `−Y`; side cross product → outward radial `+X` on the `+X` side). `RecalculateNormals` then derives flat per-face normals from this winding. If the placed cone shows black/inside-out faces, the index triples were transcribed wrong — re-check against Task 1 Step 1 (note the apex is the *middle* index of each side triple).
- **No unit-test framework.** Verification is the MCP compile/console check after every `.cs` or asset change, plus the Task 8 Step 2 editor checklist — there is no automated test to add. This matches the project's existing practice (PR 1a, the desert-level plans).
- **`.meta` for the one new script.** Only `ThrusterMeshAuthor.cs` is a new script; its canonical `MonoImporter` `.meta` is hand-written in Task 2 Step 2 (the C1/C2 quirk). `PrimitiveMeshes.cs`, `ShapeDefinition.cs`, `CategoryFlyout.cs`, `BuildToolbarController.cs`, `GameData.cs`, `FlyWeaponToolbarController.cs` already have `.meta` files — they are not re-created. New *assets* (`.mat`, `.asset`, `.prefab`) get Unity-generated `.meta`s via the MCP creation tools — those are normal `NativeFormatImporter` / `PrefabImporter` metas, not the `MonoImporter` quirk, and need no hand-authoring.

## Self-review

- **Spec coverage** — every PR 1 (placeable) item in `thruster_boost_spec.md` §2, §3, §8 is mapped to a task:
  - §2.1 `PrimitiveMeshes.Cone` → **Task 1** (complete `BuildCone` code, winding derived).
  - §2.4 `ThrusterMeshAuthor.cs` → **Task 2** (mirror of `CylinderMeshAuthor`, + canonical `.meta`).
  - §3 `ShapeCategory.Utility`, the `weaponMaterial`→`coupledMaterial` rename + `[FormerlySerializedAs]`, updated `ResolveMaterial`, new `IsArmour` / `UsesCoupledMaterial` → **Task 3** (atomic with all reference updates so the project compiles).
  - §3.3 `GameData` save/load path `IsWeapon`→`UsesCoupledMaterial` → **Task 4**.
  - §2.5 `ThrusterMatDef.asset` + `ThrusterMat.mat` → **Task 5**.
  - §2.3 `PlacedThruster.prefab` (MeshFilter empty slot, cell-bounds BoxCollider, CubeStats, ThrusterMeshAuthor, **no** ThrusterBehavior, layer PlacedCube) → **Task 6**.
  - §2.2 `ShapeUtilityThruster.asset` + §8 `ShapeRegistry` registration → **Task 7**.
  - §4 "Utilities" toolbar category — delivered as a data-only addition (PR 1a's `CategoryFlyout` is category-agnostic); the only toolbar-code change is the `CategoryButtonLabel` `case ShapeCategory.Utility: return "Utilities";` in **Task 3 Step 6**.
  - §10 PR 1 test plan → **Task 8** editor checklist.
  - Out of scope and stated so: §1/§5/§6/§7/§9 (Boost resource, activation, flight integration, HUD bar) and §8's PR 2 file list — all PR 2.
- **Placeholder scan** — no `TODO`, no "similar to…", no "etc." stand-ins. Every code step gives complete content: Task 1 the full `Cone` property + `BuildCone` body; Task 2 the full `ThrusterMeshAuthor.cs` and its complete `.meta`; Task 3 exact before/after for the enum, both doc comments, the field + attribute, `ResolveMaterial` + all three helpers, every `weaponMaterial` reference (CategoryFlyout ×2, BuildToolbarController comment, FlyWeaponToolbarController ×2), and the `CategoryButtonLabel` case; Task 4 exact before/after for the `LoadFromSave` branch and two comments. Asset tasks (5-7) give exact field values, reference YAML to mirror, and a literal `execute_code` C# fallback — the only un-fillable values are GUIDs Unity emits, each with an explicit read-back step.
- **Type / name consistency** — new type `CubeFly.Core.ThrusterMeshAuthor` (mirrors `CylinderMeshAuthor`). New mesh API `PrimitiveMeshes.Cone` (property) + `BuildCone()` (private), matching the `HollowCylinder` / `BuildHollowCylinder` pattern. `ShapeCategory` becomes `{ Armour=0, Weapon=1, Utility=2 }` — append-only, so serialized `category: 1` weapon assets are unaffected and the new shape uses `category: 2`. `ShapeDefinition.weaponMaterial` → `coupledMaterial` (with `[FormerlySerializedAs("weaponMaterial")]`); new members `IsArmour`, `UsesCoupledMaterial`; `IsWeapon` and `ResolveMaterial` retained (signature unchanged). `GameData.LoadFromSave` swaps `shape.IsWeapon` → `shape.UsesCoupledMaterial` — both `bool`, drop-in. New assets and their menu/script GUIDs: `ThrusterMat.mat` (URP/Lit shader `933532a4fcc9baf4fa0491de14d08ed7`); `ThrusterMatDef.asset` (`MaterialDefinition`, script `1b2a4f1c8e3a4d6db5f1eecf4a0a1b03`, fields `displayName/material/healthPoints/armourValue/mass` = `Thruster / ThrusterMat / 5 / 0 / 0.5`); `PlacedThruster.prefab` (layer 6; components `PlacedCubeData` `a0a0a0a0…0003`, `CubeStats` `a0a0a0a0…010e`, `ThrusterMeshAuthor` `4c0d1d8b3e624f1ba4a2c3b0e3d1f022`); `ShapeUtilityThruster.asset` (`ShapeDefinition`, script `1b2a4f1c8e3a4d6db5f1eecf4a0a1b01`, `category 2`, `coupledMaterial`→ThrusterMatDef, `prefab`→PlacedThruster, only `faceNegY` true). `ShapeRegistry.asset` `shapes` list grows from 4 to 5 entries, Thruster appended last so the Utilities category orders after Weapons.
