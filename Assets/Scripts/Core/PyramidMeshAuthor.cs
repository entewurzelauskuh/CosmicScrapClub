using UnityEngine;

namespace CubeFly.Core
{
    // Assigns the runtime-generated square-pyramid mesh to this
    // GameObject's MeshFilter (and MeshCollider, if present) on Awake
    // — but only when the slot is empty. If the prefab references
    // the imported Pyramid.obj mesh directly, this component becomes
    // a no-op. Mirror of PrismMeshAuthor for the slope shape.
    [RequireComponent(typeof(MeshFilter))]
    public class PyramidMeshAuthor : MonoBehaviour
    {
        void Awake()
        {
            Mesh mesh = PrimitiveMeshes.SquarePyramid;

            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh == null) mf.sharedMesh = mesh;

            MeshCollider mc = GetComponent<MeshCollider>();
            if (mc != null && mc.sharedMesh == null) mc.sharedMesh = mesh;
        }
    }
}
