# HitContext Refactor — Design Spec

**Status:** Approved design, ready for implementation planning
**Date:** 2026-05-20
**Branch:** `feat/hitcontext-refactor` off `main`

## Overview

Phase 1 of the Up Next timeline (`../../ROADMAP.md` → *Up Next* → item 1).
Introduces a `HitContext` value type that flows through the damage pipeline,
unifying the per-hit metadata each damage source carries today as ad-hoc
positional parameters.

This is a **pure refactor**: gameplay values do not change, the death drift
is unchanged, the self-hit filter is unchanged. The only player-visible
side-effect is a slightly richer log line that names the damage type. The
value of shipping it now is that phase 2 (Power & Energy) lands a clean
shield + damage-type-modifier layer on top of one struct instead of bolting
a third positional parameter onto every call site.

## Background — current systems

`CubeDamage.ApplyAndLog` is the shared damage entry point for both
projectile and crash damage:

```csharp
public static float ApplyAndLog(
    CubeStats stats, float incoming, Vector3 outwardOrigin,
    string sourceTag, bool ignoreArmour = false)
```

Three call sites today:

- `Assets/Scripts/Fly/ProjectileHit.cs` — invoked by `Bullet` / `Rocket`
  through `ProjectileHit.ApplyAndLog(hit, damage, projectileTag)`.
  Constructs `outwardOrigin` from `stats.transform.parent.position` (or
  self when free-standing), defaults `ignoreArmour` to `false`.
- `Assets/Scripts/Fly/FlyCrashHandler.cs` — invoked twice per collision
  (once for our cube, once for theirs) with `ignoreArmour: true` and
  `outwardOrigin = transform.position` (the construct centre).

`CubeStats` exposes two arithmetic primitives, both clamping HP at zero
and returning the actual delta: `TakeDamage(float)` applies the
`effective = max(0, raw − armourValue)` formula; `TakeRawDamage(float)`
bypasses armour.

The projectile pipeline already collects rich hit metadata
(`RaycastHit.point`, `RaycastHit.normal`, the firing construct's
`Transform`) but discards it before reaching `CubeDamage`. The crash
pipeline likewise has `ContactPoint.point` / `ContactPoint.normal` and
`Collision.impulse` but never passes them through.

## Approach — pure HitContext-based API

Replace the positional-parameter `CubeDamage.ApplyAndLog` signature with
`CubeDamage.ApplyAndLog(in HitContext)`. Each caller assembles a
`HitContext` at the hit. The damage pipeline gains a single, named-fields
shape future features can extend by adding new fields (or new `HitFlags`
bits) — without rewriting every call site.

`CubeStats.TakeDamage` / `TakeRawDamage` stay as the arithmetic primitive
layer; `CubeDamage` decides which to call based on
`HitFlags.BypassArmour`. The clean three-layer separation (sources →
`CubeDamage` policy → `CubeStats` arithmetic) is preserved.

Rejected alternatives: keeping the old signature as a thin wrapper
alongside `HitContext` (with only 3 call sites, the wrapper costs more
than it saves); pushing `HitContext` into `CubeStats` directly (conflates
arithmetic with damage-type policy — out of scope for v1).

## Design

### New file: `Assets/Scripts/Core/HitContext.cs`

One file, three cohesive types — `DamageType` enum, `HitFlags` enum, and
the `HitContext` struct. Namespace `CubeFly.Core` (matches `CubeStats`,
`GameData`, the other core data types). Lives in `Core` rather than `Fly`
because shields / enemies / AI (future phases) will all construct one.

```csharp
public enum DamageType
{
    Projectile,   // Bullets, rockets — armour-aware path.
    Energy,       // Lasers (phase 2). Future shields take +10% from this.
    Kinetic,      // Crash impacts. Armour-bypassing.
}

[System.Flags]
public enum HitFlags
{
    None         = 0,
    BypassArmour = 1 << 0,  // Crash damage today; reserved for future
                            // armour-piercing weapons.
    // Future bits: NoShieldInteraction, Critical, …
}

public readonly struct HitContext
{
    public readonly CubeStats Target;          // Cube being hit; never null at construct time.
    public readonly float Amount;              // Raw incoming damage (pre-armour / pre-shield / pre-modifier).
    public readonly DamageType Type;
    public readonly HitFlags Flags;
    public readonly Vector3 Point;             // World-space hit position (Vector3.zero when no surface — e.g. abstract sources).
    public readonly Vector3 Normal;            // World-space surface normal (Vector3.up fallback).
    public readonly Vector3 Impulse;           // Reserved for future knockback. Vector3.zero in v1.
    public readonly Vector3 OutwardOrigin;     // Death-drift "away from" point used by CubeDeath.
    public readonly Transform SourceConstruct; // Firing construct (for self-hit checks). May be null.
    public readonly string   SourceTag;        // Logging tag — never null.

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
```

`readonly struct` matches the codebase's existing pattern for value types
(`Placement` in `GameData.cs`). Passed `in` everywhere to avoid the copy.

### Modified: `Assets/Scripts/Fly/CubeDamage.cs`

Replace the existing positional `ApplyAndLog` with:

```csharp
public static float ApplyAndLog(in HitContext context)
```

Internals:

1. Resolve `stats = context.Target`. Null-guard returns `0f` (matches
   today's `if (stats == null) return 0f;`).
2. Dispatch:
   `bool bypass = (context.Flags & HitFlags.BypassArmour) != 0;`
   `applied = bypass ? stats.TakeRawDamage(context.Amount) : stats.TakeDamage(context.Amount);`
3. Log a single line — branch only on `bypass` for the armour stanza:
   - Armour-aware: `Hit '{stats.name}' for {applied:F1} damage (raw {context.Amount:F1}, type {context.Type}, AV {stats.armourValue:F1}). HP: {hpBefore:F1} → {stats.healthPoints:F1}.`
   - Bypass: `Hit '{stats.name}' for {applied:F1} damage (raw {context.Amount:F1}, type {context.Type}, armour bypassed). HP: {hpBefore:F1} → {stats.healthPoints:F1}.`
   - Log tag stays `context.SourceTag`.
4. Fatal-hit branch unchanged:
   - Alpha cube → `GameOverMenu.Instance?.TriggerGameOver()`, return `applied`.
   - Otherwise → `PlacedCubeData` lookup → `GameData.Remove(placed.cell)` →
     `CubeDeath.BeginDeath(context.OutwardOrigin)` → conditional
     `CubeDeath.RaiseCubeDied()` based on whether a real construct cube
     left the placement list.

### Modified: `Assets/Scripts/Fly/ProjectileHit.cs`

`ApplyAndLog` gains a `Transform firingConstruct` parameter (every caller
already has it). New signature:

```csharp
public static void ApplyAndLog(RaycastHit hit, float damage,
    Transform firingConstruct, string projectileTag)
```

Builds a HitContext at the hit:

- `Target` = `stats` (from the existing `GetComponentInParent<CubeStats>()` lookup).
- `Amount` = `damage`.
- `Type` = `DamageType.Projectile`.
- `Flags` = `HitFlags.None`.
- `Point` = `hit.point`.
- `Normal` = `hit.normal`.
- `Impulse` = `Vector3.zero`.
- `OutwardOrigin` = `stats.transform.parent != null ? stats.transform.parent.position : stats.transform.position` (preserves today's behaviour).
- `SourceConstruct` = `firingConstruct`.
- `SourceTag` = `projectileTag`.

Then calls `CubeDamage.ApplyAndLog(in context)`. The `if (stats == null)`
guard + its warning log stay where they are.

### Modified: `Assets/Scripts/Fly/Bullet.cs` and `Rocket.cs`

A single line each — pass `_firingConstruct` through to
`ProjectileHit.ApplyAndLog`:

```csharp
ProjectileHit.ApplyAndLog(hit, _damage, _firingConstruct, TAG);
```

### Modified: `Assets/Scripts/Fly/FlyCrashHandler.cs`

`OnCollisionEnter` builds a HitContext for each side. Shared fields:
- `Type` = `DamageType.Kinetic`.
- `Flags` = `HitFlags.BypassArmour`.
- `Point` = `contact.point`.
- `Normal` = `contact.normal`.
- `Impulse` = `Vector3.zero` (Unity's `Collision.impulse` exists but is
  reserved for future knockback work).
- `OutwardOrigin` = `transform.position` (construct centre — matches today).
- `SourceConstruct` = `null` (crash damage has no clean "firer"; both
  sides participate symmetrically).
- `SourceTag` = `"Crash"`.

Per-side `Target` and `Amount` are the only differences (both sides take
the same `damage` value, but each is a separate call with its own
`Target`).

### `CubeStats.cs`

**Unchanged.** Its two arithmetic methods (`TakeDamage(float)`,
`TakeRawDamage(float)`) keep their current contracts. `CubeDamage`
remains the policy layer that decides which to call.

## Files touched

- **Create:** `Assets/Scripts/Core/HitContext.cs` (+ `.meta` from Unity).
- **Modify:** `Assets/Scripts/Fly/CubeDamage.cs` — new signature, flag-dispatch, log update.
- **Modify:** `Assets/Scripts/Fly/ProjectileHit.cs` — `firingConstruct` parameter, HitContext construction.
- **Modify:** `Assets/Scripts/Fly/Bullet.cs` — pass `_firingConstruct` through.
- **Modify:** `Assets/Scripts/Fly/Rocket.cs` — pass `_firingConstruct` through.
- **Modify:** `Assets/Scripts/Fly/FlyCrashHandler.cs` — HitContext per side.

## Out of scope (phase 2 Power & Energy or later)

- **Shields** — `HitContext.Type` is read by the future shield resolver
  to decide projectile-vs-energy modifiers. Not in this PR.
- **Damage-type modifiers** (−10% projectile / +10% energy). Live on
  shields; the bare `HitContext` doesn't apply them.
- **Splash radius using `Point`** — future weapons / explosions.
- **Knockback using `Impulse`** — future physics work. `Collision.impulse`
  is available on the crash path but stays unwired.
- **Friendly-fire policy via `SourceConstruct`** — current behaviour is
  "no friendly fire from your own construct" via the self-filter inside
  `ProjectileHit`. That filter doesn't change here.
- **Replacing `CubeStats.TakeDamage` / `TakeRawDamage`** with a
  HitContext-aware method. Save for when shields force the rewrite of
  HP-absorption flow anyway.

## Verification

No automated tests in this project (deferred with F5). Verification is
the Unity compile-check (`refresh_unity` + `read_console`) plus a
FlyScene play-test confirming behavioural parity:

- Fire bullets / rockets at WorldTargetCubes — damage and death-drift
  visuals identical to pre-refactor. Console log line now includes the
  damage type, e.g. `Hit 'WorldTargetCube_01' for 50.0 damage (raw 50.0, type Projectile, AV 10.0). HP: …`.
- Crash the construct into a WorldTargetCube head-on — both sides take
  damage with `type Kinetic, armour bypassed` in the log.
- Self-hits still filtered: a bullet that grazes a construct cube on its
  way out doesn't damage the own construct.
- Alpha-cube death still routes to `GameOverMenu` (the policy branch in
  `CubeDamage` is unchanged in semantics; only its inputs changed shape).
- Compile clean, no `Assets/Scripts` errors / warnings outside the
  `RegistryValidator` / `MCP-FOR-UNITY` log noise.
