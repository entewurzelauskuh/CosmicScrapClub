using System;
using CubeFly.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CubeFly.Build
{
    // Top-left BuildScene dropdown for picking the construct's ship
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

        Canvas _canvas;
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
            UIStyle.EnsureEventSystem();
            // sortingOrder 95: above the build toolbar (90), below the
            // persistent corner UI (100). The Dropdown bumps its own
            // sorting when the option list opens.
            _canvas = UIStyle.BuildScreenSpaceCanvas("BuildShipClassCanvas", sortingOrder: 95);
            RectTransform root = (RectTransform)_canvas.transform;

            // Anchored / pivoted middle-left so anchoredPosition reads
            // as an offset from the screen's left edge at vertical centre.
            Vector2 midLeft = new Vector2(0f, 0.5f);

            Text label = UIStyle.BuildLabel(root, "Class", fontSize: fontSize, style: FontStyle.Bold);
            label.alignment = TextAnchor.MiddleLeft;
            RectTransform labelRT = (RectTransform)label.transform;
            labelRT.anchorMin = labelRT.anchorMax = labelRT.pivot = midLeft;
            labelRT.sizeDelta = new Vector2(70f, dropdownSize.y);
            labelRT.anchoredPosition = anchoredPosition;

            _dropdown = UIStyle.BuildDropdown(root, dropdownSize, fontSize);
            RectTransform ddRT = (RectTransform)_dropdown.transform;
            ddRT.anchorMin = ddRT.anchorMax = ddRT.pivot = midLeft;
            ddRT.anchoredPosition = new Vector2(anchoredPosition.x + 78f, anchoredPosition.y);
        }
    }
}
