# Operations-Based Rules Architecture

- **Status:** Proposed architecture
- **Audience:** Gameplay and rules engineers
- **Scope:** Rules execution, reactions, state changes, active effects, and rule-facing Unity integration

This architecture uses a Redux-like unidirectional data flow, extended with typed results, nested operations, and explicit lifecycle operations for Pathfinder 2e timing rules.

The central idea is deliberately small:

1. Code dispatches an immutable **Operation**, or **Op**, describing requested rules work.
2. A handler or reducer resolves the Op.
3. Reducers are the only code allowed to change authoritative rules state.
4. A successful state change emits immutable **Facts** describing what actually changed.
5. Middleware can observe or alter explicit lifecycle Ops, such as an action beginning or a creature leaving a square.

PF2e **actions** remain a rules concept. We avoid calling every message an “action,” as Redux does, because names such as `TakeActionAction` would be confusing in this codebase.

---

## 1. Goals and non-goals

### Goals

- Give junior engineers one predictable route for rules work: construct an Op and dispatch it.
- Keep authoritative state changes centralized, deterministic, and testable without a Unity scene.
- Support PF2e timing windows, including reactions, disruption, replacement, and prevention.
- Preserve the distinction between an attempted change and a committed change.
- Make nested activities such as Strike, Tumble Through, spellcasting, and chained damage composable.
- Make active feats, spells, conditions, and equipment extend the engine without central feature switches.
- Retain enough provenance to answer “what caused this damage?” and “which spell caused this creature to reach 0 HP?”
- Allow incremental migration from the current Unity gameplay code.

### Non-goals

- This is not an event-sourced persistence system. Facts may be logged for debugging or replay, but the initial implementation does not rebuild all game state from a permanent fact log.
- This is not a general-purpose workflow framework. The contracts should contain only capabilities needed by game rules.
- This does not require every read to become an Op. Plain queries and selectors remain appropriate for side-effect-free reads.
- This does not move presentation concerns such as animation timing into the rules layer.

---

## 2. The mental model

An Op is a request to the rules engine. A Fact is evidence that state changed.

For example:

```text
ApplyDamageOp(target, 8 slashing)
        |
        v
validate -> reduce authoritative state -> emit facts
                                        |
                                        +-- DamageAppliedFact(8)
                                        +-- CreatureReducedToZeroFact(...)
```

The Op and the Facts must remain separate because a request is not proof of success. Damage might be prevented, reduced, redirected, or rejected. Code that reacts to a creature actually reaching 0 HP should listen for `CreatureReducedToZeroFact`, not for `ApplyDamageOp`.

There is no separate general-purpose `Event` hierarchy. A pre-commit lifecycle message is an Op because it is work that can be handled, intercepted, and given a typed result. A post-commit notification is a Fact because it describes something already true. The rules engine therefore has two public message concepts with distinct timing and responsibilities: Ops for requested work and Facts for committed changes.

The engine also needs interception points before some changes commit. These are modeled as ordinary, explicit Ops:

```text
StrikeActionOp
    -> validate
    -> commit action cost
    -> ActionBegunOp                 <-- reactions can run here
    -> ResolveStrikeOp
         -> CollectAttackModifiersOp <-- Bless contributes here
         -> ApplyDamageOp
              -> committed Facts     <-- post-commit rules observe these
```

This keeps the public model close to Redux—messages enter a dispatcher and state changes in reducers—without pretending that a PF2e activity can always be reduced in one synchronous function.

### Vocabulary

| Term | Meaning |
| --- | --- |
| `IRuleOp` | Marker for an immutable request sent to the rules engine. |
| `IRuleOp<TResult>` | An Op whose caller expects a typed result. |
| `ActionOp<TResult>` | An Op representing a PF2e action, reaction, or free action. |
| `ActionProfile` | Frozen rules metadata for one invocation of an `ActionOp`: cost, traits, and whether it can trigger reactions. |
| Handler | Orchestrates an Op, often by dispatching smaller Ops. It cannot directly mutate rules state. |
| Reducer | Validates and atomically changes authoritative rules state for a small state-changing Op. |
| Middleware | Wraps resolution of a selected Op type and can run before or after the next resolver. |
| Fact | Immutable description of a state change that committed. |
| Active binding | Connects an active feat, spell, condition, or item instance to its middleware and fact listeners. |
| Selector | A side-effect-free function that reads a snapshot and returns derived information. |

---

## 3. Core contracts

The code below is representative C#. Exact collection and identifier types can follow project conventions.

### 3.1 Operations

```csharp
public interface IRuleOp
{
}

public interface IRuleOp<TResult> : IRuleOp
{
}
```

Ops are immutable records containing identifiers and rules data. They must not contain `GameObject`, `MonoBehaviour`, `Transform`, callbacks, or mutable collections.

```csharp
public sealed record ApplyDamageOp(
    CreatureId Target,
    DamagePacket Damage,
    DamageSource Source) : IRuleOp<DamageOutcome>;

public sealed record PromptChoiceOp<TChoice>(
    PlayerId Player,
    ChoiceRequest<TChoice> Request) : IRuleOp<ChoiceResult<TChoice>>;
```

An Op is not inherently a state change. Some Ops gather modifiers, roll a check, ask a player a question, or orchestrate other Ops.

### 3.2 PF2e actions

Every operation that represents a PF2e action, reaction, or free action derives from `ActionOp<TResult>`.

```csharp
public abstract record ActionOp<TResult>(
    CreatureId Actor,
    ActionDefinitionId DefinitionId) : IRuleOp<TResult>
{
    public abstract ActionProfile GetBaseProfile(IActionCatalog catalog);
}
```

This distinction lets the dispatcher apply one mandatory action lifecycle. A normal state-changing Op such as `ApplyDamageOp` does not pay an action cost and does not open an action-begun reaction window.

An action subclass defines the profile information available from its own immutable data:

```csharp
public sealed record StrikeActionOp(
    CreatureId Actor,
    ItemId Weapon,
    CreatureId Target)
    : ActionOp<StrikeOutcome>(Actor, ActionIds.Strike)
{
    public override ActionProfile GetBaseProfile(IActionCatalog catalog)
    {
        var weapon = catalog.GetWeaponDefinition(Weapon);

        return ActionProfile.OneAction(
            traits: weapon.Traits.Add(Trait.Attack),
            canTriggerReactions: true);
    }
}
```

`GetBaseProfile` does not read live combat state. It reads stable definition data captured by IDs in the Op. Live state is handled by the profile resolver described below.

### 3.3 ActionProfile

```csharp
public sealed record ActionProfile(
    ActionCost Cost,
    ImmutableArray<RuleCost> AdditionalCosts,
    ImmutableHashSet<Trait> Traits,
    bool CanTriggerReactions = true);
```

The fields have different jobs:

- `Cost` is the PF2e action-economy cost: zero, one to three actions, reaction, or free action.
- `AdditionalCosts` contains consumable costs such as a spell slot, Focus Point, ammunition, or once-per-round use.
- `Traits` classify the action for rules that refer to traits.
- `CanTriggerReactions` is a blanket eligibility flag. It defaults to `true`; Step and other actions that explicitly trigger no reactions set it to `false`.

Traits do not determine reaction eligibility by themselves. A Step still has `Trait.Move`, but its profile sets `CanTriggerReactions` to `false`. Lifecycle Ops still occur for consistency and so non-reaction rules can observe what happened. Each reaction must inspect the originating action's frozen profile and return without prompting when `CanTriggerReactions` is `false`; it then matches its own trigger wording against the originating Op, its traits, and any needed geometry.

```csharp
public override ActionProfile GetBaseProfile(IActionCatalog catalog) =>
    ActionProfile.OneAction(
        traits: [Trait.Move],
        canTriggerReactions: false); // Step triggers no reactions.
```

`Trait` is an open, data-backed slug type shared with character, item, spell, and action definitions. Common traits may have helpers such as `Trait.Attack`, but the engine does not use a closed enum that must be edited for every new PF2e trait.

The base profile is resolved against the authoritative snapshot before validation:

```csharp
public interface IActionProfileResolver
{
    ActionProfile Resolve(
        ActionOpInfo action,
        ActionProfile baseProfile,
        RulesSnapshot snapshot);
}
```

The resolver handles state-dependent changes such as:

- a condition preventing reactions;
- Quickened Casting changing a spell's action cost;
- Conceal Spell adding traits;
- a stance or feat changing whether the action can trigger reactions;
- the selected spell variant changing components, cost, or traits.

The effective profile is frozen in the `OpFrame` for that invocation. Validation, cost commitment, lifecycle middleware, and the handler all see the same value even if nested rules later change state.

### 3.4 Frames and provenance

Every dispatch creates an engine-owned frame.

```csharp
public sealed record OpFrame<TOp>(
    OpId Id,
    OpId RootId,
    OpId? ParentId,
    OpId? CauseId,
    InvocationPolicy InvocationPolicy,
    TOp Op,
    ActionProfile? ActionProfile,
    RulesSnapshot StartSnapshot)
    where TOp : IRuleOp;
```

The identifiers serve different purposes:

- `Id` uniquely identifies this invocation.
- `RootId` groups all nested work started by one external dispatch.
- `ParentId` records control flow: which Op dispatched this one.
- `CauseId` records mechanical causation. It normally points to the parent, but an effect may preserve the spell, attack, or active rule that caused it.

The dispatcher, not callers, creates these values. It retains a `ResolutionTrace` that can answer questions such as:

```csharp
trace.IsDescendantOf(candidateOpId, ancestorOpId);
trace.IsCausedBy(candidateOpId, causeOpId);
trace.FindNearestAncestor<CastSpellActionOp>(candidateOpId);
trace.FindCausingAncestor<CastSpellActionOp>(candidateOpId);
```

This prevents privileged behavior from being selected with caller-controlled flags. For example, a reactive Strike does not ask the normal Strike handler to trust `CostsAction = false`. It invokes a nested-only strike resolution from a trusted reaction frame.

`InvocationPolicy` is either:

- `ExternalAllowed`: UI, AI, and tests may dispatch the Op as a root request.
- `NestedOnly`: only an authorized engine handler or binding may dispatch it.

Cost commitment, profile lifecycle Ops, generic reducers, and privileged rule workflows are normally nested-only.

### 3.5 Results

```csharp
public enum OpStatus
{
    Resolved,
    Invalid,
    Interrupted,
    Cancelled
}

public abstract class OpResult<TResult>
{
    public abstract OpStatus Status { get; }
    public ImmutableArray<RuleFact> Facts { get; }
}

public sealed class ResolvedOpResult<TResult> : OpResult<TResult>
{
    public TResult Value { get; }
    public override OpStatus Status => OpStatus.Resolved;
}

public sealed class InvalidOpResult<TResult> : OpResult<TResult>
{
    public string Reason { get; }
    public override OpStatus Status => OpStatus.Invalid;
}

public sealed class InterruptedOpResult<TResult> : OpResult<TResult>
{
    public override OpStatus Status => OpStatus.Interrupted;
}

public sealed class CancelledOpResult<TResult> : OpResult<TResult>
{
    public override OpStatus Status => OpStatus.Cancelled;
}
```

The meanings are important:

- `Resolved`: the rules request legally resolved. A failed attack roll or failed skill check still has this status.
- `Invalid`: the request could not legally begin. It spends no cost and opens no action lifecycle window.
- `Interrupted`: it legally began and committed its costs, but a rule disrupted it before its main effect.
- `Cancelled`: an explicit workflow ended without committing its intended state change. This is used sparingly; declining a prompt usually resolves the prompt rather than cancelling its parent.

The four outcomes are sealed structural cases, not one record containing conditionally valid fields. Only
`ResolvedOpResult<TResult>` exposes `Value`, and only `InvalidOpResult<TResult>` exposes `Reason`.
`Status` is derived from the concrete type for tracing and compact diagnostics; it is not a separate
discriminator that makes another base-class property safe to read. Callers pattern-match the concrete
case whenever they need outcome-specific data and explicitly decide how Invalid, Interrupted, and
Cancelled propagate through a composite workflow.

Factories on `OpResult<TResult>` construct the four cases without exposing their constructors:
`Resolved(value)`, `Invalid(reason)`, `Interrupted()`, and `Cancelled()`. The dispatcher preserves
the concrete case when it attaches the completed subtree's Facts.

The runner automatically includes committed descendant Facts in the result envelope. Handlers never maintain parallel “effects applied” or “facts emitted” builders.

---

## 4. Authoritative state, reducers, and Facts

### 4.1 One authoritative rules store

`RulesState` is the canonical model for combat rules. It contains stable IDs and plain data for creatures, health, positions, action economy, MAP, conditions, active effects, rule bindings, and frequency use.

```csharp
public interface IRulesStore
{
    RulesSnapshot Snapshot { get; }

    ReductionResult<TResult> Reduce<TOp, TResult>(
        OpFrame<TOp> frame,
        IOpReducer<TOp, TResult> reducer)
        where TOp : IRuleOp<TResult>;
}
```

Only reducers receive controlled write access. Handlers, middleware, selectors, prompt adapters, and Unity presenters receive read-only snapshots.

During migration, existing Unity components may remain the storage authority for systems not yet moved. A migrated slice must still have exactly one owner: the reducer updates rules state and a Unity adapter projects that committed state into scene objects.

### 4.2 Small state-changing Ops use reducers

```csharp
public interface IOpReducer<TOp, TResult>
    where TOp : IRuleOp<TResult>
{
    ReductionResult<TResult> Reduce(
        OpFrame<TOp> frame,
        RulesStateDraft state,
        FactSink facts);
}
```

`RulesStateDraft` is a private copy-on-write transaction, not the live store exposed elsewhere. A reducer is deterministic: it cannot dispatch, prompt, roll dice, call Unity, or perform I/O. Given the same starting snapshot and Op, it produces the same next state, result, and Facts. An immutable `state -> newState` implementation would satisfy the same contract; the draft form avoids copying the entire combat model for every small change.

Example:

```csharp
public sealed class ApplyDamageReducer
    : IOpReducer<ApplyDamageOp, DamageOutcome>
{
    public ReductionResult<DamageOutcome> Reduce(
        OpFrame<ApplyDamageOp> frame,
        RulesStateDraft state,
        FactSink facts)
    {
        var target = state.Creatures.Get(frame.Op.Target);
        var applied = target.Defenses.Apply(frame.Op.Damage);

        if (applied.Total <= 0)
            return ReductionResult.Resolved(new DamageOutcome(0));

        var previousHp = target.HitPoints.Current;
        target.HitPoints.Current = Math.Max(0, previousHp - applied.Total);

        facts.Emit(new DamageAppliedFact(
            frame.Op.Target,
            applied,
            frame.Op.Source));

        if (previousHp > 0 && target.HitPoints.Current == 0)
        {
            facts.Emit(new CreatureReducedToZeroFact(
                frame.Op.Target,
                frame.Op.Source));
        }

        return ReductionResult.Resolved(
            new DamageOutcome(applied.Total));
    }
}
```

The actual damage pipeline may need a pre-damage lifecycle for resistance replacement or prevention. The invariant remains: calculation can occur in a handler or pure service, but the final HP mutation occurs once, in a reducer.

### 4.3 Facts describe committed changes

```csharp
public abstract record RuleFact
{
    public FactId Id { get; internal init; }
    public OpId SourceOpId { get; internal init; }
    public OpId RootOpId { get; internal init; }
    public RuleSource Source { get; internal init; } = null!;
}
```

The `FactSink` supplies identity and provenance from the current frame when a reducer emits a domain Fact. Individual reducers supply only the domain data. A Fact also carries the transition payload that cannot be reconstructed from current state alone, such as old and new squares, damage applied, or a resource amount spent.

Useful Facts include:

- `ActionCostSpentFact`
- `ReactionSpentFact`
- `SpellSlotSpentFact`
- `DamageAppliedFact`
- `CreatureReducedToZeroFact`
- `TokenMovedFact`
- `OccupiedSpaceTraversedFact`
- `ConditionAppliedFact`
- `ActiveEffectCreatedFact`
- `ActiveEffectStateChangedFact`
- `MultipleAttackPenaltyChangedFact`

No Fact is emitted for a rejected mutation. If damage resolves to zero, the reducer can return a zero-damage outcome without emitting `DamageAppliedFact`. Facts are records of reality, not requested intent.

Committed Facts also drive the player-facing combat log. A Unity log presenter can listen for relevant Facts and render messages for damage, movement, conditions, disruption, effect expiration, and creatures reaching 0 HP without requiring each feature handler to construct log strings.

### 4.4 Commit and notification timing

Reducers apply one atomic state transition and commit its Facts. Dynamic typed Fact observers run after each individual reduction commit and are awaited before that reducer result returns to its parent handler. They receive the exact committed snapshot and may deliberately pace continuation, such as waiting for one movement animation before the next square is reduced. Binding-scoped Fact listeners retain their separate behavior: they run after the completed root resolution. Neither callback can retroactively prevent or roll back committed state.

Use the correct extension point:

- To modify or prevent work currently in progress, use middleware on an explicit lifecycle Op.
- To react to something that has already happened, listen to a committed Fact and dispatch new Ops.

For example, a damage-prevention feature participates before `ApplyDamageReducer` commits. Cranial Detonation listens to `CreatureReducedToZeroFact` because reaching 0 HP is its completed trigger.

An invalid reducer transition does not commit Facts. A root deliberately returned as invalid does not open post-commit listener delivery, even when an earlier nested reducer committed state and its durable Facts remain attached for diagnostics. If resolution throws after a nested commit, listeners receive those durable Facts exactly once before the resolution exception propagates. When resolution and notification both fail, the dispatcher reports a stable aggregate with the resolution exception first and the notification exception second.

---

## 5. Dispatch and extension points

### 5.1 Handlers orchestrate; they do not mutate

```csharp
public interface IOpHandler<TOp, TResult>
    where TOp : IRuleOp<TResult>
{
    ValueTask<TResult> Handle(
        OpFrame<TOp> frame,
        OpHandlerContext context);
}
```

A handler can read `context.Snapshot`, call pure services, roll through an injected deterministic roll service, prompt through an Op, and dispatch child Ops.

The worked examples later in this document show complete handlers. In each one, the handler uses the typed result of one child Op to decide whether to stop, recover, or dispatch the next child Op. `context.Snapshot` is refreshed after a child reducer commits.

### 5.2 Middleware wraps selected Ops

```csharp
public delegate ValueTask<OpResult<TResult>>
    OpNext<TResult>();

public interface IOpMiddleware<TOp, TResult>
    where TOp : IRuleOp<TResult>
{
    ValueTask<OpResult<TResult>> Invoke(
        OpFrame<TOp> frame,
        OpMiddlewareContext context,
        OpNext<TResult> next);
}
```

`OpHandlerContext` and `OpMiddlewareContext` make callback authority explicit. Both expose snapshot,
trace, and nested-dispatch services, while only `OpMiddlewareContext` exposes its required active
`Binding` and `Source`. A handler therefore cannot accidentally receive or exercise binding-scoped
authority, and middleware has one authoritative source for the binding that selected it.

Middleware is appropriate when a rule needs to inspect or alter an in-progress operation. Examples include:

- Reactive Strike middleware around `ActionBegunOp`;
- Bless middleware around `CollectAttackModifiersOp`;
- a replacement effect around a damage lifecycle Op;
- a reaction around `MovementLeavingSquareOp`.

Middleware ordering is deterministic, using the fixed semantic phases `Prevention`, `Transformation`, `Reaction`, and `Observation`, followed by active binding creation order and binding ID. Middleware nests in reverse phase order so returned results settle through those semantic phases in the listed order: observation wrappers therefore see the result after transformation and reaction middleware has finished. The first-pass design does not expose numeric priorities, which would create hard-to-see dependencies across unrelated rules. If two rules need meaningful ordering, represent that relationship with distinct lifecycle Ops or phases.

Middleware may dispatch nested Ops and await their typed results. Its `next()` continuation and a
nested dispatch share one callback-owned in-flight work slot: middleware must consume either result
before starting the other, even when the operation completed synchronously. A rejected overlap does
not consume an unused continuation or disturb the work already in progress. Middleware cannot
directly mutate state. Handler, middleware, and Fact-listener callbacks retain responsibility for
work they start until its result is consumed. If a callback throws after leaving that work
unconsumed, the dispatcher waits for cleanup before releasing ownership. A callback failure or
cleanup failure propagates unchanged when it is the only failure; when both fail, the dispatcher
reports an ordered aggregate containing the callback failure first and the cleanup failure second.

### 5.3 Fact observers and listeners run after commits

Unity-facing and other dynamically owned adapters use the minimal awaited observer contract:

```csharp
public interface IFactObserver<TFact>
    where TFact : RuleFact
{
    ValueTask OnFactCommitted(
        TFact fact,
        RulesSnapshot currentSnapshot);
}
```

Immediately before observer delivery, the dispatcher snapshots its current dynamic registrations in deterministic registration order, then delivers the reduction's Facts in commit order. Registration changes during a callback affect later reductions, not remaining Facts in that notification plan. Every selected matching delivery runs even when another observer fails. State is already durable; one failure propagates unchanged and multiple failures produce a deterministic aggregate without rollback.

Observers use only `currentSnapshot` for identity lookup and derived current state. Any before-and-after values needed by presentation belong in the typed Fact transition payload. There is no previous-snapshot envelope.

Rules selected through active bindings continue to use the root-scoped listener contract:

```csharp
public interface IRuleFactListener<TFact>
    where TFact : RuleFact
{
    ValueTask OnFactCommitted(
        TFact fact,
        FactContext context);
}
```

`FactContext.Binding` and `FactContext.Source` identify the active rule instance that selected the
listener, so the callback does not receive a second binding value that could disagree with its
context.

Typed registration matters. A rule interested in a creature reaching 0 HP registers once for `CreatureReducedToZeroFact`; it does not need to know every command, spell, hazard, or attack capable of dealing damage.

Listener eligibility is frozen from the source operation frame's start snapshot. A binding enabled or created by that frame cannot observe Facts committed by that frame or any earlier frame, but it participates in later frames that begin after the enabling commit. The dispatcher retains only the immutable bound listener registrations needed for that decision, not the complete historical snapshot. Immediately before each delivery it also checks the live snapshot, so a binding disabled, removed, or otherwise changed after an eligible Fact committed is skipped.

Some rules need to consider all matching Facts from one committed root together. The registry also supports a batch form:

```csharp
public interface IRuleFactBatchListener<TFact>
    where TFact : RuleFact
{
    ValueTask OnFactsCommitted(
        CommittedFactBatch<TFact> batch,
        FactContext context);
}
```

The runner groups the batch by committed root and delivers it only after that root has finished. Cranial Detonation uses this form so one spell that reduces several enemies to 0 HP creates one trigger and one prompt.

Fact listeners may dispatch Ops. Those Ops form a new, causally linked resolution batch. They still pass through normal validation and reducer rules.

### 5.4 ActionOp has a mandatory lifecycle

An `ActionOp` is specifically an Op for a PF2e action, such as Strike, Stride, Cast a Spell, a reaction, or a free action. These rules have a shared order for validation, paying costs, publishing the action-begun timing point, and resolving the action's own behavior. The mandatory lifecycle puts that shared order in one engine-owned pipeline instead of repeating it in every action handler.

The dispatcher recognizes `ActionOp<TResult>` and applies this template around its feature handler:

```text
1. Authorize invocation and create the frame.
2. Build and freeze the effective ActionProfile.
3. Run pure validation.
4. Atomically commit all action and additional costs.
5. Dispatch ActionBegunOp.
6. If ActionBegunOp reports disruption, return Interrupted.
7. Otherwise invoke the feature handler.
8. Complete the result and publish committed Facts.
```

Representative dispatcher code:

```csharp
private async ValueTask<OpResult<TResult>> DispatchAction<TResult>(
    ActionOp<TResult> action,
    DispatchRequest request)
{
    var frame = CreateActionFrame(action, request);
    var profile = profileResolver.Resolve(
        ActionOpInfo.From(frame),
        action.GetBaseProfile(actionCatalog),
        store.Snapshot);
    frame = frame with { ActionProfile = profile };

    var validation = validators.Validate(frame, store.Snapshot);
    if (!validation.IsValid)
        return OpResult<TResult>.Invalid(validation.Reason);

    var costs = await DispatchNested(
        frame,
        new CommitActionCostsOp(action.Actor, profile));
    if (costs is InvalidOpResult<ActionCostsOutcome> invalidCosts)
        return OpResult<TResult>.Invalid(invalidCosts.Reason);
    if (costs is not ResolvedOpResult<ActionCostsOutcome>)
    {
        throw new InvalidOperationException(
            "Atomic cost commitment can only resolve or reject before an action begins.");
    }

    var begun = await DispatchNested(
        frame,
        new ActionBegunOp(frame.Id));
    if (begun is not ResolvedOpResult<ActionStartOutcome> resolvedBegun)
    {
        throw new InvalidOperationException(
            "ActionBegunOp reports disruption through ActionStartOutcome.");
    }
    if (resolvedBegun.Value.Decision == ActionStartDecision.Interrupted)
        return OpResult<TResult>.Interrupted();

    return await InvokeHandler(frame);
}
```

`CommitActionCostsOp` receives the engine-owned frozen profile, is nested-only, rechecks availability, and commits all costs atomically. No listener can observe a partially spent action-plus-spell-slot cost.

This ordering has two important consequences:

- An invalid action never prompts reactions.
- A legally begun action keeps its committed costs when a reaction disrupts it.

Feature handlers do not dispatch `ActionBegunOp` and cannot forget it. They begin only after the standard lifecycle succeeds.

### 5.5 ActionBegunOp carries identity, not copied metadata

```csharp
public sealed record ActionBegunOp(OpId ActionOpId)
    : IRuleOp<ActionStartOutcome>;
```

Middleware follows `ActionOpId` to its trusted frame and reads the frozen `ActionProfile`, actor, target data, and provenance there. Reaction middleware first checks `ActionProfile.CanTriggerReactions`; other middleware may observe the lifecycle Op regardless of that flag. The action handler is not responsible for predicting all information future listeners might need.

If a future trigger needs information that is not universal to actions, it should use either:

- typed data from the originating action Op, obtained from the frame; or
- a more specific lifecycle Op at the actual timing point.

For example, leaving a threatened square is represented by `MovementLeavingSquareOp`, because it occurs during movement and contains square-level geometry. It identifies the originating action so reaction middleware can read that action's frozen profile before matching the departure trigger. The movement workflow dispatches this lifecycle Op even when reactions are ineligible.

Ordinary path movement resolves square by square. For each step it dispatches `MovementLeavingSquareOp`, commits the position change and a `TokenMovedFact` containing the old and new squares, awaits matching reduction observers, and only then continues to the next square. This keeps reaction timing authoritative while allowing presentation to pace the visible token path without gaining authority over the commit.

An authorized occupied crossing is the narrow exception to per-step commits. The movement workflow dispatches the occupied entry and immediate-exit `MovementLeavingSquareOp` timing points in order while the preceding legal square remains authoritative, then commits both position transitions and their combined budget change in one reducer transaction while the reserved occupant remains through exit timing. An exit interruption, cancellation, or invalid result settles that transaction and then stops at the legal exit. If either timing callback throws or commit-time revalidation rejects either half, neither half commits. Ordered `TokenMovedFact`, `OccupiedSpaceTraversedFact`, and exit `TokenMovedFact` payloads still describe both transitions, but every observer receives the transaction's final post-exit snapshot. If either timing point vacates the reserved occupant before entry commits, the entry and exit instead resume ordinary per-step commits and observer pacing, and an exit stop preserves only the legal entry prefix. No externally observable snapshot can therefore place the mover and reserved occupant in the same cell.

### 5.6 Nested operations and prompts

Composite rules dispatch ordinary child Ops:

```csharp
// A Tumble Through handler first resolves the check.
var check = await context.Dispatch(new SkillCheckOp(
    actor,
    Skill.Acrobatics,
    context.Snapshot.ReflexDc(enemy)));

if (check is InvalidOpResult<CheckOutcome> invalidCheck)
    return TumbleThroughOutcome.InvalidCheck(invalidCheck.Reason);
if (check is not ResolvedOpResult<CheckOutcome> resolvedCheck)
    throw new InvalidOperationException("A skill check cannot be interrupted or cancelled.");

// A legal failed check changes control flow: movement ends and the rule
// exposes the synthetic departure reaction window required by Tumble Through.
if (resolvedCheck.Value.Degree < DegreeOfSuccess.Success)
{
    await context.Dispatch(new MovementLeavingSquareOp(
        frame.Id,
        actor,
        startingSquare,
        startingSquare,
        MovementTriggerKind.TumbleThroughFailure,
        context.NewTriggerId()));

    return TumbleThroughOutcome.FailedCheck(resolvedCheck.Value.Degree);
}

// Only a successful check reaches the movement Op. Its committed Facts,
// rather than the successful check, determine whether traversal occurred.
var movement = await context.Dispatch(new MovePathOp(
    actor,
    crossingPath,
    movementBudget,
    tumbleThroughPermission));

if (movement is InvalidOpResult<MovePathOutcome> invalidMovement)
    return TumbleThroughOutcome.InvalidMovement(invalidMovement.Reason);
if (movement is not ResolvedOpResult<MovePathOutcome> resolvedMovement)
    throw new InvalidOperationException("Nested path movement cannot be interrupted or cancelled.");

var passedThrough = movement.Facts
    .OfType<OccupiedSpaceTraversedFact>()
    .Any(fact => fact.Occupant == enemy);

return passedThrough
    ? TumbleThroughOutcome.PassedThrough(resolvedCheck.Value.Degree, resolvedMovement.Value)
    : TumbleThroughOutcome.CouldNotPass(resolvedMovement.Value);
```

`Skill` is an open, slug-backed value rather than a closed enum. Static fields provide the standard
PF2e skills, while data can define Lore skills and other content-specific skills without an engine
code change. `SkillCheckOp` internally dispatches `CollectSkillCheckModifiersOp` before rolling;
`SavingThrowOp` does the same with `CollectSavingThrowModifiersOp`. Active effects therefore modify
checks and saves through the same typed, traceable middleware pattern used for attacks.

The player implementation, AI implementation, replay implementation, and tests register an
`IPromptAdapter<TChoice>` for each concrete choice type they resolve. The adapter receives only the
immutable `PromptChoiceOp<TChoice>` and the frame's captured `RulesSnapshot`; it does not receive a
dispatcher, mutable store, callback, or privileged context. A handler therefore never opens UI
directly, pauses a coroutine, or grants presentation code a path to mutate rules state.

`ChoiceResult<TChoice>` uses structural cases for normal outcomes:

- `SelectedChoiceResult<TChoice>` contains one request-declared choice. A content-level decline is a
  selected value, such as `false`, rather than a cancelled operation.
- `UnavailableChoiceResult<TChoice>` explains that no adapter can currently present or evaluate the
  request.
- `FailedChoiceResult<TChoice>` carries a typed `TimedOut` or `Disconnected` adapter-boundary failure.

All three are resolved prompt values. Only explicit cancellation of the surrounding decision workflow
returns `CancelledOpResult<ChoiceResult<TChoice>>`; adapters do not return `Invalid` or `Interrupted`
for expected prompt outcomes. EditMode tests use a test-assembly scripted adapter that consumes
explicit results in order. Production replay and simulation adapters inspect each request's stable
identity and declared choices before selecting a result; they do not depend on prompt order alone.

The dispatcher serializes root resolution through an asynchronous ownership gate. An unrelated
external root waits before allocating its root ID or frame, so a prompt can suspend its current root
without combat state changing underneath it. Nested reactions and other child Ops remain available
because they belong to the same resolution tree, and causally dispatched Fact-listener roots retain
the original external root's ownership window through post-commit notification. Code already executing
inside a resolution must use its handler, middleware, or Fact-listener context for nested work; calling
the dispatcher's public root API while that resolution still owns the gate is rejected instead of
waiting on its own ownership. A delayed continuation may submit a normal root after its originating
resolution releases ownership; it then follows the same serialization rules as any independent caller.

### 5.7 Read-only queries stay simple

Use selectors or pure services for reads that cannot prompt, mutate, or be intercepted:

```csharp
public interface IRulesSelectors
{
    int GetArmorClass(RulesSnapshot snapshot, CreatureId creature);
    bool IsEnemy(RulesSnapshot snapshot, CreatureId left, CreatureId right);
    GridDistance Distance(RulesSnapshot snapshot, TokenId left, TokenId right);
}
```

Use an Op when the work needs middleware, provenance, a typed asynchronous result, a prompt, a random roll recorded in the resolution, or any possible state change. `CollectAttackModifiersOp`, `CollectSkillCheckModifiersOp`, and `CollectSavingThrowModifiersOp` are therefore Ops even though they do not mutate state: active effects must be able to contribute before the corresponding roll.

---

## 6. Active rules and effect-instance state

### 6.1 Definitions are static; bindings are active

The registry contains static definitions:

```csharp
public sealed record RuleDefinition(
    RuleDefinitionId Id,
    ImmutableArray<MiddlewareRegistration> Middleware,
    ImmutableArray<FactListenerRegistration> FactListeners);
```

The store contains only currently active instances and bindings:

```csharp
public sealed record ActiveRuleBinding(
    BindingId Id,
    RuleDefinitionId DefinitionId,
    CreatureId Owner,
    ActiveEffectId? EffectId,
    RuleSource Source,
    long CreationOrder,
    bool IsEnabled = true);
```

At each operation frame boundary, the registry selects registrations whose bindings are active and enabled in the frame's start snapshot. Removing a condition, expiring a spell, unequipping an item, or spending a temporary granted reaction removes or disables its binding without rebuilding global listener lists. A binding activated during an operation begins participating with the next frame; a binding disabled or removed by a committed child operation is skipped immediately if its turn in the current middleware or listener plan has not begun. Fact delivery additionally preserves each source frame's selection as described in Section 5.3, preventing later activation from retroactively observing earlier commits in the same root.

### 6.2 Active effects own typed instance state

```csharp
public sealed record ActiveEffectInstance(
    ActiveEffectId Id,
    RuleDefinitionId DefinitionId,
    CreatureId Source,
    EffectDuration Duration,
    EffectStateVersion Version,
    IEffectState State);

public interface IEffectState
{
}

public sealed record UpdateActiveEffectStateOp<TState>(
    ActiveEffectId EffectId,
    EffectStateVersion ExpectedVersion,
    TState NewState) : IRuleOp<EffectStateUpdateOutcome>
    where TState : IEffectState;
```

The generic reducer checks that the definition accepts `TState`, verifies the expected version, updates it, and emits `ActiveEffectStateChangedFact`.

For example, Bless creates one aura effect whose instance state starts at a 15-foot radius. Sustaining it updates that same effect rather than replacing it or writing to a Bless-specific dictionary:

```csharp
public sealed record BlessAuraState(
    int RadiusFeet,
    RoundNumber CreatedRound,
    RoundNumber? LastExpandedRound) : IEffectState;

var aura = new ActiveEffectInstance(
    Id: context.NewActiveEffectId(),
    DefinitionId: BlessRule.AuraDefinitionId,
    Source: caster,
    Duration: EffectDuration.OneMinute,
    Version: EffectStateVersion.Initial,
    State: new BlessAuraState(
        RadiusFeet: 15,
        CreatedRound: context.Snapshot.Round,
        LastExpandedRound: null));

var created = await context.Dispatch(new CreateActiveEffectOp(aura));
if (created is InvalidOpResult<ActiveEffectCreationOutcome> invalidCreation)
    return CastSpellOutcome.EffectFailed(invalidCreation.Reason);
if (created is not ResolvedOpResult<ActiveEffectCreationOutcome>)
    throw new InvalidOperationException("Effect creation cannot be interrupted or cancelled.");

// On an eligible later turn, Sustain Bless reads the current version and state.
var current = context.Snapshot.ActiveEffects.Get(aura.Id);
var bless = current.GetState<BlessAuraState>();

var expanded = await context.Dispatch(
    new UpdateActiveEffectStateOp<BlessAuraState>(
        EffectId: aura.Id,
        ExpectedVersion: current.Version,
        NewState: bless with
        {
            RadiusFeet = bless.RadiusFeet + 10,
            LastExpandedRound = context.Snapshot.Round
        }));
```

This prevents every stateful rule from inventing a parallel dictionary or adding a field to a central switch. Bless can store its radius; a stance can store its selected mode; a once-per-target effect can store target IDs.

### 6.3 Derived effects are preferable to synchronized child effects

If a bonus is fully determined by current state, expose it through middleware or a selector instead of creating and removing many child effects.

Bless is the motivating example. Whether a creature receives its bonus is a function of:

- the active Bless effect;
- its current radius;
- the caster and candidate's teams;
- their current positions;
- the roll being an attack roll.

The Bless binding can calculate that during `CollectAttackModifiersOp`. Movement does not need to maintain a second list of child bonuses, and teleportation cannot make that list stale.

Presentation uses the same selectors to show aura geometry and visible bonus icons. UI projections are derived views, not authoritative rule state. A typed observer can refresh an affected view from the current committed snapshot when a relevant transition Fact arrives; the rules model does not maintain synchronized presentation state.

---

## 7. Action selection and the Unity boundary

### 7.1 Selection payloads are typed per action

A single record with nullable weapon, target, path, spell, and area fields will not scale. Each action definition therefore owns the exact selection data its UI workflow must produce.

An action definition connects the generic action bar to a concrete `ActionOp`:

- `GetAvailability` tells the action bar whether to show the action as usable and, when it is unavailable, why.
- `CreateSelectionWorkflow` tells Unity which choices to collect. Strike asks for a weapon and one creature; Tumble Through asks for a path and enemy; a spell might ask for several targets or an area orientation.
- `CreateOp` converts the completed, typed selection into the immutable root Op sent to the rules engine.

```csharp
public interface IActionDefinition<TSelection, TOp, TResult>
    where TOp : ActionOp<TResult>
{
    ActionAvailability GetAvailability(
        RulesSnapshot snapshot,
        CreatureId actor);

    SelectionWorkflow<TSelection> CreateSelectionWorkflow(
        RulesSnapshot snapshot,
        CreatureId actor);

    TOp CreateOp(CreatureId actor, TSelection selection);
}

public sealed record StrikeSelection(ItemId Weapon, CreatureId Target);

public sealed class StrikeActionDefinition
    : IActionDefinition<StrikeSelection, StrikeActionOp, StrikeOutcome>
{
    public ActionAvailability GetAvailability(
        RulesSnapshot snapshot,
        CreatureId actor) =>
        snapshot.ActionEconomy.CanSpend(actor, ActionCost.One) &&
        snapshot.Equipment.HasWieldedStrikeWeapon(actor)
            ? ActionAvailability.Available
            : ActionAvailability.Unavailable("No usable Strike");

    public SelectionWorkflow<StrikeSelection> CreateSelectionWorkflow(
        RulesSnapshot snapshot,
        CreatureId actor) =>
        SelectionWorkflow
            .ChooseOne(snapshot.Equipment.WieldedStrikeWeapons(actor))
            .ThenChooseOne((weapon, current) =>
                current.Targeting.LegalStrikeTargets(actor, weapon))
            .Select((weapon, target) => new StrikeSelection(weapon, target));

    public StrikeActionOp CreateOp(
        CreatureId actor,
        StrikeSelection selection) =>
        new(actor, selection.Weapon, selection.Target);
}

public sealed record TumbleThroughSelection(
    ImmutableArray<GridPosition> Path,
    CreatureId Enemy,
    MovementMode Mode);

public sealed record CastSpellSelection(
    SpellSlotPoolId SlotPool,
    SpellVariantId Variant,
    ISpellTargetSelection Targets);
```

When the player clicks Strike, the action bar asks `StrikeActionDefinition` for its workflow. Unity runs the two choices, receives a `StrikeSelection`, calls `CreateOp`, and dispatches the resulting `StrikeActionOp`. AI can produce the same selection without using Unity UI. The rules handler receives only the completed Op and still performs authoritative validation, since preview state may have changed before dispatch.

`SelectionWorkflow<TSelection>` may be one click, a path plus target, multiple creatures, an area template and orientation, or several ordered choices. The generic action bar only handles availability and launches the definition's workflow; it does not need nullable fields or a switch for every PF2e action.

Each concrete `ActionSelectionRequest<TSelection>` owns the immutable constraints for one action parameter, and the workflow rejects a resolver value outside those constraints. Completion, cancellation, and invalidity are separate structural outcomes. `Then` and `Select` compose typed values while cancellation or invalidity discards partial choices and skips all later steps. Only a completed outcome reaches `CreateOp`; selection cancellation and invalidity create no root frame and spend no resource.

The common framework exposes one generic action-selection resolver operation rather than enumerating every possible choice needed by current or future actions. Concrete request types and their player or AI handling land with the production feature that proves their shape. Rules-time decisions remain `PromptChoiceOp<TChoice>` operations and are not action-selection requests. Likewise, the action-bar registration and migration bridge are defined when the first production action uses them, so framework code does not prematurely choose scene-object mappings, AI planning APIs, or legacy replacement semantics.

### 7.2 Unity is an adapter, not the rules authority

Unity-facing code has four jobs:

1. Translate clicks, selected scene objects, and paths into stable IDs and typed selections.
2. Dispatch root Ops.
3. Observe committed Facts and snapshots.
4. Animate and render the result.

For example, when a player clicks the Strike button, Unity gathers a `StrikeSelection` and dispatches `StrikeActionOp`. The nested Strike rules may later dispatch `ApplyDamageOp`, but UI, AI, and other external callers cannot submit that nested-only mutation directly. Rules code reads positions from `RulesSnapshot`; it does not read a `Transform` or call `Creature.TakeDamage`.

Animations may lag behind committed state, or an awaited observer may intentionally pace a multi-reduction workflow. In both cases presentation observes a transition that is already true. It cannot reject, alter, or roll back that commit; failure is reported only after every matching observer selected for that notification has been attempted.

The main Unity assembly provides a configurable generic `MonoBehaviour` helper implementing `IFactObserver<TFact>`. A concrete component receives its `RuleDispatcher` explicitly from the composition root, registers while configured and enabled, and unregisters when disabled or destroyed. It uses no static event or singleton lookup. Unregistration prevents selection by later notification passes but does not cancel an observation already selected for an in-progress notification.

Each concrete observer handles one typed transition. For example, a `TokenMovedFact` supplies its old and new squares for animation while `currentSnapshot` supplies the token's authoritative current position and any derived current aura or UI state. This keeps transition history in Facts, current-state lookup in the snapshot, and Unity concerns outside the rules authority.

### 7.3 Feature modules own feature semantics

A rule, feat, spell, or action owns the code that explains what that feature means. Its cohesive
feature module contains its feature-specific Ops, validation, handlers, binding listeners, selectors,
persistent state, and any Unity adapter that extracts or presents feature data. These responsibilities
may be split across several focused classes; feature ownership is a dependency-boundary rule, not a
requirement to create one oversized class.

Shared runtime and Unity integration code provide mechanisms expressed in domain-neutral vocabulary:
dispatching an `IRuleOp<TResult>`, publishing a timing Fact, registering a binding, reading a snapshot,
or projecting committed state. They do not provide shortcuts such as `DispatchSpecificFeat`, remember
whether one feat's trigger was consumed, or decide whether one spell's conditions match.

A composition root is the narrow exception. It may name a feature module to register its definitions,
handlers, listeners, or initial bindings. That reference selects installed behavior; it must not
reimplement the feature's validation or workflow.

For example, general encounter code can publish an `InitiativeRolledFact`. A Quick-Tempered binding
can listen for that Fact and own the decision to start Rage. Likewise, a Rage action-bar adapter can
construct `RageActionOp` and pass it through a generic dispatch boundary; the bridge does not need a
Rage-specific dispatch method.

Before adding a feature-named member to a shared bridge, manager, dispatcher, facade, or catalog, ask
whether the feature can instead:

- construct and dispatch its own typed Op;
- react to an existing generic lifecycle Op or committed Fact;
- expose a feature-owned selector over `RulesSnapshot`; or
- register feature-owned behavior or bindings at the composition root.

---

## 8. Worked example: normal Strike

This complete sketch keeps the action data, validation, action handler, and reusable strike-resolution handler together. The engine-provided `ActionOp` pipeline still owns validation/cost/lifecycle ordering.

```csharp
// This is the public root Op created by UI or AI.
public sealed record StrikeActionOp(
    CreatureId Actor,
    ItemId Weapon,
    CreatureId Target)
    : ActionOp<StrikeOutcome>(Actor, ActionIds.Strike)
{
    public override ActionProfile GetBaseProfile(IActionCatalog catalog)
    {
        var weapon = catalog.GetWeaponDefinition(Weapon);

        return ActionProfile.OneAction(
            traits: weapon.Traits.Add(Trait.Attack),
            canTriggerReactions: true);
    }
}

// Validators run before the action cost or ActionBegunOp. They only read state.
public sealed class StrikeActionValidator
    : IActionValidator<StrikeActionOp>
{
    private readonly IActionCatalog actionCatalog;
    private readonly ITargetingService targeting;
    private readonly ILineOfEffectService lineOfEffect;

    public StrikeActionValidator(
        IActionCatalog actionCatalog,
        ITargetingService targeting,
        ILineOfEffectService lineOfEffect)
    {
        this.actionCatalog = actionCatalog;
        this.targeting = targeting;
        this.lineOfEffect = lineOfEffect;
    }

    public ValidationResult Validate(
        OpFrame<StrikeActionOp> frame,
        RulesSnapshot snapshot)
    {
        var op = frame.Op;

        if (!snapshot.Creatures.CanAct(op.Actor))
            return ValidationResult.Invalid("Actor cannot act");

        if (!snapshot.Equipment.IsWieldedBy(op.Weapon, op.Actor))
            return ValidationResult.Invalid("Weapon is not wielded by actor");

        if (!snapshot.Creatures.Exists(op.Target) ||
            !targeting.IsLegalAttackTarget(op.Actor, op.Target, snapshot))
        {
            return ValidationResult.Invalid("Target is not a legal creature");
        }

        var weapon = actionCatalog.GetWeaponDefinition(op.Weapon);
        if (!targeting.IsInStrikeRange(
                op.Actor,
                op.Target,
                weapon.Range,
                snapshot))
        {
            return ValidationResult.Invalid("Target is out of range");
        }

        if (!lineOfEffect.Exists(op.Actor, op.Target, snapshot))
            return ValidationResult.Invalid("No line of effect");

        return ValidationResult.Valid;
    }
}

// This nested-only Op contains reusable attack work. It does not spend an
// action and it never changes MAP. Its purpose is authorized from its parent
// frame, so an external caller cannot request the privileged reaction mode.
public sealed record ResolveStrikeOp(
    CreatureId Attacker,
    ItemId Weapon,
    CreatureId Target,
    StrikePurpose Purpose,
    DamageSource Source) : IRuleOp<StrikeResolution>;

public sealed class ResolveStrikeHandler
    : IOpHandler<ResolveStrikeOp, StrikeResolution>
{
    private readonly IActionCatalog actionCatalog;
    private readonly IAttackRollService attacks;
    private readonly IStrikeDamageService damage;

    public ResolveStrikeHandler(
        IActionCatalog actionCatalog,
        IAttackRollService attacks,
        IStrikeDamageService damage)
    {
        this.actionCatalog = actionCatalog;
        this.attacks = attacks;
        this.damage = damage;
    }

    public async ValueTask<StrikeResolution> Handle(
        OpFrame<ResolveStrikeOp> frame,
        OpHandlerContext context)
    {
        var op = frame.Op;
        var weapon = actionCatalog.GetWeaponDefinition(op.Weapon);

        // Bless and similar active rules contribute through middleware here.
        var modifiers = await context.Dispatch(
            new CollectAttackModifiersOp(
                op.Attacker,
                op.Target,
                op.Weapon,
                CheckSource.From(frame.Id)));

        if (modifiers is InvalidOpResult<ModifierCollection> invalidModifiers)
            return StrikeResolution.Aborted(invalidModifiers.Reason);
        if (modifiers is not ResolvedOpResult<ModifierCollection> resolvedModifiers)
            throw new InvalidOperationException(
                "Modifier collection cannot be interrupted or cancelled.");

        // Reactive Strike is authorized to ignore MAP. A normal Strike reads
        // the actor's current penalty but increments it only after this Op.
        var mapPenalty = op.Purpose == StrikePurpose.Normal
            ? context.Snapshot.MultipleAttackPenalty.ForNextAttack(op.Attacker)
            : 0;

        var roll = attacks.Roll(
            op.Attacker,
            weapon,
            resolvedModifiers.Value,
            mapPenalty,
            context.Rolls);
        var armorClass = context.Snapshot.Defenses.ArmorClass(op.Target);
        var degree = DegreeOfSuccessResolver.Resolve(roll.Total, armorClass);

        if (degree < DegreeOfSuccess.Success)
            return StrikeResolution.Miss(roll, degree, resolvedModifiers.Value);

        // Calculation is pure. It must not call TakeDamage.
        var packet = damage.Calculate(
            op.Attacker,
            weapon,
            degree,
            context.Snapshot,
            context.Rolls);

        // ApplyDamageReducer is the one authoritative HP mutation path.
        var applied = await context.Dispatch(
            new ApplyDamageOp(op.Target, packet, op.Source));

        var damageOutcome = applied switch
        {
            ResolvedOpResult<DamageOutcome> resolvedDamage => resolvedDamage.Value,
            InvalidOpResult<DamageOutcome> => DamageOutcome.None,
            _ => throw new InvalidOperationException(
                "Damage application cannot be interrupted or cancelled.")
        };

        return StrikeResolution.Hit(
            roll,
            degree,
            resolvedModifiers.Value,
            damageOutcome);
    }
}

public sealed class StrikeActionHandler
    : IOpHandler<StrikeActionOp, StrikeOutcome>
{
    public async ValueTask<StrikeOutcome> Handle(
        OpFrame<StrikeActionOp> frame,
        OpHandlerContext context)
    {
        // The ActionOp pipeline has already validated the action, spent one
        // action, and completed ActionBegunOp before this method runs.
        var strike = await context.Dispatch(new ResolveStrikeOp(
            frame.Op.Actor,
            frame.Op.Weapon,
            frame.Op.Target,
            StrikePurpose.Normal,
            DamageSource.From(frame.Id)));

        if (strike is InvalidOpResult<StrikeResolution> invalidStrike)
            return StrikeOutcome.Aborted(invalidStrike.Reason);
        if (strike is not ResolvedOpResult<StrikeResolution> resolvedStrike)
            throw new InvalidOperationException(
                "Nested strike resolution cannot be interrupted or cancelled.");

        // Every legally resolved normal Strike changes MAP, including a miss.
        var map = await context.Dispatch(
            new IncrementMultipleAttackPenaltyOp(frame.Op.Actor));

        return new StrikeOutcome(
            resolvedStrike.Value,
            MapIncremented: map is ResolvedOpResult<MultipleAttackPenaltyOutcome>);
    }
}
```

The existing `StrikeResolutionPipeline.Resolve` reaches `ApplyDefenseAndDamageAdjustment.Apply`, which calls `TakeDamage`. Before migrating Strike, split that pipeline into pure roll/damage calculation and one HP mutation through `ApplyDamageOp`; otherwise damage would be applied twice. The runner attaches action-cost, damage, reduced-to-zero, and MAP Facts to the root result automatically.

---

## 9. Worked example: Reactive Strike

Reactive Strike demonstrates pre-emption, reactions, trusted provenance, and the difference between action-level and movement-level timing. The engine always supplies lifecycle Ops for valid actions and movement timing points. Reactive Strike owns both the `CanTriggerReactions` eligibility check and its feature-specific trigger matching.

```csharp
// Movement workflows dispatch this immediately before a qualifying departure.
// Reaction middleware follows ActionOpId and checks CanTriggerReactions before
// prompting. Tumble Through can dispatch a synthetic instance when required.
public sealed record MovementLeavingSquareOp(
    OpId ActionOpId,
    CreatureId Mover,
    GridPosition From,
    GridPosition To,
    MovementTriggerKind Kind,
    TriggerId TriggerId) : IRuleOp<MovementTriggerOutcome>;

public sealed record ReactiveStrikeActionOp(
    CreatureId Actor,
    CreatureId Target,
    OpId TriggeringOpId,
    BindingId AuthorizedBinding)
    : ActionOp<ReactiveStrikeOutcome>(Actor, ActionIds.ReactiveStrike)
{
    public override ActionProfile GetBaseProfile(IActionCatalog catalog) =>
        ActionProfile.Reaction(
            traits: [Trait.Attack],
            canTriggerReactions: true);
}

// All Reactive Strike registrations and trigger matching remain local to this
// rule. The registry invokes these delegates once for each active binding.
public static class ReactiveStrikeRule
{
    public static readonly RuleDefinitionId DefinitionId =
        RuleIds.Feat("reactive-strike");

    public static void Register(IRuleRegistryBuilder rules)
    {
        rules.ActiveMiddleware<ActionBegunOp, ActionStartOutcome>(
            DefinitionId,
            OnActionBegun);
        rules.ActiveMiddleware<MovementLeavingSquareOp, MovementTriggerOutcome>(
            DefinitionId,
            OnLeavingSquare);
        rules.Validate<ReactiveStrikeActionOp>(
            DefinitionId,
            ValidateReaction);
        rules.Handle<ReactiveStrikeActionOp, ReactiveStrikeOutcome>(
            DefinitionId,
            HandleReaction);
    }

    private static async ValueTask<OpResult<ActionStartOutcome>> OnActionBegun(
        OpFrame<ActionBegunOp> frame,
        OpMiddlewareContext context,
        OpNext<ActionStartOutcome> next)
    {
        var binding = context.Binding;
        var current = await next();
        if (current is not ResolvedOpResult<ActionStartOutcome> resolvedCurrent ||
            resolvedCurrent.Value.Decision == ActionStartDecision.Interrupted)
        {
            return current;
        }

        var triggering = context.Trace.GetAction(frame.Op.ActionOpId);
        var profile = triggering.ActionProfile!;
        if (!profile.CanTriggerReactions)
            return current;

        var traits = profile.Traits;
        var matchesTrigger =
            traits.Contains(Trait.Manipulate) ||
            traits.Contains(Trait.Move) ||
            context.ActionSemantics.IsRangedAttack(
                triggering,
                context.Snapshot);

        if (!matchesTrigger ||
            !CanReact(binding, triggering.Actor, context.Snapshot))
        {
            return current;
        }

        var choice = await context.Dispatch(new PromptChoiceOp<bool>(
            context.Snapshot.PlayerFor(binding.Owner),
            ReactiveStrikePrompt.For(binding.Owner, triggering.Actor)));

        if (choice is not ResolvedOpResult<ChoiceResult<bool>> resolvedChoice ||
            resolvedChoice.Value is not SelectedChoiceResult<bool> selectedChoice ||
            !selectedChoice.Choice)
            return current;

        // DispatchAuthorized proves this Op came from the active feat binding.
        var reaction = await context.DispatchAuthorized(
            new ReactiveStrikeActionOp(
                binding.Owner,
                triggering.Actor,
                triggering.Id,
                binding.Id));

        return reaction is ResolvedOpResult<ReactiveStrikeOutcome> resolvedReaction &&
               resolvedReaction.Value.DisruptsTriggeringAction
            ? OpResult<ActionStartOutcome>.Resolved(ActionStartOutcome.Interrupted)
            : current;
    }

    private static async ValueTask<OpResult<MovementTriggerOutcome>>
        OnLeavingSquare(
            OpFrame<MovementLeavingSquareOp> frame,
            OpMiddlewareContext context,
            OpNext<MovementTriggerOutcome> next)
    {
        var binding = context.Binding;
        var current = await next();
        if (current is not ResolvedOpResult<MovementTriggerOutcome>)
            return current;

        var triggering = context.Trace.GetAction(frame.Op.ActionOpId);
        if (!triggering.ActionProfile!.CanTriggerReactions ||
            !CanReactToDeparture(binding, frame.Op, context.Snapshot))
        {
            return current;
        }

        var choice = await context.Dispatch(new PromptChoiceOp<bool>(
            context.Snapshot.PlayerFor(binding.Owner),
            ReactiveStrikePrompt.For(binding.Owner, frame.Op.Mover)));

        if (choice is ResolvedOpResult<ChoiceResult<bool>> resolvedChoice &&
            resolvedChoice.Value is SelectedChoiceResult<bool> { Choice: true })
        {
            await context.DispatchAuthorized(
                new ReactiveStrikeActionOp(
                    binding.Owner,
                    frame.Op.Mover,
                    frame.Id,
                    binding.Id));
        }

        return current;
    }

    private static ValidationResult ValidateReaction(
        OpFrame<ReactiveStrikeActionOp> frame,
        RulesSnapshot snapshot,
        ResolutionTrace trace)
    {
        var op = frame.Op;
        var binding = snapshot.RuleBindings.Find(op.AuthorizedBinding);

        if (binding is null ||
            binding.DefinitionId != DefinitionId ||
            binding.Owner != op.Actor)
        {
            return ValidationResult.Invalid("Binding does not grant reaction");
        }

        if (!snapshot.ActionEconomy.HasReaction(op.Actor) ||
            !trace.Exists(op.TriggeringOpId) ||
            !snapshot.Targeting.IsEnemyInMeleeReach(op.Actor, op.Target))
        {
            return ValidationResult.Invalid("Reactive Strike is no longer legal");
        }

        return ValidationResult.Valid;
    }

    private static async ValueTask<ReactiveStrikeOutcome> HandleReaction(
        OpFrame<ReactiveStrikeActionOp> frame,
        OpHandlerContext context)
    {
        // The ActionOp pipeline has now atomically spent the reaction.
        var strike = await context.Dispatch(new ResolveStrikeOp(
            frame.Op.Actor,
            context.Snapshot.Equipment.PreferredMeleeWeapon(frame.Op.Actor),
            frame.Op.Target,
            StrikePurpose.Reaction,
            DamageSource.From(frame.Id)));

        if (strike is InvalidOpResult<StrikeResolution> invalidStrike)
            return ReactiveStrikeOutcome.Aborted(invalidStrike.Reason);
        if (strike is not ResolvedOpResult<StrikeResolution> resolvedStrike)
            throw new InvalidOperationException(
                "Nested strike resolution cannot be interrupted or cancelled.");

        // The nested-only reaction purpose neither applies nor increments MAP.
        var triggeringAction = context.Trace.TryGetAction(
            frame.Op.TriggeringOpId);
        var disrupts = resolvedStrike.Value.Degree == DegreeOfSuccess.CriticalSuccess &&
            triggeringAction?.ActionProfile?.Traits.Contains(Trait.Manipulate) == true;

        return new ReactiveStrikeOutcome(resolvedStrike.Value, disrupts);
    }

    // These helpers include enemy/reach/reaction/trigger-deduplication checks.
    private static bool CanReact(
        ActiveRuleBinding binding,
        CreatureId target,
        RulesSnapshot snapshot) =>
        snapshot.ActionEconomy.HasReaction(binding.Owner) &&
        snapshot.Targeting.IsEnemyInMeleeReach(binding.Owner, target);

    private static bool CanReactToDeparture(
        ActiveRuleBinding binding,
        MovementLeavingSquareOp movement,
        RulesSnapshot snapshot) =>
        CanReact(binding, movement.Mover, snapshot) &&
        snapshot.Targeting.ThreatensSquare(binding.Owner, movement.From);
}
```

The triggering action has already committed its costs before this middleware runs, so critical manipulate disruption does not refund them. The same authorized reaction action handles action-start and square-departure triggers without accepting caller-controlled MAP or action-cost flags.

---

## 10. Worked example: Bless

Bless demonstrates spellcasting costs, active effect state, derived bonuses, stacking, sustaining, and movement-safe aura membership. Project data supplies its two-action cost, traits, 15-foot starting emanation, one-minute duration, and 10-foot Sustain expansion.

```csharp
// Cast a Spell is shared by all spells. The chosen spell variant supplies the
// PF2e action profile; the engine commits its actions and slot before opening
// ActionBegunOp, so manipulate disruption retains both costs.
public sealed record CastSpellActionOp(
    CreatureId Actor,
    SpellSlotPoolId SlotPool,
    SpellId Spell,
    SpellVariantId Variant,
    ISpellTargetSelection Targets)
    : ActionOp<CastSpellOutcome>(Actor, ActionIds.CastSpell)
{
    public override ActionProfile GetBaseProfile(IActionCatalog catalog)
    {
        var variant = catalog.GetSpellVariant(Spell, Variant);
        return new ActionProfile(
            variant.ActionCost,
            [RuleCost.SpellSlot(SlotPool)],
            variant.Traits,
            CanTriggerReactions: true);
    }
}

public sealed record BlessAuraState(
    int RadiusFeet,
    RoundNumber CreatedRound,
    RoundNumber? LastExpandedRound) : IEffectState;

public sealed record SustainBlessActionOp(
    CreatureId Actor,
    ActiveEffectId BlessEffect)
    : ActionOp<SustainBlessOutcome>(Actor, ActionIds.SustainSpell)
{
    public override ActionProfile GetBaseProfile(IActionCatalog catalog) =>
        ActionProfile.OneAction(
            traits: [Trait.Concentrate],
            canTriggerReactions: true);
}

public static class BlessRule
{
    public static readonly RuleDefinitionId AuraDefinitionId =
        RuleIds.SpellEffect("bless-aura");

    public static void Register(IRuleRegistryBuilder rules)
    {
        // CastSpellHandler delegates Bless's selected spell effect here.
        rules.ResolveSpell(SpellIds.Bless, HandleCast);

        // Every active Bless binding contributes to attack modifiers.
        rules.ActiveMiddleware<CollectAttackModifiersOp, ModifierCollection>(
            AuraDefinitionId,
            AddAttackModifier);

        rules.Validate<SustainBlessActionOp>(
            AuraDefinitionId,
            ValidateSustain);
        rules.Handle<SustainBlessActionOp, SustainBlessOutcome>(
            AuraDefinitionId,
            HandleSustain);
    }

    private static async ValueTask<CastSpellOutcome> HandleCast(
        OpFrame<CastSpellActionOp> frame,
        OpHandlerContext context)
    {
        // Store only the aura's source-of-truth instance state. Creating the
        // effect also activates the binding registered above.
        var aura = new ActiveEffectInstance(
            Id: context.NewActiveEffectId(),
            DefinitionId: AuraDefinitionId,
            Source: frame.Op.Actor,
            Duration: EffectDuration.OneMinute,
            Version: EffectStateVersion.Initial,
            State: new BlessAuraState(
                RadiusFeet: 15,
                CreatedRound: context.Snapshot.Round,
                LastExpandedRound: null));

        var created = await context.Dispatch(
            new CreateActiveEffectOp(aura));

        if (created is InvalidOpResult<ActiveEffectCreationOutcome> invalidCreation)
            return CastSpellOutcome.EffectFailed(invalidCreation.Reason);
        if (created is not ResolvedOpResult<ActiveEffectCreationOutcome>)
            throw new InvalidOperationException(
                "Effect creation cannot be interrupted or cancelled.");

        return CastSpellOutcome.Applied(aura.Id);
    }

    private static async ValueTask<OpResult<ModifierCollection>>
        AddAttackModifier(
            OpFrame<CollectAttackModifiersOp> frame,
            OpMiddlewareContext context,
            OpNext<ModifierCollection> next)
    {
        var binding = context.Binding;
        var result = await next();
        if (result is not ResolvedOpResult<ModifierCollection> resolvedResult)
            return result;

        var effect = context.Snapshot.ActiveEffects.Get(
            binding.EffectId!.Value);
        var state = effect.GetState<BlessAuraState>();

        if (!IsAlly(effect.Source, frame.Op.Attacker, context.Snapshot) ||
            !IsWithinEmanation(
                effect.Source,
                frame.Op.Attacker,
                state.RadiusFeet,
                context.Snapshot))
        {
            return result;
        }

        // The central modifier resolver performs typed-bonus stacking after
        // every binding contributes. Multiple Bless auras still give only the
        // highest applicable status bonus.
        return OpResult<ModifierCollection>.Resolved(resolvedResult.Value.Add(
            Modifier.StatusBonus(
                amount: 1,
                source: binding.Source,
                appliesTo: CheckType.AttackRoll)));
    }

    private static ValidationResult ValidateSustain(
        OpFrame<SustainBlessActionOp> frame,
        RulesSnapshot snapshot)
    {
        var effect = snapshot.ActiveEffects.Find(frame.Op.BlessEffect);
        if (effect is null ||
            effect.DefinitionId != AuraDefinitionId ||
            effect.Source != frame.Op.Actor)
        {
            return ValidationResult.Invalid("Actor does not own this Bless");
        }

        var state = effect.GetState<BlessAuraState>();
        if (effect.Duration.HasExpired(snapshot) ||
            snapshot.Round <= state.CreatedRound ||
            state.LastExpandedRound == snapshot.Round)
        {
            return ValidationResult.Invalid("Bless cannot expand this round");
        }

        return ValidationResult.Valid;
    }

    private static async ValueTask<SustainBlessOutcome> HandleSustain(
        OpFrame<SustainBlessActionOp> frame,
        OpHandlerContext context)
    {
        var effect = context.Snapshot.ActiveEffects.Get(frame.Op.BlessEffect);
        var state = effect.GetState<BlessAuraState>();

        var updated = await context.Dispatch(
            new UpdateActiveEffectStateOp<BlessAuraState>(
                effect.Id,
                effect.Version,
                state with
                {
                    RadiusFeet = state.RadiusFeet + 10,
                    LastExpandedRound = context.Snapshot.Round
                }));

        if (updated is InvalidOpResult<EffectStateUpdateOutcome> invalidUpdate)
            return SustainBlessOutcome.Failed(invalidUpdate.Reason);
        if (updated is not ResolvedOpResult<EffectStateUpdateOutcome>)
            throw new InvalidOperationException(
                "Effect-state updates cannot be interrupted or cancelled.");

        return SustainBlessOutcome.Expanded(state.RadiusFeet + 10);
    }

    // The HUD/aura renderer uses the same range and alliance helpers as the
    // modifier middleware. It can show the aura, current radius, affected
    // creatures, and +1 source before the player chooses to Strike.
    public static VisibleEffectProjection GetPresentation(
        ActiveEffectInstance effect,
        CreatureId viewedCreature,
        RulesSnapshot snapshot)
    {
        var state = effect.GetState<BlessAuraState>();
        var affectsCreature = IsAlly(effect.Source, viewedCreature, snapshot) &&
            IsWithinEmanation(
                effect.Source,
                viewedCreature,
                state.RadiusFeet,
                snapshot);

        return VisibleEffectProjection.Aura(
            effect.Id,
            displayName: "Bless",
            center: snapshot.PositionOf(effect.Source),
            radiusFeet: state.RadiusFeet,
            affectsViewedCreature: affectsCreature,
            detail: "+1 status bonus to attack rolls");
    }
}
```

Stride, forced movement, teleportation, spawning, and aura expansion all use current snapshot positions, so no stored child bonus can become stale. The player-facing Bless indicator is the derived projection described in Sections 6.3 and 7.2; it is pushed to affected displays after relevant Facts commit rather than stored as a second per-target rules effect.

---

## 11. Worked example: Tumble Through

Tumble Through demonstrates a composite move action, a skill check, occupied-space permission, movement Facts, and a synthetic reaction trigger on failure.

```csharp
public sealed record TumbleThroughSelection(
    ImmutableArray<GridPosition> Path,
    CreatureId Enemy,
    MovementMode Mode);

public sealed record TumbleThroughActionOp(
    CreatureId Actor,
    ImmutableArray<GridPosition> Path,
    CreatureId Enemy,
    MovementMode Mode)
    : ActionOp<TumbleThroughOutcome>(Actor, ActionIds.TumbleThrough)
{
    public override ActionProfile GetBaseProfile(IActionCatalog catalog) =>
        ActionProfile.OneAction(
            traits: [Trait.Move],
            canTriggerReactions: true);
}

// This is nested movement work, not another PF2e action. Both calls below use
// one budget and therefore cannot spend two actions or reset the actor's Speed.
public sealed record MovePathOp(
    CreatureId Mover,
    ImmutableArray<GridPosition> Path,
    MovementBudgetId Budget,
    MovementPermission Permission)
    : IRuleOp<MovePathOutcome>;

public sealed class TumbleThroughValidator
    : IActionValidator<TumbleThroughActionOp>
{
    private readonly IPathRules pathRules;

    public TumbleThroughValidator(IPathRules pathRules)
    {
        this.pathRules = pathRules;
    }

    public ValidationResult Validate(
        OpFrame<TumbleThroughActionOp> frame,
        RulesSnapshot snapshot)
    {
        var op = frame.Op;
        if (!snapshot.Creatures.CanUseMovementMode(op.Actor, op.Mode))
            return ValidationResult.Invalid("Movement mode is unavailable");

        if (!snapshot.Creatures.IsEnemy(op.Actor, op.Enemy))
            return ValidationResult.Invalid("Chosen creature is not an enemy");

        if (!pathRules.IsContiguous(op.Path, op.Mode, snapshot) ||
            !pathRules.CrossesCreature(op.Path, op.Enemy, snapshot))
        {
            return ValidationResult.Invalid("Path does not cross the enemy");
        }

        return ValidationResult.Valid;
    }
}

public sealed class TumbleThroughHandler
    : IOpHandler<TumbleThroughActionOp, TumbleThroughOutcome>
{
    private readonly IPathPlanner pathPlanner;

    public TumbleThroughHandler(IPathPlanner pathPlanner)
    {
        this.pathPlanner = pathPlanner;
    }

    public async ValueTask<TumbleThroughOutcome> Handle(
        OpFrame<TumbleThroughActionOp> frame,
        OpHandlerContext context)
    {
        // The common ActionOp lifecycle has spent one action and opened the
        // action-level move timing point before this method begins.
        var op = frame.Op;
        var startingSquare = context.Snapshot.PositionOf(op.Actor);
        var split = pathPlanner.SplitAtCreature(
            op.Path,
            op.Enemy,
            context.Snapshot);
        var budget = context.MovementBudgets.Create(
            op.Actor,
            op.Mode,
            context.Snapshot);

        // Move normally to the last legal square before the enemy.
        var approach = await context.Dispatch(new MovePathOp(
            op.Actor,
            split.BeforeEnemy,
            budget,
            MovementPermission.Normal));

        if (approach is InvalidOpResult<MovePathOutcome> invalidApproach)
            return TumbleThroughOutcome.InvalidMovement(invalidApproach.Reason);
        if (approach is not ResolvedOpResult<MovePathOutcome> resolvedApproach)
            throw new InvalidOperationException(
                "Nested path movement cannot be interrupted or cancelled.");

        if (!resolvedApproach.Value.ReachedDestination)
            return TumbleThroughOutcome.MovementEnded(resolvedApproach.Value);

        var check = await context.Dispatch(new SkillCheckOp(
            op.Actor,
            Skill.Acrobatics,
            context.Snapshot.ReflexDc(op.Enemy),
            CheckSource.From(frame.Id)));

        if (check is InvalidOpResult<CheckOutcome> invalidCheck)
            return TumbleThroughOutcome.InvalidCheck(invalidCheck.Reason);
        if (check is not ResolvedOpResult<CheckOutcome> resolvedCheck)
            throw new InvalidOperationException(
                "A skill check cannot be interrupted or cancelled.");

        if (resolvedCheck.Value.Degree < DegreeOfSuccess.Success)
        {
            await DispatchFailedEntryTrigger(
                frame,
                startingSquare,
                context);
            return TumbleThroughOutcome.FailedCheck(resolvedCheck.Value.Degree);
        }

        // Success alone is not enough. Before entering an occupied square,
        // reserve enough remaining movement to reach the first legal exit.
        // Enemy spaces use difficult-terrain cost. A failed preflight commits
        // no occupied-space movement and uses the same failure timing below.
        var crossingPlan = pathPlanner.PreflightOccupiedCrossing(
            split.FromEnemyThroughExit,
            op.Enemy,
            budget,
            difficultTerrain: true,
            context.Snapshot);

        if (!crossingPlan.CanComplete)
        {
            await DispatchFailedEntryTrigger(
                frame,
                startingSquare,
                context);
            return TumbleThroughOutcome.CouldNotPass(
                MovePathOutcome.InsufficientMovement);
        }

        // This engine-issued permission is scoped to this frame and enemy. A
        // different caller cannot reuse it to enter occupied spaces. Entry and
        // the first legal exit commit as one occupied-crossing transaction.
        var permission = context.MovementPermissions.ForTumbleThrough(
            frame,
            op.Enemy,
            crossingPlan.Reservation);

        var crossing = await context.Dispatch(new MovePathOp(
            op.Actor,
            crossingPlan.PathThroughExit,
            budget,
            permission));

        if (crossing is InvalidOpResult<MovePathOutcome> invalidCrossing)
        {
            await DispatchFailedEntryTrigger(
                frame,
                startingSquare,
                context);
            return TumbleThroughOutcome.InvalidMovement(invalidCrossing.Reason);
        }
        if (crossing is not ResolvedOpResult<MovePathOutcome> resolvedCrossing)
            throw new InvalidOperationException(
                "Nested path movement cannot be interrupted or cancelled.");

        // Only a committed traversal Fact proves that the rule succeeded.
        var passedThrough = crossing.Facts
            .OfType<OccupiedSpaceTraversedFact>()
            .Any(fact => fact.Occupant == op.Enemy);

        if (!passedThrough)
        {
            await DispatchFailedEntryTrigger(
                frame,
                startingSquare,
                context);
            return TumbleThroughOutcome.CouldNotPass(resolvedCrossing.Value);
        }

        return TumbleThroughOutcome.PassedThrough(
            resolvedCheck.Value.Degree,
            resolvedCrossing.Value);
    }

    private static async ValueTask DispatchFailedEntryTrigger(
        OpFrame<TumbleThroughActionOp> frame,
        GridPosition actionStartingSquare,
        OpHandlerContext context)
    {
        // Failure triggers reactions as though the actor had left the square
        // where the action began. A stable TriggerId supports deduplication.
        await context.Dispatch(new MovementLeavingSquareOp(
            frame.Id,
            frame.Op.Actor,
            actionStartingSquare,
            actionStartingSquare,
            MovementTriggerKind.TumbleThroughFailure,
            context.NewTriggerId()));
    }
}
```

The action-begin timing point and the failed-entry synthetic departure are distinct. If movement is blocked before traversal, no `OccupiedSpaceTraversedFact` exists and the response cannot claim that the enemy's space was crossed.

---

## 12. Worked example: Cranial Detonation

Cranial Detonation is intentionally a stress test. It combines a completed spell trigger, one prompt for multiple initial creatures, once-per-round use, forced death, overlapping emanations, basic saves, alternate mode selection, and chained explosions.

```csharp
public sealed record CranialDetonationActionOp(
    CreatureId Actor,
    BindingId AuthorizedBinding,
    OpId TriggeringCast,
    ImmutableArray<CreatureId> InitialOrigins,
    CranialDetonationMode Mode)
    : ActionOp<CranialDetonationOutcome>(
        Actor,
        ActionIds.CranialDetonation)
{
    public override ActionProfile GetBaseProfile(IActionCatalog catalog) =>
        new(
            ActionCost.FreeAction,
            [RuleCost.OncePerRound(AuthorizedBinding)],
            CranialDetonationRule.TraitsFor(Mode),
            []);
}

public static class CranialDetonationRule
{
    public static readonly RuleDefinitionId DefinitionId =
        RuleIds.Feat("cranial-detonation");

    public static void Register(IRuleRegistryBuilder rules)
    {
        rules.ActiveFactBatchListener<CreatureReducedToZeroFact>(
            DefinitionId,
            OnCreaturesReducedToZero);
        rules.Validate<CranialDetonationActionOp>(
            DefinitionId,
            ValidateAction);
        rules.Handle<CranialDetonationActionOp, CranialDetonationOutcome>(
            DefinitionId,
            HandleAction);
    }

    private static async ValueTask OnCreaturesReducedToZero(
        CommittedFactBatch<CreatureReducedToZeroFact> batch,
        FactContext context)
    {
        var binding = context.Binding;
        var snapshot = context.Snapshot;
        if (!snapshot.Psychic.IsPsycheUnleashed(binding.Owner) ||
            !snapshot.Frequencies.IsAvailableThisRound(binding.Id))
        {
            return;
        }

        // ApplyDamageOp is normally nested under saves and spell handlers, so
        // SourceOpId will not equal CastSpellActionOp.Id. Follow the trusted
        // causation trace and group every eligible origin by its causing cast.
        var byCast = new Dictionary<OpId, List<CreatureId>>();
        foreach (var fact in batch.Facts)
        {
            var cast = context.Trace.FindCausingAncestor<CastSpellActionOp>(
                fact.SourceOpId,
                fact.Source);

            if (cast is null || cast.Op.Actor != binding.Owner ||
                !IsEligibleOrigin(binding.Owner, fact.Creature, snapshot))
            {
                continue;
            }

            if (!byCast.TryGetValue(cast.Id, out var creatures))
            {
                creatures = new List<CreatureId>();
                byCast.Add(cast.Id, creatures);
            }

            creatures.Add(fact.Creature);
        }

        // A spell that reduced several enemies creates one prompt, not one
        // prompt per fact. Declining never dispatches the ActionOp and therefore
        // does not spend the once-per-round frequency.
        foreach (var (castId, candidates) in byCast)
        {
            if (!context.Snapshot.Frequencies.IsAvailableThisRound(binding.Id))
                break;

            var origins = candidates.Distinct().ToImmutableArray();
            var choice = await context.Dispatch(
                new PromptChoiceOp<CranialDetonationChoice>(
                    snapshot.PlayerFor(binding.Owner),
                    CranialDetonationPrompt.For(origins)));

            if (choice is not ResolvedOpResult<ChoiceResult<CranialDetonationChoice>>
                    resolvedChoice ||
                resolvedChoice.Value is not
                    SelectedChoiceResult<CranialDetonationChoice> selectedChoice ||
                !selectedChoice.Choice.Accepted)
            {
                continue;
            }

            await context.DispatchAuthorized(
                new CranialDetonationActionOp(
                    binding.Owner,
                    binding.Id,
                    castId,
                    origins,
                    selectedChoice.Choice.Mode));
        }
    }

    private static ValidationResult ValidateAction(
        OpFrame<CranialDetonationActionOp> frame,
        RulesSnapshot snapshot,
        ResolutionTrace trace)
    {
        var op = frame.Op;
        var binding = snapshot.RuleBindings.Find(op.AuthorizedBinding);
        if (binding is null ||
            binding.DefinitionId != DefinitionId ||
            binding.Owner != op.Actor ||
            !snapshot.Psychic.IsPsycheUnleashed(op.Actor) ||
            !snapshot.Frequencies.IsAvailableThisRound(binding.Id))
        {
            return ValidationResult.Invalid("Feat is not currently available");
        }

        if (!trace.Is<CastSpellActionOp>(op.TriggeringCast) ||
            op.InitialOrigins.IsEmpty ||
            op.InitialOrigins.Any(origin =>
                !IsEligibleOrigin(op.Actor, origin, snapshot) ||
                !trace.WasReducedToZeroBy(origin, op.TriggeringCast)))
        {
            return ValidationResult.Invalid("Triggering spell or origins are stale");
        }

        return ValidationResult.Valid;
    }

    private static async ValueTask<CranialDetonationOutcome> HandleAction(
        OpFrame<CranialDetonationActionOp> frame,
        OpHandlerContext context)
    {
        // The ActionOp pipeline has atomically spent the once-per-round use.
        var frontier = new Queue<CreatureId>(frame.Op.InitialOrigins);
        var attemptedOrigins = new HashSet<CreatureId>();
        var resolvedTargets = new HashSet<CreatureId>();
        var detonations = 0;

        while (frontier.Count > 0)
        {
            // Process only the origins present at the start of this wave. New
            // zero-HP facts become the next wave instead of altering iteration.
            var count = frontier.Count;
            var origins = ImmutableArray.CreateBuilder<CreatureId>();
            for (var i = 0; i < count; i++)
            {
                var candidate = frontier.Dequeue();
                if (attemptedOrigins.Add(candidate) &&
                    IsEligibleOrigin(
                        frame.Op.Actor,
                        candidate,
                        context.Snapshot))
                {
                    origins.Add(candidate);
                }
            }

            var exploding = ImmutableArray.CreateBuilder<CreatureId>();
            foreach (var origin in origins)
            {
                // ApplyRuleDeathOp owns the would-die lifecycle, then applies
                // Dead through its reducer. A prevented/replaced death cannot
                // silently become a committed detonation origin.
                var death = await context.Dispatch(new ApplyRuleDeathOp(
                    origin,
                    DeathSource.From(frame.Id)));

                if (death is ResolvedOpResult<RuleDeathOutcome> resolvedDeath &&
                    resolvedDeath.Value.Died)
                    exploding.Add(origin);
            }

            if (exploding.Count == 0)
                continue;

            detonations += exploding.Count;
            var area = context.Areas.UnionEmanations(
                exploding.ToImmutable(),
                radiusFeet: 15,
                context.Snapshot);

            // Add targets to the set before damage. Overlap in this wave or a
            // later wave can never damage the same creature twice in this use.
            var targets = area.Creatures
                .Where(target => resolvedTargets.Add(target))
                .ToImmutableArray();

            var damage = RollDamage(frame.Op.Mode, context.Rolls);
            var wave = await context.Dispatch(new AreaBasicSaveDamageOp(
                targets,
                SaveFor(frame.Op.Mode),
                damage,
                DamageSource.From(frame.Id),
                TraitsFor(frame.Op.Mode)));

            // AreaBasicSaveDamageOp dispatches SavingThrowOp and ApplyDamageOp
            // per target. The runner returns their committed descendant Facts.
            foreach (var reduced in wave.Facts
                .OfType<CreatureReducedToZeroFact>())
            {
                // Repeat every eligibility check on every wave. In particular,
                // a later mindless casualty must never become a chain origin.
                if (IsEligibleOrigin(
                        frame.Op.Actor,
                        reduced.Creature,
                        context.Snapshot) &&
                    !attemptedOrigins.Contains(reduced.Creature))
                {
                    frontier.Enqueue(reduced.Creature);
                }
            }
        }

        return new CranialDetonationOutcome(
            DetonatedOrigins: detonations,
            ResolvedTargets: resolvedTargets.Count);
    }

    private static bool IsEligibleOrigin(
        CreatureId owner,
        CreatureId creature,
        RulesSnapshot snapshot) =>
        snapshot.Creatures.IsEnemy(owner, creature) &&
        snapshot.HitPoints.Current(creature) == 0 &&
        !snapshot.Creatures.HasTrait(creature, Trait.Mindless);

    public static ImmutableHashSet<Trait> TraitsFor(
        CranialDetonationMode mode) =>
        mode == CranialDetonationMode.Mindshift
            ? [Trait.Death, Trait.Mental, Trait.Mindshift, Trait.Psyche]
            : [Trait.Death, Trait.Psyche];

    private static SaveKind SaveFor(CranialDetonationMode mode) =>
        mode == CranialDetonationMode.Mindshift
            ? SaveKind.Will
            : SaveKind.Reflex;

    private static DamagePacket RollDamage(
        CranialDetonationMode mode,
        IRollService rolls) =>
        CranialDetonationDamage.Roll(mode, rolls);
}
```

`AreaBasicSaveDamageOp` and `ApplyRuleDeathOp` are generic engine workflows, not Cranial-specific mutation shortcuts. The union area and `resolvedTargets` set enforce damage at most once per use, while the refreshed snapshot and repeated non-mindless filter make chained waves terminate legally.

---

## 13. How the pieces fit together

The five examples exercise the architecture's main extension points:

| Requirement | Mechanism | Example |
| --- | --- | --- |
| PF2e action cost, traits, and reaction eligibility | `ActionOp` plus frozen `ActionProfile` | Strike, Step, Cast Spell, Tumble Through |
| Disruption after costs commit | Middleware on `ActionBegunOp` | Reactive Strike |
| Trigger during movement | `MovementLeavingSquareOp` | Reactive Strike, failed Tumble Through |
| Dynamic bonus contribution | Middleware on typed attack, skill, or save modifier-collection Ops | Bless and other check/save effects |
| Persistent feature-local state | Typed `ActiveEffectInstance.State` | Bless radius |
| State change | Reducer | HP, position, MAP, action cost, aura state |
| Reaction to committed change | Typed fact listener | Cranial Detonation |
| Composite rules workflow | Handler dispatching child Ops | Strike, Tumble Through, Cranial Detonation |
| Player or AI decision | `PromptChoiceOp<TChoice>` | Reactive Strike, Cranial Detonation |
| Trusted privilege | Engine frame plus active binding authorization | Reactive Strike's MAP behavior, Cranial frequency |
| Causation through nested work | `OpFrame` trace and Fact provenance | Spell-caused 0 HP |
| Feature-specific targeting | Typed selection workflow | Strike, Cast Spell, Tumble Through |

This is the intended simplification: engineers learn one dispatch mechanism, one mutation rule, and two kinds of extension point—middleware before commitment and Facts after commitment.

---

## 14. Engine invariants

These rules should be enforced in code review and tests:

1. **Only reducers mutate authoritative rules state.**
2. **Handlers and middleware may mutate state only by dispatching Ops.**
3. **Facts are emitted only for committed changes.**
4. **The runner, not handlers, constructs the fact/audit envelope.**
5. **An invalid `ActionOp` spends no cost and opens no lifecycle window.**
6. **All action costs commit atomically before `ActionBegunOp`.**
7. **Disruption after action begin does not refund committed costs unless a separate rule explicitly refunds them.**
8. **Ordinary check failure is a resolved outcome, not an invalid Op.**
9. **The dispatcher creates frame IDs, ancestry, causation, and invocation policy.**
10. **Nested-only Ops reject external dispatch.**
11. **Lifecycle Ops occur consistently; each reaction checks `CanTriggerReactions` before applying its own matching logic.**
12. **Active rule state lives in the store, not in middleware instances or Unity objects.**
13. **Rule ordering is deterministic.**
14. **Each migrated field has one authoritative owner.**
15. **Rules assemblies contain no Unity scene-object references.**
16. **Each operation result is one sealed structural case; only Resolved exposes a value and only Invalid exposes a reason.**
17. **Attaching descendant Facts preserves the operation result's concrete case.**
18. **Feature-specific semantics stay in the feature module; shared runtime and Unity integration expose only generic mechanisms and composition.**

---

## 15. Error handling and diagnostics

Expected rules failures are typed results, not exceptions:

- insufficient actions;
- invalid target;
- unavailable reaction;
- obstructed path;
- stale active effect version;
- declined prompt.

Exceptions are reserved for programmer errors and broken invariants, such as a missing handler registration, a reducer emitting a Fact for another root, or an external caller reaching a nested-only Op.

In development builds, the dispatcher should record a compact resolution trace:

```text
[root 81] StrikeActionOp actor=A target=B
  profile: 1 action; attack; can-trigger-reactions=true
  CommitActionCostsOp -> Resolved
    fact: ActionCostSpentFact(1)
  ActionBegunOp -> Resolved
  ResolveStrikeOp -> Resolved(CriticalSuccess)
    CollectAttackModifiersOp -> +1 status from Bless
    ApplyDamageOp -> 17
      fact: DamageAppliedFact(17)
      fact: CreatureReducedToZeroFact(B)
  IncrementMultipleAttackPenaltyOp -> Resolved
```

The trace should use stable IDs and rules data, not `ToString()` on Unity objects. It can power test diagnostics and a future in-game rules inspector.

---

## 16. Testing strategy

Most engine tests should be EditMode tests against an in-memory `RulesState`, deterministic roll
service, and a test-only scripted prompt adapter.

### Dispatcher contract tests

- invalid action: no cost Fact, no `ActionBegunOp`, no fact listeners;
- interrupted action: cost Fact exists, feature handler did not run;
- resolved failed check: `Resolved`, not `Invalid`;
- result cases expose only their valid payload and retain their case when subtree Facts are attached;
- nested-only Op rejects root dispatch;
- child Facts appear in the root result once and with correct ancestry;
- middleware and fact listeners use stable ordering;
- a suspended prompt prevents unrelated root interleaving.

### Strike tests

- one action spent, attack resolved, MAP incremented;
- miss changes no HP and emits no damage Fact;
- damage mutates HP exactly once;
- Bless contributes one status bonus;
- reaction-purpose strike neither applies nor increments MAP;
- callers cannot request reaction-purpose privileges without authorization.

### Reactive Strike tests

- illegal triggering action does not prompt;
- manipulate action commits costs before a disrupting critical hit;
- Step emits the normal action and movement lifecycle Ops, but `CanTriggerReactions = false` prevents every reaction prompt;
- a qualifying Stride departure can prompt Reactive Strike;
- spent reaction prevents a second use;
- multiple reactors resolve in deterministic order.

### Bless tests

- casting commits actions and spell slot before a manipulate disruption;
- aura begins at 15 feet and expires after its duration;
- ally inside receives +1 status to attack rolls;
- enemy or ally outside does not;
- movement and teleportation change eligibility without child-effect updates;
- overlapping Bless effects obey status-bonus stacking;
- Sustain increases radius by 10 feet only on an eligible later turn and at most once per round.

### Tumble Through tests

- both movement segments share one movement budget;
- occupied squares use difficult-terrain cost;
- failed check ends movement and emits the synthetic departure trigger;
- successful check with insufficient movement uses the failure behavior;
- blocked crossing does not emit `OccupiedSpaceTraversedFact`;
- `PassedThrough` is true only when that Fact commits;
- scoped occupied-space permission cannot escape the parent frame.

### Cranial Detonation tests

- nested spell damage Facts match through causation ancestry;
- unrelated damage nested under the same root does not match;
- all eligible initial origins are presented in one prompt;
- declining spends no frequency;
- accepting atomically spends once-per-round frequency;
- a prevented or replaced death does not create an uncommitted detonation origin;
- overlapping emanations damage a creature once;
- later waves cannot damage an already resolved target;
- mindless creatures are excluded from initial and chained origins;
- chain resolution terminates;
- alternate mode changes save, damage type, and traits without changing the chain algorithm.

Use PlayMode tests only for adapter behavior that needs Unity objects: selection-to-ID mapping, fact-driven presentation, action bar availability, aura visualization, and scene synchronization.

---

## 17. Incremental migration plan

This architecture can be introduced vertically. It does not require rewriting every rule before the first feature works.

### Phase 1: runtime foundation

- Add ID and immutable data types needed by the first slice.
- Implement `RulesState`, snapshots, dispatcher, frames, results, handler/reducer registration, and deterministic tracing.
- Implement middleware, typed fact listeners, active bindings, typed prompts, and test-only scripted prompt fixtures.
- Add architecture tests for validation, costs, interruption, provenance, and Facts.

### Phase 2: split and migrate Strike

- Separate the current Strike pipeline's calculation from `TakeDamage` mutation.
- Implement action-cost, damage, and MAP reducers.
- Implement `StrikeActionOp`, `ResolveStrikeOp`, and modifier collection.
- Adapt the existing FSM/UI to dispatch `StrikeActionOp` and present its result.
- Keep exactly one HP mutation path.

### Phase 3: movement lifecycle

- Move authoritative positions and movement budget accounting behind reducers.
- Implement `MovePathOp`, `MovementLeavingSquareOp`, movement Facts, and permissions.
- Adapt Stride and Step, explicitly testing their different reaction semantics.

### Phase 4: active rules and Bless

- Add typed active effect state and generic create/update/expire reducers.
- Add derived modifier middleware and presentation selectors.
- Migrate Bless casting and Sustain.

### Phase 5: reactions and Tumble Through

- Add reaction cost support and `ActionBegunOp` middleware arbitration.
- Implement Reactive Strike using trusted active bindings.
- Implement Tumble Through on the shared movement and skill-check Ops.

### Phase 6: broader spell and feat composition

- Add generic saving throw, area, death, frequency, and spell-causation workflows.
- Use Cranial Detonation as a late stress test, not as the first production rule.
- Migrate additional content by composing existing Ops before adding new engine concepts.

At each phase, old and new systems may coexist only at explicit adapter seams. Avoid dual-writing the same HP, position, action, condition, or effect field.

---

## 18. Practical guidance for adding a rule

When implementing a new rule, answer these questions in order:

1. **Is it a PF2e action, reaction, or free action?**

   If yes, create an `ActionOp<TResult>` and define its base `ActionProfile`.

2. **What must be validated before costs are committed?**

   Put pure checks in the action validator. Recheck consumable availability atomically in the cost reducer.

3. **Does it change state?**

   Dispatch an existing state-changing Op or add a small reducer-backed Op. Do not mutate from the handler.

4. **Does it alter work before that work commits?**

   Register middleware for an existing lifecycle Op, or add a domain lifecycle Op if the timing point is genuinely new.

5. **Does it respond after a change occurred?**

   Register a binding-scoped typed Fact listener for rules behavior, or a dynamic typed Fact observer for an awaited external adapter.

6. **Does it need persistent per-instance data?**

   Define an `IEffectState` record and update it through the generic state Op.

7. **Can its visible effect be derived from current state?**

   Prefer a selector or modifier middleware over synchronized child effects.

8. **Does it need user or AI input?**

   Dispatch a typed prompt Op.

9. **Does it need special authority?**

   Derive that authority from a trusted frame and active binding. Do not add public bypass booleans.

10. **What Facts prove it worked?**

    Test the committed Facts and state, not only the returned outcome.

11. **Where does the feature-specific knowledge live?**

    Keep the feature's operations, validation, handlers, listeners, selectors, state, and Unity
    extraction or presentation in its feature module. Let shared code publish generic timing Facts,
    dispatch generic Ops, and register modules without learning the feature's rules.

If a feature needs a central `switch` on its definition ID, or a feature-named method, cache, or
trigger flag in a shared bridge or facade, stop and check whether it belongs in a binding, typed state
record, handler registration, selector, or feature adapter instead.

---

## 19. Rules references and content policy

The worked examples are based on the current remastered PF2e rules references:

- [Strike](https://2e.aonprd.com/Actions.aspx?ID=2306)
- [Reactive Strike](https://2e.aonprd.com/Actions.aspx?ID=2256)
- [Bless](https://2e.aonprd.com/Spells.aspx?ID=1451)
- [Tumble Through](https://2e.aonprd.com/Actions.aspx?ID=2370)
- [Cranial Detonation](https://2e.aonprd.com/Feats.aspx?ID=8347)

Rules behavior should remain data-driven where it is content-defined. Imported or adapted rule content must follow the repository's ORC licensing and provenance requirements. This design document paraphrases behavior to explain engine timing; it is not a replacement for licensed rule data or a complete rules reference.

---

## 20. Summary

The design has one main pipeline:

```text
Operation -> handler/middleware -> reducer -> authoritative state -> Facts
```

`ActionOp` adds the one lifecycle shared by PF2e actions:

```text
validate -> commit costs -> ActionBegunOp -> feature handler
```

That small addition is what makes a Redux-like approach fit PF2e. It centralizes the easy-to-forget timing work while leaving feature code to compose typed Ops. Explicit lifecycle Ops handle pre-emption and modification; Facts handle reactions to committed changes; typed active bindings keep content extensible.

Strike, Reactive Strike, Bless, Tumble Through, and Cranial Detonation all use the same contracts. None requires handlers to mutate state, manually publish audit envelopes, trust bypass flags, or keep feature-specific mirrors of derived state.
