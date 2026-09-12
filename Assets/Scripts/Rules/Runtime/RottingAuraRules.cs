using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>Describes one spatially affecting Rotting Aura source.</summary>
    public sealed class RottingAuraSource
    {
        /// <summary>Creates one source captured by the host targeting boundary.</summary>
        /// <param name="creature">The aura source's rules identity.</param>
        /// <param name="level">The source level used by Rotting Aura's damage formula.</param>
        public RottingAuraSource(CreatureId creature, int level)
        {
            if (creature.IsEmpty)
                throw new ArgumentException(
                    "An aura source creature is required.",
                    nameof(creature)
                );
            Creature = creature;
            Level = level;
        }

        /// <summary>Gets the aura source's rules identity.</summary>
        public CreatureId Creature { get; }

        /// <summary>Gets the source level captured from the unmigrated creature data boundary.</summary>
        public int Level { get; }
    }

    /// <summary>
    /// Carries the narrow immutable Unity-backed data needed to resolve one actor's Rotting Aura
    /// exposure at turn entry.
    /// </summary>
    public sealed class RottingAuraTurnData
    {
        /// <summary>Creates one captured turn-entry data set.</summary>
        /// <param name="targetTraits">The acting creature's current unmigrated traits.</param>
        /// <param name="weaknesses">The acting creature's current typed weaknesses.</param>
        /// <param name="resistances">The acting creature's current typed resistances.</param>
        /// <param name="sources">Spatially affecting sources in deterministic encounter order.</param>
        public RottingAuraTurnData(
            IEnumerable<Trait> targetTraits,
            IEnumerable<TypedDefenseAdjustment> weaknesses,
            IEnumerable<TypedDefenseAdjustment> resistances,
            IEnumerable<RottingAuraSource> sources
        )
        {
            TargetTraits = Copy(targetTraits, nameof(targetTraits));
            Weaknesses = Copy(weaknesses, nameof(weaknesses));
            Resistances = Copy(resistances, nameof(resistances));
            Sources = Copy(sources, nameof(sources));
            if (TargetTraits.Any(trait => trait.IsEmpty))
                throw new ArgumentException(
                    "Aura target traits cannot be empty.",
                    nameof(targetTraits)
                );
        }

        /// <summary>Gets the acting creature's traits.</summary>
        public IReadOnlyList<Trait> TargetTraits { get; }

        /// <summary>Gets void weaknesses used by typed damage resolution.</summary>
        public IReadOnlyList<TypedDefenseAdjustment> Weaknesses { get; }

        /// <summary>Gets void resistances used by typed damage resolution.</summary>
        public IReadOnlyList<TypedDefenseAdjustment> Resistances { get; }

        /// <summary>Gets spatially affecting sources in deterministic encounter order.</summary>
        public IReadOnlyList<RottingAuraSource> Sources { get; }

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> values, string parameterName)
        {
            T[] copied = values?.ToArray() ?? throw new ArgumentNullException(parameterName);
            if (copied.Any(value => value == null))
                throw new ArgumentException(
                    "Captured aura data cannot contain null values.",
                    parameterName
                );
            return new ReadOnlyCollection<T>(copied);
        }
    }

    /// <summary>
    /// Captures only the Unity spatial and unmigrated creature values needed by Rotting Aura.
    /// </summary>
    public interface IRottingAuraDataProvider
    {
        /// <summary>Captures the acting creature and affecting aura sources for one exact turn.</summary>
        /// <param name="snapshot">The authoritative snapshot at capture time.</param>
        /// <param name="encounter">The encounter containing the exact turn.</param>
        /// <param name="target">The acting creature that may be affected.</param>
        /// <returns>Immutable feature data in deterministic source order.</returns>
        RottingAuraTurnData Capture(
            RulesSnapshot snapshot,
            EncounterId encounter,
            CreatureId target
        );
    }

    /// <summary>
    /// Reports one Rotting Aura tick after its health operation commits.
    /// </summary>
    /// <remarks>
    /// This occurrence precedes the tick root's authoritative Fact listeners. Those listeners finish
    /// before the turn listener considers the next source. Collections are copied and read-only so
    /// observers cannot change another observer's view of the committed result.
    /// </remarks>
    public sealed class RottingAuraResolvedFact : RuleFact
    {
        /// <summary>Gets the living aura source selected for the tick.</summary>
        public CreatureId Source { get; }

        /// <summary>Gets the acting creature selected as the aura target.</summary>
        public CreatureId Target { get; }

        /// <summary>Gets the immutable dispatcher-recorded roll result.</summary>
        public RollResult Roll { get; }

        /// <summary>Gets typed damage after weakness and resistance adjustment.</summary>
        public IReadOnlyList<TypedDamagePart> Damage { get; }

        /// <summary>Gets the captured weaknesses used to resolve the damage.</summary>
        public IReadOnlyList<TypedDefenseAdjustment> Weaknesses { get; }

        /// <summary>Gets the captured resistances used to resolve the damage.</summary>
        public IReadOnlyList<TypedDefenseAdjustment> Resistances { get; }

        /// <summary>Gets the exact result committed by the shared health workflow.</summary>
        public DamageOutcome Outcome { get; }

        internal RottingAuraResolvedFact(
            CreatureId source,
            CreatureId target,
            RollResult roll,
            IReadOnlyList<TypedDamagePart> damage,
            IReadOnlyList<TypedDefenseAdjustment> weaknesses,
            IReadOnlyList<TypedDefenseAdjustment> resistances,
            DamageOutcome outcome
        )
        {
            Source = source;
            Target = target;
            Roll = roll;
            Damage = Array.AsReadOnly(damage.ToArray());
            Weaknesses = Array.AsReadOnly(weaknesses.ToArray());
            Resistances = Array.AsReadOnly(resistances.ToArray());
            Outcome = outcome;
        }
    }

    /// <summary>Applies one eligible Rotting Aura source through shared typed damage and health.</summary>
    internal sealed class ApplyRottingAuraTickOp : IRuleOp<DamageOutcome>
    {
        /// <summary>Creates one complete supporting aura-damage request.</summary>
        internal ApplyRottingAuraTickOp(
            CreatureId source,
            CreatureId target,
            int sourceLevel,
            IReadOnlyList<TypedDefenseAdjustment> weaknesses,
            IReadOnlyList<TypedDefenseAdjustment> resistances
        )
        {
            if (source.IsEmpty)
                throw new ArgumentException("An aura source is required.", nameof(source));
            if (target.IsEmpty)
                throw new ArgumentException("An aura target is required.", nameof(target));
            Source = source;
            Target = target;
            SourceLevel = sourceLevel;
            Weaknesses = weaknesses ?? throw new ArgumentNullException(nameof(weaknesses));
            Resistances = resistances ?? throw new ArgumentNullException(nameof(resistances));
        }

        /// <summary>Gets the aura source.</summary>
        internal CreatureId Source { get; }

        /// <summary>Gets the acting creature receiving damage.</summary>
        internal CreatureId Target { get; }

        /// <summary>Gets the source level used by the damage formula.</summary>
        internal int SourceLevel { get; }

        /// <summary>Gets captured void weaknesses.</summary>
        internal IReadOnlyList<TypedDefenseAdjustment> Weaknesses { get; }

        /// <summary>Gets captured void resistances.</summary>
        internal IReadOnlyList<TypedDefenseAdjustment> Resistances { get; }
    }

    /// <summary>Stages the completed feature occurrence without writing health a second time.</summary>
    internal sealed class CommitRottingAuraResolvedOp : IRuleOp<DamageOutcome>, IRuleSourcedOp
    {
        internal CommitRottingAuraResolvedOp(RottingAuraResolvedFact fact) => Fact = fact;

        internal RottingAuraResolvedFact Fact { get; }

        RuleSource IRuleSourcedOp.Source => RottingAuraRules.Source;
    }

    /// <summary>Owns Rotting Aura's binding, turn response, and damage workflow.</summary>
    public static class RottingAuraRules
    {
        /// <summary>Gets the canonical slug shared by creature data, rule sources, and visuals.</summary>
        public const string Slug = "rotting-aura";

        /// <summary>Gets the stable definition installed for every encounter combatant.</summary>
        public static RuleDefinitionId DefinitionId { get; } =
            new RuleDefinitionId("rotting-aura-turn-entry");

        /// <summary>Gets the rule source used for aura damage and active bindings.</summary>
        public static RuleSource Source { get; } = RuleSource.FromSlug(Slug);

        /// <summary>Defines the feature's normal committed-turn listener.</summary>
        /// <param name="builder">The explicit production rule registry builder.</param>
        /// <param name="data">The narrow host data and targeting boundary.</param>
        public static void DefineRuleBinding(
            RuleRegistryBuilder builder,
            IRottingAuraDataProvider data
        )
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            builder
                .Define(DefinitionId)
                .FactListener(RuleLifecyclePhase.Reaction, new TurnBeganListener(data));
        }

        /// <summary>Creates the feature binding contributed by either enrollment route.</summary>
        /// <param name="owner">The combatant whose exact turn the binding observes.</param>
        /// <returns>An enabled encounter binding for Rotting Aura turn processing.</returns>
        public static ActiveRuleBinding CreateBinding(CreatureId owner) =>
            new ActiveRuleBinding(
                new BindingId($"rotting-aura-turn-entry:{owner.Value}"),
                DefinitionId,
                owner,
                default,
                Source,
                0
            );

        private sealed class TurnBeganListener : IRuleFactListener<TurnBeganFact>
        {
            private static readonly Trait Undead = Trait.FromSlug("undead");
            private static readonly Trait Construct = Trait.FromSlug("construct");
            private readonly IRottingAuraDataProvider data;

            internal TurnBeganListener(IRottingAuraDataProvider data) => this.data = data;

            /// <inheritdoc/>
            public async ValueTask OnFactCommitted(TurnBeganFact fact, FactContext context)
            {
                if (context.Binding.Owner != fact.Turn.Actor)
                    return;
                if (!IsCurrentTurn(context.Snapshot, fact.Turn, out HealthState targetHealth))
                    return;
                if (targetHealth.Current >= targetHealth.Maximum)
                    return;

                RottingAuraTurnData turnData = data.Capture(
                    context.Snapshot,
                    fact.Turn.Encounter,
                    fact.Turn.Actor
                );
                if (
                    turnData.TargetTraits.Contains(Undead)
                    || turnData.TargetTraits.Contains(Construct)
                )
                    return;

                foreach (RottingAuraSource source in turnData.Sources)
                {
                    RulesSnapshot current = context.Snapshot;
                    if (!IsCurrentTurn(current, fact.Turn, out targetHealth))
                        return;
                    if (targetHealth.Current >= targetHealth.Maximum)
                        return;
                    if (
                        !current.Health.TryGet(source.Creature, out HealthState sourceHealth)
                        || !sourceHealth.IsLiving
                    )
                        continue;

                    OpResult<DamageOutcome> result = await context.Dispatch(
                        new ApplyRottingAuraTickOp(
                            source.Creature,
                            fact.Turn.Actor,
                            source.Level,
                            turnData.Weaknesses,
                            turnData.Resistances
                        )
                    );
                    RequireResolved(result, "Rotting Aura damage did not resolve.");
                }
            }

            private static bool IsCurrentTurn(
                RulesSnapshot snapshot,
                TurnIdentity turn,
                out HealthState targetHealth
            )
            {
                targetHealth = default;
                return snapshot.Encounters.TryGet(turn.Encounter, out EncounterState encounter)
                    && encounter.Phase == EncounterPhase.Active
                    && encounter.CurrentTurn.HasValue
                    && encounter.CurrentTurn.Value == turn
                    && snapshot.Health.TryGet(turn.Actor, out targetHealth)
                    && targetHealth.IsLiving;
            }
        }

        // Supporting Aura operations must resolve; propagate an invalid reason without inventing
        // recovery or a second outcome contract for these internal steps.
        internal static DamageOutcome RequireResolved(
            OpResult<DamageOutcome> result,
            string failureMessage
        ) =>
            result switch
            {
                ResolvedOpResult<DamageOutcome> resolved => resolved.Value,
                InvalidOpResult<DamageOutcome> invalid => throw new InvalidOperationException(
                    invalid.Reason
                ),
                _ => throw new InvalidOperationException(failureMessage),
            };
    }

    /// <summary>Registers Rotting Aura's supporting damage operation.</summary>
    public static class RottingAuraRuleDispatcherExtensions
    {
        /// <summary>Adds the feature handler that rolls and delegates authoritative health writes.</summary>
        /// <param name="builder">The encounter dispatcher builder.</param>
        /// <returns>The supplied builder for fluent composition.</returns>
        public static RuleDispatcherBuilder UseRottingAuraRules(this RuleDispatcherBuilder builder)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            return builder
                .RegisterHandler<ApplyRottingAuraTickOp, DamageOutcome>(
                    new ApplyRottingAuraTickHandler()
                )
                .RegisterReducer<CommitRottingAuraResolvedOp, DamageOutcome>(
                    new CommitRottingAuraResolvedReducer(),
                    RottingAuraRules.Source
                );
        }
    }

    internal sealed class ApplyRottingAuraTickHandler
        : IOpHandler<ApplyRottingAuraTickOp, DamageOutcome>
    {
        public async ValueTask<DamageOutcome> Handle(
            OpFrame<ApplyRottingAuraTickOp> frame,
            OpHandlerContext context
        )
        {
            int diceCount = Math.Max(1, 1 + Math.Max(0, frame.Op.SourceLevel) / 6);
            DiceExpression dice = new(diceCount, 6);
            RollResult roll = context.Rolls.Roll(dice);
            IReadOnlyList<TypedDamagePart> damage = TypedDamageResolver.Resolve(
                Array.Empty<TypedDamageDice>(),
                new[] { new TypedFlatDamage(roll.Total, "void", "Rotting Aura") },
                Array.Empty<TypedDamageDice>(),
                DegreeOfSuccess.Success,
                frame.Op.Weaknesses,
                frame.Op.Resistances,
                context.Rolls
            );
            int finalDamage = damage.Sum(part => part.Amount);
            OpResult<DamageOutcome> damageResult = await context.Dispatch(
                new ApplyDamageOp(
                    frame.Op.Target,
                    finalDamage,
                    new HealthChangeOriginId($"rotting-aura-{frame.RootId.Value}"),
                    RottingAuraRules.Source
                )
            );
            DamageOutcome outcome = RottingAuraRules.RequireResolved(
                damageResult,
                "Rotting Aura health application did not resolve."
            );

            OpResult<DamageOutcome> occurrence = await context.Dispatch(
                new CommitRottingAuraResolvedOp(
                    new RottingAuraResolvedFact(
                        frame.Op.Source,
                        frame.Op.Target,
                        roll,
                        damage,
                        frame.Op.Weaknesses,
                        frame.Op.Resistances,
                        outcome
                    )
                )
            );
            return RottingAuraRules.RequireResolved(
                occurrence,
                "Rotting Aura completion did not resolve."
            );
        }
    }

    internal sealed class CommitRottingAuraResolvedReducer
        : IOpReducer<CommitRottingAuraResolvedOp, DamageOutcome>
    {
        public ReductionResult<DamageOutcome> Reduce(
            ReductionContext<CommitRottingAuraResolvedOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            facts.Stage(context.Op.Fact);
            return ReductionResult<DamageOutcome>.Accept(context.Op.Fact.Outcome);
        }
    }
}
