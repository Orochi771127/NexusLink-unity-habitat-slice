using UnityEngine;

namespace NexusLink.Moonlake.HeroSlice
{
    /// <summary>
    /// Marks one of the five Heart-Core Orbit entry points on the Moonlake path
    /// and reports when the companion is standing close enough to use it.
    ///
    /// Proximity is a plain distance test rather than a physics trigger. The
    /// companion's collider is already a trigger, and two triggers only generate
    /// events when one of them carries a Rigidbody — adding one purely so a
    /// signpost can notice someone walked up to it is not a trade worth making.
    ///
    /// SCOPE NOTE: detection only. Whether a stage is unlocked, what it costs and
    /// what it awards are gameplay decisions owned elsewhere. Moonlake Camp is the
    /// daily habitat layer and is deliberately not itself an Orbit stage.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MoonlakePathNodeBeacon : MonoBehaviour
    {
        /// <summary>The beacon the subject is currently standing in, if any.</summary>
        public static MoonlakePathNodeBeacon Current { get; private set; }

        [Tooltip("Stable id, e.g. star_grove. Matches the shared path taxonomy.")]
        public string pathNodeId;

        [Tooltip("Player-visible short name, e.g. 星林.")]
        public string displayName;

        [Tooltip("Scene to open when this node is used. Empty means not built yet.")]
        public string targetSceneName;

        [Tooltip("Who this beacon watches for.")]
        public Transform subject;

        public float radius = 1.6f;

        bool _inside;

        public bool IsSubjectInside { get { return _inside; } }

        void OnDisable()
        {
            if (Current == this) Current = null;
            _inside = false;
        }

        void Update()
        {
            if (subject == null) return;

            var flatDelta = subject.position - transform.position;
            flatDelta.y = 0f;
            var inside = flatDelta.sqrMagnitude <= radius * radius;
            if (inside == _inside) return;

            _inside = inside;
            if (inside)
            {
                Current = this;
                Debug.LogFormat("[Moonlake] Entered path node {0} ({1}).", displayName, pathNodeId);
            }
            else if (Current == this)
            {
                Current = null;
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.color = _inside ? new Color(0.35f, 0.9f, 1f, 0.9f) : new Color(0.35f, 0.9f, 1f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
