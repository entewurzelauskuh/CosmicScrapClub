using CubeFly.Core;
using UnityEngine;

namespace CubeFly.Fly
{
    // Per-thruster engine-plume driver. Attached to each PlacedThruster
    // cube by FlyController.BuildConstruct. Instantiates EnginePlume.prefab
    // as a child, oriented along -LocalThrustAxis (so particles emit out
    // through the cone's circular placement face), and drives emission
    // / lifetime / colour each LateUpdate from
    // ThrusterBehavior.CurrentInputLevel + IsBoosting.
    //
    // The EnginePlume and BoostFlare toggles are independent:
    //
    //   • EnginePlume only controls the main plume's emission rate
    //     (zeroed when the toggle is off — existing particles fade
    //     out within their lifetime; no new ones spawn). The plume
    //     prefab itself stays active so the shock-diamond child can
    //     still render.
    //   • BoostFlare gates both the boost-time amplification of the
    //     main plume (×rate, ×lifetime, hotter colour) AND the
    //     visibility of the shock-diamond child.
    //   • Boost-flare activation rule: BoostFlare toggle on AND this
    //     thruster's ThrusterBehavior.IsBoosting (i.e. contributing
    //     to an active boost this fixed step).
    //
    // The shock-diamond is INDEPENDENT of EnginePlume — turning the
    // main plume off while leaving BoostFlare on still pops the
    // shock-diamond on boost, just without the surrounding stream.
    public class ThrusterVfx : MonoBehaviour
    {
        // Per-thruster steady-state plume tuning. Lowered from the
        // original 60 / 0.4 (which produced ~24 alive particles per
        // thruster at full thrust and read as too "bushy") to 30 / 0.22
        // (~7 alive at full thrust) for a cleaner jet look. Boost
        // multipliers unchanged.
        const float BasePlumeRate     = 30f;
        const float BoostRateMul      = 1.5f;
        const float BaseLifetime      = 0.22f;
        const float BoostLifetimeMul  = 1.4f;
        static readonly Color BasePlumeColor  = new Color(0.5f, 0.75f, 1f, 1f) * 2.5f;
        static readonly Color BoostPlumeColor = new Color(0.85f, 0.92f, 1f, 1f) * 4f;

        const string TAG = "ThrusterVfx";

        [SerializeField] GameObject _plumePrefab;

        ThrusterBehavior _thruster;
        GameObject _plumeInstance;
        ParticleSystem _plumePs;
        ParticleSystem _shockPs;

        // Set by FlyController.BuildConstruct right after AddComponent.
        public void SetPlumePrefab(GameObject prefab) => _plumePrefab = prefab;

        void Awake()
        {
            _thruster = GetComponent<ThrusterBehavior>();
        }

        void Start()
        {
            if (_plumePrefab == null)
            {
                Debug.unityLogger.LogWarning(TAG, $"{name}: no EnginePlume prefab assigned; plume disabled.");
                enabled = false;
                return;
            }

            _plumeInstance = Instantiate(_plumePrefab, transform);
            // Orient the plume so its emission direction points along the
            // thruster's exhaust direction. The plume prefab's Cone shape
            // emits along its own local +Z; the thruster's exhaust is its
            // own local -Y (out through the cone's circular placement
            // face). LookRotation in LOCAL coordinates — Vector3.down /
            // Vector3.forward are the thruster's own local axes since the
            // plume is parented to the thruster, so this stays correct
            // however the construct is rotated in world space.
            _plumeInstance.transform.localPosition = Vector3.zero;
            _plumeInstance.transform.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);

            _plumePs = _plumeInstance.GetComponent<ParticleSystem>();
            Transform shockChild = _plumeInstance.transform.Find("ShockDiamond");
            _shockPs = shockChild != null ? shockChild.GetComponent<ParticleSystem>() : null;

            // Snap initial emission state to current toggles before any
            // frame renders — without this, the prefab's default
            // rateOverTime emits a flash of particles for one frame if
            // EnginePlume is off at scene load.
            UpdateEmissionState();
        }

        void LateUpdate()
        {
            UpdateEmissionState();
        }

        void UpdateEmissionState()
        {
            if (_plumePs == null || _thruster == null) return;

            float input = _thruster.CurrentInputLevel;
            bool boostingThisFrame = _thruster.IsBoosting && VfxSettings.BoostFlare;

            // Main plume emission: gated by EnginePlume toggle. When
            // off, the rate goes to 0 so no new particles spawn;
            // existing particles fade out within their lifetime
            // (~0.22 s baseline). Boost amplification only applies
            // when EnginePlume is also on — otherwise there's no
            // plume to amplify.
            var emission = _plumePs.emission;
            emission.rateOverTime = VfxSettings.EnginePlume
                ? BasePlumeRate * input * (boostingThisFrame ? BoostRateMul : 1f)
                : 0f;

            // Main plume lifetime + colour. Only meaningful when
            // EnginePlume is on (no emission otherwise), but writing
            // them unconditionally is harmless.
            var main = _plumePs.main;
            main.startLifetime = BaseLifetime * (boostingThisFrame ? BoostLifetimeMul : 1f);
            main.startColor = boostingThisFrame ? BoostPlumeColor : BasePlumeColor;

            // Shock-diamond child: gated on BoostFlare + this
            // thruster's boost state. INDEPENDENT of EnginePlume — a
            // player who turns the main plume off while leaving
            // BoostFlare on still gets a shock-diamond pop on boost
            // (without the surrounding stream around it).
            if (_shockPs != null)
            {
                bool shockActive = boostingThisFrame && input > 0f;
                if (_shockPs.gameObject.activeSelf != shockActive)
                    _shockPs.gameObject.SetActive(shockActive);
            }
        }
    }
}
