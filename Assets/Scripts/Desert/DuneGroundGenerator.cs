using UnityEngine;

namespace CubeFly.Desert
{
    /// <summary>
    /// Builds a noise-displaced grid mesh for the desert dune ground.
    /// Drive it from the custom inspector's "Generate Dune Mesh" button.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class DuneGroundGenerator : MonoBehaviour
    {
        [Header("Extent")]
        public float size = 200f;
        public int resolution = 200;

        [Header("Layered noise")]
        public int seed = 12345;
        public float swellAmplitude = 6f;
        public float swellFrequency = 0.012f;
        public float duneAmplitude = 2.5f;
        public float duneFrequency = 0.05f;
        public float rippleAmplitude = 0.4f;
        public float rippleFrequency = 0.22f;

        [Header("Output")]
        public string meshAssetPath = "Assets/Models/DesertGround.asset";

        public Mesh BuildMesh()
        {
            int n = Mathf.Max(2, resolution);
            float extent = Mathf.Max(1f, size);
            int verts = n + 1;
            var mesh = new Mesh { name = "DesertGround" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            var positions = new Vector3[verts * verts];
            var uvs = new Vector2[verts * verts];
            float step = extent / n;
            float half = extent * 0.5f;

            var rng = new System.Random(seed);
            Vector2 swellOff  = new Vector2((float)rng.NextDouble() * 1000f, (float)rng.NextDouble() * 1000f);
            Vector2 duneOff   = new Vector2((float)rng.NextDouble() * 1000f, (float)rng.NextDouble() * 1000f);
            Vector2 rippleOff = new Vector2((float)rng.NextDouble() * 1000f, (float)rng.NextDouble() * 1000f);

            for (int z = 0; z < verts; z++)
            {
                for (int x = 0; x < verts; x++)
                {
                    float wx = x * step - half;
                    float wz = z * step - half;
                    float h = 0f;
                    h += (Mathf.PerlinNoise(wx * swellFrequency + swellOff.x,  wz * swellFrequency + swellOff.y)  - 0.5f) * 2f * swellAmplitude;
                    h += (Mathf.PerlinNoise(wx * duneFrequency + duneOff.x,    wz * duneFrequency + duneOff.y)    - 0.5f) * 2f * duneAmplitude;
                    h += (Mathf.PerlinNoise(wx * rippleFrequency + rippleOff.x, wz * rippleFrequency + rippleOff.y) - 0.5f) * 2f * rippleAmplitude;
                    int i = z * verts + x;
                    positions[i] = new Vector3(wx, h, wz);
                    uvs[i] = new Vector2((float)x / n, (float)z / n);
                }
            }

            var tris = new int[n * n * 6];
            int t = 0;
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    int bl = z * verts + x;
                    int br = bl + 1;
                    int tl = bl + verts;
                    int tr = tl + 1;
                    tris[t++] = bl; tris[t++] = tl; tris[t++] = tr;
                    tris[t++] = bl; tris[t++] = tr; tris[t++] = br;
                }
            }

            mesh.vertices = positions;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
