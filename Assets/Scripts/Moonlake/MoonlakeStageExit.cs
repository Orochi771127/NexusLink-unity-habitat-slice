using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace NexusLink.Moonlake.HeroSlice
{
    /// <summary>
    /// Sends the player back to the habitat they came from. Lives in a stage
    /// scene, and is the other half of <see cref="MoonlakeStageGateway"/>.
    ///
    /// Placeholder plumbing: it proves the loop returns cleanly. Settlement,
    /// rewards and progression writes happen in the real Orbit stage, not here.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MoonlakeStageExit : MonoBehaviour
    {
        [Header("Input")]
        public InputActionAsset inputActions;
        public string exitActionPath = "Player/Interact";

        InputAction _exit;

        void OnEnable()
        {
            if (_exit == null && inputActions != null)
                _exit = inputActions.FindAction(exitActionPath, false);
            if (_exit != null) _exit.Enable();
        }

        void OnDisable()
        {
            if (_exit != null) _exit.Disable();
        }

        void Update()
        {
            if (_exit == null || !_exit.WasPressedThisFrame()) return;

            var target = MoonlakeStageReturn.Consume();
            if (!MoonlakeStageGateway.CanLoad(target))
            {
                Debug.LogWarningFormat("[Moonlake] Return scene '{0}' is not in Build Settings.", target);
                return;
            }
            Debug.LogFormat("[Moonlake] Returning to {0}.", target);
            SceneManager.LoadScene(target);
        }
    }
}
