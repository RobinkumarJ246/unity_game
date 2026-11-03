using System;
using System.Collections.Generic;
using UnityEngine;

namespace ContentHouse.Core
{
    public class GameManager : MonoBehaviour
    {
        public enum GamePhase { Menu, Act1, Act2, Act3, Act4, Epilogue }

        public static GameManager Instance { get; private set; }

        [HideInInspector] public GamePhase Phase = GamePhase.Menu;

        // Wired by HouseBuilder / looked up lazily.
        [HideInInspector] public Player.PlayerController Player;
        [HideInInspector] public UI.HUDController HUD;
        [HideInInspector] public Phone.PhoneController Phone;
        [HideInInspector] public StoryManager Story;
        [HideInInspector] public Enemy.EnemyAI Enemy;
        [HideInInspector] public PowerSystem Power;
        [HideInInspector] public Audio.AmbienceController Ambience;

        /// <summary>Player movement/interaction locked (cutscenes, death).</summary>
        [HideInInspector] public bool PlayerLocked = true;

        /// <summary>Single-item carrying per spec ("one package / one drink").</summary>
        [HideInInspector] public string HeldItem = "";

        /// <summary>Checkpoint the player respawns at when caught.</summary>
        [HideInInspector] public Vector3 Checkpoint;
        [HideInInspector] public float CheckpointYaw = 0f;

        public event Action<string> TaskCompleted;

        readonly HashSet<string> tasks = new HashSet<string>();

        public bool IsTaskDone(string id) => tasks.Contains(id);

        /// <summary>Mark a task done WITHOUT its story reactions (used by the dev quick-start).</summary>
        public void MarkDoneSilently(string id) { tasks.Add(id); }

        public void CompleteTask(string id)
        {
            if (!tasks.Add(id)) return;
            TaskCompleted?.Invoke(id);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public void SetHeldItem(string item)
        {
            HeldItem = item;
            if (HUD != null) HUD.SetHeldItem(item);
            // show it in the player's hand
            if (Player != null) ContentHouse.Player.HeldItemView.Show(Player.Camera, item);
        }

        public void SetCheckpoint(Vector3 pos, float yaw)
        {
            Checkpoint = pos;
            CheckpointYaw = yaw;
        }

        public void OnPlayerCaught()
        {
            if (Phase == GamePhase.Epilogue) return;
            if (Story != null) StartCoroutine(Story.CaughtRoutine());
        }
    }
}
