using UnityEngine;

namespace NexusLink.Moonlake.HeroSlice
{
    /// <summary>
    /// Prototype movement validation only: walks the companion between scene
    /// anchors so Play Mode can demonstrate walkable surfaces, blocking
    /// colliders, foot grounding and billboard direction switching.
    ///
    /// This is NOT a navigation authority and NOT gameplay. Real navigation,
    /// state and save data belong to this project's gameplay systems, which
    /// replace this probe once they exist.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MoonlakeWalkableProbe : MonoBehaviour
    {
        public MoonlakeCompanionBillboard billboard;
        public Transform[] waypoints = new Transform[0];
        public float speed = 1.6f;
        public float arriveDistance = 0.18f;
        public bool loop = true;

        [Tooltip("Blocking colliders that must stop the probe.")]
        public LayerMask blockingMask = 0;
        public float bodyRadius = 0.32f;

        int _index;
        bool _blocked;

        public int CurrentWaypoint { get { return _index; } }
        public bool IsBlocked { get { return _blocked; } }

        /// <summary>
        /// Tells the billboard whether to draw a walk cycle or the resting pose.
        /// Presentation input only — it carries no gameplay meaning.
        /// </summary>
        void SetMoving(bool moving)
        {
            if (billboard != null) billboard.isMoving = moving;
        }

        void Update()
        {
            if (waypoints == null || waypoints.Length == 0) { SetMoving(false); return; }
            var target = waypoints[Mathf.Clamp(_index, 0, waypoints.Length - 1)];
            if (target == null) { SetMoving(false); return; }

            var here = transform.position;
            var to = target.position - here;
            to.y = 0f;
            var dist = to.magnitude;

            if (dist <= arriveDistance)
            {
                SetMoving(false);
                _index++;
                if (_index >= waypoints.Length) _index = loop ? 0 : waypoints.Length - 1;
                return;
            }

            var dir = to / Mathf.Max(dist, 1e-5f);
            var step = speed * Time.deltaTime;

            // Collider-based walkable test: refuse to enter blocking volumes.
            _blocked = Physics.SphereCast(here + Vector3.up * 0.5f, bodyRadius, dir,
                                          out _, step + bodyRadius, blockingMask,
                                          QueryTriggerInteraction.Collide);
            if (_blocked)
            {
                SetMoving(false);
                _index++;
                if (_index >= waypoints.Length) _index = loop ? 0 : waypoints.Length - 1;
                return;
            }

            transform.position = here + dir * step;
            SetMoving(true);
            if (billboard != null)
                billboard.headingDegrees = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        }
    }
}
