using CubeFly.Core;
using UnityEngine;

namespace CubeFly.Fly
{
    // Abstract base for any weapon-type construct piece in flight.
    // Owns the reload cooldown — ticking down in Update regardless
    // of whether the player has this weapon's type selected — and a
    // public TryFire(target) entry point that FlyShootingController
    // dispatches to each frame the player holds the fire button.
    //
    // Concrete subclasses (PyramidWeapon, CylinderWeapon) override
    // Fire(target) with type-specific spawn position / direction /
    // projectile logic. The dispatcher passes the same
    // crosshair-world-target value to every weapon so the on-screen
    // reticle and actual aim always agree.
    //
    // Construct and Shape references are set by FlyController.BuildConstruct
    // after instantiation. PyramidWeapon needs Construct.forward to
    // decide between "shoot at crosshair" and "shoot along tip";
    // FlyShootingController groups by Shape so the toolbar can render
    // one entry per distinct weapon type.
    public abstract class WeaponBehavior : MonoBehaviour
    {
        [Header("Common")]
        [SerializeField] protected GameObject projectilePrefab;
        [SerializeField] protected float reloadSeconds = 0.2f;

        [Header("Stats (consumed by v2 damage pass)")]
        [SerializeField] protected float damage = 1f;
        [SerializeField] protected float armorPenetration = 0f;

        public Transform Construct { get; set; }
        public ShapeDefinition Shape { get; set; }

        public float ReloadSeconds => reloadSeconds;
        public float CooldownRemaining => _cooldown;
        public bool CanFire => _cooldown <= 0f;

        // True while this weapon cube is alive (HP > 0). Polled by
        // FlyShootingController (fire gate) and FlyWeaponToolbarController
        // (button state). The sibling CubeStats is resolved once and
        // cached — the construct is rigid for a Fly session — mirroring
        // ThrusterBehavior.LocalThrustAxis's lazy cache.
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

        float _cooldown;
        CubeStats _stats;
        bool _statsResolved;

        protected virtual void Update()
        {
            if (_cooldown > 0f) _cooldown -= Time.deltaTime;
        }

        // Public entry point from FlyShootingController. The
        // crosshair-world-target is shared across all weapons firing
        // in the same frame so reticle and aim agree by construction.
        public void TryFire(Vector3 crosshairWorldTarget)
        {
            if (!CanFire) return;
            _cooldown = reloadSeconds;
            Fire(crosshairWorldTarget);
        }

        protected abstract void Fire(Vector3 crosshairWorldTarget);
    }
}
