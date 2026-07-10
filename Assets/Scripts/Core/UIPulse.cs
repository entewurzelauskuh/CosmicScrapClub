using UnityEngine;
using UnityEngine.UI;

namespace CubeFly.Core
{
    // Gentle sine ALPHA pulse on a Graphic, active only while this component
    // is enabled. Used for "attention" states — e.g. the hangar power readout
    // while the construct is in a power deficit. Toggle .enabled to start/stop.
    //
    // Unscaled-time driven so it animates even if a caller pauses the game.
    // The host owns the graphic's RGB + steady alpha: the pulse only rides the
    // alpha channel between minAlpha and the base alpha snapshotted when it's
    // enabled, and restores that base alpha on disable — so switching the
    // host's colour (e.g. green->red) and re-enabling picks up the new alpha.
    public class UIPulse : MonoBehaviour
    {
        [Tooltip("Seconds for one full pulse cycle.")]
        public float period = 0.8f;
        [Tooltip("Alpha at the dim end of the pulse; the bright end is the graphic's base alpha.")]
        [Range(0f, 1f)] public float minAlpha = 0.4f;

        Graphic _graphic;
        float _baseAlpha = 1f;

        void Awake()
        {
            _graphic = GetComponent<Graphic>();
            if (_graphic != null) _baseAlpha = _graphic.color.a;
        }

        // Re-snapshot the base alpha each time pulsing starts — the host may
        // have re-coloured the graphic (new RGB + full alpha) while we were off.
        void OnEnable()
        {
            if (_graphic != null) _baseAlpha = _graphic.color.a;
        }

        // Leave the graphic at its steady base alpha when we stop.
        void OnDisable()
        {
            if (_graphic == null) return;
            Color c = _graphic.color;
            c.a = _baseAlpha;
            _graphic.color = c;
        }

        void Update()
        {
            if (_graphic == null) return;
            float t = 0.5f + 0.5f * Mathf.Sin(
                Time.unscaledTime * (2f * Mathf.PI / Mathf.Max(0.01f, period)));
            Color c = _graphic.color;
            c.a = Mathf.Lerp(minAlpha, _baseAlpha, t);
            _graphic.color = c;
        }
    }
}
