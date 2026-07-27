using System.IO;
using UnityEditor;
using UnityEngine;

namespace CubeFly.EditorTools
{
    // Generates all VFX assets needed by Phase B-1 (engines + boost
    // flare + RCS puffs): a procedural radial-gradient particle texture,
    // three additive particle materials, two ParticleSystem prefabs.
    //
    // Convergent — re-running the menu always lands the same end state:
    //   • Glow_64.png: skipped if the file already exists (the texture
    //     content is fixed by the spec; importer settings are written
    //     only on first creation).
    //   • Materials: created if missing; tint / blend properties
    //     reapplied on every run so any drift converges back to spec.
    //   • Prefabs: unconditionally regenerated and overwritten every
    //     run via PrefabUtility.SaveAsPrefabAsset. The prefab GUID is
    //     stable across runs (it's the asset's, not the inner objects),
    //     so scene references to the prefab stay valid.
    //
    // Stays in the repo as insurance: if anyone tweaks an asset and
    // wants to restore the spec values, re-running the menu does it
    // cleanly.
    //
    // Folder convention (NEW this PR):
    //   Assets/VFX/Textures/     -- procedural / hand-painted particle sprites
    //   Assets/VFX/Materials/    -- particle materials
    //   Assets/VFX/Prefabs/      -- ParticleSystem prefabs
    //
    // The Editor folder convention excludes this script from runtime
    // builds. Invoke via Tools/CubeFly/Generate VFX assets (or via
    // Unity MCP's execute_menu_item tool).
    public static class VfxAssetsInstaller
    {
        const string MenuPath        = "Tools/CubeFly/Generate VFX assets";
        const string TexturesDir     = "Assets/VFX/Textures";
        const string MaterialsDir    = "Assets/VFX/Materials";
        const string PrefabsDir      = "Assets/VFX/Prefabs";
        const string GlowTexturePath = TexturesDir + "/Glow_64.png";
        const string StarburstTexturePath    = TexturesDir + "/MuzzleStarburst_64.png";
        const string TracerStripeTexturePath = TexturesDir + "/BulletTracerStripe_8x32.png";
        const string EnginePlumeMatPath    = MaterialsDir + "/EnginePlumeMat.mat";
        const string BoostShockMatPath     = MaterialsDir + "/BoostShockMat.mat";
        const string RcsPuffMatPath        = MaterialsDir + "/RcsPuffMat.mat";
        const string MuzzleStarburstMatPath   = MaterialsDir + "/MuzzleStarburstMat.mat";
        const string MuzzleDiscMatPath        = MaterialsDir + "/MuzzleDiscMat.mat";
        const string BulletTracerMatPath      = MaterialsDir + "/BulletTracerMat.mat";
        const string BulletImpactDustMatPath  = MaterialsDir + "/BulletImpactDustMat.mat";
        const string RocketExhaustMatPath     = MaterialsDir + "/RocketExhaustMat.mat";
        const string RocketSmokeTrailMatPath  = MaterialsDir + "/RocketSmokeTrailMat.mat";
        const string EnginePlumePrefabPath = PrefabsDir + "/EnginePlume.prefab";
        const string RcsPuffPrefabPath     = PrefabsDir + "/RcsPuff.prefab";
        const string MuzzleFlashStarburstPrefabPath = PrefabsDir + "/MuzzleFlashStarburst.prefab";
        const string MuzzleFlashDiscPrefabPath      = PrefabsDir + "/MuzzleFlashDisc.prefab";
        const string BulletImpactSparkPrefabPath = PrefabsDir + "/BulletImpactSpark.prefab";
        const string BulletImpactDustPrefabPath  = PrefabsDir + "/BulletImpactDust.prefab";
        const string RocketExhaustPlumePrefabPath = PrefabsDir + "/RocketExhaustPlume.prefab";
        const string RocketSmokePuffPrefabPath    = PrefabsDir + "/RocketSmokePuff.prefab";
        const string BulletPrefabPath = "Assets/Prefabs/Projectiles/Bullet.prefab";
        const string RocketPrefabPath = "Assets/Prefabs/Projectiles/Rocket.prefab";

        [MenuItem(MenuPath)]
        public static void Apply()
        {
            EnsureDir(TexturesDir);
            EnsureDir(MaterialsDir);
            EnsureDir(PrefabsDir);

            Texture2D glow = EnsureGlowTexture();
            Texture2D starburst    = EnsureStarburstTexture();
            Texture2D tracerStripe = EnsureTracerStripeTexture();

            Material enginePlume = EnsureAdditiveParticleMaterial(
                EnginePlumeMatPath, glow, new Color(0.5f, 0.75f, 1f, 1f));
            Material boostShock = EnsureAdditiveParticleMaterial(
                BoostShockMatPath, glow, new Color(0.8f, 0.9f, 1f, 1f));
            Material rcsPuff = EnsureAdditiveParticleMaterial(
                RcsPuffMatPath, glow, new Color(0.5f, 0.75f, 1f, 1f));

            Material muzzleStarburst   = EnsureAdditiveParticleMaterial(
                MuzzleStarburstMatPath,  starburst,    new Color(1f,    0.96f, 0.75f, 1f));
            Material muzzleDisc        = EnsureAdditiveParticleMaterial(
                MuzzleDiscMatPath,       glow,         new Color(1f,    0.70f, 0.30f, 1f));
            Material bulletTracer      = EnsureAdditiveParticleMaterial(
                BulletTracerMatPath,     tracerStripe, new Color(1f,    1f,    1f,    1f));
            Material bulletImpactDust  = EnsureAlphaBlendedParticleMaterial(
                BulletImpactDustMatPath, glow,         new Color(0.92f, 0.82f, 0.60f, 1f));
            Material rocketExhaust     = EnsureAdditiveParticleMaterial(
                RocketExhaustMatPath,    glow,         new Color(1f,    0.70f, 0.30f, 1f));
            Material rocketSmokeTrail  = EnsureAlphaBlendedParticleMaterial(
                RocketSmokeTrailMatPath, glow,         new Color(0.92f, 0.95f, 1f,    1f));

            EnsureEnginePlumePrefab(enginePlume, boostShock);
            EnsureRcsPuffPrefab(rcsPuff);
            EnsureMuzzleFlashStarburstPrefab(muzzleStarburst);
            EnsureMuzzleFlashDiscPrefab(muzzleDisc);
            EnsureBulletImpactSparkPrefab(muzzleStarburst);   // reuses MuzzleStarburstMat
            EnsureBulletImpactDustPrefab(bulletImpactDust);
            EnsureRocketExhaustPlumePrefab(rocketExhaust);
            EnsureRocketSmokePuffPrefab(rcsPuff);             // reuses RcsPuffMat from B-1

            // Load the just-generated prefabs as assets for wiring into
            // the projectile prefabs that consume them.
            GameObject exhaustPlumeAsset = AssetDatabase.LoadAssetAtPath<GameObject>(RocketExhaustPlumePrefabPath);
            GameObject smokePuffAsset    = AssetDatabase.LoadAssetAtPath<GameObject>(RocketSmokePuffPrefabPath);

            WireBulletPrefab(bulletTracer);
            WireRocketPrefab(exhaustPlumeAsset, smokePuffAsset, rocketSmokeTrail);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("VfxAssetsInstaller: applied Phase B-1 + B-2 VFX assets " +
                "(Glow_64, MuzzleStarburst_64, BulletTracerStripe_8x32; " +
                "EnginePlumeMat, BoostShockMat, RcsPuffMat, MuzzleStarburstMat, MuzzleDiscMat, " +
                "BulletTracerMat, BulletImpactDustMat, RocketExhaustMat, RocketSmokeTrailMat; " +
                "EnginePlume.prefab, RcsPuff.prefab, MuzzleFlashStarburst.prefab, MuzzleFlashDisc.prefab, " +
                "BulletImpactSpark.prefab, BulletImpactDust.prefab, " +
                "RocketExhaustPlume.prefab, RocketSmokePuff.prefab).");
        }

        static void EnsureDir(string assetDir)
        {
            if (!Directory.Exists(assetDir))
                Directory.CreateDirectory(assetDir);
        }

        // 64x64 RGBA32 with gaussian-falloff radial gradient. White core,
        // transparent edges. Used as the base sprite for every particle
        // material in this PR; future PRs can add more sprites here.
        static Texture2D EnsureGlowTexture()
        {
            if (File.Exists(GlowTexturePath))
                return AssetDatabase.LoadAssetAtPath<Texture2D>(GlowTexturePath);

            const int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[size * size];
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float maxDist = size / 2f;
            const float sigma = 2.2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);
                    float t = Mathf.Clamp01(dist / maxDist);
                    float alpha = Mathf.Exp(-t * t * sigma * sigma);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(GlowTexturePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(GlowTexturePath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(GlowTexturePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.sRGBTexture = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(GlowTexturePath);
        }

        // 64×64 RGBA32 starburst sprite: bright white core + 4 cardinal
        // spikes + 4 fainter diagonal spikes + radial gradient falloff.
        // Used by the Pyramid muzzle flash and the bullet impact spark
        // (both share MuzzleStarburstMat). Procedurally generated for
        // git-friendliness; skipped on subsequent runs if the PNG exists.
        static Texture2D EnsureStarburstTexture()
        {
            if (File.Exists(StarburstTexturePath))
                return AssetDatabase.LoadAssetAtPath<Texture2D>(StarburstTexturePath);

            const int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[size * size];
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float maxDist = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 d = new Vector2(x - center.x, y - center.y);
                    float dist = d.magnitude;
                    float radial = Mathf.Clamp01(dist / maxDist);

                    // Core: tight gaussian falloff.
                    float core = Mathf.Exp(-radial * radial * 9f);

                    // Cardinal spikes (0°, 90°, 180°, 270°): tight angular
                    // band perpendicular to each axis, fading along
                    // length proportional to (1 - radial).
                    float spikeH = Mathf.Exp(-((d.y * d.y) / 1.2f));
                    float spikeV = Mathf.Exp(-((d.x * d.x) / 1.2f));
                    float cardinal = Mathf.Max(spikeH, spikeV) * Mathf.Max(0f, 1f - radial) * 0.85f;

                    // Diagonal spikes (45°): rotate coords by 45°, same
                    // gaussian-along-axis treatment, fainter (×0.4).
                    float c = 0.7071f; // cos 45° = sin 45°
                    float dx45 =  d.x * c + d.y * c;
                    float dy45 = -d.x * c + d.y * c;
                    float diagH = Mathf.Exp(-((dy45 * dy45) / 1.2f));
                    float diagV = Mathf.Exp(-((dx45 * dx45) / 1.2f));
                    float diagonal = Mathf.Max(diagH, diagV) * Mathf.Max(0f, 1f - radial) * 0.40f;

                    float alpha = Mathf.Clamp01(Mathf.Max(core, Mathf.Max(cardinal, diagonal)));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(StarburstTexturePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(StarburstTexturePath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(StarburstTexturePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.sRGBTexture = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(StarburstTexturePath);
        }

        // 8×32 RGBA32 cross-section gradient for the bullet TrailRenderer.
        // V=0.5 → bright white core; V→0 and V→1 → transparent hot pink.
        // U axis is uniform (no pattern); the TrailRenderer's default UV
        // mapping stretches U along the trail length, so the cross-
        // section pink-fringe halo appears across the trail's width.
        static Texture2D EnsureTracerStripeTexture()
        {
            if (File.Exists(TracerStripeTexturePath))
                return AssetDatabase.LoadAssetAtPath<Texture2D>(TracerStripeTexturePath);

            const int w = 8;
            const int h = 32;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[w * h];
            float center = (h - 1) / 2f;
            float maxDist = center;
            Color pink = new Color(1f, 0.4f, 0.75f);
            for (int y = 0; y < h; y++)
            {
                float dist = Mathf.Abs(y - center);
                float t = Mathf.Clamp01(dist / maxDist);
                // Gaussian-shaped cross-section: bright white at center,
                // fading through warm-pink to transparent at edges.
                float coreWeight = Mathf.Exp(-t * t * 4f);    // 1.0 at center, ~0.02 at edge
                Color rgb = Color.Lerp(pink, Color.white, coreWeight);
                float alpha = coreWeight;
                Color c = new Color(rgb.r, rgb.g, rgb.b, alpha);
                for (int x = 0; x < w; x++)
                    pixels[y * w + x] = c;
            }
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(TracerStripeTexturePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(TracerStripeTexturePath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(TracerStripeTexturePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.sRGBTexture = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(TracerStripeTexturePath);
        }

        // URP Particles/Unlit, additive blend. Bloom (Phase 1) pulls a
        // halo from the HDR-bright Start Color the ParticleSystem sets.
        static Material EnsureAdditiveParticleMaterial(string path, Texture2D texture, Color tint)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            if (texture != null && mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", texture);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", tint);
            if (mat.HasProperty("_MainTex") && texture != null)
                mat.SetTexture("_MainTex", texture);

            // Configure additive blending. URP/Particles/Unlit: _Surface = 1
            // (Transparent) + _Blend = 2 (Additive).
            // NOTE: _Blend = 1 is *Premultiply*, not Additive. Using 1 lets
            // URP's material validation silently revert _SrcBlend/_DstBlend
            // from the additive SrcAlpha/One back to premultiply
            // One/OneMinusSrcAlpha on every reimport — which both dulls the
            // intended bloom glow and leaves the .mat perpetually dirty in
            // git. Must be 2. (B-2 desert-verify fix)
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend"))   mat.SetFloat("_Blend", 2f);
            if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            if (mat.HasProperty("_ZWrite"))   mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3000;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            EditorUtility.SetDirty(mat);
            return mat;
        }

        // URP Particles/Unlit, alpha-blended (NOT additive). For VFX
        // that should darken or soft-overlay (smoke, dust) rather than
        // pop with bloom. SrcAlpha + OneMinusSrcAlpha standard alpha.
        static Material EnsureAlphaBlendedParticleMaterial(string path, Texture2D texture, Color tint)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            if (texture != null && mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", texture);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", tint);
            if (mat.HasProperty("_MainTex") && texture != null)
                mat.SetTexture("_MainTex", texture);

            // Alpha-blend: _Surface = 1 (Transparent) + _Blend = 0 (Alpha).
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend"))   mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite"))   mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3000;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void EnsureEnginePlumePrefab(Material plumeMat, Material shockMat)
        {
            GameObject root = new GameObject("EnginePlume");
            try
            {
                // Root holds the main plume ParticleSystem.
                ParticleSystem ps = root.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                var main = ps.main;
                main.duration = 5f;
                main.loop = true;
                // Tuning: 0.22 lifetime + 30 rate = ~7 alive particles
                // at steady-state full thrust (clean jet look). Earlier
                // values of 0.4 / 60 read as too 'bushy' in play-test.
                main.startLifetime = 0.22f;
                main.startSpeed = 6f;
                main.startSize = 0.2f;
                // HDR-bright cool blue (Color * 2.5 for bloom interaction).
                main.startColor = new Color(0.5f, 0.75f, 1f, 1f) * 2.5f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 100;
                main.playOnAwake = true;

                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 30f;          // overridden by ThrusterVfx

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 8f;
                shape.radius = 0.1f;
                shape.position = Vector3.zero;
                shape.rotation = Vector3.zero;

                var col = ps.colorOverLifetime;
                col.enabled = true;
                Gradient g = new Gradient();
                g.SetKeys(
                    new GradientColorKey[]
                    {
                        new GradientColorKey(Color.white, 0f),
                        new GradientColorKey(new Color(0.6f, 0.85f, 1f), 1f),
                    },
                    new GradientAlphaKey[]
                    {
                        new GradientAlphaKey(0f, 0f),
                        new GradientAlphaKey(1f, 0.15f),
                        new GradientAlphaKey(0f, 1f),
                    });
                col.color = new ParticleSystem.MinMaxGradient(g);

                var sz = ps.sizeOverLifetime;
                sz.enabled = true;
                AnimationCurve sc = new AnimationCurve(
                    new Keyframe(0f, 0.5f), new Keyframe(0.3f, 1.0f), new Keyframe(1f, 0.2f));
                sz.size = new ParticleSystem.MinMaxCurve(1f, sc);

                var renderer = root.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.lengthScale = 0f;
                renderer.velocityScale = 1.5f;
                renderer.sharedMaterial = plumeMat;

                // Shock-diamond child — ThrusterVfx activates on boost.
                GameObject shock = new GameObject("ShockDiamond");
                shock.transform.SetParent(root.transform, false);
                shock.SetActive(false);
                ParticleSystem shockPs = shock.AddComponent<ParticleSystem>();
                shockPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                var sm = shockPs.main;
                sm.duration = 5f;
                sm.loop = true;
                sm.startLifetime = 0.15f;
                sm.startSpeed = 0f;
                sm.startSize = 0.4f;
                sm.startColor = new Color(0.95f, 0.95f, 1f, 1f) * 4f;
                sm.simulationSpace = ParticleSystemSimulationSpace.Local;
                sm.maxParticles = 30;
                sm.playOnAwake = true;

                var se = shockPs.emission;
                se.enabled = true;
                se.rateOverTime = 40f;

                var sShape = shockPs.shape;
                sShape.enabled = true;
                sShape.shapeType = ParticleSystemShapeType.Sphere;
                sShape.radius = 0.05f;

                var sRenderer = shock.GetComponent<ParticleSystemRenderer>();
                sRenderer.renderMode = ParticleSystemRenderMode.Billboard;
                sRenderer.sharedMaterial = shockMat;

                PrefabUtility.SaveAsPrefabAsset(root, EnginePlumePrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // Pyramid muzzle flash. Single-particle one-shot starburst at the
        // weapon's tip. main.loop = false + the single-burst emission +
        // stopAction = Destroy together auto-clean the instantiated
        // GameObject once the burst's lone particle expires (after
        // startLifetime ~0.06 s).
        static void EnsureMuzzleFlashStarburstPrefab(Material starburstMat)
        {
            GameObject root = new GameObject("MuzzleFlashStarburst");
            try
            {
                ParticleSystem ps = root.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                var main = ps.main;
                main.duration = 0.10f;
                main.loop = false;
                main.startLifetime = 0.06f;
                main.startSpeed = 0f;
                main.startSize = 0.18f;
                main.startColor = new Color(1f, 0.96f, 0.75f, 1f) * 3f;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.maxParticles = 8;
                main.playOnAwake = true;
                main.stopAction = ParticleSystemStopAction.Destroy;

                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.05f;

                var sz = ps.sizeOverLifetime;
                sz.enabled = true;
                AnimationCurve szCurve = new AnimationCurve(
                    new Keyframe(0f, 1f), new Keyframe(1f, 0.4f));
                sz.size = new ParticleSystem.MinMaxCurve(1f, szCurve);

                var col = ps.colorOverLifetime;
                col.enabled = true;
                Gradient g = new Gradient();
                g.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.10f), new GradientAlphaKey(0f, 1f) });
                col.color = new ParticleSystem.MinMaxGradient(g);

                var renderer = root.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.sharedMaterial = starburstMat;

                PrefabUtility.SaveAsPrefabAsset(root, MuzzleFlashStarburstPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // Cylinder muzzle flash. Single-particle soft disc puff, warmer
        // orange tint, slight outward expansion (startSpeed > 0 +
        // expanding sizeOverLifetime). Reuses Glow_64.png via MuzzleDiscMat.
        static void EnsureMuzzleFlashDiscPrefab(Material discMat)
        {
            GameObject root = new GameObject("MuzzleFlashDisc");
            try
            {
                ParticleSystem ps = root.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                var main = ps.main;
                main.duration = 0.12f;
                main.loop = false;
                main.startLifetime = 0.10f;
                main.startSpeed = 0.5f;
                main.startSize = 0.30f;
                main.startColor = new Color(1f, 0.70f, 0.30f, 1f) * 2.5f;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.maxParticles = 6;
                main.playOnAwake = true;
                main.stopAction = ParticleSystemStopAction.Destroy;

                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.05f;

                var sz = ps.sizeOverLifetime;
                sz.enabled = true;
                AnimationCurve szCurve = new AnimationCurve(
                    new Keyframe(0f, 0.6f), new Keyframe(1f, 1.4f));
                sz.size = new ParticleSystem.MinMaxCurve(1f, szCurve);

                var col = ps.colorOverLifetime;
                col.enabled = true;
                Gradient g = new Gradient();
                g.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
                col.color = new ParticleSystem.MinMaxGradient(g);

                var renderer = root.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.sharedMaterial = discMat;

                PrefabUtility.SaveAsPrefabAsset(root, MuzzleFlashDiscPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // Bullet impact spark. Two-layer prefab:
        //  Layer 1 (root)  — small bright core, single billboard, white
        //                     starburst at quarter scale of the muzzle.
        //  Layer 2 (child) — 6 radial spark streaks flying outward,
        //                     stretched billboards velocity-aligned.
        // The whole prefab is instantiated with Quaternion.LookRotation(
        // hit.normal) by ProjectileHit.SpawnImpactVfx, so the child
        // hemisphere fires OUTWARD from the surface.
        static void EnsureBulletImpactSparkPrefab(Material sparkMat)
        {
            GameObject root = new GameObject("BulletImpactSpark");
            try
            {
                // Layer 1 — core
                ParticleSystem ps = root.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.duration = 0.10f;
                main.loop = false;
                main.startLifetime = 0.08f;
                main.startSpeed = 0f;
                main.startSize = 0.10f;
                main.startColor = new Color(1f, 0.96f, 0.75f, 1f) * 3f;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.maxParticles = 3;
                main.playOnAwake = true;
                main.stopAction = ParticleSystemStopAction.Destroy;

                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.02f;

                var sz = ps.sizeOverLifetime;
                sz.enabled = true;
                AnimationCurve szCurve = new AnimationCurve(
                    new Keyframe(0f, 1f), new Keyframe(1f, 0.5f));
                sz.size = new ParticleSystem.MinMaxCurve(1f, szCurve);

                var col = ps.colorOverLifetime;
                col.enabled = true;
                Gradient gCore = new Gradient();
                gCore.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.10f), new GradientAlphaKey(0f, 1f) });
                col.color = new ParticleSystem.MinMaxGradient(gCore);

                var renderer = root.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.sharedMaterial = sparkMat;

                // Layer 2 — sparks (child)
                GameObject sparksGo = new GameObject("Sparks");
                sparksGo.transform.SetParent(root.transform, false);
                ParticleSystem sparksPs = sparksGo.AddComponent<ParticleSystem>();
                sparksPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                var sm = sparksPs.main;
                sm.duration = 0.10f;
                sm.loop = false;
                sm.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.18f);
                sm.startSpeed = 2f;
                sm.startSize = 0.04f;
                sm.startColor = new Color(1f, 0.96f, 0.70f, 1f) * 2.5f;
                sm.simulationSpace = ParticleSystemSimulationSpace.World;
                sm.maxParticles = 8;
                sm.playOnAwake = true;
                sm.stopAction = ParticleSystemStopAction.None;

                var se = sparksPs.emission;
                se.enabled = true;
                se.rateOverTime = 0f;
                se.SetBursts(new[] { new ParticleSystem.Burst(0f, 6) });

                var sShape = sparksPs.shape;
                sShape.enabled = true;
                sShape.shapeType = ParticleSystemShapeType.Hemisphere;
                sShape.radius = 0.02f;

                var ssz = sparksPs.sizeOverLifetime;
                ssz.enabled = true;
                AnimationCurve sparkSizeCurve = new AnimationCurve(
                    new Keyframe(0f, 1f), new Keyframe(1f, 0.2f));
                ssz.size = new ParticleSystem.MinMaxCurve(1f, sparkSizeCurve);

                var scol = sparksPs.colorOverLifetime;
                scol.enabled = true;
                Gradient gSparks = new Gradient();
                gSparks.SetKeys(
                    new[]
                    {
                        new GradientColorKey(Color.white, 0f),
                        new GradientColorKey(new Color(1f, 0.4f, 0.75f), 1f),
                    },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                scol.color = new ParticleSystem.MinMaxGradient(gSparks);

                var sRenderer = sparksGo.GetComponent<ParticleSystemRenderer>();
                sRenderer.renderMode = ParticleSystemRenderMode.Stretch;
                sRenderer.lengthScale = 0f;
                sRenderer.velocityScale = 0.4f;
                sRenderer.sharedMaterial = sparkMat;

                PrefabUtility.SaveAsPrefabAsset(root, BulletImpactSparkPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // Bullet impact ground dust. Single-layer warm tan puff cluster.
        // Alpha-blended (not additive) so it darkens/soft-overlays
        // instead of glowing. Hemisphere shape aligned with hit.normal
        // at spawn time.
        static void EnsureBulletImpactDustPrefab(Material dustMat)
        {
            GameObject root = new GameObject("BulletImpactDust");
            try
            {
                ParticleSystem ps = root.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                var main = ps.main;
                main.duration = 0.30f;
                main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.40f);
                main.startSpeed = 0.8f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.25f);
                main.startColor = new Color(0.92f, 0.82f, 0.60f, 1f);
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 8;
                main.playOnAwake = true;
                main.stopAction = ParticleSystemStopAction.Destroy;

                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 5) });

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Hemisphere;
                shape.radius = 0.06f;

                var sz = ps.sizeOverLifetime;
                sz.enabled = true;
                AnimationCurve szCurve = new AnimationCurve(
                    new Keyframe(0f, 0.6f), new Keyframe(1f, 1.6f));
                sz.size = new ParticleSystem.MinMaxCurve(1f, szCurve);

                var col = ps.colorOverLifetime;
                col.enabled = true;
                Gradient g = new Gradient();
                g.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.7f, 0.10f), new GradientAlphaKey(0f, 1f) });
                col.color = new ParticleSystem.MinMaxGradient(g);

                var renderer = root.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.sharedMaterial = dustMat;

                PrefabUtility.SaveAsPrefabAsset(root, BulletImpactDustPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // Rocket exhaust plume. Continuous looping ParticleSystem, warm
        // yellow-orange HDR. Mirrors EnginePlume.prefab structure (Stretch
        // billboard, cone shape, world simulation) minus the ShockDiamond
        // child — no boost concept for rockets. Instantiated as a child
        // of Rocket; Rocket.OnDestroy detaches + Stop(KeepParticles) so
        // alive particles finish naturally.
        static void EnsureRocketExhaustPlumePrefab(Material exhaustMat)
        {
            GameObject root = new GameObject("RocketExhaustPlume");
            try
            {
                ParticleSystem ps = root.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                var main = ps.main;
                main.duration = 5f;
                main.loop = true;
                main.startLifetime = 0.18f;
                main.startSpeed = 4f;
                main.startSize = 0.15f;
                main.startColor = new Color(1f, 0.70f, 0.30f, 1f) * 2.5f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 60;
                main.playOnAwake = true;
                main.stopAction = ParticleSystemStopAction.Destroy;

                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 35f;

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 6f;
                shape.radius = 0.04f;

                var sz = ps.sizeOverLifetime;
                sz.enabled = true;
                AnimationCurve szCurve = new AnimationCurve(
                    new Keyframe(0f, 0.6f), new Keyframe(0.4f, 1.0f), new Keyframe(1f, 0.3f));
                sz.size = new ParticleSystem.MinMaxCurve(1f, szCurve);

                var col = ps.colorOverLifetime;
                col.enabled = true;
                Gradient g = new Gradient();
                g.SetKeys(
                    new[]
                    {
                        new GradientColorKey(Color.white, 0f),
                        new GradientColorKey(new Color(1f, 0.45f, 0.10f), 1f),
                    },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
                col.color = new ParticleSystem.MinMaxGradient(g);

                var renderer = root.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.lengthScale = 0f;
                renderer.velocityScale = 1.2f;
                renderer.sharedMaterial = exhaustMat;

                PrefabUtility.SaveAsPrefabAsset(root, RocketExhaustPlumePrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // Rocket smoke puff. Continuous looping ParticleSystem of soft
        // white discrete clouds. Reuses RcsPuffMat from B-1 (no new
        // material). 10x growth over 1.5 s lifetime per user spec —
        // start small (0.1), grow large (1.0), alpha-fade throughout.
        static void EnsureRocketSmokePuffPrefab(Material puffMat)
        {
            GameObject root = new GameObject("RocketSmokePuff");
            try
            {
                ParticleSystem ps = root.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                var main = ps.main;
                main.duration = 5f;
                main.loop = true;
                main.startLifetime = 1.5f;
                main.startSpeed = 0.3f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.10f);
                main.startColor = new Color(1f, 1f, 1f, 0.85f);
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 80;
                main.playOnAwake = true;
                main.stopAction = ParticleSystemStopAction.Destroy;

                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 15f;

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 5f;
                shape.radius = 0.06f;

                var sz = ps.sizeOverLifetime;
                sz.enabled = true;
                AnimationCurve szCurve = new AnimationCurve(
                    new Keyframe(0f, 0.1f), new Keyframe(1f, 1f));
                sz.size = new ParticleSystem.MinMaxCurve(1f, szCurve);

                var col = ps.colorOverLifetime;
                col.enabled = true;
                Gradient g = new Gradient();
                g.SetKeys(
                    new[]
                    {
                        new GradientColorKey(Color.white, 0f),
                        new GradientColorKey(new Color(0.92f, 0.95f, 1f), 1f),
                    },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.10f), new GradientAlphaKey(0f, 1f) });
                col.color = new ParticleSystem.MinMaxGradient(g);

                var renderer = root.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.sharedMaterial = puffMat;

                PrefabUtility.SaveAsPrefabAsset(root, RocketSmokePuffPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // Patches Bullet.prefab to set the tracerMaterial SerializeField
        // reference added in Phase B-2. Uses PrefabUtility.LoadPrefabContents
        // + SerializedObject so the prefab on disk gets the reference,
        // surviving a fresh clone of the repo. Idempotent — re-running
        // sets the same reference again.
        //
        // Bullet must have a [SerializeField] Material tracerMaterial
        // field at this point (added in the per-projectile wiring task).
        // If the field hasn't been added yet, SerializedObject.FindProperty
        // returns null and this no-ops with a warning — safe.
        static void WireBulletPrefab(Material tracerMat)
        {
            if (!File.Exists(BulletPrefabPath))
            {
                Debug.unityLogger.LogWarning("VfxAssetsInstaller",
                    $"{BulletPrefabPath} not found; skipping bullet prefab wiring.");
                return;
            }
            GameObject instance = PrefabUtility.LoadPrefabContents(BulletPrefabPath);
            try
            {
                var bullet = instance.GetComponent<CubeFly.Fly.Bullet>();
                if (bullet == null)
                {
                    Debug.unityLogger.LogWarning("VfxAssetsInstaller",
                        $"{BulletPrefabPath} has no Bullet component; skipping wiring.");
                    return;
                }
                var so = new SerializedObject(bullet);
                var prop = so.FindProperty("tracerMaterial");
                if (prop == null)
                {
                    Debug.unityLogger.LogWarning("VfxAssetsInstaller",
                        "Bullet has no 'tracerMaterial' SerializeField yet; " +
                        "wiring deferred until that field is added.");
                    return;
                }
                prop.objectReferenceValue = tracerMat;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(instance, BulletPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(instance);
            }
        }

        // Patches Rocket.prefab to set the exhaustPlumePrefab,
        // smokePuffPrefab, and smokeTrailMaterial SerializeField references
        // added in Phase B-2. Same idempotent SerializedObject path as
        // WireBulletPrefab.
        static void WireRocketPrefab(
            GameObject exhaustPlumePrefab, GameObject smokePuffPrefab, Material smokeTrailMat)
        {
            if (!File.Exists(RocketPrefabPath))
            {
                Debug.unityLogger.LogWarning("VfxAssetsInstaller",
                    $"{RocketPrefabPath} not found; skipping rocket prefab wiring.");
                return;
            }
            GameObject instance = PrefabUtility.LoadPrefabContents(RocketPrefabPath);
            try
            {
                var rocket = instance.GetComponent<CubeFly.Fly.Rocket>();
                if (rocket == null)
                {
                    Debug.unityLogger.LogWarning("VfxAssetsInstaller",
                        $"{RocketPrefabPath} has no Rocket component; skipping wiring.");
                    return;
                }
                var so = new SerializedObject(rocket);
                bool anySet = false;

                var pPlume = so.FindProperty("exhaustPlumePrefab");
                if (pPlume != null) { pPlume.objectReferenceValue = exhaustPlumePrefab; anySet = true; }

                var pPuff = so.FindProperty("smokePuffPrefab");
                if (pPuff != null) { pPuff.objectReferenceValue = smokePuffPrefab; anySet = true; }

                var pTrail = so.FindProperty("smokeTrailMaterial");
                if (pTrail != null) { pTrail.objectReferenceValue = smokeTrailMat; anySet = true; }

                if (!anySet)
                {
                    Debug.unityLogger.LogWarning("VfxAssetsInstaller",
                        "Rocket has no B-2 SerializeFields yet; wiring deferred " +
                        "until exhaustPlumePrefab/smokePuffPrefab/smokeTrailMaterial are added.");
                    return;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(instance, RocketPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(instance);
            }
        }

        static void EnsureRcsPuffPrefab(Material puffMat)
        {
            GameObject root = new GameObject("RcsPuff");
            try
            {
                ParticleSystem ps = root.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                var main = ps.main;
                main.duration = 1f;
                main.loop = false;
                main.startLifetime = 0.2f;
                main.startSpeed = 4f;
                main.startSize = 0.1f;
                main.startColor = new Color(0.5f, 0.75f, 1f, 1f) * 2f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 50;
                main.playOnAwake = false;             // RcsPuffVfx fires bursts manually

                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 0f;
                // No initial bursts — RcsPuffVfx calls ps.Emit(count).

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.05f;

                var col = ps.colorOverLifetime;
                col.enabled = true;
                Gradient g = new Gradient();
                g.SetKeys(
                    new GradientColorKey[]
                    {
                        new GradientColorKey(Color.white, 0f),
                        new GradientColorKey(new Color(0.5f, 0.75f, 1f), 1f),
                    },
                    new GradientAlphaKey[]
                    {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(0f, 1f),
                    });
                col.color = new ParticleSystem.MinMaxGradient(g);

                var renderer = root.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.sharedMaterial = puffMat;

                PrefabUtility.SaveAsPrefabAsset(root, RcsPuffPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
