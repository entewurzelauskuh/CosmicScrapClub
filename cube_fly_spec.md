# Cube Fly, also known as Cosmic Scrap Club — Specification

**Engine:** Unity 6.3 LTS (6000.3) · **Render Pipeline:** Universal Render
Pipeline (URP) — Universal 3D template.

**Project goal.** A four-scene interactive demonstrator where the player
opens a Main Menu, picks one of three save slots, builds a cube
construct in a Hangar, and then pilots the assembled construct like a
plane — including shooting with weapon-cube types. Construct data is
held in-memory by a static class for cross-scene transitions and
persisted to disk per slot.

This document is the single canonical spec. Companion docs:

- `full_architecture.md` — implementation-level architecture overview.
- `README.md` — clone / open / run instructions and a controls cheat-sheet.
- `weapon_shooting_spec.md` — deep dive on the Fly-mode shooting
  system (weapons, projectiles, crosshair, dispatch).

---

## High-Level Overview

| Scene           | Purpose                                                                                                            |
|-----------------|--------------------------------------------------------------------------------------------------------------------|
| `MainMenu`      | First scene the player sees. Shows three buttons: **Hangar** → HangarSelect, **Settings** (placeholder), **Exit**. |
| `HangarSelect`  | Save-slot picker. Three cards (one per slot); each shows stats + last-edited timestamp when filled, "<empty>" otherwise. Selecting a slot loads its `ConstructSave` into `GameData` (or clears `GameData` for an empty slot) and transitions to `BuildScene` with that slot armed for autosave. |
| `BuildScene`    | Hangar. The player places, rotates, and deletes cubes around a fixed semi-transparent **alpha cube**. Construct changes autosave (debounced) to the armed slot. |
| `FlyScene`      | Pilot mode. The construct is rebuilt from in-memory `GameData` and flown with 6-axis thrust + pitch / yaw / roll + camera mouse-look. Weapon-cubes fire on LMB; weapon-type selection on digits / scroll wheel. |

A persistent corner button (`Fly!` / `Hangar`) sits in the top-right of
BuildScene and FlyScene and toggles between them. The button is hidden
on MainMenu and HangarSelect.

A pause overlay (`PauseMenu`) self-bootstraps before the first scene
loads. ESC opens / closes the overlay in BuildScene and FlyScene only.
While open, `Time.timeScale = 0` freezes physics; on-overlay buttons
`Menu` and `Back to Desktop` jump to MainMenu or quit the app.

---

## Data & Persistence Model

`CubeFly.Core.GameData` is a **static** C# class. Its data lives for the
duration of the play session — it does not need `DontDestroyOnLoad`
because static fields survive scene unloads naturally. The on-disk
representation is handled separately by `SaveManager` + `ConstructSave`
(see below).

Each placement is recorded as:

```csharp
public readonly struct Placement
{
    public readonly Vector3Int Cell;          // grid coordinate
    public readonly int        ShapeIndex;    // index into ShapeRegistry.shapes
    public readonly int        MaterialIndex; // index into MaterialRegistry.materials
                                              // (-1 for weapon shapes, which carry their
                                              // own coupled MaterialDefinition)
    public readonly Quaternion Rotation;      // 90°-stepped orientation
}
```

Public surface (selected):

- `IReadOnlyList<Placement> PlacedCubes`
- `int ActiveSlot { get; }` — which save slot autosaves target. `-1` means "no slot armed" → autosave disabled.
- `void SetActiveSlot(int)`
- `bool TryAdd(Vector3Int cell, int shapeIndex, int materialIndex, Quaternion rotation, ShapeRegistry shapes)`
- `void Remove(Vector3Int cell)`
- `bool IsOccupied(Vector3Int cell)`
- `Placement GetPlacementAt(Vector3Int cell)`
- `bool IsValidAttachment(Vector3Int cell, int shapeIndex, Quaternion rotation, ShapeRegistry shapes)` — symmetric face-validity check; see *Shapes & face validity* below.
- `Bounds GetConstructBounds()` — AABB including the alpha cube.
- `void Clear()`
- `float SumPlacedMasses(ShapeRegistry shapes, MaterialRegistry materials)` — does **not** include the alpha cube; the caller adds that.
- `float SumPlacedHealthPoints(ShapeRegistry shapes, MaterialRegistry materials)`
- `void LoadFromSave(ConstructSave save, ShapeRegistry, MaterialRegistry)` — replay a serialised construct into memory; resolves shape / material by **name**, suspends adjacency / occupancy validation (saves are authoritative).
- `ConstructSave ToSave(string slotName, ShapeRegistry, MaterialRegistry)` — snapshot current state for the save layer.

Rules enforced by `TryAdd` (regular path, not load):
- The origin cell `(0, 0, 0)` is reserved for the alpha cube and is always rejected.
- A new cell is rejected if it is already occupied.
- The face-validity check (`IsValidAttachment`) must succeed: at least one of the six face-neighbours must be occupied (or be the origin / alpha cube), AND both the new piece's face toward that neighbour and the neighbour's face back at the new piece must be backed by real surface area in the placement's rotation.
- Reject reasons are logged distinctly ("occupied" vs "no ShapeRegistry" vs "unknown shape index" vs "no valid attachment face") so misconfiguration doesn't masquerade as a face-validity failure.

The persistent UI uses `DontDestroyOnLoad` on a single `UIManager`
singleton with a duplicate-instance guard. `PauseMenu` is a second DDOL
singleton, self-bootstrapped on `RuntimeInitializeOnLoadMethod`.

---

## Save / Load

Saves live in `Saves/` (Editor / project dev — git-ignored) or
`Application.persistentDataPath/saves/` (built players). Selection is
driven by `#if UNITY_EDITOR || SAVES_IN_PROJECT`. One file per slot,
`slotN.json`. Up to **3 slots** (`SaveManager.SlotCount`).

**Atomic writes.** `SaveManager.Save` serialises to `slotN.json.tmp`,
then prefers `File.Replace` over the existing file for atomic rename.
Some Unity runtimes throw `PlatformNotSupportedException` /
`IOException` / `UnauthorizedAccessException` from `File.Replace`;
the fallback path renames the existing file to `slotN.json.bak`, moves
the temp into place, then deletes the bak. A crash mid-write leaves
either the previous file intact or the bak available for recovery.

**On-disk format** (`ConstructSave`, version `1`):

```csharp
[Serializable]
public class ConstructSave
{
    public const int CurrentVersion = 1;
    public int    version           = CurrentVersion;
    public string slotName          = string.Empty;
    public long   createdUtcTicks;
    public long   modifiedUtcTicks;
    public float  totalMass;        // denormalised — recomputed on load
    public float  totalHealthPoints;
    public PlacementRecord[] placements = Array.Empty<PlacementRecord>();
}

[Serializable]
public struct PlacementRecord
{
    public Vector3Int cell;
    public string     shape;     // by displayName, not index
    public string     material;  // by displayName (informational for weapon shapes)
    public Vector3    rotEuler;
}
```

Schema rules:

- Shape and material are stored **by name**, so reordering
  `ShapeRegistry` / `MaterialRegistry` doesn't break existing saves.
- `version > CurrentVersion` is refused on load (returns `false`).
- Missing `placements` is tolerated (treated as empty array).
- `totalMass` / `totalHealthPoints` are denormalised for the picker UI;
  the placement list is authoritative on load.

**Autosave.** `BuildManager` subscribes a coroutine to
`ConstructChanged`. Every event restarts a 0.25 s wait; bursts of
changes (flood-fill cleanup, rapid placements) collapse into a single
write at the end of the window. On scene tear-down (`OnDestroy`) any
pending save is flushed immediately so the slot file is current. When
`GameData.ActiveSlot < 0` (developer pressed Play directly on
BuildScene), autosave is disabled and a one-line warning is logged.

**Slot metadata.** `SaveSlotInfo` is a read-only struct built once per
slot by `HangarSelectController` for the slot cards. It carries cube
count, total mass / HP, and a parsed `DateTime` for the modified
timestamp. Out-of-range ticks fall back to `default(DateTime)`.

---

## Shapes & Face Validity

Geometry and gameplay-stat identity are decoupled. `ShapeDefinition` is
the geometry-only axis; `MaterialDefinition` is the stats / colour axis.

`ShapeDefinition` (ScriptableObject):

```csharp
public enum ShapeCategory { Armour, Weapon }

public class ShapeDefinition : ScriptableObject
{
    public string             displayName;
    public GameObject         prefab;             // 1×1×1 colliders required
    public ShapeCategory      category;
    public MaterialDefinition weaponMaterial;     // used only when category == Weapon

    // Local-space attachment validity (six cube-cell face directions).
    public bool faceNegX, facePosX, faceNegY, facePosY, faceNegZ, facePosZ;

    public bool                IsLocalFaceValid(Vector3Int localDir);
    public bool                IsWorldFaceValid(Vector3Int worldDir, Quaternion rotation);
    public MaterialDefinition  ResolveMaterial(int materialIndex, MaterialRegistry registry);
    public bool                IsWeapon => category == ShapeCategory.Weapon;
}
```

**Shipped shapes** (in `Assets/Shapes/`):

| Asset                    | Category | Valid faces (identity rotation)                | Notes                                                  |
|--------------------------|----------|------------------------------------------------|--------------------------------------------------------|
| `ShapeCube.asset`        | Armour   | all six                                        | Default; reduces to old "any face-adjacent" rule.      |
| `ShapeSlope.asset`       | Armour   | bottom (-Y), back (-Z), left (-X), right (+X) | Front (+Z) and top (+Y) are cut away by the hypotenuse. |
| `ShapeWeaponPyramid.asset` | Weapon | bottom (-Y) only                              | Apex up; coupled to `PyramidWeaponMatDef`.             |
| `ShapeWeaponCylinder.asset` | Weapon | bottom (-Y) only                            | Hollow tube axis +Y; coupled to `CylinderWeaponMatDef`. |

**Symmetric face validity.** A placement at `cell` with `(shape,
rotation)` is valid only when, for at least one face-neighbour:

1. The new piece has a real surface on the local face pointing at that
   neighbour (rotation-aware).
2. The neighbour has a real surface on its face pointing back at the
   new piece (also rotation-aware).

The alpha cube at the origin is treated as a cube (all six faces
valid). The check reduces to the historical "any face-adjacent cube"
rule for all-cube constructs.

`ShapeRegistry` is a single ScriptableObject (`Assets/Shapes/ShapeRegistry.asset`)
holding the ordered shape array; the array index is what
`Placement.ShapeIndex` references.

---

## Materials

`MaterialDefinition` (ScriptableObject) carries the
visual material plus three placeholder stats:

```csharp
public class MaterialDefinition : ScriptableObject
{
    public string   displayName;
    public Material material;       // URP/Lit
    public float    healthPoints;
    public float    armourValue;
    public float    mass;

    public void  ApplyTo(GameObject placed);     // sets renderer + writes stats to CubeStats
    public Color SwatchColor { get; }            // for toolbar swatches
}
```

**Shipped armour materials** (in `Assets/Materials/Defs/`): MaterialA,
MaterialB, MaterialC, MaterialD — collected into `MaterialRegistry`.
The order is what `Placement.MaterialIndex` references.

**Coupled weapon materials**: `PyramidWeaponMatDef` and
`CylinderWeaponMatDef`. These are not in `MaterialRegistry`; they are
referenced by their parent `ShapeDefinition.weaponMaterial` field.
`ShapeDefinition.ResolveMaterial(index, registry)` returns the coupled
weapon material for weapon shapes and the registry-indexed armour
material for armour shapes — call sites don't branch on category.

**Per-shape material memory.** `BuildManager` keeps a `Dictionary<int,
int>` mapping shape index → last-armed material index. Switching to a
shape via the toolbar re-arms its remembered material (default 0).
This lets the player set distinct armour materials per shape and
return to them naturally; the dictionary is session-scoped.

---

## Cube Stats (`CubeStats`)

`CubeFly.Core.CubeStats` is a tiny `MonoBehaviour` placeholder for
in-game statistics that future combat / damage systems will mutate:

```csharp
public class CubeStats : MonoBehaviour
{
    public float healthPoints;
    public float armourValue;
    public float mass;
}
```

Every placeable shape prefab — including the alpha cube — has this
component attached. At spawn time `MaterialDefinition.ApplyTo` writes
the chosen material's HP/AV/mass into the spawned cube's `CubeStats`.
Per-cube mass is read from this component when the build UI computes
the live `Mass: X / 100` readout.

---

## Mass Budget

The construct's total mass is bounded and visibly affects flight feel.

**Cap.** `BuildManager.massLimit = 100`. Attempting to place a cube
that would push the total over the cap is rejected and the build UI
shows a fading red message ("Too much mass!") for ~5 s. The alpha
cube is included in the total. Prospective mass uses
`ShapeDefinition.ResolveMaterial` so weapon shapes pull mass from
their coupled `weaponMaterial`, not from the (irrelevant)
`MaterialRegistry` index.

**Slowdown.** `FlyController` computes a single multiplier on `Start`:

```csharp
float t = Mathf.Clamp01((totalMass - baseMassThreshold) / (massCap - baseMassThreshold));
float massMultiplier = Mathf.Lerp(1f, 1f - maxSlowdown, t);
// defaults: baseMassThreshold = 10, massCap = 100, maxSlowdown = 0.9
```

That multiplier scales **acceleration** *and* **rotation rates** — but
not `maxSpeed` or `drag`. A 10-mass ship feels nimble; a 100-mass ship
takes ~10× longer to accelerate and turn.

---

## Scenes

### `MainMenu.unity`

- Three buttons stacked centre-screen: **Hangar**, **Settings**, **Exit**.
- The persistent UICanvas is hidden while this scene is active (`UIManager.OnSceneStateChanged`).
- Clicking **Hangar** loads `HangarSelect` (not BuildScene directly). **Settings** is a placeholder hook. **Exit** quits the application (or stops Editor play mode).

### `HangarSelect.unity`

- `HangarSelectController` builds the slot picker UI in code on `Awake`.
- Reads metadata for every slot via `SaveManager.ReadAllSlotMetadata()` (each slot's `ConstructSave` is parsed once; unreadable slots degrade to `SaveSlotInfo.Empty`).
- Renders three cards horizontally — each card shows slot title, cube count, total mass, HP, and "Last edited N ago" if filled, or `<empty>` otherwise.
- Card primary button: **Start new construct** (empty) or **Continue** (filled).
- Per-card **Delete** button uses inline-confirm — first click changes the label to **Yes, delete** and reveals a Cancel button; auto-cancels after 5 s.
- Cancel returns to MainMenu. ESC also cancels.
- On primary-click: arms `GameData.ActiveSlot`, then either `GameData.Clear()` (empty) or `GameData.LoadFromSave(...)` (filled), then loads `BuildScene`.

### `BuildScene.unity`

Scene contents:

- `BuildManager` (with `CubePreview` and `BuildToolbarController` siblings).
- `Main Camera` with `BuildCamera` (orbit camera).
- `BuildIndicatorController` parents a small red arrow indicator above the cube with the highest local-Z so the player can see which face will lead in flight.
- Directional Light.
- `UIBootstrap` instantiates the persistent `UICanvas` if not already alive.

Runtime spawns:

- The **alpha cube** (1 × 1 × 1, semi-transparent, layer `AlphaCube`) at the origin. Never destroyed.
- A **composite preview** parented to the cursor's candidate cell:
  - An outer translucent cube ghost (cell-bounds visualiser, scale 0.99 to avoid z-fighting, world-axis aligned).
  - An inner mesh scaled to 0.7 — the active shape's prefab with the active material applied — that rotates with `R` / `T`.
  - The bounds ghost tints red via `MaterialPropertyBlock` for invalid placements.
- Shape prefab instances for each entry already in `GameData`.

Build-scene UI overlays (built at runtime by `BuildToolbarController`):

- **Bottom-centre toolbar** — one button per shape, with a coloured corner swatch showing each shape's currently-armed material. Plus a **Delete** button.
  - Selecting an **armour** shape opens (or re-arms) its material flyout; hovering a shape button peeks the flyout open until pinned, clicking pins it. `M` toggles. Esc closes.
  - Selecting a **weapon** shape suppresses the material flyout — weapon shapes have only their coupled `weaponMaterial`.
  - Digits `1` / `2` / `3` / `4` arm material A / B / C / D directly while an armour shape is active.
- **Top-left** — `Rotate: R/T` hint label.
- **Top-centre** — fading red floating message slot (used for "Too much mass!").
- **Bottom-left** — two stat readouts:
  - `Mass: X / 100` (live, recomputed on `ConstructChanged`).
  - `HP: Y` (sum of all placed cubes' health, including alpha).

Tools:

- **Place tool** — LMB places a cube of the selected (shape, material) tuple at the preview cell, rotated by `BuildManager.CurrentRotation`. Disallowed if the cell is occupied, fails the symmetric face-validity check, or would breach the mass cap.
- **Delete tool** — hovering tints the cube under the cursor red via `MaterialPropertyBlock` (no material allocation). LMB removes the cube and runs a flood-fill from the origin to drop any newly disconnected cubes.

Rotation:

- `R` rotates the active placement orientation by 90° around the Z axis.
- `T` rotates by 90° around the X axis.
- The orientation is per-active-placement (not per-shape) and is saved with each `Placement` so the construct rebuilds in `FlyScene` with each piece in its placed pose.

Autosave (see *Save / Load*) flushes 0.25 s after the last
`ConstructChanged` event, and immediately on scene tear-down.

### `FlyScene.unity`

Scene contents:

- `CubeConstruct` empty GameObject — the construct's pivot. Starts at `(0, 10, 0)`.
- `FlyController` rebuilds the construct on `Start`, instantiating one `alphaCubePrefab` at the origin plus one shape-prefab per `Placement` in `GameData`. `MaterialDefinition.ApplyTo` is invoked per placement. Any spawned `WeaponBehavior` is collected for the shooting controller.
- `Main Camera` with `FlyCamera`.
- `FlyShootingController` — owns the per-frame fire / weapon-selection dispatch.
- `FlyWeaponToolbarController` — bottom-of-screen weapon toolbar with reload bars.
- `FlyCrosshair` — screen-space reticle that projects `construct.forward * 100` so on-screen reticle and actual aim agree.
- Directional Light.
- `UIBootstrap` (idempotent — no-ops if `UIManager` is already alive).

Flight model:

- No `Rigidbody`. The construct's transform is moved directly each `FixedUpdate`.
- 6-axis **thrust** in the construct's local frame.
- **Pitch** rotates around local X. **Yaw** rotates around **world** Y (avoids roll coupling when pitched). **Roll** rotates around local Z.
- Velocity is per-axis accumulated, magnitude-clamped to `maxSpeed`, then decayed each step by `Mathf.Exp(-drag * dt)`.

Camera:

- Local-frame ship-stuck follow with a one-shot dynamic offset computed from `GameData.GetConstructBounds()` (bigger ships → camera further back, clamped 5–50 units).
- RMB enables free-look (mouse delta orbits the offset around the construct). On release, the camera snap-blends back to the default behind-and-above pose.
- When `PauseMenu.IsOpen`, the camera treats RMB as released — cursor is freed, mouse delta is ignored, and the offset relaxes to neutral. The body follow is naturally frozen by `Time.timeScale = 0`.

Shooting (see `weapon_shooting_spec.md` for the full design):

- LMB held → fire every weapon of the active type each frame. Per-weapon `reloadSeconds` throttles the actual rate.
- Pyramid weapons: machine-gun style. Frontal pyramids (tip aligned with `construct.forward`, dot > 0.7 = cos 45°) fire at the shared crosshair world target; off-axis pyramids fire along their tip direction (`transform.up`).
- Cylinder weapons: rocket launchers. Two-phase rockets — exit phase along the cylinder's open-end direction for 0.5 units, then re-orient toward the crosshair point captured at fire-time and travel straight to it.
- Weapon-type selection: digits `1`–`9` set by index; mouse wheel cycles (edge-detected so a single notch = one cycle).
- Reload bars in the weapon toolbar show per-type cooldown progress; all instances of a type share the first instance's cooldown reading (they fire together, so they stay synchronised).

---

## Input

Single hand-rolled wrapper at `Assets/Input/CubeFlyInputActions.cs`
(no asset code-gen). Two action maps:

**Build map**

| Action    | Binding         | Purpose                                                                          |
|-----------|-----------------|----------------------------------------------------------------------------------|
| `Place`   | LMB             | In Place tool: places a cube. In Delete tool: removes the cube under the cursor. |
| `RotateZ` | `R`             | Rotates the next placement 90° around Z.                                         |
| `RotateX` | `T`             | Rotates the next placement 90° around X.                                         |

ESC, `M`, and digits `1`–`4` are polled by `BuildToolbarController` /
`PauseMenu` outside the action map.

**Fly map**

| Action     | Binding                                                                                              | Purpose                                |
|------------|------------------------------------------------------------------------------------------------------|----------------------------------------|
| `Thrust`   | 3D-vector composite: `W/S` (forward/back), `A/D` (strafe), `Space/C` (up/down)                        | Local-frame translation.               |
| `Pitch`    | 1D-axis composite: `↑` / `↓`                                                                          | Pitch around local X.                  |
| `Yaw`      | 1D-axis composite: `←` / `→`                                                                          | Yaw around world Y.                    |
| `Roll`     | 1D-axis composite: `Q` / `E`                                                                          | Roll around local Z.                   |
| `Look`     | Mouse delta                                                                                          | Free-look while LookHeld held.         |
| `LookHeld` | RMB                                                                                                  | Gates `Look`.                          |
| `Fire`     | LMB                                                                                                  | Held-down fire (per-weapon reload).    |

ESC, digits `1`–`9`, and mouse wheel are polled by
`FlyShootingController` / `PauseMenu` outside the action map.

---

## Custom Layers

The project adds three user layers (`ProjectSettings/TagManager.asset`):

| Layer index | Name          | Used by                                                                          |
|-------------|---------------|----------------------------------------------------------------------------------|
| 6           | `PlacedCube`  | All placed shape instances (cube / slope / weapon). Build raycasts include this. |
| 7           | `AlphaCube`   | The single alpha cube. Build raycasts include this layer.                       |
| 8           | `PreviewCube` | The preview ghost (root + children). Excluded from raycasts so it cannot hit itself. |

`BuildManager` uses `LayerMask.GetMask` and falls back to "everything except `Ignore Raycast` and `PreviewCube`" if the layer names cannot be resolved (defensive — covers the case where a clean checkout has not yet imported `TagManager.asset`).

The project also adds a tag `AlphaCube` so the alpha cube is identifiable independently of its layer.

---

## Logging

A small file-logging facility runs alongside `Debug.Log`:

- `LogBootstrapper` — `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`. Replaces `Debug.unityLogger.logHandler` with a `FileLogHandler`.
- `FileLogHandler` — appends to a session-stamped file under `Logs/runtime-<timestamp>.log`. The default `UnityLogHandler` is preserved as a chain target so messages still appear in the Editor console.

The `Logs/` directory is git-ignored. Code uses category tags
(`UIManager`, `BuildManager`, `FlyController`, `FlyShooting`,
`HangarSelect`, `SaveManager`, `PauseMenu`, …) via
`Debug.unityLogger.Log(TAG, message)`.

---

## Render Pipeline & Materials

URP (`com.unity.render-pipelines.universal` 17.3). All cube materials
are URP/Lit:

| Material               | Surface     | Use                                                                  |
|------------------------|-------------|----------------------------------------------------------------------|
| `AlphaCubeMat`         | Transparent | Alpha cube. White, α 0.35, double-sided, no shadows.                 |
| `PlacedCubeMat`        | Opaque      | Cube + slope, armour material A.                                     |
| `PlacedCubeMatB`       | Opaque      | Armour material B.                                                   |
| `PlacedCubeMatC`       | Opaque      | Armour material C.                                                   |
| `PlacedCubeMatD`       | Opaque      | Armour material D.                                                   |
| `PlacedPrismMat`       | Opaque      | Default slope material (set at spawn by MaterialDefinition.ApplyTo). |
| `PyramidWeaponMat`     | Opaque      | Pyramid weapon shape.                                                |
| `CylinderWeaponMat`    | Opaque      | Cylinder weapon shape.                                               |
| `BulletMat`            | Opaque      | Pyramid-weapon projectile.                                           |
| `RocketMat`            | Opaque      | Cylinder-weapon projectile.                                          |
| `PreviewCubeMat`       | Transparent | Preview-bounds ghost (red-tinted via MPB for invalid placements).   |
| `AlphaCubeIndicatorMat`| Opaque      | Red arrow indicator.                                                 |

---

## Build Settings

`ProjectSettings/EditorBuildSettings.asset` registers, in order:

| Index | Scene                              |
|-------|------------------------------------|
| 0     | `Assets/Scenes/MainMenu.unity`     |
| 1     | `Assets/Scenes/HangarSelect.unity` |
| 2     | `Assets/Scenes/BuildScene.unity`   |
| 3     | `Assets/Scenes/FlyScene.unity`     |

The Universal 3D template's `SampleScene.unity` is left in the project
but is **not** registered.

`ProjectSettings.asset` sets `activeInputHandler: 1` (New Input System
only — legacy Input Manager is disabled).

---

## Pause Menu

`PauseMenu` is a `DontDestroyOnLoad` singleton spawned by a
`[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` hook so every play
session has exactly one instance — no scene wiring required. Execution
order is forced to `-1000` so its `Update` runs before any other
unmodified script.

- ESC opens (only in BuildScene / FlyScene; on MainMenu / HangarSelect ESC is left to the scene's own controller) and closes the overlay.
- While open, `Time.timeScale = 0` freezes physics + FixedUpdate. On close the previously-saved time scale is restored (not blindly set to 1) so a future "slow-mo" feature wouldn't be broken by pausing.
- Overlay layout: a full-screen dim panel + "Paused" title + two stacked buttons. The panel sits at sortingOrder 300 (above UIManager's 100 and BuildToolbar's 90); its `Image` is a raycast target so nothing beneath it is reachable.
- Buttons:
  - **Menu** — restores `timeScale = 1` and loads `MainMenu`.
  - **Back to Desktop** — quits the app (or stops Editor play mode).
- ESC closes the overlay (acts as Resume — no dedicated Resume button).
- `EscConsumedThisFrame` is a one-frame flag other ESC handlers (BuildToolbarController, HangarSelectController) check to avoid stealing the same key press.

---

## Implementation Notes & Constraints

- **No third-party runtime assets.** Shape geometry uses Unity's
  built-in cube primitive for the cube; runtime-generated meshes
  (`PrimitiveMeshes.TriangularPrism`, `SquarePyramid`, `HollowCylinder`)
  for the others — cached and shared across instances via
  `MeshFilter.sharedMesh`. The `PrismMeshAuthor` / `PyramidMeshAuthor`
  components attach the appropriate mesh on Awake **only when the slot
  is empty**, so a manually-imported `.obj` mesh wired into the prefab
  takes precedence and is never overwritten.
- **No physics simulation** for the construct in Fly mode — the
  transform is moved directly. Projectiles also move kinematically and
  do not interact with the construct in v1 (hit detection is in scope
  for the next pass).
- **TextMeshPro is not used.** The shipped UI is built in code with
  legacy `UnityEngine.UI.Text` + `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`. This avoids requiring users to import
  *TMP Essentials* before the UI renders.
- **Drag uses `Mathf.Exp(-drag * dt)`** (frame-rate-independent
  exponential decay).
- **Input polling pattern.** Input callbacks (`performed`) cannot
  reliably check `EventSystem.current.IsPointerOverGameObject()` — the
  build manager and fly shooting controller poll
  `WasPerformedThisFrame()` / `IsPressed()` in `Update` instead, so the
  UI hit-test runs on the right thread.
- **Scene bootstrapping.** Each gameplay scene contains a `UIBootstrap`
  that is idempotent: it instantiates `UICanvas` only if
  `UIManager.Instance == null`. Pressing Play directly on `BuildScene`
  or `FlyScene` is supported. `PauseMenu` is even simpler — it
  bootstraps from a `RuntimeInitializeOnLoadMethod`, so it's alive
  before any scene loads.
