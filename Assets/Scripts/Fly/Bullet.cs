using UnityEngine;

namespace CubeFly.Fly
{
    // Straight-line projectile fired by PyramidWeapon. Spawn via
    // Instantiate, then call Launch(...) to arm and start moving.
    // Despawns either when a hit is registered against a non-self
    // cube or after travelling `maxRange` world units without one.
    //
    // Hit detection is a per-frame swept raycast from the previous
    // position to the next, NOT a Unity trigger/collision. At our
    // default speed (80 u/s) the projectile moves > 1 unit per
    // 60 fps frame — wider than a cube — so trigger detection would
    // intermittently tunnel through targets. The raycast is
    // deterministic at any speed; the projectile itself doesn't need
    // a Collider or Rigidbody.
    //
    // Self-hit prevention: the firing weapon hands the projectile a
    // reference to its construct root. Any raycast hit on a child of
    // that root is skipped — the player's own cubes aren't valid
    // targets for their own weapons.
    public class Bullet : MonoBehaviour
    {
        [SerializeField] float speed = 80f;
        [SerializeField] float maxRange = 200f;

        Vector3 _direction;
        float _traveled;
        bool _armed;

        // Set at Launch by the firing WeaponBehavior — the projectile
        // never re-queries these mid-flight.
        Transform _firingConstruct;
        float _damage;
        int _hitLayerMask;

        const string TAG = "Bullet";

        public void Launch(Vector3 origin, Vector3 direction, Transform firingConstruct, float damage)
        {
            transform.position = origin;
            // Orient the visual along travel direction. Even though a
            // sphere is rotation-invariant, later projectile types
            // (or motion-blur material tricks) will want this.
            if (direction.sqrMagnitude > 1e-8f)
                transform.rotation = Quaternion.LookRotation(direction.normalized);

            _direction = direction.normalized;
            _traveled = 0f;
            _firingConstruct = firingConstruct;
            _damage = damage;

            // Limit raycasts to the construct layers — cuts useless
            // intersections against the preview ghost (PreviewCube) and
            // future world geometry on unrelated layers. Defensive fallback
            // mirrors BuildManager / CubePreview: if the named layers can't
            // be resolved (clean checkout without TagManager.asset imported),
            // hit everything except Ignore Raycast so the projectile
            // doesn't silently no-op.
            _hitLayerMask = LayerMask.GetMask("PlacedCube", "AlphaCube");
            if (_hitLayerMask == 0)
            {
                int ignoreRaycast = 1 << LayerMask.NameToLayer("Ignore Raycast");
                _hitLayerMask = ~ignoreRaycast;
            }

            _armed = true;
        }

        void Update()
        {
            if (!_armed) return;
            float dt = Time.deltaTime;
            float step = speed * dt;

            Vector3 from = transform.position;
            if (ProjectileHit.TrySweep(from, _direction, step, _hitLayerMask, _firingConstruct,
                    out RaycastHit hit))
            {
                ProjectileHit.ApplyAndLog(hit, _damage, TAG);
                Destroy(gameObject);
                return;
            }

            transform.position = from + _direction * step;
            _traveled += step;
            if (_traveled >= maxRange) Destroy(gameObject);
        }
    }
}
