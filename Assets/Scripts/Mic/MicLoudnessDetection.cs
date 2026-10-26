using UnityEngine;

namespace ContentHouse.Mic
{
    /// <summary>
    /// Samples the real-world microphone and computes RMS loudness (spec:
    /// MicLoudnessDetection.cs, threshold 0.15). While the player hides, loud
    /// noise is reported to the NoiseSystem so the enemy can react.
    /// Fallback (no mic / toggle off): holding SPACE raises the meter as if
    /// breathing loudly - simply do not press it while hiding.
    /// </summary>
    public class MicLoudnessDetection : MonoBehaviour
    {
        public static MicLoudnessDetection Instance { get; private set; }

        const int SampleWindow = 1024;

        public bool MicrophoneAvailable { get; private set; }
        /// <summary>Smoothed RMS level 0..1.</summary>
        public float Level { get; private set; }

        AudioClip micClip;
        string device;
        float[] samples = new float[SampleWindow];
        float rawLevel;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void Start()
        {
            if (!Core.GameConfig.UseMic) { MicrophoneAvailable = false; return; }
            try
            {
                if (Microphone.devices == null || Microphone.devices.Length == 0) { MicrophoneAvailable = false; return; }
                device = Microphone.devices[0];
                micClip = Microphone.Start(device, true, 1, 44100);
                MicrophoneAvailable = micClip != null;
            }
            catch { MicrophoneAvailable = false; }
        }

        void OnDestroy()
        {
            if (MicrophoneAvailable && device != null) Microphone.End(device);
        }

        void Update()
        {
            if (Core.GameManager.Instance == null) return;
            var gm = Core.GameManager.Instance;
            var player = gm.Player;
            if (player == null || !player.IsHiding)
            {
                Level = Mathf.Lerp(Level, 0f, 6f * Time.deltaTime);
                return;
            }

            if (MicrophoneAvailable)
            {
                rawLevel = ReadRMS();
            }
            else
            {
                // Fallback minigame: holding SPACE = loud breathing.
                rawLevel = Input.GetKey(KeyCode.Space) ? 0.28f : 0.015f;
            }

            Level = Mathf.Lerp(Level, rawLevel, 12f * Time.deltaTime);

            // Noise zones (only while hiding) -> enemy can hear the player.
            if (Level >= Core.GameConfig.MicRedLevel)
                Core.NoiseSystem.ReportNoise(player.transform.position, Core.GameConfig.RedNoiseRadius);
            else if (Level >= Core.GameConfig.MicYellowLevel)
                Core.NoiseSystem.ReportNoise(player.transform.position, Core.GameConfig.YellowNoiseRadius);
        }

        float ReadRMS()
        {
            if (micClip == null) return 0f;
            int pos = Microphone.GetPosition(device);
            int start = pos - SampleWindow;
            if (start < 0) start = 0;
            if (micClip.channels * SampleWindow > samples.Length)
                samples = new float[micClip.channels * SampleWindow];
            int count = Mathf.Min(SampleWindow, micClip.samples - start) * micClip.channels;
            if (count <= 0) return 0f;
            try { micClip.GetData(samples, start); } catch { return 0f; }

            float sum = 0f;
            for (int i = 0; i < count; i++) sum += samples[i] * samples[i];
            return Mathf.Sqrt(sum / count) * 3.2f; // scale up so speech reliably passes 0.15
        }

        /// <summary>Current zone for UI: 0 = safe/green, 1 = warning/yellow, 2 = exposed/red.</summary>
        public int Zone
        {
            get
            {
                if (Level >= Core.GameConfig.MicRedLevel) return 2;
                if (Level >= Core.GameConfig.MicYellowLevel) return 1;
                return 0;
            }
        }
    }
}
