using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NexusLink.Moonlake.HeroSlice;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Cinemachine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace NexusLink.EditorTools
{
    /// <summary>
    /// Rebuilds the existing Moonlake_HeroZone_v05 scene in place as a
    /// portrait-first clay/resin 2.5D diorama. The experimental Unified FBX
    /// pack remains available as source material, but it is intentionally not
    /// used to drive layout because its authored scales and offsets do not
    /// match the owner-approved composition.
    /// </summary>
    public static class MoonlakeHd25dSceneBuilder
    {
        const string OutputRoot = "Assets/Art/Moonlake/HD25D_v01";
        const string MaterialRoot = OutputRoot + "/Materials";
        const string MeshRoot = OutputRoot + "/Meshes";
        const string ScenePath = "Assets/Scenes/QA/Moonlake_HeroZone_v05.unity";
        const string BackupFolder = "Assets/Scenes/QA/_Backups";
        const string BackupScenePath = BackupFolder + "/Moonlake_HeroZone_v05.before-hd25d-rebuild.unity";
        const string SceneRootName = "Moonlake_Habitat_HD25D_v01";
        const string PlaceholderStageSceneName = "Orbit_Stage_Placeholder";
        const string CompanionAnimationSetPath =
            "Assets/Art/Characters/greyshade-cat/greyshade-cat_AnimationSet.asset";

        // Composition R2 keeps the camera change deliberately small. The owner
        // reference gains its visual weight from larger midground landmarks,
        // not from scaling the entire diorama (which would overfill the phone
        // foreground and companion safe zone).
        static readonly Vector3 CameraTarget = new Vector3(0f, 0.42f, 3.0f);
        static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();
        static readonly List<Renderer> ForegroundRenderers = new List<Renderer>();
        static readonly List<Renderer> NightEmissionRenderers = new List<Renderer>();
        static readonly List<Light> NightLights = new List<Light>();
        static readonly List<string> AssetPathsUsed = new List<string>();
        static int visualCount;
        static int assetBackedVisualCount;
        static int assetCrystalClusterCount;
        static int shadowCasterCount;

        [Serializable]
        sealed class BuildReport
        {
            public string taskPack;
            public string scene;
            public string sceneRoot;
            public string sourceMode;
            public int proceduralVisuals;
            public int assetBackedVisuals;
            public int assetCrystalClusters;
            public int renderers;
            public int colliders;
            public int shadowCasters;
            public int rootCount;
            public int missingScriptReferences;
            public int weatherControllerCount;
            public int campfireControllerCount;
            public float cameraFieldOfView;
            public Vector3 cameraPosition;
            public Vector3 cameraTarget;
            public string compositionRevision;
            public float plazaWidth;
            public float bridgeLength;
            public float cliffTowerHeight;
            public float cliffCapWidth;
            public float foregroundApronDepth;
            public string[] previews;
            public bool ownerReferenceCompositionApplied;
            public bool existingSceneRebuiltInPlace;
            public bool mobileSafeZoneApplied;
            public bool foregroundOccludersSeparated;
            public bool mcpUsed;
            public bool runtimeIntegrated;
            public bool assetPromoted;
            public bool assetStoreModelsIntegrated;
            public bool skySystemIntegrated;
            public bool solarArcIntegrated;
            public bool weatherSystemIntegrated;
            public bool campfireIntegrated;
            public string[] assetPathsUsed;
            public string[] weatherStates;
        }

        [MenuItem("NexusLink/Moonlake/Rebuild Current HeroZone v05")]
        public static void BuildFromMenu()
        {
            Build();
        }

        public static void BuildFromCommandLine()
        {
            Build();
        }

        [MenuItem("NexusLink/Moonlake/Refresh Current HeroZone Previews")]
        public static void RenderCurrentPreviews()
        {
            var cameraObject = GameObject.Find("Moonlake_HeroCamera");
            var camera = cameraObject != null ? cameraObject.GetComponent<Camera>() : null;
            if (camera == null)
                throw new InvalidOperationException("Moonlake_HeroCamera is not available in the active scene.");

            // Shader.WarmupAllShaders() used to run here. It compiles every
            // variant in the project in a single call, and combined with the
            // back-to-back preview renders below it was spiking the D3D12
            // constant-buffer allocator until the graphics worker thread died in
            // ConstantBuffersD3D12::ResizeConstantBuffers. One warm render is
            // enough to prime what this scene actually uses.
            WarmupRender(camera);
            var environment = UnityEngine.Object.FindAnyObjectByType<MoonlakeSkyDayWeatherController>();
            if (environment != null)
                RenderEnvironmentPreviews(camera, environment);
            else
            {
                RenderPreview(camera, 1080, 1920, "moonlake-hd25d-v01-1080x1920.png");
                RenderPreview(camera, 390, 844, "moonlake-hd25d-v01-390x844.png");
            }
            Debug.Log("[Moonlake HD25D] Current-scene previews refreshed after editor warm-up.");
        }

        static void Build()
        {
            Debug.Log("[Moonlake HD25D] Starting in-place procedural rebuild.");
            Materials.Clear();
            Meshes.Clear();
            ForegroundRenderers.Clear();
            NightEmissionRenderers.Clear();
            NightLights.Clear();
            AssetPathsUsed.Clear();
            visualCount = 0;
            assetBackedVisualCount = 0;
            assetCrystalClusterCount = 0;

            EnsureFolder("Assets/Scenes", "QA");
            EnsureFolder("Assets/Scenes/QA", "_Backups");
            EnsureFolder("Assets/Art/Moonlake", "HD25D_v01");
            EnsureFolder(OutputRoot, "Materials");
            EnsureFolder(OutputRoot, "Meshes");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            BackupExistingSceneBeforeRebuild();
            CreateMaterialLibrary();
            CreateMeshLibrary();

            var scene = AcquireTargetScene();
            ClearScene(scene);

            var root = new GameObject(SceneRootName);
            var cameras = CreateGroup(root.transform, "00_Cameras");
            var skyWeather = CreateGroup(root.transform, "05_Sky_And_Weather_Runtime");
            var lighting = CreateGroup(root.transform, "10_Lighting_DayMaster");
            var foundation = CreateGroup(root.transform, "20_Foundation");
            var water = CreateGroup(root.transform, "30_Water");
            var cliffs = CreateGroup(root.transform, "40_Cliffs_And_Waterfalls");
            var bridge = CreateGroup(root.transform, "50_Bridge_And_SteppingStones");
            var stage = CreateGroup(root.transform, "60_CompanionStage");
            var camp = CreateGroup(root.transform, "70_CampStructures");
            var campfireGroup = CreateGroup(root.transform, "72_Campfire_And_Warmth_FX");
            var vegetation = CreateGroup(root.transform, "75_Vegetation");
            var props = CreateGroup(root.transform, "80_PlaceableProps");
            var foreground = CreateGroup(root.transform, "90_ForegroundOccluders");
            var postFx = CreateGroup(root.transform, "92_PostFX");
            var pathNodes = CreateGroup(root.transform, "85_PathNodes");
            var probes = CreateGroup(root.transform, "95_GameplayProbes");
            var referenceOnly = CreateGroup(root.transform, "99_ReferenceOnly");
            referenceOnly.gameObject.SetActive(false);

            BuildFoundation(foundation, water);
            BuildCliffTower(cliffs, -3.35f, 7.35f, false);
            BuildCliffTower(cliffs, 3.35f, 7.35f, true);
            BuildBridgeAndStones(bridge);
            BuildPlaza(stage);
            BuildTent(camp, "Tent_Left_Blue", new Vector3(-3.05f, 0.18f, 0.58f), "canvas_blue");
            BuildTent(camp, "Tent_Right_Violet", new Vector3(3.05f, 0.18f, 0.58f), "canvas_violet");
            var campfire = BuildCampfire(campfireGroup);
            BuildVegetation(vegetation, foreground);
            BuildPlaceableProps(props);
            BuildFarBank(vegetation);

            var camera = CreateCamera(cameras);
            CreatePostProcessing(postFx, camera);
            Light sun;
            Light skyFill;
            CreateLighting(lighting, out sun, out skyFill);
            var environment = CreateSkyDayWeatherSystem(skyWeather, camera, sun, skyFill, campfire);
            var companion = CreateCompanion(stage, camera);
            CreateGameplayProbes(probes, companion, camera);
            CreatePlayCameraRig(cameras, camera, companion, probes);
            CreatePathNodes(pathNodes, companion);

            environment.timeOfDay = 10.5f;
            environment.weather = MoonlakeSkyDayWeatherController.WeatherState.Clear;
            environment.ApplyImmediate();
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;

            shadowCasterCount = ApplyShadowCastingBudget(root);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath, false))
                throw new InvalidOperationException("Unity failed to save " + ScenePath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            WarmupRender(camera);
            var previews = RenderEnvironmentPreviews(camera, environment);
            WriteReport(root, camera, previews);

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            Debug.LogFormat(
                "[Moonlake HD25D] Rebuild complete. Scene={0}, visuals={1}, renderers={2}",
                ScenePath,
                visualCount,
                root.GetComponentsInChildren<Renderer>(true).Length);
        }

        static Scene AcquireTargetScene()
        {
            var active = SceneManager.GetActiveScene();
            if (active.IsValid() && string.Equals(active.path, ScenePath, StringComparison.OrdinalIgnoreCase))
                return active;

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
                return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        static void ClearScene(Scene scene)
        {
            foreach (var gameObject in scene.GetRootGameObjects())
                UnityEngine.Object.DestroyImmediate(gameObject);
        }

        static Transform CreateGroup(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static void BuildFoundation(Transform foundation, Transform water)
        {
            CreatePrimitive(
                foundation,
                "Diorama_Stone_Base",
                PrimitiveType.Cube,
                new Vector3(0f, -0.46f, -2.0f),
                new Vector3(30.0f, 0.34f, 20.0f),
                Materials["stone_dark"]);

            // The elliptical island was the reason 14.1% of the portrait frame
            // rendered as bare sky: a disc tapers away from the camera axis, so
            // its silhouette was fully inside the crop and the corners fell off
            // it. Measured against the frame's own ground footprint, coverage has
            // to reach |x| 3.83 at z=-9.1 (bottom edge) out to |x| 10.68 at
            // z=39.4 (top edge). The reference is a crop of a continuous world,
            // not an object floating in the middle of one, so the near field is
            // now a slab that runs past every edge.
            CreatePrimitive(
                foundation,
                "Ground_Near_Grass",
                PrimitiveType.Cube,
                new Vector3(0f, -0.10f, -3.6f),
                new Vector3(30.0f, 0.20f, 18.0f),
                Materials["grass"]);

            // Straight stone kerb along the very bottom of the crop, the way the
            // reference closes its foreground. It sits just inside the frame's
            // bottom ground line (z=-9.1) so it reads as a band, not as an edge
            // the scene falls off.
            CreatePrimitive(
                foundation,
                "Foreground_Stone_Kerb",
                PrimitiveType.Cube,
                new Vector3(0f, -0.17f, -8.85f),
                new Vector3(30.0f, 0.50f, 0.90f),
                Materials["stone_light"]);
            CreatePrimitive(
                foundation,
                "Foreground_Stone_Kerb_Shadow",
                PrimitiveType.Cube,
                new Vector3(0f, -0.30f, -9.45f),
                new Vector3(30.0f, 0.44f, 0.55f),
                Materials["stone_dark"]);

            // Banks flanking the channel. These carry the frame's left and right
            // margins from the midground all the way to the horizon band, which
            // is where the other 17-25% of the empty pixels were.
            for (var side = -1; side <= 1; side += 2)
            {
                CreatePrimitive(
                    foundation,
                    side < 0 ? "Channel_Bank_Left" : "Channel_Bank_Right",
                    PrimitiveType.Cube,
                    new Vector3(side * 12.4f, 0.02f, 15.0f),
                    new Vector3(13.6f, 0.62f, 22.0f),
                    Materials["grass"]);
                CreatePrimitive(
                    foundation,
                    side < 0 ? "Channel_Bank_Left_Moss" : "Channel_Bank_Right_Moss",
                    PrimitiveType.Cube,
                    new Vector3(side * 6.35f, 0.10f, 15.0f),
                    new Vector3(1.60f, 0.52f, 22.0f),
                    Materials["moss"]);
            }

            CreatePrimitive(
                foundation,
                "Left_Shore",
                PrimitiveType.Sphere,
                new Vector3(-5.35f, 0.03f, 4.8f),
                new Vector3(4.05f, 0.50f, 8.35f),
                Materials["moss"]);
            CreatePrimitive(
                foundation,
                "Right_Shore",
                PrimitiveType.Sphere,
                new Vector3(5.35f, 0.03f, 4.8f),
                new Vector3(4.05f, 0.50f, 8.35f),
                Materials["moss"]);

            // A flat cube cannot catch a highlight, so the lake read as a solid
            // colour field. The nature pack's detailed water carries its ripples
            // in geometry rather than in a normal map, which is what makes the
            // surface sparkle under the key light. Its own material is a dull
            // realistic teal, so only the mesh is borrowed.
            //
            // Extended from 11.45 x 8.65 to 26 x 30: the old pond ended at z=9.4
            // while the frame still needed ground out to z=39, and it was 11.45
            // wide where the crop needed 13.9. The channel now runs to the
            // horizon band between the banks, which is also what gives the
            // reference its recession.
            if (InstantiateAssetPrefab(
                water,
                "Assets/InnerverseInteractive/Ultimate Nature – Starter/Environment/Water/Prefabs/UNS_Water_Detailed.prefab",
                "Moonlake_Water_Surface",
                new Vector3(0f, -0.05f, 13.5f),
                new Vector3(26.0f, 0.16f, 24.0f),
                Quaternion.identity,
                Materials["water"]) == null)
            {
                CreatePrimitive(
                    water,
                    "Moonlake_Water_Surface",
                    PrimitiveType.Cube,
                    new Vector3(0f, 0.04f, 13.5f),
                    new Vector3(26.0f, 0.10f, 24.0f),
                    Materials["water"],
                    Quaternion.identity,
                    false,
                    true);
            }

            CreatePrimitive(
                water,
                "Shallow_Cyan_Glade",
                PrimitiveType.Cylinder,
                new Vector3(0f, 0.11f, 5.85f),
                new Vector3(5.15f, 0.035f, 3.75f),
                Materials["water_shallow"],
                Quaternion.identity,
                false,
                true);
        }

        static void BuildCliffTower(Transform parent, float x, float z, bool mirrored)
        {
            var root = CreateGroup(parent, mirrored ? "Cliff_Right_Waterfall" : "Cliff_Left_Waterfall");
            var sign = mirrored ? -1f : 1f;

            // Stacked spheres read as a mushroom column; the owner reference
            // shows a stratified stone mesa. Authored cliff geometry carries that
            // banding in its silhouette, so the shape comes from the nature pack
            // while the surface stays on the Moonlake clay palette. Each segment
            // is a different source rock and the two towers draw from different
            // ones, because the reference towers are a matched family rather than
            // a mirrored pair.
            // Sized against the object's own axes now that the fit no longer
            // measures a rotated bounding box; the earlier, larger numbers were
            // silently shrunk by that bug. Depth is kept under the waterfall's
            // standoff at z-1.24 so the falls stay readable in front of the rock.
            // Barely tapered on purpose. The reference towers are near-vertical
            // masonry-like columns; the old 2.92 -> 2.48 taper plus a 3.65-wide
            // grass cap turned the silhouette into a mushroom, which was the most
            // obvious shape error against the reference.
            var segments = new[]
            {
                // sourceIndex, bottomY, width, height
                new Vector4(mirrored ? 3 : 1, 0.00f, 2.96f, 2.34f),
                new Vector4(mirrored ? 5 : 2, 2.18f, 2.88f, 2.06f),
                new Vector4(mirrored ? 2 : 4, 4.02f, 2.78f, 1.52f)
            };

            for (var i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                var depth = 1.98f - i * 0.09f;
                InstantiateAssetPrefab(
                    root,
                    "Assets/InnerverseInteractive/Ultimate Nature – Starter/Environment/Rocks/Cliffs/Prefabs/UNS_Rock_Cliff_0"
                        + Mathf.Clamp((int)segment.x, 1, 5) + ".prefab",
                    "Cliff_Strata_" + i.ToString("00"),
                    new Vector3(x + (i % 2 == 0 ? -0.06f : 0.09f) * sign, segment.y, z + (i % 2 == 0 ? 0.06f : -0.07f)),
                    new Vector3(segment.z, segment.w, depth),
                    Quaternion.Euler(0f, (28f * i + (mirrored ? 137f : 0f)) * sign, 0f),
                    i % 2 == 0 ? Materials["stone"] : Materials["stone_light"]);
            }

            // Horizontal courses. The reference reads these towers as stacked
            // stone rather than boulders, and the banding is what carries that at
            // this distance once depth of field has softened the surface detail.
            for (var course = 0; course < 6; course++)
            {
                var courseY = 0.62f + course * 0.86f;
                var courseWidth = Mathf.Lerp(3.02f, 2.84f, course / 5f);
                CreatePrimitive(
                    root,
                    "Cliff_Course_" + course.ToString("00"),
                    PrimitiveType.Cylinder,
                    new Vector3(x + 0.02f * sign, courseY, z),
                    new Vector3(courseWidth, 0.045f, courseWidth * 0.66f),
                    course % 2 == 0 ? Materials["stone_dark"] : Materials["stone"]);
            }

            // Cap only slightly proud of the 2.78 top segment. The old 3.65 disc
            // overhung the column by 0.44 a side and was the mushroom brim.
            CreatePrimitive(
                root,
                "Grass_Top_Cap",
                PrimitiveType.Cylinder,
                new Vector3(x, 5.43f, z),
                new Vector3(2.94f, 0.24f, 2.62f),
                Materials["grass"]);
            CreatePrimitive(
                root,
                "Cap_Stone_Lip",
                PrimitiveType.Cylinder,
                new Vector3(x, 5.24f, z),
                new Vector3(3.00f, 0.10f, 2.68f),
                Materials["stone_light"]);

            // Vines down the face, as on the reference's mossy towers.
            for (var vine = 0; vine < 5; vine++)
            {
                var vineX = x + (-1.05f + vine * 0.52f) * sign;
                var length = 0.62f + ((vine * 7) % 4) * 0.34f;
                CreatePrimitive(
                    root,
                    "Cliff_Vine_" + vine.ToString("00"),
                    PrimitiveType.Cylinder,
                    new Vector3(vineX, 5.18f - length, z - 1.02f),
                    new Vector3(0.085f, length, 0.085f),
                    vine % 2 == 0 ? Materials["foliage_dark"] : Materials["foliage_mid"]);
                CreatePrimitive(
                    root,
                    "Cliff_Vine_Tip_" + vine.ToString("00"),
                    PrimitiveType.Sphere,
                    new Vector3(vineX, 5.18f - length * 2f, z - 1.02f),
                    Vector3.one * 0.17f,
                    Materials["foliage_mid"]);
            }

            CreatePrimitive(
                root,
                "Moss_Lip",
                PrimitiveType.Sphere,
                new Vector3(x, 5.38f, z - 1.05f),
                new Vector3(2.72f, 0.34f, 0.72f),
                Materials["moss"]);

            // Real imported rock silhouettes break up the procedural mass while
            // the fitted bounds keep their authored scale from moving the cliff.
            for (var row = 0; row < 4; row++)
            {
                for (var column = -1; column <= 1; column += 2)
                {
                    var detail = row * 2 + (column > 0 ? 1 : 0);
                    // Pushed out from +/-0.68 and narrowed: at the old spread
                    // these covered the waterfall's own x range, so the falls and
                    // everything at their base were hidden behind rubble.
                    var detailX = x + (column * 1.10f + (row % 2 == 0 ? -0.10f : 0.12f) * sign);
                    InstantiateAssetPrefab(
                        root,
                        "Assets/SimpleNaturePack/Prefabs/Rock_0" + (detail % 5 + 1) + ".prefab",
                        "Asset_CliffFaceRock_" + detail.ToString("00"),
                        new Vector3(detailX, 0.10f + row * 1.18f, z - 1.16f),
                        new Vector3(1.12f - row * 0.05f, 1.18f, 0.86f),
                        Quaternion.Euler(row * 4f, (detail * 29f + column * 11f) * sign, column * 5f),
                        detail % 2 == 0 ? Materials["stone_light"] : Materials["stone"]);
                }
            }

            // The falls sit forward of the strata column (which now starts at
            // z-0.99 after the depth change) so nothing clips through them.
            var waterfallX = x + 0.10f * sign;
            const float waterfallZ = -1.62f;
            CreatePrimitive(
                root,
                "Waterfall_Sheet",
                PrimitiveType.Cube,
                new Vector3(waterfallX, 2.80f, z + waterfallZ),
                new Vector3(0.98f, 5.18f, 0.12f),
                Materials["waterfall"],
                Quaternion.Euler(0f, mirrored ? -3f : 3f, 0f),
                false,
                true);
            CreatePrimitive(
                root,
                "Waterfall_Highlight",
                PrimitiveType.Cube,
                new Vector3(waterfallX - 0.20f * sign, 2.84f, z + waterfallZ - 0.08f),
                new Vector3(0.17f, 4.92f, 0.040f),
                Materials["foam"],
                Quaternion.identity,
                false,
                true);
            CreatePrimitive(
                root,
                "Waterfall_Foam_Base",
                PrimitiveType.Sphere,
                new Vector3(waterfallX, 0.20f, z + waterfallZ - 0.14f),
                new Vector3(1.55f, 0.22f, 0.78f),
                Materials["foam"],
                Quaternion.identity,
                false,
                true);

            // Where the falls meet the lake. Without this the water column just
            // ends in mid-surface, which is the single clearest tell that the
            // waterfall is a flat card. Rings sit above the water top at y=0.09;
            // a cylinder's localScale.y is a half-height, so 0.012 clears it.
            // Two rings, small and mostly transparent. Three opaque 1.3-2.5 wide
            // discs stacked 0.10 apart in z read as a pile of white plates on the
            // lake rather than as spray spreading on its surface.
            for (var ring = 0; ring < 2; ring++)
            {
                var spread = 0.98f + ring * 0.52f;
                CreatePrimitive(
                    root,
                    "Waterfall_Splash_Ring_" + ring.ToString("00"),
                    PrimitiveType.Cylinder,
                    new Vector3(waterfallX, 0.104f + ring * 0.006f, z + waterfallZ - 0.16f + ring * 0.16f),
                    new Vector3(spread, 0.010f, spread * 0.60f),
                    ring == 0 ? Materials["foam"] : Materials["foam_soft"],
                    Quaternion.identity,
                    false,
                    true);
            }

            // Droplets thrown up at the impact point.
            var splashRandom = new System.Random(mirrored ? 91 : 47);
            for (var i = 0; i < 7; i++)
            {
                CreatePrimitive(
                    root,
                    "Waterfall_Spray_" + i.ToString("00"),
                    PrimitiveType.Sphere,
                    new Vector3(
                        waterfallX + Next(splashRandom, -0.62f, 0.62f),
                        0.16f + Next(splashRandom, 0f, 0.46f),
                        z + waterfallZ - 0.18f + Next(splashRandom, -0.16f, 0.22f)),
                    Vector3.one * Next(splashRandom, 0.09f, 0.19f),
                    Materials["foam"],
                    Quaternion.identity,
                    false,
                    true);
            }

            // The tower tops are the only place a tree is read against open sky,
            // so they get the detailed spruce rather than the low-poly pine used
            // everywhere else.
            InstantiateAssetPrefab(
                root,
                "Assets/InnerverseInteractive/Ultimate Nature – Starter/Environment/Trees/Fir/Prefabs/UNS_Spruce_0" + (mirrored ? "2" : "1") + ".prefab",
                "Asset_UNS_Spruce_Tall",
                new Vector3(x - 0.72f * sign, 5.55f, z + 0.05f),
                new Vector3(1.62f, 2.85f, 1.62f),
                Quaternion.Euler(0f, 41f * sign, 0f),
                null,
                ApplyPineAndFruitTreePalette);
            InstantiateAssetPrefab(
                root,
                "Assets/InnerverseInteractive/Ultimate Nature – Starter/Environment/Trees/Fir/Prefabs/UNS_Spruce_0" + (mirrored ? "1" : "2") + ".prefab",
                "Asset_UNS_Spruce_Short",
                new Vector3(x + 0.82f * sign, 5.55f, z + 0.30f),
                new Vector3(1.18f, 2.02f, 1.18f),
                Quaternion.Euler(0f, 152f * sign, 0f),
                null,
                ApplyPineAndFruitTreePalette);
            AddMossDots(root, new Vector3(x + 1.25f * sign, 3.15f, z - 1.03f), mirrored ? 31 : 17);
        }

        static void BuildBridgeAndStones(Transform parent)
        {
            var bridge = CreateGroup(parent, "Bridge_Slightly_Right_Of_Centre");
            const float x = 0.95f;
            var assetBridge = InstantiateAssetPrefab(
                bridge,
                "Assets/Polytope Studio/Lowpoly_Village/Prefabs/Modular/Bridge/PT_Wooden_Bridge_02.prefab",
                "Asset_PT_Wooden_Bridge",
                // Centred out over the water: the span has to start past the
                // plaza rim (z 2.74) instead of landing on the companion
                // corridor, and reach toward the far shore.
                new Vector3(x, 0.06f, 5.05f),
                // Deck width, arch height, span. The reference bridge rises
                // gently rather than arching hard, so the height comes down from
                // the 1.46 that was compensating for the mis-applied scale.
                new Vector3(1.34f, 1.02f, 5.60f),
                Quaternion.Euler(0f, 90f, 0f),
                Materials["wood"]);

            if (assetBridge == null)
            {
                const int plankCount = 16;
                for (var i = 0; i < plankCount; i++)
                {
                    var z = 1.50f + i * 0.36f;
                    CreatePrimitive(
                        bridge,
                        "Bridge_Plank_" + i.ToString("00"),
                        PrimitiveType.Cube,
                        new Vector3(x, 0.36f + i * 0.012f, z),
                        new Vector3(1.58f, 0.16f, 0.32f),
                        i % 2 == 0 ? Materials["wood"] : Materials["wood_light"],
                        Quaternion.Euler(0f, i % 2 == 0 ? -1.5f : 1.5f, 0f));
                }

                for (var side = -1; side <= 1; side += 2)
                {
                    for (var i = 0; i < 5; i++)
                    {
                        var z = 1.52f + i * 1.38f;
                        CreatePrimitive(
                            bridge,
                            "Rail_Post_" + side + "_" + i,
                            PrimitiveType.Cylinder,
                            new Vector3(x + side * 0.80f, 0.84f, z),
                            new Vector3(0.12f, 0.50f, 0.12f),
                            Materials["wood_dark"]);
                    }

                    CreateBeam(
                        bridge,
                        "Rail_Top_" + side,
                        new Vector3(x + side * 0.80f, 1.24f, 1.50f),
                        new Vector3(x + side * 0.80f, 1.31f, 6.90f),
                        0.10f,
                        Materials["wood_dark"]);
                }
            }

            var stones = CreateGroup(parent, "SteppingStones_LeftArc");
            // Re-seated out into the water on the bridge's left. The old line
            // ran from z=2.30, which the widened plaza (back rim now at z=3.44)
            // grew over, leaving the blocks piled on the paving.
            var positions = new[]
            {
                new Vector3(-3.05f, 0.19f, 4.05f),
                new Vector3(-2.42f, 0.20f, 4.92f),
                new Vector3(-1.78f, 0.21f, 5.70f),
                new Vector3(-1.10f, 0.22f, 6.40f),
                new Vector3(-0.42f, 0.23f, 7.02f)
            };
            for (var i = 0; i < positions.Length; i++)
            {
                // The reference's stepping stones are chunky cut blocks standing
                // proud of the water, not rounded river pebbles — at this
                // distance a rock silhouette just dissolves into the surface.
                // One block per stone, clearly separated. The first pass used a
                // 0.92-wide block plus a lighter cap plate, which at this spacing
                // overlapped into a stack of paving slabs instead of reading as
                // stones you could step between.
                CreatePrimitive(
                    stones,
                    "SteppingStone_Block_" + i.ToString("00"),
                    PrimitiveType.Cube,
                    positions[i] + new Vector3(0f, 0.04f, 0f),
                    new Vector3(0.60f - i * 0.02f, 0.34f, 0.54f - i * 0.02f),
                    i % 2 == 0 ? Materials["stone_light"] : Materials["stone"],
                    Quaternion.Euler(0f, -22f + i * 27f, 0f));
            }
        }

        static void BuildPlaza(Transform parent)
        {
            CreatePrimitive(
                parent,
                "Companion_Plaza_Rim",
                PrimitiveType.Cylinder,
                new Vector3(0f, 0.11f, 0.14f),
                new Vector3(6.90f, 0.18f, 6.80f),
                Materials["stone_light"]);
            CreatePrimitive(
                parent,
                "Companion_Plaza_Surface",
                PrimitiveType.Cylinder,
                new Vector3(0f, 0.27f, 0.14f),
                new Vector3(6.40f, 0.09f, 6.30f),
                Materials["plaza"]);
            // The previous rune was 1.48 across on a 5.72 plaza AND sat below the
            // surface, so it never rendered at all — which is why the plaza read
            // as blank in every build. Unity's cylinder mesh is two units tall,
            // so localScale.y is a half-height: the surface disc centred at 0.27
            // with y-scale 0.09 tops out at 0.36, and every rune has to clear
            // that. The reference carries concentric rings out to the rim plus
            // two smaller offset motifs. Geometry here is project-native
            // Cyber-Taoist ring-and-tick work, not a trace of its symbol.
            const float plazaSurfaceTop = 0.36f;
            const float plazaAspect = 6.30f / 6.40f;

            var rings = new[]
            {
                new Vector2(5.66f, 0.12f),
                new Vector2(4.00f, 0.11f),
                new Vector2(2.34f, 0.10f)
            };
            for (var i = 0; i < rings.Length; i++)
            {
                BuildPlazaRing(
                    parent,
                    "Companion_Rune_Ring_" + i.ToString("00"),
                    new Vector3(0f, plazaSurfaceTop + 0.008f + i * 0.002f, 0.14f),
                    rings[i].x,
                    rings[i].y,
                    plazaAspect);
            }

            // Radial ticks between the two outer rings. A cube mesh is one unit
            // tall, unlike the cylinders above, so its y-scale is a full height.
            for (var i = 0; i < 12; i++)
            {
                var angle = Mathf.Deg2Rad * (i * 30f + 15f);
                var radius = 4.83f * 0.5f;
                CreatePrimitive(
                    parent,
                    "Companion_Rune_Tick_" + i.ToString("00"),
                    PrimitiveType.Cube,
                    new Vector3(Mathf.Sin(angle) * radius, plazaSurfaceTop + 0.010f, 0.14f + Mathf.Cos(angle) * radius * plazaAspect),
                    new Vector3(0.075f, 0.020f, 0.30f),
                    Materials["gold_pale"],
                    Quaternion.Euler(0f, i * 30f + 15f, 0f));
            }

            // Two smaller motifs offset from the centre, as in the reference.
            BuildPlazaRing(parent, "Companion_Rune_Motif_Left", new Vector3(-1.87f, plazaSurfaceTop + 0.010f, -1.38f), 1.21f, 0.08f, plazaAspect);
            BuildPlazaRing(parent, "Companion_Rune_Motif_Right", new Vector3(2.01f, plazaSurfaceTop + 0.010f, -1.48f), 1.03f, 0.08f, plazaAspect);

            CreatePrimitive(
                parent,
                "Companion_Rune_Core",
                PrimitiveType.Cylinder,
                new Vector3(0f, plazaSurfaceTop + 0.012f, 0.14f),
                new Vector3(0.61f, 0.010f, 0.61f * plazaAspect),
                Materials["gold_pale"]);
        }

        /// <summary>
        /// Draws a carved ring as a gold disc with the plaza colour inset back
        /// over its middle, which keeps every rune on two cheap primitives.
        /// </summary>
        static void BuildPlazaRing(
            Transform parent,
            string name,
            Vector3 center,
            float width,
            float thickness,
            float aspect)
        {
            CreatePrimitive(
                parent,
                name + "_Band",
                PrimitiveType.Cylinder,
                center,
                new Vector3(width, 0.010f, width * aspect),
                Materials["gold_pale"]);
            // Inset sits marginally higher so it reliably wins the depth test
            // over the band it is carving out of.
            CreatePrimitive(
                parent,
                name + "_Inset",
                PrimitiveType.Cylinder,
                center + new Vector3(0f, 0.003f, 0f),
                new Vector3(width - thickness * 2f, 0.010f, (width - thickness * 2f) * aspect),
                Materials["plaza"]);
        }

        static void BuildTent(Transform parent, string name, Vector3 position, string canvasMaterial)
        {
            var root = CreateGroup(parent, name);
            CreatePrimitive(
                root,
                "Tent_Wall",
                PrimitiveType.Cylinder,
                position + new Vector3(0f, 0.39f, 0f),
                new Vector3(2.30f, 0.36f, 2.30f),
                Materials["cream"]);

            CreateMeshObject(
                root,
                "Tent_Striped_Canopy",
                Meshes["tent"],
                position + new Vector3(0f, 0.48f, 0f),
                new Vector3(2.98f, 2.22f, 2.98f),
                Quaternion.identity,
                new[] { Materials["cream"], Materials[canvasMaterial] });

            CreatePrimitive(
                root,
                "Tent_Entrance",
                PrimitiveType.Cube,
                position + new Vector3(0f, 0.58f, -1.13f),
                new Vector3(0.68f, 0.79f, 0.055f),
                Materials["tent_dark"]);

            for (var i = 0; i < 8; i++)
            {
                var angle = Mathf.Deg2Rad * (i * 45f);
                var edge = position + new Vector3(Mathf.Sin(angle) * 1.26f, 0.65f, Mathf.Cos(angle) * 1.26f);
                CreatePrimitive(
                    root,
                    "Gold_Canopy_Rib_" + i.ToString("00"),
                    PrimitiveType.Cylinder,
                    edge,
                    new Vector3(0.038f, 0.62f, 0.038f),
                    Materials["gold"]);
            }

            CreatePrimitive(
                root,
                "Tent_Finial",
                PrimitiveType.Cylinder,
                position + new Vector3(0f, 2.54f, 0f),
                new Vector3(0.10f, 0.24f, 0.10f),
                Materials["gold"]);
            CreateMeshObject(
                root,
                "Tent_Finial_Cap",
                Meshes["cone"],
                position + new Vector3(0f, 2.79f, 0f),
                new Vector3(0.42f, 0.56f, 0.42f),
                Quaternion.identity,
                new[] { Materials["gold"], Materials["gold"] });

            // Guy ropes anchor the canopy to the ground; the reference reads as a
            // pitched camp rather than a placed cone largely because of these.
            var outward = position.x < 0f ? -1f : 1f;
            for (var i = 0; i < 4; i++)
            {
                var angle = Mathf.Deg2Rad * (i * 90f + 45f);
                var top = position + new Vector3(Mathf.Sin(angle) * 1.06f, 1.62f, Mathf.Cos(angle) * 1.06f);
                var peg = position + new Vector3(Mathf.Sin(angle) * 1.92f, 0.04f, Mathf.Cos(angle) * 1.92f);
                CreateBeam(root, "Tent_GuyRope_" + i.ToString("00"), top, peg, 0.022f, Materials["wood_dark"]);
                CreatePrimitive(
                    root,
                    "Tent_Peg_" + i.ToString("00"),
                    PrimitiveType.Cylinder,
                    peg + new Vector3(0f, 0.05f, 0f),
                    new Vector3(0.05f, 0.09f, 0.05f),
                    Materials["gold"]);
            }

            // Banner on the outer flank of each tent, matching its canvas.
            var bannerBase = position + new Vector3(outward * 1.98f, 0f, -0.34f);
            CreatePrimitive(
                root,
                "Banner_Pole",
                PrimitiveType.Cylinder,
                bannerBase + new Vector3(0f, 1.32f, 0f),
                new Vector3(0.052f, 1.32f, 0.052f),
                Materials["gold"]);
            CreateMeshObject(
                root,
                "Banner_Pole_Cap",
                Meshes["cone"],
                bannerBase + new Vector3(0f, 2.74f, 0f),
                new Vector3(0.16f, 0.24f, 0.16f),
                Quaternion.identity,
                new[] { Materials["gold"], Materials["gold"] });
            CreatePrimitive(
                root,
                "Banner_Cloth",
                PrimitiveType.Cube,
                bannerBase + new Vector3(outward * 0.20f, 1.88f, 0f),
                new Vector3(0.34f, 1.02f, 0.035f),
                Materials[canvasMaterial],
                Quaternion.Euler(0f, outward * 6f, 0f));
            CreatePrimitive(
                root,
                "Banner_Trim",
                PrimitiveType.Cube,
                bannerBase + new Vector3(outward * 0.20f, 1.40f, 0f),
                new Vector3(0.36f, 0.075f, 0.045f),
                Materials["gold"],
                Quaternion.Euler(0f, outward * 6f, 0f));
        }

        static void BuildVegetation(Transform vegetation, Transform foreground)
        {
            BuildBroadleafTree(vegetation, new Vector3(-5.55f, 0.10f, 1.24f), 1.30f, 41);
            BuildBroadleafTree(vegetation, new Vector3(5.55f, 0.10f, 1.24f), 1.30f, 73);

            var sideBushes = new[]
            {
                new Vector3(-6.15f, 0.20f, -0.25f), new Vector3(-5.90f, 0.20f, 2.05f),
                new Vector3(-5.80f, 0.20f, 3.75f), new Vector3(6.15f, 0.20f, -0.25f),
                new Vector3(5.90f, 0.20f, 2.05f), new Vector3(5.80f, 0.20f, 3.75f),
                new Vector3(-2.90f, 0.16f, 2.15f), new Vector3(3.00f, 0.16f, 2.18f)
            };
            for (var i = 0; i < sideBushes.Length; i++)
                BuildBushCluster(vegetation, "Side_Bush_" + i.ToString("00"), sideBushes[i], 0.62f, 100 + i, false);

            // Two bands, not three. The old front row sat at z=-2.2, which the
            // widened plaza (front rim now at z=-3.285) would grow straight
            // through, and three dense rows left no grass showing between the
            // clumps. Fewer, larger, more spaced clusters read closer to the
            // reference's planting and give the companion a clear approach.
            var frontBushes = new[]
            {
                new Vector3(-5.30f, 0.15f, -3.95f), new Vector3(-3.15f, 0.15f, -4.30f),
                new Vector3(-0.95f, 0.15f, -4.45f), new Vector3(1.35f, 0.15f, -4.38f),
                new Vector3(3.50f, 0.15f, -4.15f), new Vector3(5.45f, 0.15f, -3.85f)
            };
            for (var i = 0; i < frontBushes.Length; i++)
                BuildBushCluster(foreground, "Foreground_Bush_" + i.ToString("00"), frontBushes[i], 1.08f, 200 + i, true);

            var edgeBushes = new[]
            {
                new Vector3(-5.95f, 0.10f, -6.85f), new Vector3(-3.55f, 0.10f, -7.10f),
                new Vector3(-1.15f, 0.10f, -7.22f), new Vector3(1.30f, 0.10f, -7.18f),
                new Vector3(3.70f, 0.10f, -7.00f), new Vector3(6.05f, 0.10f, -6.78f)
            };
            for (var i = 0; i < edgeBushes.Length; i++)
                BuildBushCluster(foreground, "Edge_Bush_" + i.ToString("00"), edgeBushes[i], 1.22f, 400 + i, true);

            // Thinning the bush rows left large flat stretches of grass, which
            // the reference never has: it fills the ground with small plants,
            // flowers and pebbles rather than with more shrubs. This is the
            // cheap detail layer that replaces the removed clusters.
            var groundRandom = new System.Random(777);
            for (var i = 0; i < 34; i++)
            {
                var spot = new Vector3(
                    Next(groundRandom, -6.6f, 6.6f),
                    0.06f,
                    Next(groundRandom, -8.3f, -3.3f));
                var pick = i % 5;
                var path =
                      pick == 0 ? "Assets/InnerverseInteractive/Ultimate Nature – Starter/Environment/Vegetation/Flowers/Prefabs/UNS_Flower.prefab"
                    : pick == 1 ? "Assets/InnerverseInteractive/Ultimate Nature – Starter/Environment/Vegetation/Grass/Prefabs/UNS_Grass.prefab"
                    : pick == 2 ? "Assets/InnerverseInteractive/Ultimate Nature – Starter/Environment/Vegetation/Mushrooms/Prefabs/UNS_Mushroom_Patch.prefab"
                    : pick == 3 ? "Assets/InnerverseInteractive/Ultimate Nature – Starter/Environment/Props/Branches/Prefabs/UNS_Branch.prefab"
                    : "Assets/InnerverseInteractive/Ultimate Nature – Starter/Environment/Rocks/River/Prefabs/UNS_Tiny_Rock_0" + (i % 5 + 1) + ".prefab";
                var scale =
                      pick == 4 ? new Vector3(0.42f, 0.20f, 0.36f)
                    : pick == 3 ? new Vector3(0.66f, 0.16f, 0.28f)
                    : pick == 2 ? new Vector3(0.50f, 0.34f, 0.46f)
                    : new Vector3(0.70f, 0.52f, 0.70f);
                var material =
                      pick == 0 ? Materials["flower_blue"]
                    : pick == 1 ? Materials["foliage_light"]
                    : pick == 2 ? Materials["cream"]
                    : pick == 3 ? Materials["wood_dark"]
                    : Materials["stone_light"];
                var detail = InstantiateAssetPrefab(
                    foreground,
                    path,
                    "Ground_Detail_" + i.ToString("00"),
                    spot,
                    scale,
                    Quaternion.Euler(0f, Next(groundRandom, 0f, 360f), 0f),
                    material);
                if (detail != null)
                    ForegroundRenderers.AddRange(detail.GetComponentsInChildren<Renderer>(true));
            }
        }

        static void BuildBroadleafTree(Transform parent, Vector3 position, float scale, int seed)
        {
            var root = CreateGroup(parent, position.x < 0f ? "Broadleaf_Tree_Left" : "Broadleaf_Tree_Right");
            var assetTree = InstantiateAssetPrefab(
                root,
                "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Trees/PT_Fruit_Tree_01_green.prefab",
                position.x < 0f ? "Asset_FruitTree_Left" : "Asset_FruitTree_Right",
                position,
                new Vector3(2.65f, 4.30f, 2.65f) * scale,
                Quaternion.Euler(0f, seed % 360, 0f),
                null,
                ApplyPineAndFruitTreePalette);

            if (assetTree != null)
            {
                for (var i = 0; i < 9; i++)
                {
                    var angle = Mathf.Deg2Rad * (i * 41f + seed);
                    CreatePrimitive(
                        root,
                        "AssetTree_Flower_" + i.ToString("00"),
                        PrimitiveType.Sphere,
                        position + new Vector3(Mathf.Sin(angle) * 0.90f, 2.35f + (i % 3) * 0.28f, Mathf.Cos(angle) * 0.62f) * scale,
                        Vector3.one * 0.075f * scale,
                        i % 2 == 0 ? Materials["flower_pink"] : Materials["flower_blue"]);
                }

                // Pale-gold bells hung under the canopy, as on the reference's
                // blossom trees. Each is a thread plus a bell so it reads as
                // hanging rather than floating.
                for (var i = 0; i < 4; i++)
                {
                    var angle = Mathf.Deg2Rad * (i * 87f + seed * 0.5f);
                    var anchor = position + new Vector3(Mathf.Sin(angle) * 0.98f, 2.16f, Mathf.Cos(angle) * 0.66f) * scale;
                    CreatePrimitive(
                        root,
                        "AssetTree_BellThread_" + i.ToString("00"),
                        PrimitiveType.Cylinder,
                        anchor + new Vector3(0f, -0.10f, 0f) * scale,
                        new Vector3(0.012f, 0.11f, 0.012f) * scale,
                        Materials["wood_dark"]);
                    CreatePrimitive(
                        root,
                        "AssetTree_Bell_" + i.ToString("00"),
                        PrimitiveType.Sphere,
                        anchor + new Vector3(0f, -0.26f, 0f) * scale,
                        Vector3.one * 0.105f * scale,
                        Materials["gold"]);
                }
                return;
            }

            CreatePrimitive(
                root,
                "Trunk",
                PrimitiveType.Cylinder,
                position + new Vector3(0f, 1.45f * scale, 0f),
                new Vector3(0.42f * scale, 1.45f * scale, 0.42f * scale),
                Materials["wood_dark"]);

            var random = new System.Random(seed);
            for (var i = 0; i < 10; i++)
            {
                var offset = new Vector3(
                    Next(random, -0.90f, 0.90f),
                    Next(random, -0.30f, 0.48f),
                    Next(random, -0.70f, 0.70f)) * scale;
                var size = Next(random, 0.82f, 1.16f) * scale;
                CreatePrimitive(
                    root,
                    "Canopy_Lobe_" + i.ToString("00"),
                    PrimitiveType.Sphere,
                    position + new Vector3(0f, 3.15f * scale, 0f) + offset,
                    new Vector3(size * 1.25f, size, size),
                    i % 3 == 0 ? Materials["foliage_light"] : Materials["foliage_mid"]);
            }

            for (var i = 0; i < 7; i++)
            {
                var angle = Mathf.Deg2Rad * (i * 51f);
                CreatePrimitive(
                    root,
                    "Tree_Flower_" + i.ToString("00"),
                    PrimitiveType.Sphere,
                    position + new Vector3(Mathf.Sin(angle) * 0.92f, 3.25f + (i % 2) * 0.28f, Mathf.Cos(angle) * 0.60f) * scale,
                    Vector3.one * 0.09f * scale,
                    i % 2 == 0 ? Materials["flower_pink"] : Materials["flower_blue"]);
            }
        }

        static void BuildBushCluster(Transform parent, string name, Vector3 position, float scale, int seed, bool foreground)
        {
            var root = CreateGroup(parent, name);

            // The faceted, crystalline green masses in the foreground band were
            // the SimpleNaturePack bush; its hard low-poly shell fights the
            // reference's soft rounded planting far more than the smooth lobes
            // below it do. The nature pack's bush carries real leaf geometry, so
            // it keeps the silhouette organic once it is on the Moonlake palette.
            var assetBush = InstantiateAssetPrefab(
                root,
                "Assets/InnerverseInteractive/Ultimate Nature – Starter/Environment/Vegetation/Bushes/Prefabs/UNS_Bush.prefab",
                "Asset_Bush_Core",
                position,
                new Vector3(1.26f, 0.96f, 1.06f) * scale,
                Quaternion.Euler(0f, seed * 17f % 360f, 0f),
                seed % 3 == 0 ? Materials["foliage_dark"] : Materials["foliage_mid"]);
            if (foreground && assetBush != null)
                ForegroundRenderers.AddRange(assetBush.GetComponentsInChildren<Renderer>(true));

            // A second, smaller clump breaks the "one bush per spot" rhythm the
            // reference never has.
            var assetBushSecondary = InstantiateAssetPrefab(
                root,
                "Assets/InnerverseInteractive/Ultimate Nature – Starter/Environment/Vegetation/Bushes/Prefabs/UNS_Bush.prefab",
                "Asset_Bush_Clump",
                position + new Vector3(0.46f, 0f, -0.28f) * scale,
                new Vector3(0.78f, 0.62f, 0.70f) * scale,
                Quaternion.Euler(0f, (seed * 53f + 120f) % 360f, 0f),
                Materials["foliage_light"]);
            if (foreground && assetBushSecondary != null)
                ForegroundRenderers.AddRange(assetBushSecondary.GetComponentsInChildren<Renderer>(true));

            var random = new System.Random(seed);
            for (var i = 0; i < 5; i++)
            {
                var offset = new Vector3(
                    Next(random, -0.55f, 0.55f),
                    Next(random, 0.10f, 0.42f),
                    Next(random, -0.38f, 0.38f)) * scale;
                var size = Next(random, 0.52f, 0.78f) * scale;
                var renderer = CreatePrimitive(
                    root,
                    "Leaf_Lobe_" + i.ToString("00"),
                    PrimitiveType.Sphere,
                    position + offset,
                    new Vector3(size * 1.14f, size, size),
                    i % 3 == 0 ? Materials["foliage_light"] : Materials["foliage_mid"]);
                if (foreground)
                    ForegroundRenderers.Add(renderer);
            }

            for (var i = 0; i < 5; i++)
            {
                var angle = Mathf.Deg2Rad * (seed + i * 71f);
                var renderer = CreatePrimitive(
                    root,
                    "Flower_" + i.ToString("00"),
                    PrimitiveType.Sphere,
                    position + new Vector3(Mathf.Sin(angle) * 0.47f, 0.64f, Mathf.Cos(angle) * 0.34f) * scale,
                    Vector3.one * 0.095f * scale,
                    i % 2 == 0 ? Materials["flower_blue"] : Materials["flower_pink"]);
                if (foreground)
                    ForegroundRenderers.Add(renderer);
            }
        }

        static void BuildPine(Transform parent, Vector3 position, float scale)
        {
            var root = CreateGroup(parent, "Pine");
            if (InstantiateAssetPrefab(
                root,
                "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Trees/PT_Pine_Tree_03_green.prefab",
                "Asset_PT_Pine",
                position,
                new Vector3(1.10f, 1.85f, 1.10f) * scale,
                Quaternion.Euler(0f, position.x * 23f + position.z * 11f, 0f),
                null,
                ApplyPineAndFruitTreePalette) != null)
            {
                return;
            }

            CreatePrimitive(
                root,
                "Trunk",
                PrimitiveType.Cylinder,
                position + new Vector3(0f, 0.42f * scale, 0f),
                new Vector3(0.16f * scale, 0.42f * scale, 0.16f * scale),
                Materials["wood_dark"]);
            for (var i = 0; i < 3; i++)
            {
                CreateMeshObject(
                    root,
                    "Needle_Tier_" + i.ToString("00"),
                    Meshes["cone"],
                    position + new Vector3(0f, (0.42f + i * 0.46f) * scale, 0f),
                    new Vector3((1.10f - i * 0.20f) * scale, 0.88f * scale, (1.10f - i * 0.20f) * scale),
                    Quaternion.identity,
                    new[] { Materials[i == 2 ? "foliage_light" : "foliage_dark"], Materials["foliage_dark"] });
            }
        }

        static void AddMossDots(Transform parent, Vector3 origin, int seed)
        {
            var random = new System.Random(seed);
            for (var i = 0; i < 8; i++)
            {
                CreatePrimitive(
                    parent,
                    "Cliff_Moss_" + i.ToString("00"),
                    PrimitiveType.Sphere,
                    origin + new Vector3(Next(random, -0.42f, 0.42f), Next(random, -1.05f, 1.05f), Next(random, -0.08f, 0.08f)),
                    new Vector3(Next(random, 0.12f, 0.28f), Next(random, 0.18f, 0.38f), 0.10f),
                    i % 2 == 0 ? Materials["moss"] : Materials["flower_blue"]);
            }
        }

        static void BuildPlaceableProps(Transform parent)
        {
            // The hand-authored FBX crystals read as painted cones rather than the
            // gem-like accents in the reference. The TranslucentCrystals pack
            // ships genuine faceted gem geometry, which is what is borrowed here;
            // its shader is built-in-only and cannot come with it (see below).
            BuildAssetCrystalCluster(parent, "Crystal_Camp_Left_Asset", "Assets/SineVFX/TranslucentCrystals/Prefabs/Crystalsv03.prefab", new Vector3(-3.10f, 0.22f, 2.05f), new Vector3(1.15f, 1.88f, 1.00f), -10f);
            BuildAssetCrystalCluster(parent, "Crystal_Camp_Right_Asset", "Assets/SineVFX/TranslucentCrystals/Prefabs/Crystalsv05.prefab", new Vector3(3.16f, 0.22f, 2.08f), new Vector3(1.10f, 1.76f, 0.98f), 12f);
            BuildAssetCrystalCluster(parent, "Crystal_Shore_Left_Asset", "Assets/SineVFX/TranslucentCrystals/Prefabs/Crystalsv07.prefab", new Vector3(-4.60f, 0.16f, -0.28f), new Vector3(0.88f, 1.42f, 0.82f), -18f);
            BuildAssetCrystalCluster(parent, "Crystal_Shore_Right_Asset", "Assets/SineVFX/TranslucentCrystals/Prefabs/Crystalsv02.prefab", new Vector3(4.60f, 0.16f, -0.28f), new Vector3(0.88f, 1.42f, 0.82f), 18f);

            // Lived-in camp dressing around the fire.
            InstantiateAssetPrefab(parent, "Assets/InnerverseInteractive/Ultimate Nature – Starter/Environment/Props/Logs/Prefabs/UNS_Log.prefab",
                "Asset_UNS_Log_Seat", new Vector3(-2.30f, 0.36f, 2.35f), new Vector3(1.55f, 0.42f, 0.48f),
                Quaternion.Euler(0f, 74f, 0f), Materials["wood_dark"]);
            InstantiateAssetPrefab(parent, "Assets/InnerverseInteractive/Ultimate Nature – Starter/Environment/Props/Logs/Prefabs/UNS_Stump.prefab",
                "Asset_UNS_Stump_Seat", new Vector3(-0.62f, 0.36f, 2.62f), new Vector3(0.62f, 0.50f, 0.62f),
                Quaternion.Euler(0f, 23f, 0f), Materials["wood_dark"]);
            InstantiateAssetPrefab(parent, "Assets/InnerverseInteractive/Ultimate Nature – Starter/Environment/Props/Branches/Prefabs/UNS_Branch.prefab",
                "Asset_UNS_Firewood", new Vector3(-2.75f, 0.36f, 1.05f), new Vector3(0.92f, 0.20f, 0.34f),
                Quaternion.Euler(0f, 118f, 0f), Materials["wood_dark"]);
            InstantiateAssetPrefab(parent, "Assets/InnerverseInteractive/Ultimate Nature – Starter/Environment/Vegetation/Mushrooms/Prefabs/UNS_Mushroom_Patch.prefab",
                "Asset_UNS_Mushrooms", new Vector3(4.05f, 0.12f, -1.35f), new Vector3(0.78f, 0.46f, 0.72f),
                Quaternion.Euler(0f, 55f, 0f), Materials["cream"]);

            BuildLamp(parent, new Vector3(-2.60f, 0.15f, -2.10f), 0.98f);
            BuildLamp(parent, new Vector3(2.60f, 0.15f, -2.10f), 0.98f);
            BuildLamp(parent, new Vector3(-4.35f, 0.12f, -0.62f), 0.86f);
            BuildLamp(parent, new Vector3(4.35f, 0.12f, -0.62f), 0.86f);

            var flowerPositions = new[]
            {
                new Vector3(-2.42f, 0.12f, -0.58f), new Vector3(2.52f, 0.12f, -0.62f),
                new Vector3(-3.58f, 0.10f, 1.52f), new Vector3(3.62f, 0.10f, 1.54f)
            };
            for (var i = 0; i < flowerPositions.Length; i++)
            {
                InstantiateAssetPrefab(
                    parent,
                    "Assets/SimpleNaturePack/Prefabs/Flowers_0" + (i % 2 + 1) + ".prefab",
                    "Asset_FlowerPatch_" + i.ToString("00"),
                    flowerPositions[i],
                    new Vector3(0.72f, 0.44f, 0.72f),
                    Quaternion.Euler(0f, i * 73f, 0f),
                    Materials["nature_asset"]);
            }
        }

        static void BuildCrystalCluster(Transform parent, Vector3 position, float scale)
        {
            var root = CreateGroup(parent, "Cyan_Crystal_Cluster");
            var offsets = new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(-0.22f, 0f, 0.10f), new Vector3(0.22f, 0f, 0.08f)
            };
            for (var i = 0; i < offsets.Length; i++)
            {
                CreateMeshObject(
                    root,
                    "Crystal_" + i.ToString("00"),
                    Meshes["cone"],
                    position + offsets[i] * scale,
                    new Vector3((0.34f - i * 0.04f) * scale, (1.05f - i * 0.18f) * scale, (0.34f - i * 0.04f) * scale),
                    Quaternion.Euler(0f, i * 28f, i == 1 ? -8f : i == 2 ? 8f : 0f),
                    new[] { Materials["crystal"], Materials["crystal"] });
                NightEmissionRenderers.Add(root.GetChild(root.childCount - 1).GetComponent<Renderer>());
            }
        }

        static void BuildAssetCrystalCluster(
            Transform parent,
            string name,
            string assetPath,
            Vector3 position,
            Vector3 targetSize,
            float yaw)
        {
            // Geometry from the crystal pack, material from ours. Its own
            // SineVFX/TranslucentCrystals/Crystal shader targets the built-in
            // pipeline and renders magenta under URP, so the override is not
            // optional here.
            var instance = InstantiateAssetPrefab(
                parent,
                assetPath,
                name,
                position,
                targetSize,
                Quaternion.Euler(0f, yaw, 0f),
                Materials["crystal"]);

            if (instance == null)
            {
                BuildCrystalCluster(parent, position, targetSize.y / 1.32f);
                return;
            }

            assetCrystalClusterCount++;
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                NightEmissionRenderers.Add(renderer);
        }

        static void BuildLamp(Transform parent, Vector3 position, float scale)
        {
            var root = CreateGroup(parent, "Pale_Gold_Lamp");
            CreatePrimitive(root, "Base", PrimitiveType.Cylinder, position + new Vector3(0f, 0.12f, 0f) * scale, new Vector3(0.34f, 0.12f, 0.34f) * scale, Materials["gold"]);
            // The reference lamps are not plain gold posts: the shaft is a cyan
            // crystal column captured between two gold collars.
            CreatePrimitive(root, "Post_Lower", PrimitiveType.Cylinder, position + new Vector3(0f, 0.32f, 0f) * scale, new Vector3(0.10f, 0.14f, 0.10f) * scale, Materials["gold"]);
            CreatePrimitive(root, "Post_Crystal_Shaft", PrimitiveType.Cylinder, position + new Vector3(0f, 0.76f, 0f) * scale, new Vector3(0.115f, 0.32f, 0.115f) * scale, Materials["crystal"]);
            CreatePrimitive(root, "Post_Upper", PrimitiveType.Cylinder, position + new Vector3(0f, 1.20f, 0f) * scale, new Vector3(0.10f, 0.14f, 0.10f) * scale, Materials["gold"]);
            CreatePrimitive(root, "Glow", PrimitiveType.Sphere, position + new Vector3(0f, 1.43f, 0f) * scale, new Vector3(0.30f, 0.34f, 0.30f) * scale, Materials["lamp_glow"], Quaternion.identity, false, true);
            var glowRenderer = root.GetChild(root.childCount - 1).GetComponent<Renderer>();
            if (glowRenderer != null) NightEmissionRenderers.Add(glowRenderer);
            CreateMeshObject(root, "Cap", Meshes["cone"], position + new Vector3(0f, 1.70f, 0f) * scale, new Vector3(0.48f, 0.38f, 0.48f) * scale, Quaternion.identity, new[] { Materials["gold"], Materials["gold"] });
            var lightObject = new GameObject("Night_PointLight");
            lightObject.transform.SetParent(root, false);
            lightObject.transform.position = position + new Vector3(0f, 1.43f, 0f) * scale;
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Html("#FFD890");
            light.intensity = 0f;
            light.range = 3.4f * scale;
            light.shadows = LightShadows.None;
            NightLights.Add(light);
        }

        static void BuildFarBank(Transform parent)
        {
            // A single flattened sphere behind the lake reads as a green wall and
            // gives the frame no depth past the waterfalls. The reference stacks
            // three distinct distance layers, so build them separately: hazed
            // ridges at the horizon, an irregular far shore, then islands sitting
            // in the water between that shore and the plaza.
            // Deliberately primitives, not the nature pack's mountain. Ridge
            // height here is set by projection rather than by intuition: the hero
            // camera sits 32.1 deg below level with a 16.5 deg half-FOV, so the
            // true horizon falls above the top of the frame and distant ground
            // compresses into a narrow band near it. Fitting a 54x45x51 mountain
            // into that band forces a 5:1 anisotropic squash that flattens its
            // facets into slabs. The reference's far distance is soft hazed
            // treeline anyway, so silhouette is all that survives the depth of
            // field — and a rounded primitive gives a cleaner one.
            // Pushed back behind the channel banks (which now run to z=32). The
            // height ceiling moves with distance: at z=30 anything over 2.64
            // leaves the top of frame, at z=33 over 1.81.
            var ridges = new[]
            {
                new Vector4(-10.0f, 24.5f, 22.0f, 2.00f),
                new Vector4(2.5f, 26.5f, 28.0f, 1.70f),
                new Vector4(12.0f, 24.0f, 18.0f, 1.50f)
            };
            for (var i = 0; i < ridges.Length; i++)
            {
                var ridge = ridges[i];
                CreatePrimitive(
                    parent,
                    "Far_Ridge_" + i.ToString("00"),
                    PrimitiveType.Sphere,
                    new Vector3(ridge.x, -0.35f, ridge.y),
                    new Vector3(ridge.z, ridge.w, ridge.z * 0.42f),
                    Materials["far_ridge"]);

                // A broken tree line on the crest keeps the ridge from reading as
                // one smooth dome.
                for (var t = -3; t <= 3; t++)
                {
                    var treeX = ridge.x + t * (ridge.z * 0.115f);
                    var crest = ridge.w - 0.42f - Mathf.Abs(t) * 0.09f;
                    if (crest <= 0.12f) continue;
                    BuildPine(parent, new Vector3(treeX, crest, ridge.y - 0.55f), 0.30f + ((t * t + i) % 3) * 0.055f);
                }
            }

            // The old Far_Shore_Silhouette is gone: a 14-wide mound at z=11.35 sat
            // straight across the channel and capped the recession the reference
            // depends on. The banks carry the shoreline now, so this only has to
            // break their inner edge up.
            for (var i = 0; i < 8; i++)
            {
                var side = i % 2 == 0 ? -1f : 1f;
                var depth = 8.5f + (i / 2) * 5.4f;
                var falloff = 1f - (i / 2) * 0.16f;
                InstantiateAssetPrefab(
                    parent,
                    "Assets/InnerverseInteractive/Ultimate Nature – Starter/Environment/Rocks/Forest/Prefabs/UNS_Standard_Rock_0" + (i % 5 + 1) + ".prefab",
                    "Channel_Edge_Outcrop_" + i.ToString("00"),
                    new Vector3(side * (5.9f + (i % 3) * 0.42f), 0.05f, depth),
                    new Vector3(1.95f * falloff, 0.78f * falloff, 1.45f * falloff),
                    Quaternion.Euler(0f, i * 53f, 0f),
                    i % 2 == 0 ? Materials["moss"] : Materials["stone_light"]);
            }

            // Islands in the open water: the reference uses these to stop the
            // lake reading as an empty plane between the bridge and the horizon.
            // Spread down the channel rather than clustered just behind the
            // bridge, so they step the eye back toward the horizon.
            var islands = new[]
            {
                new Vector3(-3.85f, 12.0f, 1.70f),
                new Vector3(3.95f, 15.5f, 1.35f),
                new Vector3(-1.60f, 18.0f, 1.05f),
                new Vector3(2.40f, 21.5f, 0.80f)
            };
            for (var i = 0; i < islands.Length; i++)
            {
                var island = islands[i];
                var group = CreateGroup(parent, "Lake_Island_" + i.ToString("00"));
                InstantiateAssetPrefab(
                    group,
                    "Assets/InnerverseInteractive/Ultimate Nature – Starter/Environment/Rocks/River/Prefabs/UNS_Tiny_Rock_0" + (i % 5 + 1) + ".prefab",
                    "Island_Rock",
                    new Vector3(island.x, 0.02f, island.y),
                    new Vector3(island.z, 0.34f, island.z * 0.78f),
                    Quaternion.Euler(0f, i * 71f, 0f),
                    Materials["stone_light"]);
                CreatePrimitive(
                    group,
                    "Island_Grass",
                    PrimitiveType.Sphere,
                    new Vector3(island.x, 0.24f, island.y),
                    new Vector3(island.z * 0.86f, 0.20f, island.z * 0.66f),
                    Materials["moss"]);
                BuildPine(group, new Vector3(island.x + 0.08f, 0.30f, island.y), 0.46f + i * 0.07f);
            }

            // Tree line down both banks instead of a row straight across the
            // water. Scale falls off with depth so the channel visibly narrows.
            for (var side = -1; side <= 1; side += 2)
            {
                for (var i = 0; i < 9; i++)
                {
                    var depth = 7.0f + i * 2.05f;
                    var falloff = Mathf.Max(0.24f, 0.72f - i * 0.055f);
                    BuildPine(
                        parent,
                        new Vector3(side * (5.95f + depth * 0.055f + ((i % 3) * 0.30f)), 0.32f, depth),
                        falloff);
                }
            }

            // Bank dressing. Extending the world sideways fixed the empty corners
            // but replaced them with two flat pale slabs, which is its own kind of
            // wrong: the reference has no bare ground anywhere. Rolling mounds
            // break the plane, then planting and rock sit on top of it. Everything
            // seats on the bank top at y=0.33 and shrinks with depth.
            var bankRandom = new System.Random(613);
            for (var side = -1; side <= 1; side += 2)
            {
                for (var i = 0; i < 9; i++)
                {
                    // The visible bank is a wedge that only opens up with depth:
                    // measured against the portrait frustum it is 0.42 wide at
                    // z=6, 1.76 at z=15 and 3.13 at z=24, against a shoreline
                    // fixed at |x|=5.55. Dressing the near bank is therefore
                    // wasted work, so the run starts at z=10 and the offset
                    // follows the widening wedge rather than sitting at a fixed
                    // distance out.
                    var depth = 10.0f + i * 2.0f;
                    var falloff = Mathf.Max(0.30f, 0.92f - i * 0.075f);
                    var outX = side * (5.90f + (depth - 10f) * 0.14f + ((i * 5) % 3) * 0.22f);

                    CreatePrimitive(
                        parent,
                        "Bank_Mound_" + (side < 0 ? "L" : "R") + i.ToString("00"),
                        PrimitiveType.Sphere,
                        new Vector3(outX + side * 0.90f, 0.18f, depth),
                        new Vector3(4.0f * falloff, 0.58f * falloff, 3.2f * falloff),
                        i % 3 == 0 ? Materials["moss"] : Materials["grass"]);

                    InstantiateAssetPrefab(
                        parent,
                        "Assets/InnerverseInteractive/Ultimate Nature – Starter/Environment/Vegetation/Bushes/Prefabs/UNS_Bush.prefab",
                        "Bank_Bush_" + (side < 0 ? "L" : "R") + i.ToString("00"),
                        new Vector3(outX + side * Next(bankRandom, -0.35f, 0.95f), 0.33f, depth + Next(bankRandom, -1.1f, 1.1f)),
                        new Vector3(1.75f, 1.35f, 1.55f) * falloff,
                        Quaternion.Euler(0f, Next(bankRandom, 0f, 360f), 0f),
                        i % 2 == 0 ? Materials["foliage_mid"] : Materials["foliage_dark"]);

                    if (i % 2 == 0)
                    {
                        InstantiateAssetPrefab(
                            parent,
                            "Assets/InnerverseInteractive/Ultimate Nature – Starter/Environment/Rocks/Forest/Prefabs/UNS_Standard_Rock_0" + (i % 5 + 1) + ".prefab",
                            "Bank_Rock_" + (side < 0 ? "L" : "R") + i.ToString("00"),
                            new Vector3(outX + side * 0.55f, 0.32f, depth + 1.35f),
                            new Vector3(1.45f, 0.85f, 1.20f) * falloff,
                            Quaternion.Euler(0f, i * 47f, 0f),
                            Materials["stone_light"]);
                    }

                    if (i < 6)
                    {
                        BuildBroadleafTree(
                            parent,
                            new Vector3(outX + side * 1.35f, 0.33f, depth - 0.8f),
                            0.55f * falloff,
                            i * 31 + (side < 0 ? 5 : 17));
                    }
                }
            }
        }

        static Camera CreateCamera(Transform parent)
        {
            var go = new GameObject("Moonlake_HeroCamera");
            go.transform.SetParent(parent, false);
            go.tag = "MainCamera";
            go.transform.position = new Vector3(0f, 18.0f, -25.0f);
            go.transform.rotation = Quaternion.LookRotation(CameraTarget - go.transform.position, Vector3.up);

            var camera = go.AddComponent<Camera>();
            camera.fieldOfView = 33.0f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 120f;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = Html("#8FC8E2");
            camera.allowHDR = true;
            camera.allowMSAA = true;
            camera.depth = 0f;
            return camera;
        }

        /// <summary>
        /// The owner reference reads as a physical miniature largely because of
        /// its shallow focus: the foreground planting and the far islands fall
        /// off while the plaza stays sharp. The scene had no Volume at all, so
        /// none of that was reproducible through lighting alone.
        /// </summary>
        static void CreatePostProcessing(Transform parent, Camera camera)
        {
            // Post-processing is off by default on a URP camera.
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraData.antialiasingQuality = AntialiasingQuality.High;
            cameraData.dithering = true;

            var profilePath = OutputRoot + "/M_HD25D_MoonlakeDayVolume.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, profilePath);
            }
            else
            {
                // Rebuild the stack from scratch so tuning never accumulates
                // stale overrides across runs.
                foreach (var component in profile.components.ToArray())
                {
                    profile.Remove(component.GetType());
                    UnityEngine.Object.DestroyImmediate(component, true);
                }
            }

            // Focus sits on the companion plaza: the distance from the fixed hero
            // camera to its look target.
            var focusDistance = Vector3.Distance(camera.transform.position, CameraTarget);

            // Blur strength has to be read against this scene's scale, not
            // against normal photographic intuition. The diorama only spans
            // roughly 26 m to 48 m from a camera focused at 33 m, so a 145 mm
            // f/2.4 lens resolves to a circle of confusion of about 5 px at
            // 1080x1920 — real, but invisible on flat-shaded low-poly forms.
            // 220 mm at f/2.2 lands near 15 px, which is where the foreground
            // planting and far bank actually start to read as out of focus.
            var depthOfField = profile.Add<DepthOfField>(true);
            Override(depthOfField.mode, DepthOfFieldMode.Bokeh);
            Override(depthOfField.focusDistance, focusDistance);
            Override(depthOfField.focalLength, 220f);
            Override(depthOfField.aperture, 2.2f);
            Override(depthOfField.bladeCount, 6);
            Override(depthOfField.bladeCurvature, 0.85f);

            var tonemapping = profile.Add<Tonemapping>(true);
            Override(tonemapping.mode, TonemappingMode.Neutral);

            var colorAdjustments = profile.Add<ColorAdjustments>(true);
            Override(colorAdjustments.postExposure, 0.04f);
            Override(colorAdjustments.contrast, 12f);
            Override(colorAdjustments.saturation, 12f);

            var whiteBalance = profile.Add<WhiteBalance>(true);
            Override(whiteBalance.temperature, 8f);
            Override(whiteBalance.tint, -2f);

            // Deliberately high threshold: the SSOT allows bloom only on the
            // crystal and care-light accents, never as a global haze.
            var bloom = profile.Add<Bloom>(true);
            Override(bloom.threshold, 1.15f);
            Override(bloom.intensity, 0.38f);
            Override(bloom.scatter, 0.62f);
            Override(bloom.tint, Html("#CFF3FF"));

            var vignette = profile.Add<Vignette>(true);
            Override(vignette.intensity, 0.18f);
            Override(vignette.smoothness, 0.45f);

            EditorUtility.SetDirty(profile);

            var go = new GameObject("Moonlake_DayVolume_Global");
            go.transform.SetParent(parent, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.weight = 1f;
            volume.sharedProfile = profile;
        }

        /// <summary>
        /// A volume parameter only contributes once its override flag is set;
        /// assigning the value alone silently does nothing.
        /// </summary>
        static void Override<T>(VolumeParameter<T> parameter, T value)
        {
            parameter.overrideState = true;
            parameter.value = value;
        }

        static void CreateLighting(Transform parent, out Light sun, out Light skyFill)
        {
            var key = new GameObject("Day_Key_Directional");
            key.transform.SetParent(parent, false);
            key.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            var keyLight = key.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = Html("#FFF0CF");
            keyLight.intensity = 1.18f;
            keyLight.shadows = LightShadows.Soft;
            keyLight.shadowStrength = 0.68f;
            RenderSettings.sun = keyLight;
            sun = keyLight;

            var fill = new GameObject("Sky_Fill_Directional");
            fill.transform.SetParent(parent, false);
            fill.transform.rotation = Quaternion.Euler(34f, 145f, 0f);
            var fillLight = fill.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.color = Html("#B9DBF3");
            fillLight.intensity = 0.40f;
            fillLight.shadows = LightShadows.None;
            skyFill = fillLight;

            CreatePointLight(parent, "Plaza_CareLight", new Vector3(0f, 2.7f, 0.1f), "#FFD99A", 2.2f, 7.0f);
            CreatePointLight(parent, "Water_CyanFill", new Vector3(0.4f, 2.4f, 5.4f), "#72D7F3", 1.6f, 8.0f);
        }

        static void CreatePointLight(Transform parent, string name, Vector3 position, string color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Html(color);
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
        }

        static MoonlakeCampfireFx BuildCampfire(Transform parent)
        {
            var root = CreateGroup(parent, "Campfire_Shared_Between_Tents");
            var bottomCenter = new Vector3(-1.55f, 0.36f, 1.72f);

            CreatePrimitive(
                root,
                "Charred_Ground_Disc",
                PrimitiveType.Cylinder,
                bottomCenter - Vector3.up * 0.025f,
                new Vector3(1.16f, 0.025f, 1.02f),
                Materials["charcoal"]);

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Art/Moonlake/R3_Split/moonlake_r3_campfire_split.asset");
            Renderer campfireRenderer;
            if (mesh != null)
            {
                TrackAssetPath("Assets/Art/Moonlake/R3_Split/moonlake_r3_campfire_split.asset");
                campfireRenderer = CreateMeshObject(
                    root,
                    "Asset_Moonlake_Campfire_Mesh",
                    mesh,
                    Vector3.zero,
                    Vector3.one,
                    Quaternion.Euler(0f, -18f, 0f),
                    new[] { Materials["campfire_stone"], Materials["flame"], Materials["ember"] });
                FitObjectToBounds(campfireRenderer.gameObject, bottomCenter, new Vector3(1.36f, 1.05f, 1.26f));
                assetBackedVisualCount++;
            }
            else
            {
                campfireRenderer = CreatePrimitive(
                    root,
                    "Fallback_Flame",
                    PrimitiveType.Sphere,
                    bottomCenter + Vector3.up * 0.54f,
                    new Vector3(0.38f, 0.70f, 0.38f),
                    Materials["flame"],
                    Quaternion.identity,
                    false,
                    true);
            }

            var lightObject = new GameObject("Campfire_Warm_PointLight");
            lightObject.transform.SetParent(root, false);
            lightObject.transform.position = bottomCenter + Vector3.up * 0.62f;
            var fireLight = lightObject.AddComponent<Light>();
            fireLight.type = LightType.Point;
            fireLight.color = Html("#FF9B46");
            fireLight.intensity = 2.4f;
            fireLight.range = 4.2f;
            fireLight.shadows = LightShadows.Soft;
            fireLight.shadowStrength = 0.40f;

            var flameParticles = CreateCampfireParticleSystem(
                root,
                "Campfire_Flame_Particles",
                bottomCenter + Vector3.up * 0.28f,
                Materials["flame_particle"],
                18f,
                0.68f,
                0.34f,
                new Color(1f, 0.45f, 0.12f, 0.72f),
                false);
            var emberParticles = CreateCampfireParticleSystem(
                root,
                "Campfire_Ember_Particles",
                bottomCenter + Vector3.up * 0.38f,
                Materials["ember_particle"],
                6f,
                1.35f,
                0.075f,
                new Color(1f, 0.68f, 0.18f, 0.90f),
                true);

            var fx = root.gameObject.AddComponent<MoonlakeCampfireFx>();
            fx.fireLight = fireLight;
            fx.flameRenderers = new[] { campfireRenderer };
            fx.flameParticles = flameParticles;
            fx.emberParticles = emberParticles;
            fx.ApplyImmediate();
            return fx;
        }

        static ParticleSystem CreateCampfireParticleSystem(
            Transform parent,
            string name,
            Vector3 position,
            Material material,
            float rate,
            float lifetime,
            float size,
            Color color,
            bool embers)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var system = go.AddComponent<ParticleSystem>();
            var main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = lifetime;
            main.startSpeed = embers ? 0.72f : 0.38f;
            main.startSize = size;
            main.startColor = color;
            main.maxParticles = embers ? 40 : 80;
            main.gravityModifier = embers ? -0.05f : -0.015f;

            var emission = system.emission;
            emission.rateOverTime = rate;
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = embers ? 0.24f : 0.30f;
            var noise = system.noise;
            noise.enabled = true;
            noise.strength = embers ? 0.22f : 0.10f;
            noise.frequency = 0.75f;

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            visualCount++;
            return system;
        }

        static MoonlakeSkyDayWeatherController CreateSkyDayWeatherSystem(
            Transform parent,
            Camera camera,
            Light sun,
            Light skyFill,
            MoonlakeCampfireFx campfire)
        {
            CreateGroup(parent, "01_Asset_Skyboxes_Runtime_Selected");
            var clouds = CreateGroup(parent, "02_Separate_Cloud_Layer");
            var weatherFx = CreateGroup(parent, "03_Weather_FX_No_Geometry_Replacement");

            var cloudRenderers = new List<Renderer>();
            // Lowered from the previous 4.65-5.75 band: at z=20 the top of frame
            // sits at roughly y=5.4, so the old heights pushed most of each puff
            // out of shot.
            var cloudData = new[]
            {
                new Vector4(-5.6f, 3.45f, 20.0f, 2.4f),
                new Vector4(5.9f, 3.80f, 21.5f, 2.8f),
                new Vector4(-0.4f, 4.15f, 24.0f, 1.9f)
            };
            for (var i = 0; i < cloudData.Length; i++)
            {
                var data = cloudData[i];
                BuildSoftCloud(
                    clouds,
                    "Cloud_Puff_" + (i + 1).ToString("00"),
                    new Vector3(data.x, data.y, data.z),
                    data.w,
                    i * 29 + 11,
                    cloudRenderers);
            }

            var rain = CreateWeatherParticleSystem(
                weatherFx,
                "Rain_Runtime_FX",
                new Vector3(0f, 10.8f, 2.0f),
                Materials["rain_particle"],
                true);
            var mist = CreateWeatherParticleSystem(
                weatherFx,
                "Mist_Runtime_FX",
                new Vector3(0f, 1.7f, 4.5f),
                Materials["mist_particle"],
                false);

            var controller = parent.gameObject.AddComponent<MoonlakeSkyDayWeatherController>();
            controller.heroCamera = camera;
            controller.sun = sun;
            controller.skyFill = skyFill;
            controller.cloudLayer = clouds;
            controller.cloudRenderers = cloudRenderers.ToArray();
            controller.rainParticles = rain;
            controller.mistParticles = mist;
            controller.nightLights = NightLights.ToArray();
            controller.crystalAndLanternRenderers = NightEmissionRenderers.ToArray();
            controller.campfire = campfire;
            controller.clearDaySky = LoadAndTrackAsset<Material>("Assets/AllSkyFree/Cartoon Base BlueSky/Day_BlueSky_Nothing Equirect.mat");
            controller.sunsetSky = LoadAndTrackAsset<Material>("Assets/AllSkyFree/Cold Sunset/Cold Sunset Equirect.mat");
            controller.overcastSky = LoadAndTrackAsset<Material>("Assets/AllSkyFree/Overcast Low/AllSky_Overcast4_Low Equirect.mat");
            controller.nightSky = LoadAndTrackAsset<Material>("Assets/AllSkyFree/Cartoon Base NightSky/Cartoon Base NightSky Equirect.mat");
            controller.timeOfDay = 10.5f;
            controller.weather = MoonlakeSkyDayWeatherController.WeatherState.Clear;
            controller.autoAdvanceTime = false;
            controller.fullDaySeconds = 480f;

            controller.ApplyImmediate();
            return controller;
        }

        static void BuildSoftCloud(
            Transform parent,
            string name,
            Vector3 center,
            float width,
            int seed,
            List<Renderer> renderers)
        {
            var root = CreateGroup(parent, name);
            var random = new System.Random(seed);

            // Scaled from the source aspect (14 x 10 x 50) rather than forced
            // into a shallow box, so the cloud keeps its depth instead of being
            // squashed into a card. The mesh comes from SimpleSky; the material
            // is ours because the weather controller fades cloud opacity through
            // the shared cloud material.
            var cloudAsset = InstantiateAssetPrefab(
                root,
                "Assets/SimpleSky/Prefabs/Cloud_0" + (Mathf.Abs(seed) % 6 + 1) + ".prefab",
                "Asset_SimpleSky_Cloud",
                center - Vector3.up * width * 0.30f,
                new Vector3(width * 1.50f, width * 1.07f, width * 5.36f),
                Quaternion.Euler(0f, (seed * 23f) % 40f - 20f, 0f),
                Materials["cloud_asset"]);
            if (cloudAsset != null)
            {
                renderers.AddRange(cloudAsset.GetComponentsInChildren<Renderer>(true));
                return;
            }

            for (var i = 0; i < 6; i++)
            {
                var t = i / 5f;
                var x = Mathf.Lerp(-width * 0.46f, width * 0.46f, t);
                var y = Mathf.Sin(t * Mathf.PI) * width * 0.13f + Next(random, -0.06f, 0.06f);
                var size = width * Next(random, 0.22f, 0.34f);
                var renderer = CreatePrimitive(
                    root,
                    "Cloud_Lobe_" + i.ToString("00"),
                    PrimitiveType.Sphere,
                    center + new Vector3(x, y, Next(random, -0.20f, 0.20f)),
                    new Vector3(size * 1.35f, size * 0.70f, size),
                    Materials["cloud_soft"],
                    Quaternion.identity,
                    false,
                    true);
                renderers.Add(renderer);
            }
        }

        static ParticleSystem CreateWeatherParticleSystem(
            Transform parent,
            string name,
            Vector3 position,
            Material material,
            bool rain)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var system = go.AddComponent<ParticleSystem>();
            var main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = rain ? 1.45f : 8.5f;
            main.startSpeed = rain ? 10.5f : 0.14f;
            main.startSize = rain ? 0.045f : 1.75f;
            main.startColor = rain ? new Color(0.70f, 0.86f, 1f, 0.48f) : new Color(0.82f, 0.91f, 0.93f, 0.035f);
            main.maxParticles = rain ? 900 : 55;
            main.gravityModifier = rain ? 0.18f : 0f;

            var emission = system.emission;
            emission.rateOverTime = rain ? 340f : 4f;
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = rain ? new Vector3(14f, 0.5f, 16f) : new Vector3(13f, 2.0f, 12f);
            var noise = system.noise;
            noise.enabled = !rain;
            noise.strength = 0.30f;
            noise.frequency = 0.18f;
            noise.scrollSpeed = 0.10f;

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = rain ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            renderer.lengthScale = rain ? 0.28f : 1f;
            renderer.velocityScale = rain ? 0.06f : 0f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            visualCount++;
            return system;
        }

        static GameObject CreateCompanion(Transform stage, Camera camera)
        {
            var root = new GameObject("CompanionRoot");
            root.transform.SetParent(stage, false);
            root.transform.position = new Vector3(0f, 0.36f, 0.12f);

            var collider = root.AddComponent<CapsuleCollider>();
            collider.radius = 0.26f;
            collider.height = 0.62f;
            collider.center = new Vector3(0f, 0.31f, 0f);
            collider.isTrigger = true;

            // The 512px illustrated sheets imported from the character repo are
            // the approved art. The loose legacy frames stay wired underneath so
            // the scene still builds on a machine that has not run the importer.
            var animationSet = AssetDatabase.LoadAssetAtPath<MoonlakeCharacterAnimationSet>(CompanionAnimationSetPath);
            var restingClip = animationSet != null ? animationSet.FindFirst("idle_calm", "front_walk") : null;

            var visual = new GameObject("VisualPresentationRoot");
            visual.transform.SetParent(root.transform, false);
            var spriteRenderer = visual.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = restingClip != null ? restingClip.frames[0] : LoadSprite("front", 0);
            spriteRenderer.sortingOrder = 20;

            var shadow = new GameObject("ContactShadow");
            shadow.transform.SetParent(root.transform, false);
            shadow.transform.localPosition = new Vector3(0f, 0.015f, 0f);
            shadow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var shadowRenderer = shadow.AddComponent<SpriteRenderer>();
            shadowRenderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Companions/GreyshadeCat/contact_shadow_blob.png");
            shadowRenderer.color = new Color(0.16f, 0.18f, 0.19f, 0.42f);
            shadowRenderer.sortingOrder = 5;
            shadow.transform.localScale = Vector3.one * 0.86f;

            var billboard = root.AddComponent<MoonlakeCompanionBillboard>();
            billboard.targetCamera = camera;
            billboard.spriteRenderer = spriteRenderer;
            billboard.contactShadow = shadow.transform;
            billboard.animationSet = animationSet;
            billboard.poseAnimationId = "idle_calm";
            billboard.frontFrames = LoadDirection("front");
            billboard.backFrames = LoadDirection("back");
            billboard.leftFrames = LoadDirection("left");
            billboard.rightFrames = LoadDirection("right");
            billboard.worldHeight = 1.42f;
            billboard.framesPerSecond = 8f;
            billboard.animate = true;
            billboard.snapToGround = true;
            billboard.groundMask = ~0;
            return root;
        }

        static Sprite[] LoadDirection(string direction)
        {
            var frames = new Sprite[8];
            for (var i = 0; i < frames.Length; i++)
                frames[i] = LoadSprite(direction, i);
            return frames;
        }

        static Sprite LoadSprite(string direction, int index)
        {
            var path = string.Format("Assets/Art/Companions/GreyshadeCat/greyshade_walk_{0}_{1:00}.png", direction, index);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static void CreateGameplayProbes(Transform parent, GameObject companion, Camera camera)
        {
            CreateColliderProxy(parent, "Walkable_Plaza", new Vector3(0f, 0.22f, 0.14f), new Vector3(6.25f, 0.18f, 6.15f), false);
            CreateColliderProxy(parent, "Walkable_Approach", new Vector3(0f, 0.08f, -1.78f), new Vector3(5.0f, 0.16f, 2.2f), false);
            CreateColliderProxy(parent, "Walkable_Bridge", new Vector3(0.95f, 0.35f, 4.22f), new Vector3(1.14f, 0.20f, 5.45f), false);
            CreateColliderProxy(parent, "Blocker_Tent_Left", new Vector3(-3.05f, 1.12f, 0.58f), new Vector3(2.70f, 2.3f, 2.70f), false);
            CreateColliderProxy(parent, "Blocker_Tent_Right", new Vector3(3.05f, 1.12f, 0.58f), new Vector3(2.70f, 2.3f, 2.70f), false);
            CreateColliderProxy(parent, "Blocker_Cliff_Left", new Vector3(-3.35f, 2.88f, 7.35f), new Vector3(3.55f, 5.75f, 3.35f), false);
            CreateColliderProxy(parent, "Blocker_Cliff_Right", new Vector3(3.35f, 2.88f, 7.35f), new Vector3(3.55f, 5.75f, 3.35f), false);

            var waypointRoot = CreateGroup(parent, "CompanionWaypoints");
            var positions = new[]
            {
                new Vector3(0f, 0.36f, -1.05f), new Vector3(-1.95f, 0.36f, 0.15f),
                new Vector3(0f, 0.36f, 1.55f), new Vector3(1.95f, 0.36f, 0.15f)
            };
            var waypoints = new Transform[positions.Length];
            for (var i = 0; i < positions.Length; i++)
            {
                waypoints[i] = CreateGroup(waypointRoot, "Waypoint_" + i.ToString("00"));
                waypoints[i].position = positions[i];
            }

            var billboard = companion.GetComponent<MoonlakeCompanionBillboard>();
            var inputActions = LoadAndTrackAsset<UnityEngine.InputSystem.InputActionAsset>(
                "Assets/InputSystem_Actions.inputactions");

            var controller = companion.AddComponent<MoonlakeCompanionController>();
            controller.inputActions = inputActions;
            controller.billboard = billboard;
            controller.speed = 2.4f;
            controller.bodyRadius = 0.26f;
            controller.blockingMask = ~0;
            controller.groundMask = ~0;
            controller.enabled = false;   // the camera rig turns this on in play mode

            var gateway = parent.gameObject.AddComponent<MoonlakeStageGateway>();
            gateway.inputActions = inputActions;

            var walkableProbe = companion.AddComponent<MoonlakeWalkableProbe>();
            walkableProbe.billboard = billboard;
            walkableProbe.waypoints = waypoints;
            walkableProbe.speed = 1.2f;
            walkableProbe.arriveDistance = 0.14f;
            walkableProbe.loop = true;
            walkableProbe.blockingMask = ~0;
            walkableProbe.bodyRadius = 0.26f;

            var fadeController = parent.gameObject.AddComponent<MoonlakeOccluderFade>();
            fadeController.heroCamera = camera;
            fadeController.subject = companion.transform;
            fadeController.occluders = ForegroundRenderers.Distinct().ToList();
            fadeController.fadedAlpha = 0.28f;
            fadeController.fadeSpeed = 6f;
            fadeController.probeRadius = 0.48f;
        }


        /// <summary>
        /// Adds the play camera alongside the fixed hero framing. Same 33 deg lens
        /// and the same ~32 deg pitch as the diorama shot, so switching modes does
        /// not change how the habitat reads — only how close it sits and whether
        /// it tracks the companion.
        /// </summary>
        static void CreatePlayCameraRig(Transform cameras, Camera heroCamera, GameObject companion, Transform probes)
        {
            var followOffset = new Vector3(0f, 10.5f, -16.5f);

            var playGo = new GameObject("Moonlake_PlayCamera");
            playGo.transform.SetParent(cameras, false);
            playGo.transform.position = companion.transform.position + followOffset;
            playGo.transform.rotation = Quaternion.LookRotation(
                companion.transform.position + Vector3.up * 0.7f - playGo.transform.position, Vector3.up);

            var playCamera = playGo.AddComponent<Camera>();
            playCamera.fieldOfView = 33f;
            playCamera.nearClipPlane = 0.05f;
            playCamera.farClipPlane = 120f;
            playCamera.clearFlags = CameraClearFlags.Skybox;
            playCamera.backgroundColor = Html("#8FC8E2");
            playCamera.allowHDR = true;
            playCamera.enabled = false;

            var playData = playCamera.GetUniversalAdditionalCameraData();
            playData.renderPostProcessing = true;
            playData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            playData.antialiasingQuality = AntialiasingQuality.High;
            playGo.AddComponent<CinemachineBrain>();

            var vcamGo = new GameObject("Moonlake_PlayVCam_Follow");
            vcamGo.transform.SetParent(cameras, false);
            vcamGo.transform.position = playGo.transform.position;
            vcamGo.transform.rotation = playGo.transform.rotation;

            var vcam = vcamGo.AddComponent<CinemachineCamera>();
            vcam.Lens.FieldOfView = 33f;
            vcam.Follow = companion.transform;
            vcam.LookAt = companion.transform;

            // Fixed world-space offset, never an orbit: the reference camera does
            // not rotate around the habitat and neither should play mode.
            var follow = vcamGo.AddComponent<CinemachineFollow>();
            follow.FollowOffset = followOffset;
            follow.TrackerSettings.PositionDamping = new Vector3(0.65f, 0.40f, 0.65f);

            var composer = vcamGo.AddComponent<CinemachineRotationComposer>();
            composer.TargetOffset = new Vector3(0f, 0.7f, 0f);

            var rig = cameras.gameObject.AddComponent<MoonlakeCameraRig>();
            rig.heroCamera = heroCamera;
            rig.playCamera = playCamera;
            rig.companionBillboard = companion.GetComponent<MoonlakeCompanionBillboard>();
            rig.occluderFade = probes.GetComponent<MoonlakeOccluderFade>();
            rig.ambientProbe = companion.GetComponent<MoonlakeWalkableProbe>();
            rig.playerController = companion.GetComponent<MoonlakeCompanionController>();
            rig.dayVolume = UnityEngine.Object.FindAnyObjectByType<Volume>();
            rig.focusSubject = companion.transform;
            rig.heroFocusTarget = CameraTarget;
            rig.SetMode(MoonlakeCameraRig.Mode.HeroDiorama);
        }

        /// <summary>
        /// Named anchors for the five Heart-Core Orbit zones that hang off the
        /// Moonlake path, plus the habitat interaction points.
        ///
        /// Markers only. Moonlake Camp is the daily habitat layer and is not
        /// itself an Orbit stage, and the stages live in their own scenes, so
        /// nothing here carries unlock state, progress or reward data — that is
        /// owned by the Nexus Link product.
        /// </summary>
        static void CreatePathNodes(Transform parent, GameObject companion)
        {
            // id, display name, position. The 星林/霧潮 pair unlocks first, then
            // 湖心, 晶岩 and 裂隙 — the order the shared path taxonomy defines.
            var nodes = new[]
            {
                new PathNodeSpec("star_grove", "星林", "PathNode_01_StarGrove_星林", new Vector3(-7.40f, 0.35f, 11.0f)),
                new PathNodeSpec("mist_tide", "霧潮", "PathNode_02_MistTide_霧潮", new Vector3(7.40f, 0.35f, 11.0f)),
                new PathNodeSpec("lake_heart", "湖心", "PathNode_03_LakeHeart_湖心", new Vector3(-3.85f, 0.32f, 12.0f)),
                new PathNodeSpec("crystal_rock", "晶岩", "PathNode_04_CrystalRock_晶岩", new Vector3(4.60f, 0.30f, -0.28f)),
                new PathNodeSpec("rift", "裂隙", "PathNode_05_Rift_裂隙", new Vector3(0.50f, 0.30f, 21.0f))
            };
            foreach (var node in nodes)
            {
                var go = new GameObject(node.ObjectName);
                go.transform.SetParent(parent, false);
                go.transform.position = node.Position;

                var beacon = go.AddComponent<MoonlakePathNodeBeacon>();
                beacon.pathNodeId = node.Id;
                beacon.displayName = node.DisplayName;
                beacon.subject = companion.transform;
                beacon.radius = 1.8f;
                // Every node opens the same placeholder for now; the real stage
                // scenes replace these one at a time.
                beacon.targetSceneName = PlaceholderStageSceneName;
            }

            var habitat = CreateGroup(parent, "HabitatAnchors");
            var anchors = new[]
            {
                new KeyValuePair<string, Vector3>("HabitatAnchor_Campfire_營火", new Vector3(-1.55f, 0.36f, 1.72f)),
                new KeyValuePair<string, Vector3>("HabitatAnchor_Tent_Left_Rest_休息", new Vector3(-3.05f, 0.20f, 0.58f)),
                new KeyValuePair<string, Vector3>("HabitatAnchor_Tent_Right_Rest_休息", new Vector3(3.05f, 0.20f, 0.58f)),
                new KeyValuePair<string, Vector3>("HabitatAnchor_Shore_Fishing_釣魚", new Vector3(-2.42f, 0.24f, 4.92f))
            };
            foreach (var anchor in anchors)
            {
                var go = new GameObject(anchor.Key);
                go.transform.SetParent(habitat, false);
                go.transform.position = anchor.Value;
            }
        }


        /// <summary>
        /// Caps how many renderers cast shadows. The scene was submitting 867
        /// casters against a documented budget of 5, and every one of them costs
        /// a shadow-map draw regardless of whether its shadow is visible. A pebble
        /// or a flower 20 m out contributes nothing readable, so casting is kept
        /// for objects that are both large enough to throw a shadow anyone can
        /// see and close enough for it to land inside the frame. Receiving is
        /// untouched — the ground still takes shadows from everything that casts.
        /// </summary>
        static int ApplyShadowCastingBudget(GameObject root)
        {
            var casters = 0;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer) continue;

                var size = renderer.bounds.size;
                var significant = size.magnitude >= 1.90f;
                var inNearField = renderer.bounds.center.z <= 9.0f;
                // Ground slabs and water are broad and flat; their shadows land on
                // themselves and read as nothing.
                var groundPlane = size.y < 0.60f && size.x > 6.0f;

                var cast = significant && inNearField && !groundPlane;
                renderer.shadowCastingMode = cast ? ShadowCastingMode.On : ShadowCastingMode.Off;
                if (cast) casters++;
            }
            return casters;
        }


        /// <summary>One Heart-Core Orbit entry point on the Moonlake path.</summary>
        readonly struct PathNodeSpec
        {
            public readonly string Id;
            public readonly string DisplayName;
            public readonly string ObjectName;
            public readonly Vector3 Position;

            public PathNodeSpec(string id, string displayName, string objectName, Vector3 position)
            {
                Id = id;
                DisplayName = displayName;
                ObjectName = objectName;
                Position = position;
            }
        }

        static void CreateColliderProxy(Transform parent, string name, Vector3 position, Vector3 size, bool isTrigger)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var collider = go.AddComponent<BoxCollider>();
            collider.size = size;
            collider.isTrigger = isTrigger;
        }

        static GameObject InstantiateAssetPrefab(
            Transform parent,
            string assetPath,
            string name,
            Vector3 bottomCenter,
            Vector3 targetSize,
            Quaternion rotation,
            Material overrideMaterial,
            Action<GameObject> palette = null,
            bool keepColliders = false)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
            {
                Debug.LogWarning("[Moonlake HD25D] Optional asset was not available: " + assetPath);
                return null;
            }

            var instance = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
            if (instance == null)
                return null;

            instance.name = name;
            instance.transform.SetParent(parent, false);
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = rotation;
            instance.transform.localScale = Vector3.one;

            if (palette != null)
                palette(instance);
            else if (overrideMaterial != null)
                ApplySingleMaterial(instance, overrideMaterial);

            if (!keepColliders)
            {
                foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.DestroyImmediate(collider);
            }

            FitObjectToBounds(instance, bottomCenter, targetSize);
            var rendererCount = instance.GetComponentsInChildren<Renderer>(true).Length;
            visualCount += rendererCount;
            assetBackedVisualCount += rendererCount;
            TrackAssetPath(assetPath);
            return instance;
        }

        static void ApplySingleMaterial(GameObject root, Material material)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var slotCount = Mathf.Max(1, renderer.sharedMaterials.Length);
                var materials = new Material[slotCount];
                for (var i = 0; i < slotCount; i++)
                    materials[i] = material;
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
        }

        static void ApplyPineAndFruitTreePalette(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var source = renderer.sharedMaterials;
                var mapped = new Material[Mathf.Max(1, source.Length)];
                for (var i = 0; i < mapped.Length; i++)
                {
                    var sourceName = source.Length > i && source[i] != null
                        ? source[i].name.ToLowerInvariant()
                        : string.Empty;
                    if (sourceName.Contains("trunk") || sourceName.Contains("bark")
                        || sourceName.Contains("log") || sourceName.Contains("stump")
                        || sourceName.Contains("color_palette"))
                        mapped[i] = Materials["wood_dark"];
                    else if (sourceName.Contains("leaf") || sourceName.Contains("leaves")
                             || sourceName.Contains("foliage") || sourceName.Contains("branch")
                             || sourceName.Contains("needle"))
                        mapped[i] = Materials["foliage_mid"];
                    else if (sourceName.Contains("fruit") || sourceName.Contains("apple") || sourceName.Contains("plum"))
                        mapped[i] = Materials["flower_pink"];
                    else
                        mapped[i] = Materials["foliage_mid"];
                }
                renderer.sharedMaterials = mapped;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
        }

        /// <summary>
        /// Scales an instance so its world-space extents match
        /// <paramref name="targetSize"/>, then seats it on
        /// <paramref name="bottomCenter"/>.
        ///
        /// Renderer bounds are always world-axis aligned, so a rotated object
        /// reports a box that no longer lines up with its own axes. Deriving the
        /// scale from that box and assigning it to localScale sends each factor
        /// to the wrong axis: a bridge 4.47 long turned 90 degrees had its length
        /// multiplied by the width factor and vice versa, collapsing it into a
        /// wall. Measure unrotated instead, then route each local axis to the
        /// world axis it actually becomes.
        /// </summary>
        static void FitObjectToBounds(GameObject root, Vector3 bottomCenter, Vector3 targetSize)
        {
            var transform = root.transform;
            var rotation = transform.rotation;

            transform.rotation = Quaternion.identity;
            Bounds localBounds;
            var measured = TryGetRendererBounds(root, out localBounds);
            transform.rotation = rotation;

            if (!measured)
            {
                transform.position = bottomCenter;
                return;
            }

            var localSize = localBounds.size;
            var rotationMatrix = Matrix4x4.Rotate(rotation);
            var scale = Vector3.one;
            for (var localAxis = 0; localAxis < 3; localAxis++)
            {
                var worldAxis = 0;
                var strongest = -1f;
                for (var candidate = 0; candidate < 3; candidate++)
                {
                    var weight = Mathf.Abs(rotationMatrix[candidate, localAxis]);
                    if (weight > strongest)
                    {
                        strongest = weight;
                        worldAxis = candidate;
                    }
                }
                scale[localAxis] = targetSize[worldAxis] / Mathf.Max(0.001f, localSize[localAxis]);
            }
            transform.localScale = Vector3.Scale(transform.localScale, scale);

            Bounds bounds;
            if (!TryGetRendererBounds(root, out bounds))
                return;
            transform.position += new Vector3(
                bottomCenter.x - bounds.center.x,
                bottomCenter.y - bounds.min.y,
                bottomCenter.z - bounds.center.z);
        }

        static bool TryGetRendererBounds(GameObject root, out Bounds bounds)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => !(renderer is ParticleSystemRenderer))
                .ToArray();
            if (renderers.Length == 0)
            {
                bounds = new Bounds(root.transform.position, Vector3.zero);
                return false;
            }

            bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return true;
        }

        static T LoadAndTrackAsset<T>(string assetPath) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset != null)
                TrackAssetPath(assetPath);
            else
                Debug.LogWarning("[Moonlake HD25D] Asset unavailable: " + assetPath);
            return asset;
        }

        static void TrackAssetPath(string assetPath)
        {
            if (!AssetPathsUsed.Contains(assetPath))
                AssetPathsUsed.Add(assetPath);
        }

        static Renderer CreatePrimitive(
            Transform parent,
            string name,
            PrimitiveType type,
            Vector3 position,
            Vector3 scale,
            Material material,
            Quaternion? rotation = null,
            bool keepCollider = false,
            bool noShadows = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.rotation = rotation ?? Quaternion.identity;
            go.transform.localScale = scale;
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = noShadows ? ShadowCastingMode.Off : ShadowCastingMode.On;
            renderer.receiveShadows = !noShadows;
            if (!keepCollider)
            {
                var collider = go.GetComponent<Collider>();
                if (collider != null)
                    UnityEngine.Object.DestroyImmediate(collider);
            }
            visualCount++;
            return renderer;
        }

        static Renderer CreateMeshObject(
            Transform parent,
            string name,
            Mesh mesh,
            Vector3 position,
            Vector3 scale,
            Quaternion rotation,
            Material[] materials)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.rotation = rotation;
            go.transform.localScale = scale;
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            visualCount++;
            return renderer;
        }

        static void CreateBeam(Transform parent, string name, Vector3 start, Vector3 end, float thickness, Material material)
        {
            var direction = end - start;
            CreatePrimitive(
                parent,
                name,
                PrimitiveType.Cube,
                (start + end) * 0.5f,
                new Vector3(thickness, direction.magnitude, thickness),
                material,
                Quaternion.FromToRotation(Vector3.up, direction.normalized));
        }

        static float Next(System.Random random, float min, float max)
        {
            return min + (float)random.NextDouble() * (max - min);
        }

        static void CreateMeshLibrary()
        {
            Meshes["cone"] = LoadOrCreateMesh(MeshRoot + "/Moonlake_Cone.asset", 12, false);
            Meshes["tent"] = LoadOrCreateMesh(MeshRoot + "/Moonlake_Tent_Striped.asset", 12, true);
        }

        static Mesh LoadOrCreateMesh(string path, int segments, bool alternatingSubmeshes)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            var isNewAsset = mesh == null;
            if (isNewAsset)
                mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) };
            else
                mesh.Clear();
            var vertices = new List<Vector3> { new Vector3(0f, 1f, 0f), Vector3.zero };
            for (var i = 0; i < segments; i++)
            {
                var angle = Mathf.PI * 2f * i / segments;
                vertices.Add(new Vector3(Mathf.Sin(angle) * 0.5f, 0f, Mathf.Cos(angle) * 0.5f));
            }

            var even = new List<int>();
            var odd = new List<int>();
            for (var i = 0; i < segments; i++)
            {
                var current = 2 + i;
                var next = 2 + (i + 1) % segments;
                var side = alternatingSubmeshes && i % 2 == 1 ? odd : even;
                side.Add(0);
                side.Add(current);
                side.Add(next);
                even.Add(1);
                even.Add(next);
                even.Add(current);
            }

            mesh.SetVertices(vertices);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(even, 0);
            mesh.SetTriangles(odd.Count > 0 ? odd : even, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (isNewAsset)
                AssetDatabase.CreateAsset(mesh, path);
            else
                EditorUtility.SetDirty(mesh);
            return mesh;
        }

        static void CreateMaterialLibrary()
        {
            Materials["grass"] = CreateMaterial("M_HD25D_Grass", "#78945F", 0.18f, 0f);
            Materials["moss"] = CreateMaterial("M_HD25D_Moss", "#5E8254", 0.14f, 0f);
            // Horizon ridges are deliberately desaturated toward the sky colour
            // so aerial perspective, not scale alone, pushes them into the far
            // distance.
            Materials["far_ridge"] = CreateMaterial("M_HD25D_FarRidge", "#8FA9BE", 0.10f, 0f);
            Materials["stone"] = CreateMaterial("M_HD25D_Stone", "#737A79", 0.26f, 0f);
            Materials["stone_light"] = CreateMaterial("M_HD25D_StoneLight", "#929891", 0.28f, 0f);
            Materials["stone_dark"] = CreateMaterial("M_HD25D_StoneDark", "#596268", 0.20f, 0f);
            Materials["plaza"] = CreateMaterial("M_HD25D_Plaza", "#C9C7B9", 0.34f, 0f);
            Materials["wood"] = CreateMaterial("M_HD25D_Wood", "#986A43", 0.26f, 0f);
            Materials["wood_light"] = CreateMaterial("M_HD25D_WoodLight", "#B78357", 0.28f, 0f);
            Materials["wood_dark"] = CreateMaterial("M_HD25D_WoodDark", "#60412D", 0.23f, 0f);
            Materials["gold"] = CreateMaterial("M_HD25D_PaleGold", "#C8A14E", 0.60f, 0.54f);
            Materials["gold_pale"] = CreateMaterial("M_HD25D_RuneGold", "#D7C989", 0.56f, 0.32f);
            Materials["cream"] = CreateMaterial("M_HD25D_Cream", "#E7DDC5", 0.30f, 0f);
            Materials["canvas_blue"] = CreateMaterial("M_HD25D_CanvasBlue", "#365FA2", 0.24f, 0f);
            Materials["canvas_violet"] = CreateMaterial("M_HD25D_CanvasViolet", "#896597", 0.24f, 0f);
            Materials["tent_dark"] = CreateMaterial("M_HD25D_TentDark", "#1E2533", 0.12f, 0f);
            Materials["foliage_dark"] = CreateMaterial("M_HD25D_FoliageDark", "#315843", 0.15f, 0f);
            Materials["foliage_mid"] = CreateMaterial("M_HD25D_FoliageMid", "#66885A", 0.16f, 0f);
            Materials["foliage_light"] = CreateMaterial("M_HD25D_FoliageLight", "#86A66B", 0.16f, 0f);
            Materials["flower_blue"] = CreateMaterial("M_HD25D_FlowerBlue", "#7EB8DE", 0.32f, 0f);
            Materials["flower_pink"] = CreateMaterial("M_HD25D_FlowerPink", "#DDA1B3", 0.32f, 0f);
            Materials["water"] = CreateMaterial("M_HD25D_Water", "#1B7CB8E0", 0.96f, 0.06f, true, "#0F66826A", 0.28f);
            Materials["water_shallow"] = CreateMaterial("M_HD25D_WaterShallow", "#5AD2CEC2", 0.94f, 0.03f, true, "#4ED6D066", 0.44f);
            Materials["waterfall"] = CreateMaterial("M_HD25D_Waterfall", "#6FCBE6D8", 0.82f, 0f, true, "#4CBEE083", 0.72f);
            Materials["foam"] = CreateMaterial("M_HD25D_Foam", "#DDF8F1D8", 0.72f, 0f, true, "#C8F6F0", 0.62f);
            Materials["foam_soft"] = CreateMaterial("M_HD25D_FoamSoft", "#E6FBF660", 0.70f, 0f, true, "#C8F6F0", 0.30f);
            Materials["crystal"] = CreateMaterial("M_HD25D_Crystal", "#51C8E9E8", 0.78f, 0.05f, true, "#27C6F1", 1.20f);
            Materials["lamp_glow"] = CreateMaterial("M_HD25D_LampGlow", "#FFE3A3E8", 0.74f, 0f, true, "#FFD078", 1.80f);
            Materials["charcoal"] = CreateMaterial("M_HD25D_Charcoal", "#2B2725", 0.06f, 0f);
            Materials["campfire_stone"] = CreateMaterial("M_HD25D_CampfireStone", "#6F665D", 0.18f, 0f);
            Materials["ember"] = CreateMaterial("M_HD25D_Ember", "#5A2C20", 0.12f, 0f, false, "#FF6B22", 1.15f);
            Materials["flame"] = CreateMaterial("M_HD25D_Flame", "#FF8A32E6", 0.16f, 0f, true, "#FF641C", 2.60f);

            Materials["nature_asset"] = CreateAssetBackedLitMaterial(
                "M_HD25D_Asset_SimpleNature",
                "Assets/SimpleNaturePack/Materials/SimpleNaturePack_Texture_01.mat");
            Materials["cloud_soft"] = CreateUnlitTextureMaterial(
                "M_HD25D_CloudSoft",
                null,
                "#FFFFFF72",
                true,
                false);
            Materials["cloud_asset"] = CreateUnlitTextureMaterial(
                "M_HD25D_Asset_Cloud",
                "Assets/SimpleSky/Textures/SimpleSky.png",
                "#FFFFFF7A",
                true,
                false);
            Materials["rain_particle"] = CreateUnlitTextureMaterial(
                "M_HD25D_Asset_RainParticle",
                null,
                "#A7D7F5B8",
                true,
                false);
            Materials["mist_particle"] = CreateUnlitTextureMaterial(
                "M_HD25D_Asset_MistParticle",
                "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/cfxr cloud blur.png",
                "#D8EFF12B",
                true,
                false);
            Materials["flame_particle"] = CreateUnlitTextureMaterial(
                "M_HD25D_Asset_FlameParticle",
                "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/cfxr cloud blur.png",
                "#FF8C32D9",
                true,
                true);
            Materials["ember_particle"] = CreateUnlitTextureMaterial(
                "M_HD25D_Asset_EmberParticle",
                "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/cfxr cloud blur.png",
                "#FFC25CE6",
                true,
                true);
        }

        static Material CreateMaterial(
            string assetName,
            string colorHex,
            float smoothness,
            float metallic,
            bool transparent = false,
            string emissionHex = null,
            float emissionStrength = 0f)
        {
            var path = MaterialRoot + "/" + assetName + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
                throw new InvalidOperationException("Neither URP/Lit nor Standard shader is available.");

            if (material == null)
            {
                material = new Material(shader) { name = assetName };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            var color = Html(colorHex);
            SetColor(material, "_BaseColor", "_Color", color);
            SetFloat(material, "_Smoothness", smoothness);
            SetFloat(material, "_Metallic", metallic);
            material.enableInstancing = true;
            material.doubleSidedGI = true;

            if (!string.IsNullOrWhiteSpace(emissionHex) && emissionStrength > 0f)
            {
                var emission = Html(emissionHex) * emissionStrength;
                material.EnableKeyword("_EMISSION");
                SetColor(material, "_EmissionColor", "_EmissionColor", emission);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                SetColor(material, "_EmissionColor", "_EmissionColor", Color.black);
            }

            ConfigureSurface(material, transparent);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material CreateAssetBackedLitMaterial(string assetName, string sourceMaterialPath)
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(sourceMaterialPath);
            Texture texture = null;
            if (source != null)
            {
                if (source.HasProperty("_BaseMap")) texture = source.GetTexture("_BaseMap");
                if (texture == null && source.HasProperty("_MainTex")) texture = source.GetTexture("_MainTex");
                TrackAssetPath(sourceMaterialPath);
            }

            var material = CreateMaterial(assetName, "#FFFFFF", 0.24f, 0f);
            if (texture != null)
            {
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
                if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
                TrackAssetPath(AssetDatabase.GetAssetPath(texture));
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material CreateUnlitTextureMaterial(
            string assetName,
            string texturePath,
            string colorHex,
            bool transparent,
            bool additive)
        {
            var path = MaterialRoot + "/" + assetName + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Transparent");
            if (shader == null)
                throw new InvalidOperationException("No compatible unlit shader is available.");

            if (material == null)
            {
                material = new Material(shader) { name = assetName };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            var texture = string.IsNullOrEmpty(texturePath) ? null : AssetDatabase.LoadAssetAtPath<Texture>(texturePath);
            if (texture != null)
            {
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
                if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
                TrackAssetPath(texturePath);
            }
            else
            {
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", null);
                if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", null);
            }

            var color = Html(colorHex);
            SetColor(material, "_BaseColor", "_Color", color);
            ConfigureSurface(material, transparent);
            if (additive)
            {
                SetFloat(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
                SetFloat(material, "_DstBlend", (float)BlendMode.One);
            }
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        static void ConfigureSurface(Material material, bool transparent)
        {
            if (transparent)
            {
                SetFloat(material, "_Surface", 1f);
                SetFloat(material, "_Blend", 0f);
                SetFloat(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
                SetFloat(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                SetFloat(material, "_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.DisableKeyword("_ALPHATEST_ON");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            else
            {
                SetFloat(material, "_Surface", 0f);
                SetFloat(material, "_SrcBlend", (float)BlendMode.One);
                SetFloat(material, "_DstBlend", (float)BlendMode.Zero);
                SetFloat(material, "_ZWrite", 1f);
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Geometry;
            }
        }

        static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property))
                material.SetFloat(property, value);
        }

        static void SetColor(Material material, string primary, string fallback, Color value)
        {
            if (material.HasProperty(primary))
                material.SetColor(primary, value);
            else if (material.HasProperty(fallback))
                material.SetColor(fallback, value);
        }

        static Color Html(string value)
        {
            Color color;
            if (!ColorUtility.TryParseHtmlString(value, out color))
                throw new InvalidOperationException("Invalid HTML color: " + value);
            return color;
        }

        static string RenderPreview(Camera camera, int width, int height, string fileName)
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var relative = "output/habitat/moonlake-hd25d-v01/previews/" + fileName;
            var absolute = Path.Combine(projectRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(absolute));

            var previous = camera.targetTexture;
            var previousActive = RenderTexture.active;

            // Legacy Camera.Render() bypasses the URP post-processing stack, so
            // the render has to go through a standard render request for depth
            // of field, tonemapping and bloom to reach the PNG. The destination
            // stays sRGB: the post stack already outputs display-referred colour,
            // and ReadPixels does no linear-to-sRGB conversion, so an HDR target
            // would be written out raw and read back far too dark. MSAA is off
            // because SMAA runs inside that same post stack.
            var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                antiAliasing = 1,
                name = "MoonlakePreview_" + width + "x" + height
            };
            texture.Create();

            var request = new RenderPipeline.StandardRequest { destination = texture };
            if (RenderPipeline.SupportsRenderRequest(camera, request))
            {
                RenderPipeline.SubmitRenderRequest(camera, request);
            }
            else
            {
                camera.targetTexture = texture;
                camera.Render();
            }
            RenderTexture.active = texture;

            var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply(false, false);
            File.WriteAllBytes(absolute, image.EncodeToPNG());

            UnityEngine.Object.DestroyImmediate(image);
            camera.targetTexture = previous;
            RenderTexture.active = previousActive;
            texture.Release();
            UnityEngine.Object.DestroyImmediate(texture);
            // Force the render queue to drain before the next preview is issued.
            // Five 1080x1920-class renders back to back inside one editor tick,
            // with no frame boundary to recycle GPU allocations, is what pushed
            // the D3D12 device worker over the edge.
            GL.Flush();
            System.GC.Collect();
            Resources.UnloadUnusedAssets();
            return relative;
        }

        static string[] RenderEnvironmentPreviews(Camera camera, MoonlakeSkyDayWeatherController environment)
        {
            var previews = new List<string>();

            environment.SetTime(10.5f);
            environment.SetWeather(MoonlakeSkyDayWeatherController.WeatherState.Clear);
            WarmupRender(camera);
            previews.Add(RenderPreview(camera, 1080, 1920, "moonlake-hd25d-v01-1080x1920.png"));
            previews.Add(RenderPreview(camera, 390, 844, "moonlake-hd25d-v01-390x844.png"));

            environment.SetTime(16.4f);
            environment.SetWeather(MoonlakeSkyDayWeatherController.WeatherState.PartlyCloudy);
            previews.Add(RenderPreview(camera, 390, 844, "moonlake-hd25d-v01-sunset-cloudy-390x844.png"));

            environment.SetTime(12.2f);
            environment.SetWeather(MoonlakeSkyDayWeatherController.WeatherState.Rain);
            previews.Add(RenderPreview(camera, 390, 844, "moonlake-hd25d-v01-rain-390x844.png"));

            environment.SetTime(21.1f);
            environment.SetWeather(MoonlakeSkyDayWeatherController.WeatherState.Mist);
            previews.Add(RenderPreview(camera, 390, 844, "moonlake-hd25d-v01-night-mist-390x844.png"));

            environment.SetTime(10.5f);
            environment.SetWeather(MoonlakeSkyDayWeatherController.WeatherState.Clear);
            EditorUtility.SetDirty(environment);
            return previews.ToArray();
        }

        static void WarmupRender(Camera camera)
        {
            var previous = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var texture = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32);
            texture.Create();
            camera.targetTexture = texture;
            RenderTexture.active = texture;
            camera.Render();
            camera.targetTexture = previous;
            RenderTexture.active = previousActive;
            texture.Release();
            UnityEngine.Object.DestroyImmediate(texture);
            GL.Flush();
        }

        static void WriteReport(GameObject root, Camera camera, string[] previews)
        {
            var scene = SceneManager.GetActiveScene();
            var report = new BuildReport
            {
                taskPack = "TP-U1-MOONLAKE-HD25D-SCENE-REBUILD",
                scene = ScenePath,
                sceneRoot = SceneRootName,
                sourceMode = "hybrid_asset_backed_procedural_reference_composition",
                proceduralVisuals = visualCount,
                assetBackedVisuals = assetBackedVisualCount,
                assetCrystalClusters = assetCrystalClusterCount,
                renderers = root.GetComponentsInChildren<Renderer>(true).Length,
                colliders = root.GetComponentsInChildren<Collider>(true).Length,
                shadowCasters = shadowCasterCount,
                rootCount = scene.GetRootGameObjects().Length,
                missingScriptReferences = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(root),
                weatherControllerCount = root.GetComponentsInChildren<MoonlakeSkyDayWeatherController>(true).Length,
                campfireControllerCount = root.GetComponentsInChildren<MoonlakeCampfireFx>(true).Length,
                cameraFieldOfView = camera.fieldOfView,
                cameraPosition = camera.transform.position,
                cameraTarget = CameraTarget,
                compositionRevision = "owner-reference-scale-r2",
                plazaWidth = 6.18f,
                bridgeLength = 5.60f,
                cliffTowerHeight = 5.55f,
                cliffCapWidth = 3.65f,
                foregroundApronDepth = 8.45f,
                previews = previews,
                ownerReferenceCompositionApplied = true,
                existingSceneRebuiltInPlace = true,
                mobileSafeZoneApplied = true,
                foregroundOccludersSeparated = true,
                mcpUsed = false,
                runtimeIntegrated = true,
                assetPromoted = false,
                assetStoreModelsIntegrated = assetBackedVisualCount > 0,
                skySystemIntegrated = true,
                solarArcIntegrated = true,
                weatherSystemIntegrated = true,
                campfireIntegrated = true,
                assetPathsUsed = AssetPathsUsed.OrderBy(path => path).ToArray(),
                weatherStates = new[] { "Clear", "PartlyCloudy", "Rain", "Mist" }
            };

            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var outputDir = Path.Combine(projectRoot, "output", "habitat", "moonlake-hd25d-v01");
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(Path.Combine(outputDir, "build-report.json"), JsonUtility.ToJson(report, true));
        }

        /// <summary>
        /// Snapshots the scene before every rebuild. The original one-shot backup
        /// is kept as the untouched pre-HD25D baseline, but a rebuild wipes the
        /// scene, so each run also needs its own timestamped copy — otherwise the
        /// second rebuild silently discards everything the first one produced.
        /// </summary>
        static void BackupExistingSceneBeforeRebuild()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
                return;

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(BackupScenePath) == null)
            {
                if (!AssetDatabase.CopyAsset(ScenePath, BackupScenePath))
                    throw new InvalidOperationException("Failed to preserve the pre-rebuild scene at " + BackupScenePath);
                Debug.Log("[Moonlake HD25D] Preserved pre-HD25D baseline scene: " + BackupScenePath);
            }

            var stamped = string.Format(
                "{0}/Moonlake_HeroZone_v05.before-rebuild-{1}.unity",
                BackupFolder,
                DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            if (!AssetDatabase.CopyAsset(ScenePath, stamped))
                throw new InvalidOperationException("Failed to snapshot the scene at " + stamped);

            AssetDatabase.SaveAssets();
            Debug.Log("[Moonlake HD25D] Snapshotted scene before rebuild: " + stamped);
        }

        static void EnsureFolder(string parent, string child)
        {
            var path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }
    }
}
