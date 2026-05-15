using UnityEngine;

namespace CubeFly.Core
{
    // Per-cube placeholder stats. Lives on every cube prefab (alpha + A/B/C/D)
    // so each placed instance carries its own HP/AV/Mass values that gameplay
    // systems can read and mutate later (damage, destruction, mass-based
    // inertia, …). Defaults are set on each prefab and tweakable per-instance.
    public class CubeStats : MonoBehaviour
    {
        [Tooltip("Current health points. Cube is destroyed when this hits 0.")]
        public float healthPoints = 100f;

        [Tooltip("Damage absorbed before HP starts dropping. " +
                 "Effective damage = max(0, incoming - armourValue).")]
        public float armourValue = 10f;

        [Tooltip("Mass of this cube. Reserved for fly-mode inertia calculations.")]
        public float mass = 1f;

        // Apply incoming damage with armour absorbing sub-armour hits — the
        // formula stated on armourValue's tooltip and the MaterialDefinition
        // contract. HP clamps to zero. Returns the actual HP lost so callers
        // can log / feed forward to the upcoming shield + damage-type system
        // without re-deriving the math.
        //
        // Return value is the *post-clamp* delta — if a 5-damage shot lands
        // on a cube with 1 HP remaining, this returns 1, not 5. The killing
        // blow's overkill is silently absorbed.
        //
        // Used by projectile-type damage (bullets, rockets, future energy
        // weapons). Kinetic impact damage (crashes) bypasses armour via
        // TakeRawDamage below — a steel plate stops a bullet, not a 35 m/s
        // collision into a wall.
        public float TakeDamage(float incoming)
        {
            if (incoming <= 0f) return 0f;
            float effective = Mathf.Max(0f, incoming - armourValue);
            if (effective <= 0f) return 0f;
            float hpBefore = healthPoints;
            healthPoints = Mathf.Max(0f, healthPoints - effective);
            return hpBefore - healthPoints;
        }

        // Apply damage that bypasses armour entirely. Used by kinetic impact
        // sources (crash damage) where armour value isn't a meaningful
        // mitigator. Same clamp + post-clamp-delta return semantics as
        // TakeDamage.
        public float TakeRawDamage(float incoming)
        {
            if (incoming <= 0f) return 0f;
            float hpBefore = healthPoints;
            healthPoints = Mathf.Max(0f, healthPoints - incoming);
            return hpBefore - healthPoints;
        }
    }
}
