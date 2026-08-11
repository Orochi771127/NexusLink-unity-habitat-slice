using System.Collections.Generic;
using UnityEngine;

namespace NexusLink.Tools
{
    /// <summary>
    /// Draws where the portrait crop actually falls on the ground, and calls out
    /// anything placed outside it.
    ///
    /// This exists because the same mistake happened three times while building
    /// Moonlake and the Orbit arena: scenery positioned by eye in the scene view
    /// that never appeared in the shipped 9:16 crop. The habitat's distant
    /// ridges, the arena's framing cliffs and its stone lanterns were all placed
    /// well outside the frame before anyone noticed, because the editor's own
    /// viewport is far wider than the game's.
    ///
    /// The crop is narrow near the camera and opens with depth, so "it looks fine
    /// in the scene view" is not evidence. Attach this to a scene, point it at the
    /// hero camera, and place against the drawn footprint instead.
    ///
    /// SCOPE NOTE: an authoring aid. It draws gizmos and never modifies anything.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class PortraitFramingGuide : MonoBehaviour
    {
        [Header("Framing")]
        [Tooltip("Camera the crop is judged against. Falls back to Camera.main.")]
        public Camera targetCamera;

        [Tooltip("Output aspect. 1080x1920 portrait by default.")]
        public Vector2 outputResolution = new Vector2(1080f, 1920f);

        [Tooltip("Ground plane height the footprint is projected onto.")]
        public float groundHeight;

        [Header("Depth rulers")]
        public float nearDepth = -12f;
        public float farDepth = 30f;
        public int rulerCount = 7;

        [Header("Off-frame check")]
        [Tooltip("Root whose renderers are tested against the crop. Leave empty to skip.")]
        public Transform inspectRoot;

        [Header("Colours")]
        public Color footprintColor = new Color(0.35f, 0.95f, 1f, 0.9f);
        public Color rulerColor = new Color(0.35f, 0.95f, 1f, 0.35f);
        public Color offFrameColor = new Color(1f, 0.35f, 0.35f, 0.95f);

        Camera ResolveCamera()
        {
            return targetCamera != null ? targetCamera : Camera.main;
        }

        float Aspect
        {
            get
            {
                if (outputResolution.y <= 0f) return 0.5625f;
                return outputResolution.x / outputResolution.y;
            }
        }

        /// <summary>
        /// Half-width of the crop on the ground plane at a given world z, or a
        /// negative value when the ground is not visible at that depth.
        /// </summary>
        public float HalfWidthAt(float worldZ)
        {
            var camera = ResolveCamera();
            if (camera == null) return -1f;

            var origin = camera.transform.position;
            var halfV = camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            var halfH = Mathf.Atan(Mathf.Tan(halfV) * Aspect);

            var forwardDistance = Mathf.Abs(worldZ - origin.z);
            var height = origin.y - groundHeight;
            var range = Mathf.Sqrt(forwardDistance * forwardDistance + height * height);
            return range * Mathf.Tan(halfH);
        }

        void OnDrawGizmosSelected()
        {
            DrawGuide(true);
        }

        void OnDrawGizmos()
        {
            DrawGuide(false);
        }

        void DrawGuide(bool detailed)
        {
            var camera = ResolveCamera();
            if (camera == null) return;

            var previousAspect = camera.aspect;
            camera.aspect = Aspect;

            DrawFootprint(camera, detailed);
            if (detailed && inspectRoot != null) DrawOffFrameRenderers(camera);

            camera.aspect = previousAspect;
        }

        void DrawFootprint(Camera camera, bool detailed)
        {
            Gizmos.color = footprintColor;

            var left = new List<Vector3>();
            var right = new List<Vector3>();
            const int samples = 24;

            for (var i = 0; i <= samples; i++)
            {
                var v = i / (float)samples;
                Vector3 l, r;
                if (!TryGroundHit(camera, 0f, v, out l)) continue;
                if (!TryGroundHit(camera, 1f, v, out r)) continue;
                left.Add(l);
                right.Add(r);
            }

            for (var i = 1; i < left.Count; i++)
            {
                Gizmos.DrawLine(left[i - 1], left[i]);
                Gizmos.DrawLine(right[i - 1], right[i]);
            }
            if (left.Count > 0)
            {
                Gizmos.DrawLine(left[0], right[0]);
                Gizmos.DrawLine(left[left.Count - 1], right[right.Count - 1]);
            }

            if (!detailed) return;

            // Depth rulers, so the width at a given z can be read off directly.
            Gizmos.color = rulerColor;
            var count = Mathf.Max(2, rulerCount);
            for (var i = 0; i < count; i++)
            {
                var z = Mathf.Lerp(nearDepth, farDepth, i / (float)(count - 1));
                var halfWidth = HalfWidthAt(z);
                if (halfWidth <= 0f) continue;

                var a = new Vector3(-halfWidth, groundHeight, z);
                var b = new Vector3(halfWidth, groundHeight, z);
                Gizmos.DrawLine(a, b);
#if UNITY_EDITOR
                UnityEditor.Handles.color = footprintColor;
                UnityEditor.Handles.Label(b + new Vector3(0.4f, 0f, 0f),
                    string.Format("z {0:F0}   width {1:F1}", z, halfWidth * 2f));
#endif
            }
        }

        bool TryGroundHit(Camera camera, float viewportX, float viewportY, out Vector3 point)
        {
            point = Vector3.zero;
            var ray = camera.ViewportPointToRay(new Vector3(viewportX, viewportY, 0f));
            if (Mathf.Abs(ray.direction.y) < 1e-5f) return false;
            var t = (groundHeight - ray.origin.y) / ray.direction.y;
            if (t <= 0f) return false;
            point = ray.origin + ray.direction * t;
            return true;
        }

        /// <summary>
        /// Boxes anything under the inspect root that the crop misses entirely.
        /// This is the check that would have caught the ridges, the cliffs and
        /// the lanterns before they were built invisible.
        /// </summary>
        void DrawOffFrameRenderers(Camera camera)
        {
            Gizmos.color = offFrameColor;
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            foreach (var renderer in inspectRoot.GetComponentsInChildren<Renderer>())
            {
                if (renderer is ParticleSystemRenderer) continue;
                if (GeometryUtility.TestPlanesAABB(planes, renderer.bounds)) continue;

                Gizmos.DrawWireCube(renderer.bounds.center, renderer.bounds.size);
                Gizmos.DrawLine(renderer.bounds.center, camera.transform.position);
            }
        }

        /// <summary>
        /// Counts renderers under the inspect root that fall outside the crop.
        /// Cheap enough to call from a build step or a test.
        /// </summary>
        public int CountOffFrame(out int total)
        {
            total = 0;
            var camera = ResolveCamera();
            if (camera == null || inspectRoot == null) return 0;

            var previousAspect = camera.aspect;
            camera.aspect = Aspect;

            // Bounds against the frustum planes, not the centre point: a wide
            // object can have its centre outside the crop while most of it is
            // plainly visible, and counting those as failures buries the props
            // that really are off screen.
            var offFrame = 0;
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            foreach (var renderer in inspectRoot.GetComponentsInChildren<Renderer>())
            {
                if (renderer is ParticleSystemRenderer) continue;
                total++;
                if (!GeometryUtility.TestPlanesAABB(planes, renderer.bounds)) offFrame++;
            }

            camera.aspect = previousAspect;
            return offFrame;
        }
    }
}
