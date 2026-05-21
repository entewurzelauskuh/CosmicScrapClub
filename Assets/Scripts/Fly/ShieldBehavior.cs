using CubeFly.Core;
using UnityEngine;

namespace CubeFly.Fly
{
    // A placed Shield generator in flight. Passive descriptor (the
    // ThrusterBehavior pattern). FlyController.BuildConstruct collects
    // every ShieldBehavior; ConstructEnergySystem sums the alive ones'
    // Draw (power consumed) and Contribution (shield points added to the
    // shared pool) each RecomputePower.
    public class ShieldBehavior : MonoBehaviour
    {
        [Tooltip("Power consumed while this shield is alive and powered.")]
        [SerializeField] float draw = 20f;
        [Tooltip("Shield points this cube adds to the construct's shared shield pool.")]
        [SerializeField] float contribution = 50f;

        public float Draw => draw;
        public float Contribution => contribution;

        public bool IsAlive
        {
            get
            {
                if (!_statsResolved)
                {
                    _stats = GetComponent<CubeStats>();
                    _statsResolved = true;
                }
                return _stats != null && _stats.healthPoints > 0f;
            }
        }

        CubeStats _stats;
        bool _statsResolved;
    }
}
