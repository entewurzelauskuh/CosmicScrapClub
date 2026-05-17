using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
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
                string dir = Path.GetDirectoryName(gen.meshAssetPath);
                if (string.IsNullOrEmpty(dir))
                {
                    Debug.LogError("[DuneGround] meshAssetPath must include a folder, e.g. Assets/Models/DesertGround.asset");
                    return;
                }

                Mesh built = gen.BuildMesh();
                Mesh mesh;

                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(gen.meshAssetPath);
                if (existing != null)
                {
                    EditorUtility.CopySerialized(built, existing);
                    Object.DestroyImmediate(built);
                    mesh = existing;
                }
                else
                {
                    Directory.CreateDirectory(dir);
                    AssetDatabase.CreateAsset(built, gen.meshAssetPath);
                    mesh = built;
                }
                AssetDatabase.SaveAssets();

                var mf = gen.GetComponent<MeshFilter>();
                var mc = gen.GetComponent<MeshCollider>();
                Undo.RecordObject(mf, "Generate Dune Mesh");
                mf.sharedMesh = mesh;
                if (mc != null)
                {
                    Undo.RecordObject(mc, "Generate Dune Mesh");
                    mc.sharedMesh = mesh;
                }
                EditorSceneManager.MarkSceneDirty(gen.gameObject.scene);

                Debug.Log("[DuneGround] generated " + mesh.vertexCount + " verts -> " + gen.meshAssetPath);
            }
        }
    }
}
