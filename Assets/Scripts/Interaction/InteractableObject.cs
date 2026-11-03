using System;
using UnityEngine;

namespace ContentHouse.Interaction
{
    /// <summary>
    /// Base for every [E]-interactable object (spec: InteractableObject.cs
    /// supporting ToggleDoor / LockWindow / PickUpItem etc. via subclasses).
    /// </summary>
    public abstract class InteractableObject : MonoBehaviour
    {
        /// <summary>
        /// Static broadcast: any interactable firing. Delegate field (not an
        /// event) so subclasses can raise it directly.
        /// </summary>
        public static event System.Action<InteractableObject> AnyInteracted;

        protected static void RaiseInteracted(InteractableObject obj) => AnyInteracted?.Invoke(obj);

        public virtual string Prompt => "Interact  [E]";

        /// <summary>Optional story identifier ("Router", "PatioDoor", ...).</summary>
        public string TaskId = "";

        protected bool used;

        public virtual void Interact(Player.PlayerInteraction player)
        {
            if (used) return;
            AnyInteracted?.Invoke(this);
            NotifyTask();
        }

        protected void NotifyTask()
        {
            if (!string.IsNullOrEmpty(TaskId))
                Core.GameManager.Instance?.CompleteTask(TaskId);
        }

        public virtual void MarkUsed() => used = true;

        /// <summary>
        /// Grow this object's BoxCollider (around its centre) so it is at least minWorld metres in every
        /// direction. Thin invisible "pads" are easy to miss; a generous target is much friendlier to aim at.
        /// </summary>
        protected void EnsureMinReach(float minWorld)
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return;
            var s = transform.lossyScale;
            var size = box.size;
            if (Mathf.Abs(s.x) > 1e-4f) size.x = Mathf.Max(size.x, minWorld / Mathf.Abs(s.x));
            if (Mathf.Abs(s.y) > 1e-4f) size.y = Mathf.Max(size.y, minWorld / Mathf.Abs(s.y));
            if (Mathf.Abs(s.z) > 1e-4f) size.z = Mathf.Max(size.z, minWorld / Mathf.Abs(s.z));
            box.size = size;
        }
    }
}
