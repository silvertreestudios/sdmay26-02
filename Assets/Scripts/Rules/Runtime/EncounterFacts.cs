namespace Game.Rules.Runtime
{
    /// <summary>Reports the immutable roster and round committed for a new encounter.</summary>
    public sealed class EncounterStartedFact : RuleFact
    {
        /// <summary>Gets the committed encounter snapshot.</summary>
        public EncounterState Encounter { get; }

        /// <summary>Creates a fact for the committed encounter.</summary>
        public EncounterStartedFact(EncounterState encounter) => Encounter = encounter;
    }

    /// <summary>Reports an immutable same-store reinforcement roster replacement.</summary>
    public sealed class EncounterJoinedFact : RuleFact
    {
        /// <summary>Gets the encounter after its reinforcements joined.</summary>
        public EncounterState Encounter { get; }

        /// <summary>Creates a fact for the replaced roster.</summary>
        public EncounterJoinedFact(EncounterState encounter) => Encounter = encounter;
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
        public TurnBeganFact(TurnIdentity turn) => Turn = turn;
    }

    /// <summary>Reports that an exact turn completed and its scoped resources were cleared.</summary>
    public sealed class TurnEndedFact : RuleFact
    {
        /// <summary>Gets the exact completed turn.</summary>
        public TurnIdentity Turn { get; }

        /// <summary>Creates a fact for the exact completed turn.</summary>
        public TurnEndedFact(TurnIdentity turn) => Turn = turn;
    }

    /// <summary>Reports that an encounter was suspended without an outcome.</summary>
    public sealed class EncounterSuspendedFact : RuleFact
    {
        /// <summary>Gets the suspended encounter.</summary>
        public EncounterId Encounter { get; }

        /// <summary>Creates a suspension fact.</summary>
        public EncounterSuspendedFact(EncounterId encounter) => Encounter = encounter;
    }

    /// <summary>Reports the single committed protagonist-relative encounter outcome.</summary>
    public sealed class EncounterEndedFact : RuleFact
    {
        /// <summary>Gets the ended encounter.</summary>
        public EncounterId Encounter { get; }

        /// <summary>Gets the protagonist-relative outcome.</summary>
        public EncounterOutcome Outcome { get; }

        /// <summary>Creates an encounter outcome fact.</summary>
        public EncounterEndedFact(EncounterId encounter, EncounterOutcome outcome)
        {
            Encounter = encounter;
            Outcome = outcome;
        }
    }

    /// <summary>Reports an action spend made through a narrow same-store migration port.</summary>
    public sealed class LegacyActionsSpentFact : RuleFact
    {
        /// <summary>Gets the spending actor.</summary>
        public CreatureId Actor { get; }

        /// <summary>Gets the number of actions spent.</summary>
        public int Amount { get; }

        /// <summary>Gets the actor's committed remaining actions.</summary>
        public int Remaining { get; }

        /// <summary>Creates a fact for one committed legacy action spend.</summary>
        public LegacyActionsSpentFact(CreatureId actor, int amount, int remaining)
        {
            Actor = actor;
            Amount = amount;
            Remaining = remaining;
        }
    }

    /// <summary>Reports a MAP increment made through a narrow same-store migration port.</summary>
    public sealed class LegacyMapIncrementedFact : RuleFact
    {
        /// <summary>Gets the attacking actor.</summary>
        public CreatureId Actor { get; }

        /// <summary>Gets the committed attack count.</summary>
        public int AttackCount { get; }

        /// <summary>Creates a fact for one committed legacy MAP increment.</summary>
        public LegacyMapIncrementedFact(CreatureId actor, int attackCount)
        {
            Actor = actor;
            AttackCount = attackCount;
        }
    }
}
