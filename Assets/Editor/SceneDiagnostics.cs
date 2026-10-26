using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace ContentHouseEditor
{
    /// <summary>
    /// One-click scene diagnostics: reports player state, what colliders block
    /// the player, world-space bounds of every Kenney furniture instance, and
    /// input settings. Output goes to the Console and Editor.log.
    /// Menu: Content House > Diagnostics
    /// </summary>
    public static class SceneDiagnostics
    {
        [MenuItem("Content House/Diagnostics")]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== CONTENT HOUSE DIAGNOSTICS ===");

            // ---- Player state ----
            var player = Object.FindFirstObjectByType<ContentHouse.Player.PlayerController>();
            var gm = Object.FindFirstObjectByType<ContentHouse.Core.GameManager>();
            if (player == null)
            {
                sb.AppendLine("PLAYER: NOT FOUND IN SCENE!");
            }
            else
            {
                sb.AppendLine("PLAYER pos: " + player.transform.position.ToString("F2")
                    + " yaw: " + player.transform.eulerAngles.y.ToString("F0"));
                var cc = player.GetComponent<CharacterController>();
                sb.AppendLine("PLAYER CC: height=" + cc.height.ToString("F2")
                    + " radius=" + cc.radius.ToString("F2")
                    + " center=" + cc.center.ToString("F2")
                    + " enabled=" + cc.enabled);
                sb.AppendLine("GM.PlayerLocked: " + (gm != null ? gm.PlayerLocked.ToString() : "GM MISSING"));
                sb.AppendLine("GM.Phase: " + (gm != null ? gm.Phase.ToString() : "?"));

                // What overlaps the player right now?
                var overlaps = Physics.OverlapSphere(player.transform.position + Vector3.up * 0.9f, 0.55f);
                sb.AppendLine("OVERLAPS at player (" + overlaps.Length + "):");
                foreach (var o in overlaps)
                    sb.AppendLine("   - " + o.name + " (root: " + o.transform.root.name + ") layer=" + o.gameObject.layer);
            }

            // ---- Enemy ----
            var enemy = Object.FindFirstObjectByType<ContentHouse.Enemy.EnemyAI>();
            sb.AppendLine("ENEMY activeInHierarchy: " + (enemy != null ? enemy.gameObject.activeInHierarchy.ToString() : "NOT FOUND"));

            // ---- Furniture world bounds (scale check) ----
            sb.AppendLine("--- FURNITURE WORLD BOUNDS (scale sanity) ---");
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                var n = mr.name;
                bool interesting = n.Contains("loungeSofa") || n.Contains("tableCoffee") || n.Contains("cabinetTelevision")
                    || n.Contains("televisionModern") || n.Contains("bedDouble") || n.Contains("cabinetBed")
                    || n.Contains("kitchenFridge") || n.Contains("kitchenFridgeSmall") || n.Contains("desk")
                    || n.Contains("chairDesk") || n.Contains("laptop") || n.Contains("cardboardBoxClosed")
                    || n.Contains("kitchenStove") || n.Contains("kitchenSink") || n.Contains("bookcaseClosed");
                if (!interesting) continue;
                var b = mr.bounds;
                sb.AppendLine(n + " @" + b.center.ToString("F1") + " size=" + b.size.ToString("F2"));
            }

            // ---- NavMesh presence ----
            var agent = Object.FindFirstObjectByType<NavMeshAgent>();
            sb.AppendLine("NavMeshAgent present: " + (agent != null));
            if (agent != null)
            {
                NavMeshHit nh;
                bool onMesh = NavMesh.SamplePosition(agent.transform.position, out nh, 1.0f, NavMesh.AllAreas);
                sb.AppendLine("enemy sample on NavMesh: " + onMesh);
            }

            // ---- Input settings (read from asset file - API varies by version) ----
            var imText = System.IO.File.ReadAllText("ProjectSettings/InputManager.asset");
            sb.AppendLine("InputManager Horizontal refs: " + CountOccurrences(imText, "Horizontal")
                + ", Vertical refs: " + CountOccurrences(imText, "Vertical"));

            Debug.Log(sb.ToString());
            EditorUtility.DisplayDialog("Diagnostics",
                "Diagnostics written to the Console / Editor.log.\nPlease tell the agent to read it.", "OK");
        }

        static int CountOccurrences(string s, string sub)
        {
            int count = 0, i = 0;
            while ((i = s.IndexOf(sub, i)) >= 0) { count++; i += sub.Length; }
            return count;
        }
    }
}
