using UnityEngine;

namespace CubeFly.Core
{
    // One placeable-cube type. Right-click in the Project window →
    // Create → CubeFly → Cube Type to make a new one.
    [CreateAssetMenu(fileName = "CubeType", menuName = "CubeFly/Cube Type")]
    public class CubeTypeDefinition : ScriptableObject
    {
        [Tooltip("User-facing name shown on the build toolbar.")]
        public string displayName = "Cube";

        [Tooltip("Prefab spawned when the user places this cube type.")]
        public GameObject prefab;

        [Header("Default Stats (fallback)")]
        [Tooltip("Spawned cubes inherit this when the prefab's CubeStats.healthPoints is 0.")]
        public float defaultHealthPoints = 100f;

        [Tooltip("Spawned cubes inherit this when the prefab's CubeStats.armourValue is 0.")]
        public float defaultArmourValue = 10f;

        [Tooltip("Spawned cubes inherit this when the prefab's CubeStats.mass is 0.")]
        public float defaultMass = 1f;

        // Seed any zero-valued fields on the spawned cube's CubeStats with
        // this type's defaults. A zero is treated as "unset" so the prefab
        // remains the source of truth when its values are explicit.
        public void ApplyDefaultsTo(CubeStats stats)
        {
            if (stats == null) return;
            if (Mathf.Approximately(stats.healthPoints, 0f)) stats.healthPoints = defaultHealthPoints;
            if (Mathf.Approximately(stats.armourValue, 0f))  stats.armourValue  = defaultArmourValue;
            if (Mathf.Approximately(stats.mass, 0f))         stats.mass         = defaultMass;
        }

        // Resolved mass value for a placement of this type, mirroring the
        // ApplyDefaultsTo precedence: prefab CubeStats.mass when explicit,
        // otherwise the SO defaultMass. Used by mass-budget checks before
        // any GameObject is actually instantiated.
        public float EffectiveMass() => Resolve(s => s.mass, defaultMass);

        // Resolved HP/AV values, same precedence as EffectiveMass. Used by
        // the build-scene UI to show the currently-selected cube's stats
        // before any cube is actually placed.
        public float EffectiveHealthPoints() => Resolve(s => s.healthPoints, defaultHealthPoints);
        public float EffectiveArmourValue()  => Resolve(s => s.armourValue,  defaultArmourValue);

        float Resolve(System.Func<CubeStats, float> read, float fallback)
        {
            if (prefab != null)
            {
                CubeStats stats = prefab.GetComponent<CubeStats>();
                if (stats != null)
                {
                    float v = read(stats);
                    if (!Mathf.Approximately(v, 0f)) return v;
                }
            }
            return fallback;
        }
    }
}
