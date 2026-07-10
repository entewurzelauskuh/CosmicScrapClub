using CubeFly.Core;
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
        [Tooltip("BulletTracerMat.mat (Assets/VFX/Materials/). Wired by VfxAssetsInstaller. If null, no tracer is attached.")]
        [SerializeField] Material tracerMaterial;

        Vector3 _direction;
        float _traveled;
        bool _armed;
        TrailRenderer _trail;
        LingeringTrail _lingeringTrail;

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
            _hitLayerMask = LayerMask.GetMask("PlacedCube", "AlphaCube", "World");
            if (_hitLayerMask == 0)
            {
                int ignoreRaycast = 1 << LayerMask.NameToLayer("Ignore Raycast");
                _hitLayerMask = ~ignoreRaycast;
            }

            _armed = true;
        }

        void Awake()
        {
            // Tracer setup. Toggle-gated + null-guarded. If either fails
            // at Awake, no TrailRenderer is added — flipping the toggle
            // ON later won't retroactively add one (new bullets get
            // tracers, existing don't), which is the intended behaviour.
            //
            // The TrailRenderer lives on a dedicated CHILD GameObject so
            // OnDestroy can detach the child before Unity destroys the
            // bullet's hierarchy — the orphan child then fades naturally
            // per TrailRenderer.time and autodestructs. Hosting the
            // TrailRenderer on the bullet itself would defeat the
            // detach pattern: SetParent(null) on the root being destroyed
            // doesn't preserve it from destruction.
            if (VfxSettings.BulletTracer && tracerMaterial != null)
            {
                GameObject trailGo = new GameObject("Tracer");
                trailGo.transform.SetParent(transform, false);
                trailGo.transform.localPosition = Vector3.zero;
                trailGo.transform.localRotation = Quaternion.identity;

                _trail = trailGo.AddComponent<TrailRenderer>();
                // 0.15 s lifetime — halved from the original 0.30 after
                // play-test feedback that the longer trail felt overly
                // smeared at the bullet's 80 u/s speed.
                _trail.time = 0.15f;
                _trail.startWidth = 0.05f;
                _trail.endWidth = 0.02f;
                _trail.minVertexDistance = 0.10f;
                // sharedMaterial avoids per-bullet material instantiation
                // (TrailRenderer.material clones the asset for write-isolation,
                // allocating a new Material per projectile + leaking it on
                // GameObject destruction). Matches LaserWeapon's LineRenderer
                // pattern.
                _trail.sharedMaterial = tracerMaterial;
                _trail.emitting = true;

                Gradient grad = new Gradient();
                grad.SetKeys(
                    new[]
                    {
                        new GradientColorKey(new Color(1.00f, 1.00f, 1.00f), 0f),
                        new GradientColorKey(new Color(1.00f, 0.96f, 0.70f), 0.5f),
                        new GradientColorKey(new Color(1.00f, 0.40f, 0.75f), 1f),
                    },
                    new[]
                    {
                        new GradientAlphaKey(1.00f, 0f),
                        new GradientAlphaKey(0.85f, 0.5f),
                        new GradientAlphaKey(0.00f, 1f),
                    });
                _trail.colorGradient = grad;

                _lingeringTrail = trailGo.AddComponent<LingeringTrail>();
            }
        }

        void Update()
        {
            // Poll BulletTracer toggle each frame for live Debug-tab A/B.
            if (_trail != null) _trail.emitting = VfxSettings.BulletTracer;
            if (!_armed) return;
            float dt = Time.deltaTime;
            float step = speed * dt;

            Vector3 from = transform.position;
            if (ProjectileHit.TrySweep(from, _direction, step, _hitLayerMask, _firingConstruct,
                    out RaycastHit hit))
            {
                ProjectileHit.ApplyAndLog(hit, _damage, _firingConstruct, TAG);
                ProjectileHit.SpawnImpactVfx(hit);
                Destroy(gameObject);
                return;
            }

            transform.position = from + _direction * step;
            _traveled += step;
            if (_traveled >= maxRange) Destroy(gameObject);
        }

        void OnDestroy()
        {
            // Detach trail before Unity destroys the hierarchy so the
            // remaining trail segments fade per TrailRenderer.time instead
            // of vanishing with the bullet.
            if (_lingeringTrail != null) _lingeringTrail.DetachAndFade();
        }
    }
}
