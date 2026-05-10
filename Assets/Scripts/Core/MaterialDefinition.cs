using UnityEngine;

namespace CubeFly.Core
{
    // One placeable material — visual identity + gameplay stats. A
    // (shape, material) tuple uniquely defines a placeable. Materials
    // are decoupled from shapes so the toolbar can offer any material
    // for any shape without an N×M button explosion.
    [CreateAssetMenu(fileName = "Material", menuName = "CubeFly/Material")]
    public class MaterialDefinition : ScriptableObject
    {
        [Tooltip("Short display name shown in the material flyout. Typical: 'A', 'B', 'C', …")]
        public string displayName = "A";

        [Tooltip("URP/Lit material applied to the spawned cube's renderer at instantiation.")]
        public Material material;

        [Header("Gameplay stats")]
        public float healthPoints = 100f;
        public float armourValue  = 10f;
        public float mass         = 1f;

        // Apply both visual material and gameplay stats to a freshly
        // spawned shape prefab. Called from BuildManager and
        // FlyController spawn paths so the (shape, material) tuple is
        // realised consistently in both scenes.
        public void ApplyTo(GameObject placed)
        {
            if (placed == null) return;

            if (material != null)
            {
                Renderer[] rends = placed.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rends.Length; i++)
                {
                    rends[i].sharedMaterial = material;
                }
            }

            CubeStats stats = placed.GetComponent<CubeStats>();
            if (stats != null)
            {
                stats.healthPoints = healthPoints;
                stats.armourValue  = armourValue;
                stats.mass         = mass;
            }
        }

        // Swatch colour used by the toolbar's corner badges and the
        // build-scene "Selected" stat readout. Falls back to grey if
        // the material is missing.
        public Color SwatchColor => material != null ? material.color : new Color(0.6f, 0.6f, 0.6f, 1f);
    }
}
