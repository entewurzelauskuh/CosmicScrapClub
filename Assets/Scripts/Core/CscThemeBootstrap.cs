using UnityEngine;

namespace CubeFly.Core
{
    // Assigns the brand fonts into CscTheme's slots once at startup. Kept as a
    // standalone [RuntimeInitializeOnLoadMethod] to match the project's other
    // self-bootstrapping systems (UIManager, PauseMenu, LogBootstrapper). If a
    // Resources.Load returns null, CscTheme's *Or accessors fall back to the
    // builtin font, so a missing file never breaks the UI. B1 leaves UIStyle
    // using the builtin font, so these slots are populated-but-unused until B2
    // wires them in.
    public static class CscThemeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            CscTheme.Display = Resources.Load<Font>("Fonts/Anton-Regular");
            CscTheme.Body    = Resources.Load<Font>("Fonts/Saira-SemiBold");
            CscTheme.Cond    = Resources.Load<Font>("Fonts/SairaCondensed-Bold");
            CscTheme.Stencil = Resources.Load<Font>("Fonts/SairaStencilOne-Regular");
        }
    }
}
