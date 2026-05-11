using UnityEngine;

namespace CubeFly.Core
{
    // Assigns the runtime-generated hollow-cylinder mesh to this
    // GameObject's MeshFilter (and MeshCollider, if present) on Awake
    // — but only when the slot is empty. If the prefab references a
    // baked cylinder mesh directly, this component becomes a no-op.
    // Mirror of PyramidMeshAuthor / PrismMeshAuthor.
    [RequireComponent(typeof(MeshFilter))]
    public class CylinderMeshAuthor : MonoBehaviour
    {
        void Awake()
        {
            Mesh mesh = PrimitiveMeshes.HollowCylinder;

            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh == null) mf.sharedMesh = mesh;

            MeshCollider mc = GetComponent<MeshCollider>();
            if (mc != null && mc.sharedMesh == null) mc.sharedMesh = mesh;
        }
    }
}
