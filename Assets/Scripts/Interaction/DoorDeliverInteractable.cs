namespace ContentHouse.Interaction
{
    /// <summary>Mr. IG's bedroom door knock - delivers the energy drink.</summary>
    public class DoorDeliverInteractable : InteractableObject
    {
        public string RequiredItem = "Gamer-Juice can";
        public bool Delivered { get; private set; }

        public override string Prompt
        {
            get
            {
                // Day-1: the drink is handed to Mr. IG himself (TalkInteractable). The door plate stays silent.
                if (Delivered || Core.GameConfig.HandDrinkToNpc) return "";
                var gm = Core.GameManager.Instance;
                if (gm != null && gm.HeldItem == RequiredItem) return "Leave the drink & knock  [E]";
                return "Mr. IG's door (bring him a drink)";
            }
        }

        public override void Interact(Player.PlayerInteraction player)
        {
            if (Delivered || Core.GameConfig.HandDrinkToNpc) return;
            var gm = Core.GameManager.Instance;
            if (gm == null || gm.HeldItem != RequiredItem) return;
            Delivered = true;
            gm.SetHeldItem("");
            Audio.ProceduralAudio.PlayKnock(transform.position, 3);
            base.Interact(player);
        }
    }
}
