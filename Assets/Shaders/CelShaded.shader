Shader "Desert/CelShaded"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.80, 0.60, 0.40, 1)
        _Bands ("Light Bands", Range(2, 5)) = 3
        _MinLight ("Min Light Floor", Range(0, 1)) = 0.3

        [Header(Two Tone Breakup)]
        _TwoToneColor ("Two-Tone Color", Color) = (0.70, 0.50, 0.32, 1)
        _TwoToneScale ("Two-Tone Noise Scale", Float) = 0.04
        _TwoToneStrength ("Two-Tone Strength", Range(0, 1)) = 0.0

        [Header(Strata Banding)]
        [Toggle(_STRATA_ON)] _StrataOn ("Enable Strata", Float) = 0
        _StrataColor ("Strata Color", Color) = (0.55, 0.32, 0.20, 1)
        _StrataScale ("Strata Frequency", Float) = 0.15
        _StrataStrength ("Strata Strength", Range(0, 1)) = 0.5
        _StrataWarp ("Strata Warp", Float) = 3.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local _STRATA_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _Bands;
                float  _MinLight;
                float4 _TwoToneColor;
                float  _TwoToneScale;
                float  _TwoToneStrength;
                float4 _StrataColor;
                float  _StrataScale;
                float  _StrataStrength;
                float  _StrataWarp;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float  fogCoord    : TEXCOORD2;
            };

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash21(i),               b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1)), d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = p.positionCS;
                OUT.positionWS  = p.positionWS;
                OUT.normalWS    = GetVertexNormalInputs(IN.normalOS).normalWS;
                OUT.fogCoord    = ComputeFogFactor(p.positionCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 n = normalize(IN.normalWS);

                // albedo with procedural two-tone breakup
                float3 albedo = _BaseColor.rgb;
                float tt = vnoise(IN.positionWS.xz * _TwoToneScale);
                albedo = lerp(albedo, _TwoToneColor.rgb, tt * _TwoToneStrength);

                // horizontal strata banding (rock only)
                #if defined(_STRATA_ON)
                    float warp  = vnoise(IN.positionWS.xz * _StrataScale * 0.5) * _StrataWarp;
                    float bands = frac((IN.positionWS.y + warp) * _StrataScale);
                    float strata = smoothstep(0.45, 0.55, bands);
                    albedo = lerp(albedo, _StrataColor.rgb, strata * _StrataStrength);
                #endif

                // cel lighting: half-lambert, quantised to bands
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float ndl   = dot(n, mainLight.direction) * 0.5 + 0.5;
                float steps = max(_Bands, 2.0);
                float banded = saturate(floor(ndl * steps) / (steps - 1.0));

                // combined light x shadow, clamped so full shadow is never black
                float lightTerm = max(banded * mainLight.shadowAttenuation, _MinLight);

                float3 color = albedo * mainLight.color.rgb * lightTerm;
                color = MixFog(color, IN.fogCoord);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; };

            V ShadowVert(A IN)
            {
                V OUT;
                float3 ws = TransformObjectToWorld(IN.positionOS.xyz);
                float3 ns = TransformObjectToWorldNormal(IN.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, ns, _LightDirection));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionCS = cs;
                return OUT;
            }
            half4 ShadowFrag(V IN) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex DNVert
            #pragma fragment DNFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            V DNVert(A IN)
            {
                V OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }
            half4 DNFrag(V IN) : SV_Target
            {
                return half4(normalize(IN.normalWS) * 0.5 + 0.5, 0);
            }
            ENDHLSL
        }
    }
}
