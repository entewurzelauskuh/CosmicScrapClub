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

            // Title
            Text title = UIStyle.BuildLabel(root, "Cube Fly", fontSize: 96, style: FontStyle.Bold, font: CscTheme.DisplayOr);
            RectTransform trt = (RectTransform)title.transform;
            trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(800f, 160f);
            trt.anchoredPosition = new Vector2(0f, 220f);

            // Buttons stacked vertically.
            CreateMenuButton(root, "Hangar",   new Vector2(0f, 40f),    OnHangar);
            CreateMenuButton(root, "Settings", new Vector2(0f, -60f),   OnSettings);
            CreateMenuButton(root, "Exit",     new Vector2(0f, -160f),  OnExit);
        }

        static void CreateMenuButton(RectTransform parent, string text,
            Vector2 anchoredPos, UnityEngine.Events.UnityAction onClick)
        {
            (Button button, Text _) = UIStyle.BuildLabeledButton(
                parent, text, new Vector2(360f, 80f), fontSize: 36);
            RectTransform rt = (RectTransform)button.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
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
