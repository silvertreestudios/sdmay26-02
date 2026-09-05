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
the state slices seeded or registered there. Unity objects provide initial data and then become
clients or projections of the migrated state.

| Concern | Production authority |
| --- | --- |
| Encounter phase, roster, initiative, round, and current turn | `EncounterState` in `RulesState` |
| Encounter action economy | `ActionEconomyState`; controller values are read projections |
| Health, temporary Hit Points, and defeat | `HealthState`; `CreatureComponent` receives committed projections |
| Position, land Speed, and movement budget | Runtime movement slices; Unity transforms project committed movement |
| Multiple attack penalty | `MultipleAttackPenaltyState` |
| Strike equipment, ammunition, and loaded state | Runtime equipment/ammunition slices prepared by the Strike module |
| Spell slots, Focus Points, active effects, bindings, and timing | Runtime resource/effect slices prepared by feature modules |
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
supplied explicitly; they do not discover or register themselves.

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

1. Create the immutable topology provider and Stride definition.
2. Build `UnityEncounterModuleSet`, including catalogs, rule definitions, and ordered modules.
3. Create `UnityCombatantEnrollmentPipeline` and prepare all initial combatants reversibly.
4. Seed base and feature-contributed state into `RulesStateSeed`.
5. Configure engine capabilities in this order:
   `UseHealthRules`, `UseMultipleAttackPenaltyRules`, `UseCheckResolution`,
   `UseActiveEffectRules`, `UseEncounterRules`, `UseActionLifecycle`, `UseMovementRules`, and
   `UseStrideRules`.
6. Compose feature-owned typed action presenters into `UnityActionPresentationRegistry` and ask
   dispatcher-capable feature modules to configure the builder, both in module order.
7. Build the dispatcher.
8. Register runtime modules, including the single action-presentation Fact observer, into the
   encounter `CompositeLifetime`.
9. Attach exact Unity authority and apply prepared feature installations.
10. Transfer the prepared enrollment lifetime to the encounter lifetime.

State, dispatcher registrations, and runtime observers therefore exist before Unity components are
allowed to route reads or actions through the bridge. A failure before transfer disposes the
preparation lifetime and rolls back provisional mappings and installations.

## Combatant enrollment

Initial participants and reinforcements use the same preparation path. The pipeline validates the
batch, allocates identities, captures required Unity data, installs provisional lookup maps, and
asks enrollment-capable modules for typed contributions. It moves known fallible reads and
preparation ahead of the authoritative commit, but the current Unity installation phase can still
fail afterward.

`UnityCombatantEnrollmentBuilder` collects only current production needs:

- base creature, health, position, and land Speed;
- spell-slot and rule-binding seeds;
- other typed state contributions owned by feature modules; and
- delayed Unity installations.

Do not expand that builder with a new feature-named field. Add a feature-owned contribution, or
avoid persisted state if the value can be derived.

### Initial participants

`SeedInitial` adds the prepared state before dispatcher construction. `StartEncounter` later builds
the initial roster and currently re-reads each creature's initiative modifier from Unity. The
modifier captured during preparation is not used for the initial roster.

This asymmetry is intentional documentation of current code, not recommended design. Do not assume
initial initiative is frozen at bridge construction unless the implementation is changed.

### Reinforcements

`RegisterCombatants` prepares the complete batch, dispatches one `JoinEncounterOp`, registers
feature contributions, attaches Unity authority, applies installations, and transfers ownership.
Reinforcements use the initiative modifier captured during preparation.

If preparation fails, the enrollment lifetime reverses provisional identities, lookup maps, and
feature resources. If `JoinEncounterOp` or later installation fails, the plan still disposes its
Unity-side resources and preserves both primary and cleanup failures when necessary.

Current limitation: disposal does not reverse an already committed `JoinEncounterOp` or earlier
feature registration operations. A failure after that point can therefore leave authoritative
reinforcement state in the store without its Unity attachment. Do not describe the current path as
a cross-store transaction. Prefer moving fallible work before commit; add a broader transaction or
compensation model only if a demonstrated failure mode requires one.

Enrollment must be tested for both initial participants and reinforcements; supporting only
constructor-time seeding is incomplete.

### Restored effects

The spellcasting module currently adopts restored finite spell effects during enrollment. The
supporting projection records, contribution object, timing observer, and adoption operation are a
feature-owned solution to a real restoration boundary. They are not a generic requirement that
every active effect needs a restoration DTO or adoption workflow.

## Dispatch, action, and Fact timing

The bridge exposes a synchronous Unity boundary. `Dispatch` returns structural operation results;
internal convenience paths require resolution and translate invalid requests to
`InvalidOperationException`. A Unity-originated synchronous request rejects unresolved asynchronous
callback work.

Within the dispatcher:

- one root owns its operation frames and nested dispatches;
- an action is validated, pays its complete costs atomically, resolves `ActionBegunOp`, and then
  publishes one `ActionBegunFact<TResult>` immediately before invoking its feature handler;
- reducers atomically commit state and return immutable state-change Fact payloads;
- the dispatcher records source, root, exact-snapshot, and listener-delivery provenance internally
  without mutating those payloads;
- the action lifecycle publishes one `ActionResolvedFact<TResult>` directly after a resolved
  action's handler and awaited children complete, against the unchanged committed snapshot;
- synchronous external Fact observers receive each Fact's exact associated snapshot, isolate and
  best-effort trace failures, and cannot fail or interrupt mechanics;
- asynchronous binding-scoped Fact listeners run from committed Facts and may create causal
  follow-up roots; and
- settlement observers report when roots and their causal trees finish.

For Strike, the production handler resolves the attack first, then dispatches damage, loaded-state
changes, and multiple attack penalty work. Presentation observes the parent Strike's
`ActionResolvedFact<StrikeResolution>` only after that complete workflow. Spell presentation uses
the cast's existing `CastSpellOutcome`, including its actual `SpellAttackResolution` collection;
it does not observe nested attack operations or recalculate outcomes.

`UnityActionPresentationRegistry` is the single generic Unity routing boundary. Strike and
spellcasting explicitly register typed presenters through their encounter modules, keyed by stable
`ActionDefinitionId`. The registry verifies the concrete action/outcome pair before invoking the
feature presenter. Cast a Spell may then route within its presenter by the selected
`SpellReference`. There is no static discovery or central feature switch.

The shared observer opens one encounter-owned presentation sequence for the exact immutable action
when its begun occurrence arrives. Typed beginning presentation, committed hit/defeat reactions,
and typed resolved presentation append coroutine steps to that sequence in observer order. The
Strike and Cast a Spell Unity coroutines drain that exact action after synchronous dispatch before
unlocking input. The coordinator logs the first presenter execution failure, abandons the remaining
steps for that action, and releases both its exact-action and root mappings. Invalid, interrupted,
cancelled, unpresented, and failed-presentation paths therefore do not retain queue entries.

Strike and spell presenters own attacker animation plus action summary, log, and miss presentation;
they do not re-select targets, recalculate attacks or damage, or replay target reactions from their
outcomes. On every committed `HealthFact`, `UnityHealthProjectionModule` immediately projects the
exact associated `HealthState` snapshot into `CreatureComponent`; the HUD reads the component's
authoritative `Health`. Hit and defeat reactions come from the actual committed `HealthFact` and
`CreatureDefeatCommittedFact`: they join an active action sequence in Fact order, or present
immediately when no action sequence owns their root.

### Encounter presentation settlement

`UnityEncounterProjectionModule` observes encounter Facts. Start is projected immediately; turn
begin, turn end, and encounter outcome callbacks are queued by root and drained when the causal tree
settles. This prevents visible encounter boundaries from running ahead of rules work caused by the
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
7. If the feature owns combatant state or installed actions, support both initial seeding and
   reinforcement enrollment through `IUnityCombatantEnrollmentModule`.
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
- Add Unity presentation by registering an `IUnityActionPresenter<TOp, TResult>` from the feature's
  encounter module. Queue attacker/feature presentation from the begun and resolved occurrences;
  reuse their exact action and the resolved occurrence's existing outcome. Do not loop over outcome
  targets to replay generic health, hit, or defeat presentation.

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

- `UnityCombatantEnrollmentPlan`, contribution objects, and `RegistrationToken` make multi-object
  enrollment reversible. Do not mirror that object graph in pure rules features.
- Identity reservations and exact detach checks protect Unity ownership. Do not allocate new global
  IDs for ordinary immutable values.
- Root and causal-tree settlement support post-commit listener work and encounter presentation. Do
  not make every outcome settlement-aware.
- Restored spell-effect adoption belongs to spell restoration. Do not require adoption operations
  for effects created normally by rules operations.
- `UnityCombatRulesBridge` still contains Stride-specific fields and helpers from the first migrated
  slice. They are a transitional exception, not a template for more feature methods.
- The large set of state slices in `RulesStateSeed` is an inventory of current implementation, not a
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
- Initial-only enrollment logic that fails for reinforcements.
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
