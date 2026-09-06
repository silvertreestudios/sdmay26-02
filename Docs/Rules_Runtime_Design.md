# Rules Runtime Design

This document defines the intended design of the operations-based rules runtime. It explains the
small set of architectural constraints that should remain true as features change. It is not an
inventory of production classes, a migration tracker, or a catalogue of possible future rules.

For the current code map, Unity composition order, migrated state, transitional seams, and
step-by-step recipes, use the [encounter rules implementation guide](Encounter_Rules_Architecture.md).
When documentation and production code disagree about what is currently implemented, the code is
the source of truth and the implementation guide should be corrected.

## Goals

The runtime exists to make rules behavior:

- deterministic and testable without Unity objects;
- explicit about requests, results, state changes, and timing;
- composable across actions, effects, conditions, and reactions;
- safe from competing authorities for the same state; and
- extendable by feature-owned modules instead of feature switches in shared infrastructure.

The runtime does not try to model every Pathfinder rule in advance. It does not require every
gameplay system to migrate at once, and it does not make Unity scene objects part of the rules
model.

## Complexity budget

The runtime should contain the least machinery needed by implemented vertical slices. A possible
future rule is not enough justification for a new shared DTO, state slice, lifecycle phase, or
extension point.

Use these tests before expanding the shared runtime:

- Store a value only when it is an authoritative game fact that must survive across operations or
  be queried deterministically. Derive values that can be computed from existing state.
- Create a DTO when immutable data must cross a real boundary. Do not add request, context, and
  outcome wrappers that merely copy the same fields through adjacent calls.
- Add a stable identifier when identity must survive a boundary or lifetime. Do not replace a
  feature-local value with global identity infrastructure for hypothetical replay or persistence.
- Add a shared capability only when a current feature needs it and the capability can be named
  without feature terminology. Prefer a second proven use before generalizing a local mechanism.
- Keep uncommon edge-case handling in the owning feature until another implemented feature proves
  the behavior is genuinely horizontal.
- Prefer a coordinated breaking change over compatibility state, schema versions, or fallback
  paths for formats that have not shipped.
- Remove obsolete state and adapters when authority moves. Do not synchronize two writable models.

Production mechanisms such as causal-tree settlement, reversible Unity preparation, restored-effect
extraction, and presentation queues solve current integration requirements. Their existence does not
make them mandatory patterns for every feature, and new code should not copy their supporting state
unless it has the same demonstrated requirement.

## Mental model

A rules interaction flows through a small number of roles:

```text
Unity or another client
    -> feature-owned selection/adaptation
    -> immutable typed Op
    -> dispatcher
         -> validation and action costs when applicable
         -> feature handler orchestration
         -> nested Ops and reducer commits
         -> committed Facts
         -> rule listeners and external observers
    -> structural result
```

The operation describes what is being attempted. The rules snapshot describes what is true. The
result describes how the attempt ended. Facts describe committed state changes or narrow committed
rules occurrences. Unity projects those outcomes; it does not silently decide them for a migrated
state slice.

## Core contracts

### Operations and results

An `IRuleOp<TResult>` is an immutable, typed request. It carries the data needed to resolve one
rules question or state transition. An operation should not contain Unity objects, callbacks, or
mutable service state.

`OpResult<TResult>` has structural terminal states: resolved, invalid, interrupted, or cancelled.
Expected rules outcomes use those result forms. Exceptions are reserved for programmer errors,
broken composition, and failed external infrastructure.

An action is an operation with action metadata and a mandatory action lifecycle. Supporting rules
work—checks, damage, movement steps, resource changes, and effect changes—uses ordinary nested
operations.

### One authoritative state

`RulesState` is the sole writable authority for each migrated state slice. Clients receive a
read-only `RulesSnapshot`. Unity components may cache or display a projection, but they must not be
a second writer.

State changes occur through reducers. A reducer:

1. reads the current snapshot and the operation;
2. validates the requested transition;
3. mutates only its draft state;
4. emits Facts describing the committed change; and
5. returns the operation outcome.

Handlers, middleware, listeners, and observers do not mutate state directly.

### Facts and timing

A `RuleFact` is an immutable domain payload reporting something that has committed. It carries no
store-assigned identity, mutable commit marker, or dispatcher provenance. Most Facts are emitted by
reducers for state changes. The action lifecycle runner also emits one
`ActionBegunFact<TResult>` occurrence after validation, atomic costs, and the action-begun timing
window succeed, immediately before the feature handler, plus one `ActionResolvedFact<TResult>`
occurrence after a structurally resolved action and all of its awaited child mechanics. Neither is
a request or preview. Both reuse the exact immutable action and its `ActionOpInfo`; the resolved
occurrence also carries the existing feature outcome. Fact listeners may react by dispatching more
rules work. External observers may update presentation, audio, animation, or Unity projections.

The distinction matters:

- middleware participates while an operation is resolving;
- reducers commit authoritative state;
- Facts expose the committed transition or occurrence;
- binding-scoped rule listeners are asynchronous, awaited, and may create authoritative follow-up
  rules work; and
- external Fact observers are synchronous, non-authoritative notifications that project immediately
  or enqueue host-owned presentation.

The dispatcher keeps exact source/root provenance, listener eligibility, delivery ordering, and
each delivery's exact snapshot in internal orchestration records. External observers receive only
the immutable payload, an observation-root `OpId`, and the exact snapshot. An independent dispatch
or causally dispatched action uses its exact root as the observation root; supporting causal roots
inherit that transient correlation so host presentation can retain one sequence across the causal
tree without putting provenance in Fact payloads. Observer exceptions are isolated and logged
directly to `System.Diagnostics.Trace` on a best-effort basis; neither the observer nor logging
failure stops handlers, reducers, rules Fact listeners, or other observers. Presentation does not
have retry, pending, recovery, or durable rules state.

Code that needs to veto or alter a transition belongs before the commit. Code that merely responds
to the committed result belongs after it.

### Handlers, middleware, listeners, observers, and selectors

Use the narrowest extension point that matches the behavior:

| Need | Extension point |
| --- | --- |
| Orchestrate a multi-step feature workflow | `IOpHandler<TOp, TResult>` |
| Alter or interrupt a selected operation while it resolves | `IOpMiddleware<TOp, TResult>` |
| React to a committed Fact with more rules work | `IRuleFactListener<TFact>` or batch listener |
| Project committed work outside the rules model | synchronous Fact observer |
| Read a derived answer without changing state | selector over `RulesSnapshot` |
| Perform a small authoritative mutation | `IOpReducer<TOp, TResult>` |

Do not introduce a new extension-point category for a feature-specific workflow. If none of these
roles fits, first question whether the behavior belongs inside the runtime or inside the feature's
adapter.

### Action lifecycle

Every rules-backed action follows the same engine-owned boundary:

1. resolve the base `ActionProfile` and any legitimate profile changes;
2. run action validators against the snapshot;
3. commit all action and rule-resource costs atomically;
4. resolve `ActionBegunOp`;
5. publish exactly one `ActionBegunFact<TResult>` occurrence; and
6. invoke the feature handler.

After the handler and every awaited child mechanic complete, a structurally resolved action emits
exactly one `ActionResolvedFact<TResult>`. An action rejected or interrupted before feature
execution emits neither lifecycle occurrence; a feature-level outcome such as a missed Strike emits
both. Once the begun occurrence is published, the lifecycle requires structural resolution or
propagates an exceptional dispatcher failure. These Facts are not action history or a second result
model.

The feature handler owns the action's semantics after that boundary. It should dispatch existing
generic operations for shared work rather than reimplementing checks, damage, movement, resources,
or effects. Feature code must not spend action costs again.

### Active rules and effects

Static rule definitions describe resolvers and listeners. Active bindings state which definitions
currently participate for a source and owner. Active effect instances carry only state that truly
varies per application, such as a rolled value that cannot be derived.

Prefer a binding or effect that derives its contribution from the authoritative snapshot over
synchronized child effects or copied caches. If state is not queried after the current operation,
keep it in the operation workflow instead of persisting it.

### Determinism and ordering

Registration and execution order are explicit. Modules do not discover or register themselves.
Random rules outcomes use `IRollService`; deterministic tests use a scripted service. Collections
that affect rules outcomes have defined ordering rather than relying on object discovery, hash
iteration, or Unity lifecycle timing.

## Feature ownership

A rule, feat, spell, condition, or action is a vertical feature. Its module owns its:

- operations and outcomes;
- validation and handlers;
- middleware and Fact listeners;
- selectors and genuinely persistent feature state;
- action definitions or typed catalog entries; and
- Unity extraction, installation, and presentation adapters.

Shared runtime and bridge types expose feature-agnostic capabilities. They may know how to dispatch
an operation, commit health, collect modifiers, or observe a Fact; they must not know when Rage,
Divine Lance, Slowed, or another named feature applies. The explicit composition root may name a
feature to wire it.

This boundary prevents a common failure mode: each new feature adding one flag, callback, DTO, and
special case to a central manager until the manager becomes the real rules engine.

## Unity boundary

Unity owns scene references, input, visuals, animation, and component installation. Feature adapters
translate those objects into stable rules values before dispatch and project committed results back
afterward.

Synchronous external Fact observation may feed a host-owned ordered presentation sequence. The host
opens that sequence from `ActionBegunFact<TResult>`, records feature presentation and visual
reactions from committed Facts, then drains the exact immutable action after synchronous dispatch.
Observers enqueue and return immediately; the dispatcher never awaits Unity frames. The first
presenter execution failure is logged once, aborts the action's remaining presentation, and releases
its action/root correlation so the caller can unlock. Presentation has no retry or recovery state.

Authoritative health changes at reducer commit time. Each committed health Fact immediately projects
its exact snapshot into the Unity component, and HUD reads use that authoritative health. Only hit
and defeat reactions wait in an active action presentation sequence; without one, they present
immediately.

For a migrated slice:

- Unity may seed the initial value;
- the rules store becomes authoritative when the rules commit succeeds, while attachment connects
  Unity reads and projections to that authority;
- Unity reads through the exact owning bridge or receives committed projections; and
- detach must not let an old encounter overwrite newer ownership.

Rules Runtime code must remain usable without `GameObject`, `MonoBehaviour`, scene discovery, or
Unity timing.

## Composition and lifetime

The production composition root supplies modules in a deterministic order and asks only for the
capabilities each module implements. Registration tokens and external observers have an explicit
owner and lifetime. Encounter-scoped resources end with the encounter; temporary root-scoped
resources end with that root.

The design requires explicit ownership and cleanup, not one universal lifetime abstraction for all
future clients. The current Unity implementation is documented in the
[implementation guide](Encounter_Rules_Architecture.md#production-composition).

## Required invariants

The following are architectural constraints, not optional conventions:

- A migrated state slice has exactly one writable authority.
- Only reducers mutate authoritative state.
- Facts describe committed state changes or explicit lifecycle occurrences and are published only
  after their owning commit or lifecycle boundary, without mutating their payloads.
- Every action operation passes through the action lifecycle exactly once.
- Feature semantics remain in the feature module, not shared bridges or managers.
- Composition is explicit and deterministic; no static discovery or self-registration.
- Rules-owned randomness comes from an injected roll service.
- Unity objects do not enter the rules model.
- Registration and cleanup preserve the identity of the owner they attach or detach.
- Deferred examples do not create production contracts.

## What this document intentionally does not specify

This design does not define:

- the current module list or its exact order;
- every state slice or DTO present in production;
- which features are fully migrated;
- Unity enrollment, topology refresh, projection, or teardown steps;
- exact settlement and presentation-queue mechanics;
- recipes for adding a production feature; or
- speculative implementations of unbuilt Pathfinder rules.

Those details change more quickly and belong in the
[encounter rules implementation guide](Encounter_Rules_Architecture.md) or in the feature code and
tests. Keeping them out of the design prevents today's integration complexity from becoming
tomorrow's accidental architecture.
