using UnityEngine;

namespace ContentHouse.Enemy
{
    /// <summary>Simple patrol marker.</summary>
    public class PatrolWaypoint : MonoBehaviour
    {
        public float PauseSeconds = 0.5f;
        [Tooltip("Waypoints with this flag get a slow look-around pause.")]
        public bool Inspect;
    }
}
