using CubeFly.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CubeFly.MainMenu
{
    // Builds the Main Menu UI on Awake: title + three buttons (Hangar /
    // Settings / Exit). Lives in its own scene as the first thing the player
    // sees. The persistent UICanvas (corner button) hides itself when this
    // scene is active — see UIManager.OnSceneStateChanged.
    public class MainMenuController : MonoBehaviour
    {
        const string TAG = "MainMenu";
        // Hangar button now leads into the slot picker scene rather than
        // jumping straight into the build scene. The picker resolves the
        // active slot before BuildScene is loaded.
        const string HangarSelectSceneName = "HangarSelect";

        void Awake()
        {
            // Entering the main menu means no save slot is active. Disarm
            // autosave so a slot armed earlier this session can't be
            // overwritten by a later BuildScene autosave. This is the single
            // chokepoint for every return-to-menu path (PauseMenu "Menu",
            // GameOver "Return", HangarSelect "Cancel" all load MainMenu). (AP-1)
            GameData.DisarmAutosave();
            BuildUI();
            Debug.unityLogger.Log(TAG, "Main Menu initialised.");
        }

        void BuildUI()
        {
            UIStyle.EnsureEventSystem();
            // Sit above any persistent UICanvas (sortingOrder 100); the latter
            // should be hidden anyway, but this is defensive.
            Canvas canvas = UIStyle.BuildScreenSpaceCanvas("MainMenuCanvas", sortingOrder: 200);
            RectTransform root = (RectTransform)canvas.transform;

            // Warm brand background + the Cosmic Scrap Club wordmark.
            UIStyle.BuildBrandBackground(root);
            BuildWordmark(root);

            // Buttons stacked below the wordmark.
            CreateMenuButton(root, "Hangar",   new Vector2(0f, -40f),  OnHangar);
            CreateMenuButton(root, "Settings", new Vector2(0f, -140f), OnSettings);
            CreateMenuButton(root, "Exit",     new Vector2(0f, -240f), OnExit);
        }

        // The Cosmic Scrap Club wordmark: a slightly-tilted hazard-yellow plate
        // (ink border + toon shadow) with COSMIC / SCRAP / star-CLUB-star in the
        // three brand fonts. Built inline — it is MainMenu-only.
        static void BuildWordmark(RectTransform parent)
        {
            int uiLayer = LayerMask.NameToLayer("UI");
            GameObject plateGO = new GameObject("Wordmark",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            plateGO.transform.SetParent(parent, false);
            if (uiLayer >= 0) plateGO.layer = uiLayer;
            RectTransform prt = (RectTransform)plateGO.transform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(620f, 300f);
            prt.anchoredPosition = new Vector2(0f, 200f);
            prt.localEulerAngles = new Vector3(0f, 0f, 2f);   // ~ -2 deg visual tilt
            Image plate = plateGO.GetComponent<Image>();
            plate.color = CscPalette.HazardYellow;
            CscTheme.AddToonOutline(plateGO, 4f);
            CscTheme.AddToonShadow(plateGO, 8f);

            WordmarkLine(prt, "COSMIC", 40, CscTheme.CondOr, CscPalette.Scorch, 95f);
            WordmarkLine(prt, "SCRAP", 130, CscTheme.DisplayOr, CscPalette.Scorch, 0f);
            WordmarkLine(prt, "★ CLUB ★", 44, CscTheme.StencilOr, CscPalette.Orange600, -95f);
        }

        static void WordmarkLine(RectTransform plate, string text, int size,
            Font font, Color color, float y)
        {
            Text t = UIStyle.BuildLabel(plate, text, size, FontStyle.Normal, font);
            t.color = color;
            RectTransform rt = (RectTransform)t.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(600f, size + 20f);
            rt.anchoredPosition = new Vector2(0f, y);
        }

        static void CreateMenuButton(RectTransform parent, string text,
            Vector2 anchoredPos, UnityEngine.Events.UnityAction onClick)
        {
            (Button button, Text _) = UIStyle.BuildLabeledButton(
                parent, text, new Vector2(360f, 72f), fontSize: 32);
            RectTransform rt = (RectTransform)button.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            CscTheme.AddToonShadow(button.gameObject, 6f);
            button.onClick.AddListener(onClick);
        }

        void OnHangar()
        {
            Debug.unityLogger.Log(TAG, "Hangar selected — loading HangarSelect (slot picker).");
            SceneManager.LoadScene(HangarSelectSceneName);
        }

        void OnSettings()
        {
            if (SettingsMenu.Instance == null)
            {
                Debug.unityLogger.LogWarning(TAG, "Settings unavailable — SettingsMenu did not initialise.");
                return;
            }
            Debug.unityLogger.Log(TAG, "Settings selected — opening Settings menu.");
            SettingsMenu.Instance.Show();
        }

        void OnExit()
        {
            Debug.unityLogger.Log(TAG, "Exit selected — quitting.");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
