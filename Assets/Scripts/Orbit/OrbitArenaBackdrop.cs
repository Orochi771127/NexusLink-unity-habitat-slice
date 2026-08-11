using UnityEngine;

namespace NexusLink.Orbit
{
    /// <summary>
    /// Builds the world the Orbit arena sits in.
    ///
    /// The approved arena reference does not show a field floating in a void: the
    /// platform is set into the same clay-resin habitat as Moonlake, framed by two
    /// waterfall cliffs, open water behind, a small shrine island in the middle
    /// distance and stone lanterns at the near corners. That framing is most of
    /// what makes the arena read as a place rather than a UI screen.
    ///
    /// Built from the same asset vocabulary the habitat uses, so the two scenes
    /// stay recognisably one world. Runs in edit mode for the same reason the
    /// arena does — layout has to be visible while it is being placed.
    ///
    /// SCOPE NOTE: scenery only. It owns no gameplay, and the simulation never
    /// reads anything from it.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class OrbitArenaBackdrop : MonoBehaviour
    {
        const string ViewRootName = "Orbit_Backdrop_View";
        const string NatureRoot = "Assets/InnerverseInteractive/Ultimate Nature – Starter/Environment/";

        [Header("Layout")]
        [Tooltip("Camera the portrait crop is measured against. Falls back to Camera.main.")]
        public Camera framingCamera;

        [Tooltip("Radius of the arena platform this backdrop frames.")]
        public float arenaWorldRadius = 4.5f;

        [Tooltip("Water level, relative to the arena platform.")]
        public float waterDrop = 1.4f;

        [Header("Palette")]
        public Color waterColor = new Color(0.13f, 0.58f, 0.62f, 0.94f);
        public Color stoneColor = new Color(0.70f, 0.72f, 0.75f);
        public Color mossColor = new Color(0.42f, 0.60f, 0.40f);
        public Color foliageColor = new Color(0.44f, 0.62f, 0.42f);
        public Color foliageDeepColor = new Color(0.28f, 0.46f, 0.32f);
        public Color woodColor = new Color(0.42f, 0.31f, 0.24f);
        public Color goldColor = new Color(0.95f, 0.80f, 0.42f);
        public Color waterfallColor = new Color(0.62f, 0.92f, 0.95f, 0.80f);

        bool _rebuildQueued;


        /// <summary>Camera the framing is judged against.</summary>
        Camera FramingCamera()
        {
            return framingCamera != null ? framingCamera : Camera.main;
        }

        /// <summary>
        /// Half-width of the portrait crop on the ground at a given depth. The
        /// same relation <see cref="NexusLink.Tools.PortraitFramingGuide"/> draws;
        /// duplicated here so the builder can size itself without depending on an
        /// authoring component being present in the scene.
        /// </summary>
        static float CropHalfWidthAt(Camera camera, float worldZ, float aspect = 1080f / 1920f)
        {
            if (camera == null) return 12f;
            var origin = camera.transform.position;
            var halfV = camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            var halfH = Mathf.Atan(Mathf.Tan(halfV) * aspect);
            var forward = Mathf.Abs(worldZ - origin.z);
            var height = origin.y;
            return Mathf.Sqrt(forward * forward + height * height) * Mathf.Tan(halfH);
        }

        void OnEnable() { Build(); }

        void OnValidate() { _rebuildQueued = true; }

        void Update()
        {
            if (!_rebuildQueued) return;
            _rebuildQueued = false;
            Build();
        }

        [ContextMenu("Rebuild Backdrop")]
        public void Build()
        {
            var existing = transform.Find(ViewRootName);
            if (existing != null) DestroyView(existing.gameObject);

            var container = new GameObject(ViewRootName);
            container.transform.SetParent(transform, false);
            container.hideFlags = HideFlags.DontSave;
            var root = container.transform;

            var waterY = -waterDrop;

            // Open water. The rippled mesh from the nature pack carries its waves
            // in geometry, so the surface catches the key light instead of
            // reading as one flat colour block the way a plain disc does.
            if (Prefab(root, NatureRoot + "Water/Prefabs/UNS_Water_Detailed.prefab",
                    "Backdrop_Water", new Vector3(0f, waterY - 0.10f, 10f),
                    new Vector3(76f, 0.30f, 76f), Quaternion.identity, waterColor) == null)
            {
                Primitive(root, "Backdrop_Water", PrimitiveType.Cylinder,
                    new Vector3(0f, waterY, 10f), new Vector3(76f, 0.08f, 76f), waterColor);
            }

            BuildFarIslands(root);

            // The platform's own plinth, so it reads as set into the water.
            Primitive(root, "Backdrop_Platform_Plinth", PrimitiveType.Cylinder,
                new Vector3(0f, -waterDrop * 0.5f, 0f),
                new Vector3((arenaWorldRadius + 1.5f) * 2f, waterDrop * 0.5f, (arenaWorldRadius + 1.5f) * 2f),
                stoneColor);
            Primitive(root, "Backdrop_Platform_Apron", PrimitiveType.Cylinder,
                new Vector3(0f, -0.16f, 0f),
                new Vector3((arenaWorldRadius + 2.4f) * 2f, 0.10f, (arenaWorldRadius + 2.4f) * 2f),
                mossColor);

            BuildCliff(root, -1f);
            BuildCliff(root, 1f);
            BuildShrineIsland(root);

            // Stone lanterns flanking the near corners, as in the reference.
            // Placed where the crop is actually wide enough. Near the camera the
            // portrait frame only reaches |x| 4.9, so a lantern beside the near
            // edge of the platform is off screen no matter how good it looks in
            // the scene view; at z=3.2 the frame opens to 6.2 while the platform
            // has narrowed to 4.2, which leaves room for both.
            BuildLantern(root, new Vector3(-(arenaWorldRadius + 0.8f), 0.02f, 3.2f));
            BuildLantern(root, new Vector3(arenaWorldRadius + 0.8f, 0.02f, 3.2f));

            BuildFoliageRing(root);
            BuildForegroundPaving(root);
        }


        /// <summary>
        /// Small landmasses stepping back up the channel. Without them the water
        /// between the arena and the horizon is an empty plane, which is what
        /// makes the middle distance read as a flat colour block.
        /// </summary>
        void BuildFarIslands(Transform root)
        {
            var group = new GameObject("Backdrop_Far_Islands");
            group.transform.SetParent(root, false);

            var islands = new[]
            {
                new Vector4(-13.5f, 26f, 7.5f, 1.5f),
                new Vector4(12.0f, 31f, 6.2f, 1.2f),
                new Vector4(-4.0f, 36f, 5.4f, 1.0f),
                new Vector4(7.5f, 41f, 4.6f, 0.85f)
            };

            for (var i = 0; i < islands.Length; i++)
            {
                var island = islands[i];
                var fade = 1f - i * 0.16f;
                Primitive(group.transform, "Far_Island_Rock_" + i.ToString("00"), PrimitiveType.Cylinder,
                    new Vector3(island.x, -waterDrop + island.w * 0.4f, island.y),
                    new Vector3(island.z, island.w * 0.4f, island.z * 0.72f),
                    Color.Lerp(stoneColor, waterColor, 1f - fade));
                Primitive(group.transform, "Far_Island_Grass_" + i.ToString("00"), PrimitiveType.Cylinder,
                    new Vector3(island.x, -waterDrop + island.w * 0.72f, island.y),
                    new Vector3(island.z * 0.9f, island.w * 0.12f, island.z * 0.64f),
                    Color.Lerp(mossColor, waterColor, 1f - fade));

                for (var t = 0; t < 3; t++)
                {
                    Prefab(group.transform, "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Trees/PT_Pine_Tree_03_green.prefab",
                        "Far_Island_Pine_" + i.ToString("00") + "_" + t,
                        new Vector3(island.x + (t - 1) * island.z * 0.26f, -waterDrop + island.w * 0.78f, island.y),
                        new Vector3(1.1f * fade, 2.1f * fade, 1.1f * fade),
                        Quaternion.Euler(0f, t * 71f, 0f),
                        Color.Lerp(foliageColor, waterColor, 1f - fade));
                }
            }
        }

        /// <summary>A waterfall cliff mass on one side, framing the arena.</summary>
        void BuildCliff(Transform root, float side)
        {
            var group = new GameObject(side < 0f ? "Backdrop_Cliff_Left" : "Backdrop_Cliff_Right");
            group.transform.SetParent(root, false);

            // Framing distance is set by the crop, not by taste: at z=7.5 the portrait
            // frame only reaches |x| 8.2, and the first pass put these at 11.9 where
            // nothing of them was ever on screen.
            var baseX = side * (arenaWorldRadius + 2.3f);
            var baseZ = 8.5f;

            for (var i = 0; i < 3; i++)
            {
                var height = 7.2f - i * 1.1f;
                var width = 6.4f - i * 0.5f;
                var source = (side < 0 ? i + 1 : 5 - i);
                var segment = Prefab(group.transform,
                    NatureRoot + "Rocks/Cliffs/Prefabs/UNS_Rock_Cliff_0" + Mathf.Clamp(source, 1, 5) + ".prefab",
                    "Cliff_Segment_" + i.ToString("00"),
                    new Vector3(baseX + side * i * 0.5f, -waterDrop, baseZ + i * 1.3f),
                    new Vector3(width, height, width * 0.85f),
                    Quaternion.Euler(0f, 37f * i * side, 0f),
                    i % 2 == 0 ? stoneColor : stoneColor * 1.08f);
                if (segment == null)
                {
                    Primitive(group.transform, "Cliff_Segment_" + i.ToString("00"), PrimitiveType.Cube,
                        new Vector3(baseX, -waterDrop + height * 0.5f, baseZ + i * 1.3f),
                        new Vector3(width, height, width * 0.85f), stoneColor);
                }
            }

            // Grass cap and a fall down the inner face.
            Primitive(group.transform, "Cliff_Cap", PrimitiveType.Cylinder,
                new Vector3(baseX, 5.6f, baseZ), new Vector3(6.6f, 0.30f, 5.8f), mossColor);

            var fallX = baseX - side * 2.6f;
            Primitive(group.transform, "Cliff_Waterfall", PrimitiveType.Cube,
                new Vector3(fallX, 2.4f, baseZ - 2.4f), new Vector3(1.25f, 6.6f, 0.16f), waterfallColor);
            Primitive(group.transform, "Cliff_Waterfall_Foam", PrimitiveType.Cylinder,
                new Vector3(fallX, -waterDrop + 0.10f, baseZ - 2.7f),
                new Vector3(2.4f, 0.03f, 1.5f), new Color(0.90f, 0.98f, 0.99f, 0.55f));

            for (var t = 0; t < 3; t++)
            {
                Prefab(group.transform, "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Trees/PT_Pine_Tree_03_green.prefab",
                    "Cliff_Pine_" + t.ToString("00"),
                    new Vector3(baseX + (t - 1) * 1.9f, 5.9f, baseZ + (t % 2) * 1.2f),
                    new Vector3(1.7f, 3.0f, 1.7f), Quaternion.Euler(0f, t * 63f, 0f), foliageColor);
            }
        }

        /// <summary>The small shrine island the reference places up-channel.</summary>
        void BuildShrineIsland(Transform root)
        {
            var group = new GameObject("Backdrop_Shrine_Island");
            group.transform.SetParent(root, false);
            var center = new Vector3(0.5f, -waterDrop, 16.5f);

            Primitive(group.transform, "Island_Rock", PrimitiveType.Cylinder,
                center + new Vector3(0f, 0.45f, 0f), new Vector3(7.0f, 0.45f, 5.4f), stoneColor);
            Primitive(group.transform, "Island_Grass", PrimitiveType.Cylinder,
                center + new Vector3(0f, 0.86f, 0f), new Vector3(6.2f, 0.10f, 4.7f), mossColor);

            // Shrine: a plinth, a body and a flared roof.
            Primitive(group.transform, "Shrine_Base", PrimitiveType.Cube,
                center + new Vector3(0f, 1.10f, 0.2f), new Vector3(1.5f, 0.35f, 1.5f), stoneColor);
            Primitive(group.transform, "Shrine_Body", PrimitiveType.Cube,
                center + new Vector3(0f, 1.70f, 0.2f), new Vector3(1.05f, 0.95f, 1.05f), new Color(0.86f, 0.84f, 0.78f));
            Primitive(group.transform, "Shrine_Roof", PrimitiveType.Cube,
                center + new Vector3(0f, 2.28f, 0.2f), new Vector3(2.0f, 0.22f, 2.0f), new Color(0.36f, 0.44f, 0.58f));
            Primitive(group.transform, "Shrine_Light", PrimitiveType.Sphere,
                center + new Vector3(0f, 1.70f, 0.2f), Vector3.one * 0.5f,
                new Color(1f, 0.88f, 0.62f), new Color(1f, 0.80f, 0.42f) * 2.2f);

            for (var s = 0; s < 4; s++)
            {
                Primitive(group.transform, "Island_Step_" + s.ToString("00"), PrimitiveType.Cube,
                    center + new Vector3(0f, 0.95f - s * 0.22f, -1.5f - s * 0.55f),
                    new Vector3(1.7f - s * 0.1f, 0.16f, 0.5f), stoneColor);
            }

            for (var t = 0; t < 3; t++)
            {
                Prefab(group.transform, "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Trees/PT_Fruit_Tree_01_green.prefab",
                    "Island_Tree_" + t.ToString("00"),
                    center + new Vector3(-2.4f + t * 2.3f, 0.92f, 1.5f),
                    new Vector3(2.1f, 3.2f, 2.1f), Quaternion.Euler(0f, t * 84f, 0f), foliageColor);
            }
        }

        void BuildLantern(Transform root, Vector3 position)
        {
            var group = new GameObject("Backdrop_Lantern");
            group.transform.SetParent(root, false);
            Primitive(group.transform, "Lantern_Base", PrimitiveType.Cylinder,
                position + new Vector3(0f, 0.25f, 0f), new Vector3(1.05f, 0.25f, 1.05f), stoneColor);
            Primitive(group.transform, "Lantern_Shaft", PrimitiveType.Cylinder,
                position + new Vector3(0f, 0.95f, 0f), new Vector3(0.45f, 0.5f, 0.45f), stoneColor);
            Primitive(group.transform, "Lantern_Housing", PrimitiveType.Cube,
                position + new Vector3(0f, 1.65f, 0f), new Vector3(0.95f, 0.75f, 0.95f), new Color(0.84f, 0.82f, 0.76f));
            Primitive(group.transform, "Lantern_Glow", PrimitiveType.Sphere,
                position + new Vector3(0f, 1.65f, 0f), Vector3.one * 0.62f,
                new Color(1f, 0.86f, 0.55f), new Color(1f, 0.78f, 0.38f) * 2.6f);
            Primitive(group.transform, "Lantern_Roof", PrimitiveType.Cube,
                position + new Vector3(0f, 2.14f, 0f), new Vector3(1.45f, 0.20f, 1.45f), new Color(0.36f, 0.44f, 0.58f));
            Primitive(group.transform, "Lantern_Finial", PrimitiveType.Sphere,
                position + new Vector3(0f, 2.34f, 0f), Vector3.one * 0.28f, goldColor);

            var lightGo = new GameObject("Lantern_Light");
            lightGo.transform.SetParent(group.transform, false);
            lightGo.transform.position = position + new Vector3(0f, 1.7f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.84f, 0.58f);
            light.intensity = 2.4f;
            light.range = 9f;
            light.shadows = LightShadows.None;
        }

        /// <summary>Clay-resin planting banked around the platform edge.</summary>
        void BuildFoliageRing(Transform root)
        {
            var group = new GameObject("Backdrop_Foliage");
            group.transform.SetParent(root, false);
            var random = new System.Random(4321);
            const int count = 34;
            for (var i = 0; i < count; i++)
            {
                var angle = i / (float)count * Mathf.PI * 2f;
                var radius = arenaWorldRadius + 1.9f + (float)random.NextDouble() * 0.8f;
                var position = new Vector3(Mathf.Sin(angle) * radius, -0.10f, Mathf.Cos(angle) * radius);
                var scale = 1.1f + (float)random.NextDouble() * 0.9f;

                var bush = Prefab(group.transform, NatureRoot + "Vegetation/Bushes/Prefabs/UNS_Bush.prefab",
                    "Backdrop_Bush_" + i.ToString("00"), position,
                    new Vector3(scale, scale * 0.78f, scale), Quaternion.Euler(0f, i * 47f, 0f),
                    i % 3 == 0 ? foliageDeepColor : foliageColor);

                if (bush == null)
                {
                    Primitive(group.transform, "Backdrop_Bush_" + i.ToString("00"), PrimitiveType.Sphere,
                        position + Vector3.up * scale * 0.3f,
                        new Vector3(scale, scale * 0.7f, scale),
                        i % 3 == 0 ? foliageDeepColor : foliageColor);
                }
            }
        }


        /// <summary>
        /// Cobbled apron along the near edge. The reference closes its foreground
        /// with paving and small flowers rather than letting the water run to the
        /// bottom of the crop.
        /// </summary>
        void BuildForegroundPaving(Transform root)
        {
            var group = new GameObject("Backdrop_Foreground_Paving");
            group.transform.SetParent(root, false);
            var random = new System.Random(9182);

            // A mossy bed, so the gaps between stones read as ground rather than
            // as more stone. The first pass used 2.1-unit slabs in near-white,
            // which turned the bottom quarter of the crop into a grey slab.
            var bedZ = -(arenaWorldRadius + 6.6f);
            Primitive(group.transform, "Paving_Bed", PrimitiveType.Cube,
                new Vector3(0f, -0.24f, bedZ),
                new Vector3(CropHalfWidthAt(FramingCamera(), bedZ) * 2f + 6f, 0.30f, 9.0f),
                mossColor * 0.82f);

            // Cobbles are laid to the crop, not to a fixed count. A flat run of
            // 36 per row spans +/-17 while the frame at that depth is under 6
            // wide, so nine tenths of them were generated only to be culled.
            var camera = FramingCamera();
            for (var row = 0; row < 6; row++)
            {
                var z = -(arenaWorldRadius + 3.0f) - row * 0.86f;
                var halfWidth = CropHalfWidthAt(camera, z) + 1.2f;
                var count = Mathf.Clamp(Mathf.CeilToInt(halfWidth * 2f / 0.98f), 6, 40);
                for (var i = 0; i < count; i++)
                {
                    var stagger = (row % 2) * 0.44f;
                    var x = (i - (count - 1) * 0.5f) * 0.98f + stagger;
                    var jitter = (float)random.NextDouble();
                    var size = 0.62f + jitter * 0.22f;
                    var shade = 0.46f + (float)random.NextDouble() * 0.16f;
                    var cobble = Primitive(group.transform, "Paving_" + row + "_" + i.ToString("00"), PrimitiveType.Cube,
                        new Vector3(x, -0.09f, z + jitter * 0.18f),
                        new Vector3(size, 0.14f, size * 0.80f),
                        new Color(shade, shade + 0.03f, shade + 0.05f));
                    cobble.localRotation = Quaternion.Euler(0f, jitter * 24f - 12f, 0f);

                    if (i % 3 == 2)
                    {
                        Primitive(group.transform, "Paving_Moss_" + row + "_" + i.ToString("00"), PrimitiveType.Sphere,
                            new Vector3(x + 0.5f, -0.04f, z + 0.42f),
                            new Vector3(0.62f, 0.14f, 0.52f), mossColor);
                    }
                    if (i % 9 == 4)
                    {
                        Primitive(group.transform, "Paving_Flower_" + row + "_" + i.ToString("00"), PrimitiveType.Sphere,
                            new Vector3(x - 0.4f, 0.02f, z - 0.35f), Vector3.one * 0.17f,
                            (i + row) % 2 == 0 ? new Color(0.62f, 0.82f, 0.96f) : new Color(0.97f, 0.95f, 0.99f));
                    }
                }
            }
        }

        Transform Primitive(Transform parent, string name, PrimitiveType type,
            Vector3 localPosition, Vector3 scale, Color color, Color? emission = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            Paint(go, color, emission);
            return go.transform;
        }

        /// <summary>
        /// Instantiates a pack prefab and repaints it onto this palette, then
        /// fits it to the requested size. Returns null when the pack is absent so
        /// the caller can fall back to a primitive.
        /// </summary>
        Transform Prefab(Transform parent, string assetPath, string name,
            Vector3 bottomCenter, Vector3 targetSize, Quaternion rotation, Color color)
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null) return null;

            var instance = Instantiate(prefab, parent);
            instance.name = name;
            instance.transform.localRotation = rotation;
            instance.transform.localScale = Vector3.one;

            foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) DestroyView(collider);
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                var materials = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                var painted = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
                painted.SetColor("_BaseColor", color);
                for (var i = 0; i < materials.Length; i++) materials[i] = painted;
                renderer.sharedMaterials = materials;
            }

            FitToBounds(instance, bottomCenter, targetSize, rotation);
            return instance.transform;
#else
            return null;
#endif
        }

        /// <summary>
        /// Scales an instance to the requested world extents. Renderer bounds are
        /// world-axis aligned, so the object is measured unrotated and each local
        /// axis is then routed to the world axis it actually becomes — otherwise a
        /// rotated prefab has its length and width factors swapped.
        /// </summary>
        static void FitToBounds(GameObject root, Vector3 bottomCenter, Vector3 targetSize, Quaternion rotation)
        {
            var transform = root.transform;
            transform.localRotation = Quaternion.identity;
            Bounds local;
            if (!TryGetBounds(root, out local))
            {
                transform.localRotation = rotation;
                transform.localPosition = bottomCenter;
                return;
            }
            transform.localRotation = rotation;

            var size = local.size;
            var matrix = Matrix4x4.Rotate(rotation);
            var scale = Vector3.one;
            for (var axis = 0; axis < 3; axis++)
            {
                var worldAxis = 0;
                var strongest = -1f;
                for (var candidate = 0; candidate < 3; candidate++)
                {
                    var weight = Mathf.Abs(matrix[candidate, axis]);
                    if (weight > strongest) { strongest = weight; worldAxis = candidate; }
                }
                scale[axis] = targetSize[worldAxis] / Mathf.Max(0.001f, size[axis]);
            }
            transform.localScale = scale;

            Bounds fitted;
            if (!TryGetBounds(root, out fitted)) return;
            var offset = new Vector3(
                bottomCenter.x - fitted.center.x,
                bottomCenter.y - fitted.min.y,
                bottomCenter.z - fitted.center.z);
            transform.localPosition += offset;
        }

        static bool TryGetBounds(GameObject root, out Bounds bounds)
        {
            bounds = new Bounds(root.transform.position, Vector3.zero);
            var found = false;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return found;
        }

        static void Paint(GameObject go, Color color, Color? emission)
        {
            var collider = go.GetComponent<Collider>();
            if (collider != null) DestroyView(collider);

            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
            material.SetColor("_BaseColor", color);
            if (color.a < 1f)
            {
                material.SetFloat("_Surface", 1f);
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            if (emission.HasValue)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission.Value);
            }
            go.GetComponent<Renderer>().sharedMaterial = material;
        }

        static void DestroyView(Object target)
        {
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
