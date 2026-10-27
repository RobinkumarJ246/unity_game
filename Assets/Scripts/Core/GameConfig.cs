using UnityEngine;

namespace ContentHouse.Core
{
    /// <summary>
    /// Central configuration for names, tuning values and player prefs keys.
    /// Everything here is a placeholder kept changeable post-build: edit this
    /// file (or swap PlayerPrefs) to rename the game, characters or rebalance.
    /// </summary>
    public static class GameConfig
    {
        // ---- Names / branding (change freely) ----
        public const string GameTitle = "CONTENT HOUSE";
        public const string GameSubtitle = "A 24-Hour Subathon Gone Wrong";
        public const string DefaultPlayerName = "Steve";
        public const string StreamerName = "Mr. IG";
        public const string FriendName = "Mark";
        public const string IntruderName = "Dennis Vance";

        // ---- PlayerPrefs keys ----
        public const string PlayerNameKey = "PlayerName";
        public const string UseMicKey = "UseMicrophone";

        public static string PlayerName
        {
            get
            {
                var n = PlayerPrefs.GetString(PlayerNameKey, "");
                return string.IsNullOrEmpty(n) ? DefaultPlayerName : n;
            }
        }

        public static bool UseMic => PlayerPrefs.GetInt(UseMicKey, 1) == 1;

        // ---- Enemy AI tuning (spec: patrol 1.8, investigate 2.5, pursue 4.2 m/s) ----
        public const float PatrolSpeed = 1.8f;
        public const float InvestigateSpeed = 2.5f;
        public const float PursueSpeed = 4.2f;
        public const float SightRange = 14f;
        public const float SightHalfAngle = 35f;          // 70 degree cone
        public const float SightBufferSeconds = 0.5f;     // LOS reaction delay
        public const float CatchDistance = 1.35f;
        public const float LostPlayerSeconds = 5f;
        public const float InvestigateScanSeconds = 5f;

        // ---- Mic noise detection (spec: RMS threshold 0.15) ----
        public const float MicYellowLevel = 0.06f;
        public const float MicRedLevel = 0.15f;
        public const float YellowNoiseRadius = 4f;
        public const float RedNoiseRadius = 12f;

        // ---- Player movement ----
        public const float WalkSpeed = 2.6f;
        public const float RunSpeed = 5.4f;     // enemy pursues at 4.2 - you CAN outrun him
        public const float CrouchSpeed = 1.4f;
        public const float MouseSensitivity = 2.2f;

        // ---- Phone battery ----
        public const float PhoneBatteryStart = 78f;
        public const float PhonePassiveDrainPerMinute = 1.4f;   // Acts I-II
        public const float FlashlightDrainPerMinute = 4.0f;
    }
}
