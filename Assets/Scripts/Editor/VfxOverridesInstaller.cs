using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CubeFly.EditorTools
{
    // One-shot installer for Phase 1 of the VFX pass. Applies the
    // spec's starter tunings to five URP post-processing overrides
    // (Bloom, Vignette, Tonemapping, ColorAdjustments,
    // ChromaticAberration) on DefaultVolumeProfile.
    //
    // Idempotent: the Configure* methods Add the override if missing
    // and then ALWAYS Override the tuned fields, so re-runs reset the
    // values back to the spec's defaults — useful if anyone has tweaked
    // them mid-experiment and wants a clean baseline.
    //
    // The DefaultVolumeProfile that ships with Unity 6's URP template
    // is a kitchen-sink asset that already contains all override
    // components with zero/neutral intensities. That's why the
    // Configure* methods never end up calling Add() in practice — they
    // just write the tuning values into the existing override
    // instances. But the Add fallback is kept so a sparse profile
    // (e.g. one a user has manually trimmed) still works.
    //
    // Invoked via Tools/CubeFly/Apply Phase A VFX overrides (or via
    // Unity MCP's execute_menu_item tool). The script stays in the
    // repo as insurance: if anyone resets the profile or alters the
    // tunings, re-running the menu restores the spec's values.
    public static class VfxOverridesInstaller
    {
        const string ProfilePath = "Assets/Settings/DefaultVolumeProfile.asset";
        const string MenuPath    = "Tools/CubeFly/Apply Phase A VFX overrides";

        [MenuItem(MenuPath)]
        public static void Apply()
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                Debug.LogError($"VfxOverridesInstaller: profile not found at {ProfilePath}");
                return;
            }

            ConfigureBloom(profile);
            ConfigureVignette(profile);
            ConfigureTonemapping(profile);
            ConfigureColorAdjustments(profile);
            ConfigureChromaticAberration(profile);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
            AssetDatabase.Refresh();
            Debug.Log("VfxOverridesInstaller: applied Phase A tunings to DefaultVolumeProfile " +
                "(Bloom 0.6/1.0/0.7, Vignette 0.25/0.4/black, Tonemapping ACES, " +
                "ColorAdjustments contrast +5 saturation +5, ChromaticAberration 0.08).");
        }

        static void ConfigureBloom(VolumeProfile p)
        {
            if (!p.TryGet<Bloom>(out var b))
                b = p.Add<Bloom>(true);
            b.active = true;
            b.intensity.Override(0.6f);
            b.threshold.Override(1.0f);
            b.scatter.Override(0.7f);
        }

        static void ConfigureVignette(VolumeProfile p)
        {
            if (!p.TryGet<Vignette>(out var v))
                v = p.Add<Vignette>(true);
            v.active = true;
            v.intensity.Override(0.25f);
            v.smoothness.Override(0.4f);
            v.color.Override(Color.black);
        }

        static void ConfigureTonemapping(VolumeProfile p)
        {
            if (!p.TryGet<Tonemapping>(out var t))
                t = p.Add<Tonemapping>(true);
            t.active = true;
            t.mode.Override(TonemappingMode.ACES);
        }

        static void ConfigureColorAdjustments(VolumeProfile p)
        {
            if (!p.TryGet<ColorAdjustments>(out var c))
                c = p.Add<ColorAdjustments>(true);
            c.active = true;
            c.postExposure.Override(0f);
            c.contrast.Override(5f);
            c.saturation.Override(5f);
            c.hueShift.Override(0f);
        }

        static void ConfigureChromaticAberration(VolumeProfile p)
        {
            if (!p.TryGet<ChromaticAberration>(out var ca))
                ca = p.Add<ChromaticAberration>(true);
            ca.active = true;
            ca.intensity.Override(0.08f);
        }
    }
}
