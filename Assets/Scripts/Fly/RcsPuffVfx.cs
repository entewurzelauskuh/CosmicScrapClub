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
    // Throttle: each of the 4 corner emitters tracks its own
    // _lastBurstTime, sharing a single 0.15 s cooldown across all
    // three attitude axes. So pitching and yawing simultaneously can
    // suppress one emitter's burst if the other axis just fired it.
    // Sells "small jets firing in pulses" instead of "main engine".
    //
    // Toggle: when VfxSettings.RcsPuff == false, Update early-returns
    // (no new bursts), and ApplyEnabledState SetActive(false)'s each
    // corner emitter's GameObject so any in-flight particles fade
    // out naturally. The RcsPuffVfx component itself stays enabled
    // so it can re-enable the emitters when the toggle flips back on.
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

            // Instantiate four emitters as children. Their localPositions
            // get set by RecomputeEmitterPositions() — called now, and
            // again on every CubeDied event so the corners track the
            // construct's shrinking bounds as outer cubes are destroyed.
            for (int i = 0; i < 4; i++)
            {
                GameObject emitterGO = Instantiate(_puffPrefab, transform);
                emitterGO.transform.localRotation = Quaternion.identity;
                _emitters[i] = emitterGO.GetComponent<ParticleSystem>();
            }
            RecomputeEmitterPositions();

            VfxSettings.Changed += OnVfxSettingsChanged;
            CubeDeath.CubeDied  += OnAnyCubeDied;
            ApplyEnabledState();
        }

        void OnDestroy()
        {
            VfxSettings.Changed -= OnVfxSettingsChanged;
            CubeDeath.CubeDied  -= OnAnyCubeDied;
        }

        void OnAnyCubeDied() => RecomputeEmitterPositions();

        // Place the four corner emitters at the diagonal-most cubes in
        // each of the four (±X, ±Z) sectors. For each sector with
        // direction (sx, sz), pick the cube c that maximises
        // (sx*c.x + sz*c.z) — i.e. the cube whose own outer corner
        // pokes farthest in that diagonal — then put the emitter at
        // THAT cube's outer corner. This makes every emitter sit on a
        // real piece of the construct.
        //
        // Why not the bounding-box corners: for non-rectangular
        // constructs (an arrow / T / cross shape), the +X-axis extreme
        // and the -Z-axis extreme often belong to different cubes, so
        // the (-X, -Z) box corner lands in empty space between them.
        // A T-shaped ship with a left arm out to -X and a back stem
        // down to -Z would fire its back-left puffs into the void.
        //
        // Recomputed at Start and on every CubeDeath.CubeDied event so
        // the corners track the construct as outer cubes are destroyed.
        // Uses LOCAL coordinates (each cube's localPosition + 0.5
        // half-extent) so the result is invariant under construct
        // rotation in flight.
        void RecomputeEmitterPositions()
        {
            // Sector direction vectors, indexed to match the _emitters
            // array layout used in Update() (0 = +X+Z, 1 = +X-Z,
            // 2 = -X+Z, 3 = -X-Z).
            int[] dirX = { +1, +1, -1, -1 };
            int[] dirZ = { +1, -1, +1, -1 };

            float[] bestScore = { float.NegativeInfinity, float.NegativeInfinity,
                                  float.NegativeInfinity, float.NegativeInfinity };
            Vector3[] bestCorner = new Vector3[4];
            bool foundAny = false;

            foreach (Transform child in transform)
            {
                // Skip emitter prefabs we instantiated (ParticleSystem
                // at root) and dying cubes (all colliders disabled).
                if (child.GetComponent<ParticleSystem>() != null) continue;
                bool anyColliderEnabled = false;
                foreach (Collider col in child.GetComponentsInChildren<Collider>())
                {
                    if (col.enabled) { anyColliderEnabled = true; break; }
                }
                if (!anyColliderEnabled) continue;

                Vector3 p = child.localPosition;
                for (int s = 0; s < 4; s++)
                {
                    float score = dirX[s] * p.x + dirZ[s] * p.z;
                    if (score > bestScore[s])
                    {
                        bestScore[s] = score;
                        bestCorner[s] = new Vector3(
                            p.x + dirX[s] * 0.5f,
                            0f,
                            p.z + dirZ[s] * 0.5f);
                        foundAny = true;
                    }
                }
            }

            // Defensive fallback for an empty construct (shouldn't
            // happen in practice — alpha cube is always present).
            if (!foundAny)
            {
                bestCorner[0] = new Vector3(+0.5f, 0f, +0.5f);
                bestCorner[1] = new Vector3(+0.5f, 0f, -0.5f);
                bestCorner[2] = new Vector3(-0.5f, 0f, +0.5f);
                bestCorner[3] = new Vector3(-0.5f, 0f, -0.5f);
            }

            for (int i = 0; i < 4; i++)
            {
                if (_emitters[i] != null)
                    _emitters[i].transform.localPosition = bestCorner[i];
            }
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
