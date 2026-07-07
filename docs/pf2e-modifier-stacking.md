# PF2e Modifier Stacking

This project resolves d20 check and DC modifiers through `Game.Rules.Pf2eModifierResolver`.

Rules reference: Archives of Nethys, [Checks and modifier stacking](https://2e.aonprd.com/Rules.aspx?ID=2278). Do not copy rules prose into code or data; use concise summaries and links.

## Model

Each modifier records:

- `Value`: signed integer bonus or penalty.
- `Type`: `Untyped`, `Circumstance`, `Item`, or `Status`.
- `Source`: human-readable origin for logs and tests.
- `TargetStatistic`: the statistic the modifier can affect.
- `RulesReference`: optional AoN link for rules-sensitive sources.

The resolver filters by target statistic. Untyped modifiers stack. For each typed category, the highest bonus applies and lower bonuses of the same type are suppressed; the worst penalty applies and milder penalties of the same type are suppressed. A same-type bonus and same-type penalty can both apply.

## Current Integration

Current serialized creature fields remain compatibility inputs:

- Imported NPC attack totals and weapon action bonuses are treated as untyped base attack modifiers because the current JSON does not decompose them by PF2e modifier type.
- Imported or manually assigned AC is treated as an untyped base AC unless the creature has equipped armor data available.
- Equipped armor contributes its armor AC as an item modifier, while Dex and proficiency contributions remain untyped base components.
- Saves, skills, initiative, and DC helpers route through the same resolver.

Strike adds current combat modifiers through the resolver:

- Multiple attack penalty: untyped attack penalty, [AoN](https://2e.aonprd.com/Rules.aspx?ID=2288).
- Range increment penalty: untyped attack penalty, [AoN](https://2e.aonprd.com/Rules.aspx?ID=2288).
- Cover: circumstance AC bonus, [AoN](https://2e.aonprd.com/Rules.aspx?ID=2372).
- Off-Guard / Flat-Footed: circumstance AC penalty, [AoN](https://2e.aonprd.com/Conditions.aspx?ID=58).

## Design Choices

The resolver is pure C# so EditMode tests can cover stacking behavior without scene setup. `CreatureComponent` owns runtime modifier storage for now because current combat calculations already read through that component. This avoids a bulk condition/effect refactor while giving future abilities, conditions, equipment, and spells one shared path for typed modifiers.

Damage dice, flat damage, weaknesses, and resistances are intentionally separate. They are not d20 check/DC modifiers and should only move to this model if a future rule requires typed bonuses or penalties to those calculations.