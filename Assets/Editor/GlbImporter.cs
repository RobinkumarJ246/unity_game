using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ContentHouseEditor
{
    /// <summary>
    /// Minimal GLB -> Unity asset converter for the Tripo-generated props (one mesh, embedded textures).
    /// Reads the binary glTF, flips it to Unity's left-handed space, simplifies the (very dense) mesh by
    /// vertex clustering and writes Mesh + Textures + Material into Assets/Generated.
    /// Menu: Content House > Import Generated Props
    /// </summary>
    public static class GlbImporter
    {
        const string OutDir = "Assets/Resources/Generated";

        [Serializable] class GAttr { public int POSITION = -1, NORMAL = -1, TEXCOORD_0 = -1, JOINTS_0 = -1, WEIGHTS_0 = -1; }
        [Serializable] class GNode { public string name; public int[] children; public float[] translation; public float[] rotation; public float[] scale; public int mesh = -1; public int skin = -1; }
        [Serializable] class GSkin { public int[] joints; public int inverseBindMatrices = -1; public int skeleton = -1; }
        [Serializable] class GPrim { public GAttr attributes; public int indices = -1; public int material = -1; }
        [Serializable] class GMesh { public GPrim[] primitives; }
        [Serializable] class GAcc { public int bufferView; public int byteOffset; public int componentType; public int count; public string type; public bool normalized; }
        [Serializable] class GBV { public int byteOffset; public int byteLength; public int byteStride; }
        [Serializable] class GImg { public int bufferView; public string mimeType; }
        [Serializable] class GTex { public int source; }
        [Serializable] class GTexRef { public int index = -1; }
        [Serializable] class GPbr { public GTexRef baseColorTexture; public GTexRef metallicRoughnessTexture; public float[] baseColorFactor; }
        [Serializable] class GMat { public string name; public GPbr pbrMetallicRoughness; public GTexRef normalTexture; public GTexRef emissiveTexture; }
        [Serializable] class GRoot { public GAcc[] accessors; public GBV[] bufferViews; public GMesh[] meshes; public GMat[] materials; public GImg[] images; public GTex[] textures; public GNode[] nodes; public GSkin[] skins; }

        [MenuItem("Content House/Import Generated Props")]
        public static void ImportAll()
        {
            const string src = @"C:\Unity_Games\assets_downloaded\generated_assets\";
            Import(src + "juice_can.glb", "juice_can", 110);
            Import(src + "pizza.glb", "pizza", 150);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        }

        /// <summary>Returns a short report. gridRes = clustering grid cells along the longest axis.</summary>
        public static string Import(string glbPath, string name, int gridRes)
        {
            Directory.CreateDirectory(OutDir);
            var bytes = File.ReadAllBytes(glbPath);
            if (BitConverter.ToUInt32(bytes, 0) != 0x46546C67) return "not a GLB";
            int jsonLen = (int)BitConverter.ToUInt32(bytes, 12);
            string json = System.Text.Encoding.UTF8.GetString(bytes, 20, jsonLen);
            int binStart = 20 + jsonLen + 8;                       // skip BIN chunk header
            var g = JsonUtility.FromJson<GRoot>(json);
            var prim = g.meshes[0].primitives[0];

            Vector3[] pos = ReadV3(g, bytes, binStart, prim.attributes.POSITION);
            Vector3[] nor = prim.attributes.NORMAL >= 0 ? ReadV3(g, bytes, binStart, prim.attributes.NORMAL) : null;
            Vector2[] uv = prim.attributes.TEXCOORD_0 >= 0 ? ReadV2(g, bytes, binStart, prim.attributes.TEXCOORD_0) : null;
            int[] idx = ReadIdx(g, bytes, binStart, prim.indices);

            // glTF (right-handed, +Z forward) -> Unity (left-handed): mirror X, flip winding, flip V
            for (int i = 0; i < pos.Length; i++) pos[i].x = -pos[i].x;
            if (nor != null) for (int i = 0; i < nor.Length; i++) nor[i].x = -nor[i].x;
            if (uv != null) for (int i = 0; i < uv.Length; i++) uv[i].y = 1f - uv[i].y;
            for (int i = 0; i < idx.Length; i += 3) { int t = idx[i]; idx[i] = idx[i + 2]; idx[i + 2] = t; }

            var mesh = Cluster(pos, nor, uv, idx, gridRes);
            mesh.name = name;
            AssetDatabase.DeleteAsset(OutDir + "/" + name + "_mesh.asset");
            AssetDatabase.DeleteAsset(OutDir + "/" + name + ".prefab");
            AssetDatabase.DeleteAsset(OutDir + "/" + name + "_mat.mat");
            AssetDatabase.CreateAsset(mesh, OutDir + "/" + name + "_mesh.asset");

            // textures
            var tex = new Texture2D[g.images.Length];
            for (int i = 0; i < g.images.Length; i++)
            {
                var bv = g.bufferViews[g.images[i].bufferView];
                string ext = g.images[i].mimeType == "image/png" ? ".png" : ".jpg";
                string path = OutDir + "/" + name + "_tex" + i + ext;
                var data = new byte[bv.byteLength]; Buffer.BlockCopy(bytes, binStart + bv.byteOffset, data, 0, bv.byteLength);
                File.WriteAllBytes(path, data);
            }
            AssetDatabase.Refresh();
            var gm = g.materials[prim.material < 0 ? 0 : prim.material];
            string report = "";
            Texture2D Load(GTexRef r, bool normal)
            {
                if (r == null || r.index < 0) return null;
                int img = g.textures[r.index].source;
                string ext = g.images[img].mimeType == "image/png" ? ".png" : ".jpg";
                string path = OutDir + "/" + name + "_tex" + img + ext;
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                if (ti != null) { ti.maxTextureSize = 1024; ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default; ti.SaveAndReimport(); }
                report += (normal ? "normal=" : "base=") + path + " ";
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            var baseTex = Load(gm.pbrMetallicRoughness != null ? gm.pbrMetallicRoughness.baseColorTexture : null, false);
            var normTex = Load(gm.normalTexture, true);

            var mat = new Material(Shader.Find("Standard"));
            mat.name = name + "_mat";
            if (baseTex != null) mat.mainTexture = baseTex;
            if (normTex != null) { mat.SetTexture("_BumpMap", normTex); mat.EnableKeyword("_NORMALMAP"); }
            mat.SetFloat("_Metallic", 0.05f); mat.SetFloat("_Glossiness", 0.35f);
            AssetDatabase.CreateAsset(mat, OutDir + "/" + name + "_mat.mat");

            // prefab (mesh + material), centred on its bounds so it holds nicely
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            PrefabUtility.SaveAsPrefabAsset(go, OutDir + "/" + name + ".prefab");
            UnityEngine.Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            return name + ": verts " + pos.Length + " -> " + mesh.vertexCount + ", tris " + idx.Length / 3 + " -> " + mesh.triangles.Length / 3
                   + ", bounds " + mesh.bounds.size.ToString("F3") + " center " + mesh.bounds.center.ToString("F3") + " | " + report + " | mat: " + gm.name;
        }

        // ---------- rigged character (skinned mesh + bone hierarchy) ----------

        static float[] ReadComp(GRoot g, byte[] b, int binStart, int accIdx)
        {
            var a = g.accessors[accIdx]; var bv = g.bufferViews[a.bufferView];
            int c = Comps(a.type);
            int size = a.componentType == 5126 ? 4 : (a.componentType == 5123 || a.componentType == 5122) ? 2 : 1;
            int stride = bv.byteStride > 0 ? bv.byteStride : c * size;
            var res = new float[a.count * c];
            int o = binStart + bv.byteOffset + a.byteOffset;
            for (int i = 0; i < a.count; i++)
                for (int k = 0; k < c; k++)
                {
                    int p = o + i * stride + k * size;
                    float v = a.componentType == 5126 ? BitConverter.ToSingle(b, p)
                            : a.componentType == 5123 ? BitConverter.ToUInt16(b, p)
                            : a.componentType == 5121 ? b[p] : BitConverter.ToInt16(b, p);
                    if (a.normalized) v /= a.componentType == 5123 ? 65535f : a.componentType == 5121 ? 255f : 32767f;
                    res[i * c + k] = v;
                }
            return res;
        }

        static Matrix4x4 MirrorX(Matrix4x4 m)
        {
            float[] s = { -1, 1, 1, 1 };
            var r = Matrix4x4.zero;
            for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) r[i, j] = m[i, j] * s[i] * s[j];
            return r;
        }

        /// <summary>Imports a rigged GLB into Assets/Resources/Generated/{name}.prefab (SkinnedMeshRenderer + Bip01-style
        /// bones, bind pose, textures, materials), scaled to targetHeight metres. Returns a report incl. the bone names.</summary>
        public static string ImportSkinned(string glbPath, string name, float targetHeight)
        {
            Directory.CreateDirectory(OutDir);
            var bytes = File.ReadAllBytes(glbPath);
            int jsonLen = (int)BitConverter.ToUInt32(bytes, 12);
            var g = JsonUtility.FromJson<GRoot>(System.Text.Encoding.UTF8.GetString(bytes, 20, jsonLen));
            int binStart = 20 + jsonLen + 8;

            int meshNodeIdx = -1;
            for (int i = 0; i < g.nodes.Length; i++) if (g.nodes[i].mesh >= 0 && g.nodes[i].skin >= 0) { meshNodeIdx = i; break; }
            if (meshNodeIdx < 0) return "no skinned mesh node";
            var meshNode = g.nodes[meshNodeIdx]; var skin = g.skins[meshNode.skin]; var gmesh = g.meshes[meshNode.mesh];

            // ---- geometry: all primitives become sub-meshes of one mesh ----
            var P = new List<Vector3>(); var N = new List<Vector3>(); var U = new List<Vector2>(); var BW = new List<BoneWeight>();
            var subTris = new List<int[]>(); var subMat = new List<int>();
            foreach (var prim in gmesh.primitives)
            {
                int off = P.Count;
                var pos = ReadV3(g, bytes, binStart, prim.attributes.POSITION);
                var nor = prim.attributes.NORMAL >= 0 ? ReadV3(g, bytes, binStart, prim.attributes.NORMAL) : null;
                var uv = prim.attributes.TEXCOORD_0 >= 0 ? ReadV2(g, bytes, binStart, prim.attributes.TEXCOORD_0) : null;
                var jn = ReadComp(g, bytes, binStart, prim.attributes.JOINTS_0);
                var wt = ReadComp(g, bytes, binStart, prim.attributes.WEIGHTS_0);
                for (int i = 0; i < pos.Length; i++)
                {
                    P.Add(new Vector3(-pos[i].x, pos[i].y, pos[i].z));
                    N.Add(nor != null ? new Vector3(-nor[i].x, nor[i].y, nor[i].z) : Vector3.up);
                    U.Add(uv != null ? new Vector2(uv[i].x, 1f - uv[i].y) : Vector2.zero);
                    var bw = new BoneWeight
                    {
                        boneIndex0 = (int)jn[i * 4], boneIndex1 = (int)jn[i * 4 + 1], boneIndex2 = (int)jn[i * 4 + 2], boneIndex3 = (int)jn[i * 4 + 3],
                        weight0 = wt[i * 4], weight1 = wt[i * 4 + 1], weight2 = wt[i * 4 + 2], weight3 = wt[i * 4 + 3]
                    };
                    BW.Add(bw);
                }
                var idx = ReadIdx(g, bytes, binStart, prim.indices);
                for (int i = 0; i < idx.Length; i += 3) { int t = idx[i]; idx[i] = idx[i + 2]; idx[i + 2] = t; }   // flip winding
                for (int i = 0; i < idx.Length; i++) idx[i] += off;
                subTris.Add(idx); subMat.Add(prim.material);
            }

            // ---- bones: mirror the node hierarchy into Unity ----
            var root = new GameObject(name);
            var gos = new GameObject[g.nodes.Length];
            for (int i = 0; i < g.nodes.Length; i++)
            {
                var n = g.nodes[i]; gos[i] = new GameObject(string.IsNullOrEmpty(n.name) ? "node" + i : n.name);
                var t = n.translation != null && n.translation.Length == 3 ? new Vector3(-n.translation[0], n.translation[1], n.translation[2]) : Vector3.zero;
                var q = n.rotation != null && n.rotation.Length == 4 ? new Quaternion(n.rotation[0], -n.rotation[1], -n.rotation[2], n.rotation[3]) : Quaternion.identity;
                var s = n.scale != null && n.scale.Length == 3 ? new Vector3(n.scale[0], n.scale[1], n.scale[2]) : Vector3.one;
                gos[i].transform.localPosition = t; gos[i].transform.localRotation = q; gos[i].transform.localScale = s;
            }
            var hasParent = new bool[g.nodes.Length];
            for (int i = 0; i < g.nodes.Length; i++)
                if (g.nodes[i].children != null) foreach (int c in g.nodes[i].children) { gos[c].transform.SetParent(gos[i].transform, false); hasParent[c] = true; }
            for (int i = 0; i < g.nodes.Length; i++) if (!hasParent[i] && i != meshNodeIdx) gos[i].transform.SetParent(root.transform, false);
            // the mesh's own node is not part of the skeleton (glTF ignores its transform for skinned meshes)
            UnityEngine.Object.DestroyImmediate(gos[meshNodeIdx]);

            var bones = new Transform[skin.joints.Length];
            for (int i = 0; i < bones.Length; i++) bones[i] = gos[skin.joints[i]].transform;
            var ibm = ReadFloats(g, bytes, binStart, skin.inverseBindMatrices);
            var bind = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                var m = Matrix4x4.identity;
                for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) m[r, c] = ibm[i * 16 + c * 4 + r];   // glTF is column-major
                bind[i] = MirrorX(m);
            }

            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(P); mesh.SetNormals(N); mesh.SetUVs(0, U);
            mesh.boneWeights = BW.ToArray(); mesh.bindposes = bind;
            mesh.subMeshCount = subTris.Count;
            for (int i = 0; i < subTris.Count; i++) mesh.SetTriangles(subTris[i], i);
            mesh.RecalculateBounds();
            AssetDatabase.DeleteAsset(OutDir + "/" + name + "_mesh.asset");
            AssetDatabase.CreateAsset(mesh, OutDir + "/" + name + "_mesh.asset");

            // ---- textures + materials ----
            for (int i = 0; i < g.images.Length; i++)
            {
                var bv = g.bufferViews[g.images[i].bufferView];
                string ext = g.images[i].mimeType == "image/png" ? ".png" : ".jpg";
                var data = new byte[bv.byteLength]; Buffer.BlockCopy(bytes, binStart + bv.byteOffset, data, 0, bv.byteLength);
                File.WriteAllBytes(OutDir + "/" + name + "_tex" + i + ext, data);
            }
            AssetDatabase.Refresh();
            var mats = new Material[subTris.Count];
            for (int i = 0; i < mats.Length; i++)
            {
                var gm = g.materials[subMat[i] < 0 ? 0 : subMat[i]];
                var mat = new Material(Shader.Find("Standard")) { name = name + "_" + (gm.name ?? "mat" + i) };
                var tr = gm.pbrMetallicRoughness != null ? gm.pbrMetallicRoughness.baseColorTexture : null;
                if (tr != null && tr.index >= 0)
                {
                    int img = g.textures[tr.index].source;
                    string ext = g.images[img].mimeType == "image/png" ? ".png" : ".jpg";
                    mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(OutDir + "/" + name + "_tex" + img + ext);
                }
                mat.SetFloat("_Metallic", 0f); mat.SetFloat("_Glossiness", 0.2f);
                string mp = OutDir + "/" + name + "_mat" + i + ".mat";
                AssetDatabase.DeleteAsset(mp); AssetDatabase.CreateAsset(mat, mp);
                mats[i] = mat;
            }

            // ---- the renderer ----
            var body = new GameObject("Body"); body.transform.SetParent(root.transform, false);
            var smr = body.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh; smr.bones = bones; smr.sharedMaterials = mats;
            smr.rootBone = skin.skeleton >= 0 ? gos[skin.skeleton].transform : bones[0];
            smr.updateWhenOffscreen = true;

            float height = mesh.bounds.size.y;
            float scale = height > 0.0001f ? targetHeight / height : 1f;
            root.transform.localScale = Vector3.one * scale;

            string pp = OutDir + "/" + name + ".prefab";
            AssetDatabase.DeleteAsset(pp);
            PrefabUtility.SaveAsPrefabAsset(root, pp);
            var names = new List<string>(); foreach (var bt in bones) names.Add(bt.name);
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return name + ": verts " + P.Count + ", bones " + bones.Length + ", sub-meshes " + subTris.Count + ", raw height " + height.ToString("F3")
                   + " -> scale " + scale.ToString("F3") + "\nBONES: " + string.Join(" | ", names);
        }

        // ---------- pizza: level the disc and cut it into slices ----------

        /// <summary>
        /// Takes Assets/Generated/pizza_mesh.asset, finds the flat plane of the disc (PCA: the axis with the
        /// least spread), rotates it so that axis is +Y (flipTop=true to put the other face up), centres it and
        /// cuts it into N wedges. Writes pizza_slice0..N-1 meshes + pizza_slices.prefab (children Slice0..).
        /// </summary>
        public static string BuildPizzaSlices(int n, bool flipTop)
        {
            var src = AssetDatabase.LoadAssetAtPath<Mesh>(OutDir + "/pizza_mesh.asset");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(OutDir + "/pizza_mat.mat");
            var v = src.vertices; var nm = src.normals; var uv = src.uv; var tr = src.triangles;

            Vector3 c = Vector3.zero; foreach (var p in v) c += p; c /= v.Length;
            double[,] cov = new double[3, 3];
            foreach (var p in v) { var d = p - c; double[] a = { d.x, d.y, d.z }; for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) cov[i, j] += a[i] * a[j]; }
            // Jacobi eigen-decomposition of the symmetric 3x3
            double[,] vec = { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            for (int it = 0; it < 60; it++)
            {
                int pI = 0, qI = 1; double big = Math.Abs(cov[0, 1]);
                if (Math.Abs(cov[0, 2]) > big) { pI = 0; qI = 2; big = Math.Abs(cov[0, 2]); }
                if (Math.Abs(cov[1, 2]) > big) { pI = 1; qI = 2; big = Math.Abs(cov[1, 2]); }
                if (big < 1e-12) break;
                double th = 0.5 * Math.Atan2(2 * cov[pI, qI], cov[qI, qI] - cov[pI, pI]);
                double cs = Math.Cos(th), sn = Math.Sin(th);
                for (int k = 0; k < 3; k++) { double a = cov[k, pI], b = cov[k, qI]; cov[k, pI] = cs * a - sn * b; cov[k, qI] = sn * a + cs * b; }
                for (int k = 0; k < 3; k++) { double a = cov[pI, k], b = cov[qI, k]; cov[pI, k] = cs * a - sn * b; cov[qI, k] = sn * a + cs * b; }
                for (int k = 0; k < 3; k++) { double a = vec[k, pI], b = vec[k, qI]; vec[k, pI] = cs * a - sn * b; vec[k, qI] = sn * a + cs * b; }
            }
            int small = 0; for (int i = 1; i < 3; i++) if (cov[i, i] < cov[small, small]) small = i;
            var normal = new Vector3((float)vec[0, small], (float)vec[1, small], (float)vec[2, small]).normalized;
            if (flipTop) normal = -normal;
            var rot = Quaternion.FromToRotation(normal, Vector3.up);

            var rv = new Vector3[v.Length]; var rn = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) { rv[i] = rot * (v[i] - c); rn[i] = rot * nm[i]; }

            // slice by angle around the disc axis
            // the model has a bit of white paper plate attached: drop triangles whose texture colour is white/grey
            var colTex = new Texture2D(2, 2);
            colTex.LoadImage(File.ReadAllBytes(OutDir + "/pizza_tex0.jpg"));
            int dropped = 0;
            var perSlice = new List<int>[n];
            for (int s = 0; s < n; s++) perSlice[s] = new List<int>();
            for (int i = 0; i < tr.Length; i += 3)
            {
                var uvc = (uv[tr[i]] + uv[tr[i + 1]] + uv[tr[i + 2]]) / 3f;
                float h, sat, val; Color.RGBToHSV(colTex.GetPixelBilinear(uvc.x, uvc.y), out h, out sat, out val);
                if (sat < 0.16f && val > 0.5f) { dropped++; continue; }
                var m3 = (rv[tr[i]] + rv[tr[i + 1]] + rv[tr[i + 2]]) / 3f;
                float ang = Mathf.Atan2(m3.z, m3.x);                                   // -pi..pi
                int s = Mathf.Clamp(Mathf.FloorToInt((ang + Mathf.PI) / (2f * Mathf.PI) * n), 0, n - 1);
                perSlice[s].Add(tr[i]); perSlice[s].Add(tr[i + 1]); perSlice[s].Add(tr[i + 2]);
            }

            // fresh assets every time (overwriting an asset in place orphans prefab references to it)
            for (int s = 0; s < 16; s++) AssetDatabase.DeleteAsset(OutDir + "/pizza_slice" + s + ".asset");
            AssetDatabase.DeleteAsset(OutDir + "/pizza_slices.prefab");
            var root = new GameObject("pizza_slices");
            string report = "normal=" + normal.ToString("F2") + " plate tris dropped=" + dropped + " of " + tr.Length / 3;
            for (int s = 0; s < n; s++)
            {
                var map = new Dictionary<int, int>(); var pv = new List<Vector3>(); var pn = new List<Vector3>(); var pu = new List<Vector2>(); var pt = new List<int>();
                foreach (int old in perSlice[s])
                {
                    int id; if (!map.TryGetValue(old, out id)) { id = pv.Count; map[old] = id; pv.Add(rv[old]); pn.Add(rn[old]); pu.Add(uv[old]); }
                    pt.Add(id);
                }
                var m = new Mesh { name = "pizza_slice" + s, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.SetVertices(pv); m.SetNormals(pn); m.SetUVs(0, pu); m.SetTriangles(pt, 0); m.RecalculateBounds();
                AssetDatabase.CreateAsset(m, OutDir + "/pizza_slice" + s + ".asset");
                var child = new GameObject("Slice" + s); child.transform.SetParent(root.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = m; child.AddComponent<MeshRenderer>().sharedMaterial = mat;
                report += " | s" + s + ": " + pv.Count + "v";
            }
            PrefabUtility.SaveAsPrefabAsset(root, OutDir + "/pizza_slices.prefab");
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return report;
        }

        // ---------- accessors ----------

        static int Comps(string t) => t == "VEC3" ? 3 : t == "VEC2" ? 2 : t == "VEC4" ? 4 : t == "MAT4" ? 16 : 1;

        static float[] ReadFloats(GRoot g, byte[] b, int binStart, int accIdx)
        {
            var a = g.accessors[accIdx]; var bv = g.bufferViews[a.bufferView];
            int c = Comps(a.type), stride = bv.byteStride > 0 ? bv.byteStride : c * 4;
            var res = new float[a.count * c];
            int o = binStart + bv.byteOffset + a.byteOffset;
            for (int i = 0; i < a.count; i++)
                for (int k = 0; k < c; k++) res[i * c + k] = BitConverter.ToSingle(b, o + i * stride + k * 4);
            return res;
        }

        static Vector3[] ReadV3(GRoot g, byte[] b, int s, int acc)
        {
            var f = ReadFloats(g, b, s, acc); var r = new Vector3[f.Length / 3];
            for (int i = 0; i < r.Length; i++) r[i] = new Vector3(f[i * 3], f[i * 3 + 1], f[i * 3 + 2]);
            return r;
        }

        static Vector2[] ReadV2(GRoot g, byte[] b, int s, int acc)
        {
            var f = ReadFloats(g, b, s, acc); var r = new Vector2[f.Length / 2];
            for (int i = 0; i < r.Length; i++) r[i] = new Vector2(f[i * 2], f[i * 2 + 1]);
            return r;
        }

        static int[] ReadIdx(GRoot g, byte[] b, int binStart, int accIdx)
        {
            var a = g.accessors[accIdx]; var bv = g.bufferViews[a.bufferView];
            int size = a.componentType == 5125 ? 4 : a.componentType == 5123 ? 2 : 1;
            int o = binStart + bv.byteOffset + a.byteOffset;
            var r = new int[a.count];
            for (int i = 0; i < a.count; i++)
                r[i] = size == 4 ? (int)BitConverter.ToUInt32(b, o + i * 4) : size == 2 ? BitConverter.ToUInt16(b, o + i * 2) : b[o + i];
            return r;
        }

        // ---------- simplification: vertex clustering (position cell + uv cell keeps texture islands apart) ----------

        static Mesh Cluster(Vector3[] pos, Vector3[] nor, Vector2[] uv, int[] idx, int grid)
        {
            Vector3 mn = pos[0], mx = pos[0];
            foreach (var p in pos) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
            float cell = Mathf.Max(mx.x - mn.x, Mathf.Max(mx.y - mn.y, mx.z - mn.z)) / grid;
            int uvGrid = 48;

            var map = new Dictionary<long, int>();
            var remap = new int[pos.Length];
            var sp = new List<Vector3>(); var sn = new List<Vector3>(); var su = new List<Vector2>(); var cnt = new List<int>();
            for (int i = 0; i < pos.Length; i++)
            {
                long cx = (long)((pos[i].x - mn.x) / cell), cy = (long)((pos[i].y - mn.y) / cell), cz = (long)((pos[i].z - mn.z) / cell);
                long ux = uv != null ? (long)Mathf.Clamp(uv[i].x * uvGrid, 0, uvGrid) : 0, uy = uv != null ? (long)Mathf.Clamp(uv[i].y * uvGrid, 0, uvGrid) : 0;
                long key = ((((cx * 1031 + cy) * 1031 + cz) * 61 + ux) * 61) + uy;
                int id;
                if (!map.TryGetValue(key, out id))
                {
                    id = sp.Count; map[key] = id;
                    sp.Add(Vector3.zero); sn.Add(Vector3.zero); su.Add(Vector2.zero); cnt.Add(0);
                }
                remap[i] = id;
                sp[id] += pos[i]; if (nor != null) sn[id] += nor[i]; if (uv != null) su[id] += uv[i]; cnt[id]++;
            }
            for (int i = 0; i < sp.Count; i++) { sp[i] /= cnt[i]; su[i] /= cnt[i]; sn[i] = sn[i].sqrMagnitude > 1e-8f ? sn[i].normalized : Vector3.up; }

            var tris = new List<int>();
            for (int i = 0; i < idx.Length; i += 3)
            {
                int a = remap[idx[i]], b = remap[idx[i + 1]], c = remap[idx[i + 2]];
                if (a == b || b == c || a == c) continue;
                tris.Add(a); tris.Add(b); tris.Add(c);
            }
            var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(sp); m.SetNormals(sn); if (uv != null) m.SetUVs(0, su);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
