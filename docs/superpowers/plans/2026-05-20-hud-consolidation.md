# HUD / Canvas Consolidation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Collapse the project's 10+ self-bootstrapped runtime canvases into three shared HUD roots (`PersistentHud`, `FlyHud`, `BuildHud`) so existing HUD elements behave identically and future HUD additions attach to `FooHud.Instance.Root` instead of authoring more free-standing canvases.

**Architecture:** Three new MonoBehaviours each owning a single `Canvas` + `GraphicRaycaster` + `CanvasScaler`. `PersistentHud` is a DDOL singleton, lazy-created on first `Instance` access — `UIManager` / `PauseMenu` / `GameOverMenu`'s existing `BeforeSceneLoad` bootstraps drive its creation. `FlyHud` and `BuildHud` are scene-attached `[DefaultExecutionOrder(-500)]` singletons on a new HUD GameObject per gameplay scene. Each of the 10 existing HUD scripts drops its own canvas-creation and parents its UI tree under the relevant shared root.

**Tech Stack:** Unity 6.3 LTS / URP 17.3, MonoBehaviour C#, UnityEngine.UI (legacy uGUI), new Input System, no DOTS.

**Delivery:** 3 commits in dependency order (each compiles + is independently reviewable), then one PR with Copilot review.

**Branch:** `feat/hud-consolidation` (already created off `main`; spec committed at `2d7821b`).

**Spec reference:** `docs/superpowers/specs/2026-05-20-hud-consolidation-design.md`

---

## File structure

**Create:**
- `Assets/Scripts/Core/PersistentHud.cs` (+ `.cs.meta`)
- `Assets/Scripts/Fly/FlyHud.cs` (+ `.cs.meta`)
- `Assets/Scripts/Build/BuildHud.cs` (+ `.cs.meta`)

**Modify (scripts — drop own canvas, parent to shared root):**
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
- `Assets/UI/UIBootstrap.prefab` (+ `.prefab.meta`)
- `Assets/UI/UICanvas.prefab` (+ `.prefab.meta`)

---

## Commit 1 — `PersistentHud` + persistent-UI refactor

Adds `PersistentHud`, refactors the three persistent UI scripts (`UIManager`, `PauseMenu`, `GameOverMenu`) to attach to it, deletes `UIBootstrap.cs` + the two `Assets/UI/` prefabs, removes the `UIBootstrap` GameObject from both gameplay scenes.

### Task 1: Create `PersistentHud.cs`

**Files:**
- Create: `Assets/Scripts/Core/PersistentHud.cs`
- Create: `Assets/Scripts/Core/PersistentHud.cs.meta` (Unity will generate on import)

- [ ] **Step 1: Write `PersistentHud.cs`**

```csharp
using UnityEngine;
using UnityEngine.UI;

namespace CubeFly.Core
{
    // Shared screen-space-overlay canvas for every persistent UI element
    // — the corner Fly!/Hangar button (UIManager), the ESC pause menu
    // (PauseMenu), and the Construct Destroyed overlay (GameOverMenu).
    //
    // Lifetime: DDOL singleton, lazy-created on first `Instance` access.
    // The three persistent UI scripts each call `PersistentHud.Instance.Root`
    // in their Awake/BuildUI to parent their UI tree; whichever Awake
    // runs first triggers Create(). No [RuntimeInitializeOnLoadMethod]
    // is needed because those three already self-bootstrap before scene
    // load and pull this canvas into existence as a side effect.
    //
    // Sibling order inside this canvas (later siblings render on top):
    //   corner button (UIManager)     → built first
    //   pause panel (PauseMenu)       → built second; full-screen dim
    //                                   covers the corner button when
    //                                   active.
    //   game-over panel (GameOverMenu)→ built third; covers PauseMenu
    //                                   when triggered.
    //
    // sortingOrder 200 — sits above the scene HUDs (FlyHud / BuildHud
    // at 100) so the pause / game-over dim panels visually overlay the
    // gameplay HUD.
    public class PersistentHud : MonoBehaviour
    {
        static PersistentHud _instance;
        public static PersistentHud Instance => _instance != null ? _instance : Create();

        public RectTransform Root => (RectTransform)transform;

        const string TAG = "PersistentHud";

        static PersistentHud Create()
        {
            GameObject go = new GameObject(
                "PersistentHud",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) go.layer = uiLayer;

            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _instance = go.AddComponent<PersistentHud>();
            DontDestroyOnLoad(go);
            UIStyle.EnsureEventSystem();
            Debug.unityLogger.Log(TAG, "PersistentHud canvas created.");
            return _instance;
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
```

- [ ] **Step 2: Import + verify .meta block**

Run `refresh_unity`. Unity generates `PersistentHud.cs.meta` with full `MonoImporter` block. Then `read_console` filtered to `Assets/Scripts` — expect zero errors.

If the generated `.cs.meta` lacks the `MonoImporter` block (only `fileFormatVersion` + `guid`), edit it to add the canonical block, **preserving the auto-generated GUID** (don't replace it):

```yaml
fileFormatVersion: 2
guid: <PRESERVE GENERATED VALUE>
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

`executionOrder: 0` is correct (no `[DefaultExecutionOrder]` on this class — lazy-create happens during whichever caller's Awake fires first).

---

### Task 2: Refactor `UIManager.cs`

**Files:**
- Modify: `Assets/Scripts/Core/UIManager.cs`

Drop the `[SerializeField] Button sceneSwitchButton;` + `[SerializeField] Text buttonLabel;` prefab-wiring fields and the `if (sceneSwitchButton == null || buttonLabel == null) BuildButton();` fallback branch — always build in code. Drop the `[RequireComponent(typeof(Canvas))]` attribute and the `_canvas` field. Replace `_canvas.enabled = inGameplay;` with the same `gameObject.SetActive` rule the UX-batch PR introduced for FlyScene.

- [ ] **Step 1: Replace the full file with the refactored version**

```csharp
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CubeFly.Core
{
    // The corner scene-switch button + per-scene visibility / labelling.
    // Lives in PersistentHud's canvas so it survives scene transitions
    // without its own canvas / DontDestroyOnLoad bookkeeping.
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        Button _sceneSwitchButton;
        Text _buttonLabel;

        const string BuildSceneName        = "BuildScene";
        const string FlySceneName          = "FlyScene";
        const string HangarSelectSceneName = "HangarSelect";
        const string TAG = "UIManager";

        // Self-bootstrap mirrors PauseMenu / GameOverMenu. UIBootstrap.cs
        // (and the UICanvas.prefab it instantiated) used to handle this;
        // both are deleted in this commit.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            GameObject go = new GameObject("UIManager");
            go.AddComponent<UIManager>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.unityLogger.LogWarning(TAG, "UIManager duplicate destroyed.");
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;

            BuildButton();

            _sceneSwitchButton.onClick.RemoveListener(SceneSwitcher.Toggle);
            _sceneSwitchButton.onClick.AddListener(SceneSwitcher.Toggle);

            Debug.unityLogger.Log(TAG, "UIManager initialised. Corner button live in PersistentHud.");
        }

        void OnDestroy()
        {
            if (Instance == this) SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void Start() => OnSceneStateChanged(SceneManager.GetActiveScene());

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            OnSceneStateChanged(scene);
            Debug.unityLogger.Log(TAG,
                $"Scene loaded: {scene.name}. Button label set to '{(_buttonLabel != null ? _buttonLabel.text : "<null>")}'");
        }

        // Per-scene visibility + label. The corner button only makes sense
        // on the BuildScene ("Fly!"); FlyScene uses the ESC pause menu's
        // Hangar button instead (UX batch 2026-05-20). MainMenu and
        // HangarSelect own their full screen and don't need it.
        // Reference HangarSelectSceneName so the intent stays greppable.
        void OnSceneStateChanged(Scene scene)
        {
            UpdateLabel(scene);
            _ = HangarSelectSceneName;

            if (_sceneSwitchButton != null)
            {
                _sceneSwitchButton.interactable = true;
                _sceneSwitchButton.gameObject.SetActive(scene.name == BuildSceneName);
            }
        }

        // Enable / disable the corner scene-switch button. BuildManager
        // calls this to gate the "Fly!" button while the construct
        // exceeds the active ship class's mass cap.
        public void SetSceneSwitchInteractable(bool interactable)
        {
            if (_sceneSwitchButton != null) _sceneSwitchButton.interactable = interactable;
        }

        void UpdateLabel(Scene scene)
        {
            if (_buttonLabel == null) return;
            _buttonLabel.text = scene.name == BuildSceneName ? "Fly!" : "Hangar";
        }

        // Build the corner button under PersistentHud's shared canvas.
        // PersistentHud.Instance triggers the canvas's lazy creation if
        // we're the first persistent UI script to Awake.
        void BuildButton()
        {
            (Button button, Text label) = UIStyle.BuildLabeledButton(
                PersistentHud.Instance.Root, "Fly!", new Vector2(220f, 64f), fontSize: 28);

            RectTransform brt = (RectTransform)button.transform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(1f, 1f);
            brt.anchoredPosition = new Vector2(-20f, -20f);

            _sceneSwitchButton = button;
            _buttonLabel = label;
        }
    }
}
```

**Note:** `UIManager` no longer requires `[RequireComponent(typeof(Canvas))]` — the canvas lives on `PersistentHud`. The script is now a plain MonoBehaviour that self-bootstraps like `PauseMenu` / `GameOverMenu`.

- [ ] **Step 2: Compile-check**

Run `refresh_unity`, then `read_console` filtered to `Assets/Scripts`. Expect zero errors.

---

### Task 3: Refactor `PauseMenu.cs`

**Files:**
- Modify: `Assets/Scripts/Core/PauseMenu.cs` (only `BuildUI` and adjacent helpers — keep all behaviour code intact)

Drop the per-script canvas creation and the `_root.transform.SetParent(transform, worldPositionStays: false)` reparenting workaround. Build the panel directly under `PersistentHud.Instance.Root`.

- [ ] **Step 1: Replace the `BuildUI` method body**

Replace the existing `BuildUI` (lines 211-255 of the file pre-change):

```csharp
void BuildUI()
{
    // Build the pause panel directly under the shared persistent canvas.
    // The full-screen dim is the panel root; it inherits PersistentHud's
    // DontDestroyOnLoad and sortingOrder (200) for free — no canvas, no
    // reparenting trick needed.
    GameObject panelGO = new GameObject("PauseMenuPanel",
        typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
    panelGO.transform.SetParent(PersistentHud.Instance.Root, false);
    int uiLayer = LayerMask.NameToLayer("UI");
    if (uiLayer >= 0) panelGO.layer = uiLayer;
    _root = panelGO;

    RectTransform root = (RectTransform)panelGO.transform;
    root.anchorMin = Vector2.zero;
    root.anchorMax = Vector2.one;
    root.offsetMin = Vector2.zero;
    root.offsetMax = Vector2.zero;

    // The panel's own Image is the full-screen dim — also catches
    // clicks so nothing under the overlay can be reached.
    Image bgImage = panelGO.GetComponent<Image>();
    bgImage.color = new Color(0f, 0f, 0f, 0.70f);
    bgImage.raycastTarget = true;

    // Title.
    Text title = UIStyle.BuildLabel(root, "Paused", fontSize: 96, style: FontStyle.Bold);
    RectTransform trt = (RectTransform)title.transform;
    trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
    trt.sizeDelta = new Vector2(800f, 160f);
    trt.anchoredPosition = new Vector2(0f, 140f);

    // Buttons stacked below the title. Same dimensions as the
    // MainMenu buttons to keep the visual language consistent.
    // Hangar is created first (top of the stack) but is only
    // visible in FlyScene — see OnSceneLoaded.
    _hangarButton = CreateButton(root, "Hangar",          new Vector2(0f, 100f),  OnHangarClicked);
                    CreateButton(root, "Menu",            new Vector2(0f, 0f),    OnMenuClicked);
                    CreateButton(root, "Back to Desktop", new Vector2(0f, -100f), OnExitClicked);
}
```

- [ ] **Step 2: Remove the now-redundant `UIStyle.EnsureEventSystem()` call from `Awake`**

In `Awake`, delete the line:

```csharp
UIStyle.EnsureEventSystem();
```

`PersistentHud.Instance` (called inside `BuildUI`) already calls `EnsureEventSystem` from `Create()`; the second call would be a no-op but is dead code.

- [ ] **Step 3: Verify `ShowUI` / `HideUI` still work**

No changes needed — `_root.SetActive(true/false)` still toggles the panel GameObject (now a child of `PersistentHud.Instance.Root` instead of its own canvas root). Same semantics.

- [ ] **Step 4: Compile-check**

Run `refresh_unity`, then `read_console`. Expect zero errors.

---

### Task 4: Refactor `GameOverMenu.cs`

**Files:**
- Modify: `Assets/Scripts/Core/GameOverMenu.cs` (same shape as Task 3 — only `BuildUI` + drop `EnsureEventSystem` call in Awake)

- [ ] **Step 1: Replace the `BuildUI` method body**

```csharp
void BuildUI()
{
    // Build the overlay directly under the shared persistent canvas.
    // Same pattern as PauseMenu — panel-as-Image at full-screen with
    // raycastTarget true.
    GameObject panelGO = new GameObject("GameOverMenuPanel",
        typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
    panelGO.transform.SetParent(PersistentHud.Instance.Root, false);
    int uiLayer = LayerMask.NameToLayer("UI");
    if (uiLayer >= 0) panelGO.layer = uiLayer;
    _root = panelGO;

    RectTransform root = (RectTransform)panelGO.transform;
    root.anchorMin = Vector2.zero;
    root.anchorMax = Vector2.one;
    root.offsetMin = Vector2.zero;
    root.offsetMax = Vector2.zero;

    // Slightly darker / redder than PauseMenu's dim to signal mood.
    Image bgImage = panelGO.GetComponent<Image>();
    bgImage.color = new Color(0.15f, 0.04f, 0.04f, 0.85f);
    bgImage.raycastTarget = true;

    // Title.
    Text title = UIStyle.BuildLabel(root, "Construct Destroyed",
        fontSize: 84, style: FontStyle.Bold);
    RectTransform trt = (RectTransform)title.transform;
    trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
    trt.sizeDelta = new Vector2(900f, 160f);
    trt.anchoredPosition = new Vector2(0f, 100f);

    // One button, centred below the title.
    (Button button, Text _) = UIStyle.BuildLabeledButton(
        root, "Return to main menu", new Vector2(420f, 80f), fontSize: 32);
    RectTransform rt = (RectTransform)button.transform;
    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
    rt.anchoredPosition = new Vector2(0f, -60f);
    button.onClick.AddListener(OnMainMenuClicked);
}
```

- [ ] **Step 2: Remove the `UIStyle.EnsureEventSystem()` call from `Awake`**

Delete the line from `Awake` (same rationale as PauseMenu).

- [ ] **Step 3: Compile-check**

Run `refresh_unity`, then `read_console`. Expect zero errors.

---

### Task 5: Delete `UIBootstrap.cs` and the two `Assets/UI/` prefabs

**Files:**
- Delete: `Assets/Scripts/Core/UIBootstrap.cs` + `.cs.meta`
- Delete: `Assets/UI/UIBootstrap.prefab` + `.prefab.meta`
- Delete: `Assets/UI/UICanvas.prefab` + `.prefab.meta`

`UIBootstrap.cs` instantiates `UICanvas.prefab` per scene to spawn a `UIManager`. After Task 2, `UIManager` self-bootstraps before scene load via `[RuntimeInitializeOnLoadMethod]`, so both files are dead code. `UIBootstrap.prefab` is a scene-prefab variant of the bootstrap GameObject — also dead.

- [ ] **Step 1: Delete the four files + their .meta files**

```bash
cd "/Users/anon/My project"
rm Assets/Scripts/Core/UIBootstrap.cs Assets/Scripts/Core/UIBootstrap.cs.meta
rm Assets/UI/UIBootstrap.prefab Assets/UI/UIBootstrap.prefab.meta
rm Assets/UI/UICanvas.prefab Assets/UI/UICanvas.prefab.meta
```

- [ ] **Step 2: Verify `Assets/UI/` is empty (then delete the directory if so)**

```bash
ls Assets/UI/
```

If the directory contains no remaining files, also delete it (+ its `.meta`):

```bash
ls Assets/UI.meta && rm -r Assets/UI Assets/UI.meta
```

If it still has other content, leave it alone — the spec only marks the two prefabs for deletion.

- [ ] **Step 3: Compile-check**

Run `refresh_unity`, then `read_console`. Expect zero errors. Unity may log a warning about missing prefab references in the scenes — Task 6 cleans those up.

---

### Task 6: Remove `UIBootstrap` GameObject from `FlyScene.unity` + `BuildScene.unity`

**Files:**
- Modify: `Assets/Scenes/FlyScene.unity` (delete `UIBootstrap` GameObject block at `fileID 100000` + its `Transform` `100001` + `MonoBehaviour` `100002`)
- Modify: `Assets/Scenes/BuildScene.unity` (same — `UIBootstrap` is at `fileID 100000` here too)

The GameObject is three YAML records: GameObject (`100000`), Transform (`100001`), MonoBehaviour (`100002`). All three must go.

- [ ] **Step 1: Verify the exact line ranges in each scene**

```bash
cd "/Users/anon/My project"
grep -n '^--- !u!1 &100000\|^--- !u!4 &100001\|^--- !u!114 &100002\|^--- !u!1 &200000' Assets/Scenes/FlyScene.unity Assets/Scenes/BuildScene.unity
```

Expected: each scene shows `100000` at one line, `100001` next, `100002` after, then `200000` (the start of the next GameObject — e.g. `CubeConstruct` in FlyScene, `BuildManager` in BuildScene).

- [ ] **Step 2: Delete the three records via the Unity MCP**

Use the Unity MCP to load each scene and destroy the `UIBootstrap` GameObject — this is safer than hand-editing YAML (Unity will also remove the SceneRoots reference).

```python
# For FlyScene:
manage_scene(action="open", path="Assets/Scenes/FlyScene.unity")
found = find_gameobjects(search_term="UIBootstrap", search_method="by_name")
# Use the returned instance_id:
manage_gameobject(action="delete", target=<instance_id>)
manage_scene(action="save")

# Repeat for BuildScene:
manage_scene(action="open", path="Assets/Scenes/BuildScene.unity")
found = find_gameobjects(search_term="UIBootstrap", search_method="by_name")
manage_gameobject(action="delete", target=<instance_id>)
manage_scene(action="save")
```

- [ ] **Step 3: Verify both scenes no longer reference `UIBootstrap`**

```bash
grep -Hn "UIBootstrap" Assets/Scenes/FlyScene.unity Assets/Scenes/BuildScene.unity
```

Expected: zero matches.

- [ ] **Step 4: Compile-check**

Run `refresh_unity`, then `read_console`. Expect zero errors and no missing-script warnings (the dangling script GUID `a0a0a0a0000000090000000000000009` no longer appears anywhere).

---

### Task 7: Commit 1

- [ ] **Step 1: Stage + commit**

```bash
cd "/Users/anon/My project"
git add Assets/Scripts/Core/PersistentHud.cs Assets/Scripts/Core/PersistentHud.cs.meta \
        Assets/Scripts/Core/UIManager.cs \
        Assets/Scripts/Core/PauseMenu.cs \
        Assets/Scripts/Core/GameOverMenu.cs \
        Assets/Scenes/FlyScene.unity \
        Assets/Scenes/BuildScene.unity
git rm Assets/Scripts/Core/UIBootstrap.cs Assets/Scripts/Core/UIBootstrap.cs.meta \
       Assets/UI/UIBootstrap.prefab Assets/UI/UIBootstrap.prefab.meta \
       Assets/UI/UICanvas.prefab Assets/UI/UICanvas.prefab.meta
# If Assets/UI/ was empty and deleted in Task 5 Step 2, also:
# git rm Assets/UI.meta

git commit -m "$(cat <<'EOF'
Add PersistentHud + refactor persistent UI scripts onto it

UIManager, PauseMenu, and GameOverMenu each used to build their own
ScreenSpaceOverlay canvas with hand-coordinated sortingOrders (100 /
300 / 400) and a manual SetParent reparenting trick to inherit
DontDestroyOnLoad. They now build their UI tree directly under
PersistentHud.Instance.Root — a single DDOL canvas at sortingOrder
200, lazy-created from whichever of the three scripts Awakes first.

UIBootstrap.cs + the two Assets/UI/ prefabs (UIBootstrap.prefab,
UICanvas.prefab) are deleted: UIManager now self-bootstraps via the
same BeforeSceneLoad pattern as PauseMenu / GameOverMenu, so there's
nothing left for UIBootstrap to instantiate. Both gameplay scenes
have the UIBootstrap GameObject removed.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Commit 2 — `FlyHud` + Fly HUD refactor

Adds `FlyHud`, refactors the five Fly HUD scripts (`FlyCrosshair`, `FlySpeedIndicator`, `FlyHpIndicator`, `FlyBoostBar`, `FlyWeaponToolbarController`) to attach to it, adds the `FlyHUD` GameObject to `FlyScene.unity`.

### Task 8: Create `FlyHud.cs`

**Files:**
- Create: `Assets/Scripts/Fly/FlyHud.cs` (+ `.cs.meta`)

- [ ] **Step 1: Write `FlyHud.cs`**

```csharp
using CubeFly.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CubeFly.Fly
{
    // Scene-attached shared canvas for every FlyScene HUD element —
    // the crosshair, boost bar, weapon toolbar, speed and HP labels.
    //
    // Lifetime: scene-attached, NOT DDOL — destroyed on FlyScene unload,
    // recreated on the next FlyScene load. Same singleton pattern as
    // FlyController and BuildManager.
    //
    // [DefaultExecutionOrder(-500)] forces our Awake to run before the
    // Fly HUD scripts (which run at default order 0, or +100 for the
    // crosshair / HP indicator), so their BuildUI calls find
    // FlyHud.Instance.Root already populated.
    //
    // The scene file ships a bare GameObject with this script attached;
    // Awake adds the Canvas + GraphicRaycaster + CanvasScaler in code
    // (matching the project's build-UI-in-code pattern). This keeps the
    // scene YAML minimal — no scattered Inspector-tweaked component
    // values to drift out of sync with the code.
    //
    // sortingOrder 100 — sits below the persistent UI canvas (200) so
    // pause / game-over overlays visually cover the scene HUD.
    [DefaultExecutionOrder(-500)]
    public class FlyHud : MonoBehaviour
    {
        public static FlyHud Instance { get; private set; }
        public RectTransform Root => (RectTransform)transform;

        const string TAG = "FlyHud";

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) gameObject.layer = uiLayer;

            // Adding Canvas to a GameObject with only Transform causes
            // Unity to auto-replace Transform with RectTransform.
            Canvas canvas = GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();

            CanvasScaler scaler = GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            UIStyle.EnsureEventSystem();
            Debug.unityLogger.Log(TAG, "FlyHud canvas ready.");
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
```

- [ ] **Step 2: Import + verify .meta**

Run `refresh_unity`, `read_console` filtered to `Assets/Scripts`. Expect zero errors. Verify `Assets/Scripts/Fly/FlyHud.cs.meta` has the full `MonoImporter` block; if it doesn't, edit it to add the canonical block (`executionOrder: -500` is correct here since the class has `[DefaultExecutionOrder(-500)]` — Unity should pick this up on import, but verify and set it manually if needed):

```yaml
fileFormatVersion: 2
guid: <PRESERVE GENERATED VALUE>
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: -500
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:
```

---

### Task 9: Refactor `FlyCrosshair.cs`

**Files:**
- Modify: `Assets/Scripts/Fly/FlyCrosshair.cs` (delete `_canvas` field + canvas-build steps in `BuildUI`)

- [ ] **Step 1: Drop the `Canvas _canvas;` field**

Delete the line:

```csharp
Canvas _canvas;
```

- [ ] **Step 2: Replace the canvas-build prologue in `BuildUI`**

In `BuildUI`, replace these lines:

```csharp
UIStyle.EnsureEventSystem();
_canvas = UIStyle.BuildScreenSpaceCanvas("FlyCrosshairCanvas", sortingOrder: 110);
RectTransform canvasRoot = (RectTransform)_canvas.transform;
```

with:

```csharp
RectTransform canvasRoot = FlyHud.Instance.Root;
```

The rest of `BuildUI` (the `CrosshairRoot` + center dot + four arms) is unchanged — they're already built as children of `canvasRoot`.

- [ ] **Step 3: Compile-check**

Run `refresh_unity`, `read_console`. Expect zero errors.

---

### Task 10: Refactor `FlySpeedIndicator.cs`

**Files:**
- Modify: `Assets/Scripts/Fly/FlySpeedIndicator.cs`

- [ ] **Step 1: Drop the `Canvas _canvas;` field**

Delete:

```csharp
Canvas _canvas;
```

- [ ] **Step 2: Replace the canvas-build prologue in `BuildUI`**

Replace:

```csharp
UIStyle.EnsureEventSystem();
// sortingOrder 130 sits between FlyWeaponToolbar (120) and
// FlyCrosshair (110). Doesn't matter much because the label
// sits in a corner with no other UI overlap.
_canvas = UIStyle.BuildScreenSpaceCanvas("FlySpeedCanvas", sortingOrder: 130);
RectTransform canvasRoot = (RectTransform)_canvas.transform;
```

with:

```csharp
RectTransform canvasRoot = FlyHud.Instance.Root;
```

The rest of `BuildUI` (the bottom-left label setup) is unchanged.

- [ ] **Step 3: Compile-check**

Run `refresh_unity`, `read_console`. Expect zero errors.

---

### Task 11: Refactor `FlyHpIndicator.cs`

**Files:**
- Modify: `Assets/Scripts/Fly/FlyHpIndicator.cs`

- [ ] **Step 1: Drop the `Canvas _canvas;` field**

Delete:

```csharp
Canvas _canvas;
```

- [ ] **Step 2: Replace the canvas-build prologue in `BuildUI`**

Replace:

```csharp
UIStyle.EnsureEventSystem();
_canvas = UIStyle.BuildScreenSpaceCanvas("FlyHpCanvas", sortingOrder: 130);
RectTransform canvasRoot = (RectTransform)_canvas.transform;
```

with:

```csharp
RectTransform canvasRoot = FlyHud.Instance.Root;
```

The rest of `BuildUI` unchanged.

- [ ] **Step 3: Compile-check**

Run `refresh_unity`, `read_console`. Expect zero errors.

---

### Task 12: Refactor `FlyBoostBar.cs`

**Files:**
- Modify: `Assets/Scripts/Fly/FlyBoostBar.cs`

- [ ] **Step 1: Drop the `Canvas _canvas;` field**

Delete:

```csharp
Canvas _canvas;
```

- [ ] **Step 2: Replace the canvas-build prologue in `BuildUI`**

Replace:

```csharp
UIStyle.EnsureEventSystem();
// sortingOrder 115 sits just above FlyCrosshair (110) and
// below FlyWeaponToolbar (120) — the bar reads on top of the
// reticle without occluding the toolbar.
_canvas = UIStyle.BuildScreenSpaceCanvas("FlyBoostBarCanvas", sortingOrder: 115);
RectTransform canvasRoot = (RectTransform)_canvas.transform;
int uiLayer = LayerMask.NameToLayer("UI");
```

with:

```csharp
RectTransform canvasRoot = FlyHud.Instance.Root;
int uiLayer = LayerMask.NameToLayer("UI");
```

The rest of `BuildUI` (frame + fill + flash label) unchanged. Sibling order within `FlyHud.Instance.Root` determines depth among Fly HUD elements — the boost bar is built before the weapon toolbar so the toolbar's button row renders on top of the bar if they ever overlap. (They don't in practice, but documenting the rule.)

- [ ] **Step 3: Compile-check**

Run `refresh_unity`, `read_console`. Expect zero errors.

---

### Task 13: Refactor `FlyWeaponToolbarController.cs`

**Files:**
- Modify: `Assets/Scripts/Fly/FlyWeaponToolbarController.cs`

This is the most complex Fly HUD refactor: the script destroys/rebuilds button children of `_canvasRoot` on every `TypesChanged` event, and gates the whole canvas's visibility via `_canvas.gameObject.SetActive(false)` when no weapons exist. We replace the canvas with a single dedicated "container" GameObject child of `FlyHud.Instance.Root`, so the same Active toggle and child-clear pattern works.

- [ ] **Step 1: Replace the `Canvas _canvas;` + `RectTransform _canvasRoot;` fields with a single container ref**

Replace these two lines:

```csharp
Canvas _canvas;
RectTransform _canvasRoot;
```

with:

```csharp
// Child of FlyHud.Instance.Root that owns every toolbar button + reload
// bar. RebuildButtons destroys + re-creates this container's children
// when TypesChanged fires; HideContainer / ShowContainer toggle its
// active state when the construct has no weapons.
RectTransform _container;
```

- [ ] **Step 2: Replace `BuildCanvas` with `BuildContainer`**

Replace the existing method:

```csharp
void BuildCanvas()
{
    UIStyle.EnsureEventSystem();
    _canvas = UIStyle.BuildScreenSpaceCanvas("FlyWeaponToolbarCanvas", sortingOrder: 120);
    _canvasRoot = (RectTransform)_canvas.transform;
    HideCanvas();
}

void HideCanvas()
{
    if (_canvas != null) _canvas.gameObject.SetActive(false);
}

void ShowCanvas()
{
    if (_canvas != null) _canvas.gameObject.SetActive(true);
}
```

with:

```csharp
void BuildContainer()
{
    GameObject go = new GameObject("FlyWeaponToolbar", typeof(RectTransform));
    go.transform.SetParent(FlyHud.Instance.Root, false);
    int uiLayer = LayerMask.NameToLayer("UI");
    if (uiLayer >= 0) go.layer = uiLayer;
    _container = (RectTransform)go.transform;
    // Stretch to fill the parent canvas — the per-button anchored
    // positions (bottom-centre pivot, `bottomMargin` offset) target the
    // canvas's bottom edge, so the container needs to match the
    // canvas's full rect.
    _container.anchorMin = Vector2.zero;
    _container.anchorMax = Vector2.one;
    _container.offsetMin = Vector2.zero;
    _container.offsetMax = Vector2.zero;
    HideContainer();
}

void HideContainer()
{
    if (_container != null) _container.gameObject.SetActive(false);
}

void ShowContainer()
{
    if (_container != null) _container.gameObject.SetActive(true);
}
```

- [ ] **Step 3: Update `Start` to call `BuildContainer`**

In `Start`, replace `BuildCanvas();` with `BuildContainer();`. Replace the fallback line:

```csharp
HideCanvas();
```

with:

```csharp
HideContainer();
```

- [ ] **Step 4: Update `RebuildButtons` to use `_container` instead of `_canvasRoot`**

In `RebuildButtons`, replace every reference to `_canvasRoot` with `_container`. Specifically (5 references):

```csharp
// 1. Null guard:
if (shootingController == null || _container == null) return;

// 2. Destroy prior children:
for (int i = _container.childCount - 1; i >= 0; i--)
    Destroy(_container.GetChild(i).gameObject);

// 3. Empty-toolbar path:
HideContainer();
return;
// ...
ShowContainer();

// 4. Button parent in the per-type loop:
(Button btn, Text _) = UIStyle.BuildLabeledButton(_container, label, buttonSize, fontSize);

// 5. Reload bar parent:
BuildReloadRect(_container, "ReloadBarBg" + i, ...);
_reloadBars[i] = BuildReloadRect(_container, "ReloadBarFg" + i, ...);
```

All `_canvasRoot` references must become `_container` — that's the entire migration. The `HideCanvas` / `ShowCanvas` calls become `HideContainer` / `ShowContainer`.

- [ ] **Step 5: Compile-check**

Run `refresh_unity`, `read_console`. Expect zero errors.

---

### Task 14: Add `FlyHUD` GameObject to `FlyScene.unity`

**Files:**
- Modify: `Assets/Scenes/FlyScene.unity` (add `FlyHUD` GameObject with `FlyHud` component attached)

Use the Unity MCP to create the GameObject — safer than hand-writing scene YAML.

- [ ] **Step 1: Open the scene and create the HUD GameObject**

```python
manage_scene(action="open", path="Assets/Scenes/FlyScene.unity")
# Create the empty GameObject (just Transform — FlyHud.Awake adds the
# Canvas / GraphicRaycaster / CanvasScaler in code).
manage_gameobject(action="create", name="FlyHUD")
# Attach the FlyHud component.
manage_gameobject(action="modify", target="FlyHUD",
    components_to_add=["CubeFly.Fly.FlyHud"])
manage_scene(action="save")
```

- [ ] **Step 2: Verify**

```bash
grep -n "FlyHUD\|FlyHud" Assets/Scenes/FlyScene.unity | head
```

Expected: one `m_Name: FlyHUD` line and one MonoBehaviour record whose script GUID matches `Assets/Scripts/Fly/FlyHud.cs.meta`.

- [ ] **Step 3: Compile-check + play-mode check**

Run `refresh_unity`, `read_console`. Expect zero errors. Optional: open FlyScene and enter Play mode briefly — the crosshair / boost bar / speed / HP / weapon toolbar should all render exactly as before.

---

### Task 15: Commit 2

- [ ] **Step 1: Stage + commit**

```bash
cd "/Users/anon/My project"
git add Assets/Scripts/Fly/FlyHud.cs Assets/Scripts/Fly/FlyHud.cs.meta \
        Assets/Scripts/Fly/FlyCrosshair.cs \
        Assets/Scripts/Fly/FlySpeedIndicator.cs \
        Assets/Scripts/Fly/FlyHpIndicator.cs \
        Assets/Scripts/Fly/FlyBoostBar.cs \
        Assets/Scripts/Fly/FlyWeaponToolbarController.cs \
        Assets/Scenes/FlyScene.unity

git commit -m "$(cat <<'EOF'
Add FlyHud + refactor Fly HUD scripts onto it

FlyCrosshair, FlySpeedIndicator, FlyHpIndicator, FlyBoostBar, and
FlyWeaponToolbarController each used to build their own
ScreenSpaceOverlay canvas with hand-coordinated sortingOrders
(110 / 130 / 130 / 115 / 120). They now build their UI tree directly
under FlyHud.Instance.Root — one canvas at sortingOrder 100, hosted
on a new FlyHUD GameObject in FlyScene.

FlyWeaponToolbarController keeps its destroy-children-on-rebuild
pattern but now operates on a dedicated `_container` child of
FlyHud's canvas (so the toolbar can SetActive(false) without
disabling the entire HUD canvas).

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Commit 3 — `BuildHud` + Build HUD refactor

Adds `BuildHud`, refactors the two Build HUD scripts (`BuildToolbarController`, `BuildShipClassController`) to attach to it, adds the `BuildHUD` GameObject to `BuildScene.unity`.

### Task 16: Create `BuildHud.cs`

**Files:**
- Create: `Assets/Scripts/Build/BuildHud.cs` (+ `.cs.meta`)

- [ ] **Step 1: Write `BuildHud.cs`**

```csharp
using CubeFly.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CubeFly.Build
{
    // Scene-attached shared canvas for every BuildScene HUD element —
    // the build toolbar (shape buttons, material flyout, category
    // flyouts, Delete button, stat labels), the ship-class dropdown,
    // and any future BuildScene-only UI.
    //
    // Lifetime: scene-attached, NOT DDOL — destroyed on BuildScene
    // unload, recreated on the next BuildScene load.
    //
    // [DefaultExecutionOrder(-500)] forces Awake to run before the
    // Build HUD scripts (default order 0), so their BuildUI / BuildToolbar
    // calls find BuildHud.Instance.Root already populated.
    //
    // sortingOrder 100 — sits below the persistent UI canvas (200) so
    // pause overlays cover the build HUD. FlyHud uses the same 100 in
    // FlyScene; the two never coexist so they don't fight.
    [DefaultExecutionOrder(-500)]
    public class BuildHud : MonoBehaviour
    {
        public static BuildHud Instance { get; private set; }
        public RectTransform Root => (RectTransform)transform;

        const string TAG = "BuildHud";

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) gameObject.layer = uiLayer;

            // Adding Canvas to a GameObject with only Transform causes
            // Unity to auto-replace Transform with RectTransform.
            Canvas canvas = GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();

            CanvasScaler scaler = GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            UIStyle.EnsureEventSystem();
            Debug.unityLogger.Log(TAG, "BuildHud canvas ready.");
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
```

- [ ] **Step 2: Import + verify .meta**

Run `refresh_unity`, `read_console`. Expect zero errors. Verify `Assets/Scripts/Build/BuildHud.cs.meta` has the full `MonoImporter` block with `executionOrder: -500`.

---

### Task 17: Refactor `BuildToolbarController.cs`

**Files:**
- Modify: `Assets/Scripts/Build/BuildToolbarController.cs`

The script builds a single canvas in `BuildToolbar()` and stores the canvas's `RectTransform` in `_canvasRect` (used as the parent for every toolbar element). We replace the canvas-creation prologue with a `FlyHud`-style root lookup.

- [ ] **Step 1: Replace the canvas-build prologue in `BuildToolbar`**

Replace these lines at the top of `BuildToolbar()`:

```csharp
UIStyle.EnsureEventSystem();
// Sit just under the persistent corner-button canvas (sortingOrder
// 100). Both share the screen but never overlap visually.
Canvas canvas = UIStyle.BuildScreenSpaceCanvas("BuildToolbarCanvas", sortingOrder: 90);
RectTransform root = (RectTransform)canvas.transform;
_canvasRect = root;
```

with:

```csharp
RectTransform root = BuildHud.Instance.Root;
_canvasRect = root;
```

The rest of `BuildToolbar` (hint label, floating message, shape buttons, category flyouts, Delete button, stat labels, material flyout via `BuildFlyout(root)`) is unchanged — every UI child is built under `root`, which now points at `BuildHud`'s shared canvas instead of a private one.

- [ ] **Step 2: Compile-check**

Run `refresh_unity`, `read_console`. Expect zero errors.

---

### Task 18: Refactor `BuildShipClassController.cs`

**Files:**
- Modify: `Assets/Scripts/Build/BuildShipClassController.cs`

- [ ] **Step 1: Drop the `Canvas _canvas;` field**

Delete:

```csharp
Canvas _canvas;
```

- [ ] **Step 2: Replace the canvas-build prologue in `BuildUI`**

Replace:

```csharp
UIStyle.EnsureEventSystem();
// sortingOrder 95: above the build toolbar (90), below the
// persistent corner UI (100). The Dropdown bumps its own
// sorting when the option list opens.
_canvas = UIStyle.BuildScreenSpaceCanvas("BuildShipClassCanvas", sortingOrder: 95);
RectTransform root = (RectTransform)_canvas.transform;
```

with:

```csharp
RectTransform root = BuildHud.Instance.Root;
```

The rest of `BuildUI` (label + dropdown layout) is unchanged.

**Sibling order note:** Inside `BuildHud.Instance.Root`, the ship class dropdown is built in this script's `Awake` and the toolbar (shape buttons + category flyouts) is built in `BuildToolbarController.Start`. Since `Awake` runs before `Start`, the dropdown is a sibling earlier in the list — meaning the toolbar renders on top of the dropdown if they ever overlap visually. They don't (dropdown is middle-left, toolbar is bottom-centre), but the rule preserves the previous depth (95 < 90 was inverted — keep the visual reading where the toolbar wins overlap conflicts, which is what the previous sortingOrder values _did not_ produce, but since they don't overlap the swap is invisible).

- [ ] **Step 3: Compile-check**

Run `refresh_unity`, `read_console`. Expect zero errors.

---

### Task 19: Add `BuildHUD` GameObject to `BuildScene.unity`

**Files:**
- Modify: `Assets/Scenes/BuildScene.unity` (add `BuildHUD` GameObject with `BuildHud` component)

- [ ] **Step 1: Open the scene and create the HUD GameObject**

```python
manage_scene(action="open", path="Assets/Scenes/BuildScene.unity")
manage_gameobject(action="create", name="BuildHUD")
manage_gameobject(action="modify", target="BuildHUD",
    components_to_add=["CubeFly.Build.BuildHud"])
manage_scene(action="save")
```

- [ ] **Step 2: Verify**

```bash
grep -n "BuildHUD\|BuildHud" Assets/Scenes/BuildScene.unity | head
```

Expected: one `m_Name: BuildHUD` line and one MonoBehaviour record whose script GUID matches `Assets/Scripts/Build/BuildHud.cs.meta`.

- [ ] **Step 3: Compile-check + play-mode check**

Run `refresh_unity`, `read_console`. Expect zero errors. Optional: open BuildScene briefly — the toolbar / class dropdown / stat labels / corner Fly! button should all render exactly as before.

---

### Task 20: Commit 3

- [ ] **Step 1: Stage + commit**

```bash
cd "/Users/anon/My project"
git add Assets/Scripts/Build/BuildHud.cs Assets/Scripts/Build/BuildHud.cs.meta \
        Assets/Scripts/Build/BuildToolbarController.cs \
        Assets/Scripts/Build/BuildShipClassController.cs \
        Assets/Scenes/BuildScene.unity

git commit -m "$(cat <<'EOF'
Add BuildHud + refactor Build HUD scripts onto it

BuildToolbarController and BuildShipClassController each used to
build their own ScreenSpaceOverlay canvas (sortingOrder 90 / 95).
They now build their UI tree directly under BuildHud.Instance.Root —
one canvas at sortingOrder 100, hosted on a new BuildHUD GameObject
in BuildScene.

Three commits land the full F8 consolidation: 10+ self-bootstrapped
runtime canvases → 3 shared HUD roots (PersistentHud / FlyHud /
BuildHud). Future HUD additions (shield bar, laser heat bar) attach
to FooHud.Instance.Root with no canvas bookkeeping.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 21: Push branch, open PR, request Copilot review

- [ ] **Step 1: Push the branch and open the PR**

```bash
cd "/Users/anon/My project"
git push -u origin feat/hud-consolidation
gh pr create --title "HUD / canvas consolidation (F8)" --body "$(cat <<'EOF'
## Summary

- Collapses 10+ self-bootstrapped runtime canvases into three shared HUD roots: `PersistentHud` (DDOL, sortingOrder 200), `FlyHud` (FlyScene-attached, sortingOrder 100, `[DefaultExecutionOrder(-500)]`), `BuildHud` (BuildScene-attached, sortingOrder 100, `[DefaultExecutionOrder(-500)]`).
- Refactors every existing HUD script (UIManager, PauseMenu, GameOverMenu, FlyCrosshair, FlySpeedIndicator, FlyHpIndicator, FlyBoostBar, FlyWeaponToolbarController, BuildToolbarController, BuildShipClassController) to attach to the relevant shared root instead of authoring its own canvas.
- Deletes `UIBootstrap.cs` + the two `Assets/UI/` prefabs (`UIBootstrap.prefab`, `UICanvas.prefab`); `UIManager` now self-bootstraps via the same `BeforeSceneLoad` pattern as `PauseMenu` / `GameOverMenu`.
- Removes the `UIBootstrap` GameObject from `FlyScene.unity` + `BuildScene.unity`; adds `FlyHUD` to FlyScene and `BuildHUD` to BuildScene.

Three commits in dependency order — each compiles independently. Behaviour is unchanged: visible difference is internal (fewer Unity canvases, fewer GraphicRaycasters, one place to reason about sorting and scaling).

Spec: `docs/superpowers/specs/2026-05-20-hud-consolidation-design.md`

## Test plan

- [ ] Compile clean per commit (no `Assets/Scripts` errors / warnings)
- [ ] BuildScene: corner **Fly!** button visible top-right; class dropdown switches ship class; toolbar shape buttons + material flyouts work (with the 3-s away-timer and right-side swatches from PR #40); Mass / HP / Selected stat labels populate bottom-left
- [ ] FlyScene: crosshair tracks construct.forward; boost bar fills / throbs in critical zone / shows Overboosted! flash; weapon toolbar shows reload bars (width-based fill) + partial / fully-dead marks; speed and HP labels populate bottom-left; corner button hidden
- [ ] Persistent UI survives scene transitions: PauseMenu opens on ESC in either gameplay scene; Hangar button in PauseMenu routes back to BuildScene from FlyScene; Construct Destroyed overlay appears when alpha cube hits 0 HP
- [ ] Switch scenes a few times (MainMenu → HangarSelect → BuildScene → FlyScene → Hangar back → ESC → Menu): everything survives without missing canvases or duplicate UI

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 2: Request Copilot review**

```bash
PR_NUM=$(gh pr view --json number -q .number)
gh api repos/entewurzelauskuh/CosmicScrapClub/pulls/$PR_NUM/requested_reviewers \
    -X POST -f "reviewers[]=copilot-pull-request-reviewer[bot]" 2>/dev/null || true
gh pr view --web
```

- [ ] **Step 3: Manual play-test (the user runs this on their machine)**

The user opens Unity Editor on the main project root (NOT the worktree per `feedback_unity_worktree_verification.md`), checks out `feat/hud-consolidation` there, and runs through the test plan above. Wait for the user's verdict before merging.

---

## Self-review

Before declaring the plan complete, verify:

- **Spec coverage:** every section of the design spec is implemented:
  - 3 new components (Tasks 1, 8, 16) ✓
  - 10 file refactors (Tasks 2, 3, 4, 9, 10, 11, 12, 13, 17, 18) ✓
  - Scene mods (Tasks 6, 14, 19) ✓
  - File deletions (Task 5) ✓
  - Three-commit delivery (Tasks 7, 15, 20) ✓
  - PR + Copilot review (Task 21) ✓
- **No placeholders:** every code block is concrete; no "TODO / TBD".
- **Type consistency:** `PersistentHud.Instance.Root`, `FlyHud.Instance.Root`, `BuildHud.Instance.Root` are all `RectTransform` — call sites use them uniformly.
- **Compile checkpoints:** every Task ends with `refresh_unity` + `read_console`; every Commit is followed by a separate compile-check Task in the next Commit's first task (or for the last Commit, the manual play-test).
- **Out-of-scope items left alone:** MainMenu / HangarSelect canvases unchanged; `[DefaultExecutionOrder(-1000)]` on PauseMenu / GameOverMenu preserved; UIStyle helper API unchanged.
