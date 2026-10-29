using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;       // NavMeshAgent

namespace ContentHouse.Enemy
{
    /// <summary>
    /// Intruder AI state machine (spec: EnemyAI.cs - Patrol 1.8 / Investigate 2.5 /
    /// Pursue 4.2 m/s, 0.5 s line-of-sight buffer, hearing via NoiseSystem).
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyAI : MonoBehaviour
    {
        public enum AIState { Inactive, Patrol, Investigate, Pursue }

        public NavMeshAgent Agent;
        public Light Flashlight;              // enemy flashlight cone
        public List<PatrolWaypoint> GroundRoute = new List<PatrolWaypoint>();
        public List<PatrolWaypoint> UpperRoute = new List<PatrolWaypoint>();

        public AIState State { get; private set; } = AIState.Inactive;
        public bool Active => State != AIState.Inactive;

        List<PatrolWaypoint> route;
        int wpIndex;
        float wpPause;
        Vector3 investigatePos;
        float investigateTimer;
        bool targetIsHideSpot;
        Vector3 lastKnownPlayerPos;
        float sightTimer;
        float lostTimer;
        float doorCheckTimer;
        float creakTimer;
        float repathTimer;

        const float PlayerEyeOffset = 0.5f;

        void Awake()
        {
            if (Agent == null) Agent = GetComponent<NavMeshAgent>();
            Agent.speed = Core.GameConfig.PatrolSpeed;
            Agent.angularSpeed = 360f;
            Agent.acceleration = 12f;
            Agent.stoppingDistance = 0.25f;
        }

        void OnEnable() { Core.NoiseSystem.NoiseReported += OnNoise; }
        void OnDisable() { Core.NoiseSystem.NoiseReported -= OnNoise; }

        // ---------- lifecycle ----------

        public void Activate(Vector3 position, bool upperRoute)
        {
            gameObject.SetActive(true);
            Agent.Warp(position);
            route = upperRoute && UpperRoute.Count > 0 ? UpperRoute : GroundRoute;
            wpIndex = 0;
            State = AIState.Patrol;
            Agent.isStopped = false;
            Agent.speed = Core.GameConfig.PatrolSpeed;
            if (route != null && route.Count > 0)
                Agent.SetDestination(route[0].transform.position);
        }

        public void SetRoute(bool upper)
        {
            route = upper && UpperRoute.Count > 0 ? UpperRoute : GroundRoute;
            wpIndex = 0;
            if (State == AIState.Patrol && route.Count > 0)
                Agent.SetDestination(route[0].transform.position);
        }

        /// <summary>Walk to a position and stand there silently (scripted closet beat).</summary>
        public void SendToSilentGuard(Vector3 pos)
        {
            State = AIState.Investigate;
            investigatePos = pos;
            investigateTimer = 8f;          // long scripted pause
            targetIsHideSpot = true;        // loud noise here = caught
            Agent.isStopped = false;
            Agent.speed = Core.GameConfig.PatrolSpeed * 0.8f;
            Agent.SetDestination(pos);
            if (Flashlight != null) Flashlight.enabled = true;
        }

        /// <summary>Park silently at a spot (cutscene use, e.g. facing the CCTV cam).</summary>
        public void SetSilentAt(Vector3 pos)
        {
            State = AIState.Inactive;       // brain off during cutscene
            Agent.isStopped = true;
            Agent.updatePosition = false;
            transform.position = pos;
            if (Flashlight != null) Flashlight.enabled = false;
        }

        /// <summary>Death respawn: snap back to patrol start.</summary>
        public void ResetForRespawn()
        {
            State = AIState.Patrol;
            sightTimer = 0f;
            lostTimer = 0f;
            targetIsHideSpot = false;
            Agent.speed = Core.GameConfig.PatrolSpeed;
            route = GroundRoute.Count > 0 ? GroundRoute : route;
            wpIndex = 0;
            if (route != null && route.Count > 0)
                transform.position = route[0].transform.position;
            if (Flashlight != null) Flashlight.enabled = false;
        }

        // ---------- perception ----------

        void OnNoise(Vector3 pos, float audibleRadius)
        {
            if (!Active) return;
            float dist = Vector3.Distance(transform.position, pos);
            if (dist > audibleRadius) return;

            var gm = Core.GameManager.Instance;
            var player = gm != null ? gm.Player : null;

            if (player != null && player.IsHiding)
            {
                // Mic noise / commotion at a hiding spot: lock onto it.
                targetIsHideSpot = true;
                Investigate(player.transform.position);
            }
            else if (State != AIState.Pursue)
            {
                targetIsHideSpot = false;
                Investigate(pos);
            }
        }

        bool CanSeePlayer(out Vector3 hitPos)
        {
            hitPos = Vector3.zero;
            var gm = Core.GameManager.Instance;
            if (gm == null || gm.Player == null) return false;
            var player = gm.Player;
            if (player.IsHiding) return false;

            var eye = transform.position + Vector3.up * 1.9f;
            var target = player.transform.position + Vector3.up * 1.2f;
            var toPlayer = target - eye;
            float range = Core.GameConfig.SightRange;
            if (gm.Power != null && gm.Power.BlackoutActive) range *= 0.6f;
            if (player.IsCrouching) range *= 0.75f;
            if (toPlayer.magnitude > range) return false;

            if (Vector3.Angle(transform.forward, toPlayer) > Core.GameConfig.SightHalfAngle) return false;

            RaycastHit hit;
            if (Physics.Raycast(eye, toPlayer.normalized, out hit, toPlayer.magnitude + 0.2f,
                ~0, QueryTriggerInteraction.Ignore))   // door zones are triggers - never block sight
            {
                if (hit.collider.transform.root == player.transform.root)
                {
                    hitPos = target;
                    return true;
                }
                // Something else blocks line of sight.
                return false;
            }
            hitPos = target;
            return true;
        }

        // ---------- brain ----------

        void Update()
        {
            if (!Active) return;
            if (Agent == null) return;

            ForceDoorsNearby();
            PlayMovementCreaks();

            Vector3 seen;
            bool sees = CanSeePlayer(out seen);

            switch (State)
            {
                case AIState.Patrol:
                    UpdatePatrol(sees, seen);
                    break;
                case AIState.Investigate:
                    UpdateInvestigate(sees, seen);
                    break;
                case AIState.Pursue:
                    UpdatePursue(sees, seen);
                    break;
            }
        }

        void UpdatePatrol(bool sees, Vector3 seen)
        {
            if (Flashlight != null) Flashlight.enabled = false;
            Agent.speed = Core.GameConfig.PatrolSpeed;

            if (sees)
            {
                sightTimer += Time.deltaTime;
                if (sightTimer >= Core.GameConfig.SightBufferSeconds)
                {
                    Pursue(seen);
                    return;
                }
            }
            else sightTimer = Mathf.Max(0f, sightTimer - Time.deltaTime);

            if (wpPause > 0f)
            {
                wpPause -= Time.deltaTime;
                Agent.isStopped = true;
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.Euler(0f, transform.eulerAngles.y + 90f * Mathf.Sin(Time.time * 1.5f), 0f), 2f * Time.deltaTime);
                return;
            }
            Agent.isStopped = false;

            if (route == null || route.Count == 0) return;
            var wp = route[wpIndex];
            Agent.SetDestination(wp.transform.position);
            if (!Agent.pathPending && Agent.remainingDistance < Agent.stoppingDistance + 0.2f)
            {
                wpIndex = (wpIndex + 1) % route.Count;
                wpPause = wp.PauseSeconds + (wp.Inspect ? 2.2f : 0f);
            }
        }

        void UpdateInvestigate(bool sees, Vector3 seen)
        {
            if (Flashlight != null) Flashlight.enabled = true;
            Agent.speed = Core.GameConfig.InvestigateSpeed;

            if (sees)
            {
                sightTimer += Time.deltaTime;
                if (sightTimer >= Core.GameConfig.SightBufferSeconds)
                {
                    Pursue(seen);
                    return;
                }
            }
            else sightTimer = Mathf.Max(0f, sightTimer - Time.deltaTime);

            // Reached the noise source?
            float dist = Vector3.Distance(transform.position, investigatePos);
            if (!Agent.pathPending && Agent.remainingDistance < Agent.stoppingDistance + 0.3f)
            {
                investigateTimer -= Time.deltaTime;
                Agent.isStopped = true;
                // Slow sweep of the flashlight.
                transform.Rotate(0f, 65f * Time.deltaTime, 0f);
                if (investigateTimer <= 0f)
                {
                    if (targetIsHideSpot && dist < 2.6f)
                    {
                        // He is right at the closet/bed: found the player.
                        Core.GameManager.Instance.OnPlayerCaught();
                    }
                    targetIsHideSpot = false;
                    State = AIState.Patrol;
                    Agent.isStopped = false;
                    return;
                }
            }
            else
            {
                Agent.isStopped = false;
                Agent.SetDestination(investigatePos);
            }
        }

        void UpdatePursue(bool sees, Vector3 seen)
        {
            if (Flashlight != null) Flashlight.enabled = true;
            Agent.speed = Core.GameConfig.PursueSpeed;
            Agent.isStopped = false;

            var gm = Core.GameManager.Instance;
            var player = gm != null ? gm.Player : null;
            if (player == null) { State = AIState.Patrol; return; }

            repathTimer -= Time.deltaTime;
            if (repathTimer <= 0f)
            {
                repathTimer = 0.25f;
                Agent.SetDestination(player.transform.position);
            }

            float dist = Vector3.Distance(transform.position, player.transform.position);
            if (dist < Core.GameConfig.CatchDistance)
            {
                Core.GameManager.Instance.OnPlayerCaught();
                return;
            }

            if (sees) { lastKnownPlayerPos = seen; lostTimer = 0f; }
            else
            {
                lostTimer += Time.deltaTime;
                if (lostTimer >= Core.GameConfig.LostPlayerSeconds)
                {
                    targetIsHideSpot = false;
                    Investigate(lastKnownPlayerPos);
                }
            }
        }

        // ---------- transitions ----------

        void Investigate(Vector3 pos)
        {
            State = AIState.Investigate;
            investigatePos = pos;
            investigateTimer = Core.GameConfig.InvestigateScanSeconds;
            Agent.isStopped = false;
            Agent.SetDestination(pos);
            lostTimer = 0f;
        }

        void Pursue(Vector3 pos)
        {
            State = AIState.Pursue;
            lastKnownPlayerPos = pos;
            sightTimer = 0f;
            lostTimer = 0f;
            repathTimer = 0f;
            Agent.isStopped = false;
            Audio.ProceduralAudio.PlayBang(transform.position, 0.6f);
        }

        // ---------- flavor ----------

        void ForceDoorsNearby()
        {
            doorCheckTimer -= Time.deltaTime;
            if (doorCheckTimer > 0f) return;
            doorCheckTimer = 0.5f;
            var hits = Physics.OverlapSphere(transform.position + transform.forward * 1.1f, 0.7f);
            foreach (var h in hits)
            {
                var door = h.GetComponentInParent<Interaction.DoorInteractable>();
                if (door != null && !door.IsOpen)
                {
                    door.ForceOpenForEnemy();
                    break;
                }
            }
        }

        void PlayMovementCreaks()
        {
            if (Agent.velocity.magnitude < 0.3f) return;
            creakTimer -= Time.deltaTime;
            if (creakTimer > 0f) return;
            creakTimer = Random.Range(0.6f, 1.4f);
            // Heavy floorboard creak - audible through walls (3D, generous range).
            Audio.ProceduralAudio.PlayDoorCreak(transform.position);
        }
    }
}

