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

            ResolvedOpResult<CastSpellOutcome> result = RequireResolved(
                await dispatcher.Dispatch(Cast(Target))
            );

            SpellAttackResolution attack = result.Value.ResolvedAttacks.Single();
            Assert.That(attack.AttackCheck.Total, Is.EqualTo(17));
            Assert.That(attack.AttackCheck.Degree, Is.EqualTo(DegreeOfSuccess.Success));
            Assert.That(attack.Damage.Single().DamageType, Is.EqualTo("spirit"));
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
                    weaknesses: new[] { new SpellAttackDefenseAdjustment("spirit", 3) },
                    resistances: new[] { new SpellAttackDefenseAdjustment("spirit", 2) }
                ),
                ActionValidationResult.Valid
            );
            InMemoryRulesStore store = CreateStore();
            RuleDispatcher dispatcher = CreateDispatcher(
                store,
                provider,
                new ScriptedRollService(18, 2, 3)
            );

            SpellAttackResolution attack = RequireResolved(await dispatcher.Dispatch(Cast(Target)))
                .Value.ResolvedAttacks.Single();

            Assert.That(attack.AttackCheck.Degree, Is.EqualTo(DegreeOfSuccess.CriticalSuccess));
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

            ResolvedOpResult<CastSpellOutcome> result = RequireResolved(
                await dispatcher.Dispatch(Cast(Target))
            );

            Assert.That(result.Value.ResolvedAttacks.Single().Hit, Is.False);
            Assert.That(store.Snapshot.Health[Target].Current, Is.EqualTo(30));
            Assert.That(result.Facts.OfType<DamageAppliedFact>(), Is.Empty);
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
            RuleDispatcher dispatcher = CreateDispatcher(
                store,
                provider,
                new ScriptedRollService(20)
            );
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
            RuleDispatcher dispatcher = CreateDispatcher(
                store,
                provider,
                new ScriptedRollService(20),
                registry.Build()
            );

            OpResult<CastSpellOutcome> result = await dispatcher.Dispatch(Cast(Target));

            Assert.That(result, Is.TypeOf<InterruptedOpResult<CastSpellOutcome>>());
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(1));
            Assert.That(store.Snapshot.Health[Target].Current, Is.EqualTo(30));
            Assert.That(store.Snapshot.MultipleAttackPenalty[Actor].AttackCount, Is.Zero);
            Assert.That(provider.CaptureCalls, Is.Zero);
            Assert.That(result.Facts.OfType<ActionCostSpentFact>().Count(), Is.EqualTo(1));
            Assert.That(result.Facts.OfType<DamageAppliedFact>(), Is.Empty);
            Assert.That(result.Facts.OfType<MultipleAttackPenaltyAdvancedFact>(), Is.Empty);
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
                    await dispatcher.Dispatch(
                        new ResolveSpellAttackOp(Actor, DivineLance, AttackDefinition(), Target)
                    )
            );

            Assert.That(error.Message, Does.Contain("nested-only"));
        }

        private static SpellAttackResolutionData Data(
            int armorClass,
            IEnumerable<Modifier> modifiers = null,
            IEnumerable<SpellAttackDefenseAdjustment> weaknesses = null,
            IEnumerable<SpellAttackDefenseAdjustment> resistances = null
        ) =>
            new(
                armorClass,
                modifiers ?? Array.Empty<Modifier>(),
                weaknesses ?? Array.Empty<SpellAttackDefenseAdjustment>(),
                resistances ?? Array.Empty<SpellAttackDefenseAdjustment>()
            );

        private static SpellAttackDefinition AttackDefinition() =>
            new(
                new OneCreatureSpellAttackTarget(60),
                new[] { new SpellAttackDamageComponent(2, 4, "spirit") }
            );

        private static CastSpellActionOp Cast(params CreatureId[] targets) =>
            new(
                Actor,
                DivineLance,
                TwoActions,
                new SpellCastSelection(targets ?? Array.Empty<CreatureId>())
            );

        private static InMemoryRulesStore CreateStore(ActiveRuleBinding binding = null)
        {
            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Actor, Players))
                .SeedCreature(new CreatureState(Target, Enemies))
                .SeedCreature(new CreatureState(DeadTarget, Enemies))
                .SeedHealth(Actor, new HealthState(30, 30))
                .SeedHealth(Target, new HealthState(30, 30))
                .SeedHealth(DeadTarget, new HealthState(0, 30))
                .SeedActionEconomy(Actor, new ActionEconomyState(3, true))
                .SeedMultipleAttackPenalty(Actor, new MultipleAttackPenaltyState(0));
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
