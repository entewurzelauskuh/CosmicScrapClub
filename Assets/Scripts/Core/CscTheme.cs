using UnityEngine;
using UnityEngine.UI;

namespace CubeFly.Core
{
    // ============================================================================
    // Cosmic Scrap Club — semantic theme on top of CscPalette.
    //
    // CscPalette = raw named colors. CscTheme = the *roles* the UI should use,
    // plus helpers that apply the brand to legacy uGUI controls. The intent is
    // that UIStyle.cs is refactored to call these instead of its current
    // hard-coded greys — see unity_handoff/HANDOFF.md, step 4.
    //
    // Fonts: assign CscTheme.Display / Body once they are imported (HANDOFF.md
    // step 2). Until then they fall back to the builtin LegacyRuntime font so
    // nothing breaks.
    // ============================================================================
    public static class CscTheme
    {
        // ---- Semantic color roles ----
        public static Color PanelFill   => CscPalette.HudPanel;        // floating HUD / modal panels
        public static Color ButtonFill  => CscPalette.BackgroundIdle;  // ghost button bg
        public static Color PrimaryFill => CscPalette.Ochre300;        // primary / call-to-action button
        public static Color DangerFill  => CscPalette.Critical;        // destructive button
        public static Color CardFill    => CscPalette.HudCard;         // hangar slot cards
        public static Color OutlineColor => CscPalette.Ink;            // toon outline on everything
        public static Color TextPrimary => CscPalette.Sand100;         // body text on dark
        public static Color TextOnLight => CscPalette.Scorch;          // text on ochre/sand fills
        public static Color Accent      => CscPalette.Ochre300;        // selection / focus ring
        public static Color AccentHot   => CscPalette.Orange600;

        // ---- Fonts (assigned at startup once imported; null => builtin) ----
        // e.g. in a bootstrap: CscTheme.Display = Resources.Load<Font>("Fonts/Anton-Regular");
        public static Font Display;   // Anton — titles, banners, big numbers
        public static Font Stencil;   // Saira Stencil One — decals, warnings, hull numbers
        public static Font Body;      // Saira — body copy, help text, readout values
        public static Font Cond;      // Saira Condensed (Bold) — HUD labels, buttons, tabs (the most-used UI face)

        static Font _builtin;
        public static Font BuiltinFallback
        {
            get
            {
                if (_builtin == null)
                    _builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                               ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
                return _builtin;
            }
        }
        public static Font DisplayOr => Display != null ? Display : BuiltinFallback;
        public static Font StencilOr => Stencil != null ? Stencil : BuiltinFallback;
        public static Font BodyOr    => Body    != null ? Body    : BuiltinFallback;
        public static Font CondOr    => Cond    != null ? Cond    : BodyOr;

        // ---- The shared interactive ColorBlock (hover lavender, press dim) ----
        // Matches the values already in UIStyle so behaviour is unchanged; here
        // for reuse on any Selectable (Button / Toggle / Dropdown).
        public static ColorBlock InteractiveColors()
        {
            return new ColorBlock
            {
                normalColor      = CscPalette.TintNormal,
                highlightedColor = CscPalette.TintHighlight,
                pressedColor     = CscPalette.TintPressed,
                selectedColor    = CscPalette.TintHighlight,
                disabledColor    = CscPalette.TintDisabled,
                colorMultiplier  = 1f,
                fadeDuration     = 0.1f,
            };
        }

        // ---- Add a crisp toon outline to any Graphic (the cel-shaded look) ----
        public static Outline AddToonOutline(GameObject go, float thickness = 2f)
        {
            Outline o = go.GetComponent<Outline>() ?? go.AddComponent<Outline>();
            o.effectColor = OutlineColor;
            o.effectDistance = new Vector2(thickness, -thickness);
            o.useGraphicAlpha = true;
            return o;
        }

        // ---- Lerp helper for meters (boost critical, heat cool->hot) ----
        public static Color HeatColor(float t01) => Color.Lerp(CscPalette.HeatCool, CscPalette.HeatHot, Mathf.Clamp01(t01));
    }
}
