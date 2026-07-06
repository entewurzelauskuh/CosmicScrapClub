# Desert FlyScene A3 — Cel Look + Live Toggle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give FlyScene the desert cel look (activate the built-but-unused `Desert_Renderer` outline + add the `DesertVolumeProfile` grade) behind a live, PlayerPrefs-saved "Cel look" toggle in the Settings menu, so as-is ↔ cel can be A/B-compared and refined in-game.

**Architecture:** A `CelLookSettings` static (Core, PlayerPrefs-backed, with an `OnChanged` event) is the single source of truth for the preference. A `DesertLookController` MonoBehaviour (Desert, in FlyScene) applies it at runtime — `cameraData.SetRenderer(index)` to swap `PC_Renderer`↔`Desert_Renderer` + the `DesertVolumeProfile` Volume's `weight` 0↔1 — and re-applies live on `OnChanged`. The existing `SettingsMenu` debug panel gets one more toggle wired to `CelLookSettings`. Dependency direction stays clean: UI + controller both depend on Core, not on each other.

**Tech Stack:** Unity 6.3 / URP, MonoBehaviour, `Assembly-CSharp`, namespaces `CubeFly.Core` / `.Desert`. All edits via UnityMCP against the **main project root** (branch `explore/desert-flyscene` checked out there; live Editor runs against it — not a worktree). Verification = `read_console` + **edit-time game-view screenshots** (the outline/grade render in edit mode, so the *look* is verifiable without Play) + a human live-toggle re-fly for the switch itself.

**Spec:** `docs/superpowers/specs/2026-07-03-desert-flyscene-a3-design.md`

**Refinement from planning:** the toggle lives in `SettingsMenu` (reached via PauseMenu → Settings), not hand-built on the pause panel — it's the actual "settings menu", already has the toggle pattern, and matches the brainstormed intent. Flag for the maintainer at handoff.

---

## File Structure

| File | Responsibility | Change |
|---|---|---|
| `Assets/Scripts/Core/CelLookSettings.cs` | Persisted cel-look preference: `bool Enabled` (PlayerPrefs `desert.celLook`, default on) + `OnChanged` event. | **Create**. |
| `Assets/Scripts/Desert/DesertLookController.cs` | Applies the preference in FlyScene: camera `SetRenderer` + Volume `weight`; re-applies on `OnChanged`. | **Create**. |
| `Assets/Scenes/FlyScene.unity` | Holds a `DesertLook` GameObject = global `Volume` (`DesertVolumeProfile`) + `DesertLookController` (refs to that Volume + the camera). | **Modify**. |
| `Assets/Scripts/Core/SettingsMenu.cs` | One extra debug-panel toggle ("Cel look (desert)") wired to `CelLookSettings`. | **Modify** (`BuildDebugPanel`, ~line 345). |

**Reused untouched:** `Desert_Renderer.asset` (index 1 in `PC_RPAsset`), `PC_Renderer.asset` (index 0), `DesertVolumeProfile.asset`, `CelShaded`/`OutlineEdgeDetect` shaders, `UIStyle.BuildToggle`, `TooltipTrigger`.

---

## Task 1: Spike — confirm the outline renders on FlyScene content

De-risk the key unknown (§7 of the spec) *before* building machinery: does `Desert_Renderer`'s screen-space outline actually render on FlyScene's geometry, with no errors? Verified at **edit time** by pointing the camera at renderer index 1 and screenshotting the game view, then reverting. No commit — this is validation.

**Files:** none committed (temporary edit to `Assets/Scenes/FlyScene.unity`, reverted).

- [ ] **Step 1: Ensure FlyScene is the active scene.**

`manage_scene` action `load`, `path: "Assets/Scenes/FlyScene.unity"`. Then `read_console` (Error) → clean.

- [ ] **Step 2: Point the main camera at `Desert_Renderer` (index 1) at edit time.**

`execute_code`:
```csharp
var cam = Camera.main;
if (cam == null) foreach (var c in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) { cam = c; break; }
if (cam == null) return "no camera";
var data = cam.GetUniversalAdditionalCameraData();
var so = new UnityEditor.SerializedObject(data);
var p = so.FindProperty("m_RendererIndex");
int old = p.intValue;
p.intValue = 1; // Desert_Renderer
so.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.EditorUtility.SetDirty(data);
return $"camera '{cam.name}' rendererIndex {old} -> {p.intValue}";
```
Expected: `... -> 1`. `read_console` (Error) → clean (no shader/feature errors from the outline pass).

- [ ] **Step 3: Screenshot the game view and eyeball the outline.**

`manage_camera` action `screenshot`, `capture_source: game_view`, `include_image: true`, `max_resolution: 900`. Expect black silhouette outlines on formations/ship/targets (Roberts-cross depth+normal edges). If the pass errored or the frame is broken, STOP — the fallback (single-Forward-renderer, toggle only the outline feature) from spec §7 applies; raise with the maintainer.

- [ ] **Step 4: Revert the camera to the default renderer.**

`execute_code`: same as Step 2 but `p.intValue = -1;` (pipeline default) and return the confirmation. `read_console` (Error) → clean. Do **not** save the scene (revert only).

---

## Task 2: Add the `DesertVolumeProfile` Volume to FlyScene

**Files:**
- Modify: `Assets/Scenes/FlyScene.unity` (add a `DesertLook` GameObject with a global `Volume`).

- [ ] **Step 1: Create the `DesertLook` GameObject + global Volume.**

`execute_code`:
```csharp
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if (scene.name != "FlyScene") return "load FlyScene first";
GameObject go = null;
foreach (var r in scene.GetRootGameObjects()) if (r.name == "DesertLook") { go = r; break; }
if (go == null) go = new GameObject("DesertLook");
var vol = go.GetComponent<UnityEngine.Rendering.Volume>() ?? go.AddComponent<UnityEngine.Rendering.Volume>();
vol.isGlobal = true;
vol.priority = 0f;
vol.weight = 1f; // cel default; the controller overrides per the saved pref at runtime
var profile = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>("Assets/Settings/DesertVolumeProfile.asset");
if (profile == null) return "DesertVolumeProfile not found";
vol.sharedProfile = profile;
UnityEditor.EditorUtility.SetDirty(go);
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
return $"DesertLook volume: isGlobal={vol.isGlobal} weight={vol.weight} profile={vol.sharedProfile.name}";
```
Expected: `... profile=DesertVolumeProfile`.

- [ ] **Step 2: Verify the grade at edit time (with the outline, to preview the full look).**

Temporarily set the camera renderer index to 1 (as in Task 1 Step 2), `manage_camera` screenshot `game_view` include_image, confirm outline **+** the warm/contrasty grade + bloom, then revert the camera index to -1 (Task 1 Step 4). This is a preview only.

- [ ] **Step 3: Save + compile.** `manage_scene` action `save`; `refresh_unity` (assets) + `read_console` (Error) → clean.

- [ ] **Step 4: Commit.**
```bash
cd "/Users/anon/My project"
git add Assets/Scenes/FlyScene.unity
git commit -m "feat(desert): add DesertVolumeProfile global volume to FlyScene (A3)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: `CelLookSettings` — the persisted preference (Core)

**Files:**
- Create: `Assets/Scripts/Core/CelLookSettings.cs`

- [ ] **Step 1: Write the settings static.** Create `Assets/Scripts/Core/CelLookSettings.cs`:
```csharp
using System;
using UnityEngine;

namespace CubeFly.Core
{
    // Persisted "desert cel look" graphics preference (PlayerPrefs-backed).
    // Applied by CubeFly.Desert.DesertLookController in FlyScene and toggled
    // from the SettingsMenu debug panel. Lives in Core so the UI never
    // depends on the experimental Desert namespace.
    public static class CelLookSettings
    {
        const string Key = "desert.celLook";
        static bool _loaded;
        static bool _enabled;

        // Fires whenever Enabled changes so the FlyScene controller can
        // re-apply the look live.
        public static event Action OnChanged;

        public static bool Enabled
        {
            get
            {
                if (!_loaded) { _enabled = PlayerPrefs.GetInt(Key, 1) != 0; _loaded = true; }
                return _enabled;
            }
            set
            {
                if (_loaded && _enabled == value) return;
                _enabled = value;
                _loaded = true;
                PlayerPrefs.SetInt(Key, value ? 1 : 0);
                OnChanged?.Invoke();
            }
        }
    }
}
```

- [ ] **Step 2: Compile-check.** `refresh_unity` (scripts, force, wait) + `read_console` (Error) → clean.

- [ ] **Step 3: Commit.**
```bash
cd "/Users/anon/My project"
git add Assets/Scripts/Core/CelLookSettings.cs Assets/Scripts/Core/CelLookSettings.cs.meta
git commit -m "feat(core): CelLookSettings — persisted desert cel-look preference (A3)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: `DesertLookController` — apply the preference in FlyScene

**Files:**
- Create: `Assets/Scripts/Desert/DesertLookController.cs`
- Modify: `Assets/Scenes/FlyScene.unity` (add the controller to `DesertLook`, wire refs)

- [ ] **Step 1: Write the controller.** Create `Assets/Scripts/Desert/DesertLookController.cs`:
```csharp
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using CubeFly.Core;

namespace CubeFly.Desert
{
    // Applies the CelLookSettings preference to FlyScene at runtime: swaps the
    // camera's URP renderer (default PC_Renderer <-> Desert_Renderer, which
    // carries the screen-space outline) and fades the DesertVolumeProfile
    // volume in/out. Re-applies live when the SettingsMenu toggle flips the
    // preference. No effect outside FlyScene (this component only exists here).
    public class DesertLookController : MonoBehaviour
    {
        [Tooltip("Global Volume carrying DesertVolumeProfile (grade + bloom).")]
        [SerializeField] Volume desertVolume;
        [Tooltip("Camera to switch renderers on. Falls back to Camera.main if unset.")]
        [SerializeField] Camera targetCamera;
        [Tooltip("PC_RPAsset renderer index for the cel look (Desert_Renderer = 1).")]
        [SerializeField] int celRendererIndex = 1;
        [Tooltip("PC_RPAsset renderer index for the as-is look (PC_Renderer / default = 0).")]
        [SerializeField] int asIsRendererIndex = 0;

        const string TAG = "DesertLook";

        void OnEnable()  { CelLookSettings.OnChanged += Apply; }
        void OnDisable() { CelLookSettings.OnChanged -= Apply; }

        void Start() { Apply(); }

        void Apply()
        {
            bool cel = CelLookSettings.Enabled;
            Camera cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam != null)
                cam.GetUniversalAdditionalCameraData().SetRenderer(cel ? celRendererIndex : asIsRendererIndex);
            if (desertVolume != null)
                desertVolume.weight = cel ? 1f : 0f;
            Debug.unityLogger.Log(TAG, $"Applied cel look = {cel}.");
        }
    }
}
```

- [ ] **Step 2: Compile-check.** `refresh_unity` (scripts, force, wait) + `read_console` (Error) → clean (confirms `GetUniversalAdditionalCameraData`/`SetRenderer` resolve).

- [ ] **Step 3: Add the controller to FlyScene's `DesertLook` GO + wire refs.**

`execute_code`:
```csharp
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
GameObject go = null;
foreach (var r in scene.GetRootGameObjects()) if (r.name == "DesertLook") { go = r; break; }
if (go == null) return "DesertLook GO missing (Task 2)";
var ctrl = go.GetComponent<CubeFly.Desert.DesertLookController>() ?? go.AddComponent<CubeFly.Desert.DesertLookController>();
var vol = go.GetComponent<UnityEngine.Rendering.Volume>();
Camera cam = null;
foreach (var c in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) { cam = c; break; }
var so = new UnityEditor.SerializedObject(ctrl);
so.FindProperty("desertVolume").objectReferenceValue = vol;
so.FindProperty("targetCamera").objectReferenceValue = cam;
so.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.EditorUtility.SetDirty(ctrl);
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
return $"controller wired: volume={(vol!=null)} camera={(cam!=null?cam.name:"NULL")}";
```
Expected: `volume=True camera=<name>`.

- [ ] **Step 4: Save + compile.** `manage_scene` `save`; `refresh_unity` + `read_console` (Error) → clean.

- [ ] **Step 5: Commit.**
```bash
cd "/Users/anon/My project"
git add Assets/Scripts/Desert/DesertLookController.cs Assets/Scripts/Desert/DesertLookController.cs.meta Assets/Scenes/FlyScene.unity
git commit -m "feat(desert): DesertLookController applies cel look (renderer + volume) in FlyScene (A3)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 5: Settings-menu toggle wired to `CelLookSettings`

Add one entry to `SettingsMenu`'s debug-toggle panel. Reuses the existing `(label, tooltip, getter, setter)` pattern; inserted at the **top** of the list so it's the prominent first toggle.

**Files:**
- Modify: `Assets/Scripts/Core/SettingsMenu.cs` (the `effects` array in `BuildDebugPanel`, ~line 345)

- [ ] **Step 1: Insert the cel-look entry at the head of the `effects` array.**

In `BuildDebugPanel`, the array literal currently starts:
```csharp
            var effects = new (string label, string tooltip,
                System.Func<bool> getter, System.Action<bool> setter)[]
            {
                ("Bloom",
```
Insert the cel-look tuple as the first element (before `("Bloom", ...)`):
```csharp
            var effects = new (string label, string tooltip,
                System.Func<bool> getter, System.Action<bool> setter)[]
            {
                ("Cel look (desert)",
                    "Desert stylised look in FlyScene: cel-shaded silhouettes with a black screen-space outline + warm colour grade. Toggling live-switches the FlyScene renderer.",
                    () => CelLookSettings.Enabled, v => CelLookSettings.Enabled = v),
                ("Bloom",
```
(`CelLookSettings` is in the same `CubeFly.Core` namespace as `SettingsMenu`, so no `using` is needed.)

- [ ] **Step 2: Compile-check.** `refresh_unity` (scripts, force, wait) + `read_console` (Error) → clean.

- [ ] **Step 3: Commit.**
```bash
cd "/Users/anon/My project"
git add Assets/Scripts/Core/SettingsMenu.cs
git commit -m "feat(core): add 'Cel look (desert)' toggle to the settings debug panel (A3)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 6: Live A/B + refinement + decision gate + push

The payoff — verify the live toggle and use it to refine, then gate. **Human-in-the-loop** (the headless MCP Play stays frozen, so the live switch + look judgment are the maintainer's).

**Files:** spec outcome + ROADMAP + memory; then push.

- [ ] **Step 1: Enter Play in FlyScene (maintainer).** Confirm: default **ON** (cel — outline + grade visible on load); no console errors; construct spawns normally.

- [ ] **Step 2: Toggle live.** ESC → Settings → flip "Cel look (desert)" off then on. Confirm the whole look switches **as-is ↔ cel with no hang/crash/flicker-to-black**, the toggle reflects state, and the `DesertLook: Applied cel look = …` log fires. (This is the spec §7 live-switch validation.) If the Deferred↔Forward live switch is janky, fall back to spec §7's single-Forward approach and note it.

- [ ] **Step 3: Persistence + scope.** Exit + re-enter Play → the last choice persists (PlayerPrefs). Open Settings in BuildScene → the toggle is present but harmless (no DesertLookController there; it just sets the pref).

- [ ] **Step 4: Refine (spec §5).** With the toggle live, judge + tune in-editor: outline `thickness` (1.5) on `Desert_Renderer.asset`; `ThrusterMat` metallic under the grade; grade strength; and whether the Lit ship reads OK vs wanting ship-cel (option-C follow-up). Apply any small tuning; re-verify.

- [ ] **Step 5: Record outcome + roadmap + memory.** Append an "A3 outcome" section (ship / iterate / shelve + findings, incl. the live-switch verdict and any option-C decision) to the spec; update `ROADMAP.md §4` (A3 → done + gate, point to A4); update `memory/project_desert_level.md` (A3 done; A4 next).

- [ ] **Step 6: Commit docs + push the branch.**
```bash
cd "/Users/anon/My project"
git add docs/superpowers/specs/2026-07-03-desert-flyscene-a3-design.md docs/superpowers/plans/2026-07-03-desert-flyscene-a3.md ROADMAP.md
git commit -m "docs(desert): record A3 outcome + roadmap (A3)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
git push
```
(First A3 push — bundles the spec, this plan, all task commits, and the outcome to draft PR #54.)

---

## Self-Review

**Spec coverage** (spec section → task):
- §3 renderer switch to Desert_Renderer (outline) → Task 1 (spike) + Task 4 (controller applies). ✓
- §3 DesertVolumeProfile volume → Task 2. ✓
- §4 DesertLookController (camera SetRenderer + volume weight, live re-apply) → Task 4. ✓
- §4 persisted preference (PlayerPrefs, default on) → Task 3 (`CelLookSettings`). ✓
- §4 settings toggle → Task 5. ✓ (Refinement: SettingsMenu, not PauseMenu — flagged.)
- §5 refinement loop → Task 6 Step 4. ✓
- §6 verification (default on, live toggle, persistence, scope, DesertSandbox intact) → Tasks 1–2 (edit-time look) + Task 6 (live/human). ✓
- §7 key risk (live Deferred↔Forward) → Task 1 spike (edit-time render) + Task 6 Step 2 (live) with the documented fallback. ✓

**Placeholder scan:** All code (both scripts, the array insert), MCP calls, commit commands, and expected outputs are concrete. The renderer indices (cel=1, asIs=0) are stated + serialized-tunable. No TBDs. ✓

**Type/name consistency:** `CelLookSettings.Enabled` / `.OnChanged` used identically in Tasks 3, 4, 5. `DesertLookController` fields `desertVolume` / `targetCamera` / `celRendererIndex` / `asIsRendererIndex` set in Task 4 Step 3 match the script in Step 1. The `DesertLook` GO name is consistent across Tasks 2 and 4. ✓
