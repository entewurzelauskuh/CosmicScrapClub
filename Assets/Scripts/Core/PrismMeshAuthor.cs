using UnityEngine;

namespace CubeFly.Core
{
    // Assigns the runtime-generated triangular-prism mesh to this
    // GameObject's MeshFilter (and MeshCollider, if present) on Awake
    // — but ONLY when the slot is currently empty. If the prefab has
    // an authored / imported mesh wired in (e.g. the imported
    // Slope.obj), this component becomes a no-op and never overwrites
    // it. Same null-guard pattern as PyramidMeshAuthor (and any
    // future per-shape runtime-mesh helpers).
    [RequireComponent(typeof(MeshFilter))]
    public class PrismMeshAuthor : MonoBehaviour
    {
        void Awake()
        {
            Mesh mesh = PrimitiveMeshes.TriangularPrism;

            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh == null) mf.sharedMesh = mesh;

            // Optional: keep a MeshCollider in lock-step with the visual
            // mesh so prism-specific colliders (if used in the future)
            // match the rendered shape exactly. The shipped prism prefab
            // uses a plain BoxCollider for cell-bounds raycasts, so this
            // is a no-op there. Also null-gated so user-authored meshes
            // aren't overwritten.
            MeshCollider mc = GetComponent<MeshCollider>();
            if (mc != null && mc.sharedMesh == null) mc.sharedMesh = mesh;
        }
    }
}
