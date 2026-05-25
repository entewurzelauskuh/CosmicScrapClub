using UnityEngine;
using UnityEngine.EventSystems;

namespace CubeFly.Core
{
    // Tiny IPointerEnterHandler / IPointerExitHandler that surfaces a
    // tooltip via TooltipHud on hover. Attach to any UI element that
    // has a Graphic with raycastTarget=true (so PointerEnter fires).
    //
    // Public surface:
    //   • SetText(string) — set or update the tooltip text. Safe to
    //     call before the trigger is on a hovered state. Empty / null
    //     text suppresses the tooltip on enter.
    //
    // Robustness: the trigger also hides its tooltip in OnDisable.
    // Without that, pressing ESC to close the Settings menu while the
    // cursor is still over a toggle would leave the tooltip pinned to
    // the mouse forever — Unity doesn't synthesize a PointerExit when
    // the hovered UI element is deactivated out from under the cursor.
    // OnDisable cascades naturally when the parent panel SetActive's
    // false, so this covers ESC, tab switches, scene transitions, and
    // any other "UI disappears mid-hover" case.
    //
    // The _showingFromMe flag prevents triggers on tabs that just went
    // inactive (e.g. during tab switching) from clobbering a tooltip
    // that another trigger on a still-active tab is currently showing.
    public class TooltipTrigger : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler
    {
        string _text = "";
        bool _showingFromMe;

        public void SetText(string text) => _text = text ?? "";

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (string.IsNullOrEmpty(_text)) return;
            TooltipHud.Instance.Show(_text, eventData.position);
            _showingFromMe = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            HideIfShowing();
        }

        // Triggered when the GameObject (or any of its parents) becomes
        // inactive — e.g. SettingsMenu.HideUI() flipping the modal root
        // off via SetActive(false). PointerExit never fires in that
        // case because Unity doesn't poll the disabled hierarchy.
        void OnDisable()
        {
            HideIfShowing();
        }

        void HideIfShowing()
        {
            if (!_showingFromMe) return;
            _showingFromMe = false;
            // Side-effect-free check — don't lazy-spawn the hud just to
            // call a no-op Hide(). The hud only exists if a previous
            // OnPointerEnter actually called Instance directly.
            if (TooltipHud.HasInstance) TooltipHud.Instance.Hide();
        }
    }
}
