# Scale the Desert Basin to 500×500 (A1.5) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Grow the FlyScene desert from 200×200 to a vast 500×500 basin (regenerated dune ground, ×1.2 formations pushed to the rim, an expanded ridge) so large player constructs have room to fly.

**Architecture:** All geometry lives in the `DesertEnvironment` prefab. Edit it **inside the prefab stage** (so every instance inherits the change and we don't fight per-instance override records), via UnityMCP `execute_code` for the parametric work (regenerate mesh, scale/reposition formations, duplicate ridge pieces, raycast-snap Y). Only the construct spawn is a FlyScene edit. The shared `DesertGround.asset` is left untouched — we bake a NEW `DesertGround_500.asset` so the standalone `DesertSandbox.unity` reference scene keeps its 200 mesh.

**Tech Stack:** Unity 6.3 / URP, `DuneGroundGenerator` (procedural mesh, step = size/resolution), ProBuilder rock meshes, UnityMCP (`manage_prefabs` open/save prefab stage, `execute_code`, `manage_scene`, `manage_camera`, `read_console`, `refresh_unity`). The spec is `docs/superpowers/specs/2026-06-11-desert-flyscene-a1.5-design.md`.

---

## Working agreements (read once before Task 1)

- **Branch:** `explore/desert-flyscene`. Never commit to `main`.
- **No automated tests** — "verify" = compile/console clean + the manual Play-mode re-fly (Task 6).
- **Editor runs on the main root checkout** = this branch. MCP acts on the branch's files.
- **MCP compile/console loop** when needed: `refresh_unity` (compile=request, mode=force, wait_for_ready=true) → on "Connection closed" call `refresh_unity` again → `read_console` (types `["Error"]`). `MCP-FOR-UNITY: Client handler ...` lines are tooling noise.
- **Stage surgically.** The working tree carries pre-existing modified `.mat` files + untracked `Assets/Resources/`, `CLAUDE.md`, `unity_handoff/`, `docs/code_review_2026-06-11_full_audit.md`. **Never `git add -A`.** Add only the files each commit step names.
- **Prefab-stage editing is the core technique.** To change the prefab's geometry: `manage_prefabs action=open_prefab_stage prefab_path=...`, do the edits (objects resolve by path inside the stage, e.g. `DuneGround`, `Formation_MesaArch`), then `manage_prefabs action=save_prefab_stage`, then `action=close_prefab_stage`. Changes propagate to the FlyScene instance automatically.
- **The `DuneGroundGenerator` editor "Generate Dune Mesh" button must NOT be used** — it `CopySerialized`s into the existing `DesertGround.asset` in place, which would clobber the 200 mesh DesertSandbox still uses. Task 1 bakes a new asset directly via `execute_code`.
- **Reference facts (verified 2026-06-11):**
  - Prefab: `Assets/Prefabs/Desert/DesertEnvironment.prefab`, guid `f94ee2467c76b4ac5a7ea859b6f08b0f`, Regular, 59 children. Root children: `DuneGround`, `PerimeterRidge` (nested prefab `b4294d81a9a3a4a17b80caca5da2f20e`, 20 `Ridge_00..19`), `Formation_MesaArch/HoodooSpires/SlotCanyon/ButteRing/FinField` (nested prefab instances).
  - `DuneGround` has `MeshFilter`, `MeshRenderer`, `MeshCollider`, `DuneGroundGenerator` (size 200, resolution 200, seed 12345, meshAssetPath `Assets/Models/DesertGround.asset`).
  - Shared mesh `DesertGround.asset` guid `9237d5cf8ca344d9990b461d3407590d` (referenced by the prefab AND DesertSandbox — do NOT overwrite).
  - `World` layer = index 9 (mask 512). All terrain colliders must stay on it.
  - Current formation centers/sizes (XZ, base at ground ≈ y−6): MesaArch (−50,45) 69×36; Hoodoo (45,60) 39×32; SlotCanyon (55,0) 57×95; ButteRing (0,−50) 109×108; FinField (−50,−50) 38×49.
  - Ridge pieces ring at |X| or |Z| ≈ 108, per-piece Y 14–23, varied rotation.

---

## File Structure

**New:**
- `Assets/Models/DesertGround_500.asset` (+ `.meta`) — regenerated 500×500 dune mesh.

**Modified:**
- `Assets/Prefabs/Desert/DesertEnvironment.prefab` — DuneGround generator params + mesh/collider repoint; formations scaled/repositioned/Y-reseated; ridge expanded to ~50 pieces.
- `Assets/Scenes/FlyScene.unity` — `CubeConstruct` spawn position/rotation (and possibly a fog-start tweak after the re-fly).

**Unchanged:** `DesertGround.asset` (original 200 mesh), `DesertSandbox.unity`, `DuneGroundGenerator.cs`, `SpawnSurfacePlacer.cs`, `World` layer, all gameplay scripts.

---

## Task 1: Regenerate the ground at 500×500 → new asset

Bake a new dune mesh without touching the shared 200 asset, set the generator params for reproducibility, and repoint the prefab's DuneGround.

**Files:**
- Create: `Assets/Models/DesertGround_500.asset`
- Modify: `Assets/Prefabs/Desert/DesertEnvironment.prefab` (DuneGround)

- [ ] **Step 1: Open the prefab stage**

Run (MCP): `mcp__unityMCP__manage_prefabs` `action="open_prefab_stage"`, `prefab_path="Assets/Prefabs/Desert/DesertEnvironment.prefab"`.
Expected: success; the prefab opens as the active stage.

- [ ] **Step 2: Set generator params, bake a NEW mesh asset, repoint MeshFilter + MeshCollider**

Run (MCP): `mcp__unityMCP__execute_code` with:

```csharp
// Inside the DesertEnvironment prefab stage.
var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
if (stage == null) return "ERROR: not in a prefab stage";
var dune = stage.prefabContentsRoot.transform.Find("DuneGround");
if (dune == null) return "ERROR: DuneGround not found";
var gen = dune.GetComponent<CubeFly.Desert.DuneGroundGenerator>();
gen.size = 500f;
gen.resolution = 600;
gen.meshAssetPath = "Assets/Models/DesertGround_500.asset"; // doc/repro only
// Build a fresh mesh and save it to a NEW asset (never CopySerialized onto the shared one).
var mesh = gen.BuildMesh();
System.IO.Directory.CreateDirectory("Assets/Models");
if (UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Models/DesertGround_500.asset") != null)
    return "ABORT: DesertGround_500.asset already exists — refusing to overwrite";
UnityEditor.AssetDatabase.CreateAsset(mesh, "Assets/Models/DesertGround_500.asset");
UnityEditor.AssetDatabase.SaveAssets();
var mf = dune.GetComponent<MeshFilter>();
var mc = dune.GetComponent<MeshCollider>();
mf.sharedMesh = mesh;
mc.sharedMesh = mesh;
return $"Baked DesertGround_500: verts={mesh.vertexCount}, bounds={mesh.bounds.size}; repointed MeshFilter+MeshCollider";
```
Expected: `verts=361201` (601²), bounds ≈ (500, ~30, 500), repointed.

- [ ] **Step 3: Save and close the prefab stage**

Run (MCP): `mcp__unityMCP__manage_prefabs` `action="save_prefab_stage"`, then `action="close_prefab_stage"`.
Expected: both succeed.

- [ ] **Step 4: Verify the new asset exists and the shared one is untouched**

Run: `ls -la "Assets/Models/DesertGround_500.asset" "Assets/Models/DesertGround.asset"` — both present.
Run: `git status --short Assets/Models/DesertGround.asset` — **empty** (the original 200 mesh unchanged; if it shows modified, STOP — the bake clobbered the shared asset).
Run (MCP): `read_console` types `["Error"]` → clean.

- [ ] **Step 5: Commit**

```bash
git add Assets/Models/DesertGround_500.asset Assets/Models/DesertGround_500.asset.meta Assets/Prefabs/Desert/DesertEnvironment.prefab
git commit -m "feat(desert): regenerate dune ground at 500x500 -> DesertGround_500 (A1.5)"
```

---

## Task 2: Scale + reposition the 5 formations, re-seat on the new ground

**Files:**
- Modify: `Assets/Prefabs/Desert/DesertEnvironment.prefab` (the 5 `Formation_*`)

- [ ] **Step 1: Open the prefab stage**

Run (MCP): `manage_prefabs action="open_prefab_stage" prefab_path="Assets/Prefabs/Desert/DesertEnvironment.prefab"`.

- [ ] **Step 2: Scale, reposition (XZ), and raycast-snap Y for all five**

Run (MCP): `execute_code` with:

```csharp
var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
if (stage == null) return "ERROR: not in prefab stage";
var root = stage.prefabContentsRoot.transform;
// target XZ centers (spec §4) + uniform scale (1.2,1.32,1.2)
var targets = new (string n, float x, float z)[] {
    ("Formation_MesaArch",   -150f, 135f),
    ("Formation_HoodooSpires",150f, 165f),
    ("Formation_SlotCanyon",  170f, -20f),
    ("Formation_ButteRing",   -30f, -155f),
    ("Formation_FinField",   -165f, -150f),
};
var dune = root.Find("DuneGround");
var mc = dune.GetComponent<MeshCollider>();
var sb = new System.Text.StringBuilder();
foreach (var t in targets) {
    var f = root.Find(t.n); if (f == null) { sb.Append($"{t.n}:MISSING; "); continue; }
    f.localScale = new Vector3(1.2f, 1.32f, 1.2f);
    f.localPosition = new Vector3(t.x, f.localPosition.y, t.z); // Y set next
    // Sample the dune surface height at this XZ via the DuneGround MeshCollider's
    // bounds + a raycast in stage space. The collider mesh is centered at origin,
    // so cast straight down at (x, +200, z).
    float surfaceY = 0f; bool hit = false;
    var ray = new Ray(new Vector3(t.x, 200f, t.z), Vector3.down);
    if (mc.Raycast(ray, out RaycastHit h, 400f)) { surfaceY = h.point.y; hit = true; }
    // Re-seat so the formation's scaled base sits on the surface. Pre-scale base
    // was ~ -6 below pivot; after x1.32 vertical scale the base drops to ~ -7.9.
    // Measure actual via renderers, then shift Y so min.y == surfaceY.
    var rs = f.GetComponentsInChildren<Renderer>();
    Bounds b = rs[0].bounds; for (int i=1;i<rs.Length;i++) b.Encapsulate(rs[i].bounds);
    float delta = surfaceY - b.min.y;
    f.position = new Vector3(f.position.x, f.position.y + delta, f.position.z);
    sb.Append($"{t.n}: XZ=({t.x},{t.z}) surfaceY={(hit?surfaceY.ToString("F1"):"MISS")} baseShift={delta:F1}; ");
}
return sb.ToString();
```
Expected: each formation reports a surface hit and a base shift; none "MISS".

- [ ] **Step 3: Save + close the prefab stage**

Run (MCP): `manage_prefabs action="save_prefab_stage"`, then `action="close_prefab_stage"`.

- [ ] **Step 4: Verify in the FlyScene instance**

Run (MCP): `manage_scene action="load" path="Assets/Scenes/FlyScene.unity"`, then `execute_code` to print each formation's world bounds (center XZ + min.y vs the dune surface at that XZ). Confirm each formation's base sits within ~±1u of the surface and footprints are at the new centers. Run `read_console` types `["Error"]` → clean.

- [ ] **Step 5: Commit**

```bash
git add Assets/Prefabs/Desert/DesertEnvironment.prefab
git commit -m "feat(desert): scale formations x1.2 (+x1.1 Y), push out, re-seat on new dunes (A1.5)"
```

---

## Task 3: Expand the perimeter ridge to ~50 pieces

Duplicate the existing 20 ridge instances around a larger ~258-radius ring so the 500 basin is closed naturally.

**Files:**
- Modify: `Assets/Prefabs/Desert/DesertEnvironment.prefab` (PerimeterRidge)

- [ ] **Step 1: Open the prefab stage**

Run (MCP): `manage_prefabs action="open_prefab_stage" prefab_path="Assets/Prefabs/Desert/DesertEnvironment.prefab"`.

- [ ] **Step 2: Build the larger ring from clones of the existing 20 pieces**

Run (MCP): `execute_code` with:

```csharp
var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
var root = stage.prefabContentsRoot.transform;
var ridgeParent = root.Find("PerimeterRidge");
if (ridgeParent == null) return "ERROR: no PerimeterRidge";
// Collect the existing 20 pieces (templates to clone geometry from).
var templates = new System.Collections.Generic.List<Transform>();
foreach (Transform c in ridgeParent) templates.Add(c);
int baseCount = templates.Count;
if (baseCount == 0) return "ERROR: no ridge pieces to clone";
int world = LayerMask.NameToLayer("World");
// Place N pieces on a ring of radius R, facing inward, with deterministic jitter
// (no Random — vary by index so it's reproducible). Keep the existing 20 where
// they are is NOT desired (they ring r~108); instead REPOSITION all onto the new
// ring and add clones up to N.
int N = 52;                 // ~2.5x of 20, even spacing on the 500 perimeter
float R = 258f;             // half-extent 250 + a touch outboard
// Ensure we have N pieces: clone templates round-robin until count == N.
var pieces = new System.Collections.Generic.List<Transform>(templates);
int idx = 0;
while (pieces.Count < N) {
    var src = templates[idx % baseCount];
    var clone = UnityEngine.Object.Instantiate(src.gameObject, ridgeParent);
    clone.name = $"Ridge_{pieces.Count:D2}";
    pieces.Add(clone.transform);
    idx++;
}
// Distribute all N around the ring.
for (int i = 0; i < pieces.Count; i++) {
    float ang = (i / (float)pieces.Count) * Mathf.PI * 2f;
    // deterministic jitter from index
    float jr = ((i * 37) % 13 - 6) * 1.5f;        // ±~9u radial
    float jy = ((i * 53) % 11 - 5) * 0.8f;         // ±~4u height
    float jyaw = ((i * 29) % 21 - 10) * 1.2f;      // ±~12deg yaw
    float r = R + jr;
    float x = Mathf.Sin(ang) * r;
    float z = Mathf.Cos(ang) * r;
    var p = pieces[i];
    // keep the piece's own height band (14-23) but add jitter
    float baseY = p.localPosition.y; // preserves authored height of templates/clones
    p.localPosition = new Vector3(x, Mathf.Clamp(baseY + jy, 10f, 28f), z);
    // face inward (toward origin) + jitter
    Vector3 inward = new Vector3(-x, 0f, -z).normalized;
    p.rotation = Quaternion.LookRotation(inward, Vector3.up) * Quaternion.Euler(0f, jyaw, 0f);
    p.gameObject.layer = world;
    foreach (var ch in p.GetComponentsInChildren<Transform>(true)) ch.gameObject.layer = world;
}
return $"Ridge ring: {pieces.Count} pieces at R~{R}, all on World layer";
```
Expected: `Ridge ring: 52 pieces ... on World layer`.

- [ ] **Step 3: Save + close the prefab stage**

Run (MCP): `manage_prefabs action="save_prefab_stage"`, then `action="close_prefab_stage"`.

- [ ] **Step 4: Verify full perimeter closure**

Run (MCP): reload FlyScene (`manage_scene action="load" path="Assets/Scenes/FlyScene.unity"`), then `execute_code`: walk the ring at 5° increments, raycast outward from origin at y=12 along each heading, and confirm a ridge collider is hit within ~200–290u on every heading (no gap > ~15°). Report any gaps.

```csharp
var de = GameObject.Find("DesertEnvironment");
int world = LayerMask.NameToLayer("World"); int mask = 1 << world;
int gaps = 0; var sb = new System.Text.StringBuilder();
for (int deg = 0; deg < 360; deg += 5) {
    float a = deg * Mathf.Deg2Rad;
    Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
    if (!Physics.Raycast(new Vector3(0,12,0), dir, out RaycastHit h, 320f, mask))
        { gaps++; sb.Append($"{deg} "); }
}
return gaps == 0 ? "Perimeter CLOSED (ridge hit on all 72 headings)" : $"GAPS at headings: {sb} ({gaps})";
```
Expected: "Perimeter CLOSED". If gaps, nudge `N`/`R` and re-run Task 3.
Run `read_console` types `["Error"]` → clean.

- [ ] **Step 5: Commit**

```bash
git add Assets/Prefabs/Desert/DesertEnvironment.prefab
git commit -m "feat(desert): expand perimeter ridge to ~52 pieces for the 500 basin (A1.5)"
```

---

## Task 4: Move the spawn to the central plain

**Files:**
- Modify: `Assets/Scenes/FlyScene.unity` (`CubeConstruct`)

- [ ] **Step 1: Ensure FlyScene is active**

Run (MCP): `manage_scene action="get_active"`. If not FlyScene, `manage_scene action="load" path="Assets/Scenes/FlyScene.unity"`.

- [ ] **Step 2: Reposition + aim the construct**

Run (MCP): `execute_code` with:

```csharp
var cc = GameObject.Find("CubeConstruct");
if (cc == null) return "ERROR: no CubeConstruct";
cc.transform.position = new Vector3(0f, 30f, 60f);          // central plain; SpawnSurfacePlacer re-seats Y at runtime
Vector3 hero = new Vector3(-150f, 30f, 135f);              // Mesa+Arch hero
Vector3 dir = hero - cc.transform.position; dir.y = 0f;
cc.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
// confirm (0,60) is clear of all formation footprints
var de = GameObject.Find("DesertEnvironment");
string[] names = {"Formation_MesaArch","Formation_HoodooSpires","Formation_SlotCanyon","Formation_ButteRing","Formation_FinField"};
var sb = new System.Text.StringBuilder();
Vector2 s = new Vector2(0f, 60f);
foreach (var n in names) {
    var f = de.transform.Find(n); var rs = f.GetComponentsInChildren<Renderer>();
    Bounds b = rs[0].bounds; for (int i=1;i<rs.Length;i++) b.Encapsulate(rs[i].bounds);
    bool inside = s.x>=b.min.x&&s.x<=b.max.x&&s.y>=b.min.z&&s.y<=b.max.z;
    float dx=Mathf.Max(0,Mathf.Max(b.min.x-s.x,s.x-b.max.x)), dz=Mathf.Max(0,Mathf.Max(b.min.z-s.y,s.y-b.max.z));
    sb.Append($"{n}:{(inside?"**INSIDE**":$"clear {Mathf.Sqrt(dx*dx+dz*dz):F0}u")}; ");
}
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(cc.scene);
return $"Spawn (0,30,60) facing hero. Footprint check: {sb}";
```
Expected: all formations report "clear" (none INSIDE). If any INSIDE, pick a nearby clear central point and re-run.

- [ ] **Step 3: Save + console check**

Run (MCP): `manage_scene action="save"`; `read_console` types `["Error"]` → clean.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scenes/FlyScene.unity
git commit -m "feat(desert): move spawn to the central plain facing the hero (A1.5)"
```

---

## Task 5: Re-verify navigable widths for large constructs

The whole point of A1.5 — confirm the arch + slot canyon are actually wide enough.

**Files:** none modified (measurement only)

- [ ] **Step 1: Measure the Mesa+Arch fly-through gap and the slot-canyon width**

Run (MCP): `execute_code` with:

```csharp
var de = GameObject.Find("DesertEnvironment");
// Arch gap: the MesaArch 'Arch' piece spans two legs; the opening is the inner gap.
// Approximate by the Arch child's bounds minus its solid legs — report the Arch
// bounds and the gap between MesaA/MesaB if separable.
var mesa = de.transform.Find("Formation_MesaArch");
var sb = new System.Text.StringBuilder();
foreach (Transform c in mesa) {
    var r = c.GetComponent<Renderer>(); if (r==null) continue;
    sb.Append($"{c.name} size=({r.bounds.size.x:F0}x{r.bounds.size.y:F0}x{r.bounds.size.z:F0}) ctr=({r.bounds.center.x:F0},{r.bounds.center.z:F0}); ");
}
// Slot canyon: gap between the W and E walls.
var slot = de.transform.Find("Formation_SlotCanyon");
Bounds wB=new Bounds(), eB=new Bounds(); bool wi=false, ei=false;
foreach (Transform c in slot) {
    var r=c.GetComponent<Renderer>(); if(r==null) continue;
    if(c.name.Contains("_W")){ if(!wi){wB=r.bounds;wi=true;} else wB.Encapsulate(r.bounds);}
    if(c.name.Contains("_E")){ if(!ei){eB=r.bounds;ei=true;} else eB.Encapsulate(r.bounds);}
}
float slotGap = (wi&&ei) ? (eB.min.x - wB.max.x) : -1f;
return $"MesaArch parts: {sb} | SlotCanyon W/E gap ~ {slotGap:F0}u";
```
Expected: prints the arch leg geometry + slot gap. **Judge:** is the arch opening / slot gap comfortably wider than a large construct (the construct is one Rigidbody of cube cells; a "large" build might be ~15–25u across)? Record the numbers.

- [ ] **Step 2: Screenshot the basin from above for a layout sanity check**

Run (MCP): `manage_camera action="screenshot" view_position=[0,420,-40] view_rotation=[75,0,0] include_image=true max_resolution=720`. Eyeball: 5 formations pushed to the rim, vast open centre, ridge closing the perimeter. Delete the screenshot artifact afterward (`rm -f Assets/Screenshots/*.png Assets/Screenshots/*.png.meta; rmdir Assets/Screenshots 2>/dev/null`).

- [ ] **Step 3: No commit (measurement only)** — proceed to Task 6.

---

## Task 6: Play-mode re-fly + decision gate

**Files:** `docs/superpowers/specs/2026-06-11-desert-flyscene-a1.5-design.md` (append outcome)

- [ ] **Step 1: Compile clean + enter Play**

Run (MCP): `refresh_unity` (compile=request, mode=force, wait_for_ready=true); `read_console` types `["Error"]` → clean. Ensure FlyScene active; `manage_editor action="play"`. Read console for the `SpawnPlacer: Spawn seated at y=...` line and any errors.
Expected: safe spawn log on `DuneGround`; zero errors; terrain collides on the new mesh.

- [ ] **Step 2: Hand to the user to fly**

The user flies the basin. Judge (spec §8): room for a large construct (arch + canyons wide enough)? Vast open central plain with huge mesas around? Ridge contains? Terrain collides (no fall-through on the new 500 mesh)? Note fog — sightlines are longer now; decide if the start distance wants a bump.

- [ ] **Step 3: Stop Play**

Run (MCP): `manage_editor action="stop"`.

- [ ] **Step 4 (only if the re-fly calls for it): bump fog start**

If the basin reads hazy at the new scale, run (MCP): `execute_code`: `RenderSettings.fogStartDistance = 140f;` (or the agreed value) + `MarkSceneDirty`; `manage_scene action="save"`; commit `Assets/Scenes/FlyScene.unity` with `fix(desert): widen fog start for the 500 basin (A1.5)`.

- [ ] **Step 5: Record the decision gate**

Append an "A1.5 outcome" note (ship / iterate / shelve + observations + the arch/slot widths from Task 5) to `docs/superpowers/specs/2026-06-11-desert-flyscene-a1.5-design.md`, then:

```bash
git add docs/superpowers/specs/2026-06-11-desert-flyscene-a1.5-design.md
git commit -m "docs(desert): record A1.5 re-fly outcome + decision gate"
```

---

## Self-review notes (spec coverage)

- §3 ground regen → Task 1 (new asset, repoint, shared-asset guard in Step 4).
- §4 formations scale/reposition/Y-reseat → Task 2.
- §5 ridge expand → Task 3 (+ closure check Step 4).
- §6 spawn + re-verify → Task 4 (spawn) + Task 5 (arch/slot widths, clearance).
- §7 unchanged + fog note → Task 6 Step 4 (conditional fog bump).
- §8 verification + gate → Task 6.
- §10 file manifest → matches Tasks 1–4 edits.

In-editor unknowns the implementer resolves live (not guessable): exact base-shift per formation (Task 2 raycast-snap), whether 52/R258 closes the ring (Task 3 Step 4 — nudge if gaps), whether (0,60) is clear at final scale (Task 4 Step 2), the actual arch/slot widths (Task 5), and the fog value (Task 6).
