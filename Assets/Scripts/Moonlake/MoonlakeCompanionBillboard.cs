using UnityEngine;

namespace NexusLink.Moonlake.HeroSlice
{
    /// <summary>
    /// Runtime-derived 2.5D companion presentation for the Moonlake HD-2.5D
    /// Hero Slice validation scene.
    ///
    /// SCOPE NOTE: presentation only. This component deliberately contains NO
    /// relationship, evolution, progression, Raphael, chapter or save logic.
    /// Companion identity, canon and state remain owned by the Nexus Link
    /// product; Unity owns only this SpriteRenderer configuration.
    ///
    /// Direction frames are the APPROVED greyshade-cat illustrated walk sheets
    /// (front / back / left / right, 8 frames each). No direction frame is
    /// invented or generated here.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class MoonlakeCompanionBillboard : MonoBehaviour
    {
        public enum Facing { Front = 0, Back = 1, Left = 2, Right = 3 }

        [Header("Camera")]
        [Tooltip("Leave empty to use Camera.main.")]
        public Camera targetCamera;

        [Header("Approved direction frames")]
        public Sprite[] frontFrames = new Sprite[0];
        public Sprite[] backFrames = new Sprite[0];
        public Sprite[] leftFrames = new Sprite[0];
        public Sprite[] rightFrames = new Sprite[0];

        [Header("Presentation")]
        public SpriteRenderer spriteRenderer;
        public Transform contactShadow;

        [Tooltip("World height of the sprite in metres, foot-anchored.")]
        public float worldHeight = 1.5f;

        public float framesPerSecond = 8f;
        public bool animate = true;

        [Header("Facing")]
        [Tooltip("World heading in degrees the companion body is facing (0 = +Z).")]
        public float headingDegrees;

        [Header("Grounding")]
        [Tooltip("Snap the root down onto walkable colliders each frame.")]
        public bool snapToGround = true;
        public LayerMask groundMask = ~0;
        public float groundProbeHeight = 4f;

        float _timer;
        int _frame;
        Facing _facing = Facing.Front;

        public Facing CurrentFacing { get { return _facing; } }
        public int CurrentFrame { get { return _frame; } }

        Camera ResolveCamera()
        {
            if (targetCamera != null) return targetCamera;
            return Camera.main;
        }

        void Reset()
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        void LateUpdate()
        {
            var cam = ResolveCamera();
            if (cam == null || spriteRenderer == null) return;

            ApplyGrounding();
            ApplyBillboard(cam);
            ApplyFacing(cam);
            ApplyAnimation();
            ApplyScale();
        }

        void ApplyGrounding()
        {
            if (!snapToGround || !Application.isPlaying) return;
            var origin = transform.position + Vector3.up * groundProbeHeight;
            RaycastHit hit;
            if (Physics.Raycast(origin, Vector3.down, out hit, groundProbeHeight * 2f,
                                groundMask, QueryTriggerInteraction.Collide))
            {
                var p = transform.position;
                p.y = hit.point.y;
                transform.position = p;
            }
        }

        /// <summary>
        /// Yaw-only billboard. Rotating on all axes would tip the sprite and
        /// break the foot contact with the ground plane.
        /// </summary>
        void ApplyBillboard(Camera cam)
        {
            var fwd = cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) return;
            var rot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            spriteRenderer.transform.rotation = rot;
            if (contactShadow != null)
            {
                // Shadow stays flat on the ground, independent of the billboard.
                contactShadow.rotation = Quaternion.Euler(90f, 0f, 0f);
            }
        }

        /// <summary>
        /// Choose one of the four approved sheets from the body heading
        /// expressed in the camera's frame of reference (RO convention).
        /// </summary>
        void ApplyFacing(Camera cam)
        {
            var camYaw = Mathf.Atan2(cam.transform.forward.x, cam.transform.forward.z) * Mathf.Rad2Deg;
            var rel = Mathf.DeltaAngle(camYaw, headingDegrees);
            if (rel >= -45f && rel < 45f) _facing = Facing.Back;    // facing away from camera
            else if (rel >= 45f && rel < 135f) _facing = Facing.Right;
            else if (rel >= -135f && rel < -45f) _facing = Facing.Left;
            else _facing = Facing.Front;                             // facing the camera
        }

        Sprite[] ActiveSet()
        {
            switch (_facing)
            {
                case Facing.Back: return backFrames;
                case Facing.Left: return leftFrames;
                case Facing.Right: return rightFrames;
                default: return frontFrames;
            }
        }

        void ApplyAnimation()
        {
            var set = ActiveSet();
            if (set == null || set.Length == 0)
            {
                // Safe single-sprite fallback: never blank the companion.
                if (spriteRenderer.sprite == null && frontFrames != null && frontFrames.Length > 0)
                    spriteRenderer.sprite = frontFrames[0];
                return;
            }

            if (animate && Application.isPlaying && framesPerSecond > 0f)
            {
                _timer += Time.deltaTime;
                var step = 1f / framesPerSecond;
                while (_timer >= step)
                {
                    _timer -= step;
                    _frame++;
                }
            }
            if (_frame >= set.Length || _frame < 0) _frame %= set.Length;
            var next = set[Mathf.Clamp(_frame, 0, set.Length - 1)];
            if (next != null && spriteRenderer.sprite != next) spriteRenderer.sprite = next;
        }

        void ApplyScale()
        {
            var s = spriteRenderer.sprite;
            if (s == null) return;
            var native = s.rect.height / s.pixelsPerUnit;
            if (native <= 0f) return;
            var k = worldHeight / native;
            spriteRenderer.transform.localScale = new Vector3(k, k, k);
        }
    }
}
