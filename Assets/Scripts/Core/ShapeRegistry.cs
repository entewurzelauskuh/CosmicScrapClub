using UnityEngine;

namespace CubeFly.Core
{
    // Single shared list of ShapeDefinitions. BuildManager and
    // FlyController each reference the same registry asset so the shape
    // index a placement records in GameData stays consistent across
    // scenes.
    [CreateAssetMenu(fileName = "ShapeRegistry", menuName = "CubeFly/Shape Registry")]
    public class ShapeRegistry : ScriptableObject
    {
        [Tooltip("Order matters — index here is what GameData stores per placed cell.")]
        public ShapeDefinition[] shapes;

        public int Count => shapes == null ? 0 : shapes.Length;

        public ShapeDefinition Get(int index)
        {
            if (shapes == null || index < 0 || index >= shapes.Length) return null;
            return shapes[index];
        }

        // Looks up a shape by its displayName. Used by the save layer
        // so saves reference shapes by name (registry-stable) rather
        // than index (brittle if the registry is reordered). Returns
        // -1 when no match. Comparison is ordinal case-sensitive.
        public int FindIndexByName(string displayName)
        {
            if (shapes == null || string.IsNullOrEmpty(displayName)) return -1;
            for (int i = 0; i < shapes.Length; i++)
            {
                if (shapes[i] != null && shapes[i].displayName == displayName) return i;
            }
            return -1;
        }
    }
}
