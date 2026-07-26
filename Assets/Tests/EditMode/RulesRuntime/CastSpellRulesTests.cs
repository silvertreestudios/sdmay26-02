using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    public sealed class CastSpellRulesTests
    {
        private static readonly CreatureId Actor = new("spell-actor");
        private static readonly PlayerId Player = new("spell-player");
        private static readonly SpellReference Light = new(new SpellId("light"), 1);
        private static readonly SpellActionVariant TwoActions = new(2);
        private static readonly SpellSlotPoolId RankedPool = new("spell-actor:rank-1");
        private static readonly RuleDefinitionId EffectDefinition = new("spell-effect-light");
        private static readonly RuleDefinitionId InterruptionDefinition = new(
            "spell-test-interruption"
        );
        private static readonly RuleSource TestSource = RuleSource.FromSlug("spell-test");

        [Test]
        public void OperationFreezesDefinitionVariantTraitsAndCantripCost()
        {
            TestCatalog catalog = new();
            CastSpellActionOp operation = new(
                Actor,
                Light,
                TwoActions,
                SpellCastAuthorization.Cantrip
            );

            ActionProfile profile = operation.GetBaseProfile(catalog);

            Assert.That(profile.Cost, Is.EqualTo(ActionCost.Two));
            Assert.That(profile.AdditionalCosts, Is.Empty);
            Assert.That(
                profile.Traits.Select(trait => trait.Slug),
                Is.EqualTo(new[] { "cantrip", "concentrate", "light", "manipulate" })
            );
        }

        [Test]
        public async Task ValidCastSpendsTwoActionsAndCreatesProvenancedEffectExactlyOnce()
        {
            InMemoryRulesStore store = CreateStore(3);
            RuleDispatcher dispatcher = CreateDispatcher(store, new TestBook(true));
            CountingObserver observer = new();
            dispatcher.RegisterResolvedOpObserver<CastSpellActionOp, CastSpellOutcome>(observer);

            OpResult<CastSpellOutcome> result = await dispatcher.Dispatch(
                new CastSpellActionOp(Actor, Light, TwoActions, SpellCastAuthorization.Cantrip)
            );

            Assert.That(result, Is.TypeOf<ResolvedOpResult<CastSpellOutcome>>());
            CastSpellOutcome outcome = ((ResolvedOpResult<CastSpellOutcome>)result).Value;
            Assert.That(outcome.CreatedEffects, Has.Count.EqualTo(1));
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(1));
            ActiveEffectInstance effect = store.Snapshot.ActiveEffects[
                outcome.CreatedEffects.Single()
            ];
            Assert.That(effect.Duration, Is.EqualTo(EffectDuration.Indefinite));
            Assert.That(effect.GetState<SpellEffectState>().Spell, Is.EqualTo(Light));
            Assert.That(effect.GetState<SpellEffectState>().Target, Is.EqualTo(Actor));
            Assert.That(result.Facts.OfType<ActiveEffectCreatedFact>().Count(), Is.EqualTo(1));
            Assert.That(observer.Calls, Is.EqualTo(1));
        }

        [Test]
        public async Task UnknownRankAndForgedPoolRejectBeforeAnyCost()
        {
            InMemoryRulesStore store = CreateStore(3);
            RuleDispatcher dispatcher = CreateDispatcher(store, new TestBook(true));

            OpResult<CastSpellOutcome> wrongRank = await dispatcher.Dispatch(
                new CastSpellActionOp(
                    Actor,
                    new SpellReference(new SpellId("light"), 2),
                    TwoActions,
                    SpellCastAuthorization.Cantrip
                )
            );
            OpResult<CastSpellOutcome> forgedPool = await dispatcher.Dispatch(
                new CastSpellActionOp(
                    Actor,
                    Light,
                    TwoActions,
                    SpellCastAuthorization.FromPool(new SpellSlotPoolId("forged"))
                )
            );

            Assert.That(wrongRank, Is.TypeOf<InvalidOpResult<CastSpellOutcome>>());
            Assert.That(forgedPool, Is.TypeOf<InvalidOpResult<CastSpellOutcome>>());
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(3));
            Assert.That(wrongRank.Facts, Is.Empty);
            Assert.That(forgedPool.Facts, Is.Empty);
        }

        [Test]
        public async Task InsufficientActionsOrUnpreparedSpellRejectAtomically()
        {
            InMemoryRulesStore shortStore = CreateStore(1);
            RuleDispatcher shortDispatcher = CreateDispatcher(shortStore, new TestBook(true));
            InMemoryRulesStore unpreparedStore = CreateStore(3);
            RuleDispatcher unpreparedDispatcher = CreateDispatcher(
                unpreparedStore,
                new TestBook(false)
            );

            OpResult<CastSpellOutcome> shortResult = await shortDispatcher.Dispatch(Cast());
            OpResult<CastSpellOutcome> unprepared = await unpreparedDispatcher.Dispatch(Cast());

            Assert.That(shortResult, Is.TypeOf<InvalidOpResult<CastSpellOutcome>>());
            Assert.That(unprepared, Is.TypeOf<InvalidOpResult<CastSpellOutcome>>());
            Assert.That(shortStore.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(1));
            Assert.That(
                unpreparedStore.Snapshot.ActionEconomy[Actor].ActionsRemaining,
                Is.EqualTo(3)
            );
            Assert.That(shortResult.Facts, Is.Empty);
            Assert.That(unprepared.Facts, Is.Empty);
        }

        [Test]
        public async Task RankedCastAtomicallySpendsAuthorizedSlotAndActions()
        {
            InMemoryRulesStore store = CreateStore(3, slotUses: 1);
            RuleDispatcher dispatcher = CreateDispatcher(store, new SlotTestBook());

            OpResult<CastSpellOutcome> result = await dispatcher.Dispatch(
                new CastSpellActionOp(
                    Actor,
                    Light,
                    TwoActions,
                    SpellCastAuthorization.FromPool(RankedPool)
                )
            );

            Assert.That(result, Is.TypeOf<ResolvedOpResult<CastSpellOutcome>>());
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(1));
            Assert.That(store.Snapshot.SpellSlots[RankedPool].Remaining, Is.Zero);
            Assert.That(result.Facts.OfType<ActionCostSpentFact>().Count(), Is.EqualTo(1));
            Assert.That(result.Facts.OfType<SpellSlotSpentFact>().Count(), Is.EqualTo(1));
        }

        [Test]
        public async Task InterruptedCastRetainsCommittedCostButCreatesNoEffect()
        {
            InterruptingActionMiddleware middleware = new();
            RuleRegistryBuilder registryBuilder = CreateRegistryBuilder();
            registryBuilder
                .Define(InterruptionDefinition)
                .Middleware(RuleLifecyclePhase.Reaction, middleware);
            InMemoryRulesStore store = CreateStore(
                3,
                new ActiveRuleBinding(
                    new BindingId("spell-test-interruption-binding"),
                    InterruptionDefinition,
                    Actor,
                    default,
                    TestSource,
                    0
                ),
                slotUses: 1
            );
            RuleDispatcher dispatcher = CreateDispatcher(
                store,
                new SlotTestBook(),
                registryBuilder.Build()
            );

            OpResult<CastSpellOutcome> result = await dispatcher.Dispatch(
                new CastSpellActionOp(
                    Actor,
                    Light,
                    TwoActions,
                    SpellCastAuthorization.FromPool(RankedPool)
                )
            );

            Assert.That(result, Is.TypeOf<InterruptedOpResult<CastSpellOutcome>>());
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(1));
            Assert.That(store.Snapshot.ActiveEffects.Count, Is.Zero);
            Assert.That(store.Snapshot.SpellSlots[RankedPool].Remaining, Is.Zero);
            Assert.That(result.Facts.OfType<ActionCostSpentFact>().Count(), Is.EqualTo(1));
            Assert.That(result.Facts.OfType<SpellSlotSpentFact>().Count(), Is.EqualTo(1));
            Assert.That(middleware.Calls, Is.EqualTo(1));
        }

        private static CastSpellActionOp Cast() =>
            new(Actor, Light, TwoActions, SpellCastAuthorization.Cantrip);

        private static InMemoryRulesStore CreateStore(
            int actions,
            ActiveRuleBinding binding = null,
            int? slotUses = null
        )
        {
            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Actor, Player))
                .SeedActionEconomy(Actor, new ActionEconomyState(actions, true));
            if (binding != null)
                seed.SeedRuleBinding(binding);
            if (slotUses.HasValue)
                seed.SeedSpellSlot(new SpellSlotState(RankedPool, Actor, slotUses.Value, 1));
            return new InMemoryRulesStore(seed);
        }

        private static RuleRegistryBuilder CreateRegistryBuilder()
        {
            RuleRegistryBuilder builder = new();
            builder.Define(EffectDefinition);
            return builder;
        }

        private static RuleDispatcher CreateDispatcher(
            InMemoryRulesStore store,
            ISpellBook book,
            RuleRegistry registry = null
        )
        {
            RuleRegistry effectiveRegistry = registry ?? CreateRegistryBuilder().Build();
            TestCatalog catalog = new();
            return new RuleDispatcherBuilder(store)
                .UseActiveEffectRules(effectiveRegistry)
                .UseActionLifecycle(catalog)
                .UseSpellcastingRules(catalog, new TestBookProvider(book))
                .Build();
        }

        private sealed class TestCatalog : ISpellActionCatalog
        {
            private readonly SpellDefinition definition = new(
                new SpellId("light"),
                "Light",
                1,
                new[] { TwoActions },
                new[]
                {
                    Trait.FromSlug("cantrip"),
                    Trait.FromSlug("concentrate"),
                    Trait.FromSlug("light"),
                    Trait.FromSlug("manipulate"),
                },
                new[] { new SpellEffectDirective(EffectDefinition, EffectDuration.Indefinite) }
            );

            public ActionProfile GetBaseProfile(ActionDefinitionId definitionId) =>
                throw new KeyNotFoundException();

            public bool TryGetSpell(SpellReference reference, out SpellDefinition value)
            {
                if (reference.Spell == Light.Spell && reference.Rank >= 1)
                {
                    value = definition;
                    return true;
                }
                value = null;
                return false;
            }
        }

        private sealed class TestBookProvider : ISpellBookProvider
        {
            private readonly ISpellBook book;

            public TestBookProvider(ISpellBook book) => this.book = book;

            public ISpellBook GetSpellBook(CreatureId creature) => book;
        }

        private sealed class TestBook : ISpellBook
        {
            private readonly bool prepared;

            public TestBook(bool prepared) => this.prepared = prepared;

            public IReadOnlyList<SpellReference> CastableSpells =>
                prepared ? new[] { Light } : Array.Empty<SpellReference>();
            public int SpellAttackModifier => 0;
            public int SpellDc => 10;

            public IReadOnlyList<SpellSlotState> CreateInitialSlotStates(CreatureId owner) =>
                Array.Empty<SpellSlotState>();

            public SpellCastAuthorization Authorize(
                CreatureId owner,
                SpellReference spell,
                ISpellSlotStateReader slots
            ) =>
                prepared && spell == Light
                    ? SpellCastAuthorization.Cantrip
                    : SpellCastAuthorization.Unavailable("The exact spell is not prepared.");

            public SpellCastAuthorization Authorize(SpellReference spell) =>
                prepared && spell == Light
                    ? SpellCastAuthorization.Cantrip
                    : SpellCastAuthorization.Unavailable("The exact spell is not prepared.");

            public bool TrySpend(SpellReference spell) => prepared && spell == Light;
        }

        private sealed class SlotTestBook : ISpellBook
        {
            public IReadOnlyList<SpellReference> CastableSpells => new[] { Light };
            public int SpellAttackModifier => 0;
            public int SpellDc => 10;

            public IReadOnlyList<SpellSlotState> CreateInitialSlotStates(CreatureId owner) =>
                new[] { new SpellSlotState(RankedPool, owner, 1, 1) };

            public SpellCastAuthorization Authorize(
                CreatureId owner,
                SpellReference spell,
                ISpellSlotStateReader slots
            ) =>
                spell == Light
                && slots.TryGet(RankedPool, out SpellSlotState state)
                && state.Owner == owner
                && state.Remaining > 0
                    ? SpellCastAuthorization.FromPool(RankedPool)
                    : SpellCastAuthorization.Unavailable("The ranked slot is unavailable.");

            public SpellCastAuthorization Authorize(SpellReference spell) =>
                spell == Light
                    ? SpellCastAuthorization.FromPool(RankedPool)
                    : SpellCastAuthorization.Unavailable("The ranked slot is unavailable.");

            public bool TrySpend(SpellReference spell) => spell == Light;
        }

        private sealed class CountingObserver
            : IResolvedOpObserver<CastSpellActionOp, CastSpellOutcome>
        {
            public int Calls { get; private set; }

            public ValueTask OnOperationResolved(
                CastSpellActionOp operation,
                CastSpellOutcome result,
                RulesSnapshot currentSnapshot
            )
            {
                Calls++;
                return default;
            }
        }

        private sealed class InterruptingActionMiddleware
            : IOpMiddleware<ActionBegunOp, ActionStartOutcome>
        {
            public int Calls { get; private set; }

            public ValueTask<OpResult<ActionStartOutcome>> Invoke(
                OpFrame<ActionBegunOp> frame,
                OpMiddlewareContext context,
                OpNext<ActionStartOutcome> next
            )
            {
                Calls++;
                return new ValueTask<OpResult<ActionStartOutcome>>(
                    OpResult<ActionStartOutcome>.Resolved(ActionStartOutcome.Interrupted)
                );
            }
        }
    }
}
