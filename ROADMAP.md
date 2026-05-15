# Roadmap — Cube Fly / Cosmic Scrap Club

A living planning doc. What works today, what we're building next, and where the project is headed. Open an [Issue](https://github.com/entewurzelauskuh/CosmicScrapClub/issues) if you'd like to claim one of the **Up Next** items — or just play around and tell us what you find.

---

## Vision

Cube Fly is a sandbox where you build a flying construct out of cubes, weapons, and (soon) reactors / shields / thrusters, then fly it. You can already place shapes, save / load three constructs, shoot bullets and rockets in flight, register hits, blow cubes off enemies, and take damage when you crash into things. The next chunk of work closes out the combat-damage loop (alpha death = end of run), upgrades the construct to a real physics object (no more phasing through the ground), gives ships meaningfully different classes, and lays in the power / shield / energy-weapon foundation.

It's intentionally small in scope (Unity 6.3 LTS, URP, MonoBehaviour-only, no DOTS), pure C# everywhere, and the docs are kept honest so you can read [`full_architecture.md`](full_architecture.md) and immediately know which file does what. If you've been wanting to mess around in a Unity codebase that's neither toy-sized nor incomprehensible, this might be the project for you.

---

## Where we are today

Read [`cube_fly_spec.md`](cube_fly_spec.md) for the canonical product spec and [`full_architecture.md`](full_architecture.md) for the file-by-file implementation map. In a sentence: four scenes (`MainMenu → HangarSelect → BuildScene ⇄ FlyScene`), three save slots, ESC pause overlay, a decoupled Shape × Material build system (Cube / Slope / Pyramid weapon / Cylinder weapon × four armour materials), per-cube HP / Armour / Mass placeholder stats, symmetric face-validity placement rules, mass-driven flight feel, 6-axis flight with pitch/yaw/roll + RMB free-look, a screen-space crosshair, two functioning weapons (bullets + rockets) selected from a toolbar with digit keys and mouse-wheel cycling, a basic 200×200 world map seeded with 20 target dummies, projectile hit registration with armour-aware damage, an outward-drift cube destruction animation, and kinetic crash damage that bypasses armour.

### Shipped since the last roadmap pass

- Projectile hit registration — swept raycasts on `Bullet` and `Rocket`, self-construct filtering, armour-aware damage via `CubeStats.TakeDamage`.
- Basic world map — 200×200 ground plane plus 20 hand-placed rusty-orange `WorldTargetCube` dummies in `FlyScene`.
- Cube destruction & death animation — at-zero-HP cubes detach, disable colliders, drift outward at ~2 u/s for 2 s, then despawn.
- Crash damage — per-cube swept `BoxCast` each `FixedUpdate`, entry-only damage gating, armour-bypass kinetic damage via `CubeStats.TakeRawDamage`. Player ship cubes can now actually die.

---

## Up Next

In running order. We finish closing out the combat-damage loop, then do the Rigidbody refactor as a foundation upgrade, then layer everything else on top.

### Combat & Damage Model (one item left)

- **End-of-run condition** — when the alpha cube reaches 0 HP, the run ends. The cockpit role is already filled by the alpha cube — no new "critical part" concept needed. The actual end-of-run handling (respawn? back to hangar? score screen?) is TBD; we'll figure it out when we get there. Until this lands, the alpha takes damage but doesn't die (the existing `CubeDeath` defensively skips it).

### Flight & Movement

- **🆕 Rigidbody-driven construct** — replace the current transform-based flight with the canonical Unity pattern: one `Rigidbody` on the construct root, individual `Collider`s on each cube (no per-cube Rigidbody). `FlyController` rewrites to apply forces (`AddRelativeForce` for thrust, `AddRelativeTorque` for pitch / roll, world torque for yaw) instead of writing `transform.position` / `Rotate` directly. `CollisionDetectionMode.Continuous` to prevent tunneling at top speed; `PhysicMaterial`s on Ground + WorldTargetCubes for friction and bounciness. This is the foundation for "actually bouncing off things" (no more phasing through the ground), unlocks proper inertia for ship classes, and **lets crash damage use `OnCollisionEnter` / `ContactPoint.thisCollider` to charge damage to the specific cube that actually touched the wall** — instead of every cube on the construct registering a swept-cast hit. Expect to re-tune all flight parameters (force magnitudes ≠ acceleration rates, drag uses `Rigidbody.drag`, mass-driven responsiveness comes naturally from `Rigidbody.mass`).

- **Ship classes** — pick one when you create a new slot in HangarSelect. Three classes to start:
  - **Allrounder** — the current defaults.
  - **Tank** — higher alpha cube HP, higher mass cap (~200?), proportionally lower base movement responsiveness.
  - **Scout** — lower alpha cube HP, lower mass cap (~60?), higher base movement responsiveness.

  Stored as part of the save slot metadata so the chosen class survives Hangar ↔ Fly transitions and reloads.

- **Minimum responsiveness floor** — once Tank class lifts the mass cap above 100, the current `maxSlowdown = 0.9` formula caps at 10% responsiveness; a max-out tank build would hit 4% responsiveness at mass 250 (a brick). Add an explicit `Mathf.Max(massMultiplier, MIN_RESPONSIVENESS)` floor. The Rigidbody refactor changes what "responsiveness" means structurally (it falls out of `Rigidbody.mass` rather than a manual multiplier), but the same floor concept applies — heavy ship should still be controllable.

- **Thruster cube** — a non-weapon subsystem. **Boosts acceleration in the direction opposite its placement face** (the convention matches the cylinder weapon: the placement face is what attaches to the construct, the opposite face is the "boost face"). So placing a thruster on the back of the ship with the placement face pointing forward gives you a *frontal* boost. Stacks per-direction: more thrusters facing the same way = faster acceleration toward that direction. Doesn't change `maxSpeed`, only how quickly the ship reaches it. Post-Rigidbody refactor, this just adds force in the boost direction.

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
