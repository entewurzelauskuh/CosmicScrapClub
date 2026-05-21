using CubeFly.Core;
using UnityEngine;

namespace CubeFly.Fly
{
    // A placed Reactor in flight — produces power for the construct's
    // ConstructEnergySystem. Passive descriptor (no Update), the
    // ThrusterBehavior pattern: FlyController.BuildConstruct collects every
    // ReactorBehavior into a list and hands them to the energy system,
    // which sums the alive ones' Output each RecomputePower.
    public class ReactorBehavior : MonoBehaviour
    {
        [Tooltip("Power produced while this reactor is alive. Feeds the construct's net-rate power balance.")]
        [SerializeField] float output = 10f;

        public float Output => output;

        // True while alive (HP > 0). Lazy-cached sibling CubeStats —
        // copied from WeaponBehavior.IsAlive (the construct is rigid for a
        // Fly session, so resolving once is safe).
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
