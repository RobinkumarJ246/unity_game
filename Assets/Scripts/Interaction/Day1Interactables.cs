using System.Collections;
using UnityEngine;

namespace ContentHouse.Interaction
{
    /// <summary>
    /// Day-1 slice-of-life chain (days.txt '@tasks talk deliver dinner eat wash sleep'):
    /// talk to Mr. IG -> grab his drink from the kitchen fridge -> deliver it at his
    /// door (existing knock plate) -> call him for dinner -> grab dinner from the
    /// fridge -> eat in front of the TV -> wash the plates at the sink -> sleep.
    /// Every step is gated on the previous one so the evening flows naturally.
    /// </summary>
    public class TalkInteractable : InteractableObject
    {
        public string DialogueId = "ig_1m";
        public string SpeakerName = "";
        public string UnavailableLine = "";
        /// <summary>Optional: this conversation only becomes available once that task is done.</summary>
        public string RequiresTask = "";

        bool Locked => !string.IsNullOrEmpty(RequiresTask) && !GM_IsTaskDone(RequiresTask);

        /// <summary>The drink errand ends HERE: while you carry the Gamer-Juice, walk up to Mr. IG and hand it over.</summary>
        bool CanHandOver
        {
            get
            {
                var gm = Core.GameManager.Instance;
                return gm != null && TaskId == "talk" && gm.IsTaskDone("talk") && !gm.IsTaskDone("deliver")
                       && gm.HeldItem == "Gamer-Juice can";
            }
        }

        public override string Prompt => CanHandOver
            ? "Hand " + ShortName + " the Gamer-Juice  [E]"
            : GM_IsTaskDone(TaskId)
            ? ""
            : (Locked ? ShortName + " is busy right now"
            : (IsPlayerNearDoor ? "Talk to " + ShortName + "  [E]" : (UnavailableLine.Length > 0 ? "" : "Talk to " + ShortName + "  [E]")));

        bool IsPlayerNearDoor => true;   // placed in the world; always offered until done

        string ShortName => string.IsNullOrEmpty(SpeakerName)
            ? Core.GameConfig.StreamerName : SpeakerName;

        static bool GM_IsTaskDone(string id)
        {
            var gm = Core.GameManager.Instance;
            return gm != null && !string.IsNullOrEmpty(id) && gm.IsTaskDone(id);
        }

        bool handingOver;

        public override void Interact(Player.PlayerInteraction player)
        {
            if (CanHandOver)
            {
                if (!handingOver) StartCoroutine(HandOverRoutine());
                return;
            }
            if (used || GM_IsTaskDone(TaskId) || Locked) return;
            used = true;
            var gm = Core.GameManager.Instance;
            if (gm != null) gm.StartCoroutine(PlayThenTask());
        }

        IEnumerator HandOverRoutine()
        {
            handingOver = true;
            var gm = Core.GameManager.Instance;
            var pl = gm.Player;
            gm.PlayerLocked = true;
            gm.HUD?.SetCenterPrompt("");
            var col = GetComponent<Collider>();
            Vector3 head = (col != null ? col.bounds.center : transform.position) + Vector3.up * 0.45f;
            StartCoroutine(pl.FocusOn(head, 0.5f));
            float turned = 0f;
            var to = pl.transform.position - transform.position; to.y = 0f;
            if (to.sqrMagnitude > 0.01f)
            {
                turned = Vector3.SignedAngle(transform.forward, to, Vector3.up);
                yield return StartCoroutine(TurnBy(turned, 0.6f));
            }

            gm.SetHeldItem("");                                      // the can leaves your hand
            Audio.ProceduralAudio.PlayConfirmation(transform.position);
            gm.HUD?.QueueSubtitle(ShortName, "Ice cold. You're a legend, " + Core.GameConfig.PlayerName + ". ...Chat, clap for - oh. Chat's not even live. Habit.", 4.2f);
            yield return new WaitForSeconds(1.6f);

            gm.PlayerLocked = false;
            pl.LookLocked = false;
            gm.CompleteTask("deliver");                              // objective moves on (bathroom)
            if (Mathf.Abs(turned) > 0.1f) StartCoroutine(TurnBy(-turned, 0.9f));   // back to the monitor
            handingOver = false;
        }

        /// <summary>Optional chair (parent of the seat) that swivels with the NPC to face the player.</summary>
        public Transform Swivel;

        IEnumerator PlayThenTask()
        {
            var gm = Core.GameManager.Instance;
            var dialogue = gm != null ? UI.DialogueScreen.Instance : null;
            var pl = gm != null ? gm.Player : null;

            float turned = 0f;
            if (pl != null)
            {
                // Lock the player in place and ease the view onto the NPC while he swivels round.
                if (gm != null) gm.PlayerLocked = true;
                var col = GetComponent<Collider>();
                Vector3 head = (col != null ? col.bounds.center : transform.position) + Vector3.up * 0.45f;
                StartCoroutine(pl.FocusOn(head, 0.7f));
                var to = pl.transform.position - transform.position; to.y = 0f;
                if (to.sqrMagnitude > 0.01f)
                {
                    turned = Vector3.SignedAngle(transform.forward, to, Vector3.up);
                    yield return StartCoroutine(TurnBy(turned, 0.75f));
                }
                if (gm != null) gm.PlayerLocked = false;   // DialogueScreen takes over the lock
            }

            if (dialogue != null) yield return StartCoroutine(dialogue.Play(DialogueId, SpeakerName));

            if (pl != null)
            {
                pl.LookLocked = false;
                if (Mathf.Abs(turned) > 0.1f) StartCoroutine(TurnBy(-turned, 0.9f));   // back to the monitor
            }
            NotifyTask();
            Core.GameManager.Instance?.CompleteTask(TaskId);
        }

        /// <summary>Yaw the NPC (and chair) around the chair's vertical axis, eased.</summary>
        IEnumerator TurnBy(float degrees, float seconds)
        {
            Vector3 pivot = Swivel != null ? Swivel.position : transform.position;
            float done = 0f;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float step = degrees * Mathf.SmoothStep(0f, 1f, t / seconds) - done;
                done += step;
                transform.RotateAround(pivot, Vector3.up, step);
                if (Swivel != null) Swivel.RotateAround(pivot, Vector3.up, step);
                yield return null;
            }
            float rest = degrees - done;
            transform.RotateAround(pivot, Vector3.up, rest);
            if (Swivel != null) Swivel.RotateAround(pivot, Vector3.up, rest);
        }
    }

    /// <summary>
    /// The kitchen fridge: first the celebratory drink, later dinner. Hold [E] while you rummage through it
    /// (a moment of fridge-light and clinking cans instead of an instant grab).
    /// </summary>
    public class KitchenFridgeInteractable : HoldInteractable
    {
        const string Can = "Gamer-Juice can", Pizza = "leftover pizza";

        void Awake() { HoldSeconds = 1.8f; }

        enum Offer { None, Drink, Dinner }

        Offer Current
        {
            get
            {
                var gm = Core.GameManager.Instance;
                if (gm == null || !gm.IsTaskDone("talk") || !string.IsNullOrEmpty(gm.HeldItem)) return Offer.None;
                if (!gm.IsTaskDone("deliver")) return Offer.Drink;
                if (gm.IsTaskDone("dinner") && !gm.IsTaskDone("eat")) return Offer.Dinner;
                return Offer.None;
            }
        }

        public override string Prompt
        {
            get
            {
                var gm = Core.GameManager.Instance;
                if (gm == null) return "";
                if (holding) return "";
                if (!gm.IsTaskDone("talk")) return "Mr. IG's fridge. He labels things.";
                if (gm.HeldItem == Can) return "Hands full";
                if (!gm.IsTaskDone("deliver")) return "Grab a COLD Gamer-Juice  (hold [E])";
                // "dinner" = you've texted Mr. IG and he's replied; the pizza comes AFTER that exchange.
                if (!gm.IsTaskDone("dinner")) return "I'm not hungry. Yet.";
                if (!gm.IsTaskDone("eat"))
                    return gm.HeldItem == Pizza ? "You already have the pizza" : "Grab dinner (leftover pizza)  (hold [E])";
                return "The fridge hums quietly.";
            }
        }

        protected override bool CanStartHold(Player.PlayerInteraction player) => Current != Offer.None;

        protected override void OnHoldStart()
        {
            HoldLabel = Current == Offer.Dinner ? "Digging out the pizza..." : "Rummaging past the condiments...";
            Audio.ProceduralAudio.PlayFridgeDoor(transform.position);
        }

        protected override void OnHoldComplete()
        {
            var gm = Core.GameManager.Instance;
            if (gm == null) return;
            var o = Current;
            if (o == Offer.Drink)
            {
                gm.SetHeldItem(Can);
                gm.HUD?.QueueSubtitle("", "One Gamer-Juice. Cold enough to count as a personality.", 3f);
                gm.HUD?.SetObjective("\"Back upstairs before it gets warm.\"");
            }
            else if (o == Offer.Dinner)
            {
                gm.SetHeldItem(Pizza);
                gm.HUD?.QueueSubtitle("", "Leftover pepperoni. It has seen things. Let's not ask what.", 3f);
                gm.HUD?.SetObjective("\"Pizza. TV. Sofa. Now.\"");
            }
        }
    }

    /// <summary>
    /// The kitchen sink: three dirty plates. Hold [E] to scrub each one (water runs, the view settles on the
    /// sink, a grumble per plate). Let go and the plate stays half-done; the dishes task completes after #3.
    /// </summary>
    public class SinkWashInteractable : HoldInteractable
    {
        const int Plates = 3;
        int done;
        AudioSource water;
        static AudioClip waterClip;

        static readonly string[] PlateLines =
        {
            "(scrub scrub) This plate has seen things. Mostly cereal.",
            "Gamer-Juice residue. It's glue with a marketing budget.",
            "Last one. His dishes, my evening. A fair trade? ...No. Not really.",
        };

        void Awake() { EnsureMinReach(1.5f); HoldSeconds = 3.4f; }

        public override string Prompt
        {
            get
            {
                var gm = Core.GameManager.Instance;
                if (gm == null || holding) return "";
                if (gm.IsTaskDone("wash")) return "";
                if (!gm.IsTaskDone("eat")) return "The plates can wait until after dinner.";
                return "Scrub plate " + (done + 1) + " of " + Plates + "  (hold [E])";
            }
        }

        protected override bool CanStartHold(Player.PlayerInteraction player)
        {
            var gm = Core.GameManager.Instance;
            return gm != null && gm.IsTaskDone("eat") && !gm.IsTaskDone("wash");
        }

        protected override void OnHoldStart()
        {
            HoldLabel = "Scrubbing plate " + (done + 1) + " of " + Plates + "...  keep holding [E]";
            var gm = Core.GameManager.Instance;
            EnsureWater();
            if (water == null)
            {
                water = gameObject.AddComponent<AudioSource>();
                water.clip = waterClip; water.loop = true; water.playOnAwake = false;
                water.spatialBlend = 1f; water.minDistance = 1.5f; water.maxDistance = 8f; water.volume = 0.3f;
            }
            water.Play();
            if (gm != null && gm.Player != null)
            {
                anim = Player.ScrubAnimation.Begin(gm.Player.Camera);   // plate + sponge + bubbles in your hands
                gm.HUD?.SetCenterPrompt("");
                gm.PlayerLocked = true;
                var b = GetComponent<Collider>().bounds;
                gm.Player.StartCoroutine(gm.Player.FocusOn(new Vector3(b.center.x, 1.2f, b.center.z), 0.5f));
            }
        }

        Player.ScrubAnimation anim;

        protected override void OnHoldProgress(float t) { if (anim != null) anim.SetProgress(t); }

        void Release()
        {
            if (water != null && water.isPlaying) water.Stop();
            var gm = Core.GameManager.Instance;
            if (gm != null && gm.Player != null) Player.ScrubAnimation.End(gm.Player.Camera);
            anim = null;
            if (gm == null) return;
            gm.PlayerLocked = false;
            if (gm.Player != null) gm.Player.LookLocked = false;
        }

        protected override void OnHoldCancel()
        {
            Release();
            Core.GameManager.Instance?.HUD?.QueueSubtitle("", "(shakes off the suds) ...that plate isn't clean yet.", 2f);
        }

        protected override void OnHoldComplete()
        {
            Release();
            var gm = Core.GameManager.Instance;
            done++;
            gm?.HUD?.QueueSubtitle("", PlateLines[Mathf.Min(done, Plates) - 1], 3.2f);
            Audio.ProceduralAudio.PlayClick(transform.position);
            if (done < Plates) return;                       // next plate: hold again
            used = true;
            Audio.ProceduralAudio.PlayConfirmation(transform.position);
            NotifyTask();
            gm?.CompleteTask(TaskId);
        }

        static void EnsureWater()
        {
            if (waterClip != null) return;
            // Gentle and rhythmic instead of a constant hiss: a muffled tap trickle underneath, soft
            // "swish" strokes of the sponge 2.5 times a second, and the odd water drip. Heavily low-passed.
            const int rate = 22050; int n = rate * 2;
            var d = new float[n]; var rng = new System.Random(5);
            float a = 0f, a2 = 0f, b = 0f, b2 = 0f;
            var drips = new[] { 0.31f, 0.93f, 1.42f, 1.77f };
            for (int i = 0; i < n; i++)
            {
                float tt = i / (float)rate;
                float w = (float)(rng.NextDouble() * 2 - 1);
                a += (w - a) * 0.05f; a2 += (a - a2) * 0.2f;               // very dark trickle
                b += (w - b) * 0.13f; b2 += (b - b2) * 0.3f;               // soft swish
                float stroke = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(tt * 2f * Mathf.PI * 2.5f), 2.2f);   // 5 strokes per 2 s loop
                float s = a2 * 0.55f + b2 * stroke * 1.0f;
                foreach (var dt in drips)
                {
                    float x = tt - dt;
                    if (x >= 0f && x < 0.09f) s += Mathf.Sin(x * 2f * Mathf.PI * (900f - x * 2500f)) * Mathf.Exp(-x * 60f) * 0.22f;   // 'plink'
                }
                d[i] = s;
            }
            int fade = (int)(rate * 0.05f);
            for (int i = 0; i < fade; i++) d[i] = Mathf.Lerp(d[n - fade + i], d[i], i / (float)fade);
            float peak = 0.0001f; foreach (var x in d) peak = Mathf.Max(peak, Mathf.Abs(x));
            var t = new float[n - fade];
            for (int i = 0; i < t.Length; i++) t[i] = d[i] * (0.6f / peak);   // quiet
            waterClip = AudioClip.Create("sink_water", t.Length, 1, rate, false);
            waterClip.SetData(t, 0);
        }
    }

}
