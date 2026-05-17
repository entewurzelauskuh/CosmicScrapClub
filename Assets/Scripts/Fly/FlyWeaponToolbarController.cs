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

        const string TAG = "FlyWeaponToolbar";

        Canvas _canvas;
        RectTransform _canvasRoot;
        Button[] _buttons;
        Image[] _buttonBackgrounds;
        Image[] _reloadBars;          // foreground fill (per-type colored)
        Image[] _swatches;

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

            shootingController.TypesChanged    += RebuildButtons;
            shootingController.SelectedChanged += OnSelectedChanged;

            // FlyController.Start may have already called RegisterWeapons
            // before us — query current state.
            RebuildButtons();
        }

        void OnDestroy()
        {
            if (shootingController != null)
            {
                shootingController.TypesChanged    -= RebuildButtons;
                shootingController.SelectedChanged -= OnSelectedChanged;
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
                HideCanvas();
                return;
            }
            ShowCanvas();

            _buttons = new Button[count];
            _buttonBackgrounds = new Image[count];
            _reloadBars = new Image[count];
            _swatches = new Image[count];

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
                // Optional click-to-select — wired but commented behaviour:
                // some users prefer keyboard + scroll only. Keep the
                // listener so the buttons feel responsive in any case.
                btn.onClick.AddListener(() => shootingController.SetSelected(idx));

                _buttons[i] = btn;
                _buttonBackgrounds[i] = btn.GetComponent<Image>();

                // ---- Corner swatch ----
                _swatches[i] = BuildSwatch(brt, swatchColor);

                // ---- Reload bar (background + foreground fill) ----
                float barY = bottomMargin + buttonSize.y + reloadBarGap + reloadBarSize.y / 2f;
                Vector2 barCenter = new Vector2(startX + i * (buttonSize.x + spacing), barY);

                BuildReloadRect(_canvasRoot, "ReloadBarBg" + i, reloadBarSize, barCenter, reloadBarBackground, isFill: false);
                _reloadBars[i] = BuildReloadRect(_canvasRoot, "ReloadBarFg" + i, reloadBarSize, barCenter, swatchColor, isFill: true);
            }

            ApplySelectedHighlight(shootingController.SelectedTypeIndex);
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
    }
}
