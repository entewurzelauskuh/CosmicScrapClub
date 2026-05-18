# Boost Overboost Tuning — Design Spec

**Status:** Approved design, ready for implementation planning
**Date:** 2026-05-18
**Prerequisite:** PR #30 (Thruster boost mechanic) merged to `main`

## Overview

Three gameplay-tuning adjustments to the Thruster boost mechanic shipped in
PR #30, all surfaced by play-testing:

1. **Critical-zone bar visual (1a)** — when the Boost meter sits in its bottom
   10%, the HUD bar turns red and gently throbs (brightness + size).
2. **Regen partition (1b)** — shorten the overboost penalty: after bottoming
   out, the player regains boost at the 10%-full mark instead of waiting for a
   full 0 → 100% recharge.
3. **Flash timing (2)** — slow the "Overboosted!" message flash by 1.5×.

All three are small, isolated tweaks to two existing files. No new systems.

## Background — boost mechanic as of PR #30

`FlyController` owns a 0–100 Boost resource:

- `_boost` (current value), `boostMax` (100). Starts each Fly session full.
- Drains at `boostDrainPerSecond` (40/s) while actively boosting; otherwise
  regenerates.
- `_overboosted` latch: set true when `_boost` reaches 0; cleared when `_boost`
  refills to `boostMax`. While latched, `EvaluateBoostAxes` returns no boost
  (boosting disabled) and regeneration uses the slow
  `boostRegenOverboostedPerSecond` (6/s) instead of `boostRegenPerSecond` (15/s).
- `BoostFraction` (0–1) and `IsOverboosted` are public, read by the HUD.

`FlyBoostBar` is the HUD element — a vertical bar left of the crosshair: fill
height = `BoostFraction`, alpha = `1 − BoostFraction`, blue `fillColor`. On the
`IsOverboosted` false → true edge it flashes an "Overboosted!" label `flashCount`
(3) times — `flashOnSeconds` (0.18 s) visible, `flashOffSeconds` (0.12 s) hidden
per cycle.

Play-test findings: the slow 0 → 100% recharge before boosting is allowed again
feels too punishing; the "Overboosted!" flash is too fast to read comfortably; a
clearer "running low" cue is wanted.

## Design

### Shared partition point

One new serialized field on `FlyController` underpins both 1a and 1b:

- `criticalBoostFraction` (float, default `0.10`) — the meter fraction dividing
  the "critical" bottom band from the normal band. It serves as both the
  overboost-recovery threshold (1b) and the red-zone test (1a). The requested
  "partition the bar into 0–90% and 90–100% usage" is exactly this single point
  at 10% fill.

### 1b — Regen partition

Change the `_overboosted` latch so it clears at the partition point rather than
at full:

- In `TickBoostResource()`, the latch-clear condition changes from
  `_boost >= boostMax` to `_boost >= boostMax * criticalBoostFraction`.
- Nothing else changes. `EvaluateBoostAxes` already disables boosting while
  `_overboosted`; the regen-rate selector already picks the slow rate while
  `_overboosted`. Both now apply only across the bottom 10%.

Resulting behavior:

- Drain the meter to empty → `_overboosted` true: slow regen (6/s), boosting
  disabled.
- The meter climbs slowly to 10% → `_overboosted` clears: boosting re-enabled,
  regen returns to the normal 15/s for the climb from 10% to full.
- The overboost penalty is now "wait out the slow 0 → 10% climb, no boosting",
  not "wait out a full slow recharge".

Unchanged: `_overboosted` is still entered only by hitting exactly 0. A player
who drains low (e.g. to 5%) but never bottoms out is never overboosted — they
keep boosting and regenerate at the normal rate.

### 1a — Critical-zone bar visual

`FlyController` exposes a new property:

- `IsBoostCritical` — `true` whenever `BoostFraction <= criticalBoostFraction`,
  i.e. the meter is in the bottom 10%. Independent of `_overboosted`, so it also
  shows when the player drains low without bottoming out.

`FlyBoostBar.Update()` gains a critical branch. When `IsBoostCritical`:

- The fill image renders in `criticalColor` (new serialized field, default a
  danger red) instead of `fillColor`.
- A single slow sine cycle drives a throb:
  - **Alpha** — the fill and frame images take a pulsing alpha oscillating
    between `criticalAlphaMin` (default 0.55) and 1.0.
  - **Size** — the bar frame's `localScale` oscillates by ±`criticalSizePulse`
    (default 0.01 → ±1%). The fill is a child of the frame, so it scales with it.
  - Cycle length = `criticalPulseSeconds` (default 1.2 s), driven by
    `Time.unscaledTime` (consistent with the flash coroutine's realtime timing).

When not critical: the existing behavior is unchanged — blue `fillColor`, alpha
`1 − fraction`, `localScale` 1.

The fill height continues to track `BoostFraction` via `sizeDelta`; the ±1%
throb is a purely cosmetic `localScale` layered on top.

### 2 — Flash timing

`FlyBoostBar.flashOnSeconds` changes from `0.18` to `0.27` (×1.5). The
"Overboosted!" label stays visible 1.5× longer per flash, slowing the 3-flash
sequence to a more legible pace. `flashOffSeconds` (0.12) and `flashCount` (3)
are unchanged. `flashOnSeconds` is an existing field, so both its code default
and its serialized value in `FlyScene.unity` must change to 0.27.

## New / changed serialized fields

`FlyController`:

- `criticalBoostFraction` — float, default 0.10

`FlyBoostBar`:

- `criticalColor` — Color, default danger red (≈ R 0.95 / G 0.25 / B 0.20 / A 1)
- `criticalPulseSeconds` — float, default 1.2
- `criticalSizePulse` — float, default 0.01
- `criticalAlphaMin` — float, default 0.55
- `flashOnSeconds` — existing field, default changes 0.18 → 0.27

The `FlyController` and `FlyBoostBar` MonoBehaviour blocks in `FlyScene.unity`
must be updated to carry the new fields at their defaults, and the existing
`flashOnSeconds` value updated to 0.27 (matching how PR #30 added the boost
fields to the scene).

## Files touched

- `Assets/Scripts/Fly/FlyController.cs` — `criticalBoostFraction` field,
  `IsBoostCritical` property, one line in `TickBoostResource`.
- `Assets/Scripts/Fly/FlyBoostBar.cs` — critical-state fields, the critical
  branch in `Update`, the `flashOnSeconds` default.
- `Assets/Scenes/FlyScene.unity` — new/changed serialized field values.

## Verification

FlyScene play-test:

- Drain boost to empty; confirm overboosted (slow regen, no boost). Confirm at
  the 10% mark the bar leaves the red state and boosting re-enables with the
  faster regen rate.
- Confirm the bottom-10% red throb appears both while overboosted and while
  merely draining low.
- Confirm the throb reads as ≈1.2 s per cycle, red, ±1% size.
- Confirm the "Overboosted!" flash is visibly slower.
- Compile clean, no console errors.

## Scope

This PR branches off `main` after PR #30 is merged (it edits boost code still in
flight in #30). It is independent of the Weapon Toolbar Death Response work — no
shared files.

**Out of scope:** the crosshair-right Heat bar (deferred with the Laser weapon);
the `boostMax = 0` degenerate config (a non-issue noted in the PR #30 audit).
