using CubeFly.Core;
using UnityEngine;

namespace CubeFly.Fly
{
    // Construct-level attitude-jet visualiser. Instantiates four
    // RcsPuff.prefab one-shot emitters at the bounding-box corners of
    // the construct (+X+Z, +X-Z, -X+Z, -X-Z corners projected at the
    // construct's vertical centre). Each Update, polls
    // FlyController.CurrentAttitudeInput and fires bursts on the
    // emitters whose corners correspond to the commanded attitude axis.
    //
    // Throttle: each corner emitter has a per-axis cooldown (0.15 s)
    // so a sustained yaw input produces rhythmic pulses rather than a
    // continuous spray — sells "small jets firing in pulses" instead
    // of "main engine".
    //
    // Toggle: when VfxSettings.RcsPuff == false, the root is
    // SetActive(false) so emitters don't tick.
    //
    // No thruster-coverage exclusion in v1: attitude (pitch/yaw/roll)
    // is rotational torque, applied directly by FlyController via
    // AddTorque — thrusters don't influence rotation in this codebase.
    // RCS puffs are pure visualisation of the rotation commands.
    public class RcsPuffVfx : MonoBehaviour
    {
        const int   BurstCount     = 6;
        const float BurstCooldown  = 0.15f;
        const float InputThreshold = 0.1f;

        const string TAG = "RcsPuffVfx";

        [SerializeField] GameObject _puffPrefab;     // assigned by FlyController
        [SerializeField] FlyController _flyController;

        // Four corner emitters: 0 = +X+Z, 1 = +X-Z, 2 = -X+Z, 3 = -X-Z.
        readonly ParticleSystem[] _emitters = new ParticleSystem[4];
        readonly float[] _lastBurstTime = new float[4];

        public void SetPuffPrefab(GameObject prefab) => _puffPrefab = prefab;
        public void SetFlyController(FlyController fc) => _flyController = fc;

        void Start()
        {
            if (_puffPrefab == null || _flyController == null)
            {
                Debug.unityLogger.LogWarning(TAG, "Missing puff prefab or FlyController; disabled.");
                enabled = false;
                return;
            }

            // Compute a reasonable corner offset from the construct's
            // child colliders' aggregate bounds. Fallback to 0.5 if
            // bounds can't be resolved (e.g. alpha-cube-only construct).
            Bounds aggregate = ResolveConstructBounds();
            float halfX = Mathf.Max(0.5f, aggregate.extents.x);
            float halfZ = Mathf.Max(0.5f, aggregate.extents.z);
            Vector3[] cornerOffsets =
            {
                new Vector3(+halfX, 0f, +halfZ),
                new Vector3(+halfX, 0f, -halfZ),
                new Vector3(-halfX, 0f, +halfZ),
                new Vector3(-halfX, 0f, -halfZ),
            };

            for (int i = 0; i < 4; i++)
            {
                GameObject emitterGO = Instantiate(_puffPrefab, transform);
                emitterGO.transform.localPosition = cornerOffsets[i];
                emitterGO.transform.localRotation = Quaternion.identity;
                _emitters[i] = emitterGO.GetComponent<ParticleSystem>();
            }

            VfxSettings.Changed += OnVfxSettingsChanged;
            ApplyEnabledState();
        }

        void OnDestroy()
        {
            VfxSettings.Changed -= OnVfxSettingsChanged;
        }

        void Update()
        {
            if (!VfxSettings.RcsPuff || _flyController == null) return;

            Vector3 a = _flyController.CurrentAttitudeInput;     // (pitch, yaw, roll)
            float now = Time.time;

            // Yaw (a.y > 0 = right, < 0 = left): fire +X corners for
            // right yaw, -X corners for left yaw.
            if (Mathf.Abs(a.y) > InputThreshold)
            {
                int idx0 = a.y > 0f ? 0 : 2;     // +X+Z or -X+Z
                int idx1 = a.y > 0f ? 1 : 3;     // +X-Z or -X-Z
                TryBurst(idx0, now);
                TryBurst(idx1, now);
            }

            // Pitch (a.x > 0 = up, < 0 = down): fire +Z corners for nose-
            // up, -Z corners for nose-down.
            if (Mathf.Abs(a.x) > InputThreshold)
            {
                int idx0 = a.x > 0f ? 0 : 1;     // +X+Z or +X-Z
                int idx1 = a.x > 0f ? 2 : 3;     // -X+Z or -X-Z
                TryBurst(idx0, now);
                TryBurst(idx1, now);
            }

            // Roll (a.z != 0): fire diagonal corners.
            if (Mathf.Abs(a.z) > InputThreshold)
            {
                int idx0 = a.z > 0f ? 0 : 1;
                int idx1 = a.z > 0f ? 3 : 2;
                TryBurst(idx0, now);
                TryBurst(idx1, now);
            }
        }

        void TryBurst(int emitterIndex, float now)
        {
            if (_emitters[emitterIndex] == null) return;
            if (now - _lastBurstTime[emitterIndex] < BurstCooldown) return;
            _lastBurstTime[emitterIndex] = now;
            _emitters[emitterIndex].Emit(BurstCount);
        }

        Bounds ResolveConstructBounds()
        {
            Bounds b = new Bounds(transform.position, Vector3.zero);
            bool first = true;
            foreach (Collider c in GetComponentsInChildren<Collider>())
            {
                if (first) { b = c.bounds; first = false; }
                else        { b.Encapsulate(c.bounds); }
            }
            return b;
        }

        void OnVfxSettingsChanged() => ApplyEnabledState();

        void ApplyEnabledState()
        {
            bool on = VfxSettings.RcsPuff;
            for (int i = 0; i < 4; i++)
                if (_emitters[i] != null && _emitters[i].gameObject.activeSelf != on)
                    _emitters[i].gameObject.SetActive(on);
        }
    }
}
