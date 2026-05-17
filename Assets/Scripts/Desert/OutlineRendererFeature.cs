using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

namespace CubeFly.Desert
{
    /// <summary>
    /// Screen-space depth + normal edge-detection outline for the desert level.
    /// Follows URP's FullScreenPassRendererFeature input-pass pattern, specialised
    /// for the outline edge-detect material with depth + normal requirements.
    /// </summary>
    public class OutlineRendererFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            public Shader edgeDetectShader;
            [ColorUsage(true, true)] public Color outlineColor = Color.black;
            [Range(0.5f, 4f)] public float thickness = 1f;
            [Range(0f, 5f)] public float depthThreshold = 0.5f;
            [Range(0f, 2f)] public float normalThreshold = 0.4f;
            public RenderPassEvent injectionPoint = RenderPassEvent.BeforeRenderingPostProcessing;
        }

        public Settings settings = new Settings();

        Material _material;
        OutlinePass _pass;

        public override void Create()
        {
            // URP re-runs Create() on every renderer rebuild / domain reload.
            // Free the previous material first so it is not leaked.
            if (_material != null)
            {
                CoreUtils.Destroy(_material);
                _material = null;
            }
            if (settings.edgeDetectShader != null)
                _material = CoreUtils.CreateEngineMaterial(settings.edgeDetectShader);

            _pass = new OutlinePass(_material, settings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_material == null)
                return;

            var cameraType = renderingData.cameraData.cameraType;
            if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
                return;

            _pass.renderPassEvent = settings.injectionPoint;
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_material);
        }

        class OutlinePass : ScriptableRenderPass
        {
            readonly Material _mat;
            readonly Settings _s;
            static readonly MaterialPropertyBlock s_Mpb = new MaterialPropertyBlock();

            static readonly int BlitTextureId     = Shader.PropertyToID("_BlitTexture");
            static readonly int BlitScaleBiasId   = Shader.PropertyToID("_BlitScaleBias");
            static readonly int OutlineColorId    = Shader.PropertyToID("_OutlineColor");
            static readonly int ThicknessId       = Shader.PropertyToID("_Thickness");
            static readonly int DepthThresholdId  = Shader.PropertyToID("_DepthThreshold");
            static readonly int NormalThresholdId = Shader.PropertyToID("_NormalThreshold");

            class PassData
            {
                public Material material;
                public TextureHandle source;
            }

            public OutlinePass(Material mat, Settings s)
            {
                _mat = mat;
                _s = s;
                profilingSampler = new ProfilingSampler("Desert Outline");
                ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_mat == null)
                    return;

                var resourceData = frameData.Get<UniversalResourceData>();

                _mat.SetColor(OutlineColorId, _s.outlineColor);
                _mat.SetFloat(ThicknessId, _s.thickness);
                _mat.SetFloat(DepthThresholdId, _s.depthThreshold);
                _mat.SetFloat(NormalThresholdId, _s.normalThreshold);

                TextureHandle activeColor = resourceData.activeColorTexture;

                // Copy the active colour so the edge-detect pass can sample the scene
                // while writing its result back to the same target.
                TextureDesc copyDesc = renderGraph.GetTextureDesc(activeColor);
                copyDesc.name = "_OutlineSourceCopy";
                copyDesc.clearBuffer = false;
                TextureHandle sourceCopy = renderGraph.CreateTexture(copyDesc);
                renderGraph.AddBlitPass(activeColor, sourceCopy, Vector2.one, Vector2.zero,
                    passName: "Outline Source Copy");

                using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                    "Desert Outline", out var passData, profilingSampler))
                {
                    passData.material = _mat;
                    passData.source = sourceCopy;

                    builder.UseTexture(sourceCopy, AccessFlags.Read);
                    if (resourceData.cameraDepthTexture.IsValid())
                        builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                    if (resourceData.cameraNormalsTexture.IsValid())
                        builder.UseTexture(resourceData.cameraNormalsTexture, AccessFlags.Read);
                    builder.UseAllGlobalTextures(true);

                    builder.SetRenderAttachment(activeColor, 0, AccessFlags.Write);

                    builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                    {
                        s_Mpb.Clear();
                        s_Mpb.SetTexture(BlitTextureId, data.source);
                        s_Mpb.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0,
                            MeshTopology.Triangles, 3, 1, s_Mpb);
                    });
                }
            }
        }
    }
}
