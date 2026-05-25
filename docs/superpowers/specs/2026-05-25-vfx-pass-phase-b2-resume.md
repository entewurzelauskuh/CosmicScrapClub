# VFX Phase B-2 — Brainstorm Resume Note

> **For the next session:** This is a hand-off note from a previous,
> long-running Cube Fly session that just exhausted its context window.
> The user has confirmed they want to continue the **Phase B-2 (weapons +
> impacts) brainstorm**, using the visual companion, from a fresh window.
>
> **Immediate next step:** announce the `superpowers:brainstorming` skill,
> mark step 2 (visual companion offer) as ✅ (already accepted by the user
> last session), then proceed to **step 3 — ask the first clarifying
> question**. The visual companion can be invoked the first time a
> question genuinely benefits from a mockup (engine plume colour, sprite
> shape, etc.). Don't re-offer it.
>
> The full brainstorming-skill checklist still applies after this resume.

---

## Where we left off

The brainstorm had just reached step 2 of the checklist. The user
accepted the visual companion offer but asked to reset the window first.
This file is the hand-off.

**Brainstorm checklist position:**
1. ✅ Project context explored (carried below — no need to re-explore).
2. ✅ Visual companion offered, **accepted**. Use it when a question
   genuinely benefits from visuals; default to terminal otherwise.
3. ⏳ **Ask first clarifying question (start here).**
4. ⏸ 2–3 approaches.
5. ⏸ Present design (sections, get approval).
6. ⏸ Write spec to `docs/superpowers/specs/2026-05-25-vfx-pass-phase-b2-weapons-design.md`.
7. ⏸ Self-review.
8. ⏸ User reviews spec.
9. ⏸ Hand off to `superpowers:writing-plans` (inline execution
   pre-selected by user for this whole session).

---

## Project state at hand-off

**On branch:** `main` (clean working tree apart from the recurring
material-drift noise — see *Hygiene* below).

**Recent merges on `main`** (most recent first — may roll forward
between reset and resume):
- `7ddf000` — Merge PR #48 (VFX phase B-1: engines + boost flare + RCS puffs)
- Earlier merges this session: PR #47 (VFX phase 1, post-processing +
  Debug tab), PR #46 (Settings menu scaffold), and the older Power &
  Energy / Laser PRs (#43, #44, #45).

**Open PRs (not blocking this brainstorm):**
- **PR #49** — Rocket orientation cosmetic fix (`fix/rocket-orientation`).
  Copilot review came back clean (zero comments). Awaiting user play-test
  + merge nod. Touches only `Assets/Scripts/Fly/Rocket.cs` — a static
  `MeshAlignment = Quaternion.Euler(90, 0, 0)` multiplied into the
  `LookRotation` result at both rotation call sites, so the primitive
  Cylinder mesh's +Y long axis aligns with flight direction.
- **PR #50** — Docs sync covering Settings + VFX phase 1 + VFX phase B-1
  (`chore/docs-sync-phase-3`). Copilot reviewer requested; user said
  "you don't need to wait for the audit, I'll kick the review of that
  audit later, you can just go on with Phase B-2 after the docs sync
  PR." So **proceed without waiting**.

Phase B-2 work will branch from current `main`. If the open PRs land
between reset and resume, they don't disturb the brainstorm — they
just slightly change the diff base. The architectural patterns and
hook surfaces below stay accurate.

---

## Phase B-2 — scope to brainstorm

From `docs/vfx_pass_ideas.md` §2 (Weapons & Projectiles) and §5
(part of Damage & Destruction — only the bullet-impact parts):

**Five core effects (the user's working scope):**

1. **Muzzle flash** on every Pyramid (machine gun) and Cylinder (rocket
   launcher) shot — one-frame bright sprite + small smoke puff at the
   barrel tip.
2. **Bullet tracer** — `TrailRenderer` on the Bullet prefab. Default
   colour suggestion from `vfx_pass_ideas.md`: yellow-white core + hot
   pink fringe (bloom-friendly).
3. **Bullet impact spark** at `ProjectileHit.surfacePoint` (set by the
   swept-raycast hit), oriented along the surface normal.
4. **Bullet ground impact dust** — variant of the spark for hits where
   the surface normal is roughly up.
5. **Rocket exhaust plume** — continuous ParticleSystem parented to
   the in-flight Rocket.
6. **Rocket smoke trail** — `TrailRenderer` on the Rocket, longer
   lifetime than the bullet tracer (~1 s), grey-white fading to alpha 0.

**Adjacent items intentionally deferred:**
- **Crosshair hit-confirm pulse** (§8, HUD-tier) — defer to **Phase B-4
  (HUD feedback)**. Hooking it from Phase B-2 would entangle weapon
  effects with HUD logic.
- **Rocket detonation multi-emitter** (§2) — defer to **Phase C
  (shaders + scripted sequences)**, where the explosion VFX get
  proper time. B-2 only covers exhaust + smoke trail for rockets.
- **Laser beam glow / impact heat / scorch decal** (§2 laser) — already
  marked for Phase C; not B-2.

The 5 effects naturally consolidate into **5 Debug-tab toggles**
(muzzle flash / bullet tracer / bullet impact / rocket exhaust / rocket
smoke) — taking the Debug tab to 13 total, still inside the two-column
~26-toggle capacity.

---

## Cross-cutting context for ALL Phase B PRs (user-set, applies here)

- **Asset authoring is in scope.** Particle textures, sprites, custom
  materials, prefabs — author whatever's needed. Procedural generation
  via Editor MenuItem (the established `VfxAssetsInstaller` pattern).
  Don't ask permission per asset; just do it and surface trade-offs.
- **Visual quality bar: "nice", referenced against actual space games.**
  Closest touchstone for weapons: **Freelancer's classic
  muzzle-flash + tracer + impact-spark trifecta** (cheap, readable,
  timeless). Plus **Everspace / Squadrons** for the saturated
  bloom-friendly look. **Elite Dangerous / Star Citizen** for the
  clean-sci-fi reference if needed.
- **AA settings (Phase 1.5)** is still pending — Graphics-tab dropdown.
  Sibling planned PR. Not blocking B-2.

---

## Phase 1 + B-1 patterns to reuse (verified shipped)

- **`Assets/VFX/{Textures,Materials,Prefabs}/`** folder convention.
  Subsequent VFX PRs append here.
- **`Assets/Scripts/Editor/VfxAssetsInstaller.cs`** — Editor MenuItem
  (`Tools/CubeFly/Generate VFX assets`) procedurally generates textures
  / materials / prefabs. B-2 should extend this OR add a sibling
  installer for weapon assets — a brainstorm decision. The current
  installer covers `Glow_64.png` (procedural radial gradient),
  `EnginePlumeMat` / `BoostShockMat` / `RcsPuffMat` (URP
  Particles/Unlit additive), and `EnginePlume.prefab` / `RcsPuff.prefab`.
- **`Assets/Scripts/Core/VfxSettings.cs`** — PlayerPrefs facade.
  Currently 8 typed bool keys (`VfxBloom`, `VfxVignette`, `VfxTonemapping`,
  `VfxColorAdjustments`, `VfxChromaticAberration`, `VfxEnginePlume`,
  `VfxBoostFlare`, `VfxRcsPuff`). Append 5 new keys for B-2 (e.g.
  `VfxMuzzleFlash`, `VfxBulletTracer`, `VfxBulletImpact`,
  `VfxRocketExhaust`, `VfxRocketSmoke`). Default ON.
- **`Assets/Scripts/Core/SettingsMenu.cs`**, `BuildDebugPanel` —
  one-line append per toggle to the `effects` array. The two-column
  column-major layout auto-rebalances.
- **`Glow_64.png`** (procedural 64×64 radial-gradient particle texture)
  already exists. Reusable. For B-2, additional sprites may be needed
  (e.g. a 4-spike "starburst" sprite for muzzle flash, a small spark
  sprite for impacts).

---

## Hook surfaces (verified during the previous session)

- **`Assets/Scripts/Fly/WeaponBehavior.cs`** — abstract base class.
  `public void TryFire(Vector3 crosshairWorldTarget)` is the entry,
  calling `protected abstract void Fire(Vector3 crosshairWorldTarget)`
  which the concrete weapons override.
- **`Assets/Scripts/Fly/PyramidWeapon.cs::Fire`** — spawns the bullet
  at `transform.TransformPoint(Vector3.up * 0.5f)` (the pyramid's tip),
  direction `transform.up`. Perfect muzzle anchor.
- **`Assets/Scripts/Fly/CylinderWeapon.cs::Fire`** — spawns the rocket
  at `transform.position`, direction `transform.up`. Perfect muzzle
  anchor.
- **`Assets/Scripts/Fly/Bullet.cs:80`** and **`Rocket.cs:81`** — both
  call `ProjectileHit.ApplyAndLog(hit, _damage, _firingConstruct, TAG)`
  on a swept-raycast hit. The `hit` carries `surfacePoint` and `normal`
  in world space. This is the impact-spawn hook for B-2 impact effects.
  Both projectiles `Destroy(gameObject)` on hit and on
  `_traveled >= maxRange` (Bullet) or `_seekTraveled >= maxRange`
  (Rocket).
- **`Bullet.prefab` and `Rocket.prefab`** (in `Assets/Prefabs/Projectiles/`)
  are currently simple GameObjects — MeshFilter + script only, no
  TrailRenderer or ParticleSystem yet.

---

## Architectural sketch (PROPOSED — to confirm during the brainstorm)

Not yet locked. Lay these out as the design in step 5 and let the user
push back:

- **Muzzle flash** — per-weapon child ParticleSystem (mirrors
  `ThrusterVfx`'s pattern). Each `PyramidWeapon` / `CylinderWeapon` gets
  a sibling `WeaponMuzzleVfx` component added at construct-build (in
  `FlyController.BuildConstruct` after the existing weapon collection),
  which instantiates `MuzzleFlash.prefab` as a child positioned at the
  barrel tip. The `Fire()` override calls `muzzleVfx.Play()` (a
  short-named hook). OR a base-class hook via `WeaponBehavior`. The
  brainstorm should decide.
- **Bullet tracer** — `TrailRenderer` added to the Bullet prefab via the
  installer, OR added in code at `Bullet.Awake` (probably code, since
  the prefab edit is fragile and the trail config is small). When the
  Bullet is destroyed, detach the TrailRenderer (`transform.SetParent(null)`),
  set `autodestruct = true`, set `emitting = false` so existing
  segments fade out naturally rather than vanishing with the projectile.
- **Bullet impact spark / ground dust** — instantiate the right prefab
  at `surfacePoint`, oriented along `normal`, with
  `main.stopAction = ParticleSystemStopAction.Destroy` so the prefab
  self-cleans after the burst. Spawn from a new helper in
  `ProjectileHit` (e.g. `ProjectileHit.ApplyAndLog` extends to also
  spawn the VFX, or a separate `ProjectileHit.SpawnImpactVfx`).
  Variant choice (spark vs dust) by checking `Vector3.Dot(normal,
  Vector3.up) > 0.7f` (i.e. the hit normal is roughly upward — ground
  hit).
- **Rocket exhaust** — child ParticleSystem prefab instantiated by
  `Rocket.Awake` (mirrors the engine-plume-as-instantiated-prefab
  pattern from B-1). Continuous emission while alive. Could also live
  on the Rocket prefab directly — brainstorm choice.
- **Rocket smoke trail** — TrailRenderer on the Rocket. Same
  detach-on-destroy pattern as the bullet tracer.

A small **`LingeringTrail` MonoBehaviour** (~20 lines) that, on
`OnDestroy`, detaches itself, disables emitting, and sets autodestruct
— shared between Bullet and Rocket trail handling. Saves
duplicate-handling.

---

## Questions for the brainstorm (suggested order)

The user has accepted the visual companion. Use it when a question
genuinely benefits from a mockup; otherwise stick to text.

1. **Scope confirmation.** Five effects in B-2 (muzzle / tracer /
   impact-spark / impact-dust / rocket-exhaust / rocket-smoke). Defer
   crosshair hit-confirm to Phase B-4 and rocket detonation to Phase C.
   Confirm via `AskUserQuestion`? Default Yes.
2. **Toggle granularity.** 5 toggles or 6 (split bullet-impact into
   spark vs ground-dust)? Recommend 5 (single "Bullet impact"
   toggle drives both variants — they're conceptually one effect).
3. **Visual treatment** — colours, sprite shapes per effect. **GOOD
   VISUAL-COMPANION USE.** Present side-by-side mockups: muzzle flash
   sprite options (starburst vs disc vs streak), tracer colour profile
   (yellow-white/pink vs all-yellow vs blue), rocket smoke direction
   (grey-white vs orange-tinted). This is where the companion earns
   its keep.
4. **Architectural choices** — per-weapon muzzle component vs
   `WeaponBehavior` base-class hook; bullet tracer via prefab edit vs
   `Bullet.Awake` code; `LingeringTrail` helper or inline detach.
   Mostly textual. Defaults baked in to the proposed design.
5. **Asset installer extension** — extend `VfxAssetsInstaller` or add
   `VfxWeaponAssetsInstaller`? Recommend extending — single source of
   truth, idempotent.

After questions, propose the full design (with the visual choices
embedded), get approval, write the spec, etc.

---

## Files to read first when resuming

- This file (`docs/superpowers/specs/2026-05-25-vfx-pass-phase-b2-resume.md`).
- `docs/vfx_pass_ideas.md` — VFX backlog (§2 Weapons & Projectiles
  in particular).
- `docs/superpowers/specs/2026-05-25-vfx-pass-phase-b1-engines-design.md`
  — Phase B-1 design spec; reuses many of the same patterns.
- `Assets/Scripts/Fly/WeaponBehavior.cs`, `PyramidWeapon.cs`,
  `CylinderWeapon.cs` — weapon spawn anchors.
- `Assets/Scripts/Fly/Bullet.cs`, `Rocket.cs` — projectile lifecycles
  + the `ProjectileHit.ApplyAndLog` call sites (lines 80 / 81).
- `Assets/Scripts/Editor/VfxAssetsInstaller.cs` — existing procedural
  asset generator to extend.
- `Assets/Scripts/Core/VfxSettings.cs` — append new keys here.
- `Assets/Scripts/Core/SettingsMenu.cs` (around `BuildDebugPanel`) —
  append new toggle entries here.

---

## Workflow rhythm (carried over)

- Brainstorm → spec → plan → **inline execution** (user pre-selected)
  → push → Copilot review.
- Branch naming: `feat/vfx-phase-b2-weapons` off `main`.
- Spec path: `docs/superpowers/specs/2026-05-25-vfx-pass-phase-b2-weapons-design.md`.
- Plan path: `docs/superpowers/plans/2026-05-25-vfx-pass-phase-b2-weapons.md`.
- Per the established pattern, the **whole PR** is a single brainstorm
  → single spec → single plan → multiple bite-sized task commits →
  push → Copilot.

---

## Session hygiene (do NOT forget)

- **Material drift on `Assets/Materials/{Bullet,Laser,Reactor,Rocket,Shield}Mat.mat`** is recurring Unity float-precision noise after material re-saves. **Never stage.** Leave unstaged or discard.
- **`CLAUDE.md`** is the user's local file. **Never touch.**
- **Per-script `.meta` files** often start minimal (just
  `fileFormatVersion: 2` + `guid: …`) on `Write` (and even on
  `mcp__unityMCP__create_script` since this session). Proactively
  expand them with the full `MonoImporter` block matching
  `PauseMenu.cs.meta`'s format to head off Copilot's stock
  "incomplete meta" comment.
- The `mcp__unityMCP__create_script` tool's validator has thrown false
  positives ("duplicate `Create` method" / "string concatenation in
  Update") on previous longer scripts in this session. If that
  happens, **fall back to `Write`** for the new file.
- After each script touch, **trigger `mcp__unityMCP__refresh_unity(mode="force", compile="request", scope="scripts", wait_for_ready=true)`**, then wait ~2-3 s, then
  `mcp__unityMCP__read_console(types=["error"], count=20, filter_text="Assets/Scripts", format="detailed")` — expect zero entries.

---

## One-paragraph TL;DR

**Resume the Phase B-2 brainstorm.** The user accepted the visual
companion in the previous session right before the context reset; this
is the hand-off. Phase B-2 covers five projectile-and-impact effects
(muzzle flash, bullet tracer, bullet impact spark + ground dust,
rocket exhaust plume, rocket smoke trail) — Freelancer trifecta plus
the rocket-specific additions. All Phase 1 + B-1 patterns
(`Assets/VFX/` folder, `VfxAssetsInstaller`, `VfxSettings`, Debug-tab
toggles) extend cleanly. Branch from `main`. Workflow: brainstorm →
spec → plan → inline execution → push → Copilot. The hook surfaces are
`WeaponBehavior.Fire` overrides for muzzles and
`ProjectileHit.ApplyAndLog` for impacts (Bullet.cs:80, Rocket.cs:81).
PR #49 (rocket orientation fix) and PR #50 (docs sync) are open but
not blocking. Don't wait for the docs sync audit.

**Start with: "I'm using the `superpowers:brainstorming` skill to resume
the Phase B-2 brainstorm." Mark step 2 ✅ (visual companion accepted).
Then ask the first clarifying question — scope confirmation.**
