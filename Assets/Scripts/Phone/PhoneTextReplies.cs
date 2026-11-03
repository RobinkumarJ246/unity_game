using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ContentHouse.Phone
{
    /// <summary>
    /// The secret-reply engine behind the phone's free typing. Rules come from Resources/text_replies.txt
    /// (format documented at the top of that file). Plain class: owned by PhoneController.
    /// Only a few phrases ever get an answer, and they can't be farmed: repeats are ignored, there is a
    /// global pause between secret replies, and every rule has its own max + cooldown (+ optional chance).
    /// </summary>
    public class PhoneTextReplies
    {
        class Cond { public List<string> Any = new List<string>(); public List<string> Also = new List<string>(); }

        class Rule
        {
            public string Id, To = "any";
            public List<Cond> Conds = new List<Cond>();
            public List<string> Replies = new List<string>();
            public float Chance = 1f, Cooldown = 45f, DelayMin = 2f, DelayMax = 4f;
            public int Max = 2;
            public int Count;
            public float Last = -999f;
        }

        const float GlobalPause = 8f;      // min seconds between any two secret replies
        const float RepeatWindow = 120f;   // the same text to the same person is ignored for this long

        readonly List<Rule> rules = new List<Rule>();
        readonly Dictionary<string, float> seen = new Dictionary<string, float>();
        float lastAnyReply = -999f;

        public int RuleCount => rules.Count;

        public PhoneTextReplies(string resourceName = "text_replies")
        {
            var asset = Resources.Load<TextAsset>(resourceName);
            if (asset == null) { Debug.LogWarning("[Phone] Resources/" + resourceName + ".txt missing - no secret replies."); return; }
            Parse(asset.text);
            Debug.Log("[Phone] loaded " + rules.Count + " secret reply rule(s)");
        }

        void Parse(string text)
        {
            Rule rule = null; Cond cond = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r', ' ', '\t').Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    rule = new Rule { Id = line.Substring(1, line.Length - 2).Trim() };
                    rules.Add(rule); cond = null;
                    continue;
                }
                if (rule == null) continue;
                int c = line.IndexOf(':');
                if (c < 0) continue;
                string key = line.Substring(0, c).Trim().ToLowerInvariant();
                string val = line.Substring(c + 1).Trim();
                switch (key)
                {
                    case "to": rule.To = val.ToLowerInvariant(); break;
                    case "any": cond = new Cond(); cond.Any = Phrases(val); rule.Conds.Add(cond); break;
                    case "also": if (cond != null) cond.Also = Phrases(val); break;
                    case "reply":
                        foreach (var r in val.Split(new[] { " | " }, System.StringSplitOptions.RemoveEmptyEntries))
                            if (r.Trim().Length > 0) rule.Replies.Add(r.Trim());
                        break;
                    case "chance": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out rule.Chance); break;
                    case "max": int.TryParse(val, out rule.Max); break;
                    case "cooldown": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out rule.Cooldown); break;
                    case "delay":
                        var parts = val.Split('-');
                        if (parts.Length == 2)
                        {
                            float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out rule.DelayMin);
                            float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out rule.DelayMax);
                        }
                        break;
                }
            }
        }

        static List<string> Phrases(string val)
        {
            var list = new List<string>();
            foreach (var p in val.Split(new[] { " | " }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                var n = Norm(p);
                if (n.Length > 0) list.Add(" " + n + " ");   // padded: matches whole words only
            }
            return list;
        }

        /// <summary>Lower-case, letters+digits only (apostrophes vanish: "you're" -> "youre"), single spaces.</summary>
        public static string Norm(string s)
        {
            var sb = new StringBuilder(s.Length);
            bool space = true;
            foreach (char ch in s.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch)) { sb.Append(ch); space = false; }
                else if (ch == '\'' || ch == '’') { /* drop */ }
                else if (!space) { sb.Append(' '); space = true; }
            }
            return sb.ToString().Trim();
        }

        static bool ContainsAny(string padded, List<string> phrases)
        {
            foreach (var p in phrases) if (padded.Contains(p)) return true;
            return false;
        }

        bool Matches(Rule r, string padded)
        {
            foreach (var c in r.Conds)
            {
                if (c.Any.Count == 0 || !ContainsAny(padded, c.Any)) continue;
                if (c.Also.Count > 0 && !ContainsAny(padded, c.Also)) continue;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Decide whether the typed text gets a secret answer. toKey: "ig" or "manager".
        /// now: Time.unscaledTime. Returns false (no reply) for anything that isn't a hidden phrase,
        /// and for spam.
        /// </summary>
        public bool TryReply(string toKey, string typed, float now, out string reply, out float delay)
        {
            reply = null; delay = 0f;
            string n = Norm(typed);
            if (n.Length == 0) return false;

            // repeats of the same text are ignored (first send stays "seen" for the whole window)
            string seenKey = toKey + "|" + n;
            float t;
            if (seen.TryGetValue(seenKey, out t) && now - t < RepeatWindow) return false;
            seen[seenKey] = now;

            if (now - lastAnyReply < GlobalPause) return false;

            string padded = " " + n + " ";
            foreach (var r in rules)
            {
                if (r.To != "any" && r.To != toKey) continue;
                if (r.Replies.Count == 0 || !Matches(r, padded)) continue;
                if (r.Count >= r.Max) continue;
                if (now - r.Last < r.Cooldown) continue;
                if (r.Chance < 1f && Random.value > r.Chance) continue;   // "sometimes": no state change, can retry later

                reply = r.Count == 0 ? r.Replies[0] : r.Replies[Random.Range(0, r.Replies.Count)];
                delay = Random.Range(r.DelayMin, Mathf.Max(r.DelayMin, r.DelayMax));
                r.Count++; r.Last = now; lastAnyReply = now;
                return true;
            }
            return false;
        }
    }
}
