# Encounter Rules Runtime Implementation Guide

This is the as-built guide to the production encounter rules runtime. Use it when changing an
encounter feature, action, effect, bridge, projection, or combatant-enrollment path. It records the
current code locations, composition order, authority boundaries, migration status, and working
recipes.

The [rules runtime design](Rules_Runtime_Design.md) defines the smaller, durable architecture. This
guide deliberately documents some production mechanisms that are not universal design
requirements. If this guide and production code disagree about current behavior, the code is the
source of truth and this guide should be updated with the change.

## Authority in a live encounter

`UnityCombatRulesBridge` owns one `RuleDispatcher`, one `RulesState`, and one encounter lifetime.
After a controller and creature attach to that exact bridge, the rules store is authoritative for
the state slices registered there. Unity objects provide initial data and then become
clients or projections of the migrated state.

| Concern | Production authority |
| --- | --- |
| Encounter phase, roster, initiative, round, and current turn | `EncounterState` in `RulesState` |
| Encounter action economy | `ActionEconomyState`; controller values are read projections |
| Health, temporary Hit Points, and defeat | `HealthState`; `CreatureComponent` receives committed projections |
| Position, land Speed, and movement budget | Runtime movement slices; Unity transforms project committed movement |
| Multiple attack penalty | `MultipleAttackPenaltyState` |
| Strike equipment, ammunition, and loaded state | Runtime equipment/ammunition slices prepared by the Strike module |
| Spell slots, Focus Points, active effects, and bindings | Runtime resource/effect slices prepared by feature modules |
| Active-effect timing | Membership in `ActiveEffects` means an effect is active. `ActiveEffectTimingState` is an intentionally materialized schedule: it copies immutable effect and binding identifiers, source, duration behavior (encounter-scoped or boundary-counted), and creation order so boundary advancement can filter and order without loading related state on every boundary, and removal can address the binding directly rather than reverse-searching for it. Only `RemainingBoundaries` evolves. Expiration atomically removes the effect and associated state. |
| Rule checks and modifier stacking for migrated actions | Runtime check handlers and `ModifierCollection` |

Attachment is identity-sensitive. A read through a creature or controller is valid only for the
bridge that currently owns it. Cleanup from an older encounter must not detach or overwrite a newer
owner.

The encounter is not fully rules-native. Rotting Aura and Slowed still enter turn-start resolution
through Unity-backed adapters, prepared-character and component data are still read during
enrollment, and some scene-compatible manager entry points remain. Treat those paths as migration
seams, not alternative authorities.

`UnityCombatRulesBridge.CreateExplorationStride` is a special temporary composition. It reuses the
Stride rules without attaching combat authority or spending encounter action economy.

## Production code map

| Responsibility | Primary code |
| --- | --- |
| Typed operations, frames, and structural results | [`OperationContracts.cs`](../Assets/Scripts/Rules/Runtime/OperationContracts.cs), [`OperationFrames.cs`](../Assets/Scripts/Rules/Runtime/OperationFrames.cs), [`OperationResults.cs`](../Assets/Scripts/Rules/Runtime/OperationResults.cs) |
| Dispatcher construction and execution | [`RuleDispatcherBuilder.cs`](../Assets/Scripts/Rules/Runtime/RuleDispatcherBuilder.cs), `RuleDispatcher*.cs`, [`Dispatch.cs`](../Assets/Scripts/Rules/Runtime/Dispatch.cs) |
| Store, drafts, snapshots, reducers, and Facts | [`RulesState.cs`](../Assets/Scripts/Rules/Runtime/RulesState.cs), `RulesState*.cs`, [`Reduction.cs`](../Assets/Scripts/Rules/Runtime/Reduction.cs), [`RuleDispatcherFacts.cs`](../Assets/Scripts/Rules/Runtime/RuleDispatcherFacts.cs) |
| Action lifecycle and costs | `ActionLifecycle*.cs`, [`ActionCosts.cs`](../Assets/Scripts/Rules/Runtime/ActionCosts.cs), [`ActionCostFacts.cs`](../Assets/Scripts/Rules/Runtime/ActionCostFacts.cs) |
| Definitions, bindings, and active effects | [`RuleRegistry.cs`](../Assets/Scripts/Rules/Runtime/RuleRegistry.cs), [`RuleDefinitions.cs`](../Assets/Scripts/Rules/Runtime/RuleDefinitions.cs), [`RuleBindingStateValues.cs`](../Assets/Scripts/Rules/Runtime/RuleBindingStateValues.cs), `ActiveEffect*.cs` |
| Shared health, checks, modifiers, movement, and encounter capabilities | `Health*.cs`, [`CheckHandlers.cs`](../Assets/Scripts/Rules/Runtime/CheckHandlers.cs), [`Modifiers.cs`](../Assets/Scripts/Rules/Runtime/Modifiers.cs), `Movement*.cs`, `Encounter*.cs` |
| Current action and feature rules | [`StrideRules.cs`](../Assets/Scripts/Rules/Runtime/StrideRules.cs), [`StrikeRules.cs`](../Assets/Scripts/Rules/Runtime/StrikeRules.cs), [`SpellcastingRules.cs`](../Assets/Scripts/Rules/Runtime/SpellcastingRules.cs), [`SpellAttackRules.cs`](../Assets/Scripts/Rules/Runtime/SpellAttackRules.cs), [`RageRules.cs`](../Assets/Scripts/Rules/Runtime/RageRules.cs) |
| Unity encounter composition | [`UnityEncounterModuleSet.cs`](../Assets/Scripts/Rules/Unity/Composition/UnityEncounterModuleSet.cs), [`UnityEncounterComposition.cs`](../Assets/Scripts/Rules/Unity/Composition/UnityEncounterComposition.cs) |
| Enrollment and rollback | [`UnityCombatantEnrollmentPipeline.cs`](../Assets/Scripts/Rules/Unity/Composition/UnityCombatantEnrollmentPipeline.cs) |
| Unity authority and synchronous dispatch boundary | [`UnityCombatRulesBridge.cs`](../Assets/Scripts/Rules/Unity/UnityCombatRulesBridge.cs) |
| Strike and spell Unity adapters | [`UnityStrikeEncounterModule.cs`](../Assets/Scripts/Rules/Unity/Strike/UnityStrikeEncounterModule.cs), [`UnitySpellcastingEncounterModule.cs`](../Assets/Scripts/Combat/Spells/UnitySpellcastingEncounterModule.cs) |
| Typed action lifecycle presentation routing and ordered draining | [`UnityActionPresentationRegistry.cs`](../Assets/Scripts/Rules/Unity/UnityActionPresentationRegistry.cs) |
| Health and encounter projection | [`UnityHealthProjectionModule.cs`](../Assets/Scripts/Rules/Unity/Composition/UnityHealthProjectionModule.cs), [`UnityEncounterProjectionModule.cs`](../Assets/Scripts/Rules/Unity/Composition/UnityEncounterProjectionModule.cs) |

The wildcard families above are navigation hints, not Markdown links. Inspect the neighboring files
for the selected capability rather than treating one large file as the entire subsystem.

## Production composition

`UnityEncounterModuleSet.Create` is the only production module list. Its order is part of the
composition contract:

1. `RottingAuraEncounterModule`
2. `SlowedEncounterModule`
3. `UnityRageEncounterModule`
4. `UnityStrikeEncounterModule`
5. `UnitySpellcastingEncounterModule`
6. `UnityActionPresentationModule`
7. `UnityLightEncounterModule`
8. `UnityHealthProjectionModule`
9. `UnityEncounterProjectionModule`

Before constructing that list, the module set creates shared typed contexts and catalogs, defines
every `RuleDefinitionId` required by this composition, and builds the `RuleRegistry`. Modules are
supplied explicitly; they do not discover or register themselves. The bridge supplies that exact
registry to active-effect and encounter runtime composition and supplies the composed action catalog
to the action lifecycle before any module configures the dispatcher.

`UnityEncounterComposition` preserves the supplied order and invokes only the capability interfaces
each module implements:

| Capability | Current purpose |
| --- | --- |
| `IUnityEncounterDispatcherModule` | Add feature handlers, reducers, validators, middleware, or listeners before `Build` |
| `IUnityEncounterTurnStartModule` | Supply a transitional turn-start adapter |
| `IUnityEncounterRuntimeModule` | Register observers or other encounter-owned runtime resources |
| `IUnityEncounterActionPresentationModule` | Register typed feature presenters by stable action definition |
| `IUnityEncounterTopologyModule` | Replace a feature's live Unity grid adapter after topology changes |
| `IUnityCombatantEnrollmentModule` | Prepare feature state and Unity installation for both initial participants and reinforcements |

Implement only the capabilities the feature needs. A presentation-only feature should not receive
dispatcher or enrollment hooks merely for symmetry.

### Current module capabilities

| Module | Capabilities |
| --- | --- |
| Rotting Aura | Transitional turn-start adapter |
| Slowed | Transitional turn-start adapter |
| Rage | Dispatcher configuration and combatant enrollment |
| Strike | Dispatcher, action presentation, runtime state projection, combatant enrollment, and topology refresh |
| Spellcasting | Dispatcher, action presentation, runtime effect projection, combatant enrollment, and topology refresh |
| Action presentation | Runtime registration of the shared lifecycle Fact observer and encounter-owned coordinator |
| Light | Runtime effect presentation |
| Health projection | Runtime Fact projection |
| Encounter projection | Runtime Fact and settlement projection |

## Construction order

The private `UnityCombatRulesBridge` constructor performs these boundaries in order:

1. Create the mutable topology provider, shared feature contexts, catalogs, registry, explicit
   module set, composition, and enrollment pipeline.
2. Call `UnityCombatantEnrollmentPipeline.Prepare` for all initial participants.
3. Build the dispatcher store from an empty `RulesStateSeed`; combat encounter construction does
   not seed combatant state.
4. Configure shared runtimes on `RuleDispatcherBuilder`: health, MAP, checks, active effects,
   encounter rules, action lifecycle, movement, and Stride.
5. Compose feature-owned typed action presenters into `UnityActionPresentationRegistry`, invoke
   `ConfigureDispatcher` for feature modules in module order, build the dispatcher, then invoke
   `RegisterRuntime` (including the single presentation Fact observer) in module order.
6. Dispatch `InitEncounterOp`, commit the initial prepared batch through `AddCombatantsOp`, retain
   its now-durable identity and registration-map reservations, then call `AttachAndInstall` and
   transfer the plan to the encounter lifetime before `Create` returns.
7. The caller invokes `AdvanceEncounter` to dispatch `AdvanceEncounterOp`, activate the encounter,
   publish encounter-start presentation, and reach the first turn.

State, dispatcher registrations, and runtime observers therefore exist before Unity components are
allowed to route reads or actions through the bridge. A failure during reversible preparation rolls
back provisional mappings and resources. Once `AddCombatantsOp` commits, its rules state and
registration maps are durable even if later notification or installation fails.

## Combatant enrollment

Initial participants and reinforcements use the same preparation path. The pipeline validates the
batch, allocates identities, captures required Unity data, installs provisional lookup maps, and
asks enrollment-capable modules for typed contributions. It moves known fallible reads and
preparation ahead of the authoritative commit, but the current Unity installation phase can still
fail afterward.

`Prepare` validates the complete controller batch, reserves creature/player identity allocation,
creates every Unity-to-rules map provisionally, validates future attachments, invokes every
enrollment module, captures initiative modifiers, and freezes complete `CombatantRulesState` values
plus installation plans. Creating all maps before module preparation lets restored effects resolve
cross-combatant sources while preparation is still reversible.
`UnityCombatantEnrollmentBuilder` exposes the supported contribution APIs:

- `Own<TResource>` for reversible preparation resources;
- `AddSpellSlots`, `AddRuleBindings`, `AddEquipment`, `AddAmmunition`, and `AddActiveEffects` for
  rules state committed atomically with the combatant; and
- `AddInstallation(IUnityCombatantInstallationContribution)` for precomputed Unity changes.

Do not expand that builder with a new feature-named field. Add feature-owned values to the complete
registration, or avoid persisted state if the value can be derived.

If any preparation read or preflight fails, the preparation `CompositeLifetime` rolls back maps,
identity allocation, and feature-owned resources. Cleanup failures are retained with the original
failure. Preparation must therefore perform every fallible Unity query needed by `Apply`.

This is not a promise that arbitrary work after an addition commit can roll back `RulesState`.
The commit boundary is intentional: installation contributions must be deterministic applications
of precomputed data and must not repeat fallible discovery or validation.

### One combatant-addition path

Initial participants and reinforcements call the same `UnityCombatantEnrollmentPlan.Commit`, which
dispatches one `AddCombatantsOp` containing a normalized immutable list of complete registrations.
Its handler rolls initiative and derives stable order and round eligibility. One reducer validates
the whole batch against the exact composed registry, requires each effect-backed binding and active
effect to form exactly one matching same-batch pair, commits every state slice, inserts the
initiative entries, initializes action economy and MAP, preserves an active exact turn, and stages
`CombatantsAddedFact` plus any restored `ActiveEffectCreatedFact` values. Initiative assignments
are published from a later frame so newly committed bindings observe their own assignment exactly
once.

An initialized encounter may have an empty roster, cursor `-1`, and no current turn; every other
phase requires at least one roster entry. Initial additions naturally qualify for round one. An
active-turn addition inserted at or before the reached actor waits until the next round; one inserted
after it remains eligible in the current round. Adding combatants never advances, begins, or ends a
turn.

When dispatch exits, including when post-commit notification throws, the plan checks committed
`RulesState` to retain identity and registration-map reservations for the atomic batch. A dispatch
exception does not imply rollback. On success it then calls `AttachAndInstall` before
`TransferTo(encounterLifetime)`, which transfers lifetime ownership only. If installation throws,
the failure surfaces and locally owned attachment and feature resources are disposed, but the
durable rules registration and maps are not rolled back. A new feature contributes complete
registration state and therefore supports both initial and later batches without a second state
workflow.

### Restored-effect enrollment

`UnitySpellcastingEncounterModule` is the production example of state that crosses both enrollment
routes. During preparation it converts supported `SpellEffectController` entries into paired
`ActiveEffectInstance` and `ActiveRuleBinding` values with stable IDs. The complete combatant
registration carries each restored effect and matching binding through the same addition reducer.
That reducer creates encounter timing and emits the generic `ActiveEffectCreatedFact`.
`RestoredSpellEffectTimingObserver` projects initiative-boundary counts and removes Unity effects
when `ActiveEffectRemovedFact` commits.

Finite effects created for a source in a populated initialized encounter are scheduled immediately,
before the first explicit advance, just as they are during an active encounter. This lets
initiative-assignment listeners such as Quick-Tempered apply pre-first-turn behavior without
creating an unscheduled effect.

### Encounter operations

[`EncounterRuleRuntime`](../Assets/Scripts/Rules/Runtime/EncounterRuleRuntime.cs) installs the
encounter handlers and engine reducers. Its current division of responsibility is:

- `InitEncounterHandler`: commit an empty initialized encounter and its conclusion policy without
  starting presentation or initiative.
- `AddCombatantsHandler`: roll initiative, derive stable order and eligibility, atomically commit
  complete combatant registrations, and publish assignments from a later frame.
- `AdvanceEncounterHandler`: evaluate immediate outcomes, then request the next initiative boundary.
  Its first call activates a populated initialized encounter and emits `EncounterStartedFact` before
  reaching the first boundary. The boundary reducer advances the cursor and effect countdowns,
  removes every due effect and its associated binding/frequency/timing state in deterministic order,
  and finally stages `InitiativeBoundaryReachedFact` in the same atomic commit.
- `BeginInitiativeTurnHandler`: reset movement budget, run ordered turn-start adapters, stop if the
  actor is defeated, then commit the exact turn and final action contribution.
- `EndTurnHandler`: require the exact current `TurnIdentity`, run turn-end work, reset movement,
  clear turn resources through reducers, and advance.
- `EncounterOutcomeListener`: after reaction-phase zero-HP listeners settle, finalize defeat and
  evaluate encounter outcome.
- `SuspendEncounterHandler` and `EndEncounterHandler`: remove encounter-owned timed effects as
  expired before committing suspension or outcome.
- Encounter reducers and the shared reducers they invoke atomically mutate roster, initiative
  boundary, current turn, actions, reactions, MAP, movement reset state, phase, and outcome while
  emitting committed Facts.

## Dispatch, action, and Fact timing

The bridge exposes a synchronous Unity boundary. `Dispatch` returns structural operation results;
internal convenience paths require resolution and translate invalid requests to
`InvalidOperationException`. A Unity-originated synchronous request rejects unresolved asynchronous
callback work.

Within the dispatcher:

- one root owns its operation frames and nested dispatches;
- an action is validated, pays its complete costs atomically, resolves `ActionBegunOp`, publishes
  one `ActionBegunFact<TResult>`, and then invokes its feature handler;
- reducers atomically commit state and return immutable state-change Fact payloads;
- the dispatcher records source, root, exact-snapshot, and listener-delivery provenance internally
  without mutating those payloads;
- after the handler and awaited children complete, a resolved action publishes one
  `ActionResolvedFact<TResult>` against the unchanged committed snapshot;
- synchronous external Fact observers receive the causal tree's transient observation root and the
  exact associated snapshot, independently log and swallow failures, and cannot fail or interrupt
  mechanics;
- asynchronous binding-scoped Fact listeners preserve authoritative rules semantics and may create
  causal follow-up roots; and
- settlement observers report when roots and their causal trees finish.

For Strike, the production handler resolves the attack first, then dispatches damage, loaded-state
changes, and multiple attack penalty work. Presentation observes the parent Strike's resolved
lifecycle Fact only after that complete workflow. Spell presentation uses the cast's existing
`CastSpellOutcome`, including its actual `SpellAttackResolution` collection; shared infrastructure
has no spell special case.

`UnityActionPresentationRegistry` is the generic Unity routing boundary. Feature modules explicitly
register typed presenters by stable `ActionDefinitionId`; the registry verifies the concrete
action/outcome pair. Its observer opens one encounter-owned sequence for the exact action at begin,
then appends feature presentation and committed target reactions in Fact order. Strike and spell
callers drain that exact sequence after synchronous dispatch before unlocking. The coordinator has
one top-level execution catch: the first failure is logged, remaining steps are abandoned, and
exact-action/root mappings are released. There is no retry or recovery state. Stride uses the same
synchronous observer boundary to queue committed movement steps, then drains its root-scoped Unity
projection before deciding whether an exploration route may continue.

Strike and spell presenters own attacker animation and their action result presentation. They do
not reselect targets or recalculate outcomes. Every committed health Fact immediately projects its
exact `HealthState` into `CreatureComponent`, so HUD reads remain authoritative; hit and defeat
reactions join the active action sequence in Fact order or present immediately when no sequence owns
their observation root.

### Encounter presentation settlement

`UnityEncounterProjectionModule` observes encounter Facts. Start is projected immediately; turn
begin, turn end, and encounter outcome callbacks are queued by observation root and drained when the
causal tree settles. Exact root/child settlement provenance remains internal to the dispatcher and
bridge. This prevents visible encounter boundaries from running ahead of rules work caused by the
same boundary.

The root/child queue in `UnityCombatRulesBridge` is implementation-specific presentation machinery.
Use it for the encounter boundaries it currently serves. Do not add causal IDs or settlement state
to unrelated feature DTOs unless that feature demonstrably needs settlement-aware presentation.

## Topology and ownership release

`RefreshTopology` creates and installs a new immutable `GridTopology`, refreshes topology-capable
feature adapters in module order, and then replaces the bridge's current tile array. Refresh only
between rules roots; the mutable topology provider guards against replacement during resolution.

The encounter owns one `CompositeLifetime`. Runtime observers and enrollment plans transfer their
registrations into it. `ReleaseOwnership` waits until the outer synchronous dispatch boundary ends,
projects final authoritative health, and disposes the lifetime. Detach operations verify exact
bridge identity so delayed cleanup cannot disturb newer encounter ownership.

Root-scoped temporary observers are different: keep their tokens locally owned and dispose them when
that root ends. Do not transfer short-lived observation into the encounter lifetime.

## Current implementation status

| Area | Status |
| --- | --- |
| Dispatcher, typed operations/results, reducers, snapshots, Facts, and deterministic rolls | Production |
| Action lifecycle and atomic action/rule-resource costs | Production |
| Encounter roster, initiative, turn progression, action economy, and conclusion | Production authority |
| Health, temporary Hit Points, defeat, and Unity health projection | Production authority |
| Stride and movement topology/budget | Production; bridge still has first-slice Stride helpers |
| Strike, checks, modifier collection, damage, ammunition/reload, and MAP | Production |
| Spellcasting, spell attacks, resources, effects, restoration, and presentation | Production for implemented spells |
| Rage bindings, action, effect state, and Unity enrollment | Production |
| Light effect presentation | Production adapter |
| Slowed and Rotting Aura turn-start semantics | Transitional Unity-backed adapters |
| Hypothetical rules formerly used as architecture examples | Not contracts and not implied to be implemented |

This table describes ownership and integration, not PF2e content completeness. An action being on the
runtime does not mean every trait, feat interaction, or rules option for that action exists.

## Recipe: add or migrate a vertical feature

1. Trace the current behavior from input through Unity, rules calculations, state mutation, and
   presentation. Identify the exact state authority being replaced.
2. Decide whether new persistent state is necessary. Prefer a selector over existing state, an
   operation-local value, or a feature-local adapter before adding a shared DTO or state slice.
3. Put named rule semantics in a cohesive feature module: operations, outcomes, validators,
   handlers, middleware/listeners, selectors, state, and Unity adapters.
4. Reuse shared operations for shared work. Do not reimplement health, checks, modifier collection,
   movement, resources, or active-effect lifecycle inside the feature.
5. Define action profiles, typed catalog entries, and every feature-used `RuleDefinitionId` in the
   explicit composition root before dispatcher construction.
6. Add the feature once to `UnityEncounterModuleSet` and implement only the capability interfaces it
   needs. Preserve deterministic module order.
7. If the feature owns combatant state or installed actions, contribute complete registration state
   through `IUnityCombatantEnrollmentModule` so the same addition path supports initial and later
   batches.
8. Transfer encounter-scoped observers/resources to the supplied `CompositeLifetime`. Keep
   root-scoped registrations local.
9. Project only committed Facts. Register begun and resolved action presentation through the typed
   Unity registry, let generic Fact projectors own target state/reactions, and keep feature
   presentation out of shared bridge classes.
10. Remove the old writer and fallback in the same change. Do not leave dual authority for the
    migrated slice.
11. Add deterministic EditMode tests for rules behavior and bridge composition. Add PlayMode
    coverage when component attachment, scene lifecycle, input, animation, or presentation matters.

### Rules-backed action

- Use an `ActionOp<TResult>` and a feature-owned handler.
- Supply its `ActionProfile` through the relevant typed catalog.
- Put legality checks in action validators or nested shared operations.
- Let the engine commit all costs before the handler.
- Return the feature's structural outcome; do not throw for an ordinary illegal choice.
- Register Unity presentation through `IUnityActionPresenter<TOp, TResult>` in the feature module.
  Queue feature visuals from the begun and resolved occurrences using their exact action/outcome;
  do not replay generic health, hit, or defeat presentation from outcome targets.

### Rule responding to committed state

- Emit or reuse a Fact from the reducer that owns the transition.
- Use a feature-owned Fact listener when the response creates more rules work.
- Use a synchronous observer when the response is immediate projection or queued external work.
- Add middleware only when the rule must affect the selected operation before it commits.

### Migrating a state slice

- Seed or register the initial authoritative value through enrollment.
- Route all reads through `RulesSnapshot` or an exact bridge projection.
- Make one reducer family the only writer.
- Project committed changes back to Unity where needed.
- Delete or disable the former writer; do not reconcile two live values.

## Complexity guardrails for current production machinery

Several classes exist because the Unity integration has concrete rollback, ownership, or
presentation requirements. Keep them narrowly scoped:

- `UnityCombatantEnrollmentPlan`, complete registrations, and `RegistrationToken` make multi-object
  preparation reversible while preserving committed additions. Do not mirror that object graph in
  pure rules features.
- Identity reservations and exact detach checks protect Unity ownership. Do not allocate new global
  IDs for ordinary immutable values.
- Root and causal-tree settlement support post-commit listener work and encounter presentation. Do
  not make every outcome settlement-aware.
- Restored spell-effect extraction and projection belong to spell restoration. Do not require its
  Unity adapter records for effects created normally by rules operations.
- `UnityCombatRulesBridge` still contains Stride-specific fields and helpers from the first migrated
  slice. They are a transitional exception, not a template for more feature methods.
- The complete state carried by `AddCombatantsOp` is an inventory of current implementation, not a
  checklist that every feature must populate.

When a feature appears to need new horizontal infrastructure, document the current use case and the
smaller feature-local option in the change. The burden is on the shared abstraction, not on the
feature to predict future reuse.

## Prohibited patterns

- Static discovery, self-registration, or Unity lifecycle order as rules composition.
- Feature-named flags, caches, callbacks, or helper methods in shared runtime, bridge, manager, or
  facade types.
- Direct `RulesState` mutation outside reducers.
- Unity components or mutable scene objects inside operations or state.
- A second writable copy of migrated state.
- Encounter-scoped registrations that are not owned by the encounter lifetime.
- A combatant-addition contribution that handles only initial participants or only reinforcements.
- Compatibility layers or schema dispatch for unshipped formats.
- New DTOs or state added solely to support an unimplemented example or theoretical edge case.

## Tests that define the contract

Start with the narrowest relevant suite:

- [`DispatcherTests.cs`](../Assets/Tests/EditMode/RulesRuntime/DispatcherTests.cs): dispatch,
  middleware, Facts, observers, nested work, and settlement.
- [`ActionLifecycleTests.cs`](../Assets/Tests/EditMode/RulesRuntime/ActionLifecycleTests.cs):
  validation, atomic costs, `ActionBegunOp`, and handler order.
- [`EncounterRuntimeTests.cs`](../Assets/Tests/EditMode/RulesRuntime/EncounterRuntimeTests.cs):
  encounter state and turn progression.
- [`ActiveEffectLifecycleTests.cs`](../Assets/Tests/EditMode/RulesRuntime/ActiveEffectLifecycleTests.cs):
  definitions, bindings, effect state, and timing.
- [`MovementPathRuleTests.cs`](../Assets/Tests/EditMode/RulesRuntime/MovementPathRuleTests.cs) and
  [`StrideRulesTests.cs`](../Assets/Tests/EditMode/RulesRuntime/StrideRulesTests.cs): movement and
  Stride.
- [`StrikeRulesTests.cs`](../Assets/Tests/EditMode/RulesRuntime/StrikeRulesTests.cs),
  [`CastSpellRulesTests.cs`](../Assets/Tests/EditMode/RulesRuntime/CastSpellRulesTests.cs), and
  [`RageRulesTests.cs`](../Assets/Tests/EditMode/RulesRuntime/RageRulesTests.cs): current feature
  semantics.
- [`UnityCombatRulesBridgeTests.cs`](../Assets/Tests/EditMode/UnityCombatRulesBridgeTests.cs):
  composition, enrollment, rollback, ownership, topology, projection, and release.
- [`RulesStrikeUnityTests.cs`](../Assets/Tests/EditMode/RulesStrikeUnityTests.cs) and
  [`RulesRageUnityTests.cs`](../Assets/Tests/EditMode/RulesRageUnityTests.cs): feature-to-Unity
  authority boundaries.
- [`RulesStrikeIntegrationPlayModeTests.cs`](../Assets/Tests/PlayMode/RulesStrikeIntegrationPlayModeTests.cs)
  and [`SpellcastingPresentationPlayModeTests.cs`](../Assets/Tests/PlayMode/SpellcastingPresentationPlayModeTests.cs):
  production presentation integration.

Keep tests deterministic by injecting `ScriptedRollService` or otherwise saving and restoring Unity
random state. Add a regression test at the lowest layer that owns the changed contract.
