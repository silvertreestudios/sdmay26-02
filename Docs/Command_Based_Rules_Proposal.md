# Command-Based PF2e Rules Proposal

This proposal describes a command-based rules architecture for future PF2e actions, reactions, spells, auras, feats, conditions, and item effects. The purpose is to keep PF2e feature behavior self-contained while preventing central combat, grid, and UI classes from accumulating feature-specific branches.

The proposed direction is stricter than an event bus. Gameplay operations are immutable command data, commands are resolved by stateless rule handlers, handlers emit immutable effect data, and the rules engine applies those effects through one controlled state transition path. The model is intentionally close to Redux-style state updates: requests are data, side effects are data, state changes in one engine-owned place, and the command/effect/fact stream is observable.

Rules references used as examples:

- Strike: https://2e.aonprd.com/Actions.aspx?ID=2306
- Reactive Strike: https://2e.aonprd.com/Actions.aspx?ID=2256
- Bless: https://2e.aonprd.com/Spells.aspx?ID=1451
- Tumble Through: https://2e.aonprd.com/Actions.aspx?ID=2370&Redirected=1
- Cranial Detonation: https://2e.aonprd.com/Feats.aspx?ID=8347

## Design Goals

- Keep bespoke PF2e feature behavior local to feature rule definitions.
- Avoid large central files of feature-specific `if` statements.
- Make rule resolution observable for UI, combat logs, debugging, tests, and future replay.
- Preserve immediate player transparency for visible effects such as Bless.
- Support preemption, prompts, reactions, nested commands, typed responses, and cancellation.
- Keep commands, responses, effects, and facts independent of Unity classes.
- Introduce the architecture incrementally without replacing the whole combat stack in one refactor.

## Core Design Constraints

These constraints are part of the design, not optional cleanup.

### Commands And Responses Are Data Only

A command is an immutable request. A response is an immutable result. Neither contains business logic, Unity references, mutable collections, or methods that resolve rules.

```csharp
public interface IRuleCommand
{
}

public interface IRuleCommand<TResponse> : IRuleCommand
    where TResponse : CommandResponse
{
}

public abstract record CommandResponse(
    CommandId CommandId,
    CommandOutcome Outcome,
    FailureReason? FailureReason,
    ImmutableArray<RuleEffect> AppliedEffects,
    ImmutableArray<RuleFact> Facts);

public sealed record BasicCommandResponse(
    CommandId CommandId,
    CommandOutcome Outcome,
    FailureReason? FailureReason,
    ImmutableArray<RuleEffect> AppliedEffects,
    ImmutableArray<RuleFact> Facts)
    : CommandResponse(CommandId, Outcome, FailureReason, AppliedEffects, Facts);
```

`IRuleCommand` is intentionally only a marker and typed-response relationship. Registration, validation, and execution belong to rule definitions and handlers, not command data objects.

`CommandResponse` is an abstract record because responses are immutable result data with a shared envelope. It is not just a capability contract.

Why this matters: if commands execute themselves, every command becomes a small service object and replay becomes harder. Keeping commands as data lets the engine log, serialize, replay, inspect, and test requests without hidden behavior.

### Commands And Responses Carry Provenance

Provenance should be attached through command frames and effect/fact metadata rather than through mutable fields on the command payload.

```csharp
public interface ICommandFrame
{
    CommandId Id { get; }
    CommandId? ParentId { get; }
    CommandId RootId { get; }
    RuleBindingId? SourceBinding { get; }
    RuleSourceId? SourceRule { get; }
    CreatureId? Actor { get; }
    Type CommandType { get; }
    ImmutableArray<Trait> Traits { get; }
}

public sealed record CommandFrame<TCommand>(
    CommandId Id,
    CommandId? ParentId,
    CommandId RootId,
    RuleBindingId? SourceBinding,
    RuleSourceId? SourceRule,
    CreatureId? Actor,
    Type CommandType,
    TCommand Command,
    ImmutableArray<Trait> Traits) : ICommandFrame
{
    // Initialized by the dispatcher. This is engine bookkeeping, not part of
    // the public frame contract that rules inspect.
    internal required FrameIdSequence GeneratedIds { get; init; }
}
```

`ICommandFrame` is the common metadata view for any code that needs to inspect a command without reading command-specific payload. Broad listeners should use frame metadata such as actor, traits, command type, source rule, source binding, and provenance. If a rule needs `StrikeCommand`-specific fields, it should register a typed `ICommandStartListener<StrikeCommand, StrikeResponse>` instead of casting through a generic listener.

Why this matters: a frame tells us where a command came from and how it relates to nested commands. Combat logs, once-per-trigger checks, listeners, and replay all need that history, while the command itself stays focused on what the caller requested.

### No Unity References In Rules Data

Rules-layer data must not hold `GameObject`, `MonoBehaviour`, `Transform`, Unity vectors, Unity events, scene objects, UI documents, or assets. Use rules-domain references instead.

```csharp
public readonly record struct CreatureId(string Value);
public readonly record struct TokenId(string Value);
public readonly record struct ItemId(string Value);
public readonly record struct EffectInstanceId(string Value);
public readonly record struct FeatureId(string Value);
public readonly record struct RuleSourceId(string Value);
public readonly record struct RuleBindingId(string Value);
public readonly record struct GridPosition(int X, int Y, int Z);
```

Unity adapters can translate these IDs to scene objects when rendering, animating, or collecting input.

```csharp
public interface IUnityTokenLookup
{
    GameObject GetTokenObject(TokenId token);
    TokenId GetTokenId(GameObject tokenObject);
}
```

Why this matters: PF2e rules should be portable, deterministic, and testable without a Unity scene. Unity should host the rules engine, not be embedded inside every rule object.

### Handlers Do Not Mutate State Directly

Handlers resolve command intent and yield effect data. The engine applies each yielded effect before control returns to the handler, then gives the handler the resulting facts and outcome.

```csharp
public interface ICommandHandler<TCommand, TResponse>
    where TCommand : IRuleCommand<TResponse>
    where TResponse : CommandResponse
{
    RuleProgram<TResponse> HandleCommand(
        CommandFrame<TCommand> frame,
        IRulesSnapshot snapshot,
        ResolutionContext context);
}
```

In plain language, a handler is a recipe that can pause. It asks the engine to make one state change, waits for the engine's receipt, and then decides what to do next. For a Strike, the recipe might ask to spend an action, stop if that failed, calculate the attack, ask to apply damage, and finally return the Strike result. `RuleProgram<TResponse>` represents that paused recipe and its eventual response; it is not a normal `IEnumerable` iterator. Before adopting this proposal, a vertical slice must implement it as executable C#—for example, an explicit state machine or a custom awaitable—and replace the illustrative `yield return` syntax below with that API.

A handler should yield data and branch on the application result before doing dependent work:

```csharp
var spend = yield return new SpendActionEffect(actor, ActionCost.One);
if (!spend.Succeeded)
    return Response.Invalid<MyResponse>(frame.Id, spend.FailureReason);

var damage = yield return new ApplyDamageEffect(target, amount, source: frame.Id);
if (!damage.Succeeded)
    return Response.Invalid<MyResponse>(frame.Id, damage.FailureReason);

var movement = yield return new MoveTokenEffect(token, from, to, source: frame.Id);
if (!movement.Succeeded)
    return Response.Invalid<MyResponse>(frame.Id, movement.FailureReason);
```

The production API should provide syntactic sugar for effects whose failure simply invalidates the current command. For example:

```csharp
var spend = yield return context.Require<MyResponse>(
    new SpendActionEffect(actor, ActionCost.One));

var damage = yield return context.Require<MyResponse>(
    new ApplyDamageEffect(target, amount, source: frame.Id));
```

`Require<TResponse>` means: apply this effect, and if the applier returns a failure or cancellation, stop the rule program with `Response.Invalid<TResponse>(frame.Id, failure)`. Handlers should keep the explicit result-check form when they need custom recovery, partial responses, or different failure behavior.

It should not directly mutate runtime state:

```csharp
controller.ActionPoints -= 1;
targetCreature.TakeDamage(amount);
token.transform.position = nextWorldPosition;
```

Why this matters: when every state change passes through the engine, the same place can validate it, record what happened, update the snapshot, and notify the UI. Applying each requested effect once before the handler resumes also lets later steps react to results such as “action spent,” “movement blocked,” or “target reduced to zero.”

### Handlers And Listeners

Handlers and listeners are separate concepts.

A command handler owns the primary resolution for one command type. It validates the command, calls pure services as needed, and yields effects that the engine should apply. There should normally be one authoritative handler for each command type.

A listener observes a command at a specific lifecycle point and may yield additional effects. Many active rule bindings can listen to the same command. For example, multiple creatures can have a Bless aura active, but that does not mean multiple handlers own `MovementStepCommand`; it means multiple Bless bindings listen after movement and update their own visible effects.

```csharp
public interface ICommandStartListener<TCommand, TResponse>
    where TCommand : IRuleCommand<TResponse>
    where TResponse : CommandResponse
{
    IEnumerable<RuleEffect> OnCommandStarting(
        ActiveRuleBinding binding,
        CommandFrame<TCommand> frame,
        IRulesSnapshot snapshot);
}

public interface ICommandResponseListener<TCommand, TResponse>
    where TCommand : IRuleCommand<TResponse>
    where TResponse : CommandResponse
{
    IEnumerable<RuleEffect> OnCommandResolved(
        ActiveRuleBinding binding,
        CommandFrame<TCommand> frame,
        TResponse response,
        IRulesSnapshot snapshot);
}
```

Listeners receive an `ActiveRuleBinding` so the same rule definition can run once for each active owner or effect instance. The listener object itself is stateless and globally registered; the binding is the per-creature or per-effect state that says this listener currently applies.

Why this matters: the current snapshot, rather than a hidden event subscription, tells the engine which copies of a rule are active. A test can therefore create the bindings it needs, dispatch a command, and inspect the result without setting up Unity objects or global callbacks.

### Rule Definitions Own Feature Logic

A bespoke feature should usually live in one rule definition class. That rule definition may implement command handlers and listeners directly.

Bless is a useful introductory example because it has both a command-time effect and an ongoing aura, but the behavior is still easy to understand. Spell effect resolution is another hook owned by the rule definition; it is shown here as an interface so casting a spell can delegate to the spell's rule without `CastSpellCommand` knowing every spell.

```csharp
public interface ISpellEffectRule
{
    IEnumerable<RuleEffect> ResolveSpellEffect(
        SpellEffectContext spell,
        CommandFrame<CastSpellCommand> frame,
        IRulesSnapshot snapshot);
}

public sealed class BlessRule :
    IRuleDefinition,
    ISpellEffectRule,
    ICommandResponseListener<MovementStepCommand, MovementStepResponse>,
    ICommandResponseListener<AuraRadiusChangedCommand, BasicCommandResponse>
{
    public static readonly RuleSourceId SourceId = RuleSources.Spell("bless");
    public static readonly RuleSourceId AuraSourceId = RuleSources.SpellEffect("bless-aura");
    RuleSourceId IRuleDefinition.SourceId => SourceId;

    public void Register(IRuleRegistryBuilder rules)
    {
        rules.RegisterSpellEffect(SourceId, this);
        rules.AfterCommandCommitted<MovementStepCommand, MovementStepResponse>(AuraSourceId, this);
        rules.AfterCommandCommitted<AuraRadiusChangedCommand, BasicCommandResponse>(AuraSourceId, this);
    }

    public IEnumerable<RuleEffect> ResolveSpellEffect(
        SpellEffectContext spell,
        CommandFrame<CastSpellCommand> frame,
        IRulesSnapshot snapshot)
    {
        // Create the Bless aura active effect and its active rule binding.
    }

    public IEnumerable<RuleEffect> OnCommandResolved(
        ActiveRuleBinding binding,
        CommandFrame<MovementStepCommand> frame,
        MovementStepResponse response,
        IRulesSnapshot snapshot)
    {
        // Recompute visible Bless effects for creatures that moved near this aura.
    }
}
```

Why this matters: a junior engineer should usually be able to open one feature file and see when the feature runs, what it checks, and what changes it requests. Separate helper classes are still useful when code is reused or when one file becomes hard to read, but splitting every hook into its own class would make a single PF2e feature harder to follow.

## Command Frames, Snapshots, Effects, And Facts

The engine moves immutable data through a small number of explicit concepts.

### IRulesSnapshot

`IRulesSnapshot` is the read model for current encounter state. Handlers and listeners query it but do not mutate it.

```csharp
public interface IRulesSnapshot
{
    public CreatureRulesView GetCreature(CreatureId creature);
    public TokenRulesView GetToken(TokenId token);
    public ImmutableArray<ActiveRuleBinding> ActiveBindingsFor(RuleSourceId source);
    public bool HasActiveBinding(CreatureId owner, RuleSourceId source);
    public bool IsEnemy(CreatureId a, CreatureId b);
    public bool IsAlly(CreatureId a, CreatureId b);
    public bool HasTrait(CreatureId creature, Trait trait);
    public bool HasCondition(CreatureId creature, ConditionId condition);
    public ImmutableArray<CreatureId> CreaturesInArea(AreaShape area);
    public bool CanBeDamaged(CreatureId creature);
    public DifficultyClass ResolveFeatureDc(CreatureId owner, RuleSourceId source);
}
```

Why this matters: handlers should be easy to test with a constructed `IRulesSnapshot`. They should not need a running Unity scene, singleton lookup, or a particular `GameObject` hierarchy.

### Frame-Scoped IDs

Rules often create more than one effect, active-effect instance, or binding while handling a command. The dispatcher gives each frame a deterministic `FrameIdSequence`. Extension methods expose the useful operations without making ID creation part of the broad `ICommandFrame` inspection interface:

```csharp
public static class CommandFrameIdExtensions
{
    public static EffectId NewEffectId<TCommand>(
        this CommandFrame<TCommand> frame) =>
        frame.GeneratedIds.NextEffectId();

    public static EffectInstanceId NewEffectInstanceId<TCommand>(
        this CommandFrame<TCommand> frame) =>
        frame.GeneratedIds.NextEffectInstanceId();

    public static RuleBindingId NewBindingId<TCommand>(
        this CommandFrame<TCommand> frame) =>
        frame.GeneratedIds.NextBindingId();
}
```

The dispatcher, not a caller or rule feature, initializes the sequence. That keeps generated IDs repeatable during replay and makes the familiar `frame.NewEffectId()` call available only where the concrete frame type is known.

### Rule Effects

Effects describe side effects requested by handlers and listeners. They are data, not callbacks.

```csharp
public abstract record RuleEffect(
    EffectId Id,
    CommandId SourceCommand,
    RuleSourceId? SourceRule,
    RuleBindingId? SourceBinding);

public sealed record ApplyDamageEffect(
    EffectId Id,
    CommandId SourceCommand,
    RuleSourceId? SourceRule,
    RuleBindingId? SourceBinding,
    CreatureId Target,
    DamageValue Damage)
    : RuleEffect(Id, SourceCommand, SourceRule, SourceBinding);

public sealed record MoveTokenEffect(
    EffectId Id,
    CommandId SourceCommand,
    RuleSourceId? SourceRule,
    RuleBindingId? SourceBinding,
    TokenId Token,
    GridPosition From,
    GridPosition To)
    : RuleEffect(Id, SourceCommand, SourceRule, SourceBinding);
```

Reusable effects should cover common state transitions:

- `SpendActionEffect`
- `SpendReactionEffect`
- `IncrementMultipleAttackPenaltyEffect`
- `ApplyConditionEffect`
- `RemoveConditionEffect`
- `ApplyActiveEffectEffect`
- `RemoveActiveEffectEffect`
- `CreatePersistentAreaEffect`
- `DestroyPersistentEffect`
- `PromptChoiceEffect`
- `RunNestedCommandEffect<TCommand, TResponse>`
- `RecordRuleFactEffect`

Why this matters: shared effects keep feature code small and give common changes—such as spending an action or applying a condition—one consistent implementation. Create a feature-specific effect only when no shared effect describes the requested change.

### Rule Facts

Facts are immutable observations produced during effect application or command resolution. Facts are the trigger surface for later rules and the audit surface for logs and tests.

```csharp
public abstract record RuleFact(
    FactId Id,
    CommandId SourceCommand,
    RuleSourceId? SourceRule,
    RuleBindingId? SourceBinding);

public sealed record DamageAppliedFact(
    FactId Id,
    CommandId SourceCommand,
    RuleSourceId? SourceRule,
    RuleBindingId? SourceBinding,
    CreatureId Target,
    DamageValue Damage,
    HitPoints HitPointsBefore,
    HitPoints HitPointsAfter)
    : RuleFact(Id, SourceCommand, SourceRule, SourceBinding);

public sealed record CreatureReducedToZeroFact(
    FactId Id,
    CommandId SourceCommand,
    RuleSourceId? SourceRule,
    RuleBindingId? SourceBinding,
    CreatureId Creature,
    CreatureId? CausedBy)
    : RuleFact(Id, SourceCommand, SourceRule, SourceBinding);

```

Why this matters: a listener should receive a clear statement such as `DamageAppliedFact`, not search text logs or inspect a Unity component to guess what changed. The same committed facts can drive combat-log entries for damage, conditions, expired effects, disrupted movement, and creatures reduced to 0 HP.

### Effect Application

The engine applies effects in order. Effect appliers are the only layer that mutates encounter state.

```csharp
public interface IRuleEffectApplier<TEffect>
    where TEffect : RuleEffect
{
    EffectApplicationResult Apply(
        TEffect effect,
        MutableRulesState state,
        EffectApplicationContext context);
}

public enum EffectApplicationOutcome
{
    Succeeded,
    Failed,
    Cancelled
}

public sealed record EffectApplicationResult(
    EffectApplicationOutcome Outcome,
    FailureReason? FailureReason,
    RuleEffect? AppliedEffect,
    ImmutableArray<RuleFact> Facts,
    ImmutableArray<RuleEffect> FollowUpEffects)
{
    public bool Succeeded => Outcome == EffectApplicationOutcome.Succeeded;
}

public sealed class ResolutionContext
{
    public IRulesSnapshot CurrentSnapshot { get; }
    public bool Cancelled { get; }
    public IRollService Rolls { get; }
    public IModifierService Modifiers { get; }
    public IEventLog EventLog { get; }
}
```

Why this matters: damage, movement, action spending, and active effects all pass through one auditable boundary. If a request is no longer legal, its applier returns a clear failure so dependent work can stop. After a successful change, the runtime refreshes `ResolutionContext.CurrentSnapshot`; the handler reads that new snapshot instead of relying on stale data. Unity can then animate the committed result without owning the rules state.

`IRuleEffectApplier` is one of this proposal's highest-risk seams, not a settled abstraction. Before migration, a small vertical slice should implement action spending, movement, damage, a nested prompt, and replay through the runner. If that prototype produces a large routing switch, command-specific appliers, or complicated resume behavior, this proposal should not be adopted as written; the reducer-based Ops design in `Ops_Based_Reles_Proposal.md` is the simpler fallback.

## Rule Registration And Active Bindings

Rule definitions are registered globally once. Characters and active effects do not hold live listener instances. Instead, the current rules state stores active bindings.

```csharp
public sealed record ActiveRuleBinding(
    RuleBindingId Id,
    RuleSourceId SourceId,
    CreatureId Owner,
    RuleSourceKind Kind,
    EffectInstanceId? EffectInstance,
    ItemId? SourceItem);
```

Examples:

```csharp
new ActiveRuleBinding(
    bindingId,
    RuleSources.Feat("reactive-strike"),
    owner: fighterId,
    kind: RuleSourceKind.CharacterFeat,
    effectInstance: null,
    sourceItem: null);

new ActiveRuleBinding(
    bindingId,
    RuleSources.SpellEffect("bless"),
    owner: clericId,
    kind: RuleSourceKind.ActiveEffect,
    effectInstance: blessAuraId,
    sourceItem: null);
```

The registry maps command types and phases to rule definitions.

```csharp
public interface IRuleRegistryBuilder
{
    void Handle<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> handler)
        where TCommand : IRuleCommand<TResponse>
        where TResponse : CommandResponse;

    void BeforeCommand<TCommand, TResponse>(
        RuleSourceId source,
        ICommandStartListener<TCommand, TResponse> listener)
        where TCommand : IRuleCommand<TResponse>
        where TResponse : CommandResponse;

    void AfterCommandCommitted<TCommand, TResponse>(
        RuleSourceId source,
        ICommandResponseListener<TCommand, TResponse> listener)
        where TCommand : IRuleCommand<TResponse>
        where TResponse : CommandResponse;

    void BeforeAnyCommand(
        RuleSourceId source,
        ICommandPredicateListener listener);
}
```

During dispatch, the engine invokes a listener only for active bindings of that listener's source.

```csharp
foreach (var registration in registry.AfterCommittedListenersFor<TCommand, TResponse>())
{
    foreach (var binding in snapshot.ActiveBindingsFor(registration.SourceId))
    {
        var effects = registration.Listener.OnCommandResolved(binding, frame, response, snapshot);
        engine.ApplyOrQueue(effects);
    }
}
```

Why this matters: active bindings in state are easier to inspect than hidden subscriptions. If a condition, spell, item, stance, or aura adds or removes a binding during combat, the next command automatically sees the updated set.

## Command Lifecycle

The global lifecycle should stay coarse. Domain-specific pipelines can have finer phases internally.

```csharp
public enum CommandPhase
{
    Begin,
    Handler,
    AfterCommitted,
    Cancelled
}
```

Suggested execution flow:

```csharp
public RuleProgram<TResponse> Execute<TCommand, TResponse>(TCommand command)
    where TCommand : IRuleCommand<TResponse>
    where TResponse : CommandResponse
{
    var frame = frameFactory.Create(command);
    var context = resolutionContextFactory.Create(frame);

    yield return DispatchBeginListeners(frame, context);

    if (context.Cancelled)
        return BuildCancelledResponse<TResponse>(frame, context);

    var handler = registry.GetHandler<TCommand, TResponse>();
    var response = yield return handler.HandleCommand(frame, context.CurrentSnapshot, context);

    yield return DispatchAfterCommittedListeners(frame, response, context.CurrentSnapshot);

    return response;
}
```

`Begin` listeners are for prevention, replacement, and preemption. `AfterCommitted` listeners are for triggers that depend on what actually happened. The rule-program runner applies each yielded effect once, immediately, and returns `EffectApplicationResult` to the yielding handler or listener.

`AfterCommandCommitted` is intentionally more specific than `AfterCommand`. It means the handler has completed, all yielded effects that succeeded have been applied to rules state, and the response includes the resulting facts. If a future rule needs to inspect an effect before it commits, that should be a distinct hook such as `BeforeEffectApplied`, not an ambiguous `AfterCommand` phase.

Why this matters: a short global lifecycle is understandable and reusable. Detailed Strike, damage, persistent-damage, and spell steps stay in their own services instead of becoming phases that every command must know about.

## Traits And Predicate Listeners

PF2e traits are an open set, and the rules layer should use the same trait representation for commands, items, spells, actions, and imported data. The design should not hardcode a closed enum of command-only traits.

```csharp
public readonly record struct Trait(string Slug)
{
    public static readonly Trait Attack = new("attack");
    public static readonly Trait Move = new("move");
    public static readonly Trait Manipulate = new("manipulate");
    public static readonly Trait Concentrate = new("concentrate");
    public static readonly Trait Spellshape = new("spellshape");
    public static readonly Trait Flourish = new("flourish");

    public static Trait FromSlug(string slug) => new(slug);
}
```

Commands remain data-only. They can carry traits as immutable data when the caller already knows them, but commands should not contain logic to derive traits. The frame factory can resolve or enrich traits from action definitions, spell definitions, item profiles, and optional trait providers.

```csharp
public interface ICommandFrameTraitProvider<TCommand>
{
    ImmutableArray<Trait> GetTraits(TCommand command, IRulesSnapshot snapshot);
}
```

This provider exists because some traits are not intrinsic to the command payload alone. For example, `CastSpellCommand` traits depend on the selected spell, rank, action count, and spellshape state. A `StrikeCommand` might derive traits from the selected weapon or unarmed profile. Keeping that derivation outside command objects preserves the data-only command constraint.


Predicate listeners are the escape hatch for rules that care about traits or broad command shape instead of a single concrete command type.

```csharp
public interface ICommandPredicateListener
{
    bool AppliesTo(ICommandFrame frame);
    IEnumerable<RuleEffect> OnCommandStarting(
        ActiveRuleBinding binding,
        ICommandFrame frame,
        IRulesSnapshot snapshot);
}
```

Predicate listeners receive `ICommandFrame`. That keeps broad listeners focused on generic frame metadata and traits. If a listener needs command-specific fields, it should use a typed listener registration for that command type.

Why this matters: a rule that refers to the `manipulate` trait should not need a separate registration for every command that can have that trait. Traits provide the shared match, while typed listeners still provide command-specific data when needed.

## Nested Commands And Prompts

Nested commands are represented as effects or rule-program yields, not direct method calls that bypass the engine.

```csharp
public sealed record RunNestedCommandEffect<TCommand, TResponse>(
    EffectId Id,
    CommandId SourceCommand,
    RuleSourceId? SourceRule,
    RuleBindingId? SourceBinding,
    TCommand Command)
    : RuleEffect(Id, SourceCommand, SourceRule, SourceBinding)
    where TCommand : IRuleCommand<TResponse>
    where TResponse : CommandResponse;
```

Prompts are also effect data.

```csharp
public sealed record PromptChoiceEffect(
    EffectId Id,
    CommandId SourceCommand,
    RuleSourceId? SourceRule,
    RuleBindingId? SourceBinding,
    CreatureId Chooser,
    PromptId Prompt,
    ImmutableArray<PromptOption> Options)
    : RuleEffect(Id, SourceCommand, SourceRule, SourceBinding);
```

A prompt option can submit a command:

```csharp
public sealed record PromptOption(
    PromptOptionId Id,
    string Label,
    IRuleCommand? CommandToSubmit);
```

Why this matters: the same prompt can be answered by a player, an AI policy, or a recorded replay choice. The rules do not need separate code paths for each caller.

## Commands Versus Queries

Not everything should be a command. Commands are for operations that may change rules state, produce prompts, invoke reactions, be cancelled, or return facts that later rules inspect. Pure reads remain services.

Good command candidates:

- `StrikeCommand : IRuleCommand<StrikeResponse>`
- `CastSpellCommand : IRuleCommand<CastSpellResponse>`
- `MovementCommand : IRuleCommand<MovementResponse>`
- `MovementStepCommand : IRuleCommand<MovementStepResponse>`
- `SkillCheckCommand : IRuleCommand<SkillCheckResponse>`
- `SavingThrowCommand : IRuleCommand<SavingThrowResponse>`
- `FlatCheckCommand : IRuleCommand<FlatCheckResponse>`
- `SpendActionCommand : IRuleCommand<BasicCommandResponse>`
- `SpendReactionCommand : IRuleCommand<BasicCommandResponse>`
- `ApplyConditionCommand : IRuleCommand<BasicCommandResponse>`
- `ApplyActiveEffectCommand : IRuleCommand<BasicCommandResponse>`
- `ReloadWeaponCommand : IRuleCommand<ReloadWeaponResponse>`
- `StartTurnCommand : IRuleCommand<StartTurnResponse>`
- `CombatStartCommand : IRuleCommand<BasicCommandResponse>`

Good service/query candidates:

- team relationship checks
- line-of-effect checks
- grid distance checks
- current effect lookup
- current action point lookup
- target/path/area preview
- modifier resolution
- DC calculation
- action roster queries

Why this matters: reads do not need the logging and lifecycle machinery of commands. Commands should represent meaningful attempts to do something, while queries simply inspect the current snapshot.

## Rule Services

Services are allowed and expected, but they should not mutate state. They compute reusable rules results or produce effect proposals.

Pure stateless rules can be static helpers when all dependencies are supplied as parameters.

```csharp
public static class StrikeService
{
    public static StrikeResolution Resolve(
        StrikeCommand command,
        IRulesSnapshot snapshot,
        IRollService rolls,
        IModifierService modifiers);
}

public static class BasicSaveDamage
{
    public static DamageValue Apply(DamageRoll roll, DegreeOfSuccess saveDegree);
}
```

Interfaces are useful when the implementation is stateful, environment-backed, or intentionally replaced in tests. Randomness should use an interface from the start.

```csharp
public interface IModifierService
{
    ModifierBreakdown Resolve(
        CreatureId subject,
        StatisticId statistic,
        ModifierContext context,
        IRulesSnapshot snapshot);
}

public interface IRollService
{
    D20Roll RollD20(RollPurpose purpose, CommandId source);
    DamageRoll RollDamage(DamageExpression expression, CommandId source);
}
```

Why this matters: shared services prevent each feature from reimplementing the same math. A pure calculation can be a static helper; snapshot access, randomness, catalogs, and other test-replaceable behavior should use interfaces.

## Visible Effects And Derived Effects

The UI needs to show current effects immediately. The architecture supports this in two ways.

Use stored active effects when a concrete effect instance should be visible and mechanically queryable:

```csharp
public sealed record ActiveEffectInstance(
    EffectInstanceId Id,
    RuleSourceId SourceRule,
    CreatureId? Owner,
    CreatureId? Target,
    string DisplayName,
    EffectSlug? Slug = null,
    Duration Duration,
    ImmutableArray<ModifierDefinition> Modifiers,
    ImmutableArray<ActiveRuleBinding> Bindings);
```

Use derived projections when the effect is continuously implied by current state, such as difficult terrain around a shield-bearing champion.

```csharp
public interface IDerivedEffectProvider
{
    ImmutableArray<DerivedEffect> GetDerivedEffects(IRulesSnapshot snapshot);
}
```

Concrete stored-effect flow:

```csharp
// BlessRule emits this when an ally enters the aura.
yield return new ApplyActiveEffectEffect(
    frame.NewEffectId(),
    frame.Id,
    BlessRule.SourceId,
    aura.Id,
    target,
    BuildBlessBonusEffect(frame, aura, target));

// The effect applier stores the active effect and emits a fact.
yield return new ActiveEffectAppliedFact(
    factId,
    frame.Id,
    BlessRule.SourceId,
    aura.Id,
    target,
    appliedEffect);

// UnityRulesBridge receives the committed fact and invokes the registered
// BlessEffectPresenter. That presenter asks the snapshot for the target's
// current effects and refreshes the existing HUDController.
```

In that flow, the rules snapshot owns the active effect and its modifier. Unity does not become the source of truth; it refreshes token badges, current-effect lists, and tooltips from `snapshot.ActiveEffectsFor(creature)`.

Concrete derived-effect flow:

```csharp
public sealed class ChampionAuraDerivedEffects : IDerivedEffectProvider
{
    public ImmutableArray<DerivedEffect> GetDerivedEffects(IRulesSnapshot snapshot)
    {
        return snapshot.CreaturesWithRule(ChampionAuraRule.SourceId)
            .SelectMany(champion => BuildAuraProjection(champion, snapshot))
            .ToImmutableArray();
    }
}

// Unity can query derived projections for previews without mutating rules state.
var visibleEffects = snapshot.ActiveEffectsFor(creature)
    .Concat(derivedEffectService.GetDerivedEffects(snapshot).ForCreature(creature));
```

Derived effects are useful when no individual creature needs a stored effect instance yet. The UI can still show them in current-effect panels, aura overlays, movement previews, or targeting hints, but there is no apply/remove churn when creatures move in and out unless a concrete rule needs stored state.

Why this matters: updating every creature after every movement step can create unnecessary work and stale bookkeeping. Store a real effect when the target needs its own lasting state; otherwise derive a view from the current positions and active sources.

## Unity Boundary

The rules engine should live below Unity presentation and input layers.

Unity-facing responsibilities:

- map `CreatureId`, `TokenId`, and `GridPosition` to scene objects;
- collect player targeting/path/area choices;
- display prompts emitted by `PromptChoiceEffect`;
- animate applied movement and damage after rules state changes;
- render active effects and combat logs from facts and state projections.

Rules-facing responsibilities:

- validate commands;
- resolve rolls, checks, saves, damage, effects, and triggers;
- emit immutable effects and facts;
- update `MutableRulesState` through effect appliers.

A Unity adapter can project committed facts into scene and UI work through registered presenters. In this project, the adapter should be a `MonoBehaviour` attached beside `GameManager`, because Unity lifecycle methods provide a clear place to subscribe and unsubscribe:

```csharp
public interface IUnityFactPresenter<in TFact>
    where TFact : RuleFact
{
    void Present(TFact fact, IRulesSnapshot snapshot);
}

[RequireComponent(typeof(GameManager))]
public sealed class UnityRulesBridge : MonoBehaviour
{
    [SerializeField] private HUDController hud;

    private IRulesRuntime rules;
    private UnityFactPresenterRegistry presenters;

    private void Awake()
    {
        // GameManager becomes the composition root that owns the rules runtime.
        rules = GetComponent<GameManager>().Rules;

        presenters = new UnityFactPresenterRegistry();
        presenters.Register(new BlessEffectPresenter(hud));
        presenters.Register(new MovementPresenter(
            this,
            UnityTokenLookup.Instance,
            GridPositionMapper.Instance));
        presenters.Register(new CombatLogFactPresenter(
            CombatLog.GetInstance()));
    }

    private void OnEnable()
    {
        rules.FactCommitted += OnFactCommitted;
    }

    private void OnDisable()
    {
        rules.FactCommitted -= OnFactCommitted;
    }

    private void OnFactCommitted(CommittedRuleFact committed)
    {
        presenters.Present(committed.Fact, committed.CurrentSnapshot);
    }
}

public sealed class BlessEffectPresenter : IUnityFactPresenter<ActiveEffectAppliedFact>
{
    private readonly HUDController hud;

    public BlessEffectPresenter(HUDController hud)
    {
        this.hud = hud;
    }

    public void Present(ActiveEffectAppliedFact fact, IRulesSnapshot snapshot)
    {
        if (fact.Effect.SourceRule != BlessRule.SourceId)
            return;

        hud.RefreshCreatureEffects(
            fact.Target,
            snapshot.ActiveEffectsFor(fact.Target));
    }
}

public sealed class MovementPresenter : IUnityFactPresenter<TokenMovedFact>
{
    private readonly MonoBehaviour coroutineHost;
    private readonly IUnityTokenLookup tokens;
    private readonly IGridPositionMapper positions;

    public MovementPresenter(
        MonoBehaviour coroutineHost,
        IUnityTokenLookup tokens,
        IGridPositionMapper positions)
    {
        this.coroutineHost = coroutineHost;
        this.tokens = tokens;
        this.positions = positions;
    }

    public void Present(TokenMovedFact fact, IRulesSnapshot snapshot)
    {
        var token = tokens.GetTokenObject(fact.Token).transform;
        var destination = positions.ToWorld(fact.To);
        coroutineHost.StartCoroutine(
            GridPrivate.TokenMovement.GetInstance().Hop(token, destination));
    }
}
```

`GameManager.Rules`, `HUDController.RefreshCreatureEffects`, `UnityTokenLookup`, `GridPositionMapper`, and the typed presenter registry are narrow adapters to add during migration; they are not claimed as existing APIs. `CombatLogFactPresenter` converts typed facts to the project's existing `CombatLogEntry` and calls `CombatLog.LogEntry`. `TokenMovedFact` is emitted only after `MoveTokenEffect` commits, so `TokenMovement.Hop` animates an accepted state change rather than deciding whether movement succeeds. A single bridge may own many small plain-C# presenters; they do not each need to be `MonoBehaviour` components.

Why this matters: the project can keep its existing `GameManager`, HUD, combat log, and token animation while moving rules decisions into plain C# a feature at a time.

## Action Definitions And Selection

Long term, `EntityAction` should be replaced as the rules execution abstraction. The replacement is action definition plus action selection plus command submission.

```csharp
public interface IActionDefinition
{
    ActionId Id { get; }
    string DisplayName { get; }
    ActionCost Cost { get; }
    ImmutableArray<Trait> Traits { get; }
    TargetingMode Targeting { get; }
    bool IsAvailable(CreatureId actor, IRulesSnapshot snapshot);
}

public interface IActionDefinition<TCommand, TResponse> : IActionDefinition
    where TCommand : IRuleCommand<TResponse>
    where TResponse : CommandResponse
{
    TCommand BuildCommand(CreatureId actor, ActionSelection selection, IRulesSnapshot snapshot);
}
```

`ActionSelection` is input data gathered by UI or AI.

```csharp
public sealed record ActionSelection(
    CreatureId? TargetCreature,
    GridPosition? TargetPosition,
    ImmutableArray<GridPosition> SelectedPath,
    ItemId? SelectedItem,
    SpellId? SelectedSpell);
```

Why this matters: action definitions describe available choices, commands record a chosen attempt, and handlers resolve it. Keeping those jobs separate lets UI and AI share the same rules without putting everything back into one large action class.

## Normal Strike Example

Normal Strike becomes a typed command. Existing `Unarmed` and `StrikeWeapon` wrappers can submit it during migration.

The code below is a design sketch, not a second implementation to maintain. Once Strike is migrated, replace this sketch with links to the production handler and its focused tests so the document cannot silently drift away from the real behavior.

```csharp
public sealed record StrikeCommand(
    CreatureId Actor,
    CreatureId Target,
    StrikeProfileId Profile,
    bool CostsAction,
    bool AppliesMultipleAttackPenalty,
    bool IncrementsMultipleAttackPenalty)
    : IRuleCommand<StrikeResponse>;

public sealed record StrikeResponse(
    CommandId CommandId,
    CommandOutcome Outcome,
    FailureReason? FailureReason,
    ImmutableArray<RuleEffect> AppliedEffects,
    ImmutableArray<RuleFact> Facts,
    D20Roll AttackRoll,
    DegreeOfSuccess Degree,
    DamageValue DamageApplied,
    bool TargetReducedToZero)
    : CommandResponse(CommandId, Outcome, FailureReason, AppliedEffects, Facts);
```

Handler sketch:

```csharp
public sealed class StrikeRule :
    IRuleDefinition,
    ICommandHandler<StrikeCommand, StrikeResponse>
{
    public static readonly RuleSourceId SourceId = RuleSources.Core("strike");
    RuleSourceId IRuleDefinition.SourceId => SourceId;

    public void Register(IRuleRegistryBuilder rules)
    {
        rules.Handle<StrikeCommand, StrikeResponse>(this);
    }

    public RuleProgram<StrikeResponse> HandleCommand(
        CommandFrame<StrikeCommand> frame,
        IRulesSnapshot snapshot,
        ResolutionContext context)
    {
        var command = frame.Command;
        var facts = ImmutableArray.CreateBuilder<RuleFact>();
        var effects = ImmutableArray.CreateBuilder<RuleEffect>();

        if (command.CostsAction)
        {
            var spend = new SpendActionEffect(frame.NewEffectId(), frame.Id, SourceId, frame.SourceBinding, command.Actor, ActionCost.One);
            var spendResult = yield return spend;
            if (!spendResult.Succeeded)
                return Response.Invalid<StrikeResponse>(frame.Id, spendResult.FailureReason);

            effects.Add(spend);
            facts.AddRange(spendResult.Facts);
            snapshot = context.CurrentSnapshot;
        }

        var resolution = StrikeService.Resolve(command, snapshot, context.Rolls, context.Modifiers);

        if (resolution.Hit)
        {
            var damage = new ApplyDamageEffect(frame.NewEffectId(), frame.Id, SourceId, frame.SourceBinding, command.Target, resolution.Damage);
            var damageResult = yield return damage;
            if (!damageResult.Succeeded)
                return Response.Invalid<StrikeResponse>(frame.Id, damageResult.FailureReason);

            // ApplyDamageEffect is where HP changes and reduced-to-zero facts are produced.
            effects.Add(damage);
            facts.AddRange(damageResult.Facts);
            snapshot = context.CurrentSnapshot;
        }

        if (command.IncrementsMultipleAttackPenalty)
        {
            var map = new IncrementMultipleAttackPenaltyEffect(frame.NewEffectId(), frame.Id, SourceId, frame.SourceBinding, command.Actor, resolution.MapIncrement);
            var mapResult = yield return map;
            if (!mapResult.Succeeded)
                return Response.Invalid<StrikeResponse>(frame.Id, mapResult.FailureReason);

            effects.Add(map);
            facts.AddRange(mapResult.Facts);
            snapshot = context.CurrentSnapshot;
        }

        return new StrikeResponse(
            frame.Id,
            CommandOutcome.Succeeded,
            null,
            effects.ToImmutable(),
            facts.ToImmutable(),
            resolution.AttackRoll,
            resolution.Degree,
            resolution.Damage,
            facts.OfType<CreatureReducedToZeroFact>().Any(f => f.Creature == command.Target));
    }
}
```

The existing `StrikeResolutionPipeline` remains useful. In the first migration, `StrikeService.Resolve` can wrap it. Longer term, the pipeline should be moved below the Unity boundary so it consumes rules IDs and snapshots instead of `GameObject` references.

## Reactive Strike Example

Reactive Strike is a rule definition with listeners and its own command. The listeners detect valid triggers; the command handles the optional reaction, nested Strike, and possible disruption.

Reactive Strike's trigger covers four cases: a creature within reach uses a `manipulate` action, uses a `move` action, makes a ranged attack, or leaves a square during a move action it is using. The example below keeps the trigger surface trait/listener-based and illustrative. Exact movement edge cases, including actions that carry the `move` trait but should not trigger reactions, should be resolved during the concrete Reactive Strike implementation rather than encoded into the generic command-frame contract here.

```csharp
public sealed record ReactiveStrikeCommand(
    RuleBindingId SourceBinding,
    CreatureId Actor,
    CreatureId Target,
    CommandId TriggeringCommand,
    ImmutableArray<Trait> TriggerTraits,
    StrikeProfileId StrikeProfile)
    : IRuleCommand<ReactiveStrikeResponse>;

public sealed record ReactiveStrikeResponse(
    CommandId CommandId,
    CommandOutcome Outcome,
    FailureReason? FailureReason,
    ImmutableArray<RuleEffect> AppliedEffects,
    ImmutableArray<RuleFact> Facts,
    StrikeResponse? Strike)
    : CommandResponse(CommandId, Outcome, FailureReason, AppliedEffects, Facts);
```

```csharp
public sealed class ReactiveStrikeRule :
    IRuleDefinition,
    ICommandPredicateListener,
    ICommandStartListener<StrikeCommand, StrikeResponse>,
    ICommandStartListener<MovementStepCommand, MovementStepResponse>,
    ICommandHandler<ReactiveStrikeCommand, ReactiveStrikeResponse>
{
    public static readonly RuleSourceId SourceId = RuleSources.Feat("reactive-strike");
    RuleSourceId IRuleDefinition.SourceId => SourceId;
    private static readonly PromptId UsePrompt = PromptId.For(SourceId, "use");

    public void Register(IRuleRegistryBuilder rules)
    {
        rules.BeforeAnyCommand(SourceId, this);
        rules.BeforeCommand<StrikeCommand, StrikeResponse>(SourceId, this);
        rules.BeforeCommand<MovementStepCommand, MovementStepResponse>(SourceId, this);
        rules.Handle<ReactiveStrikeCommand, ReactiveStrikeResponse>(this);
    }

    public bool AppliesTo(ICommandFrame frame)
    {
        return frame.Traits.Contains(Trait.Manipulate)
            || (frame.Traits.Contains(Trait.Move)
                && frame.CommandType != typeof(MovementStepCommand));
    }

    public IEnumerable<RuleEffect> OnCommandStarting(
        ActiveRuleBinding binding,
        ICommandFrame trigger,
        IRulesSnapshot snapshot)
    {
        if (!CanPromptReactiveStrike(binding, trigger.Actor, snapshot))
            yield break;

        yield return PromptForReactiveStrike(binding, trigger);
    }

    public IEnumerable<RuleEffect> OnCommandStarting(
        ActiveRuleBinding binding,
        CommandFrame<StrikeCommand> trigger,
        IRulesSnapshot snapshot)
    {
        if (!snapshot.GetStrikeProfile(trigger.Command.Profile).IsRanged)
            yield break;

        if (!CanPromptReactiveStrike(binding, trigger.Command.Actor, snapshot))
            yield break;

        yield return PromptForReactiveStrike(binding, trigger);
    }

    public IEnumerable<RuleEffect> OnCommandStarting(
        ActiveRuleBinding binding,
        CommandFrame<MovementStepCommand> trigger,
        IRulesSnapshot snapshot)
    {
        if (!snapshot.HasReactionAvailable(binding.Owner))
            yield break;

        if (!IsLeavingSquareWithinReach(binding.Owner, trigger.Command.Actor, trigger.Command.From, snapshot))
            yield break;

        yield return PromptForReactiveStrike(binding, trigger);
    }

    private bool CanPromptReactiveStrike(ActiveRuleBinding binding, CreatureId? triggerActor, IRulesSnapshot snapshot)
    {
        return triggerActor.HasValue
            && snapshot.HasReactionAvailable(binding.Owner)
            && CanReachEnemy(binding.Owner, triggerActor.Value, snapshot);
    }

    private RuleEffect PromptForReactiveStrike<TCommand>(
        ActiveRuleBinding binding,
        CommandFrame<TCommand> trigger)
    {
        var command = new ReactiveStrikeCommand(
            SourceBinding: binding.Id,
            Actor: binding.Owner,
            Target: trigger.Actor!.Value,
            TriggeringCommand: trigger.Id,
            TriggerTraits: trigger.Traits,
            StrikeProfile: ChooseMeleeProfile(binding.Owner));

        return new PromptChoiceEffect(
            trigger.NewEffectId(),
            trigger.Id,
            SourceId,
            binding.Id,
            Chooser: binding.Owner,
            Prompt: UsePrompt,
            Options: ImmutableArray.Create(
                PromptOption.Decline(),
                PromptOption.SubmitCommand("Strike", command)));
    }

    public RuleProgram<ReactiveStrikeResponse> HandleCommand(
        CommandFrame<ReactiveStrikeCommand> frame,
        IRulesSnapshot snapshot,
        ResolutionContext context)
    {
        var command = frame.Command;
        var binding = snapshot.GetActiveBinding(command.SourceBinding);
        var effects = ImmutableArray.CreateBuilder<RuleEffect>();
        var facts = ImmutableArray.CreateBuilder<RuleFact>();

        if (binding.SourceId != SourceId || binding.Owner != command.Actor)
            return Response.Invalid<ReactiveStrikeResponse>(frame.Id, FailureReasons.InvalidRuleBinding);

        if (!snapshot.HasReactionAvailable(command.Actor))
            return Response.Invalid<ReactiveStrikeResponse>(frame.Id, FailureReasons.NoReactionAvailable);

        var spend = new SpendReactionEffect(frame.NewEffectId(), frame.Id, SourceId, binding.Id, command.Actor);
        var spendResult = yield return spend;
        if (!spendResult.Succeeded)
            return Response.Invalid<ReactiveStrikeResponse>(frame.Id, spendResult.FailureReason);

        effects.Add(spend);
        facts.AddRange(spendResult.Facts);
        snapshot = context.CurrentSnapshot;

        var strikeCommand = new StrikeCommand(
            Actor: command.Actor,
            Target: command.Target,
            Profile: command.StrikeProfile,
            CostsAction: false,
            AppliesMultipleAttackPenalty: false,
            IncrementsMultipleAttackPenalty: false);

        var strike = yield return new RunNestedCommandEffect<StrikeCommand, StrikeResponse>(
            frame.NewEffectId(),
            frame.Id,
            SourceId,
            binding.Id,
            strikeCommand);

        effects.AddRange(strike.AppliedEffects);
        facts.AddRange(strike.Facts);

        if (strike.Outcome != CommandOutcome.Succeeded)
            return new ReactiveStrikeResponse(
                frame.Id,
                strike.Outcome,
                strike.FailureReason,
                effects.ToImmutable(),
                facts.ToImmutable(),
                strike);

        if (command.TriggerTraits.Contains(Trait.Manipulate) && strike.Degree == DegreeOfSuccess.CriticalSuccess)
        {
            var disrupt = new CancelCommandEffect(
                frame.NewEffectId(),
                frame.Id,
                SourceId,
                binding.Id,
                TargetCommand: command.TriggeringCommand,
                Reason: FailureReasons.Disrupted);

            var disruptResult = yield return disrupt;
            if (!disruptResult.Succeeded)
                return Response.Invalid<ReactiveStrikeResponse>(frame.Id, disruptResult.FailureReason);

            effects.Add(disrupt);
            facts.AddRange(disruptResult.Facts);
        }

        return new ReactiveStrikeResponse(
            frame.Id,
            CommandOutcome.Succeeded,
            null,
            effects.ToImmutable(),
            facts.ToImmutable(),
            strike);
    }
}
```

Why this matters: movement, manipulate actions, and ranged Strikes expose general facts about themselves without depending on Reactive Strike. Reactive Strike decides whether those facts match its trigger, and the frame preserves the original command so a critical hit can disrupt it. Movement that suppresses reactions still needs an explicit, shared rule during implementation.

## Bless Example

Bless demonstrates visible effects and aura maintenance. The player should see that a creature is affected before making an attack.

```csharp
public sealed class BlessRule :
    IRuleDefinition,
    ISpellEffectRule,
    ICommandResponseListener<MovementStepCommand, MovementStepResponse>,
    ICommandResponseListener<AuraRadiusChangedCommand, BasicCommandResponse>
{
    public static readonly RuleSourceId SourceId = RuleSources.Spell("bless");
    public static readonly RuleSourceId AuraSourceId = RuleSources.SpellEffect("bless-aura");
    RuleSourceId IRuleDefinition.SourceId => SourceId;
    private static readonly EffectSlug AttackBonusEffect = EffectSlug.For(SourceId, "attack-bonus");
    private static readonly ModifierSlug AttackBonusModifier = ModifierSlug.For(SourceId, "status-attack-bonus");

    public void Register(IRuleRegistryBuilder rules)
    {
        rules.RegisterSpellEffect(SourceId, this);
        rules.AfterCommandCommitted<MovementStepCommand, MovementStepResponse>(AuraSourceId, this);
        rules.AfterCommandCommitted<AuraRadiusChangedCommand, BasicCommandResponse>(AuraSourceId, this);
    }

    public IEnumerable<RuleEffect> ResolveSpellEffect(
        SpellEffectContext spell,
        CommandFrame<CastSpellCommand> frame,
        IRulesSnapshot snapshot)
    {
        var auraId = frame.NewEffectInstanceId();
        var auraBinding = new ActiveRuleBinding(
            frame.NewBindingId(),
            AuraSourceId,
            Owner: spell.Caster,
            Kind: RuleSourceKind.ActiveEffect,
            EffectInstance: auraId,
            SourceItem: spell.SpellItem);

        yield return new ApplyActiveEffectEffect(
            frame.NewEffectId(),
            frame.Id,
            SourceId,
            frame.SourceBinding,
            Target: spell.Caster,
            Effect: new ActiveEffectInstance(
                Id: auraId,
                SourceRule: AuraSourceId,
                Owner: spell.Caster,
                Target: null,
                DisplayName: "Bless Aura",
                Duration: Duration.OneMinute,
                Modifiers: ImmutableArray<ModifierDefinition>.Empty,
                Bindings: ImmutableArray.Create(auraBinding)));

        foreach (var ally in AlliesInBlessAura(spell.Caster, snapshot))
            yield return ApplyBlessBonus(frame, auraBinding, ally);
    }

    public IEnumerable<RuleEffect> OnCommandResolved(
        ActiveRuleBinding binding,
        CommandFrame<MovementStepCommand> frame,
        MovementStepResponse response,
        IRulesSnapshot snapshot)
    {
        foreach (var candidate in CandidatesNearAura(binding, frame, snapshot))
            foreach (var effect in RecomputeBlessOnCreature(frame, binding, candidate, snapshot))
                yield return effect;
    }

    public IEnumerable<RuleEffect> OnCommandResolved(
        ActiveRuleBinding binding,
        CommandFrame<AuraRadiusChangedCommand> frame,
        BasicCommandResponse response,
        IRulesSnapshot snapshot)
    {
        foreach (var candidate in snapshot.CreaturesInArea(binding.CurrentAuraArea()))
            foreach (var effect in RecomputeBlessOnCreature(frame, binding, candidate, snapshot))
                yield return effect;
    }

    private IEnumerable<RuleEffect> RecomputeBlessOnCreature(
        ICommandFrame frame,
        ActiveRuleBinding aura,
        CreatureId candidate,
        IRulesSnapshot snapshot)
    {
        bool shouldHaveBless = snapshot.IsAlly(aura.Owner, candidate)
            && snapshot.Distance(aura.Owner, candidate) <= snapshot.GetAuraRadius(aura.EffectInstance!.Value);

        bool hasBless = snapshot.HasEffectFrom(candidate, aura.Id, AttackBonusEffect);

        if (shouldHaveBless && !hasBless)
            yield return ApplyBlessBonus(frame, aura, candidate);
        else if (!shouldHaveBless && hasBless)
            yield return new RemoveActiveEffectEffect(frame.NewEffectId(), frame.Id, SourceId, aura.Id, candidate, AttackBonusEffect);
    }

    private RuleEffect ApplyBlessBonus(ICommandFrame frame, ActiveRuleBinding aura, CreatureId target)
    {
        return new ApplyActiveEffectEffect(
            frame.NewEffectId(),
            frame.Id,
            SourceId,
            aura.Id,
            target,
            BuildBlessBonusEffect(frame, aura, target));
    }

    private ActiveEffectInstance BuildBlessBonusEffect(ICommandFrame frame, ActiveRuleBinding aura, CreatureId target)
    {
        return new ActiveEffectInstance(
            Id: frame.NewEffectInstanceId(),
            SourceRule: SourceId,
            Owner: aura.Owner,
            Target: target,
            DisplayName: "Bless",
            Duration: Duration.WhileSourceExists(aura.EffectInstance!.Value),
            Slug: AttackBonusEffect,
            Modifiers: ImmutableArray.Create(
                new ModifierDefinition(
                    AttackBonusModifier,
                    StatisticIds.AttackRoll,
                    ModifierType.Status,
                    +1,
                    SourceId)),
            Bindings: ImmutableArray<ActiveRuleBinding>.Empty);
    }
}
```

When the blessed creature attacks, Strike asks the generic modifier service for attack-roll modifiers. Strike does not know Bless exists.

Why this matters: players can see who currently benefits from Bless, while attack resolution still gathers the bonus through the shared modifier system. Bless-specific IDs stay beside `BlessRule` instead of growing one global list that every new feature must edit.

## Tumble Through Example

Tumble Through is an action command that wraps movement with action-scoped permission to pass through one enemy's space. The grid should not hardcode Tumble Through.

```csharp
public sealed record TumbleThroughCommand(
    CreatureId Actor,
    CreatureId TargetEnemy,
    ImmutableArray<GridPosition> Path)
    : IRuleCommand<TumbleThroughResponse>;

public sealed record TumbleThroughResponse(
    CommandId CommandId,
    CommandOutcome Outcome,
    FailureReason? FailureReason,
    ImmutableArray<RuleEffect> AppliedEffects,
    ImmutableArray<RuleFact> Facts,
    SkillCheckResponse? AcrobaticsCheck,
    bool PassedThroughTargetSpace,
    MovementResponse Movement)
    : CommandResponse(CommandId, Outcome, FailureReason, AppliedEffects, Facts);
```

Handler sketch:

```csharp
public sealed class TumbleThroughRule :
    IRuleDefinition,
    ICommandHandler<TumbleThroughCommand, TumbleThroughResponse>
{
    public static readonly RuleSourceId SourceId = RuleSources.Action("tumble-through");
    RuleSourceId IRuleDefinition.SourceId => SourceId;

    public void Register(IRuleRegistryBuilder rules)
    {
        rules.Handle<TumbleThroughCommand, TumbleThroughResponse>(this);
    }

    public RuleProgram<TumbleThroughResponse> HandleCommand(
        CommandFrame<TumbleThroughCommand> frame,
        IRulesSnapshot snapshot,
        ResolutionContext context)
    {
        var command = frame.Command;
        var effects = ImmutableArray.CreateBuilder<RuleEffect>();
        var facts = ImmutableArray.CreateBuilder<RuleFact>();
        var targetPosition = snapshot.GetCreaturePosition(command.TargetEnemy);
        var split = MovementPath.SplitBeforePosition(command.Path, targetPosition);

        MovementResponse movement = MovementResponse.Empty(frame.Id);
        SkillCheckResponse? acrobatics = null;
        bool passedThrough = false;

        var spend = new SpendActionEffect(frame.NewEffectId(), frame.Id, SourceId, frame.SourceBinding, command.Actor, ActionCost.One);
        var spendResult = yield return spend;
        if (!spendResult.Succeeded)
            return Response.Invalid<TumbleThroughResponse>(frame.Id, spendResult.FailureReason);

        effects.Add(spend);
        facts.AddRange(spendResult.Facts);
        snapshot = context.CurrentSnapshot;

        if (split.BeforeTarget.Length > 0)
        {
            movement = yield return new RunNestedCommandEffect<MovementCommand, MovementResponse>(
                frame.NewEffectId(),
                frame.Id,
                SourceId,
                frame.SourceBinding,
                new MovementCommand(
                    Actor: command.Actor,
                    Path: split.BeforeTarget,
                    Traits: ImmutableArray.Create(Trait.Move),
                    Permissions: ImmutableArray<MovementPermission>.Empty,
                    CostsAction: false));

            effects.AddRange(movement.AppliedEffects);
            facts.AddRange(movement.Facts);
            snapshot = context.CurrentSnapshot;

            if (movement.Outcome != CommandOutcome.Succeeded)
            {
                return new TumbleThroughResponse(
                    frame.Id,
                    movement.Outcome,
                    movement.FailureReason,
                    effects.ToImmutable(),
                    facts.ToImmutable(),
                    acrobatics,
                    passedThrough,
                    movement);
            }
        }

        if (split.TargetWasInPath)
        {
            acrobatics = yield return new RunNestedCommandEffect<SkillCheckCommand, SkillCheckResponse>(
                frame.NewEffectId(),
                frame.Id,
                SourceId,
                frame.SourceBinding,
                new SkillCheckCommand(
                    Actor: command.Actor,
                    Skill: SkillIds.Acrobatics,
                    Dc: snapshot.GetReflexDc(command.TargetEnemy)));

            effects.AddRange(acrobatics.AppliedEffects);
            facts.AddRange(acrobatics.Facts);
            snapshot = context.CurrentSnapshot;

            if (acrobatics.Degree < DegreeOfSuccess.Success)
            {
                return new TumbleThroughResponse(
                    frame.Id,
                    CommandOutcome.Succeeded,
                    null,
                    effects.ToImmutable(),
                    facts.ToImmutable(),
                    acrobatics,
                    passedThrough,
                    movement);
            }

            passedThrough = true;
        }

        var pathAfterCheck = split.TargetWasInPath
            ? split.PathFromTargetThroughEnd
            : split.RemainingPathAfter(split.BeforeTarget);

        if (pathAfterCheck.Length > 0)
        {
            movement = yield return new RunNestedCommandEffect<MovementCommand, MovementResponse>(
                frame.NewEffectId(),
                frame.Id,
                SourceId,
                frame.SourceBinding,
                new MovementCommand(
                    Actor: command.Actor,
                    Path: pathAfterCheck,
                    Traits: ImmutableArray.Create(Trait.Move),
                    Permissions: passedThrough
                        ? ImmutableArray.Create(MovementPermission.EnterOccupiedSpace(
                            command.TargetEnemy,
                            SourceId,
                            TreatAsDifficultTerrain: true))
                        : ImmutableArray<MovementPermission>.Empty,
                    CostsAction: false));

            effects.AddRange(movement.AppliedEffects);
            facts.AddRange(movement.Facts);
            snapshot = context.CurrentSnapshot;
        }

        return new TumbleThroughResponse(
            frame.Id,
            movement.Outcome,
            movement.FailureReason,
            effects.ToImmutable(),
            facts.ToImmutable(),
            acrobatics,
            passedThrough,
            movement);
    }
}
```

Why this matters: Tumble Through spends one action for the whole activity. Its nested movement commands are action-free, so splitting the path around the Acrobatics check does not charge extra actions. A successful check grants permission only for this action to enter the enemy's space, which remains difficult terrain.

## Cranial Detonation Example: Illustrative Future Complex Feature

This example is intentionally more speculative than the first-pass examples. It is included to test whether the design can support complex future PF2e features. The exact implementation will likely evolve by the time this feat is implemented.

Cranial Detonation is a high-level psychic feat that can trigger after a spell reduces non-mindless enemies to 0 HP, then creates cascading area damage from those enemies. It stresses the design because it needs trigger facts, optional activation, frequency tracking, death effects, area saves, chained detonations, and once-per-use target bookkeeping.

Command and response data:

```csharp
public sealed record CranialDetonationCommand(
    RuleBindingId SourceBinding,
    CreatureId Actor,
    CommandId TriggeringSpellCommand,
    ImmutableArray<CreatureId> InitialOrigins,
    MindshiftMode MindshiftMode)
    : IRuleCommand<CranialDetonationResponse>;

public sealed record CranialDetonationResponse(
    CommandId CommandId,
    CommandOutcome Outcome,
    FailureReason? FailureReason,
    ImmutableArray<RuleEffect> AppliedEffects,
    ImmutableArray<RuleFact> Facts,
    ImmutableArray<CreatureId> ExplosionOrigins,
    ImmutableArray<CreatureId> ResolvedTargets)
    : CommandResponse(CommandId, Outcome, FailureReason, AppliedEffects, Facts);
```

Rule definition:

```csharp
public sealed class CranialDetonationRule :
    IRuleDefinition,
    ICommandResponseListener<CastSpellCommand, CastSpellResponse>,
    ICommandHandler<CranialDetonationCommand, CranialDetonationResponse>
{
    public static readonly RuleSourceId SourceId = RuleSources.Feat("cranial-detonation");
    RuleSourceId IRuleDefinition.SourceId => SourceId;
    private static readonly PromptId UsePrompt = PromptId.For(SourceId, "use");

    public void Register(IRuleRegistryBuilder rules)
    {
        rules.AfterCommandCommitted<CastSpellCommand, CastSpellResponse>(SourceId, this);
        rules.Handle<CranialDetonationCommand, CranialDetonationResponse>(this);
    }

    public IEnumerable<RuleEffect> OnCommandResolved(
        ActiveRuleBinding binding,
        CommandFrame<CastSpellCommand> frame,
        CastSpellResponse response,
        IRulesSnapshot snapshot)
    {
        if (binding.Owner != frame.Command.Actor)
            yield break;

        if (!snapshot.HasUnleashedPsyche(binding.Owner))
            yield break;

        if (!snapshot.FrequencyAvailable(binding.Owner, SourceId, FrequencyWindow.OncePerRound))
            yield break;

        var origins = response.Facts
            .OfType<CreatureReducedToZeroFact>()
            .Where(f => f.SourceCommand == frame.Id)
            .Where(f => snapshot.IsEnemy(binding.Owner, f.Creature))
            .Where(f => !snapshot.HasTrait(f.Creature, Trait.FromSlug("mindless")))
            .Select(f => f.Creature)
            .Distinct()
            .ToImmutableArray();

        if (origins.Length == 0)
            yield break;

        yield return new PromptChoiceEffect(
            frame.NewEffectId(),
            frame.Id,
            SourceId,
            binding.Id,
            Chooser: binding.Owner,
            Prompt: UsePrompt,
            Options: ImmutableArray.Create(
                PromptOption.Decline(),
                PromptOption.SubmitCommand("Detonate", new CranialDetonationCommand(
                    binding.Id,
                    binding.Owner,
                    frame.Id,
                    origins,
                    MindshiftMode.Normal))));
    }

    public RuleProgram<CranialDetonationResponse> HandleCommand(
        CommandFrame<CranialDetonationCommand> frame,
        IRulesSnapshot snapshot,
        ResolutionContext context)
    {
        var command = frame.Command;
        var binding = snapshot.GetActiveBinding(command.SourceBinding);
        var effects = ImmutableArray.CreateBuilder<RuleEffect>();
        var facts = ImmutableArray.CreateBuilder<RuleFact>();

        if (binding.SourceId != SourceId || binding.Owner != command.Actor)
            return Response.Invalid<CranialDetonationResponse>(frame.Id, FailureReasons.InvalidRuleBinding);

        if (!CanStillActivate(command, snapshot, context.EventLog))
            return Response.Invalid<CranialDetonationResponse>(frame.Id, FailureReasons.InvalidTrigger);

        var spend = new SpendFrequencyEffect(
            frame.NewEffectId(),
            frame.Id,
            SourceId,
            binding.Id,
            command.Actor,
            SourceId,
            FrequencyWindow.OncePerRound);

        var spendResult = yield return spend;
        if (!spendResult.Succeeded)
            return Response.Invalid<CranialDetonationResponse>(frame.Id, spendResult.FailureReason);

        effects.Add(spend);
        facts.AddRange(spendResult.Facts);
        snapshot = context.CurrentSnapshot;

        var detonated = ImmutableHashSet<CreatureId>.Empty;
        var resolvedTargets = ImmutableHashSet<CreatureId>.Empty;
        var frontier = command.InitialOrigins;

        while (frontier.Length > 0)
        {
            foreach (var origin in frontier)
            {
                detonated = detonated.Add(origin);

                var dead = new ApplyConditionEffect(
                    frame.NewEffectId(),
                    frame.Id,
                    SourceId,
                    binding.Id,
                    origin,
                    ConditionIds.Dead);

                var deadResult = yield return dead;
                if (!deadResult.Succeeded)
                    return Response.Invalid<CranialDetonationResponse>(frame.Id, deadResult.FailureReason);

                effects.Add(dead);
                facts.AddRange(deadResult.Facts);
                snapshot = context.CurrentSnapshot;
            }

            snapshot = context.CurrentSnapshot;
            var area = AreaShape.Union(frontier.Select(origin => AreaShape.EmanationFromCreature(origin, Feet.Of(15))));
            var targets = snapshot.CreaturesInArea(area)
                .Where(target => !resolvedTargets.Contains(target))
                .Where(target => snapshot.CanBeDamaged(target))
                .ToImmutableArray();

            if (targets.Length == 0)
                break;

            var damageSpec = CranialDetonationDamage(command.MindshiftMode);

            var areaDamage = new AreaBasicSaveDamageCommand(
                Actor: command.Actor,
                Targets: targets,
                Area: area,
                Save: damageSpec.Save,
                Dc: snapshot.ResolveFeatureDc(command.Actor, SourceId),
                Damage: damageSpec.Damage,
                Traits: damageSpec.Traits,
                SourceRule: SourceId);

            var areaResponse = yield return new RunNestedCommandEffect<AreaBasicSaveDamageCommand, AreaDamageResponse>(
                frame.NewEffectId(),
                frame.Id,
                SourceId,
                binding.Id,
                areaDamage);

            effects.AddRange(areaResponse.AppliedEffects);
            facts.AddRange(areaResponse.Facts);
            snapshot = context.CurrentSnapshot;

            if (areaResponse.Outcome != CommandOutcome.Succeeded)
            {
                return new CranialDetonationResponse(
                    frame.Id,
                    areaResponse.Outcome,
                    areaResponse.FailureReason,
                    effects.ToImmutable(),
                    facts.ToImmutable(),
                    detonated.ToImmutableArray(),
                    resolvedTargets.ToImmutableArray());
            }

            resolvedTargets = resolvedTargets.Union(targets);

            frontier = areaResponse.Facts
                .OfType<CreatureReducedToZeroFact>()
                .Where(f => snapshot.IsEnemy(command.Actor, f.Creature))
                .Where(f => !detonated.Contains(f.Creature))
                .Select(f => f.Creature)
                .Distinct()
                .ToImmutableArray();
        }

        return new CranialDetonationResponse(
            frame.Id,
            CommandOutcome.Succeeded,
            null,
            effects.ToImmutable(),
            facts.ToImmutable(),
            detonated.ToImmutableArray(),
            resolvedTargets.ToImmutableArray());
    }
}
```

The area damage is generic:

```csharp
public sealed record AreaBasicSaveDamageCommand(
    CreatureId Actor,
    ImmutableArray<CreatureId> Targets,
    AreaShape Area,
    SaveType Save,
    DifficultyClass Dc,
    DamageExpression Damage,
    ImmutableArray<Trait> Traits,
    RuleSourceId SourceRule)
    : IRuleCommand<AreaDamageResponse>;
```

The generic area-damage handler emits nested saves and damage effects. It is not specific to Cranial Detonation.

```csharp
foreach (var target in command.Targets)
{
    var save = yield return new RunNestedCommandEffect<SavingThrowCommand, SavingThrowResponse>(
        frame.NewEffectId(),
        frame.Id,
        command.SourceRule,
        frame.SourceBinding,
        new SavingThrowCommand(target, command.Save, command.Dc));

    var adjustedDamage = BasicSaveDamage.Apply(damageRoll, save.Degree);

    var damage = new ApplyDamageEffect(
        frame.NewEffectId(),
        frame.Id,
        command.SourceRule,
        frame.SourceBinding,
        target,
        adjustedDamage);

    var damageResult = yield return damage;
    if (!damageResult.Succeeded)
        return Response.Invalid<AreaDamageResponse>(frame.Id, damageResult.FailureReason);
}
```

Important design lessons from this example:

- `CastSpellResponse` must contain facts rich enough to detect enemies reduced to 0 HP by that spell.
- Prompt effects need to carry follow-up command data.
- Frequency spending should be a generic effect.
- Chained damage should use generic area damage, saves, and damage effects.
- The feat-specific handler owns only the detonation queue and once-per-use target bookkeeping.
- Final death state should likely be represented as applying the `Dead` condition rather than inventing a separate `LifeState` enum. However, effects such as Breath of Life trigger when a creature would die, so the damage/death pipeline still needs a pre-commit `CreatureWouldDieFact` or `BeforeConditionApplied(Dead)` hook before the `Dead` condition is committed.
- The implementation should mark a creature resolved once it was included in a detonation wave, even if it critically succeeded and took no damage, to preserve the likely intent that one use cannot affect the same creature repeatedly.
- Continuing rule programs must query `context.CurrentSnapshot` after each successful mutation. Cranial Detonation cannot safely calculate later waves from the original trigger snapshot because earlier waves can add Dead conditions, consume frequency, and change targetability.

## Incremental Migration Plan

The migration should be vertical by feature, not horizontal by subsystem. Existing `EntityAction`, HUD, AI, and grid-selection code can continue to call wrappers while individual actions move their resolution into commands.

Current useful seams:

- `StrikeResolutionPipeline` already separates much Strike math from action wrappers.
- `RageRule` already separates some Unity-free Rage decisions from Unity mutation.
- `PreparedCharacter` already derives owned PF2e items, roll options, modifiers, damage dice, and active effects.
- `Conditions` already tracks visible named conditions.
- `IPf2eModifierProvider` and modifier collections already provide a direction for effect-granted modifiers.

Current patterns to migrate away from:

- `DefinedAbilities` using `GameObject` callbacks.
- `ActionController` directly owning all action/reaction/MAP mutation for feature code.
- grid states directly executing rule-relevant movement.
- static or Unity events as hidden rule subscriptions.
- `UnityRuleEffectApplier` as a generic mutation switch.

### Compatibility Wrapper

A temporary command-backed action wrapper lets the current HUD and AI submit commands without a full action roster rewrite.

```csharp
public abstract class CommandBackedEntityAction<TCommand, TResponse> : MultiFrameEntityAction
    where TCommand : IRuleCommand<TResponse>
    where TResponse : CommandResponse
{
    protected override IEnumerator MFInvoke(GameObject actorObject)
    {
        var actor = unityLookup.GetCreatureId(actorObject);
        var selection = yield return GatherSelection(actorObject);
        var command = BuildCommand(actor, selection);
        var response = yield return ruleEngine.Execute(command);
        unityPresenter.ApplyResponse(response);
    }

    protected abstract TCommand BuildCommand(CreatureId actor, ActionSelection selection);
}
```

The wrapper is allowed to touch Unity because it is an adapter. The command it builds must not contain Unity references.

### Migration Order

1. **Reload**
   Add `ReloadWeaponCommand` and `ReloadWeaponResponse`. Keep the wrapper responsible only for selecting the item and submitting the command.

2. **Rage**
   Convert `RageRule` to `RageCommand` and `EndRageCommand`. Replace direct use of `UnityRuleEffectApplier` with generic effects such as `SpendActionEffect`, `ApplyActiveEffectEffect`, and `GrantTemporaryHitPointsEffect`.

3. **Strike**
   Wrap the existing `StrikeResolutionPipeline` in `StrikeCommand`. Move action cost, MAP increment, attack roll facts, damage facts, and reduced-to-zero facts into the command/effect path.

4. **Stride and Movement**
   Keep grid states for path selection and preview. Move path execution into `MovementCommand`, which emits a `MovementStepCommand` per step. Path preview and committed movement should share movement cost providers.

5. **Conditions and Turn Start**
   Migrate action restoration and condition turn hooks to `StartTurnCommand` or `ResetActionPointsCommand`. Conditions such as Slowed should no longer install hidden UnityEvent callbacks.

6. **Combat Start and End**
   Replace direct `Pf2eRulesEngine.ApplyCombatStartRules` and `EndEncounter` feature calls with `CombatStartCommand` and `CombatEndCommand` listeners. Quick-Tempered becomes a listener that emits a zero-cost `RageCommand`.

7. **Static Rule Events**
   Keep static events temporarily as compatibility adapters for audio/UI. Rule behavior should move to command responses and facts.

8. **Action Roster and AI**
   Replace `ActionController.Actions`, `Movements`, and `Reactions` with action providers that return `ActionDefinition` objects. AI evaluates definitions and selections instead of type-checking concrete action classes.

9. **Remove `EntityAction` Execution**
   Once HUD and AI submit commands directly, delete the command-backed wrappers and remove `EntityAction` as a rules execution abstraction.

A feature is fully migrated when:

- it has data-only command and response types;
- its behavior is owned by a rule definition or generic service;
- all state changes are emitted as effects and applied by the engine;
- its old wrapper, if present, only gathers Unity input and submits commands;
- tests cover the command path rather than legacy direct mutation.

## First-Pass Scope

The first pass should establish the architectural constraints without trying to implement every future PF2e edge case.

1. Define immutable command, response, effect, fact, and frame types.
2. Define rules-domain IDs and remove Unity references from new rules-layer data.
3. Add rule registry, active rule bindings, command handlers, and response/start listeners.
4. Add central effect application and fact recording.
5. Add action economy effects/commands for actions, reactions, and MAP.
6. Add a `RollService` seam for new command handlers.
7. Add compatibility wrappers for existing `EntityAction` flows.
8. Migrate Reload and Rage first.
9. Wrap Strike through `StrikeCommand` while preserving existing target selection.
10. Add movement command and movement step command.
11. Add visible active effect storage and synchronization with modifier/predicate state.
12. Implement Bless as the first visible aura/effect example.
13. Implement Reactive Strike using movement step start and trait-aware listeners.
14. Implement Tumble Through using action-scoped movement rules.

Pending next-command hooks are not required for the first pass, but the design should reserve space for them because spellshape-style features will need them.

## Deferred Scope

- full replacement of `EntityAction` with `ActionDefinition`;
- complete Unity-free rewrite of Strike internals;
- generic spell result modeling for every spell shape;
- persistent areas and hazardous terrain;
- teleport and forced movement taxonomy;
- replacement effects;
- fortune/misfortune/reroll arbitration;
- complete replay/rewind tooling;
- data-driven compilation of imported PF2e rule elements into action definitions, listeners, modifiers, and active effects;
- broad migration of static event consumers to command facts.

## Design Review Findings

### The Design Is Stronger With Data-Only Commands

Separating command request data from execution logic is worth the upfront cost. It improves replay, testing, logging, UI transparency, and future Unity detachment. The cost is extra boilerplate, especially for simple actions. The recommended mitigation is not to collapse logic back into commands, but to keep simple rule definitions small and use shared services for common math.

### Rule Definitions Should Usually Own Their Own Hooks

For bespoke PF2e features, a single class implementing its own listeners and handlers is more maintainable than separate trigger and handler classes by default. This keeps feature behavior local. Split only when reuse or complexity justifies it.

### Active Bindings Are Better Than Live Subscriptions

Do not attach listener instances to creatures at combat start. Hidden subscriptions are hard to replay, save, undo, inspect, and update when effects are gained or lost mid-combat. Store active bindings in rules state and let dispatch query the snapshot.

### Facts Are Required For Advanced Triggers

Features like Cranial Detonation and Foreseen Failure cannot be implemented cleanly if spell and damage responses only say `Succeeded`. Commands must emit meaningful facts such as damage applied, spell had no effect, creature reduced to zero, modifier granted, action disrupted, movement completed, and effect expired.

### Effect Application Is The Key Invariant Boundary

If handlers directly mutate HP, position, actions, reactions, conditions, or active effects, the architecture loses most of its value. The effect applier is where state changes, facts, logs, and UI notifications should be coordinated.

### UI Transparency Requires Current Effect Projections

Waiting until roll time to discover modifiers is mechanically workable but poor UX. The current rules snapshot must be able to answer what effects currently apply to a creature. Some effects should be stored active effects; others can be derived projections. Both should be queryable by UI.

### Coarse Command Phases Are Enough Only With Domain Pipelines

The command lifecycle should remain small. Complex domains such as Strike damage, spell resolution, afflictions, and persistent damage can have their own internal services or pipelines. Do not turn the global command phase enum into every PF2e timing hook.

### Replay Requires More Than Commands

This design makes replay feasible, but not automatic. Full replay also requires stable IDs, deterministic command ordering, recorded rolls, recorded prompt choices, versioned data, and probably periodic snapshots or deltas.

### Action Economy And Rolls Need First-Class Seams

Action points, reactions, MAP, and rolls should be first-pass engine concepts. They can start as thin wrappers around existing `ActionController`, `D20`, and `Dice` behavior, but new rule code should request these through effects, commands, or services. Direct mutation and direct randomness are two of the easiest ways to lose replayability.

### Grid FSM Should Become Input And Preview Only

The existing grid FSM still has value for selecting targets, paths, and areas. Long term it should not execute movement, spend resources, apply damage, or resolve rule triggers. Movement execution belongs in `MovementCommand`, and the same movement-cost providers should serve both preview and committed movement.

### Data-Driven Rule Elements Should Compile Into Runtime Rules

`PreparedCharacter` already converts imported PF2e data into modifiers, roll options, damage dice, and active effects. Long term, supported imported rule elements should compile into action definitions, modifier definitions, active effects, derived effects, or active rule bindings. They should not expand `DefinedAbilities` or `Pf2eRulesEngine` into larger static switchboards.

### Listener Ordering Must Be Deterministic

Multiple rules can react to the same command. Dispatch order must be deterministic, but the first-pass API should avoid numeric listener priorities because they create implicit dependencies across unrelated rules. Use a stable engine-defined ordering for independent listeners, and model real before/after requirements as explicit lifecycle hooks, typed pipelines, or command/effect relationships. Component order, registration order from scene objects, and dictionary iteration order are not acceptable rules ordering mechanisms.

## Test Expectations

Prefer deterministic EditMode tests for:

- immutable command and response construction;
- absence of Unity references in rules-layer data;
- command frame provenance and nesting;
- active binding dispatch;
- handler and listener ordering;
- effect application order;
- fact emission from effect appliers;
- blocked and cancelled typed responses;
- action economy effects;
- roll service determinism;
- Strike command parity with current behavior;
- Bless visible effect add/remove behavior;
- Reactive Strike prompt, reaction spending, and nested Strike behavior;
- Tumble Through movement-rule behavior;
- Cranial Detonation-style chained fact handling in pure rules tests when that feature is eventually implemented.

Use PlayMode tests for:

- compatibility wrapper behavior through existing HUD actions;
- AI compatibility during the migration period;
- visible active effect updates in the real UI;
- reaction prompts during movement;
- grid movement animation after command-applied movement state.

Run Unity tests with the project Unity version and do not pass `-quit`.
