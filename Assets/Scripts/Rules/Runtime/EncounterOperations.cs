using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
        /// <summary>Gets the stable identity allocated for the new encounter.</summary>
        public EncounterId Encounter { get; }

        /// <summary>Gets the team whose survival determines player-relative outcomes.</summary>
        public PlayerId ProtagonistTeam { get; }

        /// <summary>Gets the immutable registrations rolled in their supplied order.</summary>
        public IReadOnlyList<EncounterParticipant> Participants { get; }

        /// <summary>Creates a complete request for one not-yet-started encounter.</summary>
        /// <param name="encounter">The new encounter identity.</param>
        /// <param name="protagonistTeam">The player/protagonist team.</param>
        /// <param name="participants">Unique creatures in deterministic registration order.</param>
        /// <exception cref="ArgumentException">
        /// An identity is empty, the participant roster is invalid, or no participant belongs to
        /// <paramref name="protagonistTeam"/>.
        /// </exception>
        public StartEncounterOp(
            EncounterId encounter,
            PlayerId protagonistTeam,
            IEnumerable<EncounterParticipant> participants
        )
        {
            if (encounter.IsEmpty || protagonistTeam.IsEmpty)
                throw new ArgumentException("Encounter and protagonist team IDs are required.");
            IReadOnlyList<EncounterParticipant> copied = EncounterOperationValues.CopyParticipants(
                participants
            );
            if (!copied.Any(participant => participant.Team == protagonistTeam))
                throw new ArgumentException(
                    "At least one encounter participant must belong to the protagonist team.",
                    nameof(participants)
                );
            Encounter = encounter;
            ProtagonistTeam = protagonistTeam;
            Participants = copied;
        }
    }

    /// <summary>Requests same-store registration of encounter reinforcements.</summary>
    public sealed class JoinEncounterOp : IRuleOp<EncounterJoinOutcome>
    {
        /// <summary>Gets the active encounter receiving the reinforcements.</summary>
        public EncounterId Encounter { get; }

        /// <summary>Gets the complete same-store reinforcement registrations.</summary>
        public IReadOnlyList<EncounterJoinParticipant> Participants { get; }

        /// <summary>Creates an atomic reinforcement request.</summary>
        /// <param name="encounter">The active encounter identity.</param>
        /// <param name="participants">Unique reinforcement creatures and initial health.</param>
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
        /// <summary>Gets the active encounter whose next boundary should be reached.</summary>
        public EncounterId Encounter { get; }

        /// <summary>Creates an iterative advance request for an encounter without an open turn.</summary>
        /// <param name="encounter">The active encounter identity.</param>
        public AdvanceEncounterOp(EncounterId encounter) =>
            Encounter = encounter.IsEmpty
                ? throw new ArgumentException("An encounter ID is required.", nameof(encounter))
                : encounter;
    }

    /// <summary>Requests completion of one exact active turn followed by advancement.</summary>
    public sealed class EndTurnOp : IRuleOp<EncounterAdvanceOutcome>
    {
        /// <summary>Gets the exact active turn to close.</summary>
        public TurnIdentity Turn { get; }

        /// <summary>Creates an end request that rejects if any turn identity component is stale.</summary>
        /// <param name="turn">The exact encounter, sequence, actor, round, and slot.</param>
        public EndTurnOp(TurnIdentity turn) => Turn = turn;
    }

    /// <summary>Requests suspension without declaring an outcome.</summary>
    public sealed class SuspendEncounterOp : IRuleOp<EncounterSuspensionOutcome>
    {
        /// <summary>Gets the active encounter to suspend without deciding a winner.</summary>
        public EncounterId Encounter { get; }

        /// <summary>Creates an encounter-suspension request.</summary>
        /// <param name="encounter">The active encounter identity.</param>
        public SuspendEncounterOp(EncounterId encounter) =>
            Encounter = encounter.IsEmpty
                ? throw new ArgumentException("An encounter ID is required.", nameof(encounter))
                : encounter;
    }

    /// <summary>Requests protagonist-relative encounter completion.</summary>
    public sealed class EndEncounterOp : IRuleOp<EncounterEndOutcome>
    {
        /// <summary>Gets the active encounter to complete.</summary>
        public EncounterId Encounter { get; }

        /// <summary>Gets the requested protagonist-relative result, which reducers revalidate.</summary>
        public EncounterOutcome Outcome { get; }

        /// <summary>Creates an outcome request that succeeds only when current health proves it.</summary>
        /// <param name="encounter">The active encounter identity.</param>
        /// <param name="outcome">The expected player victory or defeat.</param>
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
        /// <summary>Gets the encounter to evaluate from its latest committed health snapshot.</summary>
        public EncounterId Encounter { get; }

        /// <summary>Creates a settled-snapshot outcome evaluation request.</summary>
        /// <param name="encounter">The encounter that may end or skip a defeated active actor.</param>
        public EvaluateEncounterOutcomeOp(EncounterId encounter) =>
            Encounter = encounter.IsEmpty
                ? throw new ArgumentException("An encounter ID is required.", nameof(encounter))
                : encounter;
    }

    /// <summary>Opens the narrow turn-start extension point before final resource regain.</summary>
    public sealed class TurnStartingOp : IRuleOp<TurnStartContribution>
    {
        /// <summary>Gets the encounter whose reached boundary owns the hook.</summary>
        public EncounterId Encounter { get; }

        /// <summary>Gets the living candidate receiving ordered start adapters.</summary>
        public CreatureId Actor { get; }

        internal TurnStartingOp(EncounterId encounter, CreatureId actor)
        {
            Encounter = encounter;
            Actor = actor;
        }
    }

    /// <summary>
    /// Adapts one unmigrated turn-start behavior into the authoritative encounter dispatch.
    /// </summary>
    /// <remarks>
    /// Adapters run sequentially in registration order before resource regain. They may commit
    /// health changes only through the supplied <see cref="EncounterTurnStartContext"/>, keeping
    /// the work inside the active dispatcher root. The returned contribution becomes the input to
    /// the next adapter; the final value is the action count committed with <see cref="TurnBeganFact"/>.
    /// </remarks>
    public interface IEncounterTurnStartAdapter
    {
        /// <summary>Runs one awaited turn-start contribution for the reached living actor.</summary>
        /// <param name="context">The narrow same-dispatcher services for this boundary.</param>
        /// <param name="current">The action contribution produced so far.</param>
        /// <returns>The contribution to pass to the next adapter or final turn-begin reducer.</returns>
        ValueTask<TurnStartContribution> Apply(
            EncounterTurnStartContext context,
            TurnStartContribution current
        );
    }

    /// <summary>
    /// Exposes narrow same-dispatcher health work to transitional turn-start adapters.
    /// </summary>
    public sealed class EncounterTurnStartContext
    {
        private readonly OpHandlerContext context;

        internal EncounterTurnStartContext(
            EncounterId encounter,
            CreatureId actor,
            OpHandlerContext context
        )
        {
            Encounter = encounter;
            Actor = actor;
            this.context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>Gets the encounter whose initiative boundary is being processed.</summary>
        public EncounterId Encounter { get; }

        /// <summary>Gets the living candidate receiving turn-start hooks.</summary>
        public CreatureId Actor { get; }

        /// <summary>Gets the latest committed snapshot after all previously awaited adapters.</summary>
        public RulesSnapshot Snapshot => context.Snapshot;

        /// <summary>Commits already-final damage as a nested child of the active encounter root.</summary>
        /// <param name="target">The creature receiving the final damage.</param>
        /// <param name="amount">The non-negative damage remaining after upstream calculations.</param>
        /// <param name="origin">The stable Unity health-request identity.</param>
        /// <param name="source">The rule source responsible for the damage.</param>
        /// <returns>The exact health changes committed before this method completes.</returns>
        /// <exception cref="ArgumentException"><paramref name="target"/> is not <see cref="Actor"/>.</exception>
        /// <exception cref="InvalidOperationException">The nested health request is rejected.</exception>
        public async ValueTask<DamageOutcome> ApplyFinalDamage(
            CreatureId target,
            int amount,
            HealthChangeOriginId origin,
            RuleSource source
        )
        {
            if (target != Actor)
                throw new ArgumentException(
                    "A turn-start adapter may damage only its reached actor.",
                    nameof(target)
                );
            return EncounterHandlerResults.Require(
                await context.Dispatch(new ApplyDamageOp(target, amount, origin, source)),
                "turn-start damage"
            );
        }
    }

    /// <summary>Opens the narrow exact-turn end extension point.</summary>
    public sealed class TurnEndingOp : IRuleOp<TurnEndContribution>
    {
        /// <summary>Gets the exact active turn receiving end hooks.</summary>
        public TurnIdentity Turn { get; }

        internal TurnEndingOp(TurnIdentity turn) => Turn = turn;
    }

    /// <summary>Requests same-store turn authorization and an optional legacy action spend.</summary>
    public sealed class SpendLegacyActionsOp : IRuleOp<LegacyActionSpendOutcome>
    {
        /// <summary>Gets the creature whose reducer-owned actions are spent.</summary>
        public CreatureId Actor { get; }

        /// <summary>Gets the non-negative action cost; zero validates authority without mutation.</summary>
        public int Amount { get; }

        /// <summary>Creates a temporary same-store action authorization/spend request.</summary>
        /// <param name="actor">The creature paying the action cost.</param>
        /// <param name="amount">The non-negative number of actions to spend.</param>
        public SpendLegacyActionsOp(CreatureId actor, int amount)
        {
            if (actor.IsEmpty)
                throw new ArgumentException("An actor is required.", nameof(actor));
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            Actor = actor;
            Amount = amount;
        }
    }

    /// <summary>Requests a turn-authorized same-store MAP increment for an unmigrated attack.</summary>
    public sealed class IncrementLegacyMapOp : IRuleOp<LegacyMapOutcome>
    {
        /// <summary>Gets the creature whose turn-scoped attack count is incremented.</summary>
        public CreatureId Actor { get; }

        /// <summary>Creates a temporary same-store MAP-increment request.</summary>
        /// <param name="actor">The creature that must own the exact active current turn.</param>
        public IncrementLegacyMapOp(CreatureId actor) =>
            Actor = actor.IsEmpty
                ? throw new ArgumentException("An actor is required.", nameof(actor))
                : actor;
    }

    /// <summary>Returns the encounter snapshot produced by a successful start.</summary>
    public readonly struct EncounterStartOutcome : ISettledOperationResult<EncounterStartOutcome>
    {
        /// <summary>Gets the encounter after initiative and first-turn advancement settle.</summary>
        public EncounterState State { get; }

        /// <summary>Creates an outcome from one committed encounter snapshot.</summary>
        /// <param name="state">The non-null state represented by this outcome.</param>
        public EncounterStartOutcome(EncounterState state) =>
            State = state ?? throw new ArgumentNullException(nameof(state));

        EncounterStartOutcome ISettledOperationResult<EncounterStartOutcome>.Settle(
            RulesSnapshot snapshot
        ) => new EncounterStartOutcome(snapshot.Encounters[State.Id]);
    }

    /// <summary>Returns the atomic roster replacement produced by accepted reinforcements.</summary>
    public readonly struct EncounterJoinOutcome : ISettledOperationResult<EncounterJoinOutcome>
    {
        /// <summary>Gets the encounter containing the retained roster plus additions.</summary>
        public EncounterState State { get; }

        /// <summary>Creates an outcome from one committed encounter snapshot.</summary>
        /// <param name="state">The non-null state represented by this outcome.</param>
        public EncounterJoinOutcome(EncounterState state) =>
            State = state ?? throw new ArgumentNullException(nameof(state));

        EncounterJoinOutcome ISettledOperationResult<EncounterJoinOutcome>.Settle(
            RulesSnapshot snapshot
        ) => new EncounterJoinOutcome(snapshot.Encounters[State.Id]);
    }

    /// <summary>Returns the state produced by turn progression or encounter completion.</summary>
    public readonly struct EncounterAdvanceOutcome
        : ISettledOperationResult<EncounterAdvanceOutcome>
    {
        /// <summary>Gets the latest encounter, including its new turn or final outcome.</summary>
        public EncounterState State { get; }

        /// <summary>Creates an outcome from one committed encounter snapshot.</summary>
        /// <param name="state">The non-null state represented by this outcome.</param>
        public EncounterAdvanceOutcome(EncounterState state) =>
            State = state ?? throw new ArgumentNullException(nameof(state));

        EncounterAdvanceOutcome ISettledOperationResult<EncounterAdvanceOutcome>.Settle(
            RulesSnapshot snapshot
        ) => new EncounterAdvanceOutcome(snapshot.Encounters[State.Id]);
    }

    /// <summary>Returns the encounter after active resources are cleared for suspension.</summary>
    public readonly struct EncounterSuspensionOutcome
        : ISettledOperationResult<EncounterSuspensionOutcome>
    {
        /// <summary>Gets the suspended encounter state.</summary>
        public EncounterState State { get; }

        /// <summary>Creates an outcome from one committed encounter snapshot.</summary>
        /// <param name="state">The non-null state represented by this outcome.</param>
        public EncounterSuspensionOutcome(EncounterState state) =>
            State = state ?? throw new ArgumentNullException(nameof(state));

        EncounterSuspensionOutcome ISettledOperationResult<EncounterSuspensionOutcome>.Settle(
            RulesSnapshot snapshot
        ) => new EncounterSuspensionOutcome(snapshot.Encounters[State.Id]);
    }

    /// <summary>Returns the single committed player-relative encounter result.</summary>
    public readonly struct EncounterEndOutcome : ISettledOperationResult<EncounterEndOutcome>
    {
        /// <summary>Gets the ended encounter state.</summary>
        public EncounterState State { get; }

        /// <summary>Creates an outcome from one committed encounter snapshot.</summary>
        /// <param name="state">The non-null state represented by this outcome.</param>
        public EncounterEndOutcome(EncounterState state) =>
            State = state ?? throw new ArgumentNullException(nameof(state));

        EncounterEndOutcome ISettledOperationResult<EncounterEndOutcome>.Settle(
            RulesSnapshot snapshot
        ) => new EncounterEndOutcome(snapshot.Encounters[State.Id]);
    }

    /// <summary>Returns the latest state after settled health outcome evaluation.</summary>
    public readonly struct EncounterEvaluationOutcome
        : ISettledOperationResult<EncounterEvaluationOutcome>
    {
        /// <summary>Gets the active, advanced, or ended encounter state.</summary>
        public EncounterState State { get; }

        /// <summary>Creates an outcome from one committed encounter snapshot.</summary>
        /// <param name="state">The non-null state represented by this outcome.</param>
        public EncounterEvaluationOutcome(EncounterState state) =>
            State = state ?? throw new ArgumentNullException(nameof(state));

        EncounterEvaluationOutcome ISettledOperationResult<EncounterEvaluationOutcome>.Settle(
            RulesSnapshot snapshot
        ) => new EncounterEvaluationOutcome(snapshot.Encounters[State.Id]);
    }

    /// <summary>Carries the final action count through ordered turn-start adapters.</summary>
    public readonly struct TurnStartContribution
    {
        /// <summary>Gets the non-negative actions to grant if the actor remains eligible.</summary>
        public int Actions { get; }

        /// <summary>Creates a validated contribution for final resource regain.</summary>
        /// <param name="actions">The non-negative derived action count.</param>
        public TurnStartContribution(int actions)
        {
            if (actions < 0)
                throw new ArgumentOutOfRangeException(nameof(actions));
            Actions = actions;
        }

        /// <summary>Gets the normal unmodified three-action contribution.</summary>
        public static TurnStartContribution Standard => new TurnStartContribution(3);
    }

    /// <summary>Marks successful completion of the narrow turn-end hook.</summary>
    public readonly struct TurnEndContribution
    {
        /// <summary>Gets the completed hook marker.</summary>
        public static TurnEndContribution Complete => default;
    }

    /// <summary>Returns remaining actions after same-store authorization and any requested spend.</summary>
    public readonly struct LegacyActionSpendOutcome
    {
        /// <summary>Gets the actor's committed action count.</summary>
        public int Remaining { get; }

        internal LegacyActionSpendOutcome(int remaining) => Remaining = remaining;
    }

    /// <summary>Returns turn-scoped attack count after a temporary legacy increment.</summary>
    public readonly struct LegacyMapOutcome
    {
        /// <summary>Gets the actor's committed attack count.</summary>
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
