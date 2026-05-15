using CubeFly.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CubeFly.Fly
{
    // Bottom-left HUD label showing the construct's current vs initial
    // total HP. Sums `CubeStats.healthPoints` across every cube child of
    // the construct each Update; the initial total snapshotted at Start
    // is what "max" means. As cubes take damage the current value drops
    // smoothly; as cubes die (CubeDeath detaches them from the
    // construct), they fall out of the sum, so destruction shows as a
    // step down rather than a slow bleed.
    //
    // Lives on FlyHUD alongside FlyCrosshair / FlyWeaponToolbarController /
    // FlySpeedIndicator. Anchored bottom-left, positioned above
    // FlySpeedIndicator so the two stack vertically.
    //
    // DefaultExecutionOrder(100) forces our Start to run after
    // FlyController's (default order 0). FlyController.Start calls
    // BuildConstruct synchronously to spawn the cube children of the
    // construct; we need those cubes to exist before snapshotting the
    // initial HP total, otherwise _maxHp would lock to 0.
    [DefaultExecutionOrder(100)]
    public class FlyHpIndicator : MonoBehaviour
    {
        [SerializeField] FlyController flyController;

        [Header("Layout")]
        [Tooltip("Anchored position relative to the bottom-left corner. Place above FlySpeedIndicator (which sits at (20, 20)).")]
        [SerializeField] Vector2 anchoredPosition = new Vector2(20f, 60f);
        [SerializeField] Vector2 size = new Vector2(280f, 40f);
        [SerializeField] int fontSize = 22;

        Canvas _canvas;
        Text _label;
        Transform _construct;
        float _maxHp;

        const string TAG = "FlyHp";

        void Awake()
        {
            BuildUI();
        }

        void Start()
        {
            if (flyController == null) flyController = FindAnyObjectByType<FlyController>();
            if (flyController == null)
            {
                Debug.unityLogger.LogWarning(TAG, "No FlyController in scene; HP indicator will read 0.");
                return;
            }
            _construct = flyController.Construct;
            // Snapshot the initial total HP as "max" once the construct
            // is fully built. The DefaultExecutionOrder(100) attribute
            // on the class ensures FlyController.Start (which calls
            // BuildConstruct to spawn the cubes) runs before this.
            _maxHp = SumCubeHp();
            Debug.unityLogger.Log(TAG, $"Initial HP: {_maxHp:F0}.");
        }

        void Update()
        {
            if (_label == null || _construct == null) return;
            float current = SumCubeHp();
            _label.text = $"HP: {current:F0} / {_maxHp:F0}";
        }

        // Direct child iteration only. Cubes that have already died and
        // are mid-CubeDeath drift have SetParent(null)'d themselves, so
        // they're naturally excluded from the sum — destruction reads
        // as a step-down in the readout rather than a slow bleed.
        float SumCubeHp()
        {
            float total = 0f;
            int n = _construct.childCount;
            for (int i = 0; i < n; i++)
            {
                CubeStats stats = _construct.GetChild(i).GetComponent<CubeStats>();
                if (stats != null) total += stats.healthPoints;
            }
            return total;
        }

        void BuildUI()
        {
            UIStyle.EnsureEventSystem();
            _canvas = UIStyle.BuildScreenSpaceCanvas("FlyHpCanvas", sortingOrder: 130);
            RectTransform canvasRoot = (RectTransform)_canvas.transform;

            _label = UIStyle.BuildLabel(canvasRoot, "HP: -- / --", fontSize: fontSize);
            _label.alignment = TextAnchor.MiddleLeft;
            RectTransform rt = (RectTransform)_label.transform;
            // Anchor and pivot bottom-left so anchoredPosition reads as
            // an offset from the screen's bottom-left corner.
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPosition;
        }
    }
}
