using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CubeFly.Core
{
    // Press feedback for menu / hangar buttons: on pointer-DOWN the button
    // translates into its toon shadow (the BRAND "stamp"), and restores on
    // pointer-up / exit. Reads the sibling Shadow's offset (added by the caller
    // after this component) lazily on first press so it lands exactly on the
    // shadow; falls back to (6,-6). Additive alongside the Button's own onClick.
    // Class name kept so UIStyle.BuildLabeledButton's `bounce` param still opts in.
    [RequireComponent(typeof(RectTransform))]
    public class UIClickBounce : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        RectTransform _rt;
        Vector2 _rest;
        Vector2 _stamp = new Vector2(6f, -6f);
        bool _stampResolved;
        bool _pressed;

        void Awake() => _rt = (RectTransform)transform;

        void OnDisable()
        {
            // Only restore if we're mid-press — otherwise _rest is unset and
            // we'd yank the button to (0,0).
            if (_pressed) { _rt.anchoredPosition = _rest; _pressed = false; }
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || _pressed) return;
            if (!_stampResolved)
            {
                Shadow sh = GetComponent<Shadow>();
                if (sh != null) _stamp = sh.effectDistance;
                _stampResolved = true;
            }
            _rest = _rt.anchoredPosition;
            _rt.anchoredPosition = _rest + _stamp;
            _pressed = true;
        }

        public void OnPointerUp(PointerEventData e) => Release();
        public void OnPointerExit(PointerEventData e) => Release();

        void Release()
        {
            if (!_pressed) return;
            _pressed = false;
            _rt.anchoredPosition = _rest;
        }
    }
}
