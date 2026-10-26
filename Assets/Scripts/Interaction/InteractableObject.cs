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
    }
}
