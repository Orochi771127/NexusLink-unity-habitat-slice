using UnityEngine;
using UnityEngine.UI;

namespace NexusLink.Orbit
{
    /// <summary>
    /// The two bars the arena reference carries: a stage progress track along the
    /// top with a way back, and a resonance meter along the bottom.
    ///
    /// Built in code as real uGUI rather than drawn with OnGUI, so it can be
    /// restyled later without being rewritten. It is still prototype dressing:
    /// how the whole product's UI is structured is a separate decision, and this
    /// deliberately does not pre-empt it.
    ///
    /// SCOPE NOTE: display only. It reads the simulation and shows what it finds.
    /// It never writes progression, unlock or reward state — which stage is
    /// available is a gameplay decision made elsewhere.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas))]
    public sealed class OrbitStageHud : MonoBehaviour
    {
        const string ViewRootName = "Orbit_Hud_View";

        [Header("Source")]
        public OrbitSessionController session;

        [Header("Zone progress")]
        [Tooltip("Stages in this zone. The shared structure is five per zone.")]
        public int stagesInZone = 5;

        [Tooltip("Which one this scene is, counting from one.")]
        public int currentStage = 2;

        [Header("Palette")]
        public Color panelColor = new Color(0.62f, 0.84f, 0.92f, 0.28f);
        public Color trackColor = new Color(0.86f, 0.95f, 1f, 0.55f);
        public Color nodeColor = new Color(0.80f, 0.92f, 0.98f, 0.85f);
        public Color activeColor = new Color(0.42f, 0.86f, 1f, 1f);
        public Color goalColor = new Color(0.98f, 0.86f, 0.45f, 1f);
        public Color chargeColor = new Color(0.45f, 0.88f, 1f, 0.95f);

        Image _chargeFill;
        bool _rebuildQueued;

        void OnEnable() { Build(); }

        void OnValidate() { _rebuildQueued = true; }

        void Update()
        {
            if (_rebuildQueued)
            {
                _rebuildQueued = false;
                Build();
            }
            SyncCharge();
        }

        void SyncCharge()
        {
            if (_chargeFill == null) return;
            var charge = session != null && session.Simulation != null
                ? Mathf.Clamp01(session.Simulation.ResonanceCharge)
                : 0f;
            _chargeFill.rectTransform.anchorMax = new Vector2(charge, 1f);
        }

        [ContextMenu("Rebuild HUD")]
        public void Build()
        {
            var existing = transform.Find(ViewRootName);
            if (existing != null) DestroyView(existing.gameObject);

            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();

            var root = NewRect(transform, ViewRootName, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            root.gameObject.hideFlags = HideFlags.DontSave;

            BuildProgressBar(root);
            BuildResonanceBar(root);
            SyncCharge();
        }

        void BuildProgressBar(RectTransform root)
        {
            var bar = NewRect(root, "Orbit_Hud_Progress",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(48f, -190f), new Vector2(-48f, -70f));
            Panel(bar, panelColor);

            // Way back to the habitat.
            var back = NewRect(bar, "Back", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(22f, -34f), new Vector2(90f, 34f));
            Panel(back, new Vector2(0f, 0f) == Vector2.zero ? new Color(1f, 1f, 1f, 0.16f) : Color.clear);
            var chevron = NewRect(back, "Chevron", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-14f, -3f), new Vector2(4f, 3f));
            Panel(chevron, trackColor);
            chevron.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var chevron2 = NewRect(back, "Chevron2", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-14f, -3f), new Vector2(4f, 3f));
            Panel(chevron2, trackColor);
            chevron2.localRotation = Quaternion.Euler(0f, 0f, -45f);

            // The track itself, with one node per stage in the zone.
            var track = NewRect(bar, "Track", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(130f, -3f), new Vector2(-70f, 3f));
            Panel(track, trackColor);

            var count = Mathf.Max(2, stagesInZone);
            for (var i = 0; i < count; i++)
            {
                var t = i / (float)(count - 1);
                var last = i == count - 1;
                var size = last ? 26f : 16f;
                var node = NewRect(track, "Node_" + i.ToString("00"),
                    new Vector2(t, 0.5f), new Vector2(t, 0.5f),
                    new Vector2(-size, -size), new Vector2(size, size));
                Panel(node, last ? goalColor : (i < currentStage ? activeColor : nodeColor));
                if (last)
                {
                    var inner = NewRect(node, "Inner", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                        new Vector2(-size + 7f, -size + 7f), new Vector2(size - 7f, size - 7f));
                    Panel(inner, panelColor);
                }
            }
        }

        void BuildResonanceBar(RectTransform root)
        {
            var bar = NewRect(root, "Orbit_Hud_Resonance",
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(48f, 70f), new Vector2(-48f, 190f));
            Panel(bar, panelColor);

            var badge = NewRect(bar, "Lotus", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(26f, -34f), new Vector2(94f, 34f));
            Panel(badge, new Color(1f, 1f, 1f, 0.16f));
            for (var petal = 0; petal < 3; petal++)
            {
                var size = 26f - petal * 6f;
                var lotus = NewRect(badge, "Petal_" + petal, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(-size, -size), new Vector2(size, size));
                Panel(lotus, petal % 2 == 0 ? chargeColor : panelColor);
                lotus.localRotation = Quaternion.Euler(0f, 0f, 45f * petal);
            }

            var groove = NewRect(bar, "Charge_Groove", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(132f, -9f), new Vector2(-34f, 9f));
            Panel(groove, new Color(1f, 1f, 1f, 0.14f));

            var fill = NewRect(groove, "Charge_Fill", Vector2.zero, new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            _chargeFill = Panel(fill, chargeColor);
            fill.anchorMax = new Vector2(0f, 1f);
        }

        static RectTransform NewRect(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }

        static Image Panel(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static void DestroyView(Object target)
        {
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
