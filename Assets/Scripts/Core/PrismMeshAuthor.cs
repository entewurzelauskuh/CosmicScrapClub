using UnityEngine;

namespace CubeFly.Core
{
    // Assigns the runtime-generated triangular-prism mesh to this
    // GameObject's MeshFilter (and MeshCollider, if present) on Awake.
    // The prefab YAML therefore doesn't need to reference a baked .asset
    // mesh — the geometry is owned by code and survives serialization
    // changes.
    [RequireComponent(typeof(MeshFilter))]
    public class PrismMeshAuthor : MonoBehaviour
    {
        void Awake()
        {
            Mesh mesh = PrimitiveMeshes.TriangularPrism;

            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != mesh) mf.sharedMesh = mesh;

            // Optional: keep a MeshCollider in lock-step with the visual
            // mesh so prism-specific colliders (if used in the future)
            // match the rendered shape exactly. The shipped prism prefab
            // uses a plain BoxCollider for cell-bounds raycasts, so this
            // is a no-op there.
            MeshCollider mc = GetComponent<MeshCollider>();
            if (mc != null && mc.sharedMesh != mesh) mc.sharedMesh = mesh;
        }
    }
}
