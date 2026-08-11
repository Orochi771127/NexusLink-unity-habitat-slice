using System;
using UnityEngine;

namespace NexusLink.Orbit
{
    /// <summary>
    /// One Heart-Core Orbit stage.
    ///
    /// The field names and the normalized unit-circle coordinate space are taken
    /// from the shared stage schema rather than invented here, so the twenty-five
    /// authored Moonlake stages port across as data instead of being re-designed.
    /// Positions run roughly -1..1 and are scaled by <see cref="arenaRadius"/>.
    ///
    /// Difficulty comes from field layout, flow and objective combination. There
    /// is deliberately no difficulty tier and no power multiplier.
    /// </summary>
    [CreateAssetMenu(menuName = "NexusLink/Orbit Stage", fileName = "OrbitStage")]
    public sealed class OrbitStageDefinition : ScriptableObject
    {
        public enum ObjectiveType
        {
            /// <summary>Reach the stage anchor.</summary>
            ReachAnchor = 0,
            /// <summary>Recover every misplaced memory mote.</summary>
            CollectMotes = 1,
            /// <summary>Hold inside a resonance zone under a speed cap.</summary>
            ResonateZone = 2,
            /// <summary>Stay in the field for a duration.</summary>
            Survive = 3,
            /// <summary>Settle an intrusive noise knot by repeated contact.</summary>
            ClearNoise = 4
        }

        [Serializable]
        public struct Circle
        {
            public float x;
            public float y;
            public float r;

            public Vector2 Center { get { return new Vector2(x, y); } }
        }

        [Serializable]
        public struct Objective
        {
            public ObjectiveType type;

            [Tooltip("Survive: seconds. ClearNoise: required contacts. Others: unused.")]
            public float amount;
        }

        [Serializable]
        public struct ResonanceZone
        {
            public float x;
            public float y;
            public float r;

            [Tooltip("The avatar only counts as settled below this speed.")]
            public float maxSpeed;

            public float holdSeconds;

            [Tooltip("Deceleration applied inside the zone.")]
            public float brake;

            public Vector2 Center { get { return new Vector2(x, y); } }
        }

        [Serializable]
        public struct DriftField
        {
            public float x;
            public float y;
            public float radius;

            [Tooltip("Constant push inside the radius, in arena units per second squared.")]
            public Vector2 acceleration;
        }

        [Header("Identity")]
        public string stageId = "moonlake-1";
        public string regionId = "moonlake";
        public string zoneId = "starwood_trail";
        public int index = 1;

        [Header("Player-facing copy")]
        public string title = "林口初旋";
        public string goalLabel = "沿開放軌道抵達星林錨點";
        [TextArea] public string copy;
        [TextArea] public string clearNarrative;

        [Header("Field")]
        public float arenaRadius = 1f;

        [Tooltip("A contained arena keeps the avatar inside; an open one lets it leave.")]
        public bool containedArena = true;

        public Vector2 playerStart = new Vector2(0f, 0.58f);

        public Circle[] pillars = new Circle[0];
        public Circle[] memoryMotes = new Circle[0];

        [Tooltip("Zero radius means this stage has no anchor.")]
        public Circle anchor;

        public ResonanceZone resonanceZone;
        public DriftField[] driftFields = new DriftField[0];

        [Header("Session")]
        [Tooltip("The contract's target band is roughly 35-75 seconds.")]
        public float maxSeconds = 60f;

        [Tooltip("Resonance pulses this stage authorises. Comes from the stage, " +
                 "never from bond, payment or grinding.")]
        public int resonancePulses = 1;

        public Objective[] objectives = new Objective[]
        {
            new Objective { type = ObjectiveType.ReachAnchor }
        };

        [Header("Physics tuning (per stage)")]
        [Tooltip("Velocity retained per second of flight.")]
        public float linearDamping = 0.62f;

        [Tooltip("Spin retained per second.")]
        public float spinDamping = 0.55f;

        [Tooltip("Speed kept after a boundary or pillar bounce. Must stay below 1: " +
                 "a wall may redirect the avatar but must never add energy.")]
        [Range(0.1f, 0.99f)] public float restitution = 0.86f;

        public bool HasAnchor { get { return anchor.r > 0.0001f; } }
        public bool HasResonanceZone { get { return resonanceZone.r > 0.0001f; } }
    }
}
