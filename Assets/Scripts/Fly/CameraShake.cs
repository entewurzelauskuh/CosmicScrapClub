using UnityEngine;

namespace CubeFly.Fly
{
    // Static trauma accumulator for the third-person camera shake (B-3c).
    // Triggers (crash, rocket detonation) call Add; FlyCamera reads Trauma,
    // applies a noise-driven offset scaled by trauma², and Decays it each
    // frame. Static so triggers need no camera reference (mirrors
    // CubeDeath.CubeDied). Reset on FlyScene load clears leftover trauma
    // (the static outlives scene loads).
    public static class CameraShake
    {
        static float _trauma;

        public static float Trauma => _trauma;
        public static void Add(float amount) => _trauma = Mathf.Clamp01(_trauma + amount);
        public static void Decay(float recoverPerSec, float dt) => _trauma = Mathf.Max(0f, _trauma - recoverPerSec * dt);
        public static void Reset() => _trauma = 0f;
    }
}
