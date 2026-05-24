# VFX Pass — Ideas Backlog (draft, uncommitted)

> **Status:** Scratch ideas list, not a spec. We will triage this into a real
> design via the usual brainstorm → spec → plan → implement loop. The file is
> kept around as raw input for that brainstorm. No commits yet.
>
> **Scope:** Graphics VFX only — SFX and music are explicitly deferred to a
> later audio pass.

---

## Project context (what we have to build on)

- **URP 17.3** is the active pipeline. Every scene already has a `Volume`
  GameObject and three URP `VolumeProfile` assets exist
  (`DefaultVolumeProfile`, `SampleSceneProfile`, `DesertVolumeProfile`) — but
  they currently have **no overrides** configured. Bloom, vignette, chromatic
  aberration, tonemapping, colour grading are essentially Inspector toggles
  away from working.
- **Zero `TrailRenderer` / `ParticleSystem` usage anywhere in
  `Assets/Scripts/`.** The VFX pass is greenfield — nothing to migrate, every
  particle/trail/decal/shader is a new addition.
- The codebase already exposes clean hook surfaces for VFX to attach to:
  - `WeaponBehavior.TryFire(target)` / per-weapon `Fire(...)` → muzzle
    flashes, shell ejection.
  - `LaserWeapon.Fire(...)` (continuous) → beam glow, dust, impact heat.
  - `ProjectileHit.ApplyAndLog(...)` (called from `Bullet`/`Rocket`) → bullet
    impact sparks, rocket detonation.
  - `ThrusterBehavior.LocalThrustAxis` → per-thruster plume / RCS puffs along
    a known clean ±X / ±Y / ±Z axis.
  - `CubeDeath.BeginDeath(outwardOrigin)` → death-drift VFX (sparks, smoke,
    debris).
  - `ConstructEnergySystem` events (shield hit/collapse/regen, reactor loss,
    Eject) → shield dome, reactor sparks, eject flash.
  - `FlyCrashHandler.OnCollisionEnter` → crash impact VFX + camera shake.
- The construct is a single `Rigidbody` with cube `BoxCollider`s as a compound
  body. That means a single TrailRenderer per thruster, parented to that
  cube, "just works" — no manual transform tracking.

## Guiding principles for the pass

- **URP-native first.** Use `Volume` overrides, the built-in `ParticleSystem`,
  Shader Graph, and `LineRenderer` / `TrailRenderer`. No custom render passes
  unless absolutely necessary.
- **Cheap-first ordering.** Post-processing → particles → shaders → custom
  meshes. The post-processing tier is essentially free perceived-quality.
- **Hook into existing events.** Almost every VFX below can subscribe to an
  event that already exists in code (see "Project context" above) — no new
  damage/death/fire pipelines needed.
- **Maintain readability.** Combat is already busy; VFX should *clarify* what
  happened (a hit landed, a shield absorbed, a cube died), not drown it. Keep
  durations short and footprints contained.
- **MonoBehaviour-driven.** Match the rest of the codebase. No DOTS / VFX
  Graph unless we measure a real cost we need to optimize away.
- **Each VFX is one small `MonoBehaviour` or prefab** that listens to one
  event — composable, easy to disable or replace.

Legend used in the lists below:

- **★** = seed from the user / ROADMAP (already on the list before this
  brainstorm).
- *(free)* = post-processing toggle or one-line Volume override.
- *(cheap)* = a single `ParticleSystem` prefab + a tiny script.
- *(medium)* = a Shader Graph or a multi-emitter prefab.
- *(bigger)* = new meshes, custom shader work, or scripted sequences.

---

## 0. Post-processing (cheapest wins — URP Volumes)

The free tier. Most of these are Inspector toggles on the existing volume
profiles and immediately make the game look ten years younger.

- **Bloom** *(free)* — globally lifts every emissive material (laser beam,
  reactor glow, muzzle flash, bullet, rocket exhaust) without any per-effect
  work. ★ (implicit in the "laser-beam glow" seed).
- **Vignette** *(free)* — focuses attention on the centre of the screen;
  pairs nicely with a damage-flash variant (see HUD section).
- **Tonemapping (ACES or Neutral)** *(free)* — stops bright FX from clipping
  to pure white and gives the scene cinematic colour response.
- **Colour grading** *(free)* — per-scene mood:
  - MainMenu / HangarSelect — neutral / slightly warm.
  - BuildScene — clean, slightly cool, high readability.
  - FlyScene — cinematic; cool shadows, warm highlights, mild contrast lift.
  - DesertSandbox — already has its own profile; warm/sand grade.
- **Chromatic aberration** *(free, contextual)* — subtle baseline + ramp up
  on low HP / overheat / shield-collapse (see HUD section). Don't leave it
  baked on at high intensity; it muddies the picture.
- **Lens distortion / barrel** *(free, optional)* — very mild on FlyScene to
  sell the "behind a canopy" feel. Easy to overdo.
- **Film grain** *(free, optional)* — at low intensity, hides banding on the
  big gradient skies we'll add. Skip if it clashes with the cube-shape
  aesthetic.
- **Motion blur (camera-driven)** *(free)* — gentle setting only, otherwise
  it fights the boost speed lines and crash readability.

---

## 1. Engines & thrusters

The visual signature of any flying-construct game. Currently the construct
just slides through space silently.

- **Engine plume per thruster cube** ★ *(cheap)* — a `ParticleSystem` parented
  to each `PlacedThruster`, emitting along `-LocalThrustAxis` (the existing
  per-cube exhaust direction). Scale emission rate and lifetime to the
  thruster's current input on its axis (read from `FlyController`).
- **Plume colour** *(free)* — cool blue / cyan default; matches the
  "construct = utility" aesthetic. Optionally tint per material (A/B/C/D)
  later.
- **Boost flare** ★ *(cheap)* — while `LeftCtrl` is held *and* the axis has a
  contributing thruster, intensify that thruster's plume: larger sprite,
  brighter centre, longer trail. A second small "shock-diamond" sprite
  layered inside gives the supersonic-jet look. Hooks into
  `ThrusterBehavior.IsBoosting` (or `FlyController`'s boost state).
- **RCS puffs on strafe / yaw / roll** *(cheap)* — one-shot burst when the
  pilot starts moving on an axis that isn't the main thruster axis. Sells
  the "tiny attitude jets firing" feel. Sub-thruster-cube cost: just spawn
  at the construct's corners.
- **Engine heat distortion** *(medium)* — small refraction quad behind each
  plume, animated UV-noise. Optional; URP refraction has a known cost.
- **Damage flicker** *(cheap)* — when a thruster cube's HP drops below 50%,
  its plume sputters (modulated emission rate, occasional dropouts).
- **Engine startup flame** *(cheap)* — short flash + smoke ring when
  FlyScene loads (or when first input applied). Frames the moment the ship
  comes alive.
- **Engine-off afterglow** *(cheap)* — when input drops to zero, the plume
  doesn't instantly disappear: it shrinks and fades over ~0.3 s.

## 2. Weapons & projectiles

- **Muzzle flash** ★ *(cheap)* — one-frame bright sprite + small smoke puff
  at the barrel tip of every Pyramid (machine gun) and Cylinder (rocket
  launcher) cube on each shot. Hook: per-weapon `Fire(...)` override.
- **Bullet tracer** ★ *(cheap)* — `TrailRenderer` on the `Bullet` prefab
  (yellow-white core, hot pink fringe under bloom). Fade-out lifetime ~0.15
  s so it's a streak, not a line.
- **Bullet impact spark** *(cheap)* — small radial spark burst at the
  `ProjectileHit.surfacePoint`, oriented along the surface normal. One
  prefab, spawned from a thin `BulletImpactVfx` listener on
  `ApplyAndLog`.
- **Bullet ground / world-cube impact** *(cheap)* — same spark prefab, plus
  a small dust puff variant when the hit normal is roughly up
  (i.e. hit the ground).
- **Rocket exhaust plume** *(cheap)* — continuous `ParticleSystem` parented
  to the rocket, behind it, while in flight. Bright at the nozzle, fading
  into a long whisper.
- **Rocket smoke trail** ★ *(cheap)* — `TrailRenderer` on the rocket, longer
  lifetime than the bullet tracer (~1 s), grey-white, fading to alpha 0.
- **Rocket detonation** ★ *(medium)* — multi-emitter prefab at the impact
  point:
  - One-frame white flash + brief light source.
  - Orange fireball billboard sprite (animated).
  - Dust ring expanding outward along the surface.
  - 6–12 small debris-chunk meshes scattered outward (use a few cube /
    pyramid scrap meshes recycled from the prefabs).
  - Smoke column rising for ~2 s.
- **Laser beam glow** ★ *(medium)* — replace the current flat
  `LineRenderer` material with an additive Shader-Graph material that has
  a hot inner core + soft outer halo + scrolling noise. Under bloom, this
  reads as a proper energy beam. The seed PR explicitly deferred this.
- **Laser impact heat-distortion** *(medium)* — refraction quad at the beam
  hit point, animated and brief. Adds the "the beam is melting metal" feel.
- **Laser scorch mark** *(medium)* — short-lived projected decal on the hit
  target's surface; fades in ~0.5 s. URP decals are cheap.
- **Laser dust / ion glow along the beam path** *(cheap)* — sparse particle
  emission along the beam's length (using emission shape "edge" between
  barrel and hit point), gives the impression the beam is ionising the air.
- **Laser barrel overheat vent** *(cheap)* — when the laser type enters the
  "Overheated!" lockout, every laser barrel ejects a quick steam-puff and
  glows red at the muzzle for the cooldown. Reuses the existing per-type
  `WeaponTypeGroup.Overheated` flag.
- **Out-of-power click** *(deferred — SFX-only; not in this pass.)*
- **Crosshair hit confirm** *(cheap, technically HUD)* — see §8.

## 3. Shields

The current shield system is mechanically complete but visually invisible —
a pure HUD bar. This is where VFX have the biggest gameplay-clarity payoff.

- **Shield dome around the construct** ★ *(medium → bigger)* — a translucent
  mesh fitted to the construct's bounds (axis-aligned box or convex hull).
  Hex-pattern Shader-Graph material, animated UV noise, fresnel rim. Hidden
  when the shield is collapsed; faint when full; glows when it just took a
  hit. Deferred from the Power & Energy PR.
- **Shield hit ripple** *(medium)* — expanding ring shader on the dome,
  centred at the impact's world-position, lifetime ~0.5 s. Triggered from
  `ConstructEnergySystem.ApplyToShield` when absorption > 0. Two variants:
  blue-tinted for projectile, hot pink/red for energy (matches the type
  modifier story).
- **Shield collapse** *(medium)* — when the dome goes from up to down (lost
  reactor / pool drained), the hex pattern flickers, cracks across a couple
  of frames, then dissolves. ~0.6 s total.
- **Shield recharge sweep** *(cheap)* — while regenerating, a thin bright
  scanline animates around the dome at ~1 rev/s. Distinguishes "regening"
  from "full".
- **Per-shield-cube tether (multi-shield only)** *(cheap, optional)* —
  faint arcs between shield cubes when more than one is alive; visually
  hints at the shared pool. Skip if it gets noisy.

## 4. Power & reactors

- **Reactor inner glow** *(cheap)* — emissive material on the solid-cylinder
  cap, with a slow sine-pulse on intensity (~0.5 Hz). Tells the player
  "this thing is alive". Hooks: nothing — pure visual.
- **Reactor stress sparks** *(cheap)* — when a reactor cube's HP drops
  below 30%, sparks fire occasionally from random points on its surface,
  paired with red emissive flicker. Hooks: `CubeStats.HpChanged` (or
  similar — small new event).
- **Reactor death flash** *(cheap)* — when a reactor reaches 0 HP, a brief
  white-blue flash + arc burst before the standard cube-death animation
  takes over. Sells "the core just failed".
- **Eject sequence** *(medium)* — when the player presses P, instead of all
  power cubes dying at once, stagger them by ~80 ms each, each with a
  small directed explosion. Reads as a coordinated jettison rather than a
  bug.
- **Power-loss screen flicker** *(cheap, HUD-adjacent)* — when the net
  power flips negative (e.g. reactor lost), one short chromatic-aberration
  spike + a scanline glitch sweep down the HUD. Sells "systems
  destabilising".

## 5. Damage & destruction

- **Cube hit spark** ★ *(cheap)* — already noted in §2 (projectiles), but
  shielded hits should look different from raw-HP hits. Suggest:
  - Spark when shield is *absorbing* the hit → ripple in §3 *plus* a small
    rim flash on the dome, no surface spark.
  - Spark when shield is down or overflow spills → the §2 spark
    *additionally*, on the cube surface.
- **Sustained smoke from low-HP cubes** *(cheap)* — when a placed cube's
  HP drops below 25%, attach a small slow black-smoke `ParticleSystem`
  that follows it through the construct's motion. Removed on death. Sells
  the wear-and-tear of a sustained fight.
- **Damaged cube red emissive flicker** *(cheap)* — same trigger as above:
  a faint red glow with occasional flicker on the cube's material. Use
  `MaterialPropertyBlock` to avoid material instantiation (matches the
  existing red-tint pattern in the delete tool).
- **Cube death enhancement** ★ *(medium)* — the current drift-outward
  animation is logically complete but visually flat. Add:
  - One-frame white-bloom flash at zero HP.
  - Spark burst centred on the cube.
  - 3–5 small debris-chunk meshes scattered outward along the drift
    direction.
  - A short flame/smoke trail (`TrailRenderer` on the dying cube, lifetime
    ~the same 2 s as the drift) so the player can track the wreck.
- **Alpha cube death — cinematic end-of-run sequence** *(bigger)* — when
  the alpha cube hits 0 HP, before the "Construct Destroyed" overlay
  shows, run a short scripted sequence:
  - 0.0 s: first explosion at the alpha cube (medium scale).
  - 0.2 s: chain explosions cascade through 3–5 nearby placed cubes.
  - 0.4 s: large white flash + radial blur on the camera; `Time.timeScale`
    drops to 0.25.
  - 0.6 s: final detonation, dust ring expanding outward across the
    ground plane, debris field thrown outward.
  - 1.2 s: hand off to the "Construct Destroyed" overlay; `Time.timeScale`
    restored.
  - This is the single biggest "perceived production value" item on the
    list. Worth doing well.

## 6. Environment & world

- **Space / sky skybox** *(cheap)* — replace the default Unity skybox in
  FlyScene with a starfield + nebula gradient skybox (six-sided cubemap or
  procedural Shader-Graph dome). Massive perceived-quality lift relative
  to cost.
- **Distant nebula cloud layer** *(medium)* — a far-distance parallax
  layer above the skybox (single quad shader), drifts slowly. Sells depth.
- **Sun + lens flare** *(cheap)* — pick a direction (matching the
  directional light), add a sprite-based lens flare that occludes against
  the camera frustum. Subtle, not Star-Wars-intro big.
- **Camera-anchored dust** *(cheap)* — a small particle system parented to
  the camera, emitting low-density specks that pass it on the screen Z
  axis. Sells motion when the construct is flying through "nothing".
  Crucial for FlyScene because the world is mostly empty between target
  dummies.
- **Ground heat-shimmer (desert)** *(medium)* — refraction shader on the
  ground plane near the camera, sells the desert level when that lands.
  Slots into the future desert-map roadmap item.
- **Distant horizon glow** *(cheap)* — gradient quad at the horizon,
  warm-tinted, gives the otherwise-flat ground plane a sense of distance.
- **World-target highlight** *(cheap)* — give the 20 rusty target dummies
  a subtle emissive rim so they read as deliberate targets, not random
  scenery.

## 7. Build scene

Often forgotten in the "shooty space VFX" framing, but a building-mode
game lives or dies by how tactile placement feels. None of these are big.

- **Placement-ghost shimmer** *(cheap)* — the existing `CubePreview` is a
  solid material. Add a slow horizontal scanline animation (Shader Graph)
  so it reads as "hologram, not real yet".
- **Snap-confirm sparkle** *(cheap)* — when a placement is accepted, a
  one-frame ring sparkle at the cell tells the player "yes, placed".
  Particularly important once we have a lot of cubes — you stop being able
  to see whether your click landed.
- **Mass-over-cap rejection** *(cheap)* — the existing fading red "Too
  much mass!" message could be paired with a tiny camera nudge / shake
  and a brief red flash at the ghost cube.
- **Delete tool dissolve** *(medium)* — when a cube is deleted, instead of
  vanishing, run a 0.15 s shader dissolve (vertical wipe + edge glow).
  Pair with the existing flood-fill cascade so a chain of disconnected
  cubes dissolves in a wave.
- **Toolbar swatch hover glow** *(free)* — emissive bump on hover, cheaper
  than tinting the icon. Helps the toolbar feel reactive.
- **Alpha cube indicator polish** *(cheap)* — the existing red arrow could
  bob gently, glow softly, and rotate a thin scanline ring. Subtle —
  don't make it a Christmas tree.
- **Thrust-axis force lines** *(cheap, optional)* — when a thruster cube is
  placed, a thin debug-style arrow shows briefly along its
  `LocalThrustAxis`, fading out over ~1 s. Helps new players understand
  why a given thruster boosts a given axis.

## 8. HUD / screen effects

- **Damage vignette** *(free + cheap)* — pulse the vignette colour red and
  intensity up briefly when the alpha cube takes damage. Hooks:
  `CubeStats.HpChanged` on the alpha cube. (Hooked via the existing damage
  pipeline; no new events needed.)
- **Low-HP chromatic aberration ramp** *(free)* — as the alpha cube's HP
  drops below 25%, ramp the existing chromatic-aberration override up. At
  10% HP add a slow red-pulse vignette. Wordlessly tells the pilot to
  retreat.
- **Boost speed lines** *(cheap)* — radial-streak particle system parented
  to the camera, only active while `LeftCtrl` is held and the boost meter
  has charge. Density scales with speed.
- **Crosshair hit confirm** *(cheap, HUD)* — the screen-space crosshair
  pulses (scale + brief white tint) for ~80 ms when a fired projectile
  registers a hit. Reads as "yes, that landed". Hook: `ProjectileHit`
  callback on the firing construct.
- **Crosshair shield-absorbed hint** *(cheap)* — a different colour pulse
  (cyan) when the hit was absorbed by a shield, so the player learns the
  type-vs-shield matchups (energy is great, kinetic bypasses, etc.).
- **Camera shake on crash** *(cheap)* — `FlyCrashHandler.OnCollisionEnter`
  already knows the impact severity (it scales damage by normal-component
  speed). Add a shake with amplitude proportional to that same value.
- **Camera shake on nearby detonation** *(cheap)* — rockets and the alpha-
  cube death sequence both broadcast a `WorldExplosion` event with a
  position and radius. The camera applies a falloff shake.
- **Hit-stop micropause** *(cheap, optional)* — on big events (rocket
  detonation, alpha-cube death, overheat lockout, shield collapse),
  `Time.timeScale = 0.1` for ~50 ms then snap back. Adds tactile weight.
  Use sparingly so flight still feels responsive.
- **Boost-bar / heat-bar polish** *(cheap)* — the existing throb on the
  bars is good; the new heat bar should keep its red throb in the
  overheated state, which is already implemented.

---

## Reference — what comparable space games do

Sketch only; useful as touchstones during the brainstorm later. The closest
analogue to Cube Fly is *Space Engineers* (cube construction + flight +
weapons + destruction).

- **Space Engineers** *(closest)* — voxel cube destruction with sparks,
  smoke, and fire on damaged blocks; small puffs on thruster activation;
  no shield dome in vanilla but heavy hit-spark and debris culture.
- **Elite: Dangerous** — engine plumes are the ship's visual signature;
  shield rings and the iconic hexagonal hit-pattern shader; clean kinetic
  vs energy weapon distinction; planetary surface dust kicks.
- **Everspace / Everspace 2** — saturated explosions; heavy bloom;
  juicy hit-confirm crosshair pulses; chromatic aberration on damage;
  fresnel shield flashes. A good benchmark for "feels good per click".
- **Star Citizen** — multi-stage capital-ship destruction sequences are
  the gold standard for the alpha-cube cinematic death we'd like.
- **Homeworld (1 / 2 / Remastered)** — engine plumes are *the* visual
  identity; dramatic capital-ship deaths with cascading internal
  explosions over several seconds. Same template as the alpha-cube death.
- **Star Wars: Squadrons** — colour-coded laser energies; shield
  directional impact indicators; clear hit feedback in a busy combat
  picture.
- **Freelancer** — the classic muzzle-flash + tracer + impact-spark
  trifecta. Cheap, readable, timeless.
- **EVE Online** — distant turret tracers (LOD-aware), gigantic capital-
  ship explosions with persistent debris fields.
- **X4: Foundations** — engine trails per ship class; plasma sparks on
  hits; sustained debris fields after destruction.
- **FTL: Faster Than Light** — proof that minimal, flat VFX (shield
  bubble crack, hull-breach flame, weapon charge glow) can be totally
  legible and expressive. Useful as a "what's the floor?" reference.
- **No Man's Sky** — atmospheric entry burn, planet glow, motion dust —
  good reference for the "sense of motion in empty space" problem.

Common threads worth stealing:

1. **Engine plumes carry the ship's identity.** Everyone does them. We
   should too.
2. **Shield FX double as gameplay communication.** The hexagonal ripple
   *is* the shield UI in many games — it tells you the shield ate the
   hit. Pair this with our `DamageType` modifiers and the player learns
   the system visually.
3. **Hit confirm is the cheapest big-win.** A muzzle flash + tracer +
   spark at the target sells every shot. Costs ~3 prefabs.
4. **Big deaths get cinematics.** Capital ships, hero units, and final-
   life deaths all get scripted multi-stage destruction. Our alpha cube
   is the equivalent.
5. **Empty space needs motion cues.** Camera-anchored dust, distant
   parallax, or a skybox layer. Otherwise the construct looks pinned.

---

## Suggested phasing (for the upcoming brainstorm)

A rough triage so the brainstorm has a starting point. Final ordering is
the brainstorm's call.

### Phase A — free / near-free wins

The most quality per minute of work. Mostly Inspector configuration.

- Bloom, vignette, tonemapping, colour grading (per scene).
- Camera-anchored dust + space skybox in FlyScene.
- World-target highlight (small emissive on the 20 dummies).
- Toolbar swatch hover glow + alpha-cube indicator polish in BuildScene.
- HUD: damage vignette, low-HP chromatic-aberration ramp, crosshair
  hit-confirm pulse.

### Phase B — small prefabs / new behaviours

The first batch of actual VFX work.

- Engine plume per thruster cube (with boost flare).
- Muzzle flash + bullet tracer + bullet impact spark.
- Rocket exhaust plume + smoke trail.
- Cube death enhancement (flash + spark + debris + trail).
- Camera shake on crash and on nearby detonation.

### Phase C — shaders + scripted sequences

Bigger items, mostly Shader Graph.

- Laser beam glow + impact heat-distortion + scorch decal + ion dust.
- Shield dome (hex/fresnel) + hit ripple + collapse + recharge sweep.
- Rocket detonation multi-emitter prefab.
- Delete-tool dissolve shader.
- Reactor inner glow + stress sparks + death flash + Eject staggered
  sequence.

### Phase D — the big one

- Alpha-cube death cinematic sequence (multi-stage, timescale, radial
  blur, debris field, hand-off to overlay).

---

## Settings → Debug tab integration

Hand-in-hand with the **Settings menu** scaffold (Up Next item 2 in
`ROADMAP.md`). The Settings UI lands first as six placeholder tabs
(General / Display / Graphics / Audio / Controls / Gameplay); the VFX
pass then adds a seventh **Debug** tab that surfaces every VFX item
above as a runtime toggle.

- **One toggle per logical effect.** Not one per emitter or per
  particle. "Bullet tracer" is one toggle covering the
  `TrailRenderer`-on-`Bullet` prefab; "Engine plume" is one toggle
  covering all per-thruster plumes; "Bloom" is one toggle wrapping
  the URP Volume override. Granularity matches the §0–§8 bullet
  structure of this file.
- **Hover description sourced from this file.** The tooltip for each
  toggle is the one-liner shown next to it in §0–§8, **shortened in
  the tooltip only** where the source line is too long for a UI
  hover. *This file is the source of truth and is not shortened.*
- **Defaults: ON.** Toggles exist to let the player disable individual
  effects they find noisy, and to give us a single A/B testbed during
  development. Nothing in the Debug tab is OFF by default.
- **Free-tier slots in trivially.** Post-processing items in §0 are a
  one-line `Volume` override each, so a toggle is just enabling /
  disabling the override.
- **Persistence is the Settings menu's problem, not ours.** Each
  toggle reads/writes through whatever persistence layer the Settings
  scaffold lands with; the VFX pass just registers handlers.

## Explicitly out of scope (for this pass)

- **SFX and music** — deferred to a separate audio pass per the user's
  instruction. None of the items above assume audio; they should also
  feel right with sound when that pass happens.
- **Gameplay-changing VFX** — no effect on this list adds damage, alters
  collision, changes power balance, or affects save data. The pass is
  pure visual polish.
- **DOTS / VFX Graph** — the project is intentionally MonoBehaviour-only;
  particle effects in this pass use `ParticleSystem`, `TrailRenderer`,
  `LineRenderer`, Shader Graph, and URP `Volume` overrides. No new
  architectural layer.

---

*Draft saved at `docs/vfx_pass_ideas.md`. Deliberately uncommitted — this
file is brainstorm input, not a spec. The next step is the usual
brainstorm → spec → plan → implement loop, which will pick a phasing,
nail down scope, and produce `docs/superpowers/specs/...-vfx-pass-design.md`
as the binding artefact.*
