using UnityEngine;

namespace CubeFly.Core
{
    // Runtime-generated meshes for placeables that aren't built-in
    // primitives. Cached on first request; the resulting Mesh is shared
    // by every spawned instance (set via MeshFilter.sharedMesh).
    public static class PrimitiveMeshes
    {
        static Mesh _triangularPrism;
        static Mesh _squarePyramid;
        static Mesh _hollowCylinder;

        // 1×1×1 right-triangular prism centred at the origin. The
        // hypotenuse (slope) runs from the front-bottom edge to the
        // back-top edge, so the ramp faces forward-and-up. Designed to
        // fully occupy one grid cell — same footprint as the cube
        // primitive, so adjacency / face-detection raycasts and the
        // construct's BoxCollider remain correct. Triangle windings
        // produce outward-facing normals so single-sided rendering
        // (Unity's default back-face culling) shows every face.
        public static Mesh TriangularPrism
        {
            get
            {
                if (_triangularPrism == null) _triangularPrism = BuildPrism();
                return _triangularPrism;
            }
        }

        static Mesh BuildPrism()
        {
            const float h = 0.5f;
            Vector3 BBL = new Vector3(-h, -h, -h); // back-bottom-left
            Vector3 BBR = new Vector3( h, -h, -h); // back-bottom-right
            Vector3 FBR = new Vector3( h, -h,  h); // front-bottom-right
            Vector3 FBL = new Vector3(-h, -h,  h); // front-bottom-left
            Vector3 TBL = new Vector3(-h,  h, -h); // top-back-left
            Vector3 TBR = new Vector3( h,  h, -h); // top-back-right

            // Vertices are duplicated per face so each face gets a flat
            // normal after RecalculateNormals (no smoothing across edges).
            Vector3[] verts =
            {
                // Bottom (0..3)
                BBL, BBR, FBR, FBL,
                // Back (4..7)
                BBL, BBR, TBR, TBL,
                // Slope (8..11) — front-bottom to back-top
                FBL, FBR, TBR, TBL,
                // Left side triangle (12..14)
                BBL, FBL, TBL,
                // Right side triangle (15..17)
                BBR, TBR, FBR,
            };

            // Triangle winding: each face is wound so the right-hand-rule
            // cross product of (v1→v2) × (v1→v3) points OUTWARD. In Unity
            // (left-handed coords) that corresponds to clockwise screen-
            // space winding when viewed from outside — Unity's front-face
            // convention. RecalculateNormals derives correct outward
            // normals from this winding.
            int[] tris =
            {
                // Bottom (normal -Y) — verts 0..3 = BBL, BBR, FBR, FBL
                0, 1, 2,  0, 2, 3,
                // Back (normal -Z) — verts 4..7 = BBL, BBR, TBR, TBL
                4, 7, 6,  4, 6, 5,
                // Slope (normal +Y+Z) — verts 8..11 = FBL, FBR, TBR, TBL
                8, 9, 10,  8, 10, 11,
                // Left triangle (normal -X) — verts 12..14 = BBL, FBL, TBL
                12, 13, 14,
                // Right triangle (normal +X) — verts 15..17 = BBR, TBR, FBR
                15, 16, 17,
            };

            Mesh m = new Mesh { name = "TriangularPrism" };
            m.vertices = verts;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // 1×1×1 square-base right pyramid. Base at y=-0.5 (the only
        // valid attachment face per ShapeWeaponPyramid), apex at
        // y=+0.5. Designed to fully occupy one grid cell so the cell-
        // graph adjacency / face-detection raycasts behave the same
        // as for cubes and slopes. Triangle windings produce
        // outward-facing normals in Unity's left-handed CW-front
        // convention; verified analytically.
        public static Mesh SquarePyramid
        {
            get
            {
                if (_squarePyramid == null) _squarePyramid = BuildSquarePyramid();
                return _squarePyramid;
            }
        }

        static Mesh BuildSquarePyramid()
        {
            const float h = 0.5f;
            Vector3 BBL = new Vector3(-h, -h, -h);
            Vector3 BBR = new Vector3( h, -h, -h);
            Vector3 BFR = new Vector3( h, -h,  h);
            Vector3 BFL = new Vector3(-h, -h,  h);
            Vector3 APX = new Vector3( 0,  h,  0);

            // Vertices duplicated per face → flat per-face normals
            // after RecalculateNormals.
            Vector3[] verts =
            {
                // Base (0..3): BBL, BBR, BFR, BFL
                BBL, BBR, BFR, BFL,
                // Front (4..6): BFL, BFR, APX
                BFL, BFR, APX,
                // Right (7..9): BFR, BBR, APX
                BFR, BBR, APX,
                // Back (10..12): BBR, BBL, APX
                BBR, BBL, APX,
                // Left (13..15): BBL, BFL, APX
                BBL, BFL, APX,
            };

            int[] tris =
            {
                // Base (-Y normal): 0..3 wound (0,1,2)+(0,2,3) for outward.
                0, 1, 2,  0, 2, 3,
                // Front (+Y+Z normal): 4..6
                4, 5, 6,
                // Right (+X+Y normal): 7..9
                7, 8, 9,
                // Back (-Z+Y normal): 10..12
                10, 11, 12,
                // Left (-X+Y normal): 13..15
                13, 14, 15,
            };

            Mesh m = new Mesh { name = "SquarePyramid" };
            m.vertices = verts;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // 1×1×1 hollow cylinder centred at the origin. Axis along +Y;
        // outer radius 0.5 (fills the cell horizontally), inner radius
        // 0.25, height 1 (fills the cell vertically). The bottom
        // annulus at -Y is the only valid attachment face per
        // ShapeWeaponCylinder. The walls share vertices between
        // adjacent segments so RecalculateNormals produces smooth
        // radial normals (no visible polygonal facets); the top and
        // bottom annuli use a duplicate vertex ring so they keep
        // crisp ±Y normals at the wall-annulus seam.
        public static Mesh HollowCylinder
        {
            get
            {
                if (_hollowCylinder == null) _hollowCylinder = BuildHollowCylinder();
                return _hollowCylinder;
            }
        }

        static Mesh BuildHollowCylinder()
        {
            const int N = 32;          // segments around the circumference
            const float h = 0.5f;      // half-height (fills the unit cell)
            const float outerR = 0.5f; // matches cube half-width
            const float innerR = 0.25f;

            // Vertex layout (8 × N total):
            //   [0 .. N-1]        outer wall top ring          (smooth, normal radially outward)
            //   [N .. 2N-1]       outer wall bottom ring       (smooth)
            //   [2N .. 3N-1]      inner wall top ring          (smooth, normal radially inward)
            //   [3N .. 4N-1]      inner wall bottom ring       (smooth)
            //   [4N .. 5N-1]      top annulus outer ring       (flat +Y)
            //   [5N .. 6N-1]      top annulus inner ring       (flat +Y)
            //   [6N .. 7N-1]      bottom annulus outer ring    (flat -Y)
            //   [7N .. 8N-1]      bottom annulus inner ring    (flat -Y)
            Vector3[] verts = new Vector3[8 * N];

            for (int i = 0; i < N; i++)
            {
                float theta = i * (2f * Mathf.PI / N);
                float c = Mathf.Cos(theta);
                float s = Mathf.Sin(theta);

                Vector3 outerTop = new Vector3(outerR * c,  h, outerR * s);
                Vector3 outerBot = new Vector3(outerR * c, -h, outerR * s);
                Vector3 innerTop = new Vector3(innerR * c,  h, innerR * s);
                Vector3 innerBot = new Vector3(innerR * c, -h, innerR * s);

                verts[i]            = outerTop;
                verts[N + i]        = outerBot;
                verts[2 * N + i]    = innerTop;
                verts[3 * N + i]    = innerBot;
                verts[4 * N + i]    = outerTop;   // top annulus outer copy
                verts[5 * N + i]    = innerTop;   // top annulus inner copy
                verts[6 * N + i]    = outerBot;   // bottom annulus outer copy
                verts[7 * N + i]    = innerBot;   // bottom annulus inner copy
            }

            // 4 quads per segment × 2 triangles × 3 indices = 24 indices/segment.
            int[] tris = new int[24 * N];
            int t = 0;
            for (int i = 0; i < N; i++)
            {
                int j = (i + 1) % N;

                // Outer wall — normal radially outward.
                // Quad (a, b, c, d) = (outerTop[i], outerTop[j], outerBot[j], outerBot[i])
                // Winding chosen so (b-a) × (c-a) points outward (verified at θ=0).
                tris[t++] = i;          tris[t++] = j;          tris[t++] = N + j;
                tris[t++] = i;          tris[t++] = N + j;      tris[t++] = N + i;

                // Inner wall — normal radially INWARD. Reverse winding
                // relative to outer wall so the cross product points
                // toward the axis instead of away.
                tris[t++] = 2 * N + i;  tris[t++] = 3 * N + i;  tris[t++] = 3 * N + j;
                tris[t++] = 2 * N + i;  tris[t++] = 3 * N + j;  tris[t++] = 2 * N + j;

                // Top annulus — normal +Y.
                tris[t++] = 4 * N + i;  tris[t++] = 5 * N + i;  tris[t++] = 5 * N + j;
                tris[t++] = 4 * N + i;  tris[t++] = 5 * N + j;  tris[t++] = 4 * N + j;

                // Bottom annulus — normal -Y. Reverse winding so the
                // cross product points down instead of up.
                tris[t++] = 6 * N + i;  tris[t++] = 6 * N + j;  tris[t++] = 7 * N + j;
                tris[t++] = 6 * N + i;  tris[t++] = 7 * N + j;  tris[t++] = 7 * N + i;
            }

            Mesh m = new Mesh { name = "HollowCylinder" };
            m.vertices = verts;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
