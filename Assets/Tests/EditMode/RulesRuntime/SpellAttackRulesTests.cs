using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    public sealed class SpellAttackRulesTests
    {
        private static readonly CreatureId Actor = new("spell-attacker");
        private static readonly CreatureId Target = new("spell-target");
        private static readonly CreatureId DeadTarget = new("dead-spell-target");
        private static readonly CreatureId UnknownTarget = new("unknown-spell-target");
        private static readonly PlayerId Players = new("players");
        private static readonly PlayerId Enemies = new("enemies");
        private static readonly SpellReference DivineLance = new(new SpellId("divine-lance"), 1);
        private static readonly SpellActionVariant TwoActions = new(2);

        [Test]
        public async Task SpellAttackRejectsMissingMapBeforeActionCostsCommit()
        {
            InMemoryRulesStore store = CreateStore(seedMap: false);
            RuleDispatcher dispatcher = CreateDispatcher(
                store,
                new TestResolutionDataProvider(Data(15), ActionValidationResult.Valid),
                new ScriptedRollService(20, 4, 4)
            );
            RulesSnapshot before = store.Snapshot;

            OpResult<CastSpellOutcome> result = await dispatcher.Dispatch(Cast(Target));

            Assert.That(result, Is.TypeOf<InvalidOpResult<CastSpellOutcome>>());
            Assert.That(
                ((InvalidOpResult<CastSpellOutcome>)result).Reason,
                Does.Contain("multiple-attack-penalty")
            );
            Assert.That(store.Snapshot.Version, Is.EqualTo(before.Version));
            Assert.That(store.Snapshot.Health[Target], Is.EqualTo(before.Health[Target]));
            Assert.That(
                store.Snapshot.ActionEconomy[Actor],
                Is.EqualTo(before.ActionEconomy[Actor])
            );
        }

        private static readonly RuleDefinitionId InterruptionDefinition = new(
            "spell-attack-interruption"
        );

        [Test]
        public async Task HitAppliesTypedDamageWithProvenanceAndAdvancesMap()
        {
            TestResolutionDataProvider provider = new(
                Data(armorClass: 15),
                ActionValidationResult.Valid
            );
            InMemoryRulesStore store = CreateStore();
            RuleDispatcher dispatcher = CreateDispatcher(
                store,
                provider,
                new ScriptedRollService(10, 2, 3)
            );

            var dispatch = await DispatchResolvedAttack(dispatcher, Cast(Target));
            ResolvedOpResult<CastSpellOutcome> result = dispatch.Cast;

            SpellAttackResolution attack = dispatch.Attack;
            Assert.That(dispatch.Check.Operation.Attacker, Is.EqualTo(Actor));
            Assert.That(dispatch.Check.Operation.Target, Is.EqualTo(Target));
            Assert.That(dispatch.Check.Outcome.Roll, Is.SameAs(attack.AttackRoll));
            Assert.That(dispatch.Check.Outcome.Modifiers.Total, Is.EqualTo(attack.AttackModifier));
            Assert.That(dispatch.Check.Outcome.Degree, Is.EqualTo(attack.Degree));
            Assert.That(attack.AttackRoll.Total + attack.AttackModifier, Is.EqualTo(17));
            Assert.That(attack.Degree, Is.EqualTo(DegreeOfSuccess.Success));
            Assert.That(attack.Damage.Single().DamageType, Is.EqualTo("spirit"));
            Assert.That(attack.Damage.Single().Sources, Does.Contain("divine-lance"));
            Assert.That(attack.FinalDamage, Is.EqualTo(5));
            Assert.That(store.Snapshot.Health[Target].Current, Is.EqualTo(25));
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(1));
            Assert.That(store.Snapshot.MultipleAttackPenalty[Actor].AttackCount, Is.EqualTo(1));
            DamageAppliedFact damage = result.Facts.OfType<DamageAppliedFact>().Single();
            Assert.That(damage.Creature, Is.EqualTo(Target));
            Assert.That(damage.Applied, Is.EqualTo(5));
            Assert.That(damage.Source, Is.EqualTo(RuleSource.FromSlug("divine-lance")));
            Assert.That(damage.Origin.Value, Does.StartWith("spell-"));
            Assert.That(
                result.Facts.OfType<MultipleAttackPenaltyAdvancedFact>().Single().AttackCount,
                Is.EqualTo(1)
            );
        }

        [Test]
        public async Task CriticalHitDoublesRolledDamageBeforeWeaknessAndResistance()
        {
            TestResolutionDataProvider provider = new(
                Data(
                    armorClass: 15,
                    weaknesses: new[] { new TypedDefenseAdjustment("spirit", 3) },
                    resistances: new[] { new TypedDefenseAdjustment("spirit", 2) }
                ),
                ActionValidationResult.Valid
            );
            InMemoryRulesStore store = CreateStore();
            RuleDispatcher dispatcher = CreateDispatcher(
                store,
                provider,
                new ScriptedRollService(18, 2, 3)
            );

            SpellAttackResolution attack = (
                await DispatchResolvedAttack(dispatcher, Cast(Target))
            ).Attack;

            Assert.That(attack.Degree, Is.EqualTo(DegreeOfSuccess.CriticalSuccess));
            Assert.That(attack.FinalDamage, Is.EqualTo(11));
            Assert.That(store.Snapshot.Health[Target].Current, Is.EqualTo(19));
        }

        [Test]
        public async Task MissDealsNoDamageButAdvancesSharedMap()
        {
            InMemoryRulesStore store = CreateStore();
            RuleDispatcher dispatcher = CreateDispatcher(
                store,
                new TestResolutionDataProvider(Data(20), ActionValidationResult.Valid),
                new ScriptedRollService(2)
            );

            var dispatch = await DispatchResolvedAttack(dispatcher, Cast(Target));
            ResolvedOpResult<CastSpellOutcome> result = dispatch.Cast;

            Assert.That(dispatch.Attack.Hit, Is.False);
            Assert.That(store.Snapshot.Health[Target].Current, Is.EqualTo(30));
            Assert.That(result.Facts.OfType<DamageAppliedFact>(), Is.Empty);
            Assert.That(store.Snapshot.MultipleAttackPenalty[Actor].AttackCount, Is.EqualTo(1));
        }

        [Test]
        public async Task AttackCombinesPreparedSnapshotUnityAndRulesOwnedMapModifiers()
        {
            Modifier snapshotModifier = Modifier.StatusBonus(
                1,
                RuleSource.FromSlug("snapshot-status"),
                Statistic.AttackRoll
            );
            Modifier unityModifier = new(
                2,
                ModifierType.Circumstance,
                RuleSource.FromSlug("unity-circumstance"),
                Statistic.AttackRoll
            );
            InMemoryRulesStore store = CreateStore(
                snapshotModifiers: new[] { snapshotModifier },
                priorAttacks: 1
            );
            RuleDispatcher dispatcher = CreateDispatcher(
                store,
                new TestResolutionDataProvider(
                    Data(15, modifiers: new[] { unityModifier }),
                    ActionValidationResult.Valid
                ),
                new ScriptedRollService(10, 2, 3)
            );

            SpellAttackResolution attack = (
                await DispatchResolvedAttack(dispatcher, Cast(Target))
            ).Attack;

            Assert.That(attack.AttackModifier, Is.EqualTo(5));
            Assert.That(attack.MultipleAttackPenalty, Is.EqualTo(-5));
            Assert.That(attack.Degree, Is.EqualTo(DegreeOfSuccess.Success));
            Assert.That(store.Snapshot.MultipleAttackPenalty[Actor].AttackCount, Is.EqualTo(2));
        }

        [Test]
        public async Task NaturalOneProducesCriticalFailureWithoutDamageAndAdvancesMap()
        {
            InMemoryRulesStore store = CreateStore();
            RuleDispatcher dispatcher = CreateDispatcher(
                store,
                new TestResolutionDataProvider(Data(15), ActionValidationResult.Valid),
                new ScriptedRollService(1)
            );

            SpellAttackResolution attack = (
                await DispatchResolvedAttack(dispatcher, Cast(Target))
            ).Attack;

            Assert.That(attack.Degree, Is.EqualTo(DegreeOfSuccess.CriticalFailure));
            Assert.That(attack.Damage, Is.Empty);
            Assert.That(store.Snapshot.Health[Target].Current, Is.EqualTo(30));
            Assert.That(store.Snapshot.MultipleAttackPenalty[Actor].AttackCount, Is.EqualTo(1));
        }

        [TestCase(InvalidSelection.Empty)]
        [TestCase(InvalidSelection.Multiple)]
        [TestCase(InvalidSelection.Unknown)]
        [TestCase(InvalidSelection.Dead)]
        [TestCase(InvalidSelection.OutOfRange)]
        [TestCase(InvalidSelection.Blocked)]
        public async Task InvalidOrStaleSelectionsSpendNothing(InvalidSelection invalid)
        {
            ActionValidationResult providerValidation = invalid
                is InvalidSelection.OutOfRange
                    or InvalidSelection.Blocked
                ? ActionValidationResult.Invalid(
                    invalid == InvalidSelection.OutOfRange
                        ? "The spell target is out of range."
                        : "The spell target has no line of effect."
                )
                : ActionValidationResult.Valid;
            TestResolutionDataProvider provider = new(Data(15), providerValidation);
            InMemoryRulesStore store = CreateStore();
            ScriptedRollService rolls = new(20);
            RuleDispatcher dispatcher = CreateDispatcher(store, provider, rolls);
            CastSpellActionOp operation = invalid switch
            {
                InvalidSelection.Empty => Cast(),
                InvalidSelection.Multiple => Cast(Target, DeadTarget),
                InvalidSelection.Unknown => Cast(UnknownTarget),
                InvalidSelection.Dead => Cast(DeadTarget),
                _ => Cast(Target),
            };

            OpResult<CastSpellOutcome> result = await dispatcher.Dispatch(operation);

            Assert.That(result, Is.TypeOf<InvalidOpResult<CastSpellOutcome>>());
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(3));
            Assert.That(store.Snapshot.Health[Target].Current, Is.EqualTo(30));
            Assert.That(store.Snapshot.MultipleAttackPenalty[Actor].AttackCount, Is.Zero);
            Assert.That(result.Facts, Is.Empty);
            Assert.That(provider.CaptureCalls, Is.Zero);
            Assert.That(rolls.Remaining, Is.EqualTo(1));
        }

        [Test]
        public async Task InterruptedAttackCastRetainsCostsWithoutAttackDamageOrMap()
        {
            ActiveRuleBinding binding = new(
                new BindingId("spell-attack-interruption-binding"),
                InterruptionDefinition,
                Actor,
                default,
                RuleSource.FromSlug("spell-attack-test"),
                0
            );
            RuleRegistryBuilder registry = new();
            registry
                .Define(InterruptionDefinition)
                .Middleware(RuleLifecyclePhase.Reaction, new InterruptingActionMiddleware());
            InMemoryRulesStore store = CreateStore(binding);
            TestResolutionDataProvider provider = new(Data(15), ActionValidationResult.Valid);
            ScriptedRollService rolls = new(20);
            RuleDispatcher dispatcher = CreateDispatcher(store, provider, rolls, registry.Build());

            OpResult<CastSpellOutcome> result = await dispatcher.Dispatch(Cast(Target));

            Assert.That(result, Is.TypeOf<InterruptedOpResult<CastSpellOutcome>>());
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(1));
            Assert.That(store.Snapshot.Health[Target].Current, Is.EqualTo(30));
            Assert.That(store.Snapshot.MultipleAttackPenalty[Actor].AttackCount, Is.Zero);
            Assert.That(provider.CaptureCalls, Is.Zero);
            Assert.That(result.Facts.OfType<ActionCostSpentFact>().Count(), Is.EqualTo(1));
            Assert.That(result.Facts.OfType<DamageAppliedFact>(), Is.Empty);
            Assert.That(result.Facts.OfType<MultipleAttackPenaltyAdvancedFact>(), Is.Empty);
            Assert.That(rolls.Remaining, Is.EqualTo(1));
        }

        [Test]
        public void ResolveSpellAttackRejectsExternalDispatch()
        {
            RuleDispatcher dispatcher = CreateDispatcher(
                CreateStore(),
                new TestResolutionDataProvider(Data(15), ActionValidationResult.Valid),
                new ScriptedRollService(20)
            );

            InvalidOperationException error = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await dispatcher.Dispatch(new ResolveSpellAttackOp(Actor, DivineLance, Target))
            );

            Assert.That(error.Message, Does.Contain("nested-only"));
        }

        private static SpellAttackResolutionData Data(
            int armorClass,
            IEnumerable<Modifier> modifiers = null,
            IEnumerable<TypedDefenseAdjustment> weaknesses = null,
            IEnumerable<TypedDefenseAdjustment> resistances = null
        ) =>
            new(
                armorClass,
                modifiers ?? Array.Empty<Modifier>(),
                weaknesses ?? Array.Empty<TypedDefenseAdjustment>(),
                resistances ?? Array.Empty<TypedDefenseAdjustment>()
            );

        private static SpellAttackDefinition AttackDefinition() =>
            new(
                new OneCreatureSpellAttackTarget(60),
                new[] { new TypedDamageDice(new DiceExpression(2, 4), "spirit", "divine-lance") }
            );

        private static CastSpellActionOp Cast(params CreatureId[] targets) =>
            new(
                Actor,
                DivineLance,
                TwoActions,
                new SpellCastSelection(targets ?? Array.Empty<CreatureId>())
            );

        private static InMemoryRulesStore CreateStore(
            ActiveRuleBinding binding = null,
            IEnumerable<Modifier> snapshotModifiers = null,
            int priorAttacks = 0,
            bool seedMap = true
        )
        {
            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Actor, Players))
                .SeedCreature(new CreatureState(Target, Enemies))
                .SeedCreature(new CreatureState(DeadTarget, Enemies))
                .SeedHealth(Actor, new HealthState(30, 30))
                .SeedHealth(Target, new HealthState(30, 30))
                .SeedHealth(DeadTarget, new HealthState(0, 30))
                .SeedActionEconomy(Actor, new ActionEconomyState(3, true))
                .SeedStatistics(
                    new CreatureStatisticsState(
                        Actor,
                        0,
                        10,
                        0,
                        0,
                        0,
                        new Dictionary<Skill, int>(),
                        snapshotModifiers ?? Array.Empty<Modifier>()
                    )
                );
            if (seedMap)
                seed.SeedMultipleAttackPenalty(Actor, new MultipleAttackPenaltyState(priorAttacks));
            if (binding != null)
                seed.SeedRuleBinding(binding);
            return new InMemoryRulesStore(seed);
        }

        private static RuleDispatcher CreateDispatcher(
            InMemoryRulesStore store,
            TestResolutionDataProvider provider,
            IRollService rolls,
            RuleRegistry registry = null
        )
        {
            TestCatalog catalog = new();
            RuleDispatcherBuilder builder = new RuleDispatcherBuilder(store, rolls)
                .UseHealthRules()
                .UseMultipleAttackPenaltyRules()
                .UseCheckResolution()
                .UseActionLifecycle(catalog)
                .UseSpellcastingRules(catalog, provider);
            if (registry != null)
                builder.UseRuleRegistry(registry);
            return builder.Build();
        }

        private static ResolvedOpResult<T> RequireResolved<T>(OpResult<T> result)
        {
            Assert.That(result, Is.TypeOf<ResolvedOpResult<T>>());
            return (ResolvedOpResult<T>)result;
        }

        private static async Task<(
            ResolvedOpResult<CastSpellOutcome> Cast,
            SpellAttackResolution Attack,
            CapturingAttackCheckObserver Check
        )> DispatchResolvedAttack(RuleDispatcher dispatcher, CastSpellActionOp operation)
        {
            CapturingSpellAttackObserver observer = new();
            CapturingAttackCheckObserver checkObserver = new();
            dispatcher.RegisterResolvedOpObserver<ResolveSpellAttackOp, SpellAttackResolution>(
                observer
            );
            dispatcher.RegisterResolvedOpObserver<AttackCheckOp, CheckOutcome>(checkObserver);
            ResolvedOpResult<CastSpellOutcome> cast = RequireResolved(
                await dispatcher.Dispatch(operation)
            );
            Assert.That(observer.Result, Is.Not.Null);
            Assert.That(checkObserver.Outcome, Is.Not.Null);
            return (cast, observer.Result, checkObserver);
        }

        public enum InvalidSelection
        {
            Empty,
            Multiple,
            Unknown,
            Dead,
            OutOfRange,
            Blocked,
        }

        private sealed class TestResolutionDataProvider : ISpellAttackResolutionDataProvider
        {
            private readonly SpellAttackResolutionData data;
            private readonly ActionValidationResult validation;

            public TestResolutionDataProvider(
                SpellAttackResolutionData data,
                ActionValidationResult validation
            )
            {
                this.data = data;
                this.validation = validation;
            }

            public int CaptureCalls { get; private set; }

            public ActionValidationResult Validate(
                RulesSnapshot snapshot,
                CreatureId actor,
                SpellAttackDefinition attack,
                CreatureId target
            ) => validation;

            public SpellAttackResolutionData Capture(
                RulesSnapshot snapshot,
                CreatureId actor,
                SpellAttackDefinition attack,
                CreatureId target
            )
            {
                CaptureCalls++;
                return data;
            }
        }

        private sealed class CapturingSpellAttackObserver
            : IResolvedOpObserver<ResolveSpellAttackOp, SpellAttackResolution>
        {
            public SpellAttackResolution Result { get; private set; }

            public ValueTask OnOperationResolved(
                ResolveSpellAttackOp operation,
                SpellAttackResolution result,
                RulesSnapshot currentSnapshot
            )
            {
                Result = result;
                return default;
            }
        }

        private sealed class CapturingAttackCheckObserver
            : IResolvedOpObserver<AttackCheckOp, CheckOutcome>
        {
            public AttackCheckOp Operation { get; private set; }
            public CheckOutcome Outcome { get; private set; }

            public ValueTask OnOperationResolved(
                AttackCheckOp operation,
                CheckOutcome result,
                RulesSnapshot currentSnapshot
            )
            {
                Operation = operation;
                Outcome = result;
                return default;
            }
        }

        private sealed class TestCatalog : ISpellActionCatalog
        {
            private readonly SpellDefinition definition = new(
                DivineLance.Spell,
                "Divine Lance",
                1,
                new[] { TwoActions },
                new[]
                {
                    Trait.FromSlug("attack"),
                    Trait.FromSlug("cantrip"),
                    Trait.FromSlug("concentrate"),
                    Trait.FromSlug("manipulate"),
                    Trait.FromSlug("spirit"),
                },
                Array.Empty<SpellEffectDirective>(),
                new[] { AttackDefinition() }
            );
            private readonly ISpellBook book = new TestBook();

            public ActionProfile GetBaseProfile(ActionDefinitionId definitionId) =>
                throw new KeyNotFoundException();

            public bool TryGetSpell(SpellReference reference, out SpellDefinition value)
            {
                if (reference == DivineLance)
                {
                    value = definition;
                    return true;
                }
                value = null;
                return false;
            }

            public ISpellBook GetSpellBook(CreatureId creature) => book;
        }

        private sealed class TestBook : ISpellBook
        {
            public IReadOnlyList<SpellReference> CastableSpells => new[] { DivineLance };
            public int SpellAttackModifier => 7;
            public int SpellDc => 17;

            public IReadOnlyList<SpellSlotState> CreateInitialSlotStates(CreatureId owner) =>
                Array.Empty<SpellSlotState>();

            public SpellCastAuthorization Authorize(
                CreatureId owner,
                SpellReference spell,
                ISpellSlotStateReader slots
            ) =>
                spell == DivineLance
                    ? SpellCastAuthorization.Cantrip
                    : SpellCastAuthorization.Unavailable("The spell is not prepared.");

            public SpellCastAuthorization BindResource(CreatureId owner, SpellReference spell) =>
                spell == DivineLance
                    ? SpellCastAuthorization.Cantrip
                    : SpellCastAuthorization.Unavailable("The spell is not prepared.");
        }

        private sealed class InterruptingActionMiddleware
            : IOpMiddleware<ActionBegunOp, ActionStartOutcome>
        {
            public ValueTask<OpResult<ActionStartOutcome>> Invoke(
                OpFrame<ActionBegunOp> frame,
                OpMiddlewareContext context,
                OpNext<ActionStartOutcome> next
            ) => new(OpResult<ActionStartOutcome>.Resolved(ActionStartOutcome.Interrupted));
        }
    }
}
