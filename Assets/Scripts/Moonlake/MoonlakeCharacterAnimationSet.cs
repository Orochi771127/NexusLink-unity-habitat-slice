using System;
using UnityEngine;

namespace NexusLink.Moonlake.HeroSlice
{
    /// <summary>
    /// Sliced sprite frames for one Nexus Link character, generated from that
    /// character's approved <c>metadata/animations.json</c> by
    /// <c>NexusLinkCharacterSpriteImporter</c>.
    ///
    /// SCOPE NOTE: presentation only. This asset carries frames, frame rate and
    /// loop policy so a billboard can draw the character. It deliberately holds
    /// NO relationship, bond, trust, evolution, progression, chapter, Raphael or
    /// save state.
    ///
    /// Which animation is emotionally appropriate at any moment is a gameplay
    /// decision, made by this project's own systems. The animation ids come from
    /// the shared 29-animation catalog, so the web build's behaviour reads
    /// directly across as a reference.
    /// </summary>
    public sealed class MoonlakeCharacterAnimationSet : ScriptableObject
    {
        /// <summary>One animation id from the shared 29-animation catalog.</summary>
        [Serializable]
        public sealed class Clip
        {
            [Tooltip("Shared animation_id, e.g. idle_calm / front_walk / touch_reject.")]
            public string animationId;

            [Tooltip("Catalog category: emotion / movement / touch / battle / daily / habitat / special / micro.")]
            public string category;

            [Tooltip("Frames in sheet order (left to right, top to bottom).")]
            public Sprite[] frames = new Sprite[0];

            public float fps = 8f;
            public bool loop = true;

            public bool HasFrames { get { return frames != null && frames.Length > 0; } }
        }

        [Tooltip("Character folder id, e.g. greyshade-cat.")]
        public string characterId;

        [Tooltip("Source sheet resolution the importer consumed, for provenance only.")]
        public int masterFrameSize = 512;

        public Clip[] clips = new Clip[0];

        /// <summary>Returns the clip for an animation id, or null when absent.</summary>
        public Clip Find(string animationId)
        {
            if (string.IsNullOrEmpty(animationId) || clips == null) return null;
            for (var i = 0; i < clips.Length; i++)
            {
                var clip = clips[i];
                if (clip != null && string.Equals(clip.animationId, animationId, StringComparison.Ordinal))
                    return clip;
            }
            return null;
        }

        /// <summary>
        /// Returns the first clip that exists from <paramref name="animationIds"/>.
        /// Lets callers express a graceful fallback chain (for example a specific
        /// walk direction, then a generic walk, then a calm idle) without any
        /// caller needing to know which characters are fully authored yet.
        /// </summary>
        public Clip FindFirst(params string[] animationIds)
        {
            if (animationIds == null) return null;
            for (var i = 0; i < animationIds.Length; i++)
            {
                var clip = Find(animationIds[i]);
                if (clip != null && clip.HasFrames) return clip;
            }
            return null;
        }
    }
}
