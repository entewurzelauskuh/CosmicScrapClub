using UnityEngine;

namespace CubeFly.Core
{
    // Assigns the runtime-generated solid-cylinder mesh to this
    // GameObject's MeshFilter (and MeshCollider, if present) on Awake,
    // only when the slot is empty. Mirror of CylinderMeshAuthor for the
    // Reactor cube's solid cylinder.
    [RequireComponent(typeof(MeshFilter))]
    public class ReactorMeshAuthor : MonoBehaviour
    {
        void Awake()
        {
            Mesh mesh = PrimitiveMeshes.SolidCylinder;

            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh == null) mf.sharedMesh = mesh;

            MeshCollider mc = GetComponent<MeshCollider>();
            if (mc != null && mc.sharedMesh == null) mc.sharedMesh = mesh;
        }
    }
}
