using System.Collections;
using CubeFly.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CubeFly.Build
{
    // Bottom-of-screen toolbar that lets the player switch between cube
    // types. Reads its content from BuildManager.Registry, so adding more
    // CubeTypeDefinitions to the registry automatically grows the toolbar.
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

        const string TAG = "BuildToolbar";

        Button[] _buttons;
        Image[] _backgrounds;
        Button _deleteButton;
        Image _deleteBackground;
        Text _massLabel;
        Text _hpLabel;
        Text _floatingMessage;
        Coroutine _floatingRoutine;
        static readonly Color SelectedTypeColor = new Color(0.25f, 0.45f, 0.85f, 0.95f);

        void Start()
        {
            if (buildManager == null) buildManager = FindAnyObjectByType<BuildManager>();
            if (buildManager == null)
            {
                Debug.unityLogger.LogError(TAG, "No BuildManager in scene; toolbar cannot wire up.");
                return;
            }
            BuildToolbar();
            buildManager.CurrentTypeChanged += OnCurrentTypeChanged;
            buildManager.CurrentToolChanged += OnCurrentToolChanged;
            buildManager.ConstructChanged   += RefreshStatLabels;
            UpdateButtonStates();
            RefreshStatLabels();
            Debug.unityLogger.Log(TAG, $"Build toolbar created with {_buttons?.Length ?? 0} cube entry(ies) + Delete.");
        }

        void OnDestroy()
        {
            if (buildManager != null)
            {
                buildManager.CurrentTypeChanged -= OnCurrentTypeChanged;
                buildManager.CurrentToolChanged -= OnCurrentToolChanged;
                buildManager.ConstructChanged   -= RefreshStatLabels;
            }
        }

        void BuildToolbar()
        {
            UIStyle.EnsureEventSystem();
            // Sit just under the persistent corner-button canvas (sortingOrder
            // 100). Both share the screen but never overlap visually.
            Canvas canvas = UIStyle.BuildScreenSpaceCanvas("BuildToolbarCanvas", sortingOrder: 90);
            RectTransform root = (RectTransform)canvas.transform;

            // Top-left rotation hint — small reminder that R/T rotate the
            // current placement. All values are inspector-tweakable on the
            // BuildToolbar GameObject (see [Header("Rotate hint (top-left)")]
            // fields above).
            Text hint = UIStyle.BuildLabel(root, hintText, fontSize: hintFontSize);
            hint.alignment = TextAnchor.UpperLeft;
            RectTransform hrt = (RectTransform)hint.transform;
            hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(0f, 1f);
            hrt.anchoredPosition = hintAnchoredPosition;
            hrt.sizeDelta = hintSize;

            // Top-center transient message label — used by BuildManager to
            // surface placement-denial reasons ("Too much mass!", etc.).
            // Starts hidden (alpha 0); ShowFloatingMessage drives the fade.
            _floatingMessage = UIStyle.BuildLabel(root, string.Empty, fontSize: floatingFontSize);
            _floatingMessage.alignment = TextAnchor.UpperCenter;
            _floatingMessage.color = new Color(floatingColor.r, floatingColor.g, floatingColor.b, 0f);
            RectTransform mrt = (RectTransform)_floatingMessage.transform;
            mrt.anchorMin = mrt.anchorMax = mrt.pivot = new Vector2(0.5f, 1f);
            mrt.anchoredPosition = floatingAnchoredPosition;
            mrt.sizeDelta = floatingSize;

            CubeTypeRegistry registry = buildManager.Registry;
            int count = registry != null ? registry.Count : 0;
            _buttons = new Button[count];
            _backgrounds = new Image[count];

            // The Delete button sits at the right end of the toolbar; bake it
            // into the layout width so the whole bar stays centred.
            int totalSlots = count + 1;
            float totalWidth = totalSlots * buttonSize.x + Mathf.Max(0, totalSlots - 1) * spacing;
            float startX = -totalWidth / 2f + buttonSize.x / 2f;

            for (int i = 0; i < count; i++)
            {
                int idx = i; // capture for closure
                CubeTypeDefinition def = registry.Get(i);
                string label = def != null ? def.displayName : $"Cube #{i}";

                (Button btn, Text _) = UIStyle.BuildLabeledButton(root, label, buttonSize, fontSize);
                RectTransform rt = (RectTransform)btn.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(startX + i * (buttonSize.x + spacing), bottomMargin);

                btn.onClick.AddListener(() => buildManager.SetCurrentType(idx));

                _buttons[i] = btn;
                _backgrounds[i] = btn.GetComponent<Image>();
            }

            // Delete button — last slot.
            (Button delBtn, Text _ignored) = UIStyle.BuildLabeledButton(root, deleteButtonLabel, buttonSize, fontSize);
            RectTransform delRT = (RectTransform)delBtn.transform;
            delRT.anchorMin = delRT.anchorMax = delRT.pivot = new Vector2(0.5f, 0f);
            delRT.anchoredPosition = new Vector2(startX + count * (buttonSize.x + spacing), bottomMargin);
            delBtn.onClick.AddListener(() => buildManager.SetCurrentTool(BuildTool.Delete));
            _deleteButton = delBtn;
            _deleteBackground = delBtn.GetComponent<Image>();

            // Bottom-left stat labels — persistent (no fade).
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
        }

        void OnCurrentTypeChanged(int typeIndex) => UpdateButtonStates();
        void OnCurrentToolChanged(BuildTool tool) => UpdateButtonStates();

        void UpdateButtonStates()
        {
            if (buildManager == null) return;
            bool deleteActive = buildManager.CurrentTool == BuildTool.Delete;
            int activeIdx = buildManager.CurrentTypeIndex;

            if (_backgrounds != null)
            {
                for (int i = 0; i < _backgrounds.Length; i++)
                {
                    if (_backgrounds[i] == null) continue;
                    _backgrounds[i].color = (!deleteActive && i == activeIdx)
                        ? SelectedTypeColor
                        : UIStyle.BackgroundIdle;
                }
            }
            if (_deleteBackground != null)
                _deleteBackground.color = deleteActive ? deleteSelectedColor : UIStyle.BackgroundIdle;
        }

        void RefreshStatLabels()
        {
            if (buildManager == null) return;
            float mass = buildManager.ComputeCurrentMass();
            float hp   = buildManager.ComputeCurrentHealthPoints();
            // Mass limit lives on BuildManager as a SerializeField; expose
            // via the existing getter chain rather than duplicating it. We
            // pull it from the inspector-set value reflected in the log; if
            // the field changes name, both lines update together.
            if (_massLabel != null)
                _massLabel.text = $"Mass: {mass:F1} / {buildManager.MassLimit:F0}";
            if (_hpLabel != null)
                _hpLabel.text = $"HP: {hp:F0}";
        }

        // Pop a message at the top-center for `duration` seconds and fade it
        // out linearly. Calling again restarts at full alpha with the new text.
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
