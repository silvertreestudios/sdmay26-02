using System;
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

        private static InMemoryRulesStore CreateStore() =>
            new InMemoryRulesStore(
                new RulesStateSeed()
                    .SeedPosition(Mover, Origin)
                    .SeedPosition(Occupant, Occupied)
                    .SeedPosition(OtherOccupant, new GridPosition(4, 0, 4))
                    .SeedActionEconomy(Mover, new ActionEconomyState(3, true))
            );

        private static RuleDispatcher CreateDispatcher(
            InMemoryRulesStore store,
            PermissionActionHandler handler
        ) =>
            new RuleDispatcherBuilder(store)
                .RegisterHandler<PermissionActionOp, bool>(handler)
                .RegisterHandler<MoveRelayOp, MovePathOutcome>(new MoveRelayHandler())
                .UseActionLifecycle(new FreeActionCatalog())
                .UseMovementRules(
                    new GridTopology(
                        new GridBounds(new GridPosition(0, 0, 0), new GridPosition(4, 0, 4)),
                        Array.Empty<GridCell>()
                    )
                )
                .Build();

        private static ResolvedOpResult<TResult> RequireResolved<TResult>(OpResult<TResult> result)
        {
            Assert.That(result, Is.TypeOf<ResolvedOpResult<TResult>>());
            return (ResolvedOpResult<TResult>)result;
        }

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
            public MovementFailure GrantFailure { get; private set; }

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

            private static async ValueTask<MovePathOutcome> Move(
                OpFrame<PermissionActionOp> frame,
                OpHandlerContext context,
                MovementBudgetId budgetId,
                MovementPath path,
                MovementPermission permission,
                MovementPermissionPurpose purpose
            ) =>
                RequireResolved(
                    await context.Dispatch(
                        new MovePathOp(frame.Id, Mover, budgetId, path, permission, purpose)
                    )
                ).Value;

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
    }
}
