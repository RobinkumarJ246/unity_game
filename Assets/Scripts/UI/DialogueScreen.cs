using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ContentHouse.UI
{
    /// <summary>
    /// Face-to-face conversation UI with player reply choices, driven entirely
    /// by Assets/Resources/dialogues.txt (edit conversations there, no code).
    /// The player is locked while talking; any key/click advances lines;
    /// choices are clickable buttons.
    /// </summary>
    public class DialogueScreen : MonoBehaviour
    {
        public static DialogueScreen Instance { get; private set; }

        public bool IsPlaying { get; private set; }

        RectTransform root;
        Text speakerText, lineText;
        RectTransform choicesRoot;
        readonly List<Button> choiceButtons = new List<Button>();
        readonly Dictionary<string, List<string[]>> convos = new Dictionary<string, List<string[]>>();
        // Each step: [0]=kind ("line"/"choice"), [1]=text, [2..]=options.
        int chosenIndex = -1;
        bool advanceRequested;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            BuildUI();
            LoadData();
        }

        void BuildUI()
        {
            root = UIFactory.CanvasRoot("DialogueCanvas", 450);

            var panel = UIFactory.Image("Panel", root, new Color(0.02f, 0.02f, 0.04f, 0.92f));
            panel.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            panel.rectTransform.anchorMax = new Vector2(1f, 0.32f);
            panel.rectTransform.offsetMin = new Vector2(40, 40);
            panel.rectTransform.offsetMax = new Vector2(-40, -40);

            speakerText = UIFactory.Text("Speaker", panel.rectTransform, "", 26, TextAnchor.UpperLeft,
                new Color(1f, 0.72f, 0.35f));
            speakerText.rectTransform.anchorMin = new Vector2(0f, 1f);
            speakerText.rectTransform.anchorMax = new Vector2(1f, 1f);
            speakerText.rectTransform.offsetMin = new Vector2(26, -50);
            speakerText.rectTransform.offsetMax = new Vector2(-26, -18);

            lineText = UIFactory.Text("Line", panel.rectTransform, "", 32, TextAnchor.UpperLeft,
                new Color(0.95f, 0.95f, 0.92f));
            lineText.rectTransform.anchorMin = new Vector2(0f, 1f);
            lineText.rectTransform.anchorMax = new Vector2(1f, 1f);
            lineText.rectTransform.offsetMin = new Vector2(26, -118);
            lineText.rectTransform.offsetMax = new Vector2(-26, -52);

            choicesRoot = UIFactory.NewRect("Choices", panel.rectTransform);
            choicesRoot.anchorMin = new Vector2(0f, 0f);
            choicesRoot.anchorMax = new Vector2(1f, 1f);
            choicesRoot.offsetMin = new Vector2(26, 20);
            choicesRoot.offsetMax = new Vector2(-26, -130);

            root.gameObject.SetActive(false);
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
                    var t = line.Substring(5).Trim();
                    steps.Add(new[] { "line", Tokens(t) });
                }
                else if (line.StartsWith("choice:", System.StringComparison.OrdinalIgnoreCase))
                {
                    var t = line.Substring(7).Trim();
                    var opts = t.Split(new[] { " | " }, System.StringSplitOptions.None);
                    var arr = new string[opts.Length + 2];
                    arr[0] = "choice"; arr[1] = "";
                    for (int i = 0; i < opts.Length; i++) arr[i + 2] = Tokens(opts[i].Trim());
                    steps.Add(arr);
                }
            }
            Debug.Log("[Dialogue] loaded " + convos.Count + " conversation(s): " + string.Join(", ", convos.Keys));
        }

        static string Tokens(string s) => s
            .Replace("{player}", Core.GameConfig.PlayerName)
            .Replace("{streamer}", Core.GameConfig.StreamerName)
            .Replace("{friend}", Core.GameConfig.FriendName)
            .Replace("{intruder}", Core.GameConfig.IntruderName);

        /// <summary>Play a conversation. chosenIndex-style returns are internal.</summary>
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
            speakerText.text = (speaker ?? Core.GameConfig.StreamerName).ToUpperInvariant();
            root.gameObject.SetActive(true);

            var steps = convos[convoId];
            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                if (step[0] == "line")
                {
                    ClearChoices();
                    lineText.color = new Color(0.95f, 0.95f, 0.92f, 1f);
                    lineText.text = step[1];
                    // wait for input (any key/click), like narration
                    float shown = 0f;
                    while (true)
                    {
                        if (advanceRequested && Time.time - lastAdvance > 0.25f) { advanceRequested = false; break; }
                        shown += Time.deltaTime;
                        if (shown > 2.5f + step[1].Length * 0.02f) break;   // slow auto-advance fallback
                        yield return null;
                    }
                }
                else if (step[0] == "choice")
                {
                    lineText.color = new Color(0.7f, 0.78f, 0.9f, 1f);
                    lineText.text = "";
                    ClearChoices();
                    for (int c = 0; c < step.Length - 2; c++)
                    {
                        int idx = c;
                        var btn = UIFactory.Button("Choice" + c, choicesRoot,
                            (c + 1) + ".  " + step[c + 2], 28, new Color(0.13f, 0.15f, 0.2f, 0.95f));
                        var br = btn.GetComponent<RectTransform>();
                        br.anchorMin = new Vector2(0f, 1f);
                        br.anchorMax = new Vector2(1f, 1f);
                        br.pivot = new Vector2(0.5f, 1f);
                        br.anchoredPosition = new Vector2(0, -c * 62);
                        br.sizeDelta = new Vector2(0, 56);
                        btn.onClick.AddListener(() => { chosenIndex = idx; });
                        choiceButtons.Add(btn);
                    }
                    // wait for a pick (click or 1/2/3 keys)
                    while (chosenIndex < 0)
                    {
                        if (Input.GetKeyDown(KeyCode.Alpha1)) chosenIndex = 0;
                        else if (Input.GetKeyDown(KeyCode.Alpha2) && step.Length > 3) chosenIndex = 1;
                        else if (Input.GetKeyDown(KeyCode.Alpha3) && step.Length > 4) chosenIndex = 2;
                        yield return null;
                    }
                    // player's chosen reply reads as their line
                    lineText.color = new Color(0.95f, 0.95f, 0.92f, 1f);
                    lineText.text = "(you)  " + step[chosenIndex + 2];
                    ClearChoices();
                    chosenIndex = -1;
                    float hold = 0f;
                    while (hold < 1.4f) { hold += Time.deltaTime; yield return null; }
                }
            }

            lineText.text = "";
            root.gameObject.SetActive(false);
            IsPlaying = false;
            if (gm != null) gm.PlayerLocked = prevLock;
        }

        float lastAdvance = -10f;

        /// <summary>Test/script hook: pick reply option idx (0-based) during a choice.</summary>
        public void RequestPick(int idx) => chosenIndex = idx;

        /// <summary>Test/script hook: force-advance the current line.</summary>
        public void RequestAdvance()
        {
            advanceRequested = true;
            lastAdvance = Time.time;
        }

        void ClearChoices()
        {
            foreach (var b in choiceButtons) if (b != null) Destroy(b.gameObject);
            choiceButtons.Clear();
        }

        void Update()
        {
            if (!IsPlaying) return;
            if (Input.anyKeyDown ||
                Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
            {
                advanceRequested = true;
                lastAdvance = Time.time;
            }
        }
    }
}
