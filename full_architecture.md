# Cube Fly, also known as Cosmic Scrap Club — Architecture Overview

A three-scene Unity 6.3 LTS / URP demonstrator. Players navigate from a
**Main Menu** into the **Hangar** (BuildScene), assemble a cube
construct, then pilot it in **FlyScene**. Cube data and a persistent
corner button survive scene transitions.

The canonical product spec lives in `cube_fly_spec.md`. Onboarding /
controls / how to run lives in `README.md`. This document is the
implementation map.

---

## Runtime Architecture

```
                 ┌────────────────────────────────────────────┐
                 │ static class GameData       (CubeFly.Core) │
                 │ • IReadOnlyList<Placement> PlacedCubes     │
                 │ • TryAdd / Remove / IsOccupied / Clear     │
                 │ • IsAdjacentToExisting / GetConstructBounds│
                 │ • SumPlacedMasses(CubeTypeRegistry)        │
                 │ • Neighbors[]   (shared 6-face deltas)     │
                 └────────────────────────────────────────────┘
                                 ▲
                                 │
       ┌─────────────────────────┼─────────────────────────────┐
       │                         │                             │
┌──────┴──────┐         ┌────────┴────────┐         ┌──────────┴──────────┐
│  MainMenu   │         │   BuildScene    │         │      FlyScene       │
│ • MainMenu- │         │ • BuildManager  │         │ • FlyController     │
│   Controller│ ──────► │ • CubePreview   │ ──────► │ • FlyCamera         │
│             │  load   │ • BuildCamera   │  load   │ • CubeConstruct GO  │
│             │         │ • BuildIndicator│         │ • UIBootstrap       │
│             │         │ • BuildToolbar  │         │                     │
│             │         │ • UIBootstrap   │         │                     │
└──────┬──────┘         └────────┬────────┘         └──────────┬──────────┘
       │                         │                             │
       └─────────────────────────┼─────────────────────────────┘
                                 │
                       ┌─────────┴───────────────────────┐
                       │ UIManager (DontDestroyOnLoad)   │
                       │ • Persistent corner button      │
                       │ • Hidden on MainMenu, visible   │
                       │   on BuildScene + FlyScene      │
                       └─────────────────────────────────┘

           ┌───────────────────────────────────────────┐
           │ ScriptableObject content                  │
           │ • CubeTypeRegistry  (Assets/CubeTypes/)   │
           │ • CubeTypeDefinition × 4 (A / B / C / D)  │
           └───────────────────────────────────────────┘

           ┌───────────────────────────────────────────┐
           │ Logging                                   │
           │ • LogBootstrapper (BeforeSceneLoad)       │
           │ • FileLogHandler → Logs/runtime-*.log     │
           └───────────────────────────────────────────┘
```

**Persistence model.** `GameData` is a *static* C# class — its data
naturally survives `SceneManager.LoadScene` for the lifetime of the
play session, with no `DontDestroyOnLoad` needed. The persistent UI
*does* use `DontDestroyOnLoad`, but only on a single `UIManager`
singleton with a strict duplicate-instance guard.

**Input.** New Input System only. A hand-rolled wrapper
(`CubeFlyInputActions`) exposes two action maps: `Build`
(`Place` / `RotateZ` / `RotateX`) and `Fly` (`Thrust` / `Pitch` /
`Yaw` / `Roll` / `Look` / `LookHeld`).

**Physics.** No `Rigidbody`. `Transform` is moved directly each
`FixedUpdate` for arcade flight feel.

**Raycasts.** Custom layers `PlacedCube` (6), `AlphaCube` (7),
`PreviewCube` (8). Build raycasts use `LayerMask.GetMask("PlacedCube",
"AlphaCube")` so the preview ghost can never hit itself, with a
defensive fallback to "all layers minus Ignore Raycast and PreviewCube".

---

## Directory Layout

```
<project root>/
├── README.md                         How to clone / open / play.
├── cube_fly_spec.md                  Canonical product spec.
├── full_architecture.md              This document.
├── .gitignore                        Unity caches / generated / IDE / agent.
├── Assets/
│   ├── Scenes/
│   │   ├── MainMenu.unity            Title screen, three buttons.
│   │   ├── BuildScene.unity          Hangar.
│   │   ├── FlyScene.unity            Flight.
│   │   └── SampleScene.unity         (Unity template default; unused.)
│   ├── Scripts/
│   │   ├── Core/                     Cross-scene types + UI plumbing.
│   │   ├── Build/                    BuildScene-only behaviours.
│   │   ├── Fly/                      FlyScene-only behaviours.
│   │   └── MainMenu/                 MainMenu-only behaviours.
│   ├── CubeTypes/                    CubeTypeRegistry + per-type SOs.
│   ├── Prefabs/                      AlphaCube, PlacedCube[A–D], Preview, Indicator.
│   ├── Materials/                    URP/Lit material variants (+ ext/ PBR).
│   ├── Input/                        Input Actions asset + C# wrapper.
│   ├── UI/                           Persistent UI prefabs.
│   └── Settings/                     URP render-pipeline assets.
├── Packages/
│   └── manifest.json                 UPM package manifest (URP, Input, uGUI).
└── ProjectSettings/                  Unity project settings (layers, build list, …).
```

---

## Scenes

| File | Role |
|---|---|
| `Assets/Scenes/MainMenu.unity` | First scene loaded. Hosts `MainMenuController` (builds title + 3 buttons in code), Main Camera, Directional Light. The `UIManager` corner button is hidden here. |
| `Assets/Scenes/BuildScene.unity` | Hangar. Hosts `BuildManager` (with `CubePreview`, `BuildToolbarController`, `BuildIndicatorController`), `Main Camera` with `BuildCamera`, Directional Light, `UIBootstrap`. AlphaCube and PreviewCube are spawned at runtime. |
| `Assets/Scenes/FlyScene.unity` | Flight. Hosts `UIBootstrap`, `CubeConstruct` (positioned at `(0, 10, 0)`), `FlyController` (rebuilds the construct from `GameData` on `Start`), `Main Camera` with `FlyCamera`, Directional Light. |

Registered in `ProjectSettings/EditorBuildSettings.asset` at indices
0 / 1 / 2.

---

## Scripts — Core (`CubeFly.Core`)

| File | Type | Responsibility |
|---|---|---|
| `Scripts/Core/GameData.cs` | static class | Source of truth for the construct. List of `Placement` (cell + type index + rotation), occupancy dictionary, adjacency rules, AABB, mass sum. |
| `Scripts/Core/CubeStats.cs` | `MonoBehaviour` | Per-cube placeholder stats (`healthPoints`, `armourValue`, `mass`). Attached to every placeable prefab. |
| `Scripts/Core/CubeTypeDefinition.cs` | `ScriptableObject` | One cube type: display name, prefab, default HP/AV/mass, `ApplyDefaultsTo(CubeStats)`, `EffectiveMass()`. |
| `Scripts/Core/CubeTypeRegistry.cs` | `ScriptableObject` | Ordered array of `CubeTypeDefinition` indexed by `Placement.TypeIndex`. |
| `Scripts/Core/UIManager.cs` | `MonoBehaviour` singleton | Persistent canvas + corner button. Subscribes to `SceneManager.sceneLoaded`, swaps the label between `Fly!` / `Hangar`, hides the canvas on Main Menu. |
| `Scripts/Core/UIStyle.cs` | static helpers | Shared UI builders: `BuildScreenSpaceCanvas`, `EnsureEventSystem` (DDOL-aware), `BuildLabeledButton`, `BuildLabel`. Uses legacy `UI.Text` + `LegacyRuntime.ttf`. |
| `Scripts/Core/UIBootstrap.cs` | `MonoBehaviour` | One-shot `Awake` that instantiates `UICanvas` if `UIManager.Instance == null`. One per gameplay scene so either can be entered directly via Play. |
| `Scripts/Core/SceneSwitcher.cs` | static class | Single `Toggle()` method. From BuildScene → FlyScene; from FlyScene → BuildScene. Wired by `UIManager`. |
| `Scripts/Core/FileLogHandler.cs` | `ILogHandler` | Append-only file logger. Wraps the default Unity log handler so messages still hit the Editor console. |
| `Scripts/Core/LogBootstrapper.cs` | static initialiser | `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`. Swaps `Debug.unityLogger.logHandler` for a `FileLogHandler` writing to `Logs/runtime-<timestamp>.log`. |

---

## Scripts — Build (`CubeFly.Build`)

| File | Type | Responsibility |
|---|---|---|
| `Scripts/Build/BuildManager.cs` | `MonoBehaviour` | Owns the build-scene state machine: active type index, active rotation, active tool (`BuildTool.Place` / `BuildTool.Delete`), spawn registry, mass-budget check. Subscribes to `Build.Place` / `Build.RotateZ` / `Build.RotateX`. Handles delete-tool red `MaterialPropertyBlock` hover and post-delete flood-fill. Spawns the alpha cube at scene start. Fires `CurrentTypeChanged`, `CurrentToolChanged`, `CurrentRotationChanged`, `ConstructChanged`. |
| `Scripts/Build/CubePreview.cs` | `MonoBehaviour` | Owns a runtime-spawned ghost cube. Each `Update` raycasts through the cursor against `PlacedCube`/`AlphaCube`, applies a small `hit.normal * 0.01f` nudge before `RoundToInt`, shows/hides based on adjacency validity, and applies `BuildManager.CurrentRotation`. Mesh and colour mirror the active cube type; scale 0.99 to avoid z-fighting. Hidden when the active tool is not `Place`. |
| `Scripts/Build/BuildCamera.cs` | `MonoBehaviour` | Orbit camera (right-mouse drag rotates azimuth/elevation, scroll wheel zooms). Elevation clamped ±80°. |
| `Scripts/Build/BuildToolbarController.cs` | `MonoBehaviour` | Builds the build-scene UI overlays at runtime: bottom toolbar (one button per type + Delete), top-left `Rotate: R/T` hint, top-centre fading floating message ("Too much mass!"), bottom-left `Mass: X / 100` and `HP: Y` stat labels (refreshed on `ConstructChanged`). |
| `Scripts/Build/BuildIndicatorController.cs` | `MonoBehaviour` | Reparents a small red arrow prefab to the cube with the highest local-Z so the player can see the ship's "front". Resets the indicator's world rotation to identity each frame so the arrow stays world-aligned even when its parent cube is rotated. |
| `Scripts/Build/PlacedCubeData.cs` | `MonoBehaviour` | Trivial data carrier: stores the `Vector3Int cell` of a placed cube so removal raycasts can identify which grid cell to delete. |

---

## Scripts — Fly (`CubeFly.Fly`)

| File | Type | Responsibility |
|---|---|---|
| `Scripts/Fly/FlyController.cs` | `MonoBehaviour` | On `Start`, instantiates AlphaCube + every `GameData.PlacedCubes` entry as children of the referenced `construct` Transform (preserving each placement's rotation; applying `CubeTypeDefinition.ApplyDefaultsTo` per spawned cube). Computes a single `_massMultiplier` from total mass: `Lerp(1.0, 0.1, Clamp01((mass - 10) / 90))`. Reads `Fly.Thrust` / `Pitch` / `Yaw` / `Roll` in `Update`; applies per-axis throttle accumulation, magnitude clamp to `maxSpeed`, exponential drag (`Mathf.Exp(-drag * dt)`), local-frame translation, local pitch, world-space yaw (avoids roll coupling), local roll — all rate-scaled by `_massMultiplier` — in `FixedUpdate`. |
| `Scripts/Fly/FlyCamera.cs` | `MonoBehaviour` | One-shot dynamic offset on `Start` from `GameData.GetConstructBounds()` (clamped 5–50 units). `LateUpdate` ship-stuck follow with RMB free-look gate (`Fly.LookHeld`) and snap-back blend on release. |

---

## Scripts — MainMenu (`CubeFly.MainMenu`)

| File | Type | Responsibility |
|---|---|---|
| `Scripts/MainMenu/MainMenuController.cs` | `MonoBehaviour` | `Awake` builds the title + three buttons (`Hangar` → loads `BuildScene`; `Settings` → placeholder log; `Exit` → `Application.Quit` / stops Editor play mode). Uses `UIStyle` so its visuals match the in-game corner button. |

---

## Input (`CubeFly.Input`)

| File | Role |
|---|---|
| `Assets/Input/CubeFlyInputActions.inputactions` | Input System asset describing the same shape as the C# wrapper. Kept for editor tooling; not the source of truth. |
| `Assets/Input/CubeFlyInputActions.cs` | Hand-rolled wrapper around `InputActionMap`, mirroring the shape of Unity's *Generate C# Class* output. **Build map**: `Place` ← LMB, `RotateZ` ← R, `RotateX` ← T. **Fly map**: `Thrust` ← 3D-vector composite (W/S forward, A/D strafe, Space/C up), `Pitch` ← ↑/↓, `Yaw` ← ←/→, `Roll` ← Q/E, `Look` ← Mouse delta, `LookHeld` ← RMB. Defining bindings in code keeps compilation independent of editor wrapper-generation. |

---

## Prefabs and Materials

### Prefabs

| File | Notes |
|---|---|
| `Prefabs/AlphaCube.prefab` | 1×1×1 cube + `BoxCollider` + `CubeStats`. Tag `AlphaCube`, layer `AlphaCube`. No shadow casting. Material: `AlphaCubeMat`. |
| `Prefabs/PlacedCube.prefab` | Cube A. Cube + collider + `PlacedCubeData` + `CubeStats`. Layer `PlacedCube`. Material: `PlacedCubeMat`. |
| `Prefabs/PlacedCubeB.prefab` | Cube B. Same shape; material: `PlacedCubeMatB`. Heavier-armour stats. |
| `Prefabs/PlacedCubeC.prefab` | Cube C. Material: `PlacedCubeMatC`. Light/cheap stats. |
| `Prefabs/PlacedCubeD.prefab` | Cube D. Material: `PlacedCubeMatD`. High-HP / heavy stats. |
| `Prefabs/PreviewCube.prefab` | Cube only (no collider). Layer `PreviewCube`. `MaterialPropertyBlock`-friendly material: `PreviewCubeMat`. Scaled 0.99 to avoid z-fighting. |
| `Prefabs/AlphaCubeIndicator.prefab` | Small red arrow used by `BuildIndicatorController` to flag the front of the construct. |

### Materials

| File | Surface | Use |
|---|---|---|
| `Materials/AlphaCubeMat.mat` | Transparent | Alpha cube. White, α 0.35, double-sided, no shadows. |
| `Materials/PlacedCubeMat.mat` | Opaque | Cube A flat colour. |
| `Materials/PlacedCubeMatB.mat` | Opaque | Cube B flat colour. |
| `Materials/PlacedCubeMatC.mat` | Opaque | Cube C flat colour (green). |
| `Materials/PlacedCubeMatD.mat` | Opaque | Cube D flat colour (purple). |
| `Materials/PreviewCubeMat.mat` | Opaque | Preview ghost — colour is swapped at runtime. |
| `Materials/AlphaCubeIndicatorMat.mat` | Opaque | Arrow indicator. |
| `Materials/ext/` | — | Optional PBR textures from an earlier iteration. Not currently referenced by the shipped prefabs. |

---

## CubeTypes

| File | Role |
|---|---|
| `Assets/CubeTypes/CubeTypeRegistry.asset` | The single registry instance. `BuildManager.cubeTypeRegistry` and `FlyController.cubeTypeRegistry` reference this asset. |
| `Assets/CubeTypes/CubeTypeA.asset` | Cube A: standard. References `Prefabs/PlacedCube.prefab`. |
| `Assets/CubeTypes/CubeTypeB.asset` | Cube B: heavy-armour variant. References `Prefabs/PlacedCubeB.prefab`. |
| `Assets/CubeTypes/CubeTypeC.asset` | Cube C: light variant. References `Prefabs/PlacedCubeC.prefab`. |
| `Assets/CubeTypes/CubeTypeD.asset` | Cube D: high-HP variant. References `Prefabs/PlacedCubeD.prefab`. |

The order of types in `CubeTypeRegistry.types` defines `Placement.TypeIndex`. Reordering invalidates previously placed cubes within a session — it does **not** persist across launches anyway.

---

## UI

| File | Role |
|---|---|
| `Assets/UI/UICanvas.prefab` | Canvas (Screen Space Overlay) + CanvasScaler (1920×1080) + GraphicRaycaster + `UIManager`. The corner button is built at runtime by `UIManager.BuildButton` so the prefab carries no font dependency. |
| `Assets/UI/UIBootstrap.prefab` | Trivial GameObject hosting `UIBootstrap` with a serialized reference to `UICanvas`. One instance lives in each gameplay scene. |

`MainMenu`'s UI is built entirely at runtime by `MainMenuController`, so it does not need its own canvas prefab.

---

## Project Settings

| File | Edits beyond Unity defaults |
|---|---|
| `ProjectSettings/EditorBuildSettings.asset` | `MainMenu` index 0, `BuildScene` index 1, `FlyScene` index 2. |
| `ProjectSettings/TagManager.asset` | Added tag `AlphaCube`; added user layers `PlacedCube` (6), `AlphaCube` (7), `PreviewCube` (8). |
| `ProjectSettings/ProjectSettings.asset` | `activeInputHandler: 1` (New Input System only). |
| `Packages/manifest.json` | URP 17.3, Input System ≥ 1.19, uGUI/TextMeshPro — all bundled with the Universal 3D template. The MCP-for-Unity package is pinned to its upstream git URL; the embedded checkout under `Packages/com.coplaydev.unity-mcp/` is git-ignored so the upstream source is re-fetched on clone. |

---

## Lifecycle Walkthroughs

**Cold start.**
`LogBootstrapper` swaps the Unity logger before any scene loads.
`MainMenu.unity` loads. `MainMenuController.Awake` calls
`UIStyle.EnsureEventSystem` and builds the title + three buttons. The
persistent `UIManager` does not yet exist; the corner button is absent
on the menu by design.

**Main Menu → Hangar.**
**Hangar** click → `SceneManager.LoadScene("BuildScene")`. Build scene's
`UIBootstrap.Awake` instantiates `UICanvas`; `UIManager.Awake` claims
the singleton, applies `DontDestroyOnLoad`, and shows the corner button
labelled `Fly!`. `BuildManager.Awake` constructs the
`CubeFlyInputActions` wrapper and subscribes to `Place` / `RotateZ` /
`RotateX`. `BuildManager.Start` resolves camera/preview/cubeRoot
references, spawns the alpha cube, and re-instantiates any cubes
already in `GameData.PlacedCubes`. `BuildToolbarController.Start`
builds the toolbar from `BuildManager.Registry`. `CubePreview.Awake`
spawns its (initially hidden) ghost.

**Build → Fly.**
Corner-button click → `SceneSwitcher.Toggle()`. BuildScene unloads;
`GameData` (static) and `UIManager` (DDOL) survive. FlyScene loads;
its `UIBootstrap` sees `UIManager.Instance != null` and no-ops.
`FlyController.Start` rebuilds the construct under the `CubeConstruct`
transform, applies stat defaults per cube, computes `_massMultiplier`
from total mass, and logs the resulting % slowdown. `FlyCamera.Start`
computes its one-shot offset from `GameData.GetConstructBounds()`.
`UIManager` swaps the corner-button label to `Hangar`.

**Fly → Build.**
Symmetric: FlyScene tears down, `GameData` and `UIManager` persist,
BuildScene reinstantiates placed cubes from `GameData` into the cube
root.

---

## Notable Implementation Notes

1. **UIManager builds its button hierarchy in code** when the
   serialized `Button` / `Text` fields are null. The prefab YAML
   therefore does not reference any font asset that would require the
   user to import *TMP Essentials* before the prefab could resolve.
   This also lets the corner button share visuals with the Main Menu
   buttons (both via `UIStyle.BuildLabeledButton`).
2. **Drag uses `Mathf.Exp(-drag * dt)`** instead of the textbook
   `Mathf.Pow(1 - drag, dt)`. The latter produces a negative base for
   the default `drag = 2.0`. Same frame-rate-independent exponential
   shape; only the parameterization differs.
3. **Mass scales `accelerationRate`, `pitchSensitivity`, `yawSensitivity`,
   and `rollSpeed`** — but not `maxSpeed` or `drag`. A heavy ship reaches
   the same top speed eventually, just slowly; turn rate is reduced in
   parallel so a brick handles like a brick.
4. **Yaw is applied in world space** (`Rotate(Vector3.up, yaw, Space.World)`)
   to keep "left/right" intuitive when the ship is pitched. Pitch and
   roll remain local-space.
5. **Input UI hit-testing.** `EventSystem.current.IsPointerOverGameObject()`
   does not return correct results from inside `InputAction.performed`
   callbacks. `BuildManager` polls `WasPerformedThisFrame()` in `Update`
   instead, so the UI raycast runs on the correct frame phase.
6. **`MaterialPropertyBlock` for delete-hover** instead of swapping a
   material instance — no per-cube material allocations and the tint
   cleanly clears on un-hover.
7. **CoplayDev MCP-for-Unity is git-ignored.** `Packages/manifest.json`
   pins it to the upstream git URL; the embedded checkout
   (`Packages/com.coplaydev.unity-mcp/`) is excluded so the upstream
   source is re-fetched on clone. This keeps the repo light and lets
   users pick up upstream fixes automatically.
