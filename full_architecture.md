# Cube Fly, also known as Cosmic Scrap Club — Architecture Overview

A four-scene Unity 6.3 LTS / URP demonstrator. Players navigate from a
**Main Menu** into a **Hangar slot picker** (HangarSelect), pick a save
slot, assemble a cube construct in the **Hangar** (BuildScene), then
pilot it in **FlyScene** — with weapon cubes that fire on LMB. Cube
data is held in-memory by a static `GameData` class and persisted to
disk per slot by `SaveManager`.

The canonical product spec lives in `cube_fly_spec.md`. Onboarding /
controls / how to run lives in `README.md`. The Fly-mode shooting
system has its own deep-dive at `weapon_shooting_spec.md`. This
document is the implementation map.

---

## Runtime Architecture

```
                 ┌────────────────────────────────────────────┐
                 │ static class GameData       (CubeFly.Core) │
                 │ • IReadOnlyList<Placement>  PlacedCubes    │
                 │ • ActiveSlot  (-1 = autosave off)          │
                 │ • TryAdd / Remove / IsOccupied / Clear     │
                 │ • IsValidAttachment  (symmetric face check)│
                 │ • GetConstructBounds                       │
                 │ • SumPlacedMasses / SumPlacedHealthPoints  │
                 │ • LoadFromSave / ToSave                    │
                 │ • Neighbors[]   (shared 6-face deltas)     │
                 └────────────────────────────────────────────┘
                                 ▲
                                 │
   ┌─────────────────┬───────────┴──────────────────┬───────────────────────┐
   │                 │                              │                       │
┌──┴─────┐ ┌─────────┴─────────┐ ┌──────────────────┴────────┐ ┌────────────┴──────────┐
│MainMenu│ │   HangarSelect    │ │       BuildScene          │ │       FlyScene        │
│Main-   │ │ HangarSelect-     │ │ • BuildManager            │ │ • FlyController       │
│Menu-   │ │   Controller      │ │   - shape/material        │ │ • FlyCamera           │
│Cntrlr  │ │ (slot picker UI;  │ │     registries            │ │ • FlyCrosshair        │
│        │ │  reads SaveManager│ │   - autosave coroutine    │ │ • FlyShooting-        │
│        │ │  metadata; routes │ │ • CubePreview (composite) │ │   Controller          │
│        │ │  with ActiveSlot  │ │ • BuildCamera             │ │ • FlyWeaponToolbar-   │
│        │ │  armed)           │ │ • BuildIndicator-         │ │   Controller          │
│        │ │                   │ │     Controller            │ │ • CubeConstruct GO    │
│        │ │                   │ │ • BuildToolbarController  │ │ • Weapon instances:   │
│        │ │                   │ │ • UIBootstrap             │ │   PyramidWeapon /     │
│        │ │                   │ │                           │ │   CylinderWeapon      │
│        │ │                   │ │                           │ │ • Projectile spawns:  │
│        │ │                   │ │                           │ │   Bullet, Rocket      │
│        │ │                   │ │                           │ │ • UIBootstrap         │
└────────┘ └───────────────────┘ └──────────┬────────────────┘ └──────────┬────────────┘
                                            │                             │
                                            └────────┬────────────────────┘
                                                     │
                                ┌────────────────────┴────────────────┐
                                │ DontDestroyOnLoad singletons        │
                                │ • UIManager — corner button         │
                                │   (hidden on MainMenu+HangarSelect, │
                                │    label flips Fly!↔Hangar)         │
                                │ • PauseMenu — ESC overlay           │
                                │   (self-bootstraps before any scene)│
                                └─────────────────────────────────────┘

           ┌────────────────────────────────────────────────────┐
           │ ScriptableObject content (decoupled axes)          │
           │ • ShapeRegistry      (Assets/Shapes/)              │
           │   - ShapeCube, ShapeSlope,                         │
           │     ShapeWeaponPyramid, ShapeWeaponCylinder        │
           │ • MaterialRegistry   (Assets/Materials/Defs/)      │
           │   - MaterialA / B / C / D                          │
           │   + coupled weapon mat defs:                       │
           │     PyramidWeaponMatDef, CylinderWeaponMatDef      │
           └────────────────────────────────────────────────────┘

           ┌────────────────────────────────────────────────────┐
           │ Persistence                                        │
           │ • SaveManager   — Saves/slotN.json (Editor) /     │
           │                   persistentDataPath/saves/ (build)│
           │ • ConstructSave + PlacementRecord (schema v1)      │
           │ • Atomic write: File.Replace → rename-to-bak       │
           │   fallback                                         │
           └────────────────────────────────────────────────────┘

           ┌────────────────────────────────────────────────────┐
           │ Logging                                            │
           │ • LogBootstrapper (BeforeSceneLoad)                │
           │ • FileLogHandler → Logs/runtime-*.log              │
           └────────────────────────────────────────────────────┘
```

**Persistence model.** `GameData` is a *static* C# class — its data
naturally survives `SceneManager.LoadScene` for the lifetime of the
play session, with no `DontDestroyOnLoad` needed. The persistent UI
uses `DontDestroyOnLoad` on a single `UIManager` singleton; `PauseMenu`
uses a second DDOL singleton bootstrapped from `BeforeSceneLoad`.

**On-disk saves** are handled separately by `SaveManager` (atomic
`File.Replace` with fallback) reading/writing `ConstructSave` JSON.
BuildScene autosaves on `ConstructChanged` (0.25 s debounce) and
flushes immediately on scene tear-down. `GameData.ActiveSlot < 0`
disables autosave (Play-from-scene during dev).

**Input.** New Input System only. A hand-rolled wrapper
(`CubeFlyInputActions`) exposes two action maps: `Build`
(`Place` / `RotateZ` / `RotateX`) and `Fly` (`Thrust` / `Pitch` /
`Yaw` / `Roll` / `Look` / `LookHeld` / `Fire`). ESC, `M`, digit keys,
and mouse-scroll are polled directly outside the action map.

**Physics.** No `Rigidbody`. `Transform` is moved directly each
`FixedUpdate` for arcade flight feel. Projectiles also move
kinematically (no Rigidbody / Collider in v1).

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
├── weapon_shooting_spec.md           Fly-mode shooting system deep dive.
├── .gitignore                        Unity caches / generated / IDE / agent / Saves.
├── Saves/                            (Editor / project dev — git-ignored)
│   └── slot0.json / slot1.json / slot2.json
├── Assets/
│   ├── Scenes/
│   │   ├── MainMenu.unity            Title screen, three buttons.
│   │   ├── HangarSelect.unity        Save-slot picker.
│   │   ├── BuildScene.unity          Hangar.
│   │   ├── FlyScene.unity            Flight.
│   │   └── SampleScene.unity         (Unity template default; unused.)
│   ├── Scripts/
│   │   ├── Core/                     Cross-scene types + UI plumbing + save layer.
│   │   ├── Build/                    BuildScene-only behaviours.
│   │   ├── Fly/                      FlyScene-only behaviours (+ shooting system).
│   │   ├── HangarSelect/             Slot-picker scene controller.
│   │   └── MainMenu/                 MainMenu-only behaviours.
│   ├── Shapes/                       ShapeRegistry + per-shape SOs.
│   ├── Materials/
│   │   ├── Defs/                     MaterialRegistry + per-material SOs (+ coupled weapon mats).
│   │   └── *.mat                     URP/Lit material variants used by prefabs / SOs.
│   ├── Prefabs/
│   │   ├── AlphaCube / PlacedCube[A–D] / PlacedPrism / PlacedPyramid / PlacedCylinder
│   │   ├── PreviewCube / AlphaCubeIndicator
│   │   └── Projectiles/Bullet, Rocket
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
| `Assets/Scenes/MainMenu.unity` | First scene loaded. Hosts `MainMenuController` (builds title + 3 buttons in code), Main Camera, Directional Light. The `UIManager` corner button is hidden here. Clicking **Hangar** loads `HangarSelect` (not `BuildScene` directly). |
| `Assets/Scenes/HangarSelect.unity` | Save-slot picker. Hosts `HangarSelectController` (builds 3 slot cards + Cancel button in code, reads metadata via `SaveManager.ReadAllSlotMetadata`), Main Camera, Directional Light. The `UIManager` corner button is hidden here. On primary-click, arms `GameData.ActiveSlot`, calls `GameData.Clear()` (empty slot) or `GameData.LoadFromSave(...)` (filled slot), then loads `BuildScene`. ESC cancels back to MainMenu. |
| `Assets/Scenes/BuildScene.unity` | Hangar. Hosts `BuildManager` (with `CubePreview`, `BuildToolbarController`, `BuildIndicatorController`), `Main Camera` with `BuildCamera`, Directional Light, `UIBootstrap`. AlphaCube and the composite preview are spawned at runtime. Autosaves on `ConstructChanged` (0.25 s debounce). |
| `Assets/Scenes/FlyScene.unity` | Flight. Hosts `UIBootstrap`, `CubeConstruct` (positioned at `(0, 10, 0)`), `FlyController` (rebuilds the construct from `GameData` on `Start`, collects spawned `WeaponBehavior` instances), `FlyShootingController`, `FlyWeaponToolbarController`, `FlyCrosshair`, `Main Camera` with `FlyCamera`, Directional Light. |

Registered in `ProjectSettings/EditorBuildSettings.asset` at indices
0 / 1 / 2 / 3 respectively.

---

## Scripts — Core (`CubeFly.Core`)

| File | Type | Responsibility |
|---|---|---|
| `Scripts/Core/GameData.cs` | static class | Source of truth for the construct. List of `Placement` (cell + shape index + material index + rotation), occupancy dict, symmetric face-validity check, AABB, mass/HP sums, `ActiveSlot`, `LoadFromSave` / `ToSave`. |
| `Scripts/Core/ConstructSave.cs` | `[Serializable]` POCO | On-disk save schema (v1). Holds `slotName`, ticks, denormalised totals, and a `PlacementRecord[]`. Also defines `PlacementRecord` (cell + shape/material **by name** + rot Euler) and `SaveSlotInfo` (read-only struct used by the slot picker — DateTime-safe ticks parser included). |
| `Scripts/Core/SaveManager.cs` | static class | Filesystem layer over `ConstructSave`. `SlotPath` / `Exists` / `TryLoad` / `Save` / `Delete` / `ReadAllSlotMetadata`. Atomic write via `File.Replace` with `AtomicReplaceFallback` (rename-existing-to-bak, move-temp-to-final, delete-bak; recoverable on partial failure). Saves go to `<project root>/Saves/` in Editor or under `Application.persistentDataPath/saves/` in built players. |
| `Scripts/Core/CubeStats.cs` | `MonoBehaviour` | Per-cube placeholder stats (`healthPoints`, `armourValue`, `mass`). Attached to every placeable prefab. Populated at spawn by `MaterialDefinition.ApplyTo`. |
| `Scripts/Core/ShapeDefinition.cs` | `ScriptableObject` | One placeable shape — geometry + collider + per-face attachment validity bools (six). `ShapeCategory` is `Armour` or `Weapon`. Armour shapes pull material from `MaterialRegistry`; weapon shapes use a coupled `weaponMaterial`. Exposes `IsLocalFaceValid`, `IsWorldFaceValid(rotation)`, `ResolveMaterial(index, registry)`, `IsWeapon`. |
| `Scripts/Core/ShapeRegistry.cs` | `ScriptableObject` | Ordered array of `ShapeDefinition` indexed by `Placement.ShapeIndex`. Also provides `FindIndexByName` for the save layer's name-based resolution. |
| `Scripts/Core/MaterialDefinition.cs` | `ScriptableObject` | Visual material + (HP, AV, mass) stats. `ApplyTo(GameObject)` walks all `Renderer`s and writes the material, then writes stats into the spawned `CubeStats`. `SwatchColor` powers the toolbar's corner badges. |
| `Scripts/Core/MaterialRegistry.cs` | `ScriptableObject` | Ordered array of `MaterialDefinition` for armour shapes. `FindIndexByName` for save layer parity. Weapon-shape materials live on the shape SO, not in here. |
| `Scripts/Core/PauseMenu.cs` | DDOL singleton, `[DefaultExecutionOrder(-1000)]` | ESC pause overlay for BuildScene + FlyScene. Self-bootstraps via `RuntimeInitializeOnLoadMethod(BeforeSceneLoad)`. Two buttons (`Menu` / `Back to Desktop`); ESC closes (acts as Resume). Sets `Time.timeScale = 0` while open; restores the previous value on close. `IsOpen` and `EscConsumedThisFrame` are read by other scripts to gate gameplay input and avoid double-handling. |
| `Scripts/Core/PrimitiveMeshes.cs` | static class | Lazily-built shared meshes for shapes that aren't built-in primitives: `TriangularPrism`, `SquarePyramid`, `HollowCylinder` (32-segment, smooth walls, flat ±Y annuli). Designed to fit a 1×1×1 cell so adjacency / collider behavior matches the cube primitive. |
| `Scripts/Core/PrismMeshAuthor.cs` | `MonoBehaviour` | Assigns `PrimitiveMeshes.TriangularPrism` to the `MeshFilter` (and `MeshCollider` if present) **only when those slots are empty**, so an authored / imported mesh wired into the prefab is never overwritten. (The shipped `PlacedPrism.prefab` has a prism mesh prewired, so this component is currently a fallback / no-op there.) |
| `Scripts/Core/PyramidMeshAuthor.cs` | `MonoBehaviour` | Same pattern for `PrimitiveMeshes.SquarePyramid`. (`PlacedPyramid.prefab` likewise has its mesh prewired; this is a fallback.) |
| `Scripts/Core/CylinderMeshAuthor.cs` | `MonoBehaviour` | Same pattern for `PrimitiveMeshes.HollowCylinder`. `PlacedCylinder.prefab` ships with an empty `MeshFilter.sharedMesh`, so this component is the **primary** source of the cylinder mesh at runtime. |
| `Scripts/Core/UIManager.cs` | DDOL singleton | Persistent canvas + corner button. Subscribes to `SceneManager.sceneLoaded`; flips label between `Fly!` / `Hangar`; hides the canvas on MainMenu and HangarSelect (allowlist-style — future utility scenes default to hidden). |
| `Scripts/Core/UIStyle.cs` | static helpers | Shared UI builders: `BuildScreenSpaceCanvas`, `EnsureEventSystem` (DDOL-aware), `BuildLabeledButton`, `BuildLabel`. Uses legacy `UI.Text` + `LegacyRuntime.ttf`. Exposes shared colour constants (`BackgroundIdle`, etc.) used by toolbars. |
| `Scripts/Core/UIBootstrap.cs` | `MonoBehaviour` | One-shot `Awake` that instantiates `UICanvas` if `UIManager.Instance == null`. One per gameplay scene so either can be entered directly via Play. |
| `Scripts/Core/SceneSwitcher.cs` | static class | Single `Toggle()` method that flips BuildScene ↔ FlyScene. Wired by `UIManager`'s corner button. Not used by the MainMenu → HangarSelect → BuildScene path. |
| `Scripts/Core/FileLogHandler.cs` | `ILogHandler` | Append-only file logger. Wraps the default Unity log handler so messages still hit the Editor console. |
| `Scripts/Core/LogBootstrapper.cs` | static initialiser | `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`. Swaps `Debug.unityLogger.logHandler` for a `FileLogHandler` writing to `Logs/runtime-<timestamp>.log`. |

---

## Scripts — Build (`CubeFly.Build`)

| File | Type | Responsibility |
|---|---|---|
| `Scripts/Build/BuildManager.cs` | `MonoBehaviour` | Owns the build-scene state machine: active shape index, per-shape material memory dict, active rotation, active tool (`BuildTool.Place` / `BuildTool.Delete`), spawn registry, mass-budget check. Subscribes to `Build.Place` / `Build.RotateZ` / `Build.RotateX`. Handles delete-tool red `MaterialPropertyBlock` hover and post-delete flood-fill. Spawns the alpha cube at scene start. Owns the autosave coroutine (0.25 s debounce on `ConstructChanged`; flushes on `OnDestroy`). Fires `CurrentShapeChanged`, `CurrentMaterialChanged(shape, material)`, `CurrentToolChanged`, `CurrentRotationChanged`, `ConstructChanged`. |
| `Scripts/Build/CubePreview.cs` | `MonoBehaviour` | Owns a runtime-spawned **composite** ghost: an outer translucent unit-cube (cell-bounds visualiser, world-axis aligned) plus an inner shape-prefab instance scaled to 0.7 (shows the actual shape being placed). The inner mesh rotates with `R/T`; the outer cube doesn't (rotating it would just flicker without changing which cell is occupied). Each `Update` raycasts through the cursor against `PlacedCube`/`AlphaCube`, nudges the hit by `hit.normal * 0.01f` before `RoundToInt`, runs `GameData.IsValidAttachment` for face-validity, shows/hides based on the result. Valid placements clear the bounds-cube tint (default green); invalid placements tint it red via `MaterialPropertyBlock`. Hidden when the active tool is not `Place`. |
| `Scripts/Build/BuildCamera.cs` | `MonoBehaviour` | Orbit camera (right-mouse drag rotates azimuth/elevation, scroll wheel zooms). Elevation clamped ±80°. |
| `Scripts/Build/BuildToolbarController.cs` | `MonoBehaviour` | Builds the build-scene UI overlays at runtime: bottom shape toolbar (one button per shape with a corner swatch showing the armed material, plus a Delete button), per-shape material flyout for armour shapes and a separate weapons flyout for weapon shapes (both peek-on-hover, click-to-pin, `M` toggles the one for the active shape's category, Esc closes), top-left `Rotate: R/T` hint, top-centre fading floating message ("Too much mass!"), bottom-left `Mass: X / 100` and `HP: Y` stat labels (refreshed on `ConstructChanged`). Polls digits `1`–`9` (no modifier) to arm an **armour shape** by on-screen toolbar slot order — weapons aren't reachable from the digit row — and `Shift`+digit `1`–`9` to arm the active armour shape's **material** by registry index. |
| `Scripts/Build/BuildIndicatorController.cs` | `MonoBehaviour` | Reparents a small red arrow prefab to the cube with the highest local-Z so the player can see the ship's "front". Resets the indicator's world rotation to identity each frame so the arrow stays world-aligned even when its parent cube is rotated. |
| `Scripts/Build/PlacedCubeData.cs` | `MonoBehaviour` | Trivial data carrier: stores the `Vector3Int cell` of a placed shape so removal raycasts can identify which grid cell to delete. |

---

## Scripts — Fly (`CubeFly.Fly`)

| File | Type | Responsibility |
|---|---|---|
| `Scripts/Fly/FlyController.cs` | `MonoBehaviour` | On `Start`, instantiates AlphaCube + every `GameData.PlacedCubes` entry as children of the referenced `construct` Transform. For each placement, resolves the `ShapeDefinition` prefab, applies the chosen `MaterialDefinition` via `ApplyTo`, and — if the spawned root carries a `WeaponBehavior` — wires its `Construct` + `Shape` and collects it for the shooting controller. Computes `_massMultiplier` from total mass: `Lerp(1.0, 0.1, Clamp01((mass - 10) / 90))`. Reads `Fly.Thrust` / `Pitch` / `Yaw` / `Roll` in `Update` (zeroed while `PauseMenu.IsOpen`); applies per-axis throttle accumulation, magnitude clamp to `maxSpeed`, exponential drag (`Mathf.Exp(-drag * dt)`), local-frame translation, local pitch, world-space yaw, local roll — all rate-scaled by `_massMultiplier` — in `FixedUpdate`. |
| `Scripts/Fly/FlyCamera.cs` | `MonoBehaviour` | One-shot dynamic offset on `Start` from `GameData.GetConstructBounds()` (clamped 5–50 units). `LateUpdate` ship-stuck follow with RMB free-look gate (`Fly.LookHeld`) and snap-back blend on release. While `PauseMenu.IsOpen`, behaves as if RMB were released — frees the cursor (so menu buttons are reachable) and lets the orbit offset relax to neutral. The body follow is naturally frozen by `Time.timeScale = 0`. |
| `Scripts/Fly/FlyCrosshair.cs` | `MonoBehaviour`, `[DefaultExecutionOrder(100)]` | Screen-space reticle (centre dot + four arms) that projects `construct.position + construct.forward * aimRange` to screen space each LateUpdate. Runs after `FlyCamera`'s LateUpdate so the camera transform is final by the time the projection happens. The same value is what `FlyShootingController` passes as the aim target so on-screen reticle and actual aim agree. Hidden when the projected point is behind the camera. Skipped while `PauseMenu.IsOpen` (last computed position holds). |
| `Scripts/Fly/FlyShootingController.cs` | `MonoBehaviour` | Owns the list of weapons grouped by `ShapeDefinition` (`WeaponTypeGroup` instances), the currently-selected type index, and all shoot-related input polling: Fire (LMB via `Fly.Fire`), digits 1–9 (direct select), mouse wheel (cycle, edge-detected). Each frame Fire is held, calls `TryFire(crosshairWorldTarget)` on every weapon of the selected type. Fires `TypesChanged` and `SelectedChanged` events for the toolbar UI. Skipped while `PauseMenu.IsOpen` or pointer is over UI. |
| `Scripts/Fly/FlyWeaponToolbarController.cs` | `MonoBehaviour` | Bottom-of-screen weapon toolbar UI built in code: one button per distinct weapon type with a corner swatch (matching the weapon's `weaponMaterial.SwatchColor`) and a thin reload bar above. Subscribes to `TypesChanged` (rebuild) and `SelectedChanged` (highlight); per-frame updates each reload bar's `fillAmount` from `WeaponTypeGroup.ReadyFraction`. Hidden entirely when the construct has no weapons. |
| `Scripts/Fly/WeaponBehavior.cs` | abstract `MonoBehaviour` | Base for any weapon-cube. Owns the reload cooldown (ticking down in `Update` regardless of selection) and `TryFire(crosshairWorldTarget)` public entry point. Subclasses implement `protected abstract void Fire(Vector3 target)`. Construct and Shape references are wired by `FlyController.BuildConstruct` after instantiation. |
| `Scripts/Fly/PyramidWeapon.cs` | `WeaponBehavior` subclass | Machine-gun-style. Spawn position = pyramid's apex (`transform.TransformPoint((0, 0.5, 0))`). Aim rule: if the tip direction (`transform.up`) aligns with `Construct.forward` (dot > 0.7 = cos 45°), fire at the shared crosshair world target; otherwise fire along the tip direction. 90°-stepped placements give an exact ±1 / 0 dot, so the threshold cleanly bins "frontal" vs "off-axis". |
| `Scripts/Fly/CylinderWeapon.cs` | `WeaponBehavior` subclass | Rocket-launcher style. Spawn position = cylinder centre (`transform.position`). Launch direction = barrel open-end (`transform.up` after rotation, since `ShapeWeaponCylinder.faceNegY` is the only valid attachment face). Hands the rocket the spawn pos, launch dir, exit-plane pos (`+launchExitDistance` along launch dir), and the shared crosshair target. |
| `Scripts/Fly/Bullet.cs` | `MonoBehaviour` | Straight-line projectile for `PyramidWeapon`. `Launch(origin, direction)` arms it; `Update` advances by `speed * dt`; despawns after `maxRange`. No Rigidbody / Collider in v1 — hit detection is in scope for the next pass. |
| `Scripts/Fly/Rocket.cs` | `MonoBehaviour` | Two-phase projectile for `CylinderWeapon`. **Exit phase**: travel along the launch direction until past the exit-plane (dot test against the captured exit world position). **Seek phase**: re-orient toward the locked crosshair target and travel straight to it. Target is captured once at `Launch`; later ship rotation has no effect. Despawns after `maxRange` in seek phase. |

See `weapon_shooting_spec.md` for the system-level design (frame
sequencing, aim agreement, dispatch ordering).

---

## Scripts — HangarSelect (`CubeFly.HangarSelect`)

| File | Type | Responsibility |
|---|---|---|
| `Scripts/HangarSelect/HangarSelectController.cs` | `MonoBehaviour` | Builds the slot picker UI in code on `Awake`: title, three slot cards, Cancel button. Each card carries its own state (`IsEmpty`, `DeleteConfirming`, etc.) and is rendered by `ApplySlotInfo` from `SaveSlotInfo`. Polls digits `1`–`3` (activate slot) and `Esc` (cancel). Inline-confirm delete: first click on a slot's `Delete` switches its label to `Yes, delete` + shows a Cancel button + starts a 5 s auto-cancel coroutine; second click commits the deletion via `SaveManager.Delete` and re-renders the card to its empty state. On activation: `GameData.SetActiveSlot(slot)`, then `GameData.Clear()` (empty) or `GameData.LoadFromSave(...)` (filled), then `SceneManager.LoadScene("BuildScene")`. |

---

## Scripts — MainMenu (`CubeFly.MainMenu`)

| File | Type | Responsibility |
|---|---|---|
| `Scripts/MainMenu/MainMenuController.cs` | `MonoBehaviour` | `Awake` builds the title + three buttons (`Hangar` → loads `HangarSelect`; `Settings` → placeholder log; `Exit` → `Application.Quit` / stops Editor play mode). Uses `UIStyle` so its visuals match the in-game corner button. |

---

## Input (`CubeFly.Input`)

| File | Role |
|---|---|
| `Assets/Input/CubeFlyInputActions.inputactions` | Input System asset describing the same shape as the C# wrapper. Kept for editor tooling; not the source of truth. |
| `Assets/Input/CubeFlyInputActions.cs` | Hand-rolled wrapper around `InputActionMap`, mirroring the shape of Unity's *Generate C# Class* output. **Build map**: `Place` ← LMB, `RotateZ` ← R, `RotateX` ← T. **Fly map**: `Thrust` ← 3D-vector composite (W/S forward, A/D strafe, Space/C up), `Pitch` ← ↑/↓, `Yaw` ← ←/→, `Roll` ← Q/E, `Look` ← Mouse delta, `LookHeld` ← RMB, `Fire` ← LMB. ESC, `M`, digits 1–9, and mouse scroll are polled directly outside the action map. Defining bindings in code keeps compilation independent of editor wrapper-generation. |

---

## Prefabs and Materials

### Prefabs (`Assets/Prefabs/`)

| File | Notes |
|---|---|
| `AlphaCube.prefab` | 1×1×1 cube + `BoxCollider` + `CubeStats`. Tag `AlphaCube`, layer `AlphaCube`. No shadow casting. Material: `AlphaCubeMat`. |
| `PlacedCube.prefab` | Cube shape. Cube primitive + `BoxCollider` + `PlacedCubeData` + `CubeStats`. Layer `PlacedCube`. Material set at spawn by `MaterialDefinition.ApplyTo`. |
| `PlacedCubeB.prefab` / `PlacedCubeC.prefab` / `PlacedCubeD.prefab` | Cube-shape prefab variants with their default armour material pre-applied (used by the cube SO and by older save data; the active spawn path always applies via `ApplyTo`). |
| `PlacedPrism.prefab` | Slope shape. Prewired triangular-prism mesh on the `MeshFilter` + `BoxCollider` (cell-bounds; cheap and correct for grid-cell raycasts) + `PlacedCubeData` + `CubeStats` + `PrismMeshAuthor` (fallback that no-ops when the mesh slot is already populated, as it is here). Layer `PlacedCube`. |
| `PlacedPyramid.prefab` | Pyramid weapon shape. Prewired square-pyramid mesh on the `MeshFilter` + `BoxCollider` (cell-bounds — same rationale as the prism) + `PlacedCubeData` + `CubeStats` + `PyramidMeshAuthor` (fallback) + `PyramidWeapon`. Bullet prefab wired into the `WeaponBehavior.projectilePrefab` slot. Layer `PlacedCube`. |
| `PlacedCylinder.prefab` | Cylinder weapon shape. Empty `MeshFilter.sharedMesh` — `CylinderMeshAuthor` populates it from `PrimitiveMeshes.HollowCylinder` at Awake — + `BoxCollider` (cell-bounds) + `PlacedCubeData` + `CubeStats` + `CylinderWeapon`. Rocket prefab wired into `projectilePrefab`. Layer `PlacedCube`. |
| `PreviewCube.prefab` | Translucent unit cube only (no collider, no `PlacedCubeData`). Layer `PreviewCube`. `MaterialPropertyBlock`-friendly material: `PreviewCubeMat`. `CubePreview` instantiates it as the *bounds-ghost* half of the composite preview and additionally instantiates the active shape's prefab as the inner mesh. |
| `AlphaCubeIndicator.prefab` | Small red arrow used by `BuildIndicatorController` to flag the front of the construct. |
| `Projectiles/Bullet.prefab` | `Bullet` script + visual mesh + `BulletMat`. Spawned by `PyramidWeapon.Fire`. |
| `Projectiles/Rocket.prefab` | `Rocket` script + visual mesh + `RocketMat`. Spawned by `CylinderWeapon.Fire`. |

### Materials (`Assets/Materials/`)

| File | Surface | Use |
|---|---|---|
| `AlphaCubeMat.mat` | Transparent | Alpha cube. White, α 0.35, double-sided, no shadows. |
| `PlacedCubeMat.mat` | Opaque | Default armour material A (cube + slope). |
| `PlacedCubeMatB.mat` / `PlacedCubeMatC.mat` / `PlacedCubeMatD.mat` | Opaque | Armour materials B / C / D. |
| `PlacedPrismMat.mat` | Opaque | Slope-shape default material variant. |
| `PyramidWeaponMat.mat` | Opaque | Pyramid weapon shape. |
| `CylinderWeaponMat.mat` | Opaque | Cylinder weapon shape. |
| `BulletMat.mat` | Opaque | Pyramid-weapon projectile. |
| `RocketMat.mat` | Opaque | Cylinder-weapon projectile. |
| `PreviewCubeMat.mat` | Transparent | Bounds-ghost half of the composite preview; tinted red via `MaterialPropertyBlock` for invalid placements. |
| `AlphaCubeIndicatorMat.mat` | Opaque | Arrow indicator. |

---

## Shapes (`Assets/Shapes/`)

| File | Role |
|---|---|
| `ShapeRegistry.asset` | The single registry instance. `BuildManager.shapeRegistry`, `FlyController.shapeRegistry`, and `HangarSelectController.shapeRegistry` reference this asset. |
| `ShapeCube.asset` | Cube shape. `category = Armour`. All six faces valid. Prefab: `PlacedCube.prefab`. |
| `ShapeSlope.asset` | Slope shape. `category = Armour`. Valid faces: bottom (-Y), back (-Z), left (-X), right (+X). Front (+Z) and top (+Y) are cut away. Prefab: `PlacedPrism.prefab`. |
| `ShapeWeaponPyramid.asset` | Pyramid weapon shape. `category = Weapon`. Only the bottom (-Y) face is valid (mounting base). Prefab: `PlacedPyramid.prefab`. Coupled material: `PyramidWeaponMatDef.asset`. |
| `ShapeWeaponCylinder.asset` | Cylinder weapon shape. `category = Weapon`. Only the bottom (-Y) face is valid. Prefab: `PlacedCylinder.prefab`. Coupled material: `CylinderWeaponMatDef.asset`. |

The order of shapes in `ShapeRegistry.shapes` defines `Placement.ShapeIndex` within a session. The on-disk save layer
uses `displayName` instead of the index, so reordering doesn't invalidate existing saves.

---

## Materials Defs (`Assets/Materials/Defs/`)

| File | Role |
|---|---|
| `MaterialRegistry.asset` | The single registry instance for **armour** materials. `BuildManager.materialRegistry`, `FlyController.materialRegistry`, and `HangarSelectController.materialRegistry` reference this asset. |
| `MaterialA.asset` / `B.asset` / `C.asset` / `D.asset` | Armour materials. Each pairs a URP/Lit `Material` with HP / AV / mass placeholder stats. `ApplyTo(placed)` writes both the renderer material and the stats into the spawned cube. |
| `PyramidWeaponMatDef.asset` | Coupled weapon material referenced by `ShapeWeaponPyramid.weaponMaterial`. Not in `MaterialRegistry`. |
| `CylinderWeaponMatDef.asset` | Coupled weapon material referenced by `ShapeWeaponCylinder.weaponMaterial`. Not in `MaterialRegistry`. |

---

## UI

| File | Role |
|---|---|
| `Assets/UI/UICanvas.prefab` | Canvas (Screen Space Overlay) + CanvasScaler (1920×1080) + GraphicRaycaster + `UIManager`. The corner button is built at runtime by `UIManager.BuildButton` so the prefab carries no font dependency. |
| `Assets/UI/UIBootstrap.prefab` | Trivial GameObject hosting `UIBootstrap` with a serialized reference to `UICanvas`. One instance lives in each gameplay scene. |

`MainMenu`, `HangarSelect`, the build toolbar, the pause overlay, the
fly weapon toolbar, and the fly crosshair all build their UI **in
code** via `UIStyle.BuildScreenSpaceCanvas` and friends — no per-scene
canvas prefabs needed.

---

## Project Settings

| File | Edits beyond Unity defaults |
|---|---|
| `ProjectSettings/EditorBuildSettings.asset` | `MainMenu` index 0, `HangarSelect` index 1, `BuildScene` index 2, `FlyScene` index 3. |
| `ProjectSettings/TagManager.asset` | Added tag `AlphaCube`; added user layers `PlacedCube` (6), `AlphaCube` (7), `PreviewCube` (8). |
| `ProjectSettings/ProjectSettings.asset` | `activeInputHandler: 1` (New Input System only). |
| `Packages/manifest.json` | URP 17.3, Input System ≥ 1.19, uGUI/TextMeshPro — all bundled with the Universal 3D template. The MCP-for-Unity package is pinned to its upstream git URL; the embedded checkout under `Packages/com.coplaydev.unity-mcp/` is git-ignored so the upstream source is re-fetched on clone. |

---

## Lifecycle Walkthroughs

**Cold start.**
`LogBootstrapper` swaps the Unity logger before any scene loads.
`PauseMenu.Bootstrap` (also `BeforeSceneLoad`) spawns the DDOL pause
singleton. `MainMenu.unity` loads. `MainMenuController.Awake` calls
`UIStyle.EnsureEventSystem` and builds the title + three buttons. The
persistent `UIManager` does not yet exist; the corner button is absent
on the menu by design.

**Main Menu → Hangar Slot Selector.**
**Hangar** click → `SceneManager.LoadScene("HangarSelect")`. Build
settings index 1 loads. `HangarSelectController.Awake` builds the
slot UI; `RefreshAllCards` calls
`SaveManager.ReadAllSlotMetadata()` and renders each card.

**HangarSelect → Hangar.**
Slot click (or digit 1/2/3) → `GameData.SetActiveSlot(i)`, then
`GameData.Clear()` (empty slot) or `GameData.LoadFromSave(...)`
(filled). `SceneManager.LoadScene("BuildScene")`. BuildScene's
`UIBootstrap.Awake` instantiates `UICanvas`; `UIManager.Awake` claims
the singleton, applies `DontDestroyOnLoad`, and shows the corner
button labelled `Fly!`. `BuildManager.Awake` constructs the
`CubeFlyInputActions` wrapper and subscribes to `Place` / `RotateZ` /
`RotateX`. `BuildManager.Start` resolves camera/preview/cubeRoot
references, spawns the alpha cube, re-instantiates any cubes already in
`GameData.PlacedCubes`, then subscribes `ScheduleAutosave` to
`ConstructChanged` so subsequent edits are persisted.

**Build → Fly.**
Corner-button click → `SceneSwitcher.Toggle()`. BuildScene unloads;
its `OnDestroy` flushes any pending autosave so the slot file is
current. `GameData` (static) and `UIManager` / `PauseMenu` (DDOL)
survive. FlyScene loads; its `UIBootstrap` sees `UIManager.Instance
!= null` and no-ops. `FlyController.Start` rebuilds the construct
under the `CubeConstruct` transform, applies each placement's chosen
`MaterialDefinition`, collects spawned `WeaponBehavior` instances,
computes `_massMultiplier`, and hands the weapons list to
`FlyShootingController.RegisterWeapons` (which groups by
`ShapeDefinition` and fires `TypesChanged`). `FlyWeaponToolbarController`
rebuilds the bottom toolbar from those events. `FlyCamera.Start`
computes its one-shot offset from `GameData.GetConstructBounds()`.
`UIManager` swaps the corner-button label to `Hangar`.

**Pause.**
ESC pressed in BuildScene or FlyScene → `PauseMenu.Open` sets
`Time.timeScale = 0`, shows the dim panel + "Paused" title + Menu /
Back to Desktop buttons. `BuildManager.Update` / `FlyController.Update`
/ `FlyShootingController.Update` see `PauseMenu.IsOpen` and
short-circuit. `FlyCamera.Update` treats RMB as released, freeing the
cursor for menu clicks. ESC again → `PauseMenu.Close` restores the
saved `timeScale`. **Menu** → set `timeScale = 1`, load MainMenu.
**Back to Desktop** → set `timeScale = 1`, quit / stop Editor play.

**Fly → Build.**
Symmetric to Build → Fly: FlyScene tears down, `GameData` and
`UIManager` / `PauseMenu` persist, BuildScene reinstantiates placed
cubes from `GameData`. If autosave is armed, the construct as flown
is the construct as already saved (BuildScene's previous flush ran on
its `OnDestroy`).

**Build → MainMenu (via Pause).**
ESC then `Menu` from BuildScene → BuildScene's `OnDestroy` flushes any
pending autosave; MainMenu loads. `GameData` still holds the
in-memory placements but `ActiveSlot` is unchanged; if the player
re-enters via HangarSelect → same slot → `LoadFromSave` clobbers
in-memory state with the disk snapshot, which is the same data.

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
   callbacks. `BuildManager` polls `WasPerformedThisFrame()` in `Update`,
   and `FlyShootingController` polls `IsPressed()` plus raw `Keyboard` /
   `Mouse` for digits and scroll, so the UI raycast runs on the correct
   frame phase.
6. **`MaterialPropertyBlock` for delete-hover AND preview-invalid tint**
   instead of swapping a material instance — no per-cube material
   allocations and the tint cleanly clears on un-hover / on validity
   change.
7. **Composite preview** (bounds ghost + inner mesh) lets the player
   see *both* which grid cell will be occupied *and* the actual shape
   that will be placed (with its rotation). The bounds cube stays
   world-axis aligned; only the inner mesh rotates with `R/T`.
8. **Atomic save fallback.** `File.Replace` is the preferred path
   (truly atomic on most platforms); a try/catch over
   `PlatformNotSupportedException` / `IOException` /
   `UnauthorizedAccessException` falls back to a
   rename-existing-to-bak pattern. A partial failure leaves the bak
   available for recovery; success cleans the bak up.
9. **Save schema uses names, not indices.** `PlacementRecord.shape` /
   `.material` reference the `displayName` fields of their respective
   SOs so reordering the registries doesn't invalidate saves. Weapon
   placements still write a non-empty `material` for diagnosability,
   but the load path resolves via the shape's `weaponMaterial` (the
   saved name is informational).
10. **Edge-detected scroll input.** `FlyShootingController` bins the
    raw scroll delta into `{-1, 0, +1}` against a configurable
    deadzone, and only cycles on transitions from `0 → ±1`. A
    Windows ±120-unit notch and a small trackpad swipe both
    produce one cycle.
11. **CoplayDev MCP-for-Unity is git-ignored.** `Packages/manifest.json`
    pins it to the upstream git URL; the embedded checkout
    (`Packages/com.coplaydev.unity-mcp/`) is excluded so the upstream
    source is re-fetched on clone. This keeps the repo light and lets
    users pick up upstream fixes automatically.
