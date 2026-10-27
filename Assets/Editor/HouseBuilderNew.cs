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
    /// Level generator for the downloaded "House with rounded neighbourhood"
    /// model (Assets/HousePack). Places the house + its collider companion,
    /// converts its real doors and glass panes into gameplay interactables,
    /// wires the full story (garage replaces the basement), bakes NavMesh and
    /// saves the scene.
    /// Menu: Content House > Generate House Scene (New Model)
    /// </summary>
    public static class HouseBuilderNew
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string MaterialsDir = "Assets/Materials";
        const string HouseFbx = "Assets/HousePack/House.fbx";
        const string CollidersFbx = "Assets/HousePack/House_Colliders.fbx";
        const string FbxDir = "Assets/Kenney/FBX";
        const float ModelScale = 1f;
        const float KenneyScaleFix = 1f / 4.9f;

        // Layout constants measured from the model (HouseAnalyzer).
        const float GroundY = 0.40f;   // ground-floor walkable
        const float UpperY = 3.74f;    // upper-floor walkable
        const float OutsideY = 0.00f;   // street / garage / backyard

        static Material trim, metalDark, glassMat;

        static readonly List<ContentHouse.Interaction.DoorInteractable> doors =
            new List<ContentHouse.Interaction.DoorInteractable>();
        static readonly List<Light> houseLights = new List<Light>();

        [MenuItem("Content House/Generate House Scene (New Model)")]
        public static void GenerateAll()
        {
            EnsureFolders();
            MakeMaterials();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ApplyRenderSettings();

            var root = new GameObject("GameRoot").transform;

            // ---- house: visual + colliders ----
            var houseProto = AssetDatabase.LoadAssetAtPath<GameObject>(HouseFbx);
            if (houseProto == null) { Debug.LogError("House.fbx not imported"); return; }
            var house = (GameObject)Object.Instantiate(houseProto, root);
            house.name = "HouseVisual";
            house.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var colProto = AssetDatabase.LoadAssetAtPath<GameObject>(CollidersFbx);
            if (colProto == null) { Debug.LogError("House_Colliders.fbx not imported"); return; }
            var colliders = (GameObject)Object.Instantiate(colProto, root);
            colliders.name = "HouseColliders";
            colliders.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var r in colliders.GetComponentsInChildren<MeshRenderer>())
            {
                var mr = r.GetComponent<MeshFilter>();
                if (mr != null && mr.sharedMesh != null)
                {
                    var mc = r.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mr.sharedMesh;
                    mc.convex = false;
                }
                r.enabled = false;   // collision-only
            }

            // Keep the player inside the modelled yard (play-space bounds).
            AddBoundsWalls(root);
            // Guarantee walkable floors everywhere (collider model has gaps).
            AddGuaranteedFloors(root);

            // ---- gameplay wiring on the real model ----
            BuildDoorsAndWindows(house.transform);
            BuildHideSpot(house.transform);
            BuildDevicesAndProps(house.transform);
            BuildWaypointsAndAnchors(root);
            BuildLighting(root);
            var cctv = BuildCCTV(root);            var (playerT, pc, cam, flash) = BuildPlayer(root);
            var enemy = BuildEnemy(root);
            var systems = BuildSystems(root);
            var uiPhone = BuildUI(root);

            WireEverything(systems, uiPhone, playerT, pc, cam, flash, enemy, cctv);

            // ---- NavMesh from the collider model ----
            var surfaceGo = new GameObject("NavMeshSurface");
            surfaceGo.transform.SetParent(root);
            var surface = surfaceGo.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;

            foreach (var d in doors) d.ForceOpenState();   // bake with doorways open
            surface.BuildNavMesh();
            foreach (var d in doors) d.ForceClosedState();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("HouseBuilderNew: scene generated at " + ScenePath);
        }

        // =====================================================================
        // Setup
        // =====================================================================

        static void EnsureFolders()
        {
            foreach (var d in new[] { "Assets/Scenes", MaterialsDir })
                if (!AssetDatabase.IsValidFolder(d))
                    AssetDatabase.CreateFolder(Path.GetDirectoryName(d).Replace('\\', '/'), Path.GetFileName(d));
            AssetDatabase.Refresh();
        }

        static Material Std(string name, Color c, float metallic = 0f, float smooth = 0.15f)
        {
            var path = MaterialsDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
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
            trim = Std("TrimWood", new Color(0.45f, 0.32f, 0.22f));
            metalDark = Std("MetalDark", new Color(0.25f, 0.26f, 0.28f), metallic: 0.6f, smooth: 0.5f);
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

        static void AddBoundsWalls(Transform root)
        {
            var b = new GameObject("PlayBounds").transform;
            b.SetParent(root);
            void Wall(string n, Vector3 c, Vector3 s)
            {
                var go = new GameObject(n);
                go.transform.SetParent(b);
                go.transform.position = c;
                var col = go.AddComponent<BoxCollider>();
                col.center = Vector3.zero;
                col.size = s;
            }
            // Collider model bounds: x -21..52, z -28..56. Keep inside it.
            Wall("N", new Vector3(15f, 4f, 55f), new Vector3(90f, 10f, 2f));
            Wall("S", new Vector3(15f, 4f, -27f), new Vector3(90f, 10f, 2f));
            Wall("W", new Vector3(-20f, 4f, 14f), new Vector3(2f, 10f, 90f));
            Wall("E", new Vector3(51f, 4f, 14f), new Vector3(2f, 10f, 90f));
        }

        /// <summary>
        /// The collider companion model has floor gaps (player fell through the
        /// west living room and outside). Guarantee walkable floors: ground
        /// level, upper level (around the stair hole), garage and outdoor plane.
        /// Collider-only, invisible; they also feed the NavMesh bake.
        /// </summary>
        static void AddGuaranteedFloors(Transform root)
        {
            var f = new GameObject("GuaranteedFloors").transform;
            f.SetParent(root);
            void Floor(string n, float cx, float cz, float sx, float sz, float topY, float th = 0.3f)
            {
                var go = new GameObject(n);
                go.transform.SetParent(f);
                go.transform.position = new Vector3(cx, topY - th * 0.5f, cz);
                var col = go.AddComponent<BoxCollider>();
                col.center = Vector3.zero;
                col.size = new Vector3(sx, th, sz);
            }
            // Ground floor (interior, excluding garage bay): x -10.5..0.5
            Floor("GroundHouse", -5.0f, -6.75f, 11.0f, 15.5f, 0.41f);
            // Garage bay: x 0.3..3.7, z -13.9..-1.5 (outside level)
            Floor("GarageFloor", 2.0f, -7.7f, 3.4f, 12.4f, 0.02f);
            // Upper floor around the stair hole (hole: x -6.3..-2.6, z -8.1..-5.1)
            Floor("Upper_N", -3.15f, -2.5f, 13.3f, 5.2f, 3.73f, 0.2f);      // z -5.1..0.1
            Floor("Upper_S", -3.15f, -11.1f, 13.3f, 6.0f, 3.73f, 0.2f);     // z -14.1..-8.1
            Floor("Upper_W", -8.05f, -6.6f, 3.5f, 3.0f, 3.73f, 0.2f);       // x -9.8..-6.3
            Floor("Upper_E", 0.45f, -6.6f, 6.1f, 3.0f, 3.73f, 0.2f);        // x -2.6..3.5
            // Outdoor safety plane across the whole play bounds
            Floor("OutdoorPlane", 15.5f, 14.3f, 73f, 84f, 0.0f);
        }

        // =====================================================================
        // Model helpers
        // =====================================================================

        static Transform FindDeep(Transform parent, string name)
        {
            foreach (Transform t in parent)
            {
                if (t.name == name) return t;
                var r = FindDeep(t, name);
                if (r != null) return r;
            }
            return null;
        }

        /// <summary>Kenney model with the 4.9x scale fix + visual recentering.</summary>
        static GameObject Model(string modelName, Transform parent, Vector3 pos, float yawDeg,
            float scale = 1f, bool addCollider = true)
        {
            var proto = AssetDatabase.LoadAssetAtPath<GameObject>(FbxDir + "/" + modelName + ".fbx");
            if (proto == null) { Debug.LogWarning("missing Kenney model " + modelName); return null; }
            var go = Object.Instantiate(proto, parent);
            go.name = modelName;
            go.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
            go.transform.localScale = Vector3.one * (scale * ModelScale * KenneyScaleFix);
            var renderers = go.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length > 0)
            {
                var bd = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bd.Encapsulate(renderers[i].bounds);
                go.transform.position += new Vector3(pos.x - bd.center.x, pos.y - bd.min.y, pos.z - bd.center.z);
            }
            else go.transform.position = pos;
            if (addCollider)
            {
                var col = go.AddComponent<BoxCollider>();
                var bd2 = go.GetComponentInChildren<MeshRenderer>().bounds;
                col.center = go.transform.InverseTransformPoint(bd2.center);
                var ls = go.transform.InverseTransformVector(bd2.size);
                col.size = new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
            }
            return go;
        }

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

        static Transform Anchor(string name, Transform parent, Vector3 pos, float yaw = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            return go.transform;
        }

        // =====================================================================
        // Doors + windows (converted from the real model meshes)
        // =====================================================================

        static ContentHouse.Interaction.DoorInteractable mrIgDoor, entranceDoor, closetDoor;

        static ContentHouse.Interaction.WindowLockInteractable kitchenWindow, frontWindow,
            sideWindow, escapeWindow;

        static void BuildDoorsAndWindows(Transform house)
        {
            var dw = new GameObject("GameplayDoorsWindows").transform;
            dw.SetParent(house.parent);

            // Door_002 (2.46, 3.74..-8.71): Mr. IG's bedroom, locked.
            mrIgDoor = MakeDoor(house, dw, "Door_002", locked: true,
                lockedMsg: "Mr. IG is LIVE. Do not disturb the stream.", taskId: "");
            // Door_009 (rear entrance, outside level): the house's back door.
            entranceDoor = MakeDoor(house, dw, "Door_009", locked: false, lockedMsg: "", taskId: "patio");
            // Upper room doors stay usable.
            MakeDoor(house, dw, "Door_006", locked: false, lockedMsg: "", taskId: "");
            MakeDoor(house, dw, "Door_001", locked: false, lockedMsg: "", taskId: "");
            MakeDoor(house, dw, "Door_008", locked: false, lockedMsg: "", taskId: "");   // ground bath
            MakeDoor(house, dw, "Door", locked: false, lockedMsg: "", taskId: "");       // front interior

            // Glass panes become lockable / breakable windows.
            kitchenWindow = MakeWindow(house, "Glass_005", taskId: "");     // left wall (kitchen): false alarm + intruder entry
            frontWindow = MakeWindow(house, "Glass_006", taskId: "window1"); // front big window: lockable
            sideWindow = MakeWindow(house, "Glass_007", taskId: "window2");  // front-right: lockable
            escapeWindow = MakeWindow(house, "Glass_003", taskId: "");       // Mr. IG room, right wall: QTE escape
        }

        /// <summary>Convert a model door mesh into a hinged, lockable DoorInteractable.</summary>
        static ContentHouse.Interaction.DoorInteractable MakeDoor(Transform house, Transform parent,
            string nodeName, bool locked, string lockedMsg, string taskId)
        {
            var node = FindDeep(house, nodeName);
            if (node == null) { Debug.LogWarning("door node missing: " + nodeName); return null; }
            var mr = node.GetComponent<MeshRenderer>();
            var b = mr.bounds;

            // Hinge at one vertical edge of the door slab, at floor level.
            bool thinZ = b.size.z < 0.15f;                       // door plane along X
            Vector3 hingePos = thinZ
                ? new Vector3(b.min.x, b.min.y, b.center.z)
                : new Vector3(b.center.x, b.min.y, b.min.z);

            var rig = new GameObject("DoorRig_" + nodeName);
            rig.transform.SetParent(parent);
            rig.transform.SetPositionAndRotation(hingePos, Quaternion.identity);
            var hinge = new GameObject("Hinge").transform;
            hinge.SetParent(rig.transform, false);

            node.SetParent(hinge, true);   // keep world position

            // Interaction collider on the slab.
            var col = node.gameObject.AddComponent<BoxCollider>();
            col.center = node.InverseTransformPoint(b.center);
            var ls = node.InverseTransformVector(b.size);
            col.size = new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));

            var door = rig.AddComponent<ContentHouse.Interaction.DoorInteractable>();
            door.Hinge = hinge;
            door.OpenAngle = 95f;
            door.Locked = locked;
            door.LockedMessage = lockedMsg;
            door.TaskId = taskId;
            doors.Add(door);
            return door;
        }

        /// <summary>Make a model glass pane a lockable/breakable window.</summary>
        static ContentHouse.Interaction.WindowLockInteractable MakeWindow(Transform house,
            string nodeName, string taskId)
        {
            var node = FindDeep(house, nodeName);
            if (node == null) { Debug.LogWarning("glass node missing: " + nodeName); return null; }
            var mr = node.GetComponent<MeshRenderer>();
            var b = mr.bounds;
            var col = node.gameObject.AddComponent<BoxCollider>();
            col.center = node.InverseTransformPoint(b.center);
            var ls = node.InverseTransformVector(b.size);
            col.size = new Vector3(Mathf.Abs(ls.x) + 0.02f, Mathf.Abs(ls.y), Mathf.Abs(ls.z) + 0.02f);
            var w = node.gameObject.AddComponent<ContentHouse.Interaction.WindowLockInteractable>();
            w.GlassPane = node.gameObject;
            w.StartsLocked = false;
            w.TaskId = taskId;
            return w;
        }

        // =====================================================================
        // Hide spot (scripted closet) - custom cabinet on the upper hallway
        // =====================================================================

        static ContentHouse.Interaction.HideSpot hallCloset;

        static void BuildHideSpot(Transform house)
        {
            var h = new GameObject("HideSpots").transform;
            h.SetParent(house.parent);

            // Cabinet against the z = -8.73 wall (south of the Mr. IG doorway),
            // opening facing north into the hallway.
            var closet = new GameObject("HallCloset").transform;
            closet.SetParent(h);
            Vector3 c = new Vector3(0.9f, UpperY, -8.30f);
            Prim("Closet_Back", closet, c + new Vector3(0f, 1.1f, -0.38f), new Vector3(1.5f, 2.2f, 0.08f), trim);
            Prim("Closet_SideW", closet, c + new Vector3(-0.74f, 1.1f, 0f), new Vector3(0.08f, 2.2f, 0.85f), trim);
            Prim("Closet_SideE", closet, c + new Vector3(0.74f, 1.1f, 0f), new Vector3(0.08f, 2.2f, 0.85f), trim);
            Prim("Closet_Top", closet, c + new Vector3(0f, 2.2f, 0f), new Vector3(1.5f, 0.08f, 0.85f), trim);
            Prim("Closet_Rod", closet, c + new Vector3(0f, 1.95f, 0f), new Vector3(0.04f, 0.04f, 0.8f), metalDark, collider: false);

            // Hinged door on the north face (+z).
            var hinge = new GameObject("Hinge").transform;
            hinge.SetParent(closet);
            hinge.SetPositionAndRotation(c + new Vector3(-0.7f, 0f, 0.43f), Quaternion.identity);
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "Panel";
            panel.transform.SetParent(hinge, false);
            panel.transform.localPosition = new Vector3(0.7f, 1.1f, 0f);
            panel.transform.localScale = new Vector3(1.4f, 2.2f, 0.07f);
            panel.GetComponent<MeshRenderer>().sharedMaterial = trim;
            var knob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            knob.name = "Knob";
            knob.transform.SetParent(hinge, false);
            knob.transform.localPosition = new Vector3(1.3f, 1.05f, 0.06f);
            knob.transform.localScale = Vector3.one * 0.09f;
            knob.GetComponent<MeshRenderer>().sharedMaterial = metalDark;
            var cdoor = panel.AddComponent<ContentHouse.Interaction.DoorInteractable>();
            cdoor.Hinge = hinge;
            cdoor.OpenAngle = 95f;
            doors.Add(cdoor);

            // Hide pad on the door front.
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "HidePad_Closet";
            pad.transform.SetParent(closet);
            pad.transform.SetPositionAndRotation(c + new Vector3(0f, 1.25f, 0.48f), Quaternion.Euler(0f, 180f, 0f));
            pad.transform.localScale = new Vector3(1.5f, 1.6f, 0.05f);
            pad.GetComponent<MeshRenderer>().sharedMaterial = metalDark;
            var spot = pad.AddComponent<ContentHouse.Interaction.HideSpot>();
            spot.SpotKind = ContentHouse.Interaction.HideSpot.Kind.Closet;
            spot.SpotName = "hallway closet";
            spot.StandAnchor = Anchor("Closet_Stand", closet, c + new Vector3(0f, 0.06f, -0.05f), 180f);
            spot.ExitAnchor = Anchor("Closet_Exit", closet, c + new Vector3(0f, 0.06f, 1.0f));
            spot.ClosetDoor = cdoor;
            hallCloset = spot;
        }

        // =====================================================================
        // Devices, chores, props
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

        static void BuildDevicesAndProps(Transform house)
        {
            var d = new GameObject("Devices").transform;
            d.SetParent(house.parent);

            // ---- Delivery box outside the rear entrance door (Door_009) ----
            var boxGo = Model("cardboardBoxClosed", d, new Vector3(-0.8f, OutsideY, -14.8f), 25f)
                ?? Prim("DeliveryBox", d, new Vector3(-0.8f, 0.3f, -14.8f), new Vector3(0.7f, 0.55f, 0.7f), trim);
            boxGo.name = "DeliveryBox";
            deliveryBox = boxGo.AddComponent<ContentHouse.Interaction.DeliveryBoxInteractable>();
            deliveryBox.GivesItem = "Gamer-Juice 12-pack";
            deliveryBox.TaskId = "box";
            for (int i = 0; i < 3; i++)
                Prim("Can" + i, boxGo.transform, new Vector3(-0.06f + i * 0.06f, 0.4f, 0f),
                    new Vector3(0.018f, 0.03f, 0.018f), metalDark, collider: false);

            // ---- Mr. IG's mini-fridge (upper hallway by his door) ----
            var fridgeGo = Model("kitchenFridgeSmall", d, new Vector3(3.25f, UpperY, -9.6f), 270f);
            if (fridgeGo == null)
                fridgeGo = Prim("MiniFridge", d, new Vector3(3.25f, UpperY + 0.45f, -9.6f),
                    new Vector3(0.6f, 0.9f, 0.6f), metalDark);
            fridgeGo.name = "MiniFridge";
            // Give it a real interaction collider.
            var fcol = fridgeGo.AddComponent<BoxCollider>();
            fcol.center = Vector3.zero; fcol.size = new Vector3(0.7f, 1.0f, 0.7f);
            var stockedCans = new GameObject("StockedCans");
            stockedCans.transform.SetParent(fridgeGo.transform, false);
            stockedCans.SetActive(false);
            miniFridge = fridgeGo.AddComponent<ContentHouse.Interaction.FridgeInteractable>();
            miniFridge.RequiredItem = "Gamer-Juice 12-pack";
            miniFridge.StockedCans = stockedCans;
            miniFridge.TaskId = "fridge";

            // Single can for the door delivery (hidden until stocked).
            var canGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            canGo.name = "GamerJuiceCan";
            canGo.transform.SetParent(d);
            canGo.transform.SetPositionAndRotation(new Vector3(3.25f, UpperY + 1.0f, -9.6f), Quaternion.identity);
            canGo.transform.localScale = new Vector3(0.1f, 0.14f, 0.1f);
            canGo.GetComponent<MeshRenderer>().sharedMaterial = Std("CanGreen", new Color(0.2f, 0.75f, 0.3f));
            drinkCan = canGo.AddComponent<ContentHouse.Interaction.PickupItem>();
            drinkCan.ItemName = "Gamer-Juice can";
            drinkCan.PickUpLine = "One ice-cold Gamer-Juice for the boss.";
            canGo.SetActive(false);

            // ---- Router + breakers in the garage (no basement in this house) ----
            var routerGo = new GameObject("Router");
            routerGo.transform.SetParent(d);
            routerGo.transform.SetPositionAndRotation(new Vector3(3.35f, 1.0f, -8.4f), Quaternion.Euler(0f, -90f, 0f));
            Prim("RouterBody", routerGo.transform, Vector3.zero + new Vector3(0, 0.08f, 0), new Vector3(0.34f, 0.2f, 0.2f), metalDark, collider: false);
            var ledRed = Prim("LedRed", routerGo.transform, new Vector3(0.1f, 0.1f, 0.05f), new Vector3(0.05f, 0.05f, 0.05f), Emissive("GlowLedRed", Color.black, Color.red), collider: false);
            var ledGreenObj = Prim("LedGreen", routerGo.transform, new Vector3(0.1f, 0.1f, 0.05f), new Vector3(0.05f, 0.05f, 0.05f), Emissive("GlowLedGreen", Color.black, Color.green), collider: false);
            ledGreenObj.SetActive(false);
            var routerCol = routerGo.AddComponent<BoxCollider>();
            routerCol.center = new Vector3(0, 0.1f, 0);
            routerCol.size = new Vector3(0.4f, 0.3f, 0.3f);
            router = routerGo.AddComponent<ContentHouse.Interaction.RouterInteractable>();
            router.LedRed = ledRed;
            router.LedGreen = ledGreenObj;
            router.TaskId = "router";

            var panel = new GameObject("BreakerPanel");
            panel.transform.SetParent(d);
            panel.transform.SetPositionAndRotation(new Vector3(0.85f, 1.5f, -11.9f), Quaternion.Euler(0f, 90f, 0f));
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
            breakerPanel.On = new[] { true, false, true };
            breakerPanel.TaskId = "breakers";

            // ---- Backup stream laptop on the dining table (waveform chore) ----
            var laptopGo = Model("laptop", d, new Vector3(-7.5f, GroundY + 1.1f, -6.8f), 25f);
            if (laptopGo == null)
                laptopGo = Prim("BackupLaptop", d, new Vector3(-7.5f, GroundY + 1.12f, -6.8f),
                    new Vector3(0.42f, 0.05f, 0.3f), metalDark);
            laptopGo.name = "BackupLaptop";
            laptopCal = laptopGo.AddComponent<ContentHouse.Interaction.LaptopCalibrationInteractable>();
            laptopCal.TaskId = "laptop";

            // ---- Mr. IG's stream desk + laptop (shows the GoPro feed) ----
            deskProp = Model("desk", d, new Vector3(3.35f, UpperY, -4.55f), 180f);
            if (deskProp == null)
                deskProp = Prim("MrIgDesk", d, new Vector3(3.35f, UpperY + 0.38f, -4.55f),
                    new Vector3(1.5f, 0.08f, 0.8f), trim);
            deskProp.name = "MrIgDesk";
            var igLaptop = Model("laptop", d, new Vector3(3.35f, UpperY + 0.82f, -4.55f), 180f);
            if (igLaptop == null)
                igLaptop = Prim("IgLaptop", d, new Vector3(3.35f, UpperY + 0.82f, -4.55f),
                    new Vector3(0.42f, 0.05f, 0.3f), metalDark);
            igLaptop.name = "IgStreamLaptop";
            var screenGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            screenGo.name = "IgLaptopScreen";
            screenGo.transform.SetParent(igLaptop.transform, false);
            screenGo.transform.localPosition = new Vector3(0f, 0.18f, -0.14f);
            screenGo.transform.localRotation = Quaternion.Euler(-20f, 0f, 0f);
            screenGo.transform.localScale = new Vector3(0.5f, 0.34f, 0.03f);
            Object.DestroyImmediate(screenGo.GetComponent<BoxCollider>());
            laptopScreenRenderer = screenGo.GetComponent<MeshRenderer>();
            deskBlockingPos = Anchor("DeskBlockingPos", d, new Vector3(2.46f, UpperY, -8.2f), 0f);

            // ---- Knock plate next to Mr. IG's door ----
            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "KnockPlate";
            plate.transform.SetParent(d);
            plate.transform.SetPositionAndRotation(new Vector3(3.3f, UpperY + 1.1f, -8.55f), Quaternion.identity);
            plate.transform.localScale = new Vector3(0.18f, 0.26f, 0.06f);
            plate.GetComponent<MeshRenderer>().sharedMaterial = metalDark;
            knockPlate = plate.AddComponent<ContentHouse.Interaction.DoorDeliverInteractable>();
            knockPlate.RequiredItem = "Gamer-Juice can";
            knockPlate.TaskId = "deliver";

            // ---- Mr. IG himself: seated silhouette at his stream desk ----
            var igFig = new GameObject("MrIG_Figure");
            igFig.transform.SetParent(d);
            igFig.transform.SetPositionAndRotation(new Vector3(2.8f, UpperY, -4.55f),
                Quaternion.Euler(0f, 90f, 0f));   // facing his desk / the wall
            var hoodie = Std("MrIgHoodie", new Color(0.13f, 0.14f, 0.2f));
            var torso = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            torso.name = "Torso";
            torso.transform.SetParent(igFig.transform, false);
            torso.transform.localPosition = new Vector3(0f, 0.62f, 0f);
            torso.transform.localScale = new Vector3(0.42f, 0.3f, 0.34f);
            torso.GetComponent<MeshRenderer>().sharedMaterial = hoodie;
            Object.DestroyImmediate(torso.GetComponent<CapsuleCollider>());
            var headM = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            headM.name = "Head";
            headM.transform.SetParent(igFig.transform, false);
            headM.transform.localPosition = new Vector3(0f, 1.18f, 0f);
            headM.transform.localScale = Vector3.one * 0.24f;
            headM.GetComponent<MeshRenderer>().sharedMaterial = Std("MrIgSkin", new Color(0.85f, 0.68f, 0.55f));
            Object.DestroyImmediate(headM.GetComponent<SphereCollider>());
            // Gaming headphones: band + ear cups.
            var bandM = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bandM.name = "HeadphoneBand";
            bandM.transform.SetParent(igFig.transform, false);
            bandM.transform.localPosition = new Vector3(0f, 1.3f, 0f);
            bandM.transform.localScale = new Vector3(0.26f, 0.03f, 0.03f);
            bandM.GetComponent<MeshRenderer>().sharedMaterial = metalDark;
            Object.DestroyImmediate(bandM.GetComponent<BoxCollider>());
            foreach (var ex2 in new[] { -0.13f, 0.13f })
            {
                var cup = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                cup.name = "EarCup";
                cup.transform.SetParent(igFig.transform, false);
                cup.transform.localPosition = new Vector3(ex2, 1.18f, 0f);
                cup.transform.localScale = Vector3.one * 0.09f;
                cup.GetComponent<MeshRenderer>().sharedMaterial = metalDark;
                Object.DestroyImmediate(cup.GetComponent<SphereCollider>());
            }
        }

        // =====================================================================
        // Waypoints + story anchors
        // =====================================================================

        static List<ContentHouse.Enemy.PatrolWaypoint> groundRoute, upperRoute;
        static Transform livingRoomCenter, kitchenCenter, enemySpawn, closetGuardPos,
            bedroomCenter, backyardLanding;

        static void BuildWaypointsAndAnchors(Transform root)
        {
            var w = new GameObject("AIFlow").transform;
            w.SetParent(root);
            groundRoute = new List<ContentHouse.Enemy.PatrolWaypoint>();
            upperRoute = new List<ContentHouse.Enemy.PatrolWaypoint>();

            void WP(List<ContentHouse.Enemy.PatrolWaypoint> list, string name, Vector3 pos,
                float pause = 0.6f, bool inspect = false)
            {
                var wp = new GameObject(name).AddComponent<ContentHouse.Enemy.PatrolWaypoint>();
                wp.transform.SetParent(w);
                wp.transform.position = pos;
                wp.PauseSeconds = pause;
                wp.Inspect = inspect;
                list.Add(wp);
            }

            // Ground route (kitchen/dining/living/hall/garage).
            WP(groundRoute, "G_Kitchen", new Vector3(-8.5f, GroundY, -11f), 1.2f, true);
            WP(groundRoute, "G_Dining", new Vector3(-7.4f, GroundY, -6.8f));
            WP(groundRoute, "G_Living", new Vector3(-9.0f, GroundY, -2.3f), 1.2f, true);
            WP(groundRoute, "G_Hall", new Vector3(-3.0f, GroundY, -4.5f));
            WP(groundRoute, "G_StairBase", new Vector3(-4.4f, GroundY, -6.6f));
            WP(groundRoute, "G_RearHall", new Vector3(-1.0f, GroundY, -12.4f));
            WP(groundRoute, "G_Garage", new Vector3(1.9f, OutsideY, -7.5f), 1.0f, true);
            WP(groundRoute, "G_GarageRear", new Vector3(1.9f, OutsideY, -12f));

            // Upper route (stairs, hallway, bedroom doors).
            WP(upperRoute, "U_Landing", new Vector3(-4.1f, UpperY, -5.4f));
            WP(upperRoute, "U_Hall", new Vector3(-2.0f, UpperY, -7.2f), 1.2f, true);
            WP(upperRoute, "U_MrIgDoor", new Vector3(2.4f, UpperY, -9.4f), 1.5f, true);
            WP(upperRoute, "U_RearHall", new Vector3(0.3f, UpperY, -11.5f));
            WP(upperRoute, "U_BathHall", new Vector3(-5.8f, UpperY, -9.5f));
            WP(upperRoute, "U_NearCloset", new Vector3(0.9f, UpperY, -6.6f));

            livingRoomCenter = Anchor("LivingRoomCenter", w, new Vector3(-8.5f, GroundY, -2.4f));
            kitchenCenter = Anchor("KitchenCenter", w, new Vector3(-8.5f, GroundY, -11f));
            enemySpawn = Anchor("EnemySpawn", w, new Vector3(-9.0f, GroundY, -6.05f), 90f);
            closetGuardPos = Anchor("ClosetGuardPos", w, new Vector3(0.9f, UpperY, -7.0f), 180f);
            bedroomCenter = Anchor("BedroomCenter", w, new Vector3(2.4f, UpperY, -5.9f));
            backyardLanding = Anchor("BackyardLanding", w, new Vector3(5.6f, OutsideY, -8f), 180f);
        }

        // =====================================================================
        // Lighting / power
        // =====================================================================

        static Light moonLight, doorGlow, lightningLight;

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

        static void BuildLighting(Transform root)
        {
            var l = new GameObject("Lighting").transform;
            l.SetParent(root);

            void Reg(Light x) => houseLights.Add(x);

            Reg(Point("LivingLight", l, new Vector3(-9.0f, 2.3f, -2.3f), new Color(0.95f, 0.93f, 0.85f), 0.9f, 7f));
            Reg(Point("DiningLight", l, new Vector3(-7.5f, 2.3f, -6.8f), new Color(0.95f, 0.93f, 0.85f), 0.8f, 7f));
            Reg(Point("KitchenLight", l, new Vector3(-8.5f, 2.3f, -11f), new Color(0.9f, 0.95f, 0.88f), 0.85f, 7f));
            Reg(Point("HallLight", l, new Vector3(-3.0f, 2.3f, -4.5f), new Color(0.9f, 0.9f, 0.8f), 0.8f, 8f));
            Reg(Point("RearHallLight", l, new Vector3(-1.0f, 2.3f, -12f), new Color(0.9f, 0.9f, 0.8f), 0.7f, 6f));
            Reg(Point("GarageBulb", l, new Vector3(1.9f, 2.0f, -8f), new Color(1f, 0.9f, 0.7f), 0.7f, 7f));
            Reg(Point("EntranceLight", l, new Vector3(-0.8f, 2.4f, -14.3f), new Color(1f, 0.85f, 0.6f), 0.8f, 6f));
            Reg(Point("UpperHallLight", l, new Vector3(-2.0f, 5.6f, -7.2f), new Color(0.9f, 0.9f, 0.8f), 0.8f, 8f));
            Reg(Point("UpperRearLight", l, new Vector3(0.3f, 5.6f, -11.5f), new Color(0.9f, 0.9f, 0.8f), 0.7f, 6f));
            Reg(Point("Bed03Light", l, new Vector3(-4.6f, 5.4f, -1.5f), new Color(0.9f, 0.88f, 0.8f), 0.8f, 7f));
            Reg(Point("Bed02Light", l, new Vector3(-6.4f, 5.4f, -11.8f), new Color(0.9f, 0.88f, 0.8f), 0.8f, 7f));
            Reg(Point("UpperBathLight", l, new Vector3(-6.5f, 5.2f, -7.5f), new Color(0.95f, 0.95f, 0.9f), 0.6f, 5f));

            // Mr. IG's room survives the blackout (on 5G battery).
            Point("MrIgRoomLight", l, new Vector3(2.4f, 5.4f, -5.9f), new Color(0.55f, 0.4f, 1f), 0.6f, 8f);
            Point("MrIgScreenGlow", l, new Vector3(3.35f, UpperY + 1.1f, -4.55f), new Color(0.4f, 0.6f, 1f), 0.5f, 3.5f);

            doorGlow = Point("DoorGlow", l, new Vector3(2.46f, UpperY + 0.5f, -8.55f), new Color(0.4f, 0.55f, 1f), 0.55f, 2.6f);
            doorGlow.enabled = false;

            var mgo = new GameObject("MoonLight");
            mgo.transform.SetParent(l);
            mgo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            moonLight = mgo.AddComponent<Light>();
            moonLight.type = LightType.Directional;
            moonLight.color = new Color(0.45f, 0.55f, 0.8f);
            moonLight.intensity = 0.32f;
            moonLight.enabled = false;

            var lgo = new GameObject("LightningLight");
            lgo.transform.SetParent(l);
            lgo.transform.rotation = Quaternion.Euler(65f, 20f, 0f);
            lightningLight = lgo.AddComponent<Light>();
            lightningLight.type = LightType.Directional;
            lightningLight.color = new Color(0.8f, 0.85f, 1f);
            lightningLight.intensity = 0.35f;
        }

        // =====================================================================
        // CCTV
        // =====================================================================

        static ContentHouse.CCTV.CCTVCameraFeed hallCam, livingCam;

        static (ContentHouse.CCTV.CCTVCameraFeed, ContentHouse.CCTV.CCTVCameraFeed) BuildCCTV(Transform root)
        {
            var c = new GameObject("CCTV").transform;
            c.SetParent(root);
            hallCam = BuildCam(c, "CCTV_Hall", new Vector3(0.3f, UpperY + 2.2f, -8.55f),
                new Vector3(2.46f, UpperY + 1.2f, -9.4f), "HALL CAM 02");
            livingCam = BuildCam(c, "CCTV_Living", new Vector3(-9.3f, 2.7f, -3.5f),
                new Vector3(-5f, GroundY + 0.8f, -5f), "LIVING CAM 01");
            return (hallCam, livingCam);
        }

        static ContentHouse.CCTV.CCTVCameraFeed BuildCam(Transform parent, string name, Vector3 pos,
            Vector3 lookAt, string feedName)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(lookAt - pos, Vector3.up);
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 62f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 30f;
            cam.enabled = false;
            var feed = go.AddComponent<ContentHouse.CCTV.CCTVCameraFeed>();
            feed.Cam = cam;
            feed.FeedName = feedName;
            return feed;
        }

        // =====================================================================
        // Player / enemy / systems
        // =====================================================================

        static (Transform, ContentHouse.Player.PlayerController, Camera, ContentHouse.Player.FlashlightController)
            BuildPlayer(Transform root)
        {
            var go = new GameObject("Player");
            go.transform.SetParent(root);
            // Spawn on open living-room floor (west side, clear of the TV stand
            // and armchairs - NOT in the ground-floor closet nook at z~-3.9).
            go.transform.SetPositionAndRotation(new Vector3(-8.2f, GroundY + 0.03f, -2.8f),
                Quaternion.Euler(0f, 90f, 0f));   // face east, into the house

            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.7f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.85f, 0f);
            cc.slopeLimit = 50f;
            cc.stepOffset = 0.45f;   // house entrance steps are ~0.4m

            var camGo = new GameObject("PlayerCamera");
            camGo.transform.SetParent(go.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.55f, 0f);   // eye at human height
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 68f;
            cam.nearClipPlane = 0.08f;
            cam.farClipPlane = 80f;
            cam.backgroundColor = new Color(0.01f, 0.012f, 0.02f);
            camGo.AddComponent<AudioListener>();

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
            go.AddComponent<ContentHouse.Player.PlayerInteraction>().Player = pc;
            return (go.transform, pc, cam, flash);
        }

        static ContentHouse.Enemy.EnemyAI BuildEnemy(Transform root)
        {
            var go = new GameObject("Intruder");
            go.transform.SetParent(root);
            go.transform.SetPositionAndRotation(new Vector3(-9.0f, GroundY, -6.05f), Quaternion.Euler(0f, 90f, 0f));
            go.layer = 2;   // Ignore Raycast

            var agent = go.AddComponent<NavMeshAgent>();
            agent.height = 2.0f;
            agent.radius = 0.35f;
            agent.speed = 1.8f;
            agent.angularSpeed = 360f;
            agent.acceleration = 12f;
            agent.stoppingDistance = 0.25f;

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(go.transform, false);
            body.transform.localPosition = new Vector3(0f, 1.05f, 0f);
            body.transform.localScale = new Vector3(0.52f, 1.05f, 0.4f);
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
            crowbar.GetComponent<MeshRenderer>().sharedMaterial = Std("CrowbarMat", new Color(0.35f, 0.33f, 0.3f), 0.7f, 0.55f);
            Object.DestroyImmediate(crowbar.GetComponent<BoxCollider>());

            var knife = GameObject.CreatePrimitive(PrimitiveType.Cube);
            knife.name = "HuntingKnife";
            knife.transform.SetParent(go.transform, false);
            knife.transform.localPosition = new Vector3(-0.34f, 1.35f, 0.12f);
            knife.transform.localRotation = Quaternion.Euler(0f, 0f, 65f);
            knife.transform.localScale = new Vector3(0.035f, 0.3f, 0.09f);
            knife.GetComponent<MeshRenderer>().sharedMaterial = Std("KnifeMat", new Color(0.75f, 0.78f, 0.8f), 0.85f, 0.8f);
            Object.DestroyImmediate(knife.GetComponent<BoxCollider>());
            knife.SetActive(false);
            enemyKnife = knife;

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

            go.SetActive(false);
            return ai;
        }
        static GameObject enemyKnife;

        static GameObject BuildSystems(Transform root)
        {
            var go = new GameObject("Systems");
            go.transform.SetParent(root);
            go.AddComponent<ContentHouse.Core.GameManager>();
            go.AddComponent<ContentHouse.Core.StoryManager>();
            var power = go.AddComponent<ContentHouse.Core.PowerSystem>();
            power.HouseLights = houseLights;
            power.MoonLight = moonLight;
            power.DoorGlow = doorGlow;
            var ambience = go.AddComponent<ContentHouse.Audio.AmbienceController>();
            ambience.LightningLight = lightningLight;
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
            go.AddComponent<ContentHouse.UI.NarrationScreen>();
            var phone = new GameObject("PhoneRoot");
            phone.transform.SetParent(root);
            phone.AddComponent<ContentHouse.Phone.PhoneController>();
            return phone;
        }

        // =====================================================================
        // Wiring
        // =====================================================================

        static void WireEverything(GameObject systems, GameObject phoneRoot, Transform playerT,
            ContentHouse.Player.PlayerController pc, Camera cam,
            ContentHouse.Player.FlashlightController flash, ContentHouse.Enemy.EnemyAI enemy,
            (ContentHouse.CCTV.CCTVCameraFeed, ContentHouse.CCTV.CCTVCameraFeed) cctv)
        {
            var gm = systems.GetComponent<ContentHouse.Core.GameManager>();
            var story = systems.GetComponent<ContentHouse.Core.StoryManager>();
            var ambience = systems.GetComponent<ContentHouse.Audio.AmbienceController>();
            var qte = systems.GetComponent<ContentHouse.UI.QTEController>();

            var uiGo = GameObject.Find("UI");
            var hudC = uiGo != null ? uiGo.GetComponent<ContentHouse.UI.HUDController>() : null;
            var epi = uiGo != null ? uiGo.GetComponent<ContentHouse.UI.EpilogueUI>() : null;
            var phone = phoneRoot.GetComponent<ContentHouse.Phone.PhoneController>();

            gm.Player = pc;
            gm.HUD = hudC;
            gm.Phone = phone;
            gm.Story = story;
            gm.Enemy = enemy;
            gm.Power = systems.GetComponent<ContentHouse.Core.PowerSystem>();
            gm.Ambience = ambience;
            gm.SetCheckpoint(playerT.position, playerT.eulerAngles.y);
            gm.PlayerLocked = true;

            phone.HallCam = cctv.Item1;
            phone.LivingCam = cctv.Item2;
            phone.Flashlight = flash;
            cctv.Item1.ScreenRenderer = laptopScreenRenderer;

            story.GM = gm;
            story.Player = pc;
            story.HUD = hudC;
            story.Phone = phone;
            story.Ambience = ambience;
            story.Power = gm.Power;
            story.Enemy = enemy;
            story.QTE = qte;
            story.Epilogue = epi;
            story.HallCam = cctv.Item1;
            story.MrIgDoor = mrIgDoor;
            story.HallCloset = hallCloset;
            story.KitchenWindow = kitchenWindow;
            story.DiningWindow = kitchenWindow;     // same left-wall glass = break-in point
            story.EscapeWindow = escapeWindow;
            story.LivingRoomWindow = frontWindow;
            story.EntryWindow = sideWindow;
            story.PatioDoor = entranceDoor;
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
    }
}
