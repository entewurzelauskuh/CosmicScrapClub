using System.Collections;
using CubeFly.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CubeFly.Fly
{
    // Boost HUD element — a thin vertical bar to the LEFT of the
    // crosshair, showing the FlyController Boost resource. Mirrors the
    // FlySpeedIndicator / FlyHpIndicator pattern: builds its own
    // ScreenSpaceOverlay canvas via UIStyle in Awake, auto-wires its
    // FlyController in Start, reads game state each Update.
    //
    // Fill    — bar height = BoostFraction (0-1), anchored at the
    //           bottom and growing up.
    // Opacity — alpha = 1 - BoostFraction: near-invisible at full
    //           boost, opaque when drained ("ramps with use", spec §7).
    // Position— screen-fixed near screen centre, offset left. NOT
    //           parented to the FlyCrosshair reticle — that reticle
    //           drifts as the construct turns and would drag the bar
    //           around with it (spec §7).
    //
    // "Overboosted!" — when FlyController.IsOverboosted flips
    // false->true, a label near the crosshair flashes 3x then hides.
    //
    // The Heat bar (the crosshair's right-side bar) is deferred to the
    // Laser-weapon roadmap item (spec §9) — this component builds only
    // the Boost bar.
    public class FlyBoostBar : MonoBehaviour
    {
        [SerializeField] FlyController flyController;

        [Header("Bar layout (screen-centre relative)")]
        [Tooltip("Anchored position of the bar's centre relative to screen centre. Negative x sits it left of the crosshair.")]
        [SerializeField] Vector2 anchoredPosition = new Vector2(-90f, 0f);
        [Tooltip("Bar size in pixels — thin and tall (a vertical bar).")]
        [SerializeField] Vector2 barSize = new Vector2(12f, 120f);
        [SerializeField] Color fillColor = new Color(0.36f, 0.62f, 1f, 1f);
        [SerializeField] Color frameColor = new Color(0.05f, 0.07f, 0.12f, 1f);

        [Header("Overboosted flash")]
        [Tooltip("Anchored position of the flash label relative to screen centre. Sits above the crosshair.")]
        [SerializeField] Vector2 flashAnchoredPosition = new Vector2(0f, 70f);
        [SerializeField] int flashFontSize = 26;
        [SerializeField] Color flashColor = new Color(1f, 0.45f, 0.3f, 1f);
        [Tooltip("Number of visible/hidden flash cycles on overboosted entry.")]
        [SerializeField] int flashCount = 3;
        [Tooltip("Seconds the flash label is visible per cycle.")]
        [SerializeField] float flashOnSeconds = 0.27f;
        [Tooltip("Seconds the flash label is hidden per cycle.")]
        [SerializeField] float flashOffSeconds = 0.12f;

        [Header("Critical zone (bottom of the meter)")]
        [Tooltip("Fill color while the meter is in its critical bottom band (FlyController.IsBoostCritical).")]
        [SerializeField] Color criticalColor = new Color(0.95f, 0.25f, 0.20f, 1f);
        [Tooltip("Seconds for one full throb cycle (alpha + size) while critical. Larger = slower.")]
        [SerializeField] float criticalPulseSeconds = 1.2f;
        [Tooltip("Size throb amplitude while critical — the bar's localScale oscillates by +/- this fraction. 0.05 = +/-5%.")]
        [SerializeField] float criticalSizePulse = 0.05f;
        [Tooltip("Low point of the alpha throb while critical; the high point is 1.")]
        [SerializeField] float criticalAlphaMin = 0.55f;

        Canvas _canvas;
        RectTransform _frame;    // bar background; localScale throbs while critical
        RectTransform _fill;     // grows bottom-up with BoostFraction
        Image _fillImage;
        Image _frameImage;
        Text _flashLabel;

        // Edge-detect for the Overboosted flash — fire the flash once
        // per false->true transition of FlyController.IsOverboosted.
        bool _wasOverboosted;
        Coroutine _flashRoutine;

        const string TAG = "FlyBoostBar";

        void Awake()
        {
            BuildUI();
        }

        void Start()
        {
            if (flyController == null) flyController = FindAnyObjectByType<FlyController>();
            if (flyController == null)
                Debug.unityLogger.LogWarning(TAG, "No FlyController in scene; Boost bar will idle empty.");
        }

        void Update()
        {
            if (_fill == null || flyController == null) return;

            float fraction = Mathf.Clamp01(flyController.BoostFraction);

            // Fill height tracks the fraction; the fill rect is pinned
            // to the bar's bottom edge (pivot/anchor y = 0) so it grows
            // upward.
            _fill.sizeDelta = new Vector2(barSize.x, barSize.y * fraction);

            // Critical zone (bottom band) — red fill, slow alpha + size
            // throb. Otherwise the normal look: blue fill, opacity ramps
            // with use, no throb.
            if (flyController.IsBoostCritical)
            {
                float pulse01 = 0.5f * (1f + Mathf.Sin(
                    Time.unscaledTime * (2f * Mathf.PI / criticalPulseSeconds)));
                float critAlpha = Mathf.Lerp(criticalAlphaMin, 1f, pulse01);
                SetImageAlpha(_fillImage, criticalColor, critAlpha);
                SetImageAlpha(_frameImage, frameColor, critAlpha);
                float scale = 1f + (pulse01 * 2f - 1f) * criticalSizePulse;
                _frame.localScale = new Vector3(scale, scale, 1f);
            }
            else
            {
                float alpha = 1f - fraction;
                SetImageAlpha(_fillImage, fillColor, alpha);
                SetImageAlpha(_frameImage, frameColor, alpha);
                _frame.localScale = Vector3.one;
            }

            // Edge-triggered Overboosted flash.
            bool overboosted = flyController.IsOverboosted;
            if (overboosted && !_wasOverboosted)
            {
                if (_flashRoutine != null) StopCoroutine(_flashRoutine);
                _flashRoutine = StartCoroutine(FlashOverboosted());
            }
            _wasOverboosted = overboosted;
        }

        static void SetImageAlpha(Image img, Color baseColor, float alpha)
        {
            if (img == null) return;
            img.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
        }

        // Flashes the "Overboosted!" label flashCount times (visible /
        // hidden cycles), then leaves it hidden. WaitForSecondsRealtime
        // so the flash still animates if a future pause sets
        // Time.timeScale = 0 — though overboosted entry happens during
        // active flight, so in practice unscaled vs scaled is moot.
        IEnumerator FlashOverboosted()
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

        // ---------- UI ----------

        void BuildUI()
        {
            UIStyle.EnsureEventSystem();
            // sortingOrder 115 sits just above FlyCrosshair (110) and
            // below FlyWeaponToolbar (120) — the bar reads on top of the
            // reticle without occluding the toolbar.
            _canvas = UIStyle.BuildScreenSpaceCanvas("FlyBoostBarCanvas", sortingOrder: 115);
            RectTransform canvasRoot = (RectTransform)_canvas.transform;
            int uiLayer = LayerMask.NameToLayer("UI");

            // Frame — the bar's background, centred on screen then
            // offset by anchoredPosition. Centre anchor + centre pivot
            // so anchoredPosition reads as an offset from screen centre.
            GameObject frameGO = new GameObject("BoostBarFrame",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            if (uiLayer >= 0) frameGO.layer = uiLayer;
            frameGO.transform.SetParent(canvasRoot, false);
            RectTransform frameRT = (RectTransform)frameGO.transform;
            _frame = frameRT;
            frameRT.anchorMin = frameRT.anchorMax = frameRT.pivot = new Vector2(0.5f, 0.5f);
            frameRT.sizeDelta = barSize;
            frameRT.anchoredPosition = anchoredPosition;
            _frameImage = frameGO.GetComponent<Image>();
            _frameImage.color = frameColor;
            _frameImage.raycastTarget = false;

            // Fill — child of the frame, pinned to the frame's BOTTOM
            // edge (anchor + pivot y = 0) so it grows upward as the
            // height is set each Update.
            GameObject fillGO = new GameObject("BoostBarFill",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            if (uiLayer >= 0) fillGO.layer = uiLayer;
            fillGO.transform.SetParent(frameGO.transform, false);
            _fill = (RectTransform)fillGO.transform;
            _fill.anchorMin = _fill.anchorMax = _fill.pivot = new Vector2(0.5f, 0f);
            _fill.sizeDelta = new Vector2(barSize.x, 0f);
            _fill.anchoredPosition = Vector2.zero;
            _fillImage = fillGO.GetComponent<Image>();
            _fillImage.color = fillColor;
            _fillImage.raycastTarget = false;

            // "Overboosted!" flash label — screen-centre relative,
            // offset above the crosshair. Hidden until a flash fires.
            _flashLabel = UIStyle.BuildLabel(canvasRoot, "Overboosted!", fontSize: flashFontSize, style: FontStyle.Bold);
            _flashLabel.color = flashColor;
            RectTransform flashRT = (RectTransform)_flashLabel.transform;
            flashRT.anchorMin = flashRT.anchorMax = flashRT.pivot = new Vector2(0.5f, 0.5f);
            flashRT.sizeDelta = new Vector2(360f, 44f);
            flashRT.anchoredPosition = flashAnchoredPosition;
            _flashLabel.enabled = false;
        }
    }
}
