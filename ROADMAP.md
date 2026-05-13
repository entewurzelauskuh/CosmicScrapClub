# Roadmap — Cube Fly / Cosmic Scrap Club

A living planning doc. What works today, what we're building next, and where the project is headed. Open an [Issue](https://github.com/entewurzelauskuh/CosmicScrapClub/issues) if you'd like to claim one of the **Up Next** items — or just play around and tell us what you find.

---

## Vision

Cube Fly is a sandbox where you build a flying construct out of cubes, weapons, and (soon) reactors / shields / thrusters, then fly it. You can already place shapes, save / load three constructs, and shoot bullets and rockets in flight; the next chunk of work turns that scaffold into an actual game — damage, destruction, a world to fly through, and ships that have meaningfully different roles.

It's intentionally small in scope (Unity 6.3 LTS, URP, MonoBehaviour-only, no DOTS), pure C# everywhere, and the docs are kept honest so you can read [`full_architecture.md`](full_architecture.md) and immediately know which file does what. If you've been wanting to mess around in a Unity codebase that's neither toy-sized nor incomprehensible, this might be the project for you.

---

## Where we are today

Read [`cube_fly_spec.md`](cube_fly_spec.md) for the canonical product spec and [`full_architecture.md`](full_architecture.md) for the file-by-file implementation map. In a sentence: four scenes (`MainMenu → HangarSelect → BuildScene ⇄ FlyScene`), three save slots, ESC pause overlay, a decoupled Shape × Material build system (Cube / Slope / Pyramid weapon / Cylinder weapon × four armour materials), per-cube HP / Armour / Mass placeholder stats, symmetric face-validity placement rules, mass-driven flight feel, 6-axis flight with pitch/yaw/roll + RMB free-look, a screen-space crosshair, and two functioning weapons (bullets + rockets) selected from a toolbar with digit keys, mouse-wheel cycling, and live reload bars.

The shooting system already places projectiles in the world. The next obvious gap is that nothing happens when those projectiles hit anything.

---

## Up Next

### Combat & Damage Model

- **Hit registration on projectiles** — `Bullet` and `Rocket` need colliders + a Unity physics hit pass. On hit, look up the target's `CubeStats`, apply `damage = max(0, projectile.damage - armourValue)`, subtract from `healthPoints`. Both projectiles already carry a `damage` field — the wiring is what's missing.
- **Cube destruction & death animation** — when a cube's `healthPoints` reaches 0, detach it from its parent in the construct, pick a random direction, disable its collider, drift it slowly across a short distance, then despawn. It's mostly a cinematic effect on top of the existing data flow (`GameData.Remove`, the flood-fill cleanup, etc.). Drifting with collider disabled means it can clip through other geometry — that's intentional, keeps the effect cheap.
- **Crash damage** — when the construct collides with anything else in the world, every cube that participates in the collision takes damage scaled by impact speed (a few points at slow taps, up to ~10 at terminal velocity). Encourages the player to actually *fly* their ship rather than ramming it into stuff with no consequence.
- **End-of-run condition** — if the **alpha cube** (the always-present anchor cube at the origin) reaches 0 HP, the run is over. The cockpit role is already filled by the alpha cube — no new "critical part" concept needed. The actual end-of-run handling (respawn? back to hangar? score screen?) is TBD; we'll figure it out when the rest of the damage model is in.

### Power & Energy

The big foundation block. Three damage-source types, two energy producers/consumers, a shield that interacts with both. Builds compose: dropping a reactor in your ship unlocks energy weapons and shields; losing reactors degrades them in a predictable order.

- **Damage types** — every weapon declares itself as either **projectile** (current bullets and rockets) or **energy** (forthcoming). The damage type travels with the projectile / beam so shields can react to it.
- **Reactor cube** — produces a fixed amount of power per tick. Energy weapons and shields *consume* power; armour and projectile weapons don't. Construct's total power is `sum(reactors.output) - sum(consumers.draw)`; if it goes negative, consumers shut off in priority order (see below).
- **Shield generator cube** — heavy (mass 10), draws power. Each shield cube contributes **+50 shield points** to a single shared shield pool covering the whole construct. Additive, no cap — limited by the mass budget. Shields absorb damage *before* HP. **−10% damage from projectile sources, +10% damage from energy sources** (so a shield is a counter to projectile weapons and a vulnerability against energy ones). Shield points regenerate slowly back up to max after **5 seconds without taking damage**.
- **Power-loss cascade** — when reactors are destroyed and the construct goes power-negative, the priority is **energy weapons stop first, then shields stop**. (Both are at-the-mercy-of-power consumers; weapons go first because losing fire is a survivable inconvenience, losing shields is a death sentence.)
- **First energy weapon: Laser beam** — see *Laser weapon* below. Needs the power system to be in first.

### Laser Weapon

The first energy-type weapon, and the testbed for the damage-type system.

- **Behaviour** — Hold LMB, continuous beam fires in **one direction** (the cube's barrel axis, same placement-face convention as the cylinder weapon). No reload, no per-shot cooldown.
- **Heat mechanic** — A heat value tracks 0–100. Firing increases heat fast. At 100, the laser stops firing and **"Overheated!" flashes three times** somewhere prominent (probably under the crosshair). Heat then drops slowly back toward 0. If the player releases LMB *before* hitting 100, heat drops at a faster rate. So short controlled bursts are sustainable; sustained beam isn't.
- **HUD** — a heat progress bar **below the crosshair**, visible only while the laser is the selected weapon type.
- **Energy-typed damage** — full +10% damage against shields, no special interaction with HP. Pairs naturally with the projectile-vs-energy split above.

### Ship Architecture & Movement

- **Ship classes** — pick one when you create a new slot in HangarSelect. Three classes to start:
  - **Allrounder** — the current defaults (mass cap 100, baseline HP, baseline accel/rotation).
  - **Tank** — higher alpha cube HP, higher mass cap (~200?), proportionally lower base movement responsiveness.
  - **Scout** — lower alpha cube HP, lower mass cap (~60?), higher base movement responsiveness.

  Stored as part of the save slot metadata so the chosen class survives Hangar ↔ Fly transitions and reloads.

- **Minimum responsiveness floor** — the current mass slowdown formula caps at 10% responsiveness because `maxSlowdown = 0.9`. Once Tank class lifts the mass cap above 100, the formula needs an explicit `Mathf.Max(massMultiplier, MIN_RESPONSIVENESS)` floor so a max-out tank build stays controllable (currently you'd hit 4% responsiveness at mass 250 — effectively a brick).

- **Thruster cube** — a non-weapon subsystem. **Boosts acceleration in the direction opposite its placement face** (the convention matches the cylinder weapon: the placement face is what attaches to the construct, the opposite face is the "boost face"). So placing a thruster on the back of the ship with the placement face pointing forward gives you a *frontal* boost. Stacks per-direction: more thrusters facing the same way = faster acceleration toward that direction. Doesn't change `maxSpeed`, only how quickly the ship reaches it.

### World

- **A basic game map** — a single flat square plain with a handful of static cubes scattered above it. Something to fly around, crash into (testbed for *Crash damage*), and shoot at (testbed for *Hit registration*). Specifically *not* an AI sandbox yet — just static target dummies. Replaces the current "fly in an empty void" experience.

---

## Later

These are deferred until the core combat / power / damage loop above is solid. Roughly in the order we'd pick them up.

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
