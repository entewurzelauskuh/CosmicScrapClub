using CubeFly.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CubeFly.Fly
{
    // Screen-space reticle that follows the construct's forward
    // direction (not the camera's). The third-person FlyCamera lags
    // behind the construct during fast turns; locking the reticle to
    // screen-centre would show camera forward — which is wrong,
    // because the weapons fire along construct.forward. So we project
    // (construct.position + construct.forward * aimRange) to screen
    // space each frame and place the reticle there. The same value
    // is what FlyShootingController passes to weapons as the aim
    // target, so on-screen reticle and actual aim agree.
    //
    // DefaultExecutionOrder(100) makes this run after FlyCamera's
    // LateUpdate (default order 0) so the camera's transform is
    // final by the time we project.
    [DefaultExecutionOrder(100)]
    public class FlyCrosshair : MonoBehaviour
    {
        [Header("Refs (auto-wired in Start if null)")]
        [SerializeField] FlyController flyController;
        [SerializeField] Camera flyCamera;

        [Header("Projection")]
        [Tooltip("World-space distance projected along construct.forward to derive the aim point. Keep this in sync with FlyShootingController.aimRange so the reticle and the actual shot agree.")]
        [SerializeField] float aimRange = 100f;

        [Header("Reticle visual")]
        [SerializeField] Color crosshairColor = Color.white;
        [SerializeField] Vector2 centerDotSize = new Vector2(4f, 4f);
        [Tooltip("x = arm thickness, y = arm length.")]
        [SerializeField] Vector2 armSize = new Vector2(2f, 12f);
        [Tooltip("Gap (px) between the center dot edge and the start of each arm.")]
        [SerializeField] float armGap = 2f;

        Canvas _canvas;
        RectTransform _root;

        const string TAG = "FlyCrosshair";

        void Awake()
        {
            BuildUI();
        }

        void Start()
        {
            if (flyCamera == null) flyCamera = Camera.main;
            if (flyController == null) flyController = FindAnyObjectByType<FlyController>();
            if (flyController == null)
                Debug.unityLogger.LogWarning(TAG, "No FlyController in scene; reticle will idle at screen center.");
        }

        void LateUpdate()
        {
            // Pause freezes the reticle in place — skip the projection
            // so the last computed position holds.
            if (PauseMenu.Instance != null && PauseMenu.Instance.IsOpen) return;

            if (_root == null) return;
            if (flyController == null || flyController.Construct == null) return;
            if (flyCamera == null) { flyCamera = Camera.main; if (flyCamera == null) return; }

            Transform construct = flyController.Construct;
            Vector3 aimPoint = construct.position + construct.forward * aimRange;
            Vector3 screenPos = flyCamera.WorldToScreenPoint(aimPoint);

            if (screenPos.z < 0f)
            {
                if (_root.gameObject.activeSelf) _root.gameObject.SetActive(false);
                return;
            }

            if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
            _root.position = screenPos;
        }

        // ---------- UI ----------

        void BuildUI()
        {
            UIStyle.EnsureEventSystem();
            _canvas = UIStyle.BuildScreenSpaceCanvas("FlyCrosshairCanvas", sortingOrder: 110);
            RectTransform canvasRoot = (RectTransform)_canvas.transform;

            // Root rect — pivot at center so the "+" reticle is
            // visually symmetric around the projected aim point. On
            // a ScreenSpaceOverlay canvas, RectTransform.position
            // accepts a pixel-space screen position regardless of
            // anchor, so LateUpdate's `_root.position = screenPos`
            // works correctly with these center-pivot anchors.
            GameObject rootGO = new GameObject("CrosshairRoot",
                typeof(RectTransform));
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) rootGO.layer = uiLayer;
            rootGO.transform.SetParent(canvasRoot, false);
            _root = (RectTransform)rootGO.transform;
            _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0.5f, 0.5f);
            _root.sizeDelta = new Vector2(48f, 48f);

            // Center dot.
            BuildRect(_root, "CenterDot", centerDotSize, Vector2.zero);

            // Four arms — armSize.x = thickness, armSize.y = length.
            float armLength = armSize.y;
            float armThickness = armSize.x;
            float halfCenter = centerDotSize.y / 2f;
            float armOffset = halfCenter + armGap + armLength / 2f;

            BuildRect(_root, "ArmTop",    new Vector2(armThickness, armLength), new Vector2(0,  armOffset));
            BuildRect(_root, "ArmBottom", new Vector2(armThickness, armLength), new Vector2(0, -armOffset));
            BuildRect(_root, "ArmLeft",   new Vector2(armLength, armThickness), new Vector2(-armOffset, 0));
            BuildRect(_root, "ArmRight",  new Vector2(armLength, armThickness), new Vector2( armOffset, 0));
        }

        Image BuildRect(RectTransform parent, string name, Vector2 size, Vector2 anchoredPos)
        {
            GameObject go = new GameObject(name,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rt = (RectTransform)go.transform;
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) go.layer = uiLayer;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
            Image img = go.GetComponent<Image>();
            img.color = crosshairColor;
            img.raycastTarget = false;
            return img;
        }
    }
}
