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

        [Header("Layout")]
        [SerializeField] Vector2 buttonSize = new Vector2(160f, 60f);
        [SerializeField] float spacing = 16f;
        [SerializeField] float bottomMargin = 30f;
        [SerializeField] int fontSize = 22;

        [Header("Reload bar")]
        [SerializeField] Vector2 reloadBarSize = new Vector2(140f, 6f);
        [SerializeField] float reloadBarGap = 6f;
        [SerializeField] Color reloadBarBackground = new Color(0f, 0f, 0f, 0.6f);

        [Header("Corner swatch")]
        [SerializeField] Vector2 swatchSize = new Vector2(18f, 18f);

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

        const string TAG = "FlyWeaponToolbar";

        Canvas _canvas;
        RectTransform _canvasRoot;
        Button[] _buttons;
        Image[] _buttonBackgrounds;
        Image[] _reloadBars;          // foreground fill (per-type colored)
        Image[] _swatches;
        Text[] _deathMarks;           // partial-death X mark, per button

        static readonly Color SelectedTypeColor = new Color(0.25f, 0.45f, 0.85f, 0.95f);

        void Start()
        {
            if (shootingController == null) shootingController = FindAnyObjectByType<FlyShootingController>();
            BuildCanvas();
            if (shootingController == null)
            {
                Debug.unityLogger.LogWarning(TAG, "No FlyShootingController in scene; toolbar will stay hidden.");
                HideCanvas();
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
                _reloadBars[i].fillAmount = shootingController.Types[i].ReadyFraction;
            }
            RefreshWeaponStates();
        }

        // ---------- UI construction ----------

        void BuildCanvas()
        {
            UIStyle.EnsureEventSystem();
            _canvas = UIStyle.BuildScreenSpaceCanvas("FlyWeaponToolbarCanvas", sortingOrder: 120);
            _canvasRoot = (RectTransform)_canvas.transform;
            HideCanvas();
        }

        void HideCanvas()
        {
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }

        void ShowCanvas()
        {
            if (_canvas != null) _canvas.gameObject.SetActive(true);
        }

        // Clear any existing buttons and rebuild from scratch. Called
        // when TypesChanged fires (and once during Start).
        void RebuildButtons()
        {
            if (shootingController == null || _canvasRoot == null) return;

            // Destroy prior children.
            for (int i = _canvasRoot.childCount - 1; i >= 0; i--)
                Destroy(_canvasRoot.GetChild(i).gameObject);

            int count = shootingController.Types.Count;
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
            ShowCanvas();

            _buttons = new Button[count];
            _buttonBackgrounds = new Image[count];
            _reloadBars = new Image[count];
            _swatches = new Image[count];
            _deathMarks = new Text[count];

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

                // ---- Corner swatch ----
                _swatches[i] = BuildSwatch(brt, swatchColor);

                // ---- Partial-death corner mark ----
                _deathMarks[i] = BuildDeathMark(brt);

                // ---- Reload bar (background + foreground fill) ----
                float barY = bottomMargin + buttonSize.y + reloadBarGap + reloadBarSize.y / 2f;
                Vector2 barCenter = new Vector2(startX + i * (buttonSize.x + spacing), barY);

                BuildReloadRect(_canvasRoot, "ReloadBarBg" + i, reloadBarSize, barCenter, reloadBarBackground, isFill: false);
                _reloadBars[i] = BuildReloadRect(_canvasRoot, "ReloadBarFg" + i, reloadBarSize, barCenter, swatchColor, isFill: true);
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
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
            Image img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            if (isFill)
            {
                img.type = Image.Type.Filled;
                img.fillMethod = Image.FillMethod.Horizontal;
                img.fillOrigin = (int)Image.OriginHorizontal.Left;
                img.fillAmount = 1f; // ready by default
            }
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
    }
}
