using UnityEngine;
using UnityEngine.InputSystem;

namespace NexusLink.Moonlake.HeroSlice
{
    /// <summary>
    /// Player-driven locomotion for the companion, in the Ragnarok / Octopath
    /// idiom: a 2D sprite walking a 3D habitat, moved relative to the camera
    /// rather than to its own facing.
    ///
    /// This replaces <see cref="MoonlakeWalkableProbe"/> during play. The probe
    /// stays for the fixed diorama framing, where the companion should wander on
    /// its own for capture purposes.
    ///
    /// SCOPE NOTE: movement only. It reads input and writes a position, a heading
    /// and a moving flag. Bond, trust, progression and save state belong to this
    /// project's gameplay systems, not here.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MoonlakeCompanionController : MonoBehaviour
    {
        [Header("Input")]
        public InputActionAsset inputActions;

        [Tooltip("Action map and action, as they appear in the asset.")]
        public string moveActionPath = "Player/Move";

        [Header("Presentation")]
        public MoonlakeCompanionBillboard billboard;

        [Tooltip("Movement is expressed relative to whichever camera is live.")]
        public Camera viewCamera;

        [Header("Motion")]
        public float speed = 2.4f;
        public float bodyRadius = 0.26f;

        [Tooltip("Volumes the companion must not walk into.")]
        public LayerMask blockingMask = ~0;

        [Header("Grounding")]
        public bool snapToGround = true;
        public LayerMask groundMask = ~0;
        public float groundProbeHeight = 4f;

        InputAction _move;
        bool _ownsAction;

        public bool IsMoving { get; private set; }

        void OnEnable()
        {
            ResolveAction();
            if (_move != null) _move.Enable();
        }

        void OnDisable()
        {
            if (_move != null && _ownsAction) _move.Disable();
            IsMoving = false;
            if (billboard != null) billboard.isMoving = false;
        }

        void ResolveAction()
        {
            if (_move != null || inputActions == null) return;
            _move = inputActions.FindAction(moveActionPath, false);
            if (_move == null)
            {
                Debug.LogWarningFormat(
                    "[Moonlake] Input action '{0}' was not found on {1}; the companion will not respond to input.",
                    moveActionPath, inputActions.name);
                return;
            }
            _ownsAction = true;
        }

        Camera ResolveCamera()
        {
            if (viewCamera != null && viewCamera.isActiveAndEnabled) return viewCamera;
            return Camera.main;
        }

        void Update()
        {
            var camera = ResolveCamera();
            if (_move == null || camera == null) return;

            var input = _move.ReadValue<Vector2>();
            if (input.sqrMagnitude > 1f) input.Normalize();

            if (input.sqrMagnitude < 1e-4f)
            {
                SetMoving(false);
                return;
            }

            // Camera-relative, flattened: pushing up walks away from the viewer
            // whatever angle the camera happens to sit at.
            var forward = camera.transform.forward;
            var right = camera.transform.right;
            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();

            var direction = (forward * input.y + right * input.x).normalized;
            var step = speed * Time.deltaTime;

            RaycastHit blockingHit;
            var blocked = Physics.SphereCast(
                transform.position + Vector3.up * 0.5f, bodyRadius, direction,
                out blockingHit, step + bodyRadius, blockingMask, QueryTriggerInteraction.Ignore);

            if (!blocked)
                transform.position += direction * step;

            ApplyGrounding();
            SetMoving(!blocked);

            if (billboard != null)
                billboard.headingDegrees = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        void ApplyGrounding()
        {
            if (!snapToGround) return;
            var origin = transform.position + Vector3.up * groundProbeHeight;
            RaycastHit hit;
            if (Physics.Raycast(origin, Vector3.down, out hit, groundProbeHeight * 2f,
                                groundMask, QueryTriggerInteraction.Ignore))
            {
                var position = transform.position;
                position.y = hit.point.y;
                transform.position = position;
            }
        }

        void SetMoving(bool moving)
        {
            IsMoving = moving;
            if (billboard != null) billboard.isMoving = moving;
        }
    }
}
