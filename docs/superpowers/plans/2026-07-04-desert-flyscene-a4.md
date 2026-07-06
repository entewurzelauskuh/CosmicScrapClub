# Desert FlyScene A4 — Land the Desert Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close out the desert arc — remove the exploration scaffolding, make the desert's long shadow distance scene-local, rewrite the docs from "detached demonstrator" to "integrated level", and merge PR #54 to `main`.

**Architecture:** Delete `DesertSandbox`/`FreeFlyCamera`/the orphan 200 u mesh. Duplicate `PC_RPAsset` → `Desert_RPAsset` (shadowDistance 300), restore the global `PC_RPAsset` to 50, and switch to the desert pipeline only while FlyScene is loaded via a tiny `ScenePipelineOverride` (Core) that sets `QualitySettings.renderPipeline` in `OnEnable` and restores it in `OnDisable`. Then docs + a gated merge.

**Tech Stack:** Unity 6.3 / URP, MonoBehaviour, `Assembly-CSharp`, namespaces `CubeFly.Core` / `.Desert`. All edits via UnityMCP against the **main project root** (branch `explore/desert-flyscene` checked out there; live Editor runs against it — not a worktree). Verification = `read_console` + Play-mode + doc review + `gh`. **The merge is a gated final step — do not merge without an explicit maintainer go.**

**Spec:** `docs/superpowers/specs/2026-07-04-desert-flyscene-a4-design.md`

---

## File Structure

| File | Responsibility | Change |
|---|---|---|
| `Assets/Scripts/Core/ScenePipelineOverride.cs` | While enabled (its scene is loaded), set `QualitySettings.renderPipeline` to a serialized asset; restore on disable. | **Create**. |
| `Assets/Settings/Desert_RPAsset.asset` | Desert URP pipeline (same renderer list as PC, `shadowDistance 300`). | **Create** (duplicate of `PC_RPAsset`). |
| `Assets/Settings/PC_RPAsset.asset` | Global default pipeline; `shadowDistance` 300 → **50**. | **Modify**. |
| `Assets/Scenes/FlyScene.unity` | `ScenePipelineOverride` on `DesertLook` → `Desert_RPAsset`. | **Modify**. |
| `docs/desert_level_spec.md` | Demonstrator → integrated FlyScene desert level. | **Rewrite**. |
| `docs/full_architecture.md` | Add the desert files to the map; drop `FreeFlyCamera`. | **Modify**. |
| `ROADMAP.md` | Milestone A complete. | **Modify**. |
| `DesertSandbox.unity`, `FreeFlyCamera.cs`, `DesertGround.asset` (+ `.meta`s) | Scaffolding. | **Remove**. |

---

## Task 1: Cleanup — delete the exploration scaffolding

**Files:** remove `Assets/Scenes/DesertSandbox.unity`, `Assets/Scripts/Desert/FreeFlyCamera.cs`, `Assets/Models/DesertGround.asset` (each + `.meta`).

- [ ] **Step 1: Ensure FlyScene is loaded (not DesertSandbox).**

`manage_scene` action `get_active` → confirm `FlyScene`. If not, save nothing and `load` `Assets/Scenes/FlyScene.unity`.

- [ ] **Step 2: Re-verify no live references before deleting.**

Run:
```bash
cd "/Users/anon/My project"
echo "--- FreeFlyCamera refs (expect only DesertSandbox.unity + the script itself) ---"
grep -rl "FreeFlyCamera" Assets --include=*.unity --include=*.prefab --include=*.asset --include=*.cs
echo "--- DesertGround.asset (200) guid refs (expect only DesertSandbox) ---"
DG_GUID=$(grep -m1 guid Assets/Models/DesertGround.asset.meta | awk '{print $2}')
echo "guid=$DG_GUID"; grep -rl "$DG_GUID" Assets --include=*.unity --include=*.prefab --include=*.asset | grep -v "DesertGround.asset"
```
Expected: FreeFlyCamera only in `DesertSandbox.unity` (+ `FreeFlyCamera.cs`); the 200 u `DesertGround` guid only in `DesertSandbox.unity`. If anything else appears, STOP and re-scope.

- [ ] **Step 3: Delete the three assets via the AssetDatabase** (removes asset + `.meta` + DB entry cleanly).

`execute_code`:
```csharp
string[] paths = {
  "Assets/Scenes/DesertSandbox.unity",
  "Assets/Scripts/Desert/FreeFlyCamera.cs",
  "Assets/Models/DesertGround.asset",
};
var sb = new System.Text.StringBuilder();
foreach (var p in paths) sb.Append($"{p}: {(UnityEditor.AssetDatabase.DeleteAsset(p) ? "deleted" : "FAILED")}; ");
UnityEditor.AssetDatabase.Refresh();
return sb.ToString();
```
Expected: all three `deleted`.

- [ ] **Step 4: Remove DesertSandbox from Build Settings if present.**

`execute_code`:
```csharp
var scenes = new System.Collections.Generic.List<UnityEditor.EditorBuildSettingsScene>(UnityEditor.EditorBuildSettings.scenes);
int before = scenes.Count;
scenes.RemoveAll(s => s.path.Contains("DesertSandbox"));
UnityEditor.EditorBuildSettings.scenes = scenes.ToArray();
return $"build scenes {before} -> {scenes.Count}";
```

- [ ] **Step 5: Compile/console + all-scenes check.**

`refresh_unity` (all, force) + `read_console` (Error) → clean. Then open each scene and check console clean (no missing script/asset): `manage_scene` `load` for `MainMenu`, `HangarSelect`, `BuildScene`, then back to `FlyScene`; after each, `read_console` (Error). Expected: no errors referencing the deleted files.

- [ ] **Step 6: Commit.**

```bash
cd "/Users/anon/My project"
git add -A Assets/Scenes/DesertSandbox.unity Assets/Scenes/DesertSandbox.unity.meta Assets/Scripts/Desert/FreeFlyCamera.cs Assets/Scripts/Desert/FreeFlyCamera.cs.meta Assets/Models/DesertGround.asset Assets/Models/DesertGround.asset.meta ProjectSettings/EditorBuildSettings.asset
git commit -m "chore(desert): drop exploration scaffolding — DesertSandbox, FreeFlyCamera, 200u ground (A4)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: Shadow assets — `Desert_RPAsset` + restore `PC_RPAsset`

**Files:** create `Assets/Settings/Desert_RPAsset.asset`; modify `Assets/Settings/PC_RPAsset.asset`.

- [ ] **Step 1: Duplicate PC_RPAsset → Desert_RPAsset, set 300 / restore 50.**

`execute_code`:
```csharp
const string src = "Assets/Settings/PC_RPAsset.asset";
const string dst = "Assets/Settings/Desert_RPAsset.asset";
if (!UnityEditor.AssetDatabase.CopyAsset(src, dst)) return "CopyAsset failed";
UnityEditor.AssetDatabase.Refresh();
var desert = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(dst);
var pc = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(src);
var dSo = new UnityEditor.SerializedObject(desert); dSo.FindProperty("m_ShadowDistance").floatValue = 300f; dSo.ApplyModifiedPropertiesWithoutUndo(); UnityEditor.EditorUtility.SetDirty(desert);
var pSo = new UnityEditor.SerializedObject(pc);     pSo.FindProperty("m_ShadowDistance").floatValue = 50f;  pSo.ApplyModifiedPropertiesWithoutUndo(); UnityEditor.EditorUtility.SetDirty(pc);
UnityEditor.AssetDatabase.SaveAssets();
return $"Desert_RPAsset.shadowDistance={new UnityEditor.SerializedObject(desert).FindProperty("m_ShadowDistance").floatValue}; PC_RPAsset.shadowDistance={new UnityEditor.SerializedObject(pc).FindProperty("m_ShadowDistance").floatValue}";
```
Expected: `Desert_RPAsset.shadowDistance=300; PC_RPAsset.shadowDistance=50`.

- [ ] **Step 2: Confirm Desert_RPAsset carries the same renderer list** (so the cel toggle's indices stay valid).

```bash
cd "/Users/anon/My project"
echo "--- PC renderer list ---";     grep -A6 "m_RendererDataList" Assets/Settings/PC_RPAsset.asset | head -8
echo "--- Desert renderer list ---"; grep -A6 "m_RendererDataList" Assets/Settings/Desert_RPAsset.asset | head -8
```
Expected: identical `{fileID..., guid...}` entries for `PC_Renderer` (index 0) + `Desert_Renderer` (index 1).

- [ ] **Step 3: Commit.**

```bash
cd "/Users/anon/My project"
git add Assets/Settings/Desert_RPAsset.asset Assets/Settings/Desert_RPAsset.asset.meta Assets/Settings/PC_RPAsset.asset
git commit -m "feat(desert): Desert_RPAsset (shadow 300) + restore global PC_RPAsset to 50 (A4)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: `ScenePipelineOverride` component + wire into FlyScene

**Files:** create `Assets/Scripts/Core/ScenePipelineOverride.cs`; modify `Assets/Scenes/FlyScene.unity`.

- [ ] **Step 1: Write the component.** Create `Assets/Scripts/Core/ScenePipelineOverride.cs`:
```csharp
using UnityEngine;
using UnityEngine.Rendering;

namespace CubeFly.Core
{
    // Makes a specific URP pipeline asset active while this component is enabled
    // (i.e. while its scene is loaded), restoring the previous one on disable.
    // FlyScene uses it to run the desert pipeline (Desert_RPAsset — long shadow
    // distance for the 500u basin) without changing the global default that the
    // Menu/Hangar/Build scenes render with.
    public class ScenePipelineOverride : MonoBehaviour
    {
        [Tooltip("Pipeline asset to make active while this scene is loaded (e.g. Desert_RPAsset).")]
        [SerializeField] RenderPipelineAsset overrideAsset;

        RenderPipelineAsset _previous;
        bool _applied;

        void OnEnable()
        {
            if (overrideAsset == null) return;
            _previous = QualitySettings.renderPipeline;
            QualitySettings.renderPipeline = overrideAsset;
            _applied = true;
        }

        void OnDisable()
        {
            if (!_applied) return;
            QualitySettings.renderPipeline = _previous;
            _applied = false;
        }
    }
}
```

- [ ] **Step 2: Compile-check.** `refresh_unity` (scripts, force, wait) + `read_console` (Error) → clean.

- [ ] **Step 3: Add it to FlyScene's `DesertLook` GO + assign `Desert_RPAsset`.**

`execute_code`:
```csharp
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if (scene.name != "FlyScene") return "load FlyScene first";
GameObject go = null; foreach (var r in scene.GetRootGameObjects()) if (r.name == "DesertLook") { go = r; break; }
if (go == null) return "DesertLook GO missing";
var comp = go.GetComponent<CubeFly.Core.ScenePipelineOverride>() ?? go.AddComponent<CubeFly.Core.ScenePipelineOverride>();
var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.RenderPipelineAsset>("Assets/Settings/Desert_RPAsset.asset");
var so = new UnityEditor.SerializedObject(comp);
so.FindProperty("overrideAsset").objectReferenceValue = asset;
so.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.EditorUtility.SetDirty(comp);
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
return $"ScenePipelineOverride wired: overrideAsset={(asset!=null?asset.name:"NULL")}";
```
Expected: `overrideAsset=Desert_RPAsset`.

- [ ] **Step 4: Save + compile.** `manage_scene` `save`; `refresh_unity` + `read_console` (Error) → clean.

- [ ] **Step 5: Commit.**

```bash
cd "/Users/anon/My project"
git add Assets/Scripts/Core/ScenePipelineOverride.cs Assets/Scripts/Core/ScenePipelineOverride.cs.meta Assets/Scenes/FlyScene.unity
git commit -m "feat(core): ScenePipelineOverride — desert pipeline active only in FlyScene (A4)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: Verify the shadow split

- [ ] **Step 1: Headless — the override applies in Play + restores on stop.**

Enter Play in FlyScene (`manage_editor` play). `execute_code`:
```csharp
return $"active={((QualitySettings.renderPipeline ?? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline)?.name)}";
```
Expected: `active=Desert_RPAsset`. Stop Play (`manage_editor` stop); re-run the same `execute_code` → expected `active=PC_RPAsset` (restored). This confirms `OnEnable`/`OnDisable` swap + restore.

- [ ] **Step 2: On-disk values.** `read` (grep) `Assets/Settings/PC_RPAsset.asset` and `Desert_RPAsset.asset` → `m_ShadowDistance: 50` and `300` respectively.

- [ ] **Step 3: Human Play sanity (maintainer).** FlyScene → far formations cast shadows (no pop). Load MainMenu / HangarSelect / BuildScene → shadows are crisp again (short distance), the ship in Hangar/Build no longer softly-shadowed. Return FlyScene → MainMenu confirms no leaked 300 u. (No commit — verification only.)

---

## Task 5: Docs — demonstrator → integrated level

**Files:** rewrite `docs/desert_level_spec.md`; modify `docs/full_architecture.md`, `ROADMAP.md`.

- [ ] **Step 1: Rewrite `docs/desert_level_spec.md`.** Replace the "standalone demonstrator, deliberately detached" framing with the **integrated FlyScene desert level**. Target structure:
  - *Purpose:* the desert is a FlyScene level flown with the construct/ship flight system (no longer a detached sandbox).
  - *What shipped (A1–A4):* 500 u basin (dune ground + 64-piece perimeter ridge + 5 ProBuilder formations, `World` layer); combat layout (21 `DesertTarget`s + 3 `Turret`s, `SurfaceSnap` seating); cel look (`Desert_Renderer` outline + `DesertVolumeProfile`) as a live `SettingsMenu` toggle (`CelLookSettings` + `DesertLookController`); desert-local shadow pipeline (`Desert_RPAsset` + `ScenePipelineOverride`).
  - *Key files:* the scripts/prefabs/shaders/renderer/pipeline (list them).
  - *Known cosmetics:* turret +Y-aim tilt (accepted).
  - *History:* one line per sub-phase A1/A1.5/A2/A3/A4 with pointers to `docs/superpowers/specs/2026-*-desert-flyscene-*`.
  Remove the DesertSandbox / FreeFlyCamera / 200 u references (deleted).

- [ ] **Step 2: Update `docs/full_architecture.md`.** In the file-by-file map add: `Scripts/Desert/` — `SurfaceSnap`, `DesertLookController`, `OutlineRendererFeature`, `DuneGroundGenerator` (and **remove** the `FreeFlyCamera` entry); `Scripts/Core/` — `CelLookSettings`, `ScenePipelineOverride`; `Assets/Prefabs/Desert/` — `DesertEnvironment`, `DesertTarget`, `Turret`; `Assets/Settings/` — `Desert_Renderer`, `Desert_RPAsset`; `Assets/Shaders/` — `CelShaded`, `OutlineEdgeDetect`, `GradientSkybox`; `DesertVolumeProfile`; and note FlyScene now hosts the `DesertEnvironment` instance + `DesertTargets` + `DesertLook`.

- [ ] **Step 3: Update `ROADMAP.md`.** Mark Milestone A **complete** (A1–A4 done — the desert has landed and merged); point to Milestone B (`unity_handoff/` UI rebrand) as next.

- [ ] **Step 4: Commit.**

```bash
cd "/Users/anon/My project"
git add docs/desert_level_spec.md docs/full_architecture.md ROADMAP.md
git commit -m "docs(desert): rewrite level spec (demonstrator -> integrated) + sync arch map + roadmap (A4)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

- [ ] **Step 5: Surface `CLAUDE.md` + `README.md` to the maintainer** (do NOT auto-commit). `CLAUDE.md` is untracked and still calls the desert "experimental / throwaway evaluation tooling" — propose the corrected wording and let the maintainer decide whether to commit it. Check `README.md`'s scene list / controls; propose a one-line desert note if warranted.

---

## Task 6: Final verify + push + gated merge

- [ ] **Step 1: Full compile/console sanity.** `refresh_unity` (all, force) + `read_console` (Error) → clean across the project.

- [ ] **Step 2: Record the A4 outcome** in the spec (append an "A4 outcome — LANDED" section) and commit it with the roadmap if not already. (Bundle into Task 5's docs commit if not yet pushed.)

- [ ] **Step 3: Push the branch.**
```bash
cd "/Users/anon/My project"
git push
```

- [ ] **Step 4: GATE — confirm with the maintainer before merging.** Present the final state (all A4 commits pushed, verification green) and get an **explicit go**. Offer a final Copilot review first if they want one. Do not proceed to Step 5 without it.

- [ ] **Step 5: Mark PR ready + merge to `main`.**
```bash
cd "/Users/anon/My project"
gh pr ready 54
gh pr merge 54 --merge
```
(Repo convention is merge commits. Leave the branch un-deleted unless the maintainer asks.) Confirm `main` now contains the desert; report the merge commit.

---

## Self-Review

**Spec coverage** (spec section → task):
- §3 cleanup (delete 3 assets, build settings, no-ref check) → Task 1. ✓
- §4 shadow split (Desert_RPAsset 300, PC_RPAsset 50, ScenePipelineOverride) → Tasks 2 + 3, verified Task 4. ✓
- §5 turrets (accept) → no task; documented in Task 5 Step 1. ✓
- §6 docs (spec rewrite, arch map, roadmap, CLAUDE.md/README flag) → Task 5. ✓
- §7 verification + merge → Tasks 4 + 6 (merge gated). ✓

**Placeholder scan:** All code (`ScenePipelineOverride`), MCP/execute_code snippets, grep/git/gh commands, and expected outputs are concrete. The doc rewrite (Task 5) specifies the target structure + exact additions rather than prose (appropriate for a content-transformation task). No TBDs. ✓

**Type/name consistency:** `ScenePipelineOverride` + its `overrideAsset` field used identically in Tasks 3–4; `Desert_RPAsset` / `PC_RPAsset` names + the 300/50 values consistent across Tasks 2–4; `DesertLook` GO name matches A3. ✓
