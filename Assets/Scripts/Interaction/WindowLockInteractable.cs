using UnityEngine;

namespace ContentHouse.Interaction
{
    /// <summary>
    /// A window: lockable for the Act I checklist, can be shattered by the
    /// intruder (Act II dining room) or smashed open in the escape QTE.
    /// </summary>
    public class WindowLockInteractable : InteractableObject
    {
        public GameObject GlassPane;      // collider + renderer, disabled when broken
        public bool StartsLocked;
        public bool IsLocked { get; private set; }
        public bool IsBroken { get; private set; }

        public override string Prompt
        {
            get
            {
                if (IsBroken) return "";
                if (IsLocked) return "Window locked";
                return "Lock window  [E]";
            }
        }

        void Start() { IsLocked = StartsLocked; }

        public override void Interact(Player.PlayerInteraction player)
        {
            if (IsBroken || IsLocked) return;
            IsLocked = true;
            Audio.ProceduralAudio.PlayLockClick(transform.position);
            if (Core.GameManager.Instance != null && Core.GameManager.Instance.HUD != null)
                Core.GameManager.Instance.HUD.QueueSubtitle("", "Window locked.", 1.4f);
            base.Interact(player);
        }

        /// <summary>False alarm branch bang - tree branch against the glass.</summary>
        public void Bang()
        {
            if (IsBroken) return;
            Audio.ProceduralAudio.PlayBang(transform.position, 0.9f);
            Core.NoiseSystem.ReportNoise(transform.position, 10f);
        }

        public void Shatter()
        {
            if (IsBroken) return;
            IsBroken = true;
            if (GlassPane != null) GlassPane.SetActive(false);
            Audio.ProceduralAudio.PlayGlassShatter(transform.position);
            Core.NoiseSystem.ReportNoise(transform.position, 14f);
        }
    }
}
