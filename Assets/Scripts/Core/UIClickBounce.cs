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

        void OnDisable()
        {
            // If the button is hidden mid-bounce, Unity kills the coroutine before
            // the trailing scale-reset runs — restore full size here so the button
            // isn't stranded shrunk the next time it's shown.
            if (_running != null)
            {
                StopCoroutine(_running);
                _running = null;
            }
            if (_rt != null) _rt.localScale = Vector3.one;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            // A button's own onClick can deactivate this GameObject earlier in the
            // same pointer-event dispatch: both IPointerClick handlers are captured
            // while active, then invoked in order (Button first). HangarSelect's
            // Cancel / "Yes, delete" buttons hide themselves this way. Starting a
            // coroutine on an inactive object logs a Unity error and there's nothing
            // visible left to animate, so bail.
            if (!isActiveAndEnabled) return;
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
