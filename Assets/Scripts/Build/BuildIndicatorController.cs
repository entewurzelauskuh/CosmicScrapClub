using CubeFly.Core;
using UnityEngine;

namespace CubeFly.Build
{
    // Owns one runtime-spawned indicator (red arrow) and reparents it to the
    // construct's "frontmost" cube — the one with the highest Z, or the alpha
    // cube as the tie-breaker when no placed cube is further forward.
    //
    // Lives only in the BuildScene; FlyScene has no instance, so the arrow
    // simply vanishes the moment the player flies and reappears on next
    // BuildScene load (the controller is fresh per scene; the indicator is
    // not DontDestroyOnLoad).
    public class BuildIndicatorController : MonoBehaviour
    {
        [SerializeField] BuildManager buildManager;
        [SerializeField] GameObject indicatorPrefab;

        const string TAG = "BuildIndicator";

        GameObject _indicatorInstance;

        void Start()
        {
            if (buildManager == null) buildManager = FindAnyObjectByType<BuildManager>();
            if (buildManager == null)
            {
                Debug.unityLogger.LogError(TAG, "No BuildManager in scene; indicator disabled.");
                return;
            }
            if (indicatorPrefab == null)
            {
                Debug.unityLogger.LogError(TAG, "No indicator prefab assigned; indicator disabled.");
                return;
            }

            _indicatorInstance = Instantiate(indicatorPrefab);
            _indicatorInstance.name = "ForwardIndicator";

            buildManager.ConstructChanged += UpdateIndicator;
            UpdateIndicator();

            Debug.unityLogger.Log(TAG, "Forward indicator initialised.");
        }

        void OnDestroy()
        {
            if (buildManager != null)
                buildManager.ConstructChanged -= UpdateIndicator;
        }

        void UpdateIndicator()
        {
            if (_indicatorInstance == null) return;

            Transform target = FindFrontmostCubeTransform();
            if (target == null)
            {
                _indicatorInstance.SetActive(false);
                return;
            }

            _indicatorInstance.SetActive(true);
            if (_indicatorInstance.transform.parent != target)
            {
                _indicatorInstance.transform.SetParent(target, worldPositionStays: false);
                _indicatorInstance.transform.localPosition = Vector3.zero;
                _indicatorInstance.transform.localScale = Vector3.one;
                Debug.unityLogger.Log(TAG, $"Indicator anchored to {target.name}.");
            }
            // Always realign the arrow to world +Z, regardless of whether its
            // parent cube was rotated at build time. The arrow is a UI marker
            // for ship-forward, not a child decoration, so it must point at
            // world +Z (= construct.forward in BuildScene where the construct
            // sits at identity).
            _indicatorInstance.transform.rotation = Quaternion.identity;
        }

        // The alpha cube is implicitly at z=0 with cell (0,0,0). A placed
        // cube only takes the indicator if its z is STRICTLY greater than 0,
        // and among multiple at the same z, the first encountered in
        // PlacedCubes (insertion order) wins — stable against tie-breakers
        // unless a swap is intentionally placed.
        Transform FindFrontmostCubeTransform()
        {
            int bestZ = 0;
            Vector3Int bestCell = Vector3Int.zero;

            for (int i = 0; i < GameData.PlacedCubes.Count; i++)
            {
                Vector3Int cell = GameData.PlacedCubes[i].Cell;
                if (cell.z > bestZ)
                {
                    bestZ = cell.z;
                    bestCell = cell;
                }
            }

            if (bestCell == Vector3Int.zero)
            {
                GameObject alpha = GameObject.FindGameObjectWithTag("AlphaCube");
                return alpha != null ? alpha.transform : null;
            }

            if (buildManager.TryGetSpawnedCube(bestCell, out GameObject placed) && placed != null)
                return placed.transform;
            return null;
        }
    }
}
