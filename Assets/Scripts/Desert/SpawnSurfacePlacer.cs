using UnityEngine;

namespace CubeFly.Desert
{
    // Seats this GameObject at a safe altitude above the terrain at spawn, so
    // the construct can never spawn clipped into a dune, the ground mesh, or a
    // formation. Runs in Awake — BEFORE FlyController.Start instantiates the
    // alpha + placed cubes as children — so the cubes are built at the
    // corrected height. The construct's Rigidbody has gravity off, so after
    // placement it simply hovers here until the player thrusts.
    //
    // A1 (desert -> FlyScene). Attach to the CubeConstruct GameObject in
    // FlyScene; it has no effect in scenes without terrain on `terrainMask`
    // (the fallback height is used and a warning logged).
    [DefaultExecutionOrder(-2000)]
    public class SpawnSurfacePlacer : MonoBehaviour
    {
        [Tooltip("Layers treated as terrain for the spawn raycast (set to World).")]
        [SerializeField] LayerMask terrainMask = ~0;

        [Tooltip("Height above the found surface to place the construct origin. Must clear the construct's own half-height plus a margin so no cube intersects terrain.")]
        [SerializeField] float clearance = 9f;

        [Tooltip("Y to start the downward ray from — well above any dune/formation peak.")]
        [SerializeField] float rayStartHeight = 200f;

        [Tooltip("Fallback Y if the ray finds no terrain under the spawn point (never spawn at an unknown height).")]
        [SerializeField] float fallbackHeight = 20f;

        const string TAG = "SpawnPlacer";

        void Awake()
        {
            Vector3 p = transform.position;
            Vector3 origin = new Vector3(p.x, rayStartHeight, p.z);

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                                 rayStartHeight * 2f, terrainMask, QueryTriggerInteraction.Ignore))
            {
                float y = hit.point.y + clearance;
                transform.position = new Vector3(p.x, y, p.z);
                Debug.unityLogger.Log(TAG,
                    $"Spawn seated at y={y:F2} (surface {hit.point.y:F2} + clearance {clearance:F1}) on '{hit.collider.name}'.");
            }
            else
            {
                transform.position = new Vector3(p.x, fallbackHeight, p.z);
                Debug.unityLogger.LogWarning(TAG,
                    $"No terrain under spawn ({p.x:F1}, {p.z:F1}) on the terrain mask — using fallback y={fallbackHeight:F1}.");
            }
        }
    }
}
