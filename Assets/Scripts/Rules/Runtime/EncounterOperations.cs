using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    internal static class EncounterOperationValues
    {
        public static IReadOnlyList<EncounterParticipant> CopyParticipants(
            IEnumerable<EncounterParticipant> participants
        )
        {
            EncounterParticipant[] copied =
                participants?.ToArray() ?? throw new ArgumentNullException(nameof(participants));
            if (copied.Length == 0 || copied.Any(value => value == null))
                throw new ArgumentException(
                    "At least one complete participant is required.",
                    nameof(participants)
                );
            if (copied.Select(value => value.Creature).Distinct().Count() != copied.Length)
                throw new ArgumentException(
                    "A creature can be registered only once.",
                    nameof(participants)
                );
            return Array.AsReadOnly(copied);
        }
    }

    /// <summary>Requests a new encounter, initiative rolls, and its first eligible turn.</summary>
    public sealed class StartEncounterOp : IRuleOp<EncounterStartOutcome>
    {
        public EncounterId Encounter { get; }
        public PlayerId ProtagonistTeam { get; }
        public IReadOnlyList<EncounterParticipant> Participants { get; }

        public StartEncounterOp(
            EncounterId encounter,
            PlayerId protagonistTeam,
            IEnumerable<EncounterParticipant> participants
        )
        {
            if (encounter.IsEmpty || protagonistTeam.IsEmpty)
                throw new ArgumentException("Encounter and protagonist team IDs are required.");
            Encounter = encounter;
            ProtagonistTeam = protagonistTeam;
            Participants = EncounterOperationValues.CopyParticipants(participants);
        }
    }

    /// <summary>Requests same-store registration of encounter reinforcements.</summary>
    public sealed class JoinEncounterOp : IRuleOp<EncounterJoinOutcome>
    {
        public EncounterId Encounter { get; }
        public IReadOnlyList<EncounterJoinParticipant> Participants { get; }

        public JoinEncounterOp(
            EncounterId encounter,
            IEnumerable<EncounterJoinParticipant> participants
        )
        {
            if (encounter.IsEmpty)
                throw new ArgumentException("An encounter ID is required.", nameof(encounter));
            Encounter = encounter;
            EncounterJoinParticipant[] copied =
                participants?.ToArray() ?? throw new ArgumentNullException(nameof(participants));
            if (copied.Length == 0 || copied.Any(value => value == null))
                throw new ArgumentException(
                    "At least one complete reinforcement is required.",
                    nameof(participants)
                );
            if (
                copied.Select(value => value.Participant.Creature).Distinct().Count()
                != copied.Length
            )
                throw new ArgumentException("A creature can join only once.", nameof(participants));
            Participants = Array.AsReadOnly(copied);
        }
    }

    /// <summary>Requests iterative progression to the next living initiative slot.</summary>
    public sealed class AdvanceEncounterOp : IRuleOp<EncounterAdvanceOutcome>
    {
        public EncounterId Encounter { get; }

        public AdvanceEncounterOp(EncounterId encounter) =>
            Encounter = encounter.IsEmpty
                ? throw new ArgumentException("An encounter ID is required.", nameof(encounter))
                : encounter;
    }

    /// <summary>Requests completion of one exact active turn followed by advancement.</summary>
    public sealed class EndTurnOp : IRuleOp<EncounterAdvanceOutcome>
    {
        public TurnIdentity Turn { get; }

        public EndTurnOp(TurnIdentity turn) => Turn = turn;
    }

    /// <summary>Requests suspension without declaring an outcome.</summary>
    public sealed class SuspendEncounterOp : IRuleOp<EncounterSuspensionOutcome>
    {
        public EncounterId Encounter { get; }

        public SuspendEncounterOp(EncounterId encounter) =>
            Encounter = encounter.IsEmpty
                ? throw new ArgumentException("An encounter ID is required.", nameof(encounter))
                : encounter;
    }

    /// <summary>Requests protagonist-relative encounter completion.</summary>
    public sealed class EndEncounterOp : IRuleOp<EncounterEndOutcome>
    {
        public EncounterId Encounter { get; }
        public EncounterOutcome Outcome { get; }

        public EndEncounterOp(EncounterId encounter, EncounterOutcome outcome)
        {
            if (encounter.IsEmpty)
                throw new ArgumentException("An encounter ID is required.", nameof(encounter));
            if (!Enum.IsDefined(typeof(EncounterOutcome), outcome))
                throw new ArgumentOutOfRangeException(nameof(outcome));
            Encounter = encounter;
            Outcome = outcome;
        }
    }

    /// <summary>Re-evaluates outcome after settled Reaction-phase causal work.</summary>
    public sealed class EvaluateEncounterOutcomeOp : IRuleOp<EncounterEvaluationOutcome>
    {
        public EncounterId Encounter { get; }

        public EvaluateEncounterOutcomeOp(EncounterId encounter) =>
            Encounter = encounter.IsEmpty
                ? throw new ArgumentException("An encounter ID is required.", nameof(encounter))
                : encounter;
    }

    /// <summary>Opens the narrow turn-start extension point before final resource regain.</summary>
    public sealed class TurnStartingOp : IRuleOp<TurnStartContribution>
    {
        public EncounterId Encounter { get; }
        public CreatureId Actor { get; }

        internal TurnStartingOp(EncounterId encounter, CreatureId actor)
        {
            Encounter = encounter;
            Actor = actor;
        }
    }

    /// <summary>Opens the narrow exact-turn end extension point.</summary>
    public sealed class TurnEndingOp : IRuleOp<TurnEndContribution>
    {
        public TurnIdentity Turn { get; }

        internal TurnEndingOp(TurnIdentity turn) => Turn = turn;
    }

    /// <summary>Requests a temporary same-store action spend for unmigrated Unity actions.</summary>
    public sealed class SpendLegacyActionsOp : IRuleOp<LegacyActionSpendOutcome>
    {
        public CreatureId Actor { get; }
        public int Amount { get; }

        public SpendLegacyActionsOp(CreatureId actor, int amount)
        {
            if (actor.IsEmpty)
                throw new ArgumentException("An actor is required.", nameof(actor));
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            Actor = actor;
            Amount = amount;
        }
    }

    /// <summary>Requests a temporary same-store MAP increment for an unmigrated attack.</summary>
    public sealed class IncrementLegacyMapOp : IRuleOp<LegacyMapOutcome>
    {
        public CreatureId Actor { get; }

        public IncrementLegacyMapOp(CreatureId actor) =>
            Actor = actor.IsEmpty
                ? throw new ArgumentException("An actor is required.", nameof(actor))
                : actor;
    }

    public readonly struct EncounterStartOutcome
    {
        public EncounterState State { get; }

        internal EncounterStartOutcome(EncounterState state) => State = state;
    }

    public readonly struct EncounterJoinOutcome
    {
        public EncounterState State { get; }

        internal EncounterJoinOutcome(EncounterState state) => State = state;
    }

    public readonly struct EncounterAdvanceOutcome
    {
        public EncounterState State { get; }

        internal EncounterAdvanceOutcome(EncounterState state) => State = state;
    }

    public readonly struct EncounterSuspensionOutcome
    {
        public EncounterState State { get; }

        internal EncounterSuspensionOutcome(EncounterState state) => State = state;
    }

    public readonly struct EncounterEndOutcome
    {
        public EncounterState State { get; }

        internal EncounterEndOutcome(EncounterState state) => State = state;
    }

    public readonly struct EncounterEvaluationOutcome
    {
        public EncounterState State { get; }

        internal EncounterEvaluationOutcome(EncounterState state) => State = state;
    }

    public readonly struct TurnStartContribution
    {
        public int Actions { get; }

        public TurnStartContribution(int actions)
        {
            if (actions < 0)
                throw new ArgumentOutOfRangeException(nameof(actions));
            Actions = actions;
        }

        public static TurnStartContribution Standard => new TurnStartContribution(3);
    }

    public readonly struct TurnEndContribution
    {
        public static TurnEndContribution Complete => default;
    }

    public readonly struct LegacyActionSpendOutcome
    {
        public int Remaining { get; }

        internal LegacyActionSpendOutcome(int remaining) => Remaining = remaining;
    }

    public readonly struct LegacyMapOutcome
    {
        public int AttackCount { get; }

        internal LegacyMapOutcome(int attackCount) => AttackCount = attackCount;
    }

    internal sealed class CommitEncounterStartOp : IRuleOp<EncounterStartOutcome>
    {
        public EncounterId Encounter { get; }
        public PlayerId ProtagonistTeam { get; }
        public IReadOnlyList<InitiativeEntry> Roster { get; }

        public CommitEncounterStartOp(
            EncounterId encounter,
            PlayerId protagonistTeam,
            IReadOnlyList<InitiativeEntry> roster
        )
        {
            Encounter = encounter;
            ProtagonistTeam = protagonistTeam;
            Roster = roster;
        }
    }

    internal sealed class CommitEncounterJoinOp : IRuleOp<EncounterJoinOutcome>
    {
        public EncounterId Encounter { get; }
        public IReadOnlyList<InitiativeEntry> Additions { get; }
        public IReadOnlyDictionary<CreatureId, HealthState> InitialHealth { get; }

        public CommitEncounterJoinOp(
            EncounterId encounter,
            IReadOnlyList<InitiativeEntry> additions,
            IReadOnlyDictionary<CreatureId, HealthState> initialHealth
        )
        {
            Encounter = encounter;
            Additions = additions;
            InitialHealth = initialHealth;
        }
    }

    internal sealed class CommitInitiativeBoundaryOp : IRuleOp<InitiativeBoundaryOutcome>
    {
        public EncounterId Encounter { get; }

        public CommitInitiativeBoundaryOp(EncounterId encounter) => Encounter = encounter;
    }

    internal sealed class CommitTurnBeginOp : IRuleOp<EncounterAdvanceOutcome>
    {
        public EncounterId Encounter { get; }
        public CreatureId Actor { get; }
        public int Actions { get; }

        public CommitTurnBeginOp(EncounterId encounter, CreatureId actor, int actions)
        {
            Encounter = encounter;
            Actor = actor;
            Actions = actions;
        }
    }

    internal sealed class CommitTurnEndOp : IRuleOp<EncounterAdvanceOutcome>
    {
        public TurnIdentity Turn { get; }

        public CommitTurnEndOp(TurnIdentity turn) => Turn = turn;
    }

    internal sealed class CommitEncounterSuspendOp : IRuleOp<EncounterSuspensionOutcome>
    {
        public EncounterId Encounter { get; }

        public CommitEncounterSuspendOp(EncounterId encounter) => Encounter = encounter;
    }

    internal sealed class CommitEncounterEndOp : IRuleOp<EncounterEndOutcome>
    {
        public EncounterId Encounter { get; }
        public EncounterOutcome Outcome { get; }

        public CommitEncounterEndOp(EncounterId encounter, EncounterOutcome outcome)
        {
            Encounter = encounter;
            Outcome = outcome;
        }
    }

    internal sealed class CommitLegacyActionsOp : IRuleOp<LegacyActionSpendOutcome>
    {
        public CreatureId Actor { get; }
        public int Amount { get; }

        public CommitLegacyActionsOp(CreatureId actor, int amount)
        {
            Actor = actor;
            Amount = amount;
        }
    }

    internal sealed class CommitLegacyMapOp : IRuleOp<LegacyMapOutcome>
    {
        public CreatureId Actor { get; }

        public CommitLegacyMapOp(CreatureId actor) => Actor = actor;
    }

    internal readonly struct InitiativeBoundaryOutcome
    {
        public EncounterState State { get; }
        public InitiativeEntry Entry { get; }
        public IReadOnlyList<ActiveEffectTimingState> DueEffects { get; }

        public InitiativeBoundaryOutcome(
            EncounterState state,
            InitiativeEntry entry,
            IReadOnlyList<ActiveEffectTimingState> dueEffects
        )
        {
            State = state;
            Entry = entry;
            DueEffects = dueEffects;
        }
    }
}
