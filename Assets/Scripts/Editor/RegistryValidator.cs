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
    }
}
