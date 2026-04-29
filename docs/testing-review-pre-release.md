# Testing Review for `pre-release`

## Scope

This review is based on a source-code walkthrough of the current automated tests on the `pre-release` branch.

I reviewed the current test suite in `Assets/Tests/`, compared it against the game code in `Assets/Scripts/` and `Assets/UIStuff/`, and skimmed the saved code coverage artifacts already committed in `CodeCoverage/`.

This is intentionally a **Unity-aware** review: Unity projects cannot always follow textbook unit-testing patterns, especially when scene wiring, `MonoBehaviour` lifecycle, animation, coroutines, and UI Toolkit interactions are involved. That said, this project still has several areas where much stronger automated testing is feasible with relatively modest refactoring.

## Current Test Inventory

Current suite:

- **23 tests total**
- **1 EditMode test**
- **22 PlayMode tests**

Covered areas today:

- Main menu UI
- Character creation flow
- HUD/action button presence
- Some action-state transitions (`Stride`, `Strike`)
- One map generation smoke test in EditMode

Coverage artifacts in the repo suggest fairly high line coverage in PlayMode for `MainGameAssembly`, but those numbers should be interpreted carefully:

- scene-based PlayMode tests naturally execute a lot of code just by loading scenes and clicking through flows
- the committed OpenCover artifacts report effectively **no useful branch coverage** (`0` branch points reported)
- only **one** EditMode test exists, so rules-heavy code is still under-tested even if sequence coverage looks decent on paper

## What the Team Is Doing Well

There is real progress here. The current suite is not just placeholder scaffolding anymore.

### 1. They are testing real player-facing flows

The strongest part of the suite is that it exercises actual scenes and actual UI, not only isolated methods. For a Unity project, that matters. These tests will catch:

- broken scene names
- missing `UIDocument`s
- renamed UI elements
- button wiring regressions
- some state machine regressions

That is valuable, especially late in a semester project when wiring bugs are common.

### 2. They separated EditMode and PlayMode correctly

Using separate assembly definitions for EditMode vs PlayMode is the right shape. It gives them room to grow into a two-layer strategy:

- **EditMode** for fast, deterministic rules/logic tests
- **PlayMode** for scene integration and user interaction flows

That split is exactly what a Unity project should aim for.

### 3. They already created reusable PlayMode test infrastructure

`PlayModeBase` is a good start. Shared helpers like:

- scene setup
- `UIDocument` lookup
- button pressing
- timeout-based waiting

are the right idea and reduce duplication.

### 4. They are testing both success and failure paths in some places

The `Stride` and `Strike` tests are not purely “happy path” checks. They also try invalid movement and out-of-range targeting. That is the right instinct.

### 5. They are thinking about test coverage intentionally

The presence of committed coverage output suggests the team is trying to reason about coverage rather than only writing a few tests and moving on. That mindset is good.

## Biggest Gaps in the Current Testing Strategy

The main issue is not “too few tests” in the abstract. The issue is that the suite is currently **very PlayMode-heavy** and **very UI/state-entry heavy**, while much of the game’s actual rules complexity lives elsewhere.

### 1. The PF2e rules/math layer is barely tested directly

This is the biggest strategic gap.

There are several classes with important gameplay logic that should be covered by fast EditMode tests, but currently are not meaningfully exercised as isolated logic:

- `Game.Creature.D20`
- `Game.Creature.DamageRoller`
- `Strike`
- `StrikeWeapon`
- `CreatureComponent` stat helpers (`GetSkillMod`, `calculateAC`, damage handling)
- `CombatManager`
- `TeamRules`
- `GridPrivate.Dijkstra`
- `Heap<T>`
- condition/ability logic such as `Rage`

These are high-value targets because they are:

- deterministic or mostly deterministic
- central to combat correctness
- easier to test outside full scene bootstrapping
- likely to accumulate subtle regressions over time

For a Unity game implementing a tabletop ruleset, this is where the best ROI usually is: **move as much rules logic as possible into plain C# and hammer it with EditMode tests**.

### 2. Current PlayMode tests often assert shallow outcomes

Many of the current tests verify that a state changed, a button exists, or a scene loaded. That is a solid smoke-test layer, but it is not yet deep enough to validate gameplay correctness.

Examples:

- `StrikeTests` mostly assert FSM state and action points, but not actual HP changes, critical behavior, strike penalties, weaknesses/resistances, or combat log output.
- `StrideTests` assert position changes, but do not deeply validate occupancy rules, tile bookkeeping, path preview semantics, friendly-vs-hostile movement interactions, or cancellation behavior.
- `GameUITests.UIStatesTest()` ends with `Assert.Pass(...)`, which means the end-turn path is not really asserted.
- `MainMenuTests.OptionsButtonClick()` only proves that a button click event fired, not that the production options flow did anything meaningful.

### 3. The suite is overly dependent on one or two scenes staying exactly the same

Most PlayMode tests hinge on specific scene names and specific UI element names such as:

- `UnitTestingScene`
- `MainMenuScene`
- `CharacterCreationScene`
- hard-coded button names like `UnarmedStrikeButton`, `StrideButton`, etc.

That is normal for UI tests, but right now too much confidence is concentrated there. Small content or hierarchy changes could break a large portion of the suite even when core gameplay logic still works.

This is another reason to push more verification into EditMode logic tests.

### 4. AI behavior appears untested

`MindlessController` contains nontrivial combat decision logic:

- target selection
- path choice
- detour logic
- choosing when to move vs attack
- selecting reachable cells

That is exactly the kind of logic that can regress silently and is difficult to validate only through manual playtesting.

I did not find tests covering that decision-making directly.

### 5. Conditions and abilities appear largely untested

The repo contains condition/ability infrastructure, but I did not see automated tests around:

- applying/removing conditions
- stacking behavior / repeated sources
- rage restrictions and side effects
- temporary HP interactions
- event-driven strike modification

That is important because event-driven combat modifiers are the type of system where bugs can look fine in happy-path UI tests while failing in actual gameplay combinations.

### 6. Data-loading / content-driven behavior looks under-tested

There are systems for creatures, equipment, and data conversion, but I did not see much around:

- JSON/data import correctness
- creature prefab/action initialization
- weapon-derived strike creation
- malformed or incomplete content data

For a rules-heavy game, data bugs are often just as common as logic bugs.

## Specific Weak Spots and Concrete Examples

These are the kinds of issues the current suite would likely miss.

### 1. Rules correctness around d20 degrees of success

`Game.Creature.D20.Roll()` appears to implement natural 20 and natural 1 as automatic critical success / critical failure.

That is **not the normal PF2e rule**. In PF2e, a natural 20 increases the degree of success by one step, and a natural 1 decreases it by one step. That is different from “always crit success” or “always crit fail.”

Even if this is an intentional simplification, it is important enough that it should have explicit tests documenting the chosen behavior.

### 2. Team relationship behavior likely has an untested bug

`TeamRules.HostileTo(GameObject)` returns `FriendlyTo(...)` instead of `HostileTo(...)` for the game object overload.

That looks like a straightforward bug/typo, and it is exactly the kind of thing a small EditMode test suite around team relationships would catch immediately.

### 3. `CombatManager` contract is not well pinned down by tests

A few suspicious behaviors deserve explicit tests:

- `GetCombatants()` rotates the list by moving the last entry to the front
- `GetCombatants()` assumes the list is non-empty
- `CheckForEndOfGame()` appears unsafe if the combatant list is empty and `teams[0]` is accessed
- initiative ordering behavior is important but not directly tested

Those methods affect turn order and AI targeting, so they should be much more directly specified by tests.

### 4. Damage and strike logic has important edge cases unverified

Potentially fragile areas include:

- empty damage lists
- damage types with empty strings
- multiple damage types grouped correctly
- weakness/resistance interactions
- crit doubling
- strike penalty progression
- agile vs non-agile multiple attack penalty semantics

These systems are gameplay-critical and are much easier to lock down with EditMode tests than with scene-level tests.

### 5. `Rage` action lifecycle looks risky

`Rage` has logic worth testing very directly:

- whether rage is allowed under fatigue / heavy armor / already raging
- temp HP calculation
- strike modification while raging
- listener registration and cleanup

More importantly, the implementation structure suggests coroutine/action lifecycle behavior that is easy to get wrong and hard to catch with the current suite.

### 6. The pathfinding stack is important and only lightly exercised

`Dijkstra`, `Heap<T>`, and tile occupancy/path legality are all central to combat feel and correctness.

The existing `Stride` tests cover a very narrow vertical slice, but not the algorithmic surface area. Missing cases include:

- diagonal movement constraints
- blocked diagonals
- null/out-of-bounds tiles
- friendly occupant pass-through vs hostile blocking
- range calculations
- repeated `Search()` / `Find()` behavior
- cache consistency assumptions

## Improvements That Would Make the Existing Tests Cleaner

These are mostly practical cleanup ideas rather than major architectural changes.

### 1. Make wait helpers side-effect free

A few tests perform actions inside the polling predicate passed to `WaitUntilWithTimeout`, especially in `CharacterCreatorTests.CharacterCreatorTutorialCompletes()`.

That is risky because the helper is conceptually “wait until true,” but the predicate is mutating the UI every frame. That can:

- hide race conditions
- over-click buttons
- make failures harder to interpret
- create timing-dependent flakiness

A cleaner pattern is:

1. wait for element/state to be ready
2. perform one action
3. wait for the next expected state

### 2. Reduce duplicated UI lookup/wait boilerplate

A small helper layer would make these tests more readable and more maintainable.

Examples:

- `FindButtonOrFail(name)`
- `WaitForButton(name)`
- `ClickAndWaitForState(buttonName, expectedStateType)`
- `LoadSceneAndGetRoot(sceneName)`

Right now the suite repeats a lot of “find button, wait, assert not null, click, yield null” logic.

### 3. Prefer parameterized tests for repetitive UI checks

Several tests would become smaller and clearer with NUnit built-ins like:

- `[TestCase]`
- `[TestCaseSource]`
- `[ValueSource]`

Good candidates:

- main menu button presence/interactability
- speed button existence and behavior
- strike target validity cases
- skill/weapon/range variants

This would cut repetition and make the intent more obvious.

### 4. Avoid comments that imply manual verification is still required

`StrideMoveTest()` currently says “Visually inspect this test for now.”

That is a warning sign. If a test still requires visual inspection, it is not yet carrying its full weight as automation.

It is fine to keep the test, but the next step should be to replace that human inspection note with explicit assertions.

### 5. Assert behavior, not just event wiring

A few tests are still very close to “did a click handler fire?” instead of “did the system do the right thing?”

For example:

- `OptionsButtonClick()` should ideally assert an options panel/state change, not just that a local listener attached in the test ran.
- `UIStatesTest()` should assert end-turn consequences rather than ending with `Assert.Pass`.

### 6. Make randomness deterministic where possible

Combat logic currently uses randomness (`Random.Range`) for initiative and damage.

For logic-level tests, I strongly recommend either:

- calling `Random.InitState(...)` in setup, or
- better, introducing a tiny RNG abstraction for rules code

Without deterministic control, rules tests will stay brittle or stay too shallow.

## Suggested Testing Roadmap

If the team only has time for a limited number of improvements, I would prioritize these in order.

### Priority 1: Build a real EditMode rules suite

Add fast tests around:

- `D20`
- `DamageRoller`
- `TeamRules`
- `CombatManager`
- `Heap<T>`
- `Dijkstra`
- `CreatureComponent.GetSkillMod()`
- `CreatureComponent.calculateAC()`
- condition add/remove/change behavior

This is probably the single biggest quality multiplier they can get.

### Priority 2: Strengthen combat action tests from smoke tests to behavioral tests

For `Strike` and `Stride`, add assertions around:

- HP deltas
- action point changes
- strike penalty changes
- valid vs invalid tile occupancy effects
- tile occupant lists before/after movement
- cancel/refund behavior
- correct range highlighting / preview path behavior

### Priority 3: Add explicit tests for likely regression-prone AI logic

Even a small set of deterministic tests for `MindlessController` would be high value:

- picks hostile over friendly target
- attacks when already in range
- moves when out of range
- chooses best strike by expected damage
- avoids occupied/invalid cells

### Priority 4: Add tests for data-driven initialization

Examples:

- `Unarmed.AddUnarmedStrike()`
- `StrikeWeapon.WeaponStrikeAdderAutomatic()`
- handling of missing/invalid weapon data
- content assumptions around creature stats and equipment

### Priority 5: Update `TESTING.md`

The checked-in `TESTING.md` is out of date and still talks about sample tests / fake tests / no PlayMode tests.

That documentation should be updated so future contributors understand the current test structure and expectations.

## Unity-Specific Advice

This project is exactly the kind of Unity project where a two-tier strategy works best.

### Keep in PlayMode

Use PlayMode tests for:

- scene wiring
- button hookups
- state machine integration
- full combat interaction smoke tests
- scene transitions
- HUD visibility / UI Toolkit integration

### Move toward EditMode / plain C# tests

Use EditMode tests for:

- PF2e rules math
- combat resolution logic
- pathfinding algorithms
- team/relationship rules
- condition application
- damage calculations
- AI decisions when the inputs can be modeled without a scene

If the team does one refactor for testability, this would be my recommendation:

> Extract more of the rules engine into plain C# classes that do not depend directly on scene objects or `MonoBehaviour` lifecycle.

That will make their tests:

- faster
- more deterministic
- less flaky
- easier to run in CI
- easier to trust when they fail

## Libraries / Tooling Worth Considering

These are the only additions I would seriously consider right now.

### Good immediate wins

- **NUnit parameterized tests** (`TestCase`, `TestCaseSource`, `ValueSource`) — already available, low friction
- **Unity Test Framework** — already in use, continue using it for PlayMode/integration
- **Unity Code Coverage package** — keep it, but treat coverage as a directional signal, not proof of correctness

### Worth considering after more pure C# extraction

- **NSubstitute** or **Moq** for interface-based unit tests once more code is decoupled from `MonoBehaviour`
- **FluentAssertions** if the team wants clearer, more expressive assertion messages

I would **not** recommend going heavy on mocking inside scene-driven `MonoBehaviour` tests right now. The better move is to isolate more logic first.

## Bottom Line

The current suite is a good start, especially for UI/scene smoke coverage. It already provides some real protection against broken scene wiring and basic combat flow regressions.

The next step is not just “more tests.” The next step is **better-layered tests**:

- keep the current PlayMode smoke/integration layer
- add a substantial EditMode rules layer
- make combat assertions deeper and more deterministic
- document the intended rules behavior explicitly, especially where PF2e semantics matter

If the team does that, they will move from “we have tests” to “the tests actually define and protect game behavior.”
