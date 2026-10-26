using UnityEngine;

namespace ContentHouse.Interaction
{
    /// <summary>
    /// Single-item pickup (spec: one item carried at a time).
    /// </summary>
    public class PickupItem : InteractableObject
    {
        public string ItemName = "Gamer-Juice can";
        [Tooltip("When true the item is consumed immediately (delivery box).")]
        public bool ConsumeInstantly;
        [Tooltip("Message shown when picked up.")]
        public string PickUpLine = "";

        public override string Prompt => ConsumeInstantly
            ? $"Take {ItemName}  [E]"
            : (Core.GameManager.Instance != null && !string.IsNullOrEmpty(Core.GameManager.Instance.HeldItem)
                ? $"Swap for {ItemName}  [E]"
                : $"Pick up {ItemName}  [E]");

        public override void Interact(Player.PlayerInteraction player)
        {
            if (used) return;
            used = true;
            if (ConsumeInstantly)
            {
                // Delivery box etc: adds to story state, stays in world.
                base.Interact(player);
                return;
            }

            Core.GameManager.Instance.SetHeldItem(ItemName);
            gameObject.SetActive(false);
            if (!string.IsNullOrEmpty(PickUpLine) && Core.GameManager.Instance.HUD != null)
                Core.GameManager.Instance.HUD.QueueSubtitle("", PickUpLine, 2f);
            base.Interact(player);
        }

        public void SetVisible(bool visible) => gameObject.SetActive(visible);
    }
}
