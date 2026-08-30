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

            OpResult<StrikeResolution> result = await runtime.Dispatcher.Dispatch(
                new StrikeActionOp(Actor, Weapon, Target)
            );

            ResolvedOpResult<StrikeResolution> resolved = AssertResolved(result);
            Assert.That(resolved.Value.Degree, Is.EqualTo(DegreeOfSuccess.Success));
            Assert.That(resolved.Value.FinalDamage, Is.EqualTo(6));
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
        public async Task StrikeRejectsMissingMapBeforeCostsOrHealthCommit()
        {
            TestRuntime runtime = CreateRuntime(new ScriptedRollService(20, 4), seedMap: false);
            RulesSnapshot before = runtime.Dispatcher.Snapshot;

            OpResult<StrikeResolution> result = await runtime.Dispatcher.Dispatch(
                new StrikeActionOp(Actor, Weapon, Target)
            );

            Assert.That(result, Is.TypeOf<InvalidOpResult<StrikeResolution>>());
            Assert.That(
                ((InvalidOpResult<StrikeResolution>)result).Reason,
                Does.Contain("multiple-attack-penalty")
            );
            Assert.That(runtime.Dispatcher.Snapshot.Version, Is.EqualTo(before.Version));
            Assert.That(
                runtime.Dispatcher.Snapshot.Health[Target],
                Is.EqualTo(before.Health[Target])
            );
            Assert.That(
                runtime.Dispatcher.Snapshot.ActionEconomy[Actor],
                Is.EqualTo(before.ActionEconomy[Actor])
            );
        }

        [Test]
        public async Task StrikeResolvesItsAttackThroughSharedAttackCheck()
        {
            TestRuntime runtime = CreateRuntime(new ScriptedRollService(10, 4));

            StrikeResolution resolution = AssertResolved(
                await runtime.Dispatcher.Dispatch(new StrikeActionOp(Actor, Weapon, Target))
            ).Value;

            Assert.That(runtime.Dispatcher.Diagnostics.Compact, Does.Contain("AttackCheckOp"));
            Assert.That(resolution.AttackRoll.Total, Is.EqualTo(10));
        }

        [Test]
        public async Task MissStillSpendsActionAndAdvancesMapWithoutDamage()
        {
            TestRuntime runtime = CreateRuntime(new ScriptedRollService(2));

            ResolvedOpResult<StrikeResolution> result = AssertResolved(
                await runtime.Dispatcher.Dispatch(new StrikeActionOp(Actor, Weapon, Target))
            );

            Assert.That(result.Value.Hit, Is.False);
            Assert.That(runtime.Dispatcher.Snapshot.Health[Target].Current, Is.EqualTo(20));
            Assert.That(
                runtime.Dispatcher.Snapshot.ActionEconomy[Actor].ActionsRemaining,
                Is.EqualTo(2)
            );
            Assert.That(
                runtime.Dispatcher.Snapshot.MultipleAttackPenalty[Actor].AttackCount,
                Is.EqualTo(1)
            );
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
            ).Value;

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
            ).Value;

            Assert.That(resolution.FinalDamage, Is.EqualTo(21));
            Assert.That(runtime.Rolls.Remaining, Is.Zero);
        }

        [Test]
        public async Task WeaknessAndResistanceApplyPerDamageTypeAfterCritical()
        {
            TestRuntime runtime = CreateRuntime(
                new ScriptedRollService(10, 4),
                resolutionDataProvider: new FixedResolutionDataProvider(
                    new StrikeResolutionData(
                        15,
                        Array.Empty<Modifier>(),
                        Array.Empty<TypedDamageDice>(),
                        Array.Empty<TypedFlatDamage>(),
                        new[] { new TypedDefenseAdjustment("slashing", 3) },
                        new[] { new TypedDefenseAdjustment("slashing", 1) }
                    )
                )
            );

            StrikeResolution resolution = AssertResolved(
                await runtime.Dispatcher.Dispatch(new StrikeActionOp(Actor, Weapon, Target))
            ).Value;

            Assert.That(resolution.FinalDamage, Is.EqualTo(8));
            Assert.That(resolution.Damage.Single().Amount, Is.EqualTo(8));
        }

        [Test]
        public async Task InvalidTargetingSpendsNothingAndRollsNothing()
        {
            TestRuntime runtime = CreateRuntime(new ScriptedRollService(20));
            runtime.Targeting.Result = StrikeTargetingOutcome.Invalid("blocked");

            OpResult<StrikeResolution> result = await runtime.Dispatcher.Dispatch(
                new StrikeActionOp(Actor, Weapon, Target)
            );

            Assert.That(result, Is.TypeOf<InvalidOpResult<StrikeResolution>>());
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
        public void ResolveStrikeRejectsExternalDispatchBeforeRolling()
        {
            TestRuntime runtime = CreateRuntime(new ScriptedRollService(20, 4));

            InvalidOperationException failure = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await runtime.Dispatcher.Dispatch(new ResolveStrikeOp(Actor, Weapon, Target))
            );

            Assert.That(failure.Message, Does.Contain("nested-only"));
            Assert.That(runtime.Rolls.Remaining, Is.EqualTo(2));
            Assert.That(runtime.Dispatcher.Snapshot.Health[Target].Current, Is.EqualTo(20));
        }

        [Test]
        public async Task InvalidArmorClassSpendsNothingAndDoesNotPartiallyResolveStrike()
        {
            StrikeItemDefinition ranged = CreateItem(
                reloadActions: 1,
                ammunition: StrikeAmmunitionRequirement.Required(Ammo)
            );
            TestRuntime runtime = CreateRuntime(
                new ScriptedRollService(20),
                ranged,
                ammo: 2,
                resolutionDataProvider: new InvalidArmorClassResolutionDataProvider()
            );

            OpResult<StrikeResolution> result = await runtime.Dispatcher.Dispatch(
                new StrikeActionOp(Actor, Weapon, Target)
            );

            Assert.That(result, Is.TypeOf<InvalidOpResult<StrikeResolution>>());
            Assert.That(
                ((InvalidOpResult<StrikeResolution>)result).Reason,
                Does.Contain("Armor Class")
            );
            Assert.That(
                runtime.Dispatcher.Snapshot.ActionEconomy[Actor].ActionsRemaining,
                Is.EqualTo(3)
            );
            Assert.That(runtime.Dispatcher.Snapshot.Ammunition[Ammo].Remaining, Is.EqualTo(2));
            Assert.That(runtime.Dispatcher.Snapshot.Equipment[Weapon].IsLoaded, Is.True);
            Assert.That(runtime.Dispatcher.Snapshot.Health[Target].Current, Is.EqualTo(20));
            Assert.That(
                runtime.Dispatcher.Snapshot.MultipleAttackPenalty[Actor].AttackCount,
                Is.Zero
            );
            Assert.That(runtime.Rolls.Remaining, Is.EqualTo(1));
            Assert.That(result.Facts, Is.Empty);
        }

        [Test]
        public async Task AgileMapUsesZeroFourEight()
        {
            StrikeItemDefinition agile = CreateItem(traits: new[] { Trait.FromSlug("agile") });
            TestRuntime runtime = CreateRuntime(new ScriptedRollService(2, 2, 2, 2), agile);

            StrikeResolution first = AssertResolved(
                await runtime.Dispatcher.Dispatch(new StrikeActionOp(Actor, Weapon, Target))
            ).Value;
            StrikeResolution second = AssertResolved(
                await runtime.Dispatcher.Dispatch(new StrikeActionOp(Actor, Weapon, Target))
            ).Value;
            StrikeResolution third = AssertResolved(
                await runtime.Dispatcher.Dispatch(new StrikeActionOp(Actor, Weapon, Target))
            ).Value;

            Assert.That(first.MultipleAttackPenalty, Is.Zero);
            Assert.That(second.MultipleAttackPenalty, Is.EqualTo(-4));
            Assert.That(third.MultipleAttackPenalty, Is.EqualTo(-8));
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

            OpResult<StrikeResolution> blocked = await runtime.Dispatcher.Dispatch(
                new StrikeActionOp(Actor, Weapon, Target)
            );
            Assert.That(blocked, Is.TypeOf<InvalidOpResult<StrikeResolution>>());
            Assert.That(runtime.Dispatcher.Snapshot.Ammunition[Ammo].Remaining, Is.EqualTo(1));
            Assert.That(
                runtime.Dispatcher.Snapshot.ActionEconomy[Actor].ActionsRemaining,
                Is.EqualTo(2)
            );

            ResolvedOpResult<EquipmentState> reload = AssertResolved(
                await runtime.Dispatcher.Dispatch(new ReloadActionOp(Actor, Weapon))
            );
            Assert.That(reload.Value, Is.SameAs(runtime.Dispatcher.Snapshot.Equipment[Weapon]));
            Assert.That(reload.Value.Id, Is.EqualTo(Weapon));
            Assert.That(reload.Value.IsLoaded, Is.True);
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

            OpResult<EquipmentState> result = await runtime.Dispatcher.Dispatch(
                new ReloadActionOp(Actor, Weapon)
            );

            Assert.That(result, Is.TypeOf<InvalidOpResult<EquipmentState>>());
            Assert.That(
                runtime.Dispatcher.Snapshot.ActionEconomy[Actor].ActionsRemaining,
                Is.EqualTo(3)
            );
            Assert.That(runtime.Dispatcher.Snapshot.Equipment[Weapon].IsLoaded, Is.False);
        }

        [TestCase(false, 20, "not registered")]
        [TestCase(true, 0, "cannot act")]
        public async Task ReloadRejectsUnregisteredOrDefeatedActorWithoutMutation(
            bool registerActor,
            int actorHp,
            string reason
        )
        {
            StrikeItemDefinition ranged = CreateItem(
                reloadActions: 1,
                ammunition: StrikeAmmunitionRequirement.Required(Ammo)
            );
            TestRuntime runtime = CreateRuntime(
                new ScriptedRollService(),
                ranged,
                ammo: 2,
                loaded: false,
                registerActor: registerActor,
                actorHp: actorHp
            );

            OpResult<EquipmentState> result = await runtime.Dispatcher.Dispatch(
                new ReloadActionOp(Actor, Weapon)
            );

            Assert.That(result, Is.TypeOf<InvalidOpResult<EquipmentState>>());
            Assert.That(((InvalidOpResult<EquipmentState>)result).Reason, Does.Contain(reason));
            Assert.That(
                runtime.Dispatcher.Snapshot.ActionEconomy[Actor].ActionsRemaining,
                Is.EqualTo(3)
            );
            Assert.That(runtime.Dispatcher.Snapshot.Equipment[Weapon].IsLoaded, Is.False);
            Assert.That(runtime.Dispatcher.Snapshot.Ammunition[Ammo].Remaining, Is.EqualTo(2));
            Assert.That(result.Facts, Is.Empty);
        }

        private static TestRuntime CreateRuntime(
            ScriptedRollService rolls,
            StrikeItemDefinition item = null,
            int targetHp = 20,
            int ammo = -1,
            bool loaded = true,
            IStrikeResolutionDataProvider resolutionDataProvider = null,
            bool registerActor = true,
            int actorHp = 20,
            bool seedMap = true
        )
        {
            item ??= CreateItem();
            RulesStateSeed seed = new RulesStateSeed();
            if (registerActor)
                seed.SeedCreature(new CreatureState(Actor, new PlayerId("players")));
            seed.SeedCreature(new CreatureState(Target, new PlayerId("enemies")))
                .SeedHealth(Actor, new HealthState(actorHp, 20))
                .SeedHealth(Target, new HealthState(targetHp, targetHp))
                .SeedActionEconomy(Actor, new ActionEconomyState(3, true))
                .SeedActionEconomy(Target, new ActionEconomyState(0, true))
                .SeedEquipment(new EquipmentState(item.Item, item.Definition, Actor, true, loaded));
            if (seedMap)
                seed.SeedMultipleAttackPenalty(Actor, new MultipleAttackPenaltyState(0));
            if (ammo >= 0)
                seed.SeedAmmunition(new AmmunitionState(Ammo, Actor, ammo));

            TestCatalog catalog = new TestCatalog(item);
            TestTargeting targeting = new TestTargeting();
            IStrikeResolutionDataProvider data =
                resolutionDataProvider
                ?? new FixedResolutionDataProvider(
                    new StrikeResolutionData(
                        15,
                        Array.Empty<Modifier>(),
                        Array.Empty<TypedDamageDice>(),
                        Array.Empty<TypedFlatDamage>(),
                        Array.Empty<TypedDefenseAdjustment>(),
                        Array.Empty<TypedDefenseAdjustment>()
                    )
                );
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                new InMemoryRulesStore(seed),
                rolls
            )
                .UseHealthRules()
                .UseMultipleAttackPenaltyRules()
                .UseActionLifecycle(catalog)
                .UseCheckResolution()
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
                new[] { new TypedDamageDice(new DiceExpression(1, 6), "slashing", "Test Sword") },
                new[] { new TypedFlatDamage(2, "slashing", "Strength") },
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

            public ActionValidationResult Validate(
                RulesSnapshot snapshot,
                CreatureId actor,
                StrikeItemDefinition item,
                CreatureId target,
                LegalStrikeTargetingOutcome targeting
            ) => ActionValidationResult.Valid;

            public StrikeResolutionData Capture(
                RulesSnapshot snapshot,
                CreatureId actor,
                StrikeItemDefinition item,
                CreatureId target,
                LegalStrikeTargetingOutcome targeting
            ) => data;
        }

        private sealed class InvalidArmorClassResolutionDataProvider : IStrikeResolutionDataProvider
        {
            public ActionValidationResult Validate(
                RulesSnapshot snapshot,
                CreatureId actor,
                StrikeItemDefinition item,
                CreatureId target,
                LegalStrikeTargetingOutcome targeting
            ) => ActionValidationResult.Invalid("The target's Armor Class must be positive.");

            public StrikeResolutionData Capture(
                RulesSnapshot snapshot,
                CreatureId actor,
                StrikeItemDefinition item,
                CreatureId target,
                LegalStrikeTargetingOutcome targeting
            ) => throw new InvalidOperationException("Invalid resolution data was captured.");
        }
    }
}
