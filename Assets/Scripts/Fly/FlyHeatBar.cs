using System.Collections;
using CubeFly.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CubeFly.Fly
{
    // Heat HUD element — a thin vertical bar to the RIGHT of the crosshair,
    // mirroring FlyBoostBar on the left. Reads the SELECTED weapon type's
    // shared heat from FlyShootingController; the whole element is hidden
    // unless the selected type is a laser.
    //
    // Fill   — bar height = Heat/100 (grows up as the laser heats).
    // Opacity— alpha = Heat/100 (mirrors FlyBoostBar's opacity ramp):
    //          invisible when cold, fades in with use, fades back out as it
    //          regens to 0 — so the bar only shows while in use / cooling.
    // Colour — lerps cool->hot with heat; while Overheated it pulses red
    //          (the FlyBoostBar critical-throb pattern — stays visible).
    // Flash  — "Overheated!" flashes 3x on the lockout edge, above the
    //          crosshair (the FlyBoostBar "Overboosted!" pattern).
    public class FlyHeatBar : MonoBehaviour
    {
        [SerializeField] FlyShootingController shootingController;

        [Header("Bar layout (screen-centre relative)")]
        [Tooltip("Anchored position of the bar centre relative to screen centre. Positive x sits it right of the crosshair (mirror of the boost bar).")]
        [SerializeField] Vector2 anchoredPosition = new Vector2(90f, 0f);
        [SerializeField] Vector2 barSize = new Vector2(12f, 120f);
        [SerializeField] Color coolColor = new Color(1f, 0.6f, 0.2f, 1f);
        [SerializeField] Color hotColor = new Color(1f, 0.2f, 0.1f, 1f);
        [SerializeField] Color frameColor = new Color(0.12f, 0.06f, 0.04f, 1f);

        [Header("Overheated flash")]
        [SerializeField] Vector2 flashAnchoredPosition = new Vector2(0f, 70f);
        [SerializeField] int flashFontSize = 26;
        [SerializeField] Color flashColor = new Color(1f, 0.4f, 0.25f, 1f);
        [SerializeField] int flashCount = 3;
        [SerializeField] float flashOnSeconds = 0.27f;
        [SerializeField] float flashOffSeconds = 0.12f;

        [Header("Overheated throb")]
        [SerializeField] float overheatedPulseSeconds = 0.6f;
        [SerializeField, Range(0f, 1f)] float overheatedAlphaMin = 0.45f;

        RectTransform _frame;
        RectTransform _fill;
        Image _fillImage;
        Image _frameImage;
        Text _flashLabel;

        // Edge-detect the overheat flash PER weapon-type: remember the last
        // selected group + its Overheated state, so switching away from and
        // back to an already-overheated laser (or between laser types)
        // re-establishes the baseline instead of spuriously re-flashing.
        WeaponTypeGroup _lastGroup;
        bool _lastGroupOverheated;
        Coroutine _flashRoutine;

        const string TAG = "FlyHeatBar";

        void Awake() => BuildUI();

        void OnValidate()
        {
            overheatedPulseSeconds = Mathf.Max(0.01f, overheatedPulseSeconds);
            overheatedAlphaMin = Mathf.Clamp01(overheatedAlphaMin);
        }

        void Start()
        {
            if (shootingController == null) shootingController = FindAnyObjectByType<FlyShootingController>();
            if (shootingController == null)
                Debug.unityLogger.Log(TAG, "No FlyShootingController in scene; heat bar stays hidden.");
        }

        void Update()
        {
            if (_frame == null) return;

            WeaponTypeGroup sel = shootingController != null ? shootingController.SelectedType : null;
            bool isLaser = sel != null && sel.IsLaser;

            if (_frame.gameObject.activeSelf != isLaser) _frame.gameObject.SetActive(isLaser);
            if (!isLaser)
            {
                // No baseline while hidden — re-selecting a laser establishes
                // a fresh one (below) rather than flashing.
                _lastGroup = null;
                return;
            }

            float fraction = Mathf.Clamp01(sel.Heat / 100f);
            _fill.sizeDelta = new Vector2(barSize.x, barSize.y * fraction);

            if (sel.Overheated)
            {
                float pulse01 = 0.5f * (1f + Mathf.Sin(
                    Time.unscaledTime * (2f * Mathf.PI / overheatedPulseSeconds)));
                float a = Mathf.Lerp(overheatedAlphaMin, 1f, pulse01);
                SetImageAlpha(_fillImage, hotColor, a);
                SetImageAlpha(_frameImage, frameColor, a);
            }
            else
            {
                // Alpha ramps with heat (mirrors FlyBoostBar's 1-fraction
                // opacity ramp): invisible when cold, fades in as the laser
                // heats, fades back out as it regens to 0 — so the bar only
                // shows while the laser is in use / cooling down.
                Color c = Color.Lerp(coolColor, hotColor, fraction);
                SetImageAlpha(_fillImage, c, fraction);
                SetImageAlpha(_frameImage, frameColor, fraction);
            }

            // Flash only on a genuine false->true overheat transition of the
            // SAME selected type. A type switch (sel != _lastGroup) just
            // re-establishes the baseline without flashing.
            if (sel != _lastGroup)
            {
                _lastGroup = sel;
                _lastGroupOverheated = sel.Overheated;
            }
            else
            {
                if (sel.Overheated && !_lastGroupOverheated)
                {
                    if (_flashRoutine != null) StopCoroutine(_flashRoutine);
                    _flashRoutine = StartCoroutine(FlashOverheated());
                }
                _lastGroupOverheated = sel.Overheated;
            }
        }

        static void SetImageAlpha(Image img, Color baseColor, float alpha)
        {
            if (img == null) return;
            img.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
        }

        IEnumerator FlashOverheated()
        {
            if (_flashLabel == null) yield break;
            for (int i = 0; i < flashCount; i++)
            {
                _flashLabel.enabled = true;
                yield return new WaitForSecondsRealtime(flashOnSeconds);
                _flashLabel.enabled = false;
                yield return new WaitForSecondsRealtime(flashOffSeconds);
            }
            _flashLabel.enabled = false;
            _flashRoutine = null;
        }

        void BuildUI()
        {
            RectTransform canvasRoot = FlyHud.Instance.Root;
            int uiLayer = LayerMask.NameToLayer("UI");

            GameObject frameGO = new GameObject("HeatBarFrame",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            if (uiLayer >= 0) frameGO.layer = uiLayer;
            frameGO.transform.SetParent(canvasRoot, false);
            _frame = (RectTransform)frameGO.transform;
            _frame.anchorMin = _frame.anchorMax = _frame.pivot = new Vector2(0.5f, 0.5f);
            _frame.sizeDelta = barSize;
            _frame.anchoredPosition = anchoredPosition;
            _frameImage = frameGO.GetComponent<Image>();
            _frameImage.color = frameColor;
            _frameImage.raycastTarget = false;

            GameObject fillGO = new GameObject("HeatBarFill",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            if (uiLayer >= 0) fillGO.layer = uiLayer;
            fillGO.transform.SetParent(frameGO.transform, false);
            _fill = (RectTransform)fillGO.transform;
            _fill.anchorMin = _fill.anchorMax = _fill.pivot = new Vector2(0.5f, 0f);
            _fill.sizeDelta = new Vector2(barSize.x, 0f);
            _fill.anchoredPosition = Vector2.zero;
            _fillImage = fillGO.GetComponent<Image>();
            _fillImage.color = coolColor;
            _fillImage.raycastTarget = false;

            _flashLabel = UIStyle.BuildLabel(canvasRoot, "Overheated!", fontSize: flashFontSize, style: FontStyle.Bold, font: CscTheme.StencilOr);
            _flashLabel.color = flashColor;
            RectTransform flashRT = (RectTransform)_flashLabel.transform;
            flashRT.anchorMin = flashRT.anchorMax = flashRT.pivot = new Vector2(0.5f, 0.5f);
            flashRT.sizeDelta = new Vector2(360f, 44f);
            flashRT.anchoredPosition = flashAnchoredPosition;
            _flashLabel.enabled = false;

            _frame.gameObject.SetActive(false);
        }
    }
}
