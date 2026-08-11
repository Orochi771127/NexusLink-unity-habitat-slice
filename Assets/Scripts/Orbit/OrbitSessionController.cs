using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NexusLink.Orbit
{
    /// <summary>
    /// Drives one Heart-Core Orbit session and draws it.
    ///
    /// The simulation itself is deterministic and lives in <see cref="OrbitSimulation"/>.
    /// This component only feeds it a fixed-step schedule, turns a drag into a
    /// launch, and moves transforms to match. Keeping the accumulator here is what
    /// lets the same input replay identically regardless of frame rate.
    ///
    /// It runs in edit mode as well, because stage layout is data on the stage
    /// asset: without a preview, mote and pillar placement could only be judged by
    /// entering play mode and guessing.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class OrbitSessionController : MonoBehaviour
    {
        const string ViewRootName = "Orbit_View";

        [Header("Stage")]
        public OrbitStageDefinition stage;

        [Header("Layout")]
        [Tooltip("World radius the stage's unit circle maps onto.")]
        public float worldRadius = 4.5f;

        public float fieldHeight = 0.15f;

        /// <summary>Top face of the water disc; every decal has to clear it.</summary>
        float WaterTop { get { return fieldHeight + 0.02f; } }

        [Header("Aiming")]
        [Tooltip("Screen pixels of drag that count as a full-power pull.")]
        public float fullPullPixels = 260f;

        [Tooltip("Stance bias applied to spin, -1 defensive through 1 aggressive.")]
        [Range(-1f, 1f)] public float stanceSpin = 0.35f;

        OrbitSimulation _simulation;
        float _accumulator;

        Transform _avatar;
        Transform _anchorRing;
        Transform _zoneRing;
        LineRenderer _aimLine;
        readonly List<Transform> _moteViews = new List<Transform>();
        readonly List<Transform> _pillarViews = new List<Transform>();

        bool _dragging;
        Vector2 _dragStart;
        bool _rebuildQueued;

        public OrbitSimulation Simulation { get { return _simulation; } }

        void OnEnable()
        {
            if (stage == null) return;
            if (Application.isPlaying) _simulation = new OrbitSimulation(stage);
            BuildView();
        }

        void OnValidate()
        {
            // Rebuilding from inside OnValidate destroys objects mid-validation,
            // which Unity refuses. Queue it for the next tick.
            _rebuildQueued = true;
        }

        void Update()
        {
            if (stage == null) return;

            if (_rebuildQueued)
            {
                _rebuildQueued = false;
                if (Application.isPlaying && _simulation == null) _simulation = new OrbitSimulation(stage);
                BuildView();
            }

            if (!Application.isPlaying)
            {
                SyncView();
                return;
            }

            if (_simulation == null) _simulation = new OrbitSimulation(stage);

            HandleInput();

            // Fixed-step: the simulation never sees a variable delta.
            _accumulator += Time.deltaTime;
            var guard = 0;
            while (_accumulator >= OrbitSimulation.FixedStep && guard++ < 8)
            {
                _simulation.Step();
                _accumulator -= OrbitSimulation.FixedStep;
            }

            SyncView();
        }

        void HandleInput()
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;

            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            {
                _simulation.Reset();
                _accumulator = 0f;
                RefreshMoteVisibility();
                return;
            }

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                _simulation.Withdraw();

            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame && _simulation.TryPulse())
            {
                Debug.LogFormat("[Orbit] Boundary resonance released. Charge spent total {0:F2}.",
                    _simulation.ChargeSpentTotal);
            }

            if (mouse == null || _simulation.CurrentPhase != OrbitSimulation.Phase.Aiming) return;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                _dragging = true;
                _dragStart = mouse.position.ReadValue();
            }
            else if (_dragging && mouse.leftButton.wasReleasedThisFrame)
            {
                _dragging = false;
                CommitLaunch(mouse.position.ReadValue());
            }
        }

        /// <summary>
        /// Pull back to aim, as with a slingshot: the shot goes opposite the drag,
        /// and the drag length sets the power band.
        /// </summary>
        void CommitLaunch(Vector2 dragEnd)
        {
            var drag = _dragStart - dragEnd;
            if (drag.sqrMagnitude < 4f) return;

            var pull = Mathf.Clamp01(drag.magnitude / Mathf.Max(1f, fullPullPixels));
            var direction = AimDirection(drag);
            _simulation.Launch(direction, pull, stanceSpin);
            Debug.LogFormat("[Orbit] Launched. pull={0:P0} stance={1:F2}", pull, stanceSpin);
        }

        /// <summary>Screen drag mapped into the arena plane, relative to the viewer.</summary>
        Vector2 AimDirection(Vector2 drag)
        {
            var camera = Camera.main;
            var flat = drag.normalized;
            if (camera == null) return flat;

            var forward = camera.transform.forward;
            var right = camera.transform.right;
            forward.y = 0f; right.y = 0f;
            forward.Normalize(); right.Normalize();
            var world = right * flat.x + forward * flat.y;
            return new Vector2(world.x, world.z).normalized;
        }

        /// <summary>
        /// Rebuilds the field under a single container, styled after the approved
        /// arena reference: a water disc set into a stone rim with gold inlays,
        /// concentric guide rings, glowing memory nodes and a dashed anchor.
        /// </summary>
        void BuildView()
        {
            var existing = transform.Find(ViewRootName);
            if (existing != null) DestroyView(existing.gameObject);

            _moteViews.Clear();
            _pillarViews.Clear();

            var container = new GameObject(ViewRootName);
            container.transform.SetParent(transform, false);
            container.hideFlags = HideFlags.DontSave;
            var root = container.transform;

            var fieldRadius = stage.arenaRadius * worldRadius;

            // The rims are solid discs, so they have to sit strictly below the
            // water or they simply cover it: a cylinder's localScale.y is a
            // half-height, and the first pass put the inner rim's top face at
            // 0.21 against the water's 0.19, hiding the field entirely.
            CreatePrimitiveAt(root, "Orbit_Rim_Outer", PrimitiveType.Cylinder,
                new Vector3(0f, fieldHeight - 0.12f, 0f),
                new Vector3((fieldRadius + 0.62f) * 2f, 0.07f, (fieldRadius + 0.62f) * 2f),
                new Color(0.72f, 0.74f, 0.77f));

            CreatePrimitiveAt(root, "Orbit_Rim_Inner", PrimitiveType.Cylinder,
                new Vector3(0f, fieldHeight - 0.07f, 0f),
                new Vector3((fieldRadius + 0.30f) * 2f, 0.05f, (fieldRadius + 0.30f) * 2f),
                new Color(0.88f, 0.86f, 0.78f));

            CreatePrimitiveAt(root, "Orbit_Water", PrimitiveType.Cylinder,
                new Vector3(0f, fieldHeight, 0f),
                new Vector3(fieldRadius * 2f, 0.04f, fieldRadius * 2f),
                new Color(0.10f, 0.62f, 0.62f, 0.90f));

            for (var i = 0; i < 8; i++)
            {
                var angle = i * Mathf.PI * 0.25f;
                var gem = CreatePrimitiveAt(root, "Orbit_Rim_Gem_" + i.ToString("00"), PrimitiveType.Cube,
                    new Vector3(Mathf.Sin(angle) * (fieldRadius + 0.46f), fieldHeight + 0.03f,
                                Mathf.Cos(angle) * (fieldRadius + 0.46f)),
                    new Vector3(0.34f, 0.09f, 0.34f),
                    new Color(0.96f, 0.82f, 0.42f), new Color(0.60f, 0.45f, 0.14f));
                gem.localRotation = Quaternion.Euler(0f, i * 45f + 45f, 0f);
            }

            DrawRing(root, "Orbit_Guide_Outer", fieldRadius * 0.94f, new Color(0.95f, 0.86f, 0.55f, 0.80f), 0.035f);
            DrawRing(root, "Orbit_Guide_Mid", fieldRadius * 0.62f, new Color(0.88f, 0.92f, 0.96f, 0.42f), 0.025f);
            DrawRing(root, "Orbit_Guide_Inner", fieldRadius * 0.30f, new Color(0.88f, 0.92f, 0.96f, 0.32f), 0.02f);

            if (stage.HasResonanceZone)
            {
                _zoneRing = CreatePrimitiveAt(root, "Orbit_ResonanceZone", PrimitiveType.Cylinder,
                    ToLocal(stage.resonanceZone.Center, 0.026f),
                    new Vector3(stage.resonanceZone.r * worldRadius * 2f, 0.02f, stage.resonanceZone.r * worldRadius * 2f),
                    new Color(0.30f, 0.80f, 0.95f, 0.34f));
            }

            if (stage.HasAnchor)
            {
                _anchorRing = DrawDashedRing(root, "Orbit_Anchor",
                    ToLocal(stage.anchor.Center, 0.034f), stage.anchor.r * worldRadius,
                    new Color(0.98f, 0.86f, 0.45f), 20);
            }

            if (stage.pillars != null)
            {
                for (var i = 0; i < stage.pillars.Length; i++)
                {
                    _pillarViews.Add(CreatePrimitiveAt(root, "Orbit_Pillar_" + i.ToString("00"), PrimitiveType.Cylinder,
                        ToLocal(stage.pillars[i].Center, 0.42f),
                        new Vector3(stage.pillars[i].r * worldRadius * 2f, 0.44f, stage.pillars[i].r * worldRadius * 2f),
                        new Color(0.70f, 0.73f, 0.78f)));
                }
            }

            if (stage.memoryMotes != null)
            {
                for (var i = 0; i < stage.memoryMotes.Length; i++)
                {
                    var moteRadius = Mathf.Max(0.05f, stage.memoryMotes[i].r);
                    _moteViews.Add(CreatePrimitiveAt(root, "Orbit_Mote_" + i.ToString("00"), PrimitiveType.Sphere,
                        ToLocal(stage.memoryMotes[i].Center, 0.16f),
                        Vector3.one * moteRadius * worldRadius * 1.5f,
                        new Color(0.42f, 0.86f, 1f), new Color(0.30f, 0.78f, 1f) * 2.4f));

                    DrawRing(root, "Orbit_Mote_Halo_" + i.ToString("00"),
                        moteRadius * worldRadius * 1.8f, new Color(0.45f, 0.88f, 1f, 0.55f), 0.02f,
                        ToLocal(stage.memoryMotes[i].Center, 0.026f));
                }
            }

            _avatar = CreatePrimitiveAt(root, "Orbit_Avatar", PrimitiveType.Sphere,
                ToLocal(stage.playerStart, 0.16f), Vector3.one * 0.055f * worldRadius * 2f,
                new Color(0.58f, 0.91f, 1f), new Color(0.35f, 0.80f, 1f) * 3.2f);

            DrawRing(root, "Orbit_Avatar_Halo", 0.055f * worldRadius * 2.4f,
                new Color(0.55f, 0.90f, 1f, 0.7f), 0.03f, ToLocal(stage.playerStart, 0.026f));

            var aimGo = new GameObject("Orbit_AimLine");
            aimGo.transform.SetParent(root, false);
            _aimLine = aimGo.AddComponent<LineRenderer>();
            _aimLine.useWorldSpace = true;
            _aimLine.positionCount = 2;
            _aimLine.widthMultiplier = 0.07f;
            _aimLine.material = UnlitMaterial(new Color(1f, 0.9f, 0.6f, 0.85f));
            _aimLine.enabled = false;
        }

        Transform DrawRing(Transform parent, string name, float radius, Color color, float width, Vector3? center = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center ?? new Vector3(0f, WaterTop + 0.02f, 0f);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.widthMultiplier = width;
            line.material = UnlitMaterial(color);
            const int segments = 72;
            line.positionCount = segments;
            for (var i = 0; i < segments; i++)
            {
                var a = i / (float)segments * Mathf.PI * 2f;
                line.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
            }
            return go.transform;
        }

        Transform DrawDashedRing(Transform parent, string name, Vector3 center, float radius, Color color, int dashes)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            for (var d = 0; d < dashes; d++)
            {
                var a = d / (float)dashes * Mathf.PI * 2f;
                var dash = CreatePrimitiveAt(go.transform, name + "_Dash_" + d.ToString("00"), PrimitiveType.Cube,
                    new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius),
                    new Vector3(0.05f, 0.02f, radius * 0.34f),
                    color, color * 1.4f);
                dash.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
            }
            return go.transform;
        }

        Transform CreatePrimitiveAt(Transform parent, string name, PrimitiveType type,
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

        static Material UnlitMaterial(Color color)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = color };
            if (color.a < 1f) MakeTransparent(material);
            return material;
        }

        /// <summary>
        /// URP does not go transparent just because _Surface is set to 1. The
        /// blend factors, depth write, keyword and render queue all have to move
        /// together, and skipping them leaves the material rendering opaque.
        /// </summary>
        static void MakeTransparent(Material material)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        static void Paint(GameObject go, Color color, Color? emission)
        {
            var collider = go.GetComponent<Collider>();
            if (collider != null) DestroyView(collider);

            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
            material.SetColor("_BaseColor", color);
            if (color.a < 1f) MakeTransparent(material);
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

        void SyncView()
        {
            var position = _simulation != null ? _simulation.Position : stage.playerStart;
            if (_avatar != null) _avatar.localPosition = ToLocal(position, 0.16f);

            RefreshMoteVisibility();

            var aiming = _simulation != null
                         && _simulation.CurrentPhase == OrbitSimulation.Phase.Aiming
                         && _dragging;
            if (_aimLine == null) return;
            _aimLine.enabled = aiming;
            if (!aiming || _avatar == null) return;

            var mouse = Mouse.current;
            var drag = mouse != null ? _dragStart - mouse.position.ReadValue() : Vector2.zero;
            var pull = Mathf.Clamp01(drag.magnitude / Mathf.Max(1f, fullPullPixels));
            var direction = drag.sqrMagnitude > 1f ? AimDirection(drag) : Vector2.up;
            var world = new Vector3(direction.x, 0f, direction.y);
            _aimLine.SetPosition(0, _avatar.position);
            _aimLine.SetPosition(1, _avatar.position + world * (1.2f + pull * 2.4f));
        }

        void RefreshMoteVisibility()
        {
            for (var i = 0; i < _moteViews.Count; i++)
            {
                if (_moteViews[i] == null) continue;
                var collected = _simulation != null && _simulation.IsMoteCollected(i);
                if (_moteViews[i].gameObject.activeSelf == !collected) continue;
                _moteViews[i].gameObject.SetActive(!collected);
            }
        }

        Vector3 ToLocal(Vector2 arenaPoint, float height)
        {
            return new Vector3(arenaPoint.x * worldRadius, fieldHeight + height, arenaPoint.y * worldRadius);
        }

        void OnGUI()
        {
            if (!Application.isPlaying || _simulation == null || stage == null) return;

            var style = new GUIStyle(GUI.skin.label) { fontSize = 15 };
            var box = new Rect(14f, 14f, 470f, 190f);
            GUI.Box(box, GUIContent.none);
            GUILayout.BeginArea(new Rect(box.x + 10f, box.y + 8f, box.width - 20f, box.height - 16f));
            GUILayout.Label(stage.title + " — " + stage.goalLabel, style);
            GUILayout.Label(string.Format("phase {0}   t {1:F1}/{2:F0}s",
                _simulation.CurrentPhase, _simulation.Elapsed, stage.maxSeconds), style);
            GUILayout.Label(string.Format("objective {0}/{1}   motes {2}/{3}",
                _simulation.ObjectiveIndex, stage.objectives.Length,
                _simulation.MotesCollected, stage.memoryMotes != null ? stage.memoryMotes.Length : 0), style);
            GUILayout.Label(string.Format("resonance charge {0:F2}   pulses {1}   earned {2:F2} / spent {3:F2}",
                _simulation.ResonanceCharge, _simulation.PulsesRemaining,
                _simulation.ChargeEarnedTotal, _simulation.ChargeSpentTotal), style);
            if (_simulation.CurrentPhase == OrbitSimulation.Phase.Settled)
                GUILayout.Label("outcome: " + _simulation.Result + " (" + _simulation.ResultSubtype + ")", style);
            GUILayout.Label("drag back and release · Space pulse · R restart · Esc withdraw", style);
            GUILayout.EndArea();
        }
    }
}
