# Desert Level — Checkpoint C2 (Layout) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build checkpoint C2 of the desert demonstrator — the full 200×200u layout: a procedural dune ground, all five hero formations placed, a perimeter rock ridge, and a free-fly camera for evaluation.

**Architecture:** A procedural noise-displaced ground mesh (built by an editor tool, baked to a mesh asset) replaces the small C1 patch. Four more ProBuilder hero formations join the repositioned C1 mesa-and-arch; a perimeter rock ridge bounds the basin. A free-fly camera enables play-mode fly-through evaluation. All geometry wears the proven C1 cel shader and outline render feature.

**Tech Stack:** Unity 6.3 (6000.3.11f1), URP 17.3, ProBuilder 6.0.9, C# procedural mesh generation, the new Input System.

---

## Scope

This plan covers **Checkpoint C2 only**. C3 (dressing — lighting, sky, fog, post, detail) gets its own plan. Work continues on the existing `explore/desert-level` branch. C1 is complete: the `DesertSandbox.unity` scene, `CelShaded` shader, `OutlineRendererFeature` + `Desert_Renderer`, four palette materials, and one mesa-and-arch formation.

## Verification approach

Unity editor / asset work — no unit-test surface for meshes and hand-modelled geometry. Verification is:

1. **Compile checks** — after any C# file, refresh Unity and read the console; expect zero errors.
2. **Editor checks** — confirm assets/objects exist with the stated properties; use `manage_camera` screenshots (standalone calls with `include_image`, never inside `batch_execute` — the base64 overflows the batch result).
3. **Checkpoint review** — Task 11 is the user's fly-through review; that is the C2 acceptance gate.

Each task ends with a commit, message style matching the repo (descriptive, capitalised, no `feat:` prefix, with the `Co-Authored-By` trailer).

## Unity MCP notes (from C1)

- `manage_probuilder create_shape` takes **scalar** `width`/`height`/`depth` (a `size` array throws a JArray cast error).
- New C# scripts: after creation, replace the minimal `.meta` stub with the full canonical `MonoImporter` block (keep Unity's generated GUID).
- Poll `mcpforunity://editor/state` until `is_compiling` is false after a script change before continuing.
- ProBuilder objects do **not** get a collider automatically — add `MeshCollider` explicitly.

## File structure

| File | Responsibility |
|---|---|
| `Assets/Scripts/Desert/DuneGroundGenerator.cs` | Component that builds a noise-displaced grid mesh from parameters |
| `Assets/Scripts/Desert/Editor/DuneGroundGeneratorEditor.cs` | Custom inspector — a "Generate Dune Mesh" button that bakes the mesh asset |
| `Assets/Scripts/Desert/FreeFlyCamera.cs` | Minimal play-mode free-fly camera (new Input System) |
| `Assets/Models/DesertGround.asset` | The baked dune mesh |
| `Assets/Prefabs/Desert/*.prefab` | The five formation prefabs |
| `Assets/Scenes/DesertSandbox.unity` | Modified — patch removed, dune ground + formations + ridge added |

---

## Task 1: Dune ground generator tool

**Files:**
- Create: `Assets/Scripts/Desert/DuneGroundGenerator.cs`
- Create: `Assets/Scripts/Desert/Editor/DuneGroundGeneratorEditor.cs`

- [ ] **Step 1: Create the generator component**

Create `Assets/Scripts/Desert/DuneGroundGenerator.cs` with exactly this content:

```csharp
using UnityEngine;

/// <summary>
/// Builds a noise-displaced grid mesh for the desert dune ground.
/// Drive it from the custom inspector's "Generate Dune Mesh" button.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class DuneGroundGenerator : MonoBehaviour
{
    [Header("Extent")]
    public float size = 200f;
    public int resolution = 200;

    [Header("Layered noise")]
    public int seed = 12345;
    public float swellAmplitude = 6f;
    public float swellFrequency = 0.012f;
    public float duneAmplitude = 2.5f;
    public float duneFrequency = 0.05f;
    public float rippleAmplitude = 0.4f;
    public float rippleFrequency = 0.22f;

    [Header("Output")]
    public string meshAssetPath = "Assets/Models/DesertGround.asset";

    public Mesh BuildMesh()
    {
        int n = Mathf.Max(2, resolution);
        int verts = n + 1;
        var mesh = new Mesh { name = "DesertGround" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        var positions = new Vector3[verts * verts];
        var uvs = new Vector2[verts * verts];
        float step = size / n;
        float half = size * 0.5f;

        var rng = new System.Random(seed);
        Vector2 swellOff  = new Vector2((float)rng.NextDouble() * 1000f, (float)rng.NextDouble() * 1000f);
        Vector2 duneOff   = new Vector2((float)rng.NextDouble() * 1000f, (float)rng.NextDouble() * 1000f);
        Vector2 rippleOff = new Vector2((float)rng.NextDouble() * 1000f, (float)rng.NextDouble() * 1000f);

        for (int z = 0; z < verts; z++)
        {
            for (int x = 0; x < verts; x++)
            {
                float wx = x * step - half;
                float wz = z * step - half;
                float h = 0f;
                h += (Mathf.PerlinNoise(wx * swellFrequency + swellOff.x,  wz * swellFrequency + swellOff.y)  - 0.5f) * 2f * swellAmplitude;
                h += (Mathf.PerlinNoise(wx * duneFrequency + duneOff.x,    wz * duneFrequency + duneOff.y)    - 0.5f) * 2f * duneAmplitude;
                h += (Mathf.PerlinNoise(wx * rippleFrequency + rippleOff.x, wz * rippleFrequency + rippleOff.y) - 0.5f) * 2f * rippleAmplitude;
                int i = z * verts + x;
                positions[i] = new Vector3(wx, h, wz);
                uvs[i] = new Vector2((float)x / n, (float)z / n);
            }
        }

        var tris = new int[n * n * 6];
        int t = 0;
        for (int z = 0; z < n; z++)
        {
            for (int x = 0; x < n; x++)
            {
                int bl = z * verts + x;
                int br = bl + 1;
                int tl = bl + verts;
                int tr = tl + 1;
                tris[t++] = bl; tris[t++] = tl; tris[t++] = tr;
                tris[t++] = bl; tris[t++] = tr; tris[t++] = br;
            }
        }

        mesh.vertices = positions;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
```

- [ ] **Step 2: Create the custom inspector**

Create `Assets/Scripts/Desert/Editor/DuneGroundGeneratorEditor.cs` with exactly this content:

```csharp
using System.IO;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DuneGroundGenerator))]
public class DuneGroundGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var gen = (DuneGroundGenerator)target;

        EditorGUILayout.Space();
        if (GUILayout.Button("Generate Dune Mesh"))
        {
            Mesh mesh = gen.BuildMesh();

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(gen.meshAssetPath);
            if (existing != null)
            {
                existing.Clear();
                EditorUtility.CopySerialized(mesh, existing);
                mesh = existing;
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(gen.meshAssetPath));
                AssetDatabase.CreateAsset(mesh, gen.meshAssetPath);
            }
            AssetDatabase.SaveAssets();

            gen.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mc = gen.GetComponent<MeshCollider>();
            if (mc != null) mc.sharedMesh = mesh;
            EditorUtility.SetDirty(gen);

            Debug.Log("[DuneGround] generated " + mesh.vertexCount + " verts -> " + gen.meshAssetPath);
        }
    }
}
```

- [ ] **Step 3: Canonical `.meta` files**

After Unity imports both scripts, replace each minimal `.meta` stub with the full canonical body (keep Unity's generated GUID):

```
fileFormatVersion: 2
guid: <KEEP UNITY'S GENERATED GUID>
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

- [ ] **Step 4: Verify it compiles**

Refresh Unity, poll `editor_state` until `is_compiling` is false, read the console. Expected: `DuneGroundGenerator` and `DuneGroundGeneratorEditor` compile with no errors.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Desert/DuneGroundGenerator.cs" "Assets/Scripts/Desert/DuneGroundGenerator.cs.meta" \
        "Assets/Scripts/Desert/Editor" "Assets/Scripts/Desert/Editor.meta"
git commit -m "Add dune ground generator tool"
```

---

## Task 2: Generate the dune ground in the scene

**Files:**
- Modify: `Assets/Scenes/DesertSandbox.unity`
- Create: `Assets/Models/DesertGround.asset`

- [ ] **Step 1: Remove the C1 ground patch**

In `DesertSandbox.unity`, delete the `GroundPatch` GameObject (the 80×80 C1 patch).

- [ ] **Step 2: Create the DuneGround object**

Create an empty GameObject `DuneGround` at world origin `(0, 0, 0)`. Add components: `MeshFilter`, `MeshRenderer`, `MeshCollider`, `DuneGroundGenerator`. On the `DuneGroundGenerator`, leave parameters at their defaults (size 200, resolution 200, the noise defaults).

- [ ] **Step 3: Generate the mesh**

Select `DuneGround` and click **Generate Dune Mesh** in the inspector (or invoke `DuneGroundGeneratorEditor`'s generate logic via `execute_code`). This bakes `Assets/Models/DesertGround.asset` and assigns it to the `MeshFilter` and `MeshCollider`.

- [ ] **Step 4: Apply the material**

Set the `DuneGround` `MeshRenderer`'s material to `Assets/Materials/Desert/Sand.mat`.

- [ ] **Step 5: Verify**

Expected: a 200×200u gently undulating dune ground centred on the origin, cel-shaded sand, with a `MeshCollider` matching the mesh. Console clear. Capture a screenshot to confirm the dunes read.

- [ ] **Step 6: Commit**

```bash
git add "Assets/Scenes/DesertSandbox.unity" "Assets/Models/DesertGround.asset" \
        "Assets/Models/DesertGround.asset.meta"
git commit -m "Generate the 200x200 procedural dune ground"
```

---

## Task 3: Reposition the mesa-and-arch formation

**Files:**
- Modify: `Assets/Scenes/DesertSandbox.unity`

- [ ] **Step 1: Move the formation**

Move `Formation_MesaArch` from the origin to its layout coordinate: world position `(-50, -6, 45)`. The `y = -6` seats the formation into the dune ground so its base does not float above the undulating terrain.

- [ ] **Step 2: Verify**

Expected: the mesa-and-arch cluster sits in the north-west quadrant, anchored into the dunes with no visible floating gap. Console clear.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Scenes/DesertSandbox.unity"
git commit -m "Reposition mesa-and-arch formation into the layout"
```

---

## Task 4: Slot Canyon formation

**Files:**
- Modify: `Assets/Scenes/DesertSandbox.unity`

- [ ] **Step 1: Build the formation**

Create an empty parent `Formation_SlotCanyon` at world `(55, -6, 0)`. Under it, hand-build a winding slot canyon with ProBuilder cubes (faceted, no smoothing):

- **Two rock walls**, each made of 3-4 elongated ProBuilder cube segments (~12u thick, ~40u tall, ~28u long), placed end-to-end running roughly north-south. Kink each segment 10-20° in Y from the previous one so the corridor *winds*.
- The gap between the two walls is the navigable corridor: vary it from ~18u (pinch points) to ~35u (wider stretches) along the ~90u length.
- Materials: alternate `RedSandstone` and `Limestone` across segments.
- Every ProBuilder mesh gets a `MeshCollider`. Keep all geometry faceted.

- [ ] **Step 2: Verify**

Expected: a winding canyon corridor, walls 30-60u tall, corridor 18-35u wide, readable as a fly-through slot. Console clear.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Scenes/DesertSandbox.unity"
git commit -m "Add Slot Canyon formation"
```

---

## Task 5: Fin Field formation

**Files:**
- Modify: `Assets/Scenes/DesertSandbox.unity`

- [ ] **Step 1: Build the formation**

Create an empty parent `Formation_FinField` at world `(-50, -6, -50)`. Under it, build a cluster of 6-8 tall thin sandstone blades with ProBuilder cubes:

- Each fin: ~3-5u thick, 25-40u tall, 15-25u long (vary the dimensions).
- Arrange them loosely parallel but each rotated ±15-25° in Y, spaced 15-25u apart so a ship can weave between them.
- Material: `RedSandstone` on all fins (strata banding reads strongly on the tall blades).
- `MeshCollider` on each. Faceted.

- [ ] **Step 2: Verify**

Expected: a field of tall thin fins with 15-25u weave gaps. Console clear.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Scenes/DesertSandbox.unity"
git commit -m "Add Fin Field formation"
```

---

## Task 6: Hoodoo Spires formation

**Files:**
- Modify: `Assets/Scenes/DesertSandbox.unity`

- [ ] **Step 1: Build the formation**

Create an empty parent `Formation_HoodooSpires` at world `(45, -6, 60)`. Under it, build a cluster of pillars with ProBuilder:

- 5-7 tall thin pillars — ProBuilder cylinders (or thin tapered cubes), ~4-7u wide, 20-45u tall (vary heights), spaced 15-30u apart.
- One **balanced rock**: a wider rock chunk (~12u) perched on top of one thin pillar.
- Materials: mix `OxidizedRock` and `RedSandstone`.
- `MeshCollider` on each. Faceted.

- [ ] **Step 2: Verify**

Expected: a slalom of pillars of varying height plus a balanced rock. Console clear.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Scenes/DesertSandbox.unity"
git commit -m "Add Hoodoo Spires formation"
```

---

## Task 7: Butte Ring formation

**Files:**
- Modify: `Assets/Scenes/DesertSandbox.unity`

- [ ] **Step 1: Build the formation**

Create an empty parent `Formation_ButteRing` at world `(0, -6, -50)`. Under it, build a ring of rock around an open bowl with ProBuilder cubes/mesas:

- 8-12 rock chunks, ~15-25u wide each, heights varying 20-40u, arranged in a circle of radius ~35-40u.
- Leave a ~60-80u open bowl in the centre, and leave 2-3 gaps between chunks so a ship can fly in and out of the bowl.
- Materials: mix `Limestone`, `RedSandstone`, `OxidizedRock`.
- `MeshCollider` on each. Faceted.

- [ ] **Step 2: Verify**

Expected: a ring of buttes enclosing an open arena bowl, with fly-in gaps. Console clear.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Scenes/DesertSandbox.unity"
git commit -m "Add Butte Ring formation"
```

---

## Task 8: Perimeter ridge

**Files:**
- Modify: `Assets/Scenes/DesertSandbox.unity`

- [ ] **Step 1: Build the ridge**

Create an empty parent `PerimeterRidge` at world origin. Under it, build a containing rim of large rock around the 200×200 perimeter with ProBuilder cubes/mesas:

- 16-24 large rock chunks, ~25-40u wide, 40-70u tall (vary), placed around the border so their outer faces sit near |X| ≈ 90-100 or |Z| ≈ 90-100, forming a near-continuous high ridge on all four sides.
- Seat them ~8u into the dune ground (parent or per-chunk `y ≈ -8`).
- Materials: mostly `Limestone` and `OxidizedRock`, occasional `RedSandstone`.
- `MeshCollider` on each so flight is physically contained on the sides. Faceted.

- [ ] **Step 2: Verify**

Expected: a continuous-looking rock rim ringing the basin; the 200×200 playfield reads as an enclosed desert basin. Console clear.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Scenes/DesertSandbox.unity"
git commit -m "Add perimeter rock ridge"
```

---

## Task 9: Save formations as prefabs

**Files:**
- Create: `Assets/Prefabs/Desert/Formation_MesaArch.prefab`
- Create: `Assets/Prefabs/Desert/Formation_SlotCanyon.prefab`
- Create: `Assets/Prefabs/Desert/Formation_FinField.prefab`
- Create: `Assets/Prefabs/Desert/Formation_HoodooSpires.prefab`
- Create: `Assets/Prefabs/Desert/Formation_ButteRing.prefab`
- Modify: `Assets/Scenes/DesertSandbox.unity`

- [ ] **Step 1: Save each formation as a prefab**

For each of the five `Formation_*` GameObjects, save it as a prefab under `Assets/Prefabs/Desert/` and connect the scene object as an instance — `PrefabUtility.SaveAsPrefabAssetAndConnect` (via `execute_code`). The `PerimeterRidge` stays scene-only (it is unique scenery, not a reusable piece).

- [ ] **Step 2: Verify**

Expected: five `.prefab` assets under `Assets/Prefabs/Desert/`; the scene's five formation objects are prefab instances; nothing visually changed. Console clear.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Prefabs/Desert" "Assets/Prefabs/Desert.meta" "Assets/Scenes/DesertSandbox.unity"
git commit -m "Save the five hero formations as prefabs"
```

---

## Task 10: Free-fly camera

**Files:**
- Create: `Assets/Scripts/Desert/FreeFlyCamera.cs`
- Modify: `Assets/Scenes/DesertSandbox.unity`

- [ ] **Step 1: Create the script**

Create `Assets/Scripts/Desert/FreeFlyCamera.cs` with exactly this content:

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Minimal play-mode free-fly camera for evaluating the desert level.
/// Hold right mouse to look; WASD to move; E/Q for up/down; Shift to boost.
/// </summary>
public class FreeFlyCamera : MonoBehaviour
{
    public float moveSpeed = 35f;
    public float boostMultiplier = 3f;
    public float lookSensitivity = 0.12f;

    float _yaw;
    float _pitch;

    void Start()
    {
        Vector3 e = transform.eulerAngles;
        _yaw = e.y;
        _pitch = e.x;
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (kb == null || mouse == null)
            return;

        if (mouse.rightButton.isPressed)
        {
            Vector2 d = mouse.delta.ReadValue() * lookSensitivity;
            _yaw += d.x;
            _pitch = Mathf.Clamp(_pitch - d.y, -89f, 89f);
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        Vector3 local = Vector3.zero;
        if (kb.wKey.isPressed) local += Vector3.forward;
        if (kb.sKey.isPressed) local += Vector3.back;
        if (kb.aKey.isPressed) local += Vector3.left;
        if (kb.dKey.isPressed) local += Vector3.right;

        Vector3 world = Vector3.zero;
        if (kb.eKey.isPressed) world += Vector3.up;
        if (kb.qKey.isPressed) world += Vector3.down;

        float speed = moveSpeed * (kb.leftShiftKey.isPressed ? boostMultiplier : 1f);
        Vector3 move = transform.TransformDirection(local.normalized) + world.normalized;
        transform.position += move * speed * Time.deltaTime;
    }
}
```

- [ ] **Step 2: Canonical `.meta`**

Replace the minimal `.meta` stub for `FreeFlyCamera.cs` with the full canonical `MonoImporter` body (keep Unity's GUID — see the template in Task 1 Step 3).

- [ ] **Step 3: Verify it compiles**

Refresh Unity, poll `editor_state` until not compiling, read the console. Expected: `FreeFlyCamera` compiles with no errors. (The new Input System is already enabled in this project.)

- [ ] **Step 4: Add it to the scene camera**

Add the `FreeFlyCamera` component to the `Main Camera` GameObject in `DesertSandbox.unity`.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Desert/FreeFlyCamera.cs" "Assets/Scripts/Desert/FreeFlyCamera.cs.meta" \
        "Assets/Scenes/DesertSandbox.unity"
git commit -m "Add free-fly camera for level evaluation"
```

---

## Task 11: Checkpoint C2 review

**Files:** none (review gate)

- [ ] **Step 1: Capture views**

Capture screenshots of `DesertSandbox.unity` from several angles: a high overview of the whole basin, and ground-level views near each of the five formations and the perimeter ridge.

- [ ] **Step 2: Present for review**

Present the screenshots to the user against the C2 criteria:
- Do the canyons, arches, fins and pillars navigate well at the 3-6u reference ship scale?
- Does the layout flow — can the formations be strung into a course?
- Do the formations read at distance?
- Does the perimeter ridge contain the basin?

- [ ] **Step 3: Address feedback and commit any tuning**

Apply any layout/scale tuning the user asks for. Commit each round:

```bash
git add -A
git commit -m "C2 review: <describe the tuning>"
```

- [ ] **Step 4: Checkpoint complete**

On user approval, C2 is done. The next step is a writing-plans pass for Checkpoint C3 (dressing).

---

## Definition of done (C2)

- The C1 ground patch is gone; a 200×200u procedural dune ground (baked `DesertGround.asset`) wears the `Sand` material with a matching `MeshCollider`.
- All five hero formations — mesa-and-arch, slot canyon, fin field, hoodoo spires, butte ring — are placed per the layout coordinates, faceted, palette-coloured, with `MeshCollider`s, and saved as prefabs.
- A perimeter rock ridge rings and physically contains the basin.
- A `FreeFlyCamera` on the Main Camera allows play-mode fly-through.
- No console errors; the user has reviewed and approved the C2 layout.
