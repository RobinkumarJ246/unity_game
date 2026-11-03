using UnityEngine;

namespace ContentHouse.Player
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public Camera Camera;
        public float WalkSpeed = Core.GameConfig.WalkSpeed;
        public float RunSpeed = Core.GameConfig.RunSpeed;
        public float CrouchSpeed = Core.GameConfig.CrouchSpeed;
        public float Sensitivity = Core.GameConfig.MouseSensitivity;

        public bool IsHiding { get; private set; }
        public bool IsCrouching { get; private set; }
        public bool IsRunning { get; private set; }

        CharacterController cc;
        float yaw, pitch;
        float standHeight = 1.7f;
        float crouchHeight = 1.05f;
        float camStandY = 1.55f;   // eye near the TOP of the capsule (human height)
        float camCrouchY = 0.9f;
        float footstepTimer;
        float stepIntervalWalk = 0.5f, stepIntervalRun = 0.34f, stepIntervalCrouch = 0.75f;
        Interaction.HideSpot currentSpot;

        public Interaction.HideSpot CurrentHideSpot => currentSpot;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            yaw = transform.eulerAngles.y;
        }

        void Update()
        {
            var gm = Core.GameManager.Instance;
            bool phoneOpen = gm != null && gm.Phone != null && gm.Phone.IsOpen;
            bool frozen = (gm != null && gm.PlayerLocked) || IsHiding || phoneOpen;

            Cursor.lockState = frozen ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = frozen;

            if (frozen)
            {
                if (!phoneOpen && !LookLocked) ApplyLook();   // look around while hiding / cutscene
                return;
            }

            ApplyLook();
            ApplyCrouchToggle();
            ApplyMovement();
        }

        void ApplyLook()
        {
            float mx = Input.GetAxis("Mouse X") * Sensitivity;
            float my = Input.GetAxis("Mouse Y") * Sensitivity;
            yaw += mx;
            pitch = Mathf.Clamp(pitch - my, -85f, 85f);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (Camera != null) Camera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        void ApplyCrouchToggle()
        {
            if (Input.GetKeyDown(KeyCode.C)) IsCrouching = !IsCrouching;
        }

        void ApplyMovement()
        {
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            // Running is for escaping only: Shift does nothing unless the intruder is actually chasing you.
            var gmv = Core.GameManager.Instance;
            bool chased = gmv != null && gmv.Enemy != null && gmv.Enemy.State == Enemy.EnemyAI.AIState.Pursue;
            IsRunning = chased && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                        && (Mathf.Abs(h) + Mathf.Abs(v)) > 0.1f;
            if (IsRunning) IsCrouching = false;
            // Speeds come from GameConfig (the serialized copies in the scene are stale and ignored).
            float speed = IsRunning ? Core.GameConfig.RunSpeed : (IsCrouching ? Core.GameConfig.CrouchSpeed : Core.GameConfig.WalkSpeed);

            var move = (transform.right * h + transform.forward * v).normalized;
            cc.SimpleMove(move * speed);

            // Smooth crouch height / camera.
            float targetH = IsCrouching ? crouchHeight : standHeight;
            cc.height = Mathf.Lerp(cc.height, targetH, 10f * Time.deltaTime);
            cc.center = new Vector3(0f, cc.height * 0.5f, 0f);
            if (Camera != null)
            {
                float targetY = IsCrouching ? camCrouchY : camStandY;
                var lp = Camera.transform.localPosition;
                lp.y = Mathf.Lerp(lp.y, targetY + cc.height * 0.5f - 0.85f, 10f * Time.deltaTime);
                Camera.transform.localPosition = lp;
            }

            // Footsteps: sound + noise report radius (crouch 1.5 / walk 4 / run 8).
            if (cc.velocity.magnitude > 0.4f)
            {
                float interval = IsRunning ? stepIntervalRun : (IsCrouching ? stepIntervalCrouch : stepIntervalWalk);
                footstepTimer += Time.deltaTime;
                if (footstepTimer >= interval)
                {
                    footstepTimer = 0f;
                    Audio.ProceduralAudio.PlayFootstep(transform.position, IsCrouching);
                    if (IsHiding) return;
                    float radius = IsCrouching ? 1.5f : (IsRunning ? 8f : 4f);
                    Core.NoiseSystem.ReportNoise(transform.position, radius);
                }
            }
            else footstepTimer = 0f;
        }

        /// <summary>While true the mouse can't move the view (conversations: cursor is free for the UI).</summary>
        public bool LookLocked { get; set; }

        /// <summary>Smoothly turn the view toward a world point (eased), then keep it there while LookLocked.</summary>
        public System.Collections.IEnumerator FocusOn(Vector3 point, float seconds)
        {
            LookLocked = true;
            var d = point - Camera.transform.position;
            float y0 = yaw, p0 = pitch;
            float y1 = y0 + Mathf.DeltaAngle(y0, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
            float p1 = Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, -85f, 85f);
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / seconds);
                yaw = Mathf.Lerp(y0, y1, k); pitch = Mathf.Lerp(p0, p1, k);
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                Camera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                yield return null;
            }
            yaw = y1; pitch = p1;
        }

        public void TeleportTo(Vector3 pos, float yawDegrees)
        {
            transform.position = pos;
            yaw = yawDegrees;
            pitch = 0f;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        public void EnterHiding(Interaction.HideSpot spot)
        {
            currentSpot = spot;
            IsHiding = true;
            transform.position = spot.StandAnchor.position;
            yaw = spot.StandAnchor.eulerAngles.y;
            pitch = 0f;
        }

        public void ExitHiding()
        {
            if (currentSpot == null) { IsHiding = false; return; }
            transform.position = currentSpot.ExitAnchor.position;
            IsHiding = false;
            var spot = currentSpot;
            currentSpot = null;
            spot.OnPlayerExited();
        }
    }
}

