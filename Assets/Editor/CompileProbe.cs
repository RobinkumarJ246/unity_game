using UnityEngine;
using UnityEditor;

namespace ContentHouseEditor
{
    public static class CompileProbe
    {
        [MenuItem("Content House/Probe")]
        public static void Probe()
        {
            // Each line tests one resolution style; compiler errors pinpoint the broken form.
            var a = typeof(ContentHouse.Core.GameManager);
            var b = typeof(ContentHouse.Interaction.DoorInteractable);
            var c = typeof(ContentHouse.Player.PlayerController);
            var d = typeof(ContentHouse.Enemy.EnemyAI);
            var e = typeof(ContentHouse.CCTV.CCTVCameraFeed);
            var f = typeof(ContentHouse.Mic.MicLoudnessDetection);
            var g = typeof(ContentHouse.Phone.PhoneController);
            var h = typeof(ContentHouse.UI.HUDController);
            var i = typeof(ContentHouse.Audio.AmbienceController);
            Debug.Log("probe ok " + a + b + c + d + e + f + g + h + i);
        }
    }
}
