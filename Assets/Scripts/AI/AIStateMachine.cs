using System;
using System.Collections.Generic;
using UnityEngine;

namespace Skyrift
{
    // ─── State enum ───────────────────────────────────────────────────────────
    /// <summary>Discrete states an enemy AI aircraft can occupy.</summary>
    public enum AIState
    {
        Idle,       // on ground / waiting for mission start
        Patrol,     // following waypoints, no threat
        Engage,     // moving toward player, climbing to attack altitude
        Attack,     // within weapons range, actively firing
        Evade,      // taking damage, breaking off
        Retreat,    // low health, heading back to base
        Dead        // destroyed
    }

    // ─── Base transition interface ────────────────────────────────────────────
    /// <summary>Condition that tests whether an AI state transition should fire.</summary>
    public abstract class AITransition
    {
        public AIState TargetState { get; protected set; }
        public abstract bool Evaluate(EnemyAI agent);
    }

    // ─── Concrete transitions ─────────────────────────────────────────────────
    public class TransitionPlayerInRange : AITransition
    {
        private readonly float _range;
        public TransitionPlayerInRange(float range, AIState target)
        {
            _range      = range;
            TargetState = target;
        }
        public override bool Evaluate(EnemyAI agent) =>
            agent.DistanceToPlayer < _range;
    }

    public class TransitionPlayerOutOfRange : AITransition
    {
        private readonly float _range;
        public TransitionPlayerOutOfRange(float range, AIState target)
        {
            _range      = range;
            TargetState = target;
        }
        public override bool Evaluate(EnemyAI agent) =>
            agent.DistanceToPlayer > _range;
    }

    public class TransitionHealthBelow : AITransition
    {
        private readonly float _threshold;
        public TransitionHealthBelow(float threshold, AIState target)
        {
            _threshold  = threshold;
            TargetState = target;
        }
        public override bool Evaluate(EnemyAI agent)
        {
            var stats = agent.GetComponent<AircraftStats>();
            return stats != null && stats.HealthNormalized < _threshold;
        }
    }

    public class TransitionInAttackCone : AITransition
    {
        private readonly float _range;
        private readonly float _angleDeg;
        public TransitionInAttackCone(float range, float angleDeg, AIState target)
        {
            _range      = range;
            _angleDeg   = angleDeg;
            TargetState = target;
        }
        public override bool Evaluate(EnemyAI agent) =>
            agent.DistanceToPlayer < _range && agent.AngleToPlayer < _angleDeg;
    }

    public class TransitionEvadeCooldown : AITransition
    {
        private readonly float _duration;
        public TransitionEvadeCooldown(float dur, AIState target)
        {
            _duration   = dur;
            TargetState = target;
        }
        public override bool Evaluate(EnemyAI agent) =>
            agent.StateTimer > _duration;
    }

    // ─── State machine ────────────────────────────────────────────────────────
    /// <summary>
    /// Lightweight state machine used by <see cref="EnemyAI"/>.
    /// Each state holds a list of outgoing transitions evaluated in order
    /// every frame.  The first transition that passes fires.
    /// </summary>
    public class AIStateMachine
    {
        public AIState CurrentState  { get; private set; }
        public float   StateTimer    { get; private set; }

        private readonly Dictionary<AIState, List<AITransition>> _transitions
            = new Dictionary<AIState, List<AITransition>>();

        private readonly EnemyAI _agent;

        // ─── Events ───────────────────────────────────────────────────────────
        public event Action<AIState, AIState> OnStateChanged;

        public AIStateMachine(EnemyAI agent, AIState initial)
        {
            _agent       = agent;
            CurrentState = initial;
        }

        // ─── Building the graph ───────────────────────────────────────────────

        /// <summary>Adds a transition from <paramref name="from"/> state.</summary>
        public void AddTransition(AIState from, AITransition transition)
        {
            if (!_transitions.ContainsKey(from))
                _transitions[from] = new List<AITransition>();
            _transitions[from].Add(transition);
        }

        // ─── Tick ─────────────────────────────────────────────────────────────

        /// <summary>Evaluates all transitions for the current state. Call from Update.</summary>
        public void Tick(float dt)
        {
            StateTimer += dt;

            if (!_transitions.TryGetValue(CurrentState, out var list)) return;

            foreach (var t in list)
            {
                if (t.Evaluate(_agent))
                {
                    TransitionTo(t.TargetState);
                    return;
                }
            }
        }

        private void TransitionTo(AIState next)
        {
            AIState prev = CurrentState;
            CurrentState = next;
            StateTimer   = 0f;
            OnStateChanged?.Invoke(prev, next);
        }

        /// <summary>Forces an immediate state change (e.g. received damage).</summary>
        public void ForceState(AIState state)
        {
            TransitionTo(state);
        }
    }
}
