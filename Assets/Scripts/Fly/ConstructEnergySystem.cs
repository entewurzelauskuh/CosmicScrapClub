using System.Collections.Generic;
using CubeFly.Core;
using UnityEngine;

namespace CubeFly.Fly
{
    // Construct-wide power + shield system. One per construct, on the
    // CubeConstruct GameObject (sibling to FlyController). FlyController
    // collects ReactorBehavior + ShieldBehavior instances during
    // BuildConstruct and hands them over via RegisterCubes; this system
    // owns the instantaneous net-rate power balance, the single shared
    // shield pool, regen, and the consumer-priority cascade.
    //
    // Power model: NetPower (the player-facing demand balance) =
    // Σ(alive reactor Output) − Σ(alive shield Draw). The shield is a
    // single all-or-nothing consumer that claims power first (so the
    // laser, a later lower-priority consumer, is what gets cut first
    // under contention): powered iff total output >= total shield draw.
    //
    // Damage interception: CubeDamage.ApplyAndLog resolves this system via
    // GetComponentInParent on the hit cube and calls ApplyToShield, which
    // absorbs against the pool (type-scaled) before the overflow reaches HP.
    public class ConstructEnergySystem : MonoBehaviour
    {
        [Header("Shield regen")]
        [Tooltip("Shield points regenerated per second once the regen delay has elapsed.")]
        [SerializeField] float regenRate = 20f;
        [Tooltip("Seconds without taking damage before the shield starts regenerating.")]
        [SerializeField] float regenDelaySeconds = 5f;

        [Header("Shield damage-type modifiers")]
        [Tooltip("Multiplier on projectile damage while the shield absorbs it. <1 = shield resists projectiles.")]
        [SerializeField] float projectileModifier = 0.9f;
        [Tooltip("Multiplier on energy damage while the shield absorbs it. >1 = shield is weak to energy.")]
        [SerializeField] float energyModifier = 1.1f;
        [Tooltip("Multiplier on kinetic (crash) damage while the shield absorbs it.")]
        [SerializeField] float kineticModifier = 1f;

        readonly List<ReactorBehavior> _reactors = new();
        readonly List<ShieldBehavior> _shields = new();

        float _shieldPoints;
        float _shieldMax;
        float _shieldDraw;
        float _totalOutput;
        bool _shieldPowered;
        float _timeSinceDamage;

        const string TAG = "Energy";

        // --- HUD read-only surface ---
        public float ShieldPoints => _shieldPoints;
        public float ShieldMax => _shieldMax;
        // Player-facing demand balance: output − total nominal shield draw
        // (later also − active laser draw). Negative = under-powered.
        public float NetPower => _totalOutput - _shieldDraw;
        public bool ShieldActive => _shieldPowered;
        // Derived from the recomputed ALIVE totals (set in RecomputePower,
        // which runs on Start + every cube death) rather than the
        // registered list counts — so the HUD bar / readout disappear once
        // all shield / power cubes are destroyed, not just when none were
        // ever built.
        public bool HasShieldCubes => _shieldMax > 0f;
        public bool HasPowerCubes => _totalOutput > 0f || _shieldMax > 0f;

        // Called once by FlyController.Start after BuildConstruct.
        public void RegisterCubes(IEnumerable<ReactorBehavior> reactors, IEnumerable<ShieldBehavior> shields)
        {
            _reactors.Clear();
            _shields.Clear();
            _reactors.AddRange(reactors);
            _shields.AddRange(shields);
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
            for (int i = 0; i < _reactors.Count; i++)
                if (_reactors[i] != null && _reactors[i].IsAlive) _totalOutput += _reactors[i].Output;

            _shieldDraw = 0f;
            _shieldMax = 0f;
            for (int i = 0; i < _shields.Count; i++)
            {
                ShieldBehavior s = _shields[i];
                if (s != null && s.IsAlive) { _shieldDraw += s.Draw; _shieldMax += s.Contribution; }
            }

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
        }

        // Called from CubeDamage.ApplyAndLog for any hit on a construct
        // cube. Resets the regen timer (the construct was hit), absorbs
        // against the pool if powered, and returns the overflow that should
        // continue to HP. When the shield is down, returns the amount
        // unchanged (full overflow, no type modifier — the modifier is a
        // shield property).
        public float ApplyToShield(float amount, DamageType type)
        {
            _timeSinceDamage = 0f;
            if (!_shieldPowered || _shieldPoints <= 0f) return amount;

            float scaled = amount * TypeModifier(type);
            float absorbed = Mathf.Min(scaled, _shieldPoints);
            _shieldPoints -= absorbed;
            return scaled - absorbed;
        }

        float TypeModifier(DamageType type)
        {
            switch (type)
            {
                case DamageType.Projectile: return projectileModifier;
                case DamageType.Energy:     return energyModifier;
                default:                    return kineticModifier; // Kinetic
            }
        }
    }
}
