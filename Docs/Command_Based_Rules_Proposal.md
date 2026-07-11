# Command-Based PF2e Rules Proposal

This proposal describes a command-based rules architecture for future PF2e actions, reactions, spells, auras, and feat behavior. The goal is to prevent core gameplay classes from accumulating feature-specific `if` statements while still giving players transparent UI state such as active effects, available reactions, and logged rule causes.

The key idea is that gameplay operations are typed commands with typed responses and lifecycle phases. Features register listeners for the specific command types and phases they care about. A feature such as Reactive Strike listens to movement and manipulate commands at `Begin`; a feature such as Bless listens to movement commands at `End`; a feature such as Tumble Through creates a movement command with action-scoped movement rules.

Rules references:

- Strike: https://2e.aonprd.com/Actions.aspx?ID=2306
- Reactive Strike: https://2e.aonprd.com/Actions.aspx?ID=2256
- Bless: https://2e.aonprd.com/Spells.aspx?ID=1451
- Tumble Through: https://2e.aonprd.com/Actions.aspx?ID=2370&Redirected=1

## Design Goals

- Keep PF2e feature behavior self-contained in feature classes.
- Let the engine expose generic command lifecycles instead of feature-specific hooks.
- Preserve immediate UI transparency for active effects such as Bless.
- Support preemption, prompts, nested commands, typed responses, and cancellation.
- Keep modifier math centralized through `IPf2eModifierProvider` and `Pf2eModifierResolver`.
- Introduce this incrementally without replacing existing `EntityAction`, HUD, grid, and Strike code in one large rewrite.

## Core Model

Use typed commands and typed responses, not one generic stringly-typed event payload. Every command declares the response type it returns. Commands whose response is not important can use `BasicCommandResponse`; commands whose results drive later rules should expose typed response fields.

```csharp
public interface IRuleCommand
{
    Guid Id { get; }
    Guid? ParentId { get; set; }
    Guid RootId { get; set; }
    string SourceFeatureId { get; set; }
    GameObject Actor { get; }
    IReadOnlyList<GameObject> Targets { get; }
    bool Cancelled { get; }

    void Cancel(string source, string reason);
}

public interface IRuleCommand<TResponse> : IRuleCommand
    where TResponse : CommandResponse
{
}

public abstract class RuleCommand<TResponse> : IRuleCommand<TResponse>
    where TResponse : CommandResponse
{
    public Guid Id { get; } = Guid.NewGuid();
    public Guid? ParentId { get; set; }
    public Guid RootId { get; set; }
    public string SourceFeatureId { get; set; }
    public GameObject Actor { get; init; }
    public virtual IReadOnlyList<GameObject> Targets => Array.Empty<GameObject>();
    public bool Cancelled { get; private set; }
    public string CancellationSource { get; private set; }
    public string CancellationReason { get; private set; }

    public void Cancel(string source, string reason)
    {
        Cancelled = true;
        CancellationSource = source;
        CancellationReason = reason;
    }
}

public abstract class CommandResponse
{
    public bool Succeeded { get; init; }
    public bool Cancelled { get; init; }
    public string SourceFeatureId { get; init; }
    public List<CommandFact> Facts { get; } = new();
}

public sealed class BasicCommandResponse : CommandResponse
{
}

public sealed class CommandFact
{
    public string Kind { get; init; }
    public GameObject Subject { get; init; }
    public GameObject Object { get; init; }
    public string SourceFeatureId { get; init; }
    public int Amount { get; init; }
}
```

Commands with explicit target fields should override `Targets` or populate a target list so generic UI, logs, and trigger checks do not need type-specific target lookup.

Typed responses keep feature code explicit:

```csharp
public sealed class StrikeCommand : RuleCommand<StrikeCommandResponse>
{
    public GameObject Target;
    public override IReadOnlyList<GameObject> Targets =>
        Target == null ? Array.Empty<GameObject>() : new[] { Target };
    public Strike SourceStrike;
    public StrikeTargetResult TargetingResult;
}

public sealed class StrikeCommandResponse : CommandResponse
{
    public GameObject Target { get; init; }
    public D20Result AttackRoll { get; init; }
    public DegreeOfSuccess Degree { get; init; }
    public uint DamageApplied { get; init; }
    public bool TargetReducedToZero { get; init; }
    public AttackResultContext AttackResult { get; init; }
}
```

The generic `Facts` list gives cross-cutting systems a uniform audit and trigger surface without erasing typed response data. For example, a `StrikeCommandResponse` can expose `TargetReducedToZero` directly and also add a `CommandFact` with kind `ReducedToZero` for generic death-triggered listeners.

## Lifecycle Phases

Commands flow through shared lifecycle phases.

```csharp
public enum CommandPhase
{
    Begin,
    End,
    Cancelled
}

public interface ICommandListener<TCommand, TResponse>
    where TCommand : IRuleCommand<TResponse>
    where TResponse : CommandResponse
{
    int Priority { get; }
    CommandPhase Phase { get; }
    IEnumerator OnCommandPhase(
        TCommand command,
        CommandPhaseContext<TResponse> context,
        CommandFrame frame);
}

public sealed class CommandPhaseContext<TResponse>
    where TResponse : CommandResponse
{
    public CommandPhase Phase { get; set; }
    public TResponse Response { get; set; }
}
```

The command runner owns lifecycle ordering. This pseudo-code uses `CoroutineResult<TResponse>` to fit Unity coroutine flow; a future task-based runner could return `TResponse` directly.

```csharp
public IEnumerator Execute<TCommand, TResponse>(
    TCommand command,
    CoroutineResult<TResponse> response)
    where TCommand : IRuleCommand<TResponse>
    where TResponse : CommandResponse
{
    AssignParentAndRootIds(command);

    CommandPhaseContext<TResponse> context = new() { Phase = CommandPhase.Begin };
    yield return Dispatch(command, context);

    if (!command.Cancelled)
    {
        CoroutineResult<TResponse> handlerResult = new();
        yield return RunCommandHandler<TCommand, TResponse>(command, handlerResult);
        context.Response = handlerResult.Value;
    }

    if (command.Cancelled)
    {
        context.Response ??= BuildCancelledResponse<TResponse>(command);
        context.Phase = CommandPhase.Cancelled;
        yield return Dispatch(command, context);
    }
    else
    {
        context.Phase = CommandPhase.End;
        yield return Dispatch(command, context);
    }

    response.Value = context.Response;
}
```

`Begin` is for preemption and validation. The command handler runs the primary effect between `Begin` and `End`. `End` is for follow-up state updates after the command succeeded. `Cancelled` is for cleanup and logging when the command did not complete.

Nested command execution is explicit through a frame object:

```csharp
public sealed class CommandFrame
{
    public IRuleCommand Current { get; }
    public IRuleCommand Parent { get; }
    public Guid RootId { get; }

    public IEnumerator Execute<TResponse>(
        IRuleCommand<TResponse> nestedCommand,
        CoroutineResult<TResponse> response)
        where TResponse : CommandResponse;
}
```

This lets a listener pause the current command, resolve prompts or reactions, and then allow the parent command to continue or cancel it. Parent id, root id, and source feature id should be assigned immediately in the first pass because they are painful to retrofit and support logs, once-per-use effects, and command causality.

## Commands Versus Queries

Not everything needs to be a command. Commands are for operations with lifecycle, side effects, prompts, cancellation, logs, or a response that later rules may inspect. Pure reads should remain services.

Good command candidates:

- `StrikeCommand : IRuleCommand<StrikeCommandResponse>`
- `MovementCommand : IRuleCommand<MovementResponse>`
- `MovementStepCommand : IRuleCommand<MovementStepResponse>`
- `PromptChoiceCommand : IRuleCommand<PromptChoiceResponse>`
- `SpendReactionCommand : IRuleCommand<BasicCommandResponse>`
- `ApplyEffectCommand : IRuleCommand<BasicCommandResponse>`
- `RemoveEffectCommand : IRuleCommand<BasicCommandResponse>`
- `SkillCheckCommand : IRuleCommand<SkillCheckResponse>`
- `FlatCheckCommand : IRuleCommand<FlatCheckResponse>`
- `ActionCommand : IRuleCommand<ActionCommandResponse>`

Good service/query candidates:

- team relationship checks
- grid distance checks
- line-of-effect checks
- active effect lookup
- current action point lookup
- current reaction availability lookup
- modifier resolution

This distinction keeps rules expressive without turning simple reads into coroutine workflows.

## Runtime Components

`RuleCommandBus` executes commands, dispatches listeners, applies listener ordering, tracks command nesting, and returns typed responses.

`ActiveEffectTracker` lives on creatures. It stores visible effects and implements `IPf2eModifierProvider`, allowing UI and mechanics to read the same state.

```csharp
public sealed class ActiveEffectTracker : MonoBehaviour, IPf2eModifierProvider
{
    public IReadOnlyList<ActiveEffectInstance> Effects => effects;

    public void AddOrRefresh(ActiveEffectInstance effect);
    public void RemoveBySource(Guid sourceInstanceId);

    public IEnumerable<Pf2eModifier> GetModifiers(Pf2eStatistic statistic)
    {
        foreach (ActiveEffectInstance effect in effects)
        {
            foreach (Pf2eModifier modifier in effect.GetModifiers(statistic))
                yield return modifier;
        }
    }
}
```

`ChoiceService` or `PromptChoiceCommand` routes player and AI decisions through one abstraction. Player-controlled actors can open UI prompts; AI-controlled actors can answer immediately from policy.

`ReactionService` owns reaction availability and spending. Features should not directly decrement reaction counters.

`StrikeResolutionService` resolves `StrikeCommand` so normal Strikes, Reactive Strike, and future reaction attacks share one attack/damage path.

`MovementService` resolves `MovementCommand` and per-cell `MovementStepCommand` child commands.

`MovementCostProvider` implementations participate in both path preview and actual movement execution. This is needed early so dynamic costs such as difficult terrain, feat-based spaces, and future aura terrain do not diverge between UI preview and committed movement.

`AuraFeature` implementations listen to command phases and apply or remove visible active effects as creatures enter, leave, or move the aura source.

## Movement Cost Providers

Movement path preview and movement execution must ask the same providers for movement cost. Otherwise a path can look legal in UI and fail during execution.

```csharp
public interface IMovementCostProvider
{
    bool AppliesTo(MovementStepPreview step);
    int AdditionalCost(MovementStepPreview step);
}

public sealed class MovementStepPreview
{
    public GameObject Actor { get; init; }
    public Vector3Int From { get; init; }
    public Vector3Int To { get; init; }
    public GameObject Occupant { get; init; }
    public int BaseCost { get; init; }
}
```

The committed `MovementStepCommand` should include the cost computed from the same provider set.

```csharp
public sealed class MovementStepCommand : RuleCommand<MovementStepResponse>
{
    public Vector3Int From;
    public Vector3Int To;
    public int BaseCost;
    public int TotalCost;
    public GameObject ToOccupant;
}

public sealed class MovementStepResponse : CommandResponse
{
    public Vector3Int From { get; init; }
    public Vector3Int To { get; init; }
    public bool Moved { get; init; }
    public int CostPaid { get; init; }
}
```

The first implementation only needs base terrain plus a test provider, but the hook should be present from the start.

## Listener Ordering

Multiple features can listen to the same command phase. Ordering must be deterministic.

Use:

- explicit numeric `Priority`
- stable tie-breaking by feature id or type name
- tests for interactions that depend on order

Suggested convention:

- lower priorities run earlier
- validation/prevention listeners run before prompts
- prompts/reactions run before the command handler
- post-handler UI/effect refresh listeners run at `End`

## Normal Strike Example

Normal Strike becomes a typed `StrikeCommand`. Existing `EntityAction` wrappers can create the command internally so the current HUD action list does not need to change immediately.

```csharp
public sealed class StrikeCommand : RuleCommand<StrikeCommandResponse>
{
    public GameObject Target;
    public override IReadOnlyList<GameObject> Targets =>
        Target == null ? Array.Empty<GameObject>() : new[] { Target };
    public Strike SourceStrike;
    public StrikeTargetResult TargetingResult;

    public bool AppliesMultipleAttackPenalty = true;
    public bool IncrementsMultipleAttackPenalty = true;
    public bool CostsActionPoint = true;
}

public sealed class StrikeCommandResponse : CommandResponse
{
    public GameObject Target { get; init; }
    public D20Result AttackRoll { get; init; }
    public DegreeOfSuccess Degree { get; init; }
    public uint DamageApplied { get; init; }
    public bool TargetReducedToZero { get; init; }
    public AttackResultContext AttackResult { get; init; }
}
```

Flow:

```text
Player selects Strike
  -> existing EntityAction creates StrikeCommand
  -> Execute(StrikeCommand) returns StrikeCommandResponse
  -> Begin listeners may adjust roll context or cancel
  -> handler resolves attack roll, AC, damage, and AttackResultPipeline
  -> End spends action cost, increments MAP, logs result
```

Pseudo-code:

```csharp
public IEnumerator Resolve(
    StrikeCommand command,
    CoroutineResult<StrikeCommandResponse> response)
{
    CreatureComponent attacker = command.Actor.GetComponent<CreatureComponent>();
    CreatureComponent target = command.Target.GetComponent<CreatureComponent>();

    int mapPenalty = command.AppliesMultipleAttackPenalty
        ? MultipleAttackPenalty.Calculate(command.Actor, command.SourceStrike)
        : 0;

    Pf2eModifierResolution attack = attacker.ResolveAttackRoll(
        command.SourceStrike.AttackModifierOverride,
        BuildStrikeAttackModifiers(mapPenalty, command.TargetingResult.RangePenalty));

    Pf2eModifierResolution ac = target.ResolveArmorClass(
        BuildStrikeAcModifiers(command.TargetingResult.CoverAcBonus));

    D20Result attackRoll = D20.Roll(attack.Total, ac.Total);
    AttackResultContext attackResult = null;
    uint damageApplied = 0;
    bool reducedToZero = false;

    if (attackRoll.degree == DegreeOfSuccess.Success ||
        attackRoll.degree == DegreeOfSuccess.CriticalSuccess)
    {
        attackResult = BuildAttackResultContext(command, attack, ac, attackRoll);
        int hpBefore = target.hp;
        AttackResultPipeline.ProcessHit(attackResult);
        damageApplied = attackResult.FinalAppliedDamage;
        reducedToZero = hpBefore > 0 && target.hp == 0;
    }

    StrikeCommandResponse result = new()
    {
        Succeeded = true,
        SourceFeatureId = command.SourceFeatureId,
        Target = command.Target,
        AttackRoll = attackRoll,
        Degree = attackRoll.degree,
        DamageApplied = damageApplied,
        TargetReducedToZero = reducedToZero,
        AttackResult = attackResult
    };

    if (reducedToZero)
    {
        result.Facts.Add(new CommandFact
        {
            Kind = "ReducedToZero",
            Subject = command.Target,
            Object = command.Actor,
            SourceFeatureId = command.SourceFeatureId
        });
    }

    response.Value = result;
}
```

The current `AttackResultPipeline` remains valuable and should be reused rather than replaced.

## Reactive Strike Example

Reactive Strike should be implemented as a feature listener, not as special engine logic. It listens to command phases that can trigger it.

Movement trigger:

```csharp
public sealed class ReactiveStrikeFeature :
    ICommandListener<MovementStepCommand, MovementStepResponse>,
    ICommandListener<ActionCommand, ActionCommandResponse>
{
    public int Priority => 100;
    public CommandPhase Phase => CommandPhase.Begin;

    public IEnumerator OnCommandPhase(
        MovementStepCommand move,
        CommandPhaseContext<MovementStepResponse> context,
        CommandFrame frame)
    {
        if (!CanReactiveStrike(move.Actor, move.From, move.To))
            yield break;

        CoroutineResult<PromptChoiceResponse> choice = new();
        yield return frame.Execute(new PromptChoiceCommand
        {
            Actor = owner,
            Prompt = "Use Reactive Strike?",
            SourceFeatureId = "reactive-strike",
            Target = move.Actor
        }, choice);
        if (!choice.Value.Accepted)
            yield break;

        CoroutineResult<BasicCommandResponse> spend = new();
        yield return frame.Execute(new SpendReactionCommand
        {
            Actor = owner,
            SourceFeatureId = "reactive-strike"
        }, spend);
        if (!spend.Value.Succeeded)
            yield break;

        CoroutineResult<StrikeCommandResponse> strike = new();
        yield return frame.Execute(new StrikeCommand
        {
            Actor = owner,
            Target = move.Actor,
            AppliesMultipleAttackPenalty = false,
            IncrementsMultipleAttackPenalty = false,
            CostsActionPoint = false,
            SourceFeatureId = "reactive-strike"
        }, strike);
    }
}
```

Manipulate trigger:

```csharp
public IEnumerator OnCommandPhase(
    ActionCommand action,
    CommandPhaseContext<ActionCommandResponse> context,
    CommandFrame frame)
{
    if (!action.HasTrait("manipulate"))
        yield break;
    if (!CanReactiveStrike(action.Actor))
        yield break;

    CoroutineResult<StrikeCommandResponse> strike = new();
    yield return PromptSpendAndStrike(action.Actor, frame, strike);

    if (strike.Value != null &&
        strike.Value.Degree == DegreeOfSuccess.CriticalSuccess)
    {
        action.Cancel("Reactive Strike", "Critical hit disrupted manipulate action.");
    }
}
```

Important rules note: base Reactive Strike disrupts manipulate actions on a critical hit. It does not normally require a flat check to disrupt movement. `FlatCheckCommand` is still useful for future features that do require flat checks.

## Bless Example

Bless is an aura with event-maintained visible effects. Do not wait until roll time to reveal whether a creature is affected. Do not directly mutate `attackBonus`.

When Bless is cast:

```text
Execute(CastSpellCommand: Bless) returns CastSpellResponse
  -> handler creates BlessAuraFeature instance
  -> End recomputes all combatants
  -> affected allies receive visible Bless active effect
```

Aura membership updates on movement:

```csharp
public sealed class BlessAuraFeature :
    ICommandListener<MovementStepCommand, MovementStepResponse>,
    ICommandListener<AuraRadiusChangedCommand, BasicCommandResponse>,
    ICommandListener<EffectEndedCommand, BasicCommandResponse>
{
    public int Priority => 500;
    public CommandPhase Phase => CommandPhase.End;

    public IEnumerator OnCommandPhase(
        MovementStepCommand move,
        CommandPhaseContext<MovementStepResponse> context,
        CommandFrame frame)
    {
        yield return RecomputeFor(move.Actor, frame);

        if (move.Actor == source)
            yield return RecomputeAllCombatants(frame);
    }
}
```

Membership logic:

```csharp
private IEnumerator RecomputeFor(GameObject candidate, CommandFrame frame)
{
    bool affected =
        IsActive &&
        TeamService.IsSelfOrAlly(source, candidate) &&
        GridDistance.IsWithinFeet(source, candidate, radiusFeet);

    if (affected)
    {
        CoroutineResult<BasicCommandResponse> apply = new();
        yield return frame.Execute(new ApplyEffectCommand
        {
            Actor = candidate,
            Effect = BuildBlessEffect(),
            SourceFeatureId = "bless"
        }, apply);
    }
    else
    {
        CoroutineResult<BasicCommandResponse> remove = new();
        yield return frame.Execute(new RemoveEffectCommand
        {
            Actor = candidate,
            SourceInstanceId = blessInstanceId,
            SourceFeatureId = "bless"
        }, remove);
    }
}
```

The effect contributes a status bonus through `ActiveEffectTracker`:

```csharp
public sealed class BlessEffect : ActiveEffectInstance
{
    public override IEnumerable<Pf2eModifier> GetModifiers(Pf2eStatistic statistic)
    {
        if (statistic == Pf2eStatistic.AttackRoll)
        {
            yield return new Pf2eModifier(
                +1,
                Pf2eModifierType.Status,
                "Bless",
                Pf2eStatistic.AttackRoll);
        }
    }
}
```

When a Blessed creature attacks, the existing modifier resolver handles stacking with other status bonuses.

```text
StrikeCommand handler
  -> CreatureComponent.ResolveAttackRoll(...)
  -> ActiveEffectTracker contributes Bless modifier
  -> Pf2eModifierResolver applies stacking rules
  -> StrikeCommandResponse records the final roll and result
```

This gives immediate UI feedback and keeps combat math centralized.

## Tumble Through Example

Tumble Through is an action command that creates a movement command with action-scoped movement rules. The grid should not hardcode the name "Tumble Through".

```csharp
public sealed class TumbleThroughCommand : RuleCommand<TumbleThroughResponse>
{
    public MovementCommand Movement;
    public GameObject TargetEnemy;
    public override IReadOnlyList<GameObject> Targets =>
        TargetEnemy == null ? Array.Empty<GameObject>() : new[] { TargetEnemy };
}

public sealed class TumbleThroughResponse : CommandResponse
{
    public GameObject TargetEnemy { get; init; }
    public DegreeOfSuccess AcrobaticsResult { get; init; }
    public bool PassedThroughEnemySpace { get; init; }
    public MovementResponse Movement { get; init; }
}
```

Flow:

```text
Player selects Tumble Through
  -> Execute(TumbleThroughCommand) returns TumbleThroughResponse
  -> command creates MovementCommand with TumbleThroughMovementRule
  -> movement pathing allows provisional enemy-occupied cell
  -> entering enemy space rolls Acrobatics vs Reflex DC
  -> success allows passage and applies extra movement cost
  -> failure cancels the movement step and movement stops
```

Movement rules are attached to the movement command:

```csharp
public interface IMovementRule
{
    bool CanEnter(MovementStepPreview step);
    int GetAdditionalCost(MovementStepPreview step);
    IEnumerator OnBeforeEnter(MovementStepCommand step, CommandFrame frame);
}
```

Tumble Through supplies its own movement rule:

```csharp
public sealed class TumbleThroughMovementRule : IMovementRule
{
    public bool CheckResolved { get; private set; }
    public DegreeOfSuccess AcrobaticsResult { get; private set; }

    public bool CanEnter(MovementStepPreview step)
    {
        if (step.Occupant == null)
            return true;

        return step.Occupant == targetEnemy;
    }

    public int GetAdditionalCost(MovementStepPreview step)
    {
        return step.Occupant == targetEnemy ? step.BaseCost : 0;
    }

    public IEnumerator OnBeforeEnter(MovementStepCommand step, CommandFrame frame)
    {
        if (step.ToOccupant != targetEnemy || CheckResolved)
            yield break;

        CoroutineResult<SkillCheckResponse> roll = new();
        yield return frame.Execute(new SkillCheckCommand
        {
            Actor = actor,
            SkillName = "Acrobatics",
            DifficultyClass = ReflexDcService.GetReflexDc(targetEnemy),
            SourceFeatureId = "tumble-through"
        }, roll);

        CheckResolved = true;
        AcrobaticsResult = roll.Value.Degree;

        if (roll.Value.Degree < DegreeOfSuccess.Success)
            step.Cancel("Tumble Through", "Failed to move through enemy space.");
    }
}
```

The Tumble Through command response is built from the movement response and the action-scoped movement rule state.

```csharp
CoroutineResult<MovementResponse> movement = new();
yield return frame.Execute(command.Movement, movement);

response.Value = new TumbleThroughResponse
{
    Succeeded = movement.Value.Succeeded,
    TargetEnemy = command.TargetEnemy,
    AcrobaticsResult = tumbleRule.AcrobaticsResult,
    PassedThroughEnemySpace = tumbleRule.CheckResolved &&
        tumbleRule.AcrobaticsResult >= DegreeOfSuccess.Success,
    Movement = movement.Value
};
```

Reactive Strike does not need to know Tumble Through exists. It only sees `MovementStepCommand.Begin` like any other movement.

## First-Pass Scope

Implement this in stages to avoid a broad rewrite.

1. Add `RuleCommand<TResponse>`, `CommandResponse`, `BasicCommandResponse`, `CommandFact`, command frame, and listener registration.
2. Assign parent id, root id, actor, targets, and source feature id for every command.
3. Add typed listener priority and lifecycle phases.
4. Add `ActiveEffectTracker` and make it an `IPf2eModifierProvider`.
5. Add the movement cost provider hook and ensure path preview and movement execution share it.
6. Wrap current Strike resolution in `StrikeCommand` while preserving existing `EntityAction` entry points.
7. Add `MovementStepCommand` around the current step loop in `StateStride`.
8. Implement Bless as the first active-effect aura.
9. Implement Reactive Strike using `MovementStepCommand.Begin` and `ActionCommand.Begin`.
10. Implement Tumble Through using movement command rules.

## Deferred Scope

These are intentionally not first-pass requirements, but the model should leave room for them.

- pending next-command effects, such as spellshape benefits
- persistent area effects and hazardous terrain zones
- teleport and forced movement taxonomy
- full spell result modeling
- fortune and misfortune arbitration
- chained damage and defeat resolution
- generic replacement effects

Pending next-command effects can be documented as a future hook without implementing storage, invalidation, UI, or tests in v1.

```csharp
public interface IPendingCommandEffect
{
    bool AppliesTo(IRuleCommand command);
    IEnumerator Apply(IRuleCommand command, CommandFrame frame);
    void Expire(PendingEffectExpireReason reason);
}
```

## Caveats

Listener priority is not optional. PF2e has many reactions and triggered effects; component order is not a valid rules engine.

Prompting must be coroutine-safe. A command listener can pause a parent command while waiting for UI or AI choice, but the parent command must remain visible in the command frame for cancellation and logs.

Reaction spending should be centralized. Features can request reaction spending, but the reaction service must own availability, one-reaction-per-round behavior, and UI state.

Cancellation should mutate the parent command directly. Prefer `command.Cancel(source, reason)` over a loose `CancelEvent` targeting another event by id.

Effect state should be visible and mechanical. If a player should know they are affected, use `ActiveEffectTracker`. If a value only matters for the immediate roll, pass it as a contextual modifier.

Keep commands typed. A generic `RuleCommand` with arbitrary tags would be harder to search, test, and refactor.

Keep responses typed. Feature logic should inspect `StrikeCommandResponse`, `MovementStepResponse`, and similar explicit response types rather than downcasting a generic result object.

Keep facts generic. Cross-cutting logs and delayed triggers can inspect `CommandFact` without depending on every response type.

## Test Expectations

Prefer deterministic EditMode tests for:

- command lifecycle ordering
- typed command responses
- command fact recording
- parent id and root id propagation
- nested command execution
- cancellation behavior
- listener priority
- active effect add/remove behavior
- movement cost provider parity between preview and execution
- Bless modifier stacking through `Pf2eModifierResolver`
- Reactive Strike reaction spending and MAP exemption
- Tumble Through skill check and movement stop behavior

Use PlayMode tests for:

- HUD action compatibility through existing `EntityAction` wrappers
- visible active effect updates after movement
- reaction prompts during movement
- grid movement behavior for Tumble Through

Run Unity tests with the project Unity version and do not pass `-quit`.
