# Desert Level — Checkpoint C1 (Style) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build checkpoint C1 of the desert demonstrator — a cel-shaded, ink-outlined mesa-and-arch hero formation in a new standalone scene — proving the Valley-of-Fire visual style end to end.

**Architecture:** A standalone URP scene (`DesertSandbox.unity`) detached from the game. One hand-written URP cel shader (banded lighting + min-light shadow floor + procedural strata/two-tone) covers every surface; a screen-space depth+normal edge-detection `ScriptableRendererFeature`, mounted on a dedicated `Desert_Renderer`, draws ink outlines. The C1 geometry is a hand-built ProBuilder mesa-and-arch formation on a small ground patch.

**Tech Stack:** Unity 6.3 (6000.3.11f1), URP 17.3 (RenderGraph API), ProBuilder 6.0.9, hand-written HLSL shaders, C# `ScriptableRendererFeature`.

---

## Scope

This plan covers **Checkpoint C1 only**. Checkpoints C2 (layout) and C3 (dressing)
get their own plans when reached — the spec (`desert_level_spec.md`) holds the
full three-checkpoint design, and each checkpoint's editor review informs the
next. Work happens on the existing `explore/desert-level` branch.

## Verification approach

This is Unity editor / asset work — there is no meaningful unit-test surface for
shaders, render features or hand-modelled geometry. Verification is therefore:

1. **Compile checks** — after creating any shader or C# file, refresh Unity and
   read the console; expect zero errors/shader-compile errors.
2. **Editor checks** — confirm assets exist, render, and wire up as described.
3. **Checkpoint review** — Task 8 is the user's editor review against the C1
   criteria. That is the acceptance gate for the whole checkpoint.

Each task ends with a commit. Commit messages follow the repo style (descriptive,
capitalised, no `feat:` prefix).

## File structure

| File | Responsibility |
|---|---|
| `Assets/Scenes/DesertSandbox.unity` | The standalone demonstrator scene |
| `Assets/Shaders/CelShaded.shader` | The one cel shader — banded light, min-light floor, strata, two-tone; has the `DepthNormals` pass the outline feature needs |
| `Assets/Shaders/OutlineEdgeDetect.shader` | Fullscreen edge-detection shader (depth + normals → ink) |
| `Assets/Scripts/Desert/OutlineRendererFeature.cs` | URP RenderGraph render feature that drives the edge-detect blit |
| `Assets/Settings/Desert_Renderer.asset` | Dedicated URP renderer carrying the outline feature |
| `Assets/Materials/Desert/{Sand,RedSandstone,Limestone,OxidizedRock}.mat` | The four-colour palette, all instances of `CelShaded` |

Modified (additive only): `Assets/Settings/PC_RPAsset.asset` — append `Desert_Renderer` to its renderer list.

---

## Task 1: Scene scaffold

**Files:**
- Create: `Assets/Scenes/DesertSandbox.unity`

- [ ] **Step 1: Create the scene**

In Unity (via Unity MCP `manage_scene`, or manually), create a new scene at
`Assets/Scenes/DesertSandbox.unity` containing only:
- A `Camera` named `Main Camera`, tag `MainCamera`, at position `(0, 12, -45)`,
  rotation `(12, 0, 0)`, field of view `60`. Background type: Skybox.
- A `Directional Light` named `Sun`, rotation `(50, -30, 0)`, color warm white
  `#FFF4E0`, intensity `1.2`, shadows: Soft.

Do **not** add the scene to Build Settings — it must stay out of the game's
scene flow.

- [ ] **Step 2: Verify**

Open `DesertSandbox.unity`. Confirm the camera and `Sun` exist and the Unity
console is clear of errors. Confirm the scene is absent from
`File > Build Settings`.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Scenes/DesertSandbox.unity" "Assets/Scenes/DesertSandbox.unity.meta"
git commit -m "Add DesertSandbox scene scaffold"
```

---

## Task 2: Cel shader

**Files:**
- Create: `Assets/Shaders/CelShaded.shader` (new `Assets/Shaders/` folder)

- [ ] **Step 1: Create the shader**

Create `Assets/Shaders/CelShaded.shader` with exactly this content:

```hlsl
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
```

- [ ] **Step 2: Verify it compiles**

Refresh Unity (`AssetDatabase.Refresh` / Unity MCP `refresh_unity`) and read the
console. Expected: no shader-compilation errors for `Desert/CelShaded`. If any
appear, fix the HLSL and refresh again before continuing.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Shaders/CelShaded.shader" "Assets/Shaders/CelShaded.shader.meta" "Assets/Shaders.meta"
git commit -m "Add CelShaded URP shader for the desert level"
```

---

## Task 3: Outline edge-detect shader

**Files:**
- Create: `Assets/Shaders/OutlineEdgeDetect.shader`

- [ ] **Step 1: Create the shader**

Create `Assets/Shaders/OutlineEdgeDetect.shader` with exactly this content:

```hlsl
Shader "Desert/OutlineEdgeDetect"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off

        Pass
        {
            Name "OutlineEdgeDetect"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_CameraNormalsTexture);
            SAMPLER(sampler_CameraNormalsTexture);

            float4 _OutlineColor;
            float  _Thickness;
            float  _DepthThreshold;
            float  _NormalThreshold;
            float4 _BlitTexture_TexelSize;

            float3 SampleNormal(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_CameraNormalsTexture, sampler_CameraNormalsTexture, uv).rgb;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 uv    = IN.texcoord;
                float2 texel = _BlitTexture_TexelSize.xy * _Thickness;

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

                half4 sceneColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);
                return lerp(sceneColor, _OutlineColor, edge * _OutlineColor.a);
            }
            ENDHLSL
        }
    }
}
```

Note: `Blit.hlsl` provides the `Vert` fullscreen vertex shader, the `Varyings`
struct (with `.texcoord`), `_BlitTexture`, and `sampler_LinearClamp`.

- [ ] **Step 2: Verify it compiles**

Refresh Unity and read the console. Expected: no shader-compilation errors for
`Desert/OutlineEdgeDetect`. If the `Blit.hlsl` symbols differ in this URP build,
correct against `Library/PackageCache/com.unity.render-pipelines.universal@*/ShaderLibrary/Blit.hlsl`.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Shaders/OutlineEdgeDetect.shader" "Assets/Shaders/OutlineEdgeDetect.shader.meta"
git commit -m "Add outline edge-detect shader"
```

---

## Task 4: Outline render feature

**Files:**
- Create: `Assets/Scripts/Desert/OutlineRendererFeature.cs` (new `Assets/Scripts/Desert/` folder)
- Create: `Assets/Scripts/Desert/OutlineRendererFeature.cs.meta`

- [ ] **Step 1: Create the render feature**

Create `Assets/Scripts/Desert/OutlineRendererFeature.cs` with exactly this content:

```csharp
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

/// <summary>
/// Screen-space depth + normal edge-detection outline for the desert level.
/// Blits the camera colour through the edge-detect material before post.
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
        if (settings.edgeDetectShader != null)
            _material = CoreUtils.CreateEngineMaterial(settings.edgeDetectShader);

        _pass = new OutlinePass(_material, settings)
        {
            renderPassEvent = settings.injectionPoint
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_material == null) return;
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

        static readonly int OutlineColorId   = Shader.PropertyToID("_OutlineColor");
        static readonly int ThicknessId      = Shader.PropertyToID("_Thickness");
        static readonly int DepthThreshId    = Shader.PropertyToID("_DepthThreshold");
        static readonly int NormalThreshId   = Shader.PropertyToID("_NormalThreshold");

        public OutlinePass(Material mat, Settings s)
        {
            _mat = mat;
            _s = s;
            // ask URP to make depth + normals available this frame
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_mat == null) return;

            var resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer) return;

            _mat.SetColor(OutlineColorId, _s.outlineColor);
            _mat.SetFloat(ThicknessId, _s.thickness);
            _mat.SetFloat(DepthThreshId, _s.depthThreshold);
            _mat.SetFloat(NormalThreshId, _s.normalThreshold);

            TextureHandle source = resourceData.activeColorTexture;

            TextureDesc desc = renderGraph.GetTextureDesc(source);
            desc.name = "_OutlineTemp";
            desc.clearBuffer = false;
            desc.depthBufferBits = DepthBits.None;
            TextureHandle temp = renderGraph.CreateTexture(desc);

            // source -> temp through the edge-detect material
            var blitParams = new RenderGraphUtils.BlitMaterialParameters(source, temp, _mat, 0);
            renderGraph.AddBlitPass(blitParams, "Outline EdgeDetect");

            // temp -> source (copy result back onto the camera colour)
            renderGraph.AddBlitPass(temp, source, Vector2.one, Vector2.zero, "Outline Copy Back");
        }
    }
}
```

> **URP 17.3 API note:** RenderGraph evolved across URP 14→17. Before relying on
> this verbatim, the executor must compile-verify it against the installed URP
> 17.3 package. The likely-to-shift symbols: `RenderGraphUtils.BlitMaterialParameters`,
> the `renderGraph.AddBlitPass` overloads, `renderGraph.GetTextureDesc`, and the
> `UnityEngine.Rendering.RenderGraphModule[.Util]` namespaces. If a symbol is
> missing, consult the URP 17.3 docs / a stock URP fullscreen-pass sample and
> adjust — the pass logic (ConfigureInput depth+normal, blit-through-material,
> copy back) stays the same.

- [ ] **Step 2: Create the canonical `.meta`**

Unity will generate a `.meta` for the new script. Ensure
`Assets/Scripts/Desert/OutlineRendererFeature.cs.meta` contains the full
`MonoImporter` block (Unity's auto-stub is sometimes incomplete — this is a repo
routine). Keep the GUID Unity generated; the body must be:

```
fileFormatVersion: 2
guid: <KEEP UNITY'S GENERATED GUID>
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:
```

- [ ] **Step 3: Check the assembly setup**

If `Assets/Scripts/` (or a parent) contains an `.asmdef`, add assembly
references to `Unity.RenderPipelines.Universal.Runtime` and
`Unity.RenderPipelines.Core.Runtime` so the URP types resolve. If there is no
`.asmdef` (scripts compile into `Assembly-CSharp`), no action is needed — URP is
referenced automatically.

- [ ] **Step 4: Verify it compiles**

Refresh Unity and read the console. Expected: `OutlineRendererFeature` compiles
with no errors. Resolve any URP 17.3 API mismatches per the API note above, then
refresh again.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Desert" "Assets/Scripts/Desert.meta"
git commit -m "Add outline render feature (URP RenderGraph)"
```

---

## Task 5: Desert_Renderer and wiring

**Files:**
- Create: `Assets/Settings/Desert_Renderer.asset`
- Modify: `Assets/Settings/PC_RPAsset.asset` (additive — append to renderer list)
- Modify: `Assets/Scenes/DesertSandbox.unity` (camera renderer index)

- [ ] **Step 1: Create the renderer asset**

Create a new URP renderer: `Assets/Settings/Desert_Renderer.asset`
(`Create > Rendering > URP Universal Renderer`). Leave its settings at default.

- [ ] **Step 2: Add the outline feature**

On `Desert_Renderer`, add the `OutlineRendererFeature`. Configure it:
- `Edge Detect Shader` → `Desert/OutlineEdgeDetect` (the Task 3 shader).
- `Outline Color` → black `#000000`, alpha `1`.
- `Thickness` `1`, `Depth Threshold` `0.5`, `Normal Threshold` `0.4`.
- `Injection Point` → `Before Rendering Post Processing`.

- [ ] **Step 3: Register the renderer (additive edit)**

Open `Assets/Settings/PC_RPAsset.asset` and **append** `Desert_Renderer` to its
Renderer List. Do not remove or reorder existing renderers — the game's default
renderer must stay at index 0. Note the index `Desert_Renderer` lands at.

- [ ] **Step 4: Point the desert camera at it**

In `DesertSandbox.unity`, select `Main Camera` → Rendering → Renderer → choose
`Desert_Renderer`.

- [ ] **Step 5: Verify**

Enter Play mode in `DesertSandbox.unity`. Expected: the scene renders with no
console errors, and `Desert_Renderer` is the active renderer for the camera.
(No outline is visible yet — there is no geometry; that arrives in Task 7.)

- [ ] **Step 6: Commit**

```bash
git add "Assets/Settings/Desert_Renderer.asset" "Assets/Settings/Desert_Renderer.asset.meta" \
        "Assets/Settings/PC_RPAsset.asset" "Assets/Scenes/DesertSandbox.unity"
git commit -m "Add Desert_Renderer and wire the outline feature"
```

---

## Task 6: Palette materials

**Files:**
- Create: `Assets/Materials/Desert/Sand.mat`
- Create: `Assets/Materials/Desert/RedSandstone.mat`
- Create: `Assets/Materials/Desert/Limestone.mat`
- Create: `Assets/Materials/Desert/OxidizedRock.mat`

- [ ] **Step 1: Create the four materials**

Create `Assets/Materials/Desert/` and four materials, each using the
`Desert/CelShaded` shader, with these values:

| Material | Base Color (RGB) | Bands | Min Light | Strata | Strata Color | Strata Strength |
|---|---|---|---|---|---|---|
| `Sand` | `(0.85, 0.69, 0.44)` | 3 | 0.3 | Off | — | — |
| `RedSandstone` | `(0.78, 0.27, 0.16)` | 3 | 0.3 | **On** | `(0.56, 0.20, 0.13)` | 0.55 |
| `Limestone` | `(0.80, 0.75, 0.62)` | 3 | 0.3 | **On** | `(0.66, 0.60, 0.48)` | 0.40 |
| `OxidizedRock` | `(0.43, 0.28, 0.18)` | 3 | 0.3 | **On** | `(0.30, 0.18, 0.12)` | 0.45 |

Leave `Two-Tone Strength` at `0` for all four — the two-tone pass is tuned in C3.
For the strata materials set `Strata Frequency` `0.15`, `Strata Warp` `3`.

- [ ] **Step 2: Verify**

Apply each material to a test primitive (e.g. a temporary cube) in
`DesertSandbox.unity`. Expected: each shows flat banded cel shading; the three
strata materials show horizontal colour bands. Delete the test primitive after
checking. Console clear.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Materials/Desert" "Assets/Materials/Desert.meta"
git commit -m "Add desert palette materials"
```

---

## Task 7: Mesa-and-arch hero formation

**Files:**
- Modify: `Assets/Scenes/DesertSandbox.unity`

- [ ] **Step 1: Build the ground patch**

In `DesertSandbox.unity`, create a ProBuilder plane named `GroundPatch`:
`80 × 80` units, `8 × 8` subdivisions, centred at origin, y `0`. Apply the
`Sand` material. Confirm it has a `MeshCollider`.

- [ ] **Step 2: Build the formation**

Create an empty GameObject `Formation_MesaArch` at origin, and hand-build a
mesa-and-arch cluster under it with ProBuilder — faceted low-poly, hard edges:
- `MesaA` — a flat-topped mesa, ~30u wide, ~24u tall, base slightly wider than
  top. Material `RedSandstone`.
- `MesaB` — a second mesa, ~24u wide, ~30u tall, ~42u from `MesaA`. Material
  `Limestone`.
- `Arch` — a chunky rock arch (two angled legs + a span across the top) bridging
  the gap between the mesas. **The opening must be 12-18u of clear flight space**
  (width and height). Material `RedSandstone`.

Give every ProBuilder mesh a `MeshCollider`. Keep all geometry faceted (no
smoothing groups) so the cel banding and outline read crisply.

- [ ] **Step 3: Verify**

Expected: in the Scene/Game view the formation shows banded cel shading, ink
outlines on silhouettes and creases, three palette colours, and no pure-black
shadowed faces (the `_MinLight` floor). The arch opening visibly clears 12-18u.
Console clear.

- [ ] **Step 4: Commit**

```bash
git add "Assets/Scenes/DesertSandbox.unity"
git commit -m "Add mesa-and-arch hero formation"
```

---

## Task 8: Checkpoint C1 review

**Files:** none (review gate)

- [ ] **Step 1: Capture views**

Capture screenshots of `DesertSandbox.unity` from 3-4 angles, including one
looking through the arch and one of a fully shadowed face.

- [ ] **Step 2: Present for review**

Present the screenshots to the user against the C1 criteria:
- Does the cel-shaded Valley-of-Fire look land — banded lighting, the palette?
- Do the ink outlines read well (silhouettes and interior creases), not too busy?
- Are shadows lifted — no pure black anywhere?

- [ ] **Step 3: Address feedback and commit any tuning**

Apply any tuning the user asks for (shader bands/min-light, outline thresholds,
material colours, formation shape). Commit each round of changes:

```bash
git add -A
git commit -m "C1 review: <describe the tuning>"
```

- [ ] **Step 4: Checkpoint complete**

On user approval, C1 is done. The next step is a writing-plans pass for
Checkpoint C2 (layout).

---

## Definition of done (C1)

- `DesertSandbox.unity` opens with no console errors and is absent from Build Settings.
- The cel shader, outline shader and render feature all compile clean.
- `Desert_Renderer` carries the outline feature and the desert camera uses it; the
  game's renderers are untouched apart from the additive `PC_RPAsset` entry.
- The mesa-and-arch formation renders cel-shaded and ink-outlined in the four-colour
  palette, with no pure-black shadows, and the arch clears 12-18u.
- The user has reviewed and approved the C1 look.
