using System.Collections.Generic;
using UnityEngine;

namespace NexusLink.Moonlake.HeroSlice
{
    /// <summary>
    /// Fades foreground occluders that come between the fixed hero camera and
    /// the companion, so the 2D companion stays readable behind 3D foliage.
    ///
    /// Presentation only -- no gameplay, save or progression state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MoonlakeOccluderFade : MonoBehaviour
    {
        public Camera heroCamera;
        public Transform subject;

        [Tooltip("Renderers eligible to fade (foreground framing vegetation, props).")]
        public List<Renderer> occluders = new List<Renderer>();

        [Range(0f, 1f)] public float fadedAlpha = 0.32f;
        public float fadeSpeed = 6f;
        public float probeRadius = 0.55f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int SurfaceId = Shader.PropertyToID("_Surface");

        readonly Dictionary<Renderer, float> _current = new Dictionary<Renderer, float>();
        readonly HashSet<Renderer> _blocking = new HashSet<Renderer>();
        MaterialPropertyBlock _mpb;

        void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            foreach (var r in occluders) if (r != null) _current[r] = 1f;
        }

        void LateUpdate()
        {
            if (heroCamera == null || subject == null || _mpb == null) return;

            _blocking.Clear();
            var origin = heroCamera.transform.position;
            var dir = subject.position - origin;
            var dist = dir.magnitude;
            if (dist > 0.001f)
            {
                var hits = Physics.SphereCastAll(origin, probeRadius, dir.normalized,
                                                 dist, ~0, QueryTriggerInteraction.Collide);
                foreach (var h in hits)
                {
                    var r = h.collider.GetComponentInParent<Renderer>();
                    if (r != null && _current.ContainsKey(r)) _blocking.Add(r);
                }
            }

            foreach (var r in occluders)
            {
                if (r == null) continue;
                var want = _blocking.Contains(r) ? fadedAlpha : 1f;
                var have = _current.ContainsKey(r) ? _current[r] : 1f;
                var next = Mathf.MoveTowards(have, want, fadeSpeed * Time.deltaTime);
                _current[r] = next;

                r.GetPropertyBlock(_mpb);
                var c = r.sharedMaterial != null && r.sharedMaterial.HasProperty(BaseColorId)
                    ? r.sharedMaterial.GetColor(BaseColorId)
                    : Color.white;
                c.a = next;
                _mpb.SetColor(BaseColorId, c);
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
