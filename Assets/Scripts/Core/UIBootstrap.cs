using UnityEngine;

namespace CubeFly.Core
{
    // Spawns the persistent UIManager prefab if one does not already exist.
    // Both BuildScene and FlyScene host an instance of this so either can be
    // entered directly via Play.
    public class UIBootstrap : MonoBehaviour
    {
        [SerializeField] GameObject uiManagerPrefab;

        const string TAG = "UIBootstrap";

        void Awake()
        {
            if (UIManager.Instance != null)
            {
                Debug.unityLogger.Log(TAG, "UIBootstrap: UIManager already present — skipping instantiation.");
                return;
            }
            if (uiManagerPrefab == null) return;
            Debug.unityLogger.Log(TAG, "UIBootstrap: UIManager not found — instantiating UICanvas prefab.");
            Instantiate(uiManagerPrefab);
        }
    }
}
