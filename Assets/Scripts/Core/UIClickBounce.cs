using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CubeFly.Core
{
    // Marginal press feedback for menu / hangar buttons: a left-click shrinks the
    // target ~5% then eases it back to full size over ~0.5s. Purely cosmetic and
    // additive — it rides alongside the Button's own onClick (both IPointerClick
    // handlers on the GameObject fire). Uses unscaled time so it still animates if
    // a pause overlay has frozen Time.timeScale.
    [RequireComponent(typeof(RectTransform))]
    public class UIClickBounce : MonoBehaviour, IPointerClickHandler
    {
        const float ShrinkTo = 0.95f;
        const float Duration = 0.5f;

        RectTransform _rt;
        Coroutine _running;

        void Awake() => _rt = (RectTransform)transform;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (_running != null) StopCoroutine(_running);
            _running = StartCoroutine(Bounce());
        }

        IEnumerator Bounce()
        {
            float t = 0f;
            while (t < Duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / Duration);
                _rt.localScale = Vector3.one * Mathf.Lerp(ShrinkTo, 1f, k);
                yield return null;
            }
            _rt.localScale = Vector3.one;
        }
    }
}
