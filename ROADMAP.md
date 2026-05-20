# Roadmap — Cube Fly / Cosmic Scrap Club

A living planning doc. What works today, what we're building next, and where the project is headed. Open an [Issue](https://github.com/entewurzelauskuh/CosmicScrapClub/issues) if you'd like to claim one of the **Up Next** items — or just play around and tell us what you find.

---

## Vision

Cube Fly is a sandbox where you build a flying construct out of cubes, weapons, and thrusters — and (soon) reactors / shields — then fly it. You can already place shapes, save / load three constructs, fly a physics-driven construct that bounces off the world, shoot bullets and rockets, register hits, blow cubes off targets, take kinetic damage when you crash, boost with thruster cubes, and lose the run when your anchor cube dies. The next chunk of work lays in the power / shield / energy-weapon foundation.

It's intentionally small in scope (Unity 6.3 LTS, URP, MonoBehaviour-only, no DOTS), pure C# everywhere, and the docs are kept honest so you can read [`full_architecture.md`](full_architecture.md) and immediately know which file does what. If you've been wanting to mess around in a Unity codebase that's neither toy-sized nor incomprehensible, this might be the project for you.

---

## Where we are today

Read [`cube_fly_spec.md`](cube_fly_spec.md) for the canonical product spec and [`full_architecture.md`](full_architecture.md) for the file-by-file implementation map. In a sentence: four scenes (`MainMenu → HangarSelect → BuildScene ⇄ FlyScene`), three save slots, three ship classes (Allrounder / Tank / Scout) chosen per slot, ESC pause overlay, a decoupled Shape × Material build system (Cube / Slope / Pyramid weapon / Cylinder weapon / Thruster utility × four armour materials), per-cube HP / Armour / Mass stats, symmetric face-validity placement rules, Rigidbody-driven 6-axis flight with real bouncing off the world and an adaptive third-person camera, a Left-Ctrl boost mechanic fed by thruster cubes, a screen-space crosshair, two functioning weapons (bullets + rockets) selected from a toolbar with digit keys and mouse-wheel cycling, Speed + HP + Boost HUD readouts, a basic 200×200 world map seeded with 20 target dummies, projectile hit registration with armour-aware damage, an outward-drift cube destruction animation, kinetic crash damage on collision, and an end-of-run condition when the alpha cube dies.

### Shipped since the last roadmap pass

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

In running order. Ship classes, the thruster / boost mechanic, the combat-damage loop and the Rigidbody foundation are done; from here it's the Power & Energy block, then the energy weapon.

### Power & Energy

The big foundation block. Three damage-source types, two energy producers/consumers, a shield that interacts with both. Builds compose: dropping a reactor in your ship unlocks energy weapons and shields; losing reactors degrades them in a predictable order.

- **Damage types** — every weapon declares itself as either **projectile** (current bullets and rockets) or **energy** (forthcoming). The damage type travels with the projectile / beam so shields can react to it. The existing `CubeStats.TakeRawDamage` covers the third type (kinetic / crash damage) and gives us a template for how the type system gets wired in.
- **Reactor cube** — produces a fixed amount of power per tick. Energy weapons and shields *consume* power; armour and projectile weapons don't. Construct's total power is `sum(reactors.output) - sum(consumers.draw)`; if it goes negative, consumers shut off in priority order (see below).
- **Shield generator cube** — heavy (mass 10), draws power. Each shield cube contributes **+50 shield points** to a single shared shield pool covering the whole construct. Additive, no cap — limited by the mass budget. Shields absorb damage *before* HP. **−10% damage from projectile sources, +10% damage from energy sources** (so a shield is a counter to projectile weapons and a vulnerability against energy ones). Shield points regenerate slowly back up to max after **5 seconds without taking damage**.
- **Power-loss cascade** — when reactors are destroyed and the construct goes power-negative, the priority is **energy weapons stop first, then shields stop**.

### Laser Weapon

The first energy-type weapon, and the testbed for the damage-type system. Depends on **Power & Energy** above.

- **Behaviour** — Hold LMB, continuous beam fires in **one direction** (the cube's barrel axis, same placement-face convention as the cylinder weapon). No reload, no per-shot cooldown.
- **Heat mechanic** — A heat value tracks 0–100. Firing increases heat fast. At 100, the laser stops firing and **"Overheated!" flashes three times** somewhere prominent (probably under the crosshair). Heat then drops slowly back toward 0. If the player releases LMB *before* hitting 100, heat drops at a faster rate. So short controlled bursts are sustainable; sustained beam isn't.
- **HUD** — a heat progress bar **below the crosshair**, visible only while the laser is the selected weapon type.
- **Energy-typed damage** — full +10% damage against shields, no special interaction with HP. Pairs naturally with the projectile-vs-energy split above.

---

## Later

These are deferred until the active sections above are largely done. Roughly in the order we'd pick them up.

- **More weapon variants** — homing missile, shotgun, mine layer, etc. (Each is a small `WeaponBehavior` subclass — the architecture already supports it.)
- **Audio + SFX pass** — engine hum, weapon SFX, impact thuds, ambient. The project is silent today.
- **Visual effects pass** — engine trails, muzzle flashes, projectile trails, explosion particles. Cheap polish, big perceived-quality win.
- **AI-controlled enemy ships** — start with simple chase-and-shoot drones. Reuses the existing construct rebuild path (an AI ship is just a `ConstructSave` driven by a different controller).
- **Game modes** — once AI enemies exist, real game modes become possible: wave survival, time trial, escort, etc. Big design question, comes later.
- **Settings menu (functional, not placeholder)** — volume, FOV, mouse sensitivity, key rebinding. The Settings button on MainMenu currently logs and does nothing.
- **Photo mode in flight** — pause-overlay variant: free camera, hide HUD, screenshot to a folder. Trivial to implement (the camera is already pause-aware) and great for sharing builds.
- **Sensor cubes + fog of war** — sensor cube extends the player's draw distance / awareness radius. Requires a fog-of-war / dynamic-visibility system, which is its own substantial piece of work — both are paired for "much later."
- **Save format versioning / migration** — until then, schema changes break old saves with no remorse (`ConstructSave.version > CurrentVersion` is rejected). Becomes important once people care about their builds across game-versions.

### Architecture & infrastructure

Refactors and tooling deferred from the 2026-05-17 codebase audit (`CODEBASE_REVIEW_AUDIT.txt`). Pure infra — none of these change what the game *does*; they make adding the gameplay above cheaper and safer.

- **`ConstructModel` domain layer** — extract a Unity-independent construct model (cells, face-aware connectivity, mass/stat aggregation, save/load, events) out of `GameData`'s static facade. Cleanly separates the saved design from the runtime flight body and makes the flight-snapshot a first-class concept rather than the workaround it is today. The biggest item and the foundation for thrusters/shields/enemies/progression — best done in its own asmdef with EditMode tests from day one. (Audit F6 + arch-recs 1 & 2.)
- **Project test suite + asmdef split** — runtime/editor/test assembly definitions plus a starter EditMode + PlayMode test suite. Highest leverage when paired with `ConstructModel` above (new domain code, born tested). A full retrofit of `Assembly-CSharp` is low-ROI on its own — start with the new domain layer. (Audit F5.)
- **Damage / `HitContext` model** — introduce a `DamageRequest` / `HitContext` (source, target, amount, type, impulse, point, normal, flags) before adding shields, resistances, splash falloff, or enemies. A likely prerequisite for the **Power & Energy** block in Up Next. (Audit arch-rec 7.)
- **Split `BuildToolbarController`** — the file is ~900 lines and orchestrates UI construction, input polling, selection state, material memory, flyout, and the Fly button. Split into a `BuildSelectionModel` + per-palette views + a small input adapter. Pick up when build UI next grows (filters, tooltips, gamepad). (Audit F7.)
- **HUD / canvas consolidation** — many self-bootstrapped canvases today (corner UI, pause, game over, build toolbar, ship-class dropdown, crosshair, weapon toolbar, speed, HP, boost). One HUD root per mode (or a shared bootstrapper) for sorting / scaling / theming / EventSystem ownership. Pick up when HUD next gains a screen. (Audit F8 + arch-rec 6.)
- **Centralize input ownership** — `FlyController`, `FlyCamera`, and `FlyShootingController` each `new CubeFlyInputActions()`. A small `PlayerInputService` consolidates lifecycle, rebinding, pause behaviour, and UI focus. Pick up when gamepad / rebinding work begins. (Audit arch-rec 5.)
- **Extract thruster / boost into a dedicated system** — boost works today but lives inside `FlyController`. Pull it out into a `ConstructThrusterSystem` that owns thruster scanning, boost resource / cooldown, and HUD / audio / VFX wiring. Worth doing once more thruster types arrive. (Audit arch-rec 4.)
- **Docs as build contract** — explicitly tag each doc as Current behaviour, Accepted next-step spec, or Historical reference, with a docs index in `README.md`. Stops follow-up work from acting on stale roadmap text. (Audit arch-rec 8.)

---

## Ideas (not yet scoped)

A grab-bag of things that have come up in conversation but aren't planned. Nothing here is committed; some won't ever happen. Throw something into [Issues](https://github.com/entewurzelauskuh/CosmicScrapClub/issues) if you want to advocate for one.

Repair node cubes · cube-blueprint export / import for sharing builds · day/night cycle on the map · asteroid-field map variant · achievements · controller / gamepad support · cube color customization (per-material color picker) · damage decals (visible cracks / scorch marks) · leaderboards · boss encounters · headless build / benchmarking mode · multiplayer (probably never, but the question deserves an honest "probably never" rather than a quiet omission).

---

## Contributing

The project is a small Unity 6.3 LTS / URP demo at the moment. The codebase is around six thousand lines of C# spread across well-bounded MonoBehaviours and ScriptableObjects, and every file in `Assets/Scripts/` is documented in [`full_architecture.md`](full_architecture.md) with its responsibility in one sentence. If you've ever wanted to learn Unity by extending a real project rather than another todo list, this is meant to be a friendly entry point.

Concretely:

1. **[`README.md`](README.md)** explains how to clone, open, and play. Five minutes from `git clone` to flying a ship.
2. **[`cube_fly_spec.md`](cube_fly_spec.md)** is the canonical spec — what the game *is*. Read it to understand what the existing rules are before you change them.
3. **[`full_architecture.md`](full_architecture.md)** is the implementation map — every script, every prefab, every scene. Read it to find where to make a change.
4. **[`weapon_shooting_spec.md`](weapon_shooting_spec.md)** is a deep dive on the shooting system specifically. Mostly relevant if you're working on weapons.

Pick something from **Up Next**, open an Issue saying you're taking it (so we don't double up), and send a PR. We use Copilot's PR reviewer as a second pair of eyes — don't be surprised if it leaves a handful of comments. Address them, push fixups, merge.

No formal style guide yet; match the existing code's voice (small classes, generous comments explaining *why* not *what*, log lines with category tags). If something looks awkward in the docs, fix the docs in the same PR — they're a first-class output.
