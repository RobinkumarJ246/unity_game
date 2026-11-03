using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ContentHouse.EditorTools
{
    /// <summary>
    /// Player-build safety net: shaders that the code looks up by name at runtime (Shader.Find) are stripped
    /// from builds unless listed in Always Included Shaders, and TvVideo reads the TV mesh at runtime
    /// (needs Read/Write on the house model). Runs on every editor load; it only changes things when needed.
    /// </summary>
    [InitializeOnLoad]
    static class BuildFixes
    {
        static readonly string[] Shaders = { "Unlit/Texture", "Unlit/Color", "Sprites/Default", "Standard" };

        static BuildFixes()
        {
            EditorApplication.delayCall += Apply;
        }

        static void Apply()
        {
            var gs = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
            if (gs != null)
            {
                var so = new SerializedObject(gs);
                var arr = so.FindProperty("m_AlwaysIncludedShaders");
                bool changed = false;
                foreach (var name in Shaders)
                {
                    var sh = Shader.Find(name);
                    if (sh == null) continue;
                    bool has = false;
                    for (int i = 0; i < arr.arraySize; i++)
                        if (arr.GetArrayElementAtIndex(i).objectReferenceValue == sh) { has = true; break; }
                    if (has) continue;
                    arr.InsertArrayElementAtIndex(arr.arraySize);
                    arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
                    changed = true;
                }
                if (changed) { so.ApplyModifiedProperties(); AssetDatabase.SaveAssets(); Debug.Log("[BuildFixes] added runtime shaders to Always Included Shaders"); }
            }

            var imp = AssetImporter.GetAtPath("Assets/HousePack/House.fbx") as ModelImporter;
            if (imp != null && !imp.isReadable)
            {
                imp.isReadable = true;
                imp.SaveAndReimport();
                Debug.Log("[BuildFixes] House.fbx set Read/Write enabled (TV screen quad)");
            }
        }
    }
}
