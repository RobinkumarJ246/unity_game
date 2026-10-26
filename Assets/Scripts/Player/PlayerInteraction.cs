using UnityEngine;

namespace ContentHouse.Player
{
    /// <summary>
    /// Raycast-based [E] interaction from the player camera.
    /// </summary>
    public class PlayerInteraction : MonoBehaviour
    {
        public PlayerController Player;
        public float InteractRange = 2.4f;

        Interaction.InteractableObject hovered;

        void Update()
        {
            var gm = Core.GameManager.Instance;
            if (gm == null || gm.PlayerLocked) { hovered = null; return; }
            if (gm.Phone != null && gm.Phone.IsOpen) { hovered = null; gm.HUD.SetCenterPrompt(""); return; }

            if (Player != null && Player.IsHiding)
            {
                if (gm.HUD != null) gm.HUD.SetCenterPrompt("[E]  Stay quiet... or leave");
                if (Input.GetKeyDown(KeyCode.E)) Player.ExitHiding();
                return;
            }

            var ray = new Ray(Player.Camera.transform.position, Player.Camera.transform.forward);
            RaycastHit hit;
            hovered = null;
            if (Physics.Raycast(ray, out hit, InteractRange))
                hovered = hit.collider.GetComponentInParent<Interaction.InteractableObject>();

            if (gm.HUD != null)
                gm.HUD.SetCenterPrompt(hovered != null ? hovered.Prompt : "");

            if (hovered != null && Input.GetKeyDown(KeyCode.E))
                hovered.Interact(this);
        }
    }
}
