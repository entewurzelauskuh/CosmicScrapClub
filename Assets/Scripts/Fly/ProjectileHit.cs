using CubeFly.Core;
using UnityEngine;

namespace CubeFly.Fly
{
    // Shared helper for projectile hit detection + damage application.
    // Both Bullet and Rocket use the same sweep semantics — keeping the
    // implementation in one place avoids drift if the contract (self-hit
    // rules, damage formula routing, log format) changes.
    //
    // Lives as a static class on purpose: projectiles don't share state,
    // and there's nothing here that benefits from being a MonoBehaviour.
    public static class ProjectileHit
    {
        // Allocation-amortising buffer for RaycastNonAlloc. 8 is plenty —
        // a single sweep over a one-frame step typically intersects at most
        // 1-2 cubes; 8 covers the pathological "fire straight along a row
        // of cubes" case without ever GC-allocating. RaycastNonAlloc
        // doesn't guarantee distance order, so we sort the populated
        // prefix in place via the insertion sort below.
        static readonly RaycastHit[] s_HitBuffer = new RaycastHit[8];

        // Sweep from `origin` along `direction` for `distance` units. If a
        // collider on `mask` is intersected and is NOT a child of
        // `firingConstruct`, fill `hit` and return true. The closest
        // non-self hit wins; self-hits on intermediate cubes don't block
        // the projectile from hitting something farther on.
        public static bool TrySweep(Vector3 origin, Vector3 direction, float distance,
            int mask, Transform firingConstruct, out RaycastHit hit)
        {
            hit = default;
            if (distance <= 0f) return false;

            int n = Physics.RaycastNonAlloc(origin, direction, s_HitBuffer, distance, mask);
            if (n == 0) return false;

            // Insertion sort by distance — n ≤ s_HitBuffer.Length = 8, so
            // the O(n²) worst case is bounded at 64 compares; no allocation
            // overhead vs Array.Sort's IComparer machinery, and the code is
            // legible at a glance.
            for (int i = 1; i < n; i++)
            {
                RaycastHit current = s_HitBuffer[i];
                int j = i - 1;
                while (j >= 0 && s_HitBuffer[j].distance > current.distance)
                {
                    s_HitBuffer[j + 1] = s_HitBuffer[j];
                    j--;
                }
                s_HitBuffer[j + 1] = current;
            }

            for (int i = 0; i < n; i++)
            {
                if (IsSelf(s_HitBuffer[i].collider.transform, firingConstruct)) continue;
                hit = s_HitBuffer[i];
                return true;
            }
            return false;
        }

        // The firing construct's own cubes aren't valid targets. Treats a
        // destroyed weapon-cube (Unity's "fake null" reference) as not-self
        // so an in-flight projectile from a since-deleted weapon still
        // damages real targets — losing the firing cube mid-flight is rare
        // but possible.
        static bool IsSelf(Transform candidate, Transform firingConstruct)
        {
            if (firingConstruct == null || candidate == null) return false;
            return candidate == firingConstruct || candidate.IsChildOf(firingConstruct);
        }

        // Resolve the CubeStats on the hit object (or the nearest ancestor
        // that has one — covers prefabs where the collider lives on a
        // child of the stats root), apply the damage, and log the result.
        // No-ops when the hit object has no CubeStats — which shouldn't
        // happen for the layers we mask against, but is defensive.
        public static void ApplyAndLog(RaycastHit hit, float damage, string projectileTag)
        {
            CubeStats stats = hit.collider.GetComponent<CubeStats>();
            if (stats == null) stats = hit.collider.GetComponentInParent<CubeStats>();
            if (stats == null)
            {
                Debug.unityLogger.LogWarning(projectileTag,
                    $"Hit '{hit.collider.name}' but no CubeStats found — damage dropped.");
                return;
            }

            float hpBefore = stats.healthPoints;
            float applied = stats.TakeDamage(damage);
            Debug.unityLogger.Log(projectileTag,
                $"Hit '{hit.collider.name}' for {applied:F1} damage " +
                $"(raw {damage:F1}, AV {stats.armourValue:F1}). " +
                $"HP: {hpBefore:F1} → {stats.healthPoints:F1}.");
        }
    }
}
