using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    /// <summary>Verifies engine-issued occupied-space authority and every scoped reuse boundary.</summary>
    public sealed class MovementPermissionTests
    {
        private static readonly CreatureId Mover = new CreatureId("permission-mover");
        private static readonly CreatureId Occupant = new CreatureId("permission-occupant");
        private static readonly CreatureId OtherOccupant = new CreatureId(
            "permission-other-occupant"
        );
        private static readonly ActionDefinitionId ActionDefinition = new ActionDefinitionId(
            "permission-action"
        );
        private static readonly RuleDefinitionId MiddlewareDefinition = new RuleDefinitionId(
            "permission-departure-middleware"
        );
        private static readonly BindingId MiddlewareBinding = new BindingId(
            "permission-departure-binding"
        );
        private static readonly MovementPermissionPurpose Purpose =
            MovementPermissionPurpose.FromSlug("occupied-crossing-test");
        private static readonly MovementPermissionPurpose OtherPurpose =
            MovementPermissionPurpose.FromSlug("different-crossing-test");
        private static readonly RuleSource TestSource = RuleSource.FromSlug("permission-test");
        private static readonly GridPosition Origin = new GridPosition(0, 0, 0);
        private static readonly GridPosition Occupied = new GridPosition(1, 0, 0);
        private static readonly GridPosition SecondIntermediate = new GridPosition(2, 0, 0);
        private static readonly GridPosition Exit = new GridPosition(3, 0, 0);
        private static readonly MovementPath CrossingPath = new MovementPath(
            Origin,
            new[] { Occupied, SecondIntermediate, Exit }
        );

        [Test]
        public async Task MatchingPermissionCommitsOneTraversalFactAndCannotBeReused()
        {
            PermissionActionHandler handler = new PermissionActionHandler(
                PermissionScenario.SuccessThenReuse
            );
            InMemoryRulesStore store = CreateStore();
            RuleDispatcher dispatcher = CreateDispatcher(store, handler);
            CrossingSnapshotObserver observer = new CrossingSnapshotObserver();
            dispatcher.RegisterFactObserver<TokenMovedFact>(observer);
            dispatcher.RegisterFactObserver<OccupiedSpaceTraversedFact>(observer);

            OpResult<bool> result = await dispatcher.Dispatch(new PermissionActionOp());

            Assert.That(RequireResolved(result).Value, Is.True);
            Assert.That(handler.FirstMove.ReachedDestination, Is.True);
            Assert.That(handler.SecondMove.Status, Is.EqualTo(MovePathStatus.Stopped));
            Assert.That(
                handler.SecondMove.Failure.PermissionFailure,
                Is.EqualTo(MovementPermissionFailureKind.Reused)
            );
            Assert.That(store.Snapshot.Positions[Mover], Is.EqualTo(Exit));
            Assert.That(result.Facts.OfType<TokenMovedFact>().Count(), Is.EqualTo(3));
            Assert.That(
                result.Facts.OfType<TokenMovedFact>().Select(fact => fact.Cost.Feet),
                Is.EqualTo(new[] { 10, 5, 5 })
            );
            Assert.That(store.Snapshot.MovementBudgets[Mover].Remaining.Feet, Is.EqualTo(10));
            OccupiedSpaceTraversedFact traversal = result
                .Facts.OfType<OccupiedSpaceTraversedFact>()
                .Single();
            Assert.That(traversal.Occupant, Is.EqualTo(Occupant));
            Assert.That(traversal.OccupiedPosition, Is.EqualTo(Occupied));
            Assert.That(traversal.Purpose, Is.EqualTo(Purpose));
            Assert.That(
                result.Facts.OfType<TokenMovedFact>().Count(fact => fact.To == Occupied),
                Is.EqualTo(1)
            );
            Assert.That(
                observer.Positions,
                Is.EqualTo(
                    new[] { SecondIntermediate, SecondIntermediate, SecondIntermediate, Exit }
                )
            );
            Assert.That(observer.UniquePositions, Is.All.True);
            Assert.That(handler.PositionsUniqueAtAllMoveBoundaries, Is.True);
        }

        [Test]
        public async Task CommittedTraversalConsumesPermissionWhenObserverThrows()
        {
            PermissionActionHandler handler = new PermissionActionHandler(
                PermissionScenario.ObserverFailureThenReuse
            );
            InMemoryRulesStore store = CreateStore();
            RuleDispatcher dispatcher = CreateDispatcher(store, handler);
            ThrowingTraversalObserver observer = new ThrowingTraversalObserver();
            dispatcher.RegisterFactObserver(observer);

            OpResult<bool> result = await dispatcher.Dispatch(new PermissionActionOp());

            Assert.That(RequireResolved(result).Value, Is.True);
            Assert.That(handler.CaughtObserverFailure, Is.True);
            Assert.That(handler.SecondMove.Status, Is.EqualTo(MovePathStatus.Stopped));
            Assert.That(
                handler.SecondMove.Failure.PermissionFailure,
                Is.EqualTo(MovementPermissionFailureKind.Reused)
            );
            Assert.That(store.Snapshot.Positions[Mover], Is.EqualTo(Origin));
            Assert.That(result.Facts.OfType<TokenMovedFact>().Count(), Is.EqualTo(2));
            Assert.That(result.Facts.OfType<OccupiedSpaceTraversedFact>().Count(), Is.EqualTo(1));
            Assert.That(result.Facts.OfType<TokenRelocatedFact>().Count(), Is.EqualTo(1));
            Assert.That(observer.ObservedMoverPosition, Is.EqualTo(SecondIntermediate));
            Assert.That(observer.ObservedUniquePositions, Is.True);
            Assert.That(handler.PositionsUniqueAtAllMoveBoundaries, Is.True);
        }

        [Test]
        public void OccupiedCrossingPreflightRejectsTopologyOnlyBudget()
        {
            MovementBudgetId budgetId = new MovementBudgetId(new OpId(10));
            InMemoryRulesStore store = new InMemoryRulesStore(
                new RulesStateSeed()
                    .SeedPosition(Mover, Origin)
                    .SeedPosition(Occupant, Occupied)
                    .SeedMovementBudget(
                        Mover,
                        new MovementBudgetState(
                            budgetId,
                            Mover,
                            new GridDistance(15),
                            DiagonalMovementPhase.NextCostsFiveFeet
                        )
                    )
            );
            MovementPathValidator validator = new MovementPathValidator(
                new GridTopology(
                    new GridBounds(new GridPosition(0, 0, 0), new GridPosition(4, 0, 4)),
                    Array.Empty<GridCell>()
                )
            );

            MovementPathValidation validation = validator.Validate(
                store.Snapshot,
                Mover,
                budgetId,
                CrossingPath,
                OccupiedTraversalAllowance.ForReservedPosition(Occupant, Occupied)
            );

            Assert.That(validation.IsValid, Is.False);
            Assert.That(
                validation.Failure.Kind,
                Is.EqualTo(MovementFailureKind.InsufficientMovement)
            );
            Assert.That(validation.Failure.StepNumber, Is.EqualTo(3));
            Assert.That(store.Snapshot.Version, Is.Zero);
        }

        [Test]
        public async Task DepartureRelocationOfReservedOccupantRemovesOccupiedSurcharge()
        {
            PermissionActionHandler handler = new PermissionActionHandler(
                PermissionScenario.OccupantRelocatesBeforeTraversal
            );
            InMemoryRulesStore store = CreateStore(seedMiddleware: true);
            RuleDispatcher dispatcher = CreateDispatcher(
                store,
                handler,
                new OccupantDepartureMiddleware(new GridPosition(4, 0, 3))
            );

            OpResult<bool> result = await dispatcher.Dispatch(new PermissionActionOp());

            Assert.That(RequireResolved(result).Value, Is.True);
            Assert.That(handler.FirstMove.ReachedDestination, Is.True);
            Assert.That(
                result.Facts.OfType<TokenMovedFact>().Select(fact => fact.Cost.Feet),
                Is.EqualTo(new[] { 5, 5, 5 })
            );
            Assert.That(result.Facts.OfType<OccupiedSpaceTraversedFact>(), Is.Empty);
            Assert.That(store.Snapshot.MovementBudgets[Mover].Remaining.Feet, Is.EqualTo(15));
        }

        [TestCase(DepartureStopResult.ResolvedInterruption, MovementFailureKind.TriggerInterrupted)]
        [TestCase(DepartureStopResult.Interrupted, MovementFailureKind.TriggerInterrupted)]
        [TestCase(DepartureStopResult.Cancelled, MovementFailureKind.TriggerCancelled)]
        [TestCase(DepartureStopResult.Invalid, MovementFailureKind.TriggerInvalid)]
        public async Task StopWhileLeavingReservedCellSettlesAtExitAndConsumesPermission(
            DepartureStopResult stopResult,
            MovementFailureKind expectedFailure
        )
        {
            PermissionActionHandler handler = new PermissionActionHandler(
                PermissionScenario.StopThenReuse
            );
            InMemoryRulesStore store = CreateStore(seedMiddleware: true);
            CrossingStopMiddleware middleware = new CrossingStopMiddleware(stopResult);
            RuleDispatcher dispatcher = CreateDispatcher(store, handler, middleware);

            OpResult<bool> result = await dispatcher.Dispatch(new PermissionActionOp());

            Assert.That(RequireResolved(result).Value, Is.True);
            Assert.That(handler.FirstMove.Status, Is.EqualTo(MovePathStatus.Stopped));
            Assert.That(handler.FirstMove.Failure.Kind, Is.EqualTo(expectedFailure));
            Assert.That(handler.FirstMove.Failure.StepNumber, Is.EqualTo(2));
            Assert.That(handler.FirstMove.FinalPosition, Is.EqualTo(SecondIntermediate));
            Assert.That(handler.FirstMove.CommittedSteps, Is.EqualTo(2));
            Assert.That(handler.FirstMove.DistanceSpent.Feet, Is.EqualTo(15));
            Assert.That(
                handler.SecondMove.Failure.PermissionFailure,
                Is.EqualTo(MovementPermissionFailureKind.Reused)
            );
            Assert.That(
                result.Facts.OfType<TokenMovedFact>().Select(fact => fact.Cost.Feet),
                Is.EqualTo(new[] { 10, 5 })
            );
            Assert.That(result.Facts.OfType<OccupiedSpaceTraversedFact>().Count(), Is.EqualTo(1));
            Assert.That(result.Facts.OfType<TokenRelocatedFact>().Count(), Is.EqualTo(1));
            Assert.That(store.Snapshot.Positions[Mover], Is.EqualTo(Origin));
            Assert.That(store.Snapshot.Positions[Occupant], Is.EqualTo(Occupied));
            Assert.That(store.Snapshot.MovementBudgets[Mover].Remaining.Feet, Is.EqualTo(15));
            Assert.That(store.Snapshot.Version, Is.EqualTo(3));
            Assert.That(middleware.AuthoritativePositions, Is.EqualTo(new[] { Origin, Origin }));
            Assert.That(middleware.DepartureCells, Is.EqualTo(new[] { Origin, Occupied }));
            Assert.That(handler.PositionsUniqueAtAllMoveBoundaries, Is.True);
        }

        [Test]
        public void ThrowingReservedExitMiddlewareCommitsNeitherHalfOfCrossing()
        {
            PermissionActionHandler handler = new PermissionActionHandler(
                PermissionScenario.ThrowingReservedExit
            );
            InMemoryRulesStore store = CreateStore(seedMiddleware: true);
            RuleDispatcher dispatcher = CreateDispatcher(
                store,
                handler,
                new ThrowingReservedExitMiddleware()
            );
            CrossingSnapshotObserver observer = new CrossingSnapshotObserver();
            dispatcher.RegisterFactObserver<TokenMovedFact>(observer);

            InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await dispatcher.Dispatch(new PermissionActionOp())
            );

            Assert.That(exception.Message, Is.EqualTo("injected reserved-exit failure"));
            Assert.That(store.Snapshot.Positions[Mover], Is.EqualTo(Origin));
            Assert.That(store.Snapshot.Positions[Occupant], Is.EqualTo(Occupied));
            Assert.That(store.Snapshot.MovementBudgets[Mover].Remaining.Feet, Is.EqualTo(30));
            Assert.That(store.Snapshot.Version, Is.EqualTo(1));
            Assert.That(observer.Positions, Is.Empty);
            Assert.That(handler.PositionsUniqueAtAllMoveBoundaries, Is.True);
            AssertUniquePositions(store.Snapshot);
        }

        [Test]
        public async Task ExitContentionRejectsWholeCrossingAndLeavesPermissionReusable()
        {
            PermissionActionHandler handler = new PermissionActionHandler(
                PermissionScenario.ExitContentionThenRetry
            );
            InMemoryRulesStore store = CreateStore(seedMiddleware: true);
            RuleDispatcher dispatcher = CreateDispatcher(
                store,
                handler,
                new OneTimeExitContentionMiddleware()
            );

            OpResult<bool> result = await dispatcher.Dispatch(new PermissionActionOp());

            Assert.That(RequireResolved(result).Value, Is.True);
            Assert.That(handler.FirstMove.Status, Is.EqualTo(MovePathStatus.Stopped));
            Assert.That(handler.FirstMove.Failure.Kind, Is.EqualTo(MovementFailureKind.Occupied));
            Assert.That(handler.FirstMove.Failure.StepNumber, Is.EqualTo(2));
            Assert.That(handler.FirstMove.FinalPosition, Is.EqualTo(Origin));
            Assert.That(handler.FirstMove.CommittedSteps, Is.Zero);
            Assert.That(handler.SecondMove.ReachedDestination, Is.True);
            Assert.That(
                handler.ThirdMove.Failure.PermissionFailure,
                Is.EqualTo(MovementPermissionFailureKind.Reused)
            );
            Assert.That(result.Facts.OfType<OccupiedSpaceTraversedFact>().Count(), Is.EqualTo(1));
            Assert.That(result.Facts.OfType<TokenMovedFact>().Count(), Is.EqualTo(3));
            Assert.That(store.Snapshot.Positions[Mover], Is.EqualTo(Origin));
            Assert.That(store.Snapshot.Positions[Occupant], Is.EqualTo(Occupied));
            Assert.That(
                store.Snapshot.Positions[OtherOccupant],
                Is.EqualTo(new GridPosition(4, 0, 4))
            );
            Assert.That(handler.PositionsUniqueAtAllMoveBoundaries, Is.True);
            AssertUniquePositions(store.Snapshot);
        }

        [TestCase(PermissionScenario.PathMismatch, MovementPermissionFailureKind.PathMismatch)]
        [TestCase(
            PermissionScenario.PurposeMismatch,
            MovementPermissionFailureKind.PurposeMismatch
        )]
        [TestCase(
            PermissionScenario.ParentMismatch,
            MovementPermissionFailureKind.ParentFrameMismatch
        )]
        [TestCase(PermissionScenario.BudgetMismatch, MovementPermissionFailureKind.BudgetMismatch)]
        [TestCase(
            PermissionScenario.OccupantMismatch,
            MovementPermissionFailureKind.OccupantMismatch
        )]
        [TestCase(
            PermissionScenario.ReservationMismatch,
            MovementPermissionFailureKind.InvalidReservation
        )]
        [TestCase(PermissionScenario.MoverMismatch, MovementPermissionFailureKind.MoverMismatch)]
        public async Task PermissionRejectsEveryMismatchedScope(
            PermissionScenario scenario,
            MovementPermissionFailureKind expected
        )
        {
            PermissionActionHandler handler = new PermissionActionHandler(scenario);
            InMemoryRulesStore store = CreateStore();
            RuleDispatcher dispatcher = CreateDispatcher(store, handler);

            OpResult<bool> result = await dispatcher.Dispatch(new PermissionActionOp());

            Assert.That(RequireResolved(result).Value, Is.False);
            Assert.That(handler.FirstMove.Status, Is.EqualTo(MovePathStatus.Stopped));
            Assert.That(
                handler.FirstMove.Failure.Kind,
                Is.EqualTo(MovementFailureKind.PermissionRejected)
            );
            Assert.That(handler.FirstMove.Failure.PermissionFailure, Is.EqualTo(expected));
            Assert.That(result.Facts.OfType<TokenMovedFact>(), Is.Empty);
            Assert.That(result.Facts.OfType<OccupiedSpaceTraversedFact>(), Is.Empty);
        }

        [Test]
        public async Task PermissionCannotEscapeItsIssuingRoot()
        {
            PermissionActionHandler handler = new PermissionActionHandler(
                PermissionScenario.EscapeThenReuseOnSecondRoot
            );
            InMemoryRulesStore store = CreateStore();
            RuleDispatcher dispatcher = CreateDispatcher(store, handler);

            Assert.That(
                RequireResolved(await dispatcher.Dispatch(new PermissionActionOp())).Value,
                Is.True
            );
            OpResult<bool> second = await dispatcher.Dispatch(new PermissionActionOp());

            Assert.That(RequireResolved(second).Value, Is.False);
            Assert.That(
                handler.FirstMove.Failure.PermissionFailure,
                Is.EqualTo(MovementPermissionFailureKind.RootMismatch)
            );
            Assert.That(second.Facts.OfType<TokenMovedFact>(), Is.Empty);
        }

        [Test]
        public async Task PermissionFromAnotherRuntimeIsRejectedAsNotIssued()
        {
            PermissionActionHandler issuer = new PermissionActionHandler(
                PermissionScenario.IssueOnly
            );
            RuleDispatcher firstDispatcher = CreateDispatcher(CreateStore(), issuer);
            await firstDispatcher.Dispatch(new PermissionActionOp());

            PermissionActionHandler forger = new PermissionActionHandler(
                PermissionScenario.UseInjected,
                issuer.IssuedPermission
            );
            InMemoryRulesStore secondStore = CreateStore();
            RuleDispatcher secondDispatcher = CreateDispatcher(secondStore, forger);
            OpResult<bool> result = await secondDispatcher.Dispatch(new PermissionActionOp());

            Assert.That(RequireResolved(result).Value, Is.False);
            Assert.That(
                forger.FirstMove.Failure.PermissionFailure,
                Is.EqualTo(MovementPermissionFailureKind.NotIssued)
            );
            Assert.That(secondStore.Snapshot.Positions[Mover], Is.EqualTo(Origin));
        }

        [Test]
        public async Task OrdinaryValueCannotForgeAnOccupiedCrossing()
        {
            PermissionActionHandler handler = new PermissionActionHandler(
                PermissionScenario.UseOrdinaryValue
            );
            InMemoryRulesStore store = CreateStore();
            OpResult<bool> result = await CreateDispatcher(store, handler)
                .Dispatch(new PermissionActionOp());

            Assert.That(RequireResolved(result).Value, Is.False);
            Assert.That(handler.FirstMove.Failure.Kind, Is.EqualTo(MovementFailureKind.Occupied));
            Assert.That(result.Facts.OfType<TokenMovedFact>(), Is.Empty);
            Assert.That(result.Facts.OfType<OccupiedSpaceTraversedFact>(), Is.Empty);
        }

        [Test]
        public async Task PermissionIssuanceRejectsAPathThatDoesNotTraverseAndExitTheOccupant()
        {
            PermissionActionHandler handler = new PermissionActionHandler(
                PermissionScenario.InvalidReservation
            );
            InMemoryRulesStore store = CreateStore();
            OpResult<bool> result = await CreateDispatcher(store, handler)
                .Dispatch(new PermissionActionOp());

            Assert.That(RequireResolved(result).Value, Is.False);
            Assert.That(
                handler.GrantFailure.Kind,
                Is.EqualTo(MovementFailureKind.PermissionRejected)
            );
            Assert.That(
                handler.GrantFailure.PermissionFailure,
                Is.EqualTo(MovementPermissionFailureKind.InvalidReservation)
            );
            Assert.That(result.Facts.OfType<TokenMovedFact>(), Is.Empty);
        }

        private static InMemoryRulesStore CreateStore(bool seedMiddleware = false)
        {
            RulesStateSeed seed = new RulesStateSeed()
                .SeedPosition(Mover, Origin)
                .SeedPosition(Occupant, Occupied)
                .SeedPosition(OtherOccupant, new GridPosition(4, 0, 4))
                .SeedActionEconomy(Mover, new ActionEconomyState(3, true));
            if (seedMiddleware)
            {
                seed.SeedRuleBinding(
                    new ActiveRuleBinding(
                        MiddlewareBinding,
                        MiddlewareDefinition,
                        Mover,
                        null,
                        TestSource,
                        0
                    )
                );
            }
            return new InMemoryRulesStore(seed);
        }

        private static RuleDispatcher CreateDispatcher(
            InMemoryRulesStore store,
            PermissionActionHandler handler
        ) => CreateDispatcher(store, handler, OccupantDepartureRegistration.None);

        private static RuleDispatcher CreateDispatcher(
            InMemoryRulesStore store,
            PermissionActionHandler handler,
            IOpMiddleware<MovementLeavingSquareOp, MovementTriggerOutcome> middleware
        ) => CreateDispatcher(store, handler, new OccupantDepartureRegistration(middleware));

        private static RuleDispatcher CreateDispatcher(
            InMemoryRulesStore store,
            PermissionActionHandler handler,
            OccupantDepartureRegistration departureRegistration
        )
        {
            RuleDispatcherBuilder builder = new RuleDispatcherBuilder(store)
                .RegisterHandler<PermissionActionOp, bool>(handler)
                .RegisterHandler<MoveRelayOp, MovePathOutcome>(new MoveRelayHandler())
                .UseActionLifecycle(new FreeActionCatalog())
                .UseMovementRules(
                    new GridTopology(
                        new GridBounds(new GridPosition(0, 0, 0), new GridPosition(4, 0, 4)),
                        Array.Empty<GridCell>()
                    )
                );
            if (departureRegistration.IsConfigured)
            {
                RuleRegistryBuilder registry = new RuleRegistryBuilder();
                registry
                    .Define(MiddlewareDefinition)
                    .Middleware(RuleLifecyclePhase.Reaction, departureRegistration.Middleware);
                builder.UseRuleRegistry(registry.Build());
            }
            return builder.Build();
        }

        private static ResolvedOpResult<TResult> RequireResolved<TResult>(OpResult<TResult> result)
        {
            Assert.That(result, Is.TypeOf<ResolvedOpResult<TResult>>());
            return (ResolvedOpResult<TResult>)result;
        }

        private static void AssertUniquePositions(RulesSnapshot snapshot) =>
            Assert.That(
                snapshot.Positions.Select(pair => pair.Value).Distinct().Count(),
                Is.EqualTo(snapshot.Positions.Count)
            );

        private sealed class PermissionActionOp : ActionOp<bool>
        {
            public PermissionActionOp()
                : base(Mover, ActionDefinition) { }
        }

        private sealed class FreeActionCatalog : IActionCatalog
        {
            public ActionProfile GetBaseProfile(ActionDefinitionId definitionId) =>
                ActionProfile.Create(ActionCost.None, Array.Empty<Trait>());
        }

        public enum PermissionScenario
        {
            SuccessThenReuse,
            PathMismatch,
            PurposeMismatch,
            ParentMismatch,
            BudgetMismatch,
            OccupantMismatch,
            ReservationMismatch,
            MoverMismatch,
            EscapeThenReuseOnSecondRoot,
            IssueOnly,
            UseInjected,
            UseOrdinaryValue,
            InvalidReservation,
            ObserverFailureThenReuse,
            OccupantRelocatesBeforeTraversal,
            StopThenReuse,
            ThrowingReservedExit,
            ExitContentionThenRetry,
        }

        /// <summary>Names the injected result returned at the reserved cell's departure.</summary>
        public enum DepartureStopResult
        {
            /// <summary>Returns a resolved trigger decision that requests interruption.</summary>
            ResolvedInterruption,

            /// <summary>Returns a structurally interrupted operation.</summary>
            Interrupted,

            /// <summary>Returns a structurally cancelled operation.</summary>
            Cancelled,

            /// <summary>Returns a structurally invalid operation.</summary>
            Invalid,
        }

        private sealed class PermissionActionHandler : IOpHandler<PermissionActionOp, bool>
        {
            private readonly PermissionScenario scenario;
            private readonly MovementPermission injectedPermission;
            private int calls;

            public PermissionActionHandler(PermissionScenario scenario)
                : this(scenario, MovementPermission.None) { }

            public PermissionActionHandler(
                PermissionScenario scenario,
                MovementPermission injectedPermission
            )
            {
                this.scenario = scenario;
                this.injectedPermission =
                    injectedPermission
                    ?? throw new ArgumentNullException(nameof(injectedPermission));
            }

            public MovementPermission IssuedPermission { get; private set; } =
                MovementPermission.None;
            public MovePathOutcome FirstMove { get; private set; }
            public MovePathOutcome SecondMove { get; private set; }
            public MovePathOutcome ThirdMove { get; private set; }
            public MovementFailure GrantFailure { get; private set; }
            public bool CaughtObserverFailure { get; private set; }
            public bool PositionsUniqueAtAllMoveBoundaries { get; private set; } = true;

            public async ValueTask<bool> Handle(
                OpFrame<PermissionActionOp> frame,
                OpHandlerContext context
            )
            {
                calls++;
                MovementBudgetStartOutcome started = RequireResolved(
                    await context.Dispatch(
                        new BeginMovementBudgetOp(frame.Id, Mover, new GridDistance(30))
                    )
                ).Value;
                Assert.That(started.IsStarted, Is.True);

                if (scenario == PermissionScenario.UseOrdinaryValue)
                {
                    FirstMove = RequireResolved(
                        await context.Dispatch(
                            new MovePathOp(frame.Id, Mover, started.Budget.Id, CrossingPath)
                        )
                    ).Value;
                    return FirstMove.ReachedDestination;
                }
                if (scenario == PermissionScenario.UseInjected)
                {
                    FirstMove = await Move(
                        frame,
                        context,
                        started.Budget.Id,
                        CrossingPath,
                        injectedPermission,
                        Purpose
                    );
                    return FirstMove.ReachedDestination;
                }
                if (scenario == PermissionScenario.EscapeThenReuseOnSecondRoot && calls > 1)
                {
                    FirstMove = await Move(
                        frame,
                        context,
                        started.Budget.Id,
                        CrossingPath,
                        IssuedPermission,
                        Purpose
                    );
                    return FirstMove.ReachedDestination;
                }

                MovementPath reservation =
                    scenario == PermissionScenario.InvalidReservation
                        ? new MovementPath(Origin, new[] { new GridPosition(0, 0, 1) })
                        : CrossingPath;
                MovementPermissionRequestOutcome grant = RequireResolved(
                    await context.Dispatch(
                        new RequestMovementPermissionOp(
                            frame.Id,
                            Mover,
                            Occupant,
                            started.Budget.Id,
                            reservation,
                            Purpose
                        )
                    )
                ).Value;
                if (!grant.IsGranted)
                {
                    GrantFailure = grant.Failure;
                    return false;
                }

                IssuedPermission = grant.Permission;
                if (
                    scenario == PermissionScenario.IssueOnly
                    || scenario == PermissionScenario.EscapeThenReuseOnSecondRoot
                )
                {
                    return true;
                }

                if (scenario == PermissionScenario.StopThenReuse)
                {
                    FirstMove = await Move(
                        frame,
                        context,
                        started.Budget.Id,
                        CrossingPath,
                        IssuedPermission,
                        Purpose
                    );
                    await Relocate(frame, context, Mover, FirstMove.FinalPosition, Origin);
                    SecondMove = await Move(
                        frame,
                        context,
                        started.Budget.Id,
                        CrossingPath,
                        IssuedPermission,
                        Purpose
                    );
                    return SecondMove.Failure.PermissionFailure
                        == MovementPermissionFailureKind.Reused;
                }

                if (scenario == PermissionScenario.ExitContentionThenRetry)
                {
                    FirstMove = await Move(
                        frame,
                        context,
                        started.Budget.Id,
                        CrossingPath,
                        IssuedPermission,
                        Purpose
                    );
                    await Relocate(
                        frame,
                        context,
                        OtherOccupant,
                        SecondIntermediate,
                        new GridPosition(4, 0, 4)
                    );
                    SecondMove = await Move(
                        frame,
                        context,
                        started.Budget.Id,
                        CrossingPath,
                        IssuedPermission,
                        Purpose
                    );
                    await Relocate(frame, context, Mover, Exit, Origin);
                    ThirdMove = await Move(
                        frame,
                        context,
                        started.Budget.Id,
                        CrossingPath,
                        IssuedPermission,
                        Purpose
                    );
                    return SecondMove.ReachedDestination
                        && ThirdMove.Failure.PermissionFailure
                            == MovementPermissionFailureKind.Reused;
                }

                if (scenario == PermissionScenario.ObserverFailureThenReuse)
                {
                    try
                    {
                        FirstMove = await Move(
                            frame,
                            context,
                            started.Budget.Id,
                            CrossingPath,
                            IssuedPermission,
                            Purpose
                        );
                    }
                    catch (InvalidOperationException exception)
                    {
                        CaughtObserverFailure = exception.Message == "injected observer failure";
                    }

                    await Relocate(frame, context, Mover, SecondIntermediate, Origin);
                    SecondMove = await Move(
                        frame,
                        context,
                        started.Budget.Id,
                        CrossingPath,
                        IssuedPermission,
                        Purpose
                    );
                    return SecondMove.Failure.PermissionFailure
                        == MovementPermissionFailureKind.Reused;
                }

                MovementPath movePath =
                    scenario == PermissionScenario.PathMismatch
                        ? new MovementPath(Origin, new[] { Occupied, new GridPosition(2, 0, 1) })
                        : CrossingPath;
                MovementPermissionPurpose purpose =
                    scenario == PermissionScenario.PurposeMismatch ? OtherPurpose : Purpose;
                MovementBudgetId budgetId =
                    scenario == PermissionScenario.BudgetMismatch
                        ? new MovementBudgetId(new OpId(9999))
                        : started.Budget.Id;

                if (scenario == PermissionScenario.OccupantMismatch)
                {
                    await Relocate(frame, context, Occupant, Occupied, new GridPosition(4, 0, 3));
                    await Relocate(
                        frame,
                        context,
                        OtherOccupant,
                        new GridPosition(4, 0, 4),
                        Occupied
                    );
                }
                if (scenario == PermissionScenario.ReservationMismatch)
                {
                    await Relocate(frame, context, Occupant, Occupied, SecondIntermediate);
                }

                if (scenario == PermissionScenario.MoverMismatch)
                {
                    FirstMove = RequireResolved(
                        await context.Dispatch(
                            new MovePathOp(
                                frame.Id,
                                OtherOccupant,
                                budgetId,
                                movePath,
                                IssuedPermission,
                                purpose
                            )
                        )
                    ).Value;
                }
                else if (scenario == PermissionScenario.ParentMismatch)
                {
                    FirstMove = RequireResolved(
                        await context.Dispatch(
                            new MoveRelayOp(frame.Id, budgetId, movePath, IssuedPermission, purpose)
                        )
                    ).Value;
                }
                else
                {
                    FirstMove = await Move(
                        frame,
                        context,
                        budgetId,
                        movePath,
                        IssuedPermission,
                        purpose
                    );
                }

                if (scenario == PermissionScenario.SuccessThenReuse)
                {
                    SecondMove = await Move(
                        frame,
                        context,
                        started.Budget.Id,
                        CrossingPath,
                        IssuedPermission,
                        Purpose
                    );
                }
                return FirstMove.ReachedDestination;
            }

            private async ValueTask<MovePathOutcome> Move(
                OpFrame<PermissionActionOp> frame,
                OpHandlerContext context,
                MovementBudgetId budgetId,
                MovementPath path,
                MovementPermission permission,
                MovementPermissionPurpose purpose
            )
            {
                try
                {
                    return RequireResolved(
                        await context.Dispatch(
                            new MovePathOp(frame.Id, Mover, budgetId, path, permission, purpose)
                        )
                    ).Value;
                }
                finally
                {
                    PositionsUniqueAtAllMoveBoundaries &=
                        context.Snapshot.Positions.Select(pair => pair.Value).Distinct().Count()
                        == context.Snapshot.Positions.Count;
                }
            }

            private static async ValueTask Relocate(
                OpFrame<PermissionActionOp> frame,
                OpHandlerContext context,
                CreatureId creature,
                GridPosition from,
                GridPosition to
            )
            {
                RelocationOutcome outcome = RequireResolved(
                    await context.Dispatch(
                        new RelocateTokenOp(
                            creature,
                            from,
                            to,
                            frame.Id,
                            RelocationKind.FromSlug("permission-test-relocation"),
                            TestSource
                        )
                    )
                ).Value;
                Assert.That(outcome.Relocated, Is.True);
            }
        }

        private sealed class MoveRelayOp : IRuleOp<MovePathOutcome>
        {
            public MoveRelayOp(
                OpId actionOpId,
                MovementBudgetId budgetId,
                MovementPath path,
                MovementPermission permission,
                MovementPermissionPurpose purpose
            )
            {
                ActionOpId = actionOpId;
                BudgetId = budgetId;
                Path = path;
                Permission = permission;
                Purpose = purpose;
            }

            public OpId ActionOpId { get; }
            public MovementBudgetId BudgetId { get; }
            public MovementPath Path { get; }
            public MovementPermission Permission { get; }
            public MovementPermissionPurpose Purpose { get; }
        }

        private sealed class MoveRelayHandler : IOpHandler<MoveRelayOp, MovePathOutcome>
        {
            public async ValueTask<MovePathOutcome> Handle(
                OpFrame<MoveRelayOp> frame,
                OpHandlerContext context
            ) =>
                RequireResolved(
                    await context.Dispatch(
                        new MovePathOp(
                            frame.Op.ActionOpId,
                            Mover,
                            frame.Op.BudgetId,
                            frame.Op.Path,
                            frame.Op.Permission,
                            frame.Op.Purpose
                        )
                    )
                ).Value;
        }

        private readonly struct OccupantDepartureRegistration
        {
            public static OccupantDepartureRegistration None => default;

            public OccupantDepartureRegistration(
                IOpMiddleware<MovementLeavingSquareOp, MovementTriggerOutcome> middleware
            )
            {
                Middleware = middleware;
                IsConfigured = true;
            }

            public bool IsConfigured { get; }
            public IOpMiddleware<
                MovementLeavingSquareOp,
                MovementTriggerOutcome
            > Middleware { get; }
        }

        private sealed class CrossingStopMiddleware
            : IOpMiddleware<MovementLeavingSquareOp, MovementTriggerOutcome>
        {
            private readonly DepartureStopResult result;

            public CrossingStopMiddleware(DepartureStopResult result) => this.result = result;

            public List<GridPosition> AuthoritativePositions { get; } = new List<GridPosition>();
            public List<GridPosition> DepartureCells { get; } = new List<GridPosition>();

            public async ValueTask<OpResult<MovementTriggerOutcome>> Invoke(
                OpFrame<MovementLeavingSquareOp> frame,
                OpMiddlewareContext context,
                OpNext<MovementTriggerOutcome> next
            )
            {
                AuthoritativePositions.Add(context.Snapshot.Positions[Mover]);
                DepartureCells.Add(frame.Op.From);
                if (frame.Op.TriggerId.StepNumber != 2)
                    return await next();

                switch (result)
                {
                    case DepartureStopResult.ResolvedInterruption:
                        return OpResult<MovementTriggerOutcome>.Resolved(
                            MovementTriggerOutcome.Interrupted
                        );
                    case DepartureStopResult.Interrupted:
                        return OpResult<MovementTriggerOutcome>.Interrupted();
                    case DepartureStopResult.Cancelled:
                        return OpResult<MovementTriggerOutcome>.Cancelled();
                    default:
                        return OpResult<MovementTriggerOutcome>.Invalid(
                            "injected reserved-exit invalidity"
                        );
                }
            }
        }

        private sealed class OccupantDepartureMiddleware
            : IOpMiddleware<MovementLeavingSquareOp, MovementTriggerOutcome>
        {
            private readonly GridPosition destination;

            public OccupantDepartureMiddleware(GridPosition destination) =>
                this.destination = destination;

            public async ValueTask<OpResult<MovementTriggerOutcome>> Invoke(
                OpFrame<MovementLeavingSquareOp> frame,
                OpMiddlewareContext context,
                OpNext<MovementTriggerOutcome> next
            )
            {
                OpResult<MovementTriggerOutcome> current = await next();
                if (frame.Op.TriggerId.StepNumber != 1)
                    return current;

                RelocationOutcome relocated = RequireResolved(
                    await context.Dispatch(
                        new RelocateTokenOp(
                            Occupant,
                            Occupied,
                            destination,
                            frame.Id,
                            RelocationKind.FromSlug("permission-occupant-departure"),
                            TestSource
                        )
                    )
                ).Value;
                Assert.That(relocated.Relocated, Is.True);
                return current;
            }
        }

        private sealed class ThrowingReservedExitMiddleware
            : IOpMiddleware<MovementLeavingSquareOp, MovementTriggerOutcome>
        {
            public async ValueTask<OpResult<MovementTriggerOutcome>> Invoke(
                OpFrame<MovementLeavingSquareOp> frame,
                OpMiddlewareContext context,
                OpNext<MovementTriggerOutcome> next
            )
            {
                OpResult<MovementTriggerOutcome> current = await next();
                if (frame.Op.TriggerId.StepNumber == 2)
                    throw new InvalidOperationException("injected reserved-exit failure");
                return current;
            }
        }

        private sealed class OneTimeExitContentionMiddleware
            : IOpMiddleware<MovementLeavingSquareOp, MovementTriggerOutcome>
        {
            private bool relocated;

            public async ValueTask<OpResult<MovementTriggerOutcome>> Invoke(
                OpFrame<MovementLeavingSquareOp> frame,
                OpMiddlewareContext context,
                OpNext<MovementTriggerOutcome> next
            )
            {
                OpResult<MovementTriggerOutcome> current = await next();
                if (relocated || frame.Op.TriggerId.StepNumber != 2)
                    return current;

                relocated = true;
                RelocationOutcome outcome = RequireResolved(
                    await context.Dispatch(
                        new RelocateTokenOp(
                            OtherOccupant,
                            new GridPosition(4, 0, 4),
                            SecondIntermediate,
                            frame.Id,
                            RelocationKind.FromSlug("permission-exit-contention"),
                            TestSource
                        )
                    )
                ).Value;
                Assert.That(outcome.Relocated, Is.True);
                return current;
            }
        }

        private sealed class CrossingSnapshotObserver
            : IFactObserver<TokenMovedFact>,
                IFactObserver<OccupiedSpaceTraversedFact>
        {
            public List<GridPosition> Positions { get; } = new List<GridPosition>();
            public List<bool> UniquePositions { get; } = new List<bool>();

            public ValueTask OnFactCommitted(TokenMovedFact fact, RulesSnapshot currentSnapshot) =>
                Record(currentSnapshot);

            public ValueTask OnFactCommitted(
                OccupiedSpaceTraversedFact fact,
                RulesSnapshot currentSnapshot
            ) => Record(currentSnapshot);

            private ValueTask Record(RulesSnapshot snapshot)
            {
                Positions.Add(snapshot.Positions[Mover]);
                UniquePositions.Add(
                    snapshot.Positions.Select(pair => pair.Value).Distinct().Count()
                        == snapshot.Positions.Count
                );
                return default;
            }
        }

        private sealed class ThrowingTraversalObserver : IFactObserver<OccupiedSpaceTraversedFact>
        {
            public GridPosition ObservedMoverPosition { get; private set; }
            public bool ObservedUniquePositions { get; private set; }

            public ValueTask OnFactCommitted(
                OccupiedSpaceTraversedFact fact,
                RulesSnapshot currentSnapshot
            )
            {
                ObservedMoverPosition = currentSnapshot.Positions[Mover];
                ObservedUniquePositions =
                    currentSnapshot.Positions.Select(pair => pair.Value).Distinct().Count()
                    == currentSnapshot.Positions.Count;
                throw new InvalidOperationException("injected observer failure");
            }
        }
    }
}
