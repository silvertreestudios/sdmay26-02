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
        private static readonly ItemId ExtraWeapon = new ItemId("extra-weapon");
        private static readonly ItemId ExtraAmmo = new ItemId("extra-ammo");

        /// <summary>Identifies one conflicting Strike registration replay state.</summary>
        public enum StrikeRegistrationConflict
        {
            /// <summary>The actor owns an unrequested Strike item.</summary>
            ExtraEquipment,

            /// <summary>The actor owns an unrequested ammunition pool.</summary>
            ExtraAmmunition,

            /// <summary>One expected Strike item is absent.</summary>
            MissingEquipment,

            /// <summary>One expected Strike item has different state.</summary>
            ChangedEquipment,

            /// <summary>One expected ammunition pool has different state.</summary>
            ChangedAmmunition,

            /// <summary>The contribution-owned MAP state is absent.</summary>
            MissingMap,

            /// <summary>The contribution-owned MAP state has already advanced.</summary>
            ChangedMap,
        }

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
            CapturingAttackCheckObserver observer = new();
            runtime.Dispatcher.RegisterResolvedOpObserver<AttackCheckOp, CheckOutcome>(observer);

            StrikeResolution resolution = AssertResolved(
                await runtime.Dispatcher.Dispatch(new StrikeActionOp(Actor, Weapon, Target))
            ).Value;

            Assert.That(observer.Operation, Is.Not.Null);
            Assert.That(observer.Operation.Attacker, Is.EqualTo(Actor));
            Assert.That(observer.Operation.Target, Is.EqualTo(Target));
            Assert.That(observer.Outcome.Roll, Is.SameAs(resolution.AttackRoll));
            Assert.That(observer.Outcome.Modifiers.Total, Is.EqualTo(resolution.AttackModifier));
            Assert.That(observer.Outcome.Degree, Is.EqualTo(resolution.Degree));
        }

        [TestCase(2, false, 17, 2)]
        [TestCase(0, true, 13, -2)]
        public async Task ContextualDefenseAdjustmentIsCollectedExactlyOnce(
            int coverBonus,
            bool offGuard,
            int expectedArmorClass,
            int expectedCircumstanceValue
        )
        {
            TestRuntime runtime = CreateRuntime(new ScriptedRollService(1));
            runtime.Targeting.Result = StrikeTargetingOutcome.Legal(5, 0, coverBonus, offGuard);
            CapturingDefenseCollectionObserver observer = new();
            runtime.Dispatcher.RegisterResolvedOpObserver<
                CollectDefenseModifiersOp,
                ModifierCollection
            >(observer);

            StrikeResolution resolution = AssertResolved(
                await runtime.Dispatcher.Dispatch(new StrikeActionOp(Actor, Weapon, Target))
            ).Value;

            Assert.That(resolution.ArmorClass, Is.EqualTo(expectedArmorClass));
            Assert.That(observer.Collection, Is.Not.Null);
            Modifier[] circumstance = observer
                .Collection.Applied.Where(modifier => modifier.Type == ModifierType.Circumstance)
                .ToArray();
            Assert.That(circumstance.Length, Is.EqualTo(1));
            Assert.That(circumstance[0].Value, Is.EqualTo(expectedCircumstanceValue));
            Assert.That(
                observer.Collection.Suppressed.Where(modifier =>
                    modifier.Type == ModifierType.Circumstance
                ),
                Is.Empty
            );
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
                        Array.Empty<TypedDamageImmunity>(),
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

        [Test]
        public async Task StrikeRegistrationExactReplayRequiresCompleteOwnedCollectionsAndMap()
        {
            StrikeCombatantRegistration registration = CreateStrikeRegistration();
            RuleDispatcher dispatcher = CreateRegistrationDispatcher(registration);
            RulesSnapshot before = dispatcher.Snapshot;

            ResolvedOpResult<bool> replay = AssertResolved(
                await dispatcher.Dispatch(new RegisterStrikeCombatantOp(registration))
            );

            Assert.That(replay.Value, Is.False);
            Assert.That(replay.Facts, Is.Empty);
            Assert.That(dispatcher.Snapshot, Is.SameAs(before));
            Assert.That(dispatcher.Snapshot.Version, Is.EqualTo(before.Version));
        }

        [TestCase(StrikeRegistrationConflict.ExtraEquipment)]
        [TestCase(StrikeRegistrationConflict.ExtraAmmunition)]
        [TestCase(StrikeRegistrationConflict.MissingEquipment)]
        [TestCase(StrikeRegistrationConflict.ChangedEquipment)]
        [TestCase(StrikeRegistrationConflict.ChangedAmmunition)]
        [TestCase(StrikeRegistrationConflict.MissingMap)]
        [TestCase(StrikeRegistrationConflict.ChangedMap)]
        public void StrikeRegistrationRejectsIncompleteOrConflictingExactReplay(
            StrikeRegistrationConflict conflict
        )
        {
            StrikeCombatantRegistration registration = CreateStrikeRegistration();
            RuleDispatcher dispatcher = CreateRegistrationDispatcher(registration, conflict);
            RulesSnapshot before = dispatcher.Snapshot;

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await dispatcher.Dispatch(new RegisterStrikeCombatantOp(registration))
            );

            Assert.That(dispatcher.Snapshot, Is.SameAs(before));
            Assert.That(dispatcher.Snapshot.Version, Is.EqualTo(before.Version));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void StrikeRegistrationRejectsDuplicateOwnedIdentifiers(bool equipment)
        {
            EquipmentState item = CreateEquipment();
            AmmunitionState pool = new AmmunitionState(Ammo, Actor, 2);

            Assert.Throws<ArgumentException>(() =>
                new StrikeCombatantRegistration(
                    Actor,
                    equipment ? new[] { item, item } : new[] { item },
                    equipment ? new[] { pool } : new[] { pool, pool }
                )
            );
        }

        private static StrikeCombatantRegistration CreateStrikeRegistration() =>
            new StrikeCombatantRegistration(
                Actor,
                new[] { CreateEquipment() },
                new[] { new AmmunitionState(Ammo, Actor, 2) }
            );

        private static EquipmentState CreateEquipment(bool loaded = true) =>
            new EquipmentState(Weapon, new ItemDefinitionId("test-sword"), Actor, true, loaded);

        private static RuleDispatcher CreateRegistrationDispatcher(
            StrikeCombatantRegistration expected,
            StrikeRegistrationConflict? conflict = null
        )
        {
            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Actor, new PlayerId("players")))
                .SeedCreature(new CreatureState(Target, new PlayerId("enemies")));
            if (conflict != StrikeRegistrationConflict.MissingEquipment)
            {
                seed.SeedEquipment(
                    conflict == StrikeRegistrationConflict.ChangedEquipment
                        ? CreateEquipment(false)
                        : expected.Equipment.Single()
                );
            }
            seed.SeedAmmunition(
                conflict == StrikeRegistrationConflict.ChangedAmmunition
                    ? new AmmunitionState(Ammo, Actor, 1)
                    : expected.Ammunition.Single()
            );
            if (conflict == StrikeRegistrationConflict.ExtraEquipment)
            {
                seed.SeedEquipment(
                    new EquipmentState(
                        ExtraWeapon,
                        new ItemDefinitionId("extra-sword"),
                        Actor,
                        true,
                        true
                    )
                );
            }
            if (conflict == StrikeRegistrationConflict.ExtraAmmunition)
                seed.SeedAmmunition(new AmmunitionState(ExtraAmmo, Actor, 1));
            if (conflict != StrikeRegistrationConflict.MissingMap)
            {
                seed.SeedMultipleAttackPenalty(
                    Actor,
                    new MultipleAttackPenaltyState(
                        conflict == StrikeRegistrationConflict.ChangedMap ? 1 : 0
                    )
                );
            }
            StrikeItemDefinition definition = CreateItem(
                reloadActions: 1,
                ammunition: StrikeAmmunitionRequirement.Required(Ammo)
            );
            TestCatalog catalog = new TestCatalog(definition);
            return new RuleDispatcherBuilder(
                new InMemoryRulesStore(seed),
                new ScriptedRollService()
            )
                .UseMultipleAttackPenaltyRules()
                .UseActionLifecycle(catalog)
                .UseCheckResolution()
                .UseStrikeRules(
                    catalog,
                    new TestTargeting(),
                    new FixedResolutionDataProvider(
                        new StrikeResolutionData(
                            15,
                            Array.Empty<Modifier>(),
                            Array.Empty<TypedDamageDice>(),
                            Array.Empty<TypedFlatDamage>(),
                            Array.Empty<TypedDamageImmunity>(),
                            Array.Empty<TypedDefenseAdjustment>(),
                            Array.Empty<TypedDefenseAdjustment>()
                        )
                    )
                )
                .Build();
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
            RulesStateSeed seed = new RulesStateSeed()
                .SeedPreparedInputs(Actor, PreparedCreatureInputs.Empty)
                .SeedPreparedInputs(Target, PreparedCreatureInputs.Empty);
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
                        Array.Empty<TypedDamageImmunity>(),
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
                .UsePreparedContributions()
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

        private sealed class CapturingDefenseCollectionObserver
            : IResolvedOpObserver<CollectDefenseModifiersOp, ModifierCollection>
        {
            public ModifierCollection Collection { get; private set; }

            public ValueTask OnOperationResolved(
                CollectDefenseModifiersOp operation,
                ModifierCollection result,
                RulesSnapshot currentSnapshot
            )
            {
                Collection = result;
                return default;
            }
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
