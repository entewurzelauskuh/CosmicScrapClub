using CubeFly.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CubeFly.Build
{
    // Scene-attached shared canvas for every BuildScene HUD element —
    // the build toolbar (shape buttons, material flyout, category
    // flyouts, Delete button, stat labels), the ship-class dropdown,
    // and any future BuildScene-only UI.
    //
    // Lifetime: scene-attached, NOT DDOL — destroyed on BuildScene
    // unload, recreated on the next BuildScene load.
    //
    // [DefaultExecutionOrder(-500)] forces Awake to run before the
    // Build HUD scripts (default order 0), so their BuildUI / BuildToolbar
    // calls find BuildHud.Instance.Root already populated.
    //
    // sortingOrder 100 — sits below the persistent UI canvas (200) so
    // pause overlays cover the build HUD. FlyHud uses the same 100 in
    // FlyScene; the two never coexist so they don't fight.
    [DefaultExecutionOrder(-500)]
    public class BuildHud : MonoBehaviour
    {
        public static BuildHud Instance { get; private set; }
        public RectTransform Root => (RectTransform)transform;

        const string TAG = "BuildHud";

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) gameObject.layer = uiLayer;

            // Adding Canvas to a GameObject with only Transform causes
            // Unity to auto-replace Transform with RectTransform.
            Canvas canvas = GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();

            CanvasScaler scaler = GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            UIStyle.EnsureEventSystem();
            Debug.unityLogger.Log(TAG, "BuildHud canvas ready.");
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
