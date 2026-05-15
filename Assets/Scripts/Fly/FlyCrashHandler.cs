using CubeFly.Core;
using UnityEngine;

namespace CubeFly.Fly
{
    // Crash damage handler for the player construct. Replaces the
    // bespoke per-cube swept BoxCast in FlyCrashDetector now that the
    // construct is Rigidbody-driven and Unity's collision events fire
    // reliably.
    //
    // Lives on the same GameObject as the construct's Rigidbody
    // (CubeConstruct). OnCollisionEnter only fires for collisions
    // BETWEEN distinct rigid bodies / static colliders; internal
    // contacts within the compound collider (cube-to-cube on the same
    // ship) don't fire events, so no self-hit filter is needed.
    //
    // Impact speed uses the NORMAL component of collision.relativeVelocity
    // — Mathf.Abs(Vector3.Dot(relativeVelocity, contact.normal)) —
    // not the raw magnitude. A high-speed glancing blow shouldn't deal
    // head-on damage; only the velocity going INTO the surface matters.
    //
    // Damage is routed through CubeDamage.ApplyAndLog with
    // ignoreArmour: true (kinetic impact bypasses armour, same
    // rationale as FlyCrashDetector's behaviour before).
    //
    // Both sides of the collision take damage: the contact cube on the
    // ship AND the other side if it carries CubeStats. World target
    // cubes finally get crashed-into damage, which the swept-BoxCast
    // version didn't deliver.
    [RequireComponent(typeof(Rigidbody))]
    public class FlyCrashHandler : MonoBehaviour
    {
        [Header("Damage tuning")]
        [Tooltip("Damage = clamp(normalImpactSpeed * DamageScale, MinDamage, MaxDamage). 0.3 maps a ~37 u/s head-on crash to the cap of 10.")]
        [SerializeField] float damageScale = 0.3f;
        [SerializeField] float minDamage = 1f;
        [SerializeField] float maxDamage = 10f;
        [Tooltip("Normal-component impact speeds below this don't produce damage at all — landing gently shouldn't hurt.")]
        [SerializeField] float minSpeedForDamage = 3f;

        const string TAG = "Crash";

        void OnCollisionEnter(Collision collision)
        {
            if (collision.contactCount == 0) return;

            ContactPoint contact = collision.GetContact(0);

            // Normal-component impact speed. relativeVelocity points
            // from THEM to US in collider-pair semantics, so we take
            // the magnitude of its projection along the contact
            // normal. Glancing blows have small normal components and
            // produce little damage; head-on hits dump the full speed.
            float normalImpactSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal));
            if (normalImpactSpeed < minSpeedForDamage) return;

            float damage = Mathf.Clamp(normalImpactSpeed * damageScale, minDamage, maxDamage);

            // Death-drift origin for ANY dying cube from this crash is
            // the construct centre, so dying ship cubes (and world
            // cubes if they happen to die) drift outward away from us.
            Vector3 outwardOrigin = transform.position;

            // Our side — contact.thisCollider is one of the construct's
            // cube colliders.
            CubeStats ourCube = ResolveCubeStats(contact.thisCollider);
            if (ourCube != null)
            {
                CubeDamage.ApplyAndLog(ourCube, damage, outwardOrigin, TAG, ignoreArmour: true);
            }

            // Their side — could be a WorldTargetCube (has CubeStats)
            // or the Ground (no CubeStats; we just bounce off it).
            CubeStats theirCube = ResolveCubeStats(collision.collider);
            if (theirCube != null)
            {
                CubeDamage.ApplyAndLog(theirCube, damage, outwardOrigin, TAG, ignoreArmour: true);
            }

            Debug.unityLogger.Log(TAG,
                $"Contact: ours='{contact.thisCollider.name}', theirs='{collision.collider.name}' " +
                $"at {normalImpactSpeed:F1} u/s normal-component impact speed.");
        }

        // Covers both collider-on-root and collider-on-child layouts.
        static CubeStats ResolveCubeStats(Collider collider)
        {
            if (collider == null) return null;
            return collider.GetComponentInParent<CubeStats>();
        }
    }
}
