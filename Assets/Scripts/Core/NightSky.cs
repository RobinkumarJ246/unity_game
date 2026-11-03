using UnityEngine;

namespace ContentHouse.Core
{
    /// <summary>
    /// Stars + a moon on a big dome that follows the player camera (so it never gets closer). The dark gradient
    /// itself comes from the night skybox material (Assets/Materials/NightSky.mat). Builds everything in code.
    /// </summary>
    public class NightSky : MonoBehaviour
    {
        public int StarCount = 700;
        public float Radius = 400f;
        [Tooltip("Moon direction: compass azimuth (0 = +Z) and height above the horizon, degrees.")]
        public float MoonAzimuth = 12f, MoonElevation = 11f;
        public float MoonSize = 15f;

        Transform follow;

        void Start()
        {
            var starMat = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.HideAndDontSave };   // not hidden by fog
            BuildStars(starMat);
            BuildMoon(starMat);
        }

        static Vector3 Dir(float azimuthDeg, float elevationDeg)
        {
            float a = azimuthDeg * Mathf.Deg2Rad, e = elevationDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(a) * Mathf.Cos(e));
        }

        void BuildStars(Material mat)
        {
            var rng = new System.Random(42);
            var verts = new Vector3[StarCount * 4]; var cols = new Color[StarCount * 4]; var tris = new int[StarCount * 6];
            for (int i = 0; i < StarCount; i++)
            {
                // upper hemisphere only, denser toward the zenith-ish; a few reach down near the horizon
                float az = (float)rng.NextDouble() * 360f, el = Mathf.Lerp(4f, 90f, Mathf.Pow((float)rng.NextDouble(), 0.8f));
                var c = Dir(az, el) * Radius;
                var up = Vector3.Cross(c.normalized, Vector3.right).sqrMagnitude < 0.01f ? Vector3.forward : Vector3.right;
                var r = Vector3.Cross(c.normalized, up).normalized; var u = Vector3.Cross(r, c.normalized).normalized;
                float size = Mathf.Lerp(0.5f, 1.5f, Mathf.Pow((float)rng.NextDouble(), 3f));
                float b = Mathf.Lerp(0.35f, 1f, size / 1.5f);
                var tint = Color.Lerp(new Color(0.8f, 0.88f, 1f), new Color(1f, 0.94f, 0.82f), (float)rng.NextDouble());
                tint *= b; tint.a = 1f;
                int v = i * 4;
                verts[v] = c - r * size - u * size; verts[v + 1] = c + r * size - u * size; verts[v + 2] = c + r * size + u * size; verts[v + 3] = c - r * size + u * size;
                for (int k = 0; k < 4; k++) cols[v + k] = tint;
                int t = i * 6; tris[t] = v; tris[t + 1] = v + 2; tris[t + 2] = v + 1; tris[t + 3] = v; tris[t + 4] = v + 3; tris[t + 5] = v + 2;
            }
            var m = new Mesh { name = "Stars", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.vertices = verts; m.colors = cols; m.triangles = tris; m.bounds = new Bounds(Vector3.zero, Vector3.one * Radius * 3f);
            var go = new GameObject("Stars"); go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        }

        void BuildMoon(Material mat)
        {
            // a soft disc: a sprite texture generated here (bright core, gentle glow)
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(n / 2f, n / 2f)) / (n / 2f);   // 0 centre .. 1 edge
                float disc = Mathf.Clamp01((0.42f - d) / 0.03f);                                           // crisp disc, radius 0.42
                float glow = Mathf.Pow(Mathf.Clamp01(1f - d), 2.5f) * 0.35f;
                float a = Mathf.Clamp01(disc + glow);
                // faint maria on the disc
                float shade = 1f - 0.14f * Mathf.PerlinNoise(x * 0.09f, y * 0.09f) * disc;
                px[y * n + x] = new Color(0.92f * shade, 0.94f * shade, 1f * shade, a);
            }
            tex.SetPixels32(px); tex.Apply();
            var moonMat = new Material(mat.shader) { mainTexture = tex, hideFlags = HideFlags.HideAndDontSave };
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); q.name = "Moon";
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(transform, false);
            var dir = Dir(MoonAzimuth, MoonElevation);
            q.transform.localPosition = dir * (Radius * 0.95f);
            q.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);        // faces the viewer at the centre
            q.transform.localScale = Vector3.one * MoonSize * 2f;
            var r = q.GetComponent<Renderer>(); r.sharedMaterial = moonMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void LateUpdate()
        {
            if (follow == null)
            {
                var gm = GameManager.Instance;
                if (gm != null && gm.Player != null && gm.Player.Camera != null) follow = gm.Player.Camera.transform;
                else if (Camera.main != null) follow = Camera.main.transform;
            }
            if (follow != null) transform.position = follow.position;      // rotation stays fixed to the world
        }
    }
}
