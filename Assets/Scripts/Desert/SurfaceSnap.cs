using UnityEngine;

namespace CubeFly.Desert
{
    // Seats this GameObject on the terrain surface at Awake: raycasts straight
    // down onto `terrainMask` and places the transform at (surface + clearance),
    // or `fallbackHeight` if no terrain is found. Runs at -2000 so it lands
    // BEFORE other Awake/Start logic (e.g. the construct's FlyController building
    // its cubes as children) reads the position.
    //
    // General desert placer (A1/A2): used by the construct spawn (clearance set
    // to hover the ship above the dunes) and by scattered targets/turrets
    // (clearance set so the base rests on the sand). No effect in scenes without
    // terrain on `terrainMask` — the fallback height is used and a warning logged.
    [DefaultExecutionOrder(-2000)]
    public class SurfaceSnap : MonoBehaviour
    {
        [Tooltip("Layers treated as terrain for the downward raycast (set to World).")]
        [SerializeField] LayerMask terrainMask = ~0;

        [Tooltip("Height above the found surface to place this object's origin. Construct: clears its half-height + margin to hover. Target/turret: its half-height, so the base rests on the surface.")]
        [SerializeField] float clearance = 9f;

        [Tooltip("Y to start the downward ray from — well above any dune/formation peak.")]
        [SerializeField] float rayStartHeight = 200f;

        [Tooltip("Fallback Y if the ray finds no terrain under this object (never sit at an unknown height).")]
        [SerializeField] float fallbackHeight = 20f;

        const string TAG = "SurfaceSnap";

        void Awake()
        {
            Vector3 p = transform.position;
            Vector3 origin = new Vector3(p.x, rayStartHeight, p.z);

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                                 rayStartHeight * 2f, terrainMask, QueryTriggerInteraction.Ignore))
            {
                // Snap onto the surface. Deliberately no success log here: with
                // ~25 seated objects (construct + targets + turrets) it spams the
                // console every Play and buries real warnings. Only the miss warns.
                transform.position = new Vector3(p.x, hit.point.y + clearance, p.z);
            }
            else
            {
                transform.position = new Vector3(p.x, fallbackHeight, p.z);
                Debug.unityLogger.LogWarning(TAG,
                    $"No terrain under '{name}' ({p.x:F1}, {p.z:F1}) on the terrain mask — using fallback y={fallbackHeight:F1}.");
            }
        }
    }
}
