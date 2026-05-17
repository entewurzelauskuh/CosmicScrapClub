# Thruster PR 2 — Boost Mechanic Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the inert placed Thruster into an active one — the fly-side **Boost** mechanic. A new `ThrusterBehavior` MonoBehaviour on `PlacedThruster.prefab` exposes each thruster's thrust axis in the construct's local frame; `FlyController` collects them in `BuildConstruct` (the `_spawnedWeapons` pattern), owns a `0–100` Boost resource with drain/regen/overboosted state, and — per `FixedUpdate` — multiplies the thrust force ×1.3 on any input axis with a contributing thruster while raising the linear-velocity clamp to `maxSpeed × 1.3`. A new `Boost` input action (Left Ctrl) gates it; a new `FlyBoostBar` HUD element shows the resource left of the crosshair. After PR 2 the Thruster feature is complete: placeable in the hangar (PR 1) and a working boost in flight (PR 2).

**Architecture:** The Boost resource lives on `FlyController` because flight is intrinsic to it — exposed read-only as `BoostFraction` (0–1) and `IsOverboosted` for the HUD. `ThrusterBehavior` mirrors `WeaponBehavior`'s collected-into-a-list shape: a `Construct` reference set by `FlyController.BuildConstruct`, and a `LocalThrustAxis` snapped to one of the six construct-local axes (a thruster's exhaust points along local `+Y` / `transform.up`; thrust is `−transform.up`; placements are 90°-stepped so the construct-local thrust direction is exactly `±X/±Y/±Z`). Activation, evaluated per `FixedUpdate`, ANDs three conditions: Left Ctrl held, boost available, and `_thrustInput` commanding movement along the thruster's axis with the matching sign. The effect is **flat** — any of the 6 input axes with ≥1 contributing thruster gets its thrust-force component ×1.3 (thruster count never stacks); while ≥1 thruster contributes anywhere the construct is *actively boosting* and the speed clamp ceiling rises to `maxSpeed × 1.3`. When boosting ends the ceiling drops to `maxSpeed` but excess speed is eased down at a serialized `overCapDecaySpeed` rather than hard-snapped — the hard clamp still applies as a true ceiling at the *active* cap. `CubeFlyInputActions` is a **hand-written** wrapper (not a generated class), so the new `Boost` action is added in code (and mirrored, for parity, in the companion `.inputactions` asset). `FlyBoostBar` follows the project's HUD pattern (`FlySpeedIndicator` / `FlyHpIndicator`): it builds its own `ScreenSpaceOverlay` canvas via `UIStyle`, reads game state in `Update`, and is screen-fixed near screen centre — **not** parented to the drifting `FlyCrosshair` reticle.

**Tech Stack:** Unity 6.3 LTS (6000.3.x) / URP, MonoBehaviour-only C#, pure C# (no DOTS), the new Input System (`UnityEngine.InputSystem`), legacy `UnityEngine.UI`. Compilation + console checks via Unity MCP; editor / play-mode checks by hand. No unit-test framework.

---

## Starting state

- Branch: **`feat/thruster-boost`**, off `main`. **PR 1 (the placeable Thruster) is merged to `main`** — `PrimitiveMeshes.Cone`, `ShapeCategory.Utility`, the `coupledMaterial` rename, `ThrusterMeshAuthor`, `ThrusterMatDef`, `ThrusterMat`, `PlacedThruster.prefab`, `ShapeUtilityThruster.asset`, the "Utilities" toolbar category. Do not redo any PR 1 work.
- **PR 2 (this plan) is the final Thruster PR.** It is fly-side only: `ThrusterBehavior`, the Boost resource, the Left-Ctrl input, the flight-force integration, and the Boost HUD bar. After PR 2 the Thruster feature in `ROADMAP.md` → *Flight & Movement* is complete.
- Today `PlacedThruster.prefab` has `MeshFilter` / `MeshRenderer` / `BoxCollider` / `PlacedCubeData` / `CubeStats` / `ThrusterMeshAuthor` and is **inert in flight** — no `ThrusterBehavior` (the script does not exist yet). `FlyController.BuildConstruct` collects `WeaponBehavior`s into `_spawnedWeapons` but knows nothing of thrusters. `CubeFlyInputActions.Fly` has `Thrust / Pitch / Yaw / Roll / Look / LookHeld / Fire` — no `Boost`. The `FlyHUD` GameObject hosts `FlyCrosshair`, `FlyWeaponToolbarController`, `FlySpeedIndicator`, `FlyHpIndicator` — no `FlyBoostBar`.
- The Heat bar (the crosshair's right-side resource bar) is **out of scope** — spec §7 / §9 defer it to the Laser-weapon roadmap item. PR 2 builds **only the Boost bar (left)**. Do not build, wire, or stub a Heat bar.

## Conventions for every task

- **Compile/console check:** after creating or editing any `.cs` file, refresh Unity and wait for the domain reload to finish, then read the console filtered to errors. Concretely: `mcp__UnityMCP__refresh_unity` with `compile="request"`, `mode="force"`, `scope="all"`, `wait_for_ready=true`; poll the `mcpforunity://editor/state` resource until `is_compiling=false` and `ready_for_tools=true`; then `mcp__UnityMCP__read_console` with `action="get"`, `types=["error"]`, `count=50`. **Zero compile errors before proceeding.** (MCP `Client handler exited` lines are infrastructure noise, not errors.)
- **`.meta` for new scripts (project quirk):** Unity's auto-generated `.meta` stub for a new `.cs` file omits the full `MonoImporter` block this project expects. Each new script (`ThrusterBehavior.cs`, `FlyBoostBar.cs`) gets a hand-written canonical `.meta` containing a complete `MonoImporter` block — see Task 1 Step 2 and Task 6 Step 2 for the exact template. It mirrors `Assets/Scripts/Core/ThrusterMeshAuthor.cs.meta` (a `MonoImporter` with `externalObjects`, `serializedVersion: 2`, empty `defaultReferences`, `executionOrder: 0`, a zero `icon`, empty `userData` / `assetBundleName` / `assetBundleVariant`).
- **Prefab / scene edits via Unity MCP.** `PlacedThruster.prefab` (Task 2) and `FlyScene.unity` (Task 7) are modified at execution time with Unity MCP tools (`manage_prefabs`, `manage_gameobject`, `manage_scene`, or an `execute_code` editor snippet) so component wiring and GUIDs are correct — not by hand-writing YAML. The plan gives the exact component to add and its target field values; the reference YAML shapes shown are the *expected result* to verify against, not text to paste.
- **Commit** at the end of each task with the exact `git add` paths shown, on branch `feat/thruster-boost`. Do not amend; each task is a fresh commit. Commit messages are imperative ("Add …", "Wire …"), matching the repo style (recent: "Add ShapeUtilityThruster shape and register it in ShapeRegistry", "Add PlacedThruster prefab — inert cone placeable").
- **Tuning values are placeholders.** Spec §5's resource numbers (max 100, drain 40/s, regen 15/s, overboosted regen 6/s), the ×1.3 multipliers, and `overCapDecaySpeed` are starting points; expect in-editor iteration. Serialize them so they are tunable without a recompile.

---

## Task 1: Add `ThrusterBehavior.cs` + its canonical `.meta`

**Files:**
- Create: `Assets/Scripts/Fly/ThrusterBehavior.cs`
- Create: `Assets/Scripts/Fly/ThrusterBehavior.cs.meta`

The construct-collected component for a placed Thruster — the boost analogue of `WeaponBehavior`. `FlyController.BuildConstruct` finds every `ThrusterBehavior` on the spawned construct and stores it in a list, exactly as it does for `WeaponBehavior` / `_spawnedWeapons`. `ThrusterBehavior` carries a `Construct` reference (set by `FlyController` after instantiation, same as `WeaponBehavior.Construct`) and exposes its **thrust direction in the construct's local frame**, snapped to one of the six axes.

Thrust geometry (spec §6.1): a thruster's exhaust points along its local `+Y` — the cone apex, `transform.up`. Thrust is the opposite, `−transform.up`. Placements are 90°-stepped, so the thrust direction expressed in the construct's local frame is exactly one of `±X / ±Y / ±Z`. `ResolveThrustAxis` converts the world-space `−transform.up` into the construct's local space (`construct.InverseTransformDirection`) and snaps each component to the nearest integer in `{−1, 0, +1}`, giving a clean unit axis vector immune to float drift.

- [ ] **Step 1: Create `ThrusterBehavior.cs`**

Create `Assets/Scripts/Fly/ThrusterBehavior.cs` with this exact content:

```csharp
using UnityEngine;

namespace CubeFly.Fly
{
    // A placed Thruster in flight — the boost-side analogue of
    // WeaponBehavior. FlyController.BuildConstruct collects every
    // ThrusterBehavior on the spawned construct into a list (the same
    // pattern as _spawnedWeapons / WeaponBehavior) and sets Construct
    // after instantiation.
    //
    // A thruster's exhaust points along its local +Y — the cone apex,
    // transform.up. Thrust acts in the OPPOSITE direction: -transform.up
    // (out through the circular placement face). Placements are
    // 90°-stepped, so the thrust direction expressed in the construct's
    // local frame is exactly one of the six local axes (±X / ±Y / ±Z).
    // LocalThrustAxis exposes it snapped to that — a clean unit axis
    // vector, immune to float drift, that FlyController matches against
    // _thrustInput per FixedUpdate to decide whether this thruster is
    // pushing the way the player is commanding thrust.
    //
    // This component has no Update — it holds no per-frame state. It is
    // a passive descriptor; FlyController drives all the boost logic.
    public class ThrusterBehavior : MonoBehaviour
    {
        // Set by FlyController.BuildConstruct right after Instantiate,
        // exactly as WeaponBehavior.Construct is. Needed to express the
        // thrust direction in the construct's local frame.
        public Transform Construct { get; set; }

        // The thrust direction in the construct's LOCAL frame, snapped
        // to one of the six unit axes (±X / ±Y / ±Z). Recomputed on
        // demand from the current transforms; cached after the first
        // read because the construct is rigid (cube poses are fixed for
        // the lifetime of a Fly session).
        public Vector3 LocalThrustAxis
        {
            get
            {
                if (!_axisResolved)
                {
                    _localThrustAxis = ResolveThrustAxis();
                    _axisResolved = true;
                }
                return _localThrustAxis;
            }
        }

        Vector3 _localThrustAxis;
        bool _axisResolved;

        // Convert world-space thrust direction (-transform.up) into the
        // construct's local frame, then snap each component to the
        // nearest integer in {-1, 0, +1}. With 90°-stepped placements
        // the result is exactly one signed unit axis; the snap removes
        // any floating-point fuzz so the per-axis sign comparison in
        // FlyController is exact. Falls back to the world thrust
        // direction if Construct is somehow unset.
        Vector3 ResolveThrustAxis()
        {
            Vector3 worldThrust = -transform.up;
            if (Construct == null) return worldThrust;

            Vector3 local = Construct.InverseTransformDirection(worldThrust);
            return new Vector3(
                Mathf.Round(Mathf.Clamp(local.x, -1f, 1f)),
                Mathf.Round(Mathf.Clamp(local.y, -1f, 1f)),
                Mathf.Round(Mathf.Clamp(local.z, -1f, 1f)));
        }
    }
}
```

- [ ] **Step 2: Create the canonical `.meta` for `ThrusterBehavior.cs`**

Unity's auto-generated stub `.meta` omits the full `MonoImporter` block this project expects. Create `Assets/Scripts/Fly/ThrusterBehavior.cs.meta` with this exact content (a fresh unique 32-hex GUID — the one below is unused in the project; if Unity has already generated a `.meta`, overwrite it with the block below but keep whatever GUID Unity assigned if it already differs and is already referenced anywhere):

```
fileFormatVersion: 2
guid: 7c1e2d3a4b5c6d7e8f9a0b1c2d3e4f01
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:
```

This matches the shape of `Assets/Scripts/Core/ThrusterMeshAuthor.cs.meta`. Confirm the GUID `7c1e2d3a4b5c6d7e8f9a0b1c2d3e4f01` is unused with `grep -rl 7c1e2d3a4b5c6d7e8f9a0b1c2d3e4f01 Assets/` before committing — if it collides, pick another random 32-hex value.

- [ ] **Step 3: Compile + console check**

Refresh Unity (`refresh_unity`, `compile="request"`, `mode="force"`, `scope="all"`, `wait_for_ready=true`), poll `mcpforunity://editor/state` until `is_compiling=false` and `ready_for_tools=true`, then `read_console(action="get", types=["error"], count=50)`.

Expected: zero errors. `ThrusterBehavior` references only `UnityEngine`; it is referenced by nothing yet (the prefab gets it in Task 2, `FlyController` collects it in Task 4), so it must compile purely on its own.

- [ ] **Step 4: Commit**

```bash
git add "Assets/Scripts/Fly/ThrusterBehavior.cs" "Assets/Scripts/Fly/ThrusterBehavior.cs.meta"
git commit -m "Add ThrusterBehavior — exposes a thruster's construct-local thrust axis"
```

---

## Task 2: Add `ThrusterBehavior` to `PlacedThruster.prefab`

**Files:**
- Modify: `Assets/Prefabs/PlacedThruster.prefab`

`PlacedThruster.prefab` currently has `MeshFilter`, `MeshRenderer`, `BoxCollider`, `PlacedCubeData`, `CubeStats`, `ThrusterMeshAuthor`. PR 1 deliberately left it without `ThrusterBehavior` because the script did not exist. Task 1 created it — now add it as a seventh component so every spawned thruster carries it and `FlyController.BuildConstruct` (Task 4) can collect it.

- [ ] **Step 1: Add the `ThrusterBehavior` component to the prefab**

Add a `CubeFly.Fly.ThrusterBehavior` component to `Assets/Prefabs/PlacedThruster.prefab`. Recommended — `mcp__UnityMCP__manage_prefabs` (open the prefab, add the component, save) or `mcp__UnityMCP__execute_code` with an editor snippet:

```csharp
// Editor-side, run via execute_code.
var path = "Assets/Prefabs/PlacedThruster.prefab";
var go = UnityEditor.PrefabUtility.LoadPrefabContents(path);
if (go.GetComponent<CubeFly.Fly.ThrusterBehavior>() == null)
    go.AddComponent<CubeFly.Fly.ThrusterBehavior>();
UnityEditor.PrefabUtility.SaveAsPrefabAsset(go, path);
UnityEditor.PrefabUtility.UnloadPrefabContents(go);
```

`ThrusterBehavior` has no serialized fields (`Construct` is a runtime property set by `FlyController`, and `LocalThrustAxis` is computed), so nothing needs configuring on the component — adding it is the whole change.

- [ ] **Step 2: Verify the prefab**

Read `Assets/Prefabs/PlacedThruster.prefab` and confirm a seventh `MonoBehaviour` is present whose `m_Script` GUID is `7c1e2d3a4b5c6d7e8f9a0b1c2d3e4f01` (the Task 1 `ThrusterBehavior` GUID) — its `m_EditorClassIdentifier` should read `Assembly-CSharp::CubeFly.Fly.ThrusterBehavior`. The prefab's GameObject `m_Component` list should now hold seven entries (Transform, MeshFilter, MeshRenderer, BoxCollider, `PlacedCubeData`, `CubeStats`, `ThrusterMeshAuthor`, `ThrusterBehavior` — note the existing six plus the new one). Expected new component block:

```yaml
--- !u!114 &<fresh fileID>
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 1538907889469750075}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: 7c1e2d3a4b5c6d7e8f9a0b1c2d3e4f01, type: 3}
  m_Name:
  m_EditorClassIdentifier: Assembly-CSharp::CubeFly.Fly.ThrusterBehavior
```

Refresh Unity and `read_console(action="get", types=["error"], count=50)` — zero errors (a missing-script reference on the prefab would surface here).

- [ ] **Step 3: Commit**

```bash
git add "Assets/Prefabs/PlacedThruster.prefab"
git commit -m "Add ThrusterBehavior component to PlacedThruster prefab"
```

---

## Task 3: `CubeFlyInputActions` — add the `Boost` action on Left Ctrl

**Files:**
- Modify: `Assets/Input/CubeFlyInputActions.cs`
- Modify: `Assets/Input/CubeFlyInputActions.inputactions`

`CubeFlyInputActions.cs` is a **hand-written wrapper**, not a Unity-generated class — its own header says: *"Hand-rolled wrapper that mirrors the Generate-C#-Class output Unity would produce … Defining the bindings in code avoids a hard dependency on the editor's wrapper-generation pass."* So the `Boost` action is added by hand-editing the `.cs` file: a new `InputAction` built in the constructor's Fly-map block, threaded through the `FlyActions` constructor, and exposed as a `public InputAction Boost { get; }` property. `FlyController` will then read `_input.Fly.Boost` exactly as it reads `_input.Fly.Fire`.

The companion `Assets/Input/CubeFlyInputActions.inputactions` asset is **not** the source of truth at runtime (the `.cs` wrapper is hand-rolled and self-contained — nothing loads the asset), but it is kept in sync as documentation / for any future regeneration. It gets a matching `Boost` action + binding. The `.cs` edit is load-bearing; the `.inputactions` edit is for parity.

`Boost` is a **Button** action bound to `<Keyboard>/leftCtrl`, mirroring `LookHeld` / `Fire` (both `InputActionType.Button` with `initialStateCheck: false`).

- [ ] **Step 1: Add the `boost` action in the constructor's Fly-map block**

In `Assets/Input/CubeFlyInputActions.cs`, find the end of the Fly-map action setup — the `fire` action and the `Fly = new FlyActions(...)` line:

```csharp
            // Fire: LMB. Held-down semantics — FlyShootingController polls
            // IsPressed() each frame; per-weapon reload throttles the rate.
            InputAction fire = _flyMap.AddAction("Fire", InputActionType.Button, "<Mouse>/leftButton");

            Fly = new FlyActions(_flyMap, thrust, pitch, yaw, roll, look, lookHeld, fire);
```

Replace it with:

```csharp
            // Fire: LMB. Held-down semantics — FlyShootingController polls
            // IsPressed() each frame; per-weapon reload throttles the rate.
            InputAction fire = _flyMap.AddAction("Fire", InputActionType.Button, "<Mouse>/leftButton");

            // Boost: Left Ctrl. Held-down semantics — FlyController polls
            // IsPressed() each FixedUpdate; the boost only actually fires
            // a thruster when the player is also commanding thrust along
            // that thruster's push axis (see ThrusterBehavior / the
            // FixedUpdate activation rule).
            InputAction boost = _flyMap.AddAction("Boost", InputActionType.Button, "<Keyboard>/leftCtrl");

            Fly = new FlyActions(_flyMap, thrust, pitch, yaw, roll, look, lookHeld, fire, boost);
```

- [ ] **Step 2: Thread `Boost` through the `FlyActions` struct**

Still in `CubeFlyInputActions.cs`, find the `FlyActions` struct:

```csharp
        public readonly struct FlyActions
        {
            readonly InputActionMap _map;
            public InputAction Thrust   { get; }
            public InputAction Pitch    { get; }
            public InputAction Yaw      { get; }
            public InputAction Roll     { get; }
            public InputAction Look     { get; }
            public InputAction LookHeld { get; }
            public InputAction Fire     { get; }

            public FlyActions(InputActionMap map,
                InputAction thrust, InputAction pitch, InputAction yaw,
                InputAction roll, InputAction look, InputAction lookHeld,
                InputAction fire)
            {
                _map     = map;
                Thrust   = thrust;
                Pitch    = pitch;
                Yaw      = yaw;
                Roll     = roll;
                Look     = look;
                LookHeld = lookHeld;
                Fire     = fire;
            }

            public void Enable() => _map.Enable();
            public void Disable() => _map.Disable();
        }
```

Replace it with:

```csharp
        public readonly struct FlyActions
        {
            readonly InputActionMap _map;
            public InputAction Thrust   { get; }
            public InputAction Pitch    { get; }
            public InputAction Yaw      { get; }
            public InputAction Roll     { get; }
            public InputAction Look     { get; }
            public InputAction LookHeld { get; }
            public InputAction Fire     { get; }
            public InputAction Boost    { get; }

            public FlyActions(InputActionMap map,
                InputAction thrust, InputAction pitch, InputAction yaw,
                InputAction roll, InputAction look, InputAction lookHeld,
                InputAction fire, InputAction boost)
            {
                _map     = map;
                Thrust   = thrust;
                Pitch    = pitch;
                Yaw      = yaw;
                Roll     = roll;
                Look     = look;
                LookHeld = lookHeld;
                Fire     = fire;
                Boost    = boost;
            }

            public void Enable() => _map.Enable();
            public void Disable() => _map.Disable();
        }
```

`Enable()` / `Disable()` are unchanged — they call `_flyMap.Enable()` / `.Disable()`, which enables/disables every action in the map including the new `Boost`. No other call site changes (`FlyController.OnEnable` / `OnDisable` call `_input.Fly.Enable()` / `.Disable()`).

- [ ] **Step 3: Mirror the `Boost` action into the `.inputactions` asset**

In `Assets/Input/CubeFlyInputActions.inputactions`, add a `Boost` action to the `Fly` map's `actions` array and a matching binding to its `bindings` array. The `Fly` map ends its `actions` array with `LookHeld` (id `22222222-0000-0000-0000-000000000060`) — note the asset currently has **no `Fire` action** in it (the `.cs` wrapper has drifted ahead of the asset; this is consistent with the asset not being the runtime source of truth). Add `Boost` as a new final entry of the `actions` array:

```json
                {
                    "name": "Boost",
                    "type": "Button",
                    "id": "22222222-0000-0000-0000-000000000070",
                    "expectedControlType": "Button",
                    "processors": "",
                    "interactions": "",
                    "initialStateCheck": false
                }
```

(Add a comma after the `LookHeld` action's closing `}` so the array stays valid JSON.) Then add a matching binding as a new final entry of the `Fly` map's `bindings` array:

```json
                {
                    "name": "",
                    "id": "22222222-0000-0000-0000-000000000700",
                    "path": "<Keyboard>/leftCtrl",
                    "interactions": "",
                    "processors": "",
                    "groups": "",
                    "action": "Boost",
                    "isComposite": false,
                    "isPartOfComposite": false
                }
```

(Add a comma after the `LookHeld` binding's closing `}`.) This keeps the asset a faithful mirror of the wrapper. If editing the JSON by hand is error-prone, it is acceptable to skip the asset edit and note in the commit that the asset is intentionally left as documentation only — but the preferred outcome is the asset and the wrapper agreeing. The runtime behaviour comes entirely from the `.cs` change in Steps 1–2.

- [ ] **Step 4: Compile + console check**

Refresh Unity (`refresh_unity`, `compile="request"`, `mode="force"`, `scope="all"`, `wait_for_ready=true`), poll `mcpforunity://editor/state` until `is_compiling=false` and `ready_for_tools=true`, then `read_console(action="get", types=["error"], count=50)`.

Expected: zero errors. The `FlyActions` constructor gained one parameter and one call site (the `Fly = new FlyActions(...)` in the same file) was updated in lock-step, so the project compiles. If the console reports *"No overload for method 'FlyActions' takes 8 arguments"*, the constructor call in Step 1 was not updated — re-check that the `Fly = new FlyActions(...)` line passes `boost` as the ninth argument.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Input/CubeFlyInputActions.cs" "Assets/Input/CubeFlyInputActions.inputactions"
git commit -m "Add Boost input action bound to Left Ctrl"
```

---

## Task 4: `FlyController` — collect `ThrusterBehavior`s, Boost resource + drain/regen/overboosted state machine

**Files:**
- Modify: `Assets/Scripts/Fly/FlyController.cs`

The first of two `FlyController` tasks. This one wires the **data and state**: collect every `ThrusterBehavior` on the construct in `BuildConstruct` (mirroring `_spawnedWeapons`), add the serialized Boost parameters, add the Boost resource field + the overboosted flag, add the read-only `BoostFraction` / `IsOverboosted` properties for the HUD, sample the `Boost` input each `Update`, and tick the drain/regen/overboosted state machine. The **per-`FixedUpdate` activation rule, the per-axis ×1.3, the boosted max-speed clamp, and the over-cap decay** are Task 5 — split out so this task compiles and the boost-state machine can be reasoned about on its own. Task 4 alone does not yet change flight forces; it adds a `_thrustInput.sqrMagnitude`-keyed drain that Task 5 refines into the real "actively boosting" condition. To keep Task 4 self-contained and correct, this task ticks the resource using a **provisional** `_boostingThisFrame` flag that Task 5 takes over — see Step 7.

- [ ] **Step 1: Add the serialized Boost parameter block**

In `Assets/Scripts/Fly/FlyController.cs`, find the end of the linear-thrust header block:

```csharp
        [Header("Linear thrust (Rigidbody.AddForce)")]
        [Tooltip("Force in Newtons applied per FixedUpdate while thrust input is held. Mass affects acceleration: accel = thrustForce / Rigidbody.mass. Starting value tuned for a ~25-mass construct; expect to retune.")]
        [SerializeField] float thrustForce = 100f;
        [Tooltip("Hard cap on Rigidbody.linearVelocity magnitude. Independent of mass — heavy ships just take longer to reach it.")]
        [SerializeField] float maxSpeed = 37.5f;
```

Replace it with:

```csharp
        [Header("Linear thrust (Rigidbody.AddForce)")]
        [Tooltip("Force in Newtons applied per FixedUpdate while thrust input is held. Mass affects acceleration: accel = thrustForce / Rigidbody.mass. Starting value tuned for a ~25-mass construct; expect to retune.")]
        [SerializeField] float thrustForce = 100f;
        [Tooltip("Hard cap on Rigidbody.linearVelocity magnitude. Independent of mass — heavy ships just take longer to reach it.")]
        [SerializeField] float maxSpeed = 37.5f;

        [Header("Boost (Thruster cubes — Left Ctrl)")]
        [Tooltip("Max Boost resource. The 0-100 meter starts each Fly session full at this value.")]
        [SerializeField] float boostMax = 100f;
        [Tooltip("Boost drained per second while actively boosting (>=1 thruster contributing).")]
        [SerializeField] float boostDrainPerSecond = 40f;
        [Tooltip("Boost regenerated per second when not boosting and not overboosted.")]
        [SerializeField] float boostRegenPerSecond = 15f;
        [Tooltip("Boost regenerated per second while overboosted — the slow recovery rate. Overboosted is entered when boost hits 0 and cleared only when boost regenerates all the way back to boostMax.")]
        [SerializeField] float boostRegenOverboostedPerSecond = 6f;
        [Tooltip("Per-axis thrust-force multiplier applied to any of the 6 input axes with at least one contributing thruster. Flat — the number of aligned thrusters does not matter.")]
        [SerializeField] float boostThrustMultiplier = 1.3f;
        [Tooltip("Max-speed multiplier while actively boosting — the linear-velocity clamp ceiling rises to maxSpeed * this.")]
        [SerializeField] float boostMaxSpeedMultiplier = 1.3f;
        [Tooltip("Speed (u/s per second) at which over-cap velocity eases back down to maxSpeed once boosting ends. Tuned so the drop from maxSpeed*boostMaxSpeedMultiplier to maxSpeed reads as quick but not an instant snap — a fraction of a second.")]
        [SerializeField] float overCapDecaySpeed = 60f;
```

`boostThrustMultiplier`, `boostMaxSpeedMultiplier`, and `overCapDecaySpeed` are read by Task 5 (the flight integration) — they are declared here so all Boost tuning sits in one serialized block and Task 5 adds no new fields.

- [ ] **Step 2: Add the `_spawnedThrusters` list next to `_spawnedWeapons`**

Find the `_spawnedWeapons` declaration:

```csharp
        // Collected during BuildConstruct, handed off to FlyShootingController
        // so it can group weapons by ShapeDefinition for selection + dispatch.
        readonly List<WeaponBehavior> _spawnedWeapons = new();
```

Replace it with:

```csharp
        // Collected during BuildConstruct, handed off to FlyShootingController
        // so it can group weapons by ShapeDefinition for selection + dispatch.
        readonly List<WeaponBehavior> _spawnedWeapons = new();

        // Collected during BuildConstruct — every ThrusterBehavior on the
        // spawned construct. The boost activation rule in FixedUpdate
        // walks this list each physics step to decide which input axes
        // get the ×1.3 thrust multiplier. Same collected-into-a-list
        // pattern as _spawnedWeapons.
        readonly List<ThrusterBehavior> _spawnedThrusters = new();
```

- [ ] **Step 3: Add the Boost resource state fields**

Find the cached flight-factor fields and the `TAG` constant:

```csharp
        float _linearForceFactor = 1f;
        float _torqueFactor = 1f;

        const string TAG = "FlyController";
```

Replace it with:

```csharp
        float _linearForceFactor = 1f;
        float _torqueFactor = 1f;

        // --- Boost resource state ---
        // _boost is the 0-100 meter; it starts full at boostMax (set in
        // Start). _overboosted latches true when _boost hits 0 and
        // clears only when _boost regenerates all the way back to
        // boostMax — while latched, boosting is disabled entirely.
        // _boostingThisFrame is set each FixedUpdate by the activation
        // rule (Task 5; provisionally in Task 4) — true iff >=1 thruster
        // is contributing, i.e. the construct is "actively boosting";
        // it drives drain-vs-regen and the boosted speed clamp.
        float _boost;
        bool _overboosted;
        bool _boostingThisFrame;

        // Sampled in Update from the Boost input action (Left Ctrl),
        // consumed in FixedUpdate — same Update-samples / FixedUpdate-
        // applies split as _thrustInput and the rotation inputs.
        bool _boostHeld;

        const string TAG = "FlyController";

        // Boost meter as a 0-1 fraction, for the HUD (FlyBoostBar).
        public float BoostFraction => boostMax > 0f ? Mathf.Clamp01(_boost / boostMax) : 0f;

        // True while the Boost resource is exhausted and recovering —
        // boosting is disabled until the meter refills to boostMax.
        // FlyBoostBar reads this to drive the "Overboosted!" flash.
        public bool IsOverboosted => _overboosted;
```

- [ ] **Step 4: Initialise `_boost` to full in `Start`**

Find the `Start` method's opening — `BuildConstruct()` and the log line:

```csharp
        void Start()
        {
            BuildConstruct();
            int total = GameData.PlacedCubes.Count + 1; // +1 for the alpha cube
            Debug.unityLogger.Log(TAG, $"FlyScene ready. Construct rebuilt: {total} cube(s) (including alpha). Weapons: {_spawnedWeapons.Count}.");
```

Replace it with:

```csharp
        void Start()
        {
            BuildConstruct();
            // Boost begins each Fly session full (spec §5).
            _boost = boostMax;
            int total = GameData.PlacedCubes.Count + 1; // +1 for the alpha cube
            Debug.unityLogger.Log(TAG, $"FlyScene ready. Construct rebuilt: {total} cube(s) (including alpha). Weapons: {_spawnedWeapons.Count}. Thrusters: {_spawnedThrusters.Count}.");
```

(The log line gains a `Thrusters: …` count — handy when verifying the Task 4 collection in the play-test.)

- [ ] **Step 5: Collect `ThrusterBehavior`s in `BuildConstruct`**

Find the weapon-collection block at the end of the `BuildConstruct` placement loop:

```csharp
                // Collect any WeaponBehavior on this placement — wire
                // it to the construct (so weapons can compare their
                // local rotation against construct.forward) and to
                // the source ShapeDefinition (so the toolbar can group
                // and label by shape).
                WeaponBehavior weapon = go.GetComponent<WeaponBehavior>();
                if (weapon != null)
                {
                    weapon.Construct = construct;
                    weapon.Shape = shape;
                    _spawnedWeapons.Add(weapon);
                }
            }
        }
```

Replace it with:

```csharp
                // Collect any WeaponBehavior on this placement — wire
                // it to the construct (so weapons can compare their
                // local rotation against construct.forward) and to
                // the source ShapeDefinition (so the toolbar can group
                // and label by shape).
                WeaponBehavior weapon = go.GetComponent<WeaponBehavior>();
                if (weapon != null)
                {
                    weapon.Construct = construct;
                    weapon.Shape = shape;
                    _spawnedWeapons.Add(weapon);
                }

                // Collect any ThrusterBehavior on this placement — wire
                // it to the construct so it can express its thrust
                // direction in the construct's local frame. Same
                // collected-into-a-list pattern as WeaponBehavior above.
                ThrusterBehavior thruster = go.GetComponent<ThrusterBehavior>();
                if (thruster != null)
                {
                    thruster.Construct = construct;
                    _spawnedThrusters.Add(thruster);
                }
            }
        }
```

- [ ] **Step 6: Sample the `Boost` input in `Update`**

Find the input-sampling block at the end of `Update`:

```csharp
            // Sample the input every frame; physics-paced application happens
            // in FixedUpdate.
            _thrustInput = _input.Fly.Thrust.ReadValue<Vector3>();
            _pitchInput  = _input.Fly.Pitch.ReadValue<float>();
            _yawInput    = _input.Fly.Yaw.ReadValue<float>();
            _rollInput   = _input.Fly.Roll.ReadValue<float>();
        }
```

Replace it with:

```csharp
            // Sample the input every frame; physics-paced application happens
            // in FixedUpdate.
            _thrustInput = _input.Fly.Thrust.ReadValue<Vector3>();
            _pitchInput  = _input.Fly.Pitch.ReadValue<float>();
            _yawInput    = _input.Fly.Yaw.ReadValue<float>();
            _rollInput   = _input.Fly.Roll.ReadValue<float>();
            _boostHeld   = _input.Fly.Boost.IsPressed();
        }
```

Note: the pause guard earlier in `Update` returns before this block, so a paused frame leaves `_boostHeld` at its last value — harmless, because `Time.timeScale = 0` freezes `FixedUpdate` so the boost state machine does not tick while paused. For symmetry with `_thrustInput` etc. it could be zeroed in the pause block, but it is not load-bearing; leave the pause block as-is.

- [ ] **Step 7: Add the Boost state-machine tick to `FixedUpdate`**

This task adds a **provisional** `_boostingThisFrame` evaluation and the drain/regen tick. Task 5 replaces the provisional evaluation with the real per-thruster activation rule; the drain/regen/overboosted code written here is final and Task 5 does not touch it.

Find the start of `FixedUpdate` — the `_rb` null-guard and the linear-thrust block:

```csharp
        void FixedUpdate()
        {
            if (_rb == null) return;

            // Linear thrust — local-frame input rotated into world frame,
            // then applied as continuous force. ForceMode.Force integrates
            // over dt internally; multiplying by Time.fixedDeltaTime here
            // would double-integrate and is a classic Rigidbody bug.
            if (_thrustInput.sqrMagnitude > 0f)
            {
                Vector3 worldThrust = construct.right   * _thrustInput.x +
                                      construct.up      * _thrustInput.y +
                                      construct.forward * _thrustInput.z;
                _rb.AddForce(worldThrust * (thrustForce * _linearForceFactor), ForceMode.Force);
            }
```

Replace it with:

```csharp
        void FixedUpdate()
        {
            if (_rb == null) return;

            // --- Boost: evaluate whether the construct is actively
            // boosting this physics step, then tick the resource.
            // PROVISIONAL evaluation (Task 4): boost-held + resource
            // available + any thrust input. Task 5 replaces this with
            // the real per-thruster, per-axis activation rule. The
            // drain/regen/overboosted code below is final.
            bool boostAvailable = _boostHeld && _boost > 0f && !_overboosted;
            _boostingThisFrame = boostAvailable && _thrustInput.sqrMagnitude > 0f && _spawnedThrusters.Count > 0;
            TickBoostResource();

            // Linear thrust — local-frame input rotated into world frame,
            // then applied as continuous force. ForceMode.Force integrates
            // over dt internally; multiplying by Time.fixedDeltaTime here
            // would double-integrate and is a classic Rigidbody bug.
            if (_thrustInput.sqrMagnitude > 0f)
            {
                Vector3 worldThrust = construct.right   * _thrustInput.x +
                                      construct.up      * _thrustInput.y +
                                      construct.forward * _thrustInput.z;
                _rb.AddForce(worldThrust * (thrustForce * _linearForceFactor), ForceMode.Force);
            }
```

- [ ] **Step 8: Add the `TickBoostResource` method**

Add the boost state-machine method to the `FlyController` class. Place it immediately after `FixedUpdate`'s closing brace, before the brace that closes the class:

```csharp

        // Ticks the Boost resource each FixedUpdate: drains while
        // actively boosting, regenerates otherwise, and runs the
        // overboosted latch. Overboosted is entered the moment boost
        // hits 0 and cleared only when boost regenerates all the way
        // back to boostMax — a full recovery is required (spec §5).
        // _boostingThisFrame is set by the activation rule just above
        // the call site in FixedUpdate.
        void TickBoostResource()
        {
            float dt = Time.fixedDeltaTime;

            if (_boostingThisFrame)
            {
                _boost -= boostDrainPerSecond * dt;
            }
            else
            {
                float regen = _overboosted ? boostRegenOverboostedPerSecond : boostRegenPerSecond;
                _boost += regen * dt;
            }

            _boost = Mathf.Clamp(_boost, 0f, boostMax);

            // Overboosted latch. Enter at 0; exit only at full boostMax.
            if (!_overboosted)
            {
                if (_boost <= 0f) _overboosted = true;
            }
            else
            {
                if (_boost >= boostMax) _overboosted = false;
            }
        }
```

- [ ] **Step 9: Compile + console check**

Refresh Unity (`refresh_unity`, `compile="request"`, `mode="force"`, `scope="all"`, `wait_for_ready=true`), poll `mcpforunity://editor/state` until `is_compiling=false` and `ready_for_tools=true`, then `read_console(action="get", types=["error"], count=50)`.

Expected: zero errors. `ThrusterBehavior` (Task 1) and `_input.Fly.Boost` (Task 3) both exist, so all references resolve. The Boost block does not yet change flight forces — that is Task 5.

- [ ] **Step 10: Commit**

```bash
git add "Assets/Scripts/Fly/FlyController.cs"
git commit -m "Collect ThrusterBehaviors and add the Boost resource state machine"
```

---

## Task 5: `FlyController` — per-`FixedUpdate` activation rule, per-axis ×1.3, boosted max-speed clamp + over-cap decay

**Files:**
- Modify: `Assets/Scripts/Fly/FlyController.cs`

The second `FlyController` task — the **flight integration**. It replaces Task 4's provisional `_boostingThisFrame` with the real activation rule (spec §6.2), applies the per-axis ×1.3 thrust multiplier (§6.3), and replaces the hard `maxSpeed` clamp with the boosted clamp + post-boost over-cap decay (§6.4).

**The activation rule (spec §6.2)** — a thruster contributes this `FixedUpdate` iff all three hold:
1. Left Ctrl is held (`_boostHeld`).
2. Boost resource `> 0` and not overboosted (`_boost > 0f && !_overboosted`).
3. The player is commanding movement along that thruster's thrust axis — the matching component of `_thrustInput` is non-zero **and the same sign** as the thrust direction.

`_thrustInput` is in the construct's local frame (`x` = strafe, `y` = vertical, `z` = forward — see the field comment). `ThrusterBehavior.LocalThrustAxis` is the thrust direction in the **same** construct-local frame, snapped to a unit axis. So condition 3 is a direct per-component sign match: a thruster with `LocalThrustAxis = (0,0,+1)` (thrust = construct-forward) contributes only when `_thrustInput.z > 0`.

**The effect (spec §6.3)** — collect the contributions into a per-axis `Vector3 boostAxes` where each component is `0` or `1` (`1` = "≥1 thruster contributes on this axis"). It is a flat OR — a second aligned thruster does not raise the value past `1` (no stacking). The thrust force on a boosted axis is multiplied by `boostThrustMultiplier` (×1.3). `_boostingThisFrame` ("actively boosting") is true iff any component of `boostAxes` is `1`.

**The clamp (spec §6.4)** — the velocity-clamp ceiling is `maxSpeed * boostMaxSpeedMultiplier` while `_boostingThisFrame`, else `maxSpeed`. The hard clamp applies at that *active* ceiling (a true cap — thrust cannot push past it). Separately, when not boosting and the speed is still above `maxSpeed` (leftover from a just-ended boost), the speed is eased down toward `maxSpeed` at `overCapDecaySpeed` u/s per second — a fast but non-instant decay. The hard clamp and the decay coexist: the clamp stops *new* overshoot, the decay bleeds off *existing* over-cap speed.

- [ ] **Step 1: Replace the provisional `_boostingThisFrame` with the real activation rule**

In `Assets/Scripts/Fly/FlyController.cs`, find the Task 4 provisional boost block and the linear-thrust block in `FixedUpdate`:

```csharp
            // --- Boost: evaluate whether the construct is actively
            // boosting this physics step, then tick the resource.
            // PROVISIONAL evaluation (Task 4): boost-held + resource
            // available + any thrust input. Task 5 replaces this with
            // the real per-thruster, per-axis activation rule. The
            // drain/regen/overboosted code below is final.
            bool boostAvailable = _boostHeld && _boost > 0f && !_overboosted;
            _boostingThisFrame = boostAvailable && _thrustInput.sqrMagnitude > 0f && _spawnedThrusters.Count > 0;
            TickBoostResource();

            // Linear thrust — local-frame input rotated into world frame,
            // then applied as continuous force. ForceMode.Force integrates
            // over dt internally; multiplying by Time.fixedDeltaTime here
            // would double-integrate and is a classic Rigidbody bug.
            if (_thrustInput.sqrMagnitude > 0f)
            {
                Vector3 worldThrust = construct.right   * _thrustInput.x +
                                      construct.up      * _thrustInput.y +
                                      construct.forward * _thrustInput.z;
                _rb.AddForce(worldThrust * (thrustForce * _linearForceFactor), ForceMode.Force);
            }
```

Replace it with:

```csharp
            // --- Boost: evaluate the per-axis activation rule, tick the
            // resource, then apply the per-axis ×1.3 to the thrust.
            // boostAxes.{x,y,z} is 1 when >=1 thruster contributes on
            // that input axis, else 0 — a flat OR, no stacking.
            Vector3 boostAxes = EvaluateBoostAxes();
            _boostingThisFrame = boostAxes.x > 0f || boostAxes.y > 0f || boostAxes.z > 0f;
            TickBoostResource();

            // Linear thrust — local-frame input rotated into world frame,
            // then applied as continuous force. ForceMode.Force integrates
            // over dt internally; multiplying by Time.fixedDeltaTime here
            // would double-integrate and is a classic Rigidbody bug.
            //
            // Boost: each input axis with a contributing thruster has its
            // thrust-force component multiplied by boostThrustMultiplier
            // (×1.3). Flat — boostAxes components are 0 or 1, so the
            // multiplier never compounds with the count of thrusters.
            if (_thrustInput.sqrMagnitude > 0f)
            {
                float mulX = boostAxes.x > 0f ? boostThrustMultiplier : 1f;
                float mulY = boostAxes.y > 0f ? boostThrustMultiplier : 1f;
                float mulZ = boostAxes.z > 0f ? boostThrustMultiplier : 1f;
                Vector3 worldThrust = construct.right   * (_thrustInput.x * mulX) +
                                      construct.up      * (_thrustInput.y * mulY) +
                                      construct.forward * (_thrustInput.z * mulZ);
                _rb.AddForce(worldThrust * (thrustForce * _linearForceFactor), ForceMode.Force);
            }
```

- [ ] **Step 2: Replace the hard `maxSpeed` clamp with the boosted clamp + over-cap decay**

Find the hard speed-cap block in `FixedUpdate`:

```csharp
            // Hard speed cap. linearVelocity is the Unity 6 name; pre-6
            // would be `velocity`. We accept whatever drag has already done
            // and clamp on top so a long burn doesn't blow past maxSpeed.
            Vector3 v = _rb.linearVelocity;
            if (v.sqrMagnitude > maxSpeed * maxSpeed)
                _rb.linearVelocity = v.normalized * maxSpeed;
```

Replace it with:

```csharp
            // Speed cap with Boost. While actively boosting, the ceiling
            // is maxSpeed * boostMaxSpeedMultiplier (×1.3); otherwise it
            // is maxSpeed. The hard clamp applies at that *active*
            // ceiling — a true cap, so thrust can't push past it.
            //
            // Post-boost over-cap decay: when boosting has just ended,
            // speed may still sit above maxSpeed. We don't hard-snap it
            // down — we ease it toward maxSpeed at overCapDecaySpeed
            // (u/s per second), a fast but non-instant drop. The hard
            // clamp (above) and the decay coexist: the clamp stops new
            // overshoot at the active ceiling, the decay bleeds off
            // existing over-cap speed once the ceiling has dropped.
            float speedCeiling = _boostingThisFrame
                ? maxSpeed * boostMaxSpeedMultiplier
                : maxSpeed;

            Vector3 v = _rb.linearVelocity;
            float speed = v.magnitude;

            if (speed > speedCeiling)
            {
                // Hard clamp at the active ceiling.
                _rb.linearVelocity = v * (speedCeiling / speed);
            }
            else if (!_boostingThisFrame && speed > maxSpeed)
            {
                // Not boosting, still over the normal cap (boost just
                // ended) — ease down toward maxSpeed, don't snap.
                float decayed = Mathf.MoveTowards(speed, maxSpeed, overCapDecaySpeed * Time.fixedDeltaTime);
                _rb.linearVelocity = v * (decayed / speed);
            }
```

Note the clamp branch divides by `speed` (the actual magnitude) rather than calling `v.normalized` — `v.normalized * ceiling` is equivalent, but reusing the already-computed `speed` avoids a second `sqrt`. The `speed > speedCeiling` guard means `speed` is `> 0`, so the division is safe.

- [ ] **Step 3: Add the `EvaluateBoostAxes` method**

Add the activation-rule method to the `FlyController` class. Place it immediately after `TickBoostResource` (added in Task 4), before the brace that closes the class:

```csharp

        // Evaluates the boost activation rule (spec §6.2) and returns a
        // per-axis mask: component = 1 when >=1 thruster contributes on
        // that input axis this FixedUpdate, else 0. A thruster
        // contributes iff ALL THREE hold:
        //   (1) Left Ctrl is held;
        //   (2) the Boost resource is >0 and not overboosted;
        //   (3) the player is commanding thrust along the thruster's
        //       axis — the matching _thrustInput component is non-zero
        //       and the SAME SIGN as the thruster's thrust direction.
        // _thrustInput and ThrusterBehavior.LocalThrustAxis are both in
        // the construct's local frame, so condition (3) is a direct
        // per-component sign match. The mask is a flat OR — a second
        // aligned thruster does not push a component past 1 (no
        // stacking, spec §6.3).
        Vector3 EvaluateBoostAxes()
        {
            Vector3 axes = Vector3.zero;

            // Conditions (1) and (2) are global — fail them and no
            // thruster can contribute, so skip the per-thruster walk.
            if (!_boostHeld || _boost <= 0f || _overboosted) return axes;

            for (int i = 0; i < _spawnedThrusters.Count; i++)
            {
                ThrusterBehavior thruster = _spawnedThrusters[i];
                if (thruster == null) continue;

                Vector3 dir = thruster.LocalThrustAxis;

                // Condition (3), per axis: input non-zero AND same sign
                // as the thrust direction. dir is snapped to a unit
                // axis so exactly one component is non-zero; the
                // products below isolate that axis.
                if (dir.x != 0f && _thrustInput.x * dir.x > 0f) axes.x = 1f;
                if (dir.y != 0f && _thrustInput.y * dir.y > 0f) axes.y = 1f;
                if (dir.z != 0f && _thrustInput.z * dir.z > 0f) axes.z = 1f;
            }

            return axes;
        }
```

- [ ] **Step 4: Compile + console check**

Refresh Unity (`refresh_unity`, `compile="request"`, `mode="force"`, `scope="all"`, `wait_for_ready=true`), poll `mcpforunity://editor/state` until `is_compiling=false` and `ready_for_tools=true`, then `read_console(action="get", types=["error"], count=50)`.

Expected: zero errors. `EvaluateBoostAxes` uses `_spawnedThrusters` / `_boostHeld` / `_boost` / `_overboosted` / `_thrustInput` (all from Task 4 or pre-existing) and `boostThrustMultiplier` / `boostMaxSpeedMultiplier` / `overCapDecaySpeed` (the Task 4 serialized block). The variable `v` is still declared exactly once in `FixedUpdate` (the clamp block) — Step 2 replaced the old `Vector3 v` line with a new one, it did not add a second. If the console reports *"a local variable named 'v' is already defined"*, an old `Vector3 v` line survived — re-check Step 2's replacement covered the whole original clamp block.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Fly/FlyController.cs"
git commit -m "Apply the per-axis boost multiplier and boosted speed clamp in FixedUpdate"
```

---

## Task 6: Add `FlyBoostBar.cs` + its canonical `.meta`

**Files:**
- Create: `Assets/Scripts/Fly/FlyBoostBar.cs`
- Create: `Assets/Scripts/Fly/FlyBoostBar.cs.meta`

The Boost HUD element (spec §7) — a thin **vertical bar left of the crosshair**, **screen-fixed near screen centre**. It follows the project's HUD-element pattern (`FlySpeedIndicator` / `FlyHpIndicator`): a `MonoBehaviour` that builds its own `ScreenSpaceOverlay` canvas via `UIStyle.BuildScreenSpaceCanvas` in `Awake`, auto-wires its `FlyController` reference in `Start`, and reads game state each `Update`.

Behaviour:
- **Fill** = `BoostFraction` (0–1). The bar's fill `Image` is anchored at its bottom edge and grows upward; `rectTransform.sizeDelta.y` (or an anchor) scales with the fraction.
- **Opacity ramps with use** — `alpha = 1 − BoostFraction`. At full boost (`BoostFraction == 1`) the bar is near-invisible; drained (`BoostFraction == 0`) it is opaque. Applied to both the fill and the bar's background/frame.
- **Screen-fixed**, anchored at screen centre with a leftward offset — **not** parented to `FlyCrosshair`'s reticle (the reticle drifts as the construct turns; spec §7 is explicit).
- **`"Overboosted!"` flash** — when `IsOverboosted` flips false→true, a text label near the crosshair flashes **3×** (visible/hidden cycles), then hides. A coroutine drives the flash; it is edge-triggered off `IsOverboosted` so it fires once per overboosted entry.

It does **not** build a Heat bar — that is deferred (spec §9).

- [ ] **Step 1: Create `FlyBoostBar.cs`**

Create `Assets/Scripts/Fly/FlyBoostBar.cs` with this exact content:

```csharp
using System.Collections;
using CubeFly.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CubeFly.Fly
{
    // Boost HUD element — a thin vertical bar to the LEFT of the
    // crosshair, showing the FlyController Boost resource. Mirrors the
    // FlySpeedIndicator / FlyHpIndicator pattern: builds its own
    // ScreenSpaceOverlay canvas via UIStyle in Awake, auto-wires its
    // FlyController in Start, reads game state each Update.
    //
    // Fill    — bar height = BoostFraction (0-1), anchored at the
    //           bottom and growing up.
    // Opacity — alpha = 1 - BoostFraction: near-invisible at full
    //           boost, opaque when drained ("ramps with use", spec §7).
    // Position— screen-fixed near screen centre, offset left. NOT
    //           parented to the FlyCrosshair reticle — that reticle
    //           drifts as the construct turns and would drag the bar
    //           around with it (spec §7).
    //
    // "Overboosted!" — when FlyController.IsOverboosted flips
    // false->true, a label near the crosshair flashes 3× then hides.
    //
    // The Heat bar (the crosshair's right-side bar) is deferred to the
    // Laser-weapon roadmap item (spec §9) — this component builds only
    // the Boost bar.
    public class FlyBoostBar : MonoBehaviour
    {
        [SerializeField] FlyController flyController;

        [Header("Bar layout (screen-centre relative)")]
        [Tooltip("Anchored position of the bar's centre relative to screen centre. Negative x sits it left of the crosshair.")]
        [SerializeField] Vector2 anchoredPosition = new Vector2(-90f, 0f);
        [Tooltip("Bar size in pixels — thin and tall (a vertical bar).")]
        [SerializeField] Vector2 barSize = new Vector2(12f, 120f);
        [SerializeField] Color fillColor = new Color(0.36f, 0.62f, 1f, 1f);
        [SerializeField] Color frameColor = new Color(0.05f, 0.07f, 0.12f, 1f);

        [Header("Overboosted flash")]
        [Tooltip("Anchored position of the flash label relative to screen centre. Sits above the crosshair.")]
        [SerializeField] Vector2 flashAnchoredPosition = new Vector2(0f, 70f);
        [SerializeField] int flashFontSize = 26;
        [SerializeField] Color flashColor = new Color(1f, 0.45f, 0.3f, 1f);
        [Tooltip("Number of visible/hidden flash cycles on overboosted entry.")]
        [SerializeField] int flashCount = 3;
        [Tooltip("Seconds the flash label is visible per cycle.")]
        [SerializeField] float flashOnSeconds = 0.18f;
        [Tooltip("Seconds the flash label is hidden per cycle.")]
        [SerializeField] float flashOffSeconds = 0.12f;

        Canvas _canvas;
        RectTransform _fill;     // grows bottom-up with BoostFraction
        Image _fillImage;
        Image _frameImage;
        Text _flashLabel;

        // Edge-detect for the Overboosted flash — fire the flash once
        // per false->true transition of FlyController.IsOverboosted.
        bool _wasOverboosted;
        Coroutine _flashRoutine;

        const string TAG = "FlyBoostBar";

        void Awake()
        {
            BuildUI();
        }

        void Start()
        {
            if (flyController == null) flyController = FindAnyObjectByType<FlyController>();
            if (flyController == null)
                Debug.unityLogger.LogWarning(TAG, "No FlyController in scene; Boost bar will idle empty.");
        }

        void Update()
        {
            if (_fill == null || flyController == null) return;

            float fraction = Mathf.Clamp01(flyController.BoostFraction);

            // Fill height tracks the fraction; the fill rect is pinned
            // to the bar's bottom edge (pivot/anchor y = 0) so it grows
            // upward.
            _fill.sizeDelta = new Vector2(barSize.x, barSize.y * fraction);

            // Opacity ramps with use: invisible at full boost, opaque
            // when drained.
            float alpha = 1f - fraction;
            SetImageAlpha(_fillImage, fillColor, alpha);
            SetImageAlpha(_frameImage, frameColor, alpha);

            // Edge-triggered Overboosted flash.
            bool overboosted = flyController.IsOverboosted;
            if (overboosted && !_wasOverboosted)
            {
                if (_flashRoutine != null) StopCoroutine(_flashRoutine);
                _flashRoutine = StartCoroutine(FlashOverboosted());
            }
            _wasOverboosted = overboosted;
        }

        static void SetImageAlpha(Image img, Color baseColor, float alpha)
        {
            if (img == null) return;
            img.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
        }

        // Flashes the "Overboosted!" label flashCount times (visible /
        // hidden cycles), then leaves it hidden. WaitForSecondsRealtime
        // so the flash still animates if a future pause sets
        // Time.timeScale = 0 — though overboosted entry happens during
        // active flight, so in practice unscaled vs scaled is moot.
        IEnumerator FlashOverboosted()
        {
            if (_flashLabel == null) yield break;

            for (int i = 0; i < flashCount; i++)
            {
                _flashLabel.enabled = true;
                yield return new WaitForSecondsRealtime(flashOnSeconds);
                _flashLabel.enabled = false;
                yield return new WaitForSecondsRealtime(flashOffSeconds);
            }
            _flashLabel.enabled = false;
            _flashRoutine = null;
        }

        // ---------- UI ----------

        void BuildUI()
        {
            UIStyle.EnsureEventSystem();
            // sortingOrder 115 sits just above FlyCrosshair (110) and
            // below FlyWeaponToolbar (120) — the bar reads on top of the
            // reticle without occluding the toolbar.
            _canvas = UIStyle.BuildScreenSpaceCanvas("FlyBoostBarCanvas", sortingOrder: 115);
            RectTransform canvasRoot = (RectTransform)_canvas.transform;
            int uiLayer = LayerMask.NameToLayer("UI");

            // Frame — the bar's background, centred on screen then
            // offset by anchoredPosition. Centre anchor + centre pivot
            // so anchoredPosition reads as an offset from screen centre.
            GameObject frameGO = new GameObject("BoostBarFrame",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            if (uiLayer >= 0) frameGO.layer = uiLayer;
            frameGO.transform.SetParent(canvasRoot, false);
            RectTransform frameRT = (RectTransform)frameGO.transform;
            frameRT.anchorMin = frameRT.anchorMax = frameRT.pivot = new Vector2(0.5f, 0.5f);
            frameRT.sizeDelta = barSize;
            frameRT.anchoredPosition = anchoredPosition;
            _frameImage = frameGO.GetComponent<Image>();
            _frameImage.color = frameColor;
            _frameImage.raycastTarget = false;

            // Fill — child of the frame, pinned to the frame's BOTTOM
            // edge (anchor + pivot y = 0) so it grows upward as the
            // height is set each Update.
            GameObject fillGO = new GameObject("BoostBarFill",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            if (uiLayer >= 0) fillGO.layer = uiLayer;
            fillGO.transform.SetParent(frameGO.transform, false);
            _fill = (RectTransform)fillGO.transform;
            _fill.anchorMin = _fill.anchorMax = _fill.pivot = new Vector2(0.5f, 0f);
            _fill.sizeDelta = new Vector2(barSize.x, 0f);
            _fill.anchoredPosition = Vector2.zero;
            _fillImage = fillGO.GetComponent<Image>();
            _fillImage.color = fillColor;
            _fillImage.raycastTarget = false;

            // "Overboosted!" flash label — screen-centre relative,
            // offset above the crosshair. Hidden until a flash fires.
            _flashLabel = UIStyle.BuildLabel(canvasRoot, "Overboosted!", fontSize: flashFontSize, style: FontStyle.Bold);
            _flashLabel.color = flashColor;
            RectTransform flashRT = (RectTransform)_flashLabel.transform;
            flashRT.anchorMin = flashRT.anchorMax = flashRT.pivot = new Vector2(0.5f, 0.5f);
            flashRT.sizeDelta = new Vector2(360f, 44f);
            flashRT.anchoredPosition = flashAnchoredPosition;
            _flashLabel.enabled = false;
        }
    }
}
```

Notes for the implementer:
- `UIStyle.BuildLabel(parent, text, fontSize, style)` — confirmed signature from `Assets/Scripts/Core/UIStyle.cs`: `public static Text BuildLabel(Transform parent, string text, int fontSize, FontStyle style = FontStyle.Normal)`. `UIStyle.BuildScreenSpaceCanvas(name, sortingOrder)` and `UIStyle.EnsureEventSystem()` are likewise used verbatim from that file.
- The fill grows by setting `sizeDelta.y` on a bottom-pivoted rect — the same "anchored rect, set size each frame" approach the other HUD elements use; no `Image.fillAmount` / `Image.Type.Filled` sprite needed (the project builds plain `Image` rects, e.g. `FlyCrosshair`).
- `_flashLabel.enabled` toggles the `Text` component's rendering — cheaper than `SetActive` and standard for a flash.

- [ ] **Step 2: Create the canonical `.meta` for `FlyBoostBar.cs`**

Create `Assets/Scripts/Fly/FlyBoostBar.cs.meta` with this exact content (a fresh unique 32-hex GUID — unused in the project; if Unity already generated a `.meta`, overwrite it with this block but keep Unity's GUID if it differs and is already referenced):

```
fileFormatVersion: 2
guid: 7c1e2d3a4b5c6d7e8f9a0b1c2d3e4f02
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:
```

Confirm the GUID `7c1e2d3a4b5c6d7e8f9a0b1c2d3e4f02` is unused with `grep -rl 7c1e2d3a4b5c6d7e8f9a0b1c2d3e4f02 Assets/` before committing — if it collides, pick another random 32-hex value (and it must differ from `ThrusterBehavior`'s `…4f01`).

- [ ] **Step 3: Compile + console check**

Refresh Unity (`refresh_unity`, `compile="request"`, `mode="force"`, `scope="all"`, `wait_for_ready=true`), poll `mcpforunity://editor/state` until `is_compiling=false` and `ready_for_tools=true`, then `read_console(action="get", types=["error"], count=50)`.

Expected: zero errors. `FlyBoostBar` references `UIStyle` (internal to `CubeFly.Core` — `FlyBoostBar` is in `CubeFly.Fly`, which already `using CubeFly.Core;` as the other HUD scripts do, and `UIStyle` is `internal` so same-assembly access works — both namespaces compile into `Assembly-CSharp`) and `FlyController.BoostFraction` / `IsOverboosted` (added in Task 4). If the console reports *"'UIStyle' is inaccessible due to its protection level"*, confirm `FlyBoostBar` is in `Assembly-CSharp` (it is — no `.asmdef` in `Assets/Scripts/Fly/`, same as `FlySpeedIndicator`).

- [ ] **Step 4: Commit**

```bash
git add "Assets/Scripts/Fly/FlyBoostBar.cs" "Assets/Scripts/Fly/FlyBoostBar.cs.meta"
git commit -m "Add FlyBoostBar — Boost HUD bar left of the crosshair"
```

---

## Task 7: Add `FlyBoostBar` to `FlyScene.unity`

**Files:**
- Modify: `Assets/Scenes/FlyScene.unity`

Add the `FlyBoostBar` component to the `FlyHUD` GameObject in `FlyScene.unity`, alongside `FlyCrosshair`, `FlyWeaponToolbarController`, `FlySpeedIndicator`, and `FlyHpIndicator`. `FlyBoostBar` auto-wires its `FlyController` in `Start` (`FindAnyObjectByType<FlyController>()`) — like `FlySpeedIndicator` and `FlyHpIndicator`, whose serialized `flyController` is `{fileID: 0}` in the scene — so an explicit serialized reference is not required, though wiring it is harmless and slightly faster.

`FlyHUD` is GameObject `&700000` in `FlyScene.unity`; its `m_Component` list currently has five entries (`Transform 700001`, `FlyCrosshair 700002`, `FlyWeaponToolbarController 700003`, `FlySpeedIndicator 700004`, `FlyHpIndicator 700005`).

- [ ] **Step 1: Add the `FlyBoostBar` component to `FlyHUD`**

Add a `CubeFly.Fly.FlyBoostBar` component to the `FlyHUD` GameObject in `Assets/Scenes/FlyScene.unity`. Recommended — `mcp__UnityMCP__manage_gameobject` (target `FlyHUD` in the loaded `FlyScene`, add component `CubeFly.Fly.FlyBoostBar`) or `mcp__UnityMCP__manage_scene` + an `execute_code` editor snippet:

```csharp
// Editor-side, run via execute_code. Assumes FlyScene is the open scene.
var hud = GameObject.Find("FlyHUD");
if (hud != null && hud.GetComponent<CubeFly.Fly.FlyBoostBar>() == null)
{
    hud.AddComponent<CubeFly.Fly.FlyBoostBar>();
    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(hud.scene);
    UnityEditor.SceneManagement.EditorSceneManager.SaveScene(hud.scene);
}
```

Leave the `FlyBoostBar` serialized fields at their script defaults (`anchoredPosition (-90, 0)`, `barSize (12, 120)`, the colours, the flash parameters) — they are the spec §7 layout and are tunable later in the inspector. The `flyController` field may be left `{fileID: 0}` (auto-wired in `Start`) or pointed at the `FlyController` component (`&300002` on the `FlyController` GameObject) — either is fine; matching `FlySpeedIndicator` / `FlyHpIndicator` (which leave it `0`) is simplest.

- [ ] **Step 2: Verify the scene edit**

Read the `FlyHUD` region of `Assets/Scenes/FlyScene.unity` and confirm:
- The `FlyHUD` GameObject's `m_Component` list now has **six** entries — the original five plus one more.
- A new `MonoBehaviour` block exists with `m_Script` GUID `7c1e2d3a4b5c6d7e8f9a0b1c2d3e4f02` (the Task 6 `FlyBoostBar` GUID); its `m_EditorClassIdentifier` reads `Assembly-CSharp::CubeFly.Fly.FlyBoostBar`. Expected shape:

```yaml
--- !u!114 &<fresh fileID>
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 700000}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: 7c1e2d3a4b5c6d7e8f9a0b1c2d3e4f02, type: 3}
  m_Name:
  m_EditorClassIdentifier: Assembly-CSharp::CubeFly.Fly.FlyBoostBar
  flyController: {fileID: 0}
  anchoredPosition: {x: -90, y: 0}
  barSize: {x: 12, y: 120}
  ...
```

Refresh Unity and `read_console(action="get", types=["error"], count=50)` — zero errors (a missing-script reference on `FlyHUD` would surface here).

- [ ] **Step 3: Commit**

```bash
git add "Assets/Scenes/FlyScene.unity"
git commit -m "Add FlyBoostBar to the FlyHUD in FlyScene"
```

---

## Task 8: FlyScene play-test verification

**Files:**
- No code changes expected. If the play-test uncovers a defect, fix it in the relevant file from Tasks 1–7, re-verify, and commit the fix.

The acceptance gate for PR 2 — spec §10's "PR 2" test plan. The Thruster, inert since PR 1, now boosts.

- [ ] **Step 1: Clean compile**

Refresh Unity (`refresh_unity`, `compile="request"`, `mode="force"`, `scope="all"`, `wait_for_ready=true`), poll `mcpforunity://editor/state` until `is_compiling=false` and `ready_for_tools=true`, then `read_console(action="get", types=["error"], count=50)`. Zero errors required.

- [ ] **Step 2: Build a test construct with a backward-pointing thruster**

In the Build scene (Play mode), build a small construct: the alpha cube plus **one Thruster placed so its cone tip points backward** — i.e. the apex faces the construct's `−Z` (rear). Thrust is `−transform.up` = the construct's `+Z` (forward), so the thruster pushes the ship forward. Save the construct. (If a quicker path exists — a pre-saved test construct, or editing `GameData.PlacedCubes` directly — that is fine; the requirement is a construct with at least one forward-thrusting thruster.)

- [ ] **Step 3: Play-test the Boost in FlyScene**

Enter the FlyScene with that construct and verify each item — spec §10's PR 2 acceptance criteria:

1. **Thruster collected** — the console's `FlyScene ready` log line reports `Thrusters: 1` (the Task 4 Step 4 log). If it reads `Thrusters: 0`, `BuildConstruct` did not collect the thruster — re-check Task 2 (the prefab has the component) and Task 4 Step 5 (the collection block).
2. **Boosted acceleration** — fly forward (`W`) without boost and note the spin-up; then hold **Left Ctrl** while holding `W`. Acceleration is **noticeably stronger** with Ctrl held — the construct picks up speed faster.
3. **Speed past the normal cap** — holding `W` + Left Ctrl, the `Speed: NN.N u/s` readout climbs **past the normal `maxSpeed`** (37.5 by default) toward `maxSpeed × 1.3` (≈48.75). Without boost it plateaus at ≈37.5.
4. **Boost bar fades in** — the thin vertical bar **left of the crosshair** is near-invisible at the start of the session (full boost) and **fades toward opaque** as boost drains while boosting. Its fill height shrinks as the meter drains. It is screen-fixed near centre — it does **not** drift when the ship turns (turn the ship hard while watching it).
5. **Overboosted at zero** — keep holding `W` + Left Ctrl until the Boost meter hits 0. At that moment: `"Overboosted!"` **flashes 3×** near the crosshair, the boost cuts out (acceleration drops back to normal, speed eases down — see item 8), and the meter then **regenerates slowly** (the 6/s overboosted rate) until full, at which point overboosted clears and a normal boost is available again.
6. **No matching input drains nothing** — with the meter part-full and **not** overboosted: hold Left Ctrl while commanding thrust on an axis with no aligned thruster (e.g. strafe `A`/`D`, or reverse `S`), or hold Left Ctrl with no thrust input at all. The Boost meter does **not** drain and the bar does not fade in — a thruster only fires when thrust is commanded the way it pushes.
7. **Per-axis correctness** — the boost applies only to the forward axis here (the one thruster pushes `+Z`). Confirm `W` + Ctrl boosts but `S` + Ctrl (reverse, no aligned thruster) does not. (Optional: place thrusters on other faces and confirm strafe / vertical boost works per-axis — spec §10 "all 6 axes".)
8. **Post-boost over-cap decay** — while boosting, get the speed above the normal cap (item 3), then **release Left Ctrl** (keep `W` held). The speed does **not** snap instantly to `maxSpeed` — it **eases down** over a fraction of a second to ≈37.5, then holds there. The drop is quick but visibly smooth, not a hard cut.
9. **Releasing Ctrl reverts the cap** — once the over-cap decay has settled, with Ctrl released the speed is clamped at the normal `maxSpeed` again; pressing Ctrl (boost available) lifts the ceiling back to `maxSpeed × 1.3`.

- [ ] **Step 4: Final commit**

If Step 3 surfaced no defect, Tasks 1–7 already committed everything — `git status` is clean for all touched files; record completion without a new commit. If Step 3 required a fix, commit it with a message naming the defect, e.g.:

```bash
git add <fixed file(s)>
git commit -m "Fix Boost defect found in FlyScene play-test"
```

Then report PR 2 — and the whole Thruster feature — complete: a placed Thruster (PR 1) now boosts in flight. Holding Left Ctrl while commanding thrust along a thruster's push axis multiplies that axis's acceleration ×1.3 and lifts the speed cap ×1.3; the Boost resource drains while boosting and regenerates otherwise; exhausting it triggers the overboosted lock-out with an `"Overboosted!"` flash; and the Boost bar left of the crosshair shows the meter.

---

## Notes & risks

- **`CubeFlyInputActions` is hand-written — edit the `.cs`, not the asset (Task 3, the main input risk).** The file's own header states it is a *"Hand-rolled wrapper that mirrors the Generate-C#-Class output Unity would produce … Defining the bindings in code avoids a hard dependency on the editor's wrapper-generation pass."* So the runtime `Boost` action comes entirely from the `.cs` edit (the new `InputAction`, the `FlyActions` constructor parameter, the `Boost` property). The `.inputactions` JSON asset is **not loaded at runtime** — it is already out of sync with the wrapper (the asset has no `Fire` action, the wrapper does). Task 3 Step 3 updates the asset for parity / documentation, but if the JSON edit is fiddly it may be skipped with a commit note — the behaviour is unaffected. The one real failure mode is forgetting to update the single `Fly = new FlyActions(...)` call site when the constructor signature grows by one parameter; Task 3 Step 1 changes the call and the constructor in the same replacement, and Step 4 calls out the exact compile error if they drift. `<Keyboard>/leftCtrl` is the correct control path for Left Ctrl (the new Input System exposes left/right modifiers as distinct controls — `leftCtrl` / `rightCtrl`; a generic `ctrl` would also bind but Left Ctrl specifically is the spec requirement).
- **`FlyController.FixedUpdate` integration — split across two tasks on purpose (Tasks 4 & 5).** The Boost change to `FixedUpdate` is substantial: a new activation evaluation, a per-axis thrust multiplier, and a rewritten speed clamp. Task 4 lands the *data + state machine* with a **provisional** `_boostingThisFrame` (boost-held + any thrust input) so the project compiles and the drain/regen/overboosted machine can be verified in isolation; Task 5 swaps in the real per-thruster, per-axis rule and the boosted clamp + decay. The drain/regen code in `TickBoostResource` is written final in Task 4 and untouched by Task 5 — only the *evaluation* of `_boostingThisFrame` changes. The risk is the `Vector3 v` local in the clamp block: Task 5 Step 2 replaces the **entire** original clamp block (comment + `Vector3 v` + the `if`), so `v` is still declared exactly once; Step 4 names the exact "already defined" compile error if a stale line survives. A second subtlety — the per-axis multiplier multiplies `_thrustInput` components *before* they are scaled by `construct.right/up/forward`, so the ×1.3 lands on the force contribution of that axis only, exactly as spec §6.3 wants; it does not touch the other axes' contributions even when the player commands a diagonal.
- **`_thrustInput` and `LocalThrustAxis` share the construct-local frame — that is what makes the sign match exact.** `_thrustInput` is documented in `FlyController` as `x = strafe (+R/-L), y = vertical (+U/-D), z = forward (+F/-B)` — the construct's local axes. `ThrusterBehavior.LocalThrustAxis` is `−transform.up` run through `construct.InverseTransformDirection` and snapped — the thrust direction in the *same* frame. So activation condition 3 (`_thrustInput.<axis> * dir.<axis> > 0`) is a direct, exact per-component sign test. The snap (`Mathf.Round(Mathf.Clamp(…, −1, 1))`) is essential: without it a thruster placed at a 90° step could read `0.99999` and the `!= 0` test would still pass, but the snap also guards the (out-of-spec but defensive) case of a non-axis-aligned placement by collapsing it to the nearest axis.
- **`LocalThrustAxis` is cached after first read — correct because the construct is rigid.** `ResolveThrustAxis` needs `Construct` set (done by `FlyController.BuildConstruct` before any `FixedUpdate`) and the cube's local pose final (also fixed at build time). Caching on first access avoids an `InverseTransformDirection` per thruster per physics step. Cube destruction mid-flight does not move surviving cubes, so the cached axis stays valid — the same "resolved once" assumption `FlyController.ResolveRigidbody` already makes for mass. If a future feature re-poses cubes mid-flight, `LocalThrustAxis` would need invalidation; out of scope here.
- **`FlyBoostBar` is screen-fixed, NOT crosshair-parented (spec §7).** `FlyCrosshair`'s reticle `_root` is repositioned every `LateUpdate` to the projection of `construct.forward` — it drifts across the screen as the ship turns. The spec is explicit that the resource bars must *not* ride that reticle. `FlyBoostBar` therefore builds its **own** canvas and anchors the bar at screen centre + a fixed offset, exactly as `FlySpeedIndicator` / `FlyHpIndicator` anchor to a screen corner. Do not parent any `FlyBoostBar` rect under `FlyCrosshair`'s hierarchy.
- **Heat bar is deferred — do not build it (spec §7 / §9).** Spec §7 designs both crosshair bars together so the layout is settled, but §9 explicitly defers the **Heat** bar (the right-side bar) to the Laser-weapon roadmap item: *"an always-invisible bar adds nothing and the alpha curve can't be verified without a heat resource."* PR 2 builds **only** the Boost bar (left). `FlyBoostBar` contains no Heat code; no inert/placeholder Heat bar is created. If a future PR wants the right-side slot reserved, that is its call.
- **`.meta` for the two new scripts (project quirk).** `ThrusterBehavior.cs` and `FlyBoostBar.cs` are new scripts; each gets a hand-written canonical `MonoImporter` `.meta` (Task 1 Step 2, Task 6 Step 2) because Unity's auto-stub omits the full block this project standardises on — same quirk handled in the PR 1 plans for `ThrusterMeshAuthor.cs`. `FlyController.cs` and `CubeFlyInputActions.cs` already have `.meta` files and are not re-created. The two new GUIDs (`…4f01`, `…4f02`) must be unique and distinct from each other — each task step says to `grep` for collisions first.
- **Prefab / scene edits via Unity MCP, not hand-written YAML (Tasks 2 & 7).** Adding `ThrusterBehavior` to `PlacedThruster.prefab` and `FlyBoostBar` to `FlyScene.unity`'s `FlyHUD` is done with `manage_prefabs` / `manage_gameobject` / `execute_code` so Unity writes the component block, the fresh `fileID`, and the `m_Component` list entry correctly. The reference YAML in those tasks is the *expected result to verify against*, not text to paste. Both components have only default-valued or auto-wired serialized fields, so adding the component is the whole change — no inspector configuration needed.
- **No unit-test framework.** Verification is the MCP compile/console check after every `.cs` change, plus the Task 8 play-test checklist — there is no automated test to add. This matches the project's existing practice (the PR 1 plans, the desert-level plans).

## Self-review

- **Spec coverage** — every PR 2 item in `thruster_boost_spec.md` §5, §6, §7, §8 is mapped to a task:
  - §5 The Boost resource — the `0–100` meter, max 100, drain 40/s, regen 15/s, overboosted regen 6/s, overboosted entry at 0 / exit at 100 → **Task 4** (the serialized parameter block, the `_boost` / `_overboosted` fields, `TickBoostResource`, `_boost = boostMax` init in `Start`).
  - §6.1 Thruster thrust direction (`−transform.up`, snapped to a construct-local axis) → **Task 1** (`ThrusterBehavior.LocalThrustAxis` / `ResolveThrustAxis`).
  - §6.2 The three-part activation rule, evaluated per `FixedUpdate` → **Task 5** (`EvaluateBoostAxes` — Left Ctrl held, resource available, per-axis sign match).
  - §6.3 The effect — flat per-axis ×1.3 (no stacking) + max-speed lift ×1.3 → **Task 5** (the per-axis `mulX/mulY/mulZ` on `_thrustInput`, `boostAxes` as a flat OR mask, `_boostingThisFrame`).
  - §6.4 `FlyController` integration + post-boost over-cap decay → **Task 4** (collect `ThrusterBehavior`s in `BuildConstruct`; the drain/regen tick) and **Task 5** (the boosted speed-ceiling clamp + the `Mathf.MoveTowards` over-cap decay at `overCapDecaySpeed`). `BoostFraction` / `IsOverboosted` read-only HUD accessors → **Task 4**.
  - §7 HUD — the Boost bar left of the crosshair, fill = `boost/100`, `alpha = 1 − boost/100`, screen-fixed (not reticle-parented), `"Overboosted!"` flash 3× → **Task 6** (`FlyBoostBar`) and **Task 7** (added to `FlyHUD` in `FlyScene`). The Heat bar is explicitly **not built** (§7 / §9 deferral) — stated in Starting state, Notes, and here.
  - §8 PR 2 file list — **new** `Assets/Scripts/Fly/ThrusterBehavior.cs` (Task 1), `Assets/Scripts/Fly/FlyBoostBar.cs` (Task 6); **modified** `Assets/Input/CubeFlyInputActions.cs` (Task 3), `Assets/Scripts/Fly/FlyController.cs` (Tasks 4 & 5), `Assets/Prefabs/PlacedThruster.prefab` (Task 2), `Assets/Scenes/FlyScene.unity` (Task 7). The `Assets/Input/CubeFlyInputActions.inputactions` asset is also touched (Task 3) for parity — it is the wrapper's documentation mirror, not a runtime dependency.
  - The `Boost` input action bound to Left Ctrl (spec §6.2, §8) → **Task 3**.
  - §10 PR 2 test plan → **Task 8** play-test checklist (thruster collected, boosted accel, speed past cap, bar fades in, overboosted flash, no-match drains nothing, per-axis correctness, over-cap decay, cap reverts).
  - Out of scope and stated so: §1–§4 (the PR 1 placeable — merged to `main`) and the §7 / §9 Heat bar (deferred to the Laser-weapon roadmap item).
- **Placeholder scan** — no `TODO`, no "similar to…", no "etc." stand-ins. Every code step gives complete content: Task 1 the full `ThrusterBehavior.cs` and its complete `.meta`; Task 3 exact before/after for the constructor `boost` action, the whole `FlyActions` struct, and the literal `.inputactions` JSON fragments; Task 4 exact before/after for the serialized block, the `_spawnedThrusters` list, the state fields + `BoostFraction` / `IsOverboosted`, the `Start` init, the `BuildConstruct` collection block, the `Update` sample, the `FixedUpdate` provisional block, and the full `TickBoostResource` method; Task 5 exact before/after for the `FixedUpdate` activation + per-axis multiply block and the whole speed-clamp block, plus the full `EvaluateBoostAxes` method; Task 6 the full `FlyBoostBar.cs` and its complete `.meta`. Prefab/scene tasks (2, 7) give the exact component to add, an `execute_code` snippet, and the expected-result YAML to verify against — the only un-fillable values are the `fileID`s Unity emits, each with an explicit read-back verification step.
- **Type / name consistency** — new type `CubeFly.Fly.ThrusterBehavior` with a `Transform Construct { get; set; }` property (mirrors `WeaponBehavior.Construct`) and a `Vector3 LocalThrustAxis { get; }` property; private helper `ResolveThrustAxis()`. New type `CubeFly.Fly.FlyBoostBar` (mirrors `FlySpeedIndicator` / `FlyHpIndicator`). `FlyController` gains: serialized fields `boostMax`, `boostDrainPerSecond`, `boostRegenPerSecond`, `boostRegenOverboostedPerSecond`, `boostThrustMultiplier`, `boostMaxSpeedMultiplier`, `overCapDecaySpeed`; the `readonly List<ThrusterBehavior> _spawnedThrusters` (paralleling `_spawnedWeapons`); private state `_boost`, `_overboosted`, `_boostingThisFrame`, `_boostHeld`; public read-only `BoostFraction` (float 0–1) and `IsOverboosted` (bool); private methods `TickBoostResource()` and `EvaluateBoostAxes()`. The HUD reads exactly `flyController.BoostFraction` and `flyController.IsOverboosted` — the same names `FlyBoostBar` consumes; no drift. `overCapDecaySpeed` is the single serialized name used in the field declaration (Task 4) and its only consumer (Task 5's `Mathf.MoveTowards`). `CubeFlyInputActions.FlyActions` gains an `InputAction Boost { get; }` property and a ninth constructor parameter `boost`; `FlyController` reads `_input.Fly.Boost.IsPressed()`. `ShapeCategory` / `coupledMaterial` and all PR 1 surface are untouched. `_boostingThisFrame` is the one name that spans Tasks 4 and 5 — declared and provisionally set in Task 4, its evaluation replaced (not renamed) in Task 5, and read by both `TickBoostResource` and the speed clamp; consistent throughout.
