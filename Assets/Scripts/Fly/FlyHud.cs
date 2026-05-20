using CubeFly.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CubeFly.Fly
{
    // Scene-attached shared canvas for every FlyScene HUD element —
    // the crosshair, boost bar, weapon toolbar, speed and HP labels.
    //
    // Lifetime: scene-attached, NOT DDOL — destroyed on FlyScene unload,
    // recreated on the next FlyScene load. Same singleton pattern as
    // FlyController and BuildManager.
    //
    // [DefaultExecutionOrder(-500)] forces our Awake to run before the
    // Fly HUD scripts (which run at default order 0, or +100 for the
    // crosshair / HP indicator), so their BuildUI calls find
    // FlyHud.Instance.Root already populated.
    //
    // The scene file ships a bare GameObject with this script attached;
    // Awake adds the Canvas + GraphicRaycaster + CanvasScaler in code
    // (matching the project's build-UI-in-code pattern). This keeps the
    // scene YAML minimal — no scattered Inspector-tweaked component
    // values to drift out of sync with the code.
    //
    // sortingOrder 100 — sits below the persistent UI canvas (200) so
    // pause / game-over overlays visually cover the scene HUD.
    [DefaultExecutionOrder(-500)]
    public class FlyHud : MonoBehaviour
    {
        public static FlyHud Instance { get; private set; }
        public RectTransform Root => (RectTransform)transform;

        const string TAG = "FlyHud";

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
            Debug.unityLogger.Log(TAG, "FlyHud canvas ready.");
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
