using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ContentHouse.UI
{
    /// <summary>
    /// Fears-to-Fathom-style full-screen narration (spec request): solid
    /// background, typewriter cards with soft ticks and fades, any-key to
    /// advance, Escape aborts the whole section. Fully data-driven from
    /// Assets/Resources/narration.txt - edit that file to change or add
    /// narration any time ([section] + blank-line-separated cards).
    /// Reusable anywhere: NarrationScreen.Instance.Play("after_sleep").
    /// </summary>
    public class NarrationScreen : MonoBehaviour
    {
        public static NarrationScreen Instance { get; private set; }

        public bool IsPlaying { get; private set; }

        // ---- pacing (tune here if needed) ----
        const float CharDelay = 0.03f;
        const float CardFadeOut = 0.3f;
        const float BgFadeIn = 0.5f;
        const float BgFadeOut = 1.0f;
        const int FontSize = 46;
        const float AdvanceCooldown = 0.25f;

        RectTransform root;
        Image bg;
        Text cardText, hint;
        Font font;
        AudioSource ticks;
        AudioClip tickClip;
        readonly Dictionary<string, List<string>> sections = new Dictionary<string, List<string>>();
        bool abort;
        bool advanceRequested;
        float lastConsume = -10f;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            font = Resources.Load<Font>("Fonts/SpecialElite-Regular");
            if (font == null) font = UIFactory.Font;
            tickClip = Audio.ProceduralAudio.GetNarrationTick();
            ticks = gameObject.AddComponent<AudioSource>();
            ticks.spatialBlend = 0f;
            ticks.playOnAwake = false;
            BuildUI();
            LoadData();
        }

        void BuildUI()
        {
            root = UIFactory.CanvasRoot("NarrationCanvas", 250);

            bg = UIFactory.Image("Bg", root, new Color(0.055f, 0.045f, 0.05f, 0f));
            UIFactory.Stretch(bg.rectTransform);

            cardText = UIFactory.Text("Card", root, "", FontSize, TextAnchor.MiddleCenter,
                new Color(0.84f, 0.82f, 0.76f));
            cardText.font = font;
            cardText.rectTransform.anchorMin = cardText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            cardText.rectTransform.sizeDelta = new Vector2(1300, 520);
            cardText.horizontalOverflow = HorizontalWrapMode.Wrap;
            cardText.verticalOverflow = VerticalWrapMode.Overflow;

            hint = UIFactory.Text("Hint", root, "any key", 20, TextAnchor.MiddleCenter,
                new Color(1, 1, 1, 0.35f));
            hint.font = font;
            hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            hint.rectTransform.anchoredPosition = new Vector2(0, 46);
            hint.rectTransform.sizeDelta = new Vector2(300, 24);
            hint.gameObject.SetActive(false);

            var shadow = cardText.gameObject.AddComponent<UnityEngine.UI.Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);

            root.gameObject.SetActive(false);
        }

        void LoadData()
        {
            var asset = Resources.Load<TextAsset>("narration");
            if (asset == null)
            {
                Debug.LogWarning("[Narration] Assets/Resources/narration.txt missing - narration disabled.");
                return;
            }
            string section = null;
            List<string> cards = null;
            List<string> current = null;

            void FlushCard()
            {
                if (current == null || current.Count == 0) return;
                if (cards != null) cards.Add(string.Join(" ", current));
                current = null;
            }

            foreach (var raw in asset.text.Split('\n'))
            {
                var line = raw.TrimEnd('\r', ' ', '\t');
                var t = line.Trim();
                if (t.StartsWith("#")) continue;                          // comment
                if (t.Length == 0) { FlushCard(); continue; }            // card separator
                if (t.StartsWith("[") && t.EndsWith("]"))
                {
                    FlushCard();
                    section = t.Substring(1, t.Length - 2).Trim();
                    cards = new List<string>();
                    sections[section] = cards;
                    continue;
                }
                if (section == null) continue;
                if (current == null) current = new List<string>();
                current.Add(t);
            }
            FlushCard();
            Debug.Log("[Narration] loaded " + sections.Count + " section(s): "
                + string.Join(", ", sections.Keys));
        }

        /// <summary>Replace {player} {streamer} {friend} {intruder} tokens.</summary>
        static string Tokens(string s) => s
            .Replace("{player}", Core.GameConfig.PlayerName)
            .Replace("{streamer}", Core.GameConfig.StreamerName)
            .Replace("{friend}", Core.GameConfig.FriendName)
            .Replace("{intruder}", Core.GameConfig.IntruderName);

        // =====================================================================
        // Public API
        // =====================================================================

        /// <summary>Play a narration section; coroutine so story code can chain it.</summary>
        public IEnumerator Play(string sectionId)
        {
            if (IsPlaying) yield break;
            if (!sections.ContainsKey(sectionId))
            {
                Debug.LogWarning("[Narration] no section '" + sectionId + "' in narration.txt");
                yield break;
            }

            var gm = Core.GameManager.Instance;
            bool prevLock = gm != null && gm.PlayerLocked;
            if (gm != null) gm.PlayerLocked = true;
            if (gm != null && gm.HUD != null) gm.HUD.SetCenterPrompt("");

            IsPlaying = true;
            abort = false;
            advanceRequested = false;
            lastConsume = -10f;
            root.gameObject.SetActive(true);

            // Reset visuals: a previous section's outro fades left the text
            // and hint at alpha 0 - without this, replayed narrations type
            // invisibly (ticks but no text) until the second card.
            var baseCol = cardText.color; baseCol.a = 1f; cardText.color = baseCol;
            cardText.text = "";
            var hintCol = hint.color; hintCol.a = 0.35f; hint.color = hintCol;

            // Background fades in WHILE the first card starts typing - no dead wait.
            StartCoroutine(FadeImage(bg, 0f, 1f, BgFadeIn));

            var cards = sections[sectionId];
            for (int i = 0; i < cards.Count; i++)
            {
                if (abort) break;
                string text = Tokens(cards[i]);
                hint.gameObject.SetActive(true);   // every card: the player controls pacing

                // typewriter (any key completes the card instantly)
                int shown = 0;
                while (shown < text.Length)
                {
                    if (abort) break;
                    if (ConsumeAdvance()) { shown = text.Length; }
                    else
                    {
                        shown++;
                        cardText.text = text.Substring(0, shown);   // render as it types
                        if (shown % 4 == 0 && tickClip != null) ticks.PlayOneShot(tickClip, 0.05f);
                        yield return new WaitForSeconds(CharDelay);
                    }
                }
                cardText.text = text;

                if (abort) break;

                // The card stays on screen until the player presses a key or
                // clicks - no auto-advance, read at your own pace.
                while (!abort)
                {
                    if (ConsumeAdvance()) break;
                    yield return null;
                }

                if (abort) break;
                if (i < cards.Count - 1)
                {
                    yield return FadeText(cardText, 1f, 0f, CardFadeOut);
                    cardText.text = "";
                    yield return FadeText(cardText, 0f, 1f, 0.1f);
                }
            }

            yield return FadeText(cardText, cardText.color.a, 0f, 0.25f);
            cardText.text = "";
            hint.gameObject.SetActive(false);
            yield return FadeImage(bg, bg.color.a, 0f, BgFadeOut);
            root.gameObject.SetActive(false);

            IsPlaying = false;
            if (gm != null) gm.PlayerLocked = prevLock;
        }

        /// <summary>Test/script hook: counts as the player pressing a key.</summary>
        public void RequestAdvance() => advanceRequested = true;

        /// <summary>Test/script hook: abort the whole section.</summary>
        public void RequestAbort() => abort = true;

        // =====================================================================
        // internal
        // =====================================================================

        void Update()
        {
            if (!IsPlaying) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { abort = true; return; }
            // Captured EVERY frame so single-frame key presses are never missed.
            if (Input.anyKeyDown ||
                Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
                advanceRequested = true;
        }

        bool ConsumeAdvance()
        {
            if (!advanceRequested) return false;
            if (Time.time - lastConsume < AdvanceCooldown) return false;
            lastConsume = Time.time;
            advanceRequested = false;
            return true;
        }

        IEnumerator FadeImage(Image img, float from, float to, float seconds)
        {
            for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, seconds))
            {
                var c = img.color; c.a = Mathf.Lerp(from, to, t); img.color = c;
                yield return null;
            }
            var c2 = img.color; c2.a = to; img.color = c2;
        }

        IEnumerator FadeText(Text txt, float from, float to, float seconds)
        {
            var baseColor = txt.color;
            for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, seconds))
            {
                var c = baseColor; c.a = Mathf.Lerp(from, to, t); txt.color = c;
                yield return null;
            }
            var c3 = baseColor; c3.a = to; txt.color = c3;
        }
    }
}
