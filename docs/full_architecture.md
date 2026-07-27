# Cube Fly, also known as Cosmic Scrap Club — Architecture Overview

A four-scene Unity 6.3 LTS / URP demonstrator. Players navigate from a
**Main Menu** into a **Hangar slot picker** (HangarSelect), pick a save
slot, assemble a cube construct in the **Hangar** (BuildScene), then
pilot it in **FlyScene** — with weapon cubes that fire on LMB. Cube
data is held in-memory by a static `GameData` class and persisted to
disk per slot by `SaveManager`.

The canonical product spec lives in `cube_fly_spec.md`. Onboarding /
controls / how to run lives in `../README.md`. The Fly-mode shooting
system has its own deep-dive at `weapon_shooting_spec.md`; the
power / shield / energy-weapon foundation (reactor / shield / laser
cubes + `ConstructEnergySystem`) is specced in `power_and_energy_spec.md`.
This document is the implementation map.

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
│Cntrlr  │ │ (slot picker UI;  │ │     registries            │ │ • FlyShooting-        │
│        │ │  reads SaveManager│ │   - autosave coroutine    │ │   Controller          │
│        │ │  metadata; routes │ │ • CubePreview (composite) │ │ • CubeConstruct GO    │
│        │ │  with ActiveSlot  │ │ • BuildCamera             │ │   (Rigidbody +        │
│        │ │  armed)           │ │ • BuildIndicator-         │ │    FlyCrashHandler +  │
│        │ │                   │ │     Controller            │ │    ConstructEnergy    │
│        │ │                   │ │ • BuildHUD GameObject:    │ │    System +           │
│        │ │                   │ │   - BuildHud canvas       │ │    RcsPuffVfx)        │
│        │ │                   │ │   - BuildToolbar-         │ │ • Weapon instances:   │
│        │ │                   │ │     Controller            │ │   PyramidWeapon /     │
│        │ │                   │ │   - BuildShipClass-       │ │   CylinderWeapon /    │
│        │ │                   │ │     Controller            │ │   LaserWeapon         │
│        │ │                   │ │                           │ │ • Thrusters: per-cube │
│        │ │                   │ │                           │ │   ThrusterVfx + child │
│        │ │                   │ │                           │ │   EnginePlume         │
│        │ │                   │ │                           │ │ • Reactor / Shield    │
│        │ │                   │ │                           │ │   cubes (passive)     │
│        │ │                   │ │                           │ │ • Projectile spawns:  │
│        │ │                   │ │                           │ │   Bullet, Rocket      │
│        │ │                   │ │                           │ │ • FlyHUD GameObject:  │
│        │ │                   │ │                           │ │   - FlyHud canvas     │
│        │ │                   │ │                           │ │   - FlyCrosshair      │
│        │ │                   │ │                           │ │   - FlyWeaponToolbar- │
│        │ │                   │ │                           │ │     Controller        │
│        │ │                   │ │                           │ │   - FlySpeedIndicator │
│        │ │                   │ │                           │ │   - FlyHpIndicator    │
│        │ │                   │ │                           │ │   - FlyBoostBar       │
│        │ │                   │ │                           │ │   - FlyShieldIndicator│
│        │ │                   │ │                           │ │   - FlyHeatBar        │
│        │ │                   │ │                           │ │ • Desert level:       │
│        │ │                   │ │                           │ │   DesertEnvironment + │
│        │ │                   │ │                           │ │   ~21 DesertTargets   │
└────────┘ └───────────────────┘ └──────────┬────────────────┘ └──────────┬────────────┘
                                            │                             │
                                            └────────┬────────────────────┘
                                                     │
                                ┌────────────────────┴────────────────────┐
                                │ DontDestroyOnLoad singletons            │
                                │ • PersistentHud — shared canvas         │
                                │   (lazy-created on first Instance       │
                                │    access; sortingOrder 200)            │
                                │ • UIManager — corner button             │
                                │   (hidden on MainMenu+HangarSelect+Fly, │
                                │    label flips Fly!↔Hangar)             │
                                │ • PauseMenu — ESC overlay               │
                                │ • GameOverMenu — Construct Destroyed    │
                                │   (panel canvas sortingOrder 400)       │
                                │ • SettingsMenu — tabbed Settings modal  │
                                │   (panel canvas sortingOrder 350)       │
                                │ • VfxApplier — URP profile sync         │
                                │   (no UI; listens to VfxSettings)       │
                                │ • LogBootstrapper — file logger         │
                                │ (PersistentHud is lazy-created; the     │
                                │  other six self-bootstrap BeforeSceneLoad.│
                                │  Whichever UI singleton first accesses  │
                                │  PersistentHud.Instance triggers its    │
                                │  creation — VfxApplier owns no UI.)     │
                                │                                         │
                                │ Lazy DDOL singleton (separate tier)     │
                                │ • TooltipHud — floating hover tooltip   │
                                │   (own canvas sortingOrder 500 — above  │
                                │    every other persistent UI; spawned   │
                                │    on first TooltipHud.Instance access) │
                                └─────────────────────────────────────────┘

           ┌────────────────────────────────────────────────────┐
           │ ScriptableObject content (decoupled axes)          │
           │ • ShapeRegistry      (Assets/Shapes/)              │
           │   - ShapeCube, ShapeSlope,                         │
           │     ShapeWeaponPyramid, ShapeWeaponCylinder,       │
           │     ShapeWeaponLaser, ShapeUtilityThruster,        │
           │     ShapeUtilityReactor, ShapeUtilityShield        │
           │ • MaterialRegistry   (Assets/Materials/Defs/)      │
           │   - MaterialA / B / C / D                          │
           │   + coupled weapon/utility mat defs:               │
           │     PyramidWeaponMatDef, CylinderWeaponMatDef,     │
           │     LaserMatDef, ThrusterMatDef,                   │
           │     ReactorMatDef, ShieldMatDef                    │
           └────────────────────────────────────────────────────┘

           ┌────────────────────────────────────────────────────┐
           │ Persistence                                        │
           │ • SaveManager   — Saves/slotN.json (Editor) /     │
           │                   persistentDataPath/saves/ (build)│
           │ • ConstructSave + PlacementRecord                  │
           │   (schema v2 — + shipClass)                        │
           │ • Atomic write: File.Replace → rename-to-bak       │
           │   fallback                                         │
           └────────────────────────────────────────────────────┘

           ┌────────────────────────────────────────────────────┐
           │ Logging                                            │
           │ • LogBootstrapper (DDOL singleton, self-           │
           │   bootstraps BeforeSceneLoad)                      │
           │ • FileLogHandler →                                 │
           │   persistentDataPath/Logs/CubeFly_*.log            │
           └────────────────────────────────────────────────────┘
```

**Persistence model.** `GameData` is a *static* C# class — its data
naturally survives `SceneManager.LoadScene` for the lifetime of the
play session, with no `DontDestroyOnLoad` needed. The persistent UI is
six `DontDestroyOnLoad` singletons; five of them self-bootstrap from
`[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` — `UIManager`
(corner scene-switch button), `PauseMenu` (ESC overlay), `GameOverMenu`
("Construct Destroyed" overlay), `SettingsMenu` (tabbed Settings modal),
and `LogBootstrapper` (file logger). The sixth, `PersistentHud`, is a
shared screen-space-overlay canvas at `sortingOrder 200` that hosts the
corner button + pause panel + game-over panel + settings panel; it's
**lazy-created** on first `Instance` access, triggered by whichever of
the five `BeforeSceneLoad` singletons Awakes first and calls
`PersistentHud.Instance.Root` to parent its UI. Both
`SettingsMenu`'s panel and `GameOverMenu`'s panel set
`Canvas.overrideSorting = true` so they render above the base
`sortingOrder 200` — Settings at 350, GameOver at 400. A seventh DDOL
singleton, `VfxApplier`, also self-bootstraps `BeforeSceneLoad` but
owns no UI: it listens to `VfxSettings.Changed` + `sceneLoaded` and
applies the active VFX toggles to the scene's URP volume profile.

`TooltipHud` is a **separate, lazy-created** DDOL singleton with its
own canvas at `sortingOrder 500` (above every other persistent UI —
tooltips always on top). It spawns only on first
`TooltipHud.Instance` access, which happens on the first
`TooltipTrigger.OnPointerEnter` with a non-empty tooltip string.

**On-disk saves** are handled separately by `SaveManager` (atomic
`File.Replace` with fallback) reading/writing `ConstructSave` JSON. The
schema is v2, carrying a `shipClass` name string alongside the
placements. BuildScene autosaves on `ConstructChanged` (0.25 s
debounce) and flushes immediately on scene tear-down.
`GameData.ActiveSlot < 0` disables autosave (Play-from-scene during
dev).

**Input.** New Input System only. A hand-rolled wrapper
(`CubeFlyInputActions`) exposes two action maps: `Build`
(`Place` / `RotateZ` / `RotateX`) and `Fly` (`Thrust` / `Pitch` /
`Yaw` / `Roll` / `Look` / `LookHeld` / `Fire` / `Boost`). ESC, `M`,
digit keys, and mouse-scroll are polled directly outside the action
map.

**Physics.** The construct is a non-kinematic `Rigidbody` on
`CubeConstruct` with the cube `BoxCollider`s forming a compound
collider. `FlyController` drives it with `AddForce` / `AddTorque`;
Unity handles collision response (bouncing off the ground and world
cubes). Projectiles, by contrast, still move kinematically with no
Rigidbody / Collider — they do their own swept raycasts.

**Raycasts.** Custom layers `PlacedCube` (6), `AlphaCube` (7),
`PreviewCube` (8), `World` (9 — desert terrain). Build raycasts use
`LayerMask.GetMask("PlacedCube", "AlphaCube")` so the preview ghost can
never hit itself (defensive fallback: "all layers minus Ignore Raycast and
PreviewCube"); **projectile** raycasts (`Bullet` / `Rocket` / `LaserWeapon`)
use `PlacedCube|AlphaCube|World`, so shots are blocked by the desert terrain
instead of passing through.

---

## Directory Layout

```
<project root>/
├── README.md                         How to clone / open / play.
├── ROADMAP.md                        Live planning doc (what's shipped, what's next).
├── .gitignore                        Unity caches / generated / IDE / agent / Saves.
├── docs/
│   ├── cube_fly_spec.md              Canonical product spec.
│   ├── full_architecture.md          This document.
│   ├── weapon_shooting_spec.md       Fly-mode shooting system deep dive.
│   ├── weapon_death_hud_spec.md      Weapon-toolbar response to weapon-cube death.
│   ├── thruster_boost_spec.md        Thruster cube + boost mechanic design.
│   ├── boost_overboost_tuning_spec.md   Boost / overboost tuning notes.
│   ├── desert_level_spec.md          Experimental desert level (see ROADMAP item 10).
│   ├── CODEBASE_REVIEW_AUDIT.txt     2026-05-17 codebase audit (historical artifact).
│   └── superpowers/                  Per-feature brainstorm specs + execution plans.
│       ├── specs/
│       └── plans/
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
│   │   ├── Core/                     Cross-scene types + UI plumbing + save layer + death anim.
│   │   ├── Build/                    BuildScene-only behaviours.
│   │   ├── Fly/                      FlyScene-only behaviours (shooting, crash detection, damage pipeline).
│   │   ├── HangarSelect/             Slot-picker scene controller.
│   │   ├── MainMenu/                 MainMenu-only behaviours.
│   │   ├── Desert/                   Integrated desert FlyScene level (see docs/desert_level_spec.md).
│   │   └── Editor/                   Editor-only tooling (excluded from runtime builds).
│   ├── Shapes/                       ShapeRegistry + per-shape SOs.
│   ├── Materials/
│   │   ├── Defs/                     MaterialRegistry + per-material SOs (+ coupled weapon mats).
│   │   └── *.mat                     URP/Lit material variants used by prefabs / SOs.
│   ├── PhysicMaterials/              PhysicsMaterial assets — bounce / friction for collisions.
│   ├── Prefabs/
│   │   ├── AlphaCube / PlacedCube[A–D] / PlacedPrism / PlacedPyramid / PlacedCylinder
│   │   ├── PlacedThruster                   Utility thruster cube.
│   │   ├── PlacedReactor / PlacedShield     Utility power cubes (reactor / shield).
│   │   ├── PlacedLaser                       Laser weapon cube.
│   │   ├── PreviewCube / AlphaCubeIndicator
│   │   ├── Ground / WorldTargetCube         Target-cube prefab (base for the desert `DesertTarget` variant); the flat `Ground` is retired.
│   │   └── Projectiles/Bullet, Rocket
│   ├── Input/                        Input Actions asset + C# wrapper.
│   ├── Resources/                     Brand fonts (`Fonts/` — Anton / Saira family) + 12 UI glyph sprites (`UI/Sprites/`), `Resources.Load`ed by the Csc UI system.
│   ├── Settings/                     URP render-pipeline assets.
│   └── VFX/                          Particle / post-FX assets (generated by `VfxAssetsInstaller`).
│       ├── Textures/                 Procedural glow sprite (`Glow_64.png`).
│       ├── Materials/                Additive URP particle materials — B-1 (`EnginePlumeMat`, `BoostShockMat`, `RcsPuffMat`) + B-2 (`MuzzleStarburstMat`, `MuzzleDiscMat`, `BulletTracerMat`, `RocketExhaustMat`), plus alpha-blended `BulletImpactDustMat` / `RocketSmokeTrailMat`.
│       └── Prefabs/                  ParticleSystem prefabs (`EnginePlume.prefab`, `RcsPuff.prefab`).
├── Packages/
│   └── manifest.json                 UPM package manifest (URP, Input, uGUI).
└── ProjectSettings/                  Unity project settings (layers, build list, …).
```

---

## Scenes

| File | Role |
|---|---|
| `Assets/Scenes/MainMenu.unity` | First scene loaded. Hosts `MainMenuController` (builds the **COSMIC SCRAP CLUB** wordmark + menu buttons in code over a warm procedural brand background), Main Camera, Directional Light. The `UIManager` corner button is hidden here. Clicking **Hangar** loads `HangarSelect` (not `BuildScene` directly). |
| `Assets/Scenes/HangarSelect.unity` | Save-slot picker. Hosts `HangarSelectController` (builds 3 slot cards + Cancel button in code, reads metadata via `SaveManager.ReadAllSlotMetadata`), Main Camera, Directional Light. The `UIManager` corner button is hidden here. On primary-click, arms `GameData.ActiveSlot`, calls `GameData.Clear()` (empty slot) or `GameData.LoadFromSave(...)` (filled slot), then loads `BuildScene`. ESC cancels back to MainMenu. |
| `Assets/Scenes/BuildScene.unity` | Hangar. Hosts `BuildManager` (with `CubePreview`, `BuildIndicatorController`), `Main Camera` with `BuildCamera`, Directional Light, and a `BuildHUD` GameObject carrying `BuildHud` (shared canvas, `sortingOrder 100`, `[DefaultExecutionOrder(-500)]`), `BuildToolbarController` (its bottom-left stat block shows `MASS:` / `HP:` / `POWER:` big-value readouts, POWER red-pulsing on a deficit), and `BuildShipClassController`. `BuildManager.ConfigureHangarLighting` flips the hangar to flat shadowless lighting at runtime. AlphaCube and the composite preview are spawned at runtime. Autosaves on `ConstructChanged` (0.25 s debounce). |
| `Assets/Scenes/FlyScene.unity` | Flight. Hosts `CubeConstruct` (positioned at `(0, 10, 0)`; carries a non-kinematic `Rigidbody` + `FlyCrashHandler` + a `ConstructEnergySystem` sibling to `FlyController`), `FlyController` with a `FlyShootingController` sibling, `Main Camera` with `FlyCamera`, Directional Light, and a `FlyHUD` GameObject carrying `FlyHud` (shared canvas, `sortingOrder 100`, `[DefaultExecutionOrder(-500)]`), `FlyCrosshair`, `FlyWeaponToolbarController`, `FlySpeedIndicator`, `FlyHpIndicator`, `FlyBoostBar`, `FlyShieldIndicator`, and `FlyHeatBar`. The `FlyController` carries serialized references to `Assets/VFX/Prefabs/EnginePlume.prefab` and `Assets/VFX/Prefabs/RcsPuff.prefab` which are AddComponent-wired during `BuildConstruct`. Also a `DesertEnvironment` prefab instance (dune ground + perimeter ridge + 5 rock formations, all on the `World` layer) and a `DesertTargets` container (~21 `DesertTarget`s + 3 `Turret`s) — the 500×500 cel-shaded desert combat level — plus a `DesertLook` GameObject carrying the live cel-look toggle + scene-local shadow pipeline. See `docs/desert_level_spec.md`. |

Registered in `ProjectSettings/EditorBuildSettings.asset` at indices
0 / 1 / 2 / 3 respectively.

---

## Scripts — Core (`CubeFly.Core`)

| File | Type | Responsibility |
|---|---|---|
| `Scripts/Core/GameData.cs` | static class | Source of truth for the construct. List of `Placement` (cell + shape index + material index + rotation), occupancy dict, symmetric face-validity check, AABB, mass/HP sums, `ActiveSlot`, `LoadFromSave` / `ToSave`. |
| `Scripts/Core/ConstructSave.cs` | `[Serializable]` POCO | On-disk save schema (v2). Holds `slotName`, ticks, denormalised totals, a `shipClass` name string, and a `PlacementRecord[]`. Also defines `PlacementRecord` (cell + shape/material **by name** + rot Euler) and `SaveSlotInfo` (read-only struct used by the slot picker — now also carries a `ShipClass`; DateTime-safe ticks parser included). |
| `Scripts/Core/ShipClass.cs` | enum + struct + static lookup | Defines the `ShipClass` enum (`Allrounder` / `Tank` / `Scout`), the `ShipClassStats` struct (`AlphaHealthPoints`, `MassCap`, `MovementMultiplier`), and the static `ShipClasses` lookup mapping each enum value to its stats (Allrounder 100/100/1.0, Tank 200/180/0.7, Scout 60/60/1.4). |
| `Scripts/Core/SaveManager.cs` | static class | Filesystem layer over `ConstructSave`. `SlotPath` / `Exists` / `TryLoad` / `Save` / `Delete` / `ReadAllSlotMetadata`. Atomic write via `File.Replace` with `AtomicReplaceFallback` (rename-existing-to-bak, move-temp-to-final, delete-bak; recoverable on partial failure). Saves go to `<project root>/Saves/` in Editor or under `Application.persistentDataPath/saves/` in built players. |
| `Scripts/Core/CubeStats.cs` | `MonoBehaviour` | Per-cube placeholder stats (`healthPoints`, `armourValue`, `mass`). Attached to every placeable prefab. Populated at spawn by `MaterialDefinition.ApplyTo`. Two damage-application methods: `TakeDamage(incoming)` applies the documented `effective = max(0, raw − armourValue)` formula (projectile path); `TakeRawDamage(incoming)` bypasses armour entirely (kinetic / crash path). Both clamp HP at zero and return the actual HP delta. |
| `Scripts/Core/CubeDeath.cs` | `MonoBehaviour` | Cinematic death animation. Lazily `AddComponent`'d on the rare cube whose HP hits zero — living cubes pay no idle cost. Detaches the cube from its parent, disables all colliders, drifts at ~2 u/s for 2 s along a direction biased 70% outward from a caller-supplied origin (the construct center for player cubes; random with upward bias for free-standing world cubes), then `Destroy`s the GameObject. Skips silently on alpha cubes — end-of-run owns that case. |
| `Scripts/Core/ShapeDefinition.cs` | `ScriptableObject` | One placeable shape — geometry + collider + per-face attachment validity bools (six). `ShapeCategory` is `Armour`, `Weapon`, or `Utility`. Armour shapes pull material from `MaterialRegistry`; weapon and utility shapes use a coupled `coupledMaterial` (renamed from `weaponMaterial`, kept loadable via `[FormerlySerializedAs]`). Exposes `IsLocalFaceValid`, `IsWorldFaceValid(rotation)`, `ResolveMaterial(index, registry)`, `IsWeapon`, and the helpers `IsArmour` / `UsesCoupledMaterial`. |
| `Scripts/Core/ShapeRegistry.cs` | `ScriptableObject` | Ordered array of `ShapeDefinition` indexed by `Placement.ShapeIndex`. Also provides `FindIndexByName` for the save layer's name-based resolution. |
| `Scripts/Core/MaterialDefinition.cs` | `ScriptableObject` | Visual material + (HP, AV, mass) stats. `ApplyTo(GameObject)` walks all `Renderer`s and writes the material, then writes stats into the spawned `CubeStats`. `SwatchColor` powers the toolbar's corner badges. |
| `Scripts/Core/MaterialRegistry.cs` | `ScriptableObject` | Ordered array of `MaterialDefinition` for armour shapes. `FindIndexByName` for save layer parity. Weapon-shape materials live on the shape SO, not in here. |
| `Scripts/Core/PauseMenu.cs` | DDOL singleton, `[DefaultExecutionOrder(-1000)]` | ESC pause overlay for BuildScene + FlyScene. Self-bootstraps via `RuntimeInitializeOnLoadMethod(BeforeSceneLoad)`. Four buttons (`Hangar` / `Settings` / `Menu` / `Back to Desktop`; Hangar is only visible in FlyScene); ESC closes (acts as Resume). Sets `Time.timeScale = 0` while open; restores the previous value on close. `IsOpen` and `EscConsumedThisFrame` are read by other scripts to gate gameplay input and avoid double-handling. **Settings** button calls `HideUI()` and `SettingsMenu.Instance.Show()` — IsOpen / timeScale stay owned by PauseMenu so the navigate-to drill-down restores cleanly on Settings close (its `Hide` re-calls `PauseMenu.Instance.ShowUI()`); for that reason `ShowUI` / `HideUI` are `internal`. The Update ESC handler also short-circuits when `SettingsMenu.Instance.IsOpen` or `SettingsMenu.Instance.EscConsumedThisFrame` is set, so SettingsMenu owns ESC while up. Builds the pause panel under `PersistentHud.Instance.Root` — no own canvas. |
| `Scripts/Core/GameOverMenu.cs` | DDOL singleton, `[DefaultExecutionOrder(-1000)]` | End-of-run overlay for FlyScene. Self-bootstraps via `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` so every play session has exactly one instance — no scene wiring. `TriggerGameOver` (called by `CubeDamage.ApplyAndLog` when the alpha cube dies) shows a "Construct Destroyed" panel, freezes `Time.timeScale`, and offers a single Return-to-main-menu button. Idempotent — repeated fatal hits on a 0-HP alpha don't re-trigger. Builds the panel under `PersistentHud.Instance.Root` — no own canvas. |
| `Scripts/Core/PrimitiveMeshes.cs` | static class | Lazily-built shared meshes for shapes that aren't built-in primitives: `TriangularPrism`, `SquarePyramid`, `HollowCylinder` (32-segment, smooth walls, flat ±Y annuli), `SolidCylinder` (capped cylinder shared by the reactor and the laser barrel), `Cone`. Designed to fit a 1×1×1 cell so adjacency / collider behavior matches the cube primitive. |
| `Scripts/Core/PrismMeshAuthor.cs` | `MonoBehaviour` | Assigns `PrimitiveMeshes.TriangularPrism` to the `MeshFilter` (and `MeshCollider` if present) **only when those slots are empty**, so an authored / imported mesh wired into the prefab is never overwritten. (The shipped `PlacedPrism.prefab` has a prism mesh prewired, so this component is currently a fallback / no-op there.) |
| `Scripts/Core/PyramidMeshAuthor.cs` | `MonoBehaviour` | Same pattern for `PrimitiveMeshes.SquarePyramid`. (`PlacedPyramid.prefab` likewise has its mesh prewired; this is a fallback.) |
| `Scripts/Core/CylinderMeshAuthor.cs` | `MonoBehaviour` | Same pattern for `PrimitiveMeshes.HollowCylinder`. `PlacedCylinder.prefab` ships with an empty `MeshFilter.sharedMesh`, so this component is the **primary** source of the cylinder mesh at runtime. |
| `Scripts/Core/ThrusterMeshAuthor.cs` | `MonoBehaviour` | Same pattern for `PrimitiveMeshes.Cone` — assigns the cone mesh to an empty `MeshFilter` (and `MeshCollider` if present) slot, never overwriting a prewired mesh. Mirror of the Prism / Pyramid / Cylinder mesh authors. |
| `Scripts/Core/SolidCylinderMeshAuthor.cs` | `MonoBehaviour` | Same pattern for `PrimitiveMeshes.SolidCylinder` (renamed from `ReactorMeshAuthor`, same GUID — now shared by the reactor cube and the laser barrel, the latter on a `(0.3, 1, 0.3)`-scaled child). Assigns the solid-cylinder mesh to an empty `MeshFilter` (and `MeshCollider` if present) slot, never overwriting a prewired mesh. |
| `Scripts/Core/UIManager.cs` | DDOL singleton, self-bootstraps `BeforeSceneLoad` | The corner scene-switch button inside `PersistentHud`'s shared canvas. Subscribes to `SceneManager.sceneLoaded`; flips label between `Fly!` / `Hangar`; hides its button GameObject on every scene except BuildScene (FlyScene routes scene-switch through the pause-menu Hangar button; MainMenu / HangarSelect own their full screen). Builds the button under `PersistentHud.Instance.Root` — no own canvas. |
| `Scripts/Core/PersistentHud.cs` | DDOL singleton, lazy-created | Shared screen-space-overlay canvas for every persistent UI element (`UIManager`'s corner button, `PauseMenu`'s panel, `GameOverMenu`'s panel). `Canvas` + `GraphicRaycaster` + `CanvasScaler` at `sortingOrder 200`. `Instance` getter triggers `Create()` on first access — whichever persistent UI script Awakes first pulls the canvas into existence. |
| `Scripts/Core/UIStyle.cs` | static helpers | The central code-built-UI toolkit, themed through `CscTheme`/`CscPalette` (Cosmic Scrap Club brand). Builders: `BuildScreenSpaceCanvas`, `EnsureEventSystem`, `BuildLabeledButton` (with `ButtonKind.Ghost/Primary/Danger` — ochre CTAs), `BuildLabel`, `BuildToggle`, `BuildDropdown`, plus `BuildBrandBackground` (warm procedural bg), `DecorateToolbarSlot` / `AddSelectionOutline` (glyph slots + ochre selection ring), `SplitEntryText` / `FlyoutEntryHeight` (flyout rows), `ApplyLetterSpacing`. Procedural sprite factories (uGUI overlay has no MSAA, so they feather their own AA): `MakePlateSprite`, `MakeRoundedPlate`, `MakeHazardStripe`, `BoltSprite`. Legacy `UI.Text` (no TMP); fonts/colours come from `CscTheme`, `LegacyRuntime.ttf` is the fallback. HUD scripts call `BuildLabel` directly under a shared root. |
| `Scripts/Core/CscPalette.cs` | static class | The Cosmic Scrap Club brand palette — ~50 named `static readonly Color` tokens (sRGB value/255): warm scrap/desert (sand, ochre, rust, brown), hazard yellow/stripe, worn steel, `Ink` (toon outline), cool HUD chrome (`HudPanel`/`HudCard`), and the functional HUD accents (`Boost`, `Shield`, `HeatCool`→`HeatHot`, `Critical`, `WarnFlash`, `Eject`, `PowerPositive/Negative`, `Label`, `BackgroundIdle`). |
| `Scripts/Core/CscTheme.cs` | static class | Semantic roles + helpers over `CscPalette`: fill/text/outline roles, font slots (`Display`=Anton / `Body`=Saira / `Cond`=Saira Condensed / `Stencil`=Saira Stencil, with `…Or` builtin-fallback accessors), the shared interactive `ColorBlock`, and `AddToonOutline` / `AddToonShadow` (ink `Outline`/`Shadow` for the cel look). |
| `Scripts/Core/CscThemeBootstrap.cs` | static, `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` | Loads the brand fonts from `Assets/Resources/Fonts/` into `CscTheme`'s font slots before any scene builds UI. |
| `Scripts/Core/CscSprites.cs` | static class | Cached `Resources.Load<Sprite>("UI/Sprites/…")` for the 12 brand glyph PNGs, with `ForShape(displayName, materialIndex)` mapping each shape (cube / slope / pyramid / cylinder / laser / thruster / reactor / shield) to its glyph. |
| `Scripts/Core/UIPulse.cs` | `MonoBehaviour` | Gentle sine **alpha** pulse on a `Graphic` while enabled (unscaled time; restores base alpha on disable). Drives the hangar POWER readout + the fly EJECT hint in a power deficit. |
| `Scripts/Core/LetterSpacing.cs` | `BaseMeshEffect` | Adds tracking to a legacy `Text` (which has none) by shifting each glyph's 6 mesh verts; used for the display-font tracking on titles / buttons. |
| `Scripts/Core/UIClickBounce.cs` | `MonoBehaviour` | Press-stamp feedback on a button — on pointer-down it sinks the button into its toon `Shadow` (collapses the shadow offset), restoring on pointer-up. Opt-in via `BuildLabeledButton(..., bounce: true)`. |
| `Scripts/Core/SceneSwitcher.cs` | static class | Single `Toggle()` method that flips BuildScene ↔ FlyScene. Wired by `UIManager`'s corner button. Not used by the MainMenu → HangarSelect → BuildScene path. |
| `Scripts/Core/HitContext.cs` | `readonly struct` + enums | Per-hit metadata carried from damage source to `CubeDamage.ApplyAndLog`. Holds `Target`, `Amount`, `DamageType` (`Projectile` / `Energy` / `Kinetic`), `HitFlags` (`None` / `BypassArmour`), surface `Point` + `Normal`, `OutwardOrigin` (the death-drift bias point `CubeDeath` reads), a reserved `Impulse` field for future knockback, the source-construct `Transform`, and a log tag. The `DamageType` split now drives shield outcomes: `ConstructEnergySystem.ApplyToShield` scales `Projectile ×0.9` / `Energy ×1.1` and lets `Kinetic` bypass the pool entirely. See `power_and_energy_spec.md`. |
| `Scripts/Core/FileLogHandler.cs` | `ILogHandler` | Append-only file logger. Wraps the default Unity log handler so messages still hit the Editor console. |
| `Scripts/Core/LogBootstrapper.cs` | DDOL singleton, self-bootstraps `BeforeSceneLoad` | Swaps `Debug.unityLogger.logHandler` for a `FileLogHandler` writing to `Application.persistentDataPath/Logs/CubeFly_<timestamp>.log`. Spawns its own DDOL host before any scene loads, so MainMenu / HangarSelect also write to the session log. |
| `Scripts/Core/SettingsMenu.cs` | DDOL singleton, `[DefaultExecutionOrder(-2000)]` | Tabbed Settings UI reachable from `MainMenuController.OnSettings` and the **Settings** button on `PauseMenu`. Self-bootstraps `BeforeSceneLoad`. Seven tabs (`General` / `Display` / `Graphics` / `Audio` / `Controls` / `Gameplay` / `Debug`) built procedurally via `UIStyle` under a panel parented to `PersistentHud.Instance.Root`, with the panel's own `Canvas` override set `overrideSorting = true` / `sortingOrder = 350` so the modal draws above MainMenu's scene canvas and any sibling PersistentHud UI (corner button, pause panel) while staying below `GameOverMenu` (400). ESC closes (own ESC handler at execution order -2000 runs first; `PauseMenu` checks `SettingsMenu.IsOpen` / `EscConsumedThisFrame` and short-circuits its own ESC handling that frame). `Show()` snapshots `Time.timeScale` and freezes time; `Hide()` restores it. Navigate-to drill-down: `OnSettingsClicked` on the pause panel calls `PauseMenu.HideUI()` then `SettingsMenu.Show()` (PauseMenu's `IsOpen` / `Time.timeScale` stay owned by PauseMenu); on close, `SettingsMenu.Hide` re-calls `PauseMenu.Instance.ShowUI()` so the player lands back where they were. The **Debug** tab is the only one with content so far — eight VFX toggles in a two-column column-major layout (width 500 px per column), each wired to a `VfxSettings` property and a `TooltipTrigger` describing the effect. Other tabs render a "Coming soon" placeholder. |
| `Scripts/Core/TooltipHud.cs` | lazy DDOL singleton | Floating hover-tooltip label that `TooltipTrigger` drives. Parented under `PersistentHud.Instance.Root` with its OWN `Canvas` override at `sortingOrder = 500` — above `SettingsMenu` (350), `GameOverMenu` (400), and every other persistent UI, so tooltips always render on top. `GraphicRaycaster` is disabled so the tooltip never eats clicks for the element being hovered. `Show(text, screenPos)` sets the text, activates the panel, and starts following the cursor in `Update`; `Hide()` deactivates. Long strings wrap to multiple lines at a 400 px panel-width cap (`HorizontalWrapMode.Wrap`); `UpdatePosition` calls `LayoutRebuilder.ForceRebuildLayoutImmediate` after setting the wrapped width so the subsequent `preferredHeight` read reflects the actual multi-line height, then grows the panel vertically to fit. Screen-edge clamping flips the panel to the opposite side of the cursor if it would extend past the right or bottom edge. Lazy-create: `Instance` getter spawns the singleton on first access (typically when a `TooltipTrigger.OnPointerEnter` first runs). `HasInstance` is a side-effect-free existence check used by triggers so a no-op `Hide()` doesn't accidentally spawn the hud. |
| `Scripts/Core/TooltipTrigger.cs` | `MonoBehaviour`, `IPointerEnterHandler` + `IPointerExitHandler` | Attach to any UI element with a raycast-target `Graphic` to surface a tooltip on hover. `SetText(string)` sets / updates the tooltip text (empty / null suppresses). `OnPointerEnter` calls `TooltipHud.Instance.Show` and flips a `_showingFromMe` guard; `OnPointerExit` and **`OnDisable`** both call the gated `HideIfShowing`. The `OnDisable` path covers the case where the hovered UI element is deactivated out from under the cursor (ESC closes Settings, tab switch, scene transition) — Unity doesn't synthesize a `PointerExit` in that case, so without it the tooltip would stay pinned to the mouse. The hide path uses `TooltipHud.HasInstance` so it doesn't lazy-spawn the hud just to call a no-op `Hide()` on a never-shown tooltip. |
| `Scripts/Core/VfxSettings.cs` | static class | PlayerPrefs facade for the Debug-tab VFX toggles. Eight typed bool properties — `Bloom` / `Vignette` / `Tonemapping` / `ColorAdjustments` / `ChromaticAberration` (Phase A post-FX overrides) + `EnginePlume` / `BoostFlare` / `RcsPuff` (Phase B-1 particle effects) — backed by PlayerPrefs keys prefixed `Vfx`. Default is **ON** for every key (first-launch matches the spec's "Defaults: ON" rule). Each setter writes PlayerPrefs, calls `PlayerPrefs.Save()`, and fires the static `Changed` event so subscribers (currently `VfxApplier`, `ThrusterVfx`, `RcsPuffVfx`) re-apply immediately — no Apply button, the Debug tab is a real-time A/B comparison surface. |
| `Scripts/Core/VfxApplier.cs` | DDOL singleton, `[DefaultExecutionOrder(-1500)]` | Applies `VfxSettings` to the URP volume profile affecting the active scene. Self-bootstraps `BeforeSceneLoad`. Subscribes to `SceneManager.sceneLoaded` (re-apply on scene change) and `VfxSettings.Changed` (re-apply on toggle), and runs an initial `Apply()` in `Awake`. `ResolveActiveProfile` is two-step — first a scene-attached `Volume` (FlyScene's `DesertLook` volume uses this pattern), else URP's global default via `GraphicsSettings.GetRenderPipelineSettings<URPDefaultVolumeProfileSettings>().volumeProfile` (MainMenu / HangarSelect / BuildScene have no scene `Volume` and inherit the default profile; FlyScene now carries the desert `Volume`). `Apply()` is idempotent and profile-agnostic — it `TryGet`s each of the five Phase-A overrides (`Bloom`, `Vignette`, `Tonemapping`, `ColorAdjustments`, `ChromaticAberration`) and writes only `override.active` from the corresponding `VfxSettings` property; missing overrides are silently skipped. |
| `Scripts/Core/CelLookSettings.cs` | static class | PlayerPrefs facade (`desert.celLook`, default **on**) for the FlyScene desert cel look, with an `OnChanged` event. Read + applied by `DesertLookController`; toggled from the SettingsMenu Debug tab. In Core so the UI doesn't depend on the experimental Desert namespace (mirrors `VfxSettings`, incl. `PlayerPrefs.Save()` on change). |
| `Scripts/Core/ScenePipelineOverride.cs` | `MonoBehaviour` | Makes a serialized URP pipeline asset active while its scene is loaded — `OnEnable` sets `QualitySettings.renderPipeline`, `OnDisable` restores the previous. On FlyScene's `DesertLook` GO → `Desert_RPAsset` (300 u shadows) so the desert's long shadows stay scene-local; the other scenes keep the global `PC_RPAsset` (50 u). |

---

## Scripts — Desert (`CubeFly.Desert`)

The integrated desert FlyScene level (full picture in `docs/desert_level_spec.md`).

| File | Type | Responsibility |
|---|---|---|
| `Scripts/Desert/OutlineRendererFeature.cs` | `ScriptableRendererFeature` | Screen-space depth+normal edge-detect outline on the `Desert_Renderer` (URP RenderGraph API). Black Roberts-cross ink injected before post-processing; **distance-scaled thickness** (`thicknessFalloffStart` 35 u / `minThicknessScale` 0.2) so far crowded edges don't merge into blobs. |
| `Scripts/Desert/DuneGroundGenerator.cs` (+ `Editor/`) | `MonoBehaviour` + custom inspector | Builds the procedural dune-ground mesh (layered Perlin swell/dune/ripple) and bakes it via a "Generate" button. The shipped basin uses the baked `DesertGround_500.asset` (500 u, res 600). The in-place button `CopySerialized`s onto the *bound* mesh — bake to a new path to avoid clobbering. |
| `Scripts/Desert/SurfaceSnap.cs` | `MonoBehaviour`, `[DefaultExecutionOrder(-2000)]` | Seats its object on the terrain at Awake: raycast down onto the `World` mask → `surface + clearance`, else a fallback height + warning. Used by the construct spawn (clearance 9, hover) and every `DesertTarget`/`Turret` (clearance 0.5, base on the sand). |
| `Scripts/Desert/DesertLookController.cs` | `MonoBehaviour` | On FlyScene's `DesertLook` GO. Applies `CelLookSettings` at runtime — `cameraData.SetRenderer` (`PC_Renderer` ↔ `Desert_Renderer`) + the `DesertVolumeProfile` volume weight — on `Start` and on `CelLookSettings.OnChanged`, so the Settings toggle flips the whole cel look live. |

---

## Scripts — Build (`CubeFly.Build`)

| File | Type | Responsibility |
|---|---|---|
| `Scripts/Build/BuildManager.cs` | `MonoBehaviour` | Owns the build-scene state machine: active shape index, per-shape material memory dict, active rotation, active tool (`BuildTool.Place` / `BuildTool.Delete`), spawn registry, mass-budget check. `MassLimit` is a **computed property** reading the active `ShipClass`'s `MassCap` (no serialized constant); `OnShipClassChanged` re-applies the new class's alpha HP. Subscribes to `Build.Place` / `Build.RotateZ` / `Build.RotateX`. Handles delete-tool red `MaterialPropertyBlock` hover and post-delete flood-fill. Spawns the alpha cube at scene start. Owns the autosave coroutine (0.25 s debounce on `ConstructChanged`; flushes on `OnDestroy`). Fires `CurrentShapeChanged`, `CurrentMaterialChanged(shape, material)`, `CurrentToolChanged`, `CurrentRotationChanged`, `ConstructChanged`. `ComputeCurrentNetPower(out hasPowerCubes)` sums reactor `Output` − (shield `Draw` + laser `PowerDraw`) across the placed cubes (lasers count as power cubes) so the toolbar shows a worst-case build-time power balance. `ConfigureHangarLighting` (from `Start`) flips the hangar to flat bright ambient + directional shadows-off at runtime (scene-local) so the construct is evenly lit from all sides. |
| `Scripts/Build/CubePreview.cs` | `MonoBehaviour` | Owns a runtime-spawned **composite** ghost: an outer translucent unit-cube (cell-bounds visualiser, world-axis aligned) plus an inner shape-prefab instance scaled to 0.7 (shows the actual shape being placed). The inner mesh rotates with `R/T`; the outer cube doesn't (rotating it would just flicker without changing which cell is occupied). Each `Update` raycasts through the cursor against `PlacedCube`/`AlphaCube`, nudges the hit by `hit.normal * 0.01f` before `RoundToInt`, runs `GameData.IsValidAttachment` for face-validity, shows/hides based on the result. Valid placements clear the bounds-cube tint (default green); invalid placements tint it red via `MaterialPropertyBlock`. Hidden when the active tool is not `Place`. |
| `Scripts/Build/BuildCamera.cs` | `MonoBehaviour` | Orbit camera (right-mouse drag rotates azimuth/elevation, scroll wheel zooms). Elevation clamped ±80°. |
| `Scripts/Build/BuildHud.cs` | scene-attached singleton, `[DefaultExecutionOrder(-500)]` | The BuildScene-local shared UI canvas. Lives on the `BuildHUD` GameObject in `BuildScene.unity` (same GameObject that hosts `BuildToolbarController` + `BuildShipClassController`). Awake adds `Canvas` (ScreenSpaceOverlay, `sortingOrder 100`) + `GraphicRaycaster` + `CanvasScaler` programmatically. `BuildHud.Instance.Root` is the parent every BuildScene HUD script attaches its UI tree under. |
| `Scripts/Build/BuildToolbarController.cs` | `MonoBehaviour` | Builds the branded build-scene UI overlays under `BuildHud.Instance.Root` at runtime: bottom category toolbar (brand glyph slots via `CscSprites` + `DecorateToolbarSlot` with a 3px ochre selection ring; the cube slot's glyph tracks the armed material; a red-**X** Delete slot), per-shape material flyout for armour shapes and a `CategoryFlyout` per non-armour category — weapon **and** utility (click / right-click / `M` to pin, Esc closes, auto-close 3 s after the cursor leaves; each flyout entry splits into a title + stat row via `UIStyle.SplitEntryText`), a hazard-stripe trim, top-left `ROTATE: R/T` hint, top-centre fading floating message ("Too much mass!"), and bottom-left `MASS: X / cap`, `HP: Y`, `POWER: ±N` big-value stat labels (POWER green ≥ 0 / red-pulsing < 0 via `UIPulse`, hidden when no power cubes — value from `BuildManager.ComputeCurrentNetPower`; refreshed on `ConstructChanged`). Polls digits `1`–`9` (no modifier) to arm an **armour shape** by on-screen toolbar slot order — non-armour shapes aren't reachable from the digit row — and `Shift`+digit `1`–`9` to arm the active armour shape's **material** by registry index. |
| `Scripts/Build/BuildIndicatorController.cs` | `MonoBehaviour` | Reparents a small red arrow prefab to the cube with the highest local-Z so the player can see the ship's "front". Resets the indicator's world rotation to identity each frame so the arrow stays world-aligned even when its parent cube is rotated. |
| `Scripts/Build/BuildShipClassController.cs` | `MonoBehaviour` | The middle-left "Class" picker dropdown, built under `BuildHud.Instance.Root`. Lets the player choose the construct's `ShipClass` (Allrounder / Tank / Scout); on change, calls `GameData.SetShipClass` and `BuildManager.OnShipClassChanged` so the new class's alpha HP and mass cap take effect (and the change autosaves with the slot). |
| `Scripts/Build/CategoryFlyout.cs` | plain C# class | Reusable non-armour toolbar category — one instance per non-armour `ShapeCategory` (Weapons, Utilities). Builds and owns a peek-on-hover / click-to-pin flyout listing every shape in its category. Extracted so `BuildToolbarController` drives Weapons and Utilities through identical machinery. |
| `Scripts/Build/PlacedCubeData.cs` | `MonoBehaviour` | Trivial data carrier: stores the `Vector3Int cell` of a placed shape so removal raycasts can identify which grid cell to delete. |

---

## Scripts — Fly (`CubeFly.Fly`)

| File | Type | Responsibility |
|---|---|---|
| `Scripts/Fly/FlyController.cs` | `MonoBehaviour` | On `Start`, instantiates AlphaCube + every `GameData.PlacedCubes` entry as children of the referenced `construct` Transform; resolves each placement's `ShapeDefinition` prefab, applies the `MaterialDefinition` via `ApplyTo`, collects any `WeaponBehavior` for the shooting controller, and collects spawned thruster cubes into `_spawnedThrusters` (their `ThrusterBehavior.LocalThrustAxis` values tell Boost which axes are thrustered). `BuildConstruct` also gathers the reactor / shield / laser cubes and hands them to the sibling `ConstructEnergySystem` via `RegisterCubes(reactors, shields, lasers)` in `Start`. `ResolveRigidbody` then configures the construct's `Rigidbody` (gravity off, continuous collision detection, `maxAngularVelocity` cap) and sets `rb.mass` from the summed cube masses so Unity computes the inertia tensor from the actual compound collider. Reads `Fly.Thrust` / `Pitch` / `Yaw` / `Roll` / `Boost` in `Update` (zeroed while `PauseMenu.IsOpen`); in `FixedUpdate` applies `AddForce` for thrust (clamped to `maxSpeed`), `AddRelativeTorque` for pitch/roll and world-axis `AddTorque` for yaw. Torque is scaled by `mass^rotationMassCompensation` so heavy ships aren't unturnable, and thrust/torque also take the active `ShipClass`'s movement multiplier. Owns a 0–100 **Boost** resource (Left-Ctrl `Boost` action; drains at 40/s while boosting, regens at 15/s — 6/s while overboosted; an overboost latch locks regen until the meter recovers); while boosting along a thrustered axis it grants per-axis ×1.3 thrust and a ×1.3 max-speed ceiling that decays back after release. Exposes `BoostFraction` / `IsOverboosted` / `IsBoostCritical` for the HUD, plus `CurrentAttitudeInput` (Vector3 of `_pitchInput` / `_yawInput` / `_rollInput`) for `RcsPuffVfx`. On cube death (`OnCubeDied`) it re-sums mass and calls `ConstructEnergySystem.RecomputePower()` after the disconnect cascade settles, so losing a reactor immediately re-balances power and may collapse the shield. **Phase B-1 VFX wiring**: two new `[SerializeField] GameObject` prefab references (`enginePlumePrefab`, `rcsPuffPrefab`) point at `Assets/VFX/Prefabs/` assets (wired on the FlyScene's `FlyController` GameObject). During `BuildConstruct`, each spawned thruster gets an `AddComponent<ThrusterVfx>` + `SetPlumePrefab(enginePlumePrefab)`, and the construct root gets an `AddComponent<RcsPuffVfx>` + `SetPuffPrefab(rcsPuffPrefab)` + `SetFlyController(this)`. Each `FixedUpdate` calls `DriveThrusterVfxState(boostAxes)` once — mirrors `EvaluateBoostAxes`'s `_thrustInput`-vs-`LocalThrustAxis` sign match to push `CurrentInputLevel` + `IsBoosting` onto each `ThrusterBehavior` for `ThrusterVfx` to read in `LateUpdate`. |
| `Scripts/Fly/FlyCrashHandler.cs` | `MonoBehaviour`, `[RequireComponent(typeof(Rigidbody))]` | Crash damage for the player construct. Lives on `CubeConstruct` (the Rigidbody owner) so `OnCollisionEnter` fires here for the compound collider; internal cube-to-cube contacts within the same body don't generate events, so no self-hit filter is needed. Impact speed is the normal component of `collision.relativeVelocity` (glancing blows do little, head-on hits do full). Damage is `clamp(normalImpactSpeed * 0.3, 1, 10)` above 3 u/s, packaged into a `HitContext` with `DamageType.Kinetic` + `HitFlags.BypassArmour` and routed through `CubeDamage.ApplyAndLog`. Applies to BOTH sides of the collision — the construct's contact cube AND the other collider if it carries `CubeStats` (so world target cubes take crash damage too). Replaced the pre-Rigidbody swept-BoxCast `FlyCrashDetector`. |
| `Scripts/Fly/CubeDamage.cs` | static class | Shared damage-application pipeline routed to by every damage source in Fly mode. Single entry point: `ApplyAndLog(in HitContext)` — the struct carries target, raw amount, `DamageType`, `HitFlags`, surface point/normal, source-construct transform, and log tag. Reads `HitFlags.BypassArmour` to choose `TakeRawDamage` (kinetic / crash) vs `TakeDamage` (projectile / energy with armour mitigation). Before HP, non-kinetic hits are intercepted by the construct's shared shield: it resolves the struck cube's `ConstructEnergySystem` (via `GetComponentInParent`) and calls `ApplyToShield(amount, type)`, which absorbs against the pool (type-scaled `Projectile ×0.9` / `Energy ×1.1`) and only spills the overflow through to HP; `Kinetic` skips the pool entirely. Logs the hit with source-specific wording (`AV NN` vs `armour bypassed`). On fatal hits handles the alpha-skip (calling `GameOverMenu.TriggerGameOver`) + `GameData.Remove` (for player-construct cubes carrying `PlacedCubeData`) + `AddComponent<CubeDeath>` + `BeginDeath` wiring in one place. |
| `Scripts/Fly/ProjectileHit.cs` | static class | Shared hit-detection helper for `Bullet`, `Rocket`, and `LaserWeapon`. `TrySweep` runs `Physics.RaycastNonAlloc` against a static **64-element** buffer (insertion-sorted by distance) with self-construct filtering; the projectile hit masks are `PlacedCube|AlphaCube|World`, so shots stop at the desert terrain. `ApplyAndLog` resolves the hit collider's `CubeStats` (via `GetComponentInParent`), computes the death-drift origin, builds a `HitContext` (`DamageType.Projectile` + the swept-ray point/normal), and delegates to `CubeDamage.ApplyAndLog`; a hit with no `CubeStats` is a silent no-op (World terrain has none — it only warns for a non-World object missing its stats). Takes an optional `DamageType` (default `Projectile`) so the laser routes its ticks as `Energy`. **B-2 impact VFX**: `SpawnImpactVfx(hit, scale)` instantiates the spark prefab on *any* hit and the dust prefab on upward-facing surfaces (`Dot(normal, up) > 0.7`, so bullets kick dust off the sand); spark/dust prefabs are injected once by `FlyController.Awake` via `ConfigureImpactPrefabs`, each independently Debug-toggleable. |
| `Scripts/Fly/FlyCamera.cs` | `MonoBehaviour` | One-shot dynamic offset on `Start` from `GameData.GetConstructBounds()` (clamped 5–50 units). `LateUpdate` ship-stuck follow with RMB free-look gate (`Fly.LookHeld`) and snap-back blend on release. Follow speed is **adaptive**: `followSpeed + construct.angularVelocity.magnitude * angularFollowBoost`, so the camera stays glued during fast turns instead of lagging behind. While `PauseMenu.IsOpen`, behaves as if RMB were released — frees the cursor (so menu buttons are reachable) and lets the orbit offset relax to neutral. The body follow is naturally frozen by `Time.timeScale = 0`. |
| `Scripts/Fly/FlyHud.cs` | scene-attached singleton, `[DefaultExecutionOrder(-500)]` | The FlyScene-local shared UI canvas. Lives on the `FlyHUD` GameObject in `FlyScene.unity` (same GameObject that hosts every Fly HUD script). Awake adds `Canvas` (ScreenSpaceOverlay, `sortingOrder 100`) + `GraphicRaycaster` + `CanvasScaler` programmatically. `FlyHud.Instance.Root` is the parent every Fly HUD script attaches its UI tree under. |
| `Scripts/Fly/FlyCrosshair.cs` | `MonoBehaviour`, `[DefaultExecutionOrder(100)]` | Screen-space reticle (centre dot + four arms) built under `FlyHud.Instance.Root`. Projects `construct.position + construct.forward * aimRange` to screen space each LateUpdate. Runs after `FlyCamera`'s LateUpdate so the camera transform is final by the time the projection happens. The same value is what `FlyShootingController` passes as the aim target so on-screen reticle and actual aim agree. Hidden when the projected point is behind the camera. Skipped while `PauseMenu.IsOpen` (last computed position holds). |
| `Scripts/Fly/FlyShootingController.cs` | `MonoBehaviour` | Owns the list of weapons grouped by `ShapeDefinition` (`WeaponTypeGroup` instances), the currently-selected type index, and all shoot-related input polling: Fire (LMB via `Fly.Fire`), digits 1–9 (direct select), mouse wheel (cycle, edge-detected). Each frame Fire is held, calls `TryFire(crosshairWorldTarget)` on every weapon of the selected type. Laser dispatch is **power-gated**: it reads the `ConstructEnergySystem.AvailableForWeapons` budget and only fires `floor(available / perLaserDraw)` of the lasers wanting to fire (the rest are cut), so a laser needs spare reactor power after the shield's claim. Also owns the **shared per-laser-type heat** stored on each laser's `WeaponTypeGroup` (rises 50/s while firing, cools 30/s on release, locks out at 100 and cools at 15/s until 0). `WeaponTypeGroup` gained `Heat` / `Overheated` / `IsLaser` fields and a heat-based `ReadyFraction` (`1 − heat/100` for lasers) so the toolbar bar drains as the laser heats. Fires `TypesChanged` and `SelectedChanged` events for the toolbar UI, exposes `GroupEnergyStarved(i)` (a selected laser with no spare reactor power), and fires an edge-triggered `UnpoweredFireAttempt` when the player presses fire on an unpowered laser (drives the "No Power!" flash). Skipped while `PauseMenu.IsOpen` or pointer is over UI. |
| `Scripts/Fly/FlyWeaponToolbarController.cs` | `MonoBehaviour` | Bottom-of-screen weapon toolbar UI built under a dedicated `_container` child of `FlyHud.Instance.Root` (the container lets the toolbar `SetActive(false)` when the construct has no weapons without disabling the whole HUD canvas). One **64×64 brand glyph slot** per distinct weapon type (glyph via `CscSprites.ForShape` + `DecorateToolbarSlot`, number badge, a width-based reload bar along the slot's **bottom** edge); the selected slot gets an ochre `AddSelectionOutline` ring. Subscribes to `TypesChanged` (rebuild). Per-frame it updates each reload bar's width from `WeaponTypeGroup.ReadyFraction` (routed through `FirstAliveInstance`) and paints state in the **top-right corner** (the per-slot swatch was removed): a red **✕** for a partially- or fully-dead type (fully-dead also dims the slot to 40% via a `CanvasGroup`), or a bright yellow **bolt** (`UIStyle.BoltSprite`) for an alive laser with no spare power — the two are mutually exclusive. Also owns the centre-screen **"No Power!" flash** (3× blink, matching Overboosted!/Overheated!) fired by `FlyShootingController.UnpoweredFireAttempt`. |
| `Scripts/Fly/FlySpeedIndicator.cs` | `MonoBehaviour` | Bottom-left HUD label, `SPEED <big>N</big> u/s` (Condensed font, big value), read each `Update` from the construct's `Rigidbody.linearVelocity.magnitude`. Built under `FlyHud.Instance.Root`. |
| `Scripts/Fly/FlyHpIndicator.cs` | `MonoBehaviour`, `[DefaultExecutionOrder(100)]` | Bottom-left HUD label above the speed readout, `HP <big>current</big> / initial` (big value; turns `Critical` red below 25% of the initial total). Sums `CubeStats.healthPoints` across the construct's cube children each `Update`; the initial total is snapshotted at `Start` (execution order forced after `FlyController.Start` so the cubes exist). Cubes that have died and detached fall out of the sum naturally. Built under `FlyHud.Instance.Root`. |
| `Scripts/Fly/FlyBoostBar.cs` | `MonoBehaviour` | Boost HUD bar to the left of the crosshair, built under `FlyHud.Instance.Root`. Reads `FlyController.BoostFraction` to fill the bar each frame; throbs red in the critical zone (`IsBoostCritical`) and flashes an "Overboosted!" message while `IsOverboosted`. |
| `Scripts/Fly/FlyShieldIndicator.cs` | `MonoBehaviour` | Shield + power HUD, built under `FlyHud.Instance.Root`. Bottom-left cyan **shield bar** (above the HP label; ink-framed toon outline) filled from `ConstructEnergySystem.ShieldPoints / ShieldMax` — greyed when the pool is collapsed, hidden when no shield cubes. A bottom-left `POWER: ±N` **readout** (green ≥ 0, red < 0, from `NetPower`; hidden when no power cubes). And a top-left **"EJECT: P" hint** shown only while `CanEject` — now red + pulsing (`UIPulse`, matching the hangar power-deficit cue) — with **P** invoking `ConstructEnergySystem.Eject()`. |
| `Scripts/Fly/FlyHeatBar.cs` | `MonoBehaviour` | Laser-heat HUD bar to the right of the crosshair (mirrors `FlyBoostBar`), built under `FlyHud.Instance.Root`. Fill **and** opacity track the selected laser type's `Heat / 100`, so it's invisible when cold, fades in with use, and fades out as heat regens; throbs red with an "Overheated!" flash at lockout. Shown only while a laser is the selected weapon type. |
| `Scripts/Fly/WeaponBehavior.cs` | abstract `MonoBehaviour` | Base for any weapon-cube. Owns the reload cooldown (ticking down in `Update` regardless of selection) and `TryFire(crosshairWorldTarget)` public entry point. Subclasses implement `protected abstract void Fire(Vector3 target)`. Construct and Shape references are wired by `FlyController.BuildConstruct` after instantiation, which also assigns the per-subclass muzzle-flash prefab (pyramid → `MuzzleFlashStarburst`, cylinder → `MuzzleFlashDisc`; B-2) that each `Fire` spawns when its Debug toggle is on. |
| `Scripts/Fly/ThrusterBehavior.cs` | `MonoBehaviour` | Passive descriptor on `PlacedThruster.prefab`. Carries no per-frame logic of its own — it exposes `LocalThrustAxis` (the construct-local axis the thruster boosts along, derived from its placement face) so `FlyController` knows which axes Boost should amplify. **Phase B-1** added VFX-side state: read-only `CurrentInputLevel` (float 0–1, magnitude of the player's thrust input on this thruster's axis, 0 if signs mismatch) and `IsBoosting` (bool, true while contributing to an active boost) with `internal SetInputLevel` / `SetBoosting` setters that `FlyController.DriveThrusterVfxState` calls once per `FixedUpdate`. The sibling `ThrusterVfx` reads these in `LateUpdate` to drive its plume's emission rate / lifetime / colour. No effect on flight forces or torque — purely a data hand-off for VFX. |
| `Scripts/Fly/ConstructEnergySystem.cs` | `MonoBehaviour` | One per construct, on `CubeConstruct` (sibling to `FlyController`). `RegisterCubes(reactors, shields, lasers)` takes the cubes `FlyController` collects; `RecomputePower()` (on `Start` + every cube death) re-sums the **alive** cubes into an instantaneous net rate (no battery): `totalOutput = Σ reactor Output`, `shieldDraw`/`shieldMax = Σ shield Draw`/`Contribution`. The shield is an all-or-nothing first-priority consumer (`shieldPowered = totalOutput ≥ shieldDraw`); `NetPower = totalOutput − shieldDraw` (player readout) and `AvailableForWeapons = max(0, totalOutput − (shieldPowered ? shieldDraw : 0))` is the spare budget the laser tier reads. Owns the single shared **shield pool**: `ApplyToShield(amount, type)` absorbs damage (type-scaled `Projectile ×0.9` / `Energy ×1.1`, `Kinetic` bypasses), spills overflow to HP, regens toward `ShieldMax` at 20/s starting 5 s after the last projectile/energy hit, and collapses to 0 the instant the construct goes power-negative. `CanEject` (`aliveReactorCount == 0 && (alive shields > 0 || alive lasers > 0)`) gates `Eject()`, which self-destructs every alive shield + laser (drop from `GameData`, zero HP, death-drift) and raises `CubeDied`. See `power_and_energy_spec.md`. |
| `Scripts/Fly/ReactorBehavior.cs` | `MonoBehaviour` | Passive descriptor on `PlacedReactor.prefab` (the `ThrusterBehavior` pattern). Carries no per-frame logic — exposes `Output` (the power it adds to the construct, +10 starter) for `ConstructEnergySystem` to sum. |
| `Scripts/Fly/ShieldBehavior.cs` | `MonoBehaviour` | Passive descriptor on `PlacedShield.prefab`. Exposes `Draw` (the power it claims while up, −20 starter) and `Contribution` (its share of the shared pool, +50 starter) for `ConstructEnergySystem`. |
| `Scripts/Fly/PyramidWeapon.cs` | `WeaponBehavior` subclass | Machine-gun-style. Spawn position = pyramid's apex (`transform.TransformPoint((0, 0.5, 0))`). Aim rule: if the tip direction (`transform.up`) aligns with `Construct.forward` (dot > 0.7 = cos 45°), fire at the shared crosshair world target; otherwise fire along the tip direction. 90°-stepped placements give an exact ±1 / 0 dot, so the threshold cleanly bins "frontal" vs "off-axis". `Bullet.Launch` is called with `(origin, direction, Construct, damage)` so the bullet can run self-hit prevention and snapshot the weapon's damage value. |
| `Scripts/Fly/CylinderWeapon.cs` | `WeaponBehavior` subclass | Rocket-launcher style. Spawn position = cylinder centre (`transform.position`). Launch direction = barrel open-end (`transform.up` after rotation, since `ShapeWeaponCylinder.faceNegY` is the only valid attachment face). `Rocket.Launch` is called with `(spawnPos, launchDir, exitPos, crosshairTarget, Construct, damage)` so the rocket can run self-hit prevention in both phases. |
| `Scripts/Fly/LaserWeapon.cs` | `WeaponBehavior` subclass | The first **energy** weapon and second power consumer — a continuous hitscan beam (`reloadSeconds = 0`, no projectile). Each fire raycasts from the barrel (`transform.position` along the fixed `transform.up`, **not** crosshair-tracked) via `ProjectileHit.TrySweep`, up to `range` (100), single-target; a runtime `LineRenderer` draws barrel→hit (off in `LateUpdate` on any frame it isn't dispatched). Damage is applied in fixed **ticks** (every 0.1 s, raw 6) routed as `DamageType.Energy` through `CubeDamage.ApplyAndLog`, so it excels vs shields and light/AV-0 targets and is weak vs heavy armour. Exposes `PowerDraw` (−5 while firing); heat and the power gate are owned by `FlyShootingController` (shared per laser type). |
| `Scripts/Fly/Bullet.cs` | `MonoBehaviour` | Straight-line projectile for `PyramidWeapon`. `Launch(origin, direction, firingConstruct, damage)` arms it and snapshots the firing construct + damage value. Each `Update` does a swept raycast from previous-frame position to current via `ProjectileHit.TrySweep`; on hit, routes through `ProjectileHit.ApplyAndLog` (→ `CubeDamage`) and despawns. Otherwise advances by `speed * dt`; despawns after `maxRange`. No Rigidbody / Collider on the projectile itself — the raycast does the work. A `TrailRenderer` bullet tracer (B-2) is attached at `Awake` from `tracerMaterial` when `VfxBulletTracer` is on, and detached to linger briefly on despawn. |
| `Scripts/Fly/Rocket.cs` | `MonoBehaviour` | Two-phase projectile for `CylinderWeapon`. `Launch(...)` takes spawn pos, launch dir, exit-plane pos, crosshair target, firing construct, and damage. **Exit phase**: travel along the launch direction until past the exit-plane (dot test against the captured exit world position). **Seek phase**: re-orient toward the locked crosshair target and travel straight to it. Target is captured once at `Launch`; later ship rotation has no effect. Each Update runs a `ProjectileHit.TrySweep` for the current phase's direction; on hit, applies damage and despawns. Otherwise despawns after `maxRange` in seek phase. The cylinder mesh's long axis is local +Y, so a static `MeshAlignment = Quaternion.Euler(90, 0, 0)` is right-multiplied into the `Quaternion.LookRotation(dir)` at both rotation sites (launch + seek-phase transition) so the mesh ends up aligned with flight direction instead of standing perpendicular to it. Purely cosmetic — collision is unchanged (swept raycasts). **B-2 VFX** (all Debug-toggleable, polled live in `Update`): a child exhaust-plume + smoke-puff ParticleSystem instantiated at `Awake`, a `TrailRenderer` grey smoke trail, and an impact effect spawned on hit at 1.2× scale (warhead reads bigger than a bullet puncture). |
| `Scripts/Fly/ThrusterVfx.cs` | `MonoBehaviour` | Per-thruster engine-plume driver. `AddComponent`'d onto each `PlacedThruster` cube by `FlyController.BuildConstruct`. On `Start`, instantiates `EnginePlume.prefab` as a child and orients it so the plume's local +Z emission direction (Cone shape) aligns with the thruster's local −Y exhaust direction (`Quaternion.LookRotation(Vector3.down, Vector3.forward)` in local coordinates — stays correct under construct rotation). Each `LateUpdate`, reads `ThrusterBehavior.CurrentInputLevel` + `IsBoosting` and writes the main ParticleSystem's emission rate / `startLifetime` / `startColor`. The `EnginePlume` and `BoostFlare` toggles in `VfxSettings` are independent — when `EnginePlume` is OFF but `BoostFlare` is ON and boost is engaged on this thruster, the main plume emits at the BASELINE rate (not amplified) so the shock-diamond child has a plume to sit on (the "boost cue" plume). The `ShockDiamond` child is `SetActive`-gated on the same condition. Lifetime + colour amplification only when `EnginePlume` is ON and boosting. |
| `Scripts/Fly/RcsPuffVfx.cs` | `MonoBehaviour` | Construct-level attitude-jet visualiser. `AddComponent`'d onto the construct root by `FlyController.BuildConstruct`. On `Start`, instantiates four `RcsPuff.prefab` one-shot emitters as children, one per `(±X, ±Z)` sector (indices `0 = +X+Z`, `1 = +X−Z`, `2 = −X+Z`, `3 = −X−Z`). Emitter positions are picked by a per-sector best-cube scan: for each sector direction `(sx, sz)`, pick the alive cube `c` that maximises `sx·c.x + sz·c.z` and place the emitter at THAT cube's outer corner — so emitters always sit on a real piece of the construct, even for irregular (T / cross / arrow) shapes where the bounding-box corner would land in empty space. Subscribes to `CubeDeath.CubeDied` and recomputes positions on every death so the corners track the construct's shrinking bounds. Each `Update`, reads `FlyController.CurrentAttitudeInput` (pitch / yaw / roll) and fires throttled 6-particle bursts on the corner emitters whose sector matches the commanded axis (`Mathf.Abs > 0.1` input threshold). Per-emitter cooldown 0.15 s shared across all three axes — pitching + yawing simultaneously can suppress one axis's burst. When `VfxSettings.RcsPuff == false`, `Update` early-returns and `ApplyEnabledState` `Stop()`s each emitter (`StopEmitting`) so any in-flight burst particles finish their lifetime and fade out naturally; the emitters stay active and a flip back on `Play()`s them. |

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
| `Scripts/MainMenu/MainMenuController.cs` | `MonoBehaviour` | `Awake` builds the **COSMIC SCRAP CLUB** wordmark (rotated hazard-yellow plate + the three brand fonts) over a warm procedural `UIStyle.BuildBrandBackground`, plus three toon-shadowed buttons — `Hangar` (ochre `ButtonKind.Primary` CTA) → loads `HangarSelect`; `Settings` → `SettingsMenu.Instance.Show()`; `Exit` → `Application.Quit` / stops Editor play mode. Uses `UIStyle`/`CscTheme` so its visuals match the rest of the brand UI. |

---

## Scripts — Editor (`CubeFly.EditorTools`)

| File | Type | Responsibility |
|---|---|---|
| `Scripts/Editor/RegistryValidator.cs` | static class, `[MenuItem]` | On-demand registry validation. Walks `ShapeRegistry`, `MaterialRegistry`, every shape's spawn prefab, every coupled `MaterialDefinition`, and the required gameplay layers (`PlacedCube`, `AlphaCube`, `PreviewCube`). Reports each finding to the Console with the offending asset as click-context, then surfaces a summary dialog. Menu path `Tools/CubeFly/Validate Registries`. |
| `Scripts/Editor/VfxOverridesInstaller.cs` | static class, `[MenuItem]` | Applies the Phase 1 starter tunings to `Assets/Settings/DefaultVolumeProfile.asset` via `VolumeProfile.Add<T>()` / `Override(...)`: Bloom 0.6 / 1.0 / 0.7, Vignette 0.25 / 0.4 / black, Tonemapping ACES, ColorAdjustments contrast +5 / saturation +5, ChromaticAberration 0.08. Idempotent — `Configure*` methods Add the override if missing and then ALWAYS `Override` the tuned fields, so re-running restores spec defaults if anyone drifts them in the Inspector. Menu path `Tools/CubeFly/Apply Phase A VFX overrides`. Excluded from runtime builds by the `Editor` folder convention. |
| `Scripts/Editor/VfxAssetsInstaller.cs` | static class, `[MenuItem]` | Procedurally generates the Phase B-1 **and B-2** asset set under `Assets/VFX/`: `Textures/Glow_64.png` (64×64 RGBA gaussian radial gradient written via `Texture2D.SetPixels` + `EncodeToPNG`, importer configured `alphaIsTransparency = true` / no mipmaps / bilinear / clamp / sRGB), three additive URP `Particles/Unlit` materials (`EnginePlumeMat`, `BoostShockMat`, `RcsPuffMat` — additive blend, no ZWrite), and two ParticleSystem prefabs (`EnginePlume.prefab` — main Stretch-billboard ParticleSystem with Cone shape + `ShockDiamond` child as a Billboard for the boost shock-diamond — and `RcsPuff.prefab` — single Billboard ParticleSystem set up as a manual one-shot burst, `playOnAwake = false`). **B-2** appends the weapon-VFX set: procedural starburst + tracer-stripe textures; additive muzzle (`MuzzleStarburstMat`/`MuzzleDiscMat`), tracer (`BulletTracerMat`) and rocket-exhaust materials, plus alpha-blended impact-dust + smoke-trail materials; the `MuzzleFlashStarburst`/`MuzzleFlashDisc`/`BulletImpactSpark`/`BulletImpactDust`/`RocketExhaustPlume`/`RocketSmokePuff` prefabs; and `WireBulletPrefab`/`WireRocketPrefab` steps that assign tracer/exhaust refs into the `Bullet`/`Rocket` prefabs. **Additive-blend invariant:** the "additive" particle materials must use URP `_Blend = 2` (Additive) — `_Blend = 1` is *Premultiply*, and URP's material validation silently reverts `_SrcBlend`/`_DstBlend` back to premultiply on every reimport (dulls the bloom glow + leaves the `.mat` perpetually git-dirty). Convergent — texture skipped if it exists, materials reapplied, prefabs unconditionally regenerated via `PrefabUtility.SaveAsPrefabAsset` (stable GUID, so scene references stay valid). Menu path `Tools/CubeFly/Generate VFX assets`. Excluded from runtime builds. |

---

## Input (`CubeFly.Input`)

| File | Role |
|---|---|
| `Assets/Input/CubeFlyInputActions.inputactions` | Input System asset describing the same shape as the C# wrapper. Kept for editor tooling; not the source of truth. |
| `Assets/Input/CubeFlyInputActions.cs` | Hand-rolled wrapper around `InputActionMap`, mirroring the shape of Unity's *Generate C# Class* output. **Build map**: `Place` ← LMB, `RotateZ` ← R, `RotateX` ← T. **Fly map**: `Thrust` ← 3D-vector composite (W/S forward, A/D strafe, Space/C up), `Pitch` ← ↑/↓, `Yaw` ← ←/→, `Roll` ← Q/E, `Look` ← Mouse delta, `LookHeld` ← RMB, `Fire` ← LMB, `Boost` ← Left Ctrl. ESC, `M`, digits 1–9, and mouse scroll are polled directly outside the action map. Defining bindings in code keeps compilation independent of editor wrapper-generation. |

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
| `PlacedThruster.prefab` | Thruster utility shape. Cone mesh authored at runtime by `ThrusterMeshAuthor` from `PrimitiveMeshes.Cone` + `BoxCollider` (cell-bounds) + `PlacedCubeData` + `CubeStats` + `ThrusterBehavior`. Layer `PlacedCube`. |
| `PlacedReactor.prefab` | Reactor utility shape. Solid-cylinder mesh authored at runtime by `SolidCylinderMeshAuthor` from `PrimitiveMeshes.SolidCylinder` + `BoxCollider` (cell-bounds) + `PlacedCubeData` + `CubeStats` + `ReactorBehavior`. Mounts on its −Y face. Layer `PlacedCube`. |
| `PlacedShield.prefab` | Shield utility shape. Built-in cube primitive on a `(0.5, 0.5, 0.5)`-scaled child offset to sit flush against the −Y mount face + `BoxCollider` (cell-bounds) + `PlacedCubeData` + `CubeStats` + `ShieldBehavior`. Layer `PlacedCube`. |
| `PlacedLaser.prefab` | Laser weapon shape. Thin barrel — solid-cylinder mesh authored at runtime by `SolidCylinderMeshAuthor` scaled `(0.3, 1, 0.3)` + `BoxCollider` (cell-bounds) + `PlacedCubeData` + `CubeStats` + `LaserWeapon`. Mounts on its −Y barrel face; the beam `LineRenderer` is created at runtime. Layer `PlacedCube`. |
| `PreviewCube.prefab` | Translucent unit cube only (no collider, no `PlacedCubeData`). Layer `PreviewCube`. `MaterialPropertyBlock`-friendly material: `PreviewCubeMat`. `CubePreview` instantiates it as the *bounds-ghost* half of the composite preview and additionally instantiates the active shape's prefab as the inner mesh. |
| `AlphaCubeIndicator.prefab` | Small red arrow used by `BuildIndicatorController` to flag the front of the construct. |
| `Ground.prefab` | Unity Plane primitive scaled `(20, 1, 20)` = 200×200 world units, `MeshCollider` (with `GroundPhysMat`), `GroundMat` renderer material. **Retired** — the old flat-arena ground; FlyScene now uses the desert's `DesertGround` mesh, so this is no longer instanced (kept in `Assets/Prefabs/` for reference). No `CubeStats` — it was terrain, not a damageable target. |
| `WorldTargetCube.prefab` | Cube primitive + `BoxCollider` (with `WorldTargetCubePhysMat`) + `CubeStats` (`healthPoints: 30, armourValue: 0` so it's destructible), on the `PlacedCube` layer so the projectile mask covers it. `WorldTargetCubeMat` renderer material. The **base prefab for the desert's `DesertTarget` variant** — no longer hand-placed directly; FlyScene's `DesertTargets` container holds ~21 `DesertTarget`s. |
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
| `ThrusterMat.mat` | Opaque | Thruster utility shape. |
| `ReactorMat.mat` | Opaque | Reactor utility shape. |
| `ShieldMat.mat` | Opaque | Shield utility shape. |
| `LaserMat.mat` | Opaque | Laser weapon shape. |
| `BulletMat.mat` | Opaque | Pyramid-weapon projectile. |
| `RocketMat.mat` | Opaque | Cylinder-weapon projectile. |
| `PreviewCubeMat.mat` | Transparent | Bounds-ghost half of the composite preview; tinted red via `MaterialPropertyBlock` for invalid placements. |
| `AlphaCubeIndicatorMat.mat` | Opaque | Arrow indicator. |
| `GroundMat.mat` | Opaque | Dark olive-grey `(0.20, 0.22, 0.18)` ground plane. Neutral so it doesn't compete visually with either ship cubes or world targets. |
| `WorldTargetCubeMat.mat` | Opaque | Rusty orange `(0.65, 0.35, 0.15)` for the world target dummies — visually distinct from the A/B/C/D armour palette so world cubes read as "scenery" rather than ship parts at a glance. |

### PhysicsMaterials (`Assets/PhysicMaterials/`)

Control bounce + friction for the Rigidbody-driven construct's collisions. Assigned on the relevant collider's `material` slot.

| File | Bounciness | Friction | Used by |
|---|---|---|---|
| `GroundPhysMat.physicMaterial` | 0.3 | 0.4 | `Ground.prefab`'s `MeshCollider` — arcade-light recoil when the construct hits the ground. |
| `WorldTargetCubePhysMat.physicMaterial` | 0.4 | 0.3 | `WorldTargetCube.prefab`'s `BoxCollider` — slightly punchier bounce than the ground. |

Player ship cube prefabs carry no PhysicsMaterial (Unity default — bounciness 0) so the construct doesn't bounce off its own geometry.

---

## Shapes (`Assets/Shapes/`)

| File | Role |
|---|---|
| `ShapeRegistry.asset` | The single registry instance. `BuildManager.shapeRegistry`, `FlyController.shapeRegistry`, and `HangarSelectController.shapeRegistry` reference this asset. |
| `ShapeCube.asset` | Cube shape. `category = Armour`. All six faces valid. Prefab: `PlacedCube.prefab`. |
| `ShapeSlope.asset` | Slope shape. `category = Armour`. Valid faces: bottom (-Y), back (-Z), left (-X), right (+X). Front (+Z) and top (+Y) are cut away. Prefab: `PlacedPrism.prefab`. |
| `ShapeWeaponPyramid.asset` | Pyramid weapon shape. `category = Weapon`. Only the bottom (-Y) face is valid (mounting base). Prefab: `PlacedPyramid.prefab`. Coupled material: `PyramidWeaponMatDef.asset`. |
| `ShapeWeaponCylinder.asset` | Cylinder weapon shape. `category = Weapon`. Only the bottom (-Y) face is valid. Prefab: `PlacedCylinder.prefab`. Coupled material: `CylinderWeaponMatDef.asset`. |
| `ShapeWeaponLaser.asset` | Laser weapon shape. `category = Weapon`. Only the bottom (-Y) barrel face is valid. Prefab: `PlacedLaser.prefab`. Coupled material: `LaserMatDef.asset`. |
| `ShapeUtilityThruster.asset` | Thruster utility shape. `category = Utility`. Only the bottom (-Y) face (`faceNegY`) is valid. Prefab: `PlacedThruster.prefab`. Coupled material: `ThrusterMatDef.asset`. |
| `ShapeUtilityReactor.asset` | Reactor utility shape. `category = Utility`. Only the bottom (-Y) mount face is valid. Prefab: `PlacedReactor.prefab`. Coupled material: `ReactorMatDef.asset`. |
| `ShapeUtilityShield.asset` | Shield utility shape. `category = Utility`. Only the bottom (-Y) mount face is valid. Prefab: `PlacedShield.prefab`. Coupled material: `ShieldMatDef.asset`. |

The order of shapes in `ShapeRegistry.shapes` defines `Placement.ShapeIndex` within a session. The on-disk save layer
uses `displayName` instead of the index, so reordering doesn't invalidate existing saves.

---

## Materials Defs (`Assets/Materials/Defs/`)

| File | Role |
|---|---|
| `MaterialRegistry.asset` | The single registry instance for **armour** materials. `BuildManager.materialRegistry`, `FlyController.materialRegistry`, and `HangarSelectController.materialRegistry` reference this asset. |
| `MaterialA.asset` / `B.asset` / `C.asset` / `D.asset` | Armour materials. Each pairs a URP/Lit `Material` with HP / AV / mass placeholder stats. `ApplyTo(placed)` writes both the renderer material and the stats into the spawned cube. |
| `PyramidWeaponMatDef.asset` | Coupled weapon material referenced by `ShapeWeaponPyramid.coupledMaterial`. Not in `MaterialRegistry`. |
| `CylinderWeaponMatDef.asset` | Coupled weapon material referenced by `ShapeWeaponCylinder.coupledMaterial`. Not in `MaterialRegistry`. |
| `LaserMatDef.asset` | Coupled material for the laser weapon shape, referenced by `ShapeWeaponLaser.coupledMaterial` (starter stats HP 40 / AV 0 / mass 2). Not in `MaterialRegistry`. |
| `ThrusterMatDef.asset` | Coupled material for the thruster utility shape, referenced by `ShapeUtilityThruster.coupledMaterial`. Not in `MaterialRegistry`. |
| `ReactorMatDef.asset` | Coupled material for the reactor utility shape, referenced by `ShapeUtilityReactor.coupledMaterial` (starter stats HP 60 / AV 5 / mass 10). Not in `MaterialRegistry`. |
| `ShieldMatDef.asset` | Coupled material for the shield utility shape, referenced by `ShapeUtilityShield.coupledMaterial` (starter stats HP 50 / AV 5 / mass 5). Not in `MaterialRegistry`. |

---

## UI

Every UI surface in the project is built **in code**. The HUD-consolidation
PR (#41) collapsed the legacy `UICanvas.prefab` / `UIBootstrap.prefab`
chain into three shared HUD root MonoBehaviours, each owning a single
`Canvas` + `GraphicRaycaster` + `CanvasScaler` configured programmatically:

| Root | Lifetime | sortingOrder | Hosts |
|---|---|---|---|
| `PersistentHud` | DDOL, lazy-created on first `Instance` access | 200 | Corner button (`UIManager`), pause panel (`PauseMenu`), Construct Destroyed panel (`GameOverMenu`, own canvas override 400), Settings panel (`SettingsMenu`, own canvas override 350), and the floating tooltip panel (`TooltipHud`, own canvas override 500). |
| `FlyHud` | Scene-attached on `FlyHUD` GameObject (FlyScene), `[DefaultExecutionOrder(-500)]` | 100 | Crosshair, 64×64 weapon toolbar, big-value SPEED / HP labels, ink-framed boost / shield / heat bars, `POWER:` readout + red-pulsing "EJECT: P" hint (`FlyShieldIndicator`). |
| `BuildHud` | Scene-attached on `BuildHUD` GameObject (BuildScene), `[DefaultExecutionOrder(-500)]` | 100 | Branded build toolbar (glyph slots + flyouts + red-X Delete + `MASS:` / `HP:` / `POWER:` big-value readouts), ship-class dropdown. |

`MainMenu` and `HangarSelect` each own their own full-screen canvas
(built in their controller's `Awake` via `UIStyle.BuildScreenSpaceCanvas`)
— they're not fragmented across multiple HUD elements, so the
shared-root pattern would be churn there.

### Brand look — Cosmic Scrap Club

All four surfaces are styled to the **Cosmic Scrap Club** identity (Milestone B),
a code-built theme layered over `UIStyle`:

- **Palette + theme.** `CscPalette` (~50 brand colour tokens) + `CscTheme`
  (semantic roles, font slots, `AddToonOutline` / `AddToonShadow`). The functional
  HUD accents (`Boost` / `Shield` / `Heat` / `Critical` / `Power…`) are the exact
  literals the Fly HUD used before, so adopting the palette was a visual no-op there;
  the new scrap / desert + hazard + steel + `Ink` tokens drive the restyle.
- **Fonts.** Anton (titles / big numbers / wordmark), Saira Condensed Bold (buttons /
  labels / tabs — uppercased with `LetterSpacing` tracking), Saira Stencil (warnings),
  Saira SemiBold (body / readouts) — loaded from `Assets/Resources/Fonts/` by
  `CscThemeBootstrap`, `LegacyRuntime.ttf` fallback.
- **Cel look on the UI.** Black `Ink` toon outlines + hard drop-shadows on plates and
  buttons; a press-stamp (`UIClickBounce`) sinks a button into its shadow; ochre
  `ButtonKind.Primary` CTAs; procedural feathered sprites
  (`MakePlateSprite` / `MakeRoundedPlate` / `MakeHazardStripe` / `BoltSprite`) since the
  uGUI overlay has no MSAA.
- **Two worlds in tension.** Menus / hangar = warm dusty scrap
  (`BuildBrandBackground` rust-and-sand gradient, ochre CTAs, hazard-stripe trim, the
  COSMIC SCRAP CLUB wordmark); the flight HUD = a cool sharp esports overlay
  (translucent-navy `HudPanel` meter frames + ink borders + energy fills). No white
  background, no emoji — brand glyphs (`CscSprites`) + the procedural bolt.

### Settings UI

`SettingsMenu` is a DDOL-singleton seven-tab procedural modal
(General / Display / Graphics / Audio / Controls / Gameplay / Debug)
parented under `PersistentHud.Instance.Root` with the panel's own
`Canvas` override at `sortingOrder = 350` — above MainMenu's scene
canvas (200) and any PersistentHud sibling (corner button, pause
panel), below `GameOverMenu`'s 400 and `TooltipHud`'s 500. Reachable
from `MainMenuController.OnSettings` and the new **Settings** button
on `PauseMenu`. The navigate-to drill-down with `PauseMenu` keeps
ownership clean — `PauseMenu`'s `ShowUI` / `HideUI` are `internal`
so `SettingsMenu.OnSettingsClicked` and `SettingsMenu.Hide` can
toggle the pause panel's visibility without ever touching
`PauseMenu.IsOpen` or `Time.timeScale`. ESC routing is owned by
`SettingsMenu` while open via execution order `-2000` (below
`PauseMenu`'s `-1000`); `PauseMenu` checks
`SettingsMenu.EscConsumedThisFrame` and short-circuits.

The **Debug** tab is the only tab with content so far: eight VFX
toggles in a two-column column-major layout, each wired to a
`VfxSettings` property and carrying a `TooltipTrigger` that
describes the effect. The other six tabs render a "Coming soon"
placeholder.

`TooltipHud` + `TooltipTrigger` form the reusable hover-tooltip
pair the Debug tab consumes. `TooltipHud` is a **separate**
lazy-created DDOL singleton with its own canvas at
`sortingOrder = 500` (tooltips on top of everything). Long
strings wrap at a 400 px panel-width cap;
`LayoutRebuilder.ForceRebuildLayoutImmediate` is called after
setting the wrapped width so the subsequent `preferredHeight`
read reflects accurate multi-line height. Screen-edge clamping
flips the panel to the opposite side of the cursor if it would
run off-screen. `TooltipTrigger` is the
`IPointerEnter/Exit + OnDisable` glue — the `OnDisable` path
hides the tooltip when its host UI is deactivated mid-hover (ESC
closes Settings, tab switch, scene transition), avoiding
"tooltip pinned to cursor forever" when Unity doesn't synthesize
a `PointerExit`.

---

## Project Settings

| File | Edits beyond Unity defaults |
|---|---|
| `ProjectSettings/EditorBuildSettings.asset` | `MainMenu` index 0, `HangarSelect` index 1, `BuildScene` index 2, `FlyScene` index 3. |
| `ProjectSettings/TagManager.asset` | Added tag `AlphaCube`; added user layers `PlacedCube` (6), `AlphaCube` (7), `PreviewCube` (8), `World` (9 — desert terrain). |
| `ProjectSettings/ProjectSettings.asset` | `activeInputHandler: 1` (New Input System only). |
| `Packages/manifest.json` | URP 17.3, Input System ≥ 1.19, uGUI/TextMeshPro — all bundled with the Universal 3D template. The MCP-for-Unity package is pinned to its upstream git URL; the embedded checkout under `Packages/com.coplaydev.unity-mcp/` is git-ignored so the upstream source is re-fetched on clone. |

---

## Lifecycle Walkthroughs

**Cold start.**
`UIManager.Bootstrap`, `PauseMenu.Bootstrap`, `GameOverMenu.Bootstrap`,
`SettingsMenu.Bootstrap`, `VfxApplier.Bootstrap`, and
`LogBootstrapper.Bootstrap` (all `BeforeSceneLoad`) each spawn
their DDOL singleton before any scene loads. The first of those Awakes
calls `PersistentHud.Instance`, which lazy-creates the shared persistent
canvas. `VfxApplier.Awake` runs an initial `Apply()` against URP's
default volume profile so the active VFX toggles are honoured on the
very first frame. The file logger comes up immediately — MainMenu and
HangarSelect both write to the session log file. `MainMenu.unity`
loads. `MainMenuController.Awake` calls `UIStyle.EnsureEventSystem` and
builds the title + three buttons in its own scene canvas.
`UIManager.OnSceneLoaded` runs and hides the corner button GameObject
(MainMenu isn't in the allowlist). `TooltipHud` does NOT spawn yet —
it's lazy-created on the first `TooltipHud.Instance` access, which
happens when the user opens Settings → Debug and hovers a toggle.

**Main Menu → Hangar Slot Selector.**
**Hangar** click → `SceneManager.LoadScene("HangarSelect")`. Build
settings index 1 loads. `HangarSelectController.Awake` builds the
slot UI; `RefreshAllCards` calls
`SaveManager.ReadAllSlotMetadata()` and renders each card.

**HangarSelect → Hangar.**
Slot click (or digit 1/2/3) → `GameData.SetActiveSlot(i)`, then
`GameData.Clear()` (empty slot) or `GameData.LoadFromSave(...)`
(filled). `SceneManager.LoadScene("BuildScene")`. The scene's
`BuildHUD` GameObject Awakes first (`[DefaultExecutionOrder(-500)]`):
`BuildHud.Awake` adds Canvas / GraphicRaycaster / CanvasScaler at
sortingOrder 100. `UIManager.OnSceneLoaded` shows the corner button
GameObject under `PersistentHud` and sets its label to `Fly!`.
`BuildToolbarController.Start` and `BuildShipClassController.Start`
build their UI trees under `BuildHud.Instance.Root`. `BuildManager.Awake`
constructs the `CubeFlyInputActions` wrapper and subscribes to
`Place` / `RotateZ` / `RotateX`. `BuildManager.Start` resolves
camera/preview/cubeRoot references, spawns the alpha cube,
re-instantiates any cubes already in `GameData.PlacedCubes`, then
subscribes `ScheduleAutosave` to `ConstructChanged` so subsequent
edits are persisted.

**Build → Fly.**
Corner-button click → `SceneSwitcher.Toggle()`. BuildScene unloads;
its `OnDestroy` flushes any pending autosave so the slot file is
current. The `BuildHud` singleton is destroyed with its scene; the
DDOL singletons (`GameData` static; `UIManager` / `PauseMenu` /
`GameOverMenu` / `SettingsMenu` / `VfxApplier` / `LogBootstrapper`
DDOL; `PersistentHud` DDOL; `TooltipHud` DDOL if previously
lazy-spawned) all survive. `VfxApplier.OnSceneLoaded` re-applies
the active VFX toggles to whichever volume profile the new scene
resolves to. FlyScene loads; the `FlyHUD` GameObject Awakes first
(`[DefaultExecutionOrder(-500)]`) and creates the `FlyHud` canvas at
sortingOrder 100. `FlyController.Start` rebuilds the construct under
the `CubeConstruct` transform, applies each placement's chosen
`MaterialDefinition`, collects spawned `WeaponBehavior` instances
(including any `LaserWeapon`) plus the reactor / shield / laser cubes,
configures the construct `Rigidbody` and sets its mass from the summed
cube masses, hands the weapons list to
`FlyShootingController.RegisterWeapons` (which groups by
`ShapeDefinition` and fires `TypesChanged`), and registers the power
cubes with the sibling `ConstructEnergySystem` via `RegisterCubes`
(which runs its first `RecomputePower`). `BuildConstruct` also
`AddComponent`'s a `ThrusterVfx` (with `enginePlumePrefab` wired) on
every spawned thruster cube and a single `RcsPuffVfx` (with
`rcsPuffPrefab` + back-pointer to FlyController) on the construct
root. The Fly HUD scripts (`FlyCrosshair`, `FlyWeaponToolbarController`,
`FlySpeedIndicator`, `FlyHpIndicator`, `FlyBoostBar`,
`FlyShieldIndicator`, `FlyHeatBar`) each build their UI under
`FlyHud.Instance.Root`. `FlyCamera.Start` computes its one-shot offset
from `GameData.GetConstructBounds()`. `UIManager.OnSceneLoaded` hides
the corner button (FlyScene is not in its allowlist; scene-switching is
routed through the pause menu's Hangar button instead).

**Pause.**
ESC pressed in BuildScene or FlyScene → `PauseMenu.Open` sets
`Time.timeScale = 0`, shows the dim panel + "Paused" title +
Hangar (FlyScene only) / Settings / Menu / Back to Desktop buttons.
`BuildManager.Update` / `FlyController.Update`
/ `FlyShootingController.Update` see `PauseMenu.IsOpen` and
short-circuit. `FlyCamera.Update` treats RMB as released, freeing the
cursor for menu clicks. ESC again → `PauseMenu.Close` restores the
saved `timeScale`. **Settings** → `PauseMenu.HideUI()` then
`SettingsMenu.Show()` (PauseMenu's IsOpen / timeScale stay set);
ESC in Settings closes it and SettingsMenu's `Hide` re-calls
`PauseMenu.Instance.ShowUI()` to restore the pause panel.
**Menu** → set `timeScale = 1`, load MainMenu.
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
2. **Construct flight is Rigidbody-driven.** A non-kinematic
   `Rigidbody` on `CubeConstruct` + cube `BoxCollider`s form a
   compound body. `FlyController.FixedUpdate` applies `AddForce`
   (thrust, clamped to `maxSpeed`) and `AddTorque` (pitch/roll local,
   yaw world). `Rigidbody.linearDamping` / `angularDamping` provide
   the decay; `angularDamping` is set substantial so rotation comes
   to rest rather than drifting forever.
3. **Mass affects flight via real physics**, not a manual multiplier.
   F=ma makes heavier ships accelerate slower; τ=Iα (the inertia
   tensor Unity computes from the compound collider) makes them turn
   slower. Two knobs balance the rotation curve: `rotationMassCompensation`
   scales applied torque by `mass^p` so heavy ships aren't unturnable,
   and `maxAngularSpeed` caps angular velocity so light ships (tiny
   inertia tensor) don't spin out.
4. **Yaw is applied in world space** (`AddTorque(Vector3.up, …)`)
   to keep "left/right" intuitive when the ship is pitched. Pitch and
   roll use `AddRelativeTorque` (local-space).
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
   but the load path resolves via the shape's `coupledMaterial` (the
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
