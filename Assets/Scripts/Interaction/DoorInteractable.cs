using UnityEngine;

namespace ContentHouse.Interaction
{
    /// <summary>
    /// Hinged door. The pivot (hinge) child rotates; the visible door hangs
    /// off it. Supports locking, enemy force-open with a slow creak, and the
    /// Act III slam.
    /// </summary>
    public class DoorInteractable : InteractableObject
    {
        public Transform Hinge;
        public float OpenAngle = 95f;
        public float ClosedAngle = 0f;
        public float LerpSpeed = 3f;
        public bool Locked;
        public string LockedMessage = "It's locked.";

        public bool IsOpen { get; private set; }

        float target;
        float t;

        void OnEnable() { target = ClosedAngle; t = 0f; }

        /// <summary>Optional: carrying this target's item turns the door into its hand-over point
        /// (Mr. IG's door: leave the drink and knock).</summary>
        public DoorDeliverInteractable DeliverTarget;

        bool CanDeliver
        {
            get
            {
                if (DeliverTarget == null || DeliverTarget.Delivered || Core.GameConfig.HandDrinkToNpc) return false;
                var gm = Core.GameManager.Instance;
                return gm != null && gm.HeldItem == DeliverTarget.RequiredItem;
            }
        }

        /// <summary>Story hooks: return true to take over the interaction / return text to replace the prompt.</summary>
        public System.Func<DoorInteractable, bool> InteractIntercept;
        public System.Func<string> PromptOverride;

        public override string Prompt =>
            (PromptOverride != null && PromptOverride() != null) ? PromptOverride() :
            CanDeliver ? "Leave the drink & knock  [E]" :
            Locked ? "Locked  [E]" :
            (IsOpen ? "Close door  [E]" : "Open door  [E]");

        public override void Interact(Player.PlayerInteraction player)
        {
            if (InteractIntercept != null && InteractIntercept(this)) return;
            if (CanDeliver) { DeliverTarget.Interact(player); return; }
            if (Locked)
            {
                if (Core.GameManager.Instance != null && Core.GameManager.Instance.HUD != null)
                    Core.GameManager.Instance.HUD.QueueSubtitle("", LockedMessage, 1.6f);
                Audio.ProceduralAudio.PlayDoorRattle(transform.position);
                return;
            }
            SetOpen(!IsOpen, fast: false);
            AnyInteractedSafe();
        }

        void AnyInteractedSafe()
        {
            // Non-consuming interact: story still hears about it.
            RaiseInteracted(this);
            NotifyTask();
        }

        public void SetOpen(bool open, bool fast)
        {
            if (Locked && open) { return; }
            IsOpen = open;
            target = open ? OpenAngle : ClosedAngle;
            t = 0f;
            if (fast) Snap();
            Audio.ProceduralAudio.PlayDoorCreak(transform.position);
            Core.NoiseSystem.ReportNoise(transform.position, 4f);
        }

        public void Slam()
        {
            IsOpen = false;
            target = ClosedAngle;
            t = 0f;
            Snap();
            Audio.ProceduralAudio.PlayDoorSlam(transform.position);
            Core.NoiseSystem.ReportNoise(transform.position, 7f);
        }

        /// <summary>Enemy pushes the door open slowly, with a long creak.</summary>
        public void ForceOpenForEnemy()
        {
            if (IsOpen) return;
            Locked = false;
            IsOpen = true;
            target = OpenAngle;
            t = -0.6f; // extra slow start
            Audio.ProceduralAudio.PlayDoorCreakLong(transform.position);
        }

        /// <summary>Door is being battered - violent jitter on the hinge.</summary>
        public void PanicShake(float seconds)
        {
            StartCoroutine(ShakeRoutine(seconds));
        }

        System.Collections.IEnumerator ShakeRoutine(float seconds)
        {
            float baseY = Current;
            float t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                if (Hinge != null)
                    Hinge.localRotation = Quaternion.Euler(0f,
                        baseY + Mathf.Sin(t * 34f) * 2.4f * (1f - t / seconds), 0f);
                yield return null;
            }
            if (Hinge != null) Hinge.localRotation = Quaternion.Euler(0f, baseY, 0f);
        }

        /// <summary>Hide-spot door move: absolutely silent - never alerts the enemy.</summary>
        public void SetOpenSilent(bool open)
        {
            IsOpen = open;
            target = open ? OpenAngle : ClosedAngle;
            t = 1f;
            Snap();
        }

        /// <summary>Leave the door ajar (peek gap) while hiding - silent.</summary>
        public void SetAjar(float angle = 14f)
        {
            IsOpen = false;
            target = angle;
            t = 1f;
            Snap();
        }

        /// <summary>Editor/bootstrap: snap closed with no sound or noise events.</summary>
        public void ForceClosedState()
        {
            IsOpen = false;
            target = ClosedAngle;
            t = 1f;
            Snap();
        }

        /// <summary>Editor/bootstrap: snap open with no sound or noise events (navmesh bake).</summary>
        public void ForceOpenState()
        {
            IsOpen = true;
            target = OpenAngle;
            t = 1f;
            Snap();
        }

        public void Unlock()
        {
            Locked = false;
        }

        void Update()
        {
            if (Hinge == null) return;
            if (t >= 1f) return;
            t = Mathf.Min(t + Time.deltaTime * LerpSpeed * 0.5f, 1f);
            float eased = t < 0 ? 0f : t * t * (3f - 2f * t);
            Hinge.localRotation = Quaternion.Euler(0f, Mathf.LerpAngle(Current, target, eased), 0f);
        }

        float Current => Hinge.localRotation.eulerAngles.y > 180f
            ? Hinge.localRotation.eulerAngles.y - 360f
            : Hinge.localRotation.eulerAngles.y;

        void Snap()
        {
            if (Hinge != null) Hinge.localRotation = Quaternion.Euler(0f, target, 0f);
        }
    }
}

