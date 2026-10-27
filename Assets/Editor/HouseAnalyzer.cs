using System.Text;
using UnityEditor;
using UnityEngine;

namespace ContentHouseEditor
{
    /// <summary>
    /// Loads Assets/HousePack/House.fbx, walks the hierarchy and reports every
    /// gameplay-relevant node (doors, windows, floors, furniture landmarks) with
    /// world positions and bounds. Output goes to the Console / Editor.log.
    /// Menu: Content House > Analyze House Model
    /// </summary>
    public static class HouseAnalyzer
    {
        const string HousePath = "Assets/HousePack/House.fbx";
        const string CollidersPath = "Assets/HousePack/House_Colliders.fbx";

        [MenuItem("Content House/Analyze House Model")]
        public static void Run()
        {
            var proto = AssetDatabase.LoadAssetAtPath<GameObject>(HousePath);
            if (proto == null)
            {
                Debug.LogError("HouseAnalyzer: " + HousePath + " not imported yet. Let the editor refresh first.");
                return;
            }

            var inst = (GameObject)Object.Instantiate(proto);
            inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var sb = new StringBuilder();
            sb.AppendLine("=== HOUSE ANALYSIS ===");
            sb.AppendLine("root localScale: " + inst.transform.localScale.ToString("F4"));

            // Overall bounds of everything, and bounds excluding neighbourhood filler.
            var all = new Bounds(); bool any = false;
            var houseOnly = new Bounds(); bool anyHouse = false;
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>())
            {
                all = Encap(all, r, ref any);
                if (!IsFiller(r.name))
                    houseOnly = Encap(houseOnly, r, ref anyHouse);
            }
            sb.AppendLine("ALL bounds: c=" + all.center.ToString("F2") + " s=" + all.size.ToString("F2"));
            sb.AppendLine("HOUSE(no trees/fences) bounds: c=" + houseOnly.center.ToString("F2") + " s=" + houseOnly.size.ToString("F2"));

            // Grouped sections
            Section(sb, inst, "DOORS", @"^Door(_)?\d+");
            Section(sb, inst, "DOOR FRAMES", @"^Door_frame");
            Section(sb, inst, "WINDOW FRAMES", @"^Window_frame");
            Section(sb, inst, "WINDOWS/GLASS", @"^Windows?$");
            Section(sb, inst, "FLOORS", @"^Floor");
            Section(sb, inst, "CEILINGS", @"^Ceiling");
            Section(sb, inst, "STAIRS", @"[Ss]tair");
            Section(sb, inst, "KITCHEN", @"[Kk]itchen|[Ss]ink|[Ss]tove|[Ff]ridge|[Cc]ounter");
            Section(sb, inst, "BATH", @"[Bb]ath|[Tt]oilet");
            Section(sb, inst, "BEDS", @"^bed|Bed_");
            Section(sb, inst, "LIVING", @"TV|Sofa|[Cc]ouch|[Ll]iving");
            Section(sb, inst, "TABLES/CHAIRS", @"^Table|^Chair");
            Section(sb, inst, "STORAGE", @"[Cc]upboard|[Ss]helf|[Cc]abinet|[Ww]ardrobe|[Cc]hest|[Dd]rawer");
            Section(sb, inst, "LAMPS/LIGHTS", @"[Ll]amp|[Ll]ight");
            Section(sb, inst, "GARAGE", @"[Gg]arage");
            Section(sb, inst, "LADDERS", @"[Ll]adder");
            Section(sb, inst, "PORCH/TERRACE", @"[Pp]orch|[Tt]errace|[Bb]alcony");

            // Vertical connectors: any mesh inside the house footprint that
            // spans between floor levels (stairs candidates).
            var vert = new StringBuilder();
            int vc = 0;
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>())
            {
                var b = r.bounds;
                if (b.center.x < -11 || b.center.x > 5) continue;
                if (b.center.z < -15 || b.center.z > 1) continue;
                if (b.size.y < 1.6f) continue;
                if (IsFiller(r.name)) continue;
                vert.AppendLine("   " + Pad(r.name) + " pos=" + r.transform.position.ToString("F2")
                    + " boundsY=" + b.min.y.ToString("F2") + ".." + b.max.y.ToString("F2")
                    + " size=" + b.size.ToString("F2"));
                vc++;
            }
            sb.AppendLine("--- VERTICAL-SPAN MESHES in house (" + vc + ")");
            sb.Append(vert);

            // FULL inventory: every renderer, unsuppressed, file only.
            var inv = new StringBuilder();
            int invCount = 0;
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>())
            {
                inv.AppendLine(Pad(r.name) + " pos=" + r.transform.position.ToString("F2")
                    + " size=" + r.bounds.size.ToString("F2")
                    + " boundsY=" + r.bounds.min.y.ToString("F2") + ".." + r.bounds.max.y.ToString("F2"));
                invCount++;
            }
            sb.AppendLine("--- FULL INVENTORY (" + invCount + ")");
            sb.Append(inv);

            int filler = 0;
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>())
                if (IsFiller(r.name)) filler++;
            sb.AppendLine("filler objects (trees/bushes/grass/fence/lamppost/car): " + filler);

            // Collider model
            var colProto = AssetDatabase.LoadAssetAtPath<GameObject>(CollidersPath);
            if (colProto != null)
            {
                var colInst = (GameObject)Object.Instantiate(colProto);
                colInst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var cb = new Bounds(); bool anyC = false;
                var list = new StringBuilder();
                int n = 0;
                foreach (var r in colInst.GetComponentsInChildren<MeshRenderer>())
                {
                    cb = Encap(cb, r, ref anyC);
                    list.AppendLine("   " + Pad(r.name) + " pos=" + r.transform.position.ToString("F2") + " size=" + r.bounds.size.ToString("F2"));
                    n++;
                }
                sb.AppendLine("COLLIDER MODEL: " + n + " meshes, bounds c=" + cb.center.ToString("F2") + " s=" + cb.size.ToString("F2"));
                if (n <= 30) sb.Append(list.ToString());
                Object.DestroyImmediate(colInst);
            }

            Object.DestroyImmediate(inst);
            var report = sb.ToString();
            Debug.Log(report);
            // Robust: also write to disk so it survives editor restarts / log rotation.
            System.IO.File.WriteAllText("C:/Unity_Games/house_analysis.txt", report);
            EditorUtility.DisplayDialog("House Analyzer",
                "Analysis complete - report saved to C:/Unity_Games/house_analysis.txt\nand printed to the Console.", "OK");
        }

        static bool IsFiller(string name) =>
            name.StartsWith("Tree") || name.StartsWith("Bush") || name.StartsWith("grass") ||
            name.StartsWith("Grass") || name.StartsWith("Fence") || name.StartsWith("Lamppost") ||
            name.StartsWith("Road") || name.StartsWith("Car") || name.StartsWith("Carrier");

        static Bounds Encap(Bounds b, Renderer r, ref bool any)
        {
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
            return b;
        }

        static string Pad(string s) { s = s ?? ""; while (s.Length < 26) s += " "; return s; }

        static void Section(StringBuilder sb, GameObject root, string title, string pattern)
        {
            var re = new System.Text.RegularExpressions.Regex(pattern);
            var lines = new StringBuilder();
            int count = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>())
            {
                if (t == root.transform) continue;
                if (!re.IsMatch(t.name)) continue;
                var mr = t.GetComponent<MeshRenderer>();
                if (mr == null) continue;
                lines.AppendLine("   " + Pad(t.name)
                    + " pos=" + t.position.ToString("F2")
                    + " size=" + mr.bounds.size.ToString("F2"));
                count++;
            }
            sb.AppendLine("--- " + title + " (" + count + ")");
            if (count > 0 && count <= 60) sb.Append(lines);
        }
    }
}
