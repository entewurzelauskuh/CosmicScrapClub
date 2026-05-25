using System;
using UnityEngine;

namespace CubeFly.Core
{
    // PlayerPrefs-backed static facade for the VFX Debug-tab toggles.
    // The typed bool properties below each read a PlayerPrefs key
    // (default 1 = ON); each Set writes + saves + fires Changed. No
    // batching, no Apply button: changes take effect immediately
    // because the Debug tab is a real-time A/B comparison surface.
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
        const string KMuzzleFlashPyramid  = "VfxMuzzleFlashPyramid";
        const string KMuzzleFlashCylinder = "VfxMuzzleFlashCylinder";
        const string KBulletTracer        = "VfxBulletTracer";
        const string KBulletImpactSpark   = "VfxBulletImpactSpark";
        const string KBulletImpactDust    = "VfxBulletImpactDust";
        const string KRocketExhaust       = "VfxRocketExhaust";
        const string KRocketSmokeTrail    = "VfxRocketSmokeTrail";
        const string KRocketSmokePuff     = "VfxRocketSmokePuff";

        public static event Action Changed;

        public static bool Bloom               { get => Get(KBloom); set => Set(KBloom, value); }
        public static bool Vignette            { get => Get(KVignette); set => Set(KVignette, value); }
        public static bool Tonemapping         { get => Get(KTonemapping); set => Set(KTonemapping, value); }
        public static bool ColorAdjustments    { get => Get(KColorAdjustments); set => Set(KColorAdjustments, value); }
        public static bool ChromaticAberration { get => Get(KChromaticAberration); set => Set(KChromaticAberration, value); }
        public static bool EnginePlume         { get => Get(KEnginePlume); set => Set(KEnginePlume, value); }
        public static bool BoostFlare          { get => Get(KBoostFlare); set => Set(KBoostFlare, value); }
        public static bool RcsPuff             { get => Get(KRcsPuff); set => Set(KRcsPuff, value); }
        public static bool MuzzleFlashPyramid  { get => Get(KMuzzleFlashPyramid);  set => Set(KMuzzleFlashPyramid,  value); }
        public static bool MuzzleFlashCylinder { get => Get(KMuzzleFlashCylinder); set => Set(KMuzzleFlashCylinder, value); }
        public static bool BulletTracer        { get => Get(KBulletTracer);        set => Set(KBulletTracer,        value); }
        public static bool BulletImpactSpark   { get => Get(KBulletImpactSpark);   set => Set(KBulletImpactSpark,   value); }
        public static bool BulletImpactDust    { get => Get(KBulletImpactDust);    set => Set(KBulletImpactDust,    value); }
        public static bool RocketExhaust       { get => Get(KRocketExhaust);       set => Set(KRocketExhaust,       value); }
        public static bool RocketSmokeTrail    { get => Get(KRocketSmokeTrail);    set => Set(KRocketSmokeTrail,    value); }
        public static bool RocketSmokePuff     { get => Get(KRocketSmokePuff);     set => Set(KRocketSmokePuff,     value); }

        static bool Get(string key) => PlayerPrefs.GetInt(key, 1) != 0;

        static void Set(string key, bool value)
        {
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
