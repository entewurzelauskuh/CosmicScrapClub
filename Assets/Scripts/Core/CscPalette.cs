using UnityEngine;

namespace CubeFly.Core
{
    // ============================================================================
    // Cosmic Scrap Club — brand palette (generated from the design system).
    //
    // Channel floats are sRGB value/255, matching the convention already used in
    // UIStyle.cs and the Fly HUD scripts (e.g. shield (0.3,0.8,1) == #4DCCFF).
    // Functional HUD accents below are the EXACT values currently hard-coded in
    // the gameplay scripts — centralise on these so every call site agrees.
    //
    // Need a color not listed? Add it here, or parse a hex at startup with
    // UnityEngine.ColorUtility.TryParseHtmlString("#D9A441", out var c).
    // ============================================================================
    public static class CscPalette
    {
        // ---- Scrap & desert (dominant world palette) ----
        public static readonly Color Sand100            = new Color(0.957f, 0.894f, 0.757f);  // #F4E4C1 sun-bleached sand / light text
        public static readonly Color Sand200            = new Color(0.914f, 0.812f, 0.58f);  // #E9CF94 sandstone highlight
        public static readonly Color Ochre300           = new Color(0.851f, 0.643f, 0.255f);  // #D9A441 signature ochre (primary accent)
        public static readonly Color Ochre500           = new Color(0.784f, 0.522f, 0.118f);  // #C8851E deep ochre
        public static readonly Color Orange600          = new Color(0.784f, 0.322f, 0.118f);  // #C8521E burnt orange (secondary accent)
        public static readonly Color Rust700            = new Color(0.635f, 0.231f, 0.118f);  // #A23B1E oxidised red / rust
        public static readonly Color Rust800            = new Color(0.494f, 0.18f, 0.094f);  // #7E2E18 deep rust shadow
        public static readonly Color Brown800           = new Color(0.29f, 0.216f, 0.165f);  // #4A372A dusty brown
        public static readonly Color Brown900           = new Color(0.18f, 0.133f, 0.098f);  // #2E2219 darkest worn earth

        // ---- Hazard signage ----
        public static readonly Color HazardYellow       = new Color(0.949f, 0.718f, 0.02f);  // #F2B705 painted hazard / sticker yellow
        public static readonly Color HazardStripe       = new Color(0.11f, 0.078f, 0.047f);  // #1C140C the dark in hazard stripes

        // ---- Worn steel (ship & hangar metal) ----
        public static readonly Color Steel100           = new Color(0.725f, 0.698f, 0.651f);  // #B9B2A6 chipped-paint highlight
        public static readonly Color Steel300           = new Color(0.494f, 0.467f, 0.424f);  // #7E776C worn galvanised
        public static readonly Color Steel500           = new Color(0.322f, 0.298f, 0.267f);  // #524C44 gunmetal
        public static readonly Color Steel700           = new Color(0.204f, 0.188f, 0.169f);  // #34302B dark worn steel
        public static readonly Color Steel900           = new Color(0.118f, 0.106f, 0.094f);  // #1E1B18 scorched panel
        public static readonly Color Scorch             = new Color(0.078f, 0.063f, 0.047f);  // #14100C burn-mark black-brown
        public static readonly Color Ink                = new Color(0.043f, 0.035f, 0.024f);  // #0B0906 toon outline ink

        // ---- HUD surfaces (cool dark overlay chrome) ----
        public static readonly Color HudPanel           = new Color(0.07f, 0.06f, 0.09f, 0.86f);  // glassy combat panel
        public static readonly Color BackgroundIdle     = new Color(0.165f, 0.137f, 0.173f, 0.9f);  // #2a232c ghost button/control idle fill
        public static readonly Color HudCard            = new Color(0.1f, 0.1f, 0.14f, 0.92f);  // src: HangarSelect card bg

        // ---- Tints (src: UIStyle ColorBlock) ----
        public static readonly Color TintNormal         = new Color(1f, 1f, 1f, 1f);  // src: TintNormal
        public static readonly Color TintHighlight      = new Color(0.69f, 0.69f, 0.82f, 1f);  // hover/selected tint — darkened so hover reads distinctly vs the lighter idle fill
        public static readonly Color TintPressed        = new Color(0.55f, 0.55f, 0.7f, 1f);  // src: TintPressed
        public static readonly Color TintDisabled       = new Color(0.5f, 0.5f, 0.5f, 0.5f);  // src: TintDisabled
        public static readonly Color Label              = new Color(1f, 1f, 1f, 1f);  // src: LabelColor (white)

        // ---- Energy accents (shield / thruster / reactor / laser / boost) ----
        public static readonly Color Shield             = new Color(0.3f, 0.8f, 1f);  // src: FlyShieldIndicator shieldFillColor  #4DCCFF
        public static readonly Color Boost              = new Color(0.36f, 0.62f, 1f);  // src: FlyBoostBar fillColor              #5C9EFF
        public static readonly Color EnergyGlow         = new Color(0.749f, 0.902f, 1f);  // #BFE6FF blue-white emissive core

        // ---- Heat & fire (rockets / explosions / heat / warnings) ----
        public static readonly Color HeatCool           = new Color(1f, 0.6f, 0.2f);  // src: FlyHeatBar coolColor               #FF9933
        public static readonly Color HeatHot            = new Color(1f, 0.2f, 0.1f);  // src: FlyHeatBar hotColor                #FF3319
        public static readonly Color Critical           = new Color(0.95f, 0.25f, 0.2f);  // src: FlyBoostBar criticalColor      #F24033
        public static readonly Color WarnFlash          = new Color(1f, 0.45f, 0.3f);  // src: FlyBoostBar flashColor            #FF734D
        public static readonly Color Eject              = new Color(1f, 0.55f, 0.2f);  // src: FlyShieldIndicator ejectHintColor #FF8C33

        // ---- State readouts ----
        public static readonly Color PowerPositive      = new Color(0.4f, 1f, 0.5f);  // src: powerPositiveColor             #66FF80
        public static readonly Color PowerNegative      = new Color(1f, 0.4f, 0.35f);  // src: powerNegativeColor            #FF6659
        public static readonly Color ShieldDown         = new Color(0.4f, 0.4f, 0.45f);  // src: shieldDownColor             #66666B

        // ---- Faction / team colors (small accents & decals only) ----
        public static readonly Color TeamBlue           = new Color(0.184f, 0.659f, 1f);  // #2FA8FF Team 1 / friendly
        public static readonly Color TeamRed            = new Color(1f, 0.267f, 0.22f);  // #FF4438 Team 2 / enemy
        public static readonly Color TeamAmber          = new Color(1f, 0.769f, 0f);  // #FFC400 Team 3
        public static readonly Color TeamViolet         = new Color(0.698f, 0.42f, 1f);  // #B26BFF Team 4

        // ---- Armour material colors (Build shapes A-D) ----
        public static readonly Color MaterialA          = new Color(0.494f, 0.467f, 0.424f);  // A worn galvanised
        public static readonly Color MaterialB          = new Color(0.851f, 0.643f, 0.255f);  // B ochre
        public static readonly Color MaterialC          = new Color(0.784f, 0.322f, 0.118f);  // C burnt orange
        public static readonly Color MaterialD          = new Color(0.635f, 0.231f, 0.118f);  // D rust
    }
}