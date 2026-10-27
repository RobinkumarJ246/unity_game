using UnityEngine;

namespace ContentHouse.Interaction
{
    /// <summary>
    /// Closet or under-bed hiding spot. Interact to climb in; mic noise
    /// detection becomes live while inside; [E] (via PlayerInteraction) exits.
    /// </summary>
    public class HideSpot : InteractableObject
    {
        public enum Kind { Closet, UnderBed }
        public Kind SpotKind;

        /// <summary>Transform the player teleports to while hiding (inside).</summary>
        public Transform StandAnchor;
        /// <summary>Transform the player returns to when leaving.</summary>
        public Transform ExitAnchor;
        public DoorInteractable ClosetDoor;
        [Tooltip("Shown in the objective when hiding matters.")]
        public string SpotName = "hallway closet";

        public bool PlayerInside { get; private set; }

        /// <summary>Hiding is only offered while the intruder is in the house.</summary>
        bool HidingMatters
        {
            get
            {
                var gm = Core.GameManager.Instance;
                return gm != null &&
                    (gm.Phase == Core.GameManager.GamePhase.Act3 || gm.Phase == Core.GameManager.GamePhase.Act4);
            }
        }

        public override string Prompt
        {
            get
            {
                if (PlayerInside || !HidingMatters) return "";
                return SpotKind == Kind.Closet
                    ? $"Hide in the {SpotName}  [E]"
                    : "Hide under the bed  [E]";
            }
        }

        public override void Interact(Player.PlayerInteraction player)
        {
            if (PlayerInside) return;
            if (!HidingMatters)
            {
                if (Core.GameManager.Instance != null && Core.GameManager.Instance.HUD != null)
                    Core.GameManager.Instance.HUD.QueueSubtitle("", "No reason to hide... yet.", 1.8f);
                return;
            }
            var pc = player.Player;
            if (pc == null) return;
            PlayerInside = true;
            pc.EnterHiding(this);
            // Silent: the door must NEVER alert the enemy, and it stays ajar
            // so the player can peek through the gap while hiding.
            if (ClosetDoor != null) ClosetDoor.SetAjar(14f);
            if (Core.GameManager.Instance != null && Core.GameManager.Instance.HUD != null)
                Core.GameManager.Instance.HUD.ShowNoiseMeter(true);
            RaiseInteracted(this);
            NotifyTask();
        }

        public void OnPlayerExited()
        {
            PlayerInside = false;
            if (ClosetDoor != null) ClosetDoor.SetOpenSilent(true);
            if (Core.GameManager.Instance != null && Core.GameManager.Instance.HUD != null)
                Core.GameManager.Instance.HUD.ShowNoiseMeter(false);
        }

        /// <summary>Story convenience: hide the player programmatically.</summary>
        public void ForceHide(Player.PlayerController pc)
        {
            if (PlayerInside) return;
            PlayerInside = true;
            pc.EnterHiding(this);
            if (ClosetDoor != null) ClosetDoor.SetAjar(14f);
            if (Core.GameManager.Instance != null && Core.GameManager.Instance.HUD != null)
                Core.GameManager.Instance.HUD.ShowNoiseMeter(true);
        }
    }
}

