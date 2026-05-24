# Roadmap — Cube Fly / Cosmic Scrap Club

A living planning doc. What works today, what we're building next, and where the project is headed. Open an [Issue](https://github.com/entewurzelauskuh/CosmicScrapClub/issues) if you'd like to claim one of the **Up Next** items — or just play around and tell us what you find.

---

## Vision

Cube Fly is a sandbox where you build a flying construct out of cubes, weapons, thrusters, reactors, and shields — then fly it. You can already place shapes, save / load three constructs, fly a physics-driven construct that bounces off the world, shoot bullets and rockets, fire a continuous energy laser, register hits, blow cubes off targets, take kinetic damage when you crash, boost with thruster cubes, power reactors that run shields and energy weapons, and lose the run when your anchor cube dies. The next chunk of work is a polish pass — VFX and a settings menu.

It's intentionally small in scope (Unity 6.3 LTS, URP, MonoBehaviour-only, no DOTS), pure C# everywhere, and the docs are kept honest so you can read [`docs/full_architecture.md`](docs/full_architecture.md) and immediately know which file does what. If you've been wanting to mess around in a Unity codebase that's neither toy-sized nor incomprehensible, this might be the project for you.

---

## Where we are today

Read [`docs/cube_fly_spec.md`](docs/cube_fly_spec.md) for the canonical product spec and [`docs/full_architecture.md`](docs/full_architecture.md) for the file-by-file implementation map. In a sentence: four scenes (`MainMenu → HangarSelect → BuildScene ⇄ FlyScene`), three save slots, three ship classes (Allrounder / Tank / Scout) chosen per slot, ESC pause overlay, a decoupled Shape × Material build system (Cube / Slope armour · Pyramid / Cylinder / Laser weapons · Thruster / Reactor / Shield utilities × four armour materials), per-cube HP / Armour / Mass stats, symmetric face-validity placement rules, Rigidbody-driven 6-axis flight with real bouncing off the world and an adaptive third-person camera, a Left-Ctrl boost mechanic fed by thruster cubes, a screen-space crosshair, three weapons (bullets, rockets, and a continuous energy laser) selected from a toolbar with digit keys and mouse-wheel cycling, Speed + HP + Boost + Shield + Power + laser-heat HUD readouts, a basic 200×200 world map seeded with 20 target dummies, projectile hit registration with armour-aware damage, an outward-drift cube destruction animation, kinetic crash damage on collision, a construct-wide power system (reactors power shields + the laser; a shared shield pool absorbs damage before HP with projectile / energy / kinetic profiles; an "Eject" sheds dead-weight power cubes when all reactors are lost), and an end-of-run condition when the alpha cube dies.

### Shipped since the last roadmap pass

- **Power & Energy** — a construct-wide `ConstructEnergySystem` with an instantaneous net-rate power balance. **Reactor** cubes (solid cylinder, +10 output / mass 10) produce power; **Shield** cubes (half-size cube, −20 draw / mass 5 / +50 pool) draw it and add a single shared pool that absorbs damage before HP (projectile ×0.9, energy ×1.1, kinetic bypasses entirely; overflow spills to HP; collapses when unpowered; regens 20/s after a 5 s lull). Two reactors power one shield. The shield is an all-or-nothing first-priority consumer: when output covers its draw the shield comes up and the laser runs on the remainder; when it can't, the shield stays offline and frees its whole budget for the laser. An **Eject (P)** sheds reactor-less power cubes as dead weight. HUD: shield bar + `Power:` readout (FlyScene **and** BuildScene). See [`docs/power_and_energy_spec.md`](docs/power_and_energy_spec.md).
- **Laser Weapon** — the first **energy**-type weapon: `LaserWeapon : WeaponBehavior`, a continuous hitscan beam (thin-barrel cube, mass 2) along the fixed barrel axis with a runtime `LineRenderer`, ticked energy damage (~60 raw DPS, shield ×1.1), shared per-type **heat** (rise 50/s, cool 30/s, overheat lockout recovering 15/s with an "Overheated!" flash), and per-cube power draw (5; needs a reactor to fire). A `FlyHeatBar` mirrors the boost bar right of the crosshair, fading with heat.
- **HUD / canvas consolidation (F8)** — 10+ self-bootstrapped runtime canvases collapsed into three shared HUD roots: `PersistentHud` (DDOL, lazy-created, sortingOrder 200), `FlyHud` (FlyScene-attached, sortingOrder 100, `[DefaultExecutionOrder(-500)]`), and `BuildHud` (BuildScene-attached, same). Every HUD script now parents its UI under `FooHud.Instance.Root`; future HUD additions (shield bar, laser heat bar) no longer need their own canvas + EventSystem + sortingOrder bookkeeping. `UIBootstrap` and the two `Assets/UI/` prefabs are deleted; `LogBootstrapper` self-bootstraps `BeforeSceneLoad` like the other persistent UI scripts.
- **HitContext refactor** — a `HitContext` readonly struct now carries source, target, amount, `DamageType` (`Projectile` / `Energy` / `Kinetic`), `HitFlags` (`None` / `BypassArmour`), surface point + normal, `OutwardOrigin` (the death-drift bias point `CubeDeath` reads), and a reserved `Impulse` field for future knockback. Threaded through `CubeDamage.ApplyAndLog`, `Bullet`, `Rocket`, `FlyCrashHandler`, `CubeStats`. The phase-2 shield system reads `Type` directly — no call-site changes needed when shields land.
- Projectile hit registration — swept raycasts on `Bullet` and `Rocket`, self-construct filtering, armour-aware damage via `CubeStats.TakeDamage`.
- Basic world map — 200×200 ground plane plus 20 hand-placed rusty-orange `WorldTargetCube` dummies in `FlyScene`.
- Cube destruction & death animation — at-zero-HP cubes detach, disable colliders, drift outward at ~2 u/s for 2 s, then despawn.
- Crash damage — kinetic, armour-bypassing damage on collision via `CubeStats.TakeRawDamage`. Player ship cubes can now actually die.
- End-of-run condition — alpha cube at 0 HP shows a "Construct Destroyed" overlay and returns to the main menu. Closes the Combat & Damage Model section.
- Rigidbody-driven construct — the construct is now a non-kinematic `Rigidbody` compound body. Physics-based flight (`AddForce` / `AddTorque`), real bouncing off the ground and world cubes, `OnCollisionEnter`-based crash damage charged to the contact-point cube. Adaptive third-person camera. Speed + HP HUD readouts.
- Ship classes — Allrounder / Tank / Scout, chosen via a dropdown in BuildScene and stored per save slot. Each class sets the alpha cube's HP, the build mass cap, and a movement multiplier (`ShipClass` / `ShipClasses`).
- Minimum responsiveness floor — above `maxResponsivenessMass`, applied thrust and torque scale up by `mass / cap`, so linear acceleration and turn rate flatten out instead of falling toward zero and the heaviest Tank build still flies (`FlyController.ResolveRigidbody`).
- Thruster cube — a placeable Utility cone shape and a new Utilities toolbar category (`ShapeUtilityThruster` / `PlacedThruster` / `ThrusterMatDef`). Marks the construct-local axes the boost can amplify.
- Boost mechanic — Left-Ctrl boost backed by a 0–100 Boost resource with an overboost lockout (`ThrusterBehavior`); per-axis ×1.3 acceleration + ×1.3 max-speed while engaged, with a `FlyBoostBar` HUD bar that throbs red in the critical zone.

---

## Up Next

In running order. Phase 1 (HitContext + HUD consolidation) and phase 2 (Power & Energy + Laser) are done; next is a polish pass (VFX + settings), and finally an experimental merge of the long-standing desert map work. Docs are re-synced at the close-out of each phase.

### 1. Extended VFX pass

Engine trails, muzzle flashes, projectile trails, explosion / death particles, hit sparks — plus the deferred laser-beam glow and the shield dome. Cheap polish, big perceived-quality win. Mostly URP particles plus a couple of shader graphs — no new gameplay systems.

Phasing per `docs/vfx_pass_ideas.md`:

- **Phase 1 — Post-processing + Debug tab (in-flight).** Bloom, Vignette, Tonemapping (ACES), ColorAdjustments, ChromaticAberration as URP Volume overrides on the main game profile, plus the seventh `Debug` tab in Settings with per-effect toggles backed by PlayerPrefs. Establishes the Debug-tab pattern subsequent phases append to. Plus reusable Tooltip helper.
- **Phase B — Small prefabs / new behaviours.** Engine plumes per thruster cube (with boost flare), muzzle flash + bullet tracer + impact spark, rocket exhaust + smoke trail, cube death enhancement (flash + spark + debris + trail), camera shake on crash / detonation.
- **Phase C — Shaders + scripted sequences.** Laser beam glow + impact heat-distortion + scorch decal, shield dome (hex/fresnel) + hit ripple + collapse, rocket detonation multi-emitter, delete-tool dissolve, reactor inner glow + stress sparks.
- **Phase D — Alpha-cube cinematic death.** Multi-stage explosion + time-scale dip + radial blur + debris field, before the existing "Construct Destroyed" overlay.

### 2. Settings menu

A tabbed settings UI, reachable from **both** the Main Menu's `Settings` button (currently logs and does nothing) and the ESC pause overlay (which gains a new `Settings` button alongside `Menu` / `Back to Desktop`) — same UI from two entry points.

Six tabs scaffolded as **placeholders** — the scaffolding is the deliverable; real controls fill in tab by tab later, as each becomes relevant:

- **General** · **Display** · **Graphics** · **Audio** · **Controls** · **Gameplay**

A seventh **Debug** tab is added during the VFX pass (item 1 above): a per-effect on/off toggle for every VFX item in `docs/vfx_pass_ideas.md`, with a hover-over description sourced from that file's one-liner per item (shortened for the tooltip where needed; the ideas file remains the source of truth and is not shortened). Lets the player disable individual effects without an Inspector trip and gives us a single in-game A/B testbed during VFX development.

### 3. Docs sync — phase 3 close-out

Refresh `README.md`, `ROADMAP.md`, `docs/full_architecture.md`, `docs/cube_fly_spec.md` to reflect the VFX pass + settings menu. Audit the docs index in `README.md`'s companion-docs list and trim anything that's outlived its usefulness.

### 4. Desert-map FlyScene experiment

After everything above lands. Try merging the long-standing desert map work (see [`docs/desert_level_spec.md`](docs/desert_level_spec.md) and `Assets/Scripts/Desert/`) into the live FlyScene on an experimental branch — replacing or supplementing the current 200×200 ground plane + 20 dummy cubes with the dune terrain + formations. The construct's flight logic should already work in the desert (it's just colliders + Rigidbody); the experiment is whether the existing desert assets compose cleanly with the current FlyScene HUD, world spawns, and physics, and whether the resulting feel is what the desert spec promised. Tagged **experimental** — outcome could be ship, iterate, or shelve.

---

## Later

These are deferred until the active sections above are largely done. Roughly in the order we'd pick them up.

- **More weapon variants** — homing missile, shotgun, mine layer, etc. (Each is a small `WeaponBehavior` subclass — the architecture already supports it.)
- **Audio + SFX pass** — engine hum, weapon SFX, impact thuds, ambient. The project is silent today.
- **AI-controlled enemy ships** — start with simple chase-and-shoot drones. Reuses the existing construct rebuild path (an AI ship is just a `ConstructSave` driven by a different controller).
- **Game modes** — once AI enemies exist, real game modes become possible: wave survival, time trial, escort, etc. Big design question, comes later.
- **Photo mode in flight** — pause-overlay variant: free camera, hide HUD, screenshot to a folder. Trivial to implement (the camera is already pause-aware) and great for sharing builds.
- **Sensor cubes + fog of war** — sensor cube extends the player's draw distance / awareness radius. Requires a fog-of-war / dynamic-visibility system, which is its own substantial piece of work — both are paired for "much later."
- **Save format versioning / migration** — until then, schema changes break old saves with no remorse (`ConstructSave.version > CurrentVersion` is rejected). Becomes important once people care about their builds across game-versions.

### Architecture & infrastructure

Refactors and tooling deferred from the 2026-05-17 codebase audit (`docs/CODEBASE_REVIEW_AUDIT.txt`). Pure infra — none of these change what the game *does*; they make adding the gameplay above cheaper and safer.

- **`ConstructModel` domain layer** — extract a Unity-independent construct model (cells, face-aware connectivity, mass/stat aggregation, save/load, events) out of `GameData`'s static facade. Cleanly separates the saved design from the runtime flight body and makes the flight-snapshot a first-class concept rather than the workaround it is today. The biggest item and the foundation for thrusters/shields/enemies/progression — best done in its own asmdef with EditMode tests from day one. (Audit F6 + arch-recs 1 & 2.)
- **Project test suite + asmdef split** — runtime/editor/test assembly definitions plus a starter EditMode + PlayMode test suite. Highest leverage when paired with `ConstructModel` above (new domain code, born tested). A full retrofit of `Assembly-CSharp` is low-ROI on its own — start with the new domain layer. (Audit F5.)
- **Split `BuildToolbarController`** — the file is ~900 lines and orchestrates UI construction, input polling, selection state, material memory, flyout, and the Fly button. Split into a `BuildSelectionModel` + per-palette views + a small input adapter. Pick up when build UI next grows (filters, tooltips, gamepad). (Audit F7.)
- **Centralize input ownership** — `FlyController`, `FlyCamera`, and `FlyShootingController` each `new CubeFlyInputActions()`. A small `PlayerInputService` consolidates lifecycle, rebinding, pause behaviour, and UI focus. Pick up when gamepad / rebinding work begins. (Audit arch-rec 5.)
- **Extract thruster / boost into a dedicated system** — boost works today but lives inside `FlyController`. Pull it out into a `ConstructThrusterSystem` that owns thruster scanning, boost resource / cooldown, and HUD / audio / VFX wiring. Worth doing once more thruster types arrive. (Audit arch-rec 4.)
- **Docs as build contract** — explicitly tag each doc as Current behaviour, Accepted next-step spec, or Historical reference, with a docs index in `README.md`. Stops follow-up work from acting on stale roadmap text. (Audit arch-rec 8.)

---

## Ideas (not yet scoped)

A grab-bag of things that have come up in conversation but aren't planned. Nothing here is committed; some won't ever happen. Throw something into [Issues](https://github.com/entewurzelauskuh/CosmicScrapClub/issues) if you want to advocate for one.

Repair node cubes · cube-blueprint export / import for sharing builds · day/night cycle on the map · asteroid-field map variant · achievements · controller / gamepad support · cube color customization (per-material color picker) · damage decals (visible cracks / scorch marks) · leaderboards · boss encounters · headless build / benchmarking mode · multiplayer (probably never, but the question deserves an honest "probably never" rather than a quiet omission).

---

## Contributing

The project is a small Unity 6.3 LTS / URP demo at the moment. The codebase is around six thousand lines of C# spread across well-bounded MonoBehaviours and ScriptableObjects, and every file in `Assets/Scripts/` is documented in [`docs/full_architecture.md`](docs/full_architecture.md) with its responsibility in one sentence. If you've ever wanted to learn Unity by extending a real project rather than another todo list, this is meant to be a friendly entry point.

Concretely:

1. **[`README.md`](README.md)** explains how to clone, open, and play. Five minutes from `git clone` to flying a ship.
2. **[`docs/cube_fly_spec.md`](docs/cube_fly_spec.md)** is the canonical spec — what the game *is*. Read it to understand what the existing rules are before you change them.
3. **[`docs/full_architecture.md`](docs/full_architecture.md)** is the implementation map — every script, every prefab, every scene. Read it to find where to make a change.
4. **[`docs/weapon_shooting_spec.md`](docs/weapon_shooting_spec.md)** is a deep dive on the shooting system specifically. Mostly relevant if you're working on weapons.

Pick something from **Up Next**, open an Issue saying you're taking it (so we don't double up), and send a PR. We use Copilot's PR reviewer as a second pair of eyes — don't be surprised if it leaves a handful of comments. Address them, push fixups, merge.

No formal style guide yet; match the existing code's voice (small classes, generous comments explaining *why* not *what*, log lines with category tags). If something looks awkward in the docs, fix the docs in the same PR — they're a first-class output.
