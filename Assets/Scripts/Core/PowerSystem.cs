using System.Collections.Generic;
using UnityEngine;

namespace ContentHouse.Core
{
    /// <summary>
    /// Tracks every powered house light and can kill them all for the Act II
    /// blackout. Moonlight / battery lights stay on separately.
    /// </summary>
    public class PowerSystem : MonoBehaviour
    {
        public List<Light> HouseLights = new List<Light>();
        [Tooltip("Dim emergency/moon light that stays on after the blackout.")]
        public Light MoonLight;
        [Tooltip("Spotlight under Mr. IG's door - monitor glow, always on.")]
        public Light DoorGlow;

        public bool BlackoutActive { get; private set; }

        public void Blackout()
        {
            BlackoutActive = true;
            foreach (var l in HouseLights)
                if (l != null) l.enabled = false;
            if (MoonLight != null) MoonLight.enabled = true;
            if (DoorGlow != null) DoorGlow.enabled = true;
        }

        public void Restore()
        {
            BlackoutActive = false;
            foreach (var l in HouseLights)
                if (l != null) l.enabled = true;
            if (MoonLight != null) MoonLight.enabled = false;
        }
    }
}
