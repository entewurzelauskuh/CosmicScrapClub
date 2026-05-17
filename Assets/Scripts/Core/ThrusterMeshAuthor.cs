using UnityEngine;

namespace CubeFly.Core
{
    // Assigns the runtime-generated cone mesh to this GameObject's
    // MeshFilter (and MeshCollider, if present) on Awake — but only
    // when the slot is empty. If the prefab references a baked cone
    // mesh directly, this component becomes a no-op. Mirror of
    // CylinderMeshAuthor / PyramidMeshAuthor.
    [RequireComponent(typeof(MeshFilter))]
    public class ThrusterMeshAuthor : MonoBehaviour
    {
        void Awake()
        {
            Mesh mesh = PrimitiveMeshes.Cone;

            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh == null) mf.sharedMesh = mesh;

            MeshCollider mc = GetComponent<MeshCollider>();
            if (mc != null && mc.sharedMesh == null) mc.sharedMesh = mesh;
        }
    }
}
