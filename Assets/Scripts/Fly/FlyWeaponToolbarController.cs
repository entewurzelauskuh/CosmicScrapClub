using CubeFly.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CubeFly.Fly
{
    // Bottom-of-screen weapon toolbar in the Fly scene. One button
    // per distinct weapon type on the construct, with a thin reload
    // progress bar above each button. Active type is highlighted
    // with the same blue tint BuildToolbarController uses for active
    // shape selections.
    //
    // The toolbar is purely visual; selection input lives in
    // FlyShootingController. We subscribe to its TypesChanged and
    // SelectedChanged events and reflect state via per-frame fill
    // updates on the reload bars.
    //
    // Hidden entirely when the construct has no weapons.
    public class FlyWeaponToolbarController : MonoBehaviour
    {
        [SerializeField] FlyShootingController shootingController;

        Vector2 buttonSize = new Vector2(64f, 64f);
        [Header("Layout")]
        [SerializeField] float spacing = 16f;
        [SerializeField] float bottomMargin = 30f;
        [SerializeField] int fontSize = 22;

        Vector2 reloadBarSize = new Vector2(54f, 4f);
        // Reload-track backing: HudPanel hue at the original 0.6 alpha (spec alpha-preserve).
        Color reloadBarBackground = new Color(CscPalette.HudPanel.r, CscPalette.HudPanel.g, CscPalette.HudPanel.b, 0.6f);

        [Header("Corner swatch")]
        [SerializeField] Vector2 swatchSize = new Vector2(18f, 18f);

        Color deathMarkColor = CscPalette.Critical;
        [Header("Death response")]
        [Tooltip("Size of the partial-death corner mark, in UI units.")]
        [SerializeField] Vector2 deathMarkSize = new Vector2(16f, 16f);
        [Tooltip("Period of the partial-death mark's alpha pulse, in seconds.")]
        [SerializeField] float deathMarkPulseSeconds = 0.9f;
        [Tooltip("Minimum alpha at the dim end of the partial-death mark pulse.")]
        [SerializeField, Range(0f, 1f)] float deathMarkAlphaMin = 0.25f;

        const string TAG = "FlyWeaponToolbar";

        // Child of FlyHud.Instance.Root that owns every toolbar button +
        // reload bar. RebuildButtons destroys + re-creates this container's
        // children when TypesChanged fires; HideContainer / ShowContainer
        // toggle its active state when the construct has no weapons.
        RectTransform _container;
        Button[] _buttons;
        Image[] _buttonBackgrounds;
        Image[] _reloadBars;          // foreground fill (per-type colored)
        Image[] _swatches;
        Text[] _deathMarks;           // partial-death X mark, per button
        Outline[] _selectionOutlines; // ochre selection ring, per button
        CanvasGroup[] _canvasGroups;  // dead-dim group, per button

        void Start()
        {
            if (shootingController == null) shootingController = FindAnyObjectByType<FlyShootingController>();
            BuildContainer();
            if (shootingController == null)
            {
                Debug.unityLogger.LogWarning(TAG, "No FlyShootingController in scene; toolbar will stay hidden.");
                HideContainer();
                return;
            }

            shootingController.TypesChanged += RebuildButtons;

            // FlyController.Start may have already called RegisterWeapons
            // before us — query current state.
            RebuildButtons();
        }

        void OnDestroy()
        {
            if (shootingController != null)
            {
                shootingController.TypesChanged -= RebuildButtons;
            }
        }

        void Update()
        {
            if (shootingController == null || !shootingController.HasWeapons) return;
            if (_reloadBars == null) return;
            for (int i = 0; i < _reloadBars.Length; i++)
            {
                if (_reloadBars[i] == null) continue;
                // Width-based fill: foreground sizeDelta.x = ready * full
                // width. Left-anchored pivot (see BuildReloadRect) keeps
                // the left edge fixed so the bar shrinks from the right.
                RectTransform fgRT = (RectTransform)_reloadBars[i].transform;
                fgRT.sizeDelta = new Vector2(
                    reloadBarSize.x * shootingController.Types[i].ReadyFraction,
                    reloadBarSize.y);
            }
            RefreshWeaponStates();
        }

        // ---------- UI construction ----------

        void BuildContainer()
        {
            GameObject go = new GameObject("FlyWeaponToolbar", typeof(RectTransform));
            go.transform.SetParent(FlyHud.Instance.Root, false);
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) go.layer = uiLayer;
            _container = (RectTransform)go.transform;
            // Stretch to fill the parent canvas — the per-button anchored
            // positions (bottom-centre pivot, `bottomMargin` offset) target
            // the canvas's bottom edge, so the container needs to match the
            // canvas's full rect.
            _container.anchorMin = Vector2.zero;
            _container.anchorMax = Vector2.one;
            _container.offsetMin = Vector2.zero;
            _container.offsetMax = Vector2.zero;
            HideContainer();
        }

        void HideContainer()
        {
            if (_container != null) _container.gameObject.SetActive(false);
        }

        void ShowContainer()
        {
            if (_container != null) _container.gameObject.SetActive(true);
        }

        // Clear any existing buttons and rebuild from scratch. Called
        // when TypesChanged fires (and once during Start).
        void RebuildButtons()
        {
            if (shootingController == null || _container == null) return;

            // Destroy prior children.
            for (int i = _container.childCount - 1; i >= 0; i--)
                Destroy(_container.GetChild(i).gameObject);

            int count = shootingController.Types.Count;
            if (count == 0)
            {
                _buttons = null;
                _buttonBackgrounds = null;
                _reloadBars = null;
                _swatches = null;
                _deathMarks = null;
                _selectionOutlines = null;
                _canvasGroups = null;
                HideContainer();
                return;
            }
            ShowContainer();

            _buttons = new Button[count];
            _buttonBackgrounds = new Image[count];
            _reloadBars = new Image[count];
            _swatches = new Image[count];
            _deathMarks = new Text[count];
            _selectionOutlines = new Outline[count];
            _canvasGroups = new CanvasGroup[count];

            float totalWidth = count * buttonSize.x + Mathf.Max(0, count - 1) * spacing;
            float startX = -totalWidth / 2f + buttonSize.x / 2f;

            for (int i = 0; i < count; i++)
            {
                int idx = i; // capture for closure
                WeaponTypeGroup group = shootingController.Types[i];
                ShapeDefinition shape = group.Shape;
                string label = shape != null ? shape.displayName : $"Weapon #{i}";
                Color swatchColor = (shape != null && shape.coupledMaterial != null)
                    ? shape.coupledMaterial.SwatchColor
                    : Color.gray;

                // ---- Button ----
                (Button btn, Text lbl) = UIStyle.BuildLabeledButton(_container, label, buttonSize, fontSize);
                Sprite glyph = shape != null ? CscSprites.ForShape(shape.displayName, 0) : null;
                UIStyle.DecorateToolbarSlot(btn, lbl, glyph, (idx + 1).ToString(), string.Empty);   // caption suppressed; glyph identifies the weapon
                _selectionOutlines[i] = UIStyle.AddSelectionOutline(btn.gameObject);
                _canvasGroups[i] = btn.gameObject.AddComponent<CanvasGroup>();
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

                // ---- Corner swatch ----
                _swatches[i] = BuildSwatch(brt, swatchColor);

                // ---- Partial-death corner mark ----
                _deathMarks[i] = BuildDeathMark(brt);

                // ---- Reload bar along the slot's bottom edge ----
                float barY = bottomMargin + 3f;
                Vector2 barCenter = new Vector2(startX + i * (buttonSize.x + spacing), barY);

                BuildReloadRect(_container, "ReloadBarBg" + i, reloadBarSize, barCenter, reloadBarBackground, isFill: false);
                _reloadBars[i] = BuildReloadRect(_container, "ReloadBarFg" + i, reloadBarSize, barCenter, swatchColor, isFill: true);
            }

            RefreshWeaponStates();
            Debug.unityLogger.Log(TAG, $"Toolbar rebuilt with {count} weapon type(s).");
        }

        Image BuildSwatch(RectTransform buttonRT, Color color)
        {
            GameObject go = new GameObject("Swatch", typeof(RectTransform), typeof(Image));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(buttonRT, false);
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-4f, -4f);
            rt.sizeDelta = swatchSize;
            Image img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        Image BuildReloadRect(RectTransform parent, string name, Vector2 size, Vector2 anchoredPos, Color color, bool isFill)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            if (isFill)
            {
                // Left-anchored pivot so the per-frame Update can shrink the
                // bar's right edge by changing sizeDelta.x while the left
                // edge stays fixed. Image.Type.Filled with no sprite
                // assigned didn't reliably clip against fillAmount across
                // Unity 6.x patches — width-based fill is sprite-free and
                // unambiguous.
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = size;
                rt.anchoredPosition = new Vector2(anchoredPos.x - size.x / 2f, anchoredPos.y + size.y / 2f);
            }
            else
            {
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
                rt.sizeDelta = size;
                rt.anchoredPosition = anchoredPos;
            }
            Image img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

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
                // One Instances scan per group per frame — GetDeadState
                // derives both flags from a single AliveCount walk.
                shootingController.Types[i].GetDeadState(
                    out bool fullyDead, out bool partiallyDead);

                if (_buttons[i] != null)
                    _buttons[i].interactable = !fullyDead;

                if (_buttonBackgrounds[i] != null)
                    _buttonBackgrounds[i].color = UIStyle.BackgroundIdle;   // dark slot always
                if (_selectionOutlines != null && _selectionOutlines[i] != null)
                    _selectionOutlines[i].enabled = (i == selected && !fullyDead);
                if (_canvasGroups != null && _canvasGroups[i] != null)
                    _canvasGroups[i].alpha = fullyDead ? 0.4f : 1f;

                if (_deathMarks[i] != null)
                {
                    // Show the X mark for BOTH fully-dead and partially-
                    // dead types. Partial pulses (sine alpha) to flag a
                    // recoverable-looking state; fully-dead is static at
                    // full opacity to signal "this slot is gone."
                    bool showMark = fullyDead || partiallyDead;
                    _deathMarks[i].enabled = showMark;
                    if (showMark)
                    {
                        Color c = deathMarkColor;
                        if (partiallyDead)
                        {
                            // Slow sine alpha pulse between deathMarkAlphaMin
                            // and 1, driven by unscaled time so it keeps
                            // pulsing while the game is paused.
                            float period = Mathf.Max(0.01f, deathMarkPulseSeconds);
                            float phase = 0.5f + 0.5f *
                                Mathf.Sin(Time.unscaledTime * (2f * Mathf.PI / period));
                            c.a = Mathf.Lerp(deathMarkAlphaMin, 1f, phase);
                        }
                        // else: fullyDead — the mark's own colour alpha stays opaque;
                        // the slot's CanvasGroup separately dims the whole button to 40%.
                        _deathMarks[i].color = c;
                    }
                }
            }
        }
    }
}
