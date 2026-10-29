using UnityEngine;

namespace ContentHouse.Player
{
    /// <summary>
    /// Raycast-based [E] interaction from the player camera.
    /// </summary>
    public class PlayerInteraction : MonoBehaviour
    {
        public PlayerController Player;
        public float InteractRange = 2.7f;

    Interaction.InteractableObject hovered;
    float lastHoverTime;
    string lastPrompt = "";

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
        {
            if (hovered != null)
            {
                lastPrompt = hovered.Prompt;
                lastHoverTime = Time.time;
                gm.HUD.SetCenterPrompt(lastPrompt);
            }
            else if (Time.time - lastHoverTime < 0.35f && lastPrompt.Length > 0)
            {
                // Sticky prompt: keep it visible a moment so it doesn't flicker.
                gm.HUD.SetCenterPrompt(lastPrompt);
            }
            else
            {
                gm.HUD.SetCenterPrompt("");
            }
        }

        // Interact slightly forgiving: press E while the prompt is showing.
        if (Input.GetKeyDown(KeyCode.E) && hovered != null)
            hovered.Interact(this);
    }
    }
}
