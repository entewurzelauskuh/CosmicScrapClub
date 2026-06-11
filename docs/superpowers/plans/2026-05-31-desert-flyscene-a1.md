# Desert Terrain into FlyScene (A1) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace FlyScene's flat ground + targets with the desert dune basin (terrain only, current renderer) so the construct flies a believable desert, spawning safely in a hero-facing dune lane.

**Architecture:** Bundle the existing desert geometry (dune ground + perimeter ridge + 5 formation prefabs) into one new `DesertEnvironment` prefab; drop a single instance into FlyScene. A new `SpawnSurfacePlacer` component raycasts the terrain at `Awake` to seat the construct at a guaranteed-clear altitude. FlyScene's RenderSettings adopt the desert's warm ambient + gradient sky + a *subtle* distance fog. The cel renderer (A3) and target placement (A2) are out of scope.

**Tech Stack:** Unity 6.3 LTS / URP, C# MonoBehaviour, the New Input System. All scene/prefab/asset mutations go through the **UnityMCP** tools (`mcp__unityMCP__*`) against the live Editor — NOT by hand-editing `.unity`/`.prefab` YAML (fileID/GUID/prefab-modification edits are error-prone). The spec is `docs/superpowers/specs/2026-05-31-desert-flyscene-a1-design.md`.

---

## Working agreements (read once before Task 1)

- **Branch:** `explore/desert-flyscene`. All commits land here; never on `main`.
- **No automated tests exist** in this project (no asmdefs / EditMode / PlayMode). "Verify" means: compile clean via UnityMCP `read_console`, plus the manual Play-mode pass in Task 7. TDD's red/green is replaced by **compile-and-console-check** after each code/scene change.
- **The live Unity Editor runs on the main project root**, not necessarily this branch's checkout. If implementing in a worktree, the edited files must reach the root checkout the Editor has open before MCP scene/prefab/Play operations act on them. If working directly in the root checkout (recommended for A1, since it's scene-heavy and MCP-driven), this is automatic.
- **MCP compile/console loop** after any `.cs` change or before Play: `mcp__unityMCP__refresh_unity` (compile=request, mode=force, wait_for_ready=true) → if it returns "Connection closed", call `refresh_unity` again (a domain reload drops the bridge once) → `mcp__unityMCP__read_console` (types `["Error"]`). `MCP-FOR-UNITY: Client handler ...` console lines are tooling noise, not game errors.
- **`.gitignore` note:** the working tree carries pre-existing modified `.mat` files and untracked `Assets/Resources/`, `CLAUDE.md`, `unity_handoff/`. **Never `git add -A`.** Stage only the exact files named in each commit step.
- **Reference GUIDs** (verified from `.meta` on 2026-05-31):
  | Asset | GUID |
  |---|---|
  | `Formation_MesaArch.prefab` | `df940d82fd529454898929f591e5911f` |
  | `Formation_HoodooSpires.prefab` | `a6db95457104b463f87fdba87ba4358e` |
  | `Formation_SlotCanyon.prefab` | `fc3a87917a0364242bb2f0ff179cb641` |
  | `Formation_ButteRing.prefab` | `9ef6c5302123443b0b90652c7a076a7a` |
  | `Formation_FinField.prefab` | `ce80bb77c51644cf187e2bcc71ea42f5` |
  | `DesertGround.asset` (mesh) | `9237d5cf8ca344d9990b461d3407590d` |
  | `Sand.mat` | `57f054b79dcb04bbfbe572ab83f892f2` |
  | `DesertSky.mat` (gradient sky) | `77d63470f0b284cb98fccb940932f661` |
  | `Ground.prefab` (to remove) | `035d2678f177b4961a15c139c0ed33f8` |
- **Layers:** slots 0–8 are used (`Default`, `TransparentFX`, `Ignore Raycast`, —, `Water`, `UI`, `PlacedCube`, `AlphaCube`, `PreviewCube`). Slot 9+ free. We add a `World` layer (Task 1).
- **Formation positions** (from `desert_level_spec.md §4.3`, X,Z; Y comes from the dune surface): MesaArch (−50,+45) · HoodooSpires (+45,+60) · SlotCanyon (+55,0) · ButteRing (0,−50) · FinField (−50,−50).

---

## File Structure

**New files:**
- `Assets/Scripts/Desert/SpawnSurfacePlacer.cs` — one MonoBehaviour: `Awake`-time downward raycast that seats its GameObject at `surface + clearance`. Sole responsibility: spawn-height safety. (+ canonical `.meta`.)
- `Assets/Prefabs/Desert/DesertEnvironment.prefab` — the geometry bundle (ground + ridge + 5 formations). Authored via MCP. (+ `.meta`.)

**Modified files:**
- `ProjectSettings/TagManager.asset` — add `World` layer (via MCP `add_layer`).
- `Assets/Scenes/FlyScene.unity` — remove Ground/targets/turrets; add the `DesertEnvironment` instance; reposition + tag `CubeConstruct`; RenderSettings (ambient/fog/skybox) + Directional Light. All via MCP.

**Unchanged:** all Fly gameplay scripts, the other 3 scenes, `DesertSandbox.unity` (stays as the standalone reference), the desert source assets.

---

## Task 1: Add the `World` layer for terrain

**Files:**
- Modify: `ProjectSettings/TagManager.asset` (via MCP, slot 9)

- [ ] **Step 1: Confirm the layer is absent and a slot is free**

Run: Read `ProjectSettings/TagManager.asset` with the Read tool and confirm there is no `World` entry and slots 9–31 are blank (slots 0–8 are `Default`, `TransparentFX`, `Ignore Raycast`, —, `Water`, `UI`, `PlacedCube`, `AlphaCube`, `PreviewCube`).
Expected: no `World`; slot 9 empty.

- [ ] **Step 2: Add the layer**

Run (MCP): `mcp__unityMCP__manage_editor` with `action="add_layer"`, `layer_name="World"`.
Expected: success; the tool reports the assigned index (should be 9).

- [ ] **Step 3: Verify it persisted**

Run: Read `ProjectSettings/TagManager.asset`; confirm `World` now occupies the first free user slot (index 9).
Also run the compile/console loop is NOT needed (no scripts changed), but confirm `mcp__unityMCP__read_console` types `["Error"]` is clean.
Expected: `World` present at index 9; no console errors.

- [ ] **Step 4: Commit**

```bash
git add ProjectSettings/TagManager.asset
git commit -m "feat(desert): add World layer for terrain collision (A1)"
```

---

## Task 2: `SpawnSurfacePlacer` component

Seats the construct at a guaranteed-clear altitude by raycasting the terrain downward at `Awake` (before `FlyController.Start`/`BuildConstruct` instantiates the cubes). Gravity is off, so the construct then hovers at that height.

**Files:**
- Create: `Assets/Scripts/Desert/SpawnSurfacePlacer.cs`
- Verify: compile via MCP `read_console`

- [ ] **Step 1: Create the script via MCP**

Run (MCP): `mcp__unityMCP__create_script` with path `Assets/Scripts/Desert/SpawnSurfacePlacer.cs` and this exact content:

```csharp
using UnityEngine;

namespace CubeFly.Desert
{
    // Seats this GameObject at a safe altitude above the terrain at spawn, so
    // the construct can never spawn clipped into a dune, the ground mesh, or a
    // formation. Runs in Awake — BEFORE FlyController.Start instantiates the
    // alpha + placed cubes as children — so the cubes are built at the
    // corrected height. The construct's Rigidbody has gravity off, so after
    // placement it simply hovers here until the player thrusts.
    //
    // A1 (desert → FlyScene). Attach to the CubeConstruct GameObject in
    // FlyScene; it has no effect in scenes without terrain on `terrainMask`
    // (the fallback height is used and a warning logged).
    [DefaultExecutionOrder(-2000)]
    public class SpawnSurfacePlacer : MonoBehaviour
    {
        [Tooltip("Layers treated as terrain for the spawn raycast (set to World).")]
        [SerializeField] LayerMask terrainMask = ~0;

        [Tooltip("Height above the found surface to place the construct origin. Must clear the construct's own half-height plus a margin so no cube intersects terrain.")]
        [SerializeField] float clearance = 9f;

        [Tooltip("Y to start the downward ray from — well above any dune/formation peak.")]
        [SerializeField] float rayStartHeight = 200f;

        [Tooltip("Fallback Y if the ray finds no terrain under the spawn point (never spawn at an unknown height).")]
        [SerializeField] float fallbackHeight = 20f;

        const string TAG = "SpawnPlacer";

        void Awake()
        {
            Vector3 p = transform.position;
            Vector3 origin = new Vector3(p.x, rayStartHeight, p.z);

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                                 rayStartHeight * 2f, terrainMask, QueryTriggerInteraction.Ignore))
            {
                float y = hit.point.y + clearance;
                transform.position = new Vector3(p.x, y, p.z);
                Debug.unityLogger.Log(TAG,
                    $"Spawn seated at y={y:F2} (surface {hit.point.y:F2} + clearance {clearance:F1}) on '{hit.collider.name}'.");
            }
            else
            {
                transform.position = new Vector3(p.x, fallbackHeight, p.z);
                Debug.unityLogger.LogWarning(TAG,
                    $"No terrain under spawn ({p.x:F1}, {p.z:F1}) on the terrain mask — using fallback y={fallbackHeight:F1}.");
            }
        }
    }
}
```

- [ ] **Step 2: Confirm the canonical `.meta` exists**

`create_script` writes a `.meta`. Run: Read `Assets/Scripts/Desert/SpawnSurfacePlacer.cs.meta` and confirm it has a `MonoImporter` block with a non-zero `guid`. (Project routine — every script gets a full `.meta`; Unity's stub is acceptable here since MCP generated it.)
Expected: a `guid` line present.

- [ ] **Step 3: Compile and check console**

Run (MCP): `mcp__unityMCP__refresh_unity` (compile=request, scope=scripts, mode=force, wait_for_ready=true); on "Connection closed" call `refresh_unity` again; then `mcp__unityMCP__read_console` types `["Error"]`.
Expected: no compile errors referencing `SpawnSurfacePlacer`.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Desert/SpawnSurfacePlacer.cs Assets/Scripts/Desert/SpawnSurfacePlacer.cs.meta
git commit -m "feat(desert): SpawnSurfacePlacer — raycast-safe spawn altitude (A1)"
```

---

## Task 3: Author the `DesertEnvironment` prefab

Bundle the dune ground + perimeter ridge + 5 formations under one prefab root. Built via MCP from the existing assets.

**Files:**
- Create: `Assets/Prefabs/Desert/DesertEnvironment.prefab` (+ `.meta`)

- [ ] **Step 1: Create an empty root GameObject in a scratch context**

Author the prefab headlessly. Run (MCP): `mcp__unityMCP__manage_gameobject` with `action="create"`, `name="DesertEnvironment"`, `position=[0,0,0]`. This creates it in the active scene temporarily; we convert to a prefab and delete the scene copy.

> If the active scene is FlyScene, create it there transiently — it will be removed from the scene in Step 6 after the prefab asset is saved. Alternatively open `DesertSandbox.unity` first (`manage_scene load`) to crib exact transforms, then create in an empty scratch scene. Keep whichever scene was active before, restored at the end.

- [ ] **Step 2: Add the dune ground child**

Run (MCP): `mcp__unityMCP__manage_gameobject` `action="create"`, `name="DuneGround"`, `parent="DesertEnvironment"`, `position=[0,0,0]`, then add components via `manage_components` (or `components_to_add` on create): `MeshFilter`, `MeshRenderer`, `MeshCollider`.
Then set serialized refs:
- `MeshFilter.m_Mesh` → `{guid: 9237d5cf8ca344d9990b461d3407590d}` (DesertGround mesh)
- `MeshCollider.m_Mesh` → same mesh guid
- `MeshRenderer.m_Materials[0]` → `{guid: 57f054b79dcb04bbfbe572ab83f892f2}` (Sand.mat)
- Set `DuneGround` layer to `World`.

Expected: a ground object rendering the dune mesh with a MeshCollider.

- [ ] **Step 3: Add the five formation prefab instances as children**

Run (MCP): for each formation, `mcp__unityMCP__manage_gameobject` `action="create"` with `prefab_path` (instantiates the prefab) and `parent="DesertEnvironment"`, at the spec'd X,Z and the **Y the formation uses in DesertSandbox** (load DesertSandbox to read each instance's `m_LocalPosition`, or place at Y=0 and adjust so the base sits on the dune):

| Formation | prefab_path | position (x, y, z) |
|---|---|---|
| MesaArch | `Assets/Prefabs/Desert/Formation_MesaArch.prefab` | (−50, *fromSandbox*, +45) |
| HoodooSpires | `Assets/Prefabs/Desert/Formation_HoodooSpires.prefab` | (+45, *fromSandbox*, +60) |
| SlotCanyon | `Assets/Prefabs/Desert/Formation_SlotCanyon.prefab` | (+55, *fromSandbox*, 0) |
| ButteRing | `Assets/Prefabs/Desert/Formation_ButteRing.prefab` | (0, *fromSandbox*, −50) |
| FinField | `Assets/Prefabs/Desert/Formation_FinField.prefab` | (−50, *fromSandbox*, −50) |

Set each formation's layer (and children) to `World`. Confirm formation prefabs already carry MeshColliders (per `desert_level_spec.md §8`); if a formation's collider layer differs, set it to `World`.

> **Read the exact Y from DesertSandbox.** Load `Assets/Scenes/DesertSandbox.unity` via `manage_scene get_hierarchy` (include_transform=true) and copy each `Formation_*` world position verbatim, rather than guessing Y. This guarantees the formations sit on the dunes exactly as the demonstrator intended.

- [ ] **Step 4: Add the perimeter ridge**

The ridge is 20 `Ridge_00..19` pieces under a `PerimeterRidge` parent in DesertSandbox. Two options — pick the one that matches how the ridge is stored:
- **If `PerimeterRidge` is itself a prefab:** instantiate it like the formations (read its GUID from DesertSandbox/`.meta`).
- **If it's loose scene objects:** in DesertSandbox, select `PerimeterRidge`, and use `mcp__unityMCP__manage_prefabs` `action="create_from_gameobject"` to bake it into `Assets/Prefabs/Desert/PerimeterRidge.prefab` first, then instantiate that under `DesertEnvironment`.

Set the ridge (and children) layer to `World`. Position at (0,0,0) (its pieces carry their own offsets).

> Determine which case applies by `manage_scene get_hierarchy` on DesertSandbox and checking whether `PerimeterRidge` shows as a prefab instance. Record the choice in the commit message.

- [ ] **Step 5: Save as a prefab asset**

Run (MCP): `mcp__unityMCP__manage_prefabs` `action="create_from_gameobject"`, `target="DesertEnvironment"`, `prefab_path="Assets/Prefabs/Desert/DesertEnvironment.prefab"`, `allow_overwrite=false`.
Expected: prefab asset created with a new GUID; the scene object becomes an instance of it.

- [ ] **Step 6: Verify prefab contents + clean up scratch**

Run (MCP): `mcp__unityMCP__manage_prefabs` `action="get_hierarchy"`, `prefab_path="Assets/Prefabs/Desert/DesertEnvironment.prefab"`. Confirm: `DuneGround` + `PerimeterRidge` + 5 `Formation_*` all present, all on `World` layer.
If the prefab was authored in a scratch/FlyScene context, delete the transient scene instance now (`manage_gameobject action="delete"`) so no stray copy is committed — unless this IS the FlyScene instance you want (Task 4 handles the FlyScene instance explicitly, so delete here and re-add cleanly there).
Run `read_console` types `["Error"]` → clean.

- [ ] **Step 7: Commit**

```bash
git add Assets/Prefabs/Desert/DesertEnvironment.prefab Assets/Prefabs/Desert/DesertEnvironment.prefab.meta
# include PerimeterRidge.prefab (+ .meta) only if Step 4 created it:
# git add Assets/Prefabs/Desert/PerimeterRidge.prefab Assets/Prefabs/Desert/PerimeterRidge.prefab.meta
git commit -m "feat(desert): DesertEnvironment prefab (ground + ridge + 5 formations) (A1)"
```

---

## Task 4: Swap FlyScene contents — remove old world, add desert

**Files:**
- Modify: `Assets/Scenes/FlyScene.unity` (via MCP)

- [ ] **Step 1: Open FlyScene and snapshot its hierarchy**

Run (MCP): `mcp__unityMCP__manage_scene` `action="load"`, `path="Assets/Scenes/FlyScene.unity"`, then `action="get_hierarchy"` (include_transform=true). Record the names/ids of: `Ground`, the 20 `WorldTargetCube_*`, the 4 `Turret_*`/`AutoTurret` objects, `CubeConstruct`, `FlyController`, `Main Camera`, `Directional Light`, `FlyHUD`.
Expected: confirms the objects to remove still exist.

- [ ] **Step 2: Remove the old world objects**

Run (MCP): `mcp__unityMCP__manage_gameobject` `action="delete"` for: `Ground`, every `WorldTargetCube_*` (use `find_gameobjects` by name prefix to enumerate), and every turret object. Delete the turret **parents** if turrets are children of WorldTargetCubes (per the FlyScene diff, turrets were `AddedGameObjects` on world-cube prefab instances — deleting the cube removes its turret child).
Expected: hierarchy now has no Ground / target / turret objects.

- [ ] **Step 3: Add one DesertEnvironment instance**

Run (MCP): `mcp__unityMCP__manage_gameobject` `action="create"`, `prefab_path="Assets/Prefabs/Desert/DesertEnvironment.prefab"`, `position=[0,0,0]`, no parent (scene root).
Expected: a single `DesertEnvironment` instance at origin.

- [ ] **Step 4: Save the scene**

Run (MCP): `mcp__unityMCP__manage_scene` `action="save"`.
Run `read_console` types `["Error"]` → clean (no missing-script / null-ref from the deletions).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scenes/FlyScene.unity
git commit -m "feat(desert): swap FlyScene flat arena for DesertEnvironment instance (A1)"
```

---

## Task 5: Reposition + wire the construct spawn

**Files:**
- Modify: `Assets/Scenes/FlyScene.unity` (via MCP) — `CubeConstruct` transform, layer, + `SpawnSurfacePlacer`

- [ ] **Step 1: Move CubeConstruct to the hero-facing dune lane**

Run (MCP): `mcp__unityMCP__manage_gameobject` `action="modify"`, `target="CubeConstruct"`, `position=[25, 30, -40]` (an open lane in the +X/−Z quadrant; Y is provisional — `SpawnSurfacePlacer` overrides it at runtime, but set a sane editor value).
Then aim it at the Mesa+Arch hero at (−50,+45): `mcp__unityMCP__manage_gameobject` `action="look_at"`, `target="CubeConstruct"`, `look_at_target=[-50, 30, 45]`, `look_at_up=[0,1,0]`.

> Verify the chosen XZ (25,−40) does not sit inside a formation footprint (SlotCanyon spans ~(+55,0)±; ButteRing ~(0,−50); the (25,−40) lane should be clear — confirm against the Task 4 hierarchy snapshot). If it overlaps, nudge to a clear lane and re-`look_at`.

- [ ] **Step 2: Attach SpawnSurfacePlacer and set its mask**

Run (MCP): `mcp__unityMCP__manage_components` `action="add"` on `CubeConstruct`, component `CubeFly.Desert.SpawnSurfacePlacer`. Then `set_property`: `terrainMask` → the `World` layer (layer index 9 as a mask = 512), `clearance` → 9, `fallbackHeight` → 20.
Expected: component present with the World mask.

- [ ] **Step 3: Save + sanity-check execution order**

Run (MCP): `manage_scene action="save"`. Confirm `SpawnSurfacePlacer` (`[DefaultExecutionOrder(-2000)]`) runs before `FlyController` (default order 0) — its `Awake` re-seats `CubeConstruct.transform` before `FlyController.Start` reads it. (No code change; the attribute guarantees this.)
Run `read_console` types `["Error"]` → clean.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scenes/FlyScene.unity
git commit -m "feat(desert): spawn construct in hero-facing dune lane with safe-altitude placer (A1)"
```

---

## Task 6: Desert atmosphere — ambient, sky, subtle fog

Apply the desert's lighting feel under the **current renderer** (no post-FX swap). FlyScene RenderSettings are edited via MCP (`execute_code` is the reliable path for RenderSettings, which `manage_gameobject` doesn't cover).

**Files:**
- Modify: `Assets/Scenes/FlyScene.unity` RenderSettings + Directional Light (via MCP)

- [ ] **Step 1: Read the desert scene's exact lighting values**

Already captured (from `DesertSandbox.unity` RenderSettings on 2026-05-31):
- Ambient: `m_AmbientMode: 0` (trilight); Sky `(0.212,0.227,0.259)`, Equator `(0.114,0.125,0.133)`, Ground `(0.047,0.043,0.035)`, Intensity 1.
- Skybox material: `DesertSky.mat` guid `77d63470f0b284cb98fccb940932f661`.
- Desert fog: Linear, color `(0.91,0.81,0.62)`, start 45, end 280. **We override start to ~90 for "subtle".**

- [ ] **Step 2: Apply RenderSettings via execute_code**

Run (MCP): `mcp__unityMCP__execute_code` with:

```csharp
var sky = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(
    UnityEditor.AssetDatabase.GUIDToAssetPath("77d63470f0b284cb98fccb940932f661"));
RenderSettings.skybox = sky;
RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
RenderSettings.ambientSkyColor      = new Color(0.212f, 0.227f, 0.259f);
RenderSettings.ambientEquatorColor  = new Color(0.114f, 0.125f, 0.133f);
RenderSettings.ambientGroundColor   = new Color(0.047f, 0.043f, 0.035f);
RenderSettings.ambientIntensity = 1f;
RenderSettings.fog = true;
RenderSettings.fogMode = FogMode.Linear;
RenderSettings.fogColor = new Color(0.91f, 0.81f, 0.62f);
RenderSettings.fogStartDistance = 90f;   // subtle: near/mid crystal clear
RenderSettings.fogEndDistance = 280f;
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
    UnityEngine.SceneManagement.SceneManager.GetActiveScene());
return "RenderSettings applied";
```
Expected: `"RenderSettings applied"`.

- [ ] **Step 3: Match the Directional Light to a warm desert sun (optional but recommended)**

Run (MCP): `mcp__unityMCP__manage_gameobject` `action="modify"`, `target="Directional Light"`, `rotation=[50,-30,0]` (≈45–60° elevation per spec §5.4), and set its Light color warm-white via `manage_components set_property` `m_Color` → `(1.0, 0.96, 0.88)`. Skip if the existing light already reads warm; record the decision.

- [ ] **Step 4: Save + verify**

Run (MCP): `manage_scene action="save"`. Then `mcp__unityMCP__manage_camera action="screenshot"` (or `scene_view_frame`) to eyeball: warm sky, dunes visible, far formations gently hazed (NOT a fog wall). If fog reads too strong/weak, adjust `fogStartDistance` (higher = subtler) and re-save.
Run `read_console` types `["Error"]` → clean.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scenes/FlyScene.unity
git commit -m "feat(desert): warm ambient + gradient sky + subtle distance fog in FlyScene (A1)"
```

---

## Task 7: Play-mode verification + decision gate

No automated tests — this is the manual gate from spec §9.

**Files:** none modified (verification only)

- [ ] **Step 1: Compile clean**

Run (MCP): `refresh_unity` (compile=request, mode=force, wait_for_ready=true); `read_console` types `["Error"]`.
Expected: zero errors (MCP tooling lines are fine).

- [ ] **Step 2: Enter Play mode in FlyScene**

Ensure FlyScene is the active scene (`manage_scene get_active`); if not, `manage_scene load` it. Run (MCP): `mcp__unityMCP__manage_editor action="play"`. Wait, then `read_console` types `["Error","Warning"]`.
Expected: the `SpawnPlacer` log line `Spawn seated at y=...`; NO missing-script / NullReference / "referenced script ... is missing" errors.

- [ ] **Step 3: Confirm spawn is clear and hovering**

Run (MCP): `manage_camera action="screenshot"` (game view) and/or `find_gameobjects` to read `CubeConstruct` world Y. Confirm the construct sits above the dune surface in the lane (Y ≈ surface + 9), is not intersecting any dune/formation, and is stationary (gravity off).
Expected: ship visibly clear of terrain, oriented toward the mesa.

- [ ] **Step 4: Fly the basin (human-in-the-loop)**

The implementer (or user) flies with WASD/Space/C + arrows + Q/E for a minute. Judge against spec §9: navigation reads well at ship scale; canyons/arches/fins are flyable; formations read at distance through the subtle fog; the ridge contains the sides; the ship bounces off terrain (doesn't pass through). Note any issue.

- [ ] **Step 5: Stop Play mode**

Run (MCP): `mcp__unityMCP__manage_editor action="stop"`.

- [ ] **Step 6: Record the decision gate**

Append a short "A1 outcome" note (ship / iterate / shelve + observations) to the spec file `docs/superpowers/specs/2026-05-31-desert-flyscene-a1-design.md`, then:

```bash
git add docs/superpowers/specs/2026-05-31-desert-flyscene-a1-design.md
git commit -m "docs(desert): record A1 play-test outcome + decision gate"
```

- [ ] **Step 7: Push the branch and open a draft PR (optional, for review)**

```bash
git push -u origin explore/desert-flyscene
gh pr create --base main --head explore/desert-flyscene --draft \
  --title "[experimental] Desert terrain into FlyScene (A1)" \
  --body "Milestone A1 per docs/superpowers/plans/2026-05-31-desert-flyscene-a1.md. Experimental — terrain only, current renderer. A2 (targets) + A3 (cel look) follow. Draft pending the decision gate."
```

---

## Self-review notes (coverage check)

Spec §-by-§ → task mapping (all covered):
- §3 DesertEnvironment prefab → Task 3.
- §4 Layer & collision → Task 1 (`World` layer) + Tasks 3/5 (apply layer; mask). **In-editor verification** of projectile-mask non-interaction happens in Task 7 (terrain on `World` is outside the `PlacedCube|AlphaCube` projectile masks, so bullets/laser won't damage it — confirm no errors when firing during the fly test).
- §5 Spawn placement + safe altitude → Task 2 (component) + Task 5 (placement/wiring).
- §6 Atmosphere → Task 6.
- §7 Bounds (ridge-only, no code) → satisfied by Task 3/4 (ridge colliders present); Camera unchanged (no task needed — explicitly verified in spec).
- §8 Removed targets/turrets → Task 4.
- §9 Verification + decision gate → Task 7.
- §11 File manifest → matches Tasks 1–3 new files + scene edits.

Known in-editor unknowns deliberately left for the implementer to resolve against the live scene (not guessable from YAML): exact formation Y (read from DesertSandbox in Task 3 Step 3), whether `PerimeterRidge` is a prefab or loose objects (Task 4 Step 4), the precise clear-lane XZ (Task 5 Step 1), and the final fog start value (Task 6 Step 4). Each task says how to resolve its unknown in-editor.
