using CubeFly.Core;
using UnityEngine;

namespace CubeFly.Fly
{
    // A placed Thruster in flight — the boost-side analogue of
    // WeaponBehavior. FlyController.BuildConstruct collects every
    // ThrusterBehavior on the spawned construct into a list (the same
    // pattern as _spawnedWeapons / WeaponBehavior) and sets Construct
    // after instantiation.
    //
    // A thruster's exhaust points along its local +Y — the cone apex,
    // transform.up. Thrust acts in the OPPOSITE direction: -transform.up
    // (out through the circular placement face). Placements are
    // 90°-stepped, so the thrust direction expressed in the construct's
    // local frame is exactly one of the six local axes (±X / ±Y / ±Z).
    // LocalThrustAxis exposes it snapped to that — a clean unit axis
    // vector, immune to float drift, that FlyController matches against
    // _thrustInput per FixedUpdate to decide whether this thruster is
    // pushing the way the player is commanding thrust.
    //
    // This component has no Update — its flight-side behaviour is a
    // passive descriptor that FlyController reads (LocalThrustAxis) to
    // drive boost logic. Phase B-1 added per-frame VFX-side state
    // (CurrentInputLevel + IsBoosting) pushed by FlyController each
    // FixedUpdate; the sibling ThrusterVfx reads it in LateUpdate to
    // drive the plume. The state is purely for VFX — no effect on
    // flight forces or torque.
    public class ThrusterBehavior : MonoBehaviour
    {
        // Set by FlyController.BuildConstruct right after Instantiate,
        // exactly as WeaponBehavior.Construct is. Needed to express the
        // thrust direction in the construct's local frame.
        public Transform Construct { get; set; }

        // The thrust direction in the construct's LOCAL frame, snapped
        // to one of the six unit axes (±X / ±Y / ±Z). Recomputed on
        // demand from the current transforms; cached after the first
        // read because the construct is rigid (cube poses are fixed for
        // the lifetime of a Fly session).
        public Vector3 LocalThrustAxis
        {
            get
            {
                if (!_axisResolved)
                {
                    _localThrustAxis = ResolveThrustAxis();
                    _axisResolved = true;
                }
                return _localThrustAxis;
            }
        }

        Vector3 _localThrustAxis;
        bool _axisResolved;

        // VFX-side state, pushed by FlyController each FixedUpdate
        // (CurrentInputLevel = magnitude of the player's thrust input on
        // this thruster's axis, 0 if the sign mismatches; IsBoosting =
        // true when this thruster is contributing to an active boost).
        // ThrusterVfx reads these in LateUpdate to drive its plume's
        // emission rate / lifetime / colour. No effect on flight logic
        // itself — purely a data hand-off for VFX.
        float _currentInputLevel;
        bool _isBoosting;

        public float CurrentInputLevel => _currentInputLevel;
        public bool  IsBoosting        => _isBoosting;

        internal void SetInputLevel(float level) => _currentInputLevel = Mathf.Clamp01(level);
        internal void SetBoosting(bool boosting) => _isBoosting = boosting;

        // True while alive (HP > 0). Lazy-cached sibling CubeStats — same
        // pattern as ReactorBehavior / ShieldBehavior. FlyController's boost
        // and VFX loops skip dead thrusters so a destroyed-but-still-drifting
        // thruster no longer grants its boost axis or emits a plume during the
        // ~2 s death drift. The construct is rigid for a Fly session, so
        // resolving the sibling once is safe. (AP-6)
        public bool IsAlive
        {
            get
            {
                if (!_statsResolved)
                {
                    _stats = GetComponent<CubeStats>();
                    _statsResolved = true;
                }
                return _stats != null && _stats.healthPoints > 0f;
            }
        }

        CubeStats _stats;
        bool _statsResolved;

        // Convert world-space thrust direction (-transform.up) into the
        // construct's local frame, then snap each component to the
        // nearest integer in {-1, 0, +1}. With 90°-stepped placements
        // the result is exactly one signed unit axis; the snap removes
        // any floating-point fuzz so the per-axis sign comparison in
        // FlyController is exact. Falls back to the world thrust
        // direction if Construct is somehow unset.
        Vector3 ResolveThrustAxis()
        {
            Vector3 worldThrust = -transform.up;
            if (Construct == null) return worldThrust;

            Vector3 local = Construct.InverseTransformDirection(worldThrust);
            return new Vector3(
                Mathf.Round(Mathf.Clamp(local.x, -1f, 1f)),
                Mathf.Round(Mathf.Clamp(local.y, -1f, 1f)),
                Mathf.Round(Mathf.Clamp(local.z, -1f, 1f)));
        }
    }
}
