# Rules Runtime Feature Lane Status

This ledger reconciles the feature-owned entries in the rules-runtime migration inventory against
production baseline `0c72371ecce0034445acd0f9eed7b3aeae01d239`. It distinguishes implemented
rules from caller integration and product decisions. The production code remains authoritative;
this document does not turn authored data into executable scope.

## Implemented feature-owned rules

| Feature | Current feature authority | Remaining boundary |
| --- | --- | --- |
| Stride | `StrideRules` owns the action and movement workflow; movement state is rules-owned. | General caller cleanup may eventually remove transitional bridge helpers. Route planning and exploration orchestration remain outside rules. |
| Strike, reload, ammunition, and MAP | `StrikeRules` owns validation, action costs, check/damage orchestration, reload, ammunition, and MAP. | Base statistics, defenses, and some prepared Strike inputs remain Unity captures pending the caller-owned statistics integration. |
| Flanking | `FlankingRules` now owns the pure opposite-side eligibility decision over authoritative roster, health, and positions. `FlankingRule` is the feature-owned Unity capture for current action availability, team relationships, melee threat, and topology. Production Strike supplies its exact `RulesSnapshot`; the retained legacy adapter uses only current grid occupants and the same selector rather than a second calculation. | Team-to-`PlayerId` composition is caller-owned. A future shared immutable Strike line-of-effect contract may narrow the topology capture, but this feature does not add one speculatively. |
| Off-Guard / Flat-Footed membership | `OffGuardRules` owns the named query over independent `ConditionRules` applications, including the retained imported/save-data alias. Production Strike uses this selector. | Rules-native AC modifiers depend on caller-owned statistics registration. Generic condition persistence remains caller-owned. |
| Rules-native spell shell | `CastSpellActionOp` and the spellcasting runtime own action/slot/effect lifecycle for supported definitions. | Shared spell installation and persistence are caller-owned integration hotspots. Catalog loading alone does not implement a spell. |
| Divine Lance | The supported spell-attack definition uses rules checks, MAP, typed damage, and health. | Defense/statistic capture remains transitional until caller integration. |
| Light | The rules runtime owns its active effect, binding, and duration; `UnityLightModule` owns presentation. | General rules-native effect persistence is absent, so Light does not survive dungeon reload. That schema/caller change is not feature-lane work. |
| Rage and Quick-Tempered | `RageRules` owns validation, frequency, effect timing, temporary HP, and cleanup; `UnityRageModule` owns immutable feature extraction and installation. | Fatigued/Encumbered, armor, level, and Constitution are still captured from Unity. Replacing those reads requires coordinated condition/statistics caller contracts. Rage's current end-on-reload normalization remains intentional until a product-approved persistence change. |
| Sourced condition applications | `ConditionRules` stores each valued application as one active effect/binding and derives the highest active value without a second aggregate state. | Generic save/restore of values and lifetimes is caller-owned. Condition-specific mechanics require their own feature. |
| Slowed | `SlowedRules` owns the maximum-value action reduction and `UnitySlowedModule` contributes one calculation binding per combatant. | The migrated implementation does not invent condition reduction/removal or a reaction rule. Any change to reaction behavior requires an explicit product decision. |
| Rotting Aura | `RottingAuraRules` owns eligibility, deterministic rolls, typed damage, health orchestration, and its completion Fact. `UnityRottingAuraModule` owns geometry/data capture and logging. | Traits, level, defenses, and aura geometry remain narrow read-only Unity inputs; no general aura state or speculative topology API is added. |
| Typed action presentation | Feature presenters consume committed lifecycle Facts through `UnityActionPresentationRegistry`. | This is presentation routing, not a rules authority or permission to add unsupported actions. |
| Area and targeting snapshots | Immutable snapshots prevent confirmation from reusing mutable preview data. | Geometry remains feature/Unity infrastructure until a demonstrated rules feature needs a narrower contract. |

## Reachable integration gaps outside this lane

These behaviors are reachable, but their remaining changes touch exclusive caller/composition
ownership and are therefore reported rather than implemented here.

| Gap | Required owner and constraint |
| --- | --- |
| Creature statistics registration | Caller task `t_47f1df99` owns complete combatant DTO/reducer/enrollment changes and switching general check consumers. Feature adapters must not seed a parallel statistics path. |
| Strike/spell defenses and prepared inputs | The Strike/spell integrator may remove Unity captures only after replacement authorities exist. Weaknesses, resistances, and traits do not justify speculative shared state. |
| Rage condition/statistic reads | Requires a coordinated provider contract using rules condition/statistic state. This lane does not change the existing public provider contract or central module construction independently. |
| Team relationships | General composition owns team-to-`PlayerId` setup. Flanking preserves current directional Unity relationships in its feature capture meanwhile. |
| Condition and active-effect persistence | General persistence callers and save DTOs must change together, preserve intended Rage normalization, and avoid compatibility or dual restore paths. |
| Rules-native Light durability | Part of the same caller-owned active-effect persistence gap, not a new Light-specific fallback. |
| General managers and action controllers | Caller task `t_47f1df99` owns central bridge/module ordering, controllers, manager aliases, and shared action installation. |

## Explicit product decisions and non-features

- Dormant legacy Shield, Guidance, Haunting Hymn, Bless, Infuse Vitality, and Heal implementations
  are not installed production behavior. No spell is enabled or migrated without explicit selection
  of its semantics. Guidance immunity duration and Infuse Vitality target count remain unresolved.
- Combat door legality and cost remain a known action-lifecycle/atomicity gap. This change does not
  alter door behavior; an approved Open Door vertical and coordinated world projection are needed.
- The legacy character-creation calculator is disconnected from preparation and encounter
  enrollment. It is not connected or migrated without a product decision defining its build rules.
- Goblin Scuttle, Scamper, Grab, Void Healing, Sneak Attack declarations, catalog feats/class
  features, unsupported rule keys, most traits, and the remaining spell catalog are data-only unless
  a reachable vertical feature explicitly implements them. Existing Sneak Attack damage remains a
  prepared Strike contribution and now receives the authoritative Off-Guard targeting option.
- Authored immunities are not imported by current DTOs and have no executable behavior; no immunity
  state is invented.
- Dungeon generation, room lifecycle, exploration planning, persistence scheduling, KayKit visual
  and topology adapters, grid rendering/input, UI presentation, animation, audio, and scene flow are
  not named rule migrations.
- Retained `.orig` files and commented `LineOfSight.cs` are cleanup inventory, not executable
  fallbacks or feature work.

## Verification expectations for this lane

`FlankingRulesTests` covers opposite-side and corner geometry, defeated/unavailable/non-cooperating
participants, missing authoritative state, deterministic copied ordering, duplicate invalid input,
and the no-mutation selector contract. `OffGuardRulesTests` covers both condition identities,
disabled/wrong/missing applications, and no state mutation. `RulesStrikeUnityTests` exercises the
production adapter with a real rules-backed Strike and an opposite living ally.
