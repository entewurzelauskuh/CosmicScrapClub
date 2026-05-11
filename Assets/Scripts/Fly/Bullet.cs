using UnityEngine;

namespace CubeFly.Fly
{
    // Straight-line projectile fired by PyramidWeapon. Spawn via
    // Instantiate, then call Launch(origin, direction) to arm and
    // start moving. Despawns after travelling `maxRange` world
    // units. No Rigidbody / Collider in v1 — hit detection is a
    // separate concern in the upcoming damage pass.
    public class Bullet : MonoBehaviour
    {
        [SerializeField] float speed = 80f;
        [SerializeField] float maxRange = 200f;

        Vector3 _direction;
        float _traveled;
        bool _armed;

        public void Launch(Vector3 origin, Vector3 direction)
        {
            transform.position = origin;
            // Orient the visual along travel direction. Even though a
            // sphere is rotation-invariant, later projectile types
            // (or motion-blur material tricks) will want this.
            if (direction.sqrMagnitude > 1e-8f)
                transform.rotation = Quaternion.LookRotation(direction.normalized);

            _direction = direction.normalized;
            _traveled = 0f;
            _armed = true;
        }

        void Update()
        {
            if (!_armed) return;
            float dt = Time.deltaTime;
            transform.position += _direction * (speed * dt);
            _traveled += speed * dt;
            if (_traveled >= maxRange) Destroy(gameObject);
        }
    }
}
