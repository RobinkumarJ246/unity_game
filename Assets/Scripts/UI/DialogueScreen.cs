using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ContentHouse.UI
{
    /// <summary>
    /// Face-to-face conversation UI with player reply choices, driven entirely
    /// by Assets/Resources/dialogues.txt (edit conversations there, no code).
    /// The player is locked while talking. NOTHING advances on its own: lines move on only when
    /// the player presses [Space] / [Enter] / [E] or clicks. Choices: click or keys 1-3.
    /// File format: see the header of dialogues.txt.
    /// </summary>
    public class DialogueScreen : MonoBehaviour
    {
        public static DialogueScreen Instance { get; private set; }

        public bool IsPlaying { get; private set; }

        RectTransform root;
        Text speakerText, lineText;
        RectTransform choicesRoot;
        CanvasGroup panelGroup;
        Image accent;
        Text hintText;
        readonly List<Button> choiceButtons = new List<Button>();
        readonly Dictionary<string, List<string[]>> convos = new Dictionary<string, List<string[]>>();
        // Steps: line   = ["line", text]
        //        choice = ["choice", n, opt1..optN, react1..reactN]   (react may be "")
        int chosenIndex = -1;
        bool advanceRequested;
        float lastAdvance = -10f;

        static readonly Color SpeakerColor = new Color(1f, 0.72f, 0.35f);
        static readonly Color PlayerColor = new Color(0.55f, 0.75f, 1f);
        static readonly Color TextColor = new Color(0.95f, 0.95f, 0.92f, 1f);

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            BuildUI();
            LoadData();
        }

        /// <summary>Parsed steps of a dialogues.txt section (used by the phone for texts and calls).</summary>
        public bool TryGetSteps(string id, out List<string[]> steps) => convos.TryGetValue(id, out steps);

        void BuildUI()
        {
            root = UIFactory.CanvasRoot("DialogueCanvas", 450);

            var panel = UIFactory.Round("Panel", root, new Color(0.02f, 0.02f, 0.04f, 0.92f), 18);
            panel.rectTransform.anchorMin = new Vector2(0.14f, 0.03f);
            panel.rectTransform.anchorMax = new Vector2(0.86f, 0.36f);
            panel.rectTransform.offsetMin = Vector2.zero;
            panel.rectTransform.offsetMax = Vector2.zero;
            panelGroup = panel.gameObject.AddComponent<CanvasGroup>();
            panelGroup.alpha = 0f;
            // thin accent bar on the left edge
            accent = UIFactory.Image("Accent", panel.rectTransform, SpeakerColor);
            accent.rectTransform.anchorMin = new Vector2(0f, 0f);
            accent.rectTransform.anchorMax = new Vector2(0f, 1f);
            accent.rectTransform.pivot = new Vector2(0f, 0.5f);
            accent.rectTransform.sizeDelta = new Vector2(5f, 0f);
            accent.raycastTarget = false;

            speakerText = UIFactory.Text("Speaker", panel.rectTransform, "", 26, TextAnchor.UpperLeft, SpeakerColor);
            speakerText.rectTransform.anchorMin = new Vector2(0f, 1f);
            speakerText.rectTransform.anchorMax = new Vector2(1f, 1f);
            speakerText.rectTransform.offsetMin = new Vector2(30, -50);
            speakerText.rectTransform.offsetMax = new Vector2(-30, -18);
            speakerText.fontStyle = FontStyle.Bold;
            speakerText.raycastTarget = false;

            lineText = UIFactory.Text("Line", panel.rectTransform, "", 32, TextAnchor.UpperLeft, TextColor);
            lineText.rectTransform.anchorMin = new Vector2(0f, 1f);
            lineText.rectTransform.anchorMax = new Vector2(1f, 1f);
            lineText.rectTransform.offsetMin = new Vector2(30, -168);
            lineText.rectTransform.offsetMax = new Vector2(-30, -54);
            lineText.raycastTarget = false;

            choicesRoot = UIFactory.NewRect("Choices", panel.rectTransform);
            choicesRoot.anchorMin = new Vector2(0f, 0f);
            choicesRoot.anchorMax = new Vector2(1f, 1f);
            choicesRoot.offsetMin = new Vector2(30, 16);
            choicesRoot.offsetMax = new Vector2(-30, -60);

            hintText = UIFactory.Text("Hint", panel.rectTransform, "", 20, TextAnchor.LowerRight,
                new Color(1f, 1f, 1f, 0.5f));
            hintText.rectTransform.anchorMin = new Vector2(0f, 0f);
            hintText.rectTransform.anchorMax = new Vector2(1f, 0f);
            hintText.rectTransform.pivot = new Vector2(1f, 0f);
            hintText.rectTransform.offsetMin = new Vector2(30, 10);
            hintText.rectTransform.offsetMax = new Vector2(-30, 38);
            hintText.raycastTarget = false;
            hintText.gameObject.SetActive(false);

            root.gameObject.SetActive(false);
        }

        IEnumerator Fade(float from, float to, float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                panelGroup.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds));
                yield return null;
            }
            panelGroup.alpha = to;
        }

        void SetSpeaker(string who, Color c)
        {
            speakerText.text = who.ToUpperInvariant();
            speakerText.color = c;
            accent.color = c;
        }

        void LoadData()
        {
            var asset = Resources.Load<TextAsset>("dialogues");
            if (asset == null)
            {
                Debug.LogWarning("[Dialogue] Assets/Resources/dialogues.txt missing.");
                return;
            }
            string id = null;
            List<string[]> steps = null;
            foreach (var raw in asset.text.Split('\n'))
            {
                var line = raw.TrimEnd('\r', ' ', '\t').Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    id = line.Substring(1, line.Length - 2).Trim();
                    steps = new List<string[]>();
                    convos[id] = steps;
                    continue;
                }
                if (id == null) continue;
                if (line.StartsWith("line:", System.StringComparison.OrdinalIgnoreCase))
                {
                    steps.Add(new[] { "line", Tokens(line.Substring(5).Trim()) });
                }
                else if (line.StartsWith("me:", System.StringComparison.OrdinalIgnoreCase))
                {
                    // the player speaking out loud (no choice): shown under the player's name
                    steps.Add(new[] { "me", Tokens(line.Substring(3).Trim()) });
                }
                else if (line.StartsWith("choice:", System.StringComparison.OrdinalIgnoreCase))
                {
                    var opts = line.Substring(7).Trim().Split(new[] { " | " }, System.StringSplitOptions.None);
                    int n = opts.Length;
                    var arr = new string[2 + n * 2];
                    arr[0] = "choice"; arr[1] = n.ToString();
                    for (int i = 0; i < n; i++) { arr[2 + i] = Tokens(opts[i].Trim()); arr[2 + n + i] = ""; }
                    steps.Add(arr);
                }
                else if (line.StartsWith("reply:", System.StringComparison.OrdinalIgnoreCase)
                         && steps != null && steps.Count > 0 && steps[steps.Count - 1][0] == "choice")
                {
                    // reply: <reaction to option 1> | <reaction to option 2> | ...   ("-" = no reaction)
                    var last = steps[steps.Count - 1];
                    int n = int.Parse(last[1]);
                    var reacts = line.Substring(6).Trim().Split(new[] { " | " }, System.StringSplitOptions.None);
                    for (int i = 0; i < n && i < reacts.Length; i++)
                    {
                        var r = reacts[i].Trim();
                        last[2 + n + i] = r == "-" ? "" : Tokens(r);
                    }
                }
            }
            Debug.Log("[Dialogue] loaded " + convos.Count + " conversation(s): " + string.Join(", ", convos.Keys));
        }

        static string Tokens(string s) => s
            .Replace("{player}", Core.GameConfig.PlayerName)
            .Replace("{streamer}", Core.GameConfig.StreamerName)
            .Replace("{friend}", Core.GameConfig.FriendName)
            .Replace("{intruder}", Core.GameConfig.IntruderName);

        /// <summary>Typewriter a line, then wait for the player. A press first completes the text, the next advances.</summary>
        IEnumerator ShowLine(string who, Color color, string text)
        {
            ClearChoices();
            SetSpeaker(who, color);
            lineText.color = TextColor;
            hintText.gameObject.SetActive(false);

            int shown = 0; float acc = 0f;
            advanceRequested = false;
            while (shown < text.Length)
            {
                if (TakeAdvance()) break;
                acc += Time.unscaledDeltaTime * 60f;
                int n = (int)acc; acc -= n;
                shown = Mathf.Min(text.Length, shown + n);
                lineText.text = text.Substring(0, shown);
                yield return null;
            }
            lineText.text = text;

            hintText.text = "[Space] / click  to continue";
            hintText.gameObject.SetActive(true);
            float t = 0f;
            while (!TakeAdvance())   // no timeout: waits for the player, however long
            {
                t += Time.unscaledDeltaTime;
                var hc = hintText.color; hc.a = 0.3f + 0.3f * Mathf.Sin(t * 5f); hintText.color = hc;
                yield return null;
            }
            hintText.gameObject.SetActive(false);
        }

        bool TakeAdvance()
        {
            if (!advanceRequested) return false;
            advanceRequested = false;
            return true;
        }

        /// <summary>Play a conversation (blocks until the player has read to the end).</summary>
        public IEnumerator Play(string convoId, string speaker = null)
        {
            if (IsPlaying) yield break;
            if (!convos.ContainsKey(convoId))
            {
                Debug.LogWarning("[Dialogue] no conversation '" + convoId + "'");
                yield break;
            }
            var gm = Core.GameManager.Instance;
            bool prevLock = gm != null && gm.PlayerLocked;
            if (gm != null) { gm.PlayerLocked = true; if (gm.HUD != null) gm.HUD.SetCenterPrompt(""); }

            IsPlaying = true;
            advanceRequested = false;
            lastAdvance = Time.unscaledTime + 0.3f;   // swallow the [E] press that opened the conversation
            chosenIndex = -1;
            string npc = string.IsNullOrEmpty(speaker) ? Core.GameConfig.StreamerName : speaker;
            SetSpeaker(npc, SpeakerColor);
            lineText.text = "";
            panelGroup.alpha = 0f;
            root.gameObject.SetActive(true);
            yield return StartCoroutine(Fade(0f, 1f, 0.25f));

            var steps = convos[convoId];
            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                if (step[0] == "line")
                {
                    yield return StartCoroutine(ShowLine(npc, SpeakerColor, step[1]));
                }
                else if (step[0] == "me")
                {
                    yield return StartCoroutine(ShowLine(Core.GameConfig.PlayerName, PlayerColor, step[1]));
                }
                else if (step[0] == "choice")
                {
                    int n = int.Parse(step[1]);
                    lineText.text = "";
                    SetSpeaker(Core.GameConfig.PlayerName, PlayerColor);
                    ClearChoices();
                    for (int c = 0; c < n; c++)
                    {
                        int idx = c;
                        var btn = UIFactory.Button("Choice" + c, choicesRoot,
                            (c + 1) + ".  " + step[2 + c], 28, Color.white);
                        var img = btn.GetComponent<Image>();
                        img.sprite = UIFactory.Rounded(12); img.type = Image.Type.Sliced;
                        var cb = btn.colors;   // hover/press feedback (tint multiplies the white image)
                        cb.normalColor = new Color(0.13f, 0.15f, 0.2f, 0.95f);
                        cb.highlightedColor = new Color(0.30f, 0.36f, 0.52f, 1f);
                        cb.pressedColor = new Color(0.42f, 0.50f, 0.72f, 1f);
                        cb.fadeDuration = 0.1f;
                        btn.colors = cb;
                        var br = btn.GetComponent<RectTransform>();
                        br.anchorMin = new Vector2(0f, 1f);
                        br.anchorMax = new Vector2(1f, 1f);
                        br.pivot = new Vector2(0.5f, 1f);
                        br.anchoredPosition = new Vector2(0, -c * 62);
                        br.sizeDelta = new Vector2(0, 56);
                        // button label: left aligned, a bit of padding
                        var lbl = btn.GetComponentInChildren<Text>();
                        if (lbl != null) { lbl.alignment = TextAnchor.MiddleLeft; lbl.rectTransform.offsetMin = new Vector2(20, 0); }
                        btn.onClick.AddListener(() => { chosenIndex = idx; });
                        choiceButtons.Add(btn);
                    }
                    hintText.text = "click, or press 1-" + n;
                    hintText.gameObject.SetActive(true);
                    var hc0 = hintText.color; hc0.a = 0.5f; hintText.color = hc0;

                    // wait for a pick (click or number keys) - never auto-picks
                    while (chosenIndex < 0)
                    {
                        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) chosenIndex = 0;
                        else if ((Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) && n > 1) chosenIndex = 1;
                        else if ((Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) && n > 2) chosenIndex = 2;
                        yield return null;
                    }
                    int pick = chosenIndex;
                    chosenIndex = -1;
                    advanceRequested = false;
                    lastAdvance = Time.unscaledTime;   // the click that picked must not also skip the reply

                    // the player's reply, then the NPC's reaction to it (if the script has one)
                    yield return StartCoroutine(ShowLine(Core.GameConfig.PlayerName, PlayerColor, step[2 + pick]));
                    string react = step[2 + n + pick];
                    if (!string.IsNullOrEmpty(react))
                        yield return StartCoroutine(ShowLine(npc, SpeakerColor, react));
                }
            }

            lineText.text = "";
            hintText.gameObject.SetActive(false);
            ClearChoices();
            yield return StartCoroutine(Fade(1f, 0f, 0.2f));
            root.gameObject.SetActive(false);
            IsPlaying = false;
            if (gm != null) gm.PlayerLocked = prevLock;
        }

        /// <summary>Test/script hook: pick reply option idx (0-based) during a choice.</summary>
        public void RequestPick(int idx) => chosenIndex = idx;

        /// <summary>Test/script hook: advance the current line (as if the player pressed [Space]).</summary>
        public void RequestAdvance()
        {
            advanceRequested = true;
            lastAdvance = Time.unscaledTime;
        }

        void ClearChoices()
        {
            foreach (var b in choiceButtons) if (b != null) Destroy(b.gameObject);
            choiceButtons.Clear();
        }

        void Update()
        {
            if (!IsPlaying) return;
            // Only deliberate "continue" inputs - NOT any key (walking keys, Shift, the opening E press ...).
            bool pressed = Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)
                        || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.E)
                        || Input.GetMouseButtonDown(0);
            if (pressed && Time.unscaledTime >= lastAdvance + 0.15f)
            {
                advanceRequested = true;
                lastAdvance = Time.unscaledTime;
            }
        }
    }
}
