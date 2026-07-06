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
            if (overrideAsset == null) return;
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
