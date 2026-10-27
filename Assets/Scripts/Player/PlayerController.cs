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
                if (!phoneOpen) ApplyLook();   // look around while hiding / cutscene
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
            // Run in any direction; holding shift cancles crouch (escape chases).
            IsRunning = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                        && (Mathf.Abs(h) + Mathf.Abs(v)) > 0.1f;
            if (IsRunning) IsCrouching = false;
            float speed = IsRunning ? RunSpeed : (IsCrouching ? CrouchSpeed : WalkSpeed);

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

