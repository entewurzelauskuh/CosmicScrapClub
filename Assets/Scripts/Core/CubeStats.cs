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
    }
}
