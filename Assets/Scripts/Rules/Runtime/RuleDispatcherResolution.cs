using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    public sealed partial class RuleDispatcher
    {
        private async ValueTask<OpResult<TResult>> DispatchRoot<TResult>(
            IRuleOp<TResult> op,
            IRegistration registration,
            RootResolution resolution,
            OpId rootId,
            OpId? causeId,
            OpId? causalParentRootId,
            IRootResolutionObserver<TResult> observer
        )
        {
            OpResult<TResult> result;
            try
            {
                try
                {
                    result = await DispatchCore(
                        op,
                        registration,
                        resolution,
                        rootId,
                        null,
                        causeId
                    );
                    await InvokeRootCallback(
                        rootId,
                        () => observer.OnRootResolved(rootId, result, Snapshot)
                    );
                }
                catch (Exception resolutionException)
                {
                    IReadOnlyList<CommittedFactRecord> committedFacts = SnapshotCommittedFacts(
                        resolution,
                        rootId
                    );
                    if (committedFacts.Count == 0)
                        throw;

                    try
                    {
                        await NotifyFactListeners(rootId, committedFacts);
                    }
                    catch (Exception notificationException)
                    {
                        throw new AggregateException(
                            "Operation resolution and post-commit Fact notification both failed.",
                            resolutionException,
                            notificationException
                        );
                    }

                    throw;
                }

                if (result.Status != OpStatus.Invalid && result.Facts.Count > 0)
                {
                    await NotifyFactListeners(rootId, SnapshotCommittedFacts(resolution, rootId));
                }
                if (
                    result is ResolvedOpResult<TResult> resolved
                    && resolved.Value is ISettledOperationResult<TResult> settled
                )
                {
                    result = OpResult<TResult>
                        .Resolved(settled.Settle(Snapshot))
                        .WithFacts(result.Facts);
                }
            }
            catch (Exception primary)
            {
                await CallbackFailure.AwaitCleanupPreservingPrimary(
                    primary,
                    NotifyRootSettled(rootId, causalParentRootId)
                );
                throw;
            }

            await NotifyRootSettled(rootId, causalParentRootId);
            return result;
        }

        private async ValueTask<OpResult<TResult>> DispatchCore<TResult>(
            IRuleOp<TResult> op,
            IRegistration registration,
            RootResolution resolution,
            OpId rootId,
            OpId? parentId,
            OpId? causeId
        )
        {
            OpId id;
            int firstFact;
            IFrameInvocation invocation;
            IReadOnlyList<BoundMiddlewareRegistration> middleware;
            IReadOnlyList<BoundFactListenerRegistration> factListeners;
            RulesSnapshot startSnapshot;
            lock (gate)
            {
                RequireActiveResolution(resolution);
                id = parentId.HasValue ? ids.Next() : rootId;
                startSnapshot = store.Snapshot;
            }

            // Catalog and profile resolution are extension points. They consume the captured
            // snapshot but must never run while the dispatcher monitor is held; a slow or
            // cross-thread implementation must not block unrelated ownership checks.
            FrameActionState actionState = actionRuntime.CreateFrameState(
                id,
                rootId,
                parentId,
                causeId,
                registration.Policy,
                op,
                startSnapshot
            );

            lock (gate)
            {
                RequireActiveResolution(resolution);
                firstFact = resolution.Facts.Count;
                invocation = registration.CreateInvocation(
                    id,
                    rootId,
                    parentId,
                    causeId,
                    op,
                    startSnapshot,
                    actionState
                );
                middleware =
                    registration.MiddlewarePolicy == ResolverMiddlewarePolicy.Disabled
                        ? NoMiddleware
                        : ruleRegistry.SelectMiddleware(
                            op.GetType(),
                            typeof(TResult),
                            startSnapshot
                        );
                factListeners = ruleRegistry.SelectFactListeners(startSnapshot);
                Trace.Add(invocation.FrameView);
                resolution.EnterFrame(id, rootId, factListeners);
            }

            try
            {
                object resultObject;
                try
                {
                    resultObject = invocation.FrameView.IsAction
                        ? await InvokeActionLifecycle(registration, invocation, middleware, op)
                        : await InvokeWithMiddleware(registration, invocation, middleware, 0);
                }
                catch
                {
                    await SettleActiveChild(resolution, id);
                    throw;
                }

                if (await SettleActiveChild(resolution, id))
                {
                    throw new InvalidOperationException(
                        $"Operation {id.Value} returned before awaiting its active child dispatch."
                    );
                }

                if (!(resultObject is OpResult<TResult> result))
                    throw new InvalidOperationException(
                        $"Resolver for {op.GetType().Name} returned an impossible result type."
                    );

                if (
                    invocation.FrameView.IsAction
                    && result is ResolvedOpResult<TResult> resolvedAction
                )
                {
                    PublishActionResolvedFact(invocation, op, resolvedAction.Value);
                }

                OpResult<TResult> completed;
                lock (gate)
                {
                    RequireActiveResolution(resolution);
                    IReadOnlyList<RuleFact> directFacts = invocation.DirectFacts;

                    int subtreeFactCount = resolution.Facts.Count - firstFact;
                    IReadOnlyList<RuleFact> subtreeFacts = NoFacts;
                    if (subtreeFactCount > 0)
                    {
                        RuleFact[] subtreeFactArray = new RuleFact[subtreeFactCount];
                        resolution.Facts.CopyTo(firstFact, subtreeFactArray, 0, subtreeFactCount);
                        subtreeFacts = Array.AsReadOnly(subtreeFactArray);
                    }
                    completed = result.WithFacts(subtreeFacts);
                    Diagnostics.Complete(id, completed.Status, directFacts);
                }
                return completed;
            }
            finally
            {
                lock (gate)
                    resolution.ExitFrame(id, rootId);
            }
        }

        private async ValueTask<bool> SettleActiveChild(RootResolution resolution, OpId parentId)
        {
            Task settlement;
            lock (gate)
            {
                RequireActiveResolution(resolution);
                settlement = resolution.GetActiveChildSettlement(parentId);
                resolution.SealFrame(parentId);
            }

            if (settlement == null)
                return false;

            await settlement;
            return true;
        }

        private void RequireActiveResolution(RootResolution resolution)
        {
            if (!ReferenceEquals(activeRoot, resolution))
                throw new InvalidOperationException(
                    "An operation crossed resolution root ownership."
                );
        }

        private IRegistration RequireRegistration(Type opType, Type resultType)
        {
            if (!registrations.TryGetValue(opType, out IRegistration registration))
                throw new InvalidOperationException(
                    $"No resolver is registered for {opType.Name}."
                );
            if (registration.ResultType != resultType)
                throw new InvalidOperationException(
                    $"Registration for {opType.Name} returns {registration.ResultType.Name}, not {resultType.Name}."
                );
            return registration;
        }

        private sealed class RootResolution
        {
            public static RootResolution Idle { get; } = new RootResolution(true);

            private readonly bool isIdle;
            private readonly HashSet<OpId> activeFrames = new HashSet<OpId>();
            private readonly HashSet<OpId> sealedFrames = new HashSet<OpId>();
            private readonly Dictionary<OpId, ChildReservation> activeChildren =
                new Dictionary<OpId, ChildReservation>();
            private readonly Dictionary<
                OpId,
                IReadOnlyList<BoundFactListenerRegistration>
            > frameFactListeners =
                new Dictionary<OpId, IReadOnlyList<BoundFactListenerRegistration>>();
            private readonly HashSet<RuleFact> factReferences = new HashSet<RuleFact>(
                ReferenceEqualityComparer<RuleFact>.Instance
            );

            public OpId RootId { get; private set; }

            // This immutable correlation is inherited by causal child roots without adding
            // dispatcher provenance to the Fact payload itself.
            public OpId ObservationRootId { get; private set; }
            public bool IsIdle => isIdle;
            public List<RuleFact> Facts { get; } = new List<RuleFact>();
            public List<CommittedFactRecord> CommittedFacts { get; } =
                new List<CommittedFactRecord>();

            public RootResolution()
                : this(false) { }

            private RootResolution(bool isIdle)
            {
                this.isIdle = isIdle;
            }

            public void Initialize(OpId rootId, OpId observationRootId)
            {
                if (IsIdle)
                    throw new InvalidOperationException(
                        "The idle root sentinel cannot be initialized."
                    );
                if (!RootId.IsEmpty)
                    throw new InvalidOperationException(
                        "A root resolution was initialized more than once."
                    );
                if (rootId.IsEmpty)
                    throw new ArgumentException(
                        "A root resolution requires an operation ID.",
                        nameof(rootId)
                    );
                if (observationRootId.IsEmpty)
                    throw new ArgumentException(
                        "A root resolution requires an observation root ID.",
                        nameof(observationRootId)
                    );
                RootId = rootId;
                ObservationRootId = observationRootId;
            }

            public void EnterFrame(
                OpId id,
                OpId rootId,
                IReadOnlyList<BoundFactListenerRegistration> factListeners
            )
            {
                RequireCurrentRoot(rootId);
                if (!activeFrames.Add(id))
                    throw new InvalidOperationException(
                        $"Operation {id.Value} began executing more than once."
                    );
                frameFactListeners.Add(
                    id,
                    factListeners ?? throw new ArgumentNullException(nameof(factListeners))
                );
            }

            public void ExitFrame(OpId id, OpId rootId)
            {
                RequireCurrentRoot(rootId);
                if (activeChildren.ContainsKey(id))
                {
                    throw new InvalidOperationException(
                        $"Operation {id.Value} cannot exit while its child dispatch is active."
                    );
                }
                if (!activeFrames.Remove(id))
                    throw new InvalidOperationException(
                        $"Operation {id.Value} was not actively executing."
                    );
                if (!frameFactListeners.Remove(id))
                    throw new InvalidOperationException(
                        $"Operation {id.Value} has no listener selection."
                    );
                sealedFrames.Remove(id);
            }

            public ChildReservation ReserveChild(OpId parentId)
            {
                if (!activeFrames.Contains(parentId) || sealedFrames.Contains(parentId))
                {
                    throw new InvalidOperationException(
                        $"Operation context {parentId.Value} is not actively executing in the current root resolution."
                    );
                }
                if (activeChildren.ContainsKey(parentId))
                {
                    throw new InvalidOperationException(
                        $"Operation {parentId.Value} cannot begin an overlapping child dispatch. "
                            + "Await the active child before dispatching another."
                    );
                }

                ChildReservation reservation = new ChildReservation(parentId);
                activeChildren.Add(parentId, reservation);
                return reservation;
            }

            public void ReleaseChild(ChildReservation reservation)
            {
                if (
                    reservation == null
                    || !activeChildren.TryGetValue(
                        reservation.ParentId,
                        out ChildReservation active
                    )
                    || !ReferenceEquals(active, reservation)
                )
                {
                    string owner =
                        reservation == null ? "<unknown>" : reservation.ParentId.Value.ToString();
                    throw new InvalidOperationException(
                        $"Operation {owner} does not own its active child reservation."
                    );
                }
                activeChildren.Remove(reservation.ParentId);
            }

            public Task GetActiveChildSettlement(OpId parentId) =>
                activeChildren.TryGetValue(parentId, out ChildReservation reservation)
                    ? reservation.Settlement
                    : null;

            public void SealFrame(OpId id)
            {
                if (!activeFrames.Contains(id))
                {
                    throw new InvalidOperationException(
                        $"Operation {id.Value} cannot stop accepting children in its current state."
                    );
                }
                if (!sealedFrames.Add(id))
                    throw new InvalidOperationException(
                        $"Operation {id.Value} stopped executing more than once."
                    );
            }

            public CommittedFactRecord AddFact(
                RuleFact fact,
                OpId sourceId,
                OpId rootId,
                RuleSource source,
                RulesSnapshot snapshot
            )
            {
                if (fact == null)
                    throw new InvalidOperationException("A reducer returned a null Fact.");
                if (rootId != RootId)
                    throw new InvalidOperationException(
                        "A reducer emitted a Fact across resolution roots."
                    );
                if (
                    !frameFactListeners.TryGetValue(
                        sourceId,
                        out IReadOnlyList<BoundFactListenerRegistration> eligibleListeners
                    )
                )
                {
                    throw new InvalidOperationException(
                        "A committed Fact has no source-frame listener selection."
                    );
                }
                if (!factReferences.Add(fact))
                    throw new InvalidOperationException(
                        "A committed Fact was aggregated more than once."
                    );
                CommittedFactRecord committed = new CommittedFactRecord(
                    fact,
                    sourceId,
                    rootId,
                    ObservationRootId,
                    source,
                    snapshot,
                    eligibleListeners
                );
                Facts.Add(fact);
                CommittedFacts.Add(committed);
                return committed;
            }

            private void RequireCurrentRoot(OpId rootId)
            {
                if (rootId != RootId)
                    throw new InvalidOperationException(
                        "An operation frame crossed resolution roots."
                    );
            }
        }

        private sealed class ChildReservation
        {
            private readonly TaskCompletionSource<bool> settled = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            public OpId ParentId { get; }
            public Task Settlement => settled.Task;

            public ChildReservation(OpId parentId) => ParentId = parentId;

            public void Settle() => settled.TrySetResult(true);
        }
    }
}
