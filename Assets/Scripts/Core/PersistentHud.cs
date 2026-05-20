using UnityEngine;
using UnityEngine.UI;

namespace CubeFly.Core
{
    // Shared screen-space-overlay canvas for every persistent UI element
    // — the corner Fly!/Hangar button (UIManager), the ESC pause menu
    // (PauseMenu), and the Construct Destroyed overlay (GameOverMenu).
    //
    // Lifetime: DDOL singleton, lazy-created on first `Instance` access.
    // The three persistent UI scripts each call `PersistentHud.Instance.Root`
    // in their Awake/BuildUI to parent their UI tree; whichever Awake
    // runs first triggers Create(). No [RuntimeInitializeOnLoadMethod]
    // is needed because those three already self-bootstrap before scene
    // load and pull this canvas into existence as a side effect.
    //
    // Sibling order inside this canvas (later siblings render on top):
    //   corner button (UIManager)     → built first
    //   pause panel (PauseMenu)       → built second; full-screen dim
    //                                   covers the corner button when
    //                                   active.
    //   game-over panel (GameOverMenu)→ built third; covers PauseMenu
    //                                   when triggered.
    //
    // sortingOrder 200 — sits above the scene HUDs (FlyHud / BuildHud
    // at 100) so the pause / game-over dim panels visually overlay the
    // gameplay HUD.
    public class PersistentHud : MonoBehaviour
    {
        static PersistentHud _instance;
        public static PersistentHud Instance => _instance != null ? _instance : Create();

        public RectTransform Root => (RectTransform)transform;

        const string TAG = "PersistentHud";

        static PersistentHud Create()
        {
            GameObject go = new GameObject(
                "PersistentHud",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) go.layer = uiLayer;

            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _instance = go.AddComponent<PersistentHud>();
            DontDestroyOnLoad(go);
            UIStyle.EnsureEventSystem();
            Debug.unityLogger.Log(TAG, "PersistentHud canvas created.");
            return _instance;
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
