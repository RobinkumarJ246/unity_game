using System.Threading;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace ContentHouseEditor
{
    /// <summary>
    /// Batch helper: adds the packages the project needs (AI Navigation for
    /// NavMeshAgent/NavMeshSurface, uGUI for the code-built UI) before the
    /// main scripts land. Run via:
    ///   Unity.exe -batchmode -quit -projectPath <path> -executeMethod
    ///   ContentHouseEditor.PackageAdder.AddAll
    /// </summary>
    public static class PackageAdder
    {
        public static void AddAll()
        {
            Add("com.unity.ugui");
            Add("com.unity.ai.navigation");
        }

        static void Add(string id)
        {
            AddRequest req = null;
            try { req = Client.Add(id); }
            catch { return; } // already present / offline - manifest edit fallback below

            while (req != null && !req.IsCompleted) Thread.Sleep(50);
            if (req != null && req.Status == StatusCode.Failure)
                UnityEngine.Debug.LogWarning("Package add failed for " + id + ": " + req.Error.message);
        }
    }
}
