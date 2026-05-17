# Desert Level — Checkpoint C3 (Dressing) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dress the rough C2 desert layout into the finished cel-shaded Valley-of-Fire look — gradient sky, warm lighting, distance fog, a post-processing volume, and a material detail pass.

**Architecture:** All dressing is scene / material / shader data — **no new C# scripts**. A new procedural gradient skybox shader provides the sky; lighting, ambient and fog are per-scene `RenderSettings`; post is a scene-local URP Volume; the existing `CelShaded.shader` already carries the two-tone and strata features, so the detail pass is pure material tuning.

**Tech Stack:** Unity 6.3 (6000.3.11f1), URP 17.3, hand-written HLSL, URP Volume framework. Execution via Unity MCP.

---

## Starting state (C1 + C2, merged to `main`)

- `Assets/Scenes/DesertSandbox.unity` — the level: `Main Camera` (has `FreeFlyCamera`, selects `Desert_Renderer`, at `(4,24,-68)` rot `(12,358,0)`), `Sun` directional light (rotation `(50,330,0)`), `DuneGround`, five `Formation_*` instances, `PerimeterRidge`.
- `Assets/Shaders/CelShaded.shader` — **already contains** the dressing shader features. Properties: `_TwoToneColor` / `_TwoToneScale` / `_TwoToneStrength`, and the `[Toggle(_STRATA_ON)]` strata feature `_StrataOn` / `_StrataColor` / `_StrataScale` / `_StrataStrength` / `_StrataWarp`. C3 only *tunes material values* — the shader needs no changes.
- `Assets/Materials/Desert/{Sand,RedSandstone,Limestone,OxidizedRock}.mat` — the four palette materials (base colors set in C1; detail params still at shader defaults: `_TwoToneStrength=0`, strata off).
- `Assets/Settings/Desert_Renderer.asset` — URP renderer with the outline feature; the camera selects it.
- Scene `RenderSettings`: Unity defaults — no custom skybox, no fog, default ambient.

The scene file `DesertSandbox.unity` stores the `Sun` light, the camera, **and** the per-scene `RenderSettings` (skybox / ambient / fog). So Tasks 2–5 each end by saving the scene.

## Conventions for every task

- **Compile/console check:** after writing a shader or running `execute_code`, poll `mcpforunity://editor/state` until `is_compiling=false` and `ready_for_tools=true`, then `read_console(types=["error"])`. Zero errors before proceeding.
- **Screenshots:** `manage_camera(action="screenshot", camera="Main Camera", include_image=true, max_resolution=768)` — always a *specific* camera, `include_image` standalone, never batched (C1/C2 lesson).
- **`execute_code` types:** fully-qualify Unity types (e.g. `UnityEngine.Rendering.VolumeProfile`) — bare `Object`/short names are ambiguous in the `execute_code` method-body context (C1 lesson).
- **Commit** at the end of each task with the exact `git add` paths shown.

---

## Task 1: Gradient skybox shader

**Files:**
- Create: `Assets/Shaders/GradientSkybox.shader`
- Create (Unity auto-generates): `Assets/Shaders/GradientSkybox.shader.meta`

- [ ] **Step 1: Write the skybox shader**

Create `Assets/Shaders/GradientSkybox.shader` with this exact content:

```hlsl
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
```

Notes baked into the code: render state (`Cull Off ZWrite Off`) sits inside the SubShader as Unity's stock skybox shaders do; the object-space vertex position is the eye-ray direction (the skybox mesh convention); `_MainLightPosition` comes free with `Core.hlsl` — do **not** redeclare it.

- [ ] **Step 2: Import and wait for compilation**

`refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`, then poll `mcpforunity://editor/state` until `is_compiling=false`.

- [ ] **Step 3: Verify the shader compiled**

Run: `read_console(action="get", types=["error"], count="20")`
Expected: no entries mentioning `GradientSkybox` or shader compilation. (MCP `Client handler exited` lines are infrastructure noise, not errors.)

- [ ] **Step 4: Commit**

```bash
git add Assets/Shaders/GradientSkybox.shader Assets/Shaders/GradientSkybox.shader.meta
git commit -m "Add gradient skybox shader for the desert level"
```

---

## Task 2: Desert sky material + assign as scene skybox

**Files:**
- Create: `Assets/Materials/Desert/DesertSky.mat`
- Modify: `Assets/Scenes/DesertSandbox.unity` (skybox is stored in `RenderSettings`)

- [ ] **Step 1: Create the sky material**

Create a material at `Assets/Materials/Desert/DesertSky.mat` using shader `Desert/GradientSkybox` (via `manage_material` create, or `execute_code`). The shader's baked defaults already produce the desert sky — no property overrides needed at creation; tuning happens at the C3 review.

- [ ] **Step 2: Assign it as the scene skybox**

Run via `execute_code`:

```csharp
var sky = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Desert/DesertSky.mat");
if (sky == null) return "FAILED: DesertSky.mat not found";
UnityEngine.RenderSettings.skybox = sky;
return "skybox assigned: " + sky.shader.name;
```

Expected return: `skybox assigned: Desert/GradientSkybox`

- [ ] **Step 3: Save the scene**

`manage_scene(action="save")` — persists the `RenderSettings.skybox` reference into `DesertSandbox.unity`.

- [ ] **Step 4: Verify the sky renders**

`manage_camera(action="screenshot", camera="Main Camera", include_image=true, max_resolution=768)`.
Expected: a warm gradient sky — pale dusty horizon blending to a soft blue above — replacing the flat default sky. The sun disc appears in the direction of the `Sun` light.

- [ ] **Step 5: Commit**

```bash
git add Assets/Materials/Desert/DesertSky.mat Assets/Materials/Desert/DesertSky.mat.meta Assets/Scenes/DesertSandbox.unity
git commit -m "Add desert gradient sky material and assign as scene skybox"
```

---

## Task 3: Desert lighting — sun + warm ambient

**Files:**
- Modify: `Assets/Scenes/DesertSandbox.unity` (the `Sun` light + `RenderSettings` ambient)

- [ ] **Step 1: Tune the Sun directional light**

Run via `execute_code` (warm-white, bright/bleached; elevation already 50° — within the spec's 45–60° — so rotation is left as is):

```csharp
var sun = GameObject.Find("Sun");
if (sun == null) return "FAILED: Sun not found";
var light = sun.GetComponent<Light>();
light.color = new Color(1.0f, 0.955f, 0.84f);
light.intensity = 1.35f;
// _MinLight in CelShaded.shader is the shadow floor (~0.3); leave shadowStrength at its default.
return "sun: intensity=" + light.intensity;
```

- [ ] **Step 2: Set warm sky ambient**

Run via `execute_code` — ambient is sourced from the (warm) skybox so it stays coherent with Task 2:

```csharp
UnityEngine.RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
UnityEngine.RenderSettings.ambientIntensity = 1.0f;
DynamicGI.UpdateEnvironment();
return "ambient mode: " + UnityEngine.RenderSettings.ambientMode;
```

Expected return: `ambient mode: Skybox`

- [ ] **Step 3: Save the scene**

`manage_scene(action="save")`.

- [ ] **Step 4: Verify the lighting**

`manage_camera(action="screenshot", camera="Main Camera", include_image=true, max_resolution=768)`.
Expected: the formations read brighter and warmer; shadowed faces are lifted (no pure black — the `_MinLight` floor), banded by the cel ramp.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scenes/DesertSandbox.unity
git commit -m "Tune desert sun light and warm sky ambient"
```

---

## Task 4: Warm distance fog

**Files:**
- Modify: `Assets/Scenes/DesertSandbox.unity` (`RenderSettings` fog)

`CelShaded.shader` is already fog-ready (`#pragma multi_compile_fog`, `MixFog`). Enabling fog in `RenderSettings` is all that is needed.

- [ ] **Step 1: Enable warm linear fog**

Run via `execute_code` — fog color matches the skybox `_HorizonColor` (≈ `0.93,0.82,0.62`) so geometry fades seamlessly into the horizon haze:

```csharp
UnityEngine.RenderSettings.fog = true;
UnityEngine.RenderSettings.fogMode = UnityEngine.FogMode.Linear;
UnityEngine.RenderSettings.fogColor = new Color(0.91f, 0.81f, 0.62f);
UnityEngine.RenderSettings.fogStartDistance = 45f;
UnityEngine.RenderSettings.fogEndDistance = 280f;
return "fog enabled: " + UnityEngine.RenderSettings.fogStartDistance + ".." + UnityEngine.RenderSettings.fogEndDistance;
```

- [ ] **Step 2: Save the scene**

`manage_scene(action="save")`.

- [ ] **Step 3: Verify the haze**

Capture a long view so the far perimeter ridge is in frame: move the camera, screenshot, restore it.

```csharp
// Step 3a — move the camera to a long-view vantage:
var cam = GameObject.Find("Main Camera").transform;
cam.position = new Vector3(0f, 30f, -110f);
cam.rotation = Quaternion.Euler(6f, 0f, 0f);
return "camera moved for fog check";
```

Then `manage_camera(action="screenshot", camera="Main Camera", include_image=true, max_resolution=768)`.
Expected: near formations stay clear; the far perimeter ridge fades into warm haze.

```csharp
// Step 3b — restore the camera to the play start:
var cam = GameObject.Find("Main Camera").transform;
cam.position = new Vector3(4f, 24f, -68f);
cam.rotation = Quaternion.Euler(12f, 358f, 0f);
return "camera restored";
```

- [ ] **Step 4: Save the scene and commit**

`manage_scene(action="save")`, then:

```bash
git add Assets/Scenes/DesertSandbox.unity
git commit -m "Add warm distance fog to the desert scene"
```

---

## Task 5: Desert post-processing Volume

**Files:**
- Create: `Assets/Settings/DesertVolumeProfile.asset`
- Modify: `Assets/Scenes/DesertSandbox.unity` (a `DesertPostVolume` GameObject + camera post flag)

- [ ] **Step 1: Create the volume profile with a gentle warm grade + light bloom**

Run via `execute_code` (deliberately minimal — heavy grading muddies flat cel color):

```csharp
var profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
UnityEditor.AssetDatabase.CreateAsset(profile, "Assets/Settings/DesertVolumeProfile.asset");

var ca = profile.Add<UnityEngine.Rendering.Universal.ColorAdjustments>(true);
ca.contrast.overrideState = true;    ca.contrast.value = 6f;
ca.colorFilter.overrideState = true; ca.colorFilter.value = new Color(1.0f, 0.97f, 0.90f);
ca.saturation.overrideState = true;  ca.saturation.value = 6f;

var bloom = profile.Add<UnityEngine.Rendering.Universal.Bloom>(true);
bloom.threshold.overrideState = true; bloom.threshold.value = 1.1f;
bloom.intensity.overrideState = true; bloom.intensity.value = 0.55f;
bloom.scatter.overrideState = true;   bloom.scatter.value = 0.6f;

UnityEditor.EditorUtility.SetDirty(profile);
UnityEditor.AssetDatabase.SaveAssets();
return "profile created with " + profile.components.Count + " effects";
```

Expected return: `profile created with 2 effects`

- [ ] **Step 2: Create a scene-local global Volume and enable camera post-processing**

Run via `execute_code`:

```csharp
var profile = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>("Assets/Settings/DesertVolumeProfile.asset");
if (profile == null) return "FAILED: DesertVolumeProfile.asset not found";

var go = new GameObject("DesertPostVolume");
var vol = go.AddComponent<UnityEngine.Rendering.Volume>();
vol.isGlobal = true;
vol.priority = 0f;
vol.sharedProfile = profile;

var camData = GameObject.Find("Main Camera").GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
camData.renderPostProcessing = true;

return "DesertPostVolume created; camera post-processing = " + camData.renderPostProcessing;
```

Expected return: `DesertPostVolume created; camera post-processing = True`

- [ ] **Step 3: Save the scene**

`manage_scene(action="save")`.

- [ ] **Step 4: Verify post-processing**

`manage_camera(action="screenshot", camera="Main Camera", include_image=true, max_resolution=768)`.
Expected: a gentle lift in warmth/contrast; bright sky/sun edges bloom softly. The effect should be subtle — if it looks heavily graded, note it for the Task 7 tune.

- [ ] **Step 5: Commit**

```bash
git add Assets/Settings/DesertVolumeProfile.asset Assets/Settings/DesertVolumeProfile.asset.meta Assets/Scenes/DesertSandbox.unity
git commit -m "Add desert post-processing volume (warm grade + light bloom)"
```

---

## Task 6: Material detail pass — two-tone + strata

**Files:**
- Modify: `Assets/Materials/Desert/Sand.mat`
- Modify: `Assets/Materials/Desert/RedSandstone.mat`
- Modify: `Assets/Materials/Desert/Limestone.mat`
- Modify: `Assets/Materials/Desert/OxidizedRock.mat`

`CelShaded.shader` already implements both features. This task dials in their per-material values. Sand gets two-tone break-up only; the three rock materials also get strata banding. Target starting values:

| Material | `_TwoToneColor` | `_TwoToneScale` | `_TwoToneStrength` | `_StrataOn` | `_StrataColor` | `_StrataScale` | `_StrataStrength` | `_StrataWarp` |
|---|---|---|---|---|---|---|---|---|
| Sand | (0.66,0.46,0.30) | 0.045 | 0.30 | 0 — off | — | — | — | — |
| RedSandstone | (0.55,0.22,0.14) | 0.05 | 0.30 | 1 — on | (0.42,0.20,0.13) | 0.12 | 0.55 | 3.0 |
| Limestone | (0.62,0.56,0.45) | 0.045 | 0.28 | 1 — on | (0.50,0.36,0.24) | 0.10 | 0.45 | 2.5 |
| OxidizedRock | (0.40,0.24,0.15) | 0.05 | 0.32 | 1 — on | (0.30,0.17,0.11) | 0.13 | 0.55 | 3.5 |

- [ ] **Step 1: Apply two-tone to all four materials, strata to the three rock materials**

Run via `execute_code`. The strata feature is `[Toggle(_STRATA_ON)]` — the float `_StrataOn` **and** the shader keyword `_STRATA_ON` must both be set, so this is done explicitly:

```csharp
System.Action<string,Color,float,float> twoTone = (path, col, scale, strength) =>
{
    var m = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
    m.SetColor("_TwoToneColor", col);
    m.SetFloat("_TwoToneScale", scale);
    m.SetFloat("_TwoToneStrength", strength);
    UnityEditor.EditorUtility.SetDirty(m);
};
System.Action<string,Color,float,float,float> strata = (path, col, scale, strength, warp) =>
{
    var m = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
    m.SetFloat("_StrataOn", 1f);
    m.EnableKeyword("_STRATA_ON");
    m.SetColor("_StrataColor", col);
    m.SetFloat("_StrataScale", scale);
    m.SetFloat("_StrataStrength", strength);
    m.SetFloat("_StrataWarp", warp);
    UnityEditor.EditorUtility.SetDirty(m);
};

string dir = "Assets/Materials/Desert/";
twoTone(dir+"Sand.mat",         new Color(0.66f,0.46f,0.30f), 0.045f, 0.30f);
twoTone(dir+"RedSandstone.mat", new Color(0.55f,0.22f,0.14f), 0.05f,  0.30f);
twoTone(dir+"Limestone.mat",    new Color(0.62f,0.56f,0.45f), 0.045f, 0.28f);
twoTone(dir+"OxidizedRock.mat", new Color(0.40f,0.24f,0.15f), 0.05f,  0.32f);

strata(dir+"RedSandstone.mat", new Color(0.42f,0.20f,0.13f), 0.12f, 0.55f, 3.0f);
strata(dir+"Limestone.mat",    new Color(0.50f,0.36f,0.24f), 0.10f, 0.45f, 2.5f);
strata(dir+"OxidizedRock.mat", new Color(0.30f,0.17f,0.11f), 0.13f, 0.55f, 3.5f);

UnityEditor.AssetDatabase.SaveAssets();
return "4 materials updated; strata enabled on 3 rock materials";
```

- [ ] **Step 2: Verify the detail reads**

`manage_camera(action="screenshot", camera="Main Camera", include_image=true, max_resolution=768)`.
Expected: rock faces show large-scale two-tone colour variation (no flat fills) and horizontal strata banding on the sandstone/limestone/oxidized rock; the sand ground has subtle two-tone variation but no banding.

- [ ] **Step 3: Commit**

```bash
git add Assets/Materials/Desert/Sand.mat Assets/Materials/Desert/RedSandstone.mat Assets/Materials/Desert/Limestone.mat Assets/Materials/Desert/OxidizedRock.mat
git commit -m "Tune material two-tone break-up and strata banding"
```

---

## Task 7: C3 checkpoint review & final tune

**Files:**
- Modify (only if the review calls for tweaks): any of the above.

- [ ] **Step 1: Capture a review set**

For each vantage below: move the camera via `execute_code`, then `manage_camera(action="screenshot", camera="Main Camera", include_image=true, max_resolution=768)`.

1. Entry / fly-through — `position (4,24,-68)`, `rotation (12,358,0)`.
2. Mid-level oblique — `position (-70,55,-75)`, `rotation (21,36,0)`.
3. Low canyon pass — `position (40,16,-10)`, `rotation (4,300,0)`.
4. Long view toward the perimeter ridge — `position (0,34,-115)`, `rotation (7,0,0)`.

Restore the camera to `position (4,24,-68)`, `rotation (12,358,0)` afterwards, then `manage_scene(action="save")`.

- [ ] **Step 2: Self-check against the C3 review question**

Spec §6 C3 review: *"the finished demonstrator — does the rusty / dusty / worn / sun-bleached DIY desert feeling come through?"* Also confirm spec §9 Definition of Done: consistent cel-shaded outlined look across ground and rock; no pure-black shadows; the perimeter ridge reads as the basin rim; warm dusty horizon.

- [ ] **Step 3: Apply any final tune**

If the review set shows values that need adjusting (fog distances, bloom intensity, a material strength, sky colours), make the minimal change, re-screenshot, and commit:

```bash
git add <changed files>
git commit -m "C3 final tune: <what changed>"
```

- [ ] **Step 4: Present to the user**

Present the review screenshots and a short assessment. **Optional polish (spec §5.7 — drifting dust / dust devils, stylized flat clouds) is explicitly optional and out of this plan's default scope** — raise it with the user; only build it if the review specifically asks for it, and if so it gets its own follow-up plan. This is the final checkpoint of the desert demonstrator: hand off to the user to fly the level and decide whether C3 — and the demonstrator — is done.

---

## Notes & risks

- **Skybox `_MainLightPosition`** — populated by URP for every camera render (edit mode included), so the sun disc tracks the `Sun` light automatically; no sync script needed.
- **Fog color must track the skybox horizon** — if the sky `_HorizonColor` is retuned at review, update `RenderSettings.fogColor` to match, or geometry will fade to a different colour than the horizon.
- **Outline + fog** — the screen-space outline draws after fog; far, hazed geometry may still show crisp ink edges. Acceptable for the cel look; if it reads as busy at the review, the depth/normal thresholds on `Desert_Renderer` are the tuning knob (not part of this plan).
- **Post-processing must be enabled in two places** — the `DesertPostVolume` (Task 5) *and* `renderPostProcessing` on the camera (Task 5 Step 2). If bloom/grade does not show, that flag is the first thing to check.
- **No new C# scripts** in C3, so there is no `MonoImporter` `.meta` routine to apply — only the skybox shader, whose Unity-generated `.meta` (ShaderImporter) is complete.

## Self-review

- **Spec coverage** — §5.4 lighting → Task 3; §5.5 sky → Tasks 1–2; §5.6 fog → Task 4, post Volume → Task 5; §5.2 two-tone/strata detail → Task 6; §5.7 optional polish → Task 7 Step 4 (deferred by design); §6 C3 review → Task 7. All covered.
- **Placeholders** — none; every shader/code block is complete, every value is concrete.
- **Consistency** — property names match `CelShaded.shader` (`_TwoToneColor/Scale/Strength`, `_StrataOn/_StrataColor/_StrataScale/_StrataStrength/_StrataWarp`, keyword `_STRATA_ON`) and `GradientSkybox.shader` as defined in Task 1; asset paths match the spec §7 file manifest.
