using UnityEngine;
using UnityEngine.SceneManagement;

namespace CubeFly.Core
{
    public static class SceneSwitcher
    {
        public const string BuildSceneName = "BuildScene";
        public const string FlySceneName = "FlyScene";

        const string TAG = "SceneSwitcher";

        public static void Toggle()
        {
            string current = SceneManager.GetActiveScene().name;
            string next = current == BuildSceneName ? FlySceneName : BuildSceneName;

            // The construct in GameData is mutated in flight (cubes that
            // reach 0 HP are removed). Snapshot it on the way into
            // FlyScene and restore it on the way back, so the Hangar
            // re-enters BuildScene with the pre-flight construct rather
            // than the shot-down one. Toggle() is the sole Build<->Fly
            // transition, so this pairing is airtight.
            if (current == BuildSceneName)    GameData.CaptureFlightSnapshot();
            else if (current == FlySceneName) GameData.RestoreFlightSnapshot();

            Debug.unityLogger.Log(TAG, $"SceneSwitcher: transitioning from '{current}' to '{next}'");
            SceneManager.LoadScene(next);
        }
    }
}
