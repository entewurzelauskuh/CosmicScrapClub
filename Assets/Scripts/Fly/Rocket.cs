using CubeFly.Core;
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
    // units in seek phase, or immediately on the first non-self
    // hit (in either phase) via the shared ProjectileHit helper.
    //
    // Self-hit prevention works exactly as for Bullet — the firing
    // construct's transform is passed in and any raycast hits on
    // its descendants are skipped.
    public class Rocket : MonoBehaviour
    {
        [SerializeField] float speed = 20f;
        [SerializeField] float maxRange = 200f;
        [Tooltip("RocketExhaustPlume.prefab (Assets/VFX/Prefabs/). Wired by VfxAssetsInstaller. Instantiated as a child of the rocket at Awake if VfxRocketExhaust is on.")]
        [SerializeField] GameObject exhaustPlumePrefab;
        [Tooltip("RocketSmokePuff.prefab (Assets/VFX/Prefabs/). Wired by VfxAssetsInstaller. Instantiated as a child of the rocket at Awake if VfxRocketSmokePuff is on.")]
        [SerializeField] GameObject smokePuffPrefab;
        [Tooltip("RocketSmokeTrailMat.mat (Assets/VFX/Materials/). Wired by VfxAssetsInstaller. Used by the TrailRenderer added at Awake if VfxRocketSmokeTrail is on.")]
        [SerializeField] Material smokeTrailMaterial;

        enum Phase { Exit, Seek }
        Phase _phase = Phase.Exit;
        Vector3 _launchDir;
        Vector3 _seekDir;
        Vector3 _exitWorld;
        Vector3 _target;
        float _seekTraveled;
        bool _armed;
        ParticleSystem _exhaustPlumePs;
        ParticleSystem _smokePuffPs;
        TrailRenderer _smokeTrail;
        LingeringTrail _smokeTrailLingering;

        Transform _firingConstruct;
        float _damage;
        int _hitLayerMask;

        const string TAG = "Rocket";

        void Awake()
        {
            // Exhaust plume child — instantiated only if toggle on AND
            // prefab non-null. Plume fires opposite to rocket flight
            // direction (rocket flies along its local +Y; plume cone
            // along -Y of its local frame).
            if (VfxSettings.RocketExhaust && exhaustPlumePrefab != null)
            {
                GameObject plumeGo = Instantiate(exhaustPlumePrefab, transform);
                plumeGo.transform.localPosition = Vector3.zero;
                // Cone shape emits along its local +Z by default. We want
                // emission along rocket -Y, so rotate -Z to point along -Y.
                plumeGo.transform.localRotation = Quaternion.LookRotation(Vector3.down);
                _exhaustPlumePs = plumeGo.GetComponent<ParticleSystem>();
            }

            // Smoke puff child — same orientation as plume.
            if (VfxSettings.RocketSmokePuff && smokePuffPrefab != null)
            {
                GameObject puffGo = Instantiate(smokePuffPrefab, transform);
                puffGo.transform.localPosition = Vector3.zero;
                puffGo.transform.localRotation = Quaternion.LookRotation(Vector3.down);
                _smokePuffPs = puffGo.GetComponent<ParticleSystem>();
            }

            // Smoke trail (TrailRenderer + LingeringTrail) added in code
            // so the toggle gates whether it exists at all — flipping ON
            // later doesn't retroactively add it.
            //
            // The TrailRenderer lives on a dedicated CHILD GameObject for
            // the same reason as Bullet's tracer: OnDestroy must detach
            // the child BEFORE Unity destroys the rocket's hierarchy, so
            // the orphan child can fade per TrailRenderer.time and then
            // autodestruct. Hosting the trail on the rocket itself would
            // defeat the detach (the root being destroyed cannot be
            // SetParent'd away from its own destruction).
            if (VfxSettings.RocketSmokeTrail && smokeTrailMaterial != null)
            {
                GameObject trailGo = new GameObject("SmokeTrail");
                trailGo.transform.SetParent(transform, false);
                trailGo.transform.localPosition = Vector3.zero;
                trailGo.transform.localRotation = Quaternion.identity;

                _smokeTrail = trailGo.AddComponent<TrailRenderer>();
                _smokeTrail.time = 1.0f;
                _smokeTrail.startWidth = 0.20f;
                _smokeTrail.endWidth = 0.05f;
                _smokeTrail.minVertexDistance = 0.05f;
                _smokeTrail.material = smokeTrailMaterial;
                _smokeTrail.emitting = true;

                Color trailColor = new Color(0.92f, 0.95f, 1.00f);
                Gradient grad = new Gradient();
                grad.SetKeys(
                    new[]
                    {
                        new GradientColorKey(trailColor, 0f),
                        new GradientColorKey(trailColor, 1f),
                    },
                    new[]
                    {
                        new GradientAlphaKey(0.70f, 0f),
                        new GradientAlphaKey(0.00f, 1f),
                    });
                _smokeTrail.colorGradient = grad;

                _smokeTrailLingering = trailGo.AddComponent<LingeringTrail>();
            }
        }

        // The Rocket prefab uses Unity's primitive Cylinder mesh,
        // whose long axis is local +Y. Quaternion.LookRotation aligns
        // the transform's +Z with flight direction by default, which
        // would leave the cylinder's long axis perpendicular to
        // travel (rocket appears "standing upright" along the path).
        // Multiplying by this offset rotates the mesh so its +Y ends
        // up along the transform's +Z (= flight direction), pointing
        // the rocket where it's going.
        static readonly Quaternion MeshAlignment = Quaternion.Euler(90f, 0f, 0f);

        public void Launch(Vector3 spawnPos, Vector3 launchDir,
            Vector3 exitWorld, Vector3 crosshairTarget,
            Transform firingConstruct, float damage)
        {
            transform.position = spawnPos;
            _launchDir = launchDir.normalized;
            if (_launchDir.sqrMagnitude > 1e-8f)
                transform.rotation = Quaternion.LookRotation(_launchDir) * MeshAlignment;

            _exitWorld = exitWorld;
            _target = crosshairTarget;
            _seekTraveled = 0f;
            _phase = Phase.Exit;
            _firingConstruct = firingConstruct;
            _damage = damage;

            // Same layer-mask logic as Bullet — see ProjectileHit for the
            // rationale on construct-layers + Ignore-Raycast fallback.
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
            // Poll toggles each frame for live Debug-tab A/B comparison.
            // No subscription model — these are short-lived and the read
            // cost is negligible.
            if (_exhaustPlumePs != null)
            {
                var em = _exhaustPlumePs.emission;
                em.enabled = VfxSettings.RocketExhaust;
            }
            if (_smokePuffPs != null)
            {
                var em = _smokePuffPs.emission;
                em.enabled = VfxSettings.RocketSmokePuff;
            }
            if (_smokeTrail != null) _smokeTrail.emitting = VfxSettings.RocketSmokeTrail;

            if (!_armed) return;
            float dt = Time.deltaTime;
            float step = speed * dt;

            Vector3 from = transform.position;
            Vector3 dir = _phase == Phase.Exit ? _launchDir : _seekDir;

            if (ProjectileHit.TrySweep(from, dir, step, _hitLayerMask, _firingConstruct,
                    out RaycastHit hit))
            {
                ProjectileHit.ApplyAndLog(hit, _damage, _firingConstruct, TAG);
                ProjectileHit.SpawnImpactVfx(hit);
                Destroy(gameObject);
                return;
            }

            if (_phase == Phase.Exit)
            {
                transform.position = from + _launchDir * step;
                // Switch to seek the moment we pass the exit plane —
                // dot(pos - exitWorld, launchDir) > 0 means we've gone
                // past the exit point along the launch direction.
                if (Vector3.Dot(transform.position - _exitWorld, _launchDir) > 0f)
                {
                    Vector3 toTarget = _target - transform.position;
                    if (toTarget.sqrMagnitude > 1e-8f)
                    {
                        _seekDir = toTarget.normalized;
                        transform.rotation = Quaternion.LookRotation(_seekDir) * MeshAlignment;
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
            transform.position = from + _seekDir * step;
            _seekTraveled += step;
            if (_seekTraveled >= maxRange) Destroy(gameObject);
        }

        void OnDestroy()
        {
            // Detach trail first so its lingering segments outlive the
            // rocket and fade per TrailRenderer.time, then detach child
            // ParticleSystems with stop-emitting (alive particles finish).
            if (_smokeTrailLingering != null) _smokeTrailLingering.DetachAndFade();
            DetachAndStop(_exhaustPlumePs);
            DetachAndStop(_smokePuffPs);
        }

        static void DetachAndStop(ParticleSystem ps)
        {
            if (ps == null) return;
            ps.transform.SetParent(null, true);   // worldPositionStays
            // StopEmitting (not StopEmittingAndClear) keeps already-
            // alive particles alive to finish their lifetimes; the
            // prefab's main.stopAction = Destroy then auto-cleans the
            // orphan GameObject once the last particle expires.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}
