# Cube Fly — Specification

**Engine:** Unity 6.3 LTS (6000.3) · **Render Pipeline:** Universal Render
Pipeline (URP) — Universal 3D template.

**Project goal.** A three-scene interactive demonstrator where the player
opens a Main Menu, builds a cube structure in a Hangar, and then pilots
the assembled construct like a plane. Cube placement data and persistent
UI carry across scene transitions.

This document is the single canonical spec. Companion docs:

- `full_architecture.md` — implementation-level architecture overview.
- `README.md` — clone / open / run instructions and a controls cheat-sheet.

---

## High-Level Overview

| Scene        | Purpose                                                                                                            |
|--------------|--------------------------------------------------------------------------------------------------------------------|
| `MainMenu`   | First scene the player sees. Shows three buttons: **Hangar** → BuildScene, **Settings** (placeholder), **Exit**.   |
| `BuildScene` | Hangar. The player places, rotates, and deletes cubes around a fixed semi-transparent **alpha cube**.              |
| `FlyScene`   | Pilot mode. The construct is rebuilt from the saved data and flown with 6-axis thrust + pitch/yaw/roll + camera mouse-look. |

A persistent corner button (`Fly!` / `Hangar`) sits in the top-right of
BuildScene and FlyScene and toggles between them. The button is hidden
on the Main Menu.

---

## Data & Persistence Model

`CubeFly.Core.GameData` is a **static** C# class. Its data lives for the
duration of the play session — it does not need `DontDestroyOnLoad`
because static fields survive scene unloads naturally.

Each placement is recorded as:

```csharp
public readonly struct Placement
{
    public readonly Vector3Int Cell;     // grid coordinate
    public readonly int        TypeIndex; // index into CubeTypeRegistry.types
    public readonly Quaternion Rotation;  // 90°-stepped orientation
}
```

Public surface:

- `IReadOnlyList<Placement> PlacedCubes`
- `bool TryAdd(Vector3Int cell, int typeIndex, Quaternion rotation)`
- `void Remove(Vector3Int cell)`
- `bool IsOccupied(Vector3Int cell)`
- `int  GetTypeAt(Vector3Int cell)`
- `bool IsAdjacentToExisting(Vector3Int cell)` — checks the six face neighbours; the implicit alpha cube at `(0,0,0)` counts as occupied.
- `Bounds GetConstructBounds()` — AABB including the alpha cube.
- `void Clear()`
- `float SumPlacedMasses(CubeTypeRegistry registry)` — does **not** include the alpha cube; the caller adds that explicitly.

Rules enforced by `TryAdd`:
- The origin cell `(0, 0, 0)` is reserved for the alpha cube and is rejected.
- A new cell is only accepted if at least one of its six face-neighbours is already occupied or is the origin.

The persistent UI uses `DontDestroyOnLoad` on a single `UIManager`
singleton with a duplicate-instance guard.

---

## Cube Types

Cube types are data-driven through ScriptableObjects so adding a new
cube type does **not** require code changes:

- `CubeTypeDefinition` — one ScriptableObject per type. Holds:
  - `displayName` — toolbar label.
  - `prefab` — the placed-cube prefab.
  - `defaultHealthPoints`, `defaultArmourValue`, `defaultMass` — fallback stats applied when the prefab's `CubeStats` field is zero.
  - `ApplyDefaultsTo(CubeStats stats)` — seeds zero-valued stats on the spawned cube.
  - `EffectiveMass()` — resolves the mass that mass-budget checks should use for a placement of this type.

- `CubeTypeRegistry` — single ScriptableObject (`Assets/CubeTypes/CubeTypeRegistry.asset`) containing the ordered array of `CubeTypeDefinition`. The order maps directly to `Placement.TypeIndex`, so re-ordering the array would invalidate previously placed cubes.

The shipped registry contains four types: **A**, **B**, **C**, **D**.
Their cosmetic identity is a flat URP/Lit colour material; their
gameplay identity (HP / AV / mass) is set in the prefab's `CubeStats`
component (with the SO defaults as fallback).

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

Every placeable cube prefab — including the alpha cube — has this
component attached. Per-cube mass is read from this component (or, if
zero, from the matching `CubeTypeDefinition`).

---

## Mass Budget

The construct's total mass is bounded and visibly affects flight feel.

**Cap.** `BuildManager.massLimit = 100`. Attempting to place a cube
that would push the total over the cap is rejected and the build UI
shows a fading red message ("Too much mass!") for ~5 s. The alpha
cube is included in the total.

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
- Clicking **Hangar** loads `BuildScene`. **Settings** is a placeholder hook. **Exit** quits the application (or stops Editor play mode).

### `BuildScene.unity`

Scene contents:

- `BuildManager` (with `CubePreview` and `BuildToolbarController` siblings).
- `Main Camera` with `BuildCamera` (orbit camera).
- `BuildIndicatorController` parents a small red arrow indicator above the cube with the highest local-Z so the player can see which face will lead in flight.
- Directional Light.
- `UIBootstrap` instantiates the persistent `UICanvas` if not already alive.

Runtime spawns:

- The **alpha cube** (1 × 1 × 1, semi-transparent, layer `AlphaCube`) at the origin. Never destroyed.
- A single **PreviewCube** (opaque, layer `PreviewCube`, scale 0.99 to avoid z-fighting) parented to the cursor's candidate cell. Its mesh and colour mirror the currently selected cube type.
- `PlacedCube*` instances for each entry already in `GameData`.

Build-scene UI overlays (built at runtime by `BuildToolbarController`):

- **Bottom-centre toolbar** — one button per cube type plus a **Delete** button. Selecting a type implicitly switches the active tool to **Place**; selecting **Delete** switches to the delete tool.
- **Top-left** — `Rotate: R/T` hint label.
- **Top-centre** — fading red floating message slot (used for "Too much mass!").
- **Bottom-left** — two stat readouts:
  - `Mass: X / 100` (live, recomputed on `ConstructChanged`).
  - `HP: Y` (sum of all placed cubes' health, including alpha).

Tools:

- **Place tool** — LMB places a cube of the selected type at the preview cell, rotated by `BuildManager.CurrentRotation`. Disallowed if the cell is occupied, not adjacent, or the placement would breach the mass cap.
- **Delete tool** — hovering tints the cube under the cursor red via `MaterialPropertyBlock` (no material allocation). LMB removes the cube and runs a flood-fill from the origin to drop any newly disconnected cubes.

Rotation:

- `R` rotates the active placement orientation by 90° around the Z axis.
- `T` rotates by 90° around the X axis.
- The orientation is per-active-placement (not per-cube-type) and is saved with each `Placement` so the construct rebuilds in `FlyScene` with each cube in its placed pose.

### `FlyScene.unity`

Scene contents:

- `CubeConstruct` empty GameObject — the construct's pivot. Starts at `(0, 10, 0)`.
- `FlyController` rebuilds the construct on `Start`, instantiating one `alphaCubePrefab` at the origin plus one prefab per `Placement` in `GameData`. Stat defaults are applied in the same pass.
- `Main Camera` with `FlyCamera`.
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

---

## Input

Single hand-rolled wrapper at `Assets/Input/CubeFlyInputActions.cs`
(no asset code-gen). Two action maps:

**Build map**

| Action    | Binding         | Purpose                                                                                                  |
|-----------|-----------------|----------------------------------------------------------------------------------------------------------|
| `Place`   | LMB             | In Place tool: places a cube. In Delete tool: removes the cube under the cursor.                         |
| `RotateZ` | `R`             | Rotates the next placement 90° around Z.                                                                 |
| `RotateX` | `T`             | Rotates the next placement 90° around X.                                                                 |

**Fly map**

| Action     | Binding                                                                                              | Purpose                       |
|------------|------------------------------------------------------------------------------------------------------|-------------------------------|
| `Thrust`   | 3D-vector composite: `W/S` (forward/back), `A/D` (strafe), `Space/C` (up/down)                        | Local-frame translation.      |
| `Pitch`    | 1D-axis composite: `↑` / `↓`                                                                          | Pitch around local X.         |
| `Yaw`      | 1D-axis composite: `←` / `→`                                                                          | Yaw around world Y.           |
| `Roll`     | 1D-axis composite: `Q` / `E`                                                                          | Roll around local Z.          |
| `Look`     | Mouse delta                                                                                          | Free-look while RMB held.     |
| `LookHeld` | RMB                                                                                                  | Gates `Look`.                 |

---

## Custom Layers

The project adds three user layers (`ProjectSettings/TagManager.asset`):

| Layer index | Name          | Used by                                                                          |
|-------------|---------------|----------------------------------------------------------------------------------|
| 6           | `PlacedCube`  | All A/B/C/D placed cubes. Build raycasts include this layer.                    |
| 7           | `AlphaCube`   | The single alpha cube. Build raycasts include this layer.                       |
| 8           | `PreviewCube` | The preview ghost. Excluded from raycasts so it cannot hit itself.              |

`BuildManager` uses `LayerMask.GetMask` and falls back to "everything except `Ignore Raycast` and `PreviewCube`" if the layer names cannot be resolved (defensive — covers the case where a clean checkout has not yet imported `TagManager.asset`).

The project also adds a tag `AlphaCube` so the alpha cube is identifiable independently of its layer.

---

## Logging

A small file-logging facility runs alongside `Debug.Log`:

- `LogBootstrapper` — `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`. Replaces `Debug.unityLogger.logHandler` with a `FileLogHandler`.
- `FileLogHandler` — appends to a session-stamped file under `Logs/runtime-<timestamp>.log`. The default `UnityLogHandler` is preserved as a chain target so messages still appear in the Editor console.

The `Logs/` directory is git-ignored. Code uses category tags
(`UIManager`, `BuildManager`, `FlyController`, …) via
`Debug.unityLogger.Log(TAG, message)`.

---

## Render Pipeline & Materials

URP (`com.unity.render-pipelines.universal` 17.3). All cube materials
are URP/Lit:

| Material               | Surface     | Use                                              |
|------------------------|-------------|--------------------------------------------------|
| `AlphaCubeMat`         | Transparent | Alpha cube. White, α 0.35, double-sided, no shadows. |
| `PlacedCubeMat`        | Opaque      | Cube A — flat colour.                             |
| `PlacedCubeMatB`       | Opaque      | Cube B — flat colour.                             |
| `PlacedCubeMatC`       | Opaque      | Cube C — flat colour.                             |
| `PlacedCubeMatD`       | Opaque      | Cube D — flat colour.                             |
| `PreviewCubeMat`       | Opaque      | Preview ghost — colour swapped at runtime to match the active type. |
| `AlphaCubeIndicatorMat`| Opaque      | Red arrow indicator.                             |

`Assets/Materials/ext/` holds optional PBR textures that an earlier
iteration referenced; the current shipped prefabs use the flat-colour
materials only.

---

## Build Settings

`ProjectSettings/EditorBuildSettings.asset` registers, in order:

| Index | Scene                          |
|-------|--------------------------------|
| 0     | `Assets/Scenes/MainMenu.unity` |
| 1     | `Assets/Scenes/BuildScene.unity` |
| 2     | `Assets/Scenes/FlyScene.unity` |

The Universal 3D template's `SampleScene.unity` is left in the project
but is **not** registered.

`ProjectSettings.asset` sets `activeInputHandler: 1` (New Input System
only — legacy Input Manager is disabled).

---

## Implementation Notes & Constraints

- **No third-party runtime assets.** All cube geometry uses Unity's
  built-in `Cube` primitive mesh.
- **No physics simulation** for the construct in Fly mode — the
  transform is moved directly. Rigidbody coupling, joints, etc. are
  intentionally out of scope.
- **TextMeshPro is not used.** The shipped UI is built in code with
  legacy `UnityEngine.UI.Text` + `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`. This avoids requiring users to import
  *TMP Essentials* before the UI renders.
- **Drag uses `Mathf.Exp(-drag * dt)`** (frame-rate-independent
  exponential decay).
- **Input polling pattern.** Input callbacks (`performed`) cannot
  reliably check `EventSystem.current.IsPointerOverGameObject()` — the
  build manager polls `WasPerformedThisFrame()` in `Update` instead so
  the UI hit-test runs on the right thread.
- **Scene bootstrapping.** Each gameplay scene contains a `UIBootstrap`
  that is idempotent: it instantiates `UICanvas` only if `UIManager.Instance == null`. Pressing Play directly on `BuildScene` or `FlyScene` is supported.
