using UnityEngine;

namespace CubeFly.Fly
{
    // Two-phase projectile fired by CylinderWeapon:
    //   • Exit phase — travel along the cylinder's "barrel"
    //     direction (the open end opposite the placement face)
    //     until it has cleared the cylinder by launchExitDistance.
    //   • Seek phase — re-orient toward the crosshair point that
    //     was locked at fire time and travel straight to it.
    //
    // The target is captured once at Launch and never re-queried,
    // so even if the ship rotates after firing, the rocket keeps
    // its locked aim. Despawns after travelling `maxRange` world
    // units in seek phase.
    public class Rocket : MonoBehaviour
    {
        [SerializeField] float speed = 20f;
        [SerializeField] float maxRange = 200f;

        enum Phase { Exit, Seek }
        Phase _phase = Phase.Exit;
        Vector3 _launchDir;
        Vector3 _seekDir;
        Vector3 _exitWorld;
        Vector3 _target;
        float _seekTraveled;
        bool _armed;

        public void Launch(Vector3 spawnPos, Vector3 launchDir,
            Vector3 exitWorld, Vector3 crosshairTarget)
        {
            transform.position = spawnPos;
            _launchDir = launchDir.normalized;
            if (_launchDir.sqrMagnitude > 1e-8f)
                transform.rotation = Quaternion.LookRotation(_launchDir);

            _exitWorld = exitWorld;
            _target = crosshairTarget;
            _seekTraveled = 0f;
            _phase = Phase.Exit;
            _armed = true;
        }

        void Update()
        {
            if (!_armed) return;
            float dt = Time.deltaTime;

            if (_phase == Phase.Exit)
            {
                transform.position += _launchDir * (speed * dt);
                // Switch to seek the moment we pass the exit plane —
                // dot(pos - exitWorld, launchDir) > 0 means we've gone
                // past the exit point along the launch direction.
                if (Vector3.Dot(transform.position - _exitWorld, _launchDir) > 0f)
                {
                    Vector3 toTarget = _target - transform.position;
                    if (toTarget.sqrMagnitude > 1e-8f)
                    {
                        _seekDir = toTarget.normalized;
                        transform.rotation = Quaternion.LookRotation(_seekDir);
                    }
                    else
                    {
                        // Target is exactly where we are — drop on the spot.
                        _seekDir = _launchDir;
                    }
                    _phase = Phase.Seek;
                }
                return;
            }

            // Seek phase — straight-line to the locked target.
            transform.position += _seekDir * (speed * dt);
            _seekTraveled += speed * dt;
            if (_seekTraveled >= maxRange) Destroy(gameObject);
        }
    }
}
