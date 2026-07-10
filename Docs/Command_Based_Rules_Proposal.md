# Command-Based PF2e Rules Proposal

This proposal describes a command-based rules architecture for future PF2e actions, reactions, spells, auras, and feat behavior. The goal is to prevent core gameplay classes from accumulating feature-specific `if` statements while still giving players transparent UI state such as active effects, available reactions, and logged rule causes.

The key idea is that gameplay operations are typed commands with lifecycle phases. Features register listeners for the specific command types and phases they care about. A feature such as Reactive Strike listens to movement and manipulate commands at `Begin`; a feature such as Bless listens to movement commands at `End`; a feature such as Tumble Through creates a movement command with action-scoped movement rules.

Rules references:

- Strike: https://2e.aonprd.com/Actions.aspx?ID=2306
- Reactive Strike: https://2e.aonprd.com/Actions.aspx?ID=2256
- Bless: https://2e.aonprd.com/Spells.aspx?ID=1451
- Tumble Through: https://2e.aonprd.com/Actions.aspx?ID=2370&Redirected=1

## Design Goals

- Keep PF2e feature behavior self-contained in feature classes.
- Let the engine expose generic command lifecycles instead of feature-specific hooks.
- Preserve immediate UI transparency for active effects such as Bless.
- Support preemption, prompts, nested commands, and cancellation.
- Keep modifier math centralized through `IPf2eModifierProvider` and `Pf2eModifierResolver`.
- Introduce this incrementally without replacing existing `EntityAction`, HUD, grid, and Strike code in one large rewrite.

## Core Model

Use typed commands, not one generic stringly-typed event payload. Commands are mutable contexts that flow through a shared lifecycle.

```csharp
public interface IRuleCommand
{
    Guid Id { get; }
    GameObject Actor { get; }
    bool Cancelled { get; }
    CommandResult Result { get; }

    void Cancel(string source, string reason);
}

public enum CommandPhase
{
    Begin,
    Commit,
    End,
    Cancelled
}

public interface ICommandListener<TCommand> where TCommand : IRuleCommand
{
    int Priority { get; }
    CommandPhase Phase { get; }
    IEnumerator OnCommandPhase(TCommand command, CommandFrame frame);
}
```

The command runner owns lifecycle ordering:

```csharp
public IEnumerator Execute<TCommand>(TCommand command)
    where TCommand : IRuleCommand
{
    yield return Dispatch(command, CommandPhase.Begin);

    if (!command.Cancelled)
        yield return RunCommandHandler(command);

    if (command.Cancelled)
        yield return Dispatch(command, CommandPhase.Cancelled);
    else
        yield return Dispatch(command, CommandPhase.End);
}
```

`Begin` is for preemption and validation. `Commit` is where the command's primary effect happens. `End` is for follow-up state updates after the command succeeded. `Cancelled` is for cleanup and logging when the command did not complete.

Nested command execution is explicit through a frame object:

```csharp
public sealed class CommandFrame
{
    public IRuleCommand Parent { get; }

    public IEnumerator Execute(IRuleCommand nestedCommand);
}
```

This lets a listener pause the current command, resolve prompts or reactions, and then allow the parent command to continue or cancel it.

## Commands Versus Queries

Not everything needs to be a command. Commands are for operations with lifecycle, side effects, prompts, cancellation, or logs. Pure reads should remain services.

Good command candidates:

- `StrikeCommand`
- `MovementCommand`
- `MovementStepCommand`
- `PromptChoiceCommand`
- `SpendReactionCommand`
- `ApplyEffectCommand`
- `RemoveEffectCommand`
- `SkillCheckCommand`
- `FlatCheckCommand`
- `ActionCommand`

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

`RuleCommandBus` executes commands, dispatches listeners, applies listener ordering, and tracks command nesting.

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

`MovementService` resolves `MovementCommand` and emits per-cell `MovementStepCommand` child commands.

`AuraFeature` implementations listen to command phases and apply or remove visible active effects as creatures enter, leave, or move the aura source.

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
- post-commit UI/effect refresh listeners run at `End`

## Normal Strike Example

Normal Strike becomes a typed `StrikeCommand`. Existing `EntityAction` wrappers can create the command internally so the current HUD action list does not need to change immediately.

```csharp
public sealed class StrikeCommand : RuleCommand
{
    public GameObject Target;
    public Strike SourceStrike;
    public StrikeTargetResult TargetingResult;

    public bool AppliesMultipleAttackPenalty = true;
    public bool IncrementsMultipleAttackPenalty = true;
    public bool CostsActionPoint = true;

    public D20Result AttackRoll;
    public DegreeOfSuccess Degree;
    public AttackResultContext AttackResult;
}
```

Flow:

```text
Player selects Strike
  -> existing EntityAction creates StrikeCommand
  -> Execute(StrikeCommand)
  -> Begin listeners may adjust roll context or cancel
  -> Commit resolves attack roll, AC, damage, and AttackResultPipeline
  -> End spends action cost, increments MAP, logs result
```

Pseudo-code:

```csharp
public IEnumerator Commit(StrikeCommand command)
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

    command.AttackRoll = D20.Roll(attack.Total, ac.Total);
    command.Degree = command.AttackRoll.degree;

    if (command.Degree == DegreeOfSuccess.Success ||
        command.Degree == DegreeOfSuccess.CriticalSuccess)
    {
        command.AttackResult = BuildAttackResultContext(command, attack, ac);
        AttackResultPipeline.ProcessHit(command.AttackResult);
    }
}
```

The current `AttackResultPipeline` remains valuable and should be reused rather than replaced.

## Reactive Strike Example

Reactive Strike should be implemented as a feature listener, not as special engine logic. It listens to command phases that can trigger it.

Movement trigger:

```csharp
public sealed class ReactiveStrikeFeature :
    ICommandListener<MovementStepCommand>,
    ICommandListener<ActionCommand>
{
    public int Priority => 100;
    public CommandPhase Phase => CommandPhase.Begin;

    public IEnumerator OnCommandPhase(MovementStepCommand move, CommandFrame frame)
    {
        if (!CanReactiveStrike(move.Actor, move.From, move.To))
            yield break;

        PromptChoiceCommand choice = new PromptChoiceCommand
        {
            Actor = owner,
            Prompt = "Use Reactive Strike?",
            Source = "Reactive Strike",
            Target = move.Actor
        };
        yield return frame.Execute(choice);
        if (!choice.Accepted)
            yield break;

        SpendReactionCommand spend = new SpendReactionCommand
        {
            Actor = owner,
            Source = "Reactive Strike"
        };
        yield return frame.Execute(spend);
        if (!spend.Result.Success)
            yield break;

        StrikeCommand strike = new StrikeCommand
        {
            Actor = owner,
            Target = move.Actor,
            AppliesMultipleAttackPenalty = false,
            IncrementsMultipleAttackPenalty = false,
            CostsActionPoint = false
        };
        yield return frame.Execute(strike);
    }
}
```

Manipulate trigger:

```csharp
public IEnumerator OnCommandPhase(ActionCommand action, CommandFrame frame)
{
    if (!action.HasTrait("manipulate"))
        yield break;
    if (!CanReactiveStrike(action.Actor))
        yield break;

    StrikeCommand strike = yield return PromptSpendAndStrike(action.Actor, frame);

    if (strike != null &&
        strike.Degree == DegreeOfSuccess.CriticalSuccess)
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
Execute(CastSpellCommand: Bless)
  -> Commit creates BlessAuraFeature instance
  -> End recomputes all combatants
  -> affected allies receive visible Bless active effect
```

Aura membership updates on movement:

```csharp
public sealed class BlessAuraFeature :
    ICommandListener<MovementStepCommand>,
    ICommandListener<AuraRadiusChangedCommand>,
    ICommandListener<EffectEndedCommand>
{
    public int Priority => 500;
    public CommandPhase Phase => CommandPhase.End;

    public IEnumerator OnCommandPhase(MovementStepCommand move, CommandFrame frame)
    {
        RecomputeFor(move.Actor, frame);

        if (move.Actor == source)
            RecomputeAllCombatants(frame);

        yield break;
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
        yield return frame.Execute(new ApplyEffectCommand
        {
            Actor = candidate,
            Effect = BuildBlessEffect()
        });
    }
    else
    {
        yield return frame.Execute(new RemoveEffectCommand
        {
            Actor = candidate,
            SourceInstanceId = blessInstanceId
        });
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
StrikeCommand.Commit
  -> CreatureComponent.ResolveAttackRoll(...)
  -> ActiveEffectTracker contributes Bless modifier
  -> Pf2eModifierResolver applies stacking rules
```

This gives immediate UI feedback and keeps combat math centralized.

## Tumble Through Example

Tumble Through is an action command that creates a movement command with action-scoped movement rules. The grid should not hardcode the name "Tumble Through".

```csharp
public sealed class TumbleThroughCommand : RuleCommand
{
    public MovementCommand Movement;
    public GameObject TargetEnemy;
    public bool CheckResolved;
    public DegreeOfSuccess AcrobaticsResult;
}
```

Flow:

```text
Player selects Tumble Through
  -> Execute(TumbleThroughCommand)
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
    bool CanEnter(MovementStepCommand step);
    int GetAdditionalCost(MovementStepCommand step);
    IEnumerator OnBeforeEnter(MovementStepCommand step, CommandFrame frame);
}
```

Tumble Through supplies its own movement rule:

```csharp
public sealed class TumbleThroughMovementRule : IMovementRule
{
    public bool CanEnter(MovementStepCommand step)
    {
        if (step.ToOccupant == null)
            return true;

        return step.ToOccupant == targetEnemy;
    }

    public int GetAdditionalCost(MovementStepCommand step)
    {
        return step.ToOccupant == targetEnemy ? step.BaseCost : 0;
    }

    public IEnumerator OnBeforeEnter(MovementStepCommand step, CommandFrame frame)
    {
        if (step.ToOccupant != targetEnemy || checkResolved)
            yield break;

        SkillCheckCommand roll = new SkillCheckCommand
        {
            Actor = actor,
            SkillName = "Acrobatics",
            DifficultyClass = ReflexDcService.GetReflexDc(targetEnemy),
            Source = "Tumble Through"
        };
        yield return frame.Execute(roll);

        checkResolved = true;
        acrobaticsResult = roll.Degree;

        if (roll.Degree < DegreeOfSuccess.Success)
            step.Cancel("Tumble Through", "Failed to move through enemy space.");
    }
}
```

Reactive Strike does not need to know Tumble Through exists. It only sees `MovementStepCommand.Begin` like any other movement.

## Suggested Vertical Slice

Implement this in stages to avoid a broad rewrite.

1. Add the command runner, typed command base, command frame, and listener registration.
2. Add `ActiveEffectTracker` and make it an `IPf2eModifierProvider`.
3. Wrap current Strike resolution in `StrikeCommand` while preserving existing `EntityAction` entry points.
4. Add `MovementStepCommand` around the current step loop in `StateStride`.
5. Implement Bless as the first active-effect aura.
6. Implement Reactive Strike using `MovementStepCommand.Begin` and `ActionCommand.Begin`.
7. Implement Tumble Through using movement command rules.

## Caveats

Listener priority is not optional. PF2e has many reactions and triggered effects; component order is not a valid rules engine.

Prompting must be coroutine-safe. A command listener can pause a parent command while waiting for UI or AI choice, but the parent command must remain visible in the command frame for cancellation and logs.

Reaction spending should be centralized. Features can request reaction spending, but the reaction service must own availability, one-reaction-per-round behavior, and UI state.

Cancellation should mutate the parent command directly. Prefer `command.Cancel(source, reason)` over a loose `CancelEvent` targeting another event by id.

Effect state should be visible and mechanical. If a player should know they are affected, use `ActiveEffectTracker`. If a value only matters for the immediate roll, pass it as a contextual modifier.

Keep commands typed. A generic `RuleCommand` with arbitrary tags would be harder to search, test, and refactor.

## Test Expectations

Prefer deterministic EditMode tests for:

- command lifecycle ordering
- nested command execution
- cancellation behavior
- listener priority
- active effect add/remove behavior
- Bless modifier stacking through `Pf2eModifierResolver`
- Reactive Strike reaction spending and MAP exemption
- Tumble Through skill check and movement stop behavior

Use PlayMode tests for:

- HUD action compatibility through existing `EntityAction` wrappers
- visible active effect updates after movement
- reaction prompts during movement
- grid movement behavior for Tumble Through

Run Unity tests with the project Unity version and do not pass `-quit`.
