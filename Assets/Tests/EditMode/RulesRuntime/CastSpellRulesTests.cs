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
        private static readonly SpellSlotPoolId AlternateRankedPool = new(
            "spell-actor:alternate-rank-1"
        );
        private static readonly RuleDefinitionId EffectDefinition = new("spell-effect-light");
        private static readonly RuleDefinitionId InterruptionDefinition = new(
            "spell-test-interruption"
        );
        private static readonly RuleSource TestSource = RuleSource.FromSlug("spell-test");

        [Test]
        public void OperationFreezesDefinitionVariantTraitsAndCantripCost()
        {
            CastSpellActionOp operation = new(Actor, Light, TwoActions, SpellCastSelection.Empty);

            ActionProfile profile = operation.GetBaseProfile(new TestCatalog(new TestBook(true)));

            Assert.That(operation.Selection.Creatures, Is.Empty);
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
                new CastSpellActionOp(Actor, Light, TwoActions, SpellCastSelection.Empty)
            );

            Assert.That(result, Is.TypeOf<ResolvedOpResult<CastSpellOutcome>>());
            CastSpellOutcome outcome = ((ResolvedOpResult<CastSpellOutcome>)result).Value;
            Assert.That(outcome.CreatedEffects, Has.Count.EqualTo(1));
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(1));
            ActiveEffectInstance effect = store.Snapshot.ActiveEffects[
                outcome.CreatedEffects.Single()
            ];
            Assert.That(effect.Duration, Is.EqualTo(EffectDuration.Indefinite));
            Assert.That(effect.Source, Is.EqualTo(RuleSource.FromSlug("light")));
            Assert.That(effect.GetState<SpellEffectState>().Spell, Is.EqualTo(Light));
            Assert.That(effect.GetState<SpellEffectState>().Target, Is.EqualTo(Actor));
            Assert.That(result.Facts.OfType<ActiveEffectCreatedFact>().Count(), Is.EqualTo(1));
            Assert.That(observer.Calls, Is.EqualTo(1));
        }

        [Test]
        public async Task SelfTargetMetadataIgnoresPlayerSelectedCreatureIds()
        {
            InMemoryRulesStore store = CreateStore(3);
            RuleDispatcher dispatcher = CreateDispatcher(store, new TestBook(true));
            SpellCastSelection forgedSelection = new(new[] { new CreatureId("forged-target") });

            OpResult<CastSpellOutcome> result = await dispatcher.Dispatch(
                new CastSpellActionOp(Actor, Light, TwoActions, forgedSelection)
            );

            Assert.That(result, Is.TypeOf<ResolvedOpResult<CastSpellOutcome>>());
            ActiveEffectId effectId = (
                (ResolvedOpResult<CastSpellOutcome>)result
            ).Value.CreatedEffects.Single();
            Assert.That(
                store.Snapshot.ActiveEffects[effectId].GetState<SpellEffectState>().Target,
                Is.EqualTo(Actor)
            );
        }

        [Test]
        public async Task UnknownRankRejectsBeforeAnyCostAndOperationCannotForgeResource()
        {
            InMemoryRulesStore store = CreateStore(3);
            RuleDispatcher dispatcher = CreateDispatcher(store, new TestBook(true));

            OpResult<CastSpellOutcome> wrongRank = await dispatcher.Dispatch(
                new CastSpellActionOp(
                    Actor,
                    new SpellReference(new SpellId("light"), 2),
                    TwoActions,
                    SpellCastSelection.Empty
                )
            );
            Assert.That(wrongRank, Is.TypeOf<InvalidOpResult<CastSpellOutcome>>());
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(3));
            Assert.That(wrongRank.Facts, Is.Empty);
            Assert.That(typeof(CastSpellActionOp).GetProperty("Authorization"), Is.Null);
            Assert.That(typeof(CastSpellActionOp).GetProperty("SlotPool"), Is.Null);
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
        public async Task RankedCastAtomicallySpendsDefinitionBoundSlotAndActions()
        {
            InMemoryRulesStore store = CreateStore(3, slotUses: 1);
            RuleDispatcher dispatcher = CreateDispatcher(store, new SlotTestBook());

            OpResult<CastSpellOutcome> result = await dispatcher.Dispatch(
                new CastSpellActionOp(Actor, Light, TwoActions, SpellCastSelection.Empty)
            );

            Assert.That(result, Is.TypeOf<ResolvedOpResult<CastSpellOutcome>>());
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(1));
            Assert.That(store.Snapshot.SpellSlots[RankedPool].Remaining, Is.Zero);
            Assert.That(result.Facts.OfType<ActionCostSpentFact>().Count(), Is.EqualTo(1));
            Assert.That(result.Facts.OfType<SpellSlotSpentFact>().Count(), Is.EqualTo(1));
        }

        [Test]
        public async Task MismatchedBoundAndAuthorizedSlotsRejectBeforeAnyCostsOrEffects()
        {
            InMemoryRulesStore store = CreateStore(3, slotUses: 1, alternateSlotUses: 1);
            RuleDispatcher dispatcher = CreateDispatcher(store, new MismatchedSlotTestBook());

            OpResult<CastSpellOutcome> result = await dispatcher.Dispatch(Cast());

            Assert.That(result, Is.TypeOf<InvalidOpResult<CastSpellOutcome>>());
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(3));
            Assert.That(store.Snapshot.SpellSlots[RankedPool].Remaining, Is.EqualTo(1));
            Assert.That(store.Snapshot.SpellSlots[AlternateRankedPool].Remaining, Is.EqualTo(1));
            Assert.That(store.Snapshot.ActiveEffects, Is.Empty);
            Assert.That(result.Facts, Is.Empty);
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
                new CastSpellActionOp(Actor, Light, TwoActions, SpellCastSelection.Empty)
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
            new(Actor, Light, TwoActions, SpellCastSelection.Empty);

        private static InMemoryRulesStore CreateStore(
            int actions,
            ActiveRuleBinding binding = null,
            int? slotUses = null,
            int? alternateSlotUses = null
        )
        {
            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Actor, Player))
                .SeedHealth(Actor, new HealthState(10, 10))
                .SeedActionEconomy(Actor, new ActionEconomyState(actions, true));
            if (binding != null)
                seed.SeedRuleBinding(binding);
            if (slotUses.HasValue)
                seed.SeedSpellSlot(new SpellSlotState(RankedPool, Actor, slotUses.Value, 1));
            if (alternateSlotUses.HasValue)
                seed.SeedSpellSlot(
                    new SpellSlotState(AlternateRankedPool, Actor, alternateSlotUses.Value, 1)
                );
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
            TestCatalog catalog = new(book);
            return new RuleDispatcherBuilder(store)
                .UseActiveEffectRules(effectiveRegistry)
                .UseActionLifecycle(catalog)
                .UseSpellcastingRules(catalog)
                .Build();
        }

        [Test]
        public async Task InvalidDefinitionVariantRejectsWithoutActionsOrEffects()
        {
            InMemoryRulesStore store = CreateStore(3);
            TestBook book = new(true);
            TestCatalog catalog = new(book);
            CastSpellActionDefinition definition = new(catalog);
            SpellActionVariant invalidVariant = new(1);

            Assert.That(
                definition.GetAvailability(store.Snapshot, Actor, Light, invalidVariant),
                Is.TypeOf<UnavailableActionAvailability>()
            );

            OpResult<CastSpellOutcome> result = await CreateDispatcher(store, book)
                .Dispatch(
                    definition.CreateOp(Actor, Light, invalidVariant, SpellCastSelection.Empty)
                );

            Assert.That(result, Is.TypeOf<InvalidOpResult<CastSpellOutcome>>());
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(3));
            Assert.That(store.Snapshot.ActiveEffects, Is.Empty);
            Assert.That(result.Facts, Is.Empty);
        }

        private sealed class TestCatalog : ISpellActionCatalog
        {
            private readonly ISpellBook book;
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
                new[]
                {
                    new SpellEffectDirective(EffectDefinition, EffectDuration.Indefinite, "self"),
                }
            );

            public TestCatalog(ISpellBook book) => this.book = book;

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

            public SpellCastAuthorization BindResource(CreatureId owner, SpellReference spell) =>
                prepared && spell == Light
                    ? SpellCastAuthorization.Cantrip
                    : SpellCastAuthorization.Unavailable("The exact spell is not prepared.");
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

            public SpellCastAuthorization BindResource(CreatureId owner, SpellReference spell) =>
                spell == Light
                    ? SpellCastAuthorization.FromPool(RankedPool)
                    : SpellCastAuthorization.Unavailable("The ranked slot is unavailable.");
        }

        private sealed class MismatchedSlotTestBook : ISpellBook
        {
            public IReadOnlyList<SpellReference> CastableSpells => new[] { Light };
            public int SpellAttackModifier => 0;
            public int SpellDc => 10;

            public IReadOnlyList<SpellSlotState> CreateInitialSlotStates(CreatureId owner) =>
                new[]
                {
                    new SpellSlotState(RankedPool, owner, 1, 1),
                    new SpellSlotState(AlternateRankedPool, owner, 1, 1),
                };

            public SpellCastAuthorization Authorize(
                CreatureId owner,
                SpellReference spell,
                ISpellSlotStateReader slots
            ) =>
                spell == Light
                && slots.TryGet(AlternateRankedPool, out SpellSlotState state)
                && state.Owner == owner
                && state.Remaining > 0
                    ? SpellCastAuthorization.FromPool(AlternateRankedPool)
                    : SpellCastAuthorization.Unavailable("The alternate slot is unavailable.");

            public SpellCastAuthorization BindResource(CreatureId owner, SpellReference spell) =>
                spell == Light
                    ? SpellCastAuthorization.FromPool(RankedPool)
                    : SpellCastAuthorization.Unavailable("The ranked slot is unavailable.");
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
