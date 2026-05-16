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

            TEXTURE2D(_BlitTexture);          SAMPLER(sampler_BlitTexture);
            TEXTURE2D(_CameraNormalsTexture); SAMPLER(sampler_CameraNormalsTexture);

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

            float3 SampleNormal(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_CameraNormalsTexture, sampler_CameraNormalsTexture, uv).rgb;
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

                // depth edges
                float d0 = SampleSceneDepth(uv0), d1 = SampleSceneDepth(uv1);
                float d2 = SampleSceneDepth(uv2), d3 = SampleSceneDepth(uv3);
                float depthEdge = sqrt((d0 - d1) * (d0 - d1) + (d2 - d3) * (d2 - d3)) * 100.0;
                depthEdge = step(_DepthThreshold, depthEdge);

                // normal edges
                float3 n0 = SampleNormal(uv0), n1 = SampleNormal(uv1);
                float3 n2 = SampleNormal(uv2), n3 = SampleNormal(uv3);
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
