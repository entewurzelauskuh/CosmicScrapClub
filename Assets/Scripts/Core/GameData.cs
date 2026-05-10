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

        internal static readonly Vector3Int[] Neighbors =
        {
            new Vector3Int( 1, 0, 0),
            new Vector3Int(-1, 0, 0),
            new Vector3Int( 0, 1, 0),
            new Vector3Int( 0,-1, 0),
            new Vector3Int( 0, 0, 1),
            new Vector3Int( 0, 0,-1),
        };

        public static bool TryAdd(Vector3Int cell, int shapeIndex, int materialIndex, Quaternion rotation)
        {
            if (cell == Vector3Int.zero)
            {
                Debug.unityLogger.LogWarning(TAG, "TryAdd rejected: cannot place at origin (0,0,0)");
                return false;
            }
            if (IsOccupied(cell))
            {
                Debug.unityLogger.LogWarning(TAG, $"TryAdd rejected (occupied): {cell}");
                return false;
            }
            if (!IsAdjacentToExisting(cell))
            {
                Debug.unityLogger.LogWarning(TAG, $"TryAdd rejected (not adjacent): {cell}");
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

        public static bool IsAdjacentToExisting(Vector3Int cell)
        {
            for (int i = 0; i < Neighbors.Length; i++)
            {
                Vector3Int neighbor = cell + Neighbors[i];
                if (neighbor == Vector3Int.zero) return true;
                if (IsOccupied(neighbor)) return true;
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

        // Sum of all placed cubes' masses, looked up by their material
        // index. Does NOT include the alpha cube — callers add that
        // separately. Returns 0 if the registry is null or any
        // placement's material lookup fails.
        public static float SumPlacedMasses(MaterialRegistry registry)
        {
            if (registry == null) return 0f;
            float total = 0f;
            for (int i = 0; i < _placedCubes.Count; i++)
            {
                MaterialDefinition def = registry.Get(_placedCubes[i].MaterialIndex);
                if (def != null) total += def.mass;
            }
            return total;
        }
    }
}
