# Settings Menu — Design Spec

**Status:** Approved design, ready for implementation planning
**Date:** 2026-05-24
**Branch:** `feat/settings-menu` off `main`
**ROADMAP item:** Up Next #2 (Settings menu)

## Overview

A tabbed Settings UI reachable from **both** the Main Menu's `Settings`
button (currently a no-op log line) and from a new `Settings` button in
the ESC pause overlay (`PauseMenu`). Same UI from two entry points.

Six tabs are scaffolded as **placeholders** — empty panels with a
"Coming soon" label. Real controls fill in tab by tab in later PRs as
each becomes relevant:

- **General** · **Display** · **Graphics** · **Audio** · **Controls** ·
  **Gameplay**

A seventh **Debug** tab is added during the VFX pass (Up Next #1, out of
scope here) to surface per-effect toggles for every item in
`docs/vfx_pass_ideas.md`.

The scaffolding is the deliverable. There is no persistence layer in
this PR — there's nothing to persist. The first real setting (Debug-tab
VFX toggles, or any real control later) brings the persistence design
with it, with a concrete consumer in hand to shape the API.

## Background — current systems

The project has a strongly established **DDOL self-bootstrapping
singleton** pattern for persistent UI. Four scripts already follow it:

- `UIManager` — corner Fly!/Hangar button.
- `PauseMenu` — ESC overlay in BuildScene / FlyScene; `Time.timeScale = 0`
  while open; two buttons (Menu / Back to Desktop) plus a context-aware
  Hangar button in FlyScene.
- `GameOverMenu` — "Construct Destroyed" overlay; idempotent
  `TriggerGameOver()`.
- `LogBootstrapper` — file-log handler setup.

Each:

1. Bootstraps in a `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`
   hook that creates the GameObject + adds the component.
2. Holds a `static Instance` reference, `DontDestroyOnLoad`s itself,
   builds its UI in `Awake`, hides it.
3. Parents its UI under `PersistentHud.Instance.Root` — a lazy-created
   shared `RenderMode.ScreenSpaceOverlay` canvas at `sortingOrder 200`.
   Sibling order in that canvas determines layering (later-added covers
   earlier siblings).
4. Uses procedural `UIStyle.BuildLabel` / `UIStyle.BuildLabeledButton`
   helpers with legacy `UnityEngine.UI.Text` + `LegacyRuntime.ttf`. No
   prefabs, no TMP.
5. Marks itself `[DefaultExecutionOrder(-1000)]` when it polls ESC, so
   its `Update` runs before scene scripts; exposes an
   `EscConsumedThisFrame` flag for cross-script coordination.

The Settings menu adopts this pattern unchanged in shape, with one
necessary deviation: it gives its panel a per-panel `Canvas` override
(`overrideSorting=true, sortingOrder=350`) so it draws reliably above
the MainMenu scene's own canvas (also at 200) without bumping
PersistentHud's base order.

`MainMenuController.OnSettings()` today logs
`"Settings selected — not implemented yet."` and does nothing else.

`PauseMenu.BuildUI()` today creates three buttons (Hangar / Menu /
Back to Desktop) stacked at `anchoredPosition` Y = +100 / 0 / -100,
each 360 × 80 px.

## Architecture

### DDOL singleton

`SettingsMenu : MonoBehaviour` in `CubeFly.Core`. Marked
`[DefaultExecutionOrder(-2000)]` so its `Update` runs **before**
`PauseMenu` (`-1000`) — guarantees Settings owns ESC when open.

Self-bootstraps via
`[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]`:

```
static void Bootstrap()
{
    if (Instance != null) return;
    GameObject go = new GameObject("SettingsMenu");
    go.AddComponent<SettingsMenu>();
}
```

One instance per play session. No scene wiring. Identical bootstrap
shape to `PauseMenu` and `GameOverMenu`.

### Public surface

```csharp
public static SettingsMenu Instance { get; private set; }
public bool IsOpen { get; private set; }
public bool EscConsumedThisFrame { get; private set; }
public static event Action OnOpened;
public static event Action OnClosed;

public void Show();
public void Hide();
```

Mirrors the surface exposed by `PauseMenu`. A future generic "modal
overlay" base could fold `PauseMenu` / `GameOverMenu` / `SettingsMenu`
together; not in this PR.

### Class structure

One file, one MonoBehaviour, ~200 lines. Six tabs are hard-coded in
`BuildUI()`. **No `ISettingsTab` interface, no per-tab base class** —
premature abstraction over six empty panels.

When the Debug tab lands during the VFX pass, we revisit if a base
class or registration interface is warranted. The brainstorm already
flagged that future moment.

### Scene-graph + canvas

```
PersistentHud  (DDOL Canvas, sortingOrder 200)
├── UICanvas corner button         (sibling 0 — UIManager)
├── PauseMenuPanel                  (sibling 1 — PauseMenu)
├── GameOverMenuPanel               (sibling 2 — GameOverMenu)
└── SettingsMenuPanel               (sibling 3 — NEW)
    └── (Canvas overrideSorting=true, sortingOrder=350)
        └── DimBackground (full-screen Image, raycastTarget=true)
            └── ModalFrame (~1340 × 760, centred, dark panel)
                ├── Title ("Settings", top)
                ├── CloseButton ("×", top-right corner)
                ├── Sidebar (~220 px wide, left)
                │   ├── Tab button: General
                │   ├── Tab button: Display
                │   ├── Tab button: Graphics
                │   ├── Tab button: Audio
                │   ├── Tab button: Controls
                │   └── Tab button: Gameplay
                └── ContentArea (right of sidebar, padded)
                    ├── Panel: General  (with "Coming soon" label)
                    ├── Panel: Display  (with "Coming soon" label)
                    ├── Panel: Graphics (with "Coming soon" label)
                    ├── Panel: Audio    (with "Coming soon" label)
                    ├── Panel: Controls (with "Coming soon" label)
                    └── Panel: Gameplay (with "Coming soon" label)
                        — only the active tab's panel is SetActive(true)
```

**Why a per-panel `Canvas` override:** `MainMenu` builds its own canvas
at `sortingOrder 200`, the same as `PersistentHud`. Tied canvas orders
fall back to scene render order, which can make a `PersistentHud`-
parented Settings panel appear *below* MainMenu's canvas content.
Giving the panel its own `Canvas` component with `overrideSorting=true,
sortingOrder=350` guarantees it draws above any sibling-canvas-tied
content, while still being parented under (and lifecycle-owned by)
`PersistentHud`. `GameOverMenu`'s effective layering (~400) stays
above — Settings must never appear over the end-of-run overlay.

## Tab layout

**Left-side vertical sidebar** (~220 px wide) holding six tab buttons
stacked top-down. Active tab button is tinted brighter than the others
(using `UIStyle`'s existing palette). Sidebar matches the project's
left-aligned UI conventions (Build toolbar, HUD bars) and scales
comfortably to the future seven-tab count when Debug joins.

**Content area** fills the rest of the modal (right side, ~20 px
padding). Tab switching is implemented as six sibling panel
GameObjects under `ContentArea`; only the active one is
`SetActive(true)`. Each placeholder panel contains a single centred
`Text` reading `"Coming soon"`.

**Modal frame** is ~70 % of the 1920 × 1080 reference resolution
(≈1340 × 760), centred. Dim background fills the full screen with
`color = (0, 0, 0, 0.70)` and `raycastTarget = true` so clicks don't
fall through.

**Close affordance:**

- ESC closes (matches `PauseMenu`'s deliberate "no Resume button"
  minimalism).
- A tiny `×` button top-right of the modal frame for mouse-only
  discoverability — without it, users may not realise ESC is the only
  exit.

## Bootstrap + lifecycle

| Method | Behaviour |
|---|---|
| `Bootstrap()` | `BeforeSceneLoad`: `new GameObject("SettingsMenu")`, `AddComponent<SettingsMenu>()`. |
| `Awake()` | Instance check, `DontDestroyOnLoad(gameObject)`, `BuildUI()`, `HideUI()`, log `"Settings menu ready."`. |
| `OnDestroy()` | Clear `Instance` if it's us. |
| `Update()` | `EscConsumedThisFrame = false`; if `IsOpen` and `Keyboard.current.escapeKey.wasPressedThisFrame`: `Hide()` + set `EscConsumedThisFrame = true`. |
| `Show()` | If `IsOpen` already, no-op. Capture `_previousTimeScale`, set `Time.timeScale = 0`, `ShowUI()`, raise `OnOpened`. |
| `Hide()` | If not `IsOpen`, no-op. Restore `_previousTimeScale`, `HideUI()`, raise `OnClosed`. If `PauseMenu.Instance?.IsOpen == true`, also call `PauseMenu.Instance.ShowUI()` to re-show the pause panel (navigate-to flow). |
| `BuildUI()` | Build the modal: dim + frame + sidebar (six buttons wired to `SelectTab(i)`) + content area (six sibling panels, each with a "Coming soon" label). Default-selected tab is index 0 (General). |
| `SelectTab(int)` | `SetActive(true)` on the chosen content panel, `SetActive(false)` on the rest; tint the sidebar buttons accordingly. |
| `ShowUI()` / `HideUI()` | Toggle the modal root GameObject's active state. |

`Time.timeScale = 0` is set unconditionally in `Show()` — from MainMenu
that's harmless (no game running) and keeps the open/close path
identical from both entry points.

## ESC handling + integration with PauseMenu

### Execution order

`SettingsMenu` is `[DefaultExecutionOrder(-2000)]`. `PauseMenu` is
`-1000`. So in any frame where ESC is pressed, `SettingsMenu.Update`
runs first.

- If Settings is open, `SettingsMenu` calls `Hide()` and sets
  `EscConsumedThisFrame = true`. `PauseMenu.Update` then checks
  `SettingsMenu.Instance.IsOpen || SettingsMenu.Instance.EscConsumedThisFrame`
  and short-circuits its own ESC handling for that frame.
- If Settings is closed, `SettingsMenu.Update` does nothing on ESC.
  `PauseMenu.Update` runs normally.

### Navigate-to flow (from PauseMenu)

```
  ↓ user in BuildScene/FlyScene, presses ESC
    PauseMenu.Open()                       — IsOpen=true, timeScale=0
  ↓ user clicks "Settings" button
    PauseMenu.OnSettingsClicked()
      → PauseMenu.HideUI()                 — panel hidden, IsOpen stays true
      → SettingsMenu.Instance.Show()       — Settings panel up, timeScale stays 0
  ↓ user presses ESC (or × button)
    SettingsMenu.Hide()
      → HideUI()
      → PauseMenu.Instance.ShowUI()         — pause panel back up
      → (PauseMenu still IsOpen, timeScale still 0)
  ↓ user presses ESC again
    PauseMenu.Close()                       — timeScale restored, back to gameplay
```

### Navigate-to flow (from MainMenu)

```
  ↓ user clicks Settings button on MainMenu
    MainMenuController.OnSettings()
      → SettingsMenu.Instance.Show()
  ↓ user presses ESC (or × button)
    SettingsMenu.Hide()
      → HideUI()
      → (PauseMenu.IsOpen is false, no re-show)
  ↓ user is back at MainMenu
```

### PauseMenu changes required

Three small edits in `Assets/Scripts/Core/PauseMenu.cs`:

1. **`Update()` short-circuit.** After the existing
   `EscConsumedThisFrame = false;`, add:
   ```csharp
   if (SettingsMenu.Instance != null &&
       (SettingsMenu.Instance.IsOpen || SettingsMenu.Instance.EscConsumedThisFrame))
       return;
   ```

2. **`ShowUI()` / `HideUI()` visibility.** Change from `private` to
   `internal` so `SettingsMenu` (same namespace, same assembly) can
   drive panel visibility without touching `PauseMenu`'s `IsOpen` or
   `Time.timeScale`. Those stay owned by `PauseMenu.Open` / `Close`.

3. **New "Settings" button in `BuildUI()`.** Insert between "Menu" and
   "Back to Desktop". Restack the four buttons so they're evenly
   spaced — Hangar at Y=+150, Menu at +50, Settings at -50,
   Back-to-Desktop at -150 (50 px gap × 4). `OnSettingsClicked()`
   handler:
   ```csharp
   void OnSettingsClicked()
   {
       HideUI();                              // pause panel hidden, IsOpen stays
       SettingsMenu.Instance.Show();
   }
   ```

## Wiring changes to MainMenuController

Two lines. In `OnSettings()`:

- **Before:**
  ```csharp
  Debug.unityLogger.Log(TAG, "Settings selected — not implemented yet.");
  ```
- **After:**
  ```csharp
  Debug.unityLogger.Log(TAG, "Settings selected — opening Settings menu.");
  SettingsMenu.Instance.Show();
  ```

Match the log-and-act shape used by `OnHangar()` and `OnExit()`.

## Files

- **NEW** `Assets/Scripts/Core/SettingsMenu.cs` (~200 lines).
- **MODIFY** `Assets/Scripts/Core/PauseMenu.cs` (~15 lines added, 2
  method visibilities flipped to `internal`, button stack restacked).
- **MODIFY** `Assets/Scripts/MainMenu/MainMenuController.cs` (one line
  in `OnSettings`).

No new prefabs, no new ScriptableObjects, no new asmdefs, no scene
file changes. Layer 5 (`UI`) is reused, no new layers.

The README's Main Menu controls table (`Settings: Placeholder`) and
the Pause Overlay controls table (currently lists Menu / Back to
Desktop) need to be updated; that's part of the implementation PR's
docs touch.

## Scope

| In | Out |
|---|---|
| `SettingsMenu` DDOL singleton (`CubeFly.Core`) | Persistence layer of any kind (PlayerPrefs, JSON, ScriptableObject) |
| Six placeholder tabs with sidebar + content layout | Debug tab (added during VFX pass) |
| ESC handling, `OnOpened` / `OnClosed` events | Any actual control in any tab |
| Navigate-to integration with PauseMenu | Volume / FOV / mouse-sensitivity / rebinding UI |
| New "Settings" button on PauseMenu | Save-format / migration for settings |
| MainMenuController Settings button wired | Settings-as-scene (singleton overlay instead) |
| README Main-Menu + Pause-Overlay controls tables updated | A generic `ModalOverlay` base class refactoring `PauseMenu` / `GameOverMenu` / `SettingsMenu` together |

## Out of scope (explicit)

- **No persistence.** Nothing in the scaffold needs to save state, so
  no `Settings` data class, no `PlayerPrefs`, no JSON file, no
  `SaveManager`-style atomic writes. Each future tab content PR
  brings whatever persistence model it needs.
- **No actual controls.** The six placeholder panels are intentionally
  empty. Adding a volume slider, FOV slider, key-binding row, etc.
  is its own future PR per tab.
- **No Debug tab.** That's added during the VFX pass (Up Next #1).
  This spec leaves room for it but does not implement it.
- **No refactoring of `PauseMenu` / `GameOverMenu`.** Pattern overlap
  is real but folding the three together into a base class is YAGNI
  with only one new client (this spec). Revisit if a fourth modal
  overlay ever lands.

## References

- `Assets/Scripts/Core/PauseMenu.cs` — canonical DDOL overlay pattern;
  ESC handling; `UIStyle` usage; `EscConsumedThisFrame` flag.
- `Assets/Scripts/Core/GameOverMenu.cs` — sibling DDOL overlay; same
  pattern; sortingOrder layering reference.
- `Assets/Scripts/Core/PersistentHud.cs` — shared canvas; lazy-create;
  sibling-order behaviour.
- `Assets/Scripts/MainMenu/MainMenuController.cs` — Settings button
  entry point.
- `Assets/Scripts/Core/UIStyle.cs` — `BuildLabel`,
  `BuildLabeledButton`, `BuildScreenSpaceCanvas`, palette.
- `ROADMAP.md` — Up Next item #2 (Settings menu) describes the user-
  facing scope.
- `docs/vfx_pass_ideas.md` — describes the future Debug tab the seventh
  slot is reserved for.
