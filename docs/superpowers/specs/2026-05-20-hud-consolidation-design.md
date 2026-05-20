# HUD / Canvas Consolidation (F8) — Design Spec

**Status:** Approved design, ready for implementation planning
**Date:** 2026-05-20
**Branch:** `feat/hud-consolidation` off `main`

## Overview

Phase 1 / item 2 of the Up Next timeline (`../../ROADMAP.md` → *Up Next*).
Collapses the project's 10+ self-bootstrapped runtime canvases into three
shared HUD roots — one per scene plus one global — so that adding new HUD
elements (shield bar, laser heat bar, etc. in phase 2 Power & Energy) is
"build your UI under `FlyHud.Instance.Root`" instead of authoring another
free-standing canvas with bespoke sorting / scaling / EventSystem setup.

Behaviour is preserved: every existing HUD element looks and behaves the
same as before. The visible difference is internal — fewer Unity canvases,
fewer GraphicRaycasters, one place to reason about sorting and scaling.

## Background — current systems

Today's runtime canvases (each built in code by its owning script):

| Owner | Canvas name | Sort | Lifetime |
|---|---|---|---|
| `UIManager` | `UICanvas` (prefab) | 100 | DDOL — instantiated via `UIBootstrap` per scene |
| `PauseMenu` | `PauseMenuCanvas` | 300 | DDOL — `[RuntimeInitializeOnLoadMethod]` self-bootstrap |
| `GameOverMenu` | `GameOverMenuCanvas` | 400 | DDOL — same self-bootstrap pattern |
| `BuildToolbarController` | `BuildToolbarCanvas` | 90 | BuildScene-local |
| `BuildShipClassController` | `BuildShipClassCanvas` | 95 | BuildScene-local |
| `FlyCrosshair` | `FlyCrosshairCanvas` | 110 | FlyScene-local |
| `FlyBoostBar` | `FlyBoostBarCanvas` | 115 | FlyScene-local |
| `FlyWeaponToolbarController` | `FlyWeaponToolbarCanvas` | 120 | FlyScene-local |
| `FlySpeedIndicator` | `FlySpeedCanvas` | 130 | FlyScene-local |
| `FlyHpIndicator` | `FlyHpCanvas` | 130 | FlyScene-local |

Plus `UIBootstrap` (one instance per gameplay scene) which exists solely to
instantiate the `UIManager` prefab if it isn't already present.

Every HUD script ends up duplicating the same boilerplate:

1. Call `UIStyle.EnsureEventSystem()`.
2. Call `UIStyle.BuildScreenSpaceCanvas("...", sortingOrder: N)`.
3. Pick a sortingOrder coordinated by reading the other files (the magic
   numbers in the table above).
4. Build the UI tree as children of the new canvas.

The audit (F8 / arch-rec 6) flagged the multiplication of canvases as
project drift: more draw batches than needed, redundant GraphicRaycasters,
and scattered sortingOrder bookkeeping that grows linearly with every new
HUD element.

## Approach — three HUD root MonoBehaviours

Three new components, each owning a single `Canvas` + `GraphicRaycaster` +
`CanvasScaler`. Every existing HUD script attaches its UI as a child of
the relevant root via `FooHud.Instance.Root` and stops creating its own
canvas. Sorting reduces to where you live: persistent above scene above
nothing. `UIBootstrap` and the `UIManager` prefab become redundant and are
deleted.

Rejected alternatives:

- **Shared `UIStyle.GetOrCreateSharedCanvas(name, sortingOrder)` helper.**
  Smallest diff, but no first-class "FlyHud" entity to find / inspect —
  sorting and scaling end up in an implicit hash table of canvases keyed
  by string. Less discoverable, harder to extend as the HUD grows.
- **Mixed: `PersistentHud` as a component, scene HUDs via helper.** Two
  patterns to learn for the same problem. Less cohesive.

## Design

### Components

**`Assets/Scripts/Core/PersistentHud.cs`** — DDOL singleton. Lazy-created
via the `Instance` getter:

```csharp
public static PersistentHud Instance => _instance != null ? _instance : Create();
```

`Create()` spawns a DDOL `GameObject` named `PersistentHud`, adds `Canvas`
+ `GraphicRaycaster` + `CanvasScaler`, configures them programmatically
(`renderMode = ScreenSpaceOverlay`, `sortingOrder = 200`,
`ScaleWithScreenSize @ 1920×1080 / MatchWidthOrHeight 0.5`), calls
`UIStyle.EnsureEventSystem()`, and exposes
`public RectTransform Root => (RectTransform)transform;`.

The other persistent UI scripts (`UIManager`, `PauseMenu`,
`GameOverMenu`) keep their existing `[RuntimeInitializeOnLoadMethod
(BeforeSceneLoad)]` Bootstraps and their per-script DDOL singletons, but
their Awake builds UI as children of `PersistentHud.Instance.Root`
instead of creating their own canvas. The first one to Awake triggers
PersistentHud's lazy creation; the others reuse it.

Sibling order inside the persistent canvas (later siblings render on
top): corner button → pause panel → game-over panel. When PauseMenu's
panel is active, its full-screen dim Image renders above the corner
button (covering it) and above the scene HUD below (because PersistentHud
sortingOrder 200 > FlyHud / BuildHud sortingOrder 100). GameOverMenu's
panel sits above both. Click absorption via dim-Image `raycastTarget`
remains unchanged.

**`Assets/Scripts/Fly/FlyHud.cs`** — scene-attached MonoBehaviour on a
new `FlyHUD` GameObject in `FlyScene.unity`. Class is decorated
`[DefaultExecutionOrder(-500)]` so it Awakes before the other Fly HUD
scripts (which run at default order 0):

```csharp
[DefaultExecutionOrder(-500)]
public class FlyHud : MonoBehaviour
{
    public static FlyHud Instance { get; private set; }
    public RectTransform Root => (RectTransform)transform;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        // Configure / add Canvas (overlay, sortingOrder 100),
        // GraphicRaycaster, CanvasScaler programmatically so the scene
        // asset only needs an empty GameObject with FlyHud attached.
        UIStyle.EnsureEventSystem(); // idempotent — first wins
    }
    void OnDestroy() { if (Instance == this) Instance = null; }
}
```

Scene-lifetime (NOT DDOL — destroyed on scene unload, recreated on next
FlyScene load). Same singleton pattern as today's `FlyController`,
`BuildManager`, etc.

**`Assets/Scripts/Build/BuildHud.cs`** — exact mirror of `FlyHud` for
`BuildScene`. Same sortingOrder (100, since the two scenes never overlap).
Same `[DefaultExecutionOrder(-500)]`.

### Per-script refactor

Each existing HUD script drops its canvas-creation and attaches to the
relevant shared root. The pattern is identical everywhere:

- Delete the `_canvas = UIStyle.BuildScreenSpaceCanvas(...)` line and the
  `Canvas _canvas;` field.
- Delete the `UIStyle.EnsureEventSystem()` call (the HUD root handles
  it once).
- Replace the parent passed into `UIStyle.BuildLabel` /
  `UIStyle.BuildLabeledButton` / `BuildDropdown` etc. — wherever the
  script previously passed `(RectTransform)_canvas.transform` (or its
  alias `canvasRoot`), pass the relevant `FooHud.Instance.Root`.

Per file:

| File | New parent root |
|---|---|
| `Assets/Scripts/Fly/FlyCrosshair.cs` | `FlyHud.Instance.Root` |
| `Assets/Scripts/Fly/FlySpeedIndicator.cs` | `FlyHud.Instance.Root` |
| `Assets/Scripts/Fly/FlyHpIndicator.cs` | `FlyHud.Instance.Root` |
| `Assets/Scripts/Fly/FlyBoostBar.cs` | `FlyHud.Instance.Root` |
| `Assets/Scripts/Fly/FlyWeaponToolbarController.cs` | `FlyHud.Instance.Root` |
| `Assets/Scripts/Build/BuildToolbarController.cs` | `BuildHud.Instance.Root` |
| `Assets/Scripts/Build/BuildShipClassController.cs` | `BuildHud.Instance.Root` |
| `Assets/Scripts/Core/UIManager.cs` | `PersistentHud.Instance.Root` |
| `Assets/Scripts/Core/PauseMenu.cs` | `PersistentHud.Instance.Root` |
| `Assets/Scripts/Core/GameOverMenu.cs` | `PersistentHud.Instance.Root` |

**`UIManager` extras.** Drop the `[SerializeField] Button sceneSwitchButton;`
+ `[SerializeField] Text buttonLabel;` prefab-wiring fields and the
`if (sceneSwitchButton == null || buttonLabel == null) BuildButton();`
branch — always build in code (today's fallback is the only sensible
path). The line `_canvas.enabled = inGameplay;` (which currently hides
the corner UI on MainMenu / HangarSelect) is replaced by
`sceneSwitchButton.gameObject.SetActive(scene.name == BuildSceneName);` —
the same condition the UX-batch PR (#40) introduced for the FlyScene hide,
naturally extending to hide the button on MainMenu and HangarSelect too.
The PersistentHud canvas itself stays enabled (since PauseMenu and
GameOverMenu's panels also live in it).

**`PauseMenu` / `GameOverMenu` extras.** Drop the
`_root.transform.SetParent(transform, worldPositionStays: false)`
reparenting trick (a workaround needed because each used to create its
own canvas as a free-standing GameObject and needed to manually DDOL it).
After this change, the panels are children of `PersistentHud`'s canvas,
which is itself DDOL — they inherit it for free. Both `BuildUI` methods
now build the panel `GameObject` directly under `PersistentHud.Instance.Root`.

### Scene + asset cleanup

- **`Assets/Scenes/FlyScene.unity`** — add a new GameObject named `FlyHUD`
  with `FlyHud` attached. Remove the existing `UIBootstrap` GameObject.
- **`Assets/Scenes/BuildScene.unity`** — add a `BuildHUD` GameObject with
  `BuildHud`. Remove the existing `UIBootstrap` GameObject.
- **Delete `Assets/Scripts/Core/UIBootstrap.cs`** + its `.cs.meta`.
  PersistentHud's self-bootstrap chain replaces it.
- **Delete the `UIManager` / `UIBootstrap` prefab** under `Assets/UI/`
  (project ships a `UICanvas` prefab referenced from `UIBootstrap`).
  Always-build-in-code makes it unreferenced.

### Sorting / scaling / EventSystem summary

| Canvas | sortingOrder | renderMode | Scaler |
|---|---|---|---|
| `FlyHud` | 100 | ScreenSpaceOverlay | ScaleWithScreenSize @ 1920×1080, match 0.5 |
| `BuildHud` | 100 | ScreenSpaceOverlay | same |
| `PersistentHud` | 200 | ScreenSpaceOverlay | same |

Within each canvas, sibling order determines depth. EventSystem is
created once via `UIStyle.EnsureEventSystem()` from whichever HUD root
Awakes first (idempotent on re-call).

## Files touched

**Create:**
- `Assets/Scripts/Core/PersistentHud.cs` (+ `.cs.meta`)
- `Assets/Scripts/Fly/FlyHud.cs` (+ `.cs.meta`)
- `Assets/Scripts/Build/BuildHud.cs` (+ `.cs.meta`)

**Modify (scripts):**
- `Assets/Scripts/Core/UIManager.cs`
- `Assets/Scripts/Core/PauseMenu.cs`
- `Assets/Scripts/Core/GameOverMenu.cs`
- `Assets/Scripts/Fly/FlyCrosshair.cs`
- `Assets/Scripts/Fly/FlySpeedIndicator.cs`
- `Assets/Scripts/Fly/FlyHpIndicator.cs`
- `Assets/Scripts/Fly/FlyBoostBar.cs`
- `Assets/Scripts/Fly/FlyWeaponToolbarController.cs`
- `Assets/Scripts/Build/BuildToolbarController.cs`
- `Assets/Scripts/Build/BuildShipClassController.cs`

**Modify (scenes):**
- `Assets/Scenes/FlyScene.unity` — add `FlyHUD` GameObject, remove `UIBootstrap`
- `Assets/Scenes/BuildScene.unity` — add `BuildHUD` GameObject, remove `UIBootstrap`

**Delete:**
- `Assets/Scripts/Core/UIBootstrap.cs` (+ `.cs.meta`)
- `Assets/UI/UICanvas.prefab` (+ `.prefab.meta`) — the persistent UIManager prefab
- Any other UIBootstrap-related prefab under `Assets/UI/` if present

## Delivery

- **Branch:** `feat/hud-consolidation` off `main`.
- **Commits**, in dependency order so each compiles + is independently
  reviewable:
  1. **`PersistentHud` + persistent-UI refactor.** Add
     `Assets/Scripts/Core/PersistentHud.cs`. Refactor `UIManager`,
     `PauseMenu`, `GameOverMenu` to attach to `PersistentHud.Instance.Root`.
     Delete `UIBootstrap.cs` + the `UIManager` prefab. Edit both scene
     assets to drop the `UIBootstrap` GameObject.
  2. **`FlyHud` + Fly HUD refactor.** Add `Assets/Scripts/Fly/FlyHud.cs`.
     Refactor the 5 Fly HUD scripts. Add the `FlyHUD` GameObject to
     `FlyScene.unity`.
  3. **`BuildHud` + Build HUD refactor.** Add
     `Assets/Scripts/Build/BuildHud.cs`. Refactor the 2 Build HUD
     scripts. Add the `BuildHUD` GameObject to `BuildScene.unity`.
- **One PR** for all three commits.

## Verification

No automated test framework (deferred with F5) — verification is the
Unity compile-check (`refresh_unity` + `read_console` filtered to
`Assets/Scripts`) **after each commit** plus a manual play-test by the
user after the branch is complete:

- All persistent UI works: the corner **Fly!** button in BuildScene; ESC
  opens the pause menu in either gameplay scene; Construct Destroyed
  overlay appears when the alpha cube reaches 0 HP; the Hangar button
  inside the pause menu still routes back to BuildScene from FlyScene.
- All FlyScene HUD elements visible and behaving: crosshair tracks the
  construct's forward direction; boost bar fills / throbs / shows the
  Overboosted! flash on overboost entry; weapon toolbar shows reload
  bars (the width-based fill from the UX batch) and partial / full death
  marks; speed and HP labels populate the bottom-left corner.
- All BuildScene HUD elements visible and behaving: build toolbar
  buttons + material flyouts (with the 3 s away-timer and right-side
  swatches from the UX batch); class dropdown switches ship class; stat
  labels update.
- Switch scenes a few times (MainMenu → HangarSelect → BuildScene →
  FlyScene → Hangar back → … ). The persistent UI survives every
  transition (PersistentHud is DDOL); scene HUDs rebuild on each
  scene load.
- Compile clean per commit, no `Assets/Scripts` errors / warnings.

## Out of scope

- **MainMenu and HangarSelect canvases.** Each is already a single full-
  screen canvas — no fragmentation. Refactoring them into the persistent
  HUD pattern would be churn for no benefit.
- **Theming / styling unification.** F8 is about the canvas plumbing, not
  about visual consistency. Future work can lift colour / font / sizing
  decisions out of individual scripts and into `UIStyle` if needed.
- **Going prefab-driven.** `UICanvas.prefab` is deleted; the project's
  established pattern (build UI in code) stays.
- **Removing the `[DefaultExecutionOrder(-1000)]` on PauseMenu /
  GameOverMenu.** Their existing early-Awake order is still useful for
  the `EscConsumedThisFrame` chain — leave alone.
