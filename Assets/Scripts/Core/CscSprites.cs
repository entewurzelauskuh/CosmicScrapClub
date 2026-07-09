using System.Collections.Generic;
using UnityEngine;

namespace CubeFly.Core
{
    // Cached loader for the brand build-shape glyph sprites. The 12 PNGs live
    // under Assets/Resources/UI/Sprites/ so the code-built UI can reach them via
    // Resources.Load — the static UIStyle builders and the toolbar can't hold
    // serialized Inspector refs. Mirrors CscThemeBootstrap's Resources.Load<Font>.
    //
    // Glyphs are pre-coloured with baked ink outlines — render them on a white
    // Image with NO extra AddToonOutline (that would double the border).
    public static class CscSprites
    {
        const string Root = "UI/Sprites/";
        static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

        // Load a sprite by file base-name (e.g. "shape_slope"), cached. Returns
        // null if absent — callers keep their text label rather than crash.
        public static Sprite Get(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName)) return null;
            if (_cache.TryGetValue(spriteName, out Sprite s)) return s;
            s = Resources.Load<Sprite>(Root + spriteName);
            _cache[spriteName] = s;   // cache misses too, so a null never re-hits disk
            return s;
        }

        // The four armour-cube variants by MaterialRegistry index (0->A ... 3->D).
        // Out-of-range clamps to A so a 5th material can never mis-index.
        public static Sprite CubeMaterial(int index)
        {
            char v = (char)('A' + Mathf.Clamp(index, 0, 3));
            return Get($"shape_cube_mat{v}");
        }

        // The tiled yellow/black hazard stripe (toolbar floor trim).
        public static Sprite Hazard() => Get("tile_hazard_stripe");

        // Maps a build shape to its glyph, keyed on the shape's stable
        // displayName; armour "Cube" additionally varies by armed material.
        // Unmapped -> null (caller keeps the text label).
        public static Sprite ForShape(string shapeDisplayName, int armedMaterialIndex)
        {
            switch (shapeDisplayName)
            {
                case "Cube":     return CubeMaterial(armedMaterialIndex);
                case "Slope":    return Get("shape_slope");
                case "Pyramid":  return Get("shape_pyramid_mg");
                case "Cylinder": return Get("shape_cylinder_rocket");
                case "Laser":    return Get("shape_laser");
                case "Thruster": return Get("shape_thruster");
                case "Reactor":  return Get("shape_reactor");
                case "Shield":   return Get("shape_shield");
                default:         return null;
            }
        }
    }
}
