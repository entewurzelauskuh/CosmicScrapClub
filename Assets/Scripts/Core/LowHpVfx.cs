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
        // Darker target red (−20% vs the first pass) so the alarm reads as a
        // warning glow, not a beacon.
        static readonly Color FlickerColor = new Color(1f, 0.15f, 0.1f) * 1.6f;
        // Intermittent "heartbeat": a short fast pulse, then a ~2 s wait.
        const float PulsePeriod = 2.2f;    // pulse-to-pulse interval
        const float PulseDuration = 0.2f;  // length of each quick pulse

        bool _canFlicker;
        CubeStats _stats;
        ParticleSystem _smoke;
        Renderer _renderer;
        Material _flickerMat;
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

            // Flicker (player cubes only) — live toggle. The cube materials
            // ship with _EMISSION off, so a MaterialPropertyBlock can't make
            // emission render (a MPB can't toggle shader keywords). Use a
            // per-renderer material instance — asset-safe, never touches the
            // shared .mat, freed with the cube — and enable the keyword once.
            if (_canFlicker && _renderer != null)
            {
                if (VfxSettings.LowHpFlicker)
                {
                    if (_flickerMat == null)
                    {
                        _flickerMat = _renderer.material;   // per-instance clone
                        _flickerMat.EnableKeyword("_EMISSION");
                    }
                    // Short fast pulse (0 → 1 → 0 over PulseDuration) then dark
                    // until the next PulsePeriod — a slow warning heartbeat.
                    float phase = Time.time % PulsePeriod;
                    float t = phase < PulseDuration ? Mathf.Sin(phase / PulseDuration * Mathf.PI) : 0f;
                    _flickerMat.SetColor(EmissionColorId, FlickerColor * t);
                    _flickering = true;
                }
                else if (_flickering)
                {
                    ClearFlicker();
                }
            }
        }

        // Kill the emissive glow (toggle off / death). Leaves the now
        // emission-black instance material in place — invisible; it's freed
        // when the cube despawns.
        void ClearFlicker()
        {
            if (_flickering && _flickerMat != null) _flickerMat.SetColor(EmissionColorId, Color.black);
            _flickering = false;
        }

        void OnDisable() => ClearFlicker();
    }
}
