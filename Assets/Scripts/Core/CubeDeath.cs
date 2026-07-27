using System.Collections;
using UnityEngine;

namespace CubeFly.Core
{
    // Cinematic death animation for a cube whose HP has reached zero.
    // Lazily AddComponent'd by the damage system the first time a cube
    // goes fatal — never pre-attached, so living cubes pay no idle cost.
    //
    // This component runs ONLY the visual sequence. Any data-layer
    // bookkeeping (e.g. GameData.Remove for player-construct cubes) is
    // the caller's responsibility — keeps the component agnostic about
    // where the cube came from.
    //
    // Sequence:
    //   1. Skip silently if this is the alpha cube. End-of-run owns
    //      that case; the alpha sits at HP 0 visually until that
    //      condition fires.
    //   2. Skip if already dying (defends against multiple fatal hits
    //      landing in the same frame).
    //   3. Detach from parent so the cube stops moving with the
    //      construct.
    //   4. Disable all colliders so projectiles + the construct phase
    //      through the dying cube.
    //   5. Drift along a computed direction at DriftSpeed for
    //      DriftDuration seconds, then Destroy.
    //
    // Drift direction is biased away from `outwardOrigin` (typically the
    // construct's center) so dying cubes "explode outward" rather than
    // back into the ship. Free-standing cubes (no meaningful origin)
    // get a random direction with upward bias so they don't disappear
    // straight into the floor.
    public class CubeDeath : MonoBehaviour
    {
        const float DriftSpeed = 2f;
        const float DriftDuration = 2f;
        // 0 = pure random, 1 = pure outward. 0.7 looks visibly directional
        // without being so mechanical that every shard travels the same line.
        const float OutwardBias = 0.7f;
        const string TAG = "CubeDeath";

        bool _dying;
        LingeringTrail _trailChild;

        // Raised when a genuine player-construct cube has been removed in
        // flight — dropped from GameData with its death sequence kicked
        // off. Raised via RaiseCubeDied from two call sites: CubeDamage
        // (fatal damage on a real construct cube — NOT world props or
        // turret pyramids) and ConstructEnergySystem.Eject (the player
        // self-destructing dead-weight power cubes after losing all
        // reactors). FlyController subscribes to recompute the construct's
        // Rigidbody mass + power balance. Static so a dying cube needs no
        // reference to its listeners; subscribers MUST unsubscribe (a
        // static event outlives scene loads).
        public static event System.Action CubeDied;

        // Raises CubeDied. Called once a construct cube's GameData entry is
        // removed and BeginDeath has detached it, so listeners observe the
        // construct already shrunk by the dead cube. Callers: CubeDamage
        // (damage deaths) and ConstructEnergySystem.Eject (eject).
        public static void RaiseCubeDied() => CubeDied?.Invoke();

        // Cube-death VFX, configured once per FlyScene load by
        // FlyController.Awake (mirrors ProjectileHit.ConfigureImpactPrefabs).
        // Static because CubeDeath is lazily AddComponent'd and every death
        // path shares one config. Null in unconfigured scenes (menus) →
        // no VFX, drift unchanged.
        public static GameObject BurstPrefab;
        public static Material TrailMaterial;

        public static void ConfigureVfx(GameObject burst, Material trail)
        {
            BurstPrefab = burst;
            TrailMaterial = trail;
        }

        public void BeginDeath(Vector3 outwardOrigin)
        {
            if (CompareTag("AlphaCube")) return;
            if (_dying) return;
            _dying = true;

            Vector3 driftDir = ComputeDriftDirection(outwardOrigin);

            transform.SetParent(null, worldPositionStays: true);

            foreach (Collider c in GetComponentsInChildren<Collider>(true))
                c.enabled = false;

            Debug.unityLogger.Log(TAG,
                $"'{name}' destroyed at {transform.position} (drift dir {driftDir}).");

            SpawnDeathVfx(driftDir);

            StartCoroutine(DriftAndDespawn(driftDir));
        }

        // Spawns the one-shot burst (flash + spark + debris) at the cube and
        // attaches a lingering flame/smoke trail child for the drift. Both
        // toggle- and null-guarded, so an unconfigured scene or a disabled
        // Debug toggle simply skips them. (B-3a)
        void SpawnDeathVfx(Vector3 driftDir)
        {
            if (VfxSettings.CubeDeathBurst && BurstPrefab != null)
            {
                // World-space one-shot; oriented so the debris cone (local
                // +Z) throws along the drift direction. The prefab self-
                // destroys via main.stopAction = Destroy.
                Instantiate(BurstPrefab, transform.position, Quaternion.LookRotation(driftDir));
            }

            if (VfxSettings.CubeDeathTrail && TrailMaterial != null)
            {
                // TrailRenderer on a dedicated child so it can detach and
                // fade past the cube's despawn (same pattern as Bullet's
                // tracer). The child rides the cube through the drift.
                GameObject trailGo = new GameObject("DeathTrail");
                trailGo.transform.SetParent(transform, false);
                trailGo.transform.localPosition = Vector3.zero;

                TrailRenderer trail = trailGo.AddComponent<TrailRenderer>();
                trail.time = DriftDuration;      // 2 s — matches the drift
                trail.startWidth = 0.3f;
                trail.endWidth = 0f;
                trail.minVertexDistance = 0.1f;
                trail.sharedMaterial = TrailMaterial;   // no per-cube clone
                trail.emitting = true;

                Gradient grad = new Gradient();
                grad.SetKeys(
                    new[]
                    {
                        new GradientColorKey(Color.white, 0f),
                        new GradientColorKey(new Color(0.8f, 0.8f, 0.8f), 1f),
                    },
                    new[]
                    {
                        new GradientAlphaKey(0.6f, 0f),
                        new GradientAlphaKey(0f, 1f),
                    });
                trail.colorGradient = grad;

                _trailChild = trailGo.AddComponent<LingeringTrail>();
            }
        }

        Vector3 ComputeDriftDirection(Vector3 outwardOrigin)
        {
            Vector3 toCube = transform.position - outwardOrigin;
            // sqrMagnitude check covers both "caller passed our own
            // position" and "caller defaulted to Vector3.zero on a cube
            // that happens to be near the origin" — either way, no
            // meaningful outward direction to bias toward.
            if (toCube.sqrMagnitude < 1e-6f)
            {
                Vector3 r = Random.onUnitSphere;
                r.y = Mathf.Abs(r.y); // map -Y to +Y so we don't drift into the floor
                return r.normalized;
            }

            Vector3 outward = toCube.normalized;
            Vector3 random = Random.onUnitSphere;
            return Vector3.Slerp(random, outward, OutwardBias).normalized;
        }

        IEnumerator DriftAndDespawn(Vector3 dir)
        {
            float elapsed = 0f;
            while (elapsed < DriftDuration)
            {
                transform.position += dir * (DriftSpeed * Time.deltaTime);
                elapsed += Time.deltaTime;
                yield return null;
            }
            if (_trailChild != null) _trailChild.DetachAndFade();
            Destroy(gameObject);
        }
    }
}
