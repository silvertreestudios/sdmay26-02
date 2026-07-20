using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    /// <summary>Verifies path validation, step timing, atomic commits, and relocation contracts.</summary>
    public sealed class MovementPathRuleTests
    {
        private static readonly CreatureId Mover = new CreatureId("movement-mover");
        private static readonly CreatureId Occupant = new CreatureId("movement-occupant");
        private static readonly ActionDefinitionId ActionDefinition = new ActionDefinitionId(
            "movement-test-action"
        );
        private static readonly RuleDefinitionId MiddlewareDefinition = new RuleDefinitionId(
            "movement-test-middleware"
        );
        private static readonly BindingId MiddlewareBinding = new BindingId(
            "movement-test-binding"
        );
        private static readonly RuleSource TestSource = RuleSource.FromSlug("movement-test");

        [Test]
        public void PurePreflightReturnsTypedFailuresWithoutMutatingState()
        {
            GridPosition origin = new GridPosition(1, 0, 1);
            MovementBudgetId budgetId = new MovementBudgetId(new OpId(10));
            GridTopology open = CreateTopology();
            RulesSnapshot snapshot = CreateStore(origin, budgetId, 9).Snapshot;
            MovementPathValidator validator = new MovementPathValidator(open);

            AssertFailure(
                validator,
                snapshot,
                budgetId,
                new MovementPath(origin, Array.Empty<GridPosition>()),
                MovementFailureKind.EmptyPath
            );
            AssertFailure(
                validator,
                snapshot,
                new MovementBudgetId(new OpId(11)),
                new MovementPath(origin, new[] { new GridPosition(2, 0, 1) }),
                MovementFailureKind.BudgetMismatch
            );
            AssertFailure(
                validator,
                CreateStore(origin).Snapshot,
                budgetId,
                new MovementPath(origin, new[] { new GridPosition(2, 0, 1) }),
                MovementFailureKind.MissingBudget
            );
            RulesSnapshot missingPosition = new InMemoryRulesStore(
                new RulesStateSeed().SeedMovementBudget(
                    Mover,
                    new MovementBudgetState(
                        budgetId,
                        Mover,
                        new GridDistance(10),
                        DiagonalMovementPhase.NextCostsFiveFeet
                    )
                )
            ).Snapshot;
            AssertFailure(
                validator,
                missingPosition,
                budgetId,
                new MovementPath(origin, new[] { new GridPosition(2, 0, 1) }),
                MovementFailureKind.MissingPosition
            );
            AssertFailure(
                validator,
                snapshot,
                budgetId,
                new MovementPath(origin, new[] { new GridPosition(3, 0, 1) }),
                MovementFailureKind.NonContiguous
            );
            AssertFailure(
                validator,
                snapshot,
                budgetId,
                new MovementPath(
                    origin,
                    new[] { new GridPosition(-1, 0, 1), new GridPosition(0, 0, 1) }
                ),
                MovementFailureKind.OutOfBounds
            );
            AssertFailure(
                validator,
                snapshot,
                budgetId,
                new MovementPath(origin, new[] { new GridPosition(5, 0, 1) }),
                MovementFailureKind.DestinationOutOfBounds
            );
            AssertFailure(
                validator,
                snapshot,
                budgetId,
                new MovementPath(new GridPosition(0, 0, 1), new[] { origin }),
                MovementFailureKind.StaleOrigin
            );
            AssertFailure(
                validator,
                snapshot,
                budgetId,
                new MovementPath(
                    origin,
                    new[] { new GridPosition(2, 0, 1), new GridPosition(3, 0, 1) }
                ),
                MovementFailureKind.InsufficientMovement
            );

            Assert.That(snapshot.Version, Is.Zero);
            Assert.That(snapshot.Positions[Mover], Is.EqualTo(origin));
            Assert.That(snapshot.MovementBudgets[Mover].Remaining.Feet, Is.EqualTo(9));
        }

        [Test]
        public async Task InvalidPathPreflightCommitsNoStateBudgetOrFacts()
        {
            GridPosition origin = new GridPosition(1, 0, 1);
            MovementBudgetId budgetId = new MovementBudgetId(new OpId(10));
            InMemoryRulesStore store = CreateStore(origin, budgetId, 30);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                store,
                new SequentialOpIdProvider(10)
            )
                .RegisterHandler<TestMoveActionOp, MovePathOutcome>(
                    new SeededBudgetMoveHandler(budgetId)
                )
                .UseActionLifecycle(new FreeActionCatalog())
                .UseMovementRules(CreateTopology())
                .Build();

            OpResult<MovePathOutcome> result = await dispatcher.Dispatch(
                new TestMoveActionOp(
                    new MovementPath(origin, new[] { new GridPosition(3, 0, 1) }),
                    30
                )
            );

            Assert.That(RequireResolved(result).Value.CommittedSteps, Is.Zero);
            Assert.That(
                RequireResolved(result).Value.Failure.Kind,
                Is.EqualTo(MovementFailureKind.NonContiguous)
            );
            Assert.That(result.Facts, Is.Empty);
            Assert.That(store.Snapshot.Version, Is.Zero);
            Assert.That(store.Snapshot.Positions[Mover], Is.EqualTo(origin));
            Assert.That(store.Snapshot.MovementBudgets[Mover].Remaining.Feet, Is.EqualTo(30));
        }

        [Test]
        public void PurePreflightDistinguishesCornersBlockersOccupancyAndDestinationFailures()
        {
            GridPosition origin = new GridPosition(1, 0, 1);
            MovementBudgetId budgetId = new MovementBudgetId(new OpId(10));
            GridTopology topology = CreateTopology(
                new GridCell(new GridPosition(2, 0, 1), true, TerrainCost.Normal),
                new GridCell(new GridPosition(1, 0, 2), true, TerrainCost.Normal),
                new GridCell(new GridPosition(4, 0, 4), true, TerrainCost.Normal)
            );
            InMemoryRulesStore store = CreateStore(
                origin,
                budgetId,
                50,
                Occupant,
                new GridPosition(3, 0, 3)
            );
            MovementPathValidator validator = new MovementPathValidator(topology);

            AssertFailure(
                validator,
                store.Snapshot,
                budgetId,
                new MovementPath(origin, new[] { new GridPosition(2, 0, 2) }),
                MovementFailureKind.CornerBlocked
            );
            AssertFailure(
                validator,
                store.Snapshot,
                budgetId,
                new MovementPath(
                    origin,
                    new[] { new GridPosition(2, 0, 1), new GridPosition(2, 0, 2) }
                ),
                MovementFailureKind.Blocked
            );
            AssertFailure(
                validator,
                store.Snapshot,
                budgetId,
                new MovementPath(origin, new[] { new GridPosition(4, 0, 4) }),
                MovementFailureKind.NonContiguous
            );
            AssertFailure(
                validator,
                store.Snapshot,
                budgetId,
                new MovementPath(
                    origin,
                    new[]
                    {
                        new GridPosition(2, 0, 2),
                        new GridPosition(3, 0, 3),
                        new GridPosition(4, 0, 3),
                    }
                ),
                MovementFailureKind.CornerBlocked
            );

            GridTopology open = CreateTopology();
            validator = new MovementPathValidator(open);
            AssertFailure(
                validator,
                store.Snapshot,
                budgetId,
                new MovementPath(
                    origin,
                    new[]
                    {
                        new GridPosition(2, 0, 2),
                        new GridPosition(3, 0, 3),
                        new GridPosition(4, 0, 3),
                    }
                ),
                MovementFailureKind.Occupied
            );
            AssertFailure(
                validator,
                store.Snapshot,
                budgetId,
                new MovementPath(
                    origin,
                    new[] { new GridPosition(2, 0, 2), new GridPosition(3, 0, 3) }
                ),
                MovementFailureKind.DestinationOccupied
            );

            GridTopology destinationBlocked = CreateTopology(
                new GridCell(new GridPosition(2, 0, 1), true, TerrainCost.Normal)
            );
            AssertFailure(
                new MovementPathValidator(destinationBlocked),
                store.Snapshot,
                budgetId,
                new MovementPath(origin, new[] { new GridPosition(2, 0, 1) }),
                MovementFailureKind.DestinationBlocked
            );
            Assert.That(store.Snapshot.Version, Is.Zero);
        }

        [Test]
        public async Task SuccessfulStepsCommitPositionAndBudgetAtomicallyAndPaceObservers()
        {
            GridPosition origin = new GridPosition(0, 0, 0);
            MovementPath path = new MovementPath(
                origin,
                new[] { new GridPosition(1, 0, 0), new GridPosition(2, 0, 1) }
            );
            GridTopology topology = CreateTopology(
                new GridCell(new GridPosition(2, 0, 1), false, TerrainCost.Difficult)
            );
            InMemoryRulesStore store = CreateStore(origin, seedMiddleware: true);
            AtomicMovementObserver observer = new AtomicMovementObserver();
            DepartureMiddleware middleware = new DepartureMiddleware(observer);
            RuleDispatcher dispatcher = CreateDispatcher(
                store,
                topology,
                new StandardMoveActionHandler(),
                middleware
            );
            dispatcher.RegisterFactObserver(observer);

            ResolvedOpResult<MovePathOutcome> result = RequireResolved(
                await dispatcher.Dispatch(new TestMoveActionOp(path, 25))
            );

            Assert.That(result.Value.ReachedDestination, Is.True);
            Assert.That(result.Value.FinalPosition, Is.EqualTo(path.Destination));
            Assert.That(result.Value.CommittedSteps, Is.EqualTo(2));
            Assert.That(result.Value.DistanceSpent.Feet, Is.EqualTo(15));
            Assert.That(store.Snapshot.Positions[Mover], Is.EqualTo(path.Destination));
            Assert.That(store.Snapshot.MovementBudgets[Mover].Remaining.Feet, Is.EqualTo(10));
            Assert.That(observer.Facts.Select(fact => fact.Cost.Feet), Is.EqualTo(new[] { 5, 10 }));
            Assert.That(observer.Snapshots.Select(value => value.Position), Is.EqualTo(path.Steps));
            Assert.That(
                observer.Snapshots.Select(value => value.Remaining),
                Is.EqualTo(new[] { 20, 10 })
            );
            Assert.That(middleware.Positions, Is.EqualTo(new[] { origin, path.Steps[0] }));
            Assert.That(middleware.ObserverCounts, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(
                middleware.Triggers.Select(trigger => trigger.StepNumber),
                Is.EqualTo(new[] { 1, 2 })
            );
            Assert.That(
                middleware.ActionIds.Distinct().Single(),
                Is.EqualTo(result.Facts.OfType<TokenMovedFact>().First().ActionOpId)
            );
            Assert.That(result.Facts.OfType<TokenMovedFact>().Count(), Is.EqualTo(2));
            Assert.That(result.Facts.OfType<OccupiedSpaceTraversedFact>(), Is.Empty);
            Assert.That(
                result.Facts.Select(fact => fact.GetType()),
                Is.EqualTo(
                    new[]
                    {
                        typeof(MovementBudgetStartedFact),
                        typeof(TokenMovedFact),
                        typeof(TokenMovedFact),
                    }
                )
            );
            Assert.That(
                result.Facts.Select(fact => fact.Id),
                Is.EqualTo(new[] { new FactId(1), new FactId(2), new FactId(3) })
            );
            TokenMovedFact[] moved = result.Facts.OfType<TokenMovedFact>().ToArray();
            Assert.That(
                moved.Select(fact => fact.RootOpId).Distinct().Single(),
                Is.EqualTo(new OpId(1))
            );
            Assert.That(
                moved.Select(fact => fact.ActionOpId).Distinct().Single(),
                Is.EqualTo(new OpId(1))
            );
            Assert.That(
                moved.Select(fact => fact.Source),
                Is.All.EqualTo(RuleSource.FromSlug("movement"))
            );
            Assert.That(
                moved.Select(fact => fact.TriggerId.StepNumber),
                Is.EqualTo(new[] { 1, 2 })
            );
            Assert.That(
                moved.Select(fact => fact.TriggerId.MovePathOpId).Distinct().Count(),
                Is.EqualTo(1)
            );
            Assert.That(moved.Select(fact => fact.SourceOpId).Distinct().Count(), Is.EqualTo(2));
        }

        [Test]
        public async Task DepartureInterruptionPreservesOnlyTheCommittedPrefix()
        {
            GridPosition origin = new GridPosition(0, 0, 0);
            MovementPath path = new MovementPath(
                origin,
                new[]
                {
                    new GridPosition(1, 0, 0),
                    new GridPosition(2, 0, 0),
                    new GridPosition(3, 0, 0),
                }
            );
            InMemoryRulesStore store = CreateStore(origin, seedMiddleware: true);
            DepartureMiddleware middleware = new DepartureMiddleware(2, false);
            RuleDispatcher dispatcher = CreateDispatcher(
                store,
                CreateTopology(),
                new StandardMoveActionHandler(),
                middleware
            );

            ResolvedOpResult<MovePathOutcome> result = RequireResolved(
                await dispatcher.Dispatch(new TestMoveActionOp(path, 25))
            );

            Assert.That(result.Value.Status, Is.EqualTo(MovePathStatus.Stopped));
            Assert.That(
                result.Value.Failure.Kind,
                Is.EqualTo(MovementFailureKind.TriggerInterrupted)
            );
            Assert.That(result.Value.Failure.StepNumber, Is.EqualTo(2));
            Assert.That(result.Value.CommittedSteps, Is.EqualTo(1));
            Assert.That(result.Value.DistanceSpent.Feet, Is.EqualTo(5));
            Assert.That(store.Snapshot.Positions[Mover], Is.EqualTo(path.Steps[0]));
            Assert.That(store.Snapshot.MovementBudgets[Mover].Remaining.Feet, Is.EqualTo(20));
            Assert.That(result.Facts.OfType<TokenMovedFact>().Count(), Is.EqualTo(1));
        }

        [Test]
        public async Task LaterStepRevalidationPreservesEarlierCommitAndRejectsOnlyCurrentStep()
        {
            GridPosition origin = new GridPosition(0, 0, 0);
            GridPosition displaced = new GridPosition(4, 0, 4);
            MovementPath path = new MovementPath(
                origin,
                new[] { new GridPosition(1, 0, 0), new GridPosition(2, 0, 0) }
            );
            InMemoryRulesStore store = CreateStore(origin, seedMiddleware: true);
            DepartureMiddleware middleware = new DepartureMiddleware(2, true, displaced);
            RuleDispatcher dispatcher = CreateDispatcher(
                store,
                CreateTopology(),
                new StandardMoveActionHandler(),
                middleware
            );

            ResolvedOpResult<MovePathOutcome> result = RequireResolved(
                await dispatcher.Dispatch(new TestMoveActionOp(path, 25))
            );

            Assert.That(result.Value.Status, Is.EqualTo(MovePathStatus.Stopped));
            Assert.That(result.Value.Failure.Kind, Is.EqualTo(MovementFailureKind.StaleOrigin));
            Assert.That(result.Value.CommittedSteps, Is.EqualTo(1));
            Assert.That(store.Snapshot.Positions[Mover], Is.EqualTo(displaced));
            Assert.That(
                store.Snapshot.MovementBudgets[Mover].Remaining.Feet,
                Is.EqualTo(20),
                "Relocation must not spend the rejected second step's budget."
            );
            Assert.That(result.Facts.OfType<TokenMovedFact>().Count(), Is.EqualTo(1));
            Assert.That(result.Facts.OfType<TokenRelocatedFact>().Count(), Is.EqualTo(1));
        }

        [Test]
        public void MovementReducerExceptionRollsBackOnlyTheUncommittedStepDraft()
        {
            GridPosition origin = new GridPosition(0, 0, 0);
            GridPosition first = new GridPosition(1, 0, 0);
            GridPosition second = new GridPosition(2, 0, 0);
            MovementBudgetId budgetId = new MovementBudgetId(new OpId(10));
            InMemoryRulesStore store = CreateStore(origin, budgetId, 20);
            GridTopology topology = CreateTopology();
            CommitMovementStepReducer reducer = new CommitMovementStepReducer(topology);

            ReductionResult<MovementStepCommitOutcome> firstCommit = store.Reduce(
                new ReductionContext<CommitMovementStepOp>(
                    CreateCommitOp(origin, first, budgetId, 1),
                    new OpId(30),
                    new OpId(10),
                    RuleSource.FromSlug("movement")
                ),
                reducer
            );

            Assert.That(firstCommit.IsAccepted, Is.True);
            Assert.That(firstCommit.Facts.Single().Id, Is.EqualTo(new FactId(1)));
            Assert.Throws<InvalidOperationException>(() =>
                store.Reduce(
                    new ReductionContext<CommitMovementStepOp>(
                        CreateCommitOp(first, second, budgetId, 2),
                        new OpId(31),
                        new OpId(10),
                        RuleSource.FromSlug("movement")
                    ),
                    new ThrowAfterReduceMovementReducer(reducer)
                )
            );

            Assert.That(store.Snapshot.Positions[Mover], Is.EqualTo(first));
            Assert.That(store.Snapshot.MovementBudgets[Mover].Remaining.Feet, Is.EqualTo(15));

            ReductionResult<MovementStepCommitOutcome> retry = store.Reduce(
                new ReductionContext<CommitMovementStepOp>(
                    CreateCommitOp(first, second, budgetId, 2),
                    new OpId(32),
                    new OpId(10),
                    RuleSource.FromSlug("movement")
                ),
                reducer
            );
            Assert.That(retry.Facts.Single().Id, Is.EqualTo(new FactId(2)));
            Assert.That(store.Snapshot.Positions[Mover], Is.EqualTo(second));
            Assert.That(store.Snapshot.MovementBudgets[Mover].Remaining.Feet, Is.EqualTo(10));
        }

        [Test]
        public async Task DiagonalPhasePersistsAcrossActionsUntilResetContractRuns()
        {
            GridPosition origin = new GridPosition(0, 0, 0);
            InMemoryRulesStore store = CreateStore(origin);
            RuleDispatcher dispatcher = CreateDispatcher(
                store,
                CreateTopology(),
                new StandardMoveActionHandler()
            );

            OpResult<MovePathOutcome> first = await dispatcher.Dispatch(
                new TestMoveActionOp(
                    new MovementPath(origin, new[] { new GridPosition(1, 0, 1) }),
                    20
                )
            );
            OpResult<MovePathOutcome> second = await dispatcher.Dispatch(
                new TestMoveActionOp(
                    new MovementPath(
                        new GridPosition(1, 0, 1),
                        new[] { new GridPosition(2, 0, 2) }
                    ),
                    20
                )
            );
            OpResult<MovementBudgetResetOutcome> reset = await dispatcher.Dispatch(
                new ResetHarnessOp()
            );
            OpResult<MovePathOutcome> third = await dispatcher.Dispatch(
                new TestMoveActionOp(
                    new MovementPath(
                        new GridPosition(2, 0, 2),
                        new[] { new GridPosition(3, 0, 3) }
                    ),
                    20
                )
            );

            Assert.That(
                new[]
                {
                    first.Facts.OfType<TokenMovedFact>().Single().Cost.Feet,
                    second.Facts.OfType<TokenMovedFact>().Single().Cost.Feet,
                    third.Facts.OfType<TokenMovedFact>().Single().Cost.Feet,
                },
                Is.EqualTo(new[] { 5, 10, 5 })
            );
            Assert.That(RequireResolved(reset).Value.WasReset, Is.True);
            Assert.That(reset.Facts.OfType<MovementBudgetResetFact>().Count(), Is.EqualTo(1));
        }

        [Test]
        public async Task RelocationChangesOnlyPositionAndEmitsCauseProvenance()
        {
            GridPosition origin = new GridPosition(0, 0, 0);
            GridPosition destination = new GridPosition(4, 0, 4);
            MovementBudgetState budget = new MovementBudgetState(
                new MovementBudgetId(new OpId(99)),
                Mover,
                new GridDistance(15),
                DiagonalMovementPhase.NextCostsTenFeet
            );
            InMemoryRulesStore store = new InMemoryRulesStore(
                new RulesStateSeed()
                    .SeedPosition(Mover, origin)
                    .SeedMovementBudget(Mover, budget)
                    .SeedActionEconomy(Mover, new ActionEconomyState(2, true))
            );
            RuleDispatcher dispatcher = CreateDispatcher(
                store,
                CreateTopology(),
                new StandardMoveActionHandler()
            );

            ResolvedOpResult<RelocationOutcome> result = RequireResolved(
                await dispatcher.Dispatch(new RelocationHarnessOp(origin, destination))
            );
            TokenRelocatedFact fact = result.Facts.OfType<TokenRelocatedFact>().Single();

            Assert.That(result.Value.Relocated, Is.True);
            Assert.That(store.Snapshot.Positions[Mover], Is.EqualTo(destination));
            Assert.That(store.Snapshot.MovementBudgets[Mover], Is.EqualTo(budget));
            Assert.That(
                store.Snapshot.ActionEconomy[Mover],
                Is.EqualTo(new ActionEconomyState(2, true))
            );
            Assert.That(fact.OriginOpId, Is.EqualTo(fact.RootOpId));
            Assert.That(fact.Kind, Is.EqualTo(RelocationKind.FromSlug("test-relocation")));
            Assert.That(fact.Source, Is.EqualTo(TestSource));
        }

        [Test]
        public void MovementWorkCannotBeDispatchedAsExternalRoots()
        {
            MovementPath path = new MovementPath(
                new GridPosition(0, 0, 0),
                new[] { new GridPosition(1, 0, 0) }
            );
            RuleDispatcher dispatcher = CreateDispatcher(
                CreateStore(path.Origin),
                CreateTopology(),
                new StandardMoveActionHandler()
            );

            InvalidOperationException moveError = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await dispatcher.Dispatch(
                        new MovePathOp(new OpId(1), Mover, new MovementBudgetId(new OpId(1)), path)
                    )
            );
            InvalidOperationException relocationError =
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await dispatcher.Dispatch(
                        new RelocateTokenOp(
                            Mover,
                            path.Origin,
                            path.Destination,
                            new OpId(1),
                            RelocationKind.FromSlug("test"),
                            TestSource
                        )
                    )
                );

            Assert.That(moveError.Message, Does.Contain("nested-only"));
            Assert.That(relocationError.Message, Does.Contain("nested-only"));
        }

        private static void AssertFailure(
            MovementPathValidator validator,
            RulesSnapshot snapshot,
            MovementBudgetId budgetId,
            MovementPath path,
            MovementFailureKind expected
        )
        {
            MovementPathValidation result = validator.Validate(
                snapshot,
                Mover,
                budgetId,
                path,
                OccupiedTraversalAllowance.None
            );
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Failure.Kind, Is.EqualTo(expected));
        }

        private static GridTopology CreateTopology(params GridCell[] cells) =>
            new GridTopology(
                new GridBounds(new GridPosition(0, 0, 0), new GridPosition(4, 0, 4)),
                cells
            );

        private static CommitMovementStepOp CreateCommitOp(
            GridPosition from,
            GridPosition to,
            MovementBudgetId budgetId,
            int stepNumber
        ) =>
            new CommitMovementStepOp(
                new OpId(10),
                Mover,
                budgetId,
                from,
                to,
                MovementCostRules.Calculate(
                    from,
                    to,
                    TerrainCost.Normal,
                    DiagonalMovementPhase.NextCostsFiveFeet
                ),
                new MovementTriggerId(new OpId(20), stepNumber),
                MovementTriggerKind.Departure,
                OccupiedTraversalAllowance.None,
                MovementPermissionPurpose.Ordinary,
                stepNumber == 2
            );

        private static InMemoryRulesStore CreateStore(
            GridPosition moverPosition,
            MovementBudgetId budgetId = default,
            int remaining = 0,
            CreatureId occupant = default,
            GridPosition occupantPosition = default,
            bool seedMiddleware = false
        )
        {
            RulesStateSeed seed = new RulesStateSeed()
                .SeedPosition(Mover, moverPosition)
                .SeedActionEconomy(Mover, new ActionEconomyState(3, true));
            if (!budgetId.IsEmpty)
            {
                seed.SeedMovementBudget(
                    Mover,
                    new MovementBudgetState(
                        budgetId,
                        Mover,
                        new GridDistance(remaining),
                        DiagonalMovementPhase.NextCostsFiveFeet
                    )
                );
            }
            if (!occupant.IsEmpty)
                seed.SeedPosition(occupant, occupantPosition);
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
            GridTopology topology,
            StandardMoveActionHandler handler
        ) => CreateDispatcher(store, topology, handler, MovementMiddlewareRegistration.None);

        private static RuleDispatcher CreateDispatcher(
            InMemoryRulesStore store,
            GridTopology topology,
            StandardMoveActionHandler handler,
            DepartureMiddleware middleware
        ) =>
            CreateDispatcher(
                store,
                topology,
                handler,
                new MovementMiddlewareRegistration(middleware)
            );

        private static RuleDispatcher CreateDispatcher(
            InMemoryRulesStore store,
            GridTopology topology,
            StandardMoveActionHandler handler,
            MovementMiddlewareRegistration middlewareRegistration
        )
        {
            RuleDispatcherBuilder builder = new RuleDispatcherBuilder(store)
                .RegisterHandler<TestMoveActionOp, MovePathOutcome>(handler)
                .RegisterHandler<ResetHarnessOp, MovementBudgetResetOutcome>(
                    new ResetHarnessHandler()
                )
                .RegisterHandler<RelocationHarnessOp, RelocationOutcome>(
                    new RelocationHarnessHandler()
                )
                .UseActionLifecycle(new FreeActionCatalog())
                .UseMovementRules(topology);
            if (middlewareRegistration.IsConfigured)
            {
                RuleRegistryBuilder registry = new RuleRegistryBuilder();
                registry
                    .Define(MiddlewareDefinition)
                    .Middleware(RuleLifecyclePhase.Reaction, middlewareRegistration.Middleware);
                builder.UseRuleRegistry(registry.Build());
            }
            return builder.Build();
        }

        private readonly struct MovementMiddlewareRegistration
        {
            public static MovementMiddlewareRegistration None => default;

            public MovementMiddlewareRegistration(DepartureMiddleware middleware)
            {
                Middleware = middleware;
                IsConfigured = true;
            }

            public bool IsConfigured { get; }
            public DepartureMiddleware Middleware { get; }
        }

        private static ResolvedOpResult<TResult> RequireResolved<TResult>(OpResult<TResult> result)
        {
            Assert.That(result, Is.TypeOf<ResolvedOpResult<TResult>>());
            return (ResolvedOpResult<TResult>)result;
        }

        private sealed class TestMoveActionOp : ActionOp<MovePathOutcome>
        {
            public TestMoveActionOp(MovementPath path, int allowanceFeet)
                : base(Mover, ActionDefinition)
            {
                Path = path;
                Allowance = new GridDistance(allowanceFeet);
            }

            public MovementPath Path { get; }
            public GridDistance Allowance { get; }
        }

        private sealed class StandardMoveActionHandler
            : IOpHandler<TestMoveActionOp, MovePathOutcome>
        {
            public async ValueTask<MovePathOutcome> Handle(
                OpFrame<TestMoveActionOp> frame,
                OpHandlerContext context
            )
            {
                MovementBudgetStartOutcome started = RequireResolved(
                    await context.Dispatch(
                        new BeginMovementBudgetOp(frame.Id, frame.Op.Actor, frame.Op.Allowance)
                    )
                ).Value;
                Assert.That(started.IsStarted, Is.True);
                return RequireResolved(
                    await context.Dispatch(
                        new MovePathOp(frame.Id, frame.Op.Actor, started.Budget.Id, frame.Op.Path)
                    )
                ).Value;
            }
        }

        private sealed class SeededBudgetMoveHandler : IOpHandler<TestMoveActionOp, MovePathOutcome>
        {
            private readonly MovementBudgetId budgetId;

            public SeededBudgetMoveHandler(MovementBudgetId budgetId) => this.budgetId = budgetId;

            public async ValueTask<MovePathOutcome> Handle(
                OpFrame<TestMoveActionOp> frame,
                OpHandlerContext context
            ) =>
                RequireResolved(
                    await context.Dispatch(
                        new MovePathOp(frame.Id, frame.Op.Actor, budgetId, frame.Op.Path)
                    )
                ).Value;
        }

        private sealed class FreeActionCatalog : IActionCatalog
        {
            public ActionProfile GetBaseProfile(ActionDefinitionId definitionId)
            {
                Assert.That(definitionId, Is.EqualTo(ActionDefinition));
                return ActionProfile.Create(
                    ActionCost.None,
                    Array.Empty<Trait>(),
                    canTriggerReactions: false
                );
            }
        }

        private sealed class ResetHarnessOp : IRuleOp<MovementBudgetResetOutcome> { }

        private sealed class ResetHarnessHandler
            : IOpHandler<ResetHarnessOp, MovementBudgetResetOutcome>
        {
            public async ValueTask<MovementBudgetResetOutcome> Handle(
                OpFrame<ResetHarnessOp> frame,
                OpHandlerContext context
            ) => RequireResolved(await context.Dispatch(new ResetMovementBudgetOp(Mover))).Value;
        }

        private sealed class RelocationHarnessOp : IRuleOp<RelocationOutcome>
        {
            public RelocationHarnessOp(GridPosition origin, GridPosition destination)
            {
                Origin = origin;
                Destination = destination;
            }

            public GridPosition Origin { get; }
            public GridPosition Destination { get; }
        }

        private sealed class RelocationHarnessHandler
            : IOpHandler<RelocationHarnessOp, RelocationOutcome>
        {
            public async ValueTask<RelocationOutcome> Handle(
                OpFrame<RelocationHarnessOp> frame,
                OpHandlerContext context
            ) =>
                RequireResolved(
                    await context.Dispatch(
                        new RelocateTokenOp(
                            Mover,
                            frame.Op.Origin,
                            frame.Op.Destination,
                            frame.Id,
                            RelocationKind.FromSlug("test-relocation"),
                            TestSource
                        )
                    )
                ).Value;
        }

        private sealed class AtomicMovementObserver : IFactObserver<TokenMovedFact>
        {
            public List<TokenMovedFact> Facts { get; } = new List<TokenMovedFact>();
            public List<StepSnapshot> Snapshots { get; } = new List<StepSnapshot>();

            public ValueTask OnFactCommitted(TokenMovedFact fact, RulesSnapshot currentSnapshot)
            {
                Facts.Add(fact);
                Snapshots.Add(
                    new StepSnapshot(
                        currentSnapshot.Positions[fact.Mover],
                        currentSnapshot.MovementBudgets[fact.Mover].Remaining.Feet
                    )
                );
                return default;
            }
        }

        private sealed class ThrowAfterReduceMovementReducer
            : IOpReducer<CommitMovementStepOp, MovementStepCommitOutcome>
        {
            private readonly CommitMovementStepReducer inner;

            public ThrowAfterReduceMovementReducer(CommitMovementStepReducer inner) =>
                this.inner = inner;

            public ReductionResult<MovementStepCommitOutcome> Reduce(
                ReductionContext<CommitMovementStepOp> context,
                RulesStateDraft state,
                FactSink facts
            )
            {
                inner.Reduce(context, state, facts);
                throw new InvalidOperationException("injected movement reducer failure");
            }
        }

        private readonly struct StepSnapshot
        {
            public StepSnapshot(GridPosition position, int remaining)
            {
                Position = position;
                Remaining = remaining;
            }

            public GridPosition Position { get; }
            public int Remaining { get; }
        }

        private sealed class DepartureMiddleware
            : IOpMiddleware<MovementLeavingSquareOp, MovementTriggerOutcome>
        {
            private readonly AtomicMovementObserver observer;
            private readonly int affectedStep;
            private readonly bool relocate;
            private readonly GridPosition relocationDestination;

            public DepartureMiddleware(AtomicMovementObserver observer)
            {
                this.observer = observer;
            }

            public DepartureMiddleware(
                int affectedStep,
                bool relocate,
                GridPosition relocationDestination = default
            )
            {
                this.affectedStep = affectedStep;
                this.relocate = relocate;
                this.relocationDestination = relocationDestination;
            }

            public List<GridPosition> Positions { get; } = new List<GridPosition>();
            public List<int> ObserverCounts { get; } = new List<int>();
            public List<MovementTriggerId> Triggers { get; } = new List<MovementTriggerId>();
            public List<OpId> ActionIds { get; } = new List<OpId>();

            public async ValueTask<OpResult<MovementTriggerOutcome>> Invoke(
                OpFrame<MovementLeavingSquareOp> frame,
                OpMiddlewareContext context,
                OpNext<MovementTriggerOutcome> next
            )
            {
                Positions.Add(context.Snapshot.Positions[frame.Op.Mover]);
                ObserverCounts.Add(observer?.Facts.Count ?? 0);
                Triggers.Add(frame.Op.TriggerId);
                ActionIds.Add(frame.Op.ActionOpId);
                if (frame.Op.TriggerId.StepNumber != affectedStep)
                    return await next();
                if (!relocate)
                    return OpResult<MovementTriggerOutcome>.Resolved(
                        MovementTriggerOutcome.Interrupted
                    );

                OpResult<MovementTriggerOutcome> current = await next();
                RelocationOutcome relocatedOutcome = RequireResolved(
                    await context.Dispatch(
                        new RelocateTokenOp(
                            frame.Op.Mover,
                            frame.Op.From,
                            relocationDestination,
                            frame.Id,
                            RelocationKind.FromSlug("test-drift"),
                            TestSource
                        )
                    )
                ).Value;
                Assert.That(relocatedOutcome.Relocated, Is.True);
                return current;
            }
        }
    }
}
