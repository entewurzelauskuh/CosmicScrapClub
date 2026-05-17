# Thruster PR 1a — CategoryFlyout Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the Weapons-button + weapons-flyout machinery out of `BuildToolbarController` into a new reusable, category-agnostic `CategoryFlyout` class — a **pure refactor with zero behavior change**, so PR 1b can add a second "Utilities" toolbar category by data alone.

**Architecture:** `CategoryFlyout` is a plain (non-`MonoBehaviour`) C# class in namespace `CubeFly.Build`. It owns one toolbar button + its corner swatch, one flyout panel + entry buttons, the peek/pin/Esc/M state, and the last-armed-shape memory for a single non-armour category. It reaches `BuildToolbarController` only through constructor-injected dependencies (the `BuildManager`, the owning `MonoBehaviour` for coroutines, the category's shape indices, layout values, two swatch-builder delegates, a `closeOthers` action, and an `anyOtherFlyoutPinned` predicate). `BuildToolbarController` keeps the armour buttons, the material flyout, digit-key shortcuts, the Delete button and stat labels; it holds a `List<CategoryFlyout>`, builds one per non-armour category after the armour buttons (before Delete), and routes M / Esc / shape-change / tool-change to them.

**Tech Stack:** Unity 6.3 LTS (6000.3.x) / URP, MonoBehaviour-only C#, pure C# (no DOTS), legacy `UnityEngine.UI`. Compilation + console checks via Unity MCP; editor regression check by hand. No unit-test framework.

---

## Starting state

- Branch: `feat/thruster-cube`. PR 1a is a **pure refactor** — no thruster shape, mesh, prefab, material, asset, or `ShapeCategory` enum change. Those all belong to **PR 1b (thruster placeable)**, a separate later plan; the **PR 2 (boost mechanic)** fly-side work is later still.
- `Assets/Scripts/Build/BuildToolbarController.cs` (~1045 lines) contains, inline, both the armour-shape machinery and the Weapons-button machinery. The Weapons machinery to extract:
  - **Fields:** `_weaponsButton`, `_weaponsBackground`, `_weaponsSwatch`, `_weaponShapeIndices`, `_weaponsFlyout`, `_weaponsFlyoutGroup`, `_weaponsFlyoutButtons`, `_weaponsFlyoutBackgrounds`, `_weaponsFlyoutPinned`, `_weaponsPeekRoutine`, `_lastArmedWeaponIndex`.
  - **Methods:** `OnWeaponsButtonClicked`, `AddWeaponsPointerHandlers`, `OnWeaponsButtonHoverEnter`, `OnWeaponsButtonHoverExit`, `WeaponsPeekAfterDelay`, `BuildWeaponsFlyout`, `OnWeaponsFlyoutEntryClicked`, `OpenWeaponsFlyout`, `HideWeaponsFlyout`, `IsPointerOverWeaponsFlyout`, `RefreshWeaponsFlyoutEntryHighlights`, `RefreshWeaponsButtonSwatch`.
  - **Call sites** inside `BuildToolbar()`, `Update()` (the M-key block + the Esc block), `OnCurrentShapeChanged`, `OnCurrentToolChanged`, `PeekAfterDelay`, and `RefreshAllSwatches`.
- After PR 1a the on-screen toolbar is unchanged: `[Cube] [Slope] [Weapons ▸] [Delete]`. The Weapons button + its flyout must behave **exactly** as today.
- `thruster_boost_spec.md` §4 / §4.1 is the design rationale for this refactor; it is the riskiest part of PR 1, and the payoff is that the future Utilities flyout (PR 1b) and Power blocks become data-only additions.

## Conventions for every task

- **Compile/console check:** after creating or editing any `.cs` file, refresh Unity and wait for the domain reload to finish, then read the console filtered to errors. Concretely: `mcp__UnityMCP__refresh_unity` with `compile="request"`, `mode="force"`, `scope="all"`, `wait_for_ready=true`; poll the `mcpforunity://editor/state` resource until `is_compiling=false` and `ready_for_tools=true`; then `mcp__UnityMCP__read_console` with `action="get"`, `types=["error"]`, `count=50`. **Zero compile errors before proceeding.** (MCP `Client handler exited` lines are infrastructure noise, not errors.)
- **No behavior change.** This is a refactor. If any step would change observable behavior, stop — it is a mistake in this plan, not an intended change.
- **Commit** at the end of each task with the exact `git add` paths shown, on branch `feat/thruster-cube`. Do not amend; each task is a fresh commit.
- **`.meta` for new scripts:** Unity's auto-generated `.meta` stub for a `.cs` file is incomplete in this project. New scripts get a hand-written canonical `.meta` containing a full `MonoImporter` block (see Task 1, Step 2).

---

## Task 1: Create `CategoryFlyout.cs` + its canonical `.meta`

**Files:**
- Create: `Assets/Scripts/Build/CategoryFlyout.cs`
- Create: `Assets/Scripts/Build/CategoryFlyout.cs.meta`

This task adds the new class only; it is wired into `BuildToolbarController` in Task 2. It compiles cleanly on its own (it is referenced by nothing yet).

- [ ] **Step 1: Create `CategoryFlyout.cs`**

Create `Assets/Scripts/Build/CategoryFlyout.cs` with this exact content:

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using CubeFly.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CubeFly.Build
{
    // One non-armour toolbar category collapsed behind a single button +
    // a dedicated flyout. Extracted verbatim (zero behaviour change) from
    // the Weapons-button machinery that used to live inline in
    // BuildToolbarController; instantiated once per non-armour category
    // (Weapons today; Utilities lands in a later PR as a data-only add).
    //
    // A CategoryFlyout owns: the toolbar button + its corner swatch, the
    // flyout panel + entry buttons + backgrounds, the peek-on-hover /
    // click-to-pin / Esc-close state, and the last-armed-shape memory for
    // that category. It is a plain C# object, NOT a MonoBehaviour — it
    // borrows the owning BuildToolbarController's coroutine runner for the
    // hover-peek delay and reaches the rest of the toolbar only through
    // the constructor-injected dependencies below.
    public class CategoryFlyout
    {
        // ---- Injected dependencies ----
        readonly BuildManager _buildManager;
        readonly MonoBehaviour _owner;            // coroutine host (the BuildToolbarController)
        readonly int[] _shapeIndices;             // ShapeRegistry indices of every shape in this category
        readonly string _buttonLabel;

        // Layout values — passed in so every category shares the
        // controller's serialized toolbar metrics.
        readonly Vector2 _buttonSize;
        readonly int _fontSize;
        readonly float _bottomMargin;
        readonly Vector2 _flyoutEntrySize;
        readonly float _flyoutEntrySpacing;
        readonly float _flyoutBottomGap;
        readonly float _peekAlpha;
        readonly float _hoverPeekDelay;

        // Swatch builders — reuse the controller's existing
        // BuildCornerSwatch / BuildEntrySwatch so swatch styling stays
        // in one place.
        readonly Func<RectTransform, Image> _buildCornerSwatch;
        readonly Func<RectTransform, Color, Image> _buildEntrySwatch;

        // Mutual exclusion: invoked right before this flyout opens so the
        // controller can close the material flyout and every other
        // category flyout. The peek logic consults the predicate to
        // suppress peek-opening while another flyout is pinned.
        readonly Action _closeOthers;
        readonly Func<bool> _anyOtherFlyoutPinned;

        // ---- Owned UI ----
        Button _button;
        Image _background;
        Image _swatch;
        GameObject _flyout;
        CanvasGroup _flyoutGroup;
        Button[] _flyoutButtons;
        Image[] _flyoutBackgrounds;
        bool _flyoutPinned;
        Coroutine _peekRoutine;

        // Last-armed shape in this category — drives the toolbar button's
        // corner swatch when a shape from another category is active.
        // Defaults to the category's first shape.
        int _lastArmedShapeIndex = -1;

        static readonly Color SelectedTypeColor = new Color(0.25f, 0.45f, 0.85f, 0.95f);
        static readonly Color FlyoutEntryIdle   = new Color(0.18f, 0.18f, 0.22f, 0.95f);
        static readonly Color FlyoutEntryActive = new Color(0.35f, 0.55f, 0.95f, 0.95f);

        public CategoryFlyout(
            BuildManager buildManager,
            MonoBehaviour owner,
            int[] shapeIndices,
            string buttonLabel,
            Vector2 buttonSize,
            int fontSize,
            float bottomMargin,
            Vector2 flyoutEntrySize,
            float flyoutEntrySpacing,
            float flyoutBottomGap,
            float peekAlpha,
            float hoverPeekDelay,
            Func<RectTransform, Image> buildCornerSwatch,
            Func<RectTransform, Color, Image> buildEntrySwatch,
            Action closeOthers,
            Func<bool> anyOtherFlyoutPinned)
        {
            _buildManager = buildManager;
            _owner = owner;
            _shapeIndices = shapeIndices ?? Array.Empty<int>();
            _buttonLabel = buttonLabel;
            _buttonSize = buttonSize;
            _fontSize = fontSize;
            _bottomMargin = bottomMargin;
            _flyoutEntrySize = flyoutEntrySize;
            _flyoutEntrySpacing = flyoutEntrySpacing;
            _flyoutBottomGap = flyoutBottomGap;
            _peekAlpha = peekAlpha;
            _hoverPeekDelay = hoverPeekDelay;
            _buildCornerSwatch = buildCornerSwatch;
            _buildEntrySwatch = buildEntrySwatch;
            _closeOthers = closeOthers;
            _anyOtherFlyoutPinned = anyOtherFlyoutPinned;
            if (_shapeIndices.Length > 0) _lastArmedShapeIndex = _shapeIndices[0];
        }

        // ---- Public surface ----

        // True while the flyout GameObject is shown (peeking or pinned).
        public bool IsOpen => _flyout != null && _flyout.activeSelf;

        // True while the flyout is shown AND was opened by a click
        // (pinned), as opposed to a transient hover-peek.
        public bool IsPinned => IsOpen && _flyoutPinned;

        // The category's last-armed ShapeRegistry index (the first shape
        // in the category until one is armed).
        public int LastArmedShapeIndex => _lastArmedShapeIndex;

        // True when `shapeIndex` belongs to this category.
        public bool ContainsShape(int shapeIndex)
        {
            for (int i = 0; i < _shapeIndices.Length; i++)
                if (_shapeIndices[i] == shapeIndex) return true;
            return false;
        }

        // Record an arm of one of this category's shapes so the toolbar
        // button's corner swatch keeps that colour when a shape from
        // another category becomes active. No-op for a foreign index.
        public void NoteArmedShape(int shapeIndex)
        {
            if (ContainsShape(shapeIndex)) _lastArmedShapeIndex = shapeIndex;
        }

        // Build the toolbar button at the given anchored-X position
        // (bottom-anchored, like the armour buttons). Mirrors what the
        // controller used to do inline for the Weapons button.
        public void BuildButton(RectTransform canvas, float anchoredX)
        {
            (Button btn, Text _) = UIStyle.BuildLabeledButton(canvas, _buttonLabel, _buttonSize, _fontSize);
            RectTransform rt = (RectTransform)btn.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(anchoredX, _bottomMargin);

            btn.onClick.AddListener(OnButtonClicked);
            AddPointerHandlers(btn.gameObject);

            _swatch = _buildCornerSwatch(rt);
            _button = btn;
            _background = btn.GetComponent<Image>();
        }

        // Build the (initially hidden) flyout panel under the canvas, one
        // entry per shape in the category. Call after BuildButton.
        public void BuildFlyout(RectTransform canvas)
        {
            int count = _shapeIndices.Length;
            _flyoutButtons = new Button[count];
            _flyoutBackgrounds = new Image[count];

            _flyout = new GameObject(_buttonLabel + "Flyout",
                typeof(RectTransform), typeof(CanvasGroup));
            RectTransform frt = (RectTransform)_flyout.transform;
            frt.SetParent(canvas, false);
            frt.anchorMin = frt.anchorMax = frt.pivot = new Vector2(0.5f, 0f);
            frt.sizeDelta = new Vector2(
                _flyoutEntrySize.x,
                count * _flyoutEntrySize.y + Mathf.Max(0, count - 1) * _flyoutEntrySpacing);

            _flyoutGroup = _flyout.GetComponent<CanvasGroup>();
            _flyoutGroup.interactable = true;
            _flyoutGroup.blocksRaycasts = true;

            for (int e = 0; e < count; e++)
            {
                int shapeIndex = _shapeIndices[e];
                ShapeDefinition shape = _buildManager.Shapes.Get(shapeIndex);
                MaterialDefinition wmat = shape != null ? shape.weaponMaterial : null;
                string title = shape != null ? shape.displayName : $"Shape #{shapeIndex}";
                string statLine = wmat != null
                    ? $"HP {wmat.healthPoints:F0}  ·  AV {wmat.armourValue:F0}  ·  M {wmat.mass:F1}"
                    : "—";

                (Button btn, Text label) = UIStyle.BuildLabeledButton(
                    frt,
                    $"{title}\n<size={Mathf.Max(10, _fontSize - 8)}>{statLine}</size>",
                    _flyoutEntrySize, _fontSize);
                label.supportRichText = true;
                label.alignment = TextAnchor.MiddleLeft;
                RectTransform brt = (RectTransform)btn.transform;
                brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0f);
                float y = e * (_flyoutEntrySize.y + _flyoutEntrySpacing);
                brt.anchoredPosition = new Vector2(0f, y);

                _buildEntrySwatch(brt, wmat != null ? wmat.SwatchColor : Color.gray);

                int captured = shapeIndex;
                btn.onClick.AddListener(() => OnFlyoutEntryClicked(captured));
                _flyoutButtons[e] = btn;
                _flyoutBackgrounds[e] = btn.GetComponent<Image>();
            }

            _flyout.SetActive(false);
        }

        // M-key behaviour: close the flyout if it is open and pinned,
        // otherwise open it pinned.
        public void Toggle()
        {
            if (IsOpen && _flyoutPinned) Hide();
            else Open(pin: true);
        }

        // Open the flyout. `pin == true` → fully opaque + interactive
        // (click / right-click / M); `pin == false` → translucent
        // hover-peek that is non-interactive.
        public void Open(bool pin)
        {
            if (_flyout == null || _button == null) return;

            // Opening one flyout closes the material flyout and every
            // other category flyout so they never visually overlap.
            _closeOthers?.Invoke();

            RectTransform btnRT = (RectTransform)_button.transform;
            RectTransform frt = (RectTransform)_flyout.transform;
            frt.anchoredPosition = new Vector2(
                btnRT.anchoredPosition.x,
                _bottomMargin + _buttonSize.y / 2f + _flyoutBottomGap);

            _flyout.SetActive(true);
            _flyoutGroup.alpha = pin ? 1f : _peekAlpha;
            _flyoutGroup.blocksRaycasts = pin;
            _flyoutPinned = pin;
            RefreshFlyoutHighlights();
        }

        // Hide the flyout and drop its pinned state.
        public void Hide()
        {
            if (_flyout == null || !_flyout.activeSelf) return;
            _flyout.SetActive(false);
            _flyoutPinned = false;
        }

        // Toolbar-button highlight: the category button gets the same
        // selected colour as an armour button, lit whenever ANY shape in
        // this category is the active shape.
        public void RefreshButtonHighlight()
        {
            if (_background == null) return;
            _background.color = IsActiveCategory() ? SelectedTypeColor : UIStyle.BackgroundIdle;
        }

        // Corner-swatch colour: the armed shape's coupled material when a
        // shape in this category is active, otherwise the last-armed
        // shape's. Falls back to the first shape on cold start.
        public void RefreshSwatch()
        {
            if (_swatch == null) return;
            if (_buildManager == null || _buildManager.Shapes == null) return;

            int activeIdx = _buildManager.CurrentShapeIndex;
            int swatchShape = ContainsShape(activeIdx) ? activeIdx : _lastArmedShapeIndex;

            ShapeDefinition shape = _buildManager.Shapes.Get(swatchShape);
            MaterialDefinition wmat = shape != null ? shape.weaponMaterial : null;
            _swatch.color = wmat != null ? wmat.SwatchColor : Color.gray;
        }

        // Flyout entry highlight: the entry for the active shape (when
        // that shape belongs to this category) gets the active colour.
        public void RefreshFlyoutHighlights()
        {
            if (_flyoutBackgrounds == null) return;
            int activeShape = _buildManager.CurrentShapeIndex;
            bool activeInCategory = ContainsShape(activeShape);
            for (int e = 0; e < _flyoutBackgrounds.Length; e++)
            {
                if (_flyoutBackgrounds[e] == null) continue;
                bool isActive = activeInCategory && _shapeIndices[e] == activeShape;
                _flyoutBackgrounds[e].color = isActive ? FlyoutEntryActive : FlyoutEntryIdle;
            }
        }

        // ---- Internals ----

        // True when the active shape belongs to this category.
        bool IsActiveCategory()
        {
            if (_buildManager == null || _buildManager.Shapes == null) return false;
            if (_buildManager.CurrentTool != BuildTool.Place) return false;
            return ContainsShape(_buildManager.CurrentShapeIndex);
        }

        // Toggle the flyout. Unlike the per-shape armour buttons, the
        // category button doesn't double as a "switch shape" shortcut —
        // picking a shape happens inside the flyout so the player can see
        // what's available.
        void OnButtonClicked() => Toggle();

        // Pointer enter / exit / right-click on the toolbar button, wired
        // via EventTrigger to avoid hand-rolling raycasts.
        void AddPointerHandlers(GameObject buttonObject)
        {
            EventTrigger trigger = buttonObject.AddComponent<EventTrigger>();

            EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => OnHoverEnter());
            trigger.triggers.Add(enter);

            EventTrigger.Entry exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => OnHoverExit());
            trigger.triggers.Add(exit);

            EventTrigger.Entry click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            click.callback.AddListener(data =>
            {
                PointerEventData ped = data as PointerEventData;
                if (ped == null) return;
                if (ped.button == PointerEventData.InputButton.Right)
                    Open(pin: true);
            });
            trigger.triggers.Add(click);
        }

        void OnHoverEnter()
        {
            if (_peekRoutine != null) _owner.StopCoroutine(_peekRoutine);
            _peekRoutine = _owner.StartCoroutine(PeekAfterDelay());
        }

        void OnHoverExit()
        {
            if (_peekRoutine != null)
            {
                _owner.StopCoroutine(_peekRoutine);
                _peekRoutine = null;
            }
            // A peek (non-pinned) flyout closes on exit; a pinned one
            // stays until Esc / M / an entry click / shape or tool
            // change. Don't close if the cursor moved INTO the flyout.
            if (IsOpen && !_flyoutPinned)
            {
                if (!IsPointerOverFlyout()) Hide();
            }
        }

        IEnumerator PeekAfterDelay()
        {
            yield return new WaitForSeconds(_hoverPeekDelay);
            // Don't peek-open if THIS flyout is already pinned, or if any
            // OTHER flyout is pinned — peek-opening would call Open with
            // pin: false and silently unpin the user's deliberate
            // selection just because they hovered a button.
            if (IsOpen && _flyoutPinned) yield break;
            if (_anyOtherFlyoutPinned != null && _anyOtherFlyoutPinned()) yield break;
            Open(pin: false);
            _peekRoutine = null;
        }

        void OnFlyoutEntryClicked(int shapeIndex)
        {
            _buildManager.SetCurrentShape(shapeIndex);
            _lastArmedShapeIndex = shapeIndex;
            Hide();
        }

        bool IsPointerOverFlyout()
        {
            if (EventSystem.current == null || _flyout == null) return false;
            PointerEventData ped = new PointerEventData(EventSystem.current)
            {
                position = Mouse.current != null ? (Vector2)Mouse.current.position.ReadValue() : Vector2.zero
            };
            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(ped, results);
            for (int i = 0; i < results.Count; i++)
            {
                Transform t = results[i].gameObject.transform;
                while (t != null)
                {
                    if (t.gameObject == _flyout) return true;
                    t = t.parent;
                }
            }
            return false;
        }
    }
}
```

Notes baked into the code:
- The class is **not** a `MonoBehaviour` — it borrows `_owner.StartCoroutine` / `StopCoroutine` for the hover-peek delay, exactly as the spec requires.
- `_closeOthers` is invoked from `Open(...)` (both the pin and peek paths) — the controller wires it to close the material flyout and every *other* category flyout. The old inline code closed only the material flyout from `OpenWeaponsFlyout`; routing through `closeOthers` generalises that to N categories with no behaviour change for the single-category (Weapons-only) case.
- `_anyOtherFlyoutPinned` replaces the old `PeekAfterDelay`'s direct check of `_flyout`/`_flyoutPinned` (material flyout) — the controller's predicate covers the material flyout and every other category flyout.
- Flyout entries still read `shape.weaponMaterial` for their stat line / swatch colour — this is the field's current name; PR 1b renames it to `coupledMaterial` (out of scope here).
- `RefreshFlyoutHighlights` keeps the original guard's intent — an entry is active only when the active shape is in this category. The old code's `buildManager.Shapes.Get(activeShape)?.IsWeapon` check is subsumed by `ContainsShape`, which is true only for this category's shapes (all weapons today).

- [ ] **Step 2: Create the canonical `.meta` for `CategoryFlyout.cs`**

Unity's auto-generated stub `.meta` for a new `.cs` file omits the full `MonoImporter` block this project expects. Create `Assets/Scripts/Build/CategoryFlyout.cs.meta` with this exact content (a fresh unique 32-hex GUID — the one below is unused; if Unity has already generated a `.meta`, overwrite it with this and keep whichever GUID Unity assigned if it already differs and is referenced):

```
fileFormatVersion: 2
guid: c1a7e9f0b2d4456a8c3e1f0d9b8a7c6e
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

This matches the shape of `Assets/Scripts/Build/BuildToolbarController.cs.meta` (a `MonoImporter` with `externalObjects`, `serializedVersion: 2`, empty `defaultReferences`, `executionOrder: 0`, a zero `icon`, and empty `userData` / `assetBundleName` / `assetBundleVariant`).

- [ ] **Step 3: Compile + console check**

Refresh Unity (`refresh_unity`, `compile="request"`, `mode="force"`, `scope="all"`, `wait_for_ready=true`), poll `mcpforunity://editor/state` until `is_compiling=false` and `ready_for_tools=true`, then `read_console(action="get", types=["error"], count=50)`.

Expected: zero errors. `CategoryFlyout` is referenced by nothing yet, so it must compile purely on its own. If the console reports an unused-field warning for the not-yet-used class members, that is acceptable (warnings, not errors) and resolves in Task 2 once the class is constructed.

- [ ] **Step 4: Commit**

```bash
git add "Assets/Scripts/Build/CategoryFlyout.cs" "Assets/Scripts/Build/CategoryFlyout.cs.meta"
git commit -m "Add CategoryFlyout — reusable non-armour toolbar category"
```

---

## Task 2: Refactor `BuildToolbarController.cs` to use `CategoryFlyout`

**Files:**
- Modify: `Assets/Scripts/Build/BuildToolbarController.cs`

Replace every `_weapons*` field and `*Weapons*` method with a single `List<CategoryFlyout>`, build one entry for the Weapons category, and route M / Esc / shape-change / tool-change / swatch-refresh / peek-suppression through the list. The material flyout, armour buttons, digit keys, Delete button and stat labels are untouched except where they referenced the removed `_weapons*` members.

Each step below shows the changed region in full. Apply them in order.

- [ ] **Step 1: Replace the Weapons UI-state field block with the category-flyout list**

Find this block (the field declarations that begin with the `// Weapons UI state` comment):

```csharp
        // Weapons UI state — single toolbar button + dedicated flyout.
        // Built only when the ShapeRegistry contains at least one
        // shape with category == Weapon. The Weapons button replaces
        // the per-shape buttons that armour shapes get.
        Button _weaponsButton;
        Image _weaponsBackground;
        Image _weaponsSwatch;
        int[] _weaponShapeIndices;        // shape indices (into ShapeRegistry) of every weapon
        GameObject _weaponsFlyout;
        CanvasGroup _weaponsFlyoutGroup;
        Button[] _weaponsFlyoutButtons;
        Image[] _weaponsFlyoutBackgrounds;
        bool _weaponsFlyoutPinned;
        Coroutine _weaponsPeekRoutine;
        // Last-armed weapon — drives the Weapons button's swatch when
        // an armour shape is active. Defaults to the first weapon.
        int _lastArmedWeaponIndex = -1;
```

Replace it with:

```csharp
        // Non-armour categories — one CategoryFlyout per category that
        // the ShapeRegistry contains (Weapons today; Utilities lands in
        // a later PR as a data-only addition). Each owns its toolbar
        // button + swatch, its flyout, and its peek/pin/last-armed
        // state. Empty when the registry has no non-armour shapes.
        readonly List<CategoryFlyout> _categoryFlyouts = new List<CategoryFlyout>();
```

- [ ] **Step 2: Rewrite `Update()` — the M-key block and the Esc block**

Find the current `Update()` method (from `void Update()` through its closing brace) and replace it in full with:

```csharp
        void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            // Pause menu owns all keyboard input while open. PauseMenu
            // runs at DefaultExecutionOrder(-1000), so by the time we
            // reach here it has already toggled itself and set
            // EscConsumedThisFrame for any pending ESC. The full
            // keyboard-shortcut listing is below, next to the code
            // that implements it (single source of truth).
            if (PauseMenu.Instance != null && PauseMenu.Instance.IsOpen) return;

            // Keyboard shortcuts:
            //   • Digits 1..9 (no modifier) → arm armour shape by
            //     toolbar slot order (the on-screen order, not the
            //     ShapeRegistry index). Non-armour shapes are reachable
            //     only through their category flyout.
            //   • Shift+Digit1..9 → set the active armour shape's
            //     material by registry index.
            // Letter keys are avoided to keep R/T (rotation) and any
            // future Build-map bindings free of conflicts.
            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;

            if (!shift && _armourShapeIndices != null)
            {
                // Map digit i → _armourShapeIndices[i]. This sidesteps
                // the case where weapons aren't all at the end of the
                // registry: digit keys correspond exactly to the
                // visible armour buttons, in the same left-to-right
                // order.
                int max = Mathf.Min(_armourShapeIndices.Length, DigitKeys.Length);
                for (int i = 0; i < max; i++)
                {
                    if (kb[DigitKeys[i]].wasPressedThisFrame)
                    {
                        buildManager.SetCurrentShape(_armourShapeIndices[i]);
                        break;
                    }
                }
            }
            else if (shift)
            {
                // Shift+digit only meaningful while an armour shape is
                // active — non-armour shapes have no material choice.
                ShapeDefinition activeShape = buildManager.Shapes != null
                    ? buildManager.Shapes.Get(buildManager.CurrentShapeIndex)
                    : null;
                bool weaponActive = activeShape != null && activeShape.IsWeapon;
                if (!weaponActive)
                {
                    int matCount = buildManager.Materials != null ? buildManager.Materials.Count : 0;
                    int matMax = Mathf.Min(DigitKeys.Length, matCount);
                    for (int i = 0; i < matMax; i++)
                    {
                        if (kb[DigitKeys[i]].wasPressedThisFrame)
                        {
                            buildManager.SetCurrentMaterial(i);
                            if (_flyout != null && _flyout.activeSelf) RefreshFlyoutEntryHighlights();
                            break;
                        }
                    }
                }
            }

            if (kb.mKey.wasPressedThisFrame)
            {
                // M toggles the flyout of the active shape's category:
                // the matching CategoryFlyout for a non-armour shape,
                // the per-shape material flyout for an armour shape.
                CategoryFlyout activeCategory =
                    FindCategoryFlyout(buildManager.CurrentShapeIndex);
                if (activeCategory != null)
                {
                    activeCategory.Toggle();
                }
                else
                {
                    if (_flyout != null && _flyout.activeSelf && _flyoutOwnerShape == buildManager.CurrentShapeIndex && _flyoutPinned)
                        HideFlyout();
                    else
                        OpenFlyoutForShape(buildManager.CurrentShapeIndex, pin: true);
                }
            }

            // Skip ESC if PauseMenu just consumed it this frame (i.e.
            // it opened or closed the pause overlay). Without this
            // guard, an ESC press that opens the pause would ALSO
            // close any flyout in the same frame, leaving the player
            // with no flyout to return to after un-pausing.
            if (kb.escapeKey.wasPressedThisFrame
                && (PauseMenu.Instance == null || !PauseMenu.Instance.EscConsumedThisFrame))
            {
                if (_flyout != null && _flyout.activeSelf) HideFlyout();
                for (int i = 0; i < _categoryFlyouts.Count; i++) _categoryFlyouts[i].Hide();
            }
        }
```

The M-key path now finds the active shape's category via `FindCategoryFlyout` (added in Step 8) and calls `Toggle()` — identical behaviour to the old `_weaponsFlyout`/`_weaponsFlyoutPinned` toggle, but for whichever category owns the active shape. The Esc path hides the material flyout, then every category flyout (today: just Weapons).

- [ ] **Step 3: Rewrite the toolbar-build sequence in `BuildToolbar()`**

In `BuildToolbar()`, find the block from the `// ---- Shape buttons + Weapons + Delete ----` comment down through the `if (hasWeapons) BuildWeaponsFlyout(root);` line **and the method's closing `}`**, and replace that whole region in full with the block below (which ends with that same closing brace):

```csharp
            // ---- Shape buttons + category flyouts + Delete ----
            //
            // Toolbar slots, left to right:
            //   • One button per ARMOUR shape (each with a material
            //     flyout via right-click / re-click / hover-peek).
            //   • One CategoryFlyout button per non-armour category in
            //     the registry (Weapons today; Utilities later), each
            //     collapsing all that category's shapes behind a
            //     dedicated flyout.
            //   • Delete tool button.
            //
            // _shapeButtons/_shapeSwatches/_shapeBackgrounds remain
            // indexed by ShapeRegistry index for simplicity — entries
            // for non-armour shapes are left null.
            ShapeRegistry shapes = buildManager.Shapes;
            int totalShapes = shapes != null ? shapes.Count : 0;
            _shapeButtons = new Button[totalShapes];
            _shapeBackgrounds = new Image[totalShapes];
            _shapeSwatches = new Image[totalShapes];

            // Partition the registry into armour shapes and the
            // non-armour categories. Each non-armour category keeps its
            // shapes in registry order; categories themselves are
            // ordered by first appearance in the registry — so Weapons
            // (and, later, Utilities) slot in deterministically.
            List<int> armourIndices = new List<int>();
            List<ShapeCategory> categoryOrder = new List<ShapeCategory>();
            Dictionary<ShapeCategory, List<int>> categoryIndices =
                new Dictionary<ShapeCategory, List<int>>();
            for (int i = 0; i < totalShapes; i++)
            {
                ShapeDefinition def = shapes.Get(i);
                if (def == null) continue;
                if (def.category == ShapeCategory.Armour)
                {
                    armourIndices.Add(i);
                }
                else
                {
                    if (!categoryIndices.TryGetValue(def.category, out List<int> list))
                    {
                        list = new List<int>();
                        categoryIndices.Add(def.category, list);
                        categoryOrder.Add(def.category);
                    }
                    list.Add(i);
                }
            }
            _armourShapeIndices = armourIndices.ToArray();

            int slotCount = armourIndices.Count + categoryOrder.Count + 1; // +1 for Delete
            float totalWidth = slotCount * buttonSize.x + Mathf.Max(0, slotCount - 1) * spacing;
            float startX = -totalWidth / 2f + buttonSize.x / 2f;
            int slot = 0;

            // Armour shape buttons.
            for (int a = 0; a < armourIndices.Count; a++)
            {
                int i = armourIndices[a];
                int idx = i;
                ShapeDefinition def = shapes.Get(i);
                string label = def != null ? def.displayName : $"Shape #{i}";

                (Button btn, Text _) = UIStyle.BuildLabeledButton(root, label, buttonSize, fontSize);
                RectTransform rt = (RectTransform)btn.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(startX + slot * (buttonSize.x + spacing), bottomMargin);

                btn.onClick.AddListener(() => OnShapeButtonClicked(idx));

                AddPointerHandlers(btn.gameObject, idx);

                Image swatch = BuildCornerSwatch(rt);
                _shapeSwatches[i] = swatch;

                _shapeButtons[i] = btn;
                _shapeBackgrounds[i] = btn.GetComponent<Image>();
                slot++;
            }

            // One CategoryFlyout per non-armour category — its button
            // takes one slot; its flyout panel is built immediately
            // after so its anchored-X tracks the button.
            _categoryFlyouts.Clear();
            for (int c = 0; c < categoryOrder.Count; c++)
            {
                ShapeCategory category = categoryOrder[c];
                int[] indices = categoryIndices[category].ToArray();
                string label = CategoryButtonLabel(category);
                float anchoredX = startX + slot * (buttonSize.x + spacing);

                CategoryFlyout flyout = new CategoryFlyout(
                    buildManager,
                    this,
                    indices,
                    label,
                    buttonSize,
                    fontSize,
                    bottomMargin,
                    flyoutEntrySize,
                    flyoutEntrySpacing,
                    flyoutBottomGap,
                    peekAlpha,
                    hoverPeekDelay,
                    BuildCornerSwatch,
                    BuildEntrySwatch,
                    () => CloseFlyoutsExcept(null),
                    () => AnyOtherFlyoutPinned(null));
                flyout.BuildButton(root, anchoredX);
                flyout.BuildFlyout(root);
                _categoryFlyouts.Add(flyout);
                slot++;
            }

            // Delete button — final slot.
            (Button delBtn, Text _ignored) = UIStyle.BuildLabeledButton(root, deleteButtonLabel, buttonSize, fontSize);
            RectTransform delRT = (RectTransform)delBtn.transform;
            delRT.anchorMin = delRT.anchorMax = delRT.pivot = new Vector2(0.5f, 0f);
            delRT.anchoredPosition = new Vector2(startX + slot * (buttonSize.x + spacing), bottomMargin);
            delBtn.onClick.AddListener(() => buildManager.SetCurrentTool(BuildTool.Delete));
            _deleteButton = delBtn;
            _deleteBackground = delBtn.GetComponent<Image>();

            // ---- Bottom-left stat labels ----
            _massLabel = UIStyle.BuildLabel(root, "Mass: 0 / 100", fontSize: statFontSize);
            _massLabel.alignment = TextAnchor.LowerLeft;
            RectTransform massRT = (RectTransform)_massLabel.transform;
            massRT.anchorMin = massRT.anchorMax = massRT.pivot = new Vector2(0f, 0f);
            massRT.anchoredPosition = massLabelAnchoredPosition;
            massRT.sizeDelta = statLabelSize;

            _hpLabel = UIStyle.BuildLabel(root, "HP: 0", fontSize: statFontSize);
            _hpLabel.alignment = TextAnchor.LowerLeft;
            RectTransform hpRT = (RectTransform)_hpLabel.transform;
            hpRT.anchorMin = hpRT.anchorMax = hpRT.pivot = new Vector2(0f, 0f);
            hpRT.anchoredPosition = hpLabelAnchoredPosition;
            hpRT.sizeDelta = statLabelSize;

            _selectedStatsLabel = UIStyle.BuildLabel(root, string.Empty, fontSize: statFontSize);
            _selectedStatsLabel.alignment = TextAnchor.LowerLeft;
            _selectedStatsLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            _selectedStatsLabel.verticalOverflow = VerticalWrapMode.Overflow;
            RectTransform selRT = (RectTransform)_selectedStatsLabel.transform;
            selRT.anchorMin = selRT.anchorMax = selRT.pivot = new Vector2(0f, 0f);
            selRT.anchoredPosition = selectedStatsAnchoredPosition;
            selRT.sizeDelta = selectedStatsSize;

            BuildFlyout(root);
        }
```

Behaviour-preservation notes for this region:
- Today the registry holds only `Armour` and `Weapon` shapes, so `categoryOrder` is exactly `[Weapon]` (when any weapon exists) — `slotCount`, `totalWidth`, `startX` and every button's anchored-X are byte-identical to the old `armourIndices.Count + (hasWeapons ? 1 : 0) + 1` arithmetic.
- The old code's `_lastArmedWeaponIndex = _weaponShapeIndices[0]` default is now the `CategoryFlyout` constructor's `_lastArmedShapeIndex = _shapeIndices[0]`.
- The Weapons button + flyout were previously built by inline code + `BuildWeaponsFlyout`; now `flyout.BuildButton` + `flyout.BuildFlyout` do exactly the same construction. The flyout panel is built right after its button (the old code deferred `BuildWeaponsFlyout` to the end of `BuildToolbar`, but it only reads `_weaponsButton`'s anchored position lazily at `Open` time — order is immaterial).
- `BuildCornerSwatch` and `BuildEntrySwatch` are passed as method-group delegates, so the category flyout reuses the controller's swatch builders verbatim.

- [ ] **Step 4: Update `PeekAfterDelay` (material-flyout peek) to consult the category list**

Find the material flyout's `PeekAfterDelay` coroutine:

```csharp
        IEnumerator PeekAfterDelay(int shapeIndex)
        {
            yield return new WaitForSeconds(hoverPeekDelay);
            // Don't peek-open if ANY flyout is already pinned —
            // peek-opening another would call OpenFlyoutForShape with
            // pin: false, which would silently unpin the user's
            // deliberate pinned selection just because they hovered
            // a different button.
            if (_flyout != null && _flyout.activeSelf && _flyoutPinned) yield break;
            if (_weaponsFlyout != null && _weaponsFlyout.activeSelf && _weaponsFlyoutPinned) yield break;
            OpenFlyoutForShape(shapeIndex, pin: false);
            _peekRoutine = null;
        }
```

Replace it with:

```csharp
        IEnumerator PeekAfterDelay(int shapeIndex)
        {
            yield return new WaitForSeconds(hoverPeekDelay);
            // Don't peek-open if ANY flyout is already pinned —
            // peek-opening another would call OpenFlyoutForShape with
            // pin: false, which would silently unpin the user's
            // deliberate pinned selection just because they hovered
            // a different button.
            if (_flyout != null && _flyout.activeSelf && _flyoutPinned) yield break;
            if (AnyCategoryFlyoutPinned()) yield break;
            OpenFlyoutForShape(shapeIndex, pin: false);
            _peekRoutine = null;
        }
```

- [ ] **Step 5: Update `OpenFlyoutForShape` (material flyout) mutual exclusion**

Find this region inside `OpenFlyoutForShape`:

```csharp
            // Mutual exclusion with the weapons flyout — opening one
            // must close the other so they never visually overlap.
            // OpenWeaponsFlyout has the symmetric call.
            if (_weaponsFlyout != null && _weaponsFlyout.activeSelf) HideWeaponsFlyout();
```

Replace it with:

```csharp
            // Mutual exclusion with the category flyouts — opening one
            // must close the others so they never visually overlap.
            // CategoryFlyout.Open has the symmetric call via closeOthers.
            CloseAllCategoryFlyouts();
```

- [ ] **Step 6: Replace `OnCurrentShapeChanged`**

Find the current `OnCurrentShapeChanged` method and replace it in full with:

```csharp
        void OnCurrentShapeChanged(int shapeIndex)
        {
            UpdateButtonStates();
            RefreshSelectedStats();
            // Refresh every category flyout's button swatch + flyout
            // highlights, and let each note an arm of one of its own
            // shapes so its corner swatch keeps that colour when the
            // player switches to another category and back.
            for (int i = 0; i < _categoryFlyouts.Count; i++)
            {
                _categoryFlyouts[i].NoteArmedShape(shapeIndex);
                _categoryFlyouts[i].RefreshSwatch();
                _categoryFlyouts[i].RefreshFlyoutHighlights();
            }
            // Closing the flyouts on shape change avoids stale state.
            HideFlyout();
            CloseAllCategoryFlyouts();
        }
```

The old method called `RefreshWeaponsButtonSwatch`, `RefreshWeaponsFlyoutEntryHighlights`, set `_lastArmedWeaponIndex` for a weapon, and called `HideWeaponsFlyout`. `NoteArmedShape` is a no-op for a shape outside a given category, so a weapon arm updates only the Weapons flyout's memory — identical to the old `if (shape.IsWeapon) _lastArmedWeaponIndex = shapeIndex`.

- [ ] **Step 7: Replace `OnCurrentToolChanged`**

Find the current `OnCurrentToolChanged` method and replace it in full with:

```csharp
        void OnCurrentToolChanged(BuildTool tool)
        {
            UpdateButtonStates();
            RefreshSelectedStats();
            if (tool == BuildTool.Delete)
            {
                HideFlyout();
                CloseAllCategoryFlyouts();
            }
        }
```

- [ ] **Step 8: Replace the entire `// ---------- Weapons button + flyout ----------` section with category-flyout helpers**

Delete the whole region from the `// ---------- Weapons button + flyout ----------` comment down to (but not including) the `// ---------- Swatch builders ----------` comment. That region is eleven methods — `OnWeaponsButtonClicked`, `AddWeaponsPointerHandlers`, `OnWeaponsButtonHoverEnter`, `OnWeaponsButtonHoverExit`, `WeaponsPeekAfterDelay`, `BuildWeaponsFlyout`, `OnWeaponsFlyoutEntryClicked`, `OpenWeaponsFlyout`, `HideWeaponsFlyout`, `IsPointerOverWeaponsFlyout`, `RefreshWeaponsFlyoutEntryHighlights` — all of them now live inside `CategoryFlyout`. The twelfth weapons method, `RefreshWeaponsButtonSwatch`, sits further down the file in the swatch-refresh area and is deleted separately in Step 9.

In their place, insert this section (so the file reads `… RefreshFlyoutEntryHighlights }` → this new section → `// ---------- Swatch builders ----------`):

```csharp
        // ---------- Category flyouts (Weapons; Utilities later) ----------

        // The CategoryFlyout that owns `shapeIndex`, or null when the
        // shape is an armour shape (no category flyout) or out of range.
        CategoryFlyout FindCategoryFlyout(int shapeIndex)
        {
            for (int i = 0; i < _categoryFlyouts.Count; i++)
            {
                if (_categoryFlyouts[i].ContainsShape(shapeIndex))
                    return _categoryFlyouts[i];
            }
            return null;
        }

        // Hide every category flyout. Used on shape / tool change and as
        // half of the material-flyout mutual-exclusion.
        void CloseAllCategoryFlyouts()
        {
            for (int i = 0; i < _categoryFlyouts.Count; i++)
                _categoryFlyouts[i].Hide();
        }

        // Mutual-exclusion helper passed to each CategoryFlyout as its
        // `closeOthers` action: close the material flyout and every
        // category flyout other than `keep` (pass null to close them
        // all). A flyout never needs to close itself, so passing the
        // caller as `keep` is harmless — Hide() on an already-shown
        // flyout that is about to re-open is a no-op-then-reopen.
        void CloseFlyoutsExcept(CategoryFlyout keep)
        {
            if (_flyout != null && _flyout.activeSelf) HideFlyout();
            for (int i = 0; i < _categoryFlyouts.Count; i++)
            {
                if (_categoryFlyouts[i] == keep) continue;
                _categoryFlyouts[i].Hide();
            }
        }

        // True when at least one category flyout is currently pinned.
        bool AnyCategoryFlyoutPinned()
        {
            for (int i = 0; i < _categoryFlyouts.Count; i++)
                if (_categoryFlyouts[i].IsPinned) return true;
            return false;
        }

        // The `anyOtherFlyoutPinned` predicate passed to each
        // CategoryFlyout: true when the material flyout, or any category
        // flyout other than `self`, is pinned. A category flyout's peek
        // logic uses it to avoid unpinning another flyout's deliberate
        // selection on a stray hover.
        bool AnyOtherFlyoutPinned(CategoryFlyout self)
        {
            if (_flyout != null && _flyout.activeSelf && _flyoutPinned) return true;
            for (int i = 0; i < _categoryFlyouts.Count; i++)
            {
                if (_categoryFlyouts[i] == self) continue;
                if (_categoryFlyouts[i].IsPinned) return true;
            }
            return false;
        }

        // Toolbar button label for a non-armour category.
        string CategoryButtonLabel(ShapeCategory category)
        {
            switch (category)
            {
                case ShapeCategory.Weapon: return weaponsButtonLabel;
                default:                   return category.ToString();
            }
        }
```

Notes:
- `CloseFlyoutsExcept` / `AnyOtherFlyoutPinned` take a `CategoryFlyout keep`/`self` so PR 1b's second category gets correct mutual exclusion for free. In Task 2's wiring (Step 3) both are called with `null` — with a single category that is exactly the old behaviour (`OpenWeaponsFlyout` closed the material flyout; `WeaponsPeekAfterDelay` checked the material flyout's pin state). The `keep`/`self` parameter is dead-but-correct until PR 1b; passing `null` keeps the single-category behaviour byte-identical.
- `CategoryButtonLabel` returns the serialized `weaponsButtonLabel` for `Weapon`. The `default` branch (`category.ToString()`) is only reachable once PR 1b adds `ShapeCategory.Utility`; it is harmless today and avoids a second edit here.

- [ ] **Step 9: Replace `RefreshAllSwatches` and delete `RefreshWeaponsButtonSwatch`**

Find the current `RefreshAllSwatches` method:

```csharp
        // Refresh the corner swatch on every shape button to reflect
        // each shape's currently-armed material.
        void RefreshAllSwatches()
        {
            if (_shapeSwatches != null)
            {
                for (int i = 0; i < _shapeSwatches.Length; i++) RefreshSwatchFor(i);
            }
            RefreshWeaponsButtonSwatch();
        }
```

Replace it with:

```csharp
        // Refresh the corner swatch on every shape button to reflect
        // each shape's currently-armed material.
        void RefreshAllSwatches()
        {
            if (_shapeSwatches != null)
            {
                for (int i = 0; i < _shapeSwatches.Length; i++) RefreshSwatchFor(i);
            }
            for (int i = 0; i < _categoryFlyouts.Count; i++)
                _categoryFlyouts[i].RefreshSwatch();
        }
```

Then delete the now-orphaned `RefreshWeaponsButtonSwatch` method. Steps 6 and 9 removed its only two callers, and it references the `_weaponsSwatch` field deleted in Step 1 — left in place it will not compile. Find and delete the whole method, including its leading comment:

```csharp
        // The Weapons button's corner swatch shows the colour of the
        // currently-armed weapon (when one is active) or the last-armed
        // weapon (when an armour shape is active). Defaults to the
        // first weapon's colour on cold-start.
        void RefreshWeaponsButtonSwatch()
        {
            if (_weaponsSwatch == null) return;
            if (buildManager == null || buildManager.Shapes == null) return;

            int activeIdx = buildManager.CurrentShapeIndex;
            ShapeDefinition activeShape = buildManager.Shapes.Get(activeIdx);
            int swatchShape = (activeShape != null && activeShape.IsWeapon)
                ? activeIdx
                : _lastArmedWeaponIndex;

            ShapeDefinition shape = buildManager.Shapes.Get(swatchShape);
            MaterialDefinition wmat = shape != null ? shape.weaponMaterial : null;
            _weaponsSwatch.color = wmat != null ? wmat.SwatchColor : Color.gray;
        }
```

- [ ] **Step 10: Update `UpdateButtonStates` — drop the `_weaponsBackground` highlight**

Find this region inside `UpdateButtonStates` (the `_weaponsBackground` highlight, after the `_shapeBackgrounds` loop):

```csharp
            // The Weapons button gets the same selected highlight as
            // armour shapes, but switches on whenever ANY weapon is the
            // active shape (rather than a specific shape index).
            if (_weaponsBackground != null)
                _weaponsBackground.color = weaponActive ? SelectedTypeColor : UIStyle.BackgroundIdle;
            if (_deleteBackground != null)
                _deleteBackground.color = deleteActive ? deleteSelectedColor : UIStyle.BackgroundIdle;
        }
```

Replace it with:

```csharp
            // Each category button gets the same selected highlight as
            // the armour buttons, lit whenever a shape in that category
            // is the active shape.
            for (int i = 0; i < _categoryFlyouts.Count; i++)
                _categoryFlyouts[i].RefreshButtonHighlight();
            if (_deleteBackground != null)
                _deleteBackground.color = deleteActive ? deleteSelectedColor : UIStyle.BackgroundIdle;
        }
```

`CategoryFlyout.RefreshButtonHighlight` reproduces the old `weaponActive ? SelectedTypeColor : UIStyle.BackgroundIdle` exactly: `IsActiveCategory()` is `CurrentTool == Place && ContainsShape(CurrentShapeIndex)`, and the old `weaponActive` was `!deleteActive && activeShape != null && activeShape.IsWeapon`. With weapons being the only non-armour category, these agree (`CurrentTool != Place` ⇔ `deleteActive`, since the only tools are `Place` and `Delete`). The `weaponActive` local in `UpdateButtonStates` is still used by the `_shapeBackgrounds` loop above this region, so leave that local in place.

- [ ] **Step 11: Confirm no `_weapons*` / `*Weapons*` references remain**

Search `BuildToolbarController.cs` for the identifiers `_weapons`, `Weapons` (method names), `_lastArmedWeaponIndex`, `_weaponShapeIndices`. The only surviving occurrences must be:
- the serialized field `weaponsButtonLabel` and its `[Header("Weapons button (toolbar)")]` attribute (kept — it is the button's label text, now consumed by `CategoryButtonLabel`);
- the word "Weapons"/"weapon" inside comments.

There must be **zero** references to any removed field (`_weaponsButton`, `_weaponsBackground`, `_weaponsSwatch`, `_weaponShapeIndices`, `_weaponsFlyout`, `_weaponsFlyoutGroup`, `_weaponsFlyoutButtons`, `_weaponsFlyoutBackgrounds`, `_weaponsFlyoutPinned`, `_weaponsPeekRoutine`, `_lastArmedWeaponIndex`) or removed method (`OnWeaponsButtonClicked`, `AddWeaponsPointerHandlers`, `OnWeaponsButtonHoverEnter`, `OnWeaponsButtonHoverExit`, `WeaponsPeekAfterDelay`, `BuildWeaponsFlyout`, `OnWeaponsFlyoutEntryClicked`, `OpenWeaponsFlyout`, `HideWeaponsFlyout`, `IsPointerOverWeaponsFlyout`, `RefreshWeaponsFlyoutEntryHighlights`, `RefreshWeaponsButtonSwatch`). If any remain, the corresponding step above was applied incompletely — fix before compiling.

- [ ] **Step 12: Compile + console check**

Refresh Unity (`refresh_unity`, `compile="request"`, `mode="force"`, `scope="all"`, `wait_for_ready=true`), poll `mcpforunity://editor/state` until `is_compiling=false` and `ready_for_tools=true`, then `read_console(action="get", types=["error"], count=50)`.

Expected: zero errors. Any "unused field/method" warnings from Task 1 are now gone (the class is fully exercised). If the console reports an unresolved name, cross-check it against the Step 11 reference list.

- [ ] **Step 13: Commit**

```bash
git add "Assets/Scripts/Build/BuildToolbarController.cs"
git commit -m "Refactor Weapons toolbar machinery into CategoryFlyout"
```

---

## Task 3: Compile, editor regression check, final commit

**Files:**
- No code changes expected. If the regression check uncovers a defect, fix it in `Assets/Scripts/Build/CategoryFlyout.cs` and/or `Assets/Scripts/Build/BuildToolbarController.cs` — a refactor regression, not a feature change — and re-verify.

This task is the behaviour-preservation gate. PR 1a is done only when the Weapons button + flyout behave **exactly** as before the refactor.

- [ ] **Step 1: Clean compile**

Refresh Unity (`refresh_unity`, `compile="request"`, `mode="force"`, `scope="all"`, `wait_for_ready=true`), poll `mcpforunity://editor/state` until `is_compiling=false` and `ready_for_tools=true`, then `read_console(action="get", types=["error"], count=50)`. Zero errors required.

- [ ] **Step 2: Editor regression check — the weapons flyout**

Open the Build scene in the editor and enter Play mode (via `manage_editor` play control, or run the scene by hand). Verify each behaviour below. Every one must match pre-refactor behaviour exactly:

1. **Toolbar layout** — the toolbar reads `[Cube] [Slope] [Weapons ▸] [Delete]`, same button positions and spacing as before.
2. **Weapon-active highlight** — arming a weapon (via the flyout) lights the **Weapons** button with the blue selected highlight; arming an armour shape clears it. The active armour button highlights as before.
3. **Left-click opens a pinned flyout** — clicking the Weapons button opens the flyout fully opaque and interactive; clicking the button again while pinned closes it.
4. **Right-click opens a pinned flyout** — right-clicking the Weapons button opens the flyout pinned.
5. **Hover peek** — resting the cursor on the Weapons button for `hoverPeekDelay` (~0.30 s) fades the flyout in at `peekAlpha` (translucent, non-interactive); moving the cursor off the button — without entering the flyout — closes the peek; moving the cursor from the button into the flyout keeps it open.
6. **Entry click arms + closes** — clicking a weapon entry in the flyout arms that weapon and closes the flyout.
7. **M toggles** — with a weapon active, `M` opens the weapons flyout (pinned) if closed and closes it if open and pinned. With an armour shape active, `M` toggles that shape's material flyout (unchanged).
8. **Esc closes** — `Esc` closes an open weapons flyout (and an open material flyout); `Esc` that opens/closes the pause overlay does **not** also close the flyout.
9. **Corner swatch tracks the last-armed weapon** — the Weapons button's corner swatch shows the active weapon's coupled-material colour while a weapon is active, and retains the last-armed weapon's colour after switching to an armour shape; on cold start it shows the first weapon's colour.
10. **Mutual exclusion** — opening the material flyout (re-click / right-click / hover-peek of an armour button) closes an open weapons flyout; opening the weapons flyout closes an open material flyout. The two are never visible at once.
11. **No peek-unpin** — with the weapons flyout pinned, hovering an armour button does **not** peek-open the material flyout (and vice versa) — the pinned flyout stays pinned.
12. **Unchanged systems** — armour shape buttons, the material flyout, digit keys (1–9 arm armour shapes; Shift+digit set the active armour material), and the Delete button all behave as before.

- [ ] **Step 3: Final commit**

If Step 2 surfaced no defect, there is nothing new to commit — Tasks 1 and 2 already committed the work; record completion with an empty-tree-safe check (`git status` should be clean for the two touched files). If Step 2 required a fix, commit it:

```bash
git add "Assets/Scripts/Build/CategoryFlyout.cs" "Assets/Scripts/Build/BuildToolbarController.cs"
git commit -m "Fix CategoryFlyout regression found in editor check"
```

Then report PR 1a complete: `BuildToolbarController` no longer carries any `_weapons*` state; the Weapons category is one `CategoryFlyout` in `_categoryFlyouts`; behaviour is unchanged; PR 1b can add the Utilities category as a data-only addition.

---

## Notes & risks

- **Single-category equivalence.** Today the registry has only `Armour` + `Weapon` shapes, so `_categoryFlyouts` holds exactly one entry. Every generalised construct (`categoryOrder`, `CloseFlyoutsExcept(null)`, `AnyOtherFlyoutPinned(null)`, the `for` loops over `_categoryFlyouts`) collapses to the old single-Weapons behaviour. The `keep`/`self` parameters are intentionally dead until PR 1b — they exist so PR 1b adds Utilities with no edit to mutual-exclusion logic.
- **Toolbar arithmetic must not move buttons.** `slotCount` is `armourIndices.Count + categoryOrder.Count + 1`; with one non-armour category that equals the old `armourIndices.Count + (hasWeapons ? 1 : 0) + 1`. If a screenshot shows buttons shifted, this arithmetic diverged — re-check Step 3 of Task 2.
- **`weaponMaterial` field name.** `CategoryFlyout.BuildFlyout` / `RefreshSwatch` read `ShapeDefinition.weaponMaterial` — its current name. `thruster_boost_spec.md` §3.1 renames it to `coupledMaterial` (with `[FormerlySerializedAs]`); that rename is **PR 1b**, explicitly out of scope here. Do not rename it in this plan.
- **`category-agnostic` requirement.** `CategoryFlyout` contains no `IsWeapon` / `ShapeCategory.Weapon` reference — it is driven entirely by the injected `int[] shapeIndices`. PR 1b constructs a second instance for Utilities with no change to the class. `CategoryButtonLabel` in the controller has a `default` branch ready for `ShapeCategory.Utility`.
- **Coroutine ownership.** `CategoryFlyout` is not a `MonoBehaviour`; it calls `_owner.StartCoroutine` / `StopCoroutine`. The `_owner` is the `BuildToolbarController` (`this` is passed in Task 2 Step 3) — a live scene component for the toolbar's lifetime, so the peek coroutine has a valid host.
- **No new `MonoImporter` routine beyond Task 1.** Only one new script (`CategoryFlyout.cs`) is added; its `.meta` is hand-written in Task 1 Step 2. `BuildToolbarController.cs` already has its `.meta` — it is not re-created.
- **No unit-test framework.** Verification is the MCP compile/console check plus the Task 3 Step 2 editor regression list — there is no automated test to add.

## Self-review

- **Spec coverage** — `thruster_boost_spec.md` §4 / §4.1 ("generalize the category flyout" into a reusable `CategoryFlyout`, instantiated per non-armour category, owning button + swatch + flyout + peek/pin/Esc/M state + last-armed memory) → Task 1 (the class) + Task 2 (the controller wiring). The spec's broader PR 1 items — the `Cone` mesh, `ShapeUtilityThruster.asset`, `ThrusterMatDef`/`ThrusterMat`, `PlacedThruster.prefab`, the `ShapeCategory.Utility` enum value, the `weaponMaterial`→`coupledMaterial` rename, `GameData` save-path change — are **deliberately excluded**: PR 1a is the pure refactor; those are PR 1b. Stated in "Starting state" and "Notes & risks".
- **Placeholder scan** — no `TODO`, no "similar to…", no "extract the methods" hand-waving. `CategoryFlyout.cs` appears verbatim and complete in Task 1 Step 1. Every modified `BuildToolbarController` region (the field block, `Update()`, the `BuildToolbar()` sequence, `PeekAfterDelay`, the `OpenFlyoutForShape` mutual-exclusion line, `OnCurrentShapeChanged`, `OnCurrentToolChanged`, the new category-flyout helper section, `RefreshAllSwatches`, the `UpdateButtonStates` highlight region) appears verbatim in its new form in Task 2.
- **Type / name consistency** — the class is `CubeFly.Build.CategoryFlyout`. Public surface used by the controller: `BuildButton(RectTransform, float)`, `BuildFlyout(RectTransform)`, `IsOpen`, `IsPinned`, `Hide()`, `Toggle()`, `Open(bool)`, `RefreshButtonHighlight()`, `RefreshSwatch()`, `RefreshFlyoutHighlights()`, `ContainsShape(int)`, `LastArmedShapeIndex`, `NoteArmedShape(int)`. Every call in Task 2 uses exactly these names: `Update()` → `Toggle()` / `Hide()`; `BuildToolbar()` → constructor + `BuildButton` + `BuildFlyout`; `OnCurrentShapeChanged` → `NoteArmedShape` + `RefreshSwatch` + `RefreshFlyoutHighlights`; `OnCurrentToolChanged`/`OpenFlyoutForShape` → `CloseAllCategoryFlyouts`; `RefreshAllSwatches` → `RefreshSwatch`; `UpdateButtonStates` → `RefreshButtonHighlight`; `FindCategoryFlyout`/`AnyCategoryFlyoutPinned`/`AnyOtherFlyoutPinned`/`CloseFlyoutsExcept` → `ContainsShape` / `IsPinned` / `Hide`. The controller field is `readonly List<CategoryFlyout> _categoryFlyouts`. Constructor parameter order (Task 1) matches the call site (Task 2 Step 3): `buildManager, this, indices, label, buttonSize, fontSize, bottomMargin, flyoutEntrySize, flyoutEntrySpacing, flyoutBottomGap, peekAlpha, hoverPeekDelay, BuildCornerSwatch, BuildEntrySwatch, closeOthers, anyOtherFlyoutPinned`. The two swatch delegates match the controller's existing signatures: `BuildCornerSwatch(RectTransform) → Image` and `BuildEntrySwatch(RectTransform, Color) → Image`.
