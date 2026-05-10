using UnityEngine;

namespace CubeFly.Core
{
    // Single shared list of CubeTypeDefinitions. BuildManager and
    // FlyController each reference the same registry asset so the type-index
    // a placed cube records in GameData stays consistent across scenes.
    [CreateAssetMenu(fileName = "CubeTypeRegistry", menuName = "CubeFly/Cube Type Registry")]
    public class CubeTypeRegistry : ScriptableObject
    {
        [Tooltip("Order matters — index here is what GameData stores per placed cell.")]
        public CubeTypeDefinition[] types;

        public int Count => types == null ? 0 : types.Length;

        public CubeTypeDefinition Get(int index)
        {
            if (types == null || index < 0 || index >= types.Length) return null;
            return types[index];
        }
    }
}
