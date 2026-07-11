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
- Support preemption, prompts, nested commands, typed responses, command traits, and cancellation.
- Keep modifier math centralized through `IPf2eModifierProvider` and `Pf2eModifierResolver`.
- Introduce this incrementally without replacing existing `EntityAction`, HUD, grid, AI, and Strike code in one large rewrite.

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
    IReadOnlyCollection<string> Traits { get; }
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
    public virtual IReadOnlyCollection<string> Traits => Array.Empty<string>();
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
    public string FailureReason { get; init; }
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

Commands with explicit target fields should override `Targets` or populate a target list so generic UI, logs, and trigger checks do not need type-specific target lookup. Commands should also expose PF2e action traits such as `attack`, `move`, `manipulate`, and `concentrate` when those traits are known; many triggers care about traits more than concrete command types.

Typed responses keep feature code explicit:

```csharp
public sealed class StrikeCommand : RuleCommand<StrikeCommandResponse>
{
    public GameObject Target;
    public override IReadOnlyList<GameObject> Targets =>
        Target == null ? Array.Empty<GameObject>() : new[] { Target };
    public StrikeProfile Profile;
    public override IReadOnlyCollection<string> Traits =>
        new[] { "attack" }.Concat(Profile?.Traits ?? Array.Empty<string>()).ToArray();
    public StrikeTargetResult TargetingResult;
}

public sealed class StrikeCommandResponse : CommandResponse
{
    public GameObject Target { get; init; }
    public D20Result AttackRoll { get; init; }
    public DegreeOfSuccess Degree { get; init; }
    public uint DamageApplied { get; init; }
    public bool TargetReducedToZero { get; init; }
    public StrikeResolutionResult Resolution { get; init; }
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

public interface ICommandPhaseListener
{
    int Priority { get; }
    CommandPhase Phase { get; }
    bool AppliesTo(IRuleCommand command);
    IEnumerator OnCommandPhase(
        IRuleCommand command,
        ICommandPhaseContext context,
        CommandFrame frame);
}

public interface ICommandPhaseContext
{
    CommandPhase Phase { get; }
    CommandResponse UntypedResponse { get; }
}

public sealed class CommandPhaseContext<TResponse> : ICommandPhaseContext
    where TResponse : CommandResponse
{
    public CommandPhase Phase { get; set; }
    public TResponse Response { get; set; }
    public CommandResponse UntypedResponse => Response;
}
```

The command runner owns lifecycle ordering. It dispatches exact typed listeners and broader predicate listeners such as trait listeners in one priority-ordered list. This pseudo-code uses `CoroutineResult<TResponse>` to fit Unity coroutine flow; a future task-based runner could return `TResponse` directly.

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

This lets a listener pause the current command, resolve prompts or reactions, and then allow the parent command to continue or cancel it. Parent id, root id, and source feature id should be assigned immediately in the first pass because they are painful to retrofit and support logs, once-per-use effects, and command causality. Trait-wide listeners should use `ICommandPhaseListener`; command-specific listeners should use `ICommandListener<TCommand, TResponse>` when they need typed response data.

## Commands Versus Queries

Not everything needs to be a command. Commands are for operations with lifecycle, side effects, prompts, cancellation, logs, or a response that later rules may inspect. Pure reads should remain services.

Good command candidates:

- `StrikeCommand : IRuleCommand<StrikeCommandResponse>`
- `MovementCommand : IRuleCommand<MovementResponse>`
- `MovementStepCommand : IRuleCommand<MovementStepResponse>`
- `PromptChoiceCommand : IRuleCommand<PromptChoiceResponse>`
- `SpendActionCommand : IRuleCommand<BasicCommandResponse>`
- `SpendReactionCommand : IRuleCommand<BasicCommandResponse>`
- `ApplyEffectCommand : IRuleCommand<BasicCommandResponse>`
- `RemoveEffectCommand : IRuleCommand<BasicCommandResponse>`
- `ApplyConditionCommand : IRuleCommand<BasicCommandResponse>`
- `RemoveConditionCommand : IRuleCommand<BasicCommandResponse>`
- `SkillCheckCommand : IRuleCommand<SkillCheckResponse>`
- `FlatCheckCommand : IRuleCommand<FlatCheckResponse>`
- `ActionCommand : IRuleCommand<ActionCommandResponse>`
- `ReloadWeaponCommand : IRuleCommand<ReloadWeaponResponse>`
- `StartTurnCommand : IRuleCommand<StartTurnResponse>`
- `CombatStartCommand : IRuleCommand<BasicCommandResponse>`
- `CombatEndCommand : IRuleCommand<BasicCommandResponse>`

Good service/query candidates:

- team relationship checks
- grid distance checks
- line-of-effect checks
- active effect lookup
- current action point lookup
- current reaction availability lookup
- action definition lookup
- target/path/area preview
- modifier resolution

This distinction keeps rules expressive without turning simple reads into coroutine workflows.

## Runtime Components

`RuleCommandBus` executes commands, dispatches listeners, applies listener ordering, tracks command nesting, and returns typed responses. It should be scene/encounter-scoped in lifetime even if the first implementation exposes a convenience singleton accessor to match existing project patterns.

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

`ActionDefinition` replaces `EntityAction` as the long-term action-list model. It describes action name, traits, cost, icon or UI metadata, targeting mode, availability, and how to build a command once target/path/area input has been selected.

`ActionSelection` stores the player or AI selection for an action definition: target creature, selected path, area placement, weapon profile, spell rank, or other input needed to build the command. Selection is UI/input data, not rules resolution.

`ChoiceService` or `PromptChoiceCommand` routes player and AI decisions through one abstraction. Player-controlled actors can open UI prompts; AI-controlled actors can answer immediately from policy.

`ActionEconomyService` owns action point spending, reaction spending, and multiple attack penalty state. Features should request spending through commands such as `SpendActionCommand`, `SpendReactionCommand`, and `IncrementMultipleAttackPenaltyCommand`; they should not directly mutate `ActionController.ActionPoints`, `Reacted`, or `StrikePenalty`.

`ReactionService` owns reaction availability and spending. Features should not directly decrement reaction counters.

`RollService` owns d20 and damage randomness for command handlers. The first pass can wrap existing `D20` and `Dice` helpers, but commands should not call `UnityEngine.Random` directly; this keeps tests deterministic and leaves room for fortune, misfortune, rerolls, and roll auditing.

`StrikeResolutionService` resolves `StrikeCommand` by wrapping the existing `StrikeResolutionPipeline`, so normal Strikes, Reactive Strike, and future reaction attacks share one attack/damage path.

`MovementService` resolves `MovementCommand` and per-cell `MovementStepCommand` child commands.

`MovementCostProvider` implementations participate in both path preview and actual movement execution. This is needed early so dynamic costs such as difficult terrain, feat-based spaces, and future aura terrain do not diverge between UI preview and committed movement.

`AuraFeature` implementations listen to command phases and apply or remove visible active effects as creatures enter, leave, or move the aura source.

`Conditions` can remain the condition tracker for named PF2e conditions. `ActiveEffectTracker` should complement it for visible non-condition effects and effect-granted modifiers. Commands such as `ApplyConditionCommand`, `RemoveConditionCommand`, `ApplyEffectCommand`, and `RemoveEffectCommand` keep both UI-visible state and prepared-rule predicate state in sync.

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
    public override IReadOnlyCollection<string> Traits => new[] { "move" };
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
    public StrikeProfile Profile;
    public override IReadOnlyCollection<string> Traits =>
        new[] { "attack" }.Concat(Profile?.Traits ?? Array.Empty<string>()).ToArray();
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
    public StrikeResolutionResult Resolution { get; init; }
}
```

Flow:

```text
Player selects Strike
  -> existing EntityAction creates StrikeCommand
  -> Execute(StrikeCommand) returns StrikeCommandResponse
  -> Begin listeners may adjust roll context or cancel
  -> handler resolves the selected Strike through StrikeResolutionPipeline
  -> End spends action cost, increments MAP, logs result
```

Pseudo-code:

```csharp
public IEnumerator Resolve(
    StrikeCommand command,
    CoroutineResult<StrikeCommandResponse> response)
{
    CreatureComponent target = command.Target.GetComponent<CreatureComponent>();
    int hpBefore = target.hp;

    StrikeResolutionResult resolution = StrikeResolutionPipeline.Resolve(
        new StrikeResolutionRequest
        {
            Attacker = command.Actor,
            Target = command.Target,
            Profile = command.Profile,
            TargetingResult = command.TargetingResult
        });

    StrikeResolutionContext context = resolution.Context;
    bool reducedToZero = hpBefore > 0 && target.hp == 0;

    StrikeCommandResponse result = new()
    {
        Succeeded = true,
        SourceFeatureId = command.SourceFeatureId,
        Target = command.Target,
        AttackRoll = context.D20Result,
        Degree = context.Degree,
        DamageApplied = resolution.FinalAppliedDamage,
        TargetReducedToZero = reducedToZero,
        Resolution = resolution
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

The current `StrikeResolutionPipeline` remains valuable and should be reused rather than replaced. The command layer should own when a Strike occurs and how its result is exposed; the Strike pipeline should continue to own Strike-specific math and damage phases.

## Reactive Strike Example

Reactive Strike should be implemented as a feature listener, not as special engine logic. It listens to command phases that can trigger it.

Movement trigger:

```csharp
public sealed class ReactiveStrikeFeature :
    ICommandListener<MovementStepCommand, MovementStepResponse>,
    ICommandPhaseListener
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
            Profile = ChooseReactiveStrikeProfile(owner, move.Actor),
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
public bool AppliesTo(IRuleCommand command)
{
    return command.Traits.Contains("manipulate") &&
        CanReactiveStrike(command.Actor);
}

public IEnumerator OnCommandPhase(
    IRuleCommand action,
    ICommandPhaseContext context,
    CommandFrame frame)
{
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
    public override IReadOnlyCollection<string> Traits => new[] { "move" };
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

## Incremental Migration Plan

The migration should be vertical by feature, not horizontal by subsystem. The existing `EntityAction`, HUD, AI, and grid selection code can continue to call into actions while individual actions move their resolution into commands.

Current responsibilities in code:

- `EntityAction` and `MultiFrameEntityAction` provide action-list entries and coroutine invocation.
- `ActionController` owns action points, turn state, reaction state, action lists, movement lists, reaction lists, and multiple attack penalty.
- `GridFSM` and its states collect player input, preview cells, and currently execute some gameplay such as Stride movement.
- `StrikeResolutionPipeline` already separates much of Strike math from `Unarmed` and `StrikeWeapon` wrappers.
- `RageRule` already separates pure Rage eligibility from Unity mutation, but `UnityRuleEffectApplier` applies those effects directly.
- `Conditions` already tracks named conditions and contributes condition modifiers through `IPf2eModifierProvider`.

The compatibility bridge is a command-backed action wrapper. It lets the HUD and AI keep using `EntityAction` while the feature's behavior moves to commands.

```csharp
public abstract class CommandBackedEntityAction<TResponse> : MultiFrameEntityAction
    where TResponse : CommandResponse
{
    protected CommandBackedEntityAction(uint cost) : base(cost)
    {
    }

    protected override IEnumerator MFInvoke(GameObject actor)
    {
        ActionController controller = actor.GetComponent<ActionController>();
        CoroutineResult<IRuleCommand<TResponse>> command = new();
        yield return BuildCommand(actor, command);
        if (command.Value == null)
        {
            if (controller != null)
                controller.IsTakingAction = false;
            yield break;
        }

        CoroutineResult<TResponse> response = new();
        yield return RuleCommandBus.Instance.Execute(command.Value, response);
        if (controller != null)
            controller.IsTakingAction = false;
    }

    protected abstract IEnumerator BuildCommand(
        GameObject actor,
        CoroutineResult<IRuleCommand<TResponse>> command);
}
```

This bridge is temporary. It is valuable because it keeps each migration small:

```text
Existing HUD/AI selects EntityAction
  -> command-backed wrapper gathers any target/path/area input
  -> wrapper submits typed command
  -> command response drives logs, UI cleanup, and follow-up rules
```

Migration order:

1. **Reload**
   Current implementation is isolated in `ReloadWeaponAction.Invoke`. Migrate it first because it has one actor, one item, one resource cost, one log line, and no targeting state. Add `ReloadWeaponCommand` and `ReloadWeaponResponse`; the wrapper only submits the command. Remove direct `CreatureComponent.ReloadWeapon`, `PayCost`, and `IsTakingAction` mutation from the action wrapper after command tests pass.

2. **Rage**
   `RageRule` is already a good pure-rule seam. Add `RageCommand` and `EndRageCommand`. The command handler calls `RageRule`, then converts each `RuleEffect` into nested commands such as `SpendActionCommand`, `GainTempHpCommand`, `RemoveTempHpCommand`, and `ApplyEffectCommand`. After this migration, `UnityRuleEffectApplier` should no longer be used by Rage.

3. **Strike**
   Wrap the existing `StrikeResolutionPipeline` in `StrikeCommand` rather than rewriting the attack pipeline. `Unarmed` and `StrikeWeapon` still gather targets through the current grid state at first, then submit `StrikeCommand`. The command handler owns attack roll response data, command facts, action cost, MAP increment, ammo consumption, logs, and miss/hit facts. After both wrappers migrate, no production path should call `StrikeResolutionPipeline.Resolve` directly except the command handler and tests focused on the pure pipeline.

4. **Stride and Movement**
   Keep `StateStride` for path selection and preview at first. Move path execution into `MovementCommand`, and have `MovementCommand` emit one `MovementStepCommand` per step. Bridge existing `Tile.OnExitTile`, `Tile.OnEnterTile`, and `OnStepEnd` behavior from the movement command handler until their rule-relevant listeners move to command listeners. Once movement commands are stable, `StateStride.ExecutePlayerMovement` should disappear; the state should only collect a path and submit a command.

5. **Conditions and Turn Start**
   `DefinedConditions.Slowed` currently installs a listener on `ResetActionPointsEvent`. Migrate action restoration to `StartTurnCommand` or `ResetActionPointsCommand`, then implement Slowed as a listener or provider for that command. This avoids anonymous UnityEvent callbacks becoming hidden rule state. Similar migrations should cover reaction suppression currently attached through `GetReactionsEvent`.

6. **Combat Start and End Rules**
   `Pf2eRulesEngine.ApplyCombatStartRules` and `EndEncounter` currently call feature behavior directly, including Quick-Tempered Rage. Replace these with `CombatStartCommand` and `CombatEndCommand`. Quick-Tempered becomes a feature listener that emits a zero-cost `RageCommand`; Rage cleanup becomes an encounter-end listener.

7. **Static Rule Events**
   Static events such as `OnAttackMiss`, `OnDamageDealt`, `OnDeath`, and `OnStrikePreparedEvent` can remain temporarily for audio and UI compatibility. Rule behavior should stop subscribing to them. Command handlers should emit facts such as `AttackMissed`, `DamageDealt`, `CreatureReducedToZero`, and an adapter can translate those facts into legacy static events until audio/UI are migrated.

8. **Action Roster and AI**
   Replace `ActionController.Actions`, `Movements`, and `Reactions` with action providers that return `ActionDefinition` objects. AI should evaluate definitions and build `ActionSelection` values rather than type-checking `Unarmed` or `StrikeWeapon`. This can happen after the migrated command-backed wrappers prove the behavior path.

9. **Remove `EntityAction`**
   Once the HUD and AI consume `ActionDefinition` directly, delete `EntityAction`, `MultiFrameEntityAction`, action-list UnityEvents, and command-backed wrappers. At that point command submission is the only action execution path.

A feature is fully migrated only when:

- it has a typed command and typed response;
- all direct side effects are inside the command handler or nested commands;
- the old `EntityAction` wrapper, if still present, only gathers selection and submits the command;
- direct action point, reaction, MAP, condition, temp HP, ammo, and log mutations are removed from the wrapper;
- tests cover the old behavior through the command path.

## Full Migration Target

The long-term target removes `EntityAction` as a rules/execution abstraction. It does not require deleting the grid state machine.

Replace `EntityAction` with action definitions:

```csharp
public interface IActionDefinition
{
    string Name { get; }
    uint ActionCost { get; }
    IReadOnlyCollection<string> Traits { get; }
    ITargetingMode Targeting { get; }
    Type ResponseType { get; }

    bool IsAvailable(GameObject actor);
}

public interface IActionDefinition<TResponse> : IActionDefinition
    where TResponse : CommandResponse
{
    IRuleCommand<TResponse> BuildCommand(
        GameObject actor,
        ActionSelection selection);
}
```

`ActionDefinition` is stable data and command construction. `ActionSelection` is player or AI input. `RuleCommand` is execution. Keeping those separate prevents UI targeting code, AI planning, and rules resolution from collapsing back into one action class.

The grid FSM should remain only if it stays an input and preview system. It still provides value for modal states such as:

- idle
- selecting a Strike target
- selecting a movement path
- selecting an area placement
- selecting an interact target

It should not own rule resolution. Long-term Stride should look like this:

```text
StrideActionDefinition selected
  -> Grid selection state previews legal paths
  -> player or AI confirms ActionSelection
  -> MovementCommand submitted
  -> MovementCommand emits MovementStepCommand per step
  -> Reactive Strike, auras, terrain, and cancellation resolve through command listeners
  -> MovementResponse returned
  -> UI exits action mode
```

Keep these existing services:

- `StrikeTargeting`
- `AreaTargeting`
- `FlankingRule`
- `Pf2eModifierResolver`
- `StrikeResolutionPipeline` or its successor service
- `RageRule`-style pure rule evaluators where they keep Unity mutation out of rule decisions

Remove or replace these long-term execution paths:

- `EntityAction.Invoke` and `MultiFrameEntityAction.MFInvoke` as action execution;
- direct `ActionController.ActionPoints`, `Reacted`, and `StrikePenalty` mutation from feature code;
- rule-relevant `StaticUnityEvent` and `UnityEvent` hooks;
- `UnityRuleEffectApplier` as a generic mutation switch;
- grid states that directly move tokens, spend resources, or apply damage.

## Design Review Findings

This proposal is compatible with the current codebase, but only if the following constraints are treated as core design decisions rather than optional cleanup.

**Command Traits Are Required**

PF2e triggers often care about traits, not class names. Reactive Strike cares about `manipulate` and movement; many future reactions care about `concentrate`, `move`, `attack`, `spellshape`, `flourish`, and similar traits. `IRuleCommand.Traits` and `ICommandPhaseListener` are therefore part of the core command contract. Without them, feature listeners would either type-check too many concrete commands or recreate a parallel trigger system later.

**Typed Responses Need Failure State**

Every command returns a response, including blocked or cancelled commands. `CommandResponse.FailureReason` keeps failure reporting and tests out of ad hoc logs. A cancelled command should return a typed response with `Cancelled = true`, `Succeeded = false`, and enough command-specific fields for callers to make safe decisions.

**Coarse Command Phases Are Enough Only With Domain Pipelines**

The command lifecycle should stay coarse: `Begin`, handler, `End`, `Cancelled`. Strike already needs finer phases for damage dice, critical traits, resistance, weakness, and logging; those belong inside `StrikeResolutionPipeline`, not in the global command phase enum. The same pattern should apply later to spell resolution, affliction saves, persistent damage, and recovery checks. The command system coordinates features; domain pipelines own domain-specific math.

**Action Economy Must Be Centralized Early**

Current code spends actions through `PayCost`, mutates MAP through `ActionController.StrikePenalty`, and suppresses reactions by editing lists. That will not scale. The first implementation should include `SpendActionCommand`, `SpendReactionCommand`, and `IncrementMultipleAttackPenaltyCommand` even if their handlers are small wrappers around `ActionController`. This lets migrated features stop mutating resource fields directly.

**Rolls Need a Testable Seam**

Existing code uses `UnityEngine.Random` through `D20`, `Dice`, and critical trait effects. That is acceptable inside legacy services, but command handlers should call a `RollService` abstraction. This is important for deterministic tests and for future PF2e roll mechanics such as fortune, misfortune, rerolls, secret checks, flat checks, and roll replacement effects. This does not require making every damage die a command in the first pass; it does require avoiding new direct random calls in command handlers.

**Visible State and Predicate State Must Stay Synchronized**

The project currently has `Conditions`, `PreparedCharacter.ActiveEffects`, `Pf2eModifierCollection`, and proposed `ActiveEffectTracker`. These cannot become four unrelated sources of truth. Applying an effect or condition through commands should update the player-visible tracker, modifier providers, and prepared rule predicate state together. If an effect only modifies one immediate roll, it should remain contextual modifier data instead of visible active state.

**Grid FSM Should Not Execute Rules Long Term**

`StateStride` currently previews and executes movement. The preview role is still useful; the execution role should move to `MovementCommand`. This distinction is essential for Reactive Strike, aura updates, difficult terrain, Tumble Through, forced movement, and future movement-triggered features. Path preview and committed movement must share movement cost providers.

**Static Events Should Become Compatibility Adapters**

Static events are useful for audio and some UI integration, but rule behavior should not depend on them. They have global lifetime, implicit ordering, and weak command provenance. Command facts and typed responses should become the rule surface. Temporary adapters can translate command facts to `OnDamageDealt`, `OnAttackMiss`, `OnDeath`, `OnActionComplete`, and similar legacy events until callers migrate.

**Nested Commands Need Reentrancy Guards**

Nested command execution is powerful enough for reactions and prompts, but it can also create loops. Listeners should be able to inspect `CommandFrame` ancestry, `RootId`, and `SourceFeatureId` to avoid triggering from their own nested commands unless explicitly allowed. This is especially important for reactions that emit Strikes, effects that apply other effects, and future replacement effects.

**Data-Driven Rule Elements Should Compile Into Features, Not Special Cases**

`Pf2eRulesEngine` and `PreparedCharacter` already consume imported rule elements. Long term, those prepared rule elements should produce modifier providers, action definitions, command listeners, or active effects. The command design should not require central `DefinedAbilities` or `Pf2eRulesEngine` switch logic for every feat. The bridge can be incremental, but the destination is feature registration, not a larger static rules facade.

**The Core Design Is Solid If These Boundaries Hold**

The command approach should hold up for substantially more PF2e content if commands own side effects, services own pure queries/math, UI owns selection, action definitions own action availability, and feature classes own their own listeners. The main risk is not missing a future command type; new command types are cheap. The main risk is allowing legacy direct mutation paths to remain after a feature is considered migrated.

## First-Pass Scope

Implement this in stages to avoid a broad rewrite.

1. Add `RuleCommand<TResponse>`, `CommandResponse`, `BasicCommandResponse`, `CommandFact`, command traits, failure reasons, command frame, and listener registration.
2. Assign parent id, root id, actor, targets, traits, and source feature id for every command.
3. Add typed listener priority and lifecycle phases.
4. Add small action-economy commands for action spending, reaction spending, and MAP increment.
5. Add a `RollService` seam used by new command handlers.
6. Add `CommandBackedEntityAction` so current HUD and AI paths can submit commands one feature at a time.
7. Migrate Reload and Rage as the first low-risk command-backed features.
8. Add `ActiveEffectTracker`, condition/effect commands, and prepared-state synchronization for visible effects.
9. Wrap `StrikeResolutionPipeline` in `StrikeCommand` while preserving existing target selection.
10. Add the movement cost provider hook and ensure path preview and movement execution share it.
11. Move Stride execution into `MovementCommand` and emit `MovementStepCommand` around each step.
12. Implement Bless as the first active-effect aura.
13. Implement Reactive Strike using `MovementStepCommand.Begin` and trait-aware `ICommandPhaseListener` handling for manipulate commands.
14. Implement Tumble Through using movement command rules.

## Deferred Scope

These are intentionally not first-pass requirements, but the model should leave room for them.

- full replacement of `EntityAction` with `ActionDefinition` after wrappers migrate
- replacing rule-relevant static events with command fact adapters
- pending next-command effects, such as spellshape benefits
- persistent area effects and hazardous terrain zones
- teleport and forced movement taxonomy
- full spell result modeling
- generalized data-driven rule-element registration into action definitions, listeners, effects, and modifier providers
- fortune and misfortune arbitration
- chained damage and defeat resolution
- generic replacement effects
- renaming or replacing the Grid FSM after it is reduced to input/preview only

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
- typed blocked/cancelled responses with failure reasons
- command fact recording
- parent id and root id propagation
- command trait propagation and trait-based listeners
- nested command execution
- cancellation behavior
- listener priority
- command-backed `EntityAction` compatibility for each migrated action
- action economy commands for AP, reactions, and MAP
- deterministic roll service usage in command handlers
- active effect add/remove behavior
- movement cost provider parity between preview and execution
- Bless modifier stacking through `Pf2eModifierResolver`
- Reload and Rage command parity with legacy behavior
- Strike command parity with `StrikeResolutionPipeline` behavior
- Reactive Strike reaction spending and MAP exemption
- Tumble Through skill check and movement stop behavior

Use PlayMode tests for:

- HUD action compatibility through existing `EntityAction` wrappers
- AI action compatibility through command-backed wrappers
- visible active effect updates after movement
- reaction prompts during movement
- grid movement behavior for Tumble Through

Run Unity tests with the project Unity version and do not pass `-quit`.
