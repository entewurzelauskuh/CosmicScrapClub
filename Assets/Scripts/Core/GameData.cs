using System.Collections.Generic;
using UnityEngine;

namespace CubeFly.Core
{
    // A single placed cube: which grid cell it occupies and which CubeType it
    // is. The TypeIndex is an offset into the active CubeTypeRegistry so the
    // build and fly scenes resolve to the same prefab.
    public readonly struct Placement
    {
        public readonly Vector3Int Cell;
        public readonly int TypeIndex;
        public readonly Quaternion Rotation;

        public Placement(Vector3Int cell, int typeIndex, Quaternion rotation)
        {
            Cell = cell;
            TypeIndex = typeIndex;
            Rotation = rotation;
        }

        public override string ToString() => $"{Cell}@type{TypeIndex}@rot{Rotation.eulerAngles}";
    }

    public static class GameData
    {
        const string TAG = "GameData";

        // Both structures stay in sync. The list preserves insertion order
        // (used by re-instantiation, flood-fill snapshots, bounds). The dict
        // is for O(1) occupancy / type lookups.
        static readonly List<Placement> _placedCubes = new();
        static readonly Dictionary<Vector3Int, int> _typeByCell = new();

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

        public static bool TryAdd(Vector3Int cell, int typeIndex, Quaternion rotation)
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
            _placedCubes.Add(new Placement(cell, typeIndex, rotation));
            _typeByCell[cell] = typeIndex;
            Debug.unityLogger.Log(TAG,
                $"Cell added: {cell} (type {typeIndex}, rot {rotation.eulerAngles}). Total placed: {_placedCubes.Count}");
            return true;
        }

        public static void Remove(Vector3Int cell)
        {
            for (int i = _placedCubes.Count - 1; i >= 0; i--)
            {
                if (_placedCubes[i].Cell == cell)
                {
                    _placedCubes.RemoveAt(i);
                    _typeByCell.Remove(cell);
                    Debug.unityLogger.Log(TAG, $"Cell removed: {cell}. Total placed: {_placedCubes.Count}");
                    return;
                }
            }
        }

        public static bool IsOccupied(Vector3Int cell) => _typeByCell.ContainsKey(cell);

        public static int GetTypeAt(Vector3Int cell)
            => _typeByCell.TryGetValue(cell, out int t) ? t : -1;

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
            _typeByCell.Clear();
            Debug.unityLogger.Log(TAG, "GameData cleared.");
        }

        // Sum of all placed cubes' effective masses (per CubeTypeDefinition).
        // Does NOT include the alpha cube — callers add that separately.
        public static float SumPlacedMasses(CubeTypeRegistry registry)
        {
            if (registry == null) return 0f;
            float total = 0f;
            for (int i = 0; i < _placedCubes.Count; i++)
            {
                CubeTypeDefinition def = registry.Get(_placedCubes[i].TypeIndex);
                if (def != null) total += def.EffectiveMass();
            }
            return total;
        }
    }
}
