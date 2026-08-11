using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NexusLink.Moonlake.HeroSlice
{
    /// <summary>
    /// Switches Moonlake between its two cameras.
    ///
    /// The habitat has to serve two jobs that want opposite things. The owner
    /// reference locks a fixed long-lens diorama framing, and every QA and
    /// marketing capture is judged against that crop, so it cannot move. Actually
    /// playing the habitat in the Ragnarok / Octopath idiom needs a camera that
    /// follows the character. Rather than compromise one into the other, both
    /// exist and this component decides which is live.
    ///
    /// SCOPE NOTE: presentation only. Choosing a camera is a viewing decision.
    /// No relationship, bond, evolution, progression, chapter, Raphael or save
    /// state is read or written here -- that belongs to this project's gameplay
    /// systems.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class MoonlakeCameraRig : MonoBehaviour
    {
        public enum Mode
        {
            /// <summary>Fixed reference framing. Used for previews and QA.</summary>
            HeroDiorama = 0,
            /// <summary>Follow camera for actually moving around the habitat.</summary>
            Play = 1
        }

        [Header("Cameras")]
        public Camera heroCamera;
        public Camera playCamera;

        [Header("Consumers that need the live camera")]
        [Tooltip("Billboards resolve their facing against whichever camera is live.")]
        public MoonlakeCompanionBillboard companionBillboard;

        [Tooltip("Foreground fade traces from the live camera to the subject.")]
        public MoonlakeOccluderFade occluderFade;

        [Header("Who drives the companion")]
        [Tooltip("Ambient wander, used under the fixed framing so captures have life.")]
        public MoonlakeWalkableProbe ambientProbe;

        [Tooltip("Player input, used in play mode.")]
        public MoonlakeCompanionController playerController;

        [Header("Shared post-processing")]
        [Tooltip("Global day volume. Depth of field is refocused per camera.")]
        public Volume dayVolume;

        [Tooltip("Subject the play camera frames, used to derive its focus distance.")]
        public Transform focusSubject;

        [Tooltip("What the fixed hero camera is aimed at, in world space.")]
        public Vector3 heroFocusTarget = new Vector3(0f, 0.42f, 3f);

        [SerializeField] Mode _mode = Mode.HeroDiorama;

        public Mode CurrentMode { get { return _mode; } }

        public Camera ActiveCamera
        {
            get { return _mode == Mode.Play && playCamera != null ? playCamera : heroCamera; }
        }

        bool _applyQueued;

        void OnEnable()
        {
            Apply();
        }

        void OnValidate()
        {
            // Applying straight from OnValidate toggles camera enable state and
            // tags mid-validation, which Unity routes through SendMessage and
            // rejects outright. Queue it for the next tick instead.
            _applyQueued = true;
        }

        void Update()
        {
            if (!_applyQueued) return;
            _applyQueued = false;
            Apply();
        }

        public void SetMode(Mode mode)
        {
            _mode = mode;
            Apply();
        }

        [ContextMenu("Use Hero Diorama Camera")]
        public void UseHeroCamera()
        {
            SetMode(Mode.HeroDiorama);
        }

        [ContextMenu("Use Play Camera")]
        public void UsePlayCamera()
        {
            SetMode(Mode.Play);
        }

        void Apply()
        {
            var play = _mode == Mode.Play && playCamera != null;

            if (heroCamera != null) heroCamera.enabled = !play;
            if (playCamera != null) playCamera.enabled = play;

            var live = ActiveCamera;
            if (live == null) return;

            // Camera.main resolves through the MainCamera tag, so the tag has to
            // move with the switch or anything falling back to Camera.main keeps
            // pointing at the camera that is now off.
            if (heroCamera != null)
                heroCamera.gameObject.tag = play ? "Untagged" : "MainCamera";
            if (playCamera != null)
                playCamera.gameObject.tag = play ? "MainCamera" : "Untagged";

            if (companionBillboard != null) companionBillboard.targetCamera = live;
            if (occluderFade != null) occluderFade.heroCamera = live;

            // Exactly one thing may move the companion. Under the fixed framing
            // that is the ambient wander, so previews are not of a statue; in play
            // mode it is the player. Leaving both on makes them fight.
            if (ambientProbe != null) ambientProbe.enabled = !play;
            if (playerController != null)
            {
                playerController.enabled = play;
                playerController.viewCamera = live;
            }

            RefocusDepthOfField(live, play);
        }

        /// <summary>
        /// Both cameras share one global volume, but they sit at very different
        /// distances from their subject: the hero camera frames the plaza from
        /// about 33 m, the play camera trails the companion at about 20 m. Leaving
        /// the hero's focus distance in place throws the entire play view out of
        /// focus. Play mode also drops to a far tamer lens — heavy bokeh sells a
        /// miniature in a still, and gets in the way when you are moving around.
        /// </summary>
        void RefocusDepthOfField(Camera live, bool play)
        {
            if (dayVolume == null || dayVolume.sharedProfile == null) return;

            DepthOfField depthOfField;
            if (!dayVolume.sharedProfile.TryGet(out depthOfField)) return;

            var subject = play && focusSubject != null
                ? focusSubject.position + Vector3.up * 0.7f
                : heroFocusTarget;
            var distance = Vector3.Distance(live.transform.position, subject);

            depthOfField.focusDistance.overrideState = true;
            depthOfField.focusDistance.value = distance;
            depthOfField.focalLength.overrideState = true;
            depthOfField.focalLength.value = play ? 95f : 220f;
            depthOfField.aperture.overrideState = true;
            depthOfField.aperture.value = play ? 5.6f : 2.2f;
        }
    }
}
