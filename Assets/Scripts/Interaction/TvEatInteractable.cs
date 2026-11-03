using UnityEngine;

namespace ContentHouse.Interaction
{
    /// <summary>
    /// The living room TV: eat dinner in front of late-night television. Hold [E] for each slice: the pizza is
    /// slowly brought up to your mouth while the ring fills, then that slice disappears and the pizza comes
    /// back down. Let go early and nothing is eaten. When the last slice is gone the "eat" task completes.
    /// The first bite also switches the TV on.
    /// </summary>
    public class TvEatInteractable : HoldInteractable
    {
        void Awake()
        {
            EnsureMinReach(1.8f);   // easy to aim at from the sofa side
            HoldSeconds = 2.4f;     // one slice
            HoldLabel = "Taking a bite...  keep holding [E]";
        }

        static readonly string[] BiteLines =
        {
            "Cold pizza. Still better than anything I could cook.",
            "(chewing) The TV is just a black screen with subtitles. Who left this tape in the player?",
            "Pepperoni. The one thing everybody agrees on.",
            "A man named Thomas keeps saying he's 'never seen anything like it'. Same, Thomas.",
            "(chewing quietly) The house is very quiet for a place that was full of noises an hour ago.",
            "Yesterday's me did something right. Rare.",
            "...I'm fully invested in a black screen. Is this what being tired is?",
        };

        Player.HeldItemView View
        {
            get
            {
                var gm = Core.GameManager.Instance;
                return gm != null && gm.Player != null ? Player.HeldItemView.Of(gm.Player.Camera) : null;
            }
        }

        public override string Prompt
        {
            get
            {
                var gm = Core.GameManager.Instance;
                if (gm == null || holding) return "";
                if (gm.IsTaskDone("eat")) return "";
                if (gm.HeldItem == Player.HeldItemView.Pizza)
                {
                    var v = View;
                    int left = v != null ? v.SlicesLeft : 0;
                    int total = v != null ? v.SliceTotal : 0;
                    return total > 0 && left < total
                        ? "Eat the next slice  (hold [E])   " + left + " of " + total + " left"
                        : "Eat while watching TV  (hold [E] for each slice)";
                }
                return "Nothing good is on at this hour.";
            }
        }

        protected override bool CanStartHold(Player.PlayerInteraction player)
        {
            var gm = Core.GameManager.Instance;
            var v = View;
            return gm != null && gm.HeldItem == Player.HeldItemView.Pizza && !gm.IsTaskDone("eat") && v != null && v.SlicesLeft > 0;
        }

        protected override void OnHoldStart()
        {
            if (TvVideo.Instance != null) TvVideo.Instance.TurnOn();   // the first bite switches the TV on
        }

        protected override void OnHoldProgress(float t)
        {
            var v = View;
            if (v != null) v.SetBiteProgress(t);   // pizza travels slowly toward your mouth
        }

        protected override void OnHoldCancel()
        {
            var v = View;
            if (v != null) v.CancelBite();
        }

        protected override void OnHoldComplete()
        {
            var gm = Core.GameManager.Instance;
            var v = View;
            if (gm == null || v == null) return;
            int left = v.FinishBite();
            if (left > 0)
            {
                if (Random.value < 0.6f && gm.HUD != null)
                    gm.HUD.QueueSubtitle("", BiteLines[Random.Range(0, BiteLines.Length)], 2.8f);
                return;
            }
            // last slice gone
            used = true;
            gm.SetHeldItem("");
            gm.HUD?.QueueSubtitle("", "(sets the box down) ...okay. That was dinner. Dishes next, because adulthood.", 3.4f);
            Audio.ProceduralAudio.PlayConfirmation(transform.position);
            NotifyTask();
            gm.CompleteTask(TaskId);
        }
    }
}
