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

        /// <summary>Dev quick-start stage from Resources/dev_start.txt ("wash", "eat", "bed") or null for a normal game.</summary>
        public string DevStage { get; private set; }

        void Awake()
        {
            Parse();
            var dev = Resources.Load<TextAsset>("dev_start");
            if (dev != null)
                foreach (var raw in dev.text.Split('\n'))
                {
                    var l = raw.Trim();
                    if (l.Length == 0 || l.StartsWith("#")) continue;
                    DevStage = l.ToLowerInvariant() == "none" ? null : l.ToLowerInvariant();
                    break;
                }
            if (DevStage != null) Debug.Log("[Director] DEV QUICK-START stage '" + DevStage + "' (edit Resources/dev_start.txt to turn off)");
        }

        // tasks that are already done when a stage starts (in story order)
        static readonly string[] Order = { "talk", "deliver", "pee", "dinnermsg", "manager", "dinner", "eat", "wash", "workmsg" };

        void ApplyDevStart()
        {
            if (DevStage == null) return;
            int upTo = DevStage == "eat" ? System.Array.IndexOf(Order, "dinner")
                     : DevStage == "wash" ? System.Array.IndexOf(Order, "eat")
                     : DevStage == "bed" ? System.Array.IndexOf(Order, "workmsg") : -1;
            if (upTo < 0) { Debug.LogWarning("[Director] unknown dev stage '" + DevStage + "'"); return; }
            var gm = Story.GM;
            for (int i = 0; i <= upTo; i++) gm.MarkDoneSilently(Order[i]);

            Vector3 pos; float yaw; string thought;
            switch (DevStage)
            {
                case "eat": pos = new Vector3(-5.8f, 0.45f, -2.4f); yaw = -90f; thought = "\"Pizza. TV. Sofa. Now.\""; gm.SetHeldItem("leftover pizza"); break;
                case "wash": pos = new Vector3(-7.6f, 0.45f, -12.7f); yaw = -117f; thought = "\"The dishes won't wash themselves. ...Sadly.\""; break;
                default: pos = new Vector3(-4.6f, 3.8f, -3.2f); yaw = 0f; thought = "\"Okay. Bed. Finally.\""; break;
            }
            var pl = Story.Player; var cc = pl.GetComponent<CharacterController>();
            cc.enabled = false; pl.TeleportTo(pos, yaw); cc.enabled = true;
            Story.HUD.SetObjective(thought);
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

            // Whatever started us (main menu, a test, a script), days.txt owns the story from here:
            // never let the legacy Act I opening (router / fridge chores text) fire alongside it.
            Story.DirectorDriven = true;
            Story.BeginGame();   // phase, checkpoint, storm defaults
            ApplyDevStart();     // (only when Resources/dev_start.txt names a stage)

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
            bool hasSleep = false, hasDemoEnd = false;

            // a new day always starts with the screen open (a previous day's fade-out must never stay black)
            if (hud != null) yield return StartCoroutine(hud.FadeTo(0f, 1.2f));

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
                        if (DevStage != null && day.Id == "day1") break;   // quick-start: you're already past this
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
                        if (DevStage != null && day.Id == "day1") break;   // quick-start sets its own thought
                        if (hud != null) hud.SetObjective(s.Value.Replace(" ; ", "\n"));
                        break;

                    case "tasks":
                        tasks = s.Args;
                        break;

                    case "sleep":
                        hasSleep = true;
                        break;

                    case "demoend":     // after this day: end card, then back to the menu - the game stops here (demo)
                        hasDemoEnd = true;
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

            // ---- demo end: closing narration + end card, and the story stops here ----
            if (hasDemoEnd)
            {
                if (narr != null)
                {
                    // The closed eyelids / black fade are drawn ABOVE the narration cards: lift them once the
                    // card screen (its own black background) is up, or the text would type out unseen.
                    narr.KeepBlackAtEnd = true;     // straight on to the end card, never back to the room
                    var play = StartCoroutine(narr.Play("demo_end"));
                    yield return new WaitForSeconds(0.8f);
                    UI.ScreenFx.Instance.SetEyelids(0f, snap: true);
                    if (hud != null) yield return StartCoroutine(hud.FadeTo(0f, 0.05f));
                    yield return play;
                }
                UI.ScreenFx.Instance.SetEyelids(0f, snap: true);
                yield return StartCoroutine(UI.DemoEndScreen.Show(this));
                while (true) yield return null;   // (the end card reloads the scene on Enter)
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
