using UnityEngine;

namespace ContentHouse.Interaction
{
    /// <summary>
    /// Bathroom break (days.txt task id "pee"): hold [E] at ANY toilet until the ring fills.
    /// While holding: the player is held in place, the view eases down into the bowl, a stream is
    /// drawn into it and a stream sound plays. Releasing early cancels (the ring resets).
    /// Available after the drink has been delivered. All sound is synthesised (no audio assets).
    /// </summary>
    public class PeeInteractable : HoldInteractable
    {
        public string UnlockAfterTask = "deliver";
        public float MaxDistance = 1.7f;   // flat distance from the bowl, so the stream reads right

        AudioSource stream;
        LineRenderer line;
        Vector3 bowlPoint;
        bool active;
        static AudioClip streamClip, flushClip;
        static Material streamMat;

        bool Unlocked
        {
            get
            {
                var gm = Core.GameManager.Instance;
                return gm != null && (string.IsNullOrEmpty(UnlockAfterTask) || gm.IsTaskDone(UnlockAfterTask));
            }
        }

        float FlatDistance()
        {
            var gm = Core.GameManager.Instance;
            if (gm == null || gm.Player == null) return 0f;
            var b = GetComponent<Collider>().bounds;
            var a = gm.Player.transform.position; var c = b.center;
            return Vector2.Distance(new Vector2(a.x, a.z), new Vector2(c.x, c.z));
        }

        public override string Prompt
        {
            get
            {
                var gm = Core.GameManager.Instance;
                if (gm == null || gm.IsTaskDone(TaskId)) return "";
                if (!Unlocked) return "The toilet (nothing urgent... yet)";
                if (active) return "";   // the ring says it all
                if (FlatDistance() > MaxDistance) return "Step closer to the toilet";
                return "Hold  [E]  to pee";
            }
        }

        // Real recording (Assets/Resources/Sfx/peeing.wav, trimmed from freesound_community-peeing-88718.mp3).
        // The hold lasts as long as the clip, so the sound ends as the ring fills and the flush follows.
        static AudioClip realClip;
        static bool realClipLoaded;

        void Awake()
        {
            if (!realClipLoaded) { realClip = Resources.Load<AudioClip>("Sfx/peeing"); realClipLoaded = true; }
            HoldSeconds = realClip != null ? realClip.length : 4.5f;
            HoldLabel = "Peeing...  keep holding [E]";
        }

        protected override bool CanStartHold(Player.PlayerInteraction player)
        {
            var gm = Core.GameManager.Instance;
            return Unlocked && gm != null && !gm.IsTaskDone(TaskId) && FlatDistance() <= MaxDistance;
        }

        protected override void OnHoldStart()
        {
            EnsureClips();
            var gm = Core.GameManager.Instance;
            var b = GetComponent<Collider>().bounds;
            bowlPoint = new Vector3(b.center.x, b.min.y + 0.42f, b.center.z);

            if (stream == null)
            {
                stream = gameObject.AddComponent<AudioSource>();
                stream.spatialBlend = 1f; stream.minDistance = 1.5f; stream.maxDistance = 12f;
                stream.playOnAwake = false;
                // real recording plays once through; the synthesised fallback loops
                stream.loop = realClip == null;
                stream.volume = realClip != null ? 0.9f : 0.5f;
                stream.clip = realClip != null ? realClip : streamClip;
            }
            stream.time = 0f;
            stream.Play();

            if (line == null)
            {
                var go = new GameObject("PeeStream");
                go.transform.SetParent(transform, false);
                line = go.AddComponent<LineRenderer>();
                line.material = StreamMaterial();
                line.positionCount = 14;
                line.numCapVertices = 2;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.widthMultiplier = 1f;
                line.widthCurve = new AnimationCurve(new Keyframe(0, 0.035f), new Keyframe(1, 0.02f));
            }
            line.enabled = true;

            // hold the player at the bowl and ease the view down into it
            active = true;
            if (gm != null && gm.Player != null)
            {
                gm.HUD?.SetCenterPrompt("");   // the interaction ray is off while locked; don't leave the old prompt up
                gm.PlayerLocked = true;
                gm.Player.StartCoroutine(gm.Player.FocusOn(bowlPoint, 0.6f));
            }
        }

        void Release()
        {
            if (!active) return;
            active = false;
            if (stream != null && stream.isPlaying) stream.Stop();
            if (line != null) line.enabled = false;
            var gm = Core.GameManager.Instance;
            if (gm != null)
            {
                gm.PlayerLocked = false;
                if (gm.Player != null) gm.Player.LookLocked = false;
            }
        }

        protected override void OnHoldCancel()
        {
            Release();
            Core.GameManager.Instance?.HUD?.QueueSubtitle("", "(zips up) ...not done yet.", 1.8f);
        }

        void LateUpdate()
        {
            if (!active || line == null) return;
            var gm = Core.GameManager.Instance;
            if (gm == null || gm.Player == null) return;
            var p = gm.Player.transform;
            // from hip height, a little ahead of the body, arcing into the bowl
            Vector3 from = p.position + Vector3.up * 0.92f + p.forward * 0.28f;
            Vector3 to = bowlPoint + new Vector3(Mathf.Sin(Time.time * 17f), 0f, Mathf.Cos(Time.time * 13f)) * 0.012f;
            for (int i = 0; i < line.positionCount; i++)
            {
                float t = i / (line.positionCount - 1f);
                Vector3 pt = Vector3.Lerp(from, to, t);
                pt.y += 0.16f * Mathf.Sin(t * Mathf.PI) * (1f - t * 0.2f);   // arc
                pt += new Vector3(Mathf.Sin(Time.time * 29f + i), 0f, Mathf.Cos(Time.time * 23f + i)) * 0.004f * t;
                line.SetPosition(i, pt);
            }
        }

        protected override void OnHoldComplete()
        {
            Release();
            used = true;
            EnsureClips();
            AudioSource.PlayClipAtPoint(flushClip, transform.position, 0.7f);
            var gm = Core.GameManager.Instance;
            gm?.HUD?.QueueSubtitle("", "(flush) Ahh. Cold drinks, warm bladder. It's an old story.", 3.5f);
            NotifyTask();
            gm?.CompleteTask(TaskId);
        }

        // ---------- procedural visuals / audio ----------

        static Material StreamMaterial()
        {
            if (streamMat != null) return streamMat;
            var sh = Shader.Find("Sprites/Default");
            streamMat = new Material(sh) { color = new Color(1f, 0.88f, 0.25f, 0.85f), hideFlags = HideFlags.HideAndDontSave };
            return streamMat;
        }

        static void EnsureClips()
        {
            if (streamClip != null && flushClip != null) return;
            const int rate = 22050;
            var rng = new System.Random(7);

            // stream: band-limited noise with a gentle wobble, 1.5 s loop (start/end crossfaded)
            int n = (int)(rate * 1.5f);
            var s = new float[n];
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float white = (float)(rng.NextDouble() * 2 - 1);
                lp += (white - lp) * 0.35f;
                lp2 += (lp - lp2) * 0.35f;
                float wob = 0.75f + 0.25f * Mathf.Sin(i / (float)rate * 2f * Mathf.PI * 6f);
                s[i] = (lp - lp2 * 0.3f) * wob * 1.6f;
            }
            int fade = (int)(rate * 0.05f);
            for (int i = 0; i < fade; i++)
                s[i] = Mathf.Lerp(s[n - fade + i], s[i], i / (float)fade);
            var trimmed = new float[n - fade]; System.Array.Copy(s, trimmed, n - fade);
            streamClip = AudioClip.Create("pee_stream", trimmed.Length, 1, rate, false);
            streamClip.SetData(trimmed, 0);

            // flush: short - rushing noise swells in 0.2 s, then drains away (~1.8 s total)
            int m = (int)(rate * 1.8f);
            var f = new float[m];
            lp = 0f; lp2 = 0f;
            for (int i = 0; i < m; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Clamp01(t / 0.2f) * Mathf.Exp(-Mathf.Max(0f, t - 0.45f) * 2.4f);
                float white = (float)(rng.NextDouble() * 2 - 1);
                float cutoff = Mathf.Lerp(0.5f, 0.14f, Mathf.Clamp01(t / 1.6f));
                lp += (white - lp) * cutoff;
                lp2 += (lp - lp2) * cutoff;
                f[i] = lp2 * env * 2.2f;
            }
            float peak = 0.0001f;
            for (int i = 0; i < m; i++) peak = Mathf.Max(peak, Mathf.Abs(f[i]));
            float norm = 0.9f / peak;   // keep under full scale - raw noise peaked at ~1.5 and would clip
            for (int i = 0; i < m; i++) f[i] *= norm;
            flushClip = AudioClip.Create("toilet_flush", m, 1, rate, false);
            flushClip.SetData(f, 0);
        }
    }
}
