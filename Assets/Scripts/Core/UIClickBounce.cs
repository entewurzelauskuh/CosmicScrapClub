using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CubeFly.Core
{
    // Press feedback for menu / hangar buttons: on pointer-DOWN the button
    // translates toward its toon shadow AND the shadow collapses (effectDistance
    // -> 0), so the button visually "stamps" down onto its shadow (the BRAND
    // press-stamp). Restores on pointer-up. The Shadow is a same-graphic
    // BaseMeshEffect (AddToonShadow), so collapsing it is what sells the sink —
    // translating alone would just drag the shadow along. Additive alongside the
    // Button's own onClick. Class name kept so UIStyle.BuildLabeledButton's
    // `bounce` param still opts in.
    [RequireComponent(typeof(RectTransform))]
    public class UIClickBounce : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        RectTransform _rt;
        Shadow _shadow;
        Vector2 _rest;
        Vector2 _stamp = new Vector2(6f, -6f);
        bool _resolved;
        bool _pressed;

        void Awake() => _rt = (RectTransform)transform;

        void Resolve()
        {
            if (_resolved) return;
            // The Shadow is added by the caller AFTER this component, so resolve lazily.
            _shadow = GetComponent<Shadow>();
            if (_shadow != null) _stamp = _shadow.effectDistance;
            _resolved = true;
        }

        void OnDisable()
        {
            if (_pressed) Restore();
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || _pressed) return;
            Resolve();
            _rest = _rt.anchoredPosition;
            _rt.anchoredPosition = _rest + _stamp;
            if (_shadow != null) _shadow.effectDistance = Vector2.zero;   // collapse — button stamps onto the shadow
            _pressed = true;
        }

        // uGUI sends OnPointerUp to the pressed object regardless of where the
        // pointer is, so this alone reliably restores — no OnPointerExit (which
        // would fire spuriously when the stamp shifts the button out from under
        // a held cursor).
        public void OnPointerUp(PointerEventData e) => Restore();

        void Restore()
        {
            if (!_pressed) return;
            _pressed = false;
            _rt.anchoredPosition = _rest;
            if (_shadow != null) _shadow.effectDistance = _stamp;
        }
    }
}
