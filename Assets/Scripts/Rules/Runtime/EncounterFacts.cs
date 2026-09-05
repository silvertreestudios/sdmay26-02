namespace Game.Rules.Runtime
{
    /// <summary>Reports the immutable roster and round committed for a new encounter.</summary>
    public sealed class EncounterStartedFact : RuleFact
    {
        /// <summary>Gets the committed encounter snapshot.</summary>
        public EncounterState Encounter { get; }

        /// <summary>Creates a fact for the committed encounter.</summary>
        /// <param name="encounter">The immutable state committed by encounter start.</param>
        public EncounterStartedFact(EncounterState encounter) => Encounter = encounter;
    }

    /// <summary>Reports an immutable same-store reinforcement roster replacement.</summary>
    public sealed class EncounterJoinedFact : RuleFact
    {
        /// <summary>Gets the encounter after its reinforcements joined.</summary>
        public EncounterState Encounter { get; }

        /// <summary>Creates a fact for the replaced roster.</summary>
        /// <param name="encounter">The encounter state containing all accepted additions.</param>
        public EncounterJoinedFact(EncounterState encounter) => Encounter = encounter;
    }

    /// <summary>Reports that one creature received its immutable encounter initiative slot.</summary>
    public sealed class InitiativeAssignedFact : RuleFact
    {
        /// <summary>Gets the encounter that owns the assigned slot.</summary>
        public EncounterId Encounter { get; }

        /// <summary>Gets the immutable initiative entry assigned to the creature.</summary>
        public InitiativeEntry Entry { get; }

        /// <summary>Creates a fact for one committed initiative assignment.</summary>
        /// <param name="encounter">The encounter that owns the initiative roster.</param>
        /// <param name="entry">The creature's committed immutable initiative entry.</param>
        public InitiativeAssignedFact(EncounterId encounter, InitiativeEntry entry)
        {
            if (encounter.IsEmpty)
                throw new System.ArgumentException(
                    "An encounter ID is required.",
                    nameof(encounter)
                );
            Encounter = encounter;
            Entry = entry ?? throw new System.ArgumentNullException(nameof(entry));
        }
    }

    /// <summary>Reports a source-initiative timing boundary, including zero-HP slots.</summary>
    public sealed class InitiativeBoundaryReachedFact : RuleFact
    {
        /// <summary>Gets the encounter whose cursor advanced.</summary>
        public EncounterId Encounter { get; }

        /// <summary>Gets the round containing the boundary.</summary>
        public RoundNumber Round { get; }

        /// <summary>Gets the creature whose immutable slot supplied the boundary.</summary>
        public CreatureId Creature { get; }

        /// <summary>Creates a fact for one committed initiative boundary.</summary>
        /// <param name="encounter">The encounter whose cursor advanced.</param>
        /// <param name="round">The round after any wrap was committed.</param>
        /// <param name="creature">The creature occupying the reached immutable slot.</param>
        public InitiativeBoundaryReachedFact(
            EncounterId encounter,
            RoundNumber round,
            CreatureId creature
        )
        {
            Encounter = encounter;
            Round = round;
            Creature = creature;
        }
    }

    /// <summary>Reports that final turn resources and exact turn identity were committed.</summary>
    public sealed class TurnBeganFact : RuleFact
    {
        /// <summary>Gets the exact committed turn.</summary>
        public TurnIdentity Turn { get; }

        /// <summary>Creates a fact for the exact turn.</summary>
        /// <param name="turn">The exact turn granted final resources.</param>
        public TurnBeganFact(TurnIdentity turn) => Turn = turn;
    }

    /// <summary>Reports that an exact turn completed and its scoped resources were cleared.</summary>
    public sealed class TurnEndedFact : RuleFact
    {
        /// <summary>Gets the exact completed turn.</summary>
        public TurnIdentity Turn { get; }

        /// <summary>Creates a fact for the exact completed turn.</summary>
        /// <param name="turn">The exact turn whose scoped resources were cleared.</param>
        public TurnEndedFact(TurnIdentity turn) => Turn = turn;
    }

    /// <summary>Reports that an encounter was suspended without an outcome.</summary>
    public sealed class EncounterSuspendedFact : RuleFact
    {
        /// <summary>Gets the suspended encounter.</summary>
        public EncounterId Encounter { get; }

        /// <summary>Creates a suspension fact.</summary>
        /// <param name="encounter">The encounter that stopped without an outcome.</param>
        public EncounterSuspendedFact(EncounterId encounter) => Encounter = encounter;
    }

    /// <summary>Reports the single committed protagonist-relative encounter outcome.</summary>
    public sealed class EncounterOutcomeCommittedFact : RuleFact
    {
        /// <summary>Gets the ended encounter.</summary>
        public EncounterId Encounter { get; }

        /// <summary>Gets the protagonist-relative outcome.</summary>
        public EncounterOutcome Outcome { get; }

        /// <summary>Creates an encounter outcome fact.</summary>
        /// <param name="encounter">The encounter that ended.</param>
        /// <param name="outcome">The committed player-relative result.</param>
        public EncounterOutcomeCommittedFact(EncounterId encounter, EncounterOutcome outcome)
        {
            Encounter = encounter;
            Outcome = outcome;
        }
    }

    /// <summary>Reports an authoritative encounter action spend.</summary>
    public sealed class EncounterActionsSpentFact : RuleFact
    {
        /// <summary>Gets the spending actor.</summary>
        public CreatureId Actor { get; }

        /// <summary>Gets the number of actions spent.</summary>
        public int Amount { get; }

        /// <summary>Gets the actor's committed remaining actions.</summary>
        public int Remaining { get; }

        /// <summary>Creates a fact for one committed encounter action spend.</summary>
        /// <param name="actor">The creature that spent actions.</param>
        /// <param name="amount">The positive action cost.</param>
        /// <param name="remaining">The committed remaining actions.</param>
        public EncounterActionsSpentFact(CreatureId actor, int amount, int remaining)
        {
            Actor = actor;
            Amount = amount;
            Remaining = remaining;
        }
    }
}
