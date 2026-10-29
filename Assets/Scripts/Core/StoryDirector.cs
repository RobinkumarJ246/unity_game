using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ContentHouse.Core
{
    /// <summary>
    /// Data-driven day sequencer: reads Assets/Resources/days.txt and plays
    /// the days in order. All texts, weather, narrations, chores and which
    /// day a set-piece happens on are edited in that file - no code needed.
    /// Set-pieces themselves (blackout, intruder, stealth, climax) live in
    /// StoryManager and are invoked by name.
    /// </summary>
    public class StoryDirector : MonoBehaviour
    {
        public StoryManager Story;

        public class DayStep
        {
            public string Kind;      // title/weather/narration/message/objective/tasks/event/sleep
            public string Value;
            public List<string> Args = new List<string>();
        }
        public class Day
        {
            public string Id;
            public List<DayStep> Steps = new List<DayStep>();
        }

        readonly List<Day> days = new List<Day>();

        void Awake()
        {
            Parse();
        }

        void Parse()
        {
            var asset = Resources.Load<TextAsset>("days");
            if (asset == null)
            {
                Debug.LogWarning("[Director] Assets/Resources/days.txt missing - falling back to StoryManager acts.");
                return;
            }
            Day day = null;
            foreach (var raw in asset.text.Split('\n'))
            {
                var line = raw.TrimEnd('\r', ' ', '\t').Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    day = new Day { Id = line.Substring(1, line.Length - 2).Trim() };
                    days.Add(day);
                    continue;
                }
                if (!line.StartsWith("@") || day == null) continue;
                int sp = line.IndexOf(' ');
                var kind = sp < 0 ? line.Substring(1) : line.Substring(1, sp - 1);
                var rest = sp < 0 ? "" : line.Substring(sp + 1).Trim();
                var step = new DayStep { Kind = kind.ToLowerInvariant(), Value = rest };
                if (kind == "tasks")
                    foreach (var t in rest.Split(' ', '\t'))
                        if (t.Trim().Length > 0) step.Args.Add(t.Trim());
                day.Steps.Add(step);
            }
            Debug.Log("[Director] loaded " + days.Count + " day(s): " + string.Join(", ", days.ConvertAll(d => d.Id)));
        }

        /// <summary>Start the game through the day script (called after the intro narration).</summary>
        public IEnumerator PlayAllDays()
        {
            if (Story == null) Story = GameManager.Instance != null ? GameManager.Instance.Story : null;
            if (days.Count == 0 || Story == null)
            {
                // Fallback: legacy act flow.
                Story.BeginGame();
                yield break;
            }

            Story.BeginGame();   // phase, checkpoint, storm defaults

            foreach (var day in days)
            {
                Debug.Log("[Director] starting day " + day.Id);
                yield return StartCoroutine(RunDay(day));
                if (Story.GM.Phase == GameManager.GamePhase.Epilogue) yield break;   // game over
            }
        }

        IEnumerator RunDay(Day day)
        {
            var gm = Story.GM;
            var hud = Story.HUD;
            var phone = Story.Phone;
            var amb = Story.Ambience;
            var narr = UI.NarrationScreen.Instance;

            string title = day.Id.ToUpperInvariant();
            List<string> tasks = null;
            bool hasSleep = false;

            // ---- opening steps (before events) ----
            int i = 0;
            for (; i < day.Steps.Count; i++)
            {
                var s = day.Steps[i];
                switch (s.Kind)
                {
                    case "title":
                        title = s.Value.ToUpperInvariant();
                        if (hud != null) hud.SetObjectiveLabel(title);
                        break;

                    case "weather":
                        if (amb != null)
                        {
                            if (s.Value.ToLowerInvariant().StartsWith("calm")) amb.StopStorm();
                            else amb.StartStorm();
                        }
                        break;

                    case "narration":
                        if (narr != null) yield return StartCoroutine(narr.Play(s.Value));
                        break;

                    case "message":
                        int bar = s.Value.IndexOf('|');
                        if (bar > 0 && phone != null)
                        {
                            phone.AddMessage(ReplaceTokens(s.Value.Substring(0, bar)),
                                             ReplaceTokens(s.Value.Substring(bar + 1)));
                            if (gm.Player != null)
                                Audio.ProceduralAudio.PlayPhoneBuzz(gm.Player.transform.position);
                        }
                        break;

                    case "objective":
                        if (hud != null) hud.SetObjective(s.Value.Replace(" ; ", "\n"));
                        break;

                    case "tasks":
                        tasks = s.Args;
                        break;

                    case "sleep":
                        hasSleep = true;
                        break;

                    case "event":
                        // events run in order after the opening steps
                        i--;   // re-process this step in the event loop
                        break;
                }
                if (s.Kind == "event") break;
            }

            // ---- events, in order ----
            for (; i < day.Steps.Count; i++)
            {
                var s = day.Steps[i];
                if (s.Kind == "sleep") { hasSleep = true; continue; }
                if (s.Kind == "title" || s.Kind == "weather" || s.Kind == "narration"
                    || s.Kind == "message" || s.Kind == "objective" || s.Kind == "tasks")
                    continue;   // opening steps already handled
                if (s.Kind != "event") continue;

                switch (s.Value.ToLowerInvariant())
                {
                    case "blackout":
                        yield return StartCoroutine(Story.RunBlackout());
                        break;
                    case "bang":
                        yield return StartCoroutine(Story.RunKitchenBang());
                        break;
                    case "intruder":
                        yield return StartCoroutine(Story.RunGlassIntrusion());
                        break;
                    case "stealth":
                        yield return StartCoroutine(Story.RunStealthPhase());
                        break;
                    case "climax":
                        yield return StartCoroutine(Story.RunClimax());
                        break;
                    default:
                        Debug.LogWarning("[Director] unknown event '" + s.Value + "'");
                        break;
                }
            }

            // ---- wait for chores ----
            if (tasks != null && tasks.Count > 0)
            {
                if (hud != null) hud.SetObjectiveLabel(title);
                while (!AllTasksDone(tasks)) yield return null;
            }

            // ---- sleep: fade out, next day ----
            if (hasSleep && hud != null)
            {
                yield return StartCoroutine(hud.FadeTo(1f, 1.5f));
                yield return new WaitForSeconds(1.2f);
            }
        }

        bool AllTasksDone(List<string> tasks)
        {
            var gm = Story.GM;
            foreach (var t in tasks)
                if (!gm.IsTaskDone(t)) return false;
            return true;
        }

        static string ReplaceTokens(string s) => s
            .Replace("{player}", GameConfig.PlayerName)
            .Replace("{streamer}", GameConfig.StreamerName)
            .Replace("{friend}", GameConfig.FriendName)
            .Replace("{intruder}", GameConfig.IntruderName);
    }
}
