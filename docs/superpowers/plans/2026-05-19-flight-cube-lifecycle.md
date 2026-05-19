# Flight Cube Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make in-flight cube destruction behave correctly — orphaned cubes
cascade-destroy, the construct resets to its saved state on Hangar return,
and the weapon toolbar reflects weapon-cube deaths.

**Architecture:** Three independent changes on one branch, one commit each.
Issue 1 snapshots `GameData` across a Fly session. Issue 2 extracts the
BuildScene connectivity flood-fill into `GameData` and reuses it in a
FlyScene cube-death cascade. Spec B polls weapon-cube liveness to drive the
toolbar — implemented per the already-approved `weapon_death_hud_spec.md`.

**Tech Stack:** Unity 6.3 LTS (6000.3.11f1), URP 17.3, MonoBehaviour C#,
new Input System. No DOTS.

**Design spec:** `docs/superpowers/specs/2026-05-19-flight-cube-lifecycle-design.md`
(Issues 1 & 2) and `weapon_death_hud_spec.md` (Spec B).

**Branch:** `feat/flight-cube-lifecycle` (already created, off `main`; the
design-spec commit `1fd9c73` is already on it).

**Testing note:** This project has **no automated test framework**.
Per-task verification is the Unity compile-check (`refresh_unity` then
`read_console`); behaviour is confirmed by the manual FlyScene play-tests in
Task 4, run by the user. MCP-bridge `MCP-FOR-UNITY: Client handler exited`
console lines are not errors — ignore them.

**Working-tree note:** `Assets/Materials/BulletMat.mat`,
`ProjectSettings/Packages/com.unity.probuilder/Settings.json`, and the
untracked `CODEBASE_REVIEW_AUDIT.txt` are pre-existing unrelated changes.
**Never stage them.** Every commit below stages only its named files.

---

## Task 1: Issue 1 — Hangar reset (GameData snapshot/restore)

**Files:**
- Modify: `Assets/Scripts/Core/GameData.cs`
- Modify: `Assets/Scripts/Core/SceneSwitcher.cs`

- [ ] **Step 1: Add the flight-snapshot fields to `GameData`**

In `Assets/Scripts/Core/GameData.cs`, find the `_loading` field (≈ line 57):

```csharp
        // True while LoadFromSave is replaying a serialised construct.
        // The flag is consumed by TryAdd to bypass adjacency / occupancy
        // validation on load, so saves are treated as authoritative
        // even if the validation rules have evolved between versions.
        static bool _loading;
```

Insert immediately after it:

```csharp

        // Flight-session snapshot. CaptureFlightSnapshot stores the
        // construct when a Fly session begins; RestoreFlightSnapshot
        // puts it back when the session ends, so in-flight cube
        // destruction (CubeDamage → Remove) never leaks into the
        // construct BuildScene re-enters. Null when no snapshot is held.
        static List<Placement> _flightSnapshot;
        static ShipClass _flightSnapshotShipClass;
```

- [ ] **Step 2: Add `CaptureFlightSnapshot` and `RestoreFlightSnapshot` to `GameData`**

In the same file, find the end of the `Clear()` method (≈ line 233):

```csharp
        public static void Clear()
        {
            _placedCubes.Clear();
            _byCell.Clear();
            // A fresh construct starts as Allrounder. HangarSelect calls
            // Clear() for an empty slot, so a new build always begins
            // here regardless of the previous slot's class.
            ActiveShipClass = ShipClass.Allrounder;
            Debug.unityLogger.Log(TAG, "GameData cleared.");
        }
```

Insert immediately after it:

```csharp

        // Capture the current placements + ship class as the flight
        // snapshot. Called by SceneSwitcher on the BuildScene → FlyScene
        // transition: GameData is the pristine pre-flight construct at
        // that moment.
        public static void CaptureFlightSnapshot()
        {
            _flightSnapshot = new List<Placement>(_placedCubes);
            _flightSnapshotShipClass = ActiveShipClass;
            Debug.unityLogger.Log(TAG,
                $"Flight snapshot captured: {_flightSnapshot.Count} placement(s).");
        }

        // Restore the construct from the flight snapshot, discarding any
        // in-flight cube losses. Called by SceneSwitcher on the FlyScene
        // → BuildScene transition. No-ops with a warning when no snapshot
        // is held — leaving the construct as-is is safer than wiping it.
        public static void RestoreFlightSnapshot()
        {
            if (_flightSnapshot == null)
            {
                Debug.unityLogger.LogWarning(TAG,
                    "RestoreFlightSnapshot: no snapshot held — construct left as-is.");
                return;
            }

            _placedCubes.Clear();
            _byCell.Clear();
            for (int i = 0; i < _flightSnapshot.Count; i++)
            {
                Placement p = _flightSnapshot[i];
                _placedCubes.Add(p);
                _byCell[p.Cell] = p;
            }
            ActiveShipClass = _flightSnapshotShipClass;

            Debug.unityLogger.Log(TAG,
                $"Flight snapshot restored: {_placedCubes.Count} placement(s).");
            _flightSnapshot = null;
        }
```

- [ ] **Step 3: Wire capture/restore into `SceneSwitcher.Toggle`**

In `Assets/Scripts/Core/SceneSwitcher.cs`, replace the `Toggle()` method:

OLD:

```csharp
        public static void Toggle()
        {
            string current = SceneManager.GetActiveScene().name;
            string next = current == BuildSceneName ? FlySceneName : BuildSceneName;
            Debug.unityLogger.Log(TAG, $"SceneSwitcher: transitioning from '{current}' to '{next}'");
            SceneManager.LoadScene(next);
        }
```

NEW:

```csharp
        public static void Toggle()
        {
            string current = SceneManager.GetActiveScene().name;
            string next = current == BuildSceneName ? FlySceneName : BuildSceneName;

            // The construct in GameData is mutated in flight (cubes that
            // reach 0 HP are removed). Snapshot it on the way into
            // FlyScene and restore it on the way back, so the Hangar
            // re-enters BuildScene with the pre-flight construct rather
            // than the shot-down one. Toggle() is the sole Build<->Fly
            // transition, so this pairing is airtight.
            if (current == BuildSceneName)    GameData.CaptureFlightSnapshot();
            else if (current == FlySceneName) GameData.RestoreFlightSnapshot();

            Debug.unityLogger.Log(TAG, $"SceneSwitcher: transitioning from '{current}' to '{next}'");
            SceneManager.LoadScene(next);
        }
```

(`SceneSwitcher` and `GameData` share the `CubeFly.Core` namespace — no
`using` change needed.)

- [ ] **Step 4: Compile-verify**

Run `refresh_unity` (mode `force`, scope `all`, compile `request`,
`wait_for_ready` true), then poll the `mcpforunity://editor/state` resource
until `is_compiling` is false, then `read_console` (types `["error",
"warning"]`).
Expected: no compilation errors. (MCP-bridge "Client handler exited" lines
are not errors.)

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Core/GameData.cs Assets/Scripts/Core/SceneSwitcher.cs
git commit -m "$(cat <<'EOF'
Reset the construct to its pre-flight state on Hangar return

In-flight cube deaths mutate the static GameData; BuildScene then
reinstantiated the damaged construct. GameData now snapshots the
construct when a Fly session begins and restores it on return, so
the Hangar always shows the saved construct.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Issue 2 — flight-side cascade cleanup

**Files:**
- Modify: `Assets/Scripts/Core/GameData.cs`
- Modify: `Assets/Scripts/Build/BuildManager.cs`
- Modify: `Assets/Scripts/Fly/FlyController.cs`

- [ ] **Step 1: Add `GetCellsConnectedToOrigin` to `GameData`**

In `Assets/Scripts/Core/GameData.cs`, find the end of `IsValidAttachment`
(≈ line 211):

```csharp
            for (int i = 0; i < Neighbors.Length; i++)
            {
                if (HasFaceConnection(cell, newShape, newRotation, Neighbors[i], shapeRegistry))
                    return true;
            }
            return false;
        }
```

Insert immediately after it (before `GetConstructBounds`):

```csharp

        // Face-aware flood-fill from the origin (the alpha cube). Returns
        // the set of cells reachable from the origin across
        // HasFaceConnection edges — the origin itself is always included.
        // Any placed cell NOT in the returned set is "dangling": still in
        // GameData but graph-disconnected from the alpha. Shared by
        // BuildManager.RemoveDanglingCubes (delete-tool cleanup) and the
        // FlyScene cube-death cascade so both judge connectivity by one
        // rule.
        public static HashSet<Vector3Int> GetCellsConnectedToOrigin(ShapeRegistry shapeRegistry)
        {
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
                    Placement p = GetPlacementAt(curr);
                    currShape = shapeRegistry != null ? shapeRegistry.Get(p.ShapeIndex) : null;
                    currRotation = p.Rotation;
                }

                for (int i = 0; i < Neighbors.Length; i++)
                {
                    Vector3Int dir = Neighbors[i];
                    Vector3Int nb = curr + dir;
                    if (visited.Contains(nb)) continue;
                    if (!HasFaceConnection(curr, currShape, currRotation, dir, shapeRegistry))
                        continue;
                    visited.Add(nb);
                    queue.Enqueue(nb);
                }
            }
            return visited;
        }
```

- [ ] **Step 2: Refactor `BuildManager.RemoveDanglingCubes` to use the shared flood-fill**

In `Assets/Scripts/Build/BuildManager.cs`, replace the whole
`RemoveDanglingCubes` method (its doc comment + body, ≈ lines 589–647):

OLD:

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

NEW:

```csharp
        // Delete cubes orphaned by a removal: a cube is dangling when it
        // is no longer face-connected to the alpha. Connectivity comes
        // from GameData.GetCellsConnectedToOrigin — the same face-aware
        // flood-fill the FlyScene cube-death cascade uses — so build and
        // flight judge "connected" identically.
        void RemoveDanglingCubes()
        {
            Debug.unityLogger.Log(TAG, "Running flood-fill to detect dangling cubes.");

            HashSet<Vector3Int> visited = GameData.GetCellsConnectedToOrigin(shapeRegistry);

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

- [ ] **Step 3: Add the cascade to `FlyController.OnCubeDied`**

In `Assets/Scripts/Fly/FlyController.cs`, replace the `OnCubeDied` method
(its doc comment + body, ≈ lines 203–208):

OLD:

```csharp
        // A cube died in flight (CubeDeath.CubeDied). It is already gone
        // from GameData and detached from the construct, so re-resolving
        // the Rigidbody now recomputes mass + the mass-derived flight
        // factors for the lighter ship (Unity rebuilds the inertia tensor
        // from the shrunken compound collider when rb.mass is set).
        void OnCubeDied() => ResolveRigidbody();
```

NEW:

```csharp
        // A cube died in flight (CubeDeath.CubeDied). The dead cube is
        // already gone from GameData and detached from the construct.
        // First cascade-destroy any cubes the death left disconnected
        // from the alpha, then re-resolve the Rigidbody once so mass +
        // the mass-derived flight factors reflect the final, lighter
        // construct (Unity rebuilds the inertia tensor from the shrunken
        // compound collider when rb.mass is set).
        void OnCubeDied()
        {
            CascadeDestroyDisconnectedCubes();
            ResolveRigidbody();
        }

        // Flight-side mirror of BuildManager's delete-tool cleanup: when a
        // cube death leaves other cubes with no face-path back to the
        // alpha, those orphans are destroyed too. Orphans are killed via
        // CubeDeath.BeginDeath directly (not CubeDamage), so this does NOT
        // re-raise CubeDied — a single flood-fill already finds every
        // orphan transitively.
        void CascadeDestroyDisconnectedCubes()
        {
            HashSet<Vector3Int> connected = GameData.GetCellsConnectedToOrigin(shapeRegistry);

            // Collect orphan cells before mutating GameData.
            List<Vector3Int> orphans = new List<Vector3Int>();
            for (int i = 0; i < GameData.PlacedCubes.Count; i++)
            {
                Vector3Int cell = GameData.PlacedCubes[i].Cell;
                if (!connected.Contains(cell)) orphans.Add(cell);
            }
            if (orphans.Count == 0) return;

            // Map each orphan cell to its live cube GameObject. Cubes
            // already dying are reparented out of `construct` by
            // CubeDeath, so GetComponentsInChildren only sees live ones.
            Dictionary<Vector3Int, GameObject> cellToCube = new Dictionary<Vector3Int, GameObject>();
            PlacedCubeData[] placedDatas = construct.GetComponentsInChildren<PlacedCubeData>();
            for (int i = 0; i < placedDatas.Length; i++)
                cellToCube[placedDatas[i].cell] = placedDatas[i].gameObject;

            Vector3 outwardOrigin = construct.position;
            for (int i = 0; i < orphans.Count; i++)
            {
                Vector3Int cell = orphans[i];
                GameData.Remove(cell);

                if (!cellToCube.TryGetValue(cell, out GameObject cube) || cube == null)
                    continue;

                // Zero HP so any liveness poll (e.g. the weapon toolbar)
                // sees the orphan as dead immediately, not only after its
                // ~2 s death drift.
                CubeStats stats = cube.GetComponent<CubeStats>();
                if (stats != null) stats.healthPoints = 0f;

                CubeDeath death = cube.GetComponent<CubeDeath>()
                               ?? cube.AddComponent<CubeDeath>();
                death.BeginDeath(outwardOrigin);
            }

            Debug.unityLogger.Log(TAG,
                $"Cascade: {orphans.Count} cube(s) disconnected by a cube death — destroyed.");
        }
```

(`FlyController` already has `using System.Collections.Generic;`,
`using CubeFly.Build;` for `PlacedCubeData`, and `using CubeFly.Core;` for
`GameData` / `CubeStats` / `CubeDeath` / `ShapeRegistry`. No `using`
change needed. `shapeRegistry` and `construct` are existing serialized
fields.)

- [ ] **Step 4: Compile-verify**

Run `refresh_unity` (mode `force`, scope `all`, compile `request`),
poll `mcpforunity://editor/state` until `is_compiling` is false, then
`read_console` (types `["error", "warning"]`).
Expected: no compilation errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Core/GameData.cs Assets/Scripts/Build/BuildManager.cs Assets/Scripts/Fly/FlyController.cs
git commit -m "$(cat <<'EOF'
Cascade-destroy cubes orphaned by an in-flight cube death

Extracts the BuildScene connectivity flood-fill into
GameData.GetCellsConnectedToOrigin and reuses it in FlyController:
when a cube death disconnects others from the alpha, those orphans
are destroyed too and the construct mass/inertia recomputed.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Spec B — weapon toolbar death response

Implements `weapon_death_hud_spec.md` (status: approved).

**Files:**
- Modify: `Assets/Scripts/Fly/WeaponBehavior.cs`
- Modify: `Assets/Scripts/Fly/FlyShootingController.cs`
- Modify: `Assets/Scripts/Fly/FlyWeaponToolbarController.cs`
- Modify: `Assets/Scenes/FlyScene.unity`

- [ ] **Step 1: Add `IsAlive` to `WeaponBehavior`**

In `Assets/Scripts/Fly/WeaponBehavior.cs`, replace this block (≈ lines 36–40):

OLD:

```csharp
        public float ReloadSeconds => reloadSeconds;
        public float CooldownRemaining => _cooldown;
        public bool CanFire => _cooldown <= 0f;

        float _cooldown;
```

NEW:

```csharp
        public float ReloadSeconds => reloadSeconds;
        public float CooldownRemaining => _cooldown;
        public bool CanFire => _cooldown <= 0f;

        // True while this weapon cube is alive (HP > 0). Polled by
        // FlyShootingController (fire gate) and FlyWeaponToolbarController
        // (button state). The sibling CubeStats is resolved once and
        // cached — the construct is rigid for a Fly session — mirroring
        // ThrusterBehavior.LocalThrustAxis's lazy cache.
        public bool IsAlive
        {
            get
            {
                if (!_statsResolved)
                {
                    _stats = GetComponent<CubeStats>();
                    _statsResolved = true;
                }
                return _stats != null && _stats.healthPoints > 0f;
            }
        }

        float _cooldown;
        CubeStats _stats;
        bool _statsResolved;
```

(`WeaponBehavior` already has `using CubeFly.Core;` for `CubeStats`.)

- [ ] **Step 2: Add alive-count accessors to `WeaponTypeGroup`**

In `Assets/Scripts/Fly/FlyShootingController.cs`, find the end of the
`WeaponTypeGroup` class — the `ReadyFraction` property (≈ lines 199–207):

```csharp
        // 0 = just fired, 1 = ready to fire. Drives the reload progress bar.
        public float ReadyFraction
        {
            get
            {
                float r = MaxReloadSeconds;
                if (r <= 0f) return 1f;
                return 1f - Mathf.Clamp01(CooldownRemaining / r);
            }
        }
```

Insert immediately after it (still inside the `WeaponTypeGroup` class,
before its closing `}`):

```csharp

        // Instances still alive — non-null (excludes Unity-destroyed
        // cubes) and IsAlive (excludes cubes mid death-drift at 0 HP).
        public int AliveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Instances.Count; i++)
                {
                    WeaponBehavior w = Instances[i];
                    if (w != null && w.IsAlive) n++;
                }
                return n;
            }
        }

        // Every instance of this type is dead. A group always has >=1
        // instance (RegisterWeapons only creates a group for a member).
        public bool IsFullyDead => AliveCount == 0;

        // Some but not all instances are dead — only meaningful for a
        // multi-instance type.
        public bool IsPartiallyDead =>
            Instances.Count > 1 && AliveCount > 0 && AliveCount < Instances.Count;
```

- [ ] **Step 3: Guard `SetSelected` and skip-dead in `CycleSelected`**

In the same file, replace `SetSelected` and `CycleSelected` (≈ lines 166–180):

OLD:

```csharp
        public void SetSelected(int i)
        {
            if (i < 0 || i >= _types.Count) return;
            if (i == _selectedTypeIndex) return;
            _selectedTypeIndex = i;
            Debug.unityLogger.Log(TAG, $"Selected weapon type index {i} ({_types[i].Shape.displayName}).");
            SelectedChanged?.Invoke(_selectedTypeIndex);
        }

        public void CycleSelected(int delta)
        {
            if (_types.Count == 0) return;
            int next = (_selectedTypeIndex + delta + _types.Count) % _types.Count;
            SetSelected(next);
        }
```

NEW:

```csharp
        public void SetSelected(int i)
        {
            if (i < 0 || i >= _types.Count) return;
            // Cannot select a fully-dead type. Centralises the rule for
            // digit keys and button clicks; CycleSelected and auto-switch
            // always pass a live index, so the guard never blocks them.
            if (_types[i].IsFullyDead) return;
            if (i == _selectedTypeIndex) return;
            _selectedTypeIndex = i;
            Debug.unityLogger.Log(TAG, $"Selected weapon type index {i} ({_types[i].Shape.displayName}).");
            SelectedChanged?.Invoke(_selectedTypeIndex);
        }

        // Step selection by `delta`, skipping past fully-dead types to the
        // next live one. Scans up to Types.Count steps; if no live type
        // exists, selection is left unchanged.
        public void CycleSelected(int delta)
        {
            if (_types.Count == 0) return;
            int step = delta >= 0 ? 1 : -1;
            int next = _selectedTypeIndex;
            for (int scanned = 0; scanned < _types.Count; scanned++)
            {
                next = (next + step + _types.Count) % _types.Count;
                if (!_types[next].IsFullyDead)
                {
                    SetSelected(next);
                    return;
                }
            }
        }
```

- [ ] **Step 4: Add auto-switch to `Update` and gate `HandleFireInput` on liveness**

In the same file, replace `Update` (≈ lines 103–112):

OLD:

```csharp
        void Update()
        {
            // Pause + UI gating, same pattern as BuildManager.
            if (PauseMenu.Instance != null && PauseMenu.Instance.IsOpen) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            if (!HasWeapons) return;

            HandleSelectionInputs();
            HandleFireInput();
        }
```

NEW:

```csharp
        void Update()
        {
            // Pause + weapon-presence gating.
            if (PauseMenu.Instance != null && PauseMenu.Instance.IsOpen) return;
            if (!HasWeapons) return;

            // Auto-switch off a fully-dead selected type. Runs before the
            // pointer-over-UI gate — a weapon dying must move selection
            // regardless of where the cursor is.
            AutoSwitchOffDeadType();

            // UI gating — selection/fire input only when not over the HUD.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            HandleSelectionInputs();
            HandleFireInput();
        }

        // If the selected type is fully dead, move selection to the
        // nearest live type. No-op when the selection is live or when no
        // live type remains (the player simply cannot fire).
        void AutoSwitchOffDeadType()
        {
            WeaponTypeGroup selected = SelectedType;
            if (selected == null || !selected.IsFullyDead) return;
            CycleSelected(1);
        }
```

Then, in the same file, in `HandleFireInput`, find (≈ lines 159–163):

```csharp
            for (int i = 0; i < active.Instances.Count; i++)
            {
                WeaponBehavior w = active.Instances[i];
                if (w != null) w.TryFire(target);
            }
```

Replace the inner `if` line so it reads:

```csharp
            for (int i = 0; i < active.Instances.Count; i++)
            {
                WeaponBehavior w = active.Instances[i];
                if (w != null && w.IsAlive) w.TryFire(target);
            }
```

- [ ] **Step 5: Add the death-response serialized fields + `_deathMarks` array to `FlyWeaponToolbarController`**

In `Assets/Scripts/Fly/FlyWeaponToolbarController.cs`, find the corner-swatch
header (≈ lines 34–35):

```csharp
        [Header("Corner swatch")]
        [SerializeField] Vector2 swatchSize = new Vector2(18f, 18f);
```

Insert immediately after it:

```csharp

        [Header("Death response")]
        [Tooltip("Background color of a fully-dead weapon type's button.")]
        [SerializeField] Color deadColor = new Color(0.32f, 0.32f, 0.34f, 0.9f);
        [Tooltip("Color of the partial-death corner mark (the X glyph).")]
        [SerializeField] Color deathMarkColor = new Color(0.95f, 0.2f, 0.2f, 1f);
        [Tooltip("Size of the partial-death corner mark, in UI units.")]
        [SerializeField] Vector2 deathMarkSize = new Vector2(16f, 16f);
        [Tooltip("Period of the partial-death mark's alpha pulse, in seconds.")]
        [SerializeField] float deathMarkPulseSeconds = 1f;
        [Tooltip("Minimum alpha at the dim end of the partial-death mark pulse.")]
        [SerializeField, Range(0f, 1f)] float deathMarkAlphaMin = 0.25f;
```

Then find the array fields (≈ lines 41–44):

```csharp
        Button[] _buttons;
        Image[] _buttonBackgrounds;
        Image[] _reloadBars;          // foreground fill (per-type colored)
        Image[] _swatches;
```

Replace with:

```csharp
        Button[] _buttons;
        Image[] _buttonBackgrounds;
        Image[] _reloadBars;          // foreground fill (per-type colored)
        Image[] _swatches;
        Text[] _deathMarks;           // partial-death X mark, per button
```

- [ ] **Step 6: Drop the `SelectedChanged` subscription in `FlyWeaponToolbarController`**

In the same file, find in `Start` (≈ lines 59–60):

```csharp
            shootingController.TypesChanged    += RebuildButtons;
            shootingController.SelectedChanged += OnSelectedChanged;
```

Replace with:

```csharp
            shootingController.TypesChanged += RebuildButtons;
```

Then in `OnDestroy` (≈ lines 69–73):

```csharp
            if (shootingController != null)
            {
                shootingController.TypesChanged    -= RebuildButtons;
                shootingController.SelectedChanged -= OnSelectedChanged;
            }
```

Replace with:

```csharp
            if (shootingController != null)
            {
                shootingController.TypesChanged -= RebuildButtons;
            }
```

- [ ] **Step 7: Call `RefreshWeaponStates` from `Update`**

In the same file, replace `Update` (≈ lines 76–85):

OLD:

```csharp
        void Update()
        {
            if (shootingController == null || !shootingController.HasWeapons) return;
            if (_reloadBars == null) return;
            for (int i = 0; i < _reloadBars.Length; i++)
            {
                if (_reloadBars[i] == null) continue;
                _reloadBars[i].fillAmount = shootingController.Types[i].ReadyFraction;
            }
        }
```

NEW:

```csharp
        void Update()
        {
            if (shootingController == null || !shootingController.HasWeapons) return;
            if (_reloadBars == null) return;
            for (int i = 0; i < _reloadBars.Length; i++)
            {
                if (_reloadBars[i] == null) continue;
                _reloadBars[i].fillAmount = shootingController.Types[i].ReadyFraction;
            }
            RefreshWeaponStates();
        }
```

- [ ] **Step 8: Allocate `_deathMarks`, build the marks, and disable button color transition in `RebuildButtons`**

In the same file, in `RebuildButtons`, find the `count == 0` early-out
(≈ lines 118–126):

```csharp
            if (count == 0)
            {
                _buttons = null;
                _buttonBackgrounds = null;
                _reloadBars = null;
                _swatches = null;
                HideCanvas();
                return;
            }
```

Replace with:

```csharp
            if (count == 0)
            {
                _buttons = null;
                _buttonBackgrounds = null;
                _reloadBars = null;
                _swatches = null;
                _deathMarks = null;
                HideCanvas();
                return;
            }
```

Then find the array allocation (≈ lines 129–132):

```csharp
            _buttons = new Button[count];
            _buttonBackgrounds = new Image[count];
            _reloadBars = new Image[count];
            _swatches = new Image[count];
```

Replace with:

```csharp
            _buttons = new Button[count];
            _buttonBackgrounds = new Image[count];
            _reloadBars = new Image[count];
            _swatches = new Image[count];
            _deathMarks = new Text[count];
```

Then find the button construction (≈ lines 148–158):

```csharp
                // ---- Button ----
                (Button btn, Text _) = UIStyle.BuildLabeledButton(_canvasRoot, label, buttonSize, fontSize);
                RectTransform brt = (RectTransform)btn.transform;
                brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0f);
                brt.anchoredPosition = new Vector2(startX + i * (buttonSize.x + spacing), bottomMargin);
                // Optional click-to-select — wired but commented behaviour:
                // some users prefer keyboard + scroll only. Keep the
                // listener so the buttons feel responsive in any case.
                btn.onClick.AddListener(() => shootingController.SetSelected(idx));

                _buttons[i] = btn;
                _buttonBackgrounds[i] = btn.GetComponent<Image>();
```

Replace with:

```csharp
                // ---- Button ----
                (Button btn, Text _) = UIStyle.BuildLabeledButton(_canvasRoot, label, buttonSize, fontSize);
                RectTransform brt = (RectTransform)btn.transform;
                brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0f);
                brt.anchoredPosition = new Vector2(startX + i * (buttonSize.x + spacing), bottomMargin);
                // RefreshWeaponStates owns button background color; switch
                // off the Button's ColorTint transition so it doesn't
                // fight the manual painting. interactable = false still
                // blocks clicks regardless of transition mode.
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => shootingController.SetSelected(idx));

                _buttons[i] = btn;
                _buttonBackgrounds[i] = btn.GetComponent<Image>();
```

Then find the corner-swatch line (≈ lines 160–161):

```csharp
                // ---- Corner swatch ----
                _swatches[i] = BuildSwatch(brt, swatchColor);
```

Replace with:

```csharp
                // ---- Corner swatch ----
                _swatches[i] = BuildSwatch(brt, swatchColor);

                // ---- Partial-death corner mark ----
                _deathMarks[i] = BuildDeathMark(brt);
```

Then find the end of `RebuildButtons` (≈ lines 171–172):

```csharp
            ApplySelectedHighlight(shootingController.SelectedTypeIndex);
            Debug.unityLogger.Log(TAG, $"Toolbar rebuilt with {count} weapon type(s).");
```

Replace with:

```csharp
            RefreshWeaponStates();
            Debug.unityLogger.Log(TAG, $"Toolbar rebuilt with {count} weapon type(s).");
```

- [ ] **Step 9: Replace `OnSelectedChanged`/`ApplySelectedHighlight` with `BuildDeathMark` + `RefreshWeaponStates`**

In the same file, find the event-driven refresh block at the end of the
class (≈ lines 211–225):

```csharp
        // ---------- Event-driven refreshes ----------

        void OnSelectedChanged(int idx) => ApplySelectedHighlight(idx);

        void ApplySelectedHighlight(int idx)
        {
            if (_buttonBackgrounds == null) return;
            for (int i = 0; i < _buttonBackgrounds.Length; i++)
            {
                if (_buttonBackgrounds[i] == null) continue;
                _buttonBackgrounds[i].color = (i == idx)
                    ? SelectedTypeColor
                    : UIStyle.BackgroundIdle;
            }
        }
```

Replace the whole block with:

```csharp
        // ---------- Death-response construction + per-frame refresh ----------

        // Build the partial-death X mark for a button — a bold red glyph
        // anchored to the button's bottom-right corner (mirroring the
        // top-right swatch). Disabled by default; RefreshWeaponStates
        // enables it while the type is partially dead.
        Text BuildDeathMark(RectTransform buttonRT)
        {
            Text mark = UIStyle.BuildLabel(
                buttonRT, "✕", Mathf.RoundToInt(deathMarkSize.y), FontStyle.Bold);
            RectTransform rt = (RectTransform)mark.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-4f, 4f);
            rt.sizeDelta = deathMarkSize;
            mark.color = deathMarkColor;
            mark.enabled = false;
            return mark;
        }

        // Per-frame weapon-state refresh — sole owner of button
        // interactability, background color, and the partial-death mark.
        // Background priority: dead > selected > idle.
        void RefreshWeaponStates()
        {
            if (_buttons == null) return;
            int selected = shootingController.SelectedTypeIndex;
            for (int i = 0; i < _buttons.Length; i++)
            {
                WeaponTypeGroup group = shootingController.Types[i];

                if (_buttons[i] != null)
                    _buttons[i].interactable = !group.IsFullyDead;

                if (_buttonBackgrounds[i] != null)
                {
                    Color bg;
                    if (group.IsFullyDead)  bg = deadColor;
                    else if (i == selected) bg = SelectedTypeColor;
                    else                    bg = UIStyle.BackgroundIdle;
                    _buttonBackgrounds[i].color = bg;
                }

                if (_deathMarks[i] != null)
                {
                    bool partial = group.IsPartiallyDead;
                    _deathMarks[i].enabled = partial;
                    if (partial)
                    {
                        // Slow sine alpha pulse between deathMarkAlphaMin
                        // and 1, driven by unscaled time so it keeps
                        // pulsing while the game is paused.
                        float period = Mathf.Max(0.01f, deathMarkPulseSeconds);
                        float phase = 0.5f + 0.5f *
                            Mathf.Sin(Time.unscaledTime * (2f * Mathf.PI / period));
                        Color c = deathMarkColor;
                        c.a = Mathf.Lerp(deathMarkAlphaMin, 1f, phase);
                        _deathMarks[i].color = c;
                    }
                }
            }
        }
```

- [ ] **Step 10: Compile-verify**

Run `refresh_unity` (mode `force`, scope `all`, compile `request`),
poll `mcpforunity://editor/state` until `is_compiling` is false, then
`read_console` (types `["error", "warning"]`).
Expected: no compilation errors.

- [ ] **Step 11: Write the new serialized fields into `FlyScene.unity`**

The five new `FlyWeaponToolbarController` fields take their C# initializer
defaults at runtime, but the scene file must record them so the commit
includes the scene change the spec calls for. Use the Unity MCP:

1. `find_gameobjects` with `search_term` `"FlyWeaponToolbarController"`,
   `search_method` `"by_component"`, to get the host GameObject's instance
   ID in FlyScene.
2. `manage_components` `set_property` on that component, setting all five
   fields (this both writes the values and marks the scene dirty):
   - `deadColor` → `[0.32, 0.32, 0.34, 0.9]`
   - `deathMarkColor` → `[0.95, 0.2, 0.2, 1.0]`
   - `deathMarkSize` → `[16, 16]`
   - `deathMarkPulseSeconds` → `1.0`
   - `deathMarkAlphaMin` → `0.25`
3. `manage_scene` save (the active scene is `FlyScene`).
4. Run `git diff Assets/Scenes/FlyScene.unity` — confirm the diff adds only
   the five fields to the `FlyWeaponToolbarController` MonoBehaviour block
   (Unity may also touch `m_EditorClassIdentifier` / serialization
   ordering — acceptable). If the diff is broad, discard it and instead
   re-save the scene from the Unity Editor once and re-check.

- [ ] **Step 12: Commit**

```bash
git add Assets/Scripts/Fly/WeaponBehavior.cs Assets/Scripts/Fly/FlyShootingController.cs Assets/Scripts/Fly/FlyWeaponToolbarController.cs Assets/Scenes/FlyScene.unity
git commit -m "$(cat <<'EOF'
Add weapon toolbar death response

Implements weapon_death_hud_spec.md: weapon cubes expose IsAlive;
WeaponTypeGroup reports alive counts; the toolbar greys out a
fully-dead type's button, shows a pulsing red X on a partially-dead
type, and selection auto-switches away from / skips dead types.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Manual verification and pull request

**No code.** Compile is already green from Tasks 1–3. This task is the
user-run play-tests and the PR.

- [ ] **Step 1: User play-test — Issue 1 (Hangar reset)**

In the Unity Editor: enter via HangarSelect, build a multi-cube construct,
fly it, take cube losses (fly into an AutoTurret's line of fire), then
press the "Hangar" button.
Expected: BuildScene shows the **full original** construct; the Mass / HP
readout matches the pre-flight construct. Editing and re-flying still work.

- [ ] **Step 2: User play-test — Issue 2 (cascade cleanup)**

Build a construct with a single "bridge" cube linking a tail section to the
alpha (e.g. alpha — cube A — cube B, where A is B's only path to the alpha).
Fly, then destroy cube A.
Expected: cube B drifts away with the CubeDeath animation right after A;
the console logs `Cascade: N cube(s) disconnected ...`; `ResolveRigidbody`
logs a reduced mass.

- [ ] **Step 3: User play-test — Spec B (weapon death HUD)**

Per the verification section of `weapon_death_hud_spec.md`: a construct with
one single-instance weapon type and one multi-instance type (≥2 cubes).
Expected: destroying one cube of the multi-instance type shows a slow
flashing red ✕ on its still-usable button; destroying the rest greys the
button and clears the ✕, auto-switching selection if it was selected;
the single-instance type greys with no ✕; scroll/digit selection skips
greyed types; a weapon does not fire during its death-drift.

- [ ] **Step 4: Push the branch and open the PR**

```bash
git push -u origin feat/flight-cube-lifecycle
gh pr create --title "Flight cube lifecycle: Hangar reset, cascade cleanup, weapon death HUD" --body "$(cat <<'EOF'
## Summary
- **Issue 1 — Hangar reset:** `GameData` snapshots the construct when a Fly
  session begins and restores it on Hangar return, so in-flight cube losses
  never reach BuildScene or the slot save.
- **Issue 2 — cascade cleanup:** the BuildScene connectivity flood-fill is
  extracted into `GameData.GetCellsConnectedToOrigin`; when an in-flight
  cube death disconnects others from the alpha, those orphans are
  destroyed too and mass/inertia recomputed.
- **Spec B — weapon death HUD:** the Fly weapon toolbar greys out fully-dead
  weapon types, marks partially-dead types with a pulsing red ✕, and
  auto-switches / skips dead types in selection.

Design: `docs/superpowers/specs/2026-05-19-flight-cube-lifecycle-design.md`
and `weapon_death_hud_spec.md`.

## Test plan
- [ ] Issue 1: fly, lose cubes, press Hangar → BuildScene shows the original construct
- [ ] Issue 2: destroy a bridge cube → the disconnected tail cascade-destroys; mass recomputes
- [ ] Spec B: weapon-cube deaths grey / ✕-mark the toolbar; selection skips dead types
- [ ] Compile clean, no console errors

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 5: Request Copilot review**

```bash
gh pr view --json number -q .number
```

Then, with that PR number `<N>`:

```bash
gh api repos/entewurzelauskuh/CosmicScrapClub/pulls/<N>/requested_reviewers -X POST -f "reviewers[]=Copilot"
```

---

## Self-review

**Spec coverage** — every design-spec requirement maps to a task:
- Issue 1 snapshot/restore → Task 1. Issue 2 shared flood-fill + cascade →
  Task 2. Spec B (`WeaponBehavior.IsAlive`, `WeaponTypeGroup` accessors,
  toolbar grey-out / ✕ / consolidated coloring / `transition = None`,
  selection auto-switch + skip, fire-liveness gate, new serialized fields +
  scene values) → Task 3. Verification play-tests + PR → Task 4.
- Cross-item note (Issue 2 zeroes orphan HP so Spec B polling greys an
  orphaned weapon) is realised by Task 2 Step 3 (`stats.healthPoints = 0f`).

**Placeholder scan** — no TBD/TODO; every code step shows complete code;
the scene step gives exact values and a verification.

**Type consistency** — names used consistently across tasks:
`CaptureFlightSnapshot` / `RestoreFlightSnapshot` (Task 1);
`GetCellsConnectedToOrigin` returning `HashSet<Vector3Int>` (Task 2, used by
both `BuildManager` and `FlyController`); `IsAlive`, `AliveCount`,
`IsFullyDead`, `IsPartiallyDead`, `RefreshWeaponStates`, `BuildDeathMark`,
`AutoSwitchOffDeadType`, `_deathMarks` (Task 3).
