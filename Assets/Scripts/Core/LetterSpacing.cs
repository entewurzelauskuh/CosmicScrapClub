using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CubeFly.Core
{
    // Per-character horizontal spacing for legacy uGUI Text (which, unlike
    // TextMeshPro, has no letter-spacing property). A BaseMeshEffect that shifts
    // each glyph quad right by an accumulating offset, reset per line and
    // recentred so the Text's own alignment is preserved. Spacing is pixels
    // added per character gap. This is the Unity-typical approach.
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

            // Split glyphs into lines by their top-vertex y.
            List<int> lineCounts = new List<int>();
            float prevTop = float.NaN;
            int run = 0;
            for (int g = 0; g < glyphs; g++)
            {
                float top = _verts[g * 6].position.y;
                if (!float.IsNaN(prevTop) && Mathf.Abs(top - prevTop) > 0.1f)
                {
                    lineCounts.Add(run);
                    run = 0;
                }
                prevTop = top;
                run++;
            }
            lineCounts.Add(run);

            TextAnchor a = text.alignment;
            bool centre = a == TextAnchor.LowerCenter || a == TextAnchor.MiddleCenter || a == TextAnchor.UpperCenter;
            bool right  = a == TextAnchor.LowerRight  || a == TextAnchor.MiddleRight  || a == TextAnchor.UpperRight;

            int gi = 0;
            for (int line = 0; line < lineCounts.Count; line++)
            {
                int n = lineCounts[line];
                float total = (n - 1) * _spacing;
                float align = centre ? -total * 0.5f : right ? -total : 0f;
                for (int k = 0; k < n; k++, gi++)
                {
                    float dx = align + k * _spacing;
                    int b = gi * 6;
                    for (int v = 0; v < 6; v++)
                    {
                        UIVertex vert = _verts[b + v];
                        vert.position.x += dx;
                        _verts[b + v] = vert;
                    }
                }
            }

            vh.Clear();
            vh.AddUIVertexTriangleStream(_verts);
        }
    }
}
