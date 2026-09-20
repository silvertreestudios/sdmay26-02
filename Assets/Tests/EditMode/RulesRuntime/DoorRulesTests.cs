using System;
using System.Linq;
using System.Threading.Tasks;
using Game.Rules.Runtime;
using NUnit.Framework;

namespace Game.Tests.EditMode.RulesRuntime
{
    /// <summary>Verifies authoritative door state and Open Door's Interact lifecycle.</summary>
    public sealed class DoorRulesTests
    {
        private static readonly DoorId Door = new("door-a");
        private static readonly CreatureId Actor = new("actor");
        private static readonly CreatureId Enemy = new("enemy");
        private static readonly PlayerId Party = new("party");
        private static readonly PlayerId Opposition = new("opposition");
        private static readonly EncounterId Encounter = new("door-encounter");
        private static readonly GridPosition ActorCell = new(1, 0, 1);
        private static readonly GridPosition DoorCell = new(2, 0, 1);

        [Test]
        public void OrdinaryAdjacentDoorCommitsExactlyOneAuthoritativeChange()
        {
            DoorWorldRuntime world = CreateDoorWorld();
            RecordingDoorObserver observer = new();
            using IDisposable registration = world.RegisterObserver(observer);

            OpResult<DoorOpenedOutcome> result = world.Open(
                new DoorOpenRequest(Door, ActorCell, true, true)
            );

            ResolvedOpResult<DoorOpenedOutcome> resolved = RequireResolved(result);
            Assert.That(world.IsOpen(Door), Is.True);
            Assert.That(world.CaptureOpenDoorIds(), Is.EqualTo(new[] { Door.Value }));
            Assert.That(resolved.Facts.OfType<DoorOpenedFact>().Count(), Is.EqualTo(1));
            Assert.That(observer.Calls, Is.EqualTo(1));
            Assert.That(observer.SawOpenState, Is.True);
        }

        [TestCase(DoorOpenRejection.ActorIsNotPlayerCharacter)]
        [TestCase(DoorOpenRejection.ActorIsNotAlive)]
        [TestCase(DoorOpenRejection.ActorIsNotAdjacent)]
        [TestCase(DoorOpenRejection.DoorAlreadyOpen)]
        [TestCase(DoorOpenRejection.DoorDoesNotExist)]
        public void InvalidWorldInteractionMutatesNothing(DoorOpenRejection rejection)
        {
            DoorWorldRuntime world = CreateDoorWorld(
                isOpen: rejection == DoorOpenRejection.DoorAlreadyOpen
            );
            DoorId selected =
                rejection == DoorOpenRejection.DoorDoesNotExist ? new DoorId("missing") : Door;
            DoorOpenRequest request = new(
                selected,
                rejection == DoorOpenRejection.ActorIsNotAdjacent
                    ? new GridPosition(0, 0, 0)
                    : ActorCell,
                rejection != DoorOpenRejection.ActorIsNotPlayerCharacter,
                rejection != DoorOpenRejection.ActorIsNotAlive
            );
            long version = world.Snapshot.Version;
            bool wasOpen = world.IsOpen(Door);

            DoorOpenValidation validation = world.Validate(request);
            OpResult<DoorOpenedOutcome> result = world.Open(request);

            Assert.That(validation.Rejection, Is.EqualTo(rejection));
            Assert.That(result, Is.TypeOf<InvalidOpResult<DoorOpenedOutcome>>());
            Assert.That(world.Snapshot.Version, Is.EqualTo(version));
            Assert.That(world.IsOpen(Door), Is.EqualTo(wasOpen));
        }

        [Test]
        public async Task EncounterInteractUsesOneActionManipulateAndLifecycleExactlyOnce()
        {
            DoorWorldRuntime world = CreateDoorWorld();
            RuleDispatcher encounter = CreateEncounterDispatcher(world);
            DoorInteractActionDefinition definition = new();
            ActionProfile profile = definition.GetBaseProfile(
                DoorInteractActionDefinition.DefinitionId
            );

            OpResult<DoorOpenedOutcome> result = await encounter.Dispatch(
                new DoorInteractActionOp(Actor, Door)
            );

            ResolvedOpResult<DoorOpenedOutcome> resolved = RequireResolved(result);
            Assert.That(profile.Cost, Is.EqualTo(ActionCost.One));
            Assert.That(profile.HasTrait(Trait.FromSlug("manipulate")), Is.True);
            Assert.That(encounter.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(2));
            Assert.That(world.IsOpen(Door), Is.True);
            Assert.That(resolved.Facts.OfType<ActionCostSpentFact>().Count(), Is.EqualTo(1));
            Assert.That(
                resolved.Facts.OfType<ActionBegunFact<DoorOpenedOutcome>>().Count(),
                Is.EqualTo(1)
            );
            Assert.That(
                resolved.Facts.OfType<ActionResolvedFact<DoorOpenedOutcome>>().Count(),
                Is.EqualTo(1)
            );
        }

        [Test]
        public async Task RejectedEncounterInteractChargesNothingAndMutatesNothing()
        {
            DoorWorldRuntime world = CreateDoorWorld(isOpen: true);
            RuleDispatcher encounter = CreateEncounterDispatcher(world);
            long doorVersion = world.Snapshot.Version;
            long encounterVersion = encounter.Snapshot.Version;

            OpResult<DoorOpenedOutcome> result = await encounter.Dispatch(
                new DoorInteractActionOp(Actor, Door)
            );

            Assert.That(result, Is.TypeOf<InvalidOpResult<DoorOpenedOutcome>>());
            Assert.That(encounter.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(3));
            Assert.That(encounter.Snapshot.Version, Is.EqualTo(encounterVersion));
            Assert.That(world.Snapshot.Version, Is.EqualTo(doorVersion));
            Assert.That(world.IsOpen(Door), Is.True);
            Assert.That(result.Facts, Is.Empty);
        }

        private static DoorWorldRuntime CreateDoorWorld(bool isOpen = false) =>
            new(new[] { new DoorState(Door, DoorCell, isOpen) }, new ScriptedRollService());

        private static RuleDispatcher CreateEncounterDispatcher(DoorWorldRuntime world)
        {
            DoorInteractActionDefinition definition = new();
            RuleRegistryBuilder registryBuilder = new();
            registryBuilder.AddOutcomeRule();
            RuleRegistry registry = registryBuilder.Build();
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                new InMemoryRulesStore(new RulesStateSeed()),
                new ScriptedRollService(20, 1)
            )
                .UseHealthRules()
                .UseMultipleAttackPenaltyRules()
                .UseActiveEffectRules(registry)
                .UseMovementBudgetResetRules()
                .UseEncounterRules(registry)
                .UseActionLifecycle(definition)
                .UseDoorInteractRules(world)
                .Build();
            RequireResolved(
                dispatcher
                    .Dispatch(new InitEncounterOp(Encounter, Party))
                    .AsTask()
                    .GetAwaiter()
                    .GetResult()
            );
            RequireResolved(
                dispatcher
                    .Dispatch(
                        new AddCombatantsOp(
                            Encounter,
                            new[]
                            {
                                Registration(Actor, Party, ActorCell, 100),
                                Registration(Enemy, Opposition, new GridPosition(5, 0, 5), 0),
                            }
                        )
                    )
                    .AsTask()
                    .GetAwaiter()
                    .GetResult()
            );
            RequireResolved(
                dispatcher
                    .Dispatch(new AdvanceEncounterOp(Encounter))
                    .AsTask()
                    .GetAwaiter()
                    .GetResult()
            );
            return dispatcher;
        }

        private static CombatantRulesState Registration(
            CreatureId creature,
            PlayerId player,
            GridPosition position,
            int initiativeModifier
        ) =>
            new(
                new CreatureState(creature, player),
                new HealthState(10, 10),
                position,
                new GridDistance(25),
                initiativeModifier,
                Array.Empty<SpellSlotState>(),
                Array.Empty<ActiveRuleBinding>(),
                Array.Empty<EquipmentState>(),
                Array.Empty<AmmunitionState>(),
                Array.Empty<ActiveEffectInstance>()
            );

        private static ResolvedOpResult<TResult> RequireResolved<TResult>(OpResult<TResult> result)
        {
            string failure = result is InvalidOpResult<TResult> invalid
                ? invalid.Reason
                : "The operation did not resolve.";
            Assert.That(result, Is.TypeOf<ResolvedOpResult<TResult>>(), failure);
            return (ResolvedOpResult<TResult>)result;
        }

        private sealed class RecordingDoorObserver : IFactObserver<DoorOpenedFact>
        {
            internal int Calls { get; private set; }
            internal bool SawOpenState { get; private set; }

            public void OnFactCommitted(
                DoorOpenedFact fact,
                OpId observationRootId,
                RulesSnapshot currentSnapshot
            )
            {
                Calls++;
                SawOpenState =
                    DoorRules.TryGetDoor(currentSnapshot, fact.Door, out DoorState door)
                    && door.IsOpen;
            }
        }
    }
}
