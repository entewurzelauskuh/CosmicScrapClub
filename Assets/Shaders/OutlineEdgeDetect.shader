Shader "Desert/OutlineEdgeDetect"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "OutlineEdgeDetect"
            // Render state must live inside the Pass: at SubShader scope it was
            // not applied and the fullscreen triangle was culled.
            ZWrite Off ZTest Always Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            TEXTURE2D(_BlitTexture);
            SAMPLER(sampler_BlitTexture);

            float4 _OutlineColor;
            float  _Thickness;
            float  _DepthThreshold;
            float  _NormalThreshold;

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = GetFullScreenTriangleVertexPosition(IN.vertexID);
                OUT.uv         = GetFullScreenTriangleTexCoord(IN.vertexID);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 uv    = IN.uv;
                float2 texel = (1.0 / _ScreenParams.xy) * _Thickness;

                // Roberts-cross sample offsets
                float2 uv0 = uv + float2(-1, -1) * texel;
                float2 uv1 = uv + float2( 1,  1) * texel;
                float2 uv2 = uv + float2(-1,  1) * texel;
                float2 uv3 = uv + float2( 1, -1) * texel;

                // depth edges - linearised so detection is consistent at any distance
                float d0 = Linear01Depth(SampleSceneDepth(uv0), _ZBufferParams);
                float d1 = Linear01Depth(SampleSceneDepth(uv1), _ZBufferParams);
                float d2 = Linear01Depth(SampleSceneDepth(uv2), _ZBufferParams);
                float d3 = Linear01Depth(SampleSceneDepth(uv3), _ZBufferParams);
                float depthEdge = sqrt((d0 - d1) * (d0 - d1) + (d2 - d3) * (d2 - d3));
                depthEdge = step(_DepthThreshold, depthEdge);

                // normal edges - SampleSceneNormals handles URP's normal packing
                float3 n0 = SampleSceneNormals(uv0);
                float3 n1 = SampleSceneNormals(uv1);
                float3 n2 = SampleSceneNormals(uv2);
                float3 n3 = SampleSceneNormals(uv3);
                float normalEdge = distance(n0, n1) + distance(n2, n3);
                normalEdge = step(_NormalThreshold, normalEdge);

                float edge = saturate(max(depthEdge, normalEdge));

                half4 sceneColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, uv);
                return lerp(sceneColor, _OutlineColor, edge * _OutlineColor.a);
            }
            ENDHLSL
        }
    }
}
