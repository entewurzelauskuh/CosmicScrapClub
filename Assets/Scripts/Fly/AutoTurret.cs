using UnityEngine;

namespace CubeFly.Fly
{
    // Standalone auto-firing turret for FlyScene world-cube props.
    //
    // Deliberately NOT a WeaponBehavior: no reload UI, no
    // FlyShootingController dispatch, no construct wiring. Every
    // fireInterval seconds it launches one Bullet straight along its
    // own local +Y (the mounted pyramid's tip direction) — a fixed
    // line of fire, with no target tracking. Kept fully self-contained
    // so the "world cubes shoot back" prop can be removed by deleting
    // this component (and its host pyramid) without touching the
    // player weapon system.
    public class AutoTurret : MonoBehaviour
    {
        [Tooltip("Bullet prefab to launch (Assets/Prefabs/Projectiles/Bullet).")]
        [SerializeField] GameObject bulletPrefab;
        [Tooltip("Seconds between shots.")]
        [SerializeField] float fireInterval = 1f;
        [Tooltip("Damage each launched bullet carries. Set well above typical cube armour so shots actually bite.")]
        [SerializeField] float damage = 40f;
        [Tooltip("Muzzle position in local space — bullets spawn here. Default is the mounted pyramid's apex.")]
        [SerializeField] Vector3 muzzleOffset = new Vector3(0f, 0.5f, 0f);

        float _timer;

        const string TAG = "AutoTurret";

        void Start()
        {
            if (bulletPrefab == null)
                Debug.unityLogger.LogWarning(TAG, $"'{name}': no bulletPrefab assigned — turret is inert.");
        }

        void Update()
        {
            if (bulletPrefab == null) return;

            _timer += Time.deltaTime;
            if (_timer < fireInterval) return;
            _timer -= fireInterval;

            // Fixed aim: straight along local +Y. Fly the construct
            // through the line of fire to take hits.
            Vector3 muzzle = transform.TransformPoint(muzzleOffset);
            Vector3 dir = transform.up;

            GameObject go = Instantiate(bulletPrefab);
            Bullet bullet = go.GetComponent<Bullet>();
            if (bullet == null)
            {
                Debug.unityLogger.LogWarning(TAG, $"'{name}': bulletPrefab has no Bullet component.");
                Destroy(go);
                return;
            }

            // firingConstruct = this turret, so the bullet skips the
            // turret's own collider; it flies outward and can still hit
            // anything else (the player construct, other world cubes).
            bullet.Launch(muzzle, dir, transform, damage);
        }
    }
}
