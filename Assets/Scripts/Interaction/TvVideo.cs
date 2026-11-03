using System.IO;
using UnityEngine;
using UnityEngine.Video;

namespace ContentHouse.Interaction
{
    /// <summary>
    /// Plays a looping video on the TV model's screen, with 3D sound from the TV.
    ///
    /// SWAPPING THE VIDEO (no code needed): put any .mp4 (H.264/AAC is safest) in Assets/StreamingAssets and
    /// write its file name on the first non-# line of Assets/StreamingAssets/tv_video.txt. Full URLs
    /// (http://... or file:///...) work too. From code, for story moments: tv.SetVideo("other.mp4").
    ///
    /// The screen is the model's second sub-mesh (the green emissive one): a quad is laid just in front of it,
    /// so no UV work is needed. The TV is OFF until something calls TurnOn() (the eating scene does).
    /// </summary>
    public class TvVideo : MonoBehaviour
    {
        public const string ConfigFile = "tv_video.txt";
        public string DefaultFile = "tv_tape.mp4";   // used when tv_video.txt is missing/empty
        public int ScreenSubMesh = 1;
        public float Volume = 0.45f;

        VideoPlayer vp;
        AudioSource src;
        RenderTexture rt;
        bool wantPlay;
        float nextCheck;

        /// <summary>The video that is queued/playing (file name or URL).</summary>
        public string CurrentVideo { get; private set; }

        public static TvVideo Instance { get; private set; }

        void Awake()
        {
            Instance = this;
            var mf = GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || mf.sharedMesh.subMeshCount <= ScreenSubMesh) return;

            // screen rectangle in world space (bounds of the sub-mesh) + the direction it faces
            var mesh = mf.sharedMesh; var verts = mesh.vertices; var nrm = mesh.normals;
            var tris = mesh.GetTriangles(ScreenSubMesh);
            Vector3 mn = new Vector3(1e9f, 1e9f, 1e9f), mx = -mn, n = Vector3.zero;
            foreach (int i in tris)
            {
                var w = transform.TransformPoint(verts[i]);
                mn = Vector3.Min(mn, w); mx = Vector3.Max(mx, w);
                n += transform.TransformDirection(nrm[i]);
            }
            n.Normalize();
            var size = mx - mn;
            float h = size.y, w2 = Mathf.Max(size.x, size.z);

            // the model's screen material glows flat green: switch that off (on this instance only)
            var rend = GetComponent<Renderer>();
            var mats = rend.materials;
            if (mats.Length > ScreenSubMesh)
            {
                mats[ScreenSubMesh].SetColor("_EmissionColor", Color.black);
                mats[ScreenSubMesh].color = Color.black;
                rend.materials = mats;
            }

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "TvScreenQuad";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(transform, true);
            quad.transform.position = (mn + mx) * 0.5f + n * 0.004f;
            quad.transform.rotation = Quaternion.LookRotation(-n, Vector3.up);   // a Quad shows its face toward -forward
            quad.transform.localScale = new Vector3(w2, h, 1f) / Mathf.Max(0.0001f, transform.lossyScale.x);   // uniformly scaled TV node

            rt = new RenderTexture(640, 360, 0) { name = "TvRT" };
            var mat = new Material(Shader.Find("Unlit/Texture")) { mainTexture = rt };
            var qr = quad.GetComponent<Renderer>();
            qr.sharedMaterial = mat;
            qr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            src = gameObject.AddComponent<AudioSource>();
            src.spatialBlend = 1f; src.minDistance = 1.5f; src.maxDistance = 11f; src.volume = Volume; src.playOnAwake = false;

            vp = gameObject.AddComponent<VideoPlayer>();
            vp.playOnAwake = false;
            vp.isLooping = true;
            vp.renderMode = VideoRenderMode.RenderTexture;
            vp.targetTexture = rt;
            vp.aspectRatio = VideoAspectRatio.FitVertically;   // keeps any aspect inside the screen without stretching
            vp.audioOutputMode = VideoAudioOutputMode.AudioSource;
            vp.prepareCompleted += OnPrepared;
            vp.errorReceived += (v, msg) => Debug.LogWarning("[TV] video error: " + msg);

            SetVideo(ReadConfiguredVideo());
        }

        string ReadConfiguredVideo()
        {
            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, ConfigFile);
                if (File.Exists(path))
                    foreach (var raw in File.ReadAllLines(path))
                    {
                        var line = raw.Trim();
                        if (line.Length > 0 && !line.StartsWith("#")) return line;
                    }
            }
            catch (System.Exception e) { Debug.LogWarning("[TV] couldn't read " + ConfigFile + ": " + e.Message); }
            return DefaultFile;
        }

        /// <summary>Switch the TV to another video: a file name in StreamingAssets, or a full URL.</summary>
        public void SetVideo(string fileOrUrl)
        {
            if (vp == null) return;
            if (string.IsNullOrWhiteSpace(fileOrUrl)) fileOrUrl = DefaultFile;
            CurrentVideo = fileOrUrl.Trim();
            bool isUrl = CurrentVideo.Contains("://");
            vp.Stop();
            vp.source = VideoSource.Url;
            vp.url = isUrl ? CurrentVideo : Path.Combine(Application.streamingAssetsPath, CurrentVideo);
            vp.EnableAudioTrack(0, true);
            vp.SetTargetAudioSource(0, src);
            vp.Prepare();
            Debug.Log("[TV] video: " + vp.url);
        }

        /// <summary>Is the picture on?</summary>
        public bool IsOn => wantPlay;

        /// <summary>Switch the TV on (starts the video). Safe to call repeatedly.</summary>
        public void TurnOn()
        {
            wantPlay = true;
            if (vp != null && vp.isPrepared && !vp.isPlaying) vp.Play();
        }

        /// <summary>Switch the TV off (black screen, silent). The next TurnOn() resumes where it left off.</summary>
        public void TurnOff()
        {
            wantPlay = false;
            if (vp != null && vp.isPlaying) vp.Pause();
        }

        void OnPrepared(VideoPlayer v)
        {
            vp.EnableAudioTrack(0, true);
            vp.SetTargetAudioSource(0, src);
            if (wantPlay) v.Play();
        }

        void Update()
        {
            if (vp == null) return;
            // the TV stays off until something turns it on (TurnOn(): the eating scene does)
            // start, and keep it going (retry if it stalled/stopped)
            if (wantPlay && Time.unscaledTime >= nextCheck)
            {
                nextCheck = Time.unscaledTime + 1f;
                if (vp.isPrepared && !vp.isPlaying) vp.Play();
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (rt != null) rt.Release();
        }
    }
}
