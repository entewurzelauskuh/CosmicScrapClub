using UnityEngine;

namespace CubeFly.Core
{
    // Runtime-generated meshes for placeables that aren't built-in
    // primitives. Cached on first request; the resulting Mesh is shared
    // by every spawned instance (set via MeshFilter.sharedMesh).
    public static class PrimitiveMeshes
    {
        static Mesh _triangularPrism;

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
    }
}
