using UnityEngine;
using UnityEngine.Rendering;

namespace CubeFly.Core
{
    // Makes a specific URP pipeline asset active while this component is enabled
    // (i.e. while its scene is loaded), restoring the previous one on disable.
    // FlyScene uses it to run the desert pipeline (Desert_RPAsset — long shadow
    // distance for the 500u basin) without changing the global default that the
    // Menu/Hangar/Build scenes render with.
    public class ScenePipelineOverride : MonoBehaviour
    {
        [Tooltip("Pipeline asset to make active while this scene is loaded (e.g. Desert_RPAsset).")]
        [SerializeField] RenderPipelineAsset overrideAsset;

        RenderPipelineAsset _previous;
        bool _applied;

        void OnEnable()
        {
            // `|| _applied` keeps OnEnable idempotent: a second enable without an
            // intervening OnDisable must not re-capture _previous as the override
            // itself (which would then "restore" the override into the next scene).
            if (overrideAsset == null || _applied) return;
            _previous = QualitySettings.renderPipeline;
            QualitySettings.renderPipeline = overrideAsset;
            _applied = true;
        }

        void OnDisable()
        {
            if (!_applied) return;
            QualitySettings.renderPipeline = _previous;
            _applied = false;
        }
    }
}
