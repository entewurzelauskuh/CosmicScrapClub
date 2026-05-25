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
        const string EnginePlumeMatPath    = MaterialsDir + "/EnginePlumeMat.mat";
        const string BoostShockMatPath     = MaterialsDir + "/BoostShockMat.mat";
        const string RcsPuffMatPath        = MaterialsDir + "/RcsPuffMat.mat";
        const string EnginePlumePrefabPath = PrefabsDir + "/EnginePlume.prefab";
        const string RcsPuffPrefabPath     = PrefabsDir + "/RcsPuff.prefab";

        [MenuItem(MenuPath)]
        public static void Apply()
        {
            EnsureDir(TexturesDir);
            EnsureDir(MaterialsDir);
            EnsureDir(PrefabsDir);

            Texture2D glow = EnsureGlowTexture();

            Material enginePlume = EnsureAdditiveParticleMaterial(
                EnginePlumeMatPath, glow, new Color(0.5f, 0.75f, 1f, 1f));
            Material boostShock = EnsureAdditiveParticleMaterial(
                BoostShockMatPath, glow, new Color(0.8f, 0.9f, 1f, 1f));
            Material rcsPuff = EnsureAdditiveParticleMaterial(
                RcsPuffMatPath, glow, new Color(0.5f, 0.75f, 1f, 1f));

            EnsureEnginePlumePrefab(enginePlume, boostShock);
            EnsureRcsPuffPrefab(rcsPuff);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("VfxAssetsInstaller: applied Phase B-1 VFX assets " +
                "(Glow_64, EnginePlumeMat, BoostShockMat, RcsPuffMat, EnginePlume.prefab, RcsPuff.prefab).");
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

            // Configure additive blending. URP/Particles/Unlit uses
            // _Surface = 1 (Transparent) + _Blend = 1 (Additive).
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend"))   mat.SetFloat("_Blend", 1f);
            if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
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
