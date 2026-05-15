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

        // Apply incoming damage. Armour fully absorbs sub-armour hits (no chip
        // damage) — matching the formula stated on armourValue's tooltip and
        // the MaterialDefinition contract. HP clamps to zero. Returns the
        // actual HP lost so callers can log / feed forward to the upcoming
        // shield + damage-type system without re-deriving the math.
        //
        // Return value is the *post-clamp* delta — if a 5-damage shot lands
        // on a cube with 1 HP remaining, this returns 1, not 5. The killing
        // blow's overkill is silently absorbed.
        public float TakeDamage(float incoming)
        {
            if (incoming <= 0f) return 0f;
            float effective = Mathf.Max(0f, incoming - armourValue);
            if (effective <= 0f) return 0f;
            float hpBefore = healthPoints;
            healthPoints = Mathf.Max(0f, healthPoints - effective);
            return hpBefore - healthPoints;
        }
    }
}
