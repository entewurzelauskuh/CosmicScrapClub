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
    // This component has no Update — it holds no per-frame state. It is
    // a passive descriptor; FlyController drives all the boost logic.
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
