using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    public sealed class StrikeRulesTests
    {
        private static readonly CreatureId Actor = new CreatureId("actor");
        private static readonly CreatureId Target = new CreatureId("target");
        private static readonly ItemId Weapon = new ItemId("weapon");
        private static readonly ItemId Ammo = new ItemId("ammo");

        [Test]
        public async Task ValidHitSpendsOneActionAdvancesMapAndMutatesHpOnce()
        {
            TestRuntime runtime = CreateRuntime(new ScriptedRollService(10, 4));

            OpResult<StrikeOutcome> result = await runtime.Dispatcher.Dispatch(
                new StrikeActionOp(Actor, Weapon, Target)
            );

            ResolvedOpResult<StrikeOutcome> resolved = AssertResolved(result);
            Assert.That(resolved.Value.Resolution.Degree, Is.EqualTo(DegreeOfSuccess.Success));
            Assert.That(resolved.Value.Resolution.FinalDamage, Is.EqualTo(6));
            Assert.That(runtime.Dispatcher.Snapshot.Health[Target].Current, Is.EqualTo(14));
            Assert.That(
                runtime.Dispatcher.Snapshot.ActionEconomy[Actor].ActionsRemaining,
                Is.EqualTo(2)
            );
            Assert.That(
                runtime.Dispatcher.Snapshot.MultipleAttackPenalty[Actor].AttackCount,
                Is.EqualTo(1)
            );
            Assert.That(result.Facts.OfType<DamageAppliedFact>().Count(), Is.EqualTo(1));
        }

        [Test]
        public async Task MissStillSpendsActionAndAdvancesMapWithoutDamage()
        {
            TestRuntime runtime = CreateRuntime(new ScriptedRollService(2));

            ResolvedOpResult<StrikeOutcome> result = AssertResolved(
                await runtime.Dispatcher.Dispatch(new StrikeActionOp(Actor, Weapon, Target))
            );

            Assert.That(result.Value.Resolution.Hit, Is.False);
            Assert.That(runtime.Dispatcher.Snapshot.Health[Target].Current, Is.EqualTo(20));
            Assert.That(
                runtime.Dispatcher.Snapshot.ActionEconomy[Actor].ActionsRemaining,
                Is.EqualTo(2)
            );
            Assert.That(result.Value.AttackCount, Is.EqualTo(1));
            Assert.That(result.Facts.OfType<DamageAppliedFact>(), Is.Empty);
        }

        [Test]
        public async Task CriticalHitDoublesBaseDamageBeforeDeadly()
        {
            StrikeItemDefinition deadly = CreateItem(
                traits: new[] { Trait.FromSlug("deadly-d10") }
            );
            TestRuntime runtime = CreateRuntime(new ScriptedRollService(20, 4, 7), deadly);

            StrikeResolution resolution = AssertResolved(
                await runtime.Dispatcher.Dispatch(new StrikeActionOp(Actor, Weapon, Target))
            ).Value.Resolution;

            Assert.That(resolution.Degree, Is.EqualTo(DegreeOfSuccess.CriticalSuccess));
            Assert.That(resolution.FinalDamage, Is.EqualTo(19));
            Assert.That(runtime.Dispatcher.Snapshot.Health[Target].Current, Is.EqualTo(1));
        }

        [Test]
        public async Task FatalUpgradesBaseDieAndAddsPostDoubleDieDeterministically()
        {
            StrikeItemDefinition fatal = CreateItem(traits: new[] { Trait.FromSlug("fatal-d12") });
            TestRuntime runtime = CreateRuntime(
                new ScriptedRollService(20, 5, 7),
                fatal,
                targetHp: 30
            );

            StrikeResolution resolution = AssertResolved(
                await runtime.Dispatcher.Dispatch(new StrikeActionOp(Actor, Weapon, Target))
            ).Value.Resolution;

            Assert.That(resolution.FinalDamage, Is.EqualTo(21));
            Assert.That(runtime.Rolls.Remaining, Is.Zero);
        }

        [Test]
        public async Task WeaknessAndResistanceApplyPerDamageTypeAfterCritical()
        {
            TestRuntime runtime = CreateRuntime(
                new ScriptedRollService(10, 4),
                resolutionData: new StrikeResolutionData(
                    15,
                    Array.Empty<Modifier>(),
                    Array.Empty<StrikeDamageComponent>(),
                    Array.Empty<StrikeFlatDamage>(),
                    new[] { new StrikeDefenseAdjustment("slashing", 3) },
                    new[] { new StrikeDefenseAdjustment("slashing", 1) }
                )
            );

            StrikeResolution resolution = AssertResolved(
                await runtime.Dispatcher.Dispatch(new StrikeActionOp(Actor, Weapon, Target))
            ).Value.Resolution;

            Assert.That(resolution.FinalDamage, Is.EqualTo(8));
            Assert.That(resolution.Damage.Single().Amount, Is.EqualTo(8));
        }

        [Test]
        public async Task InvalidTargetingSpendsNothingAndRollsNothing()
        {
            TestRuntime runtime = CreateRuntime(new ScriptedRollService(20));
            runtime.Targeting.Result = StrikeTargetingOutcome.Invalid("blocked");

            OpResult<StrikeOutcome> result = await runtime.Dispatcher.Dispatch(
                new StrikeActionOp(Actor, Weapon, Target)
            );

            Assert.That(result, Is.TypeOf<InvalidOpResult<StrikeOutcome>>());
            Assert.That(
                runtime.Dispatcher.Snapshot.ActionEconomy[Actor].ActionsRemaining,
                Is.EqualTo(3)
            );
            Assert.That(
                runtime.Dispatcher.Snapshot.MultipleAttackPenalty[Actor].AttackCount,
                Is.Zero
            );
            Assert.That(runtime.Rolls.Remaining, Is.EqualTo(1));
        }

        [Test]
        public async Task AgileMapUsesZeroFourEightAndTurnStartResets()
        {
            StrikeItemDefinition agile = CreateItem(traits: new[] { Trait.FromSlug("agile") });
            TestRuntime runtime = CreateRuntime(new ScriptedRollService(2, 2, 2, 2), agile);

            StrikeOutcome first = AssertResolved(
                await runtime.Dispatcher.Dispatch(new StrikeActionOp(Actor, Weapon, Target))
            ).Value;
            StrikeOutcome second = AssertResolved(
                await runtime.Dispatcher.Dispatch(new StrikeActionOp(Actor, Weapon, Target))
            ).Value;
            StrikeOutcome third = AssertResolved(
                await runtime.Dispatcher.Dispatch(new StrikeActionOp(Actor, Weapon, Target))
            ).Value;

            Assert.That(first.Resolution.MultipleAttackPenalty, Is.Zero);
            Assert.That(second.Resolution.MultipleAttackPenalty, Is.EqualTo(-4));
            Assert.That(third.Resolution.MultipleAttackPenalty, Is.EqualTo(-8));
            AssertResolved(await runtime.Dispatcher.Dispatch(new BeginCombatTurnOp(Actor, 3)));
            Assert.That(
                runtime.Dispatcher.Snapshot.MultipleAttackPenalty[Actor].AttackCount,
                Is.Zero
            );
            StrikeOutcome reset = AssertResolved(
                await runtime.Dispatcher.Dispatch(new StrikeActionOp(Actor, Weapon, Target))
            ).Value;
            Assert.That(reset.Resolution.MultipleAttackPenalty, Is.Zero);
        }

        [Test]
        public async Task AmmunitionAndLoadedStateHaveOneRulesWritePathThroughReload()
        {
            StrikeItemDefinition ranged = CreateItem(
                reloadActions: 1,
                ammunition: StrikeAmmunitionRequirement.Required(Ammo)
            );
            TestRuntime runtime = CreateRuntime(new ScriptedRollService(2, 2), ranged, ammo: 2);

            AssertResolved(
                await runtime.Dispatcher.Dispatch(new StrikeActionOp(Actor, Weapon, Target))
            );
            Assert.That(runtime.Dispatcher.Snapshot.Ammunition[Ammo].Remaining, Is.EqualTo(1));
            Assert.That(runtime.Dispatcher.Snapshot.Equipment[Weapon].IsLoaded, Is.False);

            OpResult<StrikeOutcome> blocked = await runtime.Dispatcher.Dispatch(
                new StrikeActionOp(Actor, Weapon, Target)
            );
            Assert.That(blocked, Is.TypeOf<InvalidOpResult<StrikeOutcome>>());
            Assert.That(runtime.Dispatcher.Snapshot.Ammunition[Ammo].Remaining, Is.EqualTo(1));
            Assert.That(
                runtime.Dispatcher.Snapshot.ActionEconomy[Actor].ActionsRemaining,
                Is.EqualTo(2)
            );

            AssertResolved(await runtime.Dispatcher.Dispatch(new ReloadActionOp(Actor, Weapon)));
            Assert.That(runtime.Dispatcher.Snapshot.Equipment[Weapon].IsLoaded, Is.True);
            Assert.That(runtime.Dispatcher.Snapshot.Ammunition[Ammo].Remaining, Is.EqualTo(1));
            Assert.That(
                runtime.Dispatcher.Snapshot.ActionEconomy[Actor].ActionsRemaining,
                Is.EqualTo(1)
            );
        }

        [Test]
        public async Task ReloadRejectsWithoutAmmunitionBeforeCost()
        {
            StrikeItemDefinition ranged = CreateItem(
                reloadActions: 1,
                ammunition: StrikeAmmunitionRequirement.Required(Ammo)
            );
            TestRuntime runtime = CreateRuntime(
                new ScriptedRollService(),
                ranged,
                ammo: 0,
                loaded: false
            );

            OpResult<ReloadOutcome> result = await runtime.Dispatcher.Dispatch(
                new ReloadActionOp(Actor, Weapon)
            );

            Assert.That(result, Is.TypeOf<InvalidOpResult<ReloadOutcome>>());
            Assert.That(
                runtime.Dispatcher.Snapshot.ActionEconomy[Actor].ActionsRemaining,
                Is.EqualTo(3)
            );
            Assert.That(runtime.Dispatcher.Snapshot.Equipment[Weapon].IsLoaded, Is.False);
        }

        private static TestRuntime CreateRuntime(
            ScriptedRollService rolls,
            StrikeItemDefinition item = null,
            int targetHp = 20,
            int ammo = -1,
            bool loaded = true,
            StrikeResolutionData resolutionData = null
        )
        {
            item ??= CreateItem();
            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Actor, new PlayerId("players")))
                .SeedCreature(new CreatureState(Target, new PlayerId("enemies")))
                .SeedHealth(Actor, new HealthState(20, 20))
                .SeedHealth(Target, new HealthState(targetHp, targetHp))
                .SeedActionEconomy(Actor, new ActionEconomyState(3, true))
                .SeedActionEconomy(Target, new ActionEconomyState(0, true))
                .SeedMultipleAttackPenalty(Actor, new MultipleAttackPenaltyState(0))
                .SeedEquipment(new EquipmentState(item.Item, item.Definition, Actor, true, loaded));
            if (ammo >= 0)
                seed.SeedAmmunition(new AmmunitionState(Ammo, Actor, ammo));

            TestCatalog catalog = new TestCatalog(item);
            TestTargeting targeting = new TestTargeting();
            FixedResolutionDataProvider data = new FixedResolutionDataProvider(
                resolutionData
                    ?? new StrikeResolutionData(
                        15,
                        Array.Empty<Modifier>(),
                        Array.Empty<StrikeDamageComponent>(),
                        Array.Empty<StrikeFlatDamage>(),
                        Array.Empty<StrikeDefenseAdjustment>(),
                        Array.Empty<StrikeDefenseAdjustment>()
                    )
            );
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                new InMemoryRulesStore(seed),
                rolls
            )
                .UseHealthRules()
                .UseCombatRuntimeRules()
                .UseActionLifecycle(catalog)
                .UseStrikeRules(catalog, targeting, data)
                .Build();
            return new TestRuntime(dispatcher, rolls, targeting);
        }

        private static StrikeItemDefinition CreateItem(
            IEnumerable<Trait> traits = null,
            int reloadActions = 0,
            StrikeAmmunitionRequirement ammunition = null
        ) =>
            new StrikeItemDefinition(
                Weapon,
                new ItemDefinitionId("test-sword"),
                "Test Sword",
                "sword",
                "martial",
                traits ?? Array.Empty<Trait>(),
                10,
                new[] { new StrikeDamageComponent(1, 6, "slashing", "Test Sword") },
                new[] { new StrikeFlatDamage(2, "slashing", "Strength") },
                5,
                reloadActions > 0 ? 30 : 0,
                reloadActions,
                ammunition ?? StrikeAmmunitionRequirement.None
            );

        private static ResolvedOpResult<T> AssertResolved<T>(OpResult<T> result)
        {
            Assert.That(result, Is.TypeOf<ResolvedOpResult<T>>());
            return (ResolvedOpResult<T>)result;
        }

        private sealed class TestRuntime
        {
            public TestRuntime(
                RuleDispatcher dispatcher,
                ScriptedRollService rolls,
                TestTargeting targeting
            )
            {
                Dispatcher = dispatcher;
                Rolls = rolls;
                Targeting = targeting;
            }

            public RuleDispatcher Dispatcher { get; }
            public ScriptedRollService Rolls { get; }
            public TestTargeting Targeting { get; }
        }

        private sealed class TestCatalog : IActionCatalog, IStrikeActionCatalog
        {
            private readonly StrikeItemDefinition item;

            public TestCatalog(StrikeItemDefinition item) => this.item = item;

            public ActionProfile GetBaseProfile(ActionDefinitionId definitionId) =>
                throw new InvalidOperationException("Selected-item actions override this lookup.");

            public StrikeItemDefinition GetStrikeItem(ItemId itemId)
            {
                if (itemId != item.Item)
                    throw new KeyNotFoundException();
                return item;
            }
        }

        private sealed class TestTargeting : IStrikeTargetingProvider
        {
            public StrikeTargetingOutcome Result { get; set; } =
                StrikeTargetingOutcome.Legal(5, 0, 0, false);

            public StrikeTargetingOutcome Evaluate(
                RulesSnapshot snapshot,
                CreatureId actor,
                StrikeItemDefinition item,
                CreatureId target
            ) => Result;
        }

        private sealed class FixedResolutionDataProvider : IStrikeResolutionDataProvider
        {
            private readonly StrikeResolutionData data;

            public FixedResolutionDataProvider(StrikeResolutionData data) => this.data = data;

            public StrikeResolutionData Capture(
                RulesSnapshot snapshot,
                CreatureId actor,
                StrikeItemDefinition item,
                CreatureId target,
                LegalStrikeTargetingOutcome targeting
            ) => data;
        }
    }
}
