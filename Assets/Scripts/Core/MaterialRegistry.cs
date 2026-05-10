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
    }
}
