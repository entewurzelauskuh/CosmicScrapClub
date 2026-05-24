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
    public class TooltipTrigger : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler
    {
        string _text = "";

        public void SetText(string text) => _text = text ?? "";

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (string.IsNullOrEmpty(_text)) return;
            TooltipHud.Instance.Show(_text, eventData.position);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (TooltipHud.Instance != null) TooltipHud.Instance.Hide();
        }
    }
}
