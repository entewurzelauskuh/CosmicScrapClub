# Registry Validation Tooling — Design Spec

**Status:** Approved design, ready for implementation planning
**Date:** 2026-05-19
**Branch:** `feat/asset-validation-tooling` off `main`

## Overview

The audit (`CODEBASE_REVIEW_AUDIT.txt`, F10) called for an editor validation
tool plus EditMode tests for the construct registry. Tests are deferred along
with the rest of F5 (asmdef split); this spec covers just the **editor tool**.

The tool runs a single bundle of checks over `ShapeRegistry`,
`MaterialRegistry`, every `ShapeDefinition`, every shape's spawn prefab, and
every coupled / armour `MaterialDefinition`, and reports findings to the Unity
console with an end-of-run summary dialog. It is on-demand (`Tools/CubeFly`
menu item), not always-on.

A missing `CubeStats`, collider, layer, or material assignment today silently
breaks placement, mass, save/load, combat, or preview at runtime. The tool
catches those before play.

## Background — current systems

`ShapeRegistry` (single asset, `Assets/Shapes/ShapeRegistry.asset`) holds the
ordered `ShapeDefinition[] shapes`. Each `ShapeDefinition` declares
`displayName`, `prefab`, `category` (Armour / Weapon / Utility),
`coupledMaterial` (used by non-armour shapes), and six face-validity flags.

`MaterialRegistry` (`Assets/Materials/Defs/MaterialRegistry.asset`) holds the
ordered `MaterialDefinition[] materials` — the **armour-only** pool
(`MaterialA/B/C/D`). Weapon and Utility shapes do not draw from this pool;
they reference their own `MaterialDefinition` via
`ShapeDefinition.coupledMaterial` (`PyramidWeaponMatDef`,
`CylinderWeaponMatDef`, `ThrusterMatDef` today).

Every placed shape prefab is expected to carry `CubeStats`, a `Collider`, a
`Renderer`, `PlacedCubeData`, and be on the `PlacedCube` layer (the layer
`BuildManager` raycasts against for placement / delete hover).

The project has one Editor folder today: `Assets/Scripts/Desert/Editor/`. The
existing editor-tool voice is minimal: inspector button +
`Debug.Log`-style console output (see `DuneGroundGeneratorEditor`).

## Approach — menu item + console output

A single static class with a `[MenuItem]` entry point. Discovery uses
`AssetDatabase.FindAssets("t:<Type>")` so paths aren't hard-coded; finding
two of either registry is itself a finding. Each check produces one log
line tagged `[RegistryValidator]` with the offending asset as the
`UnityEngine.Object` context (so the Console "ping" arrow works).

Severity follows Unity's convention: **error** for null / missing required
things, **warning** for soft smells (all-false face flags, armour shape with
a populated `coupledMaterial`, negative stats). The end-of-run summary line
prints counts; an `EditorUtility.DisplayDialog` surfaces the outcome.

Rejected alternatives: an `EditorWindow` with a clickable findings panel
(better UX but YAGNI for the current finding volume) and an
`AssetPostprocessor` that auto-validates on save (noisy; the audit asks for
a *tool*, not always-on). Either is a trivial follow-up if findings volume
later justifies it.

## Design

### File and location

- New folder: `Assets/Scripts/Editor/`. Unity excludes any folder named
  `Editor` from runtime builds, so editor-only code lives here even though
  no asmdef split is in place.
- New file: `Assets/Scripts/Editor/RegistryValidator.cs`. Namespace
  `CubeFly.EditorTools` — *not* `CubeFly.Editor`, because that name
  would shadow `UnityEditor.Editor` for any other file in the
  `CubeFly.*` hierarchy (`DuneGroundGeneratorEditor.cs` refers to
  `Editor` unqualified).
- Menu path: `Tools/CubeFly/Validate Registries`.
- Single internal static class; one public entry point invoked by the menu
  item; small private helpers per check group.

### Discovery

Use `AssetDatabase.FindAssets("t:ShapeRegistry")` and
`AssetDatabase.FindAssets("t:MaterialRegistry")`, then
`AssetDatabase.GUIDToAssetPath` + `LoadAssetAtPath`. Expect exactly one of
each. Zero or more than one is itself a finding (error: "expected exactly
one `ShapeRegistry` asset, found N").

### Checks — ShapeRegistry / ShapeDefinition

For the registry asset:

- `shapes` array non-null and non-empty (error if empty).
- No null entries in `shapes` (error: "null at index i").
- `displayName` non-empty (error).
- `displayName` unique across shapes (error — duplicates break
  `FindIndexByName`-driven save/load).

For each `ShapeDefinition`:

- `prefab` non-null (error). All subsequent prefab checks for this shape
  are skipped when this fails.
- Prefab has `CubeStats` (error).
- Prefab has at least one `Collider` (error — placement raycasts need it).
- Prefab has at least one `Renderer` (error — `MaterialDefinition.ApplyTo`
  walks renderers).
- Prefab has `PlacedCubeData` (error — `CubeDamage` reads `.cell` from it
  to keep `GameData` in sync on death).
- Prefab's `gameObject.layer` is `PlacedCube` (error if not, or if the
  `PlacedCube` layer is undefined in Project Settings).
- Non-armour shapes (`category != Armour`) have `coupledMaterial` non-null
  (error).
- Armour shapes have `coupledMaterial == null` (warning — the field is
  ignored by `ResolveMaterial`; a non-null value is a confusion smell).
- Not all six face flags are `false` (warning — "shape has no valid
  attachment face; intentionally unplaceable?").

### Checks — required layers

The `PlacedCube`, `AlphaCube`, and `PreviewCube` layers must be defined in
Project Settings → Tags and Layers (error per missing layer). The shape
prefab layer check above relies on `PlacedCube` existing.

### Checks — MaterialRegistry / armour pool

For the registry asset:

- `materials` array non-null and non-empty (error).
- No null entries (error: "null at index i").
- `displayName` non-empty (error).
- `displayName` unique across materials (error — duplicates break
  `FindIndexByName`).

For each `MaterialDefinition` in the armour pool:

- `material` (URP Material asset) non-null (error).
- `healthPoints`, `armourValue`, `mass` are finite (`!float.IsNaN`,
  `!float.IsInfinity`) and non-negative (warning if negative; error if
  not-finite).

### Checks — coupled materials

For each non-armour shape's `coupledMaterial`:

- All the per-`MaterialDefinition` checks above (material non-null, stats
  finite + non-negative).
- The coupled material is **not** also present in
  `MaterialRegistry.materials` (warning — coupled and pooled material spaces
  are deliberately separate; a leak suggests a configuration mix-up).

### Severity rules — summary

| Class | Severity | Why |
|---|---|---|
| Null / empty required reference | Error | Will fault at runtime |
| Missing required prefab component | Error | Will fault at runtime |
| Duplicate displayName | Error | Breaks `FindIndexByName` / saves |
| Missing required layer | Error | `BuildManager.ResolveLayerMasks` falls back, but the gameplay layer is wrong |
| Non-finite stat | Error | Will propagate to runtime math |
| All face flags false | Warning | Possibly intentional unplaceable |
| Armour shape with `coupledMaterial` | Warning | Ignored field, confusing |
| Coupled material in armour pool | Warning | Probable configuration mix-up |
| Negative stat | Warning | Possibly intentional but unusual |

### Output

- Each finding: one `Debug.LogError` / `LogWarning` call, message starting
  `[RegistryValidator] `, the offending `UnityEngine.Object` passed as the
  log context so Console pings the right asset.
- Final line: a single `Debug.Log` of the form
  `[RegistryValidator] Validation complete: N error(s), M warning(s), `
  `S shape(s), A armour material(s), C coupled material(s) checked.`
- `EditorUtility.DisplayDialog`: title "Registry Validation"; body either
  "All checks passed." or "Found N error(s) and M warning(s). See Console
  for details."; single OK button.

## Files touched

- Create: `Assets/Scripts/Editor/RegistryValidator.cs`.

No scene, prefab, or runtime-script changes.

## Out of scope

- **EditMode / PlayMode tests** of the validator or the registries —
  deferred with F5 (asmdef split + test infrastructure).
- **Alpha cube prefab validation** — the alpha lives outside the registry,
  referenced as a serialized field on `BuildManager` / `FlyController` in
  scenes. Reachable but more bookkeeping; defer until a missing-alpha bug
  ever surfaces.
- **Scene / bootstrap / build-settings / save-version validation** —
  arch-rec 3's broader scope; roadmapped under "Architecture &
  infrastructure" for the future construct-validation pipeline.
- **Orphan prefab detection** — `PlacedCubeB/C/D.prefab`,
  `PlacedPrism.prefab` exist in `Assets/Prefabs` but aren't referenced by
  any shape today. Project-hygiene concern, not registry validation.
- **Automatic / reactive runs** — `AssetPostprocessor`, play-mode hooks,
  CI. The current tool is on-demand only.

## Verification

- Manual: open the project, run `Tools/CubeFly/Validate Registries`, watch
  the Console.
- On the current `main`-equivalent state, the tool should report "All
  checks passed" (the registries are presumed clean; if not, the findings
  are themselves the value of shipping this).
- Hand-inject failures to confirm each branch fires once:
  - Temporarily clear a shape's `displayName` → expect one error.
  - Temporarily remove `CubeStats` from a placed prefab → expect one
    error.
  - Temporarily zero all six face flags on a shape → expect one warning.
  - Temporarily duplicate a `displayName` across two shapes → expect one
    error.
  Restore each after confirming.
- Compile clean, no console errors outside the validator's own
  `[RegistryValidator]` lines.
