using UnityEngine;

namespace ContentHouse.CCTV
{
    /// <summary>
    /// A security/GoPro camera that renders to a RenderTexture. The phone CCTV
    /// app and Mr. IG's laptop screen both show this feed. When the intruder
    /// smashes the camera (Act IV), the feed turns to static.
    /// </summary>
    public class CCTVCameraFeed : MonoBehaviour
    {
        public Camera Cam;
        public string FeedName = "HALL CAM 02";
        [Tooltip("Optional mesh renderer (Mr. IG laptop screen) showing this feed.")]
        public MeshRenderer ScreenRenderer;
        public bool Smashed { get; private set; }

        public RenderTexture RT { get; private set; }
        public Texture2D StaticTex { get; private set; }

        void Start()
        {
            RT = new RenderTexture(512, 320, 16, RenderTextureFormat.ARGB32);
            RT.Create();
            if (Cam != null)
            {
                Cam.targetTexture = RT;
                Cam.enabled = false;   // only render when someone views the feed
            }
            StaticTex = MakeStaticTexture();

            if (ScreenRenderer != null)
            {
                // Runtime unlit material showing the live feed on the laptop.
                var mat = new Material(Shader.Find("Unlit/Texture"));
                mat.mainTexture = RT;
                ScreenRenderer.material = mat;
            }
        }

        /// <summary>Feed is being viewed - start rendering.</summary>
        public void SetViewing(bool viewing)
        {
            if (Cam != null) Cam.enabled = viewing && !Smashed;
        }

        public void Smash()
        {
            Smashed = true;
            if (Cam != null) Cam.enabled = false;
            Audio.ProceduralAudio.PlayBang(transform.position, 1f);
            Audio.ProceduralAudio.PlayGlassShatter(transform.position);
        }

        /// <summary>UI helper: the texture to show (feed or static).</summary>
        public Texture CurrentTexture => Smashed ? (Texture)StaticTex : (Texture)RT;

        static Texture2D MakeStaticTexture()
        {
            var tex = new Texture2D(128, 80, TextureFormat.RGB24, false);
            var px = new Color[128 * 80];
            var rng = new System.Random(99);
            for (int i = 0; i < px.Length; i++)
            {
                float v = (float)rng.NextDouble() * 0.5f + 0.25f;
                px[i] = new Color(v, v * 1.05f, v * 0.95f);
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }
}
