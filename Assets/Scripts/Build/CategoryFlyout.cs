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
        Image _glyph;
        Text _btnLabel;
        Outline _selectionOutline;
        GameObject _flyout;
        CanvasGroup _flyoutGroup;
        Button[] _flyoutButtons;
        Image[] _flyoutBackgrounds;
        bool _flyoutPinned;               // always true while open (peek removed in UX batch 2026-05-20)
        // Seconds since the cursor last left the flyout's hover area.
        // Ticked externally via TickAwayTimer; cursor re-enter resets.
        float _awayTimer;

        // Last-armed shape in this category — drives the toolbar button's
        // corner swatch when a shape from another category is active.
        // Defaults to the category's first shape.
        int _lastArmedShapeIndex = -1;

        static readonly Color FlyoutEntryIdle   = CscPalette.HudCard;
        static readonly Color FlyoutEntryActive = CscPalette.Ochre300;

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
            (Button btn, Text lbl) = UIStyle.BuildLabeledButton(canvas, _buttonLabel, _buttonSize, _fontSize);
            _btnLabel = lbl;
            RectTransform rt = (RectTransform)btn.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(anchoredX, _bottomMargin);

            btn.onClick.AddListener(OnButtonClicked);
            AddPointerHandlers(btn.gameObject);

            _swatch = _buildCornerSwatch(rt);
            _button = btn;
            _background = btn.GetComponent<Image>();

            int armedShape = _lastArmedShapeIndex >= 0 ? _lastArmedShapeIndex : (_shapeIndices.Length > 0 ? _shapeIndices[0] : -1);
            ShapeDefinition armed = armedShape >= 0 && _buildManager.Shapes != null ? _buildManager.Shapes.Get(armedShape) : null;
            int armedMat = armed != null ? _buildManager.GetMaterialForShape(armedShape) : 0;
            Sprite g = armed != null ? CscSprites.ForShape(armed.displayName, armedMat) : null;
            _glyph = UIStyle.DecorateToolbarSlot(_button, _btnLabel, g, null, _buttonLabel);
            _selectionOutline = UIStyle.AddSelectionOutline(_button.gameObject);
            _swatch.enabled = false;   // glyph conveys the armed shape now
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
                MaterialDefinition wmat = shape != null ? shape.coupledMaterial : null;
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

                Sprite eg = shape != null ? CscSprites.ForShape(shape.displayName, 0) : null;
                if (eg != null)
                {
                    GameObject egGO = new GameObject("EntryGlyph", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    egGO.transform.SetParent(brt, false);
                    RectTransform egRT = (RectTransform)egGO.transform;
                    egRT.anchorMin = egRT.anchorMax = new Vector2(0f, 0.5f);
                    egRT.pivot = new Vector2(0f, 0.5f);
                    egRT.anchoredPosition = new Vector2(6f, 0f);
                    egRT.sizeDelta = new Vector2(28f, 28f);
                    Image egImg = egGO.GetComponent<Image>();
                    egImg.sprite = eg; egImg.color = Color.white;
                    egImg.preserveAspect = true; egImg.raycastTarget = false;
                    RectTransform elrt = (RectTransform)label.transform;
                    elrt.offsetMin = new Vector2(40f, elrt.offsetMin.y);   // inset text to clear the glyph
                }

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
            // Position the flyout ABOVE the category button so it sits
            // fully clear of the toolbar row (UX batch 2026-05-20).
            frt.anchoredPosition = new Vector2(
                btnRT.anchoredPosition.x,
                _bottomMargin + _buttonSize.y + _flyoutBottomGap);

            _flyout.SetActive(true);
            // Peek-on-hover removed (UX batch 2026-05-20); `pin` kept
            // for API compat — every caller is a click / right-click /
            // M-key, always pinned.
            _flyoutGroup.alpha = 1f;
            _flyoutGroup.blocksRaycasts = true;
            _flyoutPinned = true;
            _awayTimer = 0f;
            RefreshFlyoutHighlights();
        }

        // Per-frame tick from the owning BuildToolbarController. While
        // the flyout is open and the cursor is NOT over its hover area,
        // accumulate `dt`; reaching `closeSeconds` → auto-Hide. Cursor
        // re-enter resets the timer.
        public void TickAwayTimer(float dt, float closeSeconds)
        {
            if (!IsOpen) return;
            if (IsPointerOverFlyout()) _awayTimer = 0f;
            else                       _awayTimer += dt;
            if (_awayTimer >= closeSeconds) Hide();
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
            _background.color = CscTheme.CardFill;
            if (_selectionOutline != null) _selectionOutline.enabled = IsActiveCategory();
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
            MaterialDefinition wmat = shape != null ? shape.coupledMaterial : null;
            _swatch.color = wmat != null ? wmat.SwatchColor : Color.gray;
            if (_glyph != null && shape != null)
            {
                int mat = _buildManager.GetMaterialForShape(swatchShape);
                Sprite g = CscSprites.ForShape(shape.displayName, mat);
                if (g != null) { _glyph.sprite = g; _glyph.enabled = true; }
            }
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
            // Only right-click is wired. Peek-on-hover removed in the
            // UX batch 2026-05-20, so PointerEnter / PointerExit triggers
            // would dispatch to nothing — keeping them just burns
            // EventSystem cycles for no behaviour.
            EventTrigger trigger = buttonObject.AddComponent<EventTrigger>();

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

        void OnFlyoutEntryClicked(int shapeIndex)
        {
            _buildManager.SetCurrentShape(shapeIndex);
            _lastArmedShapeIndex = shapeIndex;
            Hide();
        }

        bool IsPointerOverFlyout()
        {
            if (_flyout == null || Mouse.current == null) return false;
            // Allocation-free rect test — see BuildToolbarController.
            // TickAwayTimer calls this every frame; the old
            // EventSystem.RaycastAll path was a steady GC source.
            return RectTransformUtility.RectangleContainsScreenPoint(
                (RectTransform)_flyout.transform,
                Mouse.current.position.ReadValue(),
                null);
        }
    }
}
