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

        // Raised once when any non-alpha cube begins its death sequence,
        // AFTER it has detached from its construct and disabled its
        // colliders. FlyController subscribes to recompute the construct's
        // Rigidbody mass. Static so a dying cube needs no reference to its
        // listeners; subscribers MUST unsubscribe (a static event outlives
        // scene loads).
        public static event System.Action CubeDied;

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

            StartCoroutine(DriftAndDespawn(driftDir));

            // The cube is now de-parented and its colliders disabled, so
            // listeners (FlyController's mass recompute) observe the
            // construct already shrunk by this cube.
            CubeDied?.Invoke();
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
            Destroy(gameObject);
        }
    }
}
