using UnityEngine;

namespace ContentHouse.Player
{
    /// <summary>
    /// [E] interaction from the player camera.
    /// Targeting is forgiving on purpose: a thin spherecast (not a pencil ray), trigger zones are
    /// honoured, and zones the camera is STANDING INSIDE still count (a plain Raycast ignores any
    /// collider it starts inside, which made prompts vanish when you got close to doors / NPCs).
    /// </summary>
    public class PlayerInteraction : MonoBehaviour
    {
        public PlayerController Player;
        public float InteractRange = 2.7f;
        public float AimRadius = 0.18f;

        Interaction.InteractableObject hovered;
        float lastHoverTime;
        string lastPrompt = "";
        Interaction.InteractableObject graceTarget;   // keeps [E] working for a few frames after aim wobbles off
        float graceUntil;

        readonly RaycastHit[] hits = new RaycastHit[32];
        readonly Collider[] overlaps = new Collider[16];

        void Update()
        {
            var gm = Core.GameManager.Instance;
            if (gm == null || gm.PlayerLocked) { hovered = null; return; }
            if (gm.Phone != null && gm.Phone.IsOpen) { hovered = null; gm.HUD.SetCenterPrompt(""); return; }

            if (Player != null && Player.IsHiding)
            {
                if (gm.HUD != null) gm.HUD.SetCenterPrompt("[E]  Stay quiet... or leave");
                if (Input.GetKeyDown(KeyCode.E)) Player.ExitHiding();
                return;
            }

            hovered = FindTarget();

            if (hovered != null) { graceTarget = hovered; graceUntil = Time.time + 0.25f; }

            if (gm.HUD != null)
            {
                if (hovered != null)
                {
                    lastPrompt = hovered.Prompt;
                    lastHoverTime = Time.time;
                    gm.HUD.SetCenterPrompt(lastPrompt);
                }
                else if (Time.time - lastHoverTime < 0.35f && lastPrompt.Length > 0)
                {
                    gm.HUD.SetCenterPrompt(lastPrompt);   // sticky: no flicker
                }
                else
                {
                    gm.HUD.SetCenterPrompt("");
                }
            }

            if (Input.GetKeyDown(KeyCode.E))
            {
                var t = hovered != null ? hovered : (Time.time < graceUntil ? graceTarget : null);
                if (t != null) t.Interact(this);
            }
        }

        // NOT transform.root: the player shares the GameRoot parent with every door and device.
        bool IsOwn(Collider c) => Player != null && c.transform.IsChildOf(Player.transform);

        Interaction.InteractableObject FindTarget()
        {
            var cam = Player.Camera.transform;
            var origin = cam.position;
            var fwd = cam.forward;

            // 1) spherecast along the view, nearest-first. Own colliders and non-interactable triggers
            //    are ignored; the first solid non-interactable collider blocks (walls).
            int n = Physics.SphereCastNonAlloc(origin, AimRadius, fwd, hits, InteractRange,
                ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, 0, n, DistanceComparer.Instance);
            for (int i = 0; i < n; i++)
            {
                var c = hits[i].collider;
                if (IsOwn(c)) continue;
                var it = c.GetComponentInParent<Interaction.InteractableObject>();
                if (it != null) return it;
                if (!c.isTrigger && hits[i].distance > 0f) break;   // wall / furniture in the way
            }

            // 2) trigger zones that contain the camera (spherecast can't see colliders it starts inside):
            //    count them when you are roughly looking at the zone.
            int m = Physics.OverlapSphereNonAlloc(origin, 0.05f, overlaps, ~0, QueryTriggerInteraction.Collide);
            Interaction.InteractableObject best = null; float bestDot = 0.35f;
            for (int i = 0; i < m; i++)
            {
                var c = overlaps[i];
                if (!c.isTrigger || IsOwn(c)) continue;
                var it = c.GetComponentInParent<Interaction.InteractableObject>();
                if (it == null) continue;
                float d = Vector3.Dot(fwd, (c.bounds.center - origin).normalized);
                if (d > bestDot) { bestDot = d; best = it; }
            }
            return best;
        }

        sealed class DistanceComparer : System.Collections.Generic.IComparer<RaycastHit>
        {
            public static readonly DistanceComparer Instance = new DistanceComparer();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }
    }
}
