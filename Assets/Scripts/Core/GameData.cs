using System.Collections.Generic;
using UnityEngine;

namespace CubeFly.Core
{
    // A single placed cube: which grid cell it occupies and which
    // (shape, material) tuple it is. Both indices reference the active
    // ShapeRegistry / MaterialRegistry so the build and fly scenes
    // resolve to the same prefabs and materials.
    public readonly struct Placement
    {
        public readonly Vector3Int Cell;
        public readonly int ShapeIndex;
        public readonly int MaterialIndex;
        public readonly Quaternion Rotation;

        public Placement(Vector3Int cell, int shapeIndex, int materialIndex, Quaternion rotation)
        {
            Cell = cell;
            ShapeIndex = shapeIndex;
            MaterialIndex = materialIndex;
            Rotation = rotation;
        }

        public override string ToString()
            => $"{Cell}@shape{ShapeIndex}+mat{MaterialIndex}@rot{Rotation.eulerAngles}";
    }

    public static class GameData
    {
        const string TAG = "GameData";

        // Both structures stay in sync. The list preserves insertion order
        // (used by re-instantiation, flood-fill snapshots, bounds). The dict
        // is for O(1) occupancy / placement lookups.
        static readonly List<Placement> _placedCubes = new();
        static readonly Dictionary<Vector3Int, Placement> _byCell = new();

        public static IReadOnlyList<Placement> PlacedCubes => _placedCubes;

        // Which slot the BuildScene autosaves to. Set by HangarSelect
        // before transitioning into BuildScene. -1 means "no slot
        // armed" — autosave is disabled in that case (e.g. when the
        // developer presses Play directly on BuildScene).
        public static int ActiveSlot { get; private set; } = -1;

        // The construct's ship class — picked via the BuildScene
        // dropdown, persisted per save slot. Drives alpha-cube HP, the
        // build mass cap, and the Fly-mode movement multiplier (see
        // ShipClasses). Defaults to Allrounder for a fresh construct.
        public static ShipClass ActiveShipClass { get; private set; } = ShipClass.Allrounder;

        // True while LoadFromSave is replaying a serialised construct.
        // The flag is consumed by TryAdd to bypass adjacency / occupancy
        // validation on load, so saves are treated as authoritative
        // even if the validation rules have evolved between versions.
        static bool _loading;

        internal static readonly Vector3Int[] Neighbors =
        {
            new Vector3Int( 1, 0, 0),
            new Vector3Int(-1, 0, 0),
            new Vector3Int( 0, 1, 0),
            new Vector3Int( 0,-1, 0),
            new Vector3Int( 0, 0, 1),
            new Vector3Int( 0, 0,-1),
        };

        public static bool TryAdd(Vector3Int cell, int shapeIndex, int materialIndex,
            Quaternion rotation, ShapeRegistry shapeRegistry)
        {
            if (cell == Vector3Int.zero)
            {
                Debug.unityLogger.LogWarning(TAG, "TryAdd rejected: cannot place at origin (0,0,0)");
                return false;
            }
            // Validation is skipped during LoadFromSave — the persisted
            // construct is treated as authoritative. Origin remains
            // rejected because that cell is the alpha cube's slot, and
            // duplicate cells within a single save are still rejected
            // to avoid overwriting earlier placements silently.
            if (!_loading)
            {
                if (IsOccupied(cell))
                {
                    Debug.unityLogger.LogWarning(TAG, $"TryAdd rejected (occupied): {cell}");
                    return false;
                }
                // Distinguish registry / lookup failures from genuine
                // face-validity failures so the diagnostic identifies
                // the real reason. Otherwise a misconfigured scene
                // shows up as "no valid attachment face" indefinitely.
                if (shapeRegistry == null)
                {
                    Debug.unityLogger.LogWarning(TAG,
                        $"TryAdd rejected (no ShapeRegistry): {cell} shape={shapeIndex}");
                    return false;
                }
                if (shapeRegistry.Get(shapeIndex) == null)
                {
                    Debug.unityLogger.LogWarning(TAG,
                        $"TryAdd rejected (unknown shape index {shapeIndex}): {cell}");
                    return false;
                }
                if (!IsValidAttachment(cell, shapeIndex, rotation, shapeRegistry))
                {
                    Debug.unityLogger.LogWarning(TAG,
                        $"TryAdd rejected (no valid attachment face): {cell} shape={shapeIndex} rot={rotation.eulerAngles}");
                    return false;
                }
            }
            else if (IsOccupied(cell))
            {
                Debug.unityLogger.LogWarning(TAG, $"LoadFromSave: duplicate cell {cell} skipped.");
                return false;
            }
            Placement p = new Placement(cell, shapeIndex, materialIndex, rotation);
            _placedCubes.Add(p);
            _byCell[cell] = p;
            Debug.unityLogger.Log(TAG,
                $"Cell added: {cell} (shape {shapeIndex}, material {materialIndex}, rot {rotation.eulerAngles}). Total placed: {_placedCubes.Count}");
            return true;
        }

        // Returns true when a placement occupied `cell` and was removed;
        // false when no placement was there (so callers can tell whether
        // a real construct cube actually left the list).
        public static bool Remove(Vector3Int cell)
        {
            for (int i = _placedCubes.Count - 1; i >= 0; i--)
            {
                if (_placedCubes[i].Cell == cell)
                {
                    _placedCubes.RemoveAt(i);
                    _byCell.Remove(cell);
                    Debug.unityLogger.Log(TAG, $"Cell removed: {cell}. Total placed: {_placedCubes.Count}");
                    return true;
                }
            }
            return false;
        }

        public static bool IsOccupied(Vector3Int cell) => _byCell.ContainsKey(cell);

        // Returns the full placement at a cell, or default(Placement) when
        // empty. Callers should check IsOccupied first.
        public static Placement GetPlacementAt(Vector3Int cell)
            => _byCell.TryGetValue(cell, out Placement p) ? p : default;

        // Face-aware connectivity primitive — shared by placement
        // validation (IsValidAttachment) and build cleanup
        // (BuildManager.RemoveDanglingCubes) so both judge "connected"
        // by the same rule.
        //
        // Returns true when a piece (sourceShape, sourceRotation) at
        // `cell` is face-connected to the cell-face neighbour in
        // direction `dir`: the neighbour cell must be occupied (or the
        // alpha cube at the origin), AND both touching faces must carry a
        // real surface — the source's face toward the neighbour and the
        // neighbour's face back toward the source.
        //
        // The alpha cube is a six-faces-valid cube; pass sourceShape ==
        // null to treat the SOURCE as the alpha. A null shapeRegistry
        // disables the face checks and falls back to pure occupancy (the
        // pre-face-aware behaviour) so cleanup degrades safely if the
        // registry is unset rather than pruning the whole build.
        public static bool HasFaceConnection(Vector3Int cell, ShapeDefinition sourceShape,
            Quaternion sourceRotation, Vector3Int dir, ShapeRegistry shapeRegistry)
        {
            Vector3Int neighborCell = cell + dir;
            bool neighborIsAlpha = neighborCell == Vector3Int.zero;
            if (!neighborIsAlpha && !IsOccupied(neighborCell)) return false;

            // No registry → can't read face flags; fall back to pure
            // occupancy so a missing registry can't delete the build.
            if (shapeRegistry == null) return true;

            // Source's face toward the neighbour. A null sourceShape is
            // the alpha cube — all six faces valid.
            if (sourceShape != null && !sourceShape.IsWorldFaceValid(dir, sourceRotation))
                return false;

            if (neighborIsAlpha) return true;

            Placement neighborPlacement = GetPlacementAt(neighborCell);
            ShapeDefinition neighborShape = shapeRegistry.Get(neighborPlacement.ShapeIndex);
            if (neighborShape == null) return false;
            return neighborShape.IsWorldFaceValid(-dir, neighborPlacement.Rotation);
        }

        // Symmetric face-validity check for PLACEMENT. A placement at
        // `cell` with (shape, rotation) is valid when it is face-connected
        // (HasFaceConnection) to at least one of its six cell-face
        // neighbours. The alpha cube at the origin counts as a cube — all
        // six faces valid. The check reduces to the old "any face-adjacent
        // cell is occupied" rule for all-cube constructs, since cubes have
        // all six faces valid.
        public static bool IsValidAttachment(Vector3Int cell, int newShapeIndex,
            Quaternion newRotation, ShapeRegistry shapeRegistry)
        {
            if (shapeRegistry == null) return false;
            ShapeDefinition newShape = shapeRegistry.Get(newShapeIndex);
            if (newShape == null) return false;

            for (int i = 0; i < Neighbors.Length; i++)
            {
                if (HasFaceConnection(cell, newShape, newRotation, Neighbors[i], shapeRegistry))
                    return true;
            }
            return false;
        }

        public static Bounds GetConstructBounds()
        {
            Bounds bounds = new Bounds(Vector3.zero, Vector3.one);
            for (int i = 0; i < _placedCubes.Count; i++)
            {
                Vector3Int c = _placedCubes[i].Cell;
                bounds.Encapsulate(new Bounds(new Vector3(c.x, c.y, c.z), Vector3.one));
            }
            return bounds;
        }

        public static void Clear()
        {
            _placedCubes.Clear();
            _byCell.Clear();
            // A fresh construct starts as Allrounder. HangarSelect calls
            // Clear() for an empty slot, so a new build always begins
            // here regardless of the previous slot's class.
            ActiveShipClass = ShipClass.Allrounder;
            Debug.unityLogger.Log(TAG, "GameData cleared.");
        }

        // Set the construct's ship class. Called by the BuildScene
        // dropdown and by LoadFromSave. The caller is responsible for
        // any downstream re-application (alpha HP, mass-cap readout) —
        // GameData just holds the value.
        public static void SetShipClass(ShipClass shipClass)
        {
            ActiveShipClass = shipClass;
            Debug.unityLogger.Log(TAG, $"Ship class set to {shipClass}.");
        }

        // Sum of all placed cubes' masses. Does NOT include the alpha
        // cube — callers add that separately. Resolves each
        // placement's material via ShapeDefinition.ResolveMaterial so
        // non-armour shapes (Weapon, Utility) pull from their coupled
        // coupledMaterial and armour shapes pull from MaterialRegistry
        // by index. Placements whose shape or material lookup fails
        // (registry null, shape not in registry, non-armour shape with
        // missing coupledMaterial) are silently skipped — this can
        // under-count total mass if the registries are misconfigured.
        // Returns 0 when `shapes` is null.
        public static float SumPlacedMasses(ShapeRegistry shapes, MaterialRegistry materials)
        {
            if (shapes == null) return 0f;
            float total = 0f;
            for (int i = 0; i < _placedCubes.Count; i++)
            {
                Placement p = _placedCubes[i];
                ShapeDefinition shape = shapes.Get(p.ShapeIndex);
                MaterialDefinition def = shape != null
                    ? shape.ResolveMaterial(p.MaterialIndex, materials)
                    : null;
                if (def != null) total += def.mass;
            }
            return total;
        }

        public static float SumPlacedHealthPoints(ShapeRegistry shapes, MaterialRegistry materials)
        {
            if (shapes == null) return 0f;
            float total = 0f;
            for (int i = 0; i < _placedCubes.Count; i++)
            {
                Placement p = _placedCubes[i];
                ShapeDefinition shape = shapes.Get(p.ShapeIndex);
                MaterialDefinition def = shape != null
                    ? shape.ResolveMaterial(p.MaterialIndex, materials)
                    : null;
                if (def != null) total += def.healthPoints;
            }
            return total;
        }

        // Sets which slot subsequent autosaves target. -1 disables
        // autosave (used when BuildScene is entered without going
        // through HangarSelect — e.g. Play-from-scene during dev).
        public static void SetActiveSlot(int slotIndex)
        {
            ActiveSlot = slotIndex;
            Debug.unityLogger.Log(TAG, $"Active slot set to {slotIndex}.");
        }

        // Replay a serialised construct into in-memory state. Clears
        // existing placements, then re-adds each PlacementRecord with
        // adjacency / occupancy validation suspended. Unknown shape /
        // material names are logged and skipped — the caller can then
        // run flood-fill cleanup if it cares about graph integrity.
        public static void LoadFromSave(ConstructSave save,
            ShapeRegistry shapeRegistry, MaterialRegistry materialRegistry)
        {
            Clear();
            if (save == null || save.placements == null)
            {
                Debug.unityLogger.LogWarning(TAG, "LoadFromSave: null or empty save.");
                return;
            }
            if (shapeRegistry == null || materialRegistry == null)
            {
                Debug.unityLogger.LogError(TAG, "LoadFromSave: registry references missing.");
                return;
            }

            // Restore the ship class. A v1 save has no shipClass field,
            // so save.shipClass is "" → Parse falls back to Allrounder.
            ActiveShipClass = ShipClasses.Parse(save.shipClass);

            _loading = true;
            try
            {
                int loaded = 0, skipped = 0;
                for (int i = 0; i < save.placements.Length; i++)
                {
                    PlacementRecord r = save.placements[i];
                    int shapeIndex = shapeRegistry.FindIndexByName(r.shape);
                    if (shapeIndex < 0)
                    {
                        Debug.unityLogger.LogWarning(TAG,
                            $"LoadFromSave: unknown shape '{r.shape}' at {r.cell} — skipped.");
                        skipped++;
                        continue;
                    }

                    // Weapon and Utility shapes have a coupled material
                    // that the load path resolves via the shape, not
                    // via name lookup in MaterialRegistry — the saved
                    // material name is informational for those entries.
                    // Set MaterialIndex to -1 (sentinel "use coupled").
                    ShapeDefinition shape = shapeRegistry.Get(shapeIndex);
                    int materialIndex;
                    if (shape != null && shape.UsesCoupledMaterial)
                    {
                        materialIndex = -1;
                    }
                    else
                    {
                        materialIndex = materialRegistry.FindIndexByName(r.material);
                        if (materialIndex < 0)
                        {
                            Debug.unityLogger.LogWarning(TAG,
                                $"LoadFromSave: unknown material '{r.material}' at {r.cell} — skipped.");
                            skipped++;
                            continue;
                        }
                    }

                    if (TryAdd(r.cell, shapeIndex, materialIndex, Quaternion.Euler(r.rotEuler), shapeRegistry))
                        loaded++;
                    else
                        skipped++;
                }
                Debug.unityLogger.Log(TAG,
                    $"LoadFromSave: {loaded} placement(s) loaded, {skipped} skipped " +
                    $"(slot '{save.slotName}', version {save.version}).");
            }
            finally
            {
                _loading = false;
            }
        }

        // Build a fresh ConstructSave from current state. Stores
        // shape / material names (not indices) for registry-stable
        // saves; denormalised totals are recomputed from registries.
        public static ConstructSave ToSave(string slotName,
            ShapeRegistry shapeRegistry, MaterialRegistry materialRegistry)
        {
            ConstructSave save = new ConstructSave
            {
                version = ConstructSave.CurrentVersion,
                slotName = slotName ?? string.Empty,
                shipClass = ShipClasses.DisplayName(ActiveShipClass),
                placements = new PlacementRecord[_placedCubes.Count],
            };

            for (int i = 0; i < _placedCubes.Count; i++)
            {
                Placement p = _placedCubes[i];
                ShapeDefinition s = shapeRegistry != null ? shapeRegistry.Get(p.ShapeIndex) : null;
                // For non-armour shapes (Weapon, Utility) the material
                // is implicit (coupled to the shape) — we still write a
                // non-empty name for diagnosability, but the load path
                // resolves via the shape rather than name-lookup.
                MaterialDefinition m = s != null ? s.ResolveMaterial(p.MaterialIndex, materialRegistry) : null;
                save.placements[i] = new PlacementRecord
                {
                    cell = p.Cell,
                    shape = s != null ? s.displayName : string.Empty,
                    material = m != null ? m.displayName : string.Empty,
                    rotEuler = p.Rotation.eulerAngles,
                };
            }

            save.totalMass = SumPlacedMasses(shapeRegistry, materialRegistry);
            save.totalHealthPoints = SumPlacedHealthPoints(shapeRegistry, materialRegistry);
            return save;
        }
    }
}
