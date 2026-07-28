using UnityEngine;

namespace CubeFly.Core
{
    // Sustained low-HP feedback on a cube below 25% HP: a looping smoke
    // ParticleSystem (all cubes) + a pulsing red emissive alarm (player
    // construct cubes only, set via Configure). Lazily AddComponent'd by
    // CubeDamage when a cube crosses the threshold — one per cube. Reads its
    // toggles each frame so the Debug A/B works live, and self-cleans on
    // death so the B-3a death burst + drift aren't fighting a live smoke +
    // red tint. (B-3b)
    public class LowHpVfx : MonoBehaviour
    {
        // Configured once per FlyScene load by FlyController.Awake (mirrors
        // CubeDeath.ConfigureVfx). Null in unconfigured scenes → no smoke.
        public static GameObject SmokePrefab;
        public static void ConfigureVfx(GameObject smoke) => SmokePrefab = smoke;

        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        // Red the flicker pulses toward, HDR-scaled so bloom picks it up.
        static readonly Color FlickerColor = new Color(1f, 0.15f, 0.1f) * 2f;
        const float FlickerHz = 3.5f;

        bool _canFlicker;
        CubeStats _stats;
        ParticleSystem _smoke;
        Renderer _renderer;
        MaterialPropertyBlock _mpb;
        bool _flickering;
        bool _done;

        // isPlayer = the cube belongs to the player construct (from CubeDamage's
        // ConstructEnergySystem lookup). Only player cubes flicker.
        public void Configure(bool isPlayer) => _canFlicker = isPlayer;

        void Awake()
        {
            _stats = GetComponent<CubeStats>();
            _renderer = GetComponentInChildren<Renderer>();
            if (SmokePrefab != null)
            {
                GameObject go = Instantiate(SmokePrefab, transform);
                go.transform.localPosition = Vector3.zero;
                _smoke = go.GetComponentInChildren<ParticleSystem>();
            }
        }

        void Update()
        {
            if (_done) return;

            // Death cleanup: stand down so the death burst/drift take over.
            if (_stats == null || _stats.healthPoints <= 0f)
            {
                if (_smoke != null)
                {
                    var em = _smoke.emission; em.enabled = false;
                    _smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
                ClearFlicker();
                _done = true;
                return;
            }

            // Smoke (all cubes) — live toggle.
            if (_smoke != null)
            {
                var em = _smoke.emission;
                em.enabled = VfxSettings.LowHpSmoke;
            }

            // Flicker (player cubes only) — live toggle.
            if (_canFlicker && _renderer != null)
            {
                if (VfxSettings.LowHpFlicker)
                {
                    if (_mpb == null) _mpb = new MaterialPropertyBlock();
                    float t = (Mathf.Sin(Time.time * FlickerHz * Mathf.PI * 2f) + 1f) * 0.5f;
                    _renderer.GetPropertyBlock(_mpb);
                    _mpb.SetColor(EmissionColorId, FlickerColor * t);
                    _renderer.SetPropertyBlock(_mpb);
                    _flickering = true;
                }
                else if (_flickering)
                {
                    ClearFlicker();
                }
            }
        }

        // Restore the cube's baseline emissive (mirror the delete tool's
        // clear-on-un-hover). SetPropertyBlock(null) drops the override.
        void ClearFlicker()
        {
            if (_flickering && _renderer != null) _renderer.SetPropertyBlock(null);
            _flickering = false;
        }

        void OnDisable() => ClearFlicker();
    }
}
