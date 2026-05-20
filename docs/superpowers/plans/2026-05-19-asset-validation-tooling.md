# Registry Validation Tooling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship an on-demand editor tool (`Tools/CubeFly/Validate Registries`) that validates `ShapeRegistry`, `MaterialRegistry`, every shape's prefab, and every coupled material — reporting findings to the Unity console with an end-of-run summary dialog.

**Architecture:** Single static C# class in a new `Assets/Scripts/Editor/` folder (auto-excluded from runtime builds by Unity convention). Discovery uses `AssetDatabase.FindAssets("t:Type")` so asset paths aren't hard-coded. Each check produces a tagged `Debug.unityLogger` line with the offending asset as `context` so Console pings the right asset on click.

**Tech Stack:** Unity 6.3 LTS, MonoBehaviour C#, Unity `AssetDatabase` + `EditorUtility` + `MenuItem`. No external libraries.

**Design spec:** `docs/superpowers/specs/2026-05-19-asset-validation-tooling-design.md`.

**Branch:** `feat/asset-validation-tooling` (already created, off `main`; the ROADMAP commit `6d5c829` and the design-spec commit `6164ffe` are already on it).

**Testing note:** This project has **no automated test framework** (F5/asmdef split is roadmapped). Verification is the Unity compile-check (`refresh_unity` + `read_console`) plus the manual hand-injection run in Task 2.

**Working-tree note:** `Assets/Materials/BulletMat.mat`, `ProjectSettings/Packages/com.unity.probuilder/Settings.json`, and the untracked `CODEBASE_REVIEW_AUDIT.txt` are pre-existing unrelated changes. **Never stage them.** Every commit below stages only its named files.

---

## Task 1: Implement `RegistryValidator.cs`

**Files:**
- Create: `Assets/Scripts/Editor/RegistryValidator.cs`

The file is built in five chunks: a compiling skeleton with empty stubs, then four stubs are filled in (one per check category). The skeleton compiles on its own — each subsequent step adds a method body and any private helpers below it.

- [ ] **Step 1: Write the file skeleton**

Create `Assets/Scripts/Editor/RegistryValidator.cs` with this content (the four `CheckXxx` stubs are intentionally empty — subsequent steps fill them in):

```csharp
using System.Collections.Generic;
using CubeFly.Build;
using CubeFly.Core;
using UnityEditor;
using UnityEngine;

namespace CubeFly.EditorTools
{
    // On-demand validator for the construct registries and their
    // prefabs. Runs via Tools/CubeFly/Validate Registries; reports each
    // finding to the Unity console with the offending asset as context
    // (so the Console "ping" arrow works), then surfaces an end-of-run
    // summary dialog.
    //
    // Spec:
    //   docs/superpowers/specs/2026-05-19-asset-validation-tooling-design.md
    public static class RegistryValidator
    {
        const string TAG = "RegistryValidator";
        const string MenuPath = "Tools/CubeFly/Validate Registries";

        // Per-run counters. Reset at the top of Validate(); read at the
        // end for the summary line + dialog.
        static int _errors;
        static int _warnings;
        static int _shapesChecked;
        static int _armourMaterialsChecked;
        static int _coupledMaterialsChecked;

        [MenuItem(MenuPath)]
        public static void Validate()
        {
            _errors = 0;
            _warnings = 0;
            _shapesChecked = 0;
            _armourMaterialsChecked = 0;
            _coupledMaterialsChecked = 0;

            ShapeRegistry shapeRegistry       = FindSoleAsset<ShapeRegistry>("ShapeRegistry");
            MaterialRegistry materialRegistry = FindSoleAsset<MaterialRegistry>("MaterialRegistry");

            CheckRequiredLayers();

            if (shapeRegistry != null)    CheckShapeRegistry(shapeRegistry);
            if (materialRegistry != null) CheckMaterialRegistry(materialRegistry);
            if (shapeRegistry != null)    CheckCoupledMaterials(shapeRegistry, materialRegistry);

            Debug.unityLogger.Log(TAG,
                $"Validation complete: {_errors} error(s), {_warnings} warning(s), " +
                $"{_shapesChecked} shape(s), {_armourMaterialsChecked} armour material(s), " +
                $"{_coupledMaterialsChecked} coupled material(s) checked.");

            string body = (_errors == 0 && _warnings == 0)
                ? "All checks passed."
                : $"Found {_errors} error(s) and {_warnings} warning(s). See Console for details.";
            EditorUtility.DisplayDialog("Registry Validation", body, "OK");
        }

        // ---------- Finding helpers ----------

        static void ReportError(string message, UnityEngine.Object context)
        {
            _errors++;
            Debug.unityLogger.LogError(TAG, message, context);
        }

        static void ReportWarning(string message, UnityEngine.Object context)
        {
            _warnings++;
            Debug.unityLogger.LogWarning(TAG, message, context);
        }

        // ---------- Discovery ----------

        // Find exactly one asset of type T anywhere under Assets. Returns
        // null and reports an error if zero or more than one match.
        static T FindSoleAsset<T>(string typeLabel) where T : UnityEngine.Object
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            if (guids == null || guids.Length == 0)
            {
                ReportError($"Expected exactly one {typeLabel} asset, found 0.", null);
                return null;
            }
            if (guids.Length > 1)
            {
                ReportError($"Expected exactly one {typeLabel} asset, found {guids.Length}.", null);
                // Continue with the first found so we still surface the
                // downstream findings rather than aborting entirely.
            }
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        // ---------- Check stubs (filled in by subsequent steps) ----------

        // Filled in by Step 2.
        static void CheckRequiredLayers() { }

        // Filled in by Step 3.
        static void CheckShapeRegistry(ShapeRegistry registry) { }

        // Filled in by Step 4.
        static void CheckMaterialRegistry(MaterialRegistry registry) { }

        // Filled in by Step 5.
        static void CheckCoupledMaterials(ShapeRegistry shapeRegistry, MaterialRegistry materialRegistry) { }
    }
}
```

- [ ] **Step 2: Fill in `CheckRequiredLayers`**

In `Assets/Scripts/Editor/RegistryValidator.cs`, replace this block:

```csharp
        // Filled in by Step 2.
        static void CheckRequiredLayers() { }
```

with:

```csharp
        // Verify the layers BuildManager filters on / the AlphaCube uses
        // are defined in Project Settings → Tags and Layers. Missing
        // layers don't break the build (BuildManager.ResolveLayerMasks
        // falls back to "everything but Ignore Raycast"), but they mean
        // gameplay is on the wrong layer.
        static void CheckRequiredLayers()
        {
            CheckLayer("PlacedCube");
            CheckLayer("AlphaCube");
            CheckLayer("PreviewCube");
        }

        static void CheckLayer(string layerName)
        {
            if (LayerMask.NameToLayer(layerName) < 0)
                ReportError($"Required layer '{layerName}' is not defined in Project Settings → Tags and Layers.", null);
        }
```

- [ ] **Step 3: Fill in `CheckShapeRegistry` + per-shape helpers**

In `Assets/Scripts/Editor/RegistryValidator.cs`, replace this block:

```csharp
        // Filled in by Step 3.
        static void CheckShapeRegistry(ShapeRegistry registry) { }
```

with:

```csharp
        // Walks every ShapeDefinition in the registry. Registry-level
        // concerns (non-empty list, no nulls, unique displayName) are
        // handled here; per-shape detail is in CheckShapeDefinition.
        static void CheckShapeRegistry(ShapeRegistry registry)
        {
            if (registry.shapes == null || registry.shapes.Length == 0)
            {
                ReportError("ShapeRegistry.shapes is empty.", registry);
                return;
            }

            HashSet<string> seenNames = new HashSet<string>();
            for (int i = 0; i < registry.shapes.Length; i++)
            {
                ShapeDefinition shape = registry.shapes[i];
                if (shape == null)
                {
                    ReportError($"ShapeRegistry.shapes[{i}] is null.", registry);
                    continue;
                }

                CheckShapeDefinition(shape);

                if (string.IsNullOrEmpty(shape.displayName))
                {
                    ReportError($"Shape '{shape.name}' has an empty displayName.", shape);
                }
                else if (!seenNames.Add(shape.displayName))
                {
                    ReportError($"Duplicate shape displayName '{shape.displayName}'.", shape);
                }
            }
        }

        // Per-shape rules: prefab presence + components, coupled-material
        // rules, face-flag sanity.
        static void CheckShapeDefinition(ShapeDefinition shape)
        {
            _shapesChecked++;

            if (shape.prefab == null)
            {
                ReportError($"Shape '{shape.displayName}': prefab is null.", shape);
            }
            else
            {
                CheckPrefabComponents(shape);
            }

            // Non-armour shapes use coupledMaterial — it must be present.
            if (shape.category != ShapeCategory.Armour && shape.coupledMaterial == null)
            {
                ReportError($"Shape '{shape.displayName}' ({shape.category}) has no coupledMaterial.", shape);
            }
            // Armour shapes ignore coupledMaterial — a populated value is
            // a confusion smell, not a bug.
            if (shape.category == ShapeCategory.Armour && shape.coupledMaterial != null)
            {
                ReportWarning($"Shape '{shape.displayName}' is Armour but has a populated coupledMaterial (the field is ignored for Armour).", shape);
            }

            // Face flags: at least one face must be valid for placement
            // to ever succeed. All-false is probably an intentional
            // unplaceable shape — warn rather than error.
            if (!shape.faceNegX && !shape.facePosX
             && !shape.faceNegY && !shape.facePosY
             && !shape.faceNegZ && !shape.facePosZ)
            {
                ReportWarning($"Shape '{shape.displayName}' has no valid attachment face (all face flags false).", shape);
            }
        }

        // Verifies the spawn prefab carries the components every placed
        // cube needs: CubeStats, a Collider (placement raycasts), a
        // Renderer (material application target), PlacedCubeData (cell
        // bookkeeping), and is on the PlacedCube layer.
        static void CheckPrefabComponents(ShapeDefinition shape)
        {
            GameObject prefab = shape.prefab;

            if (prefab.GetComponent<CubeStats>() == null)
                ReportError($"Shape '{shape.displayName}': prefab '{prefab.name}' has no CubeStats component.", shape);

            if (prefab.GetComponentInChildren<Collider>(includeInactive: true) == null)
                ReportError($"Shape '{shape.displayName}': prefab '{prefab.name}' has no Collider (placement raycasts need one).", shape);

            if (prefab.GetComponentInChildren<Renderer>(includeInactive: true) == null)
                ReportError($"Shape '{shape.displayName}': prefab '{prefab.name}' has no Renderer.", shape);

            if (prefab.GetComponent<PlacedCubeData>() == null)
                ReportError($"Shape '{shape.displayName}': prefab '{prefab.name}' has no PlacedCubeData component.", shape);

            int expectedLayer = LayerMask.NameToLayer("PlacedCube");
            if (expectedLayer >= 0 && prefab.layer != expectedLayer)
            {
                string actual = LayerMask.LayerToName(prefab.layer);
                ReportError($"Shape '{shape.displayName}': prefab '{prefab.name}' is on layer '{actual}', expected 'PlacedCube'.", shape);
            }
            // If expectedLayer < 0, CheckRequiredLayers already reported
            // the missing layer — no point repeating it per shape.
        }
```

- [ ] **Step 4: Fill in `CheckMaterialRegistry` + per-material helpers**

In `Assets/Scripts/Editor/RegistryValidator.cs`, replace this block:

```csharp
        // Filled in by Step 4.
        static void CheckMaterialRegistry(MaterialRegistry registry) { }
```

with:

```csharp
        // Walks every MaterialDefinition in the armour pool. Registry-
        // level concerns (non-empty list, no nulls, unique displayName)
        // are handled here; per-material detail is in
        // CheckMaterialDefinition.
        static void CheckMaterialRegistry(MaterialRegistry registry)
        {
            if (registry.materials == null || registry.materials.Length == 0)
            {
                ReportError("MaterialRegistry.materials is empty.", registry);
                return;
            }

            HashSet<string> seenNames = new HashSet<string>();
            for (int i = 0; i < registry.materials.Length; i++)
            {
                MaterialDefinition material = registry.materials[i];
                if (material == null)
                {
                    ReportError($"MaterialRegistry.materials[{i}] is null.", registry);
                    continue;
                }

                CheckMaterialDefinition(material, partOfArmourPool: true);

                if (string.IsNullOrEmpty(material.displayName))
                {
                    ReportError($"Material '{material.name}' has an empty displayName.", material);
                }
                else if (!seenNames.Add(material.displayName))
                {
                    ReportError($"Duplicate material displayName '{material.displayName}' in armour pool.", material);
                }
            }
        }

        // Per-material rules — apply to both the armour pool and any
        // coupled MaterialDefinition. `partOfArmourPool` selects which
        // counter to bump for the summary.
        static void CheckMaterialDefinition(MaterialDefinition material, bool partOfArmourPool)
        {
            if (partOfArmourPool) _armourMaterialsChecked++;
            else                  _coupledMaterialsChecked++;

            if (material.material == null)
                ReportError($"Material '{material.displayName}': URP Material asset is unassigned.", material);

            CheckStat(material, material.healthPoints, "healthPoints");
            CheckStat(material, material.armourValue, "armourValue");
            CheckStat(material, material.mass,        "mass");
        }

        // Stat sanity: NaN/Infinity is a bug (will propagate to runtime
        // math); negative is a smell (will usually clamp downstream but
        // is rarely intentional).
        static void CheckStat(MaterialDefinition material, float value, string fieldName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                ReportError($"Material '{material.displayName}': {fieldName} is not finite ({value}).", material);
                return;
            }
            if (value < 0f)
            {
                ReportWarning($"Material '{material.displayName}': {fieldName} is negative ({value}).", material);
            }
        }
```

- [ ] **Step 5: Fill in `CheckCoupledMaterials`**

In `Assets/Scripts/Editor/RegistryValidator.cs`, replace this block:

```csharp
        // Filled in by Step 5.
        static void CheckCoupledMaterials(ShapeRegistry shapeRegistry, MaterialRegistry materialRegistry) { }
```

with:

```csharp
        // For each non-armour shape, run the per-MaterialDefinition
        // checks against its coupledMaterial and warn if the coupled
        // material leaked into the armour MaterialRegistry pool.
        static void CheckCoupledMaterials(ShapeRegistry shapeRegistry, MaterialRegistry materialRegistry)
        {
            // Build the armour-pool set once for the leak check. A null
            // materialRegistry was already reported separately; here it
            // just means an empty pool, so no leak findings.
            HashSet<MaterialDefinition> armourPool = new HashSet<MaterialDefinition>();
            if (materialRegistry != null && materialRegistry.materials != null)
            {
                for (int i = 0; i < materialRegistry.materials.Length; i++)
                {
                    MaterialDefinition m = materialRegistry.materials[i];
                    if (m != null) armourPool.Add(m);
                }
            }

            if (shapeRegistry.shapes == null) return;

            // Dedupe: a coupled material reused across two shapes is
            // checked once. The null/missing case is already reported in
            // CheckShapeDefinition, so we skip nulls here without
            // re-reporting.
            HashSet<MaterialDefinition> seen = new HashSet<MaterialDefinition>();
            for (int i = 0; i < shapeRegistry.shapes.Length; i++)
            {
                ShapeDefinition shape = shapeRegistry.shapes[i];
                if (shape == null || shape.category == ShapeCategory.Armour) continue;
                MaterialDefinition coupled = shape.coupledMaterial;
                if (coupled == null) continue;
                if (!seen.Add(coupled)) continue;

                CheckMaterialDefinition(coupled, partOfArmourPool: false);

                if (armourPool.Contains(coupled))
                {
                    ReportWarning($"Coupled material '{coupled.displayName}' is also in the armour MaterialRegistry pool — coupled and pooled material spaces should be separate.", coupled);
                }
            }
        }
```

- [ ] **Step 6: Compile-verify**

Run `refresh_unity` (mode `force`, scope `all`, compile `request`, `wait_for_ready` true). Then poll `mcpforunity://editor/state` until `is_compiling` is false. Then `read_console` with `types: ["error", "warning"]`, `filter_text: "Assets/Scripts"`.

Expected: zero entries from `Assets/Scripts`. MCP-bridge "Client handler exited" / "Cannot access a disposed object" lines from `Packages/com.coplaydev.unity-mcp` are not errors and are filtered out by the `Assets/Scripts` filter.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Editor/RegistryValidator.cs
git commit -m "$(cat <<'EOF'
Add registry validation editor tool

Tools/CubeFly/Validate Registries runs on demand, checks the
ShapeRegistry / MaterialRegistry / their prefabs / coupled
materials / required layers, and reports findings to the Console
with an end-of-run summary dialog.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Manual verification

**No code.** Run the tool from the Unity Editor.

- [ ] **Step 1: Clean run**

In Unity, click **Tools → CubeFly → Validate Registries**.

Expected Console (filtered to `RegistryValidator`):
- A single info line: `RegistryValidator: Validation complete: 0 error(s), 0 warning(s), 5 shape(s), 4 armour material(s), 3 coupled material(s) checked.` (Counts: 5 shapes = Cube + Slope + WeaponPyramid + WeaponCylinder + UtilityThruster. 4 armour materials = A/B/C/D. 3 coupled materials = PyramidWeaponMatDef + CylinderWeaponMatDef + ThrusterMatDef.)

Expected dialog: title "Registry Validation", body "All checks passed.", single OK button.

If any finding appears on the clean state, **that is the value of shipping this tool** — investigate and fix the surfaced issue rather than the tool.

- [ ] **Step 2: Hand-inject failure modes**

Confirm each branch fires once. Restore each change after observing the finding.

**A. Empty displayName**
- In the Project window, open `Assets/Shapes/ShapeCube.asset`.
- In Inspector, clear the `Display Name` field (leave blank). Save (Ctrl/Cmd+S).
- Run the menu item. Expected: one error `RegistryValidator: Shape 'ShapeCube' has an empty displayName.`
- Restore the field to `Cube`. Save.

**B. Missing CubeStats**
- Open the `Assets/Prefabs/PlacedCube.prefab` in Prefab mode.
- Remove the `CubeStats` component from the root GameObject. Save the prefab.
- Run the menu item. Expected: one error `RegistryValidator: Shape 'Cube': prefab 'PlacedCube' has no CubeStats component.`
- Undo / restore the `CubeStats` component (use Unity's Undo or re-add and re-enter the original values: healthPoints 100, armourValue 10, mass 1). Save.

**C. All face flags false**
- Open `Assets/Shapes/ShapeCube.asset` in the Inspector.
- Uncheck all six face flags (faceNegX/PosX/NegY/PosY/NegZ/PosZ). Save.
- Run the menu item. Expected: one warning `RegistryValidator: Shape 'Cube' has no valid attachment face (all face flags false).`
- Restore all six face flags to true. Save.

**D. Duplicate displayName**
- Open `Assets/Shapes/ShapeSlope.asset` in Inspector.
- Change its `Display Name` from `Slope` to `Cube`. Save.
- Run the menu item. Expected: one error `RegistryValidator: Duplicate shape displayName 'Cube'.`
- Restore the displayName to `Slope`. Save.

- [ ] **Step 3: Confirm restoration**

Run the menu item one final time. Expected: "All checks passed." dialog and the clean summary line. If anything is still flagged, restore the offending change before continuing.

---

## Task 3: Push branch + open PR

After Task 2 confirms the tool works.

- [ ] **Step 1: Push the branch**

```bash
git push -u origin feat/asset-validation-tooling
```

- [ ] **Step 2: Open the PR**

```bash
gh pr create --title "Asset-validation tooling (F10) + roadmap deferrals" --body "$(cat <<'EOF'
## Summary
- **F10 — Asset-validation tooling:** new on-demand editor tool at `Tools/CubeFly/Validate Registries`. Checks `ShapeRegistry`, `MaterialRegistry`, every shape's spawn prefab, every coupled `MaterialDefinition`, and the required gameplay layers (`PlacedCube`, `AlphaCube`, `PreviewCube`). Findings go to the Console with the offending asset as click-context; a summary dialog reports the outcome.
- **ROADMAP update:** the audit's other deferred items (F5 tests/asmdefs, F6 ConstructModel, F7 BuildToolbar split, F8 HUD consolidation, plus architecture recs for HitContext, input centralisation, thruster system, docs-as-contract) move into a new **Architecture & infrastructure** subsection under *Later*, each with its trigger condition noted.

Design: `docs/superpowers/specs/2026-05-19-asset-validation-tooling-design.md`.

## Test plan
- [x] Clean run on the current state → "All checks passed."
- [x] Hand-injected failures fire exactly once each (empty displayName, missing CubeStats, all face flags false, duplicate displayName).
- [x] Compile clean, no console errors outside the tool's own `RegistryValidator` lines.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 3: Request Copilot review**

```bash
PR=$(gh pr view --json number -q .number)
gh api repos/entewurzelauskuh/CosmicScrapClub/pulls/$PR/requested_reviewers -X POST -f "reviewers[]=Copilot"
```

---

## Self-review

**Spec coverage** — every section of the design spec maps to a task:
- Discovery via `AssetDatabase.FindAssets("t:Type")` → Task 1 Step 1 (`FindSoleAsset<T>`).
- Required layers check → Task 1 Step 2.
- ShapeRegistry / ShapeDefinition / prefab-component / layer / coupled / face-flag checks → Task 1 Step 3.
- MaterialRegistry / per-MaterialDefinition (armour pool) checks → Task 1 Step 4.
- Coupled-material checks (per-MD plus leak warning) → Task 1 Step 5.
- Severity rules (error vs warning) → consistently applied via `ReportError` / `ReportWarning` in every step.
- Output (per-finding log with context, summary line, dialog) → Task 1 Step 1.
- File location `Assets/Scripts/Editor/RegistryValidator.cs` → Task 1 Step 1.
- Verification (clean run + four hand-injected failure modes) → Task 2.
- Spec's "out of scope" exclusions (EditMode tests, alpha cube, scene/bootstrap, orphan prefabs, auto-run) — no tasks, by design.

**Placeholder scan** — no TBD/TODO; every step contains complete code; exact commands with expected output.

**Type consistency** — names used consistently across steps:
- `TAG = "RegistryValidator"` referenced in `ReportError`/`ReportWarning` and the summary log.
- Counter fields `_errors`, `_warnings`, `_shapesChecked`, `_armourMaterialsChecked`, `_coupledMaterialsChecked` — defined in Step 1, incremented in Steps 3 and 4, read in Step 1's summary.
- `FindSoleAsset<T>(string typeLabel)` — defined Step 1, called Step 1 for both registries.
- `ReportError(string, UnityEngine.Object)` / `ReportWarning(string, UnityEngine.Object)` — defined Step 1, used throughout Steps 2–5.
- Top-level check methods (`CheckRequiredLayers`, `CheckShapeRegistry`, `CheckMaterialRegistry`, `CheckCoupledMaterials`) — stubbed Step 1, called from Step 1's orchestrator, filled in Steps 2–5.
- Helpers (`CheckLayer`, `CheckShapeDefinition`, `CheckPrefabComponents`, `CheckMaterialDefinition`, `CheckStat`) — introduced alongside their callers in Steps 2, 3, 4.
- `CheckMaterialDefinition(MaterialDefinition, bool partOfArmourPool)` signature used identically by both call sites (Step 4 armour-pool path, Step 5 coupled path).
