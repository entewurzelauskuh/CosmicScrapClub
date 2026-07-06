using System;
using UnityEngine;

namespace CubeFly.Core
{
    // Persisted "desert cel look" graphics preference (PlayerPrefs-backed).
    // Applied by CubeFly.Desert.DesertLookController in FlyScene and toggled
    // from the SettingsMenu debug panel. Lives in Core so the UI never
    // depends on the experimental Desert namespace.
    public static class CelLookSettings
    {
        const string Key = "desert.celLook";
        static bool _loaded;
        static bool _enabled;

        // Fires whenever Enabled changes so the FlyScene controller can
        // re-apply the look live.
        public static event Action OnChanged;

        public static bool Enabled
        {
            get
            {
                if (!_loaded) { _enabled = PlayerPrefs.GetInt(Key, 1) != 0; _loaded = true; }
                return _enabled;
            }
            set
            {
                if (_loaded && _enabled == value) return;
                _enabled = value;
                _loaded = true;
                PlayerPrefs.SetInt(Key, value ? 1 : 0);
                PlayerPrefs.Save();
                OnChanged?.Invoke();
            }
        }
    }
}
