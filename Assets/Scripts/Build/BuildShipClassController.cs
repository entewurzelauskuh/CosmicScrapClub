using System;
using CubeFly.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CubeFly.Build
{
    // Middle-left BuildScene dropdown for picking the construct's ship
    // class. Changing it updates GameData.ActiveShipClass and notifies
    // BuildManager, which re-applies the alpha-cube HP, refreshes the
    // Mass / HP readout, and lets the change autosave through the
    // normal ConstructChanged debounce path.
    //
    // Builds its own screen-space canvas + dropdown via UIStyle, like
    // the other code-built BuildScene overlays.
    public class BuildShipClassController : MonoBehaviour
    {
        [SerializeField] BuildManager buildManager;

        [Header("Layout")]
        [Tooltip("Anchored position of the 'Class' label, relative to the middle-left edge of the screen.")]
        [SerializeField] Vector2 anchoredPosition = new Vector2(20f, 0f);
        [SerializeField] Vector2 dropdownSize = new Vector2(200f, 36f);
        [SerializeField] int fontSize = 20;

        Dropdown _dropdown;

        // Enum values cached once. Options are added to the dropdown in
        // this order, so a dropdown index maps straight back to
        // Classes[index]. Don't assume index == (int)ShipClass — go
        // through this array so a future enum reorder can't desync.
        static readonly ShipClass[] Classes =
            (ShipClass[])Enum.GetValues(typeof(ShipClass));

        const string TAG = "BuildShipClass";

        void Awake()
        {
            BuildUI();
        }

        void Start()
        {
            if (buildManager == null) buildManager = FindAnyObjectByType<BuildManager>();
            if (buildManager == null)
                Debug.unityLogger.LogWarning(TAG, "No BuildManager in scene; class changes won't apply.");

            _dropdown.options.Clear();
            for (int i = 0; i < Classes.Length; i++)
                _dropdown.options.Add(new Dropdown.OptionData(ShipClasses.DisplayName(Classes[i])));

            // Reflect the current class (set by HangarSelect's load, or
            // Allrounder for a fresh construct) WITHOUT firing
            // onValueChanged — this is a display sync, not a user edit.
            int current = Array.IndexOf(Classes, GameData.ActiveShipClass);
            _dropdown.SetValueWithoutNotify(current < 0 ? 0 : current);
            _dropdown.RefreshShownValue();

            _dropdown.onValueChanged.AddListener(OnDropdownChanged);
        }

        void OnDestroy()
        {
            if (_dropdown != null) _dropdown.onValueChanged.RemoveListener(OnDropdownChanged);
        }

        void OnDropdownChanged(int index)
        {
            if (index < 0 || index >= Classes.Length) return;
            GameData.SetShipClass(Classes[index]);
            if (buildManager != null) buildManager.OnShipClassChanged();
        }

        void BuildUI()
        {
            RectTransform root = BuildHud.Instance.Root;

            // Cool near-black top bar spanning the screen, 2px ink bottom edge.
            GameObject barGO = new GameObject("TopBarPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            barGO.transform.SetParent(root, false);
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) barGO.layer = uiLayer;
            RectTransform barRT = (RectTransform)barGO.transform;
            barRT.anchorMin = new Vector2(0f, 1f);
            barRT.anchorMax = new Vector2(1f, 1f);
            barRT.pivot = new Vector2(0.5f, 1f);
            barRT.sizeDelta = new Vector2(0f, 64f);
            barRT.anchoredPosition = Vector2.zero;
            Image barImg = barGO.GetComponent<Image>();
            barImg.color = CscTheme.PanelFill;
            barImg.raycastTarget = false;   // decorative — don't swallow build clicks
            barGO.transform.SetAsFirstSibling();   // behind the label + dropdown

            GameObject inkGO = new GameObject("TopBarInk", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            inkGO.transform.SetParent(barGO.transform, false);
            if (uiLayer >= 0) inkGO.layer = uiLayer;
            RectTransform inkRT = (RectTransform)inkGO.transform;
            inkRT.anchorMin = new Vector2(0f, 0f);
            inkRT.anchorMax = new Vector2(1f, 0f);
            inkRT.pivot = new Vector2(0.5f, 1f);
            inkRT.sizeDelta = new Vector2(0f, 2f);
            inkRT.anchoredPosition = Vector2.zero;
            Image inkImg = inkGO.GetComponent<Image>();
            inkImg.color = CscPalette.Ink;
            inkImg.raycastTarget = false;

            // CLASS label + dropdown, seated inside the top bar: top-anchored,
            // vertically centred in the 64px band. anchoredPosition.x still
            // drives the left margin (its y now reads as the bar centre).
            Vector2 barLeft = new Vector2(0f, 1f);
            Vector2 leftPivot = new Vector2(0f, 0.5f);
            const float barCenterY = -32f;

            Text label = UIStyle.BuildLabel(root, "CLASS", fontSize: fontSize, style: FontStyle.Bold, font: CscTheme.CondOr);
            label.alignment = TextAnchor.MiddleLeft;
            label.color = CscPalette.Steel100;
            RectTransform labelRT = (RectTransform)label.transform;
            labelRT.anchorMin = labelRT.anchorMax = barLeft;
            labelRT.pivot = leftPivot;
            labelRT.sizeDelta = new Vector2(70f, dropdownSize.y);
            labelRT.anchoredPosition = new Vector2(anchoredPosition.x, barCenterY);

            _dropdown = UIStyle.BuildDropdown(root, dropdownSize, fontSize);
            RectTransform ddRT = (RectTransform)_dropdown.transform;
            ddRT.anchorMin = ddRT.anchorMax = barLeft;
            ddRT.pivot = leftPivot;
            ddRT.anchoredPosition = new Vector2(anchoredPosition.x + 78f, barCenterY);
        }
    }
}
