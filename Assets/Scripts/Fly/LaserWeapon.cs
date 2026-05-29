using CubeFly.Core;
using UnityEngine;

namespace CubeFly.Fly
{
    // Continuous hitscan beam weapon. Subclasses WeaponBehavior so it
    // rides the existing FlyShootingController select-and-dispatch loop,
    // but it has no projectile and no reload: reloadSeconds is 0 so the
    // dispatcher fires it every frame LMB is held; shared per-type heat
    // (owned by FlyShootingController) and per-cube power (allocated from
    // ConstructEnergySystem.AvailableForWeapons) gate it instead.
    //
    // Each fire: raycast from the barrel (transform.position along
    // transform.up — the fixed -Y-mount / +Y-barrel convention, NOT
    // crosshair-tracked), draw the LineRenderer barrel->hit (or ->max
    // range), and apply ENERGY damage in fixed ticks to the first cube
    // hit. On any frame the dispatcher does NOT fire it (released /
    // deselected / overheated / unpowered / over the power budget),
    // LateUpdate turns the beam off.
    public class LaserWeapon : WeaponBehavior
    {
        [Header("Laser")]
        [Tooltip("World-space beam range.")]
        [SerializeField] float range = 100f;
        [Tooltip("Seconds between damage ticks. Damage is applied in chunks (not per-frame) so each tick is meaningful against the subtractive-armour formula effective = max(0, raw - AV).")]
        [SerializeField] float tickInterval = 0.1f;
        [Tooltip("Power drawn while firing. FlyShootingController powers floor(AvailableForWeapons / this) of the laser cubes wanting to fire each frame.")]
        [SerializeField] float powerDraw = 5f;
        [Tooltip("Beam colour (tints the LineRenderer).")]
        [SerializeField] Color beamColor = new Color(1f, 0.3f, 0.15f, 1f);
        [SerializeField] float beamWidth = 0.06f;

        public float PowerDraw => powerDraw;

        LineRenderer _line;
        int _hitLayerMask;
        float _tickTimer;
        bool _beamedThisFrame;

        const string TAG = "Laser";

        // One beam material shared by every LaserWeapon (per-instance colour
        // comes from the LineRenderer's vertex colours, not the material, so
        // sharing is safe) — avoids allocating + leaking a Material per cube.
        // Lazy + null-guarded: Shader.Find can return null in a build if the
        // shader isn't in Always Included Shaders, so fall back down a chain.
        static Material s_beamMaterial;
        static Material BeamMaterial
        {
            get
            {
                if (s_beamMaterial == null)
                {
                    Shader sh = Shader.Find("Sprites/Default")
                                ?? Shader.Find("Universal Render Pipeline/Unlit")
                                ?? Shader.Find("Unlit/Color");
                    if (sh != null) s_beamMaterial = new Material(sh) { name = "LaserBeam (shared)" };
                }
                return s_beamMaterial;
            }
        }

        // Keep designer-tunable values sane. tickInterval is the decrement
        // step in Fire's while loop — 0 or negative would spin forever.
        void OnValidate()
        {
            tickInterval = Mathf.Max(0.01f, tickInterval);
            range = Mathf.Max(0.01f, range);          // 0 would make TrySweep a no-op (dead beam) (AP-8)
            beamWidth = Mathf.Max(0.001f, beamWidth);
            powerDraw = Mathf.Max(0.01f, powerDraw);  // 0 makes the power-gate budget int.MaxValue (gate bypass) (AP-8)
        }

        void Awake()
        {
            // Add + configure the LineRenderer at runtime so the prefab
            // doesn't have to serialize the verbose component.
            _line = GetComponent<LineRenderer>();
            if (_line == null) _line = gameObject.AddComponent<LineRenderer>();
            _line.positionCount = 2;
            _line.useWorldSpace = true;
            _line.startWidth = _line.endWidth = beamWidth;
            _line.startColor = _line.endColor = beamColor;
            // Shared unlit beam material (vertex-colour tinted by the
            // LineRenderer above). Real beam VFX is the Extended VFX pass
            // (roadmap item 4); this is the v1 placeholder.
            Material beam = BeamMaterial;
            if (beam != null) _line.sharedMaterial = beam;
            _line.enabled = false;

            // Same target layers + fallback as Bullet/Rocket.
            _hitLayerMask = LayerMask.GetMask("PlacedCube", "AlphaCube");
            if (_hitLayerMask == 0)
                _hitLayerMask = ~(1 << LayerMask.NameToLayer("Ignore Raycast"));
        }

        // Called by the dispatcher each frame this laser is selected, LMB
        // held, not overheated, and powered. crosshairWorldTarget is
        // ignored — the laser fires along its fixed barrel axis.
        protected override void Fire(Vector3 crosshairWorldTarget)
        {
            Vector3 origin = transform.position;
            Vector3 dir = transform.up;

            bool didHit = ProjectileHit.TrySweep(origin, dir, range, _hitLayerMask, Construct, out RaycastHit hit);
            Vector3 end = didHit ? hit.point : origin + dir * range;

            _line.enabled = true;
            _line.SetPosition(0, origin);
            _line.SetPosition(1, end);
            _beamedThisFrame = true;

            // Ticked damage — accumulate real time and apply a chunk each
            // interval to whatever the beam currently hits. While loop so a
            // long frame applies all due ticks (matching elapsed time).
            _tickTimer += Time.deltaTime;
            // Resolve the struck cube once (the swept hit is fixed for this
            // frame). On a long frame the loop applies several due ticks; if
            // the cube dies on one of them, stop — further ticks would just
            // spam the corpse and reset its shield regen timer instead of
            // moving on (next frame's sweep re-targets). No-hit ticks still
            // drain the timer so pointing at empty space can't accumulate a
            // burst that all lands at once on first contact. (AP-8)
            CubeStats hitStats = didHit && hit.collider != null
                ? hit.collider.GetComponentInParent<CubeStats>() : null;
            while (tickInterval > 0f && _tickTimer >= tickInterval)
            {
                _tickTimer -= tickInterval;
                if (!didHit) continue;
                ProjectileHit.ApplyAndLog(hit, damage, Construct, TAG, DamageType.Energy);
                if (hitStats == null || hitStats.healthPoints <= 0f) break;
            }
        }

        // Turn the beam off on any frame the dispatcher didn't fire us, and
        // reset the tick timer so the next burst's first tick is a full
        // interval rather than instant.
        void LateUpdate()
        {
            if (_beamedThisFrame) { _beamedThisFrame = false; return; }
            if (_line != null && _line.enabled) _line.enabled = false;
            _tickTimer = 0f;
        }
    }
}
