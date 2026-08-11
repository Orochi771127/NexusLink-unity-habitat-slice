using UnityEngine;

namespace NexusLink.Orbit
{
    /// <summary>
    /// The deterministic core of a Heart-Core Orbit session.
    ///
    /// Plain C# on purpose. The contract requires that the same session snapshot,
    /// the same input and the same stage seed replay identically on a fixed-step
    /// schedule, so nothing here may read <c>Time.deltaTime</c>, <c>Random</c>, or
    /// any scene state. It advances only when <see cref="Step"/> is called, always
    /// by <see cref="FixedStep"/>.
    ///
    /// It also refuses to be a combat system. There is no health, no damage and no
    /// defeat: objectives are recovering misplaced memory, reaching an anchor,
    /// settling inside a resonance zone, holding the field, or quieting an
    /// intrusive noise knot. Every ending is one of four safe outcomes.
    /// </summary>
    public sealed class OrbitSimulation
    {
        /// <summary>120 Hz. Collision against small motes needs the resolution.</summary>
        public const float FixedStep = 1f / 120f;

        public enum Phase { Aiming = 0, Flight = 1, Settled = 2 }

        /// <summary>The four safe outcome families. None of them ranks above another.</summary>
        public enum Outcome { None = 0, Stabilized, Recovered, Retreated, OverwhelmedButSafe }

        readonly OrbitStageDefinition _stage;
        readonly bool[] _moteCollected;

        public Phase CurrentPhase { get; private set; }
        public Outcome Result { get; private set; }
        public string ResultSubtype { get; private set; }

        public Vector2 Position { get; private set; }
        public Vector2 Velocity { get; private set; }
        public float Spin { get; private set; }
        public float Elapsed { get; private set; }

        public int ObjectiveIndex { get; private set; }
        public int MotesCollected { get; private set; }
        public int NoiseContacts { get; private set; }
        public float ZoneHoldSeconds { get; private set; }

        /// <summary>
        /// Session-only charge banked from boundary impacts. A pulse spends it;
        /// walls never mint it out of nothing.
        /// </summary>
        public float ResonanceCharge { get; private set; }
        public int PulsesRemaining { get; private set; }
        public float ChargeEarnedTotal { get; private set; }
        public float ChargeSpentTotal { get; private set; }

        public OrbitStageDefinition Stage { get { return _stage; } }
        public bool IsMoteCollected(int index) { return _moteCollected[index]; }

        const float AvatarRadius = 0.055f;
        const float MinLaunchSpeed = 0.55f;
        const float MaxLaunchSpeed = 2.15f;
        const float SpeedCeiling = 2.6f;
        const float ChargePerImpact = 0.42f;
        const float MaxCharge = 1f;
        const float ChargeToSpeed = 0.9f;
        const float SettleSpeed = 0.06f;

        public OrbitSimulation(OrbitStageDefinition stage)
        {
            _stage = stage;
            _moteCollected = new bool[stage.memoryMotes != null ? stage.memoryMotes.Length : 0];
            Reset();
        }

        public void Reset()
        {
            Position = _stage.playerStart;
            Velocity = Vector2.zero;
            Spin = 0f;
            Elapsed = 0f;
            ObjectiveIndex = 0;
            MotesCollected = 0;
            NoiseContacts = 0;
            ZoneHoldSeconds = 0f;
            ResonanceCharge = 0f;
            ChargeEarnedTotal = 0f;
            ChargeSpentTotal = 0f;
            PulsesRemaining = Mathf.Max(0, _stage.resonancePulses);
            for (var i = 0; i < _moteCollected.Length; i++) _moteCollected[i] = false;
            CurrentPhase = Phase.Aiming;
            Result = Outcome.None;
            ResultSubtype = null;
        }

        /// <summary>
        /// Commits the agreed launch. Pull distance sets the speed band, the aim
        /// sets the heading, and the stance biases spin. Nothing is nudged after
        /// this point: no trust, mood or spam score may bend the shot the preview
        /// already promised.
        /// </summary>
        public void Launch(Vector2 direction, float pull01, float stanceSpin)
        {
            if (CurrentPhase != Phase.Aiming) return;
            if (direction.sqrMagnitude < 1e-6f) return;

            var speed = Mathf.Lerp(MinLaunchSpeed, MaxLaunchSpeed, Mathf.Clamp01(pull01));
            Velocity = direction.normalized * speed;
            Spin = Mathf.Clamp(stanceSpin, -1f, 1f) * speed;
            CurrentPhase = Phase.Flight;
        }

        /// <summary>
        /// Boundary Resonance. Converts banked charge into forward speed and spin
        /// recovery, debited from the ledger and clamped by the speed ceiling.
        /// Returns false when there is nothing authorised or nothing banked.
        /// </summary>
        public bool TryPulse()
        {
            if (CurrentPhase != Phase.Flight) return false;
            if (PulsesRemaining <= 0) return false;
            if (ResonanceCharge <= 0.02f) return false;

            var spent = ResonanceCharge;
            ResonanceCharge = 0f;
            ChargeSpentTotal += spent;
            PulsesRemaining--;

            var heading = Velocity.sqrMagnitude > 1e-6f ? Velocity.normalized : Vector2.up;
            var speed = Mathf.Min(SpeedCeiling, Velocity.magnitude + spent * ChargeToSpeed);
            Velocity = heading * speed;
            Spin = Mathf.Clamp(Spin + spent * 0.6f * Mathf.Sign(Spin == 0f ? 1f : Spin), -SpeedCeiling, SpeedCeiling);
            return true;
        }

        /// <summary>The player choosing to leave. Never a failure, never punished.</summary>
        public void Withdraw()
        {
            if (CurrentPhase == Phase.Settled) return;
            Finish(Outcome.Retreated, "player_withdrew");
        }

        public void Step()
        {
            if (CurrentPhase != Phase.Flight) return;

            Elapsed += FixedStep;

            ApplyFields();
            Integrate();
            ResolvePillars();
            ResolveBoundary();
            CollectMotes();
            TrackResonanceZone();
            EvaluateObjectives();

            if (CurrentPhase != Phase.Flight) return;

            if (Elapsed >= _stage.maxSeconds)
            {
                // Out of time is not a loss. The avatar disperses safely.
                Finish(Outcome.OverwhelmedButSafe, "resonance_interrupted");
                return;
            }

            if (Velocity.magnitude < SettleSpeed && !HasTimedObjective())
            {
                Finish(Outcome.OverwhelmedButSafe, "avatar_disperse");
            }
        }

        void ApplyFields()
        {
            if (_stage.driftFields != null)
            {
                for (var i = 0; i < _stage.driftFields.Length; i++)
                {
                    var field = _stage.driftFields[i];
                    var delta = Position - new Vector2(field.x, field.y);
                    if (delta.sqrMagnitude <= field.radius * field.radius)
                        Velocity += field.acceleration * FixedStep;
                }
            }

            if (_stage.HasResonanceZone)
            {
                var zone = _stage.resonanceZone;
                var delta = Position - zone.Center;
                if (delta.sqrMagnitude <= zone.r * zone.r && zone.brake > 0f)
                {
                    var speed = Mathf.Max(0f, Velocity.magnitude - zone.brake * FixedStep * 0.1f);
                    Velocity = Velocity.sqrMagnitude > 1e-6f ? Velocity.normalized * speed : Vector2.zero;
                }
            }
        }

        void Integrate()
        {
            Position += Velocity * FixedStep;
            Velocity *= Mathf.Pow(_stage.linearDamping, FixedStep);
            Spin *= Mathf.Pow(_stage.spinDamping, FixedStep);
        }

        void ResolvePillars()
        {
            if (_stage.pillars == null) return;
            for (var i = 0; i < _stage.pillars.Length; i++)
            {
                var pillar = _stage.pillars[i];
                var delta = Position - pillar.Center;
                var minDistance = pillar.r + AvatarRadius;
                if (delta.sqrMagnitude >= minDistance * minDistance) continue;

                var normal = delta.sqrMagnitude > 1e-8f ? delta.normalized : Vector2.up;
                Position = pillar.Center + normal * minDistance;
                Bounce(normal);
            }
        }

        void ResolveBoundary()
        {
            var limit = _stage.arenaRadius - AvatarRadius;
            if (Position.sqrMagnitude <= limit * limit) return;

            if (!_stage.containedArena)
            {
                // An open field lets the avatar leave; it is recalled, not lost.
                Finish(Outcome.OverwhelmedButSafe, "avatar_disperse");
                return;
            }

            var normal = -Position.normalized;
            Position = -normal * limit;
            Bounce(normal);
        }

        /// <summary>
        /// Reflect and lose energy. Restitution is below one by contract, and the
        /// only thing an impact adds is resonance charge, which the ledger tracks.
        /// </summary>
        void Bounce(Vector2 normal)
        {
            var impactSpeed = Mathf.Abs(Vector2.Dot(Velocity, normal));
            Velocity = Vector2.Reflect(Velocity, normal) * _stage.restitution;

            var gained = Mathf.Min(ChargePerImpact * impactSpeed, MaxCharge - ResonanceCharge);
            if (gained > 0f)
            {
                ResonanceCharge += gained;
                ChargeEarnedTotal += gained;
            }
        }

        void CollectMotes()
        {
            if (_stage.memoryMotes == null) return;
            for (var i = 0; i < _stage.memoryMotes.Length; i++)
            {
                if (_moteCollected[i]) continue;
                var mote = _stage.memoryMotes[i];
                var delta = Position - mote.Center;
                var reach = mote.r + AvatarRadius;
                if (delta.sqrMagnitude > reach * reach) continue;
                _moteCollected[i] = true;
                MotesCollected++;
            }
        }

        void TrackResonanceZone()
        {
            if (!_stage.HasResonanceZone) return;
            var zone = _stage.resonanceZone;
            var inside = (Position - zone.Center).sqrMagnitude <= zone.r * zone.r;
            var slowEnough = Velocity.magnitude <= zone.maxSpeed;
            if (inside && slowEnough) ZoneHoldSeconds += FixedStep;
            else ZoneHoldSeconds = 0f;
        }

        bool HasTimedObjective()
        {
            if (_stage.objectives == null) return false;
            for (var i = ObjectiveIndex; i < _stage.objectives.Length; i++)
            {
                var type = _stage.objectives[i].type;
                if (type == OrbitStageDefinition.ObjectiveType.Survive ||
                    type == OrbitStageDefinition.ObjectiveType.ResonateZone) return true;
            }
            return false;
        }

        void EvaluateObjectives()
        {
            if (_stage.objectives == null || _stage.objectives.Length == 0) return;

            // Objectives run in order; each must complete before the next counts.
            while (ObjectiveIndex < _stage.objectives.Length && IsObjectiveComplete(_stage.objectives[ObjectiveIndex]))
                ObjectiveIndex++;

            if (ObjectiveIndex < _stage.objectives.Length) return;

            var recovered = MotesCollected > 0;
            Finish(
                recovered ? Outcome.Recovered : Outcome.Stabilized,
                recovered ? "memory_resequenced" : "signal_aligned");
        }

        bool IsObjectiveComplete(OrbitStageDefinition.Objective objective)
        {
            switch (objective.type)
            {
                case OrbitStageDefinition.ObjectiveType.ReachAnchor:
                    if (!_stage.HasAnchor) return true;
                    var toAnchor = Position - _stage.anchor.Center;
                    return toAnchor.sqrMagnitude <= Mathf.Pow(_stage.anchor.r + AvatarRadius, 2f);

                case OrbitStageDefinition.ObjectiveType.CollectMotes:
                    return _moteCollected.Length == 0 || MotesCollected >= _moteCollected.Length;

                case OrbitStageDefinition.ObjectiveType.ResonateZone:
                    return _stage.HasResonanceZone && ZoneHoldSeconds >= _stage.resonanceZone.holdSeconds;

                case OrbitStageDefinition.ObjectiveType.Survive:
                    return Elapsed >= Mathf.Max(1f, objective.amount);

                case OrbitStageDefinition.ObjectiveType.ClearNoise:
                    return NoiseContacts >= Mathf.Max(1, Mathf.RoundToInt(objective.amount));
            }
            return true;
        }

        void Finish(Outcome outcome, string subtype)
        {
            CurrentPhase = Phase.Settled;
            Result = outcome;
            ResultSubtype = subtype;
            Velocity = Vector2.zero;
            Spin = 0f;
        }
    }
}
