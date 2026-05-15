using UnityEngine;

namespace CubeFly.Fly
{
    // Machine-gun-style weapon attached to PlacedPyramid.prefab.
    //
    // Spawn position: the pyramid's apex, at local (0, +0.5, 0) on
    // its 1×1×1 prefab — `transform.TransformPoint(Vector3.up * 0.5f)`.
    //
    // Aim rule:
    //   • Frontal pyramid — tip direction (`transform.up`) aligned
    //     with construct.forward (Dot > 0.7) → fire at the shared
    //     crosshair world target.
    //   • Off-axis pyramid — fire straight along its tip direction
    //     regardless of the crosshair (e.g. a backward-facing pyramid
    //     keeps shooting backward).
    //
    // The 0.7 dot threshold = cos 45°. Placements rotate in 90° steps
    // so the actual dot is exactly ±1 or 0; the threshold cleanly
    // splits "aligned" from "anything else" with no false positives.
    public class PyramidWeapon : WeaponBehavior
    {
        const float FrontalDotThreshold = 0.7f;
        static readonly Vector3 LocalTipOffset = new Vector3(0f, 0.5f, 0f);

        protected override void Fire(Vector3 crosshairWorldTarget)
        {
            if (projectilePrefab == null) return;

            Vector3 tipPos = transform.TransformPoint(LocalTipOffset);
            Vector3 tipDir = transform.up;

            Vector3 fireDir;
            if (Construct != null
                && Vector3.Dot(tipDir, Construct.forward) > FrontalDotThreshold)
            {
                // Frontal pyramid → aim at the shared crosshair point.
                Vector3 toTarget = crosshairWorldTarget - tipPos;
                fireDir = toTarget.sqrMagnitude > 1e-8f ? toTarget.normalized : tipDir;
            }
            else
            {
                fireDir = tipDir;
            }

            GameObject go = Instantiate(projectilePrefab);
            Bullet bullet = go.GetComponent<Bullet>();
            if (bullet != null)
            {
                // Pass Construct + damage so the bullet can run self-hit
                // prevention and apply the weapon's damage value on hit.
                // The bullet snapshots both at Launch and never re-queries.
                bullet.Launch(tipPos, fireDir, Construct, damage);
            }
            else
            {
                // Defensive — if the wrong prefab is wired, place the
                // GO at the tip and rely on its own Update logic if any.
                go.transform.position = tipPos;
                go.transform.rotation = Quaternion.LookRotation(fireDir);
            }
        }
    }
}
