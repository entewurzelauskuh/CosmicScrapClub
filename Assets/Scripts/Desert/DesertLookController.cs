using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using CubeFly.Core;

namespace CubeFly.Desert
{
    // Applies the CelLookSettings preference to FlyScene at runtime: swaps the
    // camera's URP renderer (default PC_Renderer <-> Desert_Renderer, which
    // carries the screen-space outline) and fades the DesertVolumeProfile
    // volume in/out. Re-applies live when the SettingsMenu toggle flips the
    // preference. No effect outside FlyScene (this component only exists here).
    public class DesertLookController : MonoBehaviour
    {
        [Tooltip("Global Volume carrying DesertVolumeProfile (grade + bloom).")]
        [SerializeField] Volume desertVolume;
        [Tooltip("Camera to switch renderers on. Falls back to Camera.main if unset.")]
        [SerializeField] Camera targetCamera;
        [Tooltip("PC_RPAsset renderer index for the cel look (Desert_Renderer = 1).")]
        [SerializeField] int celRendererIndex = 1;
        [Tooltip("PC_RPAsset renderer index for the as-is look (PC_Renderer / default = 0).")]
        [SerializeField] int asIsRendererIndex = 0;

        const string TAG = "DesertLook";

        void OnEnable()  { CelLookSettings.OnChanged += Apply; }
        void OnDisable() { CelLookSettings.OnChanged -= Apply; }

        void Start() { Apply(); }

        void Apply()
        {
            bool cel = CelLookSettings.Enabled;
            Camera cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam != null)
                cam.GetUniversalAdditionalCameraData().SetRenderer(cel ? celRendererIndex : asIsRendererIndex);
            if (desertVolume != null)
                desertVolume.weight = cel ? 1f : 0f;
            Debug.unityLogger.Log(TAG, $"Applied cel look = {cel}.");
        }
    }
}
