using CubeFly.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CubeFly.Fly
{
    // Bottom-left HUD: a shield bar (cyan fill = ShieldPoints / ShieldMax)
    // stacked above the HP label, plus a "Power: +N / −N" readout. Both
    // read the construct's ConstructEnergySystem. The bar is hidden when
    // the construct has no shield cubes; the readout is hidden when it has
    // no reactor/shield cubes. Built under FlyHud.Instance.Root.
    //
    // DefaultExecutionOrder(100) so Start runs after FlyController.Start
    // (which builds the construct + registers the energy system), mirroring
    // FlyHpIndicator.
    [DefaultExecutionOrder(100)]
    public class FlyShieldIndicator : MonoBehaviour
    {
        [Header("Layout (bottom-left, above HP)")]
        [SerializeField] Vector2 barAnchoredPosition = new Vector2(20f, 100f);
        [SerializeField] Vector2 barSize = new Vector2(180f, 16f);
        [SerializeField] Vector2 powerLabelAnchoredPosition = new Vector2(20f, 122f);
        [SerializeField] int powerFontSize = 18;

        [Header("Colours")]
        [SerializeField] Color shieldFillColor = new Color(0.3f, 0.8f, 1f, 0.95f);
        [SerializeField] Color shieldFrameColor = new Color(0.05f, 0.12f, 0.16f, 0.85f);
        [SerializeField] Color shieldDownColor = new Color(0.4f, 0.4f, 0.45f, 0.6f);
        [SerializeField] Color powerPositiveColor = new Color(0.4f, 1f, 0.5f, 1f);
        [SerializeField] Color powerNegativeColor = new Color(1f, 0.4f, 0.35f, 1f);

        ConstructEnergySystem _energy;
        RectTransform _frame;
        RectTransform _fill;
        Image _fillImage;
        Text _powerLabel;

        const string TAG = "FlyShield";

        void Awake() => BuildUI();

        void Start()
        {
            _energy = FindAnyObjectByType<ConstructEnergySystem>();
            if (_energy == null)
                Debug.unityLogger.Log(TAG, "No ConstructEnergySystem in scene; shield HUD stays hidden.");
        }

        void Update()
        {
            bool hasShield = _energy != null && _energy.HasShieldCubes;
            bool hasPower  = _energy != null && _energy.HasPowerCubes;

            if (_frame != null && _frame.gameObject.activeSelf != hasShield)
                _frame.gameObject.SetActive(hasShield);
            if (_powerLabel != null) _powerLabel.enabled = hasPower;
            if (!hasPower) return;

            if (hasShield)
            {
                float frac = _energy.ShieldMax > 0f
                    ? Mathf.Clamp01(_energy.ShieldPoints / _energy.ShieldMax) : 0f;
                _fill.sizeDelta = new Vector2(barSize.x * frac, barSize.y);
                _fillImage.color = _energy.ShieldActive ? shieldFillColor : shieldDownColor;
            }

            float net = _energy.NetPower;
            _powerLabel.text = $"Power: {(net >= 0f ? "+" : "")}{net:F0}";
            _powerLabel.color = net >= 0f ? powerPositiveColor : powerNegativeColor;
        }

        void BuildUI()
        {
            RectTransform root = FlyHud.Instance.Root;
            int uiLayer = LayerMask.NameToLayer("UI");

            // Bar frame (background), bottom-left anchored.
            GameObject frameGO = new GameObject("ShieldBarFrame",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            if (uiLayer >= 0) frameGO.layer = uiLayer;
            frameGO.transform.SetParent(root, false);
            _frame = (RectTransform)frameGO.transform;
            _frame.anchorMin = _frame.anchorMax = _frame.pivot = new Vector2(0f, 0f);
            _frame.sizeDelta = barSize;
            _frame.anchoredPosition = barAnchoredPosition;
            Image frameImg = frameGO.GetComponent<Image>();
            frameImg.color = shieldFrameColor;
            frameImg.raycastTarget = false;

            // Fill — left-anchored child so width = fraction shrinks from the right.
            GameObject fillGO = new GameObject("ShieldBarFill",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            if (uiLayer >= 0) fillGO.layer = uiLayer;
            fillGO.transform.SetParent(frameGO.transform, false);
            _fill = (RectTransform)fillGO.transform;
            _fill.anchorMin = _fill.anchorMax = _fill.pivot = new Vector2(0f, 0f);
            _fill.sizeDelta = barSize;
            _fill.anchoredPosition = Vector2.zero;
            _fillImage = fillGO.GetComponent<Image>();
            _fillImage.color = shieldFillColor;
            _fillImage.raycastTarget = false;

            // Power readout label.
            _powerLabel = UIStyle.BuildLabel(root, "Power: +0", fontSize: powerFontSize);
            _powerLabel.alignment = TextAnchor.LowerLeft;
            RectTransform plRT = (RectTransform)_powerLabel.transform;
            plRT.anchorMin = plRT.anchorMax = plRT.pivot = new Vector2(0f, 0f);
            plRT.sizeDelta = new Vector2(220f, 28f);
            plRT.anchoredPosition = powerLabelAnchoredPosition;
        }
    }
}
