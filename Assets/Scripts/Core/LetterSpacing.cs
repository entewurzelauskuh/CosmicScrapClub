using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CubeFly.Core
{
    // Per-character horizontal spacing for legacy uGUI Text (which, unlike
    // TextMeshPro, has no letter-spacing property). A BaseMeshEffect that spreads
    // each glyph quad right by index*spacing, recentred for the Text's alignment.
    // Single-line only (all our spaced labels are single-line); a flat spread
    // avoids mis-grouping sub-cap-height glyphs (e.g. the comma in "YES, DELETE")
    // as separate lines. Spacing is pixels added per character gap.
    [RequireComponent(typeof(Text))]
    public class LetterSpacing : BaseMeshEffect
    {
        [SerializeField] float _spacing;
        static readonly List<UIVertex> _verts = new List<UIVertex>();

        public float Spacing
        {
            get => _spacing;
            set { _spacing = value; if (graphic != null) graphic.SetVerticesDirty(); }
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || _spacing == 0f) return;
            Text text = GetComponent<Text>();
            if (text == null) return;

            vh.GetUIVertexStream(_verts);
            int glyphs = _verts.Count / 6;   // 6 verts (two triangles) per glyph
            if (glyphs <= 1) return;

            float total = (glyphs - 1) * _spacing;
            TextAnchor a = text.alignment;
            bool centre = a == TextAnchor.LowerCenter || a == TextAnchor.MiddleCenter || a == TextAnchor.UpperCenter;
            bool right  = a == TextAnchor.LowerRight  || a == TextAnchor.MiddleRight  || a == TextAnchor.UpperRight;
            float align = centre ? -total * 0.5f : right ? -total : 0f;

            for (int g = 0; g < glyphs; g++)
            {
                float dx = align + g * _spacing;
                int b = g * 6;
                for (int v = 0; v < 6; v++)
                {
                    UIVertex vert = _verts[b + v];
                    vert.position.x += dx;
                    _verts[b + v] = vert;
                }
            }

            vh.Clear();
            vh.AddUIVertexTriangleStream(_verts);
        }
    }
}
