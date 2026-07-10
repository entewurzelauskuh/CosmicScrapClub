# Post-B3d Fixes — Design / Spec (2026-07-10)

Five items surfaced during B3d Play-testing. A mix of one **gameplay** bug (projectiles
tunnel through desert terrain) and four **hangar/HUD** UX gaps. Branch `explore/ui-rebrand`
(bundled with B3d — one push at the end; `main` stays gated behind PR #56).

**Conventions unchanged:** legacy `UnityEngine.UI` (no TMP); colours from `CscPalette`/`CscTheme`;
`new Color(r,g,b,a)` sRGB value/255; UnityMCP compile-verify + maintainer Play-verify per item.

**Decisions (user, 2026-07-10):** (a) fold weapon draw into the shown Power number · (b) fix
all three projectiles · (c) grey-out + "NO PWR" tag · (d) small top/bottom margins · (e) red "X".

---

## Item B — Projectiles tunnel through desert terrain *(gameplay; do first)*

**Problem.** The laser beam (and bullets & rockets) pass straight through the desert rock
formations. Root cause: every projectile masks to `PlacedCube|AlphaCube` and omits the desert
**`World`** layer (index 9). The formations are on `World` with MeshColliders and **no
`CubeStats`** → correctly non-breakable terrain.

**Current code.**
- `LaserWeapon.cs:98` · `Bullet.cs:65` · `Rocket.cs:174` — `_hitLayerMask = LayerMask.GetMask("PlacedCube", "AlphaCube");`
- Laser beam already ends at the hit point: `LaserWeapon.cs:112` `end = didHit ? hit.point : origin + dir*range;`
- Damage routes through `ProjectileHit.ApplyAndLog` (`ProjectileHit.cs:87-121`), which **logs a
  warning + drops damage** when the hit has no `CubeStats` (`:95-99`).

**Fix.**
1. Add `"World"` to all three masks: `LayerMask.GetMask("PlacedCube", "AlphaCube", "World")`.
   (Keeps the `== 0` fallback below each — unchanged.)
2. `ProjectileHit.ApplyAndLog` — the no-`CubeStats` branch must stop warning for **World-layer**
   hits (now expected, not a bug), while still warning for a cube-layer object that's missing its
   stats (a genuine misconfig):
   ```csharp
   CubeStats stats = hit.collider.GetComponentInParent<CubeStats>();
   if (stats == null)
   {
       // World-layer terrain legitimately has no CubeStats (non-breakable);
       // only a cube-layer object missing its stats is a real misconfiguration.
       if (hit.collider.gameObject.layer != LayerMask.NameToLayer("World"))
           Debug.unityLogger.LogWarning(projectileTag,
               $"Hit '{hit.collider.name}' but no CubeStats found — damage dropped.");
       return;
   }
   ```

**Resulting behaviour (all confirmed against the code):**
- **Laser** stops at the rock (beam draws to `hit.point`), deals no damage to non-breakable
  terrain, and — because the tick loop breaks when `hitStats == null` (`LaserWeapon.cs:137`) — makes
  at most one silent no-op `ApplyAndLog` call per frame → no console spam. Still eats through
  breakable enemy cubes tick-by-tick, unchanged.
- **Bullet / Rocket** stop at the rock: `ApplyAndLog` silently no-ops (terrain), `SpawnImpactVfx`
  fires an impact at the rock, projectile `Destroy`s. Rocket is a direct-hit projectile (no AoE),
  so nothing else changes.

**Note:** this fixes bullets/rockets too (same latent bug the user hadn't hit yet).

---

## Item A — Hangar shows no power warning for weapon load

**Problem.** The build toolbar **does** have a green/red `Power: ±N` readout
(`BuildToolbarController.cs:965-973`), but (1) it's hidden unless the ship has a reactor **or**
shield, and (2) it **ignores laser `powerDraw` entirely** — so a laser-only ship shows nothing and
"can't shoot in flight" is invisible in the hangar.

**Current code.** `BuildManager.ComputeCurrentNetPower(out bool hasPowerCubes)`
(`BuildManager.cs:327-342`) sums `ReactorBehavior.Output` and `ShieldBehavior.Draw` over the
`_spawned` instances; `hasPowerCubes` = any reactor/shield present. Label built disabled
(`BuildToolbarController.cs:484-491`), refreshed on `ConstructChanged` (`:965-973`), colour
`PowerPositive`/`PowerNegative` by sign.

**Fix.**
1. `ComputeCurrentNetPower` — fold laser draw into the same `draw` accumulator and count lasers as
   power cubes (BuildManager already references `ReactorBehavior`/`ShieldBehavior`, i.e. it already
   sees `CubeFly.Fly`; `LaserWeapon.PowerDraw` is public at `LaserWeapon.cs:33`):
   ```csharp
   LaserWeapon l = kv.Value.GetComponent<LaserWeapon>();
   if (l != null) { draw += l.PowerDraw; any = true; }
   ```
   Now a laser-only ship → `net = 0 − draw < 0` → red; the readout appears for any power-relevant
   cube. *(Deliberately diverges from flight `ConstructEnergySystem.NetPower`, which excludes weapon
   draw — the hangar reading is the pessimistic "can everything run at once?" budget check the user
   asked for.)*
2. **New `Assets/Scripts/Core/UIPulse.cs`** — a tiny reusable gentle sine **alpha** pulse on a
   `Graphic` while `enabled` (unscaled time; restores base alpha on disable). ~30 lines.
3. `BuildToolbarController` — grab a `UIPulse` ref when building `_powerLabel`; in
   `RefreshStatLabels` set `_powerPulse.enabled = hasPower && net < 0f` (pulse only on deficit). The
   `.color` assignment already flips green→red; the pulse rides on top.

---

## Item C — Fly weapon bar: no "can't fire (no energy)" indicator

**Problem.** An alive laser with no reactor looks identical to a working weapon. The dead state is
40% dim + red ✕; there's no distinct "alive but unpowered" state.

**Current code.** `FlyWeaponToolbarController.RefreshWeaponStates` (`:284-332`) knows only
dead/selected. The starvation condition already exists inline in the shooting controller:
`available = _energy.AvailableForWeapons; budget = floor(available/drawPer)` → 0 means can't fire
(`FlyShootingController.cs:215-217`). `_energy` is private; `ConstructEnergySystem.AvailableForWeapons`
is public (`:96-97`).

**Fix.**
1. `FlyShootingController` — expose the exact gate:
   ```csharp
   // True when this type is a laser with living instances that can't afford even
   // one shot right now (spare weapon power < per-cube draw). Non-lasers never starve.
   public bool GroupEnergyStarved(int index)
   {
       if (index < 0 || index >= _types.Count) return false;
       WeaponTypeGroup t = _types[index];
       if (!t.IsLaser || t.IsFullyDead) return false;
       float draw = t.LaserPowerDraw;
       if (draw <= 0f) return false;
       float available = _energy != null ? _energy.AvailableForWeapons : 0f;
       return available < draw;
   }
   ```
   *(`AvailableForWeapons` is the steady reactor-minus-shield capacity — constant regardless of
   firing — so a laser without enough reactor reads starved continuously, exactly the user's case.
   An unpowered laser never heats, so this never collides with the overheat lockout.)*
2. `FlyWeaponToolbarController` — add `Text[] _noPowerTags` (built per slot like `_deathMarks`,
   allocated/null-reset alongside them): a small amber (`CscPalette.Eject`/`HeatCool`) **"NO PWR"**
   `Text`, centred just below the glyph, `raycastTarget=false`, disabled by default. In
   `RefreshWeaponStates`, after the dead/selected block, for a **non-dead** slot:
   ```csharp
   bool starved = shootingController.GroupEnergyStarved(i);
   _noPowerTags[i].enabled = starved;
   if (_canvasGroups[i] != null && !fullyDead)
       _canvasGroups[i].alpha = starved ? 0.55f : 1f;   // 0.55 starved vs 0.40 dead
   ```
   (Dead keeps priority: the `fullyDead ? 0.4f : 1f` line stays; the starved dim only applies when
   not dead. Tag is a child of the slot so it dims with it — still legible at 0.55.)

---

## Item D — Build flyout entry text hugs top & bottom

**Problem.** In a category flyout (e.g. Utilities → "Shield"), the title and the `HP · AV · M`
stat line sit flush against the entry's top and bottom edges.

**Current code (contradicts "two texts").** The entry is a **single** 2-line rich-text label —
`"{title}\n<size=…>{statLine}</size>"` — built by `UIStyle.BuildLabeledButton`
(`CategoryFlyout.cs:199-203`), `MiddleLeft`, filling the entry with zero vertical padding
(`UIStyle.cs:259-262`). Entry height = `_flyoutEntrySize.y` = 45 (`BuildToolbarController.cs:70`).

**Fix (split into two anchored texts — matches the requested top/bottom margins).** In
`CategoryFlyout.BuildFlyout`, after `BuildLabeledButton`, repurpose the returned label as the
**title** (top-anchored, `~6px` top margin) and add a second **stat** `Text` (bottom-anchored,
`~6px` bottom margin), both honouring the existing `40px` left inset when a glyph is present:
- title: `anchorMin(0,1) anchorMax(1,1) pivot(0.5,1)`, `offsetMax.y = -6`, height ≈ fontSize+2, `UpperLeft`.
- stats: `anchorMin(0,0) anchorMax(1,0) pivot(0.5,0)`, `offsetMin.y = +6`, height ≈ fontSize-2, `LowerLeft`, `Sand100`, size `fontSize-8`.

The armour **material flyout** (inline builder in `BuildToolbarController`, same 2-line pattern)
gets the same split for consistency. Entry height stays 45 (uses the existing middle slack; no growth).

---

## Item E — Build Delete slot shows no icon

**Problem.** The Delete slot looks empty.

**Current code (contradicts "no icon").** It **does** build a centred red ✕
(`BuildToolbarController.cs:449-461`) — but the `Text` uses `CscTheme.CondOr` (Saira Condensed),
which lacks the ✕ glyph (U+2715), so it renders blank. The fly death-mark ✕ renders because it
goes through `UIStyle.BuildLabel` (a font that has the glyph).

**Fix.** Change the delete glyph `Text` to the plain ASCII letter **`"X"`** (present in every
font — and literally what the user asked). Keep it `CscPalette.Critical`, bold, size 34, centred.
One-line change at `BuildToolbarController.cs:461` (`xT.text = "✕";` → `"X"`); no font change needed.

---

## Execution order & verification

Group by concern; UnityMCP compile-verify (`refresh_unity` force/scripts → poll `editor_state` →
`read_console` filter `.cs(`) + commit **per item**; hold the push.

1. **B** — projectile World mask + terrain-silence (`LaserWeapon`, `Bullet`, `Rocket`, `ProjectileHit`).
2. **A** — build power (`BuildManager` + new `UIPulse` + `BuildToolbarController`).
3. **C** — fly no-power (`FlyShootingController` + `FlyWeaponToolbarController`).
4. **D** — flyout margins (`CategoryFlyout` + material flyout in `BuildToolbarController`).
5. **E** — delete "X" (`BuildToolbarController`).
6. **Verify + gate** — maintainer Play-check (laser stops at rocks & still kills targets; hangar
   Power red+pulse on a laser-without-reactor; fly bar "NO PWR" on the same ship; flyout margins;
   delete X). Then internal `code-reviewer` on the diff → apply → **bundle with B3d + push**.
   Retire `unity_handoff/` at the B3d gate (already pending). `main` merge stays gated (PR #56).

## Self-review
- **Placeholders:** none — every item has the exact `file:line` + code.
- **Contradictions surfaced (memory/observation vs code):** A (readout exists but reactor/shield-gated
  & weapon-blind), D (one 2-line label, not two), E (✕ built but font-missing glyph) — all reconciled.
- **Scope:** gameplay change limited to the 3 masks + 1 shared damage-log tweak; everything else is
  additive HUD/hangar. No save/scene-flow/physics changes. `UIPulse` is the only new file.
- **Risks:** (b) `NameToLayer("World")` returns −1 on a clean checkout → the `!= -1` object still
  warns (acceptable fallback). (c) `_noPowerTags` needs the same null-guards/allocation as the other
  per-slot arrays. (a) build-vs-flight NetPower semantics diverge by design (documented).
