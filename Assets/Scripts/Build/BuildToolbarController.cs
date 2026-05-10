using System.Collections;
using System.Collections.Generic;
using CubeFly.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CubeFly.Build
{
    // Bottom-of-screen toolbar in the build scene.
    //
    // Layout (left → right): one button per Shape from BuildManager.Shapes,
    // followed by a Delete button.
    //
    // Each shape button shows its display name and a small corner swatch
    // whose colour reflects the material currently armed for that shape.
    // Clicking the *active* shape (or right-clicking any shape) opens a
    // material flyout pinned above that button. Hovering a shape button
    // for >hoverPeekDelay seconds fades the flyout in at peekAlpha so the
    // player can compare materials without committing a click. Clicking
    // a material commits and closes the flyout. Clicking outside the
    // flyout, pressing Escape, or pressing M closes it.
    //
    // The bottom-left stat block ("Mass: X / 100", "HP: Y") is unchanged.
    // The "Selected" line now reads "Selected: <Shape> · Material <X>".
    public class BuildToolbarController : MonoBehaviour
    {
        [SerializeField] BuildManager buildManager;
        [SerializeField] Vector2 buttonSize = new Vector2(160f, 60f);
        [SerializeField] float spacing = 16f;
        [SerializeField] float bottomMargin = 30f;
        [SerializeField] int fontSize = 22;

        [Header("Rotate hint (top-left)")]
        [SerializeField] string hintText = "Rotate: R/T";
        [SerializeField] int hintFontSize = 18;
        [SerializeField] Vector2 hintAnchoredPosition = new Vector2(20f, -20f);
        [SerializeField] Vector2 hintSize = new Vector2(220f, 32f);

        [Header("Floating message (top-center)")]
        [SerializeField] int floatingFontSize = 36;
        [SerializeField] Vector2 floatingAnchoredPosition = new Vector2(0f, -40f);
        [SerializeField] Vector2 floatingSize = new Vector2(700f, 60f);
        [SerializeField] Color floatingColor = new Color(1f, 0.45f, 0.45f, 1f);

        [Header("Delete button (toolbar)")]
        [SerializeField] string deleteButtonLabel = "Delete";
        [SerializeField] Color deleteSelectedColor = new Color(0.85f, 0.25f, 0.25f, 0.95f);

        [Header("Stat labels (bottom-left)")]
        [SerializeField] int statFontSize = 20;
        [SerializeField] Vector2 massLabelAnchoredPosition = new Vector2(20f, 60f);
        [SerializeField] Vector2 hpLabelAnchoredPosition = new Vector2(20f, 28f);
        [SerializeField] Vector2 statLabelSize = new Vector2(260f, 28f);

        [Header("Selected-cube stat label (bottom-left, right of Mass/HP)")]
        [SerializeField] Vector2 selectedStatsAnchoredPosition = new Vector2(300f, 28f);
        [SerializeField] Vector2 selectedStatsSize = new Vector2(560f, 60f);
        [SerializeField] string deleteToolLabelText = "Delete tool — click a cube to remove";

        [Header("Material flyout")]
        [SerializeField] Vector2 flyoutEntrySize = new Vector2(220f, 56f);
        [SerializeField] float flyoutEntrySpacing = 6f;
        [SerializeField] float flyoutBottomGap = 10f;
        [Tooltip("Seconds the cursor must rest on a shape button before the flyout fades in (peek).")]
        [SerializeField] float hoverPeekDelay = 0.30f;
        [Tooltip("Alpha of the flyout when peeking (hover-only). Fully opaque when pinned via click.")]
        [SerializeField, Range(0f, 1f)] float peekAlpha = 0.6f;
        [SerializeField] Vector2 swatchSize = new Vector2(18f, 18f);

        const string TAG = "BuildToolbar";

        Button[] _shapeButtons;
        Image[] _shapeBackgrounds;
        Image[] _shapeSwatches;
        Button _deleteButton;
        Image _deleteBackground;
        Text _massLabel;
        Text _hpLabel;
        Text _selectedStatsLabel;
        Text _floatingMessage;
        Coroutine _floatingRoutine;

        // Material flyout state.
        GameObject _flyout;
        CanvasGroup _flyoutGroup;
        Button[] _flyoutButtons;
        Image[] _flyoutBackgrounds;
        int _flyoutOwnerShape = -1;       // shape whose flyout is currently shown
        bool _flyoutPinned;               // true if opened by click; false when only hover-peeking
        Coroutine _peekRoutine;
        RectTransform _canvasRect;

        static readonly Color SelectedTypeColor = new Color(0.25f, 0.45f, 0.85f, 0.95f);
        static readonly Color FlyoutEntryIdle   = new Color(0.18f, 0.18f, 0.22f, 0.95f);
        static readonly Color FlyoutEntryActive = new Color(0.35f, 0.55f, 0.95f, 0.95f);

        void Start()
        {
            if (buildManager == null) buildManager = FindAnyObjectByType<BuildManager>();
            if (buildManager == null)
            {
                Debug.unityLogger.LogError(TAG, "No BuildManager in scene; toolbar cannot wire up.");
                return;
            }
            BuildToolbar();
            buildManager.CurrentShapeChanged    += OnCurrentShapeChanged;
            buildManager.CurrentMaterialChanged += OnCurrentMaterialChanged;
            buildManager.CurrentToolChanged     += OnCurrentToolChanged;
            buildManager.ConstructChanged       += RefreshStatLabels;
            UpdateButtonStates();
            RefreshAllSwatches();
            RefreshStatLabels();
            RefreshSelectedStats();
            Debug.unityLogger.Log(TAG,
                $"Build toolbar created with {_shapeButtons?.Length ?? 0} shape entry(ies) + Delete.");
        }

        void OnDestroy()
        {
            if (buildManager != null)
            {
                buildManager.CurrentShapeChanged    -= OnCurrentShapeChanged;
                buildManager.CurrentMaterialChanged -= OnCurrentMaterialChanged;
                buildManager.CurrentToolChanged     -= OnCurrentToolChanged;
                buildManager.ConstructChanged       -= RefreshStatLabels;
            }
        }

        void Update()
        {
            // Keyboard shortcuts. 1..9 → SetCurrentShape, Q/W/E/R →
            // SetCurrentMaterial for the active shape, M → toggle flyout
            // for the active shape, Esc → close flyout.
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            // Digits 1..9 select shape (without modifier). With Shift
            // held, the same digits select material for the active
            // shape. Letter keys are avoided to keep R/T (rotation)
            // and any future Build-map bindings free of conflicts.
            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            Key[] digitKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
                                Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };

            if (!shift && _shapeButtons != null)
            {
                int max = Mathf.Min(_shapeButtons.Length, digitKeys.Length);
                for (int i = 0; i < max; i++)
                {
                    if (kb[digitKeys[i]].wasPressedThisFrame)
                    {
                        buildManager.SetCurrentShape(i);
                        break;
                    }
                }
            }
            else if (shift)
            {
                int matCount = buildManager.Materials != null ? buildManager.Materials.Count : 0;
                int matMax = Mathf.Min(digitKeys.Length, matCount);
                for (int i = 0; i < matMax; i++)
                {
                    if (kb[digitKeys[i]].wasPressedThisFrame)
                    {
                        buildManager.SetCurrentMaterial(i);
                        if (_flyout != null && _flyout.activeSelf) RefreshFlyoutEntryHighlights();
                        break;
                    }
                }
            }

            if (kb.mKey.wasPressedThisFrame)
            {
                if (_flyout != null && _flyout.activeSelf && _flyoutOwnerShape == buildManager.CurrentShapeIndex && _flyoutPinned)
                    HideFlyout();
                else
                    OpenFlyoutForShape(buildManager.CurrentShapeIndex, pin: true);
            }

            if (kb.escapeKey.wasPressedThisFrame && _flyout != null && _flyout.activeSelf)
                HideFlyout();
        }

        void BuildToolbar()
        {
            UIStyle.EnsureEventSystem();
            // Sit just under the persistent corner-button canvas (sortingOrder
            // 100). Both share the screen but never overlap visually.
            Canvas canvas = UIStyle.BuildScreenSpaceCanvas("BuildToolbarCanvas", sortingOrder: 90);
            RectTransform root = (RectTransform)canvas.transform;
            _canvasRect = root;

            // Top-left rotation hint.
            Text hint = UIStyle.BuildLabel(root, hintText, fontSize: hintFontSize);
            hint.alignment = TextAnchor.UpperLeft;
            RectTransform hrt = (RectTransform)hint.transform;
            hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(0f, 1f);
            hrt.anchoredPosition = hintAnchoredPosition;
            hrt.sizeDelta = hintSize;

            // Top-center transient message label.
            _floatingMessage = UIStyle.BuildLabel(root, string.Empty, fontSize: floatingFontSize);
            _floatingMessage.alignment = TextAnchor.UpperCenter;
            _floatingMessage.color = new Color(floatingColor.r, floatingColor.g, floatingColor.b, 0f);
            RectTransform mrt = (RectTransform)_floatingMessage.transform;
            mrt.anchorMin = mrt.anchorMax = mrt.pivot = new Vector2(0.5f, 1f);
            mrt.anchoredPosition = floatingAnchoredPosition;
            mrt.sizeDelta = floatingSize;

            // ---- Shape buttons + Delete button ----
            ShapeRegistry shapes = buildManager.Shapes;
            int count = shapes != null ? shapes.Count : 0;
            _shapeButtons = new Button[count];
            _shapeBackgrounds = new Image[count];
            _shapeSwatches = new Image[count];

            int totalSlots = count + 1;
            float totalWidth = totalSlots * buttonSize.x + Mathf.Max(0, totalSlots - 1) * spacing;
            float startX = -totalWidth / 2f + buttonSize.x / 2f;

            for (int i = 0; i < count; i++)
            {
                int idx = i; // capture for closure
                ShapeDefinition def = shapes.Get(i);
                string label = def != null ? def.displayName : $"Shape #{i}";

                (Button btn, Text _) = UIStyle.BuildLabeledButton(root, label, buttonSize, fontSize);
                RectTransform rt = (RectTransform)btn.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(startX + i * (buttonSize.x + spacing), bottomMargin);

                btn.onClick.AddListener(() => OnShapeButtonClicked(idx));

                // Hover triggers (peek) and right-click (open flyout).
                AddPointerHandlers(btn.gameObject, idx);

                // Corner swatch — small image anchored to the button's
                // top-right showing the material currently armed for this
                // shape. RefreshAllSwatches paints them after Start.
                Image swatch = BuildCornerSwatch(rt);
                _shapeSwatches[i] = swatch;

                _shapeButtons[i] = btn;
                _shapeBackgrounds[i] = btn.GetComponent<Image>();
            }

            // Delete button — last slot.
            (Button delBtn, Text _ignored) = UIStyle.BuildLabeledButton(root, deleteButtonLabel, buttonSize, fontSize);
            RectTransform delRT = (RectTransform)delBtn.transform;
            delRT.anchorMin = delRT.anchorMax = delRT.pivot = new Vector2(0.5f, 0f);
            delRT.anchoredPosition = new Vector2(startX + count * (buttonSize.x + spacing), bottomMargin);
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

        // ---------- Shape button interactions ----------

        // Click on a shape: if it's already the active shape, toggle the
        // flyout (pin); if not, make it active (and close any open flyout).
        void OnShapeButtonClicked(int shapeIndex)
        {
            if (shapeIndex == buildManager.CurrentShapeIndex && buildManager.CurrentTool == BuildTool.Place)
            {
                if (_flyout != null && _flyout.activeSelf && _flyoutOwnerShape == shapeIndex && _flyoutPinned)
                    HideFlyout();
                else
                    OpenFlyoutForShape(shapeIndex, pin: true);
                return;
            }
            buildManager.SetCurrentShape(shapeIndex);
            HideFlyout();
        }

        // Pointer enter / exit / right-click on a shape button. Wired
        // via EventTrigger to avoid hand-rolling raycasts.
        void AddPointerHandlers(GameObject buttonObject, int shapeIndex)
        {
            EventTrigger trigger = buttonObject.AddComponent<EventTrigger>();

            EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => OnShapeButtonHoverEnter(shapeIndex));
            trigger.triggers.Add(enter);

            EventTrigger.Entry exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => OnShapeButtonHoverExit(shapeIndex));
            trigger.triggers.Add(exit);

            EventTrigger.Entry click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            click.callback.AddListener(data =>
            {
                PointerEventData ped = data as PointerEventData;
                if (ped == null) return;
                if (ped.button == PointerEventData.InputButton.Right)
                    OpenFlyoutForShape(shapeIndex, pin: true);
            });
            trigger.triggers.Add(click);
        }

        void OnShapeButtonHoverEnter(int shapeIndex)
        {
            if (_peekRoutine != null) StopCoroutine(_peekRoutine);
            _peekRoutine = StartCoroutine(PeekAfterDelay(shapeIndex));
        }

        void OnShapeButtonHoverExit(int shapeIndex)
        {
            if (_peekRoutine != null) { StopCoroutine(_peekRoutine); _peekRoutine = null; }
            // If the flyout was just peeking (not pinned), close it. A
            // pinned flyout (opened by click) stays open until the user
            // clicks outside / presses Escape / picks a material.
            if (_flyout != null && _flyout.activeSelf && _flyoutOwnerShape == shapeIndex && !_flyoutPinned)
            {
                // But — don't close if the cursor moved INTO the flyout
                // itself; check via the EventSystem's current pointer.
                if (!IsPointerOverFlyout()) HideFlyout();
            }
        }

        IEnumerator PeekAfterDelay(int shapeIndex)
        {
            yield return new WaitForSeconds(hoverPeekDelay);
            // Only peek if the flyout isn't already pinned for this shape.
            if (_flyout != null && _flyout.activeSelf && _flyoutOwnerShape == shapeIndex && _flyoutPinned)
                yield break;
            OpenFlyoutForShape(shapeIndex, pin: false);
            _peekRoutine = null;
        }

        // ---------- Flyout construction & lifecycle ----------

        void BuildFlyout(RectTransform canvas)
        {
            MaterialRegistry materials = buildManager.Materials;
            int count = materials != null ? materials.Count : 0;
            _flyoutButtons = new Button[count];
            _flyoutBackgrounds = new Image[count];

            _flyout = new GameObject("MaterialFlyout", typeof(RectTransform), typeof(CanvasGroup));
            RectTransform frt = (RectTransform)_flyout.transform;
            frt.SetParent(canvas, false);
            frt.anchorMin = frt.anchorMax = frt.pivot = new Vector2(0.5f, 0f);
            frt.sizeDelta = new Vector2(flyoutEntrySize.x, count * flyoutEntrySize.y + Mathf.Max(0, count - 1) * flyoutEntrySpacing);

            _flyoutGroup = _flyout.GetComponent<CanvasGroup>();
            _flyoutGroup.interactable = true;
            _flyoutGroup.blocksRaycasts = true;

            for (int i = 0; i < count; i++)
            {
                int idx = i;
                MaterialDefinition mdef = materials.Get(i);
                string title = mdef != null ? mdef.displayName : $"Material #{i}";
                string statLine = mdef != null
                    ? $"HP {mdef.healthPoints:F0}  ·  AV {mdef.armourValue:F0}  ·  M {mdef.mass:F1}"
                    : "—";

                (Button btn, Text label) = UIStyle.BuildLabeledButton(frt, $"{title}\n<size={Mathf.Max(10, fontSize - 8)}>{statLine}</size>", flyoutEntrySize, fontSize);
                label.supportRichText = true;
                label.alignment = TextAnchor.MiddleLeft;
                RectTransform brt = (RectTransform)btn.transform;
                brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0f);
                // Stack bottom-up so the topmost entry is the one closest
                // to the shape button below.
                float y = i * (flyoutEntrySize.y + flyoutEntrySpacing);
                brt.anchoredPosition = new Vector2(0f, y);

                // Coloured swatch on the left side of each entry.
                Image swatch = BuildEntrySwatch(brt, mdef != null ? mdef.SwatchColor : Color.gray);

                btn.onClick.AddListener(() => OnFlyoutEntryClicked(idx));
                _flyoutButtons[i] = btn;
                _flyoutBackgrounds[i] = btn.GetComponent<Image>();
            }

            _flyout.SetActive(false);
        }

        void OnFlyoutEntryClicked(int materialIndex)
        {
            if (_flyoutOwnerShape < 0) return;
            buildManager.SetMaterialForShape(_flyoutOwnerShape, materialIndex);
            // Selecting a material implies the player wants this shape
            // armed too — switch the active shape if it isn't already.
            if (buildManager.CurrentShapeIndex != _flyoutOwnerShape)
                buildManager.SetCurrentShape(_flyoutOwnerShape);
            HideFlyout();
        }

        void OpenFlyoutForShape(int shapeIndex, bool pin)
        {
            if (_flyout == null) return;
            if (buildManager.Shapes == null) return;
            if (shapeIndex < 0 || shapeIndex >= buildManager.Shapes.Count) return;
            if (_shapeButtons == null || shapeIndex >= _shapeButtons.Length) return;

            // Capture "is this the same shape as the one currently
            // pinned?" BEFORE overwriting _flyoutOwnerShape, otherwise
            // the equality check is trivially true and a pinned flyout
            // for shape A would stay "pinned" when peek-opened for B.
            bool sameShape = _flyoutOwnerShape == shapeIndex;
            _flyoutOwnerShape = shapeIndex;
            _flyoutPinned = pin || (sameShape && _flyoutPinned);

            // Position the flyout above the relevant shape button.
            RectTransform shapeRT = (RectTransform)_shapeButtons[shapeIndex].transform;
            RectTransform frt = (RectTransform)_flyout.transform;
            frt.anchoredPosition = new Vector2(
                shapeRT.anchoredPosition.x,
                bottomMargin + buttonSize.y / 2f + flyoutBottomGap);

            _flyout.SetActive(true);
            _flyoutGroup.alpha = pin ? 1f : peekAlpha;
            _flyoutGroup.blocksRaycasts = pin; // peek is non-interactive
            RefreshFlyoutEntryHighlights();
        }

        void HideFlyout()
        {
            if (_flyout == null || !_flyout.activeSelf) return;
            _flyout.SetActive(false);
            _flyoutOwnerShape = -1;
            _flyoutPinned = false;
        }

        bool IsPointerOverFlyout()
        {
            if (EventSystem.current == null) return false;
            // Walk the current hovered object up to the flyout.
            GameObject hovered = EventSystem.current.currentSelectedGameObject;
            // Use raycast result instead — currentSelectedGameObject is not
            // updated for hover-only states.
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

        void RefreshFlyoutEntryHighlights()
        {
            if (_flyoutBackgrounds == null) return;
            int activeMat = _flyoutOwnerShape >= 0
                ? buildManager.GetMaterialForShape(_flyoutOwnerShape)
                : -1;
            for (int i = 0; i < _flyoutBackgrounds.Length; i++)
            {
                if (_flyoutBackgrounds[i] == null) continue;
                _flyoutBackgrounds[i].color = (i == activeMat) ? FlyoutEntryActive : FlyoutEntryIdle;
            }
        }

        // ---------- Swatch builders ----------

        Image BuildCornerSwatch(RectTransform parent)
        {
            GameObject go = new GameObject("Swatch", typeof(RectTransform), typeof(Image));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-4f, -4f);
            rt.sizeDelta = swatchSize;
            Image img = go.GetComponent<Image>();
            img.color = Color.gray;
            img.raycastTarget = false; // don't block clicks on the underlying button
            return img;
        }

        Image BuildEntrySwatch(RectTransform parent, Color color)
        {
            GameObject go = new GameObject("EntrySwatch", typeof(RectTransform), typeof(Image));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(8f, 0f);
            rt.sizeDelta = new Vector2(28f, 28f);
            Image img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        // ---------- Event-driven refreshes ----------

        void OnCurrentShapeChanged(int shapeIndex)
        {
            UpdateButtonStates();
            RefreshSelectedStats();
            // Closing the flyout on shape change avoids stale "owner" state.
            HideFlyout();
        }

        void OnCurrentMaterialChanged(int shapeIndex, int materialIndex)
        {
            RefreshSwatchFor(shapeIndex);
            // The "Selected" line follows the active shape's material.
            if (shapeIndex == buildManager.CurrentShapeIndex) RefreshSelectedStats();
            if (_flyout != null && _flyout.activeSelf && _flyoutOwnerShape == shapeIndex)
                RefreshFlyoutEntryHighlights();
        }

        void OnCurrentToolChanged(BuildTool tool)
        {
            UpdateButtonStates();
            RefreshSelectedStats();
            if (tool == BuildTool.Delete) HideFlyout();
        }

        void UpdateButtonStates()
        {
            if (buildManager == null) return;
            bool deleteActive = buildManager.CurrentTool == BuildTool.Delete;
            int activeIdx = buildManager.CurrentShapeIndex;

            if (_shapeBackgrounds != null)
            {
                for (int i = 0; i < _shapeBackgrounds.Length; i++)
                {
                    if (_shapeBackgrounds[i] == null) continue;
                    _shapeBackgrounds[i].color = (!deleteActive && i == activeIdx)
                        ? SelectedTypeColor
                        : UIStyle.BackgroundIdle;
                }
            }
            if (_deleteBackground != null)
                _deleteBackground.color = deleteActive ? deleteSelectedColor : UIStyle.BackgroundIdle;
        }

        // Refresh the corner swatch on every shape button to reflect
        // each shape's currently-armed material.
        void RefreshAllSwatches()
        {
            if (_shapeSwatches == null) return;
            for (int i = 0; i < _shapeSwatches.Length; i++) RefreshSwatchFor(i);
        }

        void RefreshSwatchFor(int shapeIndex)
        {
            if (_shapeSwatches == null || shapeIndex < 0 || shapeIndex >= _shapeSwatches.Length) return;
            if (_shapeSwatches[shapeIndex] == null) return;
            MaterialRegistry mats = buildManager.Materials;
            if (mats == null) return;
            int mIdx = buildManager.GetMaterialForShape(shapeIndex);
            MaterialDefinition mdef = mats.Get(mIdx);
            _shapeSwatches[shapeIndex].color = mdef != null ? mdef.SwatchColor : Color.gray;
        }

        // ---------- Bottom-left stat readouts ----------

        void RefreshSelectedStats()
        {
            if (_selectedStatsLabel == null || buildManager == null) return;

            if (buildManager.CurrentTool == BuildTool.Delete)
            {
                _selectedStatsLabel.text = deleteToolLabelText;
                return;
            }

            ShapeRegistry shapes = buildManager.Shapes;
            MaterialRegistry mats = buildManager.Materials;
            ShapeDefinition shape = shapes != null ? shapes.Get(buildManager.CurrentShapeIndex) : null;
            MaterialDefinition mat = mats  != null ? mats.Get(buildManager.CurrentMaterialIndex) : null;

            string sname = shape != null && !string.IsNullOrEmpty(shape.displayName) ? shape.displayName : "Shape";
            string mname = mat   != null && !string.IsNullOrEmpty(mat.displayName)   ? mat.displayName   : "—";

            float hp   = mat != null ? mat.healthPoints : 0f;
            float av   = mat != null ? mat.armourValue  : 0f;
            float mass = mat != null ? mat.mass         : 0f;
            _selectedStatsLabel.text =
                $"Selected: {sname} · Material {mname}\nHP: {hp:F0}    AV: {av:F0}    Mass: {mass:F1}";
        }

        void RefreshStatLabels()
        {
            if (buildManager == null) return;
            float mass = buildManager.ComputeCurrentMass();
            float hp   = buildManager.ComputeCurrentHealthPoints();
            if (_massLabel != null)
                _massLabel.text = $"Mass: {mass:F1} / {buildManager.MassLimit:F0}";
            if (_hpLabel != null)
                _hpLabel.text = $"HP: {hp:F0}";
        }

        // ---------- Floating message ----------

        public void ShowFloatingMessage(string message, float duration = 5f)
        {
            if (_floatingMessage == null) return;
            _floatingMessage.text = message;
            _floatingMessage.color = new Color(floatingColor.r, floatingColor.g, floatingColor.b, 1f);
            if (_floatingRoutine != null) StopCoroutine(_floatingRoutine);
            _floatingRoutine = StartCoroutine(FadeFloatingMessage(duration));
        }

        IEnumerator FadeFloatingMessage(float duration)
        {
            float elapsed = 0f;
            Color start = _floatingMessage.color;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                _floatingMessage.color = new Color(start.r, start.g, start.b, 1f - t);
                yield return null;
            }
            _floatingMessage.color = new Color(start.r, start.g, start.b, 0f);
            _floatingRoutine = null;
        }
    }
}
