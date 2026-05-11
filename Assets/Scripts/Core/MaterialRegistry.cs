using UnityEngine;

namespace CubeFly.Core
{
    // Single shared list of MaterialDefinitions for armour shapes.
    // Independent axis from ShapeRegistry — a placement records
    // (shapeIndex, materialIndex) where shapeIndex picks geometry
    // and materialIndex picks colour + stats. No requirement that
    // the two arrays be the same length or in any related order.
    // Weapon shapes bypass this registry entirely via
    // ShapeDefinition.weaponMaterial.
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
