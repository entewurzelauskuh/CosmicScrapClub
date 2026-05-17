using System.IO;
using UnityEditor;
using UnityEngine;

namespace CubeFly.Desert
{
    [CustomEditor(typeof(DuneGroundGenerator))]
    public class DuneGroundGeneratorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var gen = (DuneGroundGenerator)target;

            EditorGUILayout.Space();
            if (GUILayout.Button("Generate Dune Mesh"))
            {
                Mesh mesh = gen.BuildMesh();

                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(gen.meshAssetPath);
                if (existing != null)
                {
                    existing.Clear();
                    EditorUtility.CopySerialized(mesh, existing);
                    mesh = existing;
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(gen.meshAssetPath));
                    AssetDatabase.CreateAsset(mesh, gen.meshAssetPath);
                }
                AssetDatabase.SaveAssets();

                gen.GetComponent<MeshFilter>().sharedMesh = mesh;
                var mc = gen.GetComponent<MeshCollider>();
                if (mc != null) mc.sharedMesh = mesh;
                EditorUtility.SetDirty(gen);

                Debug.Log("[DuneGround] generated " + mesh.vertexCount + " verts -> " + gen.meshAssetPath);
            }
        }
    }
}
