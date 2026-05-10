using UnityEngine;

namespace CubeFly.Core
{
    // Single shared list of MaterialDefinitions. Indexed parallel to
    // ShapeRegistry — placements record (shapeIndex, materialIndex).
    [CreateAssetMenu(fileName = "MaterialRegistry", menuName = "CubeFly/Material Registry")]
    public class MaterialRegistry : ScriptableObject
    {
        [Tooltip("Order matters — index here is what GameData stores per placed cell.")]
        public MaterialDefinition[] materials;

        public int Count => materials == null ? 0 : materials.Length;

        public MaterialDefinition Get(int index)
        {
            if (materials == null || index < 0 || index >= materials.Length) return null;
            return materials[index];
        }

        // Looks up a material by its displayName. Used by the save
        // layer so saves reference materials by name (registry-stable)
        // rather than index. Returns -1 when no match. Comparison is
        // ordinal case-sensitive.
        public int FindIndexByName(string displayName)
        {
            if (materials == null || string.IsNullOrEmpty(displayName)) return -1;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null && materials[i].displayName == displayName) return i;
            }
            return -1;
        }
    }
}
