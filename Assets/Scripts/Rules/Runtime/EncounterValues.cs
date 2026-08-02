using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>Identifies the committed lifecycle phase of an encounter.</summary>
    public enum EncounterPhase
    {
        /// <summary>The encounter may advance turns and accept rules work.</summary>
        Active,

        /// <summary>The encounter stopped without choosing an outcome.</summary>
        Suspended,

        /// <summary>The encounter committed one final player-relative outcome.</summary>
        Ended,
    }

    /// <summary>Identifies the protagonist-relative result of a completed encounter.</summary>
    public enum EncounterOutcome
    {
        /// <summary>At least one protagonist lives and no living opposition remains.</summary>
        PlayerVictory,

        /// <summary>No protagonist lives, including when no creature on any team lives.</summary>
        PlayerDefeat,
    }

    /// <summary>Stores a positive one-based encounter round.</summary>
    public readonly struct RoundNumber : IEquatable<RoundNumber>, IComparable<RoundNumber>
    {
        /// <summary>Gets the one-based value.</summary>
        public int Value { get; }

        /// <summary>Initializes a positive round number.</summary>
        /// <param name="value">The positive one-based round.</param>
        public RoundNumber(int value)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            Value = value;
        }

        /// <summary>Gets round one.</summary>
        public static RoundNumber First => new RoundNumber(1);

        /// <summary>Gets the next round, throwing on overflow.</summary>
        public RoundNumber Next() => new RoundNumber(checked(Value + 1));

        /// <inheritdoc/>
        public int CompareTo(RoundNumber other) => Value.CompareTo(other.Value);

        /// <inheritdoc/>
        public bool Equals(RoundNumber other) => Value == other.Value;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is RoundNumber other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => Value;

        /// <inheritdoc/>
        public override string ToString() => Value.ToString();

        /// <summary>Compares two round numbers by their one-based value.</summary>
        public static bool operator ==(RoundNumber left, RoundNumber right) => left.Equals(right);

        /// <summary>Compares two round numbers by their one-based value.</summary>
        public static bool operator !=(RoundNumber left, RoundNumber right) => !left.Equals(right);
    }

    /// <summary>Captures one creature's immutable encounter registration inputs.</summary>
    public sealed class EncounterParticipant
    {
        /// <summary>Gets the registered creature.</summary>
        public CreatureId Creature { get; }

        /// <summary>Gets the creature's team.</summary>
        public PlayerId Team { get; }

        /// <summary>Gets the initiative modifier captured before rolling.</summary>
        public int InitiativeModifier { get; }

        /// <summary>Initializes a validated participant registration.</summary>
        /// <param name="creature">The stable creature identity.</param>
        /// <param name="team">The creature's collision-safe team identity.</param>
        /// <param name="initiativeModifier">The modifier captured before initiative is rolled.</param>
        public EncounterParticipant(CreatureId creature, PlayerId team, int initiativeModifier)
        {
            if (creature.IsEmpty)
                throw new ArgumentException("A creature is required.", nameof(creature));
            if (team.IsEmpty)
                throw new ArgumentException("A team is required.", nameof(team));
            Creature = creature;
            Team = team;
            InitiativeModifier = initiativeModifier;
        }
    }

    /// <summary>Captures a reinforcement and its health when it joins the same encounter store.</summary>
    public sealed class EncounterJoinParticipant
    {
        /// <summary>Gets the captured identity, team, and initiative modifier.</summary>
        public EncounterParticipant Participant { get; }

        /// <summary>Gets the health seeded atomically when the creature is new to the store.</summary>
        public HealthState InitialHealth { get; }

        /// <summary>Gets the complete same-store rules registration.</summary>
        public CombatantRulesState Combatant { get; }

        /// <summary>Creates a complete same-store reinforcement registration.</summary>
        /// <param name="participant">The reinforcement's identity and initiative inputs.</param>
        /// <param name="initialHealth">The health to seed if the creature is new to the store.</param>
        public EncounterJoinParticipant(EncounterParticipant participant, HealthState initialHealth)
        {
            Participant = participant ?? throw new ArgumentNullException(nameof(participant));
            InitialHealth = initialHealth;
            Combatant = new CombatantRulesState(
                new CreatureState(participant.Creature, participant.Team),
                initialHealth,
                new GridPosition(0, 0, 0),
                new GridDistance(0),
                new CreatureStatisticsState(
                    participant.Creature,
                    0,
                    0,
                    0,
                    0,
                    0,
                    new Dictionary<Skill, int>(),
                    Array.Empty<Modifier>()
                ),
                PreparedCreatureInputs.Empty,
                Array.Empty<SpellSlotState>(),
                Array.Empty<ActiveRuleBinding>()
            );
        }

        /// <summary>Creates a reinforcement from its complete same-store registration.</summary>
        public EncounterJoinParticipant(
            EncounterParticipant participant,
            CombatantRulesState combatant
        )
        {
            Participant = participant ?? throw new ArgumentNullException(nameof(participant));
            Combatant = combatant ?? throw new ArgumentNullException(nameof(combatant));
            if (
                combatant.Creature.Id != participant.Creature
                || combatant.Creature.Player != participant.Team
            )
                throw new ArgumentException(
                    "The reinforcement registration must match its initiative participant.",
                    nameof(combatant)
                );
            InitialHealth = combatant.Health;
        }
    }

    /// <summary>Stores one deterministic initiative entry for the encounter lifetime.</summary>
    public sealed class InitiativeEntry : IEquatable<InitiativeEntry>
    {
        /// <summary>Gets the creature occupying this immutable timing slot.</summary>
        public CreatureId Creature { get; }

        /// <summary>Gets the creature's captured team.</summary>
        public PlayerId Team { get; }

        /// <summary>Gets the injected natural d20 result.</summary>
        public int NaturalRoll { get; }

        /// <summary>Gets the captured initiative modifier.</summary>
        public int Modifier { get; }

        /// <summary>Gets the checked natural roll plus modifier.</summary>
        public int Total { get; }

        /// <summary>Gets the deterministic encounter registration tie breaker.</summary>
        public long RegistrationOrder { get; }

        /// <summary>Gets the first round in which this slot may receive a turn.</summary>
        public RoundNumber EligibleFromRound { get; }

        /// <summary>Creates a validated immutable initiative entry.</summary>
        /// <param name="creature">The creature occupying the slot.</param>
        /// <param name="team">The creature's captured team.</param>
        /// <param name="naturalRoll">The injected natural d20 result from 1 through 20.</param>
        /// <param name="modifier">The captured initiative modifier.</param>
        /// <param name="registrationOrder">The zero-based deterministic tie breaker.</param>
        /// <param name="eligibleFromRound">The first round in which the slot may take a turn.</param>
        public InitiativeEntry(
            CreatureId creature,
            PlayerId team,
            int naturalRoll,
            int modifier,
            long registrationOrder,
            RoundNumber eligibleFromRound
        )
        {
            if (creature.IsEmpty || team.IsEmpty)
                throw new ArgumentException("Initiative entries require creature and team IDs.");
            if (naturalRoll < 1 || naturalRoll > 20)
                throw new ArgumentOutOfRangeException(nameof(naturalRoll));
            if (registrationOrder < 0)
                throw new ArgumentOutOfRangeException(nameof(registrationOrder));
            Creature = creature;
            Team = team;
            NaturalRoll = naturalRoll;
            Modifier = modifier;
            Total = checked(naturalRoll + modifier);
            RegistrationOrder = registrationOrder;
            EligibleFromRound = eligibleFromRound;
        }

        /// <inheritdoc/>
        public bool Equals(InitiativeEntry other) =>
            other != null
            && Creature == other.Creature
            && Team == other.Team
            && NaturalRoll == other.NaturalRoll
            && Modifier == other.Modifier
            && RegistrationOrder == other.RegistrationOrder
            && EligibleFromRound == other.EligibleFromRound;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is InitiativeEntry other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            HashCode.Combine(
                Creature,
                Team,
                NaturalRoll,
                Modifier,
                RegistrationOrder,
                EligibleFromRound
            );
    }

    /// <summary>Identifies the exact actor and slot for one committed turn.</summary>
    public readonly struct TurnIdentity : IEquatable<TurnIdentity>
    {
        /// <summary>Gets the encounter owning the turn.</summary>
        public EncounterId Encounter { get; }

        /// <summary>Gets the unique turn sequence identity.</summary>
        public TurnId Turn { get; }

        /// <summary>Gets the exact actor.</summary>
        public CreatureId Actor { get; }

        /// <summary>Gets the round in which the turn began.</summary>
        public RoundNumber Round { get; }

        /// <summary>Gets the actor's immutable roster position at turn creation.</summary>
        public int RosterIndex { get; }

        /// <summary>Creates a complete exact turn identity.</summary>
        /// <param name="encounter">The encounter that owns the turn.</param>
        /// <param name="turn">The positive encounter-local turn sequence.</param>
        /// <param name="actor">The creature granted turn authority.</param>
        /// <param name="round">The round in which authority was granted.</param>
        /// <param name="rosterIndex">The actor's roster index when the turn began.</param>
        public TurnIdentity(
            EncounterId encounter,
            TurnId turn,
            CreatureId actor,
            RoundNumber round,
            int rosterIndex
        )
        {
            if (encounter.IsEmpty || turn.IsEmpty || actor.IsEmpty)
                throw new ArgumentException(
                    "A turn identity requires encounter, turn, and actor IDs."
                );
            if (rosterIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(rosterIndex));
            Encounter = encounter;
            Turn = turn;
            Actor = actor;
            Round = round;
            RosterIndex = rosterIndex;
        }

        /// <inheritdoc/>
        public bool Equals(TurnIdentity other) =>
            Encounter == other.Encounter
            && Turn == other.Turn
            && Actor == other.Actor
            && Round == other.Round
            && RosterIndex == other.RosterIndex;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is TurnIdentity other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            HashCode.Combine(Encounter, Turn, Actor, Round, RosterIndex);

        /// <summary>Compares every exact-turn identity component.</summary>
        public static bool operator ==(TurnIdentity left, TurnIdentity right) => left.Equals(right);

        /// <summary>Compares every exact-turn identity component.</summary>
        public static bool operator !=(TurnIdentity left, TurnIdentity right) =>
            !left.Equals(right);
    }

    /// <summary>Stores the authoritative encounter clock and immutable initiative roster.</summary>
    public sealed class EncounterState : IEquatable<EncounterState>
    {
        private readonly IReadOnlyList<InitiativeEntry> roster;
        private readonly IReadOnlyDictionary<
            CreatureId,
            CombatantRulesState
        > reinforcementRegistrations;
        private readonly IReadOnlyList<CreatureId> publishedInitiativeAssignments;

        /// <summary>Gets the stable encounter identity.</summary>
        public EncounterId Id { get; }

        /// <summary>Gets the committed lifecycle phase.</summary>
        public EncounterPhase Phase { get; }

        /// <summary>Gets the protagonist team used for player-relative outcomes.</summary>
        public PlayerId ProtagonistTeam { get; }

        /// <summary>Gets the authoritative one-based round.</summary>
        public RoundNumber Round { get; }

        /// <summary>Gets the immutable roster, including defeated slots.</summary>
        public IReadOnlyList<InitiativeEntry> Roster => roster;

        /// <summary>Gets the most recently reached roster position, or -1 before the first boundary.</summary>
        public int Cursor { get; }

        /// <summary>Gets the exact live turn when one is open.</summary>
        public TurnIdentity? CurrentTurn { get; }

        /// <summary>Gets the next deterministic turn sequence number.</summary>
        public long NextTurnSequence { get; }

        /// <summary>Gets the committed outcome after the encounter ends.</summary>
        public EncounterOutcome? Outcome { get; }

        /// <summary>
        /// Gets whether the current initiative boundary is committed but cannot publish turn
        /// authority until all effects due at that boundary finish expiring.
        /// </summary>
        public bool IsInitiativeBoundaryPending { get; }

        /// <summary>
        /// Gets whether the current round and cursor identify a published initiative boundary
        /// whose exact actor turn has not begun.
        /// </summary>
        /// <remarks>
        /// This durable checkpoint distinguishes retrying the already published boundary from
        /// ordinary advancement, which would consume the next roster slot.
        /// </remarks>
        public bool IsTurnStartPending { get; }

        /// <summary>
        /// Gets the durable progress of ordered turn-start adapters for the published boundary.
        /// </summary>
        /// <remarks>
        /// This is present exactly while <see cref="IsTurnStartPending"/> is <see langword="true"/>.
        /// It lets a fresh dispatcher root resume after the most recently committed adapter instead
        /// of replaying adapters that have already completed their external work.
        /// </remarks>
        public TurnStartAdapterProgress TurnStartAdapterProgress { get; }

        internal IReadOnlyDictionary<CreatureId, CombatantRulesState> ReinforcementRegistrations =>
            reinforcementRegistrations;

        internal IReadOnlyList<CreatureId> PublishedInitiativeAssignments =>
            publishedInitiativeAssignments;

        /// <summary>
        /// Determines whether a reinforcement registration committed for the supplied creature.
        /// </summary>
        /// <param name="creature">The prospective reinforcement identity.</param>
        /// <returns>
        /// <see langword="true"/> when the encounter contains the committed registration;
        /// otherwise, <see langword="false"/>.
        /// </returns>
        public bool HasReinforcementRegistration(CreatureId creature) =>
            reinforcementRegistrations.ContainsKey(creature);

        /// <summary>Creates a validated immutable encounter snapshot.</summary>
        /// <param name="id">The stable encounter identity.</param>
        /// <param name="phase">The committed lifecycle phase.</param>
        /// <param name="protagonistTeam">The team used for player-relative outcomes.</param>
        /// <param name="round">The current positive round.</param>
        /// <param name="roster">The immutable roster, including defeated entries.</param>
        /// <param name="cursor">The reached roster index, or -1 before the first boundary.</param>
        /// <param name="currentTurn">The exact open turn, when one exists.</param>
        /// <param name="nextTurnSequence">The next positive turn sequence.</param>
        /// <param name="outcome">The committed result for an ended encounter.</param>
        /// <param name="isInitiativeBoundaryPending">
        /// Whether <paramref name="cursor"/> identifies a boundary whose due effects must settle
        /// before its <see cref="InitiativeBoundaryReachedFact"/> can publish.
        /// </param>
        /// <param name="isTurnStartPending">
        /// Whether <paramref name="round"/> and <paramref name="cursor"/> identify an already
        /// published boundary whose exact actor turn has not begun.
        /// </param>
        public EncounterState(
            EncounterId id,
            EncounterPhase phase,
            PlayerId protagonistTeam,
            RoundNumber round,
            IEnumerable<InitiativeEntry> roster,
            int cursor,
            TurnIdentity? currentTurn,
            long nextTurnSequence,
            EncounterOutcome? outcome,
            bool isInitiativeBoundaryPending = false,
            bool isTurnStartPending = false,
            TurnStartAdapterProgress turnStartAdapterProgress = null
        )
            : this(
                id,
                phase,
                protagonistTeam,
                round,
                roster,
                cursor,
                currentTurn,
                nextTurnSequence,
                outcome,
                isInitiativeBoundaryPending,
                isTurnStartPending,
                turnStartAdapterProgress,
                Array.Empty<KeyValuePair<CreatureId, CombatantRulesState>>(),
                Array.Empty<CreatureId>()
            ) { }

        private EncounterState(
            EncounterId id,
            EncounterPhase phase,
            PlayerId protagonistTeam,
            RoundNumber round,
            IEnumerable<InitiativeEntry> roster,
            int cursor,
            TurnIdentity? currentTurn,
            long nextTurnSequence,
            EncounterOutcome? outcome,
            bool isInitiativeBoundaryPending,
            bool isTurnStartPending,
            TurnStartAdapterProgress turnStartAdapterProgress,
            IEnumerable<KeyValuePair<CreatureId, CombatantRulesState>> reinforcementRegistrations,
            IEnumerable<CreatureId> publishedInitiativeAssignments
        )
        {
            if (id.IsEmpty || protagonistTeam.IsEmpty)
                throw new ArgumentException(
                    "Encounter state requires encounter and protagonist team IDs."
                );
            if (!Enum.IsDefined(typeof(EncounterPhase), phase))
                throw new ArgumentOutOfRangeException(nameof(phase));
            InitiativeEntry[] copied =
                roster?.ToArray() ?? throw new ArgumentNullException(nameof(roster));
            if (copied.Length == 0 || copied.Any(entry => entry == null))
                throw new ArgumentException("An encounter roster cannot be empty.", nameof(roster));
            if (cursor < -1 || cursor >= copied.Length)
                throw new ArgumentOutOfRangeException(nameof(cursor));
            if (nextTurnSequence <= 0)
                throw new ArgumentOutOfRangeException(nameof(nextTurnSequence));
            if (
                isInitiativeBoundaryPending
                && (
                    phase != EncounterPhase.Active
                    || cursor < 0
                    || currentTurn.HasValue
                    || isTurnStartPending
                )
            )
                throw new ArgumentException(
                    "A pending initiative boundary requires an active encounter, a reached cursor, and no open turn.",
                    nameof(isInitiativeBoundaryPending)
                );
            if (
                isTurnStartPending
                && (
                    phase != EncounterPhase.Active
                    || cursor < 0
                    || currentTurn.HasValue
                    || isInitiativeBoundaryPending
                )
            )
                throw new ArgumentException(
                    "A pending turn start requires an active encounter, one published boundary, and no open turn.",
                    nameof(isTurnStartPending)
                );
            if (isTurnStartPending && turnStartAdapterProgress == null)
                turnStartAdapterProgress = TurnStartAdapterProgress.Initial;
            if (!isTurnStartPending && turnStartAdapterProgress != null)
                throw new ArgumentException(
                    "Turn-start adapter progress requires a published turn-start checkpoint.",
                    nameof(turnStartAdapterProgress)
                );
            Dictionary<CreatureId, CombatantRulesState> copiedRegistrations =
                reinforcementRegistrations?.ToDictionary(pair => pair.Key, pair => pair.Value)
                ?? throw new ArgumentNullException(nameof(reinforcementRegistrations));
            if (
                copiedRegistrations.Any(pair =>
                    pair.Key.IsEmpty
                    || pair.Value == null
                    || pair.Value.Creature.Id != pair.Key
                    || copied.All(entry => entry.Creature != pair.Key)
                )
            )
                throw new ArgumentException(
                    "Every reinforcement receipt must identify a creature in the encounter roster.",
                    nameof(reinforcementRegistrations)
                );
            CreatureId[] copiedAssignments =
                publishedInitiativeAssignments?.ToArray()
                ?? throw new ArgumentNullException(nameof(publishedInitiativeAssignments));
            if (
                copiedAssignments.Any(creature =>
                    creature.IsEmpty || copied.All(entry => entry.Creature != creature)
                )
                || copiedAssignments.Distinct().Count() != copiedAssignments.Length
            )
                throw new ArgumentException(
                    "Published initiative assignments must identify unique roster creatures.",
                    nameof(publishedInitiativeAssignments)
                );
            Id = id;
            Phase = phase;
            ProtagonistTeam = protagonistTeam;
            Round = round;
            this.roster = Array.AsReadOnly(copied);
            Cursor = cursor;
            CurrentTurn = currentTurn;
            NextTurnSequence = nextTurnSequence;
            Outcome = outcome;
            IsInitiativeBoundaryPending = isInitiativeBoundaryPending;
            IsTurnStartPending = isTurnStartPending;
            TurnStartAdapterProgress = turnStartAdapterProgress;
            this.reinforcementRegistrations = new ReadOnlyDictionary<
                CreatureId,
                CombatantRulesState
            >(copiedRegistrations);
            this.publishedInitiativeAssignments = Array.AsReadOnly(copiedAssignments);
        }

        internal EncounterState Replace(
            EncounterPhase? phase = null,
            RoundNumber? round = null,
            IEnumerable<InitiativeEntry> roster = null,
            int? cursor = null,
            TurnIdentity? currentTurn = null,
            bool clearCurrentTurn = false,
            long? nextTurnSequence = null,
            EncounterOutcome? outcome = null,
            bool? isInitiativeBoundaryPending = null,
            bool? isTurnStartPending = null,
            TurnStartAdapterProgress turnStartAdapterProgress = null,
            bool clearTurnStartAdapterProgress = false,
            IEnumerable<KeyValuePair<CreatureId, CombatantRulesState>> reinforcementRegistrations =
                null,
            IEnumerable<CreatureId> publishedInitiativeAssignments = null
        )
        {
            bool updatedTurnStartPending = isTurnStartPending ?? IsTurnStartPending;
            TurnStartAdapterProgress updatedProgress =
                !updatedTurnStartPending || clearTurnStartAdapterProgress
                    ? null
                    : turnStartAdapterProgress ?? TurnStartAdapterProgress;
            return new EncounterState(
                Id,
                phase ?? Phase,
                ProtagonistTeam,
                round ?? Round,
                roster ?? this.roster,
                cursor ?? Cursor,
                clearCurrentTurn ? (TurnIdentity?)null : currentTurn ?? CurrentTurn,
                nextTurnSequence ?? NextTurnSequence,
                outcome ?? Outcome,
                isInitiativeBoundaryPending ?? IsInitiativeBoundaryPending,
                updatedTurnStartPending,
                updatedProgress,
                reinforcementRegistrations ?? this.reinforcementRegistrations,
                publishedInitiativeAssignments ?? this.publishedInitiativeAssignments
            );
        }

        /// <inheritdoc/>
        public bool Equals(EncounterState other) =>
            other != null
            && Id == other.Id
            && Phase == other.Phase
            && ProtagonistTeam == other.ProtagonistTeam
            && Round == other.Round
            && Cursor == other.Cursor
            && CurrentTurn == other.CurrentTurn
            && NextTurnSequence == other.NextTurnSequence
            && Outcome == other.Outcome
            && IsInitiativeBoundaryPending == other.IsInitiativeBoundaryPending
            && IsTurnStartPending == other.IsTurnStartPending
            && Equals(TurnStartAdapterProgress, other.TurnStartAdapterProgress)
            && roster.SequenceEqual(other.roster)
            && reinforcementRegistrations.Count == other.reinforcementRegistrations.Count
            && reinforcementRegistrations.All(pair =>
                other.reinforcementRegistrations.TryGetValue(pair.Key, out var registration)
                && pair.Value.Equals(registration)
            )
            && publishedInitiativeAssignments.SequenceEqual(other.publishedInitiativeAssignments);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is EncounterState other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(Id);
            hash.Add(Phase);
            hash.Add(ProtagonistTeam);
            hash.Add(Round);
            hash.Add(Cursor);
            hash.Add(CurrentTurn);
            hash.Add(NextTurnSequence);
            hash.Add(Outcome);
            hash.Add(IsInitiativeBoundaryPending);
            hash.Add(IsTurnStartPending);
            hash.Add(TurnStartAdapterProgress);
            foreach (InitiativeEntry entry in roster)
                hash.Add(entry);
            foreach (
                KeyValuePair<
                    CreatureId,
                    CombatantRulesState
                > registration in reinforcementRegistrations.OrderBy(
                    pair => pair.Key.Value,
                    StringComparer.Ordinal
                )
            )
            {
                hash.Add(registration.Key);
                hash.Add(registration.Value);
            }
            foreach (CreatureId assignment in publishedInitiativeAssignments)
                hash.Add(assignment);
            return hash.ToHashCode();
        }
    }

    /// <summary>
    /// Identifies the next ordered adapter and its current contribution at one published turn start.
    /// </summary>
    /// <remarks>
    /// The enclosing <see cref="EncounterState"/> supplies the exact encounter, round, roster slot,
    /// and actor. This value carries only the portion that changes after each successfully returned
    /// adapter.
    /// </remarks>
    public sealed class TurnStartAdapterProgress : IEquatable<TurnStartAdapterProgress>
    {
        /// <summary>Gets the first adapter index with work that has not committed.</summary>
        public int NextAdapterIndex { get; }

        /// <summary>Gets the contribution returned by the immediately preceding adapter.</summary>
        public TurnStartContribution Contribution { get; }

        /// <summary>Gets the initial standard contribution before any adapter runs.</summary>
        public static TurnStartAdapterProgress Initial =>
            new TurnStartAdapterProgress(0, TurnStartContribution.Standard);

        /// <summary>Creates immutable validated turn-start adapter progress.</summary>
        /// <param name="nextAdapterIndex">The non-negative next adapter index.</param>
        /// <param name="contribution">The current action contribution.</param>
        public TurnStartAdapterProgress(int nextAdapterIndex, TurnStartContribution contribution)
        {
            if (nextAdapterIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(nextAdapterIndex));
            NextAdapterIndex = nextAdapterIndex;
            Contribution = contribution;
        }

        /// <inheritdoc/>
        public bool Equals(TurnStartAdapterProgress other) =>
            other != null
            && NextAdapterIndex == other.NextAdapterIndex
            && Contribution.Actions == other.Contribution.Actions
            && Contribution.ReactionAvailable == other.Contribution.ReactionAvailable;

        /// <inheritdoc/>
        public override bool Equals(object obj) =>
            obj is TurnStartAdapterProgress other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            HashCode.Combine(
                NextAdapterIndex,
                Contribution.Actions,
                Contribution.ReactionAvailable
            );
    }

    /// <summary>
    /// Schedules one active effect against its source's initiative boundaries in one encounter.
    /// </summary>
    public sealed class ActiveEffectTimingState : IEquatable<ActiveEffectTimingState>
    {
        /// <summary>Gets the scheduled effect instance.</summary>
        public ActiveEffectId Effect { get; }

        /// <summary>Gets the encounter supplying its clock.</summary>
        public EncounterId Encounter { get; }

        /// <summary>Gets the binding disabled by automatic expiry.</summary>
        public BindingId Binding { get; }

        /// <summary>Gets the creature whose initiative boundaries count down the effect.</summary>
        public CreatureId SourceCreature { get; }

        /// <summary>Gets the remaining future source boundaries.</summary>
        public int RemainingBoundaries { get; }

        /// <summary>
        /// Gets whether the source duration was encounter-scoped instead of boundary-counted.
        /// All finite timings retire when their owning encounter closes because no later encounter
        /// can advance that timing identity.
        /// </summary>
        public bool ExpiresWithEncounter { get; }

        /// <summary>Gets the deterministic simultaneous-expiry ordering key.</summary>
        public long CreationOrder { get; }

        /// <summary>Creates a validated active-effect timing schedule.</summary>
        /// <param name="effect">The scheduled effect instance.</param>
        /// <param name="encounter">The encounter supplying initiative boundaries.</param>
        /// <param name="binding">The binding disabled when automatic expiry commits.</param>
        /// <param name="sourceCreature">The creature whose future boundaries count down.</param>
        /// <param name="remainingBoundaries">The non-negative boundaries remaining.</param>
        /// <param name="expiresWithEncounter">
        /// Whether the source duration was encounter-scoped instead of boundary-counted.
        /// </param>
        /// <param name="creationOrder">The deterministic simultaneous-expiry order.</param>
        public ActiveEffectTimingState(
            ActiveEffectId effect,
            EncounterId encounter,
            BindingId binding,
            CreatureId sourceCreature,
            int remainingBoundaries,
            bool expiresWithEncounter,
            long creationOrder
        )
        {
            if (effect.IsEmpty || encounter.IsEmpty || binding.IsEmpty || sourceCreature.IsEmpty)
                throw new ArgumentException("Effect timing requires complete stable identity.");
            if (remainingBoundaries < 0 || creationOrder < 0)
                throw new ArgumentOutOfRangeException();
            Effect = effect;
            Encounter = encounter;
            Binding = binding;
            SourceCreature = sourceCreature;
            RemainingBoundaries = remainingBoundaries;
            ExpiresWithEncounter = expiresWithEncounter;
            CreationOrder = creationOrder;
        }

        internal static ActiveEffectTimingState ForEncounter(
            ActiveEffectInstance effect,
            ActiveRuleBinding binding,
            EncounterState encounter
        )
        {
            int boundaries =
                effect.Duration.Kind == EffectDurationKind.Rounds ? effect.Duration.Amount
                : effect.Duration.Kind == EffectDurationKind.Minutes
                    ? checked(effect.Duration.Amount * 10)
                : 0;
            return new ActiveEffectTimingState(
                effect.Id,
                encounter.Id,
                binding.Id,
                effect.SourceCreature,
                boundaries,
                effect.Duration.Kind == EffectDurationKind.Encounter,
                binding.CreationOrder
            );
        }

        internal ActiveEffectTimingState WithRemaining(int remaining) =>
            new ActiveEffectTimingState(
                Effect,
                Encounter,
                Binding,
                SourceCreature,
                remaining,
                ExpiresWithEncounter,
                CreationOrder
            );

        /// <inheritdoc/>
        public bool Equals(ActiveEffectTimingState other) =>
            other != null
            && Effect == other.Effect
            && Encounter == other.Encounter
            && Binding == other.Binding
            && SourceCreature == other.SourceCreature
            && RemainingBoundaries == other.RemainingBoundaries
            && ExpiresWithEncounter == other.ExpiresWithEncounter
            && CreationOrder == other.CreationOrder;

        /// <inheritdoc/>
        public override bool Equals(object obj) =>
            obj is ActiveEffectTimingState other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            HashCode.Combine(
                Effect,
                Encounter,
                Binding,
                SourceCreature,
                RemainingBoundaries,
                ExpiresWithEncounter,
                CreationOrder
            );
    }
}
