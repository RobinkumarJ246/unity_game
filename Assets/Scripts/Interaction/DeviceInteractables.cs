using System.Collections;
using UnityEngine;

namespace ContentHouse.Interaction
{
    /// <summary>Porch delivery box: grabbing it puts the 12-pack in hand.</summary>
    public class DeliveryBoxInteractable : InteractableObject
    {
        public string GivesItem = "Gamer-Juice 12-pack";

        public override string Prompt
        {
            get
            {
                var gm = Core.GameManager.Instance;
                if (gm != null && gm.IsTaskDone(TaskId)) return "";
                if (gm != null && !string.IsNullOrEmpty(gm.HeldItem)) return "Hands full";
                return "Take the Gamer-Juice box  [E]";
            }
        }

        public override void Interact(Player.PlayerInteraction player)
        {
            if (used) return;
            var gm = Core.GameManager.Instance;
            if (gm == null || !string.IsNullOrEmpty(gm.HeldItem)) return;
            used = true;
            gm.SetHeldItem(GivesItem);
            gameObject.SetActive(false);
            Audio.ProceduralAudio.PlayClick(transform.position);
            base.Interact(player);
        }
    }

    /// <summary>Basement Wi-Fi router: hold [E] briefly to reboot it.</summary>
    public class RouterInteractable : InteractableObject
    {
        public GameObject LedRed, LedGreen;
        public float HoldSeconds = 1.2f;

        bool holding;
        float t;

        public override string Prompt => IsDone ? "Router online" : "Reboot router  (hold [E])";
        public bool IsDone { get; private set; }

        void Update()
        {
            if (IsDone || !holding) { t = Mathf.Max(0, t - Time.deltaTime); return; }
            if (!Input.GetKey(KeyCode.E)) { holding = false; t = 0; return; }
            t += Time.deltaTime;
            if (t >= HoldSeconds) Finish();
        }

        public override void Interact(Player.PlayerInteraction player)
        {
            if (IsDone) return;
            holding = true;
            t = 0f;
        }

        void Finish()
        {
            IsDone = true;
            holding = false;
            if (LedRed != null) LedRed.SetActive(false);
            if (LedGreen != null) LedGreen.SetActive(true);
            Audio.ProceduralAudio.PlayClick(transform.position);
            if (Core.GameManager.Instance != null && Core.GameManager.Instance.HUD != null)
                Core.GameManager.Instance.HUD.QueueSubtitle("", "Router rebooted. Signal restored.", 2f);
            RaiseInteracted(this);
            NotifyTask();
        }
    }

    /// <summary>Basement breaker panel: three levers, all must be on.</summary>
    public class BreakerPanelInteractable : InteractableObject
    {
        public Transform[] Levers;     // rotate -35 = off, +35 = on
        public bool[] On;
        public bool AllOn { get; private set; }

        public override string Prompt => AllOn ? "Breakers on" : "Flip lever  [E]";

        void Start()
        {
            if (On == null || On.Length != Levers.Length)
                On = new bool[Levers != null ? Levers.Length : 0];
            for (int i = 0; i < Levers.Length; i++) SetLever(i, On[i]);
        }

        public override void Interact(Player.PlayerInteraction player)
        {
            if (AllOn) return;
            for (int i = 0; i < On.Length; i++)
            {
                if (!On[i]) { SetLever(i, true); break; }
            }
            Audio.ProceduralAudio.PlayLeverClunk(transform.position);
            AllOn = true;
            for (int i = 0; i < On.Length; i++) if (!On[i]) AllOn = false;
            if (AllOn)
            {
                if (Core.GameManager.Instance != null && Core.GameManager.Instance.HUD != null)
                    Core.GameManager.Instance.HUD.QueueSubtitle("", "All breakers flipped on.", 1.8f);
                RaiseInteracted(this);
                NotifyTask();
            }
        }

        void SetLever(int i, bool on)
        {
            On[i] = on;
            if (Levers != null && Levers[i] != null)
                Levers[i].localRotation = Quaternion.Euler(on ? 35f : -35f, 0f, 0f);
        }
    }

    /// <summary>Mr. IG's mini-fridge: stock it while carrying the drink box.</summary>
    public class FridgeInteractable : InteractableObject
    {
        public string RequiredItem = "Gamer-Juice 12-pack";
        public GameObject StockedCans;   // becomes visible once stocked

        public bool Stocked { get; private set; }

        public override string Prompt
        {
            get
            {
                if (Stocked) return "Mini-fridge full";
                var gm = Core.GameManager.Instance;
                if (gm != null && gm.HeldItem == RequiredItem) return "Stock mini-fridge  [E]";
                return "Empty mini-fridge (needs drinks)";
            }
        }

        public override void Interact(Player.PlayerInteraction player)
        {
            if (Stocked) return;
            var gm = Core.GameManager.Instance;
            if (gm == null || gm.HeldItem != RequiredItem) return;
            Stocked = true;
            gm.SetHeldItem("");
            if (StockedCans != null) StockedCans.SetActive(true);
            Audio.ProceduralAudio.PlayFridgeDoor(transform.position);
            if (gm.HUD != null) gm.HUD.QueueSubtitle("", "Mini-fridge stocked with Gamer-Juice.", 2f);
            base.Interact(player);
        }
    }

    /// <summary>
    /// Backup stream laptop (living room): Mic Waveform Check chore - press
    /// [E] exactly when the waveform marker is inside the green band, 3 times.
    /// </summary>
    public class LaptopCalibrationInteractable : InteractableObject
    {
        public int HitsNeeded = 3;
        public bool Calibrated { get; private set; }
        public float MarkerPhase { get; private set; }   // 0..1 moving marker
        public float GreenCenter = 0.5f, GreenWidth = 0.22f;
        public bool InMinigame { get; private set; }

        int hits;

        public override string Prompt => Calibrated ? "Waveform calibrated"
            : InMinigame ? "Press [E] in the green band!"
            : "Calibrate waveform  [E]";

        void Update()
        {
            if (!InMinigame || Calibrated) return;
            MarkerPhase = (MarkerPhase + Time.deltaTime * 0.85f) % 1f;
            if (Input.GetKeyDown(KeyCode.E))
            {
                float d = Mathf.Abs(MarkerPhase - GreenCenter);
                if (d < GreenWidth * 0.5f)
                {
                    hits++;
                    Audio.ProceduralAudio.PlayClick(transform.position);
                    if (hits >= HitsNeeded) Finish();
                }
                else
                {
                    hits = Mathf.Max(0, hits - 1);
                    Audio.ProceduralAudio.PlayBuzzError(transform.position);
                }
            }
        }

        public override void Interact(Player.PlayerInteraction player)
        {
            if (Calibrated) return;
            if (!InMinigame)
            {
                InMinigame = true;
                return;
            }
            // E presses are consumed in Update while in the minigame.
        }

        void Finish()
        {
            Calibrated = true;
            InMinigame = false;
            if (Core.GameManager.Instance != null && Core.GameManager.Instance.HUD != null)
                Core.GameManager.Instance.HUD.QueueSubtitle("", "Audio filters calibrated. Stream sounds crisp.", 2f);
            RaiseInteracted(this);
            NotifyTask();
        }
    }

}

