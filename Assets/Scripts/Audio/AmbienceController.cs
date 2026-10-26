using System.Collections;
using UnityEngine;

namespace ContentHouse.Audio
{
    /// <summary>
    /// Storm ambience: rain loop + randomized thunder with lightning flashes,
    /// plus proximity heartbeat while the intruder is hunting the player.
    /// </summary>
    public class AmbienceController : MonoBehaviour
    {
        public Light LightningLight;     // brief directional flash
        public float LightningFlashSeconds = 0.18f;
        [Range(0f, 1f)] public float StormIntensity = 0.5f;

        AudioSource rain, thunder, heartbeat;
        float nextThunderIn = 10f;
        float flashTimer;
        const float BaseIntensity = 0.35f;

        void Start()
        {
            rain = gameObject.AddComponent<AudioSource>();
            rain.clip = ProceduralAudio.GetRainLoop();
            rain.loop = true;
            rain.volume = 0.35f * StormIntensity;
            rain.spatialBlend = 0f;
            rain.Play();

            thunder = gameObject.AddComponent<AudioSource>();
            thunder.spatialBlend = 0f;

            heartbeat = gameObject.AddComponent<AudioSource>();
            heartbeat.clip = ProceduralAudio.GetHeartbeatLoop();
            heartbeat.loop = true;
            heartbeat.spatialBlend = 0f;
            heartbeat.volume = 0f;
        }

        void Update()
        {
            // Random storm thunder.
            nextThunderIn -= Time.deltaTime;
            if (nextThunderIn <= 0f)
            {
                nextThunderIn = Random.Range(9f, 22f) * (2f - StormIntensity);
                StartCoroutine(ThunderRoutine(0.45f + 0.4f * StormIntensity, flash: true));
            }

            if (flashTimer > 0f)
            {
                flashTimer -= Time.deltaTime;
                if (LightningLight != null)
                    LightningLight.intensity = Mathf.Max(BaseIntensity,
                        (flashTimer / LightningFlashSeconds) * 5.5f);
                if (flashTimer <= 0f && LightningLight != null) LightningLight.intensity = BaseIntensity;
            }

            // Heartbeat scales with intruder proximity.
            var gm = Core.GameManager.Instance;
            float hbTarget = 0f;
            if (gm != null && gm.Enemy != null && gm.Enemy.Active && gm.Player != null)
            {
                float d = Vector3.Distance(gm.Enemy.transform.position, gm.Player.transform.position);
                hbTarget = d < 9f ? Mathf.Clamp01((9f - d) / 8f) * 0.8f : 0f;
            }
            heartbeat.volume = Mathf.Lerp(heartbeat.volume, hbTarget, 2.5f * Time.deltaTime);
        }

        /// <summary>Scripted big thunderclap (Act II blackout).</summary>
        public void ThunderClap()
        {
            StartCoroutine(ThunderRoutine(1.2f, flash: true));
        }

        AudioSource sirens;
        public void StartSirens()
        {
            if (sirens == null)
            {
                sirens = gameObject.AddComponent<AudioSource>();
                sirens.clip = ProceduralAudio.GetSirenLoop();
                sirens.loop = true;
                sirens.spatialBlend = 0f;
            }
            if (!sirens.isPlaying) sirens.Play();
            StartCoroutine(SirenFadeIn());
        }

        IEnumerator SirenFadeIn()
        {
            for (float t = 0; t < 1f; t += Time.deltaTime / 4f)
            {
                if (sirens != null) sirens.volume = t * 0.4f;
                yield return null;
            }
            if (sirens != null) sirens.volume = 0.4f;
        }

        IEnumerator ThunderRoutine(float intensity, bool flash)
        {
            if (flash && LightningLight != null)
            {
                flashTimer = LightningFlashSeconds;
                LightningLight.intensity = 5.5f;
            }
            yield return new WaitForSeconds(0.5f);
            thunder.PlayOneShot(ProceduralAudio.GetThunder(), Mathf.Clamp01(intensity));
        }
    }
}
