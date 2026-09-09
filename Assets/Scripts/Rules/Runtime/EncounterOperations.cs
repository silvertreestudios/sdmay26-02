using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    internal static class EncounterOperationValues
    {
        public static IReadOnlyList<CombatantRulesState> CopyCombatants(
            IEnumerable<CombatantRulesState> combatants
        )
        {
            CombatantRulesState[] copied =
                combatants?.ToArray() ?? throw new ArgumentNullException(nameof(combatants));
            if (copied.Length == 0 || copied.Any(value => value == null))
                throw new ArgumentException(
                    "At least one complete combatant is required.",
                    nameof(combatants)
                );
            if (copied.Select(value => value.Creature.Id).Distinct().Count() != copied.Length)
                throw new ArgumentException(
                    "A creature can be registered only once.",
                    nameof(combatants)
                );
            return Array.AsReadOnly(copied);
        }

        public static IReadOnlyList<CreatureId> CopyRequiredLivingTargets(
            IEnumerable<CreatureId> targets
        )
        {
            CreatureId[] copied =
                targets?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(targets));
            if (copied.Any(target => target.IsEmpty))
                throw new ArgumentException(
                    "Required living targets cannot contain an empty creature identity.",
                    nameof(targets)
                );
            return Array.AsReadOnly(copied);
        }
    }

    /// <summary>Creates an empty encounter before combatants or turns are added.</summary>
    public sealed class InitEncounterOp : IRuleOp<EncounterInitializationOutcome>
    {
        /// <summary>Gets the stable identity allocated for the new encounter.</summary>
        public EncounterId Encounter { get; }

        /// <summary>Gets the team whose survival determines player-relative outcomes.</summary>
        public PlayerId ProtagonistTeam { get; }

        /// <summary>Gets the policy controlling automatic encounter conclusion.</summary>
        public EncounterConclusionPolicy ConclusionPolicy { get; }

        /// <summary>Creates a complete request for one not-yet-initialized encounter.</summary>
        /// <param name="encounter">The new encounter identity.</param>
        /// <param name="protagonistTeam">The player/protagonist team.</param>
        /// <param name="conclusionPolicy">The health outcomes that automatically end the encounter.</param>
        /// <exception cref="ArgumentException">
        /// An identity is empty or the conclusion policy is invalid.
        /// </exception>
        public InitEncounterOp(
            EncounterId encounter,
            PlayerId protagonistTeam,
            EncounterConclusionPolicy conclusionPolicy = EncounterConclusionPolicy.VictoryOrDefeat
        )
        {
            if (encounter.IsEmpty || protagonistTeam.IsEmpty)
                throw new ArgumentException("Encounter and protagonist team IDs are required.");
            if (!Enum.IsDefined(typeof(EncounterConclusionPolicy), conclusionPolicy))
                throw new ArgumentOutOfRangeException(nameof(conclusionPolicy));
            Encounter = encounter;
            ProtagonistTeam = protagonistTeam;
            ConclusionPolicy = conclusionPolicy;
        }
    }

    /// <summary>Adds one complete immutable batch to an initialized or active encounter.</summary>
    public sealed class AddCombatantsOp : IRuleOp<CombatantsAddedOutcome>
    {
        /// <summary>Gets the encounter receiving the combatants.</summary>
        public EncounterId Encounter { get; }

        /// <summary>Gets the complete registrations in deterministic registration order.</summary>
        public IReadOnlyList<CombatantRulesState> Combatants { get; }

        /// <summary>Creates one atomic combatant-addition request.</summary>
        /// <param name="encounter">The initialized or active encounter identity.</param>
        /// <param name="combatants">Unique complete combatant registrations.</param>
        public AddCombatantsOp(EncounterId encounter, IEnumerable<CombatantRulesState> combatants)
        {
            if (encounter.IsEmpty)
                throw new ArgumentException("An encounter ID is required.", nameof(encounter));
            Encounter = encounter;
            Combatants = EncounterOperationValues.CopyCombatants(combatants);
        }
    }

    /// <summary>
    /// Activates a populated initialized encounter if necessary, then advances to the next living
    /// initiative slot. The encounter must not have an open turn.
    /// </summary>
    public sealed class AdvanceEncounterOp : IRuleOp<EncounterAdvanceOutcome>
    {
        /// <summary>Gets the initialized or active encounter whose next boundary should be reached.</summary>
        public EncounterId Encounter { get; }

        /// <summary>Creates an iterative advance request for an encounter without an open turn.</summary>
        /// <param name="encounter">The initialized or active encounter identity.</param>
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

    /// <summary>Calculates the action allowance for one exact turn before resources commit.</summary>
    public sealed class CalculateTurnResourcesOp : IRuleOp<TurnResourceContribution>
    {
        /// <summary>Gets the exact committed turn whose allowance is being calculated.</summary>
        public TurnIdentity Turn { get; }

        internal CalculateTurnResourcesOp(TurnIdentity turn) => Turn = turn;
    }

    /// <summary>Regains actions and reaction for one exact committed turn.</summary>
    public sealed class RegainTurnResourcesOp : IRuleOp<EncounterAdvanceOutcome>
    {
        /// <summary>Gets the exact turn that must still be current when resources commit.</summary>
        public TurnIdentity Turn { get; }

        /// <summary>Creates a regain request for an already committed exact turn.</summary>
        /// <param name="turn">The exact encounter, sequence, actor, round, and slot.</param>
        public RegainTurnResourcesOp(TurnIdentity turn) => Turn = turn;
    }

    /// <summary>Opens the narrow exact-turn end extension point.</summary>
    public sealed class TurnEndingOp : IRuleOp<TurnEndContribution>
    {
        /// <summary>Gets the exact active turn receiving end hooks.</summary>
        public TurnIdentity Turn { get; }

        internal TurnEndingOp(TurnIdentity turn) => Turn = turn;
    }

    /// <summary>Requests exact-turn authorization with an optional authoritative action spend.</summary>
    public sealed class SpendEncounterActionsOp : IRuleOp<EncounterActionSpendOutcome>
    {
        /// <summary>Gets the creature whose reducer-owned actions are spent.</summary>
        public CreatureId Actor { get; }

        /// <summary>Gets the non-negative action cost; zero validates authority without mutation.</summary>
        public int Amount { get; }

        /// <summary>
        /// Gets the encounter participants that must still be living when the spend commits.
        /// </summary>
        public IReadOnlyList<CreatureId> RequiredLivingTargets { get; }

        /// <summary>Creates an authoritative encounter action authorization/spend request.</summary>
        /// <param name="actor">The creature paying the action cost.</param>
        /// <param name="amount">The non-negative number of actions to spend.</param>
        public SpendEncounterActionsOp(CreatureId actor, int amount)
            : this(actor, amount, Array.Empty<CreatureId>()) { }

        /// <summary>
        /// Creates an action spend that atomically requires one current living encounter target.
        /// </summary>
        /// <param name="actor">The creature paying the action cost.</param>
        /// <param name="amount">The non-negative number of actions to spend.</param>
        /// <param name="requiredLivingTarget">
        /// The selected participant that must remain living in the actor's encounter.
        /// </param>
        public SpendEncounterActionsOp(
            CreatureId actor,
            int amount,
            CreatureId requiredLivingTarget
        )
            : this(actor, amount, new[] { requiredLivingTarget }) { }

        /// <summary>
        /// Creates an action spend that atomically requires every selected encounter target to
        /// remain living.
        /// </summary>
        /// <param name="actor">The creature paying the action cost.</param>
        /// <param name="amount">The non-negative number of actions to spend.</param>
        /// <param name="requiredLivingTargets">
        /// The selected participants that must remain living in the actor's encounter. The
        /// operation copies and de-duplicates this sequence before dispatch.
        /// </param>
        public SpendEncounterActionsOp(
            CreatureId actor,
            int amount,
            IEnumerable<CreatureId> requiredLivingTargets
        )
        {
            if (actor.IsEmpty)
                throw new ArgumentException("An actor is required.", nameof(actor));
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            Actor = actor;
            Amount = amount;
            RequiredLivingTargets = EncounterOperationValues.CopyRequiredLivingTargets(
                requiredLivingTargets
            );
        }
    }

    /// <summary>Returns the empty encounter snapshot produced by initialization.</summary>
    public readonly struct EncounterInitializationOutcome
    {
        /// <summary>Gets the initialized encounter before combatants or turns are added.</summary>
        public EncounterState State { get; }

        /// <summary>Creates an outcome from one committed encounter snapshot.</summary>
        /// <param name="state">The non-null state represented by this outcome.</param>
        public EncounterInitializationOutcome(EncounterState state) =>
            State = state ?? throw new ArgumentNullException(nameof(state));
    }

    /// <summary>Returns the atomic roster replacement produced by accepted combatants.</summary>
    public readonly struct CombatantsAddedOutcome
    {
        /// <summary>Gets the encounter containing the retained roster plus additions.</summary>
        public EncounterState State { get; }

        /// <summary>Creates an outcome from one committed encounter snapshot.</summary>
        /// <param name="state">The non-null state represented by this outcome.</param>
        public CombatantsAddedOutcome(EncounterState state) =>
            State = state ?? throw new ArgumentNullException(nameof(state));
    }

    /// <summary>Returns the state produced by turn progression or encounter completion.</summary>
    public readonly struct EncounterAdvanceOutcome
    {
        /// <summary>Gets the encounter state produced by the advance handler.</summary>
        /// <remarks>
        /// A reached initiative boundary has no current turn. Awaited boundary listeners may begin
        /// a later turn, skip actors, or end the encounter before dispatch returns. Read the
        /// dispatcher's current snapshot when the caller needs that later authoritative state.
        /// </remarks>
        public EncounterState State { get; }

        /// <summary>Creates an outcome from one committed encounter snapshot.</summary>
        /// <param name="state">The non-null state represented by this outcome.</param>
        public EncounterAdvanceOutcome(EncounterState state) =>
            State = state ?? throw new ArgumentNullException(nameof(state));
    }

    /// <summary>Returns the encounter after active resources are cleared for suspension.</summary>
    public readonly struct EncounterSuspensionOutcome
    {
        /// <summary>Gets the suspended encounter state.</summary>
        public EncounterState State { get; }

        /// <summary>Creates an outcome from one committed encounter snapshot.</summary>
        /// <param name="state">The non-null state represented by this outcome.</param>
        public EncounterSuspensionOutcome(EncounterState state) =>
            State = state ?? throw new ArgumentNullException(nameof(state));
    }

    /// <summary>Returns the single committed player-relative encounter result.</summary>
    public readonly struct EncounterEndOutcome
    {
        /// <summary>Gets the ended encounter state.</summary>
        public EncounterState State { get; }

        /// <summary>Creates an outcome from one committed encounter snapshot.</summary>
        /// <param name="state">The non-null state represented by this outcome.</param>
        public EncounterEndOutcome(EncounterState state) =>
            State = state ?? throw new ArgumentNullException(nameof(state));
    }

    /// <summary>Returns the encounter state produced by outcome evaluation.</summary>
    public readonly struct EncounterEvaluationOutcome
    {
        /// <summary>Gets the active, advanced, or ended state produced by the handler.</summary>
        public EncounterState State { get; }

        /// <summary>Creates an outcome from one committed encounter snapshot.</summary>
        /// <param name="state">The non-null state represented by this outcome.</param>
        public EncounterEvaluationOutcome(EncounterState state) =>
            State = state ?? throw new ArgumentNullException(nameof(state));
    }

    /// <summary>Carries the calculated action allowance into exact-turn resource regain.</summary>
    public readonly struct TurnResourceContribution
    {
        /// <summary>Gets the non-negative actions to grant if the actor remains eligible.</summary>
        public int Actions { get; }

        /// <summary>Creates a validated contribution for final resource regain.</summary>
        /// <param name="actions">The non-negative derived action count.</param>
        public TurnResourceContribution(int actions)
        {
            if (actions < 0)
                throw new ArgumentOutOfRangeException(nameof(actions));
            Actions = actions;
        }

        /// <summary>Gets the normal unmodified three-action contribution.</summary>
        public static TurnResourceContribution Standard => new TurnResourceContribution(3);
    }

    /// <summary>Marks successful completion of the narrow turn-end hook.</summary>
    public readonly struct TurnEndContribution
    {
        /// <summary>Gets the completed hook marker.</summary>
        public static TurnEndContribution Complete => default;
    }

    /// <summary>Returns remaining actions after exact-turn authorization and any requested spend.</summary>
    public readonly struct EncounterActionSpendOutcome
    {
        /// <summary>Gets the actor's committed action count.</summary>
        public int Remaining { get; }

        internal EncounterActionSpendOutcome(int remaining) => Remaining = remaining;
    }

    internal sealed class CommitEncounterInitializationOp : IRuleOp<EncounterInitializationOutcome>
    {
        public EncounterId Encounter { get; }
        public PlayerId ProtagonistTeam { get; }
        public EncounterConclusionPolicy ConclusionPolicy { get; }

        public CommitEncounterInitializationOp(
            EncounterId encounter,
            PlayerId protagonistTeam,
            EncounterConclusionPolicy conclusionPolicy
        )
        {
            Encounter = encounter;
            ProtagonistTeam = protagonistTeam;
            ConclusionPolicy = conclusionPolicy;
        }
    }

    internal sealed class CommitCombatantsAdditionOp : IRuleOp<CombatantsAddedOutcome>
    {
        public EncounterId Encounter { get; }
        public IReadOnlyList<CombatantAddition> Additions { get; }

        public CommitCombatantsAdditionOp(
            EncounterId encounter,
            IReadOnlyList<CombatantAddition> additions
        )
        {
            Encounter = encounter;
            CombatantAddition[] copied =
                additions?.ToArray() ?? throw new ArgumentNullException(nameof(additions));
            if (copied.Length == 0 || copied.Any(addition => addition == null))
                throw new ArgumentException(
                    "At least one complete combatant addition is required.",
                    nameof(additions)
                );
            Additions = Array.AsReadOnly(copied);
        }
    }

    internal sealed class CombatantAddition
    {
        public CombatantAddition(InitiativeEntry initiative, CombatantRulesState combatant)
        {
            Initiative = initiative ?? throw new ArgumentNullException(nameof(initiative));
            Combatant = combatant ?? throw new ArgumentNullException(nameof(combatant));
        }

        public InitiativeEntry Initiative { get; }
        public CombatantRulesState Combatant { get; }
    }

    internal sealed class CommitEncounterActivationOp : IRuleOp<EncounterAdvanceOutcome>
    {
        public CommitEncounterActivationOp(EncounterId encounter) => Encounter = encounter;

        public EncounterId Encounter { get; }
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

    internal sealed class CommitInitiativeBoundaryOp : IRuleOp<EncounterAdvanceOutcome>
    {
        public EncounterId Encounter { get; }

        public CommitInitiativeBoundaryOp(EncounterId encounter) => Encounter = encounter;
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

        public CommitTurnBeginOp(EncounterId encounter, CreatureId actor)
        {
            Encounter = encounter;
            Actor = actor;
        }
    }

    internal sealed class CommitTurnResourcesRegainedOp : IRuleOp<EncounterAdvanceOutcome>
    {
        public TurnIdentity Turn { get; }
        public int Actions { get; }

        public CommitTurnResourcesRegainedOp(TurnIdentity turn, int actions)
        {
            if (actions < 0)
                throw new ArgumentOutOfRangeException(nameof(actions));
            Turn = turn;
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

    internal sealed class CommitEncounterActionsOp : IRuleOp<EncounterActionSpendOutcome>
    {
        public CreatureId Actor { get; }
        public int Amount { get; }
        public IReadOnlyList<CreatureId> RequiredLivingTargets { get; }

        public CommitEncounterActionsOp(
            CreatureId actor,
            int amount,
            IReadOnlyList<CreatureId> requiredLivingTargets
        )
        {
            Actor = actor;
            Amount = amount;
            RequiredLivingTargets = requiredLivingTargets;
        }
    }
}
