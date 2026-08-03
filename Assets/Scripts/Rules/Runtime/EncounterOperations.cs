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

        /// <summary>Gets the policy controlling automatic encounter conclusion.</summary>
        public EncounterConclusionPolicy ConclusionPolicy { get; }

        /// <summary>Gets the immutable registrations rolled in their supplied order.</summary>
        public IReadOnlyList<EncounterParticipant> Participants { get; }

        /// <summary>Creates a complete request for one not-yet-started encounter.</summary>
        /// <param name="encounter">The new encounter identity.</param>
        /// <param name="protagonistTeam">The player/protagonist team.</param>
        /// <param name="participants">Unique creatures in deterministic registration order.</param>
        /// <param name="conclusionPolicy">The health outcomes that automatically end the encounter.</param>
        /// <exception cref="ArgumentException">
        /// An identity is empty, the participant roster is invalid, or no participant belongs to
        /// <paramref name="protagonistTeam"/>.
        /// </exception>
        public StartEncounterOp(
            EncounterId encounter,
            PlayerId protagonistTeam,
            IEnumerable<EncounterParticipant> participants,
            EncounterConclusionPolicy conclusionPolicy
        )
        {
            if (encounter.IsEmpty || protagonistTeam.IsEmpty)
                throw new ArgumentException("Encounter and protagonist team IDs are required.");
            if (!Enum.IsDefined(typeof(EncounterConclusionPolicy), conclusionPolicy))
                throw new ArgumentOutOfRangeException(nameof(conclusionPolicy));
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
            ConclusionPolicy = conclusionPolicy;
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

        /// <summary>Creates an atomic, exact-replay-safe reinforcement request.</summary>
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

    /// <summary>Opens the narrow completion-only turn-start extension point.</summary>
    public sealed class TurnStartingOp : IRuleOp<TurnStartCompletion>
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
    /// Adapters run sequentially in registration order before resource regain. They may commit an
    /// ordered final-damage batch only through the supplied
    /// <see cref="EncounterTurnStartContext"/>, keeping the work inside the active dispatcher root.
    /// The engine checkpoints only after <see cref="Apply"/> returns successfully. An adapter that
    /// requires fallible post-damage presentation must use the context's atomic damage-and-
    /// completion boundary so recovery cannot replay its damage.
    /// </remarks>
    public interface IEncounterTurnStartAdapter
    {
        /// <summary>Runs one awaited turn-start adapter for the reached living actor.</summary>
        /// <param name="context">The narrow same-dispatcher services for this boundary.</param>
        /// <returns>A task that completes after the adapter's work finishes.</returns>
        ValueTask Apply(EncounterTurnStartContext context);
    }

    /// <summary>
    /// Exposes narrow same-dispatcher health work to transitional turn-start adapters.
    /// </summary>
    public sealed class EncounterTurnStartContext
    {
        private readonly OpHandlerContext context;
        private readonly RoundNumber round;
        private readonly int slot;
        private readonly int adapterIndex;

        internal bool HasCommittedCompletion { get; private set; }

        internal EncounterTurnStartContext(
            EncounterId encounter,
            CreatureId actor,
            RoundNumber round,
            int slot,
            int adapterIndex,
            OpHandlerContext context
        )
        {
            Encounter = encounter;
            Actor = actor;
            this.round = round;
            this.slot = slot;
            this.adapterIndex = adapterIndex;
            this.context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>Gets the encounter whose initiative boundary is being processed.</summary>
        public EncounterId Encounter { get; }

        /// <summary>Gets the living candidate receiving turn-start hooks.</summary>
        public CreatureId Actor { get; }

        /// <summary>Gets the latest committed snapshot after all previously awaited adapters.</summary>
        public RulesSnapshot Snapshot => context.Snapshot;

        /// <summary>
        /// Atomically commits an ordered final-damage batch and this adapter's completion receipt.
        /// </summary>
        /// <param name="changes">
        /// Non-empty, same-source damage entries for the reached actor. Repeated targets are
        /// allowed, but each entry requires a distinct health-change origin.
        /// </param>
        /// <returns>The exact damage outcomes in request order.</returns>
        /// <remarks>
        /// Use this boundary when an adapter must perform fallible presentation after damage has
        /// committed. The damage and adapter checkpoint share one reducer transaction, so a later
        /// observer or presentation failure resumes at the next adapter without replaying damage.
        /// After this method resolves, the adapter may perform presentation but must not attempt
        /// another authoritative mutation before returning.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// The batch does not match the exact current adapter progress or its reducer rejects.
        /// </exception>
        public async ValueTask<
            IReadOnlyList<DamageOutcome>
        > CommitFinalDamageBatchAndCompleteAdapter(IEnumerable<HealthBatchChange> changes)
        {
            CommitTurnStartDamageBatchOutcome outcome = EncounterHandlerResults.Require(
                await context.Dispatch(
                    new CommitTurnStartDamageBatchOp(
                        Encounter,
                        round,
                        slot,
                        Actor,
                        adapterIndex,
                        changes
                    )
                ),
                "turn-start damage batch"
            );
            HasCommittedCompletion = true;
            return outcome.Damage;
        }
    }

    /// <summary>Opens the narrow exact-turn end extension point.</summary>
    public sealed class TurnEndingOp : IRuleOp<TurnEndContribution>
    {
        /// <summary>Gets the exact active turn receiving end hooks.</summary>
        public TurnIdentity Turn { get; }

        internal TurnEndingOp(TurnIdentity turn) => Turn = turn;
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

    /// <summary>Marks successful completion of all ordered turn-start adapters.</summary>
    public readonly struct TurnStartCompletion
    {
        /// <summary>Gets the completed hook marker.</summary>
        public static TurnStartCompletion Complete => default;
    }

    /// <summary>Marks successful completion of the narrow turn-end hook.</summary>
    public readonly struct TurnEndContribution
    {
        /// <summary>Gets the completed hook marker.</summary>
        public static TurnEndContribution Complete => default;
    }

    internal sealed class CommitEncounterStartOp : IRuleOp<EncounterStartOutcome>
    {
        public EncounterId Encounter { get; }
        public PlayerId ProtagonistTeam { get; }
        public EncounterConclusionPolicy ConclusionPolicy { get; }
        public IReadOnlyList<InitiativeEntry> Roster { get; }

        public CommitEncounterStartOp(
            EncounterId encounter,
            PlayerId protagonistTeam,
            EncounterConclusionPolicy conclusionPolicy,
            IReadOnlyList<InitiativeEntry> roster
        )
        {
            Encounter = encounter;
            ProtagonistTeam = protagonistTeam;
            ConclusionPolicy = conclusionPolicy;
            Roster = roster;
        }
    }

    internal sealed class CommitEncounterJoinOp : IRuleOp<EncounterJoinOutcome>
    {
        public EncounterId Encounter { get; }
        public IReadOnlyList<InitiativeEntry> Additions { get; }
        public IReadOnlyDictionary<CreatureId, CombatantRulesState> Registrations { get; }

        public CommitEncounterJoinOp(
            EncounterId encounter,
            IReadOnlyList<InitiativeEntry> additions,
            IReadOnlyDictionary<CreatureId, CombatantRulesState> registrations
        )
        {
            Encounter = encounter;
            Additions = additions;
            Registrations = registrations;
        }
    }

    internal sealed class CommitInitiativeAssignmentsOp : IRuleOp<InitiativeAssignmentsOutcome>
    {
        public CommitInitiativeAssignmentsOp(
            EncounterId encounter,
            IReadOnlyList<InitiativeEntry> entries
        )
        {
            Encounter = encounter;
            Entries = entries;
        }

        public EncounterId Encounter { get; }

        public IReadOnlyList<InitiativeEntry> Entries { get; }
    }

    internal readonly struct InitiativeAssignmentsOutcome
    {
        public InitiativeAssignmentsOutcome(int count) => Count = count;

        public int Count { get; }
    }

    internal sealed class CommitInitiativeBoundaryOp : IRuleOp<InitiativeBoundaryOutcome>
    {
        public EncounterId Encounter { get; }

        public CommitInitiativeBoundaryOp(EncounterId encounter) => Encounter = encounter;
    }

    internal sealed class CommitInitiativeBoundaryPublicationOp : IRuleOp<EncounterAdvanceOutcome>
    {
        public EncounterId Encounter { get; }
        public RoundNumber Round { get; }
        public int Slot { get; }
        public CreatureId Actor { get; }

        public CommitInitiativeBoundaryPublicationOp(
            EncounterId encounter,
            RoundNumber round,
            int slot,
            CreatureId actor
        )
        {
            Encounter = encounter;
            Round = round;
            Slot = slot;
            Actor = actor;
        }
    }

    internal sealed class BeginInitiativeTurnOp : IRuleOp<EncounterAdvanceOutcome>
    {
        public EncounterId Encounter { get; }
        public RoundNumber Round { get; }
        public int Slot { get; }
        public CreatureId Actor { get; }

        public BeginInitiativeTurnOp(
            EncounterId encounter,
            RoundNumber round,
            int slot,
            CreatureId actor
        )
        {
            Encounter = encounter;
            Round = round;
            Slot = slot;
            Actor = actor;
        }
    }

    internal sealed class CommitTurnBeginOp : IRuleOp<EncounterAdvanceOutcome>
    {
        public EncounterId Encounter { get; }
        public CreatureId Actor { get; }
        public TurnResourceCommitPlan Resources { get; }

        public CommitTurnBeginOp(
            EncounterId encounter,
            CreatureId actor,
            TurnResourceCommitPlan resources
        )
        {
            Encounter = encounter;
            Actor = actor;
            Resources = resources ?? throw new ArgumentNullException(nameof(resources));
        }
    }

    /// <summary>
    /// Commits one successfully completed turn-start adapter at the exact published boundary.
    /// </summary>
    internal sealed class CommitTurnStartAdapterProgressOp : IRuleOp<TurnStartAdapterProgress>
    {
        public EncounterId Encounter { get; }
        public RoundNumber Round { get; }
        public int Slot { get; }
        public CreatureId Actor { get; }
        public int ExpectedNextAdapterIndex { get; }

        public CommitTurnStartAdapterProgressOp(
            EncounterId encounter,
            RoundNumber round,
            int slot,
            CreatureId actor,
            int expectedNextAdapterIndex
        )
        {
            Encounter = encounter;
            Round = round;
            Slot = slot;
            Actor = actor;
            ExpectedNextAdapterIndex = expectedNextAdapterIndex;
        }
    }

    internal sealed class CommitTurnStartDamageBatchOutcome
    {
        public CommitTurnStartDamageBatchOutcome(
            TurnStartAdapterProgress progress,
            IEnumerable<DamageOutcome> damage
        )
        {
            Progress = progress ?? throw new ArgumentNullException(nameof(progress));
            Damage = Array.AsReadOnly(
                damage?.ToArray() ?? throw new ArgumentNullException(nameof(damage))
            );
        }

        public TurnStartAdapterProgress Progress { get; }
        public IReadOnlyList<DamageOutcome> Damage { get; }
    }

    /// <summary>
    /// Atomically commits an ordered same-actor damage batch and its adapter completion receipt.
    /// </summary>
    internal sealed class CommitTurnStartDamageBatchOp
        : IRuleOp<CommitTurnStartDamageBatchOutcome>,
            IRuleSourcedOp
    {
        public CommitTurnStartDamageBatchOp(
            EncounterId encounter,
            RoundNumber round,
            int slot,
            CreatureId actor,
            int expectedNextAdapterIndex,
            IEnumerable<HealthBatchChange> changes
        )
        {
            HealthBatchChange[] copied =
                changes?.ToArray() ?? throw new ArgumentNullException(nameof(changes));
            if (
                copied.Length == 0
                || copied.Any(change =>
                    change == null
                    || change.Kind != HealthBatchChangeKind.Damage
                    || change.Target != actor
                )
            )
                throw new ArgumentException(
                    "A turn-start damage batch requires final damage entries for its exact actor.",
                    nameof(changes)
                );
            if (copied.Select(change => change.Origin).Distinct().Count() != copied.Length)
                throw new ArgumentException(
                    "Every turn-start damage entry requires a distinct origin.",
                    nameof(changes)
                );
            if (copied.Select(change => change.Source).Distinct().Count() != 1)
                throw new ArgumentException(
                    "Every turn-start damage entry requires the same rule source.",
                    nameof(changes)
                );
            Encounter = encounter;
            Round = round;
            Slot = slot;
            Actor = actor;
            ExpectedNextAdapterIndex = expectedNextAdapterIndex;
            Changes = Array.AsReadOnly(copied);
            Source = copied[0].Source;
        }

        public EncounterId Encounter { get; }
        public RoundNumber Round { get; }
        public int Slot { get; }
        public CreatureId Actor { get; }
        public int ExpectedNextAdapterIndex { get; }
        public IReadOnlyList<HealthBatchChange> Changes { get; }
        public RuleSource Source { get; }
    }

    internal sealed class CommitInitiativeTurnStartSkippedOp : IRuleOp<EncounterAdvanceOutcome>
    {
        public CommitInitiativeTurnStartSkippedOp(
            EncounterId encounter,
            RoundNumber round,
            int slot,
            CreatureId actor
        )
        {
            Encounter = encounter;
            Round = round;
            Slot = slot;
            Actor = actor;
        }

        public EncounterId Encounter { get; }
        public RoundNumber Round { get; }
        public int Slot { get; }
        public CreatureId Actor { get; }
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
