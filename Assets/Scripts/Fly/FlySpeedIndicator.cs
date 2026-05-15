using CubeFly.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CubeFly.Fly
{
    // Bottom-left HUD label showing the construct's current world-space
    // speed in u/s. Reads `Rigidbody.linearVelocity.magnitude` from the
    // construct each Update. Mirrors the BuildScene's `Mass: X / 100`
    // and `HP: Y` readouts in placement and visual style.
    //
    // Lives on FlyHUD alongside FlyCrosshair / FlyWeaponToolbarController.
    // Builds its own screen-space-overlay Canvas + UI.Text label so it
    // doesn't depend on any shared HUD canvas.
    public class FlySpeedIndicator : MonoBehaviour
    {
        [SerializeField] FlyController flyController;

        [Header("Layout")]
        [Tooltip("Anchored position relative to the bottom-left corner.")]
        [SerializeField] Vector2 anchoredPosition = new Vector2(20f, 20f);
        [SerializeField] Vector2 size = new Vector2(280f, 40f);
        [SerializeField] int fontSize = 22;

        Canvas _canvas;
        Text _label;

        // Cached at Start. Null-tolerant: if the construct has no
        // Rigidbody for some reason (test setup, future scene variant),
        // the readout just stays at 0.0.
        Rigidbody _constructRb;

        const string TAG = "FlySpeed";

        void Awake()
        {
            BuildUI();
        }

        void Start()
        {
            if (flyController == null) flyController = FindAnyObjectByType<FlyController>();
            if (flyController == null)
            {
                Debug.unityLogger.LogWarning(TAG, "No FlyController in scene; speed indicator will read 0.");
                return;
            }
            Transform c = flyController.Construct;
            if (c != null) _constructRb = c.GetComponent<Rigidbody>();
            if (_constructRb == null)
            {
                Debug.unityLogger.LogWarning(TAG, "Construct has no Rigidbody; speed indicator will read 0.");
            }
        }

        void Update()
        {
            if (_label == null) return;
            float speed = _constructRb != null ? _constructRb.linearVelocity.magnitude : 0f;
            _label.text = $"Speed: {speed:F1} u/s";
        }

        void BuildUI()
        {
            UIStyle.EnsureEventSystem();
            // sortingOrder 130 sits between FlyWeaponToolbar (120) and
            // FlyCrosshair (110). Doesn't matter much because the label
            // sits in a corner with no other UI overlap.
            _canvas = UIStyle.BuildScreenSpaceCanvas("FlySpeedCanvas", sortingOrder: 130);
            RectTransform canvasRoot = (RectTransform)_canvas.transform;

            _label = UIStyle.BuildLabel(canvasRoot, "Speed: 0.0 u/s", fontSize: fontSize);
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
