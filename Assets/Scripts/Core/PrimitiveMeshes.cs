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
        // construct's BoxCollider remain correct.
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

            int[] tris =
            {
                // Bottom (CCW from below — normal -Y)
                0, 3, 2,  0, 2, 1,
                // Back (CCW from -Z — normal -Z)
                4, 7, 6,  4, 6, 5,
                // Slope (CCW from outside — normal points +Z+Y)
                8, 11, 10,  8, 10, 9,
                // Left triangle (CCW from -X)
                12, 14, 13,
                // Right triangle (CCW from +X)
                15, 17, 16,
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
