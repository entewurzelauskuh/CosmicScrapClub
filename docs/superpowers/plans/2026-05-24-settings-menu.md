# Settings Menu Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. (User has pre-selected inline execution; do NOT prompt for execution mode at the end.)

**Goal:** Land the placeholder scaffold for the tabbed Settings menu (Up Next #2 in `ROADMAP.md`) on branch `feat/settings-menu`: a DDOL singleton with six empty tabs, reachable from both the Main Menu's Settings button and a new Settings button on the ESC pause overlay, with navigate-to ESC drill-down.

**Architecture:** A new `SettingsMenu` DDOL self-bootstrapping singleton in `CubeFly.Core`, parented under `PersistentHud.Instance.Root` with a per-panel `Canvas` override at `sortingOrder=350`. Mirrors the `PauseMenu` / `GameOverMenu` pattern exactly. Six tabs hard-coded in `BuildUI()` (no per-tab base class — premature with six empty panels). PauseMenu gets visibility on its `ShowUI`/`HideUI` flipped to `internal`, a new Settings button, and a one-line ESC short-circuit so `SettingsMenu` owns ESC when it's open.

**Tech Stack:** Unity 6.3 LTS / URP 17.3, MonoBehaviour C#, legacy `UnityEngine.UI.Text` + `LegacyRuntime.ttf` (no TMP), New Input System (`UnityEngine.InputSystem.Keyboard.current`). No automated tests in the project — verification per task is **Unity MCP compile-check** (`mcp__unityMCP__create_script` / `script_apply_edits` auto-trigger import+compile; poll `mcpforunity://editor/state` until `is_compiling == false`; then `mcp__unityMCP__read_console(types=["error"], count=20)` expecting zero `Assets/Scripts` errors). User play-test happens at the end of all tasks.

**Spec reference:** `docs/superpowers/specs/2026-05-24-settings-menu-design.md` (commit `51ef2fd`).

---

## File map

| File | Action | Touched in |
|---|---|---|
| `Assets/Scripts/Core/PauseMenu.cs` | Modify (visibility flip) | Task 1 |
| `Assets/Scripts/Core/SettingsMenu.cs` | **Create** | Task 2 |
| `Assets/Scripts/MainMenu/MainMenuController.cs` | Modify (wire OnSettings) | Task 3 |
| `Assets/Scripts/Core/PauseMenu.cs` | Modify (button + ESC short-circuit + restack) | Task 4 |
| `README.md` | Modify (controls tables) | Task 5 |

Order is dependency-clean: Task 1 (PauseMenu visibility) is prep so Task 2's `SettingsMenu.Hide()` can call `PauseMenu.Instance.ShowUI()`. Task 4 (PauseMenu button + ESC) is last because it depends on `SettingsMenu` existing.

---

## Task 1: PauseMenu — expose ShowUI/HideUI as `internal`

**Files:**
- Modify: `Assets/Scripts/Core/PauseMenu.cs:263-271` (the `ShowUI` / `HideUI` methods)

**Rationale:** Prep step. `SettingsMenu.Hide()` (Task 2) needs to call `PauseMenu.Instance.ShowUI()` to re-show the pause panel in the navigate-to flow. Both classes live in `CubeFly.Core` so `internal` is the right visibility — narrower than `public`, no test asmdef needed.

- [ ] **Step 1: Apply the visibility change**

Use `mcp__unityMCP__script_apply_edits` (or `Edit`) on `Assets/Scripts/Core/PauseMenu.cs` to change the two method signatures.

**Before** (lines 263 and 268 in the current file):

```csharp
        void ShowUI()
        {
            if (_root != null) _root.SetActive(true);
        }

        void HideUI()
        {
            if (_root != null) _root.SetActive(false);
        }
```

**After:**

```csharp
        internal void ShowUI()
        {
            if (_root != null) _root.SetActive(true);
        }

        internal void HideUI()
        {
            if (_root != null) _root.SetActive(false);
        }
```

- [ ] **Step 2: Wait for Unity to recompile**

Poll `mcpforunity://editor/state` until `is_compiling == false` and `ready_for_tools == true`. (`script_apply_edits` auto-triggers compilation.)

- [ ] **Step 3: Verify clean compile**

Run: `mcp__unityMCP__read_console(action="get", types=["error"], count=20, format="detailed")`
Expected: zero errors referencing `Assets/Scripts/`. Warnings unrelated to our change are fine.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Core/PauseMenu.cs
git commit -m "PauseMenu: expose ShowUI/HideUI as internal

Prep for the Settings menu's navigate-to flow — SettingsMenu.Hide()
needs to re-show PauseMenu's panel when Settings was opened from the
pause overlay. Both classes live in CubeFly.Core, so internal is the
narrowest visibility that lets the call compile. IsOpen and
Time.timeScale stay owned by PauseMenu's Open/Close.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 2: Create SettingsMenu.cs

**Files:**
- Create: `Assets/Scripts/Core/SettingsMenu.cs`

**Rationale:** The whole scaffold in one cohesive file — DDOL singleton, lifecycle, ESC, `BuildUI()`, and tab switching. Hard-coded six tabs (no `ISettingsTab` interface). One file, one MonoBehaviour, ~250 lines. After this task the singleton bootstraps, the modal builds, and `SettingsMenu.Instance.Show()` opens a working Settings UI with switchable tabs — but no entry point wires to it yet (that's Tasks 3 and 4).

- [ ] **Step 1: Create the file**

Use `mcp__unityMCP__create_script` with `path="Assets/Scripts/Core/SettingsMenu.cs"` and the contents below.

```csharp
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CubeFly.Core
{
    // Tabbed Settings UI reachable from both the Main Menu's Settings
    // button and the ESC pause overlay. Six placeholder tabs (General /
    // Display / Graphics / Audio / Controls / Gameplay) — actual
    // controls fill in tab by tab in later PRs. A seventh Debug tab
    // is added during the VFX pass to surface per-effect toggles.
    //
    // DDOL self-bootstrapping singleton, same shape as PauseMenu and
    // GameOverMenu.
    //
    // Behaviour:
    //   • Show() opens the modal and freezes time. Called from
    //     MainMenuController.OnSettings or PauseMenu.OnSettingsClicked.
    //   • ESC closes (matches PauseMenu's no-Resume-button minimalism).
    //     A small × button top-right of the modal is also available
    //     for mouse-only discoverability.
    //   • When Hide() runs, if PauseMenu was the caller (its IsOpen is
    //     still true), it re-shows PauseMenu's panel — the navigate-to
    //     drill-down is Settings → Pause → game.
    //   • Execution order is -2000 (below PauseMenu's -1000) so this
    //     script's ESC handler runs first; PauseMenu checks
    //     SettingsMenu.IsOpen / EscConsumedThisFrame and short-circuits
    //     its own ESC handling for that frame.
    //   • The modal panel adds its own Canvas override with
    //     overrideSorting=true, sortingOrder=350 so it draws above
    //     MainMenu's own scene canvas (also sortingOrder 200) and any
    //     PersistentHud sibling (UIManager corner button, PauseMenu),
    //     while staying below GameOverMenu (~400).
    //
    // Persistence: there is none. The scaffold has nothing to save;
    // the first real setting brings its own persistence design with it.
    [DefaultExecutionOrder(-2000)]
    public class SettingsMenu : MonoBehaviour
    {
        public static SettingsMenu Instance { get; private set; }

        public bool IsOpen { get; private set; }
        public bool EscConsumedThisFrame { get; private set; }

        public static event Action OnOpened;
        public static event Action OnClosed;

        const string TAG = "SettingsMenu";

        // Tab names also serve as the display label for the sidebar
        // buttons. Adding a tab later (e.g. Debug during the VFX pass)
        // means appending here + adding a content panel in BuildUI.
        static readonly string[] TabNames = new[]
        {
            "General", "Display", "Graphics", "Audio", "Controls", "Gameplay"
        };

        GameObject _root;
        GameObject[] _tabPanels = Array.Empty<GameObject>();
        Button[] _tabButtons = Array.Empty<Button>();
        int _activeTab;
        float _previousTimeScale = 1f;

        static readonly Color SidebarActiveTint   = new Color(0.85f, 0.85f, 1f, 1f);
        static readonly Color SidebarInactiveTint = new Color(0.30f, 0.30f, 0.40f, 0.9f);

        // Self-bootstrap: spawn the singleton before any scene loads.
        // BeforeSceneLoad runs once per play session in both Editor
        // and standalone, so there's no risk of duplicates.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            GameObject go = new GameObject("SettingsMenu");
            go.AddComponent<SettingsMenu>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            BuildUI();
            HideUI();

            Debug.unityLogger.Log(TAG, "Settings menu ready.");
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            EscConsumedThisFrame = false;
            if (!IsOpen) return;

            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            if (!kb.escapeKey.wasPressedThisFrame) return;

            Hide();
            EscConsumedThisFrame = true;
        }

        public void Show()
        {
            if (IsOpen) return;
            IsOpen = true;
            _previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            ShowUI();
            Debug.unityLogger.Log(TAG, "Opened.");
            OnOpened?.Invoke();
        }

        public void Hide()
        {
            if (!IsOpen) return;
            IsOpen = false;
            Time.timeScale = _previousTimeScale;
            HideUI();
            Debug.unityLogger.Log(TAG, "Closed.");
            OnClosed?.Invoke();

            // Navigate-to flow: if Settings was opened from PauseMenu,
            // restore the pause panel so the player lands back where
            // they were. PauseMenu's IsOpen / Time.timeScale stay
            // owned by PauseMenu itself.
            if (PauseMenu.Instance != null && PauseMenu.Instance.IsOpen)
            {
                PauseMenu.Instance.ShowUI();
            }
        }

        void ShowUI() { if (_root != null) _root.SetActive(true); }
        void HideUI() { if (_root != null) _root.SetActive(false); }

        // ---------- UI construction ----------

        void BuildUI()
        {
            // Parent under the shared persistent canvas. The panel
            // adds its own Canvas override below so it draws above
            // MainMenu's own scene canvas (also sortingOrder 200).
            GameObject panelGO = new GameObject("SettingsMenuPanel",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                typeof(Canvas), typeof(GraphicRaycaster));
            panelGO.transform.SetParent(PersistentHud.Instance.Root, false);
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) panelGO.layer = uiLayer;
            _root = panelGO;

            RectTransform root = (RectTransform)panelGO.transform;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            // Canvas override: draw above MainMenu's scene canvas (200)
            // and any PersistentHud sibling, but below GameOverMenu.
            Canvas canvasOverride = panelGO.GetComponent<Canvas>();
            canvasOverride.overrideSorting = true;
            canvasOverride.sortingOrder = 350;

            // Dim backdrop — full-screen, also catches clicks so
            // nothing under the overlay can be reached.
            Image dim = panelGO.GetComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.70f);
            dim.raycastTarget = true;

            // Modal frame — ~1340 x 760, centred. Holds title, close
            // button, sidebar, and the six content panels.
            GameObject frameGO = new GameObject("Frame",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            frameGO.transform.SetParent(root, false);
            if (uiLayer >= 0) frameGO.layer = uiLayer;

            RectTransform frameRT = (RectTransform)frameGO.transform;
            frameRT.anchorMin = frameRT.anchorMax = frameRT.pivot = new Vector2(0.5f, 0.5f);
            frameRT.sizeDelta = new Vector2(1340f, 760f);
            frameRT.anchoredPosition = Vector2.zero;

            Image frameImg = frameGO.GetComponent<Image>();
            frameImg.color = UIStyle.BackgroundIdle;
            frameImg.raycastTarget = true;

            // Title at top of frame.
            Text title = UIStyle.BuildLabel(frameRT, "Settings", fontSize: 48,
                style: FontStyle.Bold);
            RectTransform titleRT = (RectTransform)title.transform;
            titleRT.anchorMin = new Vector2(0f, 1f);
            titleRT.anchorMax = new Vector2(1f, 1f);
            titleRT.pivot = new Vector2(0.5f, 1f);
            titleRT.sizeDelta = new Vector2(0f, 70f);
            titleRT.anchoredPosition = new Vector2(0f, -20f);

            // × close button — top-right of frame, mouse-only convenience.
            (Button closeButton, Text _) = UIStyle.BuildLabeledButton(
                frameRT, "×", new Vector2(40f, 40f), fontSize: 28);
            RectTransform closeRT = (RectTransform)closeButton.transform;
            closeRT.anchorMin = closeRT.anchorMax = new Vector2(1f, 1f);
            closeRT.pivot = new Vector2(1f, 1f);
            closeRT.anchoredPosition = new Vector2(-20f, -20f);
            closeButton.onClick.AddListener(Hide);

            // Sidebar at left, holding tab buttons stacked top-down.
            GameObject sidebarGO = new GameObject("Sidebar", typeof(RectTransform));
            sidebarGO.transform.SetParent(frameRT, false);
            if (uiLayer >= 0) sidebarGO.layer = uiLayer;

            RectTransform sidebarRT = (RectTransform)sidebarGO.transform;
            sidebarRT.anchorMin = new Vector2(0f, 0f);
            sidebarRT.anchorMax = new Vector2(0f, 1f);
            sidebarRT.pivot = new Vector2(0f, 1f);
            sidebarRT.sizeDelta = new Vector2(220f, -120f);          // width / height-minus-title
            sidebarRT.anchoredPosition = new Vector2(20f, -100f);    // 20 px left, below title

            _tabButtons = new Button[TabNames.Length];
            for (int i = 0; i < TabNames.Length; i++)
            {
                int captured = i;
                (Button tabButton, Text _) = UIStyle.BuildLabeledButton(
                    sidebarRT, TabNames[i], new Vector2(200f, 60f), fontSize: 22);
                RectTransform tabRT = (RectTransform)tabButton.transform;
                tabRT.anchorMin = tabRT.anchorMax = tabRT.pivot = new Vector2(0f, 1f);
                tabRT.anchoredPosition = new Vector2(0f, -i * 70f);
                tabButton.onClick.AddListener(() => SelectTab(captured));
                _tabButtons[i] = tabButton;
            }

            // Content area — right of sidebar.
            GameObject contentGO = new GameObject("Content", typeof(RectTransform));
            contentGO.transform.SetParent(frameRT, false);
            if (uiLayer >= 0) contentGO.layer = uiLayer;

            RectTransform contentRT = (RectTransform)contentGO.transform;
            contentRT.anchorMin = new Vector2(0f, 0f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot = new Vector2(0.5f, 0.5f);
            contentRT.offsetMin = new Vector2(260f, 20f);            // 20 left + 220 sidebar + 20 gap
            contentRT.offsetMax = new Vector2(-20f, -100f);          // 20 right, 100 title clearance

            // Six placeholder content panels — one per tab.
            _tabPanels = new GameObject[TabNames.Length];
            for (int i = 0; i < TabNames.Length; i++)
            {
                GameObject panel = new GameObject(TabNames[i] + "Panel",
                    typeof(RectTransform));
                panel.transform.SetParent(contentRT, false);
                if (uiLayer >= 0) panel.layer = uiLayer;

                RectTransform prt = (RectTransform)panel.transform;
                prt.anchorMin = Vector2.zero;
                prt.anchorMax = Vector2.one;
                prt.offsetMin = Vector2.zero;
                prt.offsetMax = Vector2.zero;

                Text label = UIStyle.BuildLabel(prt, "Coming soon", fontSize: 32);
                RectTransform lrt = (RectTransform)label.transform;
                lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0.5f, 0.5f);
                lrt.sizeDelta = new Vector2(400f, 80f);
                lrt.anchoredPosition = Vector2.zero;

                _tabPanels[i] = panel;
            }

            SelectTab(0);
        }

        void SelectTab(int index)
        {
            if (index < 0 || index >= _tabPanels.Length) return;
            _activeTab = index;
            for (int i = 0; i < _tabPanels.Length; i++)
            {
                _tabPanels[i].SetActive(i == index);
                if (_tabButtons[i] != null)
                {
                    Image img = _tabButtons[i].GetComponent<Image>();
                    if (img != null)
                    {
                        img.color = (i == index) ? SidebarActiveTint : SidebarInactiveTint;
                    }
                }
            }
        }
    }
}
```

- [ ] **Step 2: Wait for Unity to recompile**

Poll `mcpforunity://editor/state` until `is_compiling == false` and `ready_for_tools == true`.

- [ ] **Step 3: Verify clean compile**

Run: `mcp__unityMCP__read_console(action="get", types=["error"], count=20, format="detailed")`
Expected: zero errors referencing `Assets/Scripts/SettingsMenu` or `Assets/Scripts/Core/`. A first-compile "type does not exist" cache flake on `SettingsMenu` is a known Unity quirk — if it appears, call `mcp__unityMCP__refresh_unity(mode="force")` and re-read.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Core/SettingsMenu.cs Assets/Scripts/Core/SettingsMenu.cs.meta
git commit -m "Add SettingsMenu — DDOL singleton scaffold with six placeholder tabs

New CubeFly.Core.SettingsMenu mirrors the PauseMenu / GameOverMenu
pattern: BeforeSceneLoad self-bootstrap, DontDestroyOnLoad, procedural
UI under PersistentHud.Instance.Root with a Canvas override at
sortingOrder=350 so it draws above MainMenu's scene canvas.

Six tabs hard-coded (General / Display / Graphics / Audio / Controls /
Gameplay), each rendering a centred 'Coming soon' label. Sidebar
button tints flip on tab change. ESC closes (matches PauseMenu's
no-Resume minimalism); a small × button top-right is also available
for mouse-only users.

Hide() re-shows PauseMenu when applicable so a future PauseMenu
Settings button drives the navigate-to drill-down (Settings → Pause →
game). No entry points wire to SettingsMenu yet — those land in the
next tasks.

Persistence is deliberately not part of this scaffold.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

(Unity auto-generates `SettingsMenu.cs.meta` on import. The `git add` includes it; if the meta file isn't on disk yet, give Unity a beat after the compile finishes and re-add.)

---

## Task 3: MainMenu — wire OnSettings to open SettingsMenu

**Files:**
- Modify: `Assets/Scripts/MainMenu/MainMenuController.cs:64-68` (the `OnSettings` method)

**Rationale:** First entry point wired. After this task the user can launch the game, click `Settings` on the Main Menu, see the modal open, switch tabs, and press ESC (or click `×`) to close.

- [ ] **Step 1: Apply the wiring edit**

Use `mcp__unityMCP__script_apply_edits` (or `Edit`) on `Assets/Scripts/MainMenu/MainMenuController.cs`.

**Before:**

```csharp
        void OnSettings()
        {
            // Placeholder: Settings panel is a future feature.
            Debug.unityLogger.Log(TAG, "Settings selected — not implemented yet.");
        }
```

**After:**

```csharp
        void OnSettings()
        {
            Debug.unityLogger.Log(TAG, "Settings selected — opening Settings menu.");
            SettingsMenu.Instance.Show();
        }
```

- [ ] **Step 2: Wait for Unity to recompile**

Poll `mcpforunity://editor/state` until `is_compiling == false`.

- [ ] **Step 3: Verify clean compile**

Run: `mcp__unityMCP__read_console(action="get", types=["error"], count=20, format="detailed")`
Expected: zero errors. `SettingsMenu` is in `CubeFly.Core` and `MainMenuController.cs` already has `using CubeFly.Core;` at the top (line 1) — no new using needed.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/MainMenu/MainMenuController.cs
git commit -m "MainMenu: wire Settings button to open SettingsMenu

OnSettings() was a no-op log line; it now calls
SettingsMenu.Instance.Show(). The existing 'using CubeFly.Core;' is
sufficient — no other change needed in this file.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 4: PauseMenu — Settings button + ESC short-circuit + button restack

**Files:**
- Modify: `Assets/Scripts/Core/PauseMenu.cs:99-117` (`Update()` — add ESC short-circuit)
- Modify: `Assets/Scripts/Core/PauseMenu.cs:210-249` (`BuildUI()` — add Settings button + restack)
- Modify: `Assets/Scripts/Core/PauseMenu.cs` (add `OnSettingsClicked()` handler)

**Rationale:** Second entry point + ESC drill-down. Three coordinated edits in one file, committed together as a single "PauseMenu integration" change. After this task the user can ESC into Pause from gameplay, click Settings, switch tabs, ESC back to Pause, ESC back to the game.

- [ ] **Step 1: Add the ESC short-circuit to PauseMenu.Update()**

Use `mcp__unityMCP__script_apply_edits` (or `Edit`) on `Assets/Scripts/Core/PauseMenu.cs`.

**Before** (lines 99-117):

```csharp
        void Update()
        {
            EscConsumedThisFrame = false;

            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            if (!kb.escapeKey.wasPressedThisFrame) return;

            if (IsOpen)
            {
                Close();
                EscConsumedThisFrame = true;
            }
            else if (CanOpenInActiveScene())
            {
                Open();
                EscConsumedThisFrame = true;
            }
        }
```

**After:**

```csharp
        void Update()
        {
            EscConsumedThisFrame = false;

            // If SettingsMenu owns ESC this frame (it's open, or just
            // closed itself on this same ESC press), do nothing here.
            // SettingsMenu runs at [DefaultExecutionOrder(-2000)], so
            // its Update has already run by the time we reach this.
            if (SettingsMenu.Instance != null &&
                (SettingsMenu.Instance.IsOpen || SettingsMenu.Instance.EscConsumedThisFrame))
                return;

            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            if (!kb.escapeKey.wasPressedThisFrame) return;

            if (IsOpen)
            {
                Close();
                EscConsumedThisFrame = true;
            }
            else if (CanOpenInActiveScene())
            {
                Open();
                EscConsumedThisFrame = true;
            }
        }
```

- [ ] **Step 2: Restack BuildUI() buttons and add the Settings button**

**Before** (lines 244-248 — the button-creation block at the end of `BuildUI()`):

```csharp
            // Buttons stacked below the title. Same dimensions as the
            // MainMenu buttons to keep the visual language consistent.
            // Hangar is created first (top of the stack) but is only
            // visible in FlyScene — see OnSceneLoaded.
            _hangarButton = CreateButton(root, "Hangar",          new Vector2(0f, 100f),  OnHangarClicked);
                            CreateButton(root, "Menu",            new Vector2(0f, 0f),    OnMenuClicked);
                            CreateButton(root, "Back to Desktop", new Vector2(0f, -100f), OnExitClicked);
```

**After:**

```csharp
            // Buttons stacked below the title. Same dimensions as the
            // MainMenu buttons to keep the visual language consistent.
            // Hangar is created first (top of the stack) but is only
            // visible in FlyScene — see OnSceneLoaded. Four-button
            // restack: 100 px gap between each button, centred around 0.
            _hangarButton = CreateButton(root, "Hangar",          new Vector2(0f,  150f), OnHangarClicked);
                            CreateButton(root, "Menu",            new Vector2(0f,   50f), OnMenuClicked);
                            CreateButton(root, "Settings",        new Vector2(0f,  -50f), OnSettingsClicked);
                            CreateButton(root, "Back to Desktop", new Vector2(0f, -150f), OnExitClicked);
```

- [ ] **Step 3: Add the OnSettingsClicked() handler**

Insert the new handler immediately after `OnMenuClicked()` (currently lines 164-173 in the file) and before `OnExitClicked()` (currently line 175). This keeps handlers in the same order as their buttons in `BuildUI()`.

**Insert this new method:**

```csharp
        void OnSettingsClicked()
        {
            // Navigate-to flow. Hide the pause panel WITHOUT closing
            // PauseMenu (IsOpen stays true, Time.timeScale stays 0).
            // SettingsMenu.Show() takes over; on its close it calls
            // PauseMenu.Instance.ShowUI() to restore the pause panel.
            HideUI();
            Debug.unityLogger.Log(TAG, "Settings button — opening SettingsMenu.");
            SettingsMenu.Instance.Show();
        }
```

- [ ] **Step 4: Wait for Unity to recompile**

Poll `mcpforunity://editor/state` until `is_compiling == false`.

- [ ] **Step 5: Verify clean compile**

Run: `mcp__unityMCP__read_console(action="get", types=["error"], count=20, format="detailed")`
Expected: zero errors. `SettingsMenu` is in the same `CubeFly.Core` namespace so no new `using` directive is needed.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Core/PauseMenu.cs
git commit -m "PauseMenu: add Settings button + ESC integration with SettingsMenu

Three coordinated edits:

1. Update() short-circuits when SettingsMenu owns ESC (its IsOpen or
   EscConsumedThisFrame), so a single ESC press never triggers both
   PauseMenu.Close() and SettingsMenu.Hide() on the same frame.
2. BuildUI() restacks to four buttons evenly spaced at Y=+150/+50/-50/
   -150 (was three at +100/0/-100). New 'Settings' button slots in
   between Menu and Back to Desktop.
3. New OnSettingsClicked() handler: hides the pause panel (IsOpen
   stays true, time stays frozen), opens SettingsMenu. The
   SettingsMenu.Hide() path re-shows the pause panel when the player
   ESCs back, completing the navigate-to flow.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 5: README — update Main Menu and Pause Overlay controls tables

**Files:**
- Modify: `README.md` (Main Menu section, Pause Overlay section)

**Rationale:** README is the canonical user-facing controls reference per the project's CLAUDE.md ("keep them in sync when you change behaviour"). The current Main Menu table calls Settings "Placeholder" — no longer true. The Pause Overlay table doesn't list Settings — needs a new row.

- [ ] **Step 1: Update the Main Menu Settings row**

Use `Edit` on `README.md`.

**Before:**

```markdown
| `Settings` | Placeholder. |
```

**After:**

```markdown
| `Settings` | Open the Settings menu (six placeholder tabs — General / Display / Graphics / Audio / Controls / Gameplay). |
```

- [ ] **Step 2: Add the Settings button row to the Pause Overlay table**

**Before** (the full Pause Overlay table — lines 173-178 in the current README):

```markdown
| Input | Action |
|-------|--------|
| `Esc` | Open or close the overlay. (Closing acts as Resume — no dedicated Resume button.) |
| `Menu` button | Load Main Menu. |
| `Back to Desktop` button | Quit (or stop Editor play mode). |
```

**After:**

```markdown
| Input | Action |
|-------|--------|
| `Esc` | Open or close the overlay. (Closing acts as Resume — no dedicated Resume button. If the Settings menu is open over the pause overlay, the first `Esc` closes Settings and re-shows the overlay; the second `Esc` closes the overlay.) |
| `Settings` button | Open the Settings menu (six placeholder tabs). `Esc` from Settings returns here, then `Esc` again resumes the game. |
| `Menu` button | Load Main Menu. |
| `Back to Desktop` button | Quit (or stop Editor play mode). |
```

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "Docs: update Main Menu / Pause Overlay controls tables for Settings

- Main Menu: Settings row no longer says 'Placeholder' — links to the
  six-tab scaffold.
- Pause Overlay: new Settings button row between Menu and the ESC
  note clarifying the navigate-to drill-down (ESC closes Settings →
  Pause re-appears → ESC again resumes).

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Post-task verification (user play-test)

After all five tasks land, ask the user to manually play-test:

1. **From the Main Menu:** click `Settings`. The modal opens centred over the title. Click each of the six sidebar tabs — content swaps to "Coming soon" for each, sidebar tint highlights the active tab. Click the `×` top-right — modal closes, back at the Main Menu. Re-open via `Settings`. Press `Esc` — modal closes.
2. **From in-game:** start a construct from a save slot, drop into BuildScene or FlyScene. Press `Esc` — Pause overlay appears with four buttons. Click `Settings` — Pause hides, Settings opens. Switch tabs. Press `Esc` — Settings closes, Pause re-appears (time still frozen). Press `Esc` again — Pause closes, game resumes.
3. **Edge case:** Press `Esc` to pause, click `Settings`, then click `Menu` (wait — Menu isn't in Settings; closing Settings via ESC should return to Pause, where `Menu` is). Verify the Pause buttons still work after coming back from Settings.

If anything is wrong (button mislabeled, modal doesn't open, ESC doesn't drill back), fix forward with a new commit on the same branch and re-test.

---

## Plan self-review

**Spec coverage:**

| Spec requirement | Plan task |
|---|---|
| DDOL singleton with `[DefaultExecutionOrder(-2000)]` + BeforeSceneLoad bootstrap | Task 2 |
| Public surface (`Instance`, `IsOpen`, `EscConsumedThisFrame`, `Show`, `Hide`, `OnOpened`, `OnClosed`) | Task 2 |
| Single ~200-line file, no per-tab base class | Task 2 |
| Scene-graph: parented under `PersistentHud.Instance.Root` with Canvas override `sortingOrder=350` | Task 2 (`BuildUI`) |
| Tab layout: sidebar (220 px) + content + 6 placeholder panels + × button + dim background + modal frame | Task 2 (`BuildUI`) |
| Tab switching: hard-coded six tabs, sidebar tint flips, content panels SetActive toggled | Task 2 (`SelectTab`) |
| ESC handling: `SettingsMenu.Update()` consumes ESC when `IsOpen` | Task 2 (`Update`) |
| `PauseMenu.ShowUI` / `HideUI` flipped to `internal` | Task 1 |
| `PauseMenu.Update()` short-circuit on `SettingsMenu.IsOpen` / `EscConsumedThisFrame` | Task 4, Step 1 |
| `PauseMenu` new Settings button + four-button restack | Task 4, Step 2 |
| `PauseMenu.OnSettingsClicked()` hides pause panel then calls `SettingsMenu.Show()` | Task 4, Step 3 |
| `SettingsMenu.Hide()` re-shows `PauseMenu` when navigate-to applicable | Task 2 (`Hide` body) |
| `MainMenuController.OnSettings()` calls `SettingsMenu.Instance.Show()` | Task 3 |
| README Main-Menu + Pause-Overlay tables updated | Task 5 |

All requirements covered. No spec sections orphaned.

**Placeholder scan:** No `TBD`, `TODO`, `FIXME`, "implement later", or "similar to Task N" patterns. Every code block is complete; every command shows expected output.

**Type consistency:**
- `SettingsMenu.Instance.Show()` — used in Task 3 and Task 4; defined in Task 2 as `public void Show()`. ✓
- `SettingsMenu.Instance.IsOpen` — used in Task 4 short-circuit; defined in Task 2 as `public bool IsOpen { get; private set; }`. ✓
- `SettingsMenu.Instance.EscConsumedThisFrame` — same. ✓
- `PauseMenu.Instance.ShowUI()` — called from `SettingsMenu.Hide()` in Task 2; made `internal` in Task 1. ✓
- `PauseMenu.Instance.IsOpen` — already `public bool IsOpen { get; private set; }` in current code; unchanged. ✓
- `TagNames` array order matches both `_tabPanels` and `_tabButtons` arrays. ✓
- `SelectTab(int)` signature consistent throughout Task 2. ✓

No type / signature drift between tasks.

**Out-of-scope guard:** No task touches persistence, the Debug tab, any actual control inside a tab, save format, keybinding rebinding, FOV slider, volume sliders, or `GameOverMenu`. Matches the spec's Scope table exactly.
