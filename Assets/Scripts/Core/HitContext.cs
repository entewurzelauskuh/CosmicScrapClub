using UnityEngine;

namespace CubeFly.Core
{
    // Damage-source type carried by HitContext. Lets the damage
    // resolver (and the future shield resolver) make policy decisions
    // per hit without source-type sniffing.
    //
    //   Projectile — bullets, rockets, future kinetic weapons. The
    //                armour-aware path applies. Future shields take
    //                −10% from this.
    //   Energy     — lasers (phase 2). The armour-aware path applies.
    //                Future shields take +10% from this.
    //   Kinetic    — crash impacts. Bypasses armour (see
    //                HitFlags.BypassArmour) — armour mitigates
    //                penetration, not raw kinetic energy.
    public enum DamageType
    {
        Projectile,
        Energy,
        Kinetic,
    }

    // Bit-flags carried by HitContext for per-hit policy switches.
    // Composable so future variants (armour-piercing rounds, critical
    // hits, friendly-fire-allowed, …) drop in without changing the
    // call sites that already construct HitContexts.
    [System.Flags]
    public enum HitFlags
    {
        None         = 0,
        BypassArmour = 1 << 0,  // Skip armour mitigation. Used by Kinetic crash damage today.
    }

    // Per-hit metadata carried from a damage source (projectile, crash,
    // future weapon) through CubeDamage to CubeStats. The struct is
    // readonly + passed `in` to avoid copies; CubeDamage.ApplyAndLog is
    // the single consumer.
    //
    // Several fields are reserved for the Power & Energy phase (Type's
    // modifiers via shields, Impulse for knockback) and stay at sensible
    // defaults in v1.
    public readonly struct HitContext
    {
        public readonly CubeStats Target;          // Cube being hit. Null is null-guarded by ApplyAndLog.
        public readonly float Amount;              // Raw incoming damage (pre-armour / pre-shield / pre-modifier).
        public readonly DamageType Type;
        public readonly HitFlags Flags;
        public readonly Vector3 Point;             // World-space hit position. Vector3.zero when the source has no surface point.
        public readonly Vector3 Normal;            // World-space surface normal. Vector3.up fallback when no surface.
        public readonly Vector3 Impulse;           // Reserved for future knockback. Vector3.zero in v1.
        public readonly Vector3 OutwardOrigin;     // "Away from" point CubeDeath uses to direct the death drift.
        public readonly Transform SourceConstruct; // Firing construct (for self-hit filtering / future friendly-fire). May be null.
        public readonly string SourceTag;          // Log category — never null.

        public HitContext(CubeStats target, float amount, DamageType type, HitFlags flags,
            Vector3 point, Vector3 normal, Vector3 impulse, Vector3 outwardOrigin,
            Transform sourceConstruct, string sourceTag)
        {
            Target = target;
            Amount = amount;
            Type = type;
            Flags = flags;
            Point = point;
            Normal = normal;
            Impulse = impulse;
            OutwardOrigin = outwardOrigin;
            SourceConstruct = sourceConstruct;
            SourceTag = sourceTag ?? string.Empty;
        }
    }
}
