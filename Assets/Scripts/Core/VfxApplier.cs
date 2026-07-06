using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CubeFly.Core
{
    // DDOL singleton that applies VfxSettings to the URP volume profile
    // affecting the active scene. Same shape as PauseMenu / GameOverMenu /
    // SettingsMenu: BeforeSceneLoad self-bootstrap, Instance property,
    // DontDestroyOnLoad.
    //
    // Behaviour:
    //   • On Awake: subscribe to SceneManager.sceneLoaded and
    //     VfxSettings.Changed, then Apply() once (handles the initial
    //     scene if it loaded before our Awake).
    //   • On SceneManager.sceneLoaded: Apply() to the newly-loaded
    //     scene.
    //   • On VfxSettings.Changed: Apply() (real-time A/B comparison).
    //
    // Apply() resolves the profile in two steps:
    //   1. A scene-attached Volume GameObject (FlyScene's DesertLook
    //      volume uses this pattern).
    //   2. URP's global default profile, accessed via
    //      GraphicsSettings.GetRenderPipelineSettings<URPDefaultVolumeProfileSettings>().
    //      MainMenu / HangarSelect / BuildScene have NO scene Volume — they
    //      inherit URP's default profile via UniversalRenderPipelineGlobalSettings
    //      (FlyScene now carries the desert Volume).
    //
    // Apply() is idempotent and profile-agnostic. It probes for each
    // of the five Phase-A overrides via VolumeProfile.TryGet — missing
    // overrides are silently skipped, so scenes / profiles that don't
    // have these overrides don't throw.
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
            VolumeProfile profile = ResolveActiveProfile();
            if (profile == null) return;

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

        // Two-step resolution: scene Volume first (FlyScene's DesertLook
        // volume has its own), then URP's global default profile (used by
        // MainMenu / HangarSelect / BuildScene which have no scene Volume).
        static VolumeProfile ResolveActiveProfile()
        {
            // Prefer the global Volume with the highest priority (the scene's
            // "active" profile). FindFirstObjectByType returns an arbitrary
            // instance when a scene has several Volumes (e.g. a global + a
            // local trigger), which could apply the toggles to the wrong
            // profile. Pick global over local, then highest priority; fall
            // back to URP's default below if no usable scene Volume. (AP-11)
            Volume[] volumes = FindObjectsByType<Volume>(FindObjectsSortMode.None);
            Volume best = null;
            for (int i = 0; i < volumes.Length; i++)
            {
                Volume v = volumes[i];
                // Skip disabled volumes — a disabled global/high-priority
                // Volume must not win selection and receive the toggles. (AP-11, PR review)
                if (v == null || !v.isActiveAndEnabled || v.profile == null) continue;
                if (best == null
                    || (v.isGlobal && !best.isGlobal)
                    || (v.isGlobal == best.isGlobal && v.priority > best.priority))
                    best = v;
            }
            if (best != null && best.profile != null)
                return best.profile;

            URPDefaultVolumeProfileSettings settings =
                GraphicsSettings.GetRenderPipelineSettings<URPDefaultVolumeProfileSettings>();
            return settings != null ? settings.volumeProfile : null;
        }
    }
}
