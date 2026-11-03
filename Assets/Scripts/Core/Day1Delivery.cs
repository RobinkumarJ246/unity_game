using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ContentHouse.Interaction;
using ContentHouse.UI;
using ContentHouse.Player;

namespace ContentHouse.Core
{
    /// <summary>
    /// The end-of-Day-1 beat: you lie down to sleep, the doorbell rings, you open the front door, the porch is
    /// empty for five seconds, then a delivery man steps in from the side (jumpscare). He hands over a package
    /// for Mr. IG; you text IG about it, he says to leave it by the front door; you do, and go back to bed.
    /// Triggered by the bed (BedSleepInteractable -> RingDoorbell). Lives as a component next to StoryManager.
    /// </summary>
    public class Day1Delivery : MonoBehaviour
    {
        public static Day1Delivery Instance { get; private set; }

        public DoorInteractable FrontDoor;
        public Vector3 PlayerSpot = new Vector3(-2.06f, 0.41f, -2.7f);   // where you stand at the open door
        public Vector3 DropPoint = new Vector3(-1.7f, 0.41f, -2.5f);     // floor spot for the package, inside by the door

        // the man's route on the porch (ground there is y 0.41; the porch is seen through the doorway)
        // He starts hidden behind the wall right of the doorway, slides to Stand (just outside the door, ~1.5 m from you),
        // and later walks off to ExitPoint the way he came.
        static readonly Vector3 SlideStart = new Vector3(-0.65f, 0.41f, -0.85f);
        static readonly Vector3 Stand = new Vector3(-2.05f, 0.41f, -1.2f);
        static readonly Vector3 ExitPoint = new Vector3(0.3f, 0.41f, -0.85f);

        bool doorbellPending, sceneRunning;
        public bool BellRang { get; private set; }
        public bool BellPending => doorbellPending;
        GameManager gm => GameManager.Instance;
        static AudioClip stingClip;

        void Awake() { Instance = this; }

        void Start()
        {
            if (FrontDoor == null)
            {
                var g = GameObject.Find("GameRoot/GameplayDoorsWindows/DoorRig_Door");
                if (g != null) FrontDoor = g.GetComponent<DoorInteractable>();
            }
            if (FrontDoor == null) Debug.LogWarning("[Delivery] front door not found - the doorbell scene cannot run");
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        // ------------------------------------------------------------------ 1) the doorbell

        /// <summary>Called by the bed the first time you try to sleep.</summary>
        public void RingDoorbell()
        {
            if (BellRang || FrontDoor == null) return;
            BellRang = true;
            StartCoroutine(BellRoutine());
        }

        IEnumerator BellRoutine()
        {
            doorbellPending = true;
            gm.CompleteTask("doorbell");
            FrontDoor.PromptOverride = () => doorbellPending && !sceneRunning ? "Who could that be?  [E]" : null;
            FrontDoor.InteractIntercept = d =>
            {
                if (!doorbellPending || sceneRunning) return false;
                StartCoroutine(DeliveryScene());
                return true;
            };
            gm.HUD?.SetObjective("\"...Was that the doorbell? At this hour?\"");
            yield return new WaitForSeconds(0.8f);
            for (int i = 0; i < 2; i++) { PlayBell(); yield return new WaitForSeconds(3.4f); }
            float t = 0f;
            while (doorbellPending)   // a polite reminder if you wander off
            {
                t += Time.deltaTime;
                if (t > 40f && !sceneRunning) { t = 0f; PlayBell(); }
                yield return null;
            }
        }

        void PlayBell()
        {
            var clip = Resources.Load<AudioClip>("Sfx/kaino/Doorbell 2 - Kaino");
            if (clip == null || FrontDoor == null) return;
            var go = new GameObject("DoorbellSound");
            go.transform.position = FrontDoor.transform.position + Vector3.up * 1.5f;
            var a = go.AddComponent<AudioSource>();
            a.clip = clip; a.spatialBlend = 0.35f; a.minDistance = 6f; a.maxDistance = 60f; a.volume = 1f;
            a.Play();
            Destroy(go, clip.length + 0.5f);
        }

        // ------------------------------------------------------------------ 2) the porch scene

        IEnumerator DeliveryScene()
        {
            sceneRunning = true;
            var pl = gm.Player; var cam = pl.Camera.transform;
            gm.HUD?.SetCenterPrompt("");
            gm.PlayerLocked = true;

            // stand at the doorway, camera fixed on the porch (no movement possible: nothing to glitch)
            var cc = pl.GetComponent<CharacterController>();
            cc.enabled = false; pl.TeleportTo(PlayerSpot, 0f); cc.enabled = true;
            yield return StartCoroutine(pl.FocusOn(new Vector3(PlayerSpot.x, 1.5f, 4f), 0.5f));
            FrontDoor.SetOpen(true, false);
            yield return new WaitForSeconds(1.4f);

            // ---- nobody there. The player notices, out loud, and then the silence drags on. ----
            yield return new WaitForSeconds(0.9f);
            gm.HUD?.QueueSubtitle("", "...Weird. Nobody's there.", 2.6f);
            yield return new WaitForSeconds(2.9f);
            gm.HUD?.QueueSubtitle("", "So who rang the bell, then?", 2.6f);
            yield return new WaitForSeconds(3.0f);

            // ---- the man SLIDES in from behind the wall, right up to the doorway, in a fraction of a second ----
            var prefab = Resources.Load<GameObject>("Generated/delivery_man");
            GameObject man = null; DeliveryManRig rig = null; GameObject box = null;
            if (prefab != null)
            {
                man = Instantiate(prefab, SlideStart, Quaternion.LookRotation(Vector3.back));   // facing you the whole time
                rig = man.AddComponent<DeliveryManRig>();
                var boxPrefab = Resources.Load<GameObject>("Generated/package_box");
                if (boxPrefab != null)
                {
                    box = Instantiate(boxPrefab, man.transform);
                    box.transform.localPosition = new Vector3(0f, 0.93f, 0.30f);
                    box.transform.localRotation = Quaternion.identity;
                }
            }
            else Debug.LogWarning("[Delivery] Resources/Generated/delivery_man.prefab missing");

            StartCoroutine(Sting(cam));                    // sound, white flash and shake hit on the very same frame
            if (man != null)
            {
                const float slide = 0.14f;                 // ~10 m/s: he is simply THERE
                for (float t = 0f; t < slide; t += Time.deltaTime)
                {
                    float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / slide), 3f);   // fast start, hard stop
                    man.transform.position = Vector3.Lerp(SlideStart, Stand, k);
                    yield return null;
                }
                man.transform.position = Stand;
            }
            else yield return new WaitForSeconds(0.2f);

            yield return new WaitForSeconds(1.1f);   // let the scare land
            var dlg = DialogueScreen.Instance;
            if (dlg != null) yield return StartCoroutine(dlg.Play("door_delivery", "Delivery man"));

            // ---- he hands it over ----
            if (box != null)
            {
                var from = box.transform.position; var s0 = box.transform.lossyScale;
                var to = cam.position + cam.forward * 0.45f + Vector3.down * 0.28f;
                box.transform.SetParent(null, true);
                for (float t = 0f; t < 0.55f; t += Time.deltaTime)
                {
                    float k = Mathf.SmoothStep(0f, 1f, t / 0.55f);
                    box.transform.position = Vector3.Lerp(from, to, k);
                    box.transform.localScale = Vector3.Lerp(s0, s0 * 0.6f, k);
                    yield return null;
                }
                Destroy(box);
            }
            Audio.ProceduralAudio.PlayConfirmation(PlayerSpot);
            gm.SetHeldItem(HeldItemView.Package);

            // ---- he leaves the way he came ----
            if (man != null)
            {
                yield return StartCoroutine(Turn(man.transform, Quaternion.LookRotation(Vector3.right), 0.3f));
                StartCoroutine(WalkTo(man.transform, rig, ExitPoint, 1.9f, null, () => { Destroy(man); }));
                yield return new WaitForSeconds(0.8f);
            }
            yield return new WaitForSeconds(1.2f);
            FrontDoor.SetOpen(false, false);
            yield return new WaitForSeconds(1.2f);

            // ---- free again; the player thinks out loud ----
            FrontDoor.PromptOverride = null; FrontDoor.InteractIntercept = null;
            doorbellPending = false;
            pl.LookLocked = false;
            gm.PlayerLocked = false;
            sceneRunning = false;
            gm.CompleteTask("delivery");
            gm.HUD?.QueueSubtitle("", "That's... weird. Who delivers at this hour?", 3.8f);
            yield return new WaitForSeconds(4.2f);
            gm.HUD?.QueueSubtitle("", "No van. No engine. I didn't even hear him walk up.", 4f);
            gm.HUD?.SetObjective("\"Mr. IG owes me an explanation. I'll text him.\"");
            yield return new WaitForSeconds(1.5f);

            if (gm.Phone != null)
                gm.Phone.OfferText("txt_pkg_ask", _ => StartCoroutine(IgReply()), GameConfig.StreamerName);

            // the drop-off spot, by the inside of the front door
            MakeDropSpot();
        }

        IEnumerator IgReply()
        {
            yield return new WaitForSeconds(7f);
            gm.Phone.AddMessage(GameConfig.StreamerName, gm.Phone.FirstLine("txt_pkg_ig"));
            gm.HUD?.SetObjective("\"Of course he ordered it. By the front door, then. And then, bed.\"");
            gm.CompleteTask("pkgreply");
        }

        // ------------------------------------------------------------------ the drop-off spot

        void MakeDropSpot()
        {
            if (GameObject.Find("PackageDropSpot") != null) return;
            var go = new GameObject("PackageDropSpot");
            go.transform.position = DropPoint + Vector3.up * 0.5f;
            var col = go.AddComponent<BoxCollider>(); col.size = new Vector3(1.0f, 1.0f, 1.0f); col.isTrigger = true;
            go.AddComponent<PackageDropSpot>().Floor = DropPoint;
        }

        // ------------------------------------------------------------------ helpers

        IEnumerator WalkTo(Transform t, DeliveryManRig rig, Vector3 target, float speed, System.Action<Vector3> onStep, System.Action onDone = null)
        {
            rig.Walking = true;
            while ((t.position - target).sqrMagnitude > 0.0036f)
            {
                var dir = target - t.position; dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f) t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 9f);
                t.position = Vector3.MoveTowards(t.position, target, speed * Time.deltaTime);
                if (onStep != null) onStep(t.position);
                yield return null;
            }
            rig.Walking = false;
            if (onDone != null) onDone();
        }

        static IEnumerator Turn(Transform t, Quaternion to, float seconds)
        {
            var from = t.rotation;
            for (float k = 0f; k < seconds; k += Time.deltaTime) { t.rotation = Quaternion.Slerp(from, to, k / seconds); yield return null; }
            t.rotation = to;
        }

        IEnumerator Sting(Transform cam)
        {
            EnsureSting();
            var go = new GameObject("StingSound");
            var a = go.AddComponent<AudioSource>(); a.clip = stingClip; a.spatialBlend = 0f; a.volume = 1f; a.Play();
            Destroy(go, stingClip.length + 0.3f);
            StartCoroutine(ScreenFx.Instance.Flash(new Color(1f, 1f, 1f, 0.85f), 0.05f, 0.55f));
            yield return StartCoroutine(ScreenFx.Shake(cam, 0.55f, 0.07f));
        }

        static void EnsureSting()
        {
            if (stingClip != null) return;
            const int rate = 22050; int n = (int)(rate * 1.3f);
            var d = new float[n]; var rng = new System.Random(3);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float boom = Mathf.Sin(2f * Mathf.PI * (52f + 20f * Mathf.Exp(-t * 6f)) * t) * Mathf.Exp(-t * 3.2f);                 // low thud
                float hit = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 14f);                                               // noise slam
                float f = 700f + 900f * Mathf.Min(1f, t / 0.5f);                                                                     // rising shriek
                float shriek = (Mathf.Sin(2f * Mathf.PI * f * t) + 0.6f * Mathf.Sin(2f * Mathf.PI * f * 1.41f * t)) * Mathf.Exp(-t * 4.5f) * 0.35f;
                d[i] = boom * 0.9f + hit * 0.8f + shriek;
            }
            float peak = 0.0001f; foreach (var x in d) peak = Mathf.Max(peak, Mathf.Abs(x));
            for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(d[i] * (0.95f / peak), -1f, 1f);
            stingClip = AudioClip.Create("jump_sting", n, 1, rate, false);
            stingClip.SetData(d, 0);
        }
    }

    /// <summary>Where the package goes: just inside the front door. Only reacts while you carry it.</summary>
    public class PackageDropSpot : InteractableObject
    {
        public Vector3 Floor;

        public override string Prompt
        {
            get
            {
                var g = GameManager.Instance;
                if (g == null || g.IsTaskDone("pkgplaced") || g.HeldItem != HeldItemView.Package) return "";
                if (!g.IsTaskDone("pkgreply")) return "I should at least ask Mr. IG what this is.";
                return "Leave the package here  [E]";
            }
        }

        public override void Interact(Player.PlayerInteraction player)
        {
            var g = GameManager.Instance;
            if (g == null || g.HeldItem != HeldItemView.Package || !g.IsTaskDone("pkgreply") || g.IsTaskDone("pkgplaced")) return;
            used = true;
            g.SetHeldItem("");
            var prefab = Resources.Load<GameObject>("Generated/package_box");
            if (prefab != null)
            {
                var b = Instantiate(prefab, Floor, Quaternion.Euler(0f, 24f, 0f));
                b.name = "PackageOnFloor";
            }
            Audio.ProceduralAudio.PlayConfirmation(Floor);
            g.HUD?.QueueSubtitle("", "There. Out of my hands. Out of my head. ...Mostly.", 3.6f);
            g.HUD?.SetObjective("\"Right. Bed. For real this time.\"");
            NotifyTask();
            g.CompleteTask("pkgplaced");
        }
    }

    /// <summary>
    /// Procedural pose + walk for the (rigged, animation-less) delivery man: arms hold the box in front, legs
    /// swing while Walking. Works in world directions so it doesn't depend on how the bones are oriented.
    /// </summary>
    public class DeliveryManRig : MonoBehaviour
    {
        public bool Walking;
        Transform lThigh, rThigh, lCalf, rCalf, lFoot, rFoot, lUp, rUp, lFore, rFore, lHand, rHand;
        readonly Dictionary<Transform, Quaternion> bind = new Dictionary<Transform, Quaternion>();
        float phase;

        Transform Find(string n)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == n) return t;
            return null;
        }

        void Awake()
        {
            lThigh = Find("Bip01 L Thigh"); rThigh = Find("Bip01 R Thigh"); lCalf = Find("Bip01 L Calf"); rCalf = Find("Bip01 R Calf");
            lFoot = Find("Bip01 L Foot"); rFoot = Find("Bip01 R Foot");
            lUp = Find("Bip01 L UpperArm"); rUp = Find("Bip01 R UpperArm"); lFore = Find("Bip01 L Forearm"); rFore = Find("Bip01 R Forearm");
            lHand = Find("Bip01 L Hand"); rHand = Find("Bip01 R Hand");
            foreach (var t in new[] { lThigh, rThigh, lCalf, rCalf, lUp, rUp, lFore, rFore }) if (t != null) bind[t] = t.localRotation;
        }

        static void Aim(Transform bone, Transform child, Vector3 worldDir)
        {
            if (bone == null || child == null) return;
            var cur = (child.position - bone.position).normalized;
            if (cur.sqrMagnitude < 0.5f) return;
            bone.rotation = Quaternion.FromToRotation(cur, worldDir.normalized) * bone.rotation;
        }

        void LateUpdate()
        {
            if (lUp == null) return;
            foreach (var kv in bind) kv.Key.localRotation = kv.Value;       // back to the T-pose each frame, then pose
            Vector3 fwd = transform.forward, down = Vector3.down, right = transform.right;

            // arms: upper arms hang a little forward, forearms come up in front holding the box
            float sideL = Mathf.Sign(Vector3.Dot(lUp.position - transform.position, right));
            Aim(lUp, lFore, down * 1f + fwd * 0.30f + right * sideL * 0.18f);
            Aim(rUp, rFore, down * 1f + fwd * 0.30f - right * sideL * 0.18f);
            Aim(lFore, lHand, fwd * 1f + down * 0.15f - right * sideL * 0.45f);
            Aim(rFore, rHand, fwd * 1f + down * 0.15f + right * sideL * 0.45f);

            // legs: stand straight, or swing while walking
            float a = 0f;
            if (Walking) { phase += Time.deltaTime * 7.5f; a = Mathf.Sin(phase) * 0.55f; }
            Aim(lThigh, lCalf, down * Mathf.Cos(a) + fwd * Mathf.Sin(a));
            Aim(rThigh, rCalf, down * Mathf.Cos(-a) + fwd * Mathf.Sin(-a));
            Aim(lCalf, lFoot, down * Mathf.Cos(a - Mathf.Max(0f, -a) * 0.8f) + fwd * Mathf.Sin(a - Mathf.Max(0f, -a) * 0.8f));
            Aim(rCalf, rFoot, down * Mathf.Cos(-a - Mathf.Max(0f, a) * 0.8f) + fwd * Mathf.Sin(-a - Mathf.Max(0f, a) * 0.8f));
        }
    }
}
