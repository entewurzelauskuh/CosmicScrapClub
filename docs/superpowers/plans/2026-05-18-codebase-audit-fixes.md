# Codebase Audit Fixes — Batch 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the six concrete findings from the 2026-05-17 codebase review audit (`CODEBASE_REVIEW_AUDIT.txt`) — build-mode connectivity (F1), stale flight mass (F2), projectile sweep saturation (F3), documentation drift (F4), the BuildScene hint font (F9), and the `.gitignore` gaps (F11).

**Architecture:** Each finding is an independent, contained fix — no shared files between findings.
- **F1** extracts a shared face-aware connectivity helper (`GameData.HasFaceConnection`) and routes both placement validation and build-cleanup through it.
- **F2** adds a static `CubeDeath.CubeDied` event that `FlyController` consumes to re-resolve the construct's Rigidbody mass.
- **F3** enlarges the `ProjectileHit` raycast buffer and warns on saturation.
- **F4** refreshes four root docs against the current implementation.
- **F9 / F11** are one-value / few-line hygiene fixes.

**Tech Stack:** Unity 6.3 LTS (6000.3.11f1), URP, C# MonoBehaviours, new Input System. Verification is by Unity compile-check (Unity MCP `refresh_unity` + `read_console`) and manual BuildScene / FlyScene play-tests — this project has no test framework or asmdefs (that is audit finding **F5**, deliberately out of scope for this plan), so there are no automated-test steps.

**Source:** `CODEBASE_REVIEW_AUDIT.txt` (repo root). The big architectural findings F5–F8 and F10 are intentionally **not** in this plan.

**Branch:** Implement on `fix/codebase-audit-batch-1`, branched from `main`.

---

## Setup

- [ ] **Create the feature branch off `main`**

```bash
git checkout main
git pull
git checkout -b fix/codebase-audit-batch-1
```

- [ ] **Commit this plan**

```bash
git add docs/superpowers/plans/2026-05-18-codebase-audit-fixes.md
git commit -m "Add codebase audit fixes (batch 1) implementation plan"
```

---

## Task 1: F11 — `.gitignore` gaps

The repo's `.gitignore` (the GitHub Unity template) does not ignore `.DS_Store`, `.idea/`, `.vscode/`, `.claude/`, or `*.slnx` — all of which currently show as untracked. (The audit's separate "embedded vs Package-Manager MCP" sub-point is intentionally not addressed here.)

**Files:**
- Modify: `.gitignore`

- [ ] **Step 1: Append the missing entries**

The file currently ends with:
```
# Local construct save slots (project-folder dev path; the SAVES_IN_PROJECT
# scripting define / UNITY_EDITOR routes SaveManager writes here).
/Saves/
```

Append after that block:
```
# macOS desktop-services files
.DS_Store

# IDE / editor state
.idea/
.vscode/

# Visual Studio's newer XML solution format (the older *.sln is ignored above)
*.slnx

# Claude Code agent / tooling state (worktrees, local config)
.claude/
```

- [ ] **Step 2: Verify**

```bash
git status --short
```
Expected: `.DS_Store`, `.idea/`, `.vscode/`, `.claude/`, `My project.slnx`, `Packages/.DS_Store` no longer appear as untracked (`??`) entries.

- [ ] **Step 3: Commit**

```bash
git add .gitignore
git commit -m "Ignore .DS_Store, IDE state, .slnx, and .claude tooling state"
```

---

## Task 2: F9 — stale BuildScene rotate-hint font size

`BuildToolbarController` declares `hintFontSize = 18` (script default) and `hintSize = 220x32`. `BuildScene.unity` serializes an override of `hintFontSize: 50` into the same 220×32 box, which clips/overwhelms the hint. Reset the serialized value to the script default.

**Files:**
- Modify: `Assets/Scenes/BuildScene.unity`

- [ ] **Step 1: Set `hintFontSize` to 18 on the scene's BuildToolbarController**

In Unity:
1. `manage_scene(action="load", path="Assets/Scenes/BuildScene.unity")`.
2. `find_gameobjects(search_term="BuildToolbarController", search_method="by_component")` to locate the GameObject hosting the `BuildToolbarController` component.
3. Use `manage_components` to set the `BuildToolbarController` component's `hintFontSize` property to `18`.

- [ ] **Step 2: Save the scene**

`manage_scene(action="save")`.

- [ ] **Step 3: Verify the diff**

```bash
git diff Assets/Scenes/BuildScene.unity
```
Expected: exactly one change — `hintFontSize: 50` → `hintFontSize: 18`. No other component should change. (If unrelated hunks appear, discard them — only `hintFontSize` should differ.)

- [ ] **Step 4: Commit**

```bash
git add Assets/Scenes/BuildScene.unity
git commit -m "Reset BuildScene rotate-hint font size to the 18 script default"
```

---

## Task 3: F3 — projectile sweep can miss targets when the hit buffer fills

`ProjectileHit.TrySweep` sweeps with a fixed `RaycastHit[8]` via `Physics.RaycastNonAlloc`. `RaycastNonAlloc` returns an **unordered** subset when the path intersects more colliders than the buffer holds — so with 8+ colliders (e.g. a weapon firing through its own construct) the buffer can fill with self-hits and the real target is dropped before the post-raycast self-filter runs. Fix: enlarge the buffer well past any plausible construct cube count, and warn if it ever saturates anyway.

**Files:**
- Modify: `Assets/Scripts/Fly/ProjectileHit.cs`

- [ ] **Step 1: Enlarge the buffer and update its comment**

Find:
```csharp
        // Allocation-amortising buffer for RaycastNonAlloc. 8 is plenty —
        // a single sweep over a one-frame step typically intersects at most
        // 1-2 cubes; 8 covers the pathological "fire straight along a row
        // of cubes" case without ever GC-allocating. RaycastNonAlloc
        // doesn't guarantee distance order, so we sort the populated
        // prefix in place via the insertion sort below.
        static readonly RaycastHit[] s_HitBuffer = new RaycastHit[8];
```
Replace with:
```csharp
        // Allocation-amortising buffer for RaycastNonAlloc. Sized
        // generously: RaycastNonAlloc returns an UNORDERED subset when the
        // path intersects more colliders than the buffer holds, so a
        // too-small buffer can silently drop the real target in favour of
        // nearer self-cube hits. 64 comfortably exceeds any plausible
        // construct cube count along a single one-frame sweep; TrySweep
        // logs a warning if the buffer ever fills anyway. RaycastNonAlloc
        // doesn't guarantee distance order, so we sort the populated
        // prefix in place via the insertion sort below.
        static readonly RaycastHit[] s_HitBuffer = new RaycastHit[64];
```

- [ ] **Step 2: Warn on buffer saturation**

Find:
```csharp
            int n = Physics.RaycastNonAlloc(origin, direction, s_HitBuffer, distance, mask);
            if (n == 0) return false;
```
Replace with:
```csharp
            int n = Physics.RaycastNonAlloc(origin, direction, s_HitBuffer, distance, mask);
            if (n == 0) return false;
            if (n == s_HitBuffer.Length)
                Debug.unityLogger.LogWarning("ProjectileHit",
                    $"Sweep filled the {s_HitBuffer.Length}-hit buffer — RaycastNonAlloc may have " +
                    "dropped colliders, so a valid target could be missed. Enlarge s_HitBuffer.");
```

- [ ] **Step 3: Update the insertion-sort comment**

Find:
```csharp
            // Insertion sort by distance — n ≤ s_HitBuffer.Length = 8, so
            // the O(n²) worst case is bounded at 64 compares; no allocation
            // overhead vs Array.Sort's IComparer machinery, and the code is
            // legible at a glance.
```
Replace with:
```csharp
            // Insertion sort by distance — n ≤ s_HitBuffer.Length = 64, so
            // the O(n²) worst case stays small; no allocation overhead vs
            // Array.Sort's IComparer machinery, and the code is legible at
            // a glance.
```

- [ ] **Step 4: Compile and check the console**

Run (Unity MCP):
1. `refresh_unity(compile="request", scope="scripts", mode="force")`
2. Poll the `mcpforunity://editor/state` resource until `compilation.is_compiling` is `false`.
3. `read_console(types=["error"], count=20)`

Expected: no compilation errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Fly/ProjectileHit.cs
git commit -m "Enlarge projectile hit buffer to 64 and warn on saturation"
```

---

## Task 4: F1 — build cleanup ignores face-validity

`BuildManager.RemoveDanglingCubes` flood-fills connectivity by raw cell occupancy (`GameData.IsOccupied`), while placement validation (`GameData.IsValidAttachment`) uses a stricter *symmetric face-validity* rule. Deleting a connector can therefore strand a chunk that is only occupancy-adjacent through a face neither shape actually has. Fix: extract a shared face-aware helper `GameData.HasFaceConnection` and use it in both places.

Key facts (verified):
- `GameData.Neighbors` is the six axis directions, `internal static readonly` (visible to `BuildManager`, same assembly).
- The alpha cube at `Vector3Int.zero` is **not** in `GameData`'s `_byCell` / `_placedCubes`; it is special-cased as a six-faces-valid cube.
- `GameData.GetPlacementAt` returns `default(Placement)` for an empty cell — `default(Placement).Rotation` is `(0,0,0,0)`, **not** identity — so never face-check an unoccupied cell's placement.
- `ShapeDefinition.IsWorldFaceValid(Vector3Int worldDir, Quaternion rotation)` is the face-flag lookup; a plain cube has all six `face*` flags `true` by default.
- `BuildManager` holds the registry as `[SerializeField] ShapeRegistry shapeRegistry` with no runtime fallback — the helper must degrade safely (pure occupancy) if it is null, so cleanup can never delete the whole build.

**Files:**
- Modify: `Assets/Scripts/Core/GameData.cs` (add `HasFaceConnection`, rewrite `IsValidAttachment` to use it)
- Modify: `Assets/Scripts/Build/BuildManager.cs` (rewrite `RemoveDanglingCubes` to use it)

- [ ] **Step 1: Add `HasFaceConnection` and rewrite `IsValidAttachment` in `GameData.cs`**

Find the entire current `IsValidAttachment` method **including its doc comment** (the block beginning `// Symmetric face-validity check.` and ending with the method's closing brace):
```csharp
        // Symmetric face-validity check. A placement at `cell` with
        // (shape, rotation) is valid when, for at least one of the six
        // cell-face neighbours, BOTH:
        //   • the new piece has a real surface on the face pointing at
        //     that neighbour (in its rotation), AND
        //   • the neighbour piece has a real surface on the face pointing
        //     back at us (in its rotation).
        //
        // The alpha cube at the origin counts as a cube — all six faces
        // valid. Empty cells are skipped. The check reduces to the old
        // "any face-adjacent cell is occupied" rule for all-cube
        // constructs, since cubes have all six faces valid.
        public static bool IsValidAttachment(Vector3Int cell, int newShapeIndex,
            Quaternion newRotation, ShapeRegistry shapeRegistry)
        {
            if (shapeRegistry == null) return false;
            ShapeDefinition newShape = shapeRegistry.Get(newShapeIndex);
            if (newShape == null) return false;

            for (int i = 0; i < Neighbors.Length; i++)
            {
                Vector3Int dir = Neighbors[i];
                Vector3Int neighborCell = cell + dir;

                bool isAlpha = neighborCell == Vector3Int.zero;
                if (!isAlpha && !IsOccupied(neighborCell)) continue;

                // 1. New piece's face toward the neighbour must be valid.
                if (!newShape.IsWorldFaceValid(dir, newRotation)) continue;

                // 2. Neighbour's face toward us must be valid. The alpha
                //    cube is a cube → all six trivially valid.
                if (isAlpha) return true;

                Placement neighborPlacement = GetPlacementAt(neighborCell);
                ShapeDefinition neighborShape = shapeRegistry.Get(neighborPlacement.ShapeIndex);
                if (neighborShape == null) continue;
                if (!neighborShape.IsWorldFaceValid(-dir, neighborPlacement.Rotation)) continue;

                return true;
            }
            return false;
        }
```
Replace with:
```csharp
        // Face-aware connectivity primitive — shared by placement
        // validation (IsValidAttachment) and build cleanup
        // (BuildManager.RemoveDanglingCubes) so both judge "connected"
        // by the same rule.
        //
        // Returns true when a piece (sourceShape, sourceRotation) at
        // `cell` is face-connected to the cell-face neighbour in
        // direction `dir`: the neighbour cell must be occupied (or the
        // alpha cube at the origin), AND both touching faces must carry a
        // real surface — the source's face toward the neighbour and the
        // neighbour's face back toward the source.
        //
        // The alpha cube is a six-faces-valid cube; pass sourceShape ==
        // null to treat the SOURCE as the alpha. A null shapeRegistry
        // disables the face checks and falls back to pure occupancy (the
        // pre-face-aware behaviour) so cleanup degrades safely if the
        // registry is unset rather than pruning the whole build.
        public static bool HasFaceConnection(Vector3Int cell, ShapeDefinition sourceShape,
            Quaternion sourceRotation, Vector3Int dir, ShapeRegistry shapeRegistry)
        {
            Vector3Int neighborCell = cell + dir;
            bool neighborIsAlpha = neighborCell == Vector3Int.zero;
            if (!neighborIsAlpha && !IsOccupied(neighborCell)) return false;

            // No registry → can't read face flags; fall back to pure
            // occupancy so a missing registry can't delete the build.
            if (shapeRegistry == null) return true;

            // Source's face toward the neighbour. A null sourceShape is
            // the alpha cube — all six faces valid.
            if (sourceShape != null && !sourceShape.IsWorldFaceValid(dir, sourceRotation))
                return false;

            if (neighborIsAlpha) return true;

            Placement neighborPlacement = GetPlacementAt(neighborCell);
            ShapeDefinition neighborShape = shapeRegistry.Get(neighborPlacement.ShapeIndex);
            if (neighborShape == null) return false;
            return neighborShape.IsWorldFaceValid(-dir, neighborPlacement.Rotation);
        }

        // Symmetric face-validity check for PLACEMENT. A placement at
        // `cell` with (shape, rotation) is valid when it is face-connected
        // (HasFaceConnection) to at least one of its six cell-face
        // neighbours. The alpha cube at the origin counts as a cube — all
        // six faces valid. The check reduces to the old "any face-adjacent
        // cell is occupied" rule for all-cube constructs, since cubes have
        // all six faces valid.
        public static bool IsValidAttachment(Vector3Int cell, int newShapeIndex,
            Quaternion newRotation, ShapeRegistry shapeRegistry)
        {
            if (shapeRegistry == null) return false;
            ShapeDefinition newShape = shapeRegistry.Get(newShapeIndex);
            if (newShape == null) return false;

            for (int i = 0; i < Neighbors.Length; i++)
            {
                if (HasFaceConnection(cell, newShape, newRotation, Neighbors[i], shapeRegistry))
                    return true;
            }
            return false;
        }
```

- [ ] **Step 2: Rewrite `RemoveDanglingCubes` in `BuildManager.cs`**

Find the entire current `RemoveDanglingCubes` method including its doc comment:
```csharp
        // BFS from origin across occupied cells; anything not visited is dangling.
        void RemoveDanglingCubes()
        {
            Debug.unityLogger.Log(TAG, "Running flood-fill to detect dangling cubes.");

            HashSet<Vector3Int> visited = new HashSet<Vector3Int> { Vector3Int.zero };
            Queue<Vector3Int> queue = new Queue<Vector3Int>();
            queue.Enqueue(Vector3Int.zero);

            while (queue.Count > 0)
            {
                Vector3Int curr = queue.Dequeue();
                for (int i = 0; i < GameData.Neighbors.Length; i++)
                {
                    Vector3Int nb = curr + GameData.Neighbors[i];
                    if (visited.Contains(nb)) continue;
                    if (!GameData.IsOccupied(nb)) continue;
                    visited.Add(nb);
                    queue.Enqueue(nb);
                }
            }

            // Snapshot cells before mutation.
            List<Vector3Int> snapshot = new List<Vector3Int>(GameData.PlacedCubes.Count);
            for (int i = 0; i < GameData.PlacedCubes.Count; i++)
                snapshot.Add(GameData.PlacedCubes[i].Cell);

            int removed = 0;
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (!visited.Contains(snapshot[i]))
                {
                    RemoveCell(snapshot[i]);
                    removed++;
                }
            }

            Debug.unityLogger.Log(TAG, $"Flood-fill removed {removed} dangling cube(s).");
        }
```
Replace with:
```csharp
        // BFS from origin across FACE-CONNECTED cells; anything not
        // visited is dangling. Connectivity uses GameData.HasFaceConnection
        // — the same face-aware rule placement validation uses — so the
        // cleanup graph can no longer keep a cube that is merely
        // occupancy-adjacent through a face neither shape actually has.
        void RemoveDanglingCubes()
        {
            Debug.unityLogger.Log(TAG, "Running flood-fill to detect dangling cubes.");

            HashSet<Vector3Int> visited = new HashSet<Vector3Int> { Vector3Int.zero };
            Queue<Vector3Int> queue = new Queue<Vector3Int>();
            queue.Enqueue(Vector3Int.zero);

            while (queue.Count > 0)
            {
                Vector3Int curr = queue.Dequeue();

                // Resolve the current cell's shape + rotation. The origin
                // holds the alpha cube (six-faces-valid, no Placement
                // record) — represented as a null shape, which
                // HasFaceConnection reads as the alpha.
                ShapeDefinition currShape = null;
                Quaternion currRotation = Quaternion.identity;
                if (curr != Vector3Int.zero)
                {
                    Placement p = GameData.GetPlacementAt(curr);
                    currShape = shapeRegistry != null ? shapeRegistry.Get(p.ShapeIndex) : null;
                    currRotation = p.Rotation;
                }

                for (int i = 0; i < GameData.Neighbors.Length; i++)
                {
                    Vector3Int dir = GameData.Neighbors[i];
                    Vector3Int nb = curr + dir;
                    if (visited.Contains(nb)) continue;
                    if (!GameData.HasFaceConnection(curr, currShape, currRotation, dir, shapeRegistry))
                        continue;
                    visited.Add(nb);
                    queue.Enqueue(nb);
                }
            }

            // Snapshot cells before mutation.
            List<Vector3Int> snapshot = new List<Vector3Int>(GameData.PlacedCubes.Count);
            for (int i = 0; i < GameData.PlacedCubes.Count; i++)
                snapshot.Add(GameData.PlacedCubes[i].Cell);

            int removed = 0;
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (!visited.Contains(snapshot[i]))
                {
                    RemoveCell(snapshot[i]);
                    removed++;
                }
            }

            Debug.unityLogger.Log(TAG, $"Flood-fill removed {removed} dangling cube(s).");
        }
```

- [ ] **Step 3: Compile and check the console**

Run (Unity MCP):
1. `refresh_unity(compile="request", scope="scripts", mode="force")`
2. Poll `mcpforunity://editor/state` until `compilation.is_compiling` is `false`.
3. `read_console(types=["error"], count=20)`

Expected: no compilation errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Core/GameData.cs Assets/Scripts/Build/BuildManager.cs
git commit -m "Share a face-aware connectivity rule across placement and cleanup"
```

**Risk note for the verification task:** loaded constructs bypass `IsValidAttachment` (`GameData.LoadFromSave` runs with validation skipped), so `RemoveDanglingCubes` is the first/only enforcement of face-connectivity for a loaded build. A pre-existing save containing a non-face-connected cube may shed that cube on its first build-mode cleanup. That is the rule being enforced consistently — not a regression — but it should be observed during verification (Task 10).

---

## Task 5: F2 — Rigidbody mass is stale after a cube dies in flight

`FlyController` computes Rigidbody mass once at construct build (`ResolveRigidbody` → `ComputeTotalMass`) and never updates it when cubes are destroyed mid-flight — its own `ResolveRigidbody` comment acknowledges this. Fix: `CubeDeath` raises a static `CubeDied` event after a dying cube has detached; `FlyController` subscribes and re-resolves the Rigidbody (recomputing mass and the mass-derived flight factors).

Key facts (verified):
- `CubeDeath` is at `Assets/Scripts/Core/CubeDeath.cs` (namespace `CubeFly.Core`). `BeginDeath` detaches the cube (`SetParent(null)`) and disables its colliders, then starts the drift coroutine. It currently raises nothing. The alpha cube returns early from `BeginDeath` — so `CubeDied` will only ever fire for non-alpha cubes.
- `CubeDamage.ApplyAndLog` runs `GameData.Remove(cell)` **before** `CubeDeath.BeginDeath`, so by the time the event fires `GameData` already excludes the dead cube — `FlyController.ComputeTotalMass` (which sums `GameData.SumPlacedMasses`) returns the correct reduced mass.
- Setting `Rigidbody.mass` after the dying cube's colliders are disabled makes Unity rebuild the inertia tensor / centre of mass from the shrunken compound collider automatically — no manual tensor code needed.
- `FlyController` already has `OnEnable` / `OnDisable` and `using CubeFly.Core;`. `ResolveRigidbody` is null-safe (early-returns if `construct` / `_rb` is null).

**Files:**
- Modify: `Assets/Scripts/Core/CubeDeath.cs` (add the `CubeDied` event)
- Modify: `Assets/Scripts/Fly/FlyController.cs` (subscribe; re-resolve on death)

- [ ] **Step 1: Add the `CubeDied` event to `CubeDeath.cs`**

Find:
```csharp
        bool _dying;

        public void BeginDeath(Vector3 outwardOrigin)
```
Replace with:
```csharp
        bool _dying;

        // Raised once when any non-alpha cube begins its death sequence,
        // AFTER it has detached from its construct and disabled its
        // colliders. FlyController subscribes to recompute the construct's
        // Rigidbody mass. Static so a dying cube needs no reference to its
        // listeners; subscribers MUST unsubscribe (a static event outlives
        // scene loads).
        public static event System.Action CubeDied;

        public void BeginDeath(Vector3 outwardOrigin)
```

- [ ] **Step 2: Invoke `CubeDied` at the end of `BeginDeath`**

Find:
```csharp
            StartCoroutine(DriftAndDespawn(driftDir));
        }
```
Replace with:
```csharp
            StartCoroutine(DriftAndDespawn(driftDir));

            // The cube is now de-parented and its colliders disabled, so
            // listeners (FlyController's mass recompute) observe the
            // construct already shrunk by this cube.
            CubeDied?.Invoke();
        }
```

- [ ] **Step 3: Subscribe / unsubscribe in `FlyController`'s `OnEnable` / `OnDisable`**

Find:
```csharp
        void OnEnable() => _input.Fly.Enable();
        void OnDisable() => _input.Fly.Disable();
        void OnDestroy() => _input?.Dispose();
```
Replace with:
```csharp
        void OnEnable()
        {
            _input.Fly.Enable();
            CubeDeath.CubeDied += OnCubeDied;
        }

        void OnDisable()
        {
            _input.Fly.Disable();
            CubeDeath.CubeDied -= OnCubeDied;
        }

        void OnDestroy() => _input?.Dispose();

        // A cube died in flight (CubeDeath.CubeDied). It is already gone
        // from GameData and detached from the construct, so re-resolving
        // the Rigidbody now recomputes mass + the mass-derived flight
        // factors for the lighter ship (Unity rebuilds the inertia tensor
        // from the shrunken compound collider when rb.mass is set).
        void OnCubeDied() => ResolveRigidbody();
```

- [ ] **Step 4: Make the `ResolveRigidbody` log read sensibly when re-called**

`ResolveRigidbody` now runs both at `Start` and on every cube death. Adjust its log line so the wording fits both. Find:
```csharp
            Debug.unityLogger.Log(TAG,
                $"Rigidbody armed. Total mass: {totalMass:F1} (rb.mass: {_rb.mass:F1}). " +
```
Replace with:
```csharp
            Debug.unityLogger.Log(TAG,
                $"Rigidbody mass resolved. Total mass: {totalMass:F1} (rb.mass: {_rb.mass:F1}). " +
```

- [ ] **Step 5: Compile and check the console**

Run (Unity MCP):
1. `refresh_unity(compile="request", scope="scripts", mode="force")`
2. Poll `mcpforunity://editor/state` until `compilation.is_compiling` is `false`.
3. `read_console(types=["error"], count=20)`

Expected: no compilation errors.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Core/CubeDeath.cs Assets/Scripts/Fly/FlyController.cs
git commit -m "Recompute construct Rigidbody mass when a cube dies in flight"
```

---

## F4 — Documentation refresh (Tasks 6–9)

Four root docs predate save-v2, ship classes, the Utility shape category, and the shipped thruster + boost mechanic. Each doc gets its own task. **Reference facts** (verified against current code — use these as the ground truth for all four tasks):

- **Logging:** `LogBootstrapper.cs` is a `MonoBehaviour` on `Assets/UI/UICanvas.prefab` (a `DontDestroyOnLoad` singleton, bootstrapped by `UIBootstrap` — *not* a static `BeforeSceneLoad` hook). It writes to `Application.persistentDataPath/Logs/CubeFly_yyyy-MM-dd_HH-mm-ss.log` (not `Logs/runtime-<timestamp>.log`).
- **Save schema:** `ConstructSave.CurrentVersion = 2`. v2 added a `shipClass` string field (stored by name). `SaveSlotInfo` gained a `ShipClass` field. A v1 save (no `shipClass`) loads as `Allrounder`; no migration code is needed.
- **Ship classes:** `ShipClass` enum = `Allrounder` / `Tank` / `Scout`. Per-class alpha HP / mass cap / movement multiplier: Allrounder 100/100/1.0, Tank 200/180/0.7, Scout 60/60/1.4. Chosen via the `BuildShipClassController` dropdown (middle-left of BuildScene), stored per save slot. `BuildManager.MassLimit` is a **computed property** from the active class — there is no serialized `massLimit = 100` constant.
- **Thruster (Utility shape, shipped):** `ShapeCategory` has a third member `Utility`. Assets: `ShapeUtilityThruster.asset` (cone, only `faceNegY` valid, `coupledMaterial` → `ThrusterMatDef`), `PlacedThruster.prefab`, `ThrusterMeshAuthor.cs`, `PrimitiveMeshes.Cone`, `ThrusterMatDef.asset`, `ThrusterMat.mat`. `ShapeDefinition.weaponMaterial` was renamed `coupledMaterial` (with `[FormerlySerializedAs]`); new helpers `IsArmour` / `UsesCoupledMaterial`. ShapeRegistry order: Cube, Slope, WeaponPyramid, WeaponCylinder, UtilityThruster.
- **Boost mechanic (shipped):** `ThrusterBehavior.cs` + `FlyBoostBar.cs`. `FlyController` owns a 0–100 Boost resource (drain 40/s, regen 15/s, overboosted regen 6/s, per-axis ×1.3 thrust, ×1.3 max-speed). New `Boost` input action bound to **Left Ctrl**. `FlyController` exposes `BoostFraction` / `IsOverboosted` / `IsBoostCritical`. `FlyBoostBar` is a HUD bar left of the crosshair with a critical-zone red throb and an "Overboosted!" flash.
- **`CategoryFlyout.cs`:** an extracted plain C# class — a reusable non-armour toolbar category (used for Weapons and now Utilities).
- **`GameOverMenu.cs`:** a real shipped file at `Assets/Scripts/Core/GameOverMenu.cs` (a `BeforeSceneLoad` DDOL singleton end-of-run overlay).

---

## Task 6: F4 — refresh `README.md`

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Apply these corrections**

- Companion-docs list: add `ROADMAP.md`, `thruster_boost_spec.md`, `boost_overboost_tuning_spec.md` (currently lists only `cube_fly_spec.md`, `full_architecture.md`, `weapon_shooting_spec.md`).
- "Shape × Material decoupled" line: the shape list `(Cube, Slope, Pyramid weapon, Cylinder weapon)` → add `Thruster utility`.
- "What's In Here" section: add a bullet for **ship classes** — Allrounder/Tank/Scout chosen via a BuildScene dropdown, stored per save slot, each setting alpha HP + mass cap + movement multiplier.
- "What's In Here" section: add a bullet for the **Thruster cube / boost mechanic** — placeable Utility cone thruster; Left-Ctrl boost; 0–100 Boost resource with overboost lockout; `FlyBoostBar` HUD.
- "Mass budget (cap 100)" → "Mass budget (cap set by ship class — 100 Allrounder / 180 Tank / 60 Scout)"; the live `Mass: X / 100` readout's denominator is the active class's cap, not always 100.
- "File logging" line: `Logs/runtime-<timestamp>.log` → `Application.persistentDataPath/Logs/CubeFly_<timestamp>.log`.
- Flight-controls table: add a row — `Left Ctrl (held)` → "Boost — while commanding thrust along a thrustered axis, ×1.3 acceleration and ×1.3 max-speed until the Boost meter drains."
- `Scripts/Core/` file list: add `ShipClass`, `GameOverMenu`, `ThrusterMeshAuthor`.
- `Scripts/Build/` file list: add `BuildShipClassController`, `CategoryFlyout`.
- `Scripts/Fly/` file list: add `ThrusterBehavior`, `FlyBoostBar`.
- `Shapes/` list: add `ShapeUtilityThruster`. `Materials/Defs/` list: add `ThrusterMatDef`. `Materials/` list: add `ThrusterMat`. `Prefabs/` list: add `PlacedThruster`.

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "Refresh README for ship classes, thruster/boost, save v2, logging"
```

---

## Task 7: F4 — refresh `full_architecture.md`

This is the most out-of-date doc — it needs new table rows for 7 script files plus shape/prefab/material rows and logging/persistence corrections.

**Files:**
- Modify: `full_architecture.md`

- [ ] **Step 1: Apply these corrections**

- Persistence / Logging ASCII boxes: "schema v1" → "schema v2 (+ shipClass)"; "LogBootstrapper (BeforeSceneLoad)" / "FileLogHandler → Logs/runtime-*.log" → "LogBootstrapper (MonoBehaviour on UICanvas.prefab)" / "FileLogHandler → persistentDataPath/Logs/CubeFly_*.log".
- "Persistence model" paragraph: note `GameOverMenu` is also a `BeforeSceneLoad` DDOL singleton, and `LogBootstrapper` is a DDOL `MonoBehaviour` (not a `BeforeSceneLoad` static).
- "On-disk saves" paragraph: note the schema is v2, carrying `shipClass`.
- "Input" paragraph and the Input table's Fly-map row: add `Boost` (Left Ctrl) to the Fly action list.
- Directory-layout blocks: add `PlacedThruster` (Prefabs), `ShapeUtilityThruster` (Shapes), `ThrusterMatDef` (Materials/Defs), `ThrusterMat` (Materials).
- FlyScene scene-table `FlyHUD` row: add `FlyBoostBar` to the hosted components.
- Scripts — Core table: add rows for `ShipClass.cs` (enum + `ShipClassStats` struct + static `ShipClasses` lookup), `GameOverMenu.cs` (DDOL singleton, `[RuntimeInitializeOnLoadMethod]`, end-of-run overlay, `TriggerGameOver`), and `ThrusterMeshAuthor.cs` (assigns `PrimitiveMeshes.Cone` to an empty mesh slot — mirror of the Prism/Pyramid/Cylinder mesh authors).
- `ConstructSave.cs` row: "schema (v1) ... `slotName`, ticks, denormalised totals, `PlacementRecord[]`" → "schema (v2) ... also a `shipClass` name string"; note `SaveSlotInfo` now carries a `ShipClass`.
- `PrimitiveMeshes.cs` row: add `Cone` to the listed meshes.
- `LogBootstrapper.cs` row: change type from "static initialiser / `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`" to "`MonoBehaviour` (DDOL singleton on `UICanvas.prefab`, bootstrapped by `UIBootstrap`)"; output path → `Application.persistentDataPath/Logs/CubeFly_<timestamp>.log`.
- Scripts — Build table: add rows for `BuildShipClassController.cs` (middle-left class-picker dropdown; calls `GameData.SetShipClass` + `BuildManager.OnShipClassChanged`) and `CategoryFlyout.cs` (reusable non-armour toolbar category — plain C# class, one per non-armour category).
- `BuildManager.cs` row: note `MassLimit` is a computed property from the active `ShipClass` and `OnShipClassChanged` re-applies alpha HP.
- `BuildToolbarController.cs` row: "a separate weapons flyout for weapon shapes" → weapon **and** utility shapes, via the `CategoryFlyout` machinery (one instance per non-armour category).
- Scripts — Fly table: add rows for `ThrusterBehavior.cs` (passive descriptor on `PlacedThruster.prefab`, exposes `LocalThrustAxis`) and `FlyBoostBar.cs` (Boost HUD bar, critical-zone red throb, "Overboosted!" flash).
- `FlyController.cs` row: add the Boost resource (0–100, Left-Ctrl `Boost` action, drain/regen, overboosted latch, per-axis ×1.3 thrust, ×1.3 max-speed ceiling with post-boost decay), the `_spawnedThrusters` collection, `BoostFraction`/`IsOverboosted`/`IsBoostCritical`, and that it applies the ship-class movement multiplier.
- `ShapeDefinition.cs` row / prose: `ShapeCategory` is `Armour` / `Weapon` / `Utility`; the coupled field is now `coupledMaterial` (was `weaponMaterial`, `[FormerlySerializedAs]`); helpers `IsArmour` / `UsesCoupledMaterial` exist.
- Prefabs table: add a `PlacedThruster.prefab` row (cone mesh via `ThrusterMeshAuthor` from `PrimitiveMeshes.Cone`, `BoxCollider`, `CubeStats`, `ThrusterBehavior`, layer `PlacedCube`).
- Materials table: add a `ThrusterMat` row. Materials-Defs table: add a `ThrusterMatDef.asset` row (coupled material, not in `MaterialRegistry`). Shapes table: add a `ShapeUtilityThruster.asset` row (`category = Utility`, only `faceNegY` valid, prefab `PlacedThruster`, coupled material `ThrusterMatDef`).
- "Cold start" walkthrough: `LogBootstrapper` is a `MonoBehaviour` on `UICanvas` — logging starts when the first gameplay scene's `UIBootstrap` instantiates the canvas, not before any scene loads. (`PauseMenu` / `GameOverMenu` are the actual `BeforeSceneLoad` bootstraps.)

- [ ] **Step 2: Commit**

```bash
git add full_architecture.md
git commit -m "Refresh full_architecture for ship classes, thruster/boost, save v2"
```

---

## Task 8: F4 — refresh `cube_fly_spec.md`

**Files:**
- Modify: `cube_fly_spec.md`

- [ ] **Step 1: Apply these corrections**

- "On-disk format (`ConstructSave`, version `1`)": heading → "version `2`"; the code block's `public const int CurrentVersion = 1;` → `= 2`; add `public string shipClass = string.Empty;` to the `ConstructSave` block.
- "Schema rules": add a rule — `shipClass` stored by name; a v1 save (no `shipClass`) loads as `Allrounder`; no migration code needed.
- "Slot metadata": `SaveSlotInfo` also carries a `ShipClass`.
- `ShapeDefinition` code block: `enum ShapeCategory { Armour, Weapon }` → `{ Armour, Weapon, Utility }`; field `weaponMaterial` → `coupledMaterial`; add `IsArmour` / `UsesCoupledMaterial` to the listed members.
- "Shipped shapes" table: add `ShapeUtilityThruster.asset` — category Utility, valid face `-Y` only, cone geometry, coupled to `ThrusterMatDef`.
- "Coupled weapon materials" paragraph: add `ThrusterMatDef`; the field is `coupledMaterial`; `ResolveMaterial` returns the coupled material for both Weapon and Utility shapes.
- "Mass Budget" section: `BuildManager.massLimit = 100` → the cap is `BuildManager.MassLimit`, a computed property = the active `ShipClass`'s `MassCap` (Allrounder 100 / Tank 180 / Scout 60); there is no serialized `massLimit` field.
- Add a new **"Ship Classes"** section: the `ShipClass` enum (Allrounder/Tank/Scout), per-class `AlphaHealthPoints` / `MassCap` / `MovementMultiplier`, picked via `BuildShipClassController`, stored per slot, applied in BuildScene (alpha HP, mass cap) and FlyScene (alpha HP, thrust/torque multiplier).
- Add a **Thruster / Boost** section (or subsection): the Utility thruster shape and the boost resource mechanic; cite `thruster_boost_spec.md` / `boost_overboost_tuning_spec.md` for the detailed design.
- `BuildScene.unity` section: add `BuildShipClassController` (middle-left "Class" dropdown) to the scene contents / UI overlays; toolbar bullets — describe the third **Utility** category and its "Utilities" flyout (the shared `CategoryFlyout` machinery).
- `FlyScene.unity` section: add `FlyBoostBar` to the `FlyHUD` list; add a Boost paragraph (thruster cubes, Left-Ctrl boost, 0–100 resource, ×1.3 accel + ×1.3 max-speed, overboost lockout, post-boost decay).
- Input → "Fly map" table: add a `Boost` row — binding Left Ctrl, "Held; boosts thrustered axes."
- "Logging" section: `LogBootstrapper` is a `MonoBehaviour` on `UICanvas.prefab` (DDOL singleton via `UIBootstrap`, not a `BeforeSceneLoad` hook); file path → `Application.persistentDataPath/Logs/CubeFly_<timestamp>.log`.
- Companion-docs list: add `ROADMAP.md`, `thruster_boost_spec.md`, `boost_overboost_tuning_spec.md`.

- [ ] **Step 2: Commit**

```bash
git add cube_fly_spec.md
git commit -m "Refresh cube_fly_spec for save v2, ship classes, thruster/boost"
```

---

## Task 9: F4 — refresh `ROADMAP.md`

**Files:**
- Modify: `ROADMAP.md`

- [ ] **Step 1: Apply these corrections**

- "Up Next": remove the **Thruster cube** item — the thruster cube and the boost mechanic are fully shipped (PR #30 + the boost tuning PRs). The next real Up-Next item is the Power & Energy block.
- "Shipped since the last roadmap pass": add two entries — "Thruster cube — placeable Utility cone shape, new Utilities toolbar category (`ShapeUtilityThruster` / `PlacedThruster` / `ThrusterMatDef`)" and "Boost mechanic — Left-Ctrl boost, 0–100 Boost resource with overboost lockout, `ThrusterBehavior`, per-axis ×1.3 accel + ×1.3 max-speed, `FlyBoostBar` HUD with critical-zone throb".
- "Vision" paragraph: thrusters are no longer "(soon)" — move them to the shipped side; "(soon) reactors / shields" only.
- "Where we are today" summary sentence: add the Thruster Utility shape, the three ship classes, and the boost mechanic.
- The "Ship classes ... and the Rigidbody foundation are done" line: also acknowledge the thruster/boost mechanic as done.
- Leave Power & Energy, Laser weapon + heat, and all "Later" items as-is — they are correctly categorised as not-yet-implemented.

- [ ] **Step 2: Commit**

```bash
git add ROADMAP.md
git commit -m "Update ROADMAP — thruster/boost shipped, no longer up next"
```

---

## Task 10: Verification

This project verifies via Unity compile-check and manual play-tests (no test framework — finding F5, out of scope). This task is interactive — coordinate with the user for the play-tests.

- [ ] **Step 1: Full compile check**

Run `refresh_unity(compile="request", scope="scripts", mode="force")`, poll `mcpforunity://editor/state` until not compiling, then `read_console(types=["error","warning"], count=30)`. Expected: no compilation errors from `GameData`, `BuildManager`, `ProjectileHit`, `CubeDeath`, or `FlyController`.

- [ ] **Step 2: F1 — BuildScene dangling-cube play-test**

In BuildScene: build a small construct that includes a Slope (a shape with at least one invalid face) bridging two cubes. Delete a cube that is a true face-connector and confirm the flood-fill removes exactly the cubes that are no longer face-connected to the alpha — not more, not fewer. Also load a pre-existing save and confirm it still loads; note (per the Task 4 risk note) whether any non-face-connected cube is shed on the first cleanup.

- [ ] **Step 3: F2 — FlyScene cube-death mass play-test**

In FlyScene: fly a multi-cube construct, destroy a non-alpha cube (crash it into a world cube or take fire), and confirm the console logs `Rigidbody mass resolved. Total mass: ...` with a **reduced** mass, and that the ship handles slightly lighter afterward. Confirm destroying the alpha cube still triggers Game Over (and does **not** log a mass recompute).

- [ ] **Step 4: Confirm console is clean**

`read_console(types=["error"], count=20)` — expected: no errors introduced by the batch.

---

## Self-review

- **Finding coverage:** F11 → Task 1; F9 → Task 2; F3 → Task 3; F1 → Task 4; F2 → Task 5; F4 → Tasks 6–9; verification → Task 10. All six in-scope findings covered.
- F5–F8 and F10 are intentionally excluded (large architectural efforts, deferred for separate discussion).
- New symbols are consistent across tasks: `GameData.HasFaceConnection` (Task 4) is the only new public method and is referenced only within Task 4; `CubeDeath.CubeDied` (Task 5) is defined and consumed only within Task 5.
