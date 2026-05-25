using System;
using UnityEngine;

namespace CubeFly.Core
{
    // PlayerPrefs-backed static facade for the VFX Debug-tab toggles.
    // Five typed bool properties; each Get reads PlayerPrefs (default 1
    // = ON), each Set writes + saves + fires Changed. No batching, no
    // Apply button: changes take effect immediately because the Debug
    // tab is a real-time A/B comparison surface.
    //
    // Default = ON for every key so first-launch matches the spec's
    // "Defaults: ON" rule. PlayerPrefs keys are prefixed `Vfx` so future
    // Settings consumers can use their own prefixes (`Audio`, `Display`,
    // etc.) without collision.
    //
    // Subscribers (currently just VfxApplier) listen to Changed and
    // re-apply settings to the active scene's Volume.
    public static class VfxSettings
    {
        const string KBloom               = "VfxBloom";
        const string KVignette            = "VfxVignette";
        const string KTonemapping         = "VfxTonemapping";
        const string KColorAdjustments    = "VfxColorAdjustments";
        const string KChromaticAberration = "VfxChromaticAberration";
        const string KEnginePlume         = "VfxEnginePlume";
        const string KBoostFlare          = "VfxBoostFlare";
        const string KRcsPuff             = "VfxRcsPuff";

        public static event Action Changed;

        public static bool Bloom               { get => Get(KBloom); set => Set(KBloom, value); }
        public static bool Vignette            { get => Get(KVignette); set => Set(KVignette, value); }
        public static bool Tonemapping         { get => Get(KTonemapping); set => Set(KTonemapping, value); }
        public static bool ColorAdjustments    { get => Get(KColorAdjustments); set => Set(KColorAdjustments, value); }
        public static bool ChromaticAberration { get => Get(KChromaticAberration); set => Set(KChromaticAberration, value); }
        public static bool EnginePlume         { get => Get(KEnginePlume); set => Set(KEnginePlume, value); }
        public static bool BoostFlare          { get => Get(KBoostFlare); set => Set(KBoostFlare, value); }
        public static bool RcsPuff             { get => Get(KRcsPuff); set => Set(KRcsPuff, value); }

        static bool Get(string key) => PlayerPrefs.GetInt(key, 1) != 0;

        static void Set(string key, bool value)
        {
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
