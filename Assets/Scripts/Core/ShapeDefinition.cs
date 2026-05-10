using UnityEngine;

namespace CubeFly.Core
{
    // One placeable shape — geometry + collider only. Stats / colour /
    // gameplay identity come from the chosen MaterialDefinition at
    // spawn time. A shape is independent of material so the toolbar
    // can present them as orthogonal axes.
    [CreateAssetMenu(fileName = "Shape", menuName = "CubeFly/Shape")]
    public class ShapeDefinition : ScriptableObject
    {
        [Tooltip("User-facing shape name shown on the build toolbar.")]
        public string displayName = "Shape";

        [Tooltip("Prefab spawned for this shape. Must carry a CubeStats and a 1×1×1 collider so adjacency/face-detection works in the cell-based grid. The MaterialDefinition supplies the renderer material and stat values at spawn.")]
        public GameObject prefab;
    }
}
