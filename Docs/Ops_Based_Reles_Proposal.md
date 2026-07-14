# Operations-Based Rules Architecture

- **Status:** Alternative design proposal
- **Audience:** Gameplay and rules engineers
- **Scope:** Rules execution, reactions, state changes, active effects, and rule-facing Unity integration

This document proposes an alternative to the [command-based rules proposal](Command_Based_Rules_Proposal.md). It uses a Redux-like unidirectional data flow, extended with typed results, nested operations, and explicit lifecycle operations for Pathfinder 2e timing rules.

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

There is no separate general-purpose `Event` hierarchy. A pre-commit lifecycle message is an Op because it is work that can be handled, intercepted, and given a typed result. A post-commit notification is a Fact because it describes something already true. This keeps the model to two message concepts instead of Commands, Effects, Events, and Facts with overlapping responsibilities.

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
| `ActionProfile` | Frozen rules metadata for one invocation of an `ActionOp`: cost, traits, and start triggers. |
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
        var weapon = catalog.GetWeapon(Weapon);

        return ActionProfile.OneAction(
            traits: weapon.Traits.Add(Trait.Attack),
            startTriggers: ActionTriggerCatalog.ForStrike(weapon));
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
    ImmutableHashSet<ActionStartTrigger> StartTriggers);
```

The fields have different jobs:

- `Cost` is the PF2e action-economy cost: zero, one to three actions, reaction, or free action.
- `AdditionalCosts` contains consumable costs such as a spell slot, Focus Point, ammunition, or once-per-round use.
- `Traits` classify the action for rules that refer to traits.
- `StartTriggers` state which lifecycle events occur when this invocation begins.

Traits and triggers are intentionally separate. A Step has the move trait but does not trigger reactions based on movement. It therefore retains `Trait.Move` while omitting the movement start trigger.

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
- a stance or feat changing trigger semantics;
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

public sealed record OpResult<TResult>(
    OpStatus Status,
    TResult? Value,
    ImmutableArray<RuleFact> Facts,
    InvalidReason? InvalidReason = null);
```

The meanings are important:

- `Resolved`: the rules request legally resolved. A failed attack roll or failed skill check still has this status.
- `Invalid`: the request could not legally begin. It spends no cost and opens no action lifecycle window.
- `Interrupted`: it legally began and committed its costs, but a rule disrupted it before its main effect.
- `Cancelled`: an explicit workflow ended without committing its intended state change. This is used sparingly; declining a prompt usually resolves the prompt rather than cancelling its parent.

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

The `FactSink` supplies identity and provenance from the current frame when a reducer emits a domain Fact. Individual reducers supply only the domain data.

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

### 4.4 Commit and notification timing

Reducers apply one atomic state transition and enqueue its Facts. Typed fact listeners are notified after the current resolution batch commits. A listener therefore cannot retroactively prevent that state change.

Use the correct extension point:

- To modify or prevent work currently in progress, use middleware on an explicit lifecycle Op.
- To react to something that has already happened, listen to a committed Fact and dispatch new Ops.

For example, a damage-prevention feature participates before `ApplyDamageReducer` commits. Cranial Detonation listens to `CreatureReducedToZeroFact` because reaching 0 HP is its completed trigger.

Invalid Ops do not emit committed Facts and do not notify post-commit listeners.

---

## 5. Dispatch and extension points

### 5.1 Handlers orchestrate; they do not mutate

```csharp
public interface IOpHandler<TOp, TResult>
    where TOp : IRuleOp<TResult>
{
    ValueTask<TResult> Handle(
        OpFrame<TOp> frame,
        OpContext context);
}
```

A handler can read `context.Snapshot`, call pure services, roll through an injected deterministic roll service, prompt through an Op, and dispatch child Ops.

```csharp
var damage = await context.Dispatch(
    new ApplyDamageOp(target, packet, source));

if (damage.Status != OpStatus.Resolved)
    return MyOutcome.NoDamage;

// context.Snapshot is refreshed after the child reducer commits.
```

This is normal `async` C#. No custom `yield return` syntax or iterator return channel is required.

### 5.2 Middleware wraps selected Ops

```csharp
public delegate ValueTask<OpResult<TResult>>
    OpNext<TResult>();

public interface IOpMiddleware<TOp, TResult>
    where TOp : IRuleOp<TResult>
{
    ValueTask<OpResult<TResult>> Invoke(
        OpFrame<TOp> frame,
        OpContext context,
        OpNext<TResult> next);
}
```

Middleware is appropriate when a rule needs to inspect or alter an in-progress operation. Examples include:

- Reactive Strike middleware around `ActionBegunOp`;
- Bless middleware around `CollectAttackModifiersOp`;
- a replacement effect around a damage lifecycle Op;
- a reaction around `MovementLeavingSquareOp`.

Middleware ordering is deterministic: phase, rules-defined priority, active binding creation order, then binding ID. Features must not depend on dictionary or scene traversal order.

Middleware may dispatch nested Ops and await their typed results. It cannot directly mutate state.

### 5.3 Fact listeners run after commits

```csharp
public interface IFactListener<TFact>
    where TFact : RuleFact
{
    ValueTask OnFactCommitted(
        ActiveRuleBinding binding,
        TFact fact,
        FactContext context);
}
```

Typed registration matters. A rule interested in a creature reaching 0 HP registers once for `CreatureReducedToZeroFact`; it does not need to know every command, spell, hazard, or attack capable of dealing damage.

Some rules need to consider all matching Facts from one committed root together. The registry also supports a batch form:

```csharp
public interface IFactBatchListener<TFact>
    where TFact : RuleFact
{
    ValueTask OnFactsCommitted(
        ActiveRuleBinding binding,
        CommittedFactBatch<TFact> batch,
        FactContext context);
}
```

The runner groups the batch by committed root and delivers it only after that root has finished. Cranial Detonation uses this form so one spell that reduces several enemies to 0 HP creates one trigger and one prompt.

Fact listeners may dispatch Ops. Those Ops form a new, causally linked resolution batch. They still pass through normal validation and reducer rules.

### 5.4 ActionOp has a mandatory lifecycle

The dispatcher recognizes `ActionOp<TResult>` and applies this template around its feature handler:

```text
1. Authorize invocation and create the frame.
2. Build and freeze the effective ActionProfile.
3. Run pure validation.
4. Atomically commit all action and additional costs.
5. Dispatch ActionBegunOp for the frozen profile's start triggers.
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
        return OpResult.Invalid<TResult>(validation.Reason);

    var costs = await DispatchNested(
        frame,
        new CommitActionCostsOp(action.Actor, profile));
    if (costs.Status != OpStatus.Resolved)
        return OpResult.Invalid<TResult>(costs.InvalidReason!);

    var begun = await DispatchNested(
        frame,
        new ActionBegunOp(frame.Id));
    if (begun.Value.Decision == ActionStartDecision.Interrupted)
        return OpResult.Interrupted<TResult>();

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

Middleware follows `ActionOpId` to its trusted frame and reads the frozen `ActionProfile`, actor, target data, and provenance there. The action handler is not responsible for predicting all information future reactions might need.

If a future trigger needs information that is not universal to actions, it should use either:

- typed data from the originating action Op, obtained from the frame; or
- a more specific lifecycle Op at the actual timing point.

For example, leaving a threatened square is represented by `MovementLeavingSquareOp`, because it occurs during movement and contains square-level geometry. It is not inferred from a generic move trait.

### 5.6 Nested operations and prompts

Composite rules dispatch ordinary child Ops:

```csharp
var check = await context.Dispatch(
    new SkillCheckOp(actor, Skill.Acrobatics, reflexDc));

var answer = await context.Dispatch(
    new PromptChoiceOp<bool>(player, request));
```

The player implementation, AI implementation, replay implementation, and tests provide adapters for `PromptChoiceOp<TChoice>`. A handler does not directly open UI or pause a coroutine.

The dispatcher serializes a root resolution. A prompt can suspend that resolution, but another root Op cannot interleave and change combat state underneath it. Nested reactions are allowed because they belong to the same resolution tree.

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

Use an Op when the work needs middleware, provenance, a typed asynchronous result, a prompt, a random roll recorded in the resolution, or any possible state change. `CollectAttackModifiersOp` is therefore an Op even though it does not mutate state: active effects must be able to contribute to it.

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
    long CreationOrder);
```

At dispatch time, the registry selects registrations whose bindings are active in the current snapshot. Removing a condition, expiring a spell, unequipping an item, or spending a temporary granted reaction removes or disables its binding without rebuilding global listener lists.

### 6.2 Active effects own typed instance state

```csharp
public sealed record ActiveEffectInstance(
    ActiveEffectId Id,
    RuleDefinitionId DefinitionId,
    CreatureId Source,
    EffectDuration Duration,
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

Presentation uses the same selectors to show aura geometry and visible bonus icons. UI projections are derived views, not authoritative rule state.

---

## 7. Action selection and the Unity boundary

### 7.1 Selection payloads are typed per action

A single record with nullable weapon, target, path, spell, and area fields will not scale. Each action definition owns its selection type.

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
```

Examples:

```csharp
public sealed record StrikeSelection(ItemId Weapon, CreatureId Target);

public sealed record TumbleThroughSelection(
    ImmutableArray<GridPosition> Path,
    CreatureId Enemy,
    MovementMode Mode);

public sealed record CastSpellSelection(
    SpellSlotId Slot,
    SpellVariantId Variant,
    ISpellTargetSelection Targets);
```

`SelectionWorkflow<TSelection>` may be one click, a path plus target, multiple creatures, an area template and orientation, or several ordered choices. The generic action bar only handles availability and launches the definition's workflow.

### 7.2 Unity is an adapter, not the rules authority

Unity-facing code has four jobs:

1. Translate clicks, selected scene objects, and paths into stable IDs and typed selections.
2. Dispatch root Ops.
3. Observe committed Facts and snapshots.
4. Animate and render the result.

Rules code does not read a `Transform` to decide range and does not call `Creature.TakeDamage`. It reads a `GridPosition` from `RulesSnapshot` and dispatches `ApplyDamageOp`.

Animations may lag behind committed state. A presentation queue can translate Facts into movement, hit, floating-number, condition, and death animations in order. Presentation completion must not determine whether the rules change occurred.

---

## 8. Worked example: normal Strike

This example shows the basic action lifecycle, modifier collection, attack resolution, damage mutation, and MAP.

### 8.1 Public action Op

```csharp
public sealed record StrikeActionOp(
    CreatureId Actor,
    ItemId Weapon,
    CreatureId Target)
    : ActionOp<StrikeOutcome>(Actor, ActionIds.Strike)
{
    public override ActionProfile GetBaseProfile(IActionCatalog catalog)
    {
        var weapon = catalog.GetWeapon(Weapon);

        return ActionProfile.OneAction(
            traits: weapon.Traits.Add(Trait.Attack),
            startTriggers: ActionTriggerCatalog.ForStrike(weapon));
    }
}
```

Validation checks that the actor can act, owns or wields the weapon, can target the creature, and satisfies range and line-of-effect requirements. It does not roll and does not mutate state.

### 8.2 Handler

```csharp
public sealed class StrikeActionHandler
    : IOpHandler<StrikeActionOp, StrikeOutcome>
{
    public async ValueTask<StrikeOutcome> Handle(
        OpFrame<StrikeActionOp> frame,
        OpContext context)
    {
        var strike = await context.Dispatch(
            new ResolveStrikeOp(
                frame.Op.Actor,
                frame.Op.Weapon,
                frame.Op.Target,
                StrikePurpose.Normal,
                DamageSource.From(frame.Id)));

        if (strike.Status != OpStatus.Resolved)
            return StrikeOutcome.From(strike);

        await context.Dispatch(
            new IncrementMultipleAttackPenaltyOp(frame.Op.Actor));

        return StrikeOutcome.From(strike.Value);
    }
}
```

The standard `ActionOp` wrapper has already validated the Strike, spent one action, and opened `ActionBegunOp` before this handler runs.

`ResolveStrikeOp` is nested-only. It performs the reusable attack work but does not spend actions and does not change MAP:

```csharp
public sealed record ResolveStrikeOp(
    CreatureId Attacker,
    ItemId Weapon,
    CreatureId Target,
    StrikePurpose Purpose,
    DamageSource Source) : IRuleOp<StrikeResolution>;
```

The handler for `ResolveStrikeOp`:

1. dispatches `CollectAttackModifiersOp`;
2. adds MAP from the current snapshot;
3. rolls the attack through `IRollService`;
4. calculates degree of success against the target's AC;
5. on success, calculates a damage packet without changing HP;
6. dispatches `ApplyDamageOp` once;
7. returns the roll, degree, and damage outcome.

Bless and similar rules wrap `CollectAttackModifiersOp`. Damage resistance and immunity are handled in the damage workflow. `ApplyDamageReducer` is the only code that changes HP.

### 8.3 Required change to the current Strike pipeline

The existing `StrikeResolutionPipeline.Resolve` reaches `ApplyDefenseAndDamageAdjustment.Apply`, which calls `TakeDamage`. It cannot be wrapped and then followed by `ApplyDamageOp`, because that would apply damage twice and bypass the reducer invariant.

Before migrating Strike, split the current pipeline into:

- pure validation, roll, degree-of-success, and damage calculation; and
- one authoritative HP mutation through `ApplyDamageOp`.

Compatibility code may adapt the calculated result, but it must not call both mutation paths.

### 8.4 Resulting resolution tree

```text
StrikeActionOp
  CommitActionCostsOp
    ActionCostSpentFact
  ActionBegunOp
  ResolveStrikeOp
    CollectAttackModifiersOp
    ApplyDamageOp
      DamageAppliedFact
      CreatureReducedToZeroFact?
  IncrementMultipleAttackPenaltyOp
    MultipleAttackPenaltyChangedFact
```

The runner attaches those committed Facts to the root result automatically. The Strike handler does not copy them.

---

## 9. Worked example: Reactive Strike

Reactive Strike demonstrates pre-emption, reactions, trusted provenance, and the difference between action-level and movement-level triggers.

### 9.1 Trigger model

The effective `ActionProfile` explicitly lists action-start triggers. Relevant examples include:

```csharp
public enum ActionStartTrigger
{
    ManipulateActionBegun,
    MoveActionBegun,
    RangedAttackBegun
}
```

The action's base profile declares these from its concrete definition. The profile resolver may then add, remove, or replace them based on current rule state. Middleware never assumes that every action with the move trait emits `MoveActionBegun`. Step is the simplest counterexample.

Leaving a square is not an action-start trigger:

```csharp
public sealed record MovementLeavingSquareOp(
    CreatureId Mover,
    GridPosition From,
    GridPosition To,
    MovementTriggerKind Kind,
    TriggerId TriggerId) : IRuleOp<MovementTriggerOutcome>;
```

Stride and other movement workflows dispatch this nested-only Op immediately before a qualifying departure. Step omits it. Tumble Through can dispatch a synthetic departure when its failure rule requires one.

### 9.2 Active binding middleware

Owning Reactive Strike creates an active binding with middleware for both `ActionBegunOp` and `MovementLeavingSquareOp`.

Representative action-start middleware:

```csharp
public async ValueTask<OpResult<ActionStartOutcome>> Invoke(
    OpFrame<ActionBegunOp> frame,
    OpContext context,
    OpNext<ActionStartOutcome> next)
{
    var outcome = await next();
    if (outcome.Status != OpStatus.Resolved)
        return outcome;

    if (outcome.Value.Decision == ActionStartDecision.Interrupted)
        return outcome;

    var actionFrame = context.Trace.GetAction(frame.Op.ActionOpId);
    var profile = actionFrame.ActionProfile!;

    if (!Matches(profile.StartTriggers) ||
        !IsEligibleReactor(binding.Owner, actionFrame, context.Snapshot))
    {
        return outcome;
    }

    var choice = await context.Dispatch(
        PromptForReactiveStrike(binding, actionFrame));
    if (!choice.Value.Accepted)
        return outcome;

    var reaction = await context.DispatchAuthorized(
        binding,
        new ReactiveStrikeActionOp(
            binding.Owner,
            actionFrame.Actor,
            actionFrame.Id,
            binding.Id));

    if (reaction.Status == OpStatus.Resolved &&
        reaction.Value.DisruptsTriggeringAction)
        return OpResult.Resolved(ActionStartOutcome.Interrupted);

    return outcome;
}
```

The exact call structure may run `next` before or after a middleware's opportunity according to the registered phase. The important contract is deterministic ordering and a single shared `ActionStartOutcome` that later middleware can observe.

### 9.3 The reaction is its own ActionOp

```csharp
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
            startTriggers: []);
}
```

Authorization verifies that:

- the supplied binding is active and grants Reactive Strike to the actor;
- the triggering frame exists and matches that binding's trigger;
- the target, reach, enemy relationship, and reaction availability remain valid;
- this binding has not already responded to the same `TriggerId` where the rules prohibit it.

The action wrapper atomically spends the reaction before opening its own action-begun window.

Its handler dispatches `ResolveStrikeOp` with `StrikePurpose.Reaction`. It does not dispatch `IncrementMultipleAttackPenaltyOp`, and `ResolveStrikeOp` does not apply MAP for that purpose. These privileges follow from the trusted `ReactiveStrikeActionOp` frame and active binding, not three public booleans on `StrikeActionOp`.

If the attack critically succeeds and the triggering action has the manipulate trigger, the reaction returns `DisruptsTriggeringAction = true`. The parent `ActionBegunOp` returns interruption. The triggering action's costs remain spent because they committed before this reaction window.

### 9.4 Movement departures use the same reaction implementation

Middleware on `MovementLeavingSquareOp` checks reach and eligibility, prompts, then dispatches the same `ReactiveStrikeActionOp`. The reaction targets the mover and cites the movement trigger frame as its trusted origin.

The movement workflow continues only if the lifecycle outcome allows it. This avoids feature-specific type exclusions such as “move trait except `MovementStepCommand`” and gives future movement modes one canonical place to declare whether a departure triggers reactions.

---

## 10. Worked example: Bless

Bless demonstrates spellcasting costs, active effect state, derived bonuses, stacking, sustaining, and movement-safe area membership.

The project data defines Bless as a two-action spell with aura, concentrate, manipulate, and mental traits; a 15-foot emanation; a one-minute duration; and a Sustain option that increases the radius on later turns.

### 10.1 Casting uses the common spell ActionOp

```csharp
public sealed record CastSpellActionOp(
    CreatureId Actor,
    SpellSlotId Slot,
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
            [new SpellSlotCost(Slot)],
            variant.Traits,
            variant.StartTriggers);
    }
}
```

The action wrapper validates the chosen slot and variant, atomically spends two actions plus the spell slot, and then dispatches `ActionBegunOp`. A Reactive Strike can disrupt the cast because its frozen profile contains the manipulate trigger. The slot and actions remain spent on disruption.

After that standard lifecycle, `CastSpellHandler` delegates to the selected spell implementation. Bless dispatches `CreateActiveEffectOp`.

### 10.2 Bless stores only source-of-truth state

```csharp
public sealed record BlessAuraState(
    int RadiusFeet,
    RoundNumber CreatedRound,
    RoundNumber? LastExpandedRound) : IEffectState;
```

The created effect contains:

- the caster as source and aura center;
- the Bless rule definition;
- a one-minute duration;
- `BlessAuraState` with a 15-foot radius;
- an active Bless binding.

It does not create a child bonus effect on every ally.

### 10.3 Bless contributes during modifier collection

The active binding registers middleware for `CollectAttackModifiersOp`:

```csharp
public async ValueTask<OpResult<ModifierCollection>> Invoke(
    OpFrame<CollectAttackModifiersOp> frame,
    OpContext context,
    OpNext<ModifierCollection> next)
{
    var result = await next();
    var effect = context.Snapshot.ActiveEffects.Get(binding.EffectId!.Value);
    var state = effect.GetState<BlessAuraState>();

    if (IsAlly(effect.Source, frame.Op.Attacker, context.Snapshot) &&
        IsWithinEmanation(
            effect.Source,
            frame.Op.Attacker,
            state.RadiusFeet,
            context.Snapshot))
    {
        return result.Map(modifiers => modifiers.Add(
            Modifier.StatusBonus(
                amount: 1,
                source: binding.Source,
                appliesTo: CheckType.AttackRoll)));
    }

    return result;
}
```

The central modifier resolver applies PF2e typed-bonus stacking after all contributors run. Multiple Bless auras can contribute candidates without stacking multiple status bonuses.

Because membership is derived from current positions, Stride, forced movement, teleportation, spawning, and aura expansion all work without feature-specific movement listeners. No stored bonus can become stale.

### 10.4 Sustain updates typed instance state

The Bless action definition exposes a Sustain action while the effect is active and eligible:

```csharp
public sealed record SustainBlessActionOp(
    CreatureId Actor,
    ActiveEffectId BlessEffect)
    : ActionOp<SustainBlessOutcome>(Actor, ActionIds.SustainSpell)
{
    public override ActionProfile GetBaseProfile(IActionCatalog catalog) =>
        ActionProfile.OneAction(
            traits: [Trait.Concentrate],
            startTriggers: []);
}
```

Validation checks ownership, that the one-minute duration has not elapsed, that this is a subsequent turn, and that the radius has not already increased this round. Its handler dispatches the generic `UpdateActiveEffectStateOp<BlessAuraState>` with `RadiusFeet + 10` and the current round as `LastExpandedRound`.

The state update emits `ActiveEffectStateChangedFact`. The aura renderer and modifier middleware read the new radius immediately.

### 10.5 Visible state

A `BlessPresentationSelector` derives:

- the caster-centered aura geometry;
- the current radius;
- which creatures currently appear affected;
- the visible source text for the +1 status bonus.

This selector shares its range and alliance helpers with the rules middleware. The UI does not become a second rules implementation.

---

## 11. Worked example: Tumble Through

Tumble Through demonstrates a composite move action, a skill check, occupied-space permission, movement Facts, and a synthetic reaction trigger on failure.

### 11.1 Selection and profile

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
            startTriggers: [ActionStartTrigger.MoveActionBegun]);
}
```

Root validation checks that the path is well-formed, the enemy is on the proposed path, the movement mode is available, and the request is plausible. Dynamic blockers are still handled by movement resolution.

The standard wrapper spends one action and opens the action-level move trigger before the handler begins.

### 11.2 Movement is reusable nested work

```csharp
public sealed record MovePathOp(
    CreatureId Mover,
    ImmutableArray<GridPosition> Path,
    MovementBudgetId Budget,
    MovementPermission Permission)
    : IRuleOp<MovePathOutcome>;
```

`MovePathOp` is not an `ActionOp`. It is nested movement work, so it cannot accidentally spend another action or open another generic action-start window. It does dispatch square-level lifecycle Ops and reducers as movement progresses.

A scoped `MovementPermission` may authorize this Tumble Through frame to enter the chosen enemy's occupied spaces and treat those spaces as difficult terrain. The permission is engine-issued, names the parent Op and enemy, and cannot be reused by another caller.

### 11.3 Handler flow

Representative orchestration:

```csharp
public async ValueTask<TumbleThroughOutcome> Handle(
    OpFrame<TumbleThroughActionOp> frame,
    OpContext context)
{
    var startingSquare = context.Snapshot.PositionOf(frame.Op.Actor);
    var split = pathPlanner.SplitAtCreature(
        frame.Op.Path,
        frame.Op.Enemy,
        context.Snapshot);
    var budget = context.MovementBudgets.Create(
        frame.Op.Actor,
        frame.Op.Mode,
        context.Snapshot);

    var approach = await context.Dispatch(
        new MovePathOp(
            frame.Op.Actor,
            split.BeforeEnemy,
            budget,
            MovementPermission.Normal));

    if (!approach.Value.ReachedDestination)
        return TumbleThroughOutcome.MovementEnded(approach.Value);

    var check = await context.Dispatch(
        new SkillCheckOp(
            frame.Op.Actor,
            Skill.Acrobatics,
            context.Snapshot.ReflexDc(frame.Op.Enemy),
            CheckSource.From(frame.Id)));

    if (check.Value.Degree < DegreeOfSuccess.Success)
    {
        await DispatchFailedEntryTrigger(frame, startingSquare, context);
        return TumbleThroughOutcome.FailedCheck(check.Value.Degree);
    }

    var permission = context.MovementPermissions.ForTumbleThrough(
        frame,
        frame.Op.Enemy);

    var crossing = await context.Dispatch(
        new MovePathOp(
            frame.Op.Actor,
            split.FromEnemyThroughExit,
            budget,
            permission));

    var passedThrough = crossing.Facts
        .OfType<OccupiedSpaceTraversedFact>()
        .Any(fact => fact.Occupant == frame.Op.Enemy);

    if (!passedThrough)
    {
        await DispatchFailedEntryTrigger(frame, startingSquare, context);
        return TumbleThroughOutcome.CouldNotPass(crossing.Value);
    }

    return TumbleThroughOutcome.PassedThrough(
        check.Value.Degree,
        crossing.Value);
}
```

This is a sketch, not a requirement to allocate arrays or use LINQ in a hot path.

### 11.4 Important correctness details

The implementation must preserve these details:

- The movement budget spans both nested `MovePathOp` calls. Entering occupied spaces uses the rule's difficult-terrain cost.
- The occupied segment is preflighted and reserved through the first legal exit before its first movement reducer commits. If the remaining budget or current blockers cannot complete that crossing, the actor stays in the last legal square outside the enemy's space.
- A successful Acrobatics check is not proof that the actor traversed the enemy's space. `PassedThrough` is derived from `OccupiedSpaceTraversedFact`, emitted only after movement commits.
- If the check fails, or the creature lacks enough movement to get through after succeeding, movement ends and the handler dispatches the required synthetic departure trigger from the square where the action began.
- The synthetic event uses `MovementLeavingSquareOp` with `MovementTriggerKind.TumbleThroughFailure`, so Reactive Strike and future movement reactions use the same canonical path as real departures.
- Each trigger has a stable `TriggerId`. Reaction frequency and per-trigger deduplication prevent accidental repeated responses.
- If movement is interrupted before entering the occupied square, no traversal Fact exists and the outcome cannot claim success.

The exact reaction opportunities caused by the action beginning and by the failure departure remain distinct rules timing points. The engine represents both explicitly rather than hiding one in a trait heuristic.

---

## 12. Worked example: Cranial Detonation

Cranial Detonation is intentionally a stress test. It combines a completed spell trigger, a prompt, once-per-round use, forced death, overlapping emanations, basic saves, alternate mode selection, and chained explosions.

### 12.1 Trigger from committed Facts

An active Cranial Detonation binding registers an `IFactBatchListener<CreatureReducedToZeroFact>`.

For each matching Fact in the committed batch, the listener checks:

- the owner currently satisfies the feature's state requirement, such as unleashed psyche;
- the reduced creature is an enemy;
- the creature is not mindless;
- the feature is available this round;
- the damage's causation trace leads to a spell cast by the owner.

It does not compare `fact.SourceOpId` directly with the `CastSpellActionOp` frame ID. Damage is commonly applied by nested save and damage Ops. Instead it uses the engine-owned causal trace:

```csharp
var cast = context.Trace.FindCausingAncestor<CastSpellActionOp>(
    fact.SourceOpId,
    fact.Source);

if (cast is null || cast.Op.Actor != binding.Owner)
    return;
```

The listener groups eligible creatures by the causing cast. Because it receives the batch only after the root finishes, it prompts once per qualifying cast rather than once per creature.

```csharp
var choice = await context.Dispatch(
    new PromptChoiceOp<CranialDetonationChoice>(
        ownerPlayer,
        BuildCranialPrompt(eligibleOrigins)));

if (!choice.Value.Accepted)
    return;

await context.DispatchAuthorized(
    binding,
    new CranialDetonationActionOp(
        binding.Owner,
        binding.Id,
        cast.Id,
        eligibleOrigins,
        choice.Value.Mode));
```

The prompt itself does not spend the feature. Declining has no cost.

### 12.2 Authorized free action and frequency cost

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
            TraitsFor(Mode),
            []);
}
```

Authorization and validation recheck the binding, owner state, triggering cast, causal relationship, enemy relationship, non-mindless trait, current 0-HP state, and frequency availability. This protects against a stale prompt and against callers constructing the Op directly.

The common action wrapper atomically commits the once-per-round cost before the handler kills an origin or deals damage.

### 12.3 Chained resolution

The handler maintains three sets:

```csharp
var frontier = new Queue<CreatureId>(validatedInitialOrigins);
var detonatedOrigins = new HashSet<CreatureId>();
var resolvedTargets = new HashSet<CreatureId>();
```

Resolution proceeds in waves:

1. Remove all current frontier origins from the queue.
2. Revalidate that each origin is an enemy, at 0 HP, non-mindless, and not already detonated.
3. Dispatch the domain death workflow for each accepted origin and inspect its result.
4. Build the union of 15-foot emanations from the origins whose detonation committed.
5. Select creatures in that union that are not already in `resolvedTargets`.
6. Add all selected creatures to `resolvedTargets` before applying damage.
7. Roll one damage value for the wave and dispatch a generic area basic-save-and-damage Op.
8. Inspect committed `CreatureReducedToZeroFact` descendants from that area Op.
9. Add newly reduced enemies to the next frontier only if they are non-mindless and have not detonated.
10. Repeat until the frontier is empty.

Representative core:

```csharp
while (frontier.Count > 0)
{
    var origins = TakeWave(frontier)
        .Where(origin => IsEligibleOrigin(
            origin,
            detonatedOrigins,
            context.Snapshot))
        .ToImmutableArray();

    if (origins.IsEmpty)
        continue;

    var explodingOrigins = ImmutableArray.CreateBuilder<CreatureId>();
    foreach (var origin in origins)
    {
        var death = await context.Dispatch(
            new ApplyRuleDeathOp(
                origin,
                DeathSource.From(frame.Id)));
        detonatedOrigins.Add(origin);

        if (death.Status == OpStatus.Resolved && death.Value.Died)
            explodingOrigins.Add(origin);
    }

    if (explodingOrigins.Count == 0)
        continue;

    var area = areaService.UnionEmanations(
        explodingOrigins.ToImmutable(),
        radiusFeet: 15,
        context.Snapshot);
    var targets = area.Creatures
        .Where(target => resolvedTargets.Add(target))
        .ToImmutableArray();

    var wave = await context.Dispatch(
        new AreaBasicSaveDamageOp(
            targets,
            SaveFor(frame.Op.Mode),
            RollDamage(frame.Op.Mode, context.Rolls),
            DamageSource.From(frame.Id),
            TraitsFor(frame.Op.Mode)));

    foreach (var reduced in wave.Facts
        .OfType<CreatureReducedToZeroFact>())
    {
        if (IsEnemy(frame.Op.Actor, reduced.Creature, context.Snapshot) &&
            !context.Snapshot.HasTrait(reduced.Creature, Trait.Mindless) &&
            !detonatedOrigins.Contains(reduced.Creature))
        {
            frontier.Enqueue(reduced.Creature);
        }
    }
}
```

Every wave repeats the non-mindless filter. Applying it only to the initial origins would allow a mindless creature reduced in a later wave to become an illegal new origin.

### 12.4 Generic supporting workflows

`ApplyRuleDeathOp` owns the “would die” timing window, then commits the dead state if no rule prevents or replaces it. The feature handler never sets a `Dead` field directly.

`AreaBasicSaveDamageOp` is reusable. It:

- rolls or receives one base damage value according to the calling rule;
- dispatches a `SavingThrowOp` for each target;
- applies the basic-save multiplier;
- dispatches `ApplyDamageOp` with preserved causation;
- returns all committed descendant Facts automatically.

The Cranial Detonation mode changes the save, damage type, and traits supplied to this generic workflow. It does not require a second copy of the chain algorithm.

The union area and `resolvedTargets` set guarantee that a creature takes this feature's damage at most once per use, even when emanations overlap or a later wave also reaches it.

---

## 13. How the pieces fit together

The five examples exercise the architecture's main extension points:

| Requirement | Mechanism | Example |
| --- | --- | --- |
| PF2e action cost and traits | `ActionOp` plus frozen `ActionProfile` | Strike, Cast Spell, Tumble Through |
| Disruption after costs commit | Middleware on `ActionBegunOp` | Reactive Strike |
| Trigger during movement | `MovementLeavingSquareOp` | Reactive Strike, failed Tumble Through |
| Dynamic bonus contribution | Middleware on `CollectAttackModifiersOp` | Bless |
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
11. **Traits do not implicitly create lifecycle triggers.**
12. **Active rule state lives in the store, not in middleware instances or Unity objects.**
13. **Rule ordering is deterministic.**
14. **Each migrated field has one authoritative owner.**
15. **Rules assemblies contain no Unity scene-object references.**

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
  profile: 1 action; attack; trigger=Attack
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

Most engine tests should be EditMode tests against an in-memory `RulesState`, deterministic roll service, and scripted prompt adapter.

### Dispatcher contract tests

- invalid action: no cost Fact, no `ActionBegunOp`, no fact listeners;
- interrupted action: cost Fact exists, feature handler did not run;
- resolved failed check: `Resolved`, not `Invalid`;
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
- Step does not emit the movement trigger;
- qualifying Stride departure does emit it;
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
- Implement middleware, typed fact listeners, active bindings, and scripted prompts.
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

   Register a typed Fact listener.

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

If a feature needs a central `switch` on its definition ID, stop and check whether it belongs in a binding, typed state record, handler registration, or data definition instead.

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
