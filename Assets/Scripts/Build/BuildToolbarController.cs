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

        [Header("Weapons button (toolbar)")]
        [SerializeField] string weaponsButtonLabel = "Weapons";

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
            // Keyboard shortcuts. 1..9 → SetCurrentShape, Shift+1..4 →
            // SetCurrentMaterial for the active shape, M → toggle
            // flyout for the active shape, Esc → close flyout.
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            // Pause menu owns all keyboard input while open. PauseMenu
            // runs at DefaultExecutionOrder(-1000), so by the time we
            // reach here it has already toggled itself and set
            // EscConsumedThisFrame for any pending ESC.
            if (PauseMenu.Instance != null && PauseMenu.Instance.IsOpen) return;

            // Digits 1..9 select shape (without modifier). With Shift
            // held, the same digits select material for the active
            // shape. Letter keys are avoided to keep R/T (rotation)
            // and any future Build-map bindings free of conflicts.
            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            Key[] digitKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
                                Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };

            if (!shift && _shapeButtons != null)
            {
                // Digits arm armour shapes by toolbar slot order.
                // Weapons are reachable only through the Weapons
                // button + flyout, so they're skipped here.
                int max = Mathf.Min(_shapeButtons.Length, digitKeys.Length);
                for (int i = 0; i < max; i++)
                {
                    if (_shapeButtons[i] == null) continue; // weapon — skip
                    if (kb[digitKeys[i]].wasPressedThisFrame)
                    {
                        buildManager.SetCurrentShape(i);
                        break;
                    }
                }
            }
            else if (shift)
            {
                // Shift+digit only meaningful while an armour shape is
                // active — weapons have no material choice.
                ShapeDefinition activeShape = buildManager.Shapes != null
                    ? buildManager.Shapes.Get(buildManager.CurrentShapeIndex)
                    : null;
                bool weaponActive = activeShape != null && activeShape.IsWeapon;
                if (!weaponActive)
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
            }

            if (kb.mKey.wasPressedThisFrame)
            {
                // For weapons, M toggles the weapons flyout; for
                // armour shapes it toggles the per-shape material
                // flyout (existing behaviour).
                ShapeDefinition activeShape = buildManager.Shapes != null
                    ? buildManager.Shapes.Get(buildManager.CurrentShapeIndex)
                    : null;
                if (activeShape != null && activeShape.IsWeapon)
                {
                    if (_weaponsFlyout != null && _weaponsFlyout.activeSelf && _weaponsFlyoutPinned)
                        HideWeaponsFlyout();
                    else
                        OpenWeaponsFlyout(pin: true);
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
                if (_weaponsFlyout != null && _weaponsFlyout.activeSelf) HideWeaponsFlyout();
            }
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

            // ---- Shape buttons + Weapons + Delete ----
            //
            // Toolbar slots, left to right:
            //   • One button per ARMOUR shape (each with a material
            //     flyout via right-click / re-click / hover-peek).
            //   • One "Weapons" button for ALL weapon shapes
            //     collectively, with a dedicated weapons flyout. Only
            //     built when the registry contains at least one weapon.
            //   • Delete tool button.
            //
            // _shapeButtons/_shapeSwatches/_shapeBackgrounds remain
            // indexed by ShapeRegistry index for simplicity — entries
            // for weapon shapes are left null.
            ShapeRegistry shapes = buildManager.Shapes;
            int totalShapes = shapes != null ? shapes.Count : 0;
            _shapeButtons = new Button[totalShapes];
            _shapeBackgrounds = new Image[totalShapes];
            _shapeSwatches = new Image[totalShapes];

            List<int> armourIndices = new List<int>();
            List<int> weaponIndices = new List<int>();
            for (int i = 0; i < totalShapes; i++)
            {
                ShapeDefinition def = shapes.Get(i);
                if (def == null) continue;
                if (def.IsWeapon) weaponIndices.Add(i);
                else              armourIndices.Add(i);
            }
            _weaponShapeIndices = weaponIndices.ToArray();
            if (_weaponShapeIndices.Length > 0) _lastArmedWeaponIndex = _weaponShapeIndices[0];

            bool hasWeapons = _weaponShapeIndices.Length > 0;
            int slotCount = armourIndices.Count + (hasWeapons ? 1 : 0) + 1; // +1 for Delete
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

            // Weapons button + flyout (one per category, not per weapon).
            if (hasWeapons)
            {
                (Button wBtn, Text _) = UIStyle.BuildLabeledButton(root, weaponsButtonLabel, buttonSize, fontSize);
                RectTransform wrt = (RectTransform)wBtn.transform;
                wrt.anchorMin = wrt.anchorMax = wrt.pivot = new Vector2(0.5f, 0f);
                wrt.anchoredPosition = new Vector2(startX + slot * (buttonSize.x + spacing), bottomMargin);

                wBtn.onClick.AddListener(OnWeaponsButtonClicked);
                AddWeaponsPointerHandlers(wBtn.gameObject);

                Image wSwatch = BuildCornerSwatch(wrt);
                _weaponsSwatch = wSwatch;

                _weaponsButton = wBtn;
                _weaponsBackground = wBtn.GetComponent<Image>();
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
            if (hasWeapons) BuildWeaponsFlyout(root);
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
            // Weapons don't use the material flyout — they have a
            // dedicated weapons flyout and a coupled material instead.
            if (_shapeButtons[shapeIndex] == null) return;
            ShapeDefinition def = buildManager.Shapes.Get(shapeIndex);
            if (def == null || def.IsWeapon) return;

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

        // ---------- Weapons button + flyout ----------

        void OnWeaponsButtonClicked()
        {
            // Toggle the weapons flyout. Unlike the per-shape buttons,
            // the Weapons button doesn't double as a "switch shape"
            // shortcut — picking a weapon happens inside the flyout
            // so the player can see what's available.
            if (_weaponsFlyout != null && _weaponsFlyout.activeSelf && _weaponsFlyoutPinned)
                HideWeaponsFlyout();
            else
                OpenWeaponsFlyout(pin: true);
        }

        void AddWeaponsPointerHandlers(GameObject buttonObject)
        {
            EventTrigger trigger = buttonObject.AddComponent<EventTrigger>();

            EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => OnWeaponsButtonHoverEnter());
            trigger.triggers.Add(enter);

            EventTrigger.Entry exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => OnWeaponsButtonHoverExit());
            trigger.triggers.Add(exit);

            EventTrigger.Entry click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            click.callback.AddListener(data =>
            {
                PointerEventData ped = data as PointerEventData;
                if (ped == null) return;
                if (ped.button == PointerEventData.InputButton.Right)
                    OpenWeaponsFlyout(pin: true);
            });
            trigger.triggers.Add(click);
        }

        void OnWeaponsButtonHoverEnter()
        {
            if (_weaponsPeekRoutine != null) StopCoroutine(_weaponsPeekRoutine);
            _weaponsPeekRoutine = StartCoroutine(WeaponsPeekAfterDelay());
        }

        void OnWeaponsButtonHoverExit()
        {
            if (_weaponsPeekRoutine != null)
            {
                StopCoroutine(_weaponsPeekRoutine);
                _weaponsPeekRoutine = null;
            }
            if (_weaponsFlyout != null && _weaponsFlyout.activeSelf && !_weaponsFlyoutPinned)
            {
                if (!IsPointerOverWeaponsFlyout()) HideWeaponsFlyout();
            }
        }

        IEnumerator WeaponsPeekAfterDelay()
        {
            yield return new WaitForSeconds(hoverPeekDelay);
            if (_weaponsFlyout != null && _weaponsFlyout.activeSelf && _weaponsFlyoutPinned)
                yield break;
            OpenWeaponsFlyout(pin: false);
            _weaponsPeekRoutine = null;
        }

        void BuildWeaponsFlyout(RectTransform canvas)
        {
            int count = _weaponShapeIndices != null ? _weaponShapeIndices.Length : 0;
            _weaponsFlyoutButtons = new Button[count];
            _weaponsFlyoutBackgrounds = new Image[count];

            _weaponsFlyout = new GameObject("WeaponsFlyout",
                typeof(RectTransform), typeof(CanvasGroup));
            RectTransform frt = (RectTransform)_weaponsFlyout.transform;
            frt.SetParent(canvas, false);
            frt.anchorMin = frt.anchorMax = frt.pivot = new Vector2(0.5f, 0f);
            frt.sizeDelta = new Vector2(
                flyoutEntrySize.x,
                count * flyoutEntrySize.y + Mathf.Max(0, count - 1) * flyoutEntrySpacing);

            _weaponsFlyoutGroup = _weaponsFlyout.GetComponent<CanvasGroup>();
            _weaponsFlyoutGroup.interactable = true;
            _weaponsFlyoutGroup.blocksRaycasts = true;

            for (int e = 0; e < count; e++)
            {
                int shapeIndex = _weaponShapeIndices[e];
                ShapeDefinition shape = buildManager.Shapes.Get(shapeIndex);
                MaterialDefinition wmat = shape != null ? shape.weaponMaterial : null;
                string title = shape != null ? shape.displayName : $"Weapon #{shapeIndex}";
                string statLine = wmat != null
                    ? $"HP {wmat.healthPoints:F0}  ·  AV {wmat.armourValue:F0}  ·  M {wmat.mass:F1}"
                    : "—";

                (Button btn, Text label) = UIStyle.BuildLabeledButton(
                    frt,
                    $"{title}\n<size={Mathf.Max(10, fontSize - 8)}>{statLine}</size>",
                    flyoutEntrySize, fontSize);
                label.supportRichText = true;
                label.alignment = TextAnchor.MiddleLeft;
                RectTransform brt = (RectTransform)btn.transform;
                brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0f);
                float y = e * (flyoutEntrySize.y + flyoutEntrySpacing);
                brt.anchoredPosition = new Vector2(0f, y);

                BuildEntrySwatch(brt, wmat != null ? wmat.SwatchColor : Color.gray);

                int captured = shapeIndex;
                btn.onClick.AddListener(() => OnWeaponsFlyoutEntryClicked(captured));
                _weaponsFlyoutButtons[e] = btn;
                _weaponsFlyoutBackgrounds[e] = btn.GetComponent<Image>();
            }

            _weaponsFlyout.SetActive(false);
        }

        void OnWeaponsFlyoutEntryClicked(int shapeIndex)
        {
            buildManager.SetCurrentShape(shapeIndex);
            _lastArmedWeaponIndex = shapeIndex;
            HideWeaponsFlyout();
        }

        void OpenWeaponsFlyout(bool pin)
        {
            if (_weaponsFlyout == null || _weaponsButton == null) return;

            // Opening one flyout closes the other so they never overlap.
            if (_flyout != null && _flyout.activeSelf) HideFlyout();

            RectTransform wbtnRT = (RectTransform)_weaponsButton.transform;
            RectTransform frt = (RectTransform)_weaponsFlyout.transform;
            frt.anchoredPosition = new Vector2(
                wbtnRT.anchoredPosition.x,
                bottomMargin + buttonSize.y / 2f + flyoutBottomGap);

            _weaponsFlyout.SetActive(true);
            _weaponsFlyoutGroup.alpha = pin ? 1f : peekAlpha;
            _weaponsFlyoutGroup.blocksRaycasts = pin;
            _weaponsFlyoutPinned = pin;
            RefreshWeaponsFlyoutEntryHighlights();
        }

        void HideWeaponsFlyout()
        {
            if (_weaponsFlyout == null || !_weaponsFlyout.activeSelf) return;
            _weaponsFlyout.SetActive(false);
            _weaponsFlyoutPinned = false;
        }

        bool IsPointerOverWeaponsFlyout()
        {
            if (EventSystem.current == null || _weaponsFlyout == null) return false;
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
                    if (t.gameObject == _weaponsFlyout) return true;
                    t = t.parent;
                }
            }
            return false;
        }

        void RefreshWeaponsFlyoutEntryHighlights()
        {
            if (_weaponsFlyoutBackgrounds == null || _weaponShapeIndices == null) return;
            int activeShape = buildManager.CurrentShapeIndex;
            for (int e = 0; e < _weaponsFlyoutBackgrounds.Length; e++)
            {
                if (_weaponsFlyoutBackgrounds[e] == null) continue;
                bool isActive = _weaponShapeIndices[e] == activeShape
                    && (buildManager.Shapes.Get(activeShape)?.IsWeapon ?? false);
                _weaponsFlyoutBackgrounds[e].color = isActive ? FlyoutEntryActive : FlyoutEntryIdle;
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
            RefreshWeaponsButtonSwatch();
            RefreshWeaponsFlyoutEntryHighlights();
            // Track last-armed weapon so the Weapons button swatch
            // keeps its colour when the player switches to an armour
            // shape and back.
            ShapeDefinition shape = buildManager.Shapes != null
                ? buildManager.Shapes.Get(shapeIndex) : null;
            if (shape != null && shape.IsWeapon) _lastArmedWeaponIndex = shapeIndex;
            // Closing the flyouts on shape change avoids stale state.
            HideFlyout();
            HideWeaponsFlyout();
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
            if (tool == BuildTool.Delete)
            {
                HideFlyout();
                HideWeaponsFlyout();
            }
        }

        void UpdateButtonStates()
        {
            if (buildManager == null) return;
            bool deleteActive = buildManager.CurrentTool == BuildTool.Delete;
            int activeIdx = buildManager.CurrentShapeIndex;

            ShapeDefinition activeShape = buildManager.Shapes != null
                ? buildManager.Shapes.Get(activeIdx) : null;
            bool weaponActive = !deleteActive && activeShape != null && activeShape.IsWeapon;

            if (_shapeBackgrounds != null)
            {
                for (int i = 0; i < _shapeBackgrounds.Length; i++)
                {
                    if (_shapeBackgrounds[i] == null) continue;
                    _shapeBackgrounds[i].color = (!deleteActive && !weaponActive && i == activeIdx)
                        ? SelectedTypeColor
                        : UIStyle.BackgroundIdle;
                }
            }
            // The Weapons button gets the same selected highlight as
            // armour shapes, but switches on whenever ANY weapon is the
            // active shape (rather than a specific shape index).
            if (_weaponsBackground != null)
                _weaponsBackground.color = weaponActive ? SelectedTypeColor : UIStyle.BackgroundIdle;
            if (_deleteBackground != null)
                _deleteBackground.color = deleteActive ? deleteSelectedColor : UIStyle.BackgroundIdle;
        }

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
            // ResolveMaterial picks coupled weaponMaterial for weapons;
            // registry-indexed MaterialDefinition for armour. Single
            // call site keeps the format string symmetric.
            MaterialDefinition mat = shape != null
                ? shape.ResolveMaterial(buildManager.CurrentMaterialIndex, mats)
                : null;

            string sname = shape != null && !string.IsNullOrEmpty(shape.displayName) ? shape.displayName : "Shape";
            float hp   = mat != null ? mat.healthPoints : 0f;
            float av   = mat != null ? mat.armourValue  : 0f;
            float mass = mat != null ? mat.mass         : 0f;

            if (shape != null && shape.IsWeapon)
            {
                _selectedStatsLabel.text =
                    $"Selected: {sname} (Weapon)\nHP: {hp:F0}    AV: {av:F0}    Mass: {mass:F1}";
            }
            else
            {
                string mname = mat != null && !string.IsNullOrEmpty(mat.displayName) ? mat.displayName : "—";
                _selectedStatsLabel.text =
                    $"Selected: {sname} · Material {mname}\nHP: {hp:F0}    AV: {av:F0}    Mass: {mass:F1}";
            }
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
