using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CubeFly.Core
{
    // DDOL singleton that applies VfxSettings to the active scene's
    // URP Volume profile. Same shape as PauseMenu / GameOverMenu /
    // SettingsMenu: BeforeSceneLoad self-bootstrap, Instance property,
    // DontDestroyOnLoad.
    //
    // Behaviour:
    //   • On Awake: subscribe to SceneManager.sceneLoaded and
    //     VfxSettings.Changed, then Apply() once (handles the initial
    //     scene if it loaded before our Awake).
    //   • On SceneManager.sceneLoaded: Apply() to the newly-loaded
    //     scene's Volume.
    //   • On VfxSettings.Changed: Apply() to the active scene's
    //     Volume (real-time A/B comparison).
    //
    // Apply() is idempotent and profile-agnostic. It probes for each
    // of the five Phase-A overrides via VolumeProfile.TryGet — missing
    // overrides are silently skipped, so scenes without a Volume (or
    // with a profile that doesn't have these overrides) don't throw.
    //
    // Execution order is -1500 — between SettingsMenu (-2000) and
    // PauseMenu (-1000); keeps the persistent-UI tier ordering
    // consistent.
    [DefaultExecutionOrder(-1500)]
    public class VfxApplier : MonoBehaviour
    {
        public static VfxApplier Instance { get; private set; }

        const string TAG = "VfxApplier";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            GameObject go = new GameObject("VfxApplier");
            go.AddComponent<VfxApplier>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            SceneManager.sceneLoaded += OnSceneLoaded;
            VfxSettings.Changed += Apply;

            Apply();

            Debug.unityLogger.Log(TAG, "VFX applier ready.");
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                VfxSettings.Changed -= Apply;
                Instance = null;
            }
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Apply();

        void Apply()
        {
            Volume volume = FindFirstObjectByType<Volume>();
            if (volume == null || volume.profile == null) return;
            VolumeProfile profile = volume.profile;

            if (profile.TryGet<Bloom>(out var bloom))
                bloom.active = VfxSettings.Bloom;
            if (profile.TryGet<Vignette>(out var vignette))
                vignette.active = VfxSettings.Vignette;
            if (profile.TryGet<Tonemapping>(out var tonemapping))
                tonemapping.active = VfxSettings.Tonemapping;
            if (profile.TryGet<ColorAdjustments>(out var color))
                color.active = VfxSettings.ColorAdjustments;
            if (profile.TryGet<ChromaticAberration>(out var ca))
                ca.active = VfxSettings.ChromaticAberration;
        }
    }
}
