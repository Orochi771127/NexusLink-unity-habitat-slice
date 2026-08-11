using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NexusLink.Moonlake.HeroSlice;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace NexusLink.EditorTools
{
    /// <summary>
    /// Imports approved Nexus Link character sprite sheets into Unity and slices
    /// them straight from each character's <c>metadata/animations.json</c>.
    ///
    /// The metadata is the single source of truth for sheet path, grid layout,
    /// frame count, frame rate, loop policy and anchor, so the importer never
    /// guesses from filenames. That matters because the roster is not internally
    /// consistent: greyshade-cat names its walk sheets <c>walk_front</c> while
    /// the newer companions use <c>front_walk</c>, yet both expose the same
    /// <c>front_walk</c> animation id in metadata.
    ///
    /// SCOPE NOTE: this tool moves and slices art only. It writes no relationship,
    /// bond, evolution, progression or save state, and it never edits the source
    /// repository.
    /// </summary>
    public static class NexusLinkCharacterSpriteImporter
    {
        const string SourceRootPrefKey = "NexusLink.CharacterSourceRoot";
        const string DefaultSourceRoot = @"C:\Users\User\NexusLink_RaphaelAI_Workspace\NexusLink";
        const string DestinationRoot = "Assets/Art/Characters";

        /// <summary>The companion currently staged in the Moonlake hero scene.</summary>
        public const string MoonlakeCompanionId = "greyshade-cat";

        /// <summary>
        /// 512 px master frames map to one world unit, so a sprite's native
        /// height is 1.0 and <c>MoonlakeCompanionBillboard.worldHeight</c> stays
        /// the only place that decides on-screen size.
        /// </summary>
        const int PixelsPerUnit = 512;

        public static string SourceRoot
        {
            get { return EditorPrefs.GetString(SourceRootPrefKey, DefaultSourceRoot); }
            set { EditorPrefs.SetString(SourceRootPrefKey, value); }
        }

        [MenuItem("NexusLink/Characters/Set Character Source Root...")]
        public static void SetSourceRoot()
        {
            var picked = EditorUtility.OpenFolderPanel(
                "Select the NexusLink repository root (the folder containing assets/characters)",
                Directory.Exists(SourceRoot) ? SourceRoot : Application.dataPath,
                string.Empty);
            if (string.IsNullOrEmpty(picked)) return;
            SourceRoot = picked.Replace('/', Path.DirectorySeparatorChar);
            Debug.LogFormat("[NexusLink Characters] Source root set to {0}", SourceRoot);
        }

        [MenuItem("NexusLink/Characters/Import Moonlake Companion (greyshade-cat)")]
        public static void ImportMoonlakeCompanion()
        {
            var set = ImportCharacter(MoonlakeCompanionId);
            if (set != null)
            {
                Selection.activeObject = set;
                EditorGUIUtility.PingObject(set);
            }
        }

        [MenuItem("NexusLink/Characters/Import All Characters")]
        public static void ImportAllCharacters()
        {
            var ids = DiscoverCharacterIds();
            if (ids.Count == 0)
            {
                Debug.LogWarningFormat(
                    "[NexusLink Characters] No characters found under {0}. Use " +
                    "NexusLink/Characters/Set Character Source Root... to point at the repo.",
                    CharactersRoot());
                return;
            }

            var imported = 0;
            var skipped = new List<string>();
            try
            {
                for (var i = 0; i < ids.Count; i++)
                {
                    var id = ids[i];
                    EditorUtility.DisplayProgressBar(
                        "Importing Nexus Link characters",
                        id + " (" + (i + 1) + "/" + ids.Count + ")",
                        (float)i / ids.Count);
                    if (ImportCharacter(id, refreshAssets: false) != null) imported++;
                    else skipped.Add(id);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.LogFormat(
                "[NexusLink Characters] Imported {0}/{1} characters.{2}",
                imported,
                ids.Count,
                skipped.Count > 0 ? " Skipped (no readable metadata): " + string.Join(", ", skipped) : string.Empty);
        }

        static string CharactersRoot()
        {
            return Path.Combine(SourceRoot, Path.Combine("assets", "characters"));
        }

        public static List<string> DiscoverCharacterIds()
        {
            var root = CharactersRoot();
            if (!Directory.Exists(root)) return new List<string>();
            return Directory.GetDirectories(root)
                .Select(Path.GetFileName)
                .Where(id => File.Exists(Path.Combine(root, Path.Combine(id, Path.Combine("metadata", "animations.json")))))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Imports one character. Returns the generated animation set, or null
        /// when the character has no readable metadata.
        /// </summary>
        public static MoonlakeCharacterAnimationSet ImportCharacter(string characterId, bool refreshAssets = true)
        {
            if (string.IsNullOrEmpty(characterId)) throw new ArgumentNullException("characterId");

            var metadataPath = Path.Combine(
                CharactersRoot(),
                Path.Combine(characterId, Path.Combine("metadata", "animations.json")));
            if (!File.Exists(metadataPath))
            {
                Debug.LogWarningFormat("[NexusLink Characters] {0}: no animations.json at {1}", characterId, metadataPath);
                return null;
            }

            JObject metadata;
            try
            {
                metadata = JObject.Parse(File.ReadAllText(metadataPath));
            }
            catch (Exception ex)
            {
                Debug.LogErrorFormat("[NexusLink Characters] {0}: cannot parse animations.json — {1}", characterId, ex.Message);
                return null;
            }

            EnsureFolder(DestinationRoot);
            var characterFolder = DestinationRoot + "/" + characterId;
            EnsureFolder(characterFolder);

            var clips = new List<MoonlakeCharacterAnimationSet.Clip>();
            var missingSheets = new List<string>();

            foreach (var property in metadata.Properties())
            {
                var entry = property.Value as JObject;
                if (entry == null) continue;

                var animationId = Read(entry, "id", property.Name);
                var category = Read(entry, "category", "uncategorised");
                var relativeSheet = Read(entry, "sheet", null);
                if (string.IsNullOrEmpty(relativeSheet)) continue;

                var sourceSheet = ResolveSourcePath(relativeSheet);
                if (!File.Exists(sourceSheet))
                {
                    missingSheets.Add(animationId);
                    continue;
                }

                var rows = Math.Max(1, ReadInt(entry, "rows", 1));
                var columns = Math.Max(1, ReadInt(entry, "columns", 1));
                var frameCount = Math.Max(1, ReadInt(entry, "frameCount", rows * columns));
                var fps = ReadFloat(entry, "fps", 8f);
                var loop = ReadBool(entry, "loop", true);

                EnsureFolder(characterFolder + "/" + category);
                var destinationPath = characterFolder + "/" + category + "/" + Path.GetFileName(sourceSheet);

                // Copy only when the bytes actually changed, so re-running the
                // importer does not force a full reimport of the whole roster.
                if (!FilesAreIdentical(sourceSheet, destinationPath))
                    File.Copy(sourceSheet, destinationPath, true);

                AssetDatabase.ImportAsset(destinationPath, ImportAssetOptions.ForceSynchronousImport);

                var frames = SliceSheet(destinationPath, characterId, animationId, rows, columns, frameCount);
                if (frames == null || frames.Length == 0)
                {
                    missingSheets.Add(animationId + " (slice failed)");
                    continue;
                }

                clips.Add(new MoonlakeCharacterAnimationSet.Clip
                {
                    animationId = animationId,
                    category = category,
                    frames = frames,
                    fps = fps,
                    loop = loop
                });
            }

            if (clips.Count == 0)
            {
                Debug.LogWarningFormat("[NexusLink Characters] {0}: metadata contained no usable sheets.", characterId);
                return null;
            }

            var setPath = characterFolder + "/" + characterId + "_AnimationSet.asset";
            var set = AssetDatabase.LoadAssetAtPath<MoonlakeCharacterAnimationSet>(setPath);
            var created = set == null;
            if (created) set = ScriptableObject.CreateInstance<MoonlakeCharacterAnimationSet>();

            set.characterId = characterId;
            set.masterFrameSize = PixelsPerUnit;
            set.clips = clips.OrderBy(c => c.category, StringComparer.Ordinal)
                             .ThenBy(c => c.animationId, StringComparer.Ordinal)
                             .ToArray();

            if (created) AssetDatabase.CreateAsset(set, setPath);
            EditorUtility.SetDirty(set);

            if (refreshAssets)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.LogFormat(
                "[NexusLink Characters] {0}: {1} animations imported.{2}",
                characterId,
                clips.Count,
                missingSheets.Count > 0 ? " Unavailable: " + string.Join(", ", missingSheets) : string.Empty);

            return set;
        }

        /// <summary>
        /// Slices a grid sheet into foot-anchored sprites. Frames run left to
        /// right then top to bottom, but Unity's sprite rects are measured from
        /// the bottom of the texture, so rows are flipped on the way in.
        /// </summary>
        static Sprite[] SliceSheet(string assetPath, string characterId, string animationId, int rows, int columns, int frameCount)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogErrorFormat("[NexusLink Characters] {0} is not a texture.", assetPath);
                return null;
            }

            int sourceWidth;
            int sourceHeight;
            importer.GetSourceTextureWidthAndHeight(out sourceWidth, out sourceHeight);
            if (sourceWidth <= 0 || sourceHeight <= 0) return null;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = ClampToSupportedSize(Math.Max(sourceWidth, sourceHeight));

            var frameWidth = sourceWidth / columns;
            var frameHeight = sourceHeight / rows;
            var usableFrames = Math.Min(frameCount, rows * columns);

            var factories = new SpriteDataProviderFactories();
            factories.Init();
            var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();

            var existing = provider.GetSpriteRects().ToDictionary(r => r.name, r => r.spriteID);
            var rects = new List<SpriteRect>(usableFrames);
            for (var i = 0; i < usableFrames; i++)
            {
                var column = i % columns;
                var rowFromTop = i / columns;
                var name = characterId + "_" + animationId + "_" + i.ToString("00");

                rects.Add(new SpriteRect
                {
                    name = name,
                    // Reuse the existing GUID when re-importing so any scene or
                    // prefab already referencing this frame keeps its link.
                    spriteID = existing.ContainsKey(name) ? existing[name] : GUID.Generate(),
                    rect = new Rect(
                        column * frameWidth,
                        sourceHeight - (rowFromTop + 1) * frameHeight,
                        frameWidth,
                        frameHeight),
                    alignment = SpriteAlignment.BottomCenter,
                    pivot = new Vector2(0.5f, 0f),
                    border = Vector4.zero
                });
            }

            provider.SetSpriteRects(rects.ToArray());

            var nameFileIds = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            if (nameFileIds != null)
            {
                nameFileIds.SetNameFileIdPairs(
                    rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)).ToArray());
            }

            provider.Apply();
            importer.SaveAndReimport();

            var sprites = AssetDatabase
                .LoadAllAssetRepresentationsAtPath(assetPath)
                .OfType<Sprite>()
                .ToDictionary(s => s.name, s => s);

            var ordered = new Sprite[usableFrames];
            for (var i = 0; i < usableFrames; i++)
            {
                var name = characterId + "_" + animationId + "_" + i.ToString("00");
                Sprite sprite;
                if (!sprites.TryGetValue(name, out sprite))
                {
                    Debug.LogWarningFormat("[NexusLink Characters] {0}: frame {1} missing after slice.", assetPath, name);
                    return null;
                }
                ordered[i] = sprite;
            }
            return ordered;
        }

        /// <summary>Unity only accepts power-of-two max sizes up to 16384.</summary>
        static int ClampToSupportedSize(int longestEdge)
        {
            var size = 32;
            while (size < longestEdge && size < 4096) size *= 2;
            return size;
        }

        static bool FilesAreIdentical(string sourcePath, string destinationPath)
        {
            if (!File.Exists(destinationPath)) return false;
            var source = new FileInfo(sourcePath);
            var destination = new FileInfo(destinationPath);
            return source.Length == destination.Length &&
                   source.LastWriteTimeUtc <= destination.LastWriteTimeUtc;
        }

        /// <summary>
        /// Metadata sheet paths are repo-relative in the form
        /// <c>./assets/characters/&lt;id&gt;/...</c>.
        /// </summary>
        static string ResolveSourcePath(string relativeSheet)
        {
            var trimmed = relativeSheet.Replace('\\', '/').TrimStart('.', '/');
            return Path.Combine(SourceRoot, trimmed.Replace('/', Path.DirectorySeparatorChar));
        }

        static void EnsureFolder(string unityFolderPath)
        {
            if (AssetDatabase.IsValidFolder(unityFolderPath)) return;
            var parent = Path.GetDirectoryName(unityFolderPath).Replace('\\', '/');
            var leaf = Path.GetFileName(unityFolderPath);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static string Read(JObject entry, string key, string fallback)
        {
            var token = entry[key];
            return token != null && token.Type != JTokenType.Null ? token.ToString() : fallback;
        }

        static int ReadInt(JObject entry, string key, int fallback)
        {
            var token = entry[key];
            int parsed;
            if (token == null || token.Type == JTokenType.Null) return fallback;
            return int.TryParse(token.ToString(), out parsed) ? parsed : fallback;
        }

        static float ReadFloat(JObject entry, string key, float fallback)
        {
            var token = entry[key];
            float parsed;
            if (token == null || token.Type == JTokenType.Null) return fallback;
            return float.TryParse(token.ToString(), out parsed) ? parsed : fallback;
        }

        static bool ReadBool(JObject entry, string key, bool fallback)
        {
            var token = entry[key];
            bool parsed;
            if (token == null || token.Type == JTokenType.Null) return fallback;
            return bool.TryParse(token.ToString(), out parsed) ? parsed : fallback;
        }
    }
}
