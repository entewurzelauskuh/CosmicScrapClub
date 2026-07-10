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
        // Allocation-amortising buffer for RaycastNonAlloc. Sized
        // generously: RaycastNonAlloc returns an UNORDERED subset when the
        // path intersects more colliders than the buffer holds, so a
        // too-small buffer can silently drop the real target in favour of
        // nearer self-cube hits. 64 comfortably exceeds any plausible
        // construct cube count along a single one-frame sweep; TrySweep
        // logs a warning if the buffer ever fills anyway. RaycastNonAlloc
        // doesn't guarantee distance order, so we sort the populated
        // prefix in place via the insertion sort below.
        static readonly RaycastHit[] s_HitBuffer = new RaycastHit[64];

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
            if (n == s_HitBuffer.Length)
                Debug.unityLogger.LogWarning("ProjectileHit",
                    $"Sweep filled the {s_HitBuffer.Length}-hit buffer — RaycastNonAlloc may have " +
                    "dropped colliders, so a valid target could be missed. Enlarge s_HitBuffer.");

            // Insertion sort by distance — n ≤ s_HitBuffer.Length = 64, so
            // the O(n²) worst case stays small; no allocation overhead vs
            // Array.Sort's IComparer machinery, and the code is legible at
            // a glance.
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
        // child of the stats root), then route through the shared
        // CubeDamage pipeline (which handles logging, post-armour
        // application, and the maybe-die branch). No-ops when the hit
        // object has no CubeStats — which shouldn't happen for the
        // layers we mask against, but is defensive.
        public static void ApplyAndLog(RaycastHit hit, float damage,
            Transform firingConstruct, string projectileTag,
            DamageType damageType = DamageType.Projectile)
        {
            // GetComponentInParent searches the current GameObject AND walks
            // up through parents, so a single call covers both the
            // collider-on-root and collider-on-child layouts.
            CubeStats stats = hit.collider.GetComponentInParent<CubeStats>();
            if (stats == null)
            {
                // World-layer terrain legitimately has no CubeStats (non-breakable);
                // only a cube-layer object missing its stats is a real misconfiguration.
                if (hit.collider.gameObject.layer != LayerMask.NameToLayer("World"))
                    Debug.unityLogger.LogWarning(projectileTag,
                        $"Hit '{hit.collider.name}' but no CubeStats found — damage dropped.");
                return;
            }

            // Bias the death-drift away from the construct center when the
            // cube is parented to one. Free-standing world cubes fall back
            // to a random direction inside CubeDeath.
            Vector3 outwardOrigin = stats.transform.parent != null
                ? stats.transform.parent.position
                : stats.transform.position;

            HitContext context = new HitContext(
                target: stats,
                amount: damage,
                type: damageType,
                flags: HitFlags.None,
                point: hit.point,
                normal: hit.normal,
                impulse: Vector3.zero,
                outwardOrigin: outwardOrigin,
                sourceConstruct: firingConstruct,
                sourceTag: projectileTag);
            CubeDamage.ApplyAndLog(in context);
        }

        // ---------- B-2 impact VFX dispatch ----------

        // Configured once per FlyScene load by FlyController.Awake.
        // Static rather than instance because ProjectileHit is itself
        // static (no MonoBehaviour to hang [SerializeField] off), and
        // both projectile types (Bullet, Rocket) need to dispatch to
        // the same prefab references.
        public static GameObject SparkPrefab;
        public static GameObject DustPrefab;

        // Called once by FlyController.Awake before any projectile
        // can possibly spawn. Subsequent calls overwrite; safe to
        // re-call during scene transitions.
        public static void ConfigureImpactPrefabs(GameObject spark, GameObject dust)
        {
            SparkPrefab = spark;
            DustPrefab = dust;
        }

        // Spawn the appropriate impact VFX at the hit point, oriented
        // along the surface normal. Spark fires on any hit (toggle and
        // prefab permitting). Dust additionally fires when the hit
        // surface is roughly upward (matches PyramidWeapon's
        // FrontalDotThreshold = cos 45°). The two are independent
        // toggles — both, either, or neither can fire.
        //
        // Optional `scale` uniform-scales the spawned impact prefabs.
        // Bullet uses the default 1.0; Rocket passes 1.20 so its
        // warhead-sized impact reads slightly bigger than a bullet
        // puncture.
        //
        // Called from Bullet/Rocket right after ApplyAndLog, before
        // the projectile Destroys itself. Kept here (rather than in
        // ApplyAndLog) so damage and presentation stay separately
        // call-sited.
        public static void SpawnImpactVfx(in RaycastHit hit, float scale = 1.0f)
        {
            Quaternion orientation = Quaternion.LookRotation(hit.normal);

            if (VfxSettings.BulletImpactSpark && SparkPrefab != null)
            {
                GameObject go = Object.Instantiate(SparkPrefab, hit.point, orientation);
                if (scale != 1.0f) go.transform.localScale = Vector3.one * scale;
            }

            if (VfxSettings.BulletImpactDust && DustPrefab != null
                && Vector3.Dot(hit.normal, Vector3.up) > 0.7f)
            {
                GameObject go = Object.Instantiate(DustPrefab, hit.point, orientation);
                if (scale != 1.0f) go.transform.localScale = Vector3.one * scale;
            }
        }
    }
}
