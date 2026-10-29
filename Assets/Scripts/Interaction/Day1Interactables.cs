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

        public override string Prompt => GM_IsTaskDone(TaskId)
            ? ""
            : (IsPlayerNearDoor ? "Talk to " + ShortName + "  [E]" : (UnavailableLine.Length > 0 ? "" : "Talk to " + ShortName + "  [E]"));

        bool IsPlayerNearDoor => true;   // placed in the world; always offered until done

        string ShortName => string.IsNullOrEmpty(SpeakerName)
            ? Core.GameConfig.StreamerName : SpeakerName;

        static bool GM_IsTaskDone(string id)
        {
            var gm = Core.GameManager.Instance;
            return gm != null && !string.IsNullOrEmpty(id) && gm.IsTaskDone(id);
        }

        public override void Interact(Player.PlayerInteraction player)
        {
            if (used || GM_IsTaskDone(TaskId)) return;
            used = true;
            var gm = Core.GameManager.Instance;
            if (gm != null) gm.StartCoroutine(PlayThenTask());
        }

        IEnumerator PlayThenTask()
        {
            var dialogue = Core.GameManager.Instance != null
                ? UI.DialogueScreen.Instance : null;
            if (dialogue != null) yield return StartCoroutine(dialogue.Play(DialogueId, SpeakerName));
            NotifyTask();
            Core.GameManager.Instance?.CompleteTask(TaskId);
        }
    }

    /// <summary>The kitchen fridge: first the celebratory drink, later dinner.</summary>
    public class KitchenFridgeInteractable : InteractableObject
    {
        public override string Prompt
        {
            get
            {
                var gm = Core.GameManager.Instance;
                if (gm == null) return "";
                if (!gm.IsTaskDone("talk")) return "Mr. IG's fridge (better ask first)";
                if (gm.HeldItem == "Gamer-Juice can") return "Hands full";
                if (!gm.IsTaskDone("deliver")) return "Grab a COLD Gamer-Juice  [E]";
                if (!gm.IsTaskDone("dinner")) return "Grab dinner (leftover pizza)  [E]";
                if (!gm.IsTaskDone("eat")) return "You already have the pizza";
                return "The fridge hums quietly.";
            }
        }

        public override void Interact(Player.PlayerInteraction player)
        {
            var gm = Core.GameManager.Instance;
            if (gm == null) return;
            if (!gm.IsTaskDone("talk")) return;
            if (!gm.IsTaskDone("deliver") && string.IsNullOrEmpty(gm.HeldItem))
            {
                gm.SetHeldItem("Gamer-Juice can");
                Audio.ProceduralAudio.PlayFridgeDoor(transform.position);
                gm.HUD?.QueueSubtitle("", "One ice-cold Gamer-Juice for the soon-to-be millionaire.", 2.5f);
                return;
            }
            if (gm.IsTaskDone("deliver") && !gm.IsTaskDone("dinner") && string.IsNullOrEmpty(gm.HeldItem))
            {
                gm.SetHeldItem("leftover pizza");
                Audio.ProceduralAudio.PlayFridgeDoor(transform.position);
                gm.HUD?.QueueSubtitle("", "Leftover pizza. Fine. He's the one missing dinner.", 2.5f);
                return;
            }
        }
    }

    /// <summary>The living room TV: eat dinner in front of late-night television.</summary>
    public class TvEatInteractable : InteractableObject
    {
        public override string Prompt
        {
            get
            {
                var gm = Core.GameManager.Instance;
                if (gm == null) return "";
                if (gm.IsTaskDone("eat")) return "";
                if (gm.HeldItem == "leftover pizza") return "Eat while watching TV  [E]";
                return "Nothing good is on at this hour.";
            }
        }

        public override void Interact(Player.PlayerInteraction player)
        {
            var gm = Core.GameManager.Instance;
            if (gm == null || gm.HeldItem != "leftover pizza") return;
            used = true;
            gm.SetHeldItem("");
            if (gm.HUD != null)
            {
                gm.HUD.QueueSubtitle("", "(cold pizza, some late-night talk show, rain on the window) ...this is fine.", 3.5f);
            }
            Audio.ProceduralAudio.PlayConfirmation(transform.position);
            NotifyTask();
            gm.CompleteTask(TaskId);
        }
    }

    /// <summary>The kitchen sink: wash up after dinner.</summary>
    public class SinkWashInteractable : InteractableObject
    {
        public override string Prompt
        {
            get
            {
                var gm = Core.GameManager.Instance;
                if (gm == null) return "";
                if (gm.IsTaskDone("wash")) return "";
                if (!gm.IsTaskDone("eat")) return "The plates can wait until after dinner.";
                return "Wash the plates  [E]";
            }
        }

        public override void Interact(Player.PlayerInteraction player)
        {
            var gm = Core.GameManager.Instance;
            if (gm == null || !gm.IsTaskDone("eat") || gm.IsTaskDone("wash")) return;
            used = true;
            if (gm.HUD != null) gm.HUD.QueueSubtitle("", "(scrub scrub) ...his dishes, my evening. Story of my life.", 3f);
            Audio.ProceduralAudio.PlayClick(transform.position);
            NotifyTask();
            gm.CompleteTask(TaskId);
        }
    }

    /// <summary>The player's bed: ends the day - only when everything else is done.</summary>
    public class BedSleepInteractable : InteractableObject
    {
        public override string Prompt
        {
            get
            {
                var gm = Core.GameManager.Instance;
                if (gm == null) return "";
                if (gm.IsTaskDone("sleep")) return "";
                if (ChoresLeft(gm) > 0) return "Not sleepy yet - a few things left to do";
                return "Sleep  [E]";
            }
        }

        static int ChoresLeft(Core.GameManager gm)
        {
            int left = 0;
            foreach (var id in new[] { "talk", "deliver", "dinner", "eat", "wash" })
                if (!gm.IsTaskDone(id)) left++;
            return left;
        }

        public override void Interact(Player.PlayerInteraction player)
        {
            var gm = Core.GameManager.Instance;
            if (gm == null || ChoresLeft(gm) > 0 || gm.IsTaskDone("sleep")) return;
            used = true;
            NotifyTask();
            gm.CompleteTask(TaskId);   // the day script sees all tasks done and fades to the next day
        }
    }
}
