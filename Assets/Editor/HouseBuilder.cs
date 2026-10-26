using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Unity.AI.Navigation;

namespace ContentHouseEditor
{
    /// <summary>
    /// Procedural level generator: builds the whole two-story house + basement
    /// from primitives (shell) and Kenney CC0 furniture FBX models, wires every
    /// runtime component, bakes the NavMesh and saves the scene.
    /// Menu: Content House > Generate House Scene.
    /// Batch: -executeMethod ContentHouseEditor.HouseBuilder.GenerateAll
    /// </summary>
    public static class HouseBuilder
    {
        // ---------- paths & tuning ----------
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string MaterialsDir = "Assets/Materials";
        const string FbxDir = "Assets/Kenney/FBX";
        const float WallT = 0.16f;       // wall thickness
        const float ModelScale = 1f;     // tune if Kenney models import too big/small

        // ---------- materials ----------
        static Material wallLower, wallUpper, ceiling, floorWood, floorTile, concrete,
            grass, roof, fenceMat, trim, metalDark, glassMat, glowScreen, glowLed;

        // ---------- collections ----------
        static readonly List<DoorInteractableBuild> doors = new List<DoorInteractableBuild>();
        static readonly List<Light> houseLights = new List<Light>();
        struct DoorInteractableBuild { public ContentHouse.Interaction.DoorInteractable Door; public bool StartOpen; }

        static ContentHouse.Interaction.DoorInteractable patioDoor, mrIgDoor, basementDoor, closetDoor, steveDoor,
            kitchenDiningDoor, frontDoor, entryClosetDoor;

        [MenuItem("Content House/Generate House Scene")]
        public static void GenerateAll()
        {
            EnsureFolders();
            MakeMaterials();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ApplyRenderSettings();

            var root = new GameObject("HouseRoot").transform;
            var navSurface = root.gameObject.AddComponent<NavMeshSurface>();
            navSurface.collectObjects = CollectObjects.All;

            BuildOutdoor(root);
            BuildHouseShell(root);
            BuildStairsAndBasement(root);
            BuildDoorsAndWindows(root);
            BuildFurniture(root);
            BuildLighting(root);
            BuildHideSpots(root);
            BuildCCTV(root);
            BuildInteractableDevices(root);
            BuildWaypointsAndAnchors(root);

            var (player, pc, cam, flash) = BuildPlayer(root);
            var enemy = BuildEnemy(root);
            var systems = BuildSystems(root);
            var ui = BuildUI(root);

            WireEverything(systems, ui, player, pc, cam, flash, enemy);

            // Bake navmesh with every door open, then close them for gameplay.
            foreach (var d in doors) d.Door.ForceOpenState();
            navSurface.BuildNavMesh();
            foreach (var d in doors)
            {
                if (d.StartOpen) d.Door.ForceOpenState();
                else d.Door.ForceClosedState();
            }

            ApplyPlayerSettings();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("HouseBuilder: scene generated at " + ScenePath);
        }

        // =====================================================================
        // Folders / materials / settings
        // =====================================================================

        static void EnsureFolders()
        {
            foreach (var d in new[] { "Assets/Scenes", MaterialsDir, "Assets/Kenney/FBX", "Assets/Resources/Sfx" })
                if (!AssetDatabase.IsValidFolder(d))
                {
                    var parent = Path.GetDirectoryName(d).Replace('\\', '/');
                    var leaf = Path.GetFileName(d);
                    if (!AssetDatabase.IsValidFolder(parent)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(parent));
                    AssetDatabase.CreateFolder(parent, leaf);
                }
            AssetDatabase.Refresh();
        }

        static Material Std(string name, Color c, float metallic = 0f, float smooth = 0.15f)
        {
            var path = MaterialsDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_Color", c);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Glossiness", smooth);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Emissive(string name, Color baseColor, Color emission, float intensity = 1.2f)
        {
            var m = Std(name, baseColor);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_EmissionColor", emission * intensity);
            return m;
        }

        static void MakeMaterials()
        {
            wallLower = Std("WallLower", new Color(0.55f, 0.50f, 0.44f));
            wallUpper = Std("WallUpper", new Color(0.44f, 0.46f, 0.50f));
            ceiling = Std("Ceiling", new Color(0.72f, 0.71f, 0.68f));
            floorWood = Std("FloorWood", new Color(0.40f, 0.28f, 0.18f), smooth: 0.25f);
            floorTile = Std("FloorTile", new Color(0.56f, 0.58f, 0.56f), smooth: 0.4f);
            concrete = Std("Concrete", new Color(0.34f, 0.34f, 0.36f));
            grass = Std("Grass", new Color(0.12f, 0.15f, 0.08f));
            roof = Std("Roof", new Color(0.11f, 0.11f, 0.12f));
            fenceMat = Std("Fence", new Color(0.18f, 0.16f, 0.14f));
            trim = Std("TrimWood", new Color(0.45f, 0.32f, 0.22f));
            metalDark = Std("MetalDark", new Color(0.25f, 0.26f, 0.28f), metallic: 0.6f, smooth: 0.5f);
            glowScreen = Emissive("GlowScreen", new Color(0.05f, 0.05f, 0.08f), new Color(0.4f, 0.6f, 1f));
            glowLed = Emissive("GlowLed", Color.black, Color.red);

            var glassPath = MaterialsDir + "/Glass.mat";
            glassMat = AssetDatabase.LoadAssetAtPath<Material>(glassPath);
            if (glassMat == null)
            {
                glassMat = new Material(Shader.Find("Legacy Shaders/Transparent/Diffuse"));
                AssetDatabase.CreateAsset(glassMat, glassPath);
            }
            glassMat.SetColor("_Color", new Color(0.55f, 0.65f, 0.7f, 0.22f));
        }

        static void ApplyRenderSettings()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.010f;
            RenderSettings.fogColor = new Color(0.014f, 0.016f, 0.026f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.09f, 0.095f, 0.13f);
        }

        static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "Upcheck Games";
            PlayerSettings.productName = "Content House";   // changeable later
            // Gamma space: the dim night lights read correctly; Linear made
            // the interior near-black during the first playtest.
            PlayerSettings.colorSpace = ColorSpace.Gamma;
        }

        // =====================================================================
        // Geometry helpers
        // =====================================================================

        static GameObject Prim(string name, Transform parent, Vector3 pos, Vector3 size,
            Material mat, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.position = pos;
            go.transform.localScale = size;
            if (!collider) Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>
        /// Kenney furniture FBX files import ~4.9x larger than real-world meters
        /// (measured: sofa 9.8m, bed 11.25m, fridge 9.2m tall at scale 1) and
        /// their mesh geometry sits offset from the object pivot. So every
        /// model gets the empirical scale fix and is then RECENTERED so the
        /// visual bounds-center lands on the target XZ and the model's base
        /// sits exactly on the target Y.
        /// </summary>
        const float KenneyScaleFix = 1f / 4.9f;

        static GameObject Model(string modelName, Transform parent, Vector3 pos, float yawDeg,
            float scale = 1f, bool addCollider = true)
        {
            var proto = AssetDatabase.LoadAssetAtPath<GameObject>(FbxDir + "/" + modelName + ".fbx");
            if (proto == null)
            {
                Debug.LogWarning("HouseBuilder: missing model " + modelName + " (primitive fallback expected at call site).");
                return null;
            }
            var go = Object.Instantiate(proto, parent);
            go.name = modelName;
            go.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
            go.transform.localScale = Vector3.one * (scale * ModelScale * KenneyScaleFix);
            RecenterTo(go, pos);
            if (addCollider) FitCollider(go);
            return go;
        }

        /// <summary>Shift the object so its visual bounds-center XZ = pos.XZ and bounds.min.Y = pos.Y.</summary>
        static void RecenterTo(GameObject go, Vector3 pos)
        {
            var renderers = go.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0) { go.transform.position = pos; return; }
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            go.transform.position += new Vector3(
                pos.x - b.center.x,
                pos.y - b.min.y,
                pos.z - b.center.z);
        }

        static void FitCollider(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            var col = go.AddComponent<BoxCollider>();
            col.center = go.transform.InverseTransformPoint(bounds.center);
            var localSize = go.transform.InverseTransformVector(bounds.size);
            col.size = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));
        }

        struct Gap
        {
            public float Center, Width, YBottom, YTop;
            public static Gap Door(float center, float width = 1.0f, float yTop = 2.1f)
                => new Gap { Center = center, Width = width, YBottom = 0f, YTop = yTop };
            public static Gap Window(float center, float width, float yBottom = 1.0f, float yTop = 2.2f)
                => new Gap { Center = center, Width = width, YBottom = yBottom, YTop = yTop };
            public static Gap Arch(float center, float width)
                => new Gap { Center = center, Width = width, YBottom = -1f, YTop = 999f };
        }

        /// <summary>Build a wall from a to b (heights absolute Y) with gaps by along-wall distance.</summary>
        static void Wall(string name, Transform parent, Vector3 a, Vector3 b,
            float yBottom, float yTop, Material mat, params Gap[] gaps)
        {
            var dir = (b - a).normalized;
            float len = Vector3.Distance(a, b);
            var rot = Quaternion.LookRotation(dir, Vector3.up);

            var sorted = new List<Gap>(gaps);
            sorted.Sort((x, y) => x.Center.CompareTo(y.Center));

            float cursor = 0f;
            void Solid(float from, float to, float bottom, float top)
            {
                if (to - from < 0.01f || top - bottom < 0.01f) return;
                var mid = a + dir * ((from + to) * 0.5f) + Vector3.up * ((bottom + top) * 0.5f);
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name + "_seg";
                go.transform.SetParent(parent);
                go.transform.SetPositionAndRotation(mid, rot);
                go.transform.localScale = new Vector3(WallT, top - bottom, to - from);
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }

            foreach (var g in sorted)
            {
                float gFrom = Mathf.Max(0f, g.Center - g.Width * 0.5f);
                float gTo = Mathf.Min(len, g.Center + g.Width * 0.5f);
                Solid(cursor, gFrom, yBottom, yTop);
                // Sill and header pieces around the opening.
                if (g.YBottom > yBottom) Solid(gFrom, gTo, yBottom, g.YBottom);
                if (g.YTop < yTop) Solid(gFrom, gTo, g.YTop, yTop);
                cursor = gTo;
            }
            Solid(cursor, len, yBottom, yTop);
        }

        /// <summary>Hinged door. Hinge sits at hingePos; wall runs along wallDir.</summary>
        static ContentHouse.Interaction.DoorInteractable BuildDoor(string name, Transform parent, Vector3 hingePos,
            Vector3 wallDir, float width, Material mat, bool locked = false, string lockedMsg = "",
            string taskId = "", bool startOpen = false)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent);
            root.transform.SetPositionAndRotation(hingePos, Quaternion.LookRotation(wallDir, Vector3.up));

            var hinge = new GameObject("Hinge").transform;
            hinge.SetParent(root.transform, false);

            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "Panel";
            panel.transform.SetParent(hinge, false);
            panel.transform.localPosition = new Vector3(0f, 1.02f, width * 0.5f);
            panel.transform.localScale = new Vector3(0.09f, 2.04f, width - 0.05f);
            panel.GetComponent<MeshRenderer>().sharedMaterial = mat;

            var knob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            knob.name = "Knob";
            knob.transform.SetParent(hinge, false);
            knob.transform.localPosition = new Vector3(0.06f, 1.0f, width - 0.12f);
            knob.transform.localScale = Vector3.one * 0.09f;
            knob.GetComponent<MeshRenderer>().sharedMaterial = metalDark;

            var door = root.AddComponent<ContentHouse.Interaction.DoorInteractable>();
            door.Hinge = hinge;
            door.OpenAngle = 95f;
            door.Locked = locked;
            door.LockedMessage = lockedMsg;
            door.TaskId = taskId;

            doors.Add(new DoorInteractableBuild { Door = door, StartOpen = startOpen });
            return door;
        }

        /// <summary>Window with frame + lockable/breakable glass.</summary>
        static ContentHouse.Interaction.WindowLockInteractable BuildWindow(string name, Transform parent, Vector3 center,
            Vector3 wallDir, float width, float height, bool startsLocked, string taskId)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent);
            root.transform.SetPositionAndRotation(center, Quaternion.LookRotation(wallDir, Vector3.up));
            var t = root.transform;

            var glass = GameObject.CreatePrimitive(PrimitiveType.Cube);
            glass.name = "Glass";
            glass.transform.SetParent(t, false);
            glass.transform.localPosition = Vector3.zero;
            glass.transform.localScale = new Vector3(0.05f, height, width);
            glass.GetComponent<MeshRenderer>().sharedMaterial = glassMat;

            void Frame(string fname, Vector3 lp, Vector3 ls)
            {
                var f = GameObject.CreatePrimitive(PrimitiveType.Cube);
                f.name = fname;
                f.transform.SetParent(t, false);
                f.transform.localPosition = lp;
                f.transform.localScale = ls;
                f.GetComponent<MeshRenderer>().sharedMaterial = trim;
            }
            Frame("Top", new Vector3(0f, height * 0.5f + 0.05f, 0f), new Vector3(0.12f, 0.1f, width + 0.1f));
            Frame("Bottom", new Vector3(0f, -height * 0.5f - 0.05f, 0f), new Vector3(0.12f, 0.1f, width + 0.1f));
            Frame("Left", new Vector3(0f, 0f, -width * 0.5f - 0.05f), new Vector3(0.12f, height + 0.2f, 0.1f));
            Frame("Right", new Vector3(0f, 0f, width * 0.5f + 0.05f), new Vector3(0.12f, height + 0.2f, 0.1f));

            var w = root.AddComponent<ContentHouse.Interaction.WindowLockInteractable>();
            w.GlassPane = glass;
            w.StartsLocked = startsLocked;
            w.TaskId = taskId;
            return w;
        }

        static Light Point(string name, Transform parent, Vector3 pos, Color c, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.intensity = intensity;
            l.range = range;
            return l;
        }

        static Transform Anchor(string name, Transform parent, Vector3 pos, float yaw = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            return go.transform;
        }

        // =====================================================================
        // Environment & shell
        // =====================================================================

        static void BuildOutdoor(Transform root)
        {
            var env = new GameObject("Outdoor").transform;
            env.SetParent(root);

            // Ground
            Prim("Ground", env, new Vector3(0, -0.06f, -2), new Vector3(90, 0.12f, 90), grass);

            // Perimeter fence
            void Fence(Vector3 a, Vector3 b)
            {
                var mid = (a + b) * 0.5f;
                var dir = (b - a).normalized;
                var len = Vector3.Distance(a, b);
                Prim("Fence", env, mid + Vector3.up * 0.85f, new Vector3(
                    dir.x != 0 ? len : 0.12f, 1.7f, dir.z != 0 ? len : 0.12f), fenceMat);
            }
            Fence(new Vector3(-13, 0, -14), new Vector3(13, 0, -14));
            Fence(new Vector3(-13, 0, 10), new Vector3(13, 0, 10));
            Fence(new Vector3(-13, 0, -14), new Vector3(-13, 0, 10));
            Fence(new Vector3(13, 0, -14), new Vector3(13, 0, 10));

            // Front porch (front door faces +z at x=-4)
            Prim("PorchSlab", env, new Vector3(-4, -0.04f, 6.9f), new Vector3(4.4f, 0.1f, 1.9f), concrete);
            foreach (var p in new[] { new Vector3(-6.1f, 1.25f, 7.7f), new Vector3(-1.9f, 1.25f, 7.7f) })
                Prim("PorchPost", env, p, new Vector3(0.16f, 2.5f, 0.16f), trim);
            Prim("PorchRoof", env, new Vector3(-4, 2.62f, 7.1f), new Vector3(4.8f, 0.14f, 2.4f), roof);

            // Backyard patio below the escape window
            Prim("PatioSlab", env, new Vector3(4, -0.03f, -7.6f), new Vector3(6.5f, 0.08f, 3.4f), concrete);
        }

        static void BuildHouseShell(Transform root)
        {
            var shell = new GameObject("Shell").transform;
            shell.SetParent(root);

            // ---- ground floor slabs (top at y=0) ----
            void Slab(string name, float x0, float x1, float z0, float z1, Material m, float y = -0.1f, float th = 0.2f)
                => Prim(name, shell, new Vector3((x0 + x1) / 2f, y, (z0 + z1) / 2f), new Vector3(x1 - x0, th, z1 - z0), m);

            Slab("Floor_Front", -8, 8, 0, 6, floorWood);                 // living + entry
            Slab("Floor_Kitchen", -8, 2, -6, 0, floorTile);              // kitchen
            // Dining (over basement) with stair hole at x 5.2..7.2, z -6..-2
            Slab("Floor_Dining_A", 2, 5.2f, -6, 0, floorWood);
            Slab("Floor_Dining_B", 5.2f, 8, -2, 0, floorWood);
            Slab("Floor_Dining_C", 7.2f, 8, -6, -2, floorWood);

            // ---- exterior walls, ground (y 0..2.8) ----
            // Front z=6: front door x -4.5..-3.5, living window x -7..-5.7, entry window x 4.4..5.6
            Wall("Wall_Front_Lower", shell, new Vector3(-8, 0, 6), new Vector3(8, 0, 6), 0f, 2.8f, wallLower,
                Gap.Door(-4), Gap.Window(-6.35f, 1.3f), Gap.Window(5, 1.2f));
            // Rear z=-6: kitchen window x -4.6..-3.4, patio door x 1.2..2.3, dining window x 4.9..6.1
            Wall("Wall_Rear_Lower", shell, new Vector3(-8, 0, -6), new Vector3(8, 0, -6), 0f, 2.8f, wallLower,
                Gap.Window(-4, 1.2f), Gap.Door(1.75f, 1.1f), Gap.Window(5.5f, 1.2f));
            // Left x=-8: kitchen side window z -3.5..-2.3
            Wall("Wall_Left_Lower", shell, new Vector3(-8, 0, -6), new Vector3(-8, 0, 6), 0f, 2.8f, wallLower,
                Gap.Window(-2.9f, 1.2f));
            // Right x=8: solid
            Wall("Wall_Right_Lower", shell, new Vector3(8, 0, -6), new Vector3(8, 0, 6), 0f, 2.8f, wallLower);

            // ---- interior walls, ground ----
            // z=0 between living and kitchen (x -8..2), archway x -6..-3.4
            Wall("Wall_LivingKitchen", shell, new Vector3(-8, 0, 0), new Vector3(2, 0, 0), 0f, 2.8f, wallLower,
                Gap.Arch(-4.7f, 2.6f));
            // z=0 between living and dining (x 2..8), archway x 3.5..6.5
            Wall("Wall_LivingDining", shell, new Vector3(2, 0, 0), new Vector3(8, 0, 0), 0f, 2.8f, wallLower,
                Gap.Arch(5f, 3f));
            // x=2 between kitchen and dining (z -6..0), door gap z -4..-3
            Wall("Wall_KitchenDining", shell, new Vector3(2, 0, -6), new Vector3(2, 0, 0), 0f, 2.8f, wallLower,
                Gap.Door(-3.5f));

            // ---- basement stairwell walls in dining (x=5.2 & x=7.2, z -6..-2) ----
            Wall("Wall_Stairwell_W", shell, new Vector3(5.2f, 0, -6), new Vector3(5.2f, 0, -2), 0f, 2.8f, wallLower);
            Wall("Wall_Stairwell_E", shell, new Vector3(7.2f, 0, -6), new Vector3(7.2f, 0, -2), 0f, 2.8f, wallLower);

            // ---- upper floor slabs (top at y=3) with entry stair hole x 1.2..5.6, z 4.1..6 ----
            Slab("Floor_Upper_A", -8, 8, -6, 4.1f, floorWood, y: 2.9f);
            Slab("Floor_Upper_B", -8, 1.2f, 4.1f, 6, floorWood, y: 2.9f);
            Slab("Floor_Upper_C", 5.6f, 8, 4.1f, 6, floorWood, y: 2.9f);

            // Stair hole guard walls (y 3..4.1)
            Wall("Guard_W", shell, new Vector3(1.2f, 3, 4.1f), new Vector3(1.2f, 3, 6), 3f, 4.1f, wallUpper);
            Wall("Guard_E", shell, new Vector3(5.6f, 3, 4.1f), new Vector3(5.6f, 3, 6), 3f, 4.1f, wallUpper);
            Wall("Guard_S", shell, new Vector3(1.2f, 3, 4.1f), new Vector3(5.6f, 3, 4.1f), 3f, 4.1f, wallUpper);

            // ---- exterior walls, upper (y 3..5.8) ----
            Wall("Wall_Front_Upper", shell, new Vector3(-8, 3, 6), new Vector3(8, 3, 6), 3f, 5.8f, wallUpper,
                Gap.Window(5, 1.2f, 4f, 5.2f));
            Wall("Wall_Rear_Upper", shell, new Vector3(-8, 3, -6), new Vector3(8, 3, -6), 3f, 5.8f, wallUpper,
                Gap.Window(-4.9f, 1.2f, 4f, 5.2f), Gap.Window(4.15f, 1.5f, 4f, 5.2f));   // Steve + escape windows
            Wall("Wall_Left_Upper", shell, new Vector3(-8, 3, -6), new Vector3(-8, 3, 6), 3f, 5.8f, wallUpper);
            Wall("Wall_Right_Upper", shell, new Vector3(8, 3, -6), new Vector3(8, 3, 6), 3f, 5.8f, wallUpper);

            // ---- interior walls, upper ----
            // x=0 (Steve's room split), door gap z 4.2..5.2
            Wall("Wall_SteveSplit", shell, new Vector3(0, 3, -6), new Vector3(0, 3, 6), 3f, 5.8f, wallUpper,
                Gap.Door(4.7f, 1.0f, 5.1f));
            // z=2 (hallway / Mr. IG), door gap x 3.6..4.7
            Wall("Wall_MrIgHall", shell, new Vector3(0, 3, 2), new Vector3(8, 3, 2), 3f, 5.8f, wallUpper,
                Gap.Door(4.15f, 1.1f, 5.1f));

            // ---- roof ----
            Prim("Roof", shell, new Vector3(0, 5.98f, 0), new Vector3(16.6f, 0.3f, 12.6f), roof);
        }

        static void BuildStairsAndBasement(Transform root)
        {
            var b = new GameObject("StairsAndBasement").transform;
            b.SetParent(root);

            // Entry stairs: ramp from (1.4, 0) to (5.4, 3) at z=5.05
            var ramp = Prim("Stairs_Entry", b, new Vector3(3.4f, 1.5f, 5.05f), new Vector3(5.0f, 0.18f, 1.9f), floorWood);
            ramp.transform.rotation = Quaternion.Euler(0f, 0f, 36.87f);

            // Basement stairs: ramp from (6.2, 0, -2) down to (6.2, -3, -5.8)
            var ramp2 = Prim("Stairs_Basement", b, new Vector3(6.2f, -1.5f, -3.9f), new Vector3(1.9f, 0.18f, 4.85f), concrete);
            ramp2.transform.rotation = Quaternion.Euler(-38.3f, 0f, 0f);

            // Basement floor (top y=-3) and foundation walls (y -3..0)
            Prim("Floor_Basement", b, new Vector3(5, -3.1f, -3), new Vector3(6f, 0.2f, 6f), concrete);
            Wall("Basement_W", b, new Vector3(2, -3, -6), new Vector3(2, -3, 0), -3f, 0f, concrete);
            Wall("Basement_S", b, new Vector3(2, -3, -6), new Vector3(8, -3, -6), -3f, 0f, concrete);
            Wall("Basement_E", b, new Vector3(8, -3, -6), new Vector3(8, -3, 0), -3f, 0f, concrete);
            Wall("Basement_N", b, new Vector3(2, -3, 0), new Vector3(8, -3, 0), -3f, 0f, concrete);
            // Stairwell enclosure below ground (x 5.2 / 7.2, z -6..-2)
            Wall("Basement_StairW", b, new Vector3(5.2f, -3, -6), new Vector3(5.2f, -3, -2), -3f, 0f, concrete);
            Wall("Basement_StairE", b, new Vector3(7.2f, -3, -6), new Vector3(7.2f, -3, -2), -3f, 0f, concrete);
        }

        static void BuildDoorsAndWindows(Transform root)
        {
            var dw = new GameObject("DoorsWindows").transform;
            dw.SetParent(root);

            frontDoor = BuildDoor("Door_Front", dw, new Vector3(-4.5f, 0, 6), new Vector3(1, 0, 0), 1.0f, trim);
            patioDoor = BuildDoor("Door_Patio", dw, new Vector3(1.2f, 0, -6), new Vector3(1, 0, 0), 1.1f, trim,
                startOpen: true);   // Act I chore: close + lock it
            kitchenDiningDoor = BuildDoor("Door_KitchenDining", dw, new Vector3(2, 0, -4), new Vector3(0, 0, 1), 1.0f, trim);
            basementDoor = BuildDoor("Door_Basement", dw, new Vector3(5.2f, 0, -2), new Vector3(1, 0, 0), 2.0f, trim);
            steveDoor = BuildDoor("Door_Steve", dw, new Vector3(0, 3, 4.2f), new Vector3(0, 0, 1), 1.0f, trim);
            mrIgDoor = BuildDoor("Door_MrIG", dw, new Vector3(3.6f, 3, 2), new Vector3(1, 0, 0), 1.1f, trim,
                locked: true, lockedMsg: "Mr. IG is LIVE. Do not disturb the stream.");

            // Lockable ground windows (Act I checklist)
            BuildWindow("Window_LivingFront", dw, new Vector3(-6.35f, 1.6f, 6), new Vector3(1, 0, 0), 1.3f, 1.2f,
                false, "window1");
            BuildWindow("Window_KitchenSide", dw, new Vector3(-8, 1.6f, -2.9f), new Vector3(0, 0, 1), 1.2f, 1.2f,
                false, "window2");
            BuildWindow("Window_KitchenRear", dw, new Vector3(-4, 1.6f, -6), new Vector3(1, 0, 0), 1.2f, 1.2f,
                false, "");     // false-alarm bang window
            BuildWindow("Window_DiningRear", dw, new Vector3(5.5f, 1.6f, -6), new Vector3(1, 0, 0), 1.2f, 1.2f,
                true, "");     // intruder shatters this one
            BuildWindow("Window_EntryFront", dw, new Vector3(5, 1.6f, 6), new Vector3(1, 0, 0), 1.2f, 1.2f,
                false, "");
            // Upstairs decorative + escape
            BuildWindow("Window_HallFront", dw, new Vector3(5, 4.6f, 6), new Vector3(1, 0, 0), 1.2f, 1.2f, true, "");
            BuildWindow("Window_SteveRear", dw, new Vector3(-4.9f, 4.6f, -6), new Vector3(1, 0, 0), 1.2f, 1.2f, true, "");
            BuildWindow("Window_Escape", dw, new Vector3(4.15f, 4.6f, -6), new Vector3(1, 0, 0), 1.5f, 1.2f,
                true, "");     // Act IV QTE window
        }

        // =====================================================================
        // Furniture (Kenney CC0 models)
        // =====================================================================

        static void BuildFurniture(Transform root)
        {
            var f = new GameObject("Furniture").transform;
            f.SetParent(root);

            // ---- living room (front left, around -4, 3) ----
            Model("loungeSofa", f, new Vector3(-3.4f, 0, 3.2f), 270f);        // faces TV (-x)
            Model("tableCoffee", f, new Vector3(-5.2f, 0, 3.1f), 0f);
            Model("cabinetTelevision", f, new Vector3(-7.45f, 0, 3.1f), 90f);
            Model("televisionModern", f, new Vector3(-7.3f, 0.62f, 3.1f), 90f);
            Model("rugRectangle", f, new Vector3(-5, 0.02f, 3f), 0f);
            Model("lampRoundFloor", f, new Vector3(-6.8f, 0, 1.3f), 0f);
            Model("pottedPlant", f, new Vector3(-7.4f, 0, 5.3f), 0f);
            Model("coatRackStanding", f, new Vector3(-2.2f, 0, 5.5f), 0f);
            Model("speaker", f, new Vector3(-7.4f, 0, 1.6f), 90f);

            // ---- kitchen (rear left) ----
            Model("kitchenCabinet", f, new Vector3(-6.4f, 0, -5.5f), 180f);
            Model("kitchenStove", f, new Vector3(-4.6f, 0, -5.55f), 180f);
            Model("kitchenSink", f, new Vector3(-2.8f, 0, -5.55f), 180f);
            Model("kitchenCabinet", f, new Vector3(-1.0f, 0, -5.5f), 180f);
            Model("kitchenCabinetUpper", f, new Vector3(-5.5f, 1.55f, -5.8f), 180f);
            Model("kitchenCabinetUpper", f, new Vector3(-2.2f, 1.55f, -5.8f), 180f);
            Model("kitchenFridge", f, new Vector3(1.35f, 0, -5.4f), 180f);
            Model("stoolBar", f, new Vector3(-5.8f, 0, -3.4f), 0f);
            Model("stoolBar", f, new Vector3(-3.6f, 0, -3.4f), 0f);

            // ---- dining room (rear right) ----
            Model("table", f, new Vector3(3.6f, 0, -3.2f), 0f);
            Model("chair", f, new Vector3(3.6f, 0, -1.9f), 0f);
            Model("chair", f, new Vector3(3.6f, 0, -4.5f), 180f);
            Model("chair", f, new Vector3(2.3f, 0, -3.2f), 90f);
            Model("chair", f, new Vector3(4.9f, 0, -3.2f), 270f);
            Model("bookcaseClosed", f, new Vector3(2.6f, 0, -0.5f), 0f);
            Model("pottedPlant", f, new Vector3(4.2f, 0, -5.4f), 0f);

            // ---- entry (front right, near stairs) ----
            Model("sideTableDrawers", f, new Vector3(7.4f, 0, 1.2f), 270f);
            Model("trashcan", f, new Vector3(0.4f, 0, 5.4f), 0f);

            // ---- upstairs hallway ----
            Model("kitchenFridgeSmall", f, new Vector3(7.2f, 3, 3.6f), 270f);     // Mr. IG's mini-fridge prop (visual)
            Model("lampSquareCeiling", f, new Vector3(2.5f, 5.55f, 3.8f), 0f);
            Model("rugDoormat", f, new Vector3(4.15f, 3.02f, 2.7f), 0f);

            // ---- Mr. IG bedroom (rear right upper) ----
            Model("desk", f, new Vector3(6.7f, 3, -1.6f), 270f);
            Model("computerScreen", f, new Vector3(6.95f, 3.75f, -1.6f), 270f);
            Model("laptop", f, new Vector3(6.6f, 3.75f, -0.4f), 270f);
            Model("chairDesk", f, new Vector3(5.8f, 3, -1.5f), 90f);
            Model("bedDouble", f, new Vector3(1.7f, 3, -4.9f), 0f);
            Model("bookcaseOpen", f, new Vector3(0.6f, 3, -1.2f), 90f);
            Model("lampSquareTable", f, new Vector3(6.7f, 3.78f, -2.6f), 0f);
            Model("rugSquare", f, new Vector3(4, 3.02f, -2.2f), 0f);
            Model("speakerSmall", f, new Vector3(6.9f, 3, 0.6f), 270f);

            // ---- Steve bedroom (left upper) ----
            Model("cabinetBed", f, new Vector3(-6.4f, 3, -4.4f), 0f);           // under-bed hiding below
            Model("desk", f, new Vector3(-1.0f, 3, 4.6f), 180f);
            Model("chairDesk", f, new Vector3(-1.0f, 3, 3.6f), 0f);
            Model("sideTableDrawers", f, new Vector3(-7.3f, 3, 0.4f), 90f);
            Model("lampRoundFloor", f, new Vector3(-7.4f, 3, 4.9f), 0f);
            Model("rugRounded", f, new Vector3(-4, 3.02f, 0.5f), 0f);
            Model("bear", f, new Vector3(-6.0f, 3.62f, -4.4f), 0f);
            Model("bookcaseClosedWide", f, new Vector3(-1.0f, 3, -5.5f), 180f);

            // ---- basement ----
            Model("washer", f, new Vector3(2.6f, -3, -5.5f), 0f);
            Model("dryer", f, new Vector3(4.0f, -3, -5.5f), 0f);
            Model("cardboardBoxClosed", f, new Vector3(3.2f, -3, -1.4f), 0f);
            Model("cardboardBoxClosed", f, new Vector3(3.8f, -3, -2.0f), 15f);
            Model("cardboardBoxOpen", f, new Vector3(2.7f, -3, -2.2f), -20f);
            Model("bookcaseOpen", f, new Vector3(2.4f, -3, -3.9f), 90f);
        }

        // =====================================================================
        // Lighting & power
        // =====================================================================

        static Light moonLight, doorGlow;

        static void BuildLighting(Transform root)
        {
            var l = new GameObject("Lighting").transform;
            l.SetParent(root);

            void Reg(Light light) => houseLights.Add(light);

            // Living room RGB ambience
            Reg(Point("RGB_Magenta", l, new Vector3(-4.2f, 2.3f, 2.6f), new Color(1f, 0.2f, 0.5f), 1.1f, 7f));
            Reg(Point("RGB_Cyan", l, new Vector3(-6.8f, 2.2f, 4.4f), new Color(0.2f, 0.6f, 1f), 1.0f, 7f));
            // Kitchen / dining / entry
            Reg(Point("KitchenLight", l, new Vector3(-3, 2.5f, -3), new Color(0.95f, 0.95f, 0.85f), 0.95f, 7f));
            Reg(Point("DiningLight", l, new Vector3(3.5f, 2.5f, -3), new Color(0.95f, 0.93f, 0.85f), 0.85f, 7f));
            Reg(Point("EntryLight", l, new Vector3(5.5f, 2.5f, 3.2f), new Color(0.9f, 0.9f, 0.8f), 0.8f, 7f));
            // Upstairs
            Reg(Point("HallLight", l, new Vector3(2.5f, 5.4f, 3.8f), new Color(0.9f, 0.9f, 0.8f), 0.8f, 7f));
            Reg(Point("SteveLight", l, new Vector3(-4, 5.4f, 0), new Color(0.9f, 0.88f, 0.8f), 0.85f, 8f));
            // Basement & outside
            Reg(Point("BasementBulb", l, new Vector3(5, -0.5f, -3), new Color(1f, 0.9f, 0.7f), 0.75f, 6f));
            Reg(Point("PorchLight", l, new Vector3(-4, 2.5f, 6.4f), new Color(1f, 0.85f, 0.6f), 0.8f, 6f));
            Reg(Point("PatioLight", l, new Vector3(1.8f, 2.3f, -6.5f), new Color(1f, 0.85f, 0.6f), 0.7f, 5f));

            // Mr. IG's room: on battery 5G hotspot - survives the blackout (NOT registered)
            Point("MrIgMonitorGlow", l, new Vector3(4, 5.3f, -2), new Color(0.55f, 0.4f, 1f), 0.65f, 8f);
            Point("MrIgScreenGlow", l, new Vector3(6.7f, 3.9f, -1.5f), new Color(0.4f, 0.6f, 1f), 0.5f, 3.5f);

            // Light seeping under Mr. IG's door - activates at blackout
            doorGlow = Point("DoorGlow", l, new Vector3(4.15f, 3.2f, 2.35f), new Color(0.4f, 0.55f, 1f), 0.55f, 2.6f);
            doorGlow.enabled = false;

            // Moonlight - blackout only
            var mgo = new GameObject("MoonLight");
            mgo.transform.SetParent(l);
            mgo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            moonLight = mgo.AddComponent<Light>();
            moonLight.type = LightType.Directional;
            moonLight.color = new Color(0.45f, 0.55f, 0.8f);
            moonLight.intensity = 0.32f;
            moonLight.enabled = false;

            // Lightning flash source (AmbienceController drives intensity)
            var lgo = new GameObject("LightningLight");
            lgo.transform.SetParent(l);
            lgo.transform.rotation = Quaternion.Euler(65f, 20f, 0f);
            var ll = lgo.AddComponent<Light>();
            ll.type = LightType.Directional;
            ll.color = new Color(0.8f, 0.85f, 1f);
            ll.intensity = 0.35f;
            lightningLight = ll;
        }
        static Light lightningLight;

        // =====================================================================
        // Hide spots (closets + under-bed)
        // =====================================================================

        static void BuildHideSpots(Transform root)
        {
            var h = new GameObject("HideSpots").transform;
            h.SetParent(root);

            // ---- Upstairs hallway closet (scripted Act III spot) ----
            // Interior box at x 0.5..1.9, z 5.35..6, opening facing -z.
            var closet = new GameObject("HallCloset").transform;
            closet.SetParent(h);
            Prim("Closet_SideW", closet, new Vector3(0.56f, 4.15f, 5.65f), new Vector3(0.1f, 2.3f, 1.3f), trim);
            Prim("Closet_SideE", closet, new Vector3(1.84f, 4.15f, 5.65f), new Vector3(0.1f, 2.3f, 1.3f), trim);
            Prim("Closet_Top", closet, new Vector3(1.2f, 5.25f, 5.65f), new Vector3(1.4f, 0.1f, 1.3f), trim);
            Prim("Closet_Rod", closet, new Vector3(1.2f, 5.0f, 5.65f), new Vector3(0.05f, 0.05f, 1.2f), metalDark);
            // Hinged closet door
            closetDoor = BuildDoor("Door_Closet", closet, new Vector3(0.6f, 3, 5.35f), new Vector3(1, 0, 0), 1.2f, trim);

            // HideSpot pad sits just in front of the door plane.
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "HidePad_Closet";
            pad.transform.SetParent(closet);
            pad.transform.SetPositionAndRotation(new Vector3(1.2f, 3.75f, 5.33f), Quaternion.Euler(0f, 180f, 0f));
            pad.transform.localScale = new Vector3(1.3f, 1.5f, 0.06f);
            pad.GetComponent<MeshRenderer>().sharedMaterial = metalDark;
            var closetSpot = pad.AddComponent<ContentHouse.Interaction.HideSpot>();
            closetSpot.SpotKind = ContentHouse.Interaction.HideSpot.Kind.Closet;
            closetSpot.SpotName = "hallway closet";
            closetSpot.StandAnchor = Anchor("Closet_Stand", closet, new Vector3(1.2f, 3.05f, 5.62f), 180f);
            closetSpot.ExitAnchor = Anchor("Closet_Exit", closet, new Vector3(1.25f, 3.05f, 4.35f));
            closetSpot.ClosetDoor = closetDoor;
            hallCloset = closetSpot;

            // ---- Ground-floor closet (living room west wall) ----
            var gCloset = new GameObject("LivingCloset").transform;
            gCloset.SetParent(h);
            Prim("GC_Back", gCloset, new Vector3(-7.85f, 1.15f, 4.8f), new Vector3(0.1f, 2.3f, 1.4f), trim);
            Prim("GC_SideN", gCloset, new Vector3(-7.3f, 1.15f, 4.15f), new Vector3(1.2f, 2.3f, 0.1f), trim);
            Prim("GC_SideS", gCloset, new Vector3(-7.3f, 1.15f, 5.45f), new Vector3(1.2f, 2.3f, 0.1f), trim);
            Prim("GC_Top", gCloset, new Vector3(-7.3f, 2.25f, 4.8f), new Vector3(1.2f, 0.1f, 1.4f), trim);
            entryClosetDoor = BuildDoor("Door_LivingCloset", gCloset, new Vector3(-6.75f, 0, 4.2f), new Vector3(0, 0, 1), 1.2f, trim);
            var gPad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gPad.name = "HidePad_Living";
            gPad.transform.SetParent(gCloset);
            gPad.transform.SetPositionAndRotation(new Vector3(-6.7f, 0.75f, 4.8f), Quaternion.Euler(0f, 90f, 0f));
            gPad.transform.localScale = new Vector3(1.3f, 1.5f, 0.06f);
            gPad.GetComponent<MeshRenderer>().sharedMaterial = metalDark;
            var gSpot = gPad.AddComponent<ContentHouse.Interaction.HideSpot>();
            gSpot.SpotKind = ContentHouse.Interaction.HideSpot.Kind.Closet;
            gSpot.SpotName = "coat closet";
            gSpot.StandAnchor = Anchor("GC_Stand", gCloset, new Vector3(-7.3f, 0.05f, 4.8f), 90f);
            gSpot.ExitAnchor = Anchor("GC_Exit", gCloset, new Vector3(-6.35f, 0.05f, 4.8f));
            gSpot.ClosetDoor = entryClosetDoor;

            // ---- Under Steve's bed (cabinetBed has visible cavity below) ----
            var bedPad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bedPad.name = "HidePad_UnderBed";
            bedPad.transform.SetParent(h);
            bedPad.transform.SetPositionAndRotation(new Vector3(-6.4f, 3.25f, -3.5f), Quaternion.Euler(0f, 0f, 0f));
            bedPad.transform.localScale = new Vector3(1.6f, 0.5f, 0.1f);
            bedPad.GetComponent<MeshRenderer>().sharedMaterial = metalDark;
            var bedSpot = bedPad.AddComponent<ContentHouse.Interaction.HideSpot>();
            bedSpot.SpotKind = ContentHouse.Interaction.HideSpot.Kind.UnderBed;
            bedSpot.SpotName = "bed";
            bedSpot.StandAnchor = Anchor("Bed_Stand", h, new Vector3(-6.4f, 3.28f, -4.4f), 180f);
            bedSpot.ExitAnchor = Anchor("Bed_Exit", h, new Vector3(-6.4f, 3.05f, -3.3f));
        }

        static ContentHouse.Interaction.HideSpot hallCloset;

        // =====================================================================
        // CCTV cameras
        // =====================================================================

        static ContentHouse.CCTV.CCTVCameraFeed hallCam, livingCam;

        static void BuildCCTV(Transform root)
        {
            var c = new GameObject("CCTV").transform;
            c.SetParent(root);

            hallCam = BuildCam(c, "CCTV_Hall", new Vector3(0.35f, 5.5f, 5.62f), new Vector3(4.15f, 3.2f, 2.5f), "HALL CAM 02");
            livingCam = BuildCam(c, "CCTV_Living", new Vector3(-7.55f, 2.6f, 5.6f), new Vector3(-4f, 0.8f, 2.6f), "LIVING CAM 01");
        }

        static ContentHouse.CCTV.CCTVCameraFeed BuildCam(Transform parent, string name, Vector3 pos, Vector3 lookAt, string feedName)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(lookAt - pos, Vector3.up);
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 62f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 25f;
            cam.enabled = false;   // enabled on demand when the feed is viewed
            var feed = go.AddComponent<ContentHouse.CCTV.CCTVCameraFeed>();
            feed.Cam = cam;
            feed.FeedName = feedName;
            return feed;
        }

        // =====================================================================
        // Devices / interactables (router, breakers, fridge, delivery, laptop, can, knock)
        // =====================================================================

        static ContentHouse.Interaction.DeliveryBoxInteractable deliveryBox;
        static ContentHouse.Interaction.PickupItem drinkCan;
        static ContentHouse.Interaction.LaptopCalibrationInteractable laptopCal;
        static ContentHouse.Interaction.FridgeInteractable miniFridge;
        static ContentHouse.Interaction.RouterInteractable router;
        static ContentHouse.Interaction.BreakerPanelInteractable breakerPanel;
        static ContentHouse.Interaction.DoorDeliverInteractable knockPlate;
        static MeshRenderer laptopScreenRenderer;
        static GameObject deskProp;
        static Transform deskBlockingPos;

        static void BuildInteractableDevices(Transform root)
        {
            var d = new GameObject("Devices").transform;
            d.SetParent(root);

            // ---- Porch delivery box (Gamer-Juice 12-pack) ----
            var boxGo = Model("cardboardBoxClosed", d, new Vector3(-5.2f, 0.05f, 7.2f), 25f);
            if (boxGo == null)
            {
                boxGo = Prim("DeliveryBox_Fallback", d, new Vector3(-5.2f, 0.25f, 7.2f), new Vector3(0.7f, 0.5f, 0.7f), trim);
            }
            boxGo.name = "DeliveryBox";
            deliveryBox = boxGo.AddComponent<ContentHouse.Interaction.DeliveryBoxInteractable>();
            deliveryBox.GivesItem = "Gamer-Juice 12-pack";
            deliveryBox.TaskId = "box";
            // Cans peeking out of the box
            for (int i = 0; i < 3; i++)
                Prim("Can" + i, boxGo.transform, new Vector3(-0.18f + i * 0.18f, 0.42f, 0f),
                    new Vector3(0.09f, 0.16f, 0.09f), metalDark, collider: false);

            // ---- Router (basement, east wall) ----
            var routerGo = new GameObject("Router");
            routerGo.transform.SetParent(d);
            routerGo.transform.SetPositionAndRotation(new Vector3(7.75f, -2.35f, -1.2f), Quaternion.Euler(0f, -90f, 0f));
            Prim("RouterBody", routerGo.transform, Vector3.zero + new Vector3(0, 0.1f, 0), new Vector3(0.34f, 0.2f, 0.2f), metalDark, collider: false);
            var ledRed = Prim("LedRed", routerGo.transform, new Vector3(0.1f, 0.12f, 0.05f), new Vector3(0.05f, 0.05f, 0.05f), glowLed, collider: false);
            var ledGreenObj = Prim("LedGreen", routerGo.transform, new Vector3(0.1f, 0.12f, 0.05f), new Vector3(0.05f, 0.05f, 0.05f), Emissive("GlowLedGreen", Color.black, Color.green), collider: false);
            ledGreenObj.SetActive(false);
            var routerCol = routerGo.AddComponent<BoxCollider>();
            routerCol.center = new Vector3(0, 0.1f, 0);
            routerCol.size = new Vector3(0.4f, 0.3f, 0.3f);
            router = routerGo.AddComponent<ContentHouse.Interaction.RouterInteractable>();
            router.LedRed = ledRed;
            router.LedGreen = ledGreenObj;
            router.TaskId = "router";

            // ---- Breaker panel (basement, west wall x=2) ----
            var panel = new GameObject("BreakerPanel");
            panel.transform.SetParent(d);
            panel.transform.SetPositionAndRotation(new Vector3(2.15f, -1.55f, -3.4f), Quaternion.Euler(0f, 90f, 0f));
            Prim("PanelBody", panel.transform, Vector3.zero, new Vector3(0.5f, 0.7f, 0.1f), metalDark, collider: false);
            var leverTs = new Transform[3];
            for (int i = 0; i < 3; i++)
            {
                var lever = Prim("Lever" + i, panel.transform, new Vector3(-0.14f + i * 0.14f, 0.05f, 0.08f),
                    new Vector3(0.07f, 0.22f, 0.06f), trim, collider: false);
                leverTs[i] = lever.transform;
            }
            var panelCol = panel.AddComponent<BoxCollider>();
            panelCol.center = Vector3.zero;
            panelCol.size = new Vector3(0.6f, 0.85f, 0.3f);
            breakerPanel = panel.AddComponent<ContentHouse.Interaction.BreakerPanelInteractable>();
            breakerPanel.Levers = leverTs;
            breakerPanel.On = new[] { true, false, true };     // breaker 2 tripped = frame drops
            breakerPanel.TaskId = "breakers";

            // ---- Mini-fridge (upstairs hallway, for the delivery chore) ----
            var fridgeGo = new GameObject("MiniFridge");
            fridgeGo.transform.SetParent(d);
            fridgeGo.transform.SetPositionAndRotation(new Vector3(7.5f, 3.45f, 3.6f), Quaternion.Euler(0f, 270f, 0f));
            Prim("FridgeBody", fridgeGo.transform, Vector3.zero, new Vector3(0.6f, 0.9f, 0.6f), metalDark, collider: false);
            var stockedCans = new GameObject("StockedCans");
            stockedCans.transform.SetParent(fridgeGo.transform, false);
            stockedCans.SetActive(false);
            for (int i = 0; i < 4; i++)
                Prim("SC" + i, stockedCans.transform, new Vector3(-0.15f + (i % 2) * 0.3f, 0.55f + (i / 2) * 0.18f, 0),
                    new Vector3(0.1f, 0.16f, 0.1f), metalDark, collider: false);
            var fridgeCol = fridgeGo.AddComponent<BoxCollider>();
            fridgeCol.center = Vector3.zero;
            fridgeCol.size = new Vector3(0.7f, 1.0f, 0.7f);
            miniFridge = fridgeGo.AddComponent<ContentHouse.Interaction.FridgeInteractable>();
            miniFridge.RequiredItem = "Gamer-Juice 12-pack";
            miniFridge.StockedCans = stockedCans;
            miniFridge.TaskId = "fridge";

            // ---- Single drink can (appears on the fridge after stocking) ----
            var canGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            canGo.name = "GamerJuiceCan";
            canGo.transform.SetParent(d);
            canGo.transform.SetPositionAndRotation(new Vector3(7.5f, 4.02f, 3.6f), Quaternion.identity);
            canGo.transform.localScale = new Vector3(0.1f, 0.14f, 0.1f);
            canGo.GetComponent<MeshRenderer>().sharedMaterial = Std("CanGreen", new Color(0.2f, 0.75f, 0.3f));
            drinkCan = canGo.AddComponent<ContentHouse.Interaction.PickupItem>();
            drinkCan.ItemName = "Gamer-Juice can";
            drinkCan.PickUpLine = "One ice-cold Gamer-Juice for the boss.";
            canGo.SetActive(false);   // story shows it after the fridge is stocked

            // ---- Backup stream laptop on the coffee table (waveform chore) ----
            var laptopGo = Model("laptop", d, new Vector3(-5.2f, 0.42f, 3.15f), 25f);
            if (laptopGo == null)
                laptopGo = Prim("BackupLaptop_Fallback", d, new Vector3(-5.2f, 0.45f, 3.15f),
                    new Vector3(0.42f, 0.05f, 0.3f), metalDark);
            laptopGo.name = "BackupLaptop";
            laptopCal = laptopGo.AddComponent<ContentHouse.Interaction.LaptopCalibrationInteractable>();
            laptopCal.TaskId = "laptop";

            // ---- Mr. IG's stream laptop (shows the hallway GoPro feed) ----
            var igLaptop = Model("laptop", d, new Vector3(6.6f, 3.78f, -0.4f), 270f);
            if (igLaptop == null)
                igLaptop = Prim("IgLaptop_Fallback", d, new Vector3(6.6f, 3.8f, -0.4f), new Vector3(0.42f, 0.05f, 0.3f), metalDark);
            igLaptop.name = "IgStreamLaptop";
            var screenGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            screenGo.name = "IgLaptopScreen";
            screenGo.transform.SetParent(igLaptop.transform, false);
            screenGo.transform.localPosition = new Vector3(0f, 0.18f, -0.14f);
            screenGo.transform.localRotation = Quaternion.Euler(-20f, 0f, 0f);
            screenGo.transform.localScale = new Vector3(0.5f, 0.34f, 0.03f);
            Object.DestroyImmediate(screenGo.GetComponent<BoxCollider>());
            laptopScreenRenderer = screenGo.GetComponent<MeshRenderer>();

            // ---- Knock plate next to Mr. IG's door (deliver the drink) ----
            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "KnockPlate";
            plate.transform.SetParent(d);
            plate.transform.SetPositionAndRotation(new Vector3(5.2f, 3.9f, 2.09f), Quaternion.Euler(0f, 0f, 0f));
            plate.transform.localScale = new Vector3(0.18f, 0.26f, 0.06f);
            plate.GetComponent<MeshRenderer>().sharedMaterial = metalDark;
            knockPlate = plate.AddComponent<ContentHouse.Interaction.DoorDeliverInteractable>();
            knockPlate.RequiredItem = "Gamer-Juice can";
            knockPlate.TaskId = "deliver";

            // ---- Desk prop + blocking pose for the Act IV QTE ----
            deskProp = Model("desk", d, new Vector3(6.7f, 3, -1.6f), 270f) ??
                Prim("Desk_Fallback", d, new Vector3(6.7f, 3.38f, -1.6f), new Vector3(1.5f, 0.1f, 0.8f), trim);
            deskProp.name = "MrIgDesk";
            deskBlockingPos = Anchor("DeskBlockingPos", d, new Vector3(4.15f, 3, 1.3f), 0f);
        }

        // =====================================================================
        // Waypoints & anchors
        // =====================================================================

        static Transform livingRoomCenter, kitchenCenter, enemySpawn, closetGuardPos, bedroomCenter, backyardLanding;

        static void BuildWaypointsAndAnchors(Transform root)
        {
            var w = new GameObject("AIFlow").transform;
            w.SetParent(root);

            groundRoute = new List<ContentHouse.Enemy.PatrolWaypoint>();
            upperRoute = new List<ContentHouse.Enemy.PatrolWaypoint>();

            void WP(List<ContentHouse.Enemy.PatrolWaypoint> list, string name, Vector3 pos, float pause = 0.6f, bool inspect = false)
            {
                var wp = new GameObject(name).AddComponent<ContentHouse.Enemy.PatrolWaypoint>();
                wp.transform.SetParent(w);
                wp.transform.position = pos;
                wp.PauseSeconds = pause;
                wp.Inspect = inspect;
                list.Add(wp);
            }

            // Ground floor route
            WP(groundRoute, "G_Dining", new Vector3(4, 0, -3));
            WP(groundRoute, "G_SW_Dining", new Vector3(3.2f, 0, -4.6f));
            WP(groundRoute, "G_KitchenDoor", new Vector3(0.8f, 0, -3.3f));
            WP(groundRoute, "G_Kitchen", new Vector3(-4.5f, 0, -3.4f), 1.2f, true);
            WP(groundRoute, "G_Archway", new Vector3(-4.7f, 0, 0.4f));
            WP(groundRoute, "G_Living", new Vector3(-3.8f, 0, 2.6f), 1.4f, true);
            WP(groundRoute, "G_NearStairs", new Vector3(0.6f, 0, 3.4f));
            WP(groundRoute, "G_Entry", new Vector3(6.2f, 0, 3.2f));
            WP(groundRoute, "G_LivingDiningArch", new Vector3(5, 0, 1.2f));

            // Upper floor route
            WP(upperRoute, "U_Landing", new Vector3(5.9f, 3, 4.8f));
            WP(upperRoute, "U_HallMid", new Vector3(2.5f, 3, 3.4f), 1.2f, true);
            WP(upperRoute, "U_MrIgDoor", new Vector3(4.15f, 3, 3.3f), 1.5f, true);
            WP(upperRoute, "U_HallEast", new Vector3(6.9f, 3, 4.7f));
            WP(upperRoute, "U_NearCloset", new Vector3(0.9f, 3, 4.3f));

            livingRoomCenter = Anchor("LivingRoomCenter", w, new Vector3(-4, 0, 3));
            kitchenCenter = Anchor("KitchenCenter", w, new Vector3(-3, 0, -3));
            enemySpawn = Anchor("EnemySpawn", w, new Vector3(5.5f, 0.05f, -5.3f), 180f);
            closetGuardPos = Anchor("ClosetGuardPos", w, new Vector3(1.25f, 3, 4.3f), 180f);
            bedroomCenter = Anchor("BedroomCenter", w, new Vector3(4, 3, -1.6f));
            backyardLanding = Anchor("BackyardLanding", w, new Vector3(4.1f, 0.05f, -7.6f), 180f);
        }

        static List<ContentHouse.Enemy.PatrolWaypoint> groundRoute, upperRoute;

        // =====================================================================
        // Player
        // =====================================================================

        static (Transform, ContentHouse.Player.PlayerController, Camera, ContentHouse.Player.FlashlightController) BuildPlayer(Transform root)
        {
            var go = new GameObject("Player");
            go.transform.SetParent(root);
            go.transform.SetPositionAndRotation(new Vector3(-4.5f, 0.02f, 1.5f), Quaternion.Euler(0f, 250f, 0f));

            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.7f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.85f, 0f);
            cc.slopeLimit = 50f;
            cc.stepOffset = 0.35f;

            var camGo = new GameObject("PlayerCamera");
            camGo.transform.SetParent(go.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 0.62f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 68f;
            cam.nearClipPlane = 0.08f;
            cam.farClipPlane = 60f;
            cam.backgroundColor = new Color(0.01f, 0.012f, 0.02f);
            camGo.AddComponent<AudioListener>();

            // Phone flashlight on the camera
            var flashGo = new GameObject("Flashlight");
            flashGo.transform.SetParent(camGo.transform, false);
            flashGo.transform.localPosition = new Vector3(0.12f, -0.1f, 0.25f);
            flashGo.transform.localRotation = Quaternion.Euler(3f, 0f, 0f);
            var spot = flashGo.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.spotAngle = 38f;
            spot.range = 15f;
            spot.intensity = 1.7f;
            spot.color = new Color(1f, 0.96f, 0.85f);
            spot.enabled = false;
            var flash = flashGo.AddComponent<ContentHouse.Player.FlashlightController>();
            flash.Spot = spot;

            var pc = go.AddComponent<ContentHouse.Player.PlayerController>();
            pc.Camera = cam;

            var inter = go.AddComponent<ContentHouse.Player.PlayerInteraction>();
            inter.Player = pc;

            return (go.transform, pc, cam, flash);
        }

        // =====================================================================
        // Enemy (custom build - no suitable free asset for mascot-mask intruder)
        // =====================================================================

        static ContentHouse.Enemy.EnemyAI BuildEnemy(Transform root)
        {
            var go = new GameObject("Intruder");
            go.transform.SetParent(root);
            go.transform.SetPositionAndRotation(new Vector3(5.5f, 0.05f, -5.3f), Quaternion.Euler(0f, 180f, 0f));
            go.layer = 2;   // Ignore Raycast: never blocks sight/interaction raycasts

            var agent = go.AddComponent<NavMeshAgent>();
            agent.height = 2.0f;
            agent.radius = 0.35f;
            agent.speed = 1.8f;
            agent.angularSpeed = 360f;
            agent.acceleration = 12f;
            agent.stoppingDistance = 0.25f;

            // Body: tall dark silhouette + oversized white mascot mask + crowbar.
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(go.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.52f, 1.05f, 0.4f);   // ~2.1 m tall
            body.GetComponent<MeshRenderer>().sharedMaterial = Std("EnemyCloth", new Color(0.05f, 0.05f, 0.06f));
            Object.DestroyImmediate(body.GetComponent<CapsuleCollider>());
            var bodyCol = go.AddComponent<CapsuleCollider>();
            bodyCol.center = new Vector3(0f, 1.05f, 0f);
            bodyCol.height = 2.1f;
            bodyCol.radius = 0.3f;

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "MascotMask";
            head.transform.SetParent(go.transform, false);
            head.transform.localPosition = new Vector3(0f, 1.95f, 0.02f);
            head.transform.localScale = Vector3.one * 0.42f;
            head.GetComponent<MeshRenderer>().sharedMaterial = Std("MaskWhite", new Color(0.88f, 0.87f, 0.82f));
            Object.DestroyImmediate(head.GetComponent<SphereCollider>());

            var eyeMat = Std("MaskEyes", new Color(0.02f, 0.02f, 0.03f));
            foreach (var ex in new[] { -0.09f, 0.09f })
            {
                var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                eye.name = "Eye";
                eye.transform.SetParent(go.transform, false);
                eye.transform.localPosition = new Vector3(ex, 2.0f, 0.2f);
                eye.transform.localScale = Vector3.one * 0.07f;
                eye.GetComponent<MeshRenderer>().sharedMaterial = eyeMat;
                Object.DestroyImmediate(eye.GetComponent<SphereCollider>());
            }

            var crowbar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crowbar.name = "Crowbar";
            crowbar.transform.SetParent(go.transform, false);
            crowbar.transform.localPosition = new Vector3(0.3f, 1.1f, 0.15f);
            crowbar.transform.localRotation = Quaternion.Euler(18f, 0f, 28f);
            crowbar.transform.localScale = new Vector3(0.05f, 0.05f, 0.62f);
            crowbar.GetComponent<MeshRenderer>().sharedMaterial = Std("CrowbarMat", new Color(0.35f, 0.33f, 0.3f), metallic: 0.7f, smooth: 0.55f);
            Object.DestroyImmediate(crowbar.GetComponent<BoxCollider>());

            // Hunting knife - hidden until the twist (parented so it follows him).
            var knife = GameObject.CreatePrimitive(PrimitiveType.Cube);
            knife.name = "HuntingKnife";
            knife.transform.SetParent(go.transform, false);
            knife.transform.localPosition = new Vector3(-0.34f, 1.35f, 0.12f);
            knife.transform.localRotation = Quaternion.Euler(0f, 0f, 65f);
            knife.transform.localScale = new Vector3(0.035f, 0.3f, 0.09f);
            knife.GetComponent<MeshRenderer>().sharedMaterial = Std("KnifeMat", new Color(0.75f, 0.78f, 0.8f), metallic: 0.85f, smooth: 0.8f);
            Object.DestroyImmediate(knife.GetComponent<BoxCollider>());
            knife.SetActive(false);
            enemyKnife = knife;

            // Enemy flashlight cone
            var fgo = new GameObject("EnemyFlashlight");
            fgo.transform.SetParent(go.transform, false);
            fgo.transform.localPosition = new Vector3(0f, 1.85f, 0.25f);
            fgo.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);
            var espot = fgo.AddComponent<Light>();
            espot.type = LightType.Spot;
            espot.spotAngle = 42f;
            espot.range = 13f;
            espot.intensity = 1.4f;
            espot.color = new Color(1f, 0.92f, 0.75f);
            espot.enabled = false;

            var ai = go.AddComponent<ContentHouse.Enemy.EnemyAI>();
            ai.Agent = agent;
            ai.Flashlight = espot;
            ai.GroundRoute = groundRoute;
            ai.UpperRoute = upperRoute;

            go.SetActive(false);   // spawns at the Act II glass-break
            return ai;
        }
        static GameObject enemyKnife;

        // =====================================================================
        // Systems / UI roots
        // =====================================================================

        static GameObject BuildSystems(Transform root)
        {
            var go = new GameObject("Systems");
            go.transform.SetParent(root);
            go.AddComponent<ContentHouse.Core.GameManager>();
            go.AddComponent<ContentHouse.Core.StoryManager>();
            go.AddComponent<ContentHouse.Core.PowerSystem>();
            go.AddComponent<ContentHouse.Audio.AmbienceController>();
            go.AddComponent<ContentHouse.Mic.MicLoudnessDetection>();
            go.AddComponent<ContentHouse.UI.QTEController>();
            return go;
        }

        static GameObject BuildUI(Transform root)
        {
            var go = new GameObject("UI");
            go.transform.SetParent(root);
            go.AddComponent<ContentHouse.UI.HUDController>();
            go.AddComponent<ContentHouse.UI.MainMenuUI>();
            go.AddComponent<ContentHouse.UI.EpilogueUI>();

            var phone = new GameObject("PhoneRoot");
            phone.transform.SetParent(root);
            phone.AddComponent<ContentHouse.Phone.PhoneController>();
            return phone;
        }

        // =====================================================================
        // Wiring
        // =====================================================================

        static void WireEverything(GameObject systems, GameObject phoneRoot,
            Transform playerT, ContentHouse.Player.PlayerController pc, Camera cam, ContentHouse.Player.FlashlightController flash,
            ContentHouse.Enemy.EnemyAI enemy)
        {
            var gm = systems.GetComponent<ContentHouse.Core.GameManager>();
            var story = systems.GetComponent<ContentHouse.Core.StoryManager>();
            var power = systems.GetComponent<ContentHouse.Core.PowerSystem>();
            var ambience = systems.GetComponent<ContentHouse.Audio.AmbienceController>();
            var qte = systems.GetComponent<ContentHouse.UI.QTEController>();
            var hud = systems.GetComponent<ContentHouse.UI.HUDController>() != null ? systems.GetComponent<ContentHouse.UI.HUDController>() : null;

            // HUD / menu / epilogue live on the UI GO.
            var uiGo = GameObject.Find("UI");
            var hudC = uiGo != null ? uiGo.GetComponent<ContentHouse.UI.HUDController>() : null;
            var epi = uiGo != null ? uiGo.GetComponent<ContentHouse.UI.EpilogueUI>() : null;
            var phone = phoneRoot.GetComponent<ContentHouse.Phone.PhoneController>();

            // GameManager
            gm.Player = pc;
            gm.HUD = hudC;
            gm.Phone = phone;
            gm.Story = story;
            gm.Enemy = enemy;
            gm.Power = power;
            gm.Ambience = ambience;
            gm.SetCheckpoint(playerT.position, playerT.eulerAngles.y);
            gm.PlayerLocked = true;   // menu state

            // PowerSystem
            power.HouseLights = houseLights;
            power.MoonLight = moonLight;
            power.DoorGlow = doorGlow;

            // Ambience
            ambience.LightningLight = lightningLight;

            // Phone
            phone.HallCam = hallCam;
            phone.LivingCam = livingCam;
            phone.Flashlight = flash;

            // CCTV laptop screen link
            hallCam.ScreenRenderer = laptopScreenRenderer;

            // Story
            story.GM = gm;
            story.Player = pc;
            story.HUD = hudC;
            story.Phone = phone;
            story.Ambience = ambience;
            story.Power = power;
            story.Enemy = enemy;
            story.QTE = qte;
            story.Epilogue = epi;
            story.HallCam = hallCam;
            story.MrIgDoor = mrIgDoor;
            story.HallCloset = hallCloset;
            story.KitchenWindow = FindWindow("Window_KitchenRear");
            story.DiningWindow = FindWindow("Window_DiningRear");
            story.EscapeWindow = FindWindow("Window_Escape");
            story.LivingRoomWindow = FindWindow("Window_LivingFront");
            story.EntryWindow = FindWindow("Window_EntryFront");
            story.PatioDoor = patioDoor;
            story.DrinkCanPickup = drinkCan;
            story.DeliveryBox = deliveryBox;
            story.LivingRoomCenter = livingRoomCenter;
            story.KitchenCenter = kitchenCenter;
            story.EnemySpawn = enemySpawn;
            story.ClosetGuardPos = closetGuardPos;
            story.BedroomCenter = bedroomCenter;
            story.BackyardLanding = backyardLanding;
            story.DeskProp = deskProp;
            story.DeskBlockingPos = deskBlockingPos;
            story.IntruderKnife = enemyKnife;
        }

        static ContentHouse.Interaction.WindowLockInteractable FindWindow(string name)
        {
            var go = GameObject.Find(name);
            return go != null ? go.GetComponent<ContentHouse.Interaction.WindowLockInteractable>() : null;
        }
    }
}




