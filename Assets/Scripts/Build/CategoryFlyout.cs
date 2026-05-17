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
            MaterialDefinition wmat = shape != null ? shape.coupledMaterial : null;
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
