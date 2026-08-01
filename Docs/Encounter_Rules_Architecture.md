# Authoritative Encounter Rules Architecture

This is the canonical as-built guide for encounter rules in the Unity `6000.2.1f1` project. Use it
when changing encounter lifecycle, combatant enrollment, a rules-backed action, or a feature module.
The longer [operations-based rules architecture](Ops_Based_Rules_Proposal.md) explains the design
model and contains conceptual examples; this guide records what production code does now.

The rules migration is intentionally incomplete. The claims below apply to encounter state and the
named migrated action slices, not to every gameplay system or every conceptual example in the
proposal.

## Authority after encounter cutover

`UnityCombatRulesBridge` owns one `RuleDispatcher` and its `RulesState` for an encounter. Once a
controller and creature are attached to that exact bridge, the following state is authoritative in
`RulesSnapshot`:

| Slice | Authoritative rules state and boundary |
| --- | --- |
| Turn ownership | `EncounterState.CurrentTurn` and exact `TurnIdentity`; `ActionController.HasTurnAuthority` is a read projection. |
| Actions and reactions | `ActionEconomyState`; `ActionController.ActionPoints` and `Reacted` project it. |
| Multiple attack penalty | `MultipleAttackPenaltyState`; `ActionController.StrikePenalty` projects its attack count. |
| Health | `HealthState`; `CreatureComponent.Health`, `hp`, `maxHp`, and `tempHp` read it while attached. Health Facts project committed values and presentation back to Unity. |
| Position and movement | `RulesSnapshot.Positions`, movement budgets, permissions, and movement reducers. Token movement is a committed-Fact projection. |
| Encounter roster, initiative, round, and outcome | `EncounterState`, its roster and cursor, and encounter reducers/listeners. `CombatManager` orchestrates and presents this state; it is not a second encounter scheduler. |
| Active-effect timing | `ActiveEffectInstance` and `ActiveEffectTimingState`, advanced at encounter initiative boundaries. |
| Migrated action slices | Stride, Strike, Reload, Rage, and supported Cast a Spell variants use rules operations, validation, action lifecycle, reducers, and state. |

Cutover never means “try rules, then fall back.” A detached `ActionController` exposes deliberately
unavailable read projections (`HasTurnAuthority == false`, zero action points, `Reacted == false`,
and zero MAP). A rules-backed action reports unavailable when `TryGetCombatRules` fails. Operations
that require authority, such as positive action spending or health mutation, throw when the bridge
is absent or structurally incomplete. `CreatureComponent.Health` may still read serialized or
initialized health before attachment and after final projection; that is an initialization and
persistence boundary, not competing combat authority.

Legacy-named `CombatManager` entry points still exist for scene compatibility, and many gameplay
features remain Unity-native. They must not dual-write a migrated slice or revive a legacy fallback.

## Production composition

[`UnityEncounterModuleSet`](../Assets/Scripts/Rules/Unity/Composition/UnityEncounterModuleSet.cs)
constructs the only production module sequence:

1. `RottingAuraEncounterModule`
2. `SlowedEncounterModule`
3. `UnityRageEncounterModule`
4. `UnityStrikeEncounterModule`
5. `UnitySpellcastingEncounterModule`
6. `UnityLightEncounterModule`
7. `UnityHealthProjectionModule`
8. `UnityEncounterProjectionModule`

[`UnityEncounterComposition`](../Assets/Scripts/Rules/Unity/Composition/UnityEncounterComposition.cs)
copies that explicit sequence and never scans assemblies, scene objects, statics, or attributes.
Each pass filters the same sequence by capability, preserving supplied order:

| Capability | Responsibility |
| --- | --- |
| `IUnityEncounterDispatcherModule.ConfigureDispatcher` | Register feature-owned handlers, reducers, validators, and engine composition with `RuleDispatcherBuilder`. |
| `IUnityEncounterTurnStartModule.CreateTurnStartAdapter` | Supply a transitional `IEncounterTurnStartAdapter`. Adapters run sequentially in module order. |
| `IUnityEncounterRuntimeModule.RegisterRuntime` | Register observers and other encounter-scoped resources into the supplied `CompositeLifetime`. |
| `IUnityEncounterTopologyModule.RefreshTopology` | Refresh a feature-owned Unity topology adapter after a live grid change. |
| `IUnityCombatantEnrollmentModule.PrepareCombatant` | Precompute state and Unity installation contributions for every enrolled combatant. |

Implement only the capabilities a module needs. Ordering dependencies must be visible in the module
list or expressed through rules lifecycle phases and causal operations; do not invent priorities or
registration side effects.

## Construction responsibilities and order

`UnityCombatRulesBridge.Create` performs these boundaries in order:

1. Create the mutable topology provider, shared feature contexts, catalogs, registry, explicit
   module set, composition, and enrollment pipeline.
2. Call `UnityCombatantEnrollmentPipeline.Prepare` for all initial participants.
3. Call `UnityCombatantEnrollmentPlan.SeedInitial` into one `RulesStateSeed`.
4. Configure shared runtimes on `RuleDispatcherBuilder`: health, MAP, checks, active effects,
   encounter rules, action lifecycle, movement, and Stride.
5. Invoke `ConfigureDispatcher` for feature modules in module order, build the dispatcher, then
   invoke `RegisterRuntime` in module order.
6. Call `AttachAndInstall`.
7. Transfer the prepared plan to the encounter's single `CompositeLifetime`.

`AttachAndInstall` is deliberately after authoritative state and runtime observers exist. For each
combatant it attaches health authority first, attaches `ActionController` combat authority second,
and applies already prepared feature installations last, in module contribution order. Do not move
Unity attachment or action-list mutation into preparation.

### One cleanup boundary

`UnityCombatRulesBridge` owns exactly one encounter-level `CompositeLifetime`. Runtime observer
registrations and every successfully transferred enrollment plan belong to it. `ReleaseOwnership`
disposes it once in reverse registration order, attempts all cleanup even after failures, and only
then runs release callbacks. Modules must add disposable registrations and resources to the lifetime
they receive; they must not keep a second encounter cleanup list or manually unregister observers.

`ActionController.DetachCombatRules` and `CreatureComponent.DetachHealthRules` use
`ReferenceEquals` against the owning `UnityCombatRulesBridge`. Delayed cleanup from an older
encounter therefore cannot detach a newer bridge or overwrite its health projection. Preserve this
exact identity check at every ownership-release boundary.

## Combatant enrollment

[`UnityCombatantEnrollmentPipeline`](../Assets/Scripts/Rules/Unity/Composition/UnityCombatantEnrollmentPipeline.cs)
is the one path for constructor-time participants and later reinforcements.

### Reversible preparation

`Prepare` validates the complete controller batch, reserves creature/player identity allocation,
creates Unity-to-rules maps provisionally, validates future attachments, invokes every enrollment
module, captures initiative modifiers, and freezes `CombatantRulesState` plus installation plans.
`UnityCombatantEnrollmentBuilder` exposes the supported contribution APIs:

- `Own<TResource>` for reversible preparation resources;
- `AddState(IUnityCombatantStateContribution)` for state that supports both initial seeding and
  reinforcement registration;
- `AddSpellSlots` and `AddRuleBindings` for atomic base combatant state;
- `AddInstallation(IUnityCombatantInstallationContribution)` for precomputed Unity changes.

If any preparation read or preflight fails, the preparation `CompositeLifetime` rolls back maps,
identity allocation, and feature-owned resources. Cleanup failures are retained with the original
failure. Preparation must therefore perform every fallible Unity query needed by `Apply`.

This is not a promise that arbitrary work after a reinforcement commit can roll back `RulesState`.
The commit boundary is intentional: installation contributions must be deterministic applications
of precomputed data and must not repeat fallible discovery or validation.

### Initial participants versus reinforcements

Both routes call the same `PrepareCombatant` methods in the same module order and build the same
`CombatantRulesState`.

- Initial participants call `SeedInitial`. Base creature, health, position, land speed, action
  economy, MAP, spell slots, bindings, and feature contributions enter the seed before the
  dispatcher exists. Initiative is rolled later by `StartEncounterOp`.
- Reinforcements call `CommitReinforcements`. One `JoinEncounterOp` atomically adds each prepared
  `CombatantRulesState` (including spell slots and bindings) to the active encounter, rolls
  initiative, and assigns `EligibleFromRound` so a higher-than-current result waits until the next
  round. Additional `IUnityCombatantStateContribution` objects then run their rules-owned
  registration workflows.

After either state path, call `AttachAndInstall`, then `TransferTo(encounterLifetime)`. A new feature
must support both paths; never assume all participants existed at encounter construction.

### Restored-effect adoption

`UnitySpellcastingEncounterModule` is the production example of state that crosses both enrollment
routes. During preparation it converts supported `SpellEffectController` entries into
`RestoredSpellEffectContribution` objects with stable `ActiveEffectId` and `BindingId` values.
Initial participants seed the effect and binding directly. Reinforcements dispatch the
feature-owned `AdoptRestoredSpellEffectsOp`, whose handler composes `CreateActiveEffectOp` for each
registration. `RestoredSpellEffectTimingObserver` projects initiative-boundary counts and removes
expired or removed Unity effects. Do not bypass the active-effect runtime for restored effects.

## Dispatcher and encounter runtime

`RuleDispatcher` owns operation frames, external-root serialization, nested dispatch, causal
fact-listener roots, deterministic middleware/listener selection, Fact aggregation, and settlement
notifications. Handlers orchestrate; reducers are the only writers to `RulesStateDraft`; committed
Facts are the notification contract.

[`EncounterRuleRuntime`](../Assets/Scripts/Rules/Runtime/EncounterRuleRuntime.cs) installs the
encounter handlers and engine reducers. Its current division of responsibility is:

- `StartEncounterHandler`: roll initiative through `IRollService`, retain registration-order ties,
  commit the roster, publish initiative assignments, and trigger the first boundary causally.
- `JoinEncounterHandler`: validate an active turn, roll reinforcement initiative, commit full
  combatant states, and publish assignments from a later frame so new bindings can observe them.
- `AdvanceEncounterHandler`: settle pending expirations, outcomes, initiative boundaries, skipped
  or ineligible roster slots, and effect timing in deterministic order.
- `BeginInitiativeTurnHandler`: reset movement budget, run ordered turn-start adapters, stop if the
  actor is defeated, then commit the exact turn and final action contribution.
- `EndTurnHandler`: require the exact current `TurnIdentity`, run turn-end work, reset movement,
  clear turn resources through reducers, and advance.
- `EncounterOutcomeListener`: after reaction-phase zero-HP listeners settle, finalize defeat and
  evaluate encounter outcome.
- `SuspendEncounterHandler` and `EndEncounterHandler`: expire encounter-owned timed effects before
  committing suspension or outcome.
- Encounter reducers and the shared reducers they invoke atomically mutate roster, initiative
  boundary, current turn, actions, reactions, MAP, movement reset state, phase, and outcome while
  emitting committed Facts.

`ActionOp<TResult>` uses the engine-owned lifecycle implemented by `RuleDispatcher`: freeze the
effective profile, validate, commit all costs atomically, dispatch `ActionBegunOp`, stop on
disruption, and only then invoke feature middleware and the handler. Feature code must not spend the
same costs or publish a parallel action-begun event.

## Projection, settlement, and topology

`UnityEncounterProjectionModule` observes `EncounterStartedFact`, `TurnBeganFact`, `TurnEndedFact`,
and `EncounterOutcomeCommittedFact`. Turn and outcome callbacks are queued by `RootOpId`, not raised
immediately. Its one settlement observer implements both `IRootSettlementObserver` and
`ICausalTreeSettlementObserver`: root settlement records exact causal-parent links, and causal-tree
settlement drains the root followed by children in recorded order. This ensures turn/outcome Unity
presentation occurs only after fact-listener roots such as defeat finalization, healing reactions,
and encounter outcome evaluation have settled. A duplicate settled root or an unsettled recorded
child root is an invariant failure.

Other projections use the narrowest contract:

- `IFactObserver<TFact>` for committed transitions and their current snapshot;
- `IResolvedOpObserver<TOp,TResult>` only to pace external presentation from a resolved calculation
  before its parent continues; it is not proof of mutation and has no dispatch authority;
- `IRuleFactListener<TFact>` or `IRuleFactBatchListener<TFact>` for binding-selected rules behavior
  that may causally dispatch more operations.

`UnityCombatRulesBridge.RefreshTopology` first builds and installs a new immutable `GridTopology`,
then calls every `IUnityEncounterTopologyModule` in module order and updates `CurrentTiles`.
`MutableGridTopologyProvider` rejects replacement while a rules resolution is active, so one root
sees one topology. Current Strike and spell-attack Unity contexts also refresh their feature-owned
tile adapters through this capability. Refresh topology after any live grid mutation and before the
next rules root.

## Recipe: add or migrate a vertical feature

Follow these steps in order. Use the existing production examples instead of adding a parallel
framework.

1. **Define the rules-owned slice.** Put immutable Ops, results, validators, handlers, reducers,
   Facts, selectors, and typed effect state with the feature. Use `ActionOp<TResult>` for a PF2e
   action and ordinary `IRuleOp<TResult>` for nested work. `StrideRules`, `StrikeRules`,
   `RageRules`, and `SpellcastingRules` show current contracts. For a new `ActionOp<TResult>` with a
   default profile, make its definition an `IActionCatalog` and include that definition in the
   production `CombatActionCatalog` constructed by `UnityEncounterModuleSet.Create` before
   `UnityCombatRulesBridge` passes `modules.ActionCatalog` to `UseActionLifecycle`. Rage is the
   current example: `RageActionOp` names `RageActionDefinition.DefinitionId`,
   `RageActionDefinition` implements `IActionCatalog`, and `rageDefinition` is supplied as a
   `CombatActionCatalog` feature catalog.
2. **Register dispatcher behavior explicitly.** Implement
   `IUnityEncounterDispatcherModule.ConfigureDispatcher(RuleDispatcherBuilder)` and call existing
   `Use...Rules` extensions or exact `RegisterHandler`, `RegisterReducer`, and
   `RegisterActionValidator` APIs. `UnityStrikeEncounterModule` and
   `UnitySpellcastingEncounterModule` are production examples.
3. **Model per-combatant enrollment for both routes.** Implement
   `IUnityCombatantEnrollmentModule.PrepareCombatant(UnityCombatantEnrollmentBuilder)`. Add base
   slots/bindings directly, or implement `IUnityCombatantStateContribution.Seed` and `Register`
   when feature state needs different seed and operation-based adoption mechanics. Use `Own` for
   every provisional disposable. Before seeding or adopting any `ActiveRuleBinding` or
   `ActiveEffectInstance` that uses a new `RuleDefinitionId`, define that ID on the production
   `RuleRegistryBuilder` before `registryBuilder.Build()`. Current composition calls
   `RageRules.DefineRuleBindings(registryBuilder)` for Rage's definitions and explicitly defines
   `UnitySpellcastingEncounterModule.RestoredTimedEffectDefinitionId` for the matching restored
   effect and binding.
4. **Precompute Unity installation.** If the feature changes action lists or adapters, return an
   `IUnityCombatantInstallationContribution` from preparation. Its `Apply` method may only apply
   frozen work. `UnityStrikeActionInstallationPlan` and `UnitySpellActionInstallationPlan` are the
   examples.
5. **Register presentation with encounter ownership.** Implement
   `IUnityEncounterRuntimeModule.RegisterRuntime(RuleDispatcher, CompositeLifetime)` and add every
   registration/disposable to that lifetime. Strike uses resolved-operation observers for attack
   pacing; health and Light use committed Fact observers.
6. **Add topology or turn-start capabilities only if required.** Implement
   `IUnityEncounterTopologyModule` for a live geometry adapter. Use
   `IUnityEncounterTurnStartModule` only for a transitional Unity-owned calculation that cannot yet
   be a rules feature; Rotting Aura and Slowed are current seams, not templates for new rules.
7. **Add the module once to `UnityEncounterModuleSet.Create`.** Its array position is its position
   in every applicable composition pass. Do not self-register.
8. **Test the vertical boundary.** Add deterministic EditMode tests for reducers, handlers,
   lifecycle, initial seed, reinforcement registration, failure rollback, ordering, exact identity,
   and cleanup. Add PlayMode coverage only for scene/FSM/presentation behavior. Verify unavailable
   projections and required-operation failures without a bridge.
9. **Update this guide only if the shared architecture changed.** Feature-specific behavior belongs
   with its feature documentation and tests; do not duplicate the conceptual proposal here.

## Anti-patterns

- No legacy fallback, dual reads, dual writes, or “rules when available” mutation for a migrated
  slice.
- No static discovery, singleton lookup, static event registration, scene scanning, or feature
  self-registration during composition.
- No manual observer cleanup outside the provided encounter `CompositeLifetime`.
- No feature-named conditions, caches, trigger flags, dispatch helpers, or workflow semantics in
  `UnityCombatRulesBridge`, `RuleDispatcher`, shared managers, or generic projection modules.
- No feature that handles only initial participants. Reinforcements must receive equivalent state,
  bindings, installations, topology behavior, and lifetime ownership.
- No Unity object, callback, or mutable collection in a rules Op; translate to stable IDs and plain
  data at the adapter boundary.
- No direct state mutation from handlers, middleware, listeners, observers, installers, or Unity
  components. Dispatch an Op and let a reducer commit.
- No treating `IResolvedOpObserver` as a Fact or granting it dispatch authority.
- No topology refresh during a root and no cached topology that ignores
  `IUnityEncounterTopologyModule.RefreshTopology`.

## Current transitional seams

The encounter is authoritative without being fully rules-native. Keep these seams explicit and
shrink them through vertical migrations:

- `RottingAuraEncounterModule` calls the Unity aura resolver at `TurnStartingOp` timing, while final
  damage still commits through encounter rules.
- `SlowedEncounterModule` obtains the current action contribution through
  `ActionController.CalculateTurnStartActions` and legacy `ResetActionPointsEvent` listeners.
- `UnityStrikeContext` and `UnitySpellAttackContext` adapt current creature/equipment/team/grid data
  into rule definitions and validation. They are feature-owned adapters, not alternate authorities.
- Encounter preparation still reads serialized `CreatureComponent`, `ActionController`, `Team`,
  prepared-character, spellbook, and restored-effect data to build authoritative state.
- Unity action classes, selection coroutines, AI controllers, animation, combat logs, HUD, and scene
  transforms remain input/presentation adapters. Supported spell and Strike installers reconcile
  action lists at attachment.
- `CreateExplorationStride` creates a temporary, unattached rules composition for movement outside
  initiative and projects its committed boundary position before encounter composition begins.
- Other actions, conditions, feats, spells, equipment behaviors, exploration systems, and scene
  flows not named as migrated above can still be Unity-native. Migrate them one vertical slice at a
  time; do not describe the whole game as rules-native.

## Tests that define the contract

Start with these suites when changing the architecture:

- [`UnityCombatRulesBridgeTests`](../Assets/Tests/EditMode/UnityCombatRulesBridgeTests.cs): module
  order, shared enrollment, restored effects, rollback, exact attachment, cleanup, health, and
  movement projection.
- [`EncounterRuntimeTests`](../Assets/Tests/EditMode/RulesRuntime/EncounterRuntimeTests.cs): roster,
  initiative, reinforcement eligibility, turn lifecycle, outcome, causal reactions, and effect
  timing.
- [`DispatcherTests`](../Assets/Tests/EditMode/RulesRuntime/DispatcherTests.cs): structural results,
  `CompositeLifetime`, serialized roots, nested ownership, Facts, and settlement.
- [`RulesStrikeUnityTests`](../Assets/Tests/EditMode/RulesStrikeUnityTests.cs),
  [`RulesRageUnityTests`](../Assets/Tests/EditMode/RulesRageUnityTests.cs), and
  [`RulesStrikeIntegrationPlayModeTests`](../Assets/Tests/PlayMode/RulesStrikeIntegrationPlayModeTests.cs):
  current production feature-module and projection examples.
