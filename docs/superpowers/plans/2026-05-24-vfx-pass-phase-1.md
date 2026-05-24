# VFX Pass — Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. (User has pre-selected inline execution; do NOT prompt for execution mode at the end.)

**Goal:** Land Phase 1 of the VFX pass on branch `feat/vfx-phase-1`: five URP Volume overrides on the main game profile, plus the seventh "Debug" tab in `SettingsMenu` (PR #46) with per-effect toggles backed by PlayerPrefs, plus a reusable Tooltip helper.

**Architecture:** Static `VfxSettings` facade wraps PlayerPrefs and fires a `Changed` event. DDOL `VfxApplier` singleton subscribes to `SceneManager.sceneLoaded` + `VfxSettings.Changed` and toggles each override's `.active` field on the active scene's Volume profile. Settings menu's existing tab loop gets a 7th name + a non-"Coming soon" content panel. A small `TooltipHud` + `TooltipTrigger` pair lands as a reusable helper. The five Volume overrides are applied to `DefaultVolumeProfile.asset` via a one-shot `[MenuItem]` Editor installer script invoked through Unity MCP — clean Unity API, no fragile hand-written YAML, idempotent for re-runs.

**Tech Stack:** Unity 6.3 LTS / URP 17.3, MonoBehaviour C#, New Input System (`UnityEngine.InputSystem.Mouse.current`), legacy `UnityEngine.UI.Text` + `LegacyRuntime.ttf` (no TMP). No automated tests — verification per task is **Unity MCP compile-check**: edits/creates auto-trigger import + compile, then poll `ReadMcpResourceTool(server="UnityMCP", uri="mcpforunity://editor/state")` until `is_compiling == false`, then `mcp__unityMCP__read_console(types=["error"], count=20, filter_text="Assets/Scripts")` expecting zero errors. User manual play-test at the end of all tasks.

**Spec reference:** `docs/superpowers/specs/2026-05-24-vfx-pass-phase-1-design.md` (commit `0797050`).

---

## File map

| File | Action | Touched in |
|---|---|---|
| `Assets/Scripts/Core/VfxSettings.cs` | **Create** | Task 1 |
| `Assets/Scripts/Core/TooltipHud.cs` | **Create** | Task 2 |
| `Assets/Scripts/Core/TooltipTrigger.cs` | **Create** | Task 2 |
| `Assets/Scripts/Core/VfxApplier.cs` | **Create** | Task 3 |
| `Assets/Scripts/Editor/VfxOverridesInstaller.cs` | **Create** | Task 4 |
| `Assets/Settings/DefaultVolumeProfile.asset` | Modified by the installer at runtime | Task 4 |
| `Assets/Scripts/Core/UIStyle.cs` | Modify (`BuildToggle` helper) | Task 5 |
| `Assets/Scripts/Core/SettingsMenu.cs` | Modify (append "Debug" tab + content panel) | Task 6 |
| `README.md` | Modify (controls table + What's In Here) | Task 7 |
| `ROADMAP.md` | Modify (Up Next #1 phase status) | Task 7 |

Order is dependency-clean: VfxSettings (Task 1) before everything that reads it; Tooltip pair (Task 2) before SettingsMenu integration; VfxApplier (Task 3) is independent but logically pairs with Task 4 (Volume overrides — VfxApplier becomes useful once the profile has the overrides to toggle); UIStyle helper (Task 5) before SettingsMenu uses it (Task 6); docs last (Task 7).

---

## Task 1: VfxSettings — PlayerPrefs facade

**Files:**
- Create: `Assets/Scripts/Core/VfxSettings.cs`

**Rationale:** The data layer. Static class wrapping `PlayerPrefs` with one bool key per effect + a `Changed` event. Every downstream consumer (VfxApplier, SettingsMenu's Debug toggles) reads/writes through this. Default = ON for every key.

- [ ] **Step 1: Create the file**

Use `mcp__unityMCP__create_script` with `path="Assets/Scripts/Core/VfxSettings.cs"` and the contents below.

```csharp
using System;
using UnityEngine;

namespace CubeFly.Core
{
    // PlayerPrefs-backed static facade for the VFX Debug-tab toggles.
    // Five typed bool properties; each Get reads PlayerPrefs (default 1
    // = ON), each Set writes + saves + fires Changed. No batching, no
    // Apply button: changes take effect immediately because the Debug
    // tab is a real-time A/B comparison surface.
    //
    // Default = ON for every key so first-launch matches the spec's
    // "Defaults: ON" rule. PlayerPrefs keys are prefixed `Vfx` so future
    // Settings consumers can use their own prefixes (`Audio`, `Display`,
    // etc.) without collision.
    //
    // Subscribers (currently just VfxApplier) listen to Changed and
    // re-apply settings to the active scene's Volume.
    public static class VfxSettings
    {
        const string KBloom               = "VfxBloom";
        const string KVignette            = "VfxVignette";
        const string KTonemapping         = "VfxTonemapping";
        const string KColorAdjustments    = "VfxColorAdjustments";
        const string KChromaticAberration = "VfxChromaticAberration";

        public static event Action Changed;

        public static bool Bloom               { get => Get(KBloom); set => Set(KBloom, value); }
        public static bool Vignette            { get => Get(KVignette); set => Set(KVignette, value); }
        public static bool Tonemapping         { get => Get(KTonemapping); set => Set(KTonemapping, value); }
        public static bool ColorAdjustments    { get => Get(KColorAdjustments); set => Set(KColorAdjustments, value); }
        public static bool ChromaticAberration { get => Get(KChromaticAberration); set => Set(KChromaticAberration, value); }

        static bool Get(string key) => PlayerPrefs.GetInt(key, 1) != 0;

        static void Set(string key, bool value)
        {
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
```

- [ ] **Step 2: Wait for Unity to recompile**

Call `mcp__unityMCP__refresh_unity(mode="force", compile="request", scope="all", wait_for_ready=true)`, then poll `ReadMcpResourceTool(server="UnityMCP", uri="mcpforunity://editor/state")` until `is_compiling == false` and `ready_for_tools == true`.

- [ ] **Step 3: Verify clean compile**

Run: `mcp__unityMCP__read_console(action="get", types=["error"], count=20, filter_text="VfxSettings", format="detailed")`
Expected: zero entries.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Core/VfxSettings.cs Assets/Scripts/Core/VfxSettings.cs.meta
git commit -m "Add VfxSettings — PlayerPrefs facade for VFX toggles

Static class with five typed bool properties (Bloom / Vignette /
Tonemapping / ColorAdjustments / ChromaticAberration), each backed
by a PlayerPrefs key prefixed 'Vfx'. Defaults to ON. Each Set saves
PlayerPrefs and fires a static Changed event for subscribers to
re-apply on change.

Phase 1 of the VFX pass (docs/superpowers/specs/2026-05-24-vfx-pass-phase-1-design.md).
No consumers yet — VfxApplier and SettingsMenu Debug tab land in
following tasks.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 2: Tooltip helpers (TooltipHud + TooltipTrigger)

**Files:**
- Create: `Assets/Scripts/Core/TooltipHud.cs`
- Create: `Assets/Scripts/Core/TooltipTrigger.cs`

**Rationale:** Reusable hover-tooltip pair. `TooltipHud` is a lazy DDOL singleton hosting the floating text label (own Canvas override at `sortingOrder=500` — above everything else). `TooltipTrigger` is a tiny `MonoBehaviour` that implements `IPointerEnterHandler` / `IPointerExitHandler` and drives the hud. Both land in one commit — they're a tightly coupled pair, and shipping the hud without the trigger is unusable (or vice versa).

- [ ] **Step 1: Create TooltipHud.cs**

Use `mcp__unityMCP__create_script` with `path="Assets/Scripts/Core/TooltipHud.cs"` and the contents below.

```csharp
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CubeFly.Core
{
    // Lazy DDOL singleton hosting the floating tooltip text label that
    // TooltipTrigger drives. Parented to PersistentHud.Instance.Root
    // with its OWN Canvas override at sortingOrder=500 — above the
    // SettingsMenu (350), GameOverMenu (400), and everything else.
    // Tooltips are always on top.
    //
    // Behaviour:
    //   • Show(text, screenPos): position the panel near the cursor,
    //     set the text, activate. Updated each frame while shown so
    //     the tooltip moves with the mouse.
    //   • Hide(): deactivate.
    //   • Screen-edge clamping: if a tooltip would extend past the
    //     right or bottom screen edge, it flips to the other side of
    //     the cursor so it's still visible.
    //
    // Lazy-create: the singleton spawns on first Show() call rather
    // than via BeforeSceneLoad bootstrap, because there are no
    // consumers until the user opens Settings → Debug tab.
    public class TooltipHud : MonoBehaviour
    {
        static TooltipHud _instance;
        public static TooltipHud Instance => _instance != null ? _instance : Create();

        const string TAG = "TooltipHud";

        GameObject _panel;
        RectTransform _panelRT;
        Text _label;
        bool _showing;

        static TooltipHud Create()
        {
            GameObject go = new GameObject("TooltipHud");
            _instance = go.AddComponent<TooltipHud>();
            DontDestroyOnLoad(go);
            return _instance;
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            BuildUI();
            HideUI();

            Debug.unityLogger.Log(TAG, "Tooltip hud ready.");
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        void BuildUI()
        {
            // Panel parented under PersistentHud's root. Has its own
            // Canvas override at sortingOrder=500 so tooltips always
            // render above any other UI.
            GameObject panelGO = new GameObject("TooltipPanel",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                typeof(Canvas), typeof(GraphicRaycaster));
            panelGO.transform.SetParent(PersistentHud.Instance.Root, false);
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) panelGO.layer = uiLayer;
            _panel = panelGO;
            _panelRT = (RectTransform)panelGO.transform;

            // Canvas override: tooltips render above everything.
            Canvas canvas = panelGO.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 500;

            // The tooltip itself should not block clicks on whatever
            // is being hovered.
            GraphicRaycaster raycaster = panelGO.GetComponent<GraphicRaycaster>();
            raycaster.enabled = false;

            Image bg = panelGO.GetComponent<Image>();
            bg.color = new Color(0.05f, 0.05f, 0.08f, 0.92f);
            bg.raycastTarget = false;

            // Anchor top-left so anchoredPosition values correspond to
            // (x, -y) screen pixels from the top-left of the screen.
            _panelRT.anchorMin = new Vector2(0f, 1f);
            _panelRT.anchorMax = new Vector2(0f, 1f);
            _panelRT.pivot = new Vector2(0f, 1f);

            // Text label fills the panel with a small padding.
            _label = UIStyle.BuildLabel(_panelRT, "", fontSize: 16);
            _label.alignment = TextAnchor.MiddleLeft;
            _label.raycastTarget = false;
            RectTransform lrt = (RectTransform)_label.transform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(8f, 4f);
            lrt.offsetMax = new Vector2(-8f, -4f);
        }

        public void Show(string text, Vector2 screenPos)
        {
            if (_label == null) return;
            _label.text = text;
            _showing = true;
            _panel.SetActive(true);
            UpdatePosition(screenPos);
        }

        public void Hide()
        {
            _showing = false;
            HideUI();
        }

        void HideUI()
        {
            if (_panel != null) _panel.SetActive(false);
        }

        void Update()
        {
            if (!_showing) return;
            Mouse mouse = Mouse.current;
            if (mouse == null) return;
            UpdatePosition(mouse.position.ReadValue());
        }

        void UpdatePosition(Vector2 screenPos)
        {
            if (_panelRT == null) return;

            // Size the panel to its content for this frame.
            float textWidth  = _label.preferredWidth  + 16f;
            float textHeight = _label.preferredHeight + 8f;
            _panelRT.sizeDelta = new Vector2(
                Mathf.Min(textWidth, 400f),
                Mathf.Max(textHeight, 24f));

            // Default offset: +20 right, -20 below cursor.
            // anchoredPosition is in (x, -y) from top-left.
            float x = screenPos.x + 20f;
            float y = -(Screen.height - screenPos.y) - 20f;

            // Right-edge clamp: flip to the left if it would extend
            // past the right edge.
            if (x + _panelRT.sizeDelta.x > Screen.width)
                x = screenPos.x - _panelRT.sizeDelta.x - 20f;

            // Bottom-edge clamp: flip above the cursor if it would
            // extend past the bottom edge.
            if (-(y - _panelRT.sizeDelta.y) > Screen.height)
                y = -(Screen.height - screenPos.y) + _panelRT.sizeDelta.y + 20f;

            _panelRT.anchoredPosition = new Vector2(x, y);
        }
    }
}
```

- [ ] **Step 2: Create TooltipTrigger.cs**

Use `mcp__unityMCP__create_script` with `path="Assets/Scripts/Core/TooltipTrigger.cs"` and the contents below.

```csharp
using UnityEngine;
using UnityEngine.EventSystems;

namespace CubeFly.Core
{
    // Tiny IPointerEnterHandler / IPointerExitHandler that surfaces a
    // tooltip via TooltipHud on hover. Attach to any UI element that
    // has a Graphic with raycastTarget=true (so PointerEnter fires).
    //
    // Public surface:
    //   • SetText(string) — set or update the tooltip text. Safe to
    //     call before the trigger is on a hovered state. Empty / null
    //     text suppresses the tooltip on enter.
    public class TooltipTrigger : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler
    {
        string _text = "";

        public void SetText(string text) => _text = text ?? "";

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (string.IsNullOrEmpty(_text)) return;
            TooltipHud.Instance.Show(_text, eventData.position);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (TooltipHud.Instance != null) TooltipHud.Instance.Hide();
        }
    }
}
```

- [ ] **Step 3: Wait for Unity to recompile**

Call `mcp__unityMCP__refresh_unity(mode="force", compile="request", scope="all", wait_for_ready=true)`, then poll `ReadMcpResourceTool(server="UnityMCP", uri="mcpforunity://editor/state")` until `is_compiling == false`.

- [ ] **Step 4: Verify clean compile**

Run: `mcp__unityMCP__read_console(action="get", types=["error"], count=20, filter_text="Tooltip", format="detailed")`
Expected: zero entries.

Then a broader check: `mcp__unityMCP__read_console(action="get", types=["error"], count=20, filter_text="Assets/Scripts", format="detailed")` expecting zero entries.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Core/TooltipHud.cs Assets/Scripts/Core/TooltipHud.cs.meta \
        Assets/Scripts/Core/TooltipTrigger.cs Assets/Scripts/Core/TooltipTrigger.cs.meta
git commit -m "Add Tooltip helper (TooltipHud + TooltipTrigger)

Reusable hover-tooltip pair. TooltipHud is a lazy DDOL singleton
hosting the floating text label, parented under PersistentHud with
its own Canvas override at sortingOrder=500 — above SettingsMenu,
GameOverMenu, and any other UI. Screen-edge clamping flips the
tooltip to the other side of the cursor near the right/bottom edges.

TooltipTrigger is a tiny IPointerEnterHandler / IPointerExitHandler
that calls Hud.Show on enter and Hud.Hide on exit. SetText is the
only public mutator.

First consumer is the Settings menu Debug tab (Task 6); future
Settings controls and HUD elements can reuse the pair.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 3: VfxApplier — DDOL singleton

**Files:**
- Create: `Assets/Scripts/Core/VfxApplier.cs`

**Rationale:** Glue layer. DDOL singleton (mirrors PauseMenu / GameOverMenu / SettingsMenu pattern) that subscribes to `SceneManager.sceneLoaded` and `VfxSettings.Changed` and re-applies settings to the active scene's Volume profile. Profile-agnostic via `VolumeProfile.TryGet<T>` — missing overrides are silently skipped, so this compiles + runs even before Task 4 adds the overrides to the profile.

- [ ] **Step 1: Create the file**

Use `mcp__unityMCP__create_script` with `path="Assets/Scripts/Core/VfxApplier.cs"` and the contents below.

```csharp
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CubeFly.Core
{
    // DDOL singleton that applies VfxSettings to the active scene's
    // URP Volume profile. Same shape as PauseMenu / GameOverMenu /
    // SettingsMenu: BeforeSceneLoad self-bootstrap, Instance property,
    // DontDestroyOnLoad.
    //
    // Behaviour:
    //   • On Awake: subscribe to SceneManager.sceneLoaded and
    //     VfxSettings.Changed, then Apply() once (handles the initial
    //     scene if it loaded before our Awake).
    //   • On SceneManager.sceneLoaded: Apply() to the newly-loaded
    //     scene's Volume.
    //   • On VfxSettings.Changed: Apply() to the active scene's
    //     Volume (real-time A/B comparison).
    //
    // Apply() is idempotent and profile-agnostic. It probes for each
    // of the five Phase-A overrides via VolumeProfile.TryGet — missing
    // overrides are silently skipped, so scenes without a Volume (or
    // with a profile that doesn't have these overrides) don't throw.
    //
    // Execution order is -1500 — between SettingsMenu (-2000) and
    // PauseMenu (-1000); keeps the persistent-UI tier ordering
    // consistent.
    [DefaultExecutionOrder(-1500)]
    public class VfxApplier : MonoBehaviour
    {
        public static VfxApplier Instance { get; private set; }

        const string TAG = "VfxApplier";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            GameObject go = new GameObject("VfxApplier");
            go.AddComponent<VfxApplier>();
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

            SceneManager.sceneLoaded += OnSceneLoaded;
            VfxSettings.Changed += Apply;

            Apply();

            Debug.unityLogger.Log(TAG, "VFX applier ready.");
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                VfxSettings.Changed -= Apply;
                Instance = null;
            }
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Apply();

        void Apply()
        {
            Volume volume = FindFirstObjectByType<Volume>();
            if (volume == null || volume.profile == null) return;
            VolumeProfile profile = volume.profile;

            if (profile.TryGet<Bloom>(out var bloom))
                bloom.active = VfxSettings.Bloom;
            if (profile.TryGet<Vignette>(out var vignette))
                vignette.active = VfxSettings.Vignette;
            if (profile.TryGet<Tonemapping>(out var tonemapping))
                tonemapping.active = VfxSettings.Tonemapping;
            if (profile.TryGet<ColorAdjustments>(out var color))
                color.active = VfxSettings.ColorAdjustments;
            if (profile.TryGet<ChromaticAberration>(out var ca))
                ca.active = VfxSettings.ChromaticAberration;
        }
    }
}
```

- [ ] **Step 2: Wait for Unity to recompile**

Call `mcp__unityMCP__refresh_unity(mode="force", compile="request", scope="all", wait_for_ready=true)`, then poll `ReadMcpResourceTool(server="UnityMCP", uri="mcpforunity://editor/state")` until `is_compiling == false`.

- [ ] **Step 3: Verify clean compile**

Run: `mcp__unityMCP__read_console(action="get", types=["error"], count=20, filter_text="VfxApplier", format="detailed")`
Expected: zero entries. Note: the `using UnityEngine.Rendering.Universal;` directive resolves the override types (Bloom, Vignette, etc.) from URP. If a `type or namespace name does not exist` error appears, URP isn't installed — confirm `Packages/manifest.json` has `com.unity.render-pipelines.universal`.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Core/VfxApplier.cs Assets/Scripts/Core/VfxApplier.cs.meta
git commit -m "Add VfxApplier — applies VfxSettings to active scene's Volume

DDOL singleton (same pattern as PauseMenu / GameOverMenu /
SettingsMenu). BeforeSceneLoad bootstrap, [DefaultExecutionOrder(-1500)].
Subscribes to SceneManager.sceneLoaded + VfxSettings.Changed; calls
Apply() on both. Apply() finds the active scene's Volume via
FindFirstObjectByType, probes its VolumeProfile via TryGet for each
of the five Phase-A overrides, and sets each one's .active field
based on VfxSettings.

Profile-agnostic and idempotent. Missing overrides are silently
skipped — works even before Task 4 adds the overrides to
DefaultVolumeProfile.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 4: Editor installer + apply VFX overrides to DefaultVolumeProfile

**Files:**
- Create: `Assets/Scripts/Editor/VfxOverridesInstaller.cs`
- Modify: `Assets/Settings/DefaultVolumeProfile.asset` (via the installer running through Unity MCP `execute_menu_item`)

**Rationale:** The volume profile is a ScriptableObject asset; adding 5 override components is fiddly via hand-written YAML (each requires a specific MonoScript GUID + fileID). Cleanest approach is a one-shot `[MenuItem]` Editor script that programmatically calls `VolumeProfile.Add<T>()` and `AssetDatabase.SaveAssetIfDirty()`. Script is **idempotent** (TryGet check before each Add) so it's safe to re-run, and stays in the repo as insurance — if anyone accidentally deletes an override, re-running the menu item restores it.

- [ ] **Step 1: Create the Editor installer script**

Use `mcp__unityMCP__create_script` with `path="Assets/Scripts/Editor/VfxOverridesInstaller.cs"` and the contents below.

```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CubeFly.EditorTools
{
    // One-shot installer for Phase 1 of the VFX pass. Adds five URP
    // post-processing overrides (Bloom, Vignette, Tonemapping,
    // ColorAdjustments, ChromaticAberration) to DefaultVolumeProfile
    // with the spec's starter tunings.
    //
    // Idempotent: each Ensure* method TryGets the override first and
    // returns early if already present. Safe to re-run — only adds
    // what's missing. The script stays in the repo as insurance: if
    // an override gets deleted, re-running the menu item restores it.
    //
    // Invoked via Tools/CubeFly/Apply Phase A VFX overrides (or via
    // Unity MCP's execute_menu_item tool).
    public static class VfxOverridesInstaller
    {
        const string ProfilePath = "Assets/Settings/DefaultVolumeProfile.asset";
        const string MenuPath    = "Tools/CubeFly/Apply Phase A VFX overrides";

        [MenuItem(MenuPath)]
        public static void Apply()
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                Debug.LogError($"VfxOverridesInstaller: profile not found at {ProfilePath}");
                return;
            }

            List<string> added = new List<string>();

            EnsureBloom(profile, added);
            EnsureVignette(profile, added);
            EnsureTonemapping(profile, added);
            EnsureColorAdjustments(profile, added);
            EnsureChromaticAberration(profile, added);

            if (added.Count == 0)
            {
                Debug.Log("VfxOverridesInstaller: all 5 overrides already present; nothing to add.");
                return;
            }

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
            AssetDatabase.Refresh();
            Debug.Log($"VfxOverridesInstaller: added {added.Count} overrides: {string.Join(", ", added)}");
        }

        static void EnsureBloom(VolumeProfile p, List<string> added)
        {
            if (p.TryGet<Bloom>(out _)) return;
            Bloom b = p.Add<Bloom>(true);
            b.intensity.Override(0.6f);
            b.threshold.Override(1.0f);
            b.scatter.Override(0.7f);
            added.Add(nameof(Bloom));
        }

        static void EnsureVignette(VolumeProfile p, List<string> added)
        {
            if (p.TryGet<Vignette>(out _)) return;
            Vignette v = p.Add<Vignette>(true);
            v.intensity.Override(0.25f);
            v.smoothness.Override(0.4f);
            v.color.Override(Color.black);
            added.Add(nameof(Vignette));
        }

        static void EnsureTonemapping(VolumeProfile p, List<string> added)
        {
            if (p.TryGet<Tonemapping>(out _)) return;
            Tonemapping t = p.Add<Tonemapping>(true);
            t.mode.Override(TonemappingMode.ACES);
            added.Add(nameof(Tonemapping));
        }

        static void EnsureColorAdjustments(VolumeProfile p, List<string> added)
        {
            if (p.TryGet<ColorAdjustments>(out _)) return;
            ColorAdjustments c = p.Add<ColorAdjustments>(true);
            c.postExposure.Override(0f);
            c.contrast.Override(5f);
            c.saturation.Override(5f);
            c.hueShift.Override(0f);
            added.Add(nameof(ColorAdjustments));
        }

        static void EnsureChromaticAberration(VolumeProfile p, List<string> added)
        {
            if (p.TryGet<ChromaticAberration>(out _)) return;
            ChromaticAberration ca = p.Add<ChromaticAberration>(true);
            ca.intensity.Override(0.08f);
            added.Add(nameof(ChromaticAberration));
        }
    }
}
```

- [ ] **Step 2: Wait for Unity to recompile**

Call `mcp__unityMCP__refresh_unity(mode="force", compile="request", scope="all", wait_for_ready=true)`, then poll `ReadMcpResourceTool(server="UnityMCP", uri="mcpforunity://editor/state")` until `is_compiling == false`.

- [ ] **Step 3: Verify clean compile**

Run: `mcp__unityMCP__read_console(action="get", types=["error"], count=20, filter_text="VfxOverridesInstaller", format="detailed")`
Expected: zero entries. The Editor script is gated by `using UnityEditor;` so it won't be included in runtime builds (per Unity's `Assets/Scripts/Editor/` folder convention).

- [ ] **Step 4: Invoke the menu item to populate the profile**

Run: `mcp__unityMCP__execute_menu_item(menu_path="Tools/CubeFly/Apply Phase A VFX overrides")`

Expected: returns success. (If the menu_item invocation isn't supported by this MCP version, the user can run the menu manually via Unity's `Tools` menu.)

- [ ] **Step 5: Verify the installer logged success**

Run: `mcp__unityMCP__read_console(action="get", types=["log"], count=10, filter_text="VfxOverridesInstaller", format="detailed")`
Expected: log entry reading `"VfxOverridesInstaller: added 5 overrides: Bloom, Vignette, Tonemapping, ColorAdjustments, ChromaticAberration"` (or `"all 5 overrides already present"` if the asset already has them from a previous run).

- [ ] **Step 6: Verify the profile asset was modified**

Run: `git status --short Assets/Settings/DefaultVolumeProfile.asset`
Expected: ` M Assets/Settings/DefaultVolumeProfile.asset` (modified). If the file is unchanged, the installer didn't write — check console for errors and re-run Step 4.

- [ ] **Step 7: Commit both the installer + the modified profile**

```bash
git add Assets/Scripts/Editor/VfxOverridesInstaller.cs Assets/Scripts/Editor/VfxOverridesInstaller.cs.meta \
        Assets/Settings/DefaultVolumeProfile.asset
git commit -m "VFX: install 5 Phase-A post-processing overrides on DefaultVolumeProfile

New Editor MenuItem (Tools/CubeFly/Apply Phase A VFX overrides) adds
Bloom, Vignette, Tonemapping, ColorAdjustments, and ChromaticAberration
to DefaultVolumeProfile with the spec's starter tunings:
  Bloom            intensity 0.6, threshold 1.0, scatter 0.7
  Vignette         intensity 0.25, smoothness 0.4, colour black
  Tonemapping      mode ACES
  ColorAdjustments contrast +5, saturation +5
  ChromaticAberration  intensity 0.08

Idempotent (TryGet check per override); the script stays in the repo
as insurance for re-application if any override is later deleted.
The Editor folder convention excludes it from runtime builds.

VfxApplier (already landed) now toggles each .active field based on
VfxSettings whenever the player flips a Debug-tab switch.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

(If the `Assets/Settings/DefaultVolumeProfile.asset` git diff also includes unrelated YAML noise — e.g. float-precision drift from Unity re-serialising — keep only the override additions; either review the diff manually or accept the noise. Per the project's CLAUDE.md, material assets sometimes drift on re-save; the same can happen here. If the diff is unmanageably noisy, ask the user before committing.)

---

## Task 5: UIStyle.BuildToggle — helper for the Debug tab

**Files:**
- Modify: `Assets/Scripts/Core/UIStyle.cs` — add a new public static method after `BuildLabeledButton` (currently lines 81–144 of the file).

**Rationale:** The Debug tab's 5 toggles need a UIStyle helper. Mirrors `BuildLabeledButton`'s structure: container GameObject with a Toggle component, child Background Image (the box), Checkmark Image inside the Background, and Label Text to the right.

- [ ] **Step 1: Add the BuildToggle method to UIStyle.cs**

Use `Edit` (or `mcp__unityMCP__script_apply_edits`) to add the new method. Anchor on the existing `BuildDropdown` method's opening signature so the new method is inserted between `BuildLabeledButton`'s closing brace and `BuildDropdown`.

**Before** (the line just above `BuildDropdown`):

```csharp
        public static Dropdown BuildDropdown(Transform parent, Vector2 size, int fontSize = 22)
```

**After** (prepend the new method):

```csharp
        // Builds a labelled checkbox-style Toggle. Container GameObject
        // holds the Toggle component, a Background Image (the visible
        // square), a Checkmark Image (the tick) as a child of the
        // Background, and a Label Text to the right.
        //
        // The caller drives the toggle via `.isOn` and listens to
        // `.onValueChanged`. Hover/press feedback comes from the
        // Toggle's default ColorTint transition on the Background.
        public static Toggle BuildToggle(Transform parent, string labelText,
            Vector2 size, int fontSize = 22)
        {
            GameObject containerGO = new GameObject(labelText + "Toggle",
                typeof(RectTransform), typeof(Toggle));
            containerGO.transform.SetParent(parent, false);
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) containerGO.layer = uiLayer;

            RectTransform crt = (RectTransform)containerGO.transform;
            crt.sizeDelta = size;

            Toggle toggle = containerGO.GetComponent<Toggle>();

            // Background — the visible square box on the left.
            GameObject bgGO = new GameObject("Background",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bgGO.transform.SetParent(containerGO.transform, false);
            if (uiLayer >= 0) bgGO.layer = uiLayer;
            RectTransform bgRT = (RectTransform)bgGO.transform;
            bgRT.anchorMin = new Vector2(0f, 0.5f);
            bgRT.anchorMax = new Vector2(0f, 0.5f);
            bgRT.pivot = new Vector2(0f, 0.5f);
            bgRT.sizeDelta = new Vector2(28f, 28f);
            bgRT.anchoredPosition = new Vector2(4f, 0f);
            Image bgImage = bgGO.GetComponent<Image>();
            bgImage.color = BackgroundIdle;
            toggle.targetGraphic = bgImage;

            // Checkmark — child of Background, shown when Toggle.isOn.
            GameObject checkGO = new GameObject("Checkmark",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            checkGO.transform.SetParent(bgGO.transform, false);
            if (uiLayer >= 0) checkGO.layer = uiLayer;
            RectTransform chkRT = (RectTransform)checkGO.transform;
            chkRT.anchorMin = Vector2.zero;
            chkRT.anchorMax = Vector2.one;
            chkRT.offsetMin = new Vector2(6f, 6f);
            chkRT.offsetMax = new Vector2(-6f, -6f);
            Image checkImage = checkGO.GetComponent<Image>();
            checkImage.color = TintHighlight;
            toggle.graphic = checkImage;

            // Label — text to the right of the box, fills remaining width.
            GameObject labelGO = new GameObject("Label",
                typeof(RectTransform), typeof(CanvasRenderer));
            labelGO.transform.SetParent(containerGO.transform, false);
            if (uiLayer >= 0) labelGO.layer = uiLayer;
            RectTransform lrt = (RectTransform)labelGO.transform;
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(1f, 1f);
            lrt.offsetMin = new Vector2(40f, 0f);
            lrt.offsetMax = new Vector2(0f, 0f);
            Text label = labelGO.AddComponent<Text>();
            label.font = BuiltinFont;
            label.alignment = TextAnchor.MiddleLeft;
            label.fontSize = fontSize;
            label.color = LabelColor;
            label.text = labelText;
            label.raycastTarget = true;     // needed for TooltipTrigger to fire on hover

            return toggle;
        }

        public static Dropdown BuildDropdown(Transform parent, Vector2 size, int fontSize = 22)
```

- [ ] **Step 2: Wait for Unity to recompile**

Call `mcp__unityMCP__refresh_unity(mode="force", compile="request", scope="all", wait_for_ready=true)`, then poll `ReadMcpResourceTool(server="UnityMCP", uri="mcpforunity://editor/state")` until `is_compiling == false`.

- [ ] **Step 3: Verify clean compile**

Run: `mcp__unityMCP__read_console(action="get", types=["error"], count=20, filter_text="UIStyle", format="detailed")`
Expected: zero entries.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Core/UIStyle.cs
git commit -m "UIStyle: add BuildToggle helper

New procedural helper that builds a labelled checkbox-style Toggle —
container GameObject with a Toggle component, Background Image (the
visible square box), Checkmark Image as a child of the Background
(shown when Toggle.isOn), and a Label Text to the right. Caller wires
.isOn and .onValueChanged.

Mirrors BuildLabeledButton's structure. First consumer is the
SettingsMenu Debug tab (Task 6); reusable for future Settings
controls (Audio mute toggles, Gameplay options, etc.).

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 6: SettingsMenu — Debug tab integration

**Files:**
- Modify: `Assets/Scripts/Core/SettingsMenu.cs` — append `"Debug"` to `TabNames`; branch the content-panel loop to call a new `BuildDebugPanel` for the Debug tab; add `BuildDebugPanel` + `BuildDebugToggle` helper methods.

**Rationale:** The user-visible payoff. Settings → Debug tab → 5 toggles with hover tooltips, each wired through `VfxSettings`. After this task, the player can open Settings from MainMenu or Pause, click Debug, hover/toggle each effect, and see the active scene re-render.

- [ ] **Step 1: Append "Debug" to the TabNames array**

Use `Edit` (or `mcp__unityMCP__script_apply_edits`) on `Assets/Scripts/Core/SettingsMenu.cs`.

**Before:**

```csharp
        static readonly string[] TabNames = new[]
        {
            "General", "Display", "Graphics", "Audio", "Controls", "Gameplay"
        };
```

**After:**

```csharp
        static readonly string[] TabNames = new[]
        {
            "General", "Display", "Graphics", "Audio", "Controls", "Gameplay", "Debug"
        };
```

- [ ] **Step 2: Branch the content-panel loop for the Debug tab**

**Before** (the placeholder loop inside `BuildUI()`):

```csharp
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
```

**After:**

```csharp
            // Six placeholder content panels + the Debug tab.
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

                if (TabNames[i] == "Debug")
                {
                    BuildDebugPanel(prt);
                }
                else
                {
                    Text label = UIStyle.BuildLabel(prt, "Coming soon", fontSize: 32);
                    RectTransform lrt = (RectTransform)label.transform;
                    lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0.5f, 0.5f);
                    lrt.sizeDelta = new Vector2(400f, 80f);
                    lrt.anchoredPosition = Vector2.zero;
                }

                _tabPanels[i] = panel;
            }
```

- [ ] **Step 3: Add the BuildDebugPanel + BuildDebugToggle helper methods**

Insert these new methods at the end of the `SettingsMenu` class, just before the closing brace of the class. Use `Edit` and anchor on the closing `}` of `SelectTab(int index)` (currently the last method in the class).

Anchor before:

```csharp
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

After (the same `SelectTab` followed by the new methods, then the class closing brace):

```csharp
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

        // ---------- Debug tab ----------

        void BuildDebugPanel(RectTransform parent)
        {
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) parent.gameObject.layer = uiLayer;

            // Section title at top of the content panel.
            Text title = UIStyle.BuildLabel(parent, "VFX Toggles", fontSize: 28,
                style: FontStyle.Bold);
            RectTransform titleRT = (RectTransform)title.transform;
            titleRT.anchorMin = new Vector2(0f, 1f);
            titleRT.anchorMax = new Vector2(1f, 1f);
            titleRT.pivot = new Vector2(0.5f, 1f);
            titleRT.sizeDelta = new Vector2(0f, 40f);
            titleRT.anchoredPosition = new Vector2(0f, -20f);

            // Five toggle rows. Each is anchored top-left of the content
            // area, offset by 50 px per row below the title.
            BuildDebugToggle(parent, "Bloom",
                "Globally lifts emissive surfaces (laser beam, reactor glow, muzzle flash). High visual impact for low cost.",
                0, () => VfxSettings.Bloom, v => VfxSettings.Bloom = v);
            BuildDebugToggle(parent, "Vignette",
                "Subtle dark edge that focuses attention on the centre of the screen.",
                1, () => VfxSettings.Vignette, v => VfxSettings.Vignette = v);
            BuildDebugToggle(parent, "Tonemapping (ACES)",
                "Cinematic tone curve. Stops bright effects clipping to pure white.",
                2, () => VfxSettings.Tonemapping, v => VfxSettings.Tonemapping = v);
            BuildDebugToggle(parent, "Colour grading",
                "Light contrast and saturation lift for cinematic colour response.",
                3, () => VfxSettings.ColorAdjustments, v => VfxSettings.ColorAdjustments = v);
            BuildDebugToggle(parent, "Chromatic aberration",
                "Subtle colour-fringe at screen edges. Some find it muddies the picture — toggle off if so.",
                4, () => VfxSettings.ChromaticAberration, v => VfxSettings.ChromaticAberration = v);
        }

        void BuildDebugToggle(RectTransform parent, string label, string tooltip,
            int rowIndex, System.Func<bool> getter, System.Action<bool> setter)
        {
            Toggle toggle = UIStyle.BuildToggle(parent, label, new Vector2(600f, 40f), fontSize: 22);
            RectTransform rt = (RectTransform)toggle.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            // -80 px clearance below the title, then 50 px per row.
            rt.anchoredPosition = new Vector2(20f, -80f - rowIndex * 50f);

            toggle.isOn = getter();
            toggle.onValueChanged.AddListener(value => setter(value));

            // Attach a TooltipTrigger to the toggle so hovering anywhere
            // on the row reveals the description tooltip.
            TooltipTrigger trigger = toggle.gameObject.AddComponent<TooltipTrigger>();
            trigger.SetText(tooltip);
        }
    }
}
```

- [ ] **Step 4: Wait for Unity to recompile**

Call `mcp__unityMCP__refresh_unity(mode="force", compile="request", scope="all", wait_for_ready=true)`, then poll `ReadMcpResourceTool(server="UnityMCP", uri="mcpforunity://editor/state")` until `is_compiling == false`.

- [ ] **Step 5: Verify clean compile**

Run: `mcp__unityMCP__read_console(action="get", types=["error"], count=20, filter_text="SettingsMenu", format="detailed")`
Expected: zero entries.

Then a broader check: `mcp__unityMCP__read_console(action="get", types=["error"], count=20, filter_text="Assets/Scripts", format="detailed")` expecting zero entries.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Core/SettingsMenu.cs
git commit -m "SettingsMenu: add Debug tab with 5 VFX toggles + tooltips

Append 'Debug' to TabNames (size 6 → 7). The content-panel
construction loop now branches: for the Debug tab, call
BuildDebugPanel(parent) instead of the 'Coming soon' label; for the
other six tabs, the existing placeholder label is unchanged.

BuildDebugPanel renders a 'VFX Toggles' section title plus five
toggle rows (Bloom, Vignette, Tonemapping (ACES), Colour grading,
Chromatic aberration). Each row uses UIStyle.BuildToggle, wires
through VfxSettings (getter for initial state, setter on
onValueChanged), and attaches a TooltipTrigger with a hover
description sourced from docs/vfx_pass_ideas.md §0 (shortened for
tooltip).

After this commit, the player can open Settings → Debug from either
the MainMenu Settings button or the in-game pause overlay, toggle
each effect, and watch the scene re-render via VfxApplier in real
time.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 7: README + ROADMAP docs

**Files:**
- Modify: `README.md` — add a "What's In Here" bullet for the post-processing tier; update Main Menu Settings row to mention the Debug tab.
- Modify: `ROADMAP.md` — annotate Up Next #1 with the four-phase breakdown, marking Phase 1 as in-progress / shipped.

**Rationale:** Keep the canonical docs honest per the project's CLAUDE.md ("keep them in sync when you change behaviour"). README is the user-facing reference; ROADMAP shows current vs planned work.

- [ ] **Step 1: Add the "What's In Here" bullet to README.md**

Use `Edit` on `README.md`. Anchor on the existing **"ESC pause overlay"** bullet (currently around line 35) and insert the new bullet right after it.

**Before:**

```markdown
- **ESC pause overlay.** Self-bootstrapping DDOL singleton. ESC pauses anywhere in BuildScene / FlyScene; `Menu` returns to Main Menu, `Back to Desktop` quits.
```

**After:**

```markdown
- **ESC pause overlay.** Self-bootstrapping DDOL singleton. ESC pauses anywhere in BuildScene / FlyScene; `Menu` returns to Main Menu, `Back to Desktop` quits.
- **Post-processing + VFX Debug tab.** Five URP Volume overrides (Bloom, Vignette, Tonemapping ACES, ColorAdjustments, ChromaticAberration) on the main game profile, with a seventh `Debug` tab in the Settings menu letting the player toggle each effect individually. Toggles persist via `PlayerPrefs`. First slice of the broader VFX pass — engines / weapons / shields / destruction effects follow in later PRs (see `docs/vfx_pass_ideas.md`).
```

- [ ] **Step 2: Update the Main Menu Settings row in README.md**

**Before** (the Main Menu controls table row):

```markdown
| `Settings` | Open the Settings menu (six placeholder tabs — General / Display / Graphics / Audio / Controls / Gameplay). |
```

**After:**

```markdown
| `Settings` | Open the Settings menu (six placeholder tabs — General / Display / Graphics / Audio / Controls / Gameplay — plus a `Debug` tab with per-effect VFX toggles). |
```

- [ ] **Step 3: Update the Pause Overlay Settings row in README.md**

**Before** (the Pause Overlay controls table row):

```markdown
| `Settings` button | Open the Settings menu (six placeholder tabs). `Esc` from Settings returns here, then `Esc` again resumes the game. |
```

**After:**

```markdown
| `Settings` button | Open the Settings menu (six placeholder tabs + `Debug` tab with VFX toggles). `Esc` from Settings returns here, then `Esc` again resumes the game. |
```

- [ ] **Step 4: Expand ROADMAP item #1 with the four-phase breakdown**

Use `Edit` on `ROADMAP.md`.

**Before:**

```markdown
### 1. Extended VFX pass

Engine trails, muzzle flashes, projectile trails, explosion / death particles, hit sparks — plus the deferred laser-beam glow and the shield dome. Cheap polish, big perceived-quality win. Mostly URP particles plus a couple of shader graphs — no new gameplay systems.
```

**After:**

```markdown
### 1. Extended VFX pass

Engine trails, muzzle flashes, projectile trails, explosion / death particles, hit sparks — plus the deferred laser-beam glow and the shield dome. Cheap polish, big perceived-quality win. Mostly URP particles plus a couple of shader graphs — no new gameplay systems.

Phasing per `docs/vfx_pass_ideas.md`:

- **Phase 1 — Post-processing + Debug tab (in-flight).** Bloom, Vignette, Tonemapping (ACES), ColorAdjustments, ChromaticAberration as URP Volume overrides on the main game profile, plus the seventh `Debug` tab in Settings with per-effect toggles backed by PlayerPrefs. Establishes the Debug-tab pattern subsequent phases append to. Plus reusable Tooltip helper.
- **Phase B — Small prefabs / new behaviours.** Engine plumes per thruster cube (with boost flare), muzzle flash + bullet tracer + impact spark, rocket exhaust + smoke trail, cube death enhancement (flash + spark + debris + trail), camera shake on crash / detonation.
- **Phase C — Shaders + scripted sequences.** Laser beam glow + impact heat-distortion + scorch decal, shield dome (hex/fresnel) + hit ripple + collapse, rocket detonation multi-emitter, delete-tool dissolve, reactor inner glow + stress sparks.
- **Phase D — Alpha-cube cinematic death.** Multi-stage explosion + time-scale dip + radial blur + debris field, before the existing "Construct Destroyed" overlay.
```

- [ ] **Step 5: Commit both files**

```bash
git add README.md ROADMAP.md
git commit -m "Docs: README + ROADMAP for VFX phase 1

- README 'What's In Here' gains a post-processing + Debug tab bullet
  noting the five Volume overrides and the toggles in Settings.
- README Main Menu / Pause Overlay Settings rows mention the new
  Debug tab.
- ROADMAP item #1 (Extended VFX pass) expands into a four-phase
  breakdown matching docs/vfx_pass_ideas.md: Phase 1 in-flight,
  Phases B/C/D remaining.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Post-task verification (user play-test)

After all seven tasks land, ask the user to manually play-test:

1. **Default ON in fresh session.** Delete `PlayerPrefs` (or run on a fresh machine), press Play. All five effects should be visibly active in the FlyScene (subtle bloom on the laser/reactor, slight vignette, ACES tonemapping curve, mild contrast/saturation lift, gentle CA at screen edges).
2. **From the Main Menu:** click `Settings` → `Debug` tab. Five toggles visible: Bloom, Vignette, Tonemapping (ACES), Colour grading, Chromatic aberration. All ON.
3. **Hover each toggle's label** — the tooltip should appear near the cursor with the description string. Move the cursor — tooltip moves with it. Exit — tooltip vanishes.
4. **Click `Bloom` to toggle off** — flicker the camera back to the FlyScene (close Settings, ESC, look around); the bloom halo around emissive surfaces should be gone. Toggle Bloom on again — it returns.
5. **Repeat for the other four toggles** — each should produce a visible change. Vignette is subtle; CA might be hard to see at the default 0.08; Tonemapping is most obvious on bright HDR pixels (e.g. close-range muzzle flashes when those land later).
6. **Persistence test:** Toggle Bloom off, exit to MainMenu via Pause, restart the game (or domain-reload), reopen Settings → Debug. Bloom should still be OFF.
7. **Edge case:** Open Settings during pause in FlyScene, toggle effects mid-game — VfxApplier should re-apply each change without pausing the game further (Settings already freezes time via `Time.timeScale = 0`).

If anything is wrong (toggle doesn't stick, tooltip doesn't show, profile re-saves with stale data on Play), fix forward with a new commit on the same branch and re-test.

---

## Plan self-review

**Spec coverage:**

| Spec requirement | Plan task |
|---|---|
| `VfxSettings` static class (5 properties, PlayerPrefs, default ON, Changed event) | Task 1 |
| `VfxApplier` DDOL singleton (BeforeSceneLoad, scene-loaded + Changed subscriber, profile-agnostic Apply via TryGet) | Task 3 |
| `TooltipHud` lazy DDOL singleton (sortingOrder=500, screen-edge clamping, mouse-tracking) | Task 2 |
| `TooltipTrigger` MonoBehaviour (IPointerEnter/Exit, SetText) | Task 2 |
| `UIStyle.BuildToggle` helper | Task 5 |
| SettingsMenu adds 7th "Debug" tab; branches content-panel loop; 5 toggles with tooltips wired to VfxSettings | Task 6 |
| `DefaultVolumeProfile.asset` gains 5 overrides with starter tunings | Task 4 |
| README "What's In Here" bullet + Main Menu / Pause Overlay rows | Task 7 |
| ROADMAP item #1 four-phase breakdown | Task 7 |
| Idempotent installer script in the repo as insurance | Task 4 |

All spec sections covered. No orphans.

**Placeholder scan:** No `TBD`, `TODO`, `FIXME`, "implement later", or "similar to Task N" patterns. Every code block is complete. Every command shows expected output. The one notice-comment in Task 4 Step 7 ("if the diff is unmanageably noisy, ask the user before committing") is a conditional guard, not a placeholder — it specifies a check, not a deferred decision.

**Type consistency:**
- `VfxSettings.Bloom` (etc.) — defined in Task 1 as `public static bool` with `{ get => Get(K…); set => Set(K…, value); }`. Read in Task 3 (`VfxSettings.Bloom`), Task 6 (`() => VfxSettings.Bloom`, `v => VfxSettings.Bloom = v`). Consistent.
- `VfxSettings.Changed` — defined in Task 1 as `public static event Action`. Subscribed in Task 3 (`VfxSettings.Changed += Apply`). Consistent.
- `TooltipHud.Instance.Show(string, Vector2)` / `Hide()` — defined in Task 2 (`public void Show(string text, Vector2 screenPos)`, `public void Hide()`). Called in Task 2's `TooltipTrigger.OnPointerEnter` / `OnPointerExit`. Consistent.
- `TooltipTrigger.SetText(string)` — defined in Task 2. Called in Task 6's `BuildDebugToggle` (`trigger.SetText(tooltip)`). Consistent.
- `UIStyle.BuildToggle(Transform, string, Vector2, int)` — defined in Task 5 as `public static Toggle BuildToggle(Transform parent, string labelText, Vector2 size, int fontSize = 22)`. Called in Task 6's `BuildDebugToggle` (`UIStyle.BuildToggle(parent, label, new Vector2(600f, 40f), fontSize: 22)`). Consistent.
- `VolumeProfile.TryGet<T>(out T)` — used in Task 3 (Apply) and Task 4 (installer EnsureX). Standard URP API. Consistent.
- `Bloom` / `Vignette` / `Tonemapping` / `ColorAdjustments` / `ChromaticAberration` — URP override types from `UnityEngine.Rendering.Universal`. Imported in Task 3 and Task 4. Used in both consistently.

No type / signature drift between tasks.

**Out-of-scope guard:** No task touches Phase B effects (engines, weapons, destruction prefabs), Phase C shaders (laser glow, shield dome), Phase D alpha-cube cinematic, audio, per-scene divergent grading, the contextual CA ramp on damage / overheat, or any non-VFX Settings controls. Matches the spec's "Out" column exactly.
