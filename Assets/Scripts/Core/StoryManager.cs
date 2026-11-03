using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ContentHouse.Core
{
    /// <summary>
    /// Act-by-act narrative sequencer (spec section 5). Drives objectives,
    /// phone messages, the blackout, the intruder, the double-twist cutscene,
    /// the QTE escape and the epilogue.
    /// </summary>
    public class StoryManager : MonoBehaviour
    {
        public GameManager GM;
        public Player.PlayerController Player;
        public UI.HUDController HUD;
        public Phone.PhoneController Phone;
        public Audio.AmbienceController Ambience;
        public PowerSystem Power;
        public Enemy.EnemyAI Enemy;
        public UI.QTEController QTE;
        public UI.EpilogueUI Epilogue;
        public CCTV.CCTVCameraFeed HallCam;
        public Interaction.DoorInteractable MrIgDoor;
        public Interaction.HideSpot HallCloset;
        public Interaction.WindowLockInteractable KitchenWindow;
        public Interaction.WindowLockInteractable DiningWindow;
        public Interaction.WindowLockInteractable EscapeWindow;
        public Interaction.WindowLockInteractable LivingRoomWindow;
        public Interaction.WindowLockInteractable EntryWindow;
        public Interaction.DoorInteractable PatioDoor;
        public Interaction.PickupItem DrinkCanPickup;
        public Interaction.DeliveryBoxInteractable DeliveryBox;
        public Transform LivingRoomCenter;
        public Transform KitchenCenter;
        public Transform EnemySpawn;
        public Transform ClosetGuardPos;     // where the intruder stands outside the closet
        public Transform BedroomCenter;
        public Transform BackyardLanding;
        public GameObject DeskProp;
        public Transform DeskBlockingPos;
        public GameObject IntruderKnife;

        string P => GameConfig.PlayerName;
        string IG => GameConfig.StreamerName;

        bool act1DialogueDone;
        bool blackoutStarted;
        bool kitchenBangDone;
        bool glassShattered;
        bool act3HideEventDone;
        bool doorUnlocked;
        bool act4Started;
        bool act2KitchenHint;
        float act2Elapsed;

        /// <summary>Set true when the stealth phase completes (bedroom reached).</summary>
        public bool StealthDone { get; private set; }

        /// <summary>True when StoryDirector drives the flow (days.txt present).</summary>
        public bool DirectorDriven { get; set; }

        // =====================================================================
        // Director entry points - each maps to a days.txt @event.
        // =====================================================================

        public IEnumerator RunBlackout() { yield return StartCoroutine(Act2Blackout()); }

        public IEnumerator RunKitchenBang() { yield return StartCoroutine(KitchenFalseAlarm()); }

        public IEnumerator RunGlassIntrusion() { yield return StartCoroutine(GlassIntrusion()); }

        public IEnumerator RunStealthPhase()
        {
            GM.Phase = GameManager.GamePhase.Act3;
            HUD.SetObjectiveLabel("12:19 AM - HE'S INSIDE");
            HUD.SetObjective("Get upstairs to " + IG + "'s room. Stay LOW. Stay QUIET. [C] to crouch.");
            while (!StealthDone) yield return null;
        }

        public IEnumerator RunClimax()
        {
            if (!act4Started) yield return StartCoroutine(Act4Climax());
        }
        readonly HashSet<string> required = new HashSet<string>
        { "box", "fridge", "router", "breakers", "patio", "window1", "window2", "laptop", "deliver" };

        void Start()
        {
            if (GM == null) GM = GameManager.Instance;
            GM.TaskCompleted += OnTaskCompleted;
            Interaction.InteractableObject.AnyInteracted += OnAnyInteracted;
            if (Phone != null) Phone.CallRequested += OnCallRequested;
            if (GetComponent<Day1Delivery>() == null) gameObject.AddComponent<Day1Delivery>();   // the doorbell / package beat
        }

        void OnDestroy()
        {
            if (GM != null) GM.TaskCompleted -= OnTaskCompleted;
            Interaction.InteractableObject.AnyInteracted -= OnAnyInteracted;
            if (Phone != null) Phone.CallRequested -= OnCallRequested;
        }

        // ---- Day 1 phone chain: text IG -> call Manager -> IG replies -> answer him -> pizza ----

        void OnCallRequested(string who)
        {
            if (who == "Manager")
            {
                if (GM.IsTaskDone("manager")) { Phone.ShowToast("You've heard enough from him for one night."); return; }
                if (!GM.IsTaskDone("dinnermsg")) { Phone.ShowToast("Not yet - text " + IG + " about dinner first."); return; }
                Phone.StartCall("Manager", "call_manager", () => GM.CompleteTask("manager"));
            }
            else if (who == IG)
            {
                Phone.ShowToast("(rings out)  He never picks up when he's live.");
            }
        }

        /// <summary>End of Day 1: the Manager's late text. Read it, answer it, close the phone, sleep.</summary>
        IEnumerator ManagerLateText()
        {
            HUD.SetObjective("\"I could sleep for a week.\"");
            yield return new WaitForSeconds(2.5f);
            Phone.AddMessage("Manager", Phone.FirstLine("txt_manager_work"));   // buzz + badge
            HUD.SetObjective("\"...Was that my phone?\"");
            Phone.OfferText("txt_manager_ok", _ => GM.CompleteTask("workmsg"), "Manager");
        }

        IEnumerator DinnerReply()
        {
            HUD.SetObjective("\"That man. Honestly.\"");
            yield return new WaitForSeconds(9f);   // a natural pause after the call (was 3 s - felt rushed)
            Phone.AddMessage(IG, Phone.FirstLine("txt_dinner_ig"));   // buzzes + badge
            HUD.SetObjective("\"He answered.\"");
            Phone.OfferText("txt_dinner_ok", _ => GM.CompleteTask("dinner"));
        }

        // =====================================================================
        // ACT I - The 24-Hour Subathon
        // =====================================================================

        public void BeginGame()
        {
            Debug.Log("[Story] BeginGame called - starting Act I");
            GM.Phase = GameManager.GamePhase.Act1;
            GM.PlayerLocked = false;
            GM.SetCheckpoint(Player.transform.position, Player.transform.eulerAngles.y);
            if (Ambience != null) Ambience.StartStorm();   // rain begins with the story, not the menu
            // Day content (titles, messages, objectives, tasks) comes from
            // days.txt via StoryDirector; legacy Act1Opening only runs when
            // no director is present.
            if (!DirectorDriven) StartCoroutine(Act1Opening());
        }

        IEnumerator Act1Opening()
        {
            yield return new WaitForSeconds(2f);
            Debug.Log("[Story] Act I intro: sending Mr. IG message + objective");
            Audio.ProceduralAudio.PlayPhoneBuzz(Player.transform.position);
            Phone.AddMessage(IG,
                "Yo " + P + "! Stream is dropping frames! Go reset the router and breakers in the garage, grab the energy drinks off the back step, and lock the back door while you're at it. Hurry up bro!",
                urgent: true);
            yield return new WaitForSeconds(2.5f);
            HUD.SetObjective("1. Reset the router & breakers (garage)\n2. Restock the mini-fridge (back-step delivery box)\n3. Calibrate the backup laptop waveform\n4. Lock the back door and ground windows\n5. Take " + IG + " a drink");
        }

        void OnTaskCompleted(string id)
        {
            switch (id)
            {
                // ---- Day 1 evening chain (days.txt: talk deliver dinner eat wash sleep) ----
                // The hints are the player talking to himself - never a checklist.
                case "talk":
                    HUD.SetObjective("\"One ice-cold Gamer-Juice. Kitchen.\"");
                    break;
                case "pee":
                    HUD.SetObjective("\"I wonder if he's eating tonight. I could text him.\"");
                    Phone.OfferText("txt_dinner_ask", _ => GM.CompleteTask("dinnermsg"));
                    break;
                case "dinnermsg":
                    HUD.SetObjective("\"While he thinks about it... my salary. I'm done being polite.\"");
                    break;
                case "manager":
                    StartCoroutine(DinnerReply());
                    break;
                case "dinner":
                    HUD.SetObjective("\"I'm starving. Pizza and the TV. That's the plan.\"");
                    break;
                case "eat":
                    HUD.SetObjective("\"The dishes won't wash themselves. ...Sadly.\"");
                    break;
                case "wash":
                    // before bed, the Manager texts (the workplace is deliberately never named)
                    StartCoroutine(ManagerLateText());
                    break;
                case "workmsg":
                    HUD.SetObjective("\"Okay. Bed. Finally.\"");
                    break;

                case "box":
                    Phone.AddMessage(IG, "Chat says W for the fridge restock. Make it icy cold bro.");
                    break;
                case "fridge":
                    if (DrinkCanPickup != null) DrinkCanPickup.SetVisible(true);
                    HUD.SetObjective("Take a Gamer-Juice upstairs to " + IG + "'s door.");
                    break;
                case "router":
                case "breakers":
                    if (GM.IsTaskDone("router") && GM.IsTaskDone("breakers"))
                        Phone.AddMessage(IG, "FRAMES ARE BACK. Chat is losing it. Good work " + P + ".");
                    break;
                case "laptop":
                    Phone.AddMessage(IG, "Audio sounds crisp bro. Peak content tonight.");
                    break;
                case "deliver":
                    if (GM.IsTaskDone("talk"))
                    {
                        // New day-1 flow: drink delivered, evening continues.
                        HUD.QueueSubtitle(IG, "(through the door, mid-game) Set it on the desk, king. W.", 2.5f);
                        HUD.SetObjective("\"All that Gamer-Juice. I need a bathroom.\"");
                    }
                    else
                    {
                        StartCoroutine(DeliverDialogue());
                    }
                    break;
            }
            CheckAct1Complete();
        }

        IEnumerator DeliverDialogue()
        {
            // Dialogue through Mr. IG's bedroom door (spec).
            HUD.SetObjective("");
            HUD.QueueSubtitle(IG, "(shouting through the door over loud music and fans) Thanks " + P + "! Leave it right there! Chat, drop a W in the chat for " + P + "!", 4.5f);
            yield return new WaitForSeconds(4.8f);
            HUD.QueueSubtitle(IG, "Remember to lock the windows - chat says some freak in a mascot mask is wandering down the street!", 4.5f);
            yield return new WaitForSeconds(4.8f);
            act1DialogueDone = true;
            if (!Act1ChoresDone)
                HUD.SetObjective("Finish the chores: " + RemainingChores());
            else
                HUD.SetObjective("Head back to the living room.");
        }

        bool Act1ChoresDone
        {
            get
            {
                foreach (var t in required) if (!GM.IsTaskDone(t)) return false;
                return act1DialogueDone;
            }
        }

        string RemainingChores()
        {
            var list = new List<string>();
            if (!GM.IsTaskDone("box")) list.Add("back-step delivery box");
            if (!GM.IsTaskDone("fridge")) list.Add("stock mini-fridge");
            if (!GM.IsTaskDone("router")) list.Add("reset router");
            if (!GM.IsTaskDone("breakers")) list.Add("flip breakers");
            if (!GM.IsTaskDone("laptop")) list.Add("laptop waveform");
            if (!GM.IsTaskDone("patio")) list.Add("lock patio door");
            if (!GM.IsTaskDone("window1") || !GM.IsTaskDone("window2")) list.Add("lock ground windows");
            if (!GM.IsTaskDone("deliver")) list.Add("deliver a drink");
            return string.Join(", ", list);
        }

        void CheckAct1Complete()
        {
            if (GM.Phase == GameManager.GamePhase.Act1 && Act1ChoresDone && !blackoutStarted)
                HUD.SetObjective("All done. Head back to the living room.");
        }

        // =====================================================================
        // ACT II - The Blackout
        // =====================================================================

        void Update()
        {
            if (GM == null || Player == null) return;
            if (DirectorDriven)
            {
                // The day script sequences blackout/bang/intruder itself;
                // only the stealth phase's live logic remains Update-driven.
                if (GM.Phase == GameManager.GamePhase.Act3) UpdateAct3();
                return;
            }

            switch (GM.Phase)
            {
                case GameManager.GamePhase.Act1:
                    if (Act1ChoresDone && !blackoutStarted &&
                        Vector3.Distance(Player.transform.position, LivingRoomCenter.position) < 2.6f)
                        StartCoroutine(Act2Blackout());
                    break;

                case GameManager.GamePhase.Act2:
                {
                    float d = Vector3.Distance(Player.transform.position, KitchenCenter.position);
                    act2Elapsed += Time.deltaTime;
                    if (!kitchenBangDone && !glassShattered && (d < 3.4f || act2Elapsed > 15f))
                        StartCoroutine(KitchenFalseAlarm());
                    break;
                }

                case GameManager.GamePhase.Act3:
                    UpdateAct3();
                    break;
            }
        }

        IEnumerator Act2Blackout()
        {
            blackoutStarted = true;
            act2Elapsed = 0f;
            GM.Phase = GameManager.GamePhase.Act2;
            GM.SetCheckpoint(LivingRoomCenter.position, 0f);

            // BAM - the big one.
            Ambience.ThunderClap();
            yield return new WaitForSeconds(0.6f);
            Power.Blackout();
            Phone.ForceShutdown();
            HUD.QueueSubtitle("", "WARNING: Battery 1% - Device Shutting Down...", 3f);
            HUD.SetObjectiveLabel("12:04 AM - BLACKOUT");
            HUD.SetObjective("The power is out. Navigate the dark house to the garage breaker.");
            yield return new WaitForSeconds(3f);
            Phone.AddMessage("UNKNOWN", "you locked the patio door. you forgot the window above the sink. i like that window.", urgent: true);
        }

        IEnumerator KitchenFalseAlarm()
        {
            kitchenBangDone = true;
            yield return new WaitForSeconds(1.2f);   // walking past the kitchen
            KitchenWindow.Bang();
            HUD.QueueSubtitle(P, "(a loud bang at the kitchen window) ...Just the tree branch. Just the storm.", 3.5f);
            // NOTE: the intruder entry is sequenced separately (days.txt
            // '@event intruder') - no auto-chaining here anymore.
        }

        IEnumerator GlassIntrusion()
        {
            yield return new WaitForSeconds(2.2f);
            glassShattered = true;
            DiningWindow.Shatter();     // rear dining room glass
            yield return new WaitForSeconds(1.4f);
            Audio.ProceduralAudio.PlayFootstep(EnemySpawn.position, false);
            yield return new WaitForSeconds(1.1f);
            Audio.ProceduralAudio.PlayFootstep(EnemySpawn.position, false);
            yield return new WaitForSeconds(1.0f);

            GM.Phase = GameManager.GamePhase.Act3;
            GM.SetCheckpoint(KitchenCenter.position, 90f);
            // He's locked in streaming while the house is breached - the door
            // stays shut until the stealth phase's narrative beat opens it.
            if (MrIgDoor != null) MrIgDoor.Locked = true;
            Enemy.Activate(EnemySpawn.position, upperRoute: false);
            HUD.SetObjectiveLabel("12:19 AM - HE'S INSIDE");
            HUD.SetObjective("Get upstairs to " + IG + "'s room. Stay LOW. Stay QUIET. [C] to crouch.");
        }

        // =====================================================================
        // ACT III - Infiltration & Stealth
        // =====================================================================

        bool hideEventStarted;
        bool playerInHallwayAnnounced;
        bool hideEventEnemyAtCloset;

        void UpdateAct3()
        {
            if (!playerInHallwayAnnounced &&
                Player.transform.position.y > 2.6f &&
                Vector3.Distance(Player.transform.position, HallCloset.transform.position) < 5f)
            {
                playerInHallwayAnnounced = true;
                Enemy.SetRoute(true);
                HUD.SetObjective("He's coming upstairs. HIDE - the hallway closet!");
            }

            if (playerInHallwayAnnounced && !hideEventStarted && HallCloset.PlayerInside)
            {
                hideEventStarted = true;
                StartCoroutine(ClosetHideEvent());
            }

            if (hideEventStarted && act3HideEventDone && !doorUnlocked &&
                Vector3.Distance(Player.transform.position, MrIgDoor.transform.position) < 3.2f)
            {
                doorUnlocked = true;
                MrIgDoor.Unlock();
                HUD.SetObjective("Slip into " + IG + "'s room - NOW!");
            }

            if (doorUnlocked &&
                Vector3.Distance(Player.transform.position, BedroomCenter.position) < 2.4f &&
                !act4Started)
            {
                // Stealth complete: the director (days.txt) decides what happens
                // next - the climax runs as its own day/event.
                StealthDone = true;
                GM.SetCheckpoint(Player.transform.position, Player.transform.eulerAngles.y);
            }
        }

        IEnumerator ClosetHideEvent()
        {
            HUD.SetObjectiveLabel("12:31 AM - STAY SILENT");
            HUD.SetObjective("He stopped right outside the closet. Do NOT make a sound.");
            // The intruder walks up to the closet and stands there.
            yield return new WaitForSeconds(2f);
            hideEventEnemyAtCloset = true;
            // Route him to stand at the closet guard position for six long seconds.
            Enemy.SendToSilentGuard(ClosetGuardPos.position);
            float wait = 9f;
            while (wait > 0f)
            {
                wait -= Time.deltaTime;
                // If the mic went red the enemy AI handles the catch itself.
                yield return null;
            }
            hideEventEnemyAtCloset = false;
            if (!act4Started)
            {
                act3HideEventDone = true;
                HUD.SetObjectiveLabel("12:33 AM");
                HUD.SetObjective("He turned away. Slip into " + IG + "'s room. Quietly.");
                GM.SetCheckpoint(Player.transform.position, Player.transform.eulerAngles.y);
            }
        }

        // =====================================================================
        // ACT IV - The Double-Twist Climax
        // =====================================================================

        IEnumerator Act4Climax()
        {
            act4Started = true;
            GM.Phase = GameManager.GamePhase.Act4;
            GM.PlayerLocked = true;
            MrIgDoor.Slam();
            HUD.SetObjectiveLabel("12:40 AM - THE ROOMMATE");
            HUD.SetObjective("");
            yield return new WaitForSeconds(1.2f);

            HUD.QueueSubtitle(P, IG + "! Someone broke into the house! The power is out, someone's in the hallway with a crowbar!", 4.5f);
            yield return new WaitForSeconds(5f);
            HUD.QueueSubtitle(IG, "(laughs loudly) Dude, " + P + ", relax! Look at my stream laptop!", 4f);
            yield return new WaitForSeconds(4.5f);

            // Show the live hallway GoPro feed.
            HUD.ShowFeedOverlay(true, HallCam);
            HUD.QueueSubtitle(IG, "Chat is dying! That's " + GameConfig.FriendName + ", my editor! I paid him $100 to cut the power and pull a prank on you for the subathon!", 5.5f);
            yield return new WaitForSeconds(6f);

            HUD.QueueSubtitle(IG, "Alright " + GameConfig.FriendName + ", take off the mask, you got him good!", 4f);
            // The unscripted twist: he pulls out a real hunting knife instead.
            Enemy.SetSilentAt(ClosetGuardPos.position);   // face the camera in the hallway
            if (IntruderKnife != null) IntruderKnife.SetActive(true);   // pre-parented to the enemy by the builder
            yield return new WaitForSeconds(1.5f);
            yield return new WaitForSeconds(3f);

            // The phone call.
            for (int i = 0; i < 3; i++)
            {
                Audio.ProceduralAudio.PlayPhoneBuzz(BedroomCenter.position);
                yield return new WaitForSeconds(0.7f);
            }
            HUD.QueueSubtitle("???  (speakerphone)", GameConfig.FriendName + "?! Do not open your door! Some crazy guy jumped me in the alley, knocked me out, and stole my costume and house keys! CALL THE POLICE!", 6f);
            yield return new WaitForSeconds(6.5f);

            // He smashes the camera.
            HallCam.Smash();
            yield return new WaitForSeconds(1.6f);
            HUD.ShowFeedOverlay(false, null);

            // Running footsteps charge toward the bedroom.
            for (int i = 0; i < 7; i++)
            {
                Audio.ProceduralAudio.PlayFootstep(MrIgDoor.transform.position, false);
                yield return new WaitForSeconds(0.28f);
            }

            // Jumpscare: crowbar crashes through the door.
            for (int i = 0; i < 3; i++)
            {
                Audio.ProceduralAudio.PlayCrowbarHit(MrIgDoor.transform.position);
                yield return new WaitForSeconds(0.55f);
            }
            StartCoroutine(HUD.FlashRed(0.4f));
            MrIgDoor.PanicShake(1.2f);

            // QTE 1: shove the desk.
            yield return new WaitForSeconds(0.6f);
            while (true)
            {
                yield return StartCoroutine(QTE.Run("SHOVE THE DESK AGAINST THE DOOR", 4.5f));
                if (QTE.LastResult) break;
                Audio.ProceduralAudio.PlayCrowbarHit(MrIgDoor.transform.position);
                StartCoroutine(HUD.FlashRed(0.2f));
            }
            yield return StartCoroutine(SlideDesk());

            // QTE 2: unlatch & smash the window.
            while (true)
            {
                yield return StartCoroutine(QTE.Run("UNLATCH AND SMASH THE WINDOW", 4.5f));
                if (QTE.LastResult) break;
                Audio.ProceduralAudio.PlayCrowbarHit(MrIgDoor.transform.position);
                StartCoroutine(HUD.FlashRed(0.2f));
            }
            EscapeWindow.Shatter();
            yield return new WaitForSeconds(0.5f);

            // Jump out into the backyard rain as sirens approach.
            yield return StartCoroutine(HUD.FadeTo(1f, 0.4f));
            Player.TeleportTo(BackyardLanding.position, 180f);
            Ambience.StartSirens();
            yield return new WaitForSeconds(0.6f);
            yield return StartCoroutine(HUD.FadeTo(0f, 1.2f));
            HUD.SetObjectiveLabel("12:47 AM");
            HUD.SetObjective("");
            HUD.QueueSubtitle(P, "(sirens echo down the street, rain soaking through my shirt) ...I quit. I quit. I quit.", 5f);
            yield return new WaitForSeconds(5.5f);
            yield return StartCoroutine(HUD.FadeTo(1f, 2f));

            GM.Phase = GameManager.GamePhase.Epilogue;
            yield return StartCoroutine(Epilogue.PlayEpilogue());
        }

        IEnumerator SlideDesk()
        {
            if (DeskProp == null || DeskBlockingPos == null) yield break;
            Vector3 a = DeskProp.transform.position;
            Vector3 b = DeskBlockingPos.position;
            Quaternion ra = DeskProp.transform.rotation;
            Quaternion rb = DeskBlockingPos.rotation;
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.7f)
            {
                if (DeskProp != null)
                    DeskProp.transform.SetPositionAndRotation(
                        Vector3.Lerp(a, b, t), Quaternion.Slerp(ra, rb, t));
                yield return null;
            }
            Audio.ProceduralAudio.PlayBang(MrIgDoor.transform.position, 0.8f);
        }

        // =====================================================================
        // Death & respawn
        // =====================================================================

        public IEnumerator CaughtRoutine()
        {
            if (GM.Phase == GameManager.GamePhase.Act4 || GM.Phase == GameManager.GamePhase.Epilogue)
                yield break;
            GM.PlayerLocked = true;
            Audio.ProceduralAudio.PlayCrowbarHit(Player.transform.position);
            Audio.ProceduralAudio.PlayGameOverStinger(Player.transform.position);
            StartCoroutine(HUD.FlashRed(0.5f));
            yield return new WaitForSeconds(0.4f);
            yield return StartCoroutine(HUD.FadeTo(1f, 0.5f));
            HUD.ShowDeathText(true, "HE GOT YOU");
            yield return new WaitForSeconds(2.4f);
            HUD.ShowDeathText(false);

            // Respawn at the checkpoint; reset the intruder.
            if (Player.CurrentHideSpot != null) Player.ExitHiding();
            Player.TeleportTo(GM.Checkpoint, GM.CheckpointYaw);
            Enemy.ResetForRespawn();
            if (GM.Phase == GameManager.GamePhase.Act3)
            {
                HUD.SetObjective("Get upstairs to " + IG + "'s room. Stay LOW. Stay QUIET.");
                Enemy.SetRoute(false);
                playerInHallwayAnnounced = false;
                hideEventStarted = false;
            }
            GM.PlayerLocked = false;
            yield return StartCoroutine(HUD.FadeTo(0f, 1.2f));
        }

        // =====================================================================
        // Interact reactions
        // =====================================================================

        void OnAnyInteracted(Interaction.InteractableObject obj)
        {
            // Patio door counts as locked when closed.
            if (obj == PatioDoor && !PatioDoor.IsOpen && GM.Phase == GameManager.GamePhase.Act1)
                GM.CompleteTask("patio");
        }
    }
}
