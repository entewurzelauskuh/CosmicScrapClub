using System.Collections.Generic;
using CubeFly.Build;
using CubeFly.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CubeFly.Fly
{
    // Construct-wide power + shield system. One per construct, on the
    // CubeConstruct GameObject (sibling to FlyController). FlyController
    // collects ReactorBehavior + ShieldBehavior instances (plus LaserWeapon
    // instances, for the eject check) during BuildConstruct and hands them
    // over via RegisterCubes; this system owns the instantaneous net-rate
    // power balance, the single shared shield pool, regen, and the
    // consumer-priority cascade.
    //
    // Power model: NetPower (the player-facing demand balance) =
    // Σ(alive reactor Output) − Σ(alive shield Draw). The shield is a
    // single all-or-nothing consumer that claims power first (so the
    // laser, a later lower-priority consumer, is what gets cut first
    // under contention): powered iff total output >= total shield draw.
    //
    // Damage interception: CubeDamage.ApplyAndLog resolves this system via
    // GetComponentInParent on the hit cube and calls ApplyToShield, which
    // absorbs against the pool (the type modifier scales the pool COST of
    // what's absorbed, not the damage) before the RAW overflow reaches HP.
    public class ConstructEnergySystem : MonoBehaviour
    {
        [Header("Shield regen")]
        [Tooltip("Shield points regenerated per second once the regen delay has elapsed.")]
        [SerializeField] float regenRate = 20f;
        [Tooltip("Seconds without taking damage before the shield starts regenerating.")]
        [SerializeField] float regenDelaySeconds = 5f;

        [Header("Shield damage-type modifiers")]
        [Tooltip("Pool COST per point of raw PROJECTILE damage absorbed (NOT a damage multiplier). <1 = cheap to soak, so the shield resists projectiles and lasts longer. The pool covers ShieldPoints / this much raw damage; any overflow reaches HP as raw. See ApplyToShield.")]
        [SerializeField] float projectileModifier = 0.9f;
        [Tooltip("Pool COST per point of raw ENERGY damage absorbed (NOT a damage multiplier). >1 = each point drains more pool, so the shield is weak to energy. The pool covers ShieldPoints / this much raw damage; any overflow reaches HP as raw. See ApplyToShield.")]
        [SerializeField] float energyModifier = 1.1f;
        // Kinetic (crash) damage always bypasses the shield entirely — no
        // modifier, no absorption. A shield stops projectiles / energy, not
        // a physical ram. See ApplyToShield.

        readonly List<ReactorBehavior> _reactors = new();
        readonly List<ShieldBehavior> _shields = new();
        // Laser cubes are weapon-tier power consumers (dynamic draw, handled
        // by FlyShootingController). Tracked here only so Eject + CanEject
        // treat a reactor-less laser as ejectable dead weight.
        readonly List<LaserWeapon> _lasers = new();

        float _shieldPoints;
        float _shieldMax;
        float _shieldDraw;
        float _totalOutput;
        int _aliveReactorCount;
        int _aliveShieldCount;
        int _aliveLaserCount;
        bool _shieldPowered;
        float _timeSinceDamage;

        const string TAG = "Energy";

        // --- HUD read-only surface ---
        public float ShieldPoints => _shieldPoints;
        public float ShieldMax => _shieldMax;
        // Player-facing build/HUD readout: reactor output − nominal shield
        // draw. Negative = under-powered. This is deliberately the steady
        // demand balance and does NOT subtract active laser draw — weapon
        // power is contended at firing time in FlyShootingController via
        // AvailableForWeapons, not folded into this readout. (AP-14 / CR-07)
        public float NetPower => _totalOutput - _shieldDraw;
        public bool ShieldActive => _shieldPowered;
        // Derived from the recomputed ALIVE cube counts (set in
        // RecomputePower, which runs on Start + every cube death) rather
        // than the registered list counts — so the HUD bar / readout
        // disappear once all shield / power cubes are destroyed, not just
        // when none were ever built. Counts rather than _totalOutput /
        // _shieldMax so a cube tuned to 0 output / 0 contribution still
        // reads as present.
        public bool HasShieldCubes => _aliveShieldCount > 0;
        public bool HasPowerCubes => _aliveReactorCount > 0 || _aliveShieldCount > 0;
        // True when the construct has lost all reactors but still carries
        // power-drawing cubes (shields and/or lasers) that can never
        // function again — dead weight. Drives the "Eject: P" HUD hint and
        // gates the P-key eject. Uses the alive REACTOR COUNT (not
        // _totalOutput) so a reactor tuned to 0 output doesn't read as
        // "no reactors left".
        public bool CanEject =>
            _aliveReactorCount == 0 && (_aliveShieldCount > 0 || _aliveLaserCount > 0);

        // Spare power left for the weapon tier after the shield's
        // higher-priority claim. A shield that is offline because it's
        // unaffordable draws nothing, so its budget is freed for the laser.
        // FlyShootingController allocates this across firing laser cubes
        // (weapons cut first under contention).
        public float AvailableForWeapons =>
            Mathf.Max(0f, _totalOutput - (_shieldPowered ? _shieldDraw : 0f));

        // Called once by FlyController.Start after BuildConstruct.
        public void RegisterCubes(IEnumerable<ReactorBehavior> reactors,
            IEnumerable<ShieldBehavior> shields, IEnumerable<LaserWeapon> lasers)
        {
            _reactors.Clear();
            _shields.Clear();
            _lasers.Clear();
            _reactors.AddRange(reactors);
            _shields.AddRange(shields);
            _lasers.AddRange(lasers);
            RecomputePower();
            // Seed the pool full so a freshly-built powered construct flies
            // in with shields up.
            _shieldPoints = _shieldPowered ? _shieldMax : 0f;
            Debug.unityLogger.Log(TAG,
                $"Registered {_reactors.Count} reactor(s), {_shields.Count} shield(s). " +
                $"Output {_totalOutput:F0}, shield draw {_shieldDraw:F0}, net {NetPower:F0}, " +
                $"shield {(_shieldPowered ? "ONLINE" : "OFFLINE")} (max {_shieldMax:F0}).");
        }

        // Recompute power balance + shield ceiling. Public so
        // FlyController.OnCubeDied can call it after the disconnect cascade
        // settles (a reactor/shield may have died or been orphaned).
        public void RecomputePower()
        {
            _totalOutput = 0f;
            _aliveReactorCount = 0;
            for (int i = 0; i < _reactors.Count; i++)
                if (_reactors[i] != null && _reactors[i].IsAlive) { _totalOutput += _reactors[i].Output; _aliveReactorCount++; }

            _shieldDraw = 0f;
            _shieldMax = 0f;
            _aliveShieldCount = 0;
            for (int i = 0; i < _shields.Count; i++)
            {
                ShieldBehavior s = _shields[i];
                if (s != null && s.IsAlive) { _shieldDraw += s.Draw; _shieldMax += s.Contribution; _aliveShieldCount++; }
            }

            _aliveLaserCount = 0;
            for (int i = 0; i < _lasers.Count; i++)
                if (_lasers[i] != null && _lasers[i].IsAlive) _aliveLaserCount++;

            // Shield is highest-priority consumer: powered iff output covers
            // its full draw.
            _shieldPowered = _shieldMax > 0f && _totalOutput >= _shieldDraw;

            if (!_shieldPowered) _shieldPoints = 0f;                    // field collapses
            else _shieldPoints = Mathf.Min(_shieldPoints, _shieldMax);  // clamp to (maybe reduced) ceiling
        }

        void Update()
        {
            _timeSinceDamage += Time.deltaTime;
            if (_shieldPowered && _shieldPoints < _shieldMax && _timeSinceDamage >= regenDelaySeconds)
                _shieldPoints = Mathf.Min(_shieldMax, _shieldPoints + regenRate * Time.deltaTime);

            // Eject: P self-destructs the now-useless power-drawing cubes
            // once all reactors are gone. Gated by CanEject + pause.
            if (CanEject && (PauseMenu.Instance == null || !PauseMenu.Instance.IsOpen))
            {
                Keyboard kb = Keyboard.current;
                if (kb != null && kb.pKey.wasPressedThisFrame) Eject();
            }
        }

        void OnValidate()
        {
            // The modifiers are a pool COST per point of raw damage absorbed
            // (see ApplyToShield), so they must stay > 0 — a 0/negative value
            // would let the shield absorb unbounded damage. Clamping the
            // serialized values keeps builds from leaning on ApplyToShield's
            // runtime Mathf.Max fallback. (AP-4, PR review)
            projectileModifier = Mathf.Max(0.01f, projectileModifier);
            energyModifier = Mathf.Max(0.01f, energyModifier);
            regenRate = Mathf.Max(0f, regenRate);
            regenDelaySeconds = Mathf.Max(0f, regenDelaySeconds);
        }

        // Called from CubeDamage.ApplyAndLog for any hit on a construct
        // cube. Resets the regen timer (the construct was hit), absorbs
        // against the pool if powered, and returns the RAW overflow that
        // should continue to HP. When the shield is down, returns the
        // amount unchanged. The type modifier scales the pool COST of what
        // is absorbed (see below), never the overflow, so the value
        // returned here is always <= amount.
        public float ApplyToShield(float amount, DamageType type)
        {
            // Kinetic (crash) damage bypasses the shield entirely — it
            // never touches the pool or the regen timer and always goes
            // straight through to HP.
            if (type == DamageType.Kinetic) return amount;

            _timeSinceDamage = 0f;
            if (!_shieldPowered || _shieldPoints <= 0f) return amount;

            // The type modifier is the POOL COST per unit of raw damage
            // absorbed — NOT a scale on the damage itself. Energy (×1.1)
            // drains the pool faster per point absorbed (shields are weak to
            // energy); projectile (×0.9) drains it slower (shields resist
            // projectiles). So the pool can cover `_shieldPoints / mod` raw
            // damage; anything beyond that spills to HP as RAW damage.
            // Applying the modifier only to the absorbed portion (never the
            // overflow) means a near-empty shield can never amplify a hit
            // past its raw amount, and there is no 0-vs-1-point
            // discontinuity. e.g. with 1 pt vs a 100 energy hit: absorbs
            // ~0.91 raw, ~99.09 reaches HP (never > 100). (AP-4)
            float mod = Mathf.Max(0.0001f, TypeModifier(type));
            float absorbedRaw = Mathf.Min(amount, _shieldPoints / mod);
            _shieldPoints = Mathf.Max(0f, _shieldPoints - absorbedRaw * mod);
            return amount - absorbedRaw;
        }

        // Only projectile + energy reach here (kinetic returns early in
        // ApplyToShield). Energy is the construct's vulnerability; anything
        // else uses the projectile modifier.
        float TypeModifier(DamageType type)
            => type == DamageType.Energy ? energyModifier : projectileModifier;

        // Self-destruct every alive power-drawing cube — shields AND lasers
        // (both are useless without a reactor). Called from the P-key poll
        // when CanEject. Then raise CubeDied once so FlyController recomputes
        // mass + power and cascades any cubes the removals orphaned.
        public void Eject()
        {
            Vector3 origin = transform.position;
            bool any = false;

            for (int i = 0; i < _shields.Count; i++)
            {
                ShieldBehavior s = _shields[i];
                if (s != null && s.IsAlive) { KillPowerCube(s.gameObject, origin); any = true; }
            }
            for (int i = 0; i < _lasers.Count; i++)
            {
                LaserWeapon l = _lasers[i];
                if (l != null && l.IsAlive) { KillPowerCube(l.gameObject, origin); any = true; }
            }

            if (!any) return;
            Debug.unityLogger.Log(TAG, "Eject — self-destructed all power-drawing cubes (no reactors remain).");
            CubeDeath.RaiseCubeDied();
        }

        // Drop a power-drawing cube from GameData, zero its HP, and start its
        // death drift. Mirrors FlyController's cascade-kill bookkeeping.
        static void KillPowerCube(GameObject cube, Vector3 origin)
        {
            PlacedCubeData placed = cube.GetComponent<PlacedCubeData>();
            if (placed != null) GameData.Remove(placed.cell);

            CubeStats stats = cube.GetComponent<CubeStats>();
            if (stats != null) stats.healthPoints = 0f;

            CubeDeath death = cube.GetComponent<CubeDeath>() ?? cube.AddComponent<CubeDeath>();
            death.BeginDeath(origin);
        }
    }
}
