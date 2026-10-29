using System;
using System.Collections.Generic;
using UnityEngine;

namespace ContentHouse.Audio
{
    /// <summary>
    /// Hybrid sound design: loads real CC0 clips (Kenney Impact Sounds,
    /// RPG Audio, Interface Sounds - see Assets/Resources/Sfx) at runtime via
    /// Resources, and falls back to procedural synthesis only for sounds we
    /// have no free asset for (rain, thunder, siren, heartbeat).
    /// All clips CC0 - licenses in the Sfx folders.
    /// </summary>
    public static class ProceduralAudio
    {
        const int Rate = 44100;
        static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
        static readonly System.Random rng = new System.Random(1234);

        // ================= asset loading (CC0 clips) =================

        /// <summary>Load a random variant, e.g. Res("Sfx/impact/impactGlass_heavy", 5) tries _000.._004.</summary>
        static AudioClip Res(string basePathNoExt, int variants)
        {
            for (int guard = 0; guard < variants; guard++)
            {
                int i = rng.Next(variants);
                string path = basePathNoExt + "_" + i.ToString("000");
                AudioClip c = Resources.Load<AudioClip>(path);
                if (c != null) return c;
            }
            // Also try the base name without a variant suffix.
            return Resources.Load<AudioClip>(basePathNoExt);
        }

        /// <summary>Asset clip or null (no synth fallback).</summary>
        static AudioClip OnlyRes(string basePathNoExt, int variants)
        {
            return Res(basePathNoExt, variants);
        }

        /// <summary>Direct load by exact path (no variants).</summary>
        static AudioClip Load(string path)
        {
            return Resources.Load<AudioClip>(path);
        }

        // ================= synthesis core (fallback) =================

        static AudioClip Build(string name, float seconds, Func<int, int, float> sample)
        {
            int len = Mathf.Max(1, (int)(seconds * Rate));
            var clip = AudioClip.Create(name, len, 1, Rate, false);
            var data = new float[len];
            for (int i = 0; i < len; i++) data[i] = Mathf.Clamp(sample(i, len), -1f, 1f);
            clip.SetData(data, 0);
            return clip;
        }

        static float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);

        static float Brown(int i, int len, float decay, float start)
        {
            float t = i / (float)len;
            float env = start * Mathf.Exp(-decay * t);
            float rum = Mathf.PerlinNoise(i * 0.00035f, 0.5f) * 2f - 1f;
            float grit = Noise() * 0.25f;
            return (rum * 0.8f + grit) * env;
        }

        static AudioClip Get(string name, Func<AudioClip> factory)
        {
            AudioClip c;
            if (!cache.TryGetValue(name, out c)) { c = factory(); cache[name] = c; }
            return c;
        }

        static void Play(AudioClip clip, Vector3 pos, float volume, float pitchJitter = 0.06f)
        {
            if (clip == null) return;
            var go = new GameObject("SFX_" + clip.name);
            go.transform.position = pos;
            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = 1f;
            src.minDistance = 2f;
            src.maxDistance = 40f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.pitch = 1f + (float)(rng.NextDouble() * pitchJitter * 2 - pitchJitter);
            src.clip = clip;
            src.volume = volume;
            src.Play();
            UnityEngine.Object.Destroy(go, clip.length + 0.5f);
        }

        // ================= one-shots =================

        public static void PlayFootstep(Vector3 pos, bool crouch)
        {
            var c = OnlyRes("Sfx/impact/footstep_wood", 5)
                 ?? Get("footstep", () => Build("footstep", 0.12f, (i, len) =>
                     Noise() * Mathf.Exp(-40f * i / (float)len) * 0.7f +
                     Mathf.Sin(2f * Mathf.PI * 90f * i / (float)Rate) * Mathf.Exp(-60f * i / (float)len) * 0.3f));
            Play(c, pos, crouch ? 0.12f : 0.3f);
        }

        public static void PlayDoorCreak(Vector3 pos)
        {
            var c = OnlyRes("Sfx/rpgaudio/creak1", 1) ?? OnlyRes("Sfx/rpgaudio/creak2", 1)
                 ?? Get("doorcreak", () => Build("doorcreak", 0.8f, (i, len) =>
                 {
                     float t = i / (float)Rate;
                     float f = 420f + Mathf.Sin(t * 9f) * 180f;
                     return (Mathf.Sin(2f * Mathf.PI * f * t) * 0.4f + Noise() * 0.12f)
                            * Mathf.Sin(Mathf.PI * i / (float)len) * 0.35f;
                 }));
            Play(c, pos, 0.7f);
        }

        public static void PlayDoorCreakLong(Vector3 pos)
        {
            var c = OnlyRes("Sfx/rpgaudio/creak2", 1) ?? OnlyRes("Sfx/rpgaudio/creak3", 1)
                 ?? Get("doorcreaklong", () => Build("doorcreaklong", 2.4f, (i, len) =>
                 {
                     float t = i / (float)Rate;
                     float f = 300f + Mathf.Sin(t * 4f) * 150f;
                     return (Mathf.Sin(2f * Mathf.PI * f * t) * 0.35f + Noise() * 0.1f)
                            * Mathf.Sin(Mathf.PI * i / (float)len) * 0.4f;
                 }));
            Play(c, pos, 0.85f, 0.25f);   // slowed down = long eerie creak
        }

        public static void PlayDoorSlam(Vector3 pos)
        {
            var c = Load("Sfx/kaino/Haunted Door Slam 3 - Kaino")
                 ?? OnlyRes("Sfx/rpgaudio/doorClose", 4)
                 ?? Get("doorslam", () => Build("doorslam", 0.5f, (i, len) =>
                     Noise() * Mathf.Exp(-18f * i / (float)len) +
                     Mathf.Sin(2f * Mathf.PI * 70f * i / (float)Rate) * Mathf.Exp(-14f * i / (float)len) * 0.9f));
            Play(c, pos, 0.95f);
        }

        public static void PlayDoorRattle(Vector3 pos)
        {
            var c = Load("Sfx/horror/error_02_locked_door_rattle")
                 ?? OnlyRes("Sfx/rpgaudio/metalLatch", 1)
                 ?? Get("doorrattle", () => Build("doorrattle", 0.35f, (i, len) =>
                     Noise() * Mathf.Exp(-6f * i / (float)len) * 0.3f * (i % 3000 < 1500 ? 1f : 0.4f)));
            Play(c, pos, 0.4f);
        }

        public static void PlayGlassShatter(Vector3 pos)
        {
            var c = OnlyRes("Sfx/impact/impactGlass_heavy", 5)
                 ?? OnlyRes("Sfx/impact/impactGlass_medium", 5)
                 ?? Get("glass", () => Build("glass", 1.1f, (i, len) =>
                 {
                     float t = i / (float)Rate;
                     float env = Mathf.Exp(-3.5f * t);
                     float chime = 0f;
                     int k = i % 1200;
                     if (k < 30) chime = Mathf.Sin(2f * Mathf.PI * (2400f + (i % 7) * 900f) * t) * 0.4f;
                     return (Noise() * 0.65f + chime) * env;
                 }));
            Play(c, pos, 1f);
        }

        public static void PlayBang(Vector3 pos, float intensity)
        {
            var c = OnlyRes("Sfx/impact/impactWood_heavy", 5)
                 ?? Get("bang", () => Build("bang", 0.6f, (i, len) =>
                     Noise() * Mathf.Exp(-9f * i / (float)len) * 0.8f +
                     Mathf.Sin(2f * Mathf.PI * 55f * i / (float)Rate) * Mathf.Exp(-7f * i / (float)len)));
            Play(c, pos, Mathf.Clamp01(intensity));
        }

        public static AudioClip GetThunder()
        {
            return Get("thunder", () => Build("thunder", 3.2f, (i, len) =>
            {
                float t = i / (float)Rate;
                float attack = Mathf.Clamp01(t * 6f);
                float decay = Mathf.Exp(-1.1f * t);
                float crack = t < 0.12f ? Noise() * 0.8f : 0f;
                return (Brown(i, len, 0.9f, 1f) * 0.7f + crack) * attack * decay;
            }));
        }

        public static void PlayThunder(Vector3 pos, float intensity)
        {
            Play(GetThunder(), pos, Mathf.Clamp01(intensity));
        }

        public static void PlayLockClick(Vector3 pos)
        {
            var c = Load("Sfx/horror/click_05_keyhole_click")
                 ?? OnlyRes("Sfx/rpgaudio/metalClick", 1)
                 ?? Get("lock", () => Build("lock", 0.1f, (i, len) =>
                     Noise() * Mathf.Exp(-70f * i / (float)len) * 0.5f +
                     Mathf.Sin(2f * Mathf.PI * 1800f * i / (float)Rate) * Mathf.Exp(-90f * i / (float)len) * 0.3f));
            Play(c, pos, 0.5f);
        }

        public static void PlayClick(Vector3 pos)
        {
            var c = OnlyRes("Sfx/interface/click", 5)
                 ?? Get("click", () => Build("click", 0.07f, (i, len) =>
                     Noise() * Mathf.Exp(-90f * i / (float)len) * 0.5f));
            Play(c, pos, 0.4f, 0.02f);
        }

        public static void PlayLeverClunk(Vector3 pos)
        {
            var c = OnlyRes("Sfx/impact/impactMetal_light", 5)
                 ?? Get("clunk", () => Build("clunk", 0.25f, (i, len) =>
                     Noise() * Mathf.Exp(-25f * i / (float)len) * 0.6f +
                     Mathf.Sin(2f * Mathf.PI * 140f * i / (float)Rate) * Mathf.Exp(-18f * i / (float)len) * 0.6f));
            Play(c, pos, 0.6f);
        }

        public static void PlayFridgeDoor(Vector3 pos)
        {
            var c = Load("Sfx/kaino/Fridge Door 2 - Kaino")
                 ?? OnlyRes("Sfx/rpgaudio/doorClose", 4)
                 ?? Get("fridge", () => Build("fridge", 0.5f, (i, len) =>
                     Mathf.Sin(2f * Mathf.PI * 60f * i / (float)Rate) * Mathf.Exp(-10f * i / (float)len) * 0.5f +
                     Noise() * Mathf.Exp(-20f * i / (float)len) * 0.3f));
            Play(c, pos, 0.55f);
        }

        public static void PlayKnock(Vector3 pos, int count)
        {
            var c = Load("Sfx/kaino/Doorbell 2 - Kaino")
                 ?? OnlyRes("Sfx/impact/impactWood_medium", 5)
                 ?? Get("knock", () => Build("knock", 0.22f, (i, len) =>
                     Mathf.Sin(2f * Mathf.PI * 95f * i / (float)Rate) * Mathf.Exp(-16f * i / (float)len) +
                     Noise() * Mathf.Exp(-30f * i / (float)len) * 0.4f));
            Play(c, pos, 0.75f);
        }

        public static void PlayBuzzError(Vector3 pos)
        {
            var c = Load("Sfx/horror/error_01_distorted_buzz")
                 ?? OnlyRes("Sfx/interface/error", 8)
                 ?? Get("buzzerr", () => Build("buzzerr", 0.2f, (i, len) =>
                     Mathf.Sin(2f * Mathf.PI * 110f * i / (float)Rate) * 0.4f * Mathf.Sin(Mathf.PI * i / (float)len)));
            Play(c, pos, 0.5f, 0.02f);
        }

        public static void PlayPhoneBuzz(Vector3 pos)
        {
            var c = Load("Sfx/kaino/Notification Chime 1 - Kaino")
                 ?? OnlyRes("Sfx/interface/bong", 1)
                 ?? Get("phonebuzz", () => Build("phonebuzz", 0.5f, (i, len) =>
                 {
                     float t = i / (float)Rate;
                     float pulse = Mathf.Sin(2f * Mathf.PI * 8f * t) > 0 ? 1f : 0f;
                     return Mathf.Sin(2f * Mathf.PI * 170f * t) * 0.5f * pulse;
                 }));
            Play(c, pos, 0.6f, 0f);
        }

        public static void PlayCrowbarHit(Vector3 pos)
        {
            var c = OnlyRes("Sfx/impact/impactMetal_heavy", 5)
                 ?? Get("crowbar", () => Build("crowbar", 0.9f, (i, len) =>
                 {
                     float t = i / (float)Rate;
                     float metal = Mathf.Sin(2f * Mathf.PI * 820f * t) * Mathf.Exp(-6f * t) * 0.5f
                                 + Mathf.Sin(2f * Mathf.PI * 1310f * t) * Mathf.Exp(-9f * t) * 0.25f;
                     float crunch = Noise() * Mathf.Exp(-20f * t) * 0.9f;
                     return metal + crunch;
                 }));
            Play(c, pos, 1f);
        }

        /// <summary>The hunting knife reveal on the camera feed.</summary>
        public static void PlayKnifeDraw(Vector3 pos)
        {
            var c = Load("Sfx/horror/pickup_05_cursed_item")
                 ?? OnlyRes("Sfx/rpgaudio/drawKnife", 3)
                 ?? OnlyRes("Sfx/rpgaudio/knifeSlice", 1);
            Play(c, pos, 0.9f, 0.1f);
        }

        /// <summary>CCTV feed glitch / signal loss.</summary>
        public static void PlayGlitch(Vector3 pos)
        {
            var c = Load("Sfx/horror/notify_02_radio_static_burst")
                 ?? OnlyRes("Sfx/interface/glitch", 4);
            Play(c, pos, 0.6f, 0.15f);
        }

        /// <summary>Task complete chime.</summary>
        public static void PlayConfirmation(Vector3 pos)
        {
            var c = Load("Sfx/horror/confirm_02_low_dread_hit")
                 ?? OnlyRes("Sfx/interface/confirmation", 4);
            Play(c, pos, 0.35f, 0.02f);
        }

        public static void PlayPhoneOpen(Vector3 pos)
        {
            var c = Load("Sfx/horror/menu_07_tab_whoosh_dark")
                 ?? OnlyRes("Sfx/interface/maximize", 9);
            Play(c, pos, 0.35f, 0.02f);
        }

        public static void PlayPhoneClose(Vector3 pos)
        {
            var c = Load("Sfx/horror/menu_07_tab_whoosh_dark")
                 ?? OnlyRes("Sfx/interface/minimize", 9);
            Play(c, pos, 0.3f, 0.1f);   // slightly slowed = closing feel
        }

        // ---- new: narration / death / flashlight / tension music ----

        /// <summary>Narration typewriter tick (Creepy Horror UI Sounds).</summary>
        public static AudioClip GetNarrationTick()
        {
            return Load("Sfx/horror/click_02_typewriter_key");
        }

        /// <summary>Death screen stinger when the intruder catches you.</summary>
        public static void PlayGameOverStinger(Vector3 pos)
        {
            var c = Load("Sfx/horror/game_over_stinger");
            Play(c, pos, 0.9f, 0f);
        }

        /// <summary>Phone flashlight on/off switch (Rivgo Flashlight System).</summary>
        public static void PlayFlashlightSwitch(Vector3 pos)
        {
            var c = Load("Sfx/kit/Flashlight_Switch_02");
            Play(c, pos, 0.5f, 0.03f);
        }

        /// <summary>Stealth/chase tension music loop (FreeHorrorGameKit).</summary>
        public static AudioClip GetTensionMusic()
        {
            return Load("Sfx/kit/Dark Tension Rising Music");
        }

        public static void PlayWindowSmash(Vector3 pos) => PlayGlassShatter(pos);

        // ================= loops (procedural - no free assets) =================

        public static AudioClip GetRainLoop()
        {
            // Real recording first (Kaino - rain on a window), synth fallback.
            var real = Load("Sfx/kaino/Rain on a Window 2 - Kaino");
            if (real != null) return real;
            return Get("rain", () => Build("rain", 3f, (i, len) =>
            {
                float t = i / (float)Rate;
                float soft = (Mathf.PerlinNoise(i * 0.0006f, 1.7f) * 2f - 1f) * 0.5f;
                return (Noise() * 0.22f + soft * 0.5f) * (0.85f + 0.15f * Mathf.Sin(t * 2.7f));
            }));
        }

        public static AudioClip GetStaticLoop()
        {
            return Get("static", () => Build("static", 1.2f, (i, len) => Noise() * 0.35f));
        }

        public static AudioClip GetHeartbeatLoop()
        {
            return Get("heartbeat", () => Build("heartbeat", 1.15f, (i, len) =>
            {
                float t = i / (float)Rate;
                float b1 = Mathf.Exp(-28f * t) * (t % 1.15f < 0.5f ? 1f : 0f);
                float t2 = (t + 1.15f - 0.24f) % 1.15f;
                float b2 = t2 > 0f && t2 < 0.5f ? Mathf.Exp(-34f * t2) * 0.7f : 0f;
                return Mathf.Sin(2f * Mathf.PI * 52f * t) * 0.85f * (b1 + b2);
            }));
        }

        public static AudioClip GetSirenLoop()
        {
            return Get("siren", () => Build("siren", 2.4f, (i, len) =>
            {
                float t = i / (float)Rate;
                float sweep = 700f + 280f * Mathf.Sin(2f * Mathf.PI * 0.45f * t);
                return Mathf.Sin(2f * Mathf.PI * sweep * t) * 0.35f;
            }));
        }
    }
}
