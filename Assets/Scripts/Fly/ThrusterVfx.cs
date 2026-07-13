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
    // The EnginePlume and BoostFlare toggles are independent, with
    // one nuance — when EnginePlume is OFF but BoostFlare is ON and
    // this thruster is contributing to a boost, the main plume emits
    // at its BASELINE rate (not amplified) so the shock-diamond has
    // a plume to sit on. That moment is the "boost cue" — the only
    // time a player who's turned the main plume off sees one.
    //
    //   • EnginePlume ON: main plume emits whenever the thruster is
    //     commanded (rate scales with input level). Boost amplifies
    //     (×rate, ×lifetime, hotter colour) only if BoostFlare is
    //     also ON.
    //   • EnginePlume OFF + BoostFlare ON + boost engaged on this
    //     thruster: main plume emits at BASELINE (not amplified) +
    //     shock-diamond visible. A bounded boost-cue burst.
    //   • EnginePlume OFF + (BoostFlare OFF or no boost): main plume
    //     silent.
    //   • Boost-flare activation rule: BoostFlare toggle on AND
    //     ThrusterBehavior.IsBoosting AND input > 0.
    //
    // The shock-diamond child gates on the same BoostFlare-engaged
    // condition as the boost-cue plume — they appear together.
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

            // Gate emission on IsAlive. A thruster killed mid-thrust keeps its
            // last CurrentInputLevel / IsBoosting values frozen (FlyController's
            // VFX loop skips dead thrusters without zeroing them), so without
            // this a stale plume would ride the detached cube through its ~2 s
            // death drift. Treating a dead thruster as zero-input flows through
            // the logic below to rateOverTime = 0 and drops the shock diamond. (CR-009)
            bool alive = _thruster.IsAlive;
            float input = alive ? _thruster.CurrentInputLevel : 0f;
            bool boostingThisFrame = alive && _thruster.IsBoosting && VfxSettings.BoostFlare;
            bool boostCueActive = boostingThisFrame && input > 0f;

            // Main plume emission rate. Three cases:
            //   1. EnginePlume ON — emits whenever input > 0. Boost
            //      amplifies (×rate) only if BoostFlare is also on.
            //   2. EnginePlume OFF + boost cue active — emits at the
            //      BASELINE rate (no ×BoostRateMul) so the shock-
            //      diamond has a plume to sit on. Bounded by the
            //      boost moment.
            //   3. EnginePlume OFF + no boost cue — silent.
            var emission = _plumePs.emission;
            if (VfxSettings.EnginePlume)
            {
                emission.rateOverTime =
                    BasePlumeRate * input * (boostingThisFrame ? BoostRateMul : 1f);
            }
            else if (boostCueActive)
            {
                emission.rateOverTime = BasePlumeRate * input;
            }
            else
            {
                emission.rateOverTime = 0f;
            }

            // Lifetime + colour amplification: only when EnginePlume
            // is ON AND boosting. In the boost-cue case (EnginePlume
            // OFF), the plume uses baseline lifetime/colour — the
            // shock-diamond communicates "boost engaged", the plume
            // itself stays neutral so a player who turned the main
            // plume off doesn't get a hot boosted plume by surprise.
            bool amplifyAppearance = VfxSettings.EnginePlume && boostingThisFrame;
            var main = _plumePs.main;
            main.startLifetime = BaseLifetime * (amplifyAppearance ? BoostLifetimeMul : 1f);
            main.startColor = amplifyAppearance ? BoostPlumeColor : BasePlumeColor;

            // Shock-diamond child: same condition as the boost cue —
            // appears together with the baseline plume in the
            // EnginePlume-OFF case, alongside the amplified plume in
            // the EnginePlume-ON case.
            if (_shockPs != null)
            {
                if (_shockPs.gameObject.activeSelf != boostCueActive)
                    _shockPs.gameObject.SetActive(boostCueActive);
            }
        }
    }
}
