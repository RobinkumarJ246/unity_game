using UnityEngine;
using ContentHouse.UI;

namespace ContentHouse.Interaction
{
    /// <summary>
    /// A bed (every bed in the house works). Hold [E] to lie down: your eyes slowly close.
    /// - Before everything is done it never lists what's left - the player just thinks out loud.
    /// - The FIRST real attempt is interrupted by the doorbell (Day1Delivery) at about two thirds of the way.
    /// - After the package is dealt with, holding closes your eyes completely and the day ends.
    /// </summary>
    public class BedSleepInteractable : HoldInteractable
    {
        static readonly string[] Needed = { "talk", "deliver", "pee", "dinnermsg", "manager", "dinner", "eat", "wash", "workmsg" };

        // what the player mutters about the first unfinished thing (a thought, not a checklist)
        static string Thought(string id)
        {
            switch (id)
            {
                case "talk": return "Mr. IG wanted me. I can't sleep yet.";
                case "deliver": return "That drink won't deliver itself.";
                case "pee": return "I really need the bathroom first.";
                case "dinnermsg": return "I should check if he's eating tonight.";
                case "manager": return "My salary. I won't sleep until I've chased it.";
                case "dinner": return "I'm waiting on his answer. Then food.";
                case "eat": return "I'm starving. Pizza, TV, then bed.";
                case "wash": return "The dishes are still in the sink. I can't sleep on that.";
                case "workmsg": return "My phone keeps buzzing. Better look.";
                default: return "Not yet.";
            }
        }

        static string FirstUnfinished(Core.GameManager gm)
        {
            foreach (var id in Needed) if (!gm.IsTaskDone(id)) return id;
            return null;
        }

        static Core.Day1Delivery Delivery => Core.Day1Delivery.Instance;
        static bool BellDone(Core.GameManager gm) => gm.IsTaskDone("doorbell");
        bool interrupted;

        void Awake() { HoldSeconds = 4f; HoldLabel = "Falling asleep..."; EnsureMinReach(0.6f); }

        public override string Prompt
        {
            get
            {
                var gm = Core.GameManager.Instance;
                if (gm == null || holding || gm.IsTaskDone("sleep")) return "";
                var left = FirstUnfinished(gm);
                if (left != null) return Thought(left);
                if (BellDone(gm) && !gm.IsTaskDone("pkgplaced"))
                {
                    if (Delivery != null && Delivery.BellPending) return "Someone's at the door. I can't just ignore that.";
                    return gm.HeldItem == Player.HeldItemView.Package ? "I can't sleep holding this thing." : "That package is still bugging me.";
                }
                return "Sleep.  (hold [E])";
            }
        }

        protected override bool CanStartHold(Player.PlayerInteraction player)
        {
            var gm = Core.GameManager.Instance;
            if (gm == null || gm.IsTaskDone("sleep") || FirstUnfinished(gm) != null) return false;
            return !BellDone(gm) || gm.IsTaskDone("pkgplaced");
        }

        protected override void OnHoldStart()
        {
            var gm = Core.GameManager.Instance;
            if (gm == null || gm.Player == null) return;
            interrupted = false;
            bool finalSleep = BellDone(gm);
            HoldSeconds = finalSleep ? 6.5f : 4.5f;        // the real sleep is slow
            HoldLabel = finalSleep ? "Closing your eyes..." : "Falling asleep...";
            gm.HUD?.SetCenterPrompt("");
            gm.PlayerLocked = true;
            var b = GetComponent<Collider>().bounds;
            gm.Player.StartCoroutine(gm.Player.FocusOn(new Vector3(b.center.x, b.max.y + 3f, b.center.z), HoldSeconds * 0.8f));
        }

        protected override void OnHoldProgress(float t)
        {
            var gm = Core.GameManager.Instance;
            bool finalSleep = gm != null && BellDone(gm);
            // eyes close with the ring (the real sleep: all the way)
            ScreenFx.Instance.SetEyelids(finalSleep ? t : t * 0.9f, snap: true);

            // first attempt: the doorbell cuts in just before you drop off
            if (!finalSleep && !interrupted && t >= 0.66f)
            {
                interrupted = true;
                holding = false; holdT = 0f; ProgressCircle.Hide();
                ScreenFx.Instance.SetEyelids(0f, snap: false, speed: 9f);   // eyes snap open
                gm.PlayerLocked = false;
                if (gm.Player != null) gm.Player.LookLocked = false;
                if (Delivery != null) Delivery.RingDoorbell();
            }
        }

        protected override void OnHoldCancel()
        {
            var gm = Core.GameManager.Instance;
            ScreenFx.Instance.SetEyelids(0f, snap: false, speed: 5f);       // let go: eyes open again
            if (gm == null) return;
            gm.PlayerLocked = false;
            if (gm.Player != null) gm.Player.LookLocked = false;
        }

        protected override void OnHoldComplete()
        {
            var gm = Core.GameManager.Instance;
            used = true;
            ScreenFx.Instance.SetEyelids(1f, snap: true);                   // eyes shut: stays black until the end card
            if (gm != null)
            {
                gm.PlayerLocked = false;
                if (gm.Player != null) gm.Player.LookLocked = false;
            }
            NotifyTask();
            gm?.CompleteTask(TaskId);   // the day script sees all tasks done and ends the demo
        }
    }
}
