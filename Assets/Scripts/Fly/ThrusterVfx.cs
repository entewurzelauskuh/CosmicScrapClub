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
    // Boost-flare activation rule: requires
    //   1. ThrusterBehavior.IsBoosting   (i.e. this thruster is actively
    //      contributing to a boost this fixed step), AND
    //   2. VfxSettings.BoostFlare         (player has the toggle on).
    // Engine plume itself is gated on VfxSettings.EnginePlume — when
    // off, the entire prefab root is SetActive(false).
    public class ThrusterVfx : MonoBehaviour
    {
        const float BasePlumeRate     = 60f;
        const float BoostRateMul      = 1.5f;
        const float BaseLifetime      = 0.4f;
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
            // thruster's exhaust direction (-transform.up of the thruster,
            // which is where the cone's circular face points outward).
            _plumeInstance.transform.localPosition = Vector3.zero;
            _plumeInstance.transform.localRotation = Quaternion.LookRotation(-transform.up, transform.forward);

            _plumePs = _plumeInstance.GetComponent<ParticleSystem>();
            Transform shockChild = _plumeInstance.transform.Find("ShockDiamond");
            _shockPs = shockChild != null ? shockChild.GetComponent<ParticleSystem>() : null;

            VfxSettings.Changed += OnVfxSettingsChanged;
            ApplyEnabledState();
        }

        void OnDestroy()
        {
            VfxSettings.Changed -= OnVfxSettingsChanged;
        }

        void LateUpdate()
        {
            if (_plumePs == null || _thruster == null) return;

            float input = _thruster.CurrentInputLevel;
            bool boostingThisFrame = _thruster.IsBoosting && VfxSettings.BoostFlare;

            // Emission rate scales linearly with input. Boost amplifies.
            var emission = _plumePs.emission;
            emission.rateOverTime = BasePlumeRate * input * (boostingThisFrame ? BoostRateMul : 1f);

            // Lifetime amplifies on boost (longer tail). Colour shifts
            // hotter on boost.
            var main = _plumePs.main;
            main.startLifetime = BaseLifetime * (boostingThisFrame ? BoostLifetimeMul : 1f);
            main.startColor = boostingThisFrame ? BoostPlumeColor : BasePlumeColor;

            // Shock-diamond child active only while boost is engaged on
            // this thruster.
            if (_shockPs != null)
            {
                bool shockActive = boostingThisFrame && input > 0f;
                if (_shockPs.gameObject.activeSelf != shockActive)
                    _shockPs.gameObject.SetActive(shockActive);
            }
        }

        void OnVfxSettingsChanged() => ApplyEnabledState();

        void ApplyEnabledState()
        {
            if (_plumeInstance == null) return;
            bool on = VfxSettings.EnginePlume;
            if (_plumeInstance.activeSelf != on)
                _plumeInstance.SetActive(on);
        }
    }
}
