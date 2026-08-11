using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace NexusLink.Moonlake.HeroSlice
{
    /// <summary>
    /// Remembers which habitat scene a stage was entered from, so the stage can
    /// hand control back without knowing anything about Moonlake.
    /// </summary>
    public static class MoonlakeStageReturn
    {
        public const string DefaultHabitatScene = "Moonlake_HeroZone_v05";

        public static string PendingReturnScene { get; private set; }

        public static void Remember(string sceneName)
        {
            PendingReturnScene = sceneName;
        }

        public static string Consume()
        {
            var scene = string.IsNullOrEmpty(PendingReturnScene)
                ? DefaultHabitatScene
                : PendingReturnScene;
            PendingReturnScene = null;
            return scene;
        }
    }

    /// <summary>
    /// Turns "standing on a path node and pressing Interact" into a scene change.
    ///
    /// This is the seam the Orbit stages will hang off. It deliberately carries no
    /// unlock, cost or reward logic — it only asks whether a scene exists and
    /// opens it, so the loop can be proven before any of that is written.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MoonlakeStageGateway : MonoBehaviour
    {
        [Header("Input")]
        public InputActionAsset inputActions;
        public string interactActionPath = "Player/Interact";

        InputAction _interact;

        void OnEnable()
        {
            if (_interact == null && inputActions != null)
                _interact = inputActions.FindAction(interactActionPath, false);
            if (_interact != null) _interact.Enable();
        }

        void OnDisable()
        {
            if (_interact != null) _interact.Disable();
        }

        void Update()
        {
            if (_interact == null || !_interact.WasPressedThisFrame()) return;

            var beacon = MoonlakePathNodeBeacon.Current;
            if (beacon == null) return;

            if (string.IsNullOrEmpty(beacon.targetSceneName))
            {
                Debug.LogFormat(
                    "[Moonlake] {0} ({1}) has no stage scene assigned yet.",
                    beacon.displayName, beacon.pathNodeId);
                return;
            }

            if (!CanLoad(beacon.targetSceneName))
            {
                Debug.LogWarningFormat(
                    "[Moonlake] Scene '{0}' for {1} is not in Build Settings, so it cannot be opened.",
                    beacon.targetSceneName, beacon.displayName);
                return;
            }

            MoonlakeStageReturn.Remember(SceneManager.GetActiveScene().name);
            Debug.LogFormat("[Moonlake] Entering {0} ({1}).", beacon.displayName, beacon.pathNodeId);
            SceneManager.LoadScene(beacon.targetSceneName);
        }

        /// <summary>
        /// A scene only loads by name if it is in Build Settings, and failing that
        /// check quietly would look like the gateway is broken.
        /// </summary>
        public static bool CanLoad(string sceneName)
        {
            for (var i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                var path = SceneUtility.GetScenePathByBuildIndex(i);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == sceneName) return true;
            }
            return false;
        }
    }
}
