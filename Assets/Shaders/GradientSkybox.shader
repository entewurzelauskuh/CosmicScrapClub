Shader "Desert/GradientSkybox"
{
    Properties
    {
        [HDR] _SkyColor      ("Sky Color (zenith)", Color)   = (0.42, 0.55, 0.72, 1)
        [HDR] _HorizonColor  ("Horizon Color", Color)        = (0.93, 0.82, 0.62, 1)
        [HDR] _GroundColor   ("Ground Color (nadir)", Color) = (0.52, 0.40, 0.30, 1)
        _SkyExponent     ("Sky Falloff", Range(0.2, 6))         = 1.4
        _HorizonExponent ("Horizon Falloff", Range(0.2, 6))     = 3.0
        [HDR] _SunColor  ("Sun Color", Color)                   = (1.9, 1.7, 1.3, 1)
        _SunSize         ("Sun Size", Range(0.005, 0.2))        = 0.045
        _SunHaloPower    ("Sun Halo Tightness", Range(4, 256))  = 48
        _SunHaloStrength ("Sun Halo Strength", Range(0, 1))     = 0.25
    }
    SubShader
    {
        Tags { "RenderType"="Background" "Queue"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        // Skybox render state: never cull, never write depth.
        Cull Off ZWrite Off

        Pass
        {
            Name "DesertGradientSky"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 dirOS : TEXCOORD0; };

            float4 _SkyColor;
            float4 _HorizonColor;
            float4 _GroundColor;
            float  _SkyExponent;
            float  _HorizonExponent;
            float4 _SunColor;
            float  _SunSize;
            float  _SunHaloPower;
            float  _SunHaloStrength;

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                // The skybox mesh's object-space vertex positions are the view directions.
                OUT.dirOS = IN.positionOS.xyz;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 dir = normalize(IN.dirOS);
                float  up  = dir.y;

                // Vertical gradient: sky above, ground below, both blending out of the horizon haze.
                float3 sky    = lerp(_HorizonColor.rgb, _SkyColor.rgb,    pow(saturate( up), _SkyExponent));
                float3 ground = lerp(_HorizonColor.rgb, _GroundColor.rgb, pow(saturate(-up), _HorizonExponent));
                float3 col    = (up >= 0.0) ? sky : ground;

                // Sun disc + soft halo, aligned to URP's main directional light.
                // _MainLightPosition.xyz is the direction toward the light (declared by URP's Core.hlsl includes).
                float3 sunDir = normalize(_MainLightPosition.xyz);
                float  d      = saturate(dot(dir, sunDir));
                float  disc   = smoothstep(1.0 - _SunSize, 1.0 - _SunSize * 0.5, d);
                float  halo   = pow(d, _SunHaloPower) * _SunHaloStrength;
                col += _SunColor.rgb * saturate(disc + halo);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
