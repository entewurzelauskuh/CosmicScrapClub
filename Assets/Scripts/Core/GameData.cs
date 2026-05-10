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

        public static void Remove(Vector3Int cell)
        {
            for (int i = _placedCubes.Count - 1; i >= 0; i--)
            {
                if (_placedCubes[i].Cell == cell)
                {
                    _placedCubes.RemoveAt(i);
                    _byCell.Remove(cell);
                    Debug.unityLogger.Log(TAG, $"Cell removed: {cell}. Total placed: {_placedCubes.Count}");
                    return;
                }
            }
        }

        public static bool IsOccupied(Vector3Int cell) => _byCell.ContainsKey(cell);

        // Returns the full placement at a cell, or default(Placement) when
        // empty. Callers should check IsOccupied first.
        public static Placement GetPlacementAt(Vector3Int cell)
            => _byCell.TryGetValue(cell, out Placement p) ? p : default;

        // Symmetric face-validity check. A placement at `cell` with
        // (shape, rotation) is valid when, for at least one of the six
        // cell-face neighbours, BOTH:
        //   • the new piece has a real surface on the face pointing at
        //     that neighbour (in its rotation), AND
        //   • the neighbour piece has a real surface on the face pointing
        //     back at us (in its rotation).
        //
        // The alpha cube at the origin counts as a cube — all six faces
        // valid. Empty cells are skipped. The check reduces to the old
        // "any face-adjacent cell is occupied" rule for all-cube
        // constructs, since cubes have all six faces valid.
        public static bool IsValidAttachment(Vector3Int cell, int newShapeIndex,
            Quaternion newRotation, ShapeRegistry shapeRegistry)
        {
            if (shapeRegistry == null) return false;
            ShapeDefinition newShape = shapeRegistry.Get(newShapeIndex);
            if (newShape == null) return false;

            for (int i = 0; i < Neighbors.Length; i++)
            {
                Vector3Int dir = Neighbors[i];
                Vector3Int neighborCell = cell + dir;

                bool isAlpha = neighborCell == Vector3Int.zero;
                if (!isAlpha && !IsOccupied(neighborCell)) continue;

                // 1. New piece's face toward the neighbour must be valid.
                if (!newShape.IsWorldFaceValid(dir, newRotation)) continue;

                // 2. Neighbour's face toward us must be valid. The alpha
                //    cube is a cube → all six trivially valid.
                if (isAlpha) return true;

                Placement neighborPlacement = GetPlacementAt(neighborCell);
                ShapeDefinition neighborShape = shapeRegistry.Get(neighborPlacement.ShapeIndex);
                if (neighborShape == null) continue;
                if (!neighborShape.IsWorldFaceValid(-dir, neighborPlacement.Rotation)) continue;

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
            Debug.unityLogger.Log(TAG, "GameData cleared.");
        }

        // Sum of all placed cubes' masses. Does NOT include the alpha
        // cube — callers add that separately. Resolves each
        // placement's material via ShapeDefinition.ResolveMaterial so
        // weapon shapes pull from their coupled weaponMaterial and
        // armour shapes pull from MaterialRegistry by index.
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

                    // Weapon shapes have a coupled material that the
                    // load path resolves via the shape, not via name
                    // lookup in MaterialRegistry — the saved material
                    // name is informational for those entries. Set
                    // MaterialIndex to -1 (sentinel "use coupled").
                    ShapeDefinition shape = shapeRegistry.Get(shapeIndex);
                    int materialIndex;
                    if (shape != null && shape.IsWeapon)
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
                placements = new PlacementRecord[_placedCubes.Count],
            };

            for (int i = 0; i < _placedCubes.Count; i++)
            {
                Placement p = _placedCubes[i];
                ShapeDefinition s = shapeRegistry != null ? shapeRegistry.Get(p.ShapeIndex) : null;
                // For weapon shapes the material is implicit (coupled
                // to the shape) — we still write a non-empty name for
                // diagnosability, but the load path resolves via the
                // shape rather than name-lookup.
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
