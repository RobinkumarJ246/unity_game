using UnityEngine;

namespace ContentHouse.Player
{
    /// <summary>
    /// Phone flashlight: a spotlight parented to the player camera, toggled
    /// through the phone's Flashlight app. Dies with the phone battery.
    /// </summary>
    public class FlashlightController : MonoBehaviour
    {
        public Light Spot;
        public bool IsOn { get; private set; }

        public bool CanUse
        {
            get
            {
                var phone = Core.GameManager.Instance != null ? Core.GameManager.Instance.Phone : null;
                return phone == null || phone.BatteryAlive;
            }
        }

        public void SetOn(bool on)
        {
            IsOn = on && CanUse;
            if (Spot != null) Spot.enabled = IsOn;
        }

        public void Toggle() => SetOn(!IsOn);

        /// <summary>Phone died: kill the light no matter what.</summary>
        public void ForceOff()
        {
            IsOn = false;
            if (Spot != null) Spot.enabled = false;
        }
    }
}
