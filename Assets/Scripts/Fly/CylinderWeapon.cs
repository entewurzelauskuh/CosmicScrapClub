using UnityEngine;

namespace CubeFly.Fly
{
    // Rocket-launcher-style weapon attached to PlacedCylinder.prefab.
    //
    // Spawn position: the hollow centre of the cylinder
    // (transform.position).
    //
    // Launch direction: out of the cylinder's open end opposite the
    // placement face. ShapeWeaponCylinder.faceNegY is the valid face,
    // so the barrel direction in local space is +Y → after rotation,
    // `transform.up` in world space.
    //
    // The rocket exits along `transform.up` for `launchExitDistance`
    // units, then re-orients to the crosshair-world-target that was
    // active *at fire time* and travels straight to it. Construct
    // orientation matters only for where the rocket exits — the
    // final target is shared with every other firing weapon.
    public class CylinderWeapon : WeaponBehavior
    {
        [Header("Cylinder-specific")]
        [Tooltip("World-space distance the rocket travels out of the cylinder's open end before redirecting toward the crosshair target.")]
        [SerializeField] float launchExitDistance = 0.5f;

        protected override void Fire(Vector3 crosshairWorldTarget)
        {
            if (projectilePrefab == null) return;

            Vector3 spawnPos = transform.position;
            Vector3 launchDir = transform.up;
            Vector3 exitPos = spawnPos + launchDir * launchExitDistance;

            GameObject go = Instantiate(projectilePrefab);
            Rocket rocket = go.GetComponent<Rocket>();
            if (rocket != null)
            {
                // Pass Construct + damage so the rocket can run self-hit
                // prevention (in both exit and seek phases) and apply the
                // weapon's damage value on hit. Snapshotted at Launch.
                rocket.Launch(spawnPos, launchDir, exitPos, crosshairWorldTarget,
                    Construct, damage);
            }
            else
            {
                go.transform.position = spawnPos;
                go.transform.rotation = Quaternion.LookRotation(launchDir);
            }
        }
    }
}
