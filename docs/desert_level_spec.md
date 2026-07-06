# Desert Level — Design Spec (integrated FlyScene level)

## 1. Purpose

The desert is a **playable level inside `FlyScene`**, flown with the game's real
construct/ship flight system (`FlyController` physics) — a vast, cel-shaded,
Valley-of-Fire basin that plays as a combat arena. It began as a standalone
demonstrator (`explore/desert-level`) and was **integrated into the game** across
the arc A1–A4 (ROADMAP §4); this spec describes the landed, integrated level.

The per-sub-phase design + implementation records live under
`docs/superpowers/specs|plans/2026-*-desert-flyscene-*`.

## 2. What shipped

- A **500 × 500 u** desert basin in `FlyScene`, centred on the origin, on a
  dedicated **`World`** layer (slot 9) so projectile raycasts don't treat terrain
  as targets.
- A procedural **dune ground** (`DesertGround_500.asset`) + a **64-piece perimeter
  ridge** closing the basin + **five hand-built ProBuilder hero formations**.
- A **combat layout**: ~21 destructible target cubes in curated clusters at the
  formations plus a thin plain scatter, and 3 turrets at strongpoints.
- A **cel look** (cel-shaded rock + screen-space outline + warm grade) that the
  player toggles **live from the Settings menu**, against the game's default look.
- **Desert-scene-local** long shadows so the 500 u basin doesn't force its shadow
  cost/softness onto the other scenes.
- The construct spawns in the open central plain facing the Mesa+Arch hero
  formation; the existing ship flight controls drive the camera (no bespoke
  camera).

## 3. Design decisions (carried from the demonstrator, updated at scale)

| Decision | Choice |
|---|---|
| Flight relationship | **Fly-through** — geometry is a navigable obstacle course flown by the construct |
| Basin size | **500 × 500 u** (scaled up from the original 200 in A1.5), centred on origin |
| Terrain character | Open rolling dunes + five hero formation clusters around a vast open central plain |
| Geometry toolchain | All-mesh: procedural ground mesh (`DuneGroundGenerator`) + ProBuilder formations. **No Unity Terrain** (heightfields can't do arches/overhangs; would force a second shader) |
| Shading | One hand-written URP cel shader (`CelShaded`) on ground + rock; the ship keeps its Lit materials (reads fine under the outline — no ship-cel needed) |
| Outline | One screen-space depth+normal edge-detect render feature (`OutlineEdgeDetect` on `Desert_Renderer`), **distance-scaled** so far crowded edges don't blob |
| Shadows | Min-light shader floor (no pure black) + a desert-local pipeline (300 u distance) so far formations cast shadows without pop |
| Assets | All self-authored / procedural (CC0/public-domain permitted); no other third-party assets |

## 4. Geometry

### 4.1 Ground
`DuneGroundGenerator.cs` (+ its editor "Generate" button) builds a `size × res`
grid displaced by layered Perlin noise (swell + dune + ripple), baked to
`Assets/Models/DesertGround_500.asset` (500 u, res 600, ~361 k verts, `UInt32`
index). Smooth-shaded, `MeshCollider`. (The generator's in-place "Generate" button
`CopySerialized`s onto the *bound* mesh asset — regenerate to a **new** path to
avoid clobbering.)

### 4.2 Formations
Five hero clusters, hand-built in ProBuilder (faceted low-poly against the smooth
sand), each a group of `MeshCollider`-bearing meshes, scaled `(1.2, 1.32, 1.2)` and
pushed to the rim in A1.5. Bundled (with the ground + ridge) into
`Assets/Prefabs/Desert/DesertEnvironment.prefab`.

| Archetype | Role | Navigable sizing (at 500 scale) |
|---|---|---|
| **Mesa + Arch** (hero) | flat-topped mesas + a fly-through arch | arch opening ≈ 20 u |
| **Slot Canyon** | narrow winding corridor | inner gap ≈ 17 u |
| **Fin Field** | tall thin blades to weave | gaps ~15–25 u |
| **Hoodoo Spires** | clustered pillars to slalom | ~15–30 u spacing |
| **Butte Ring** | rock ring around an open bowl — a landmark arena | bowl ≈ 60–80 u |

### 4.3 Boundary
No walls — the perimeter dunes build into a continuous **64-piece ridge** (~256 u
radius) that closes the basin on all headings and physically contains side flight
via its colliders. Vertical flight is unbounded (acceptable for the level).

## 5. Combat (A2)

- **Targets:** `Assets/Prefabs/Desert/DesertTarget.prefab` — a variant of
  `WorldTargetCube` (`CubeStats` HP 30 / AV 0, on the `PlacedCube` layer so the
  existing projectile masks hit it; damage via `CubeStats.TakeDamage`) + a
  `SurfaceSnap` placer. ~21 placed: 3–4 per formation cluster + a thin plain scatter
  kept ≥ 40 u clear of spawn.
- **Turrets:** `Assets/Prefabs/Desert/Turret.prefab` — the same variant + the
  existing `AutoTurret` (fires a `Bullet` along local **+Y**, 1 s / 40 dmg, no
  tracking). 3 placed: 2 holding the Butte Ring arena, 1 at the Slot-Canyon mouth.
- **Placement seating:** `SurfaceSnap` (a generalisation of the construct's spawn
  placer) raycasts each object onto the dunes at runtime, so authoring is XZ-only.
- All combat content lives in a `DesertTargets` container in FlyScene (not the
  environment prefab), authored by hand.
- **Known cosmetic:** because `AutoTurret` fires along local +Y, aiming a turret
  tilts its cube body — accepted as-is.

## 6. Cel look + live toggle (A3)

- **Materials:** the rock/ground materials (`Sand`, `RedSandstone`, `Limestone`,
  `OxidizedRock`) use `Assets/Shaders/CelShaded.shader` — banded lighting ramp,
  `_MinLight` floor (~0.3, no pure black), procedural two-tone break-up + strata
  banding. Sky is a gradient skybox (`GradientSkybox.shader` / `DesertSky.mat`).
- **Outline:** `OutlineRendererFeature` (`Desert_Renderer`) — depth+normal
  Roberts-cross edge detect, black, injected before post-processing;
  **distance-scaled thickness** (`thicknessFalloffStart` 35 u, `minThicknessScale`
  0.2) so far edges stay crisp without blobbing.
- **Grade:** a global `DesertLook` Volume with `DesertVolumeProfile` (warm
  contrast/saturation + light bloom). *(`Desert_Renderer` was given the
  `PostProcessData` it was missing so the grade renders under the cel renderer.)*
- **Toggle:** `CelLookSettings` (Core, PlayerPrefs `desert.celLook`, default on) is
  the persisted preference; `DesertLookController` (on FlyScene's `DesertLook` GO)
  applies it — `SetRenderer` (`PC_Renderer` ↔ `Desert_Renderer`) + Volume weight —
  on load and whenever it changes; a **"Cel look (desert)"** toggle in the
  `SettingsMenu` debug panel flips it live for in-game A/B.

## 7. Atmosphere & shadows

- Warm **distance fog** (linear, ~180 → 520 u for the longer sightlines) + warm
  ambient + the `DesertSky` skybox — all under the current renderer.
- **Desert-local shadow pipeline (A4):** `Assets/Settings/Desert_RPAsset.asset`
  (a duplicate of `PC_RPAsset` with `shadowDistance = 300`) is made active only
  while FlyScene is loaded by `ScenePipelineOverride` (Core) on the `DesertLook` GO;
  the global `PC_RPAsset` is restored to `shadowDistance = 50`. Far formations cast
  shadows in the desert without softening Menu/Hangar/Build.

## 8. The integration arc (history)

| Sub-phase | Outcome | What landed |
|---|---|---|
| **A1** | iterate | Terrain into FlyScene: `DesertEnvironment` prefab on the `World` layer, spawn placer, desert atmosphere; removed the old flat arena |
| **A1.5** | ship | Scaled the basin 200 → 500 (`DesertGround_500`), formations to the rim, ridge 20 → 64, spawn to the central plain |
| **A2** | ship | Combat layout: `DesertTarget` + `Turret` variants + `SurfaceSnap`; 21 targets + 3 turrets |
| **A3** | ship | Cel look adopted as a live Settings toggle; fixed `Desert_Renderer` post-FX; distance-scaled the outline |
| **A4** | land | Dropped the exploration scaffolding, made shadows desert-local, rewrote these docs, merged to `main` |

Details: `docs/superpowers/specs/2026-*-desert-flyscene-*-design.md` and the paired plans.

## 9. File manifest (integrated level)

**Shaders** — `Assets/Shaders/`: `CelShaded.shader`, `OutlineEdgeDetect.shader`, `GradientSkybox.shader`.
**Scripts** — `Assets/Scripts/Desert/`: `OutlineRendererFeature.cs`, `DuneGroundGenerator.cs` (+ `Editor/`), `SurfaceSnap.cs`, `DesertLookController.cs`. `Assets/Scripts/Core/`: `CelLookSettings.cs`, `ScenePipelineOverride.cs`.
**Settings** — `Assets/Settings/`: `Desert_Renderer.asset`, `Desert_RPAsset.asset`, `DesertVolumeProfile.asset`; `PC_RPAsset.asset` (renderer list carries `Desert_Renderer`; `shadowDistance` 50).
**Materials** — `Assets/Materials/Desert/`: `Sand`, `RedSandstone`, `Limestone`, `OxidizedRock`, `DesertSky`.
**Geometry / prefabs** — `Assets/Models/DesertGround_500.asset`; `Assets/Prefabs/Desert/`: `DesertEnvironment`, `PerimeterRidge`, the 5 formations, `DesertTarget`, `Turret`.
**Scene** — `Assets/Scenes/FlyScene.unity` hosts the `DesertEnvironment` instance, the `DesertTargets` container, and the `DesertLook` GO (Volume + `DesertLookController` + `ScenePipelineOverride`).

## 10. Definition of done (landed)

Flying `FlyScene`:
- The construct spawns clear on the central plain and flies the basin; the arch,
  slot canyon and fin gaps are navigable; the ridge contains the basin.
- Targets take damage and destroy; turrets return fire and are themselves
  destructible; the Butte Ring reads as a defended arena.
- The **Cel look** toggle (Settings) flips the cel/outline/grade look live, default
  on; the choice persists; far outlines don't blob.
- Shadows: far formations cast in FlyScene (300 u); Menu/Hangar/Build render crisp
  (50 u) again, restored on leaving FlyScene.
- The exploration scaffolding (DesertSandbox, FreeFlyCamera, the 200 u ground) is
  gone; nothing references it.
