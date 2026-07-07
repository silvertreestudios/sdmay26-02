# PF2e Modifier Stacking

This project resolves d20 check and DC modifiers through `Game.Rules.Pf2eModifierResolver`.

Rules reference: Archives of Nethys, [Checks and modifier stacking](https://2e.aonprd.com/Rules.aspx?ID=2278). Do not copy rules prose into code or data; use concise summaries and links.

## Model

Each modifier records:

- `Value`: signed integer bonus or penalty.
- `Type`: `Untyped`, `Circumstance`, `Item`, or `Status`.
- `Source`: human-readable origin for logs and tests.
- `TargetStatistic`: the statistic the modifier can affect.

The resolver filters by target statistic. Untyped modifiers stack. For each typed category, the highest bonus applies and lower bonuses of the same type are suppressed; the worst penalty applies and milder penalties of the same type are suppressed. A same-type bonus and same-type penalty can both apply.

## Integration Pattern

`CreatureComponent` is the compatibility adapter for current serialized creature stats and the main resolution entrypoint for common rolls. It should not become the global registry for every PF2e effect.

Ongoing effects should provide modifiers through `IPf2eModifierProvider` components on the relevant creature or target. Current examples:

- `Conditions` implements `IPf2eModifierProvider` and delegates condition-specific mappings to `ConditionModifierRules`.
- `Pf2eModifierCollection` is a generic provider for simple temporary modifiers when a source does not yet have a dedicated component.
- Strike supplies one-roll contextual modifiers directly as method parameters, such as MAP, range, and cover.

This lets class feats, spells, magic items, areas, and environmental systems own their own modifier logic while `CreatureComponent` only gathers providers and resolves the final total.

## Current Sources

Current serialized creature fields remain compatibility inputs:

- Imported NPC attack totals and weapon action bonuses are treated as untyped base attack modifiers because the current JSON does not decompose them by PF2e modifier type.
- Imported or manually assigned AC is treated as an untyped base AC unless the creature has equipped armor data available.
- Equipped armor contributes its armor AC as an item modifier; AoN's armor rules define the AC Bonus value as the armor item bonus: https://2e.aonprd.com/Rules.aspx?ID=2166.
- Saves, skills, initiative, and DC helpers route through the same resolver.

Strike adds current combat modifiers through the resolver:

- Multiple attack penalty: untyped attack penalty, [AoN](https://2e.aonprd.com/Rules.aspx?ID=2288).
- Range increment penalty: untyped attack penalty, [AoN](https://2e.aonprd.com/Rules.aspx?ID=2288).
- Cover: circumstance AC bonus, [AoN](https://2e.aonprd.com/Rules.aspx?ID=2372).
- Off-Guard / Flat-Footed: circumstance AC penalty, [AoN](https://2e.aonprd.com/Conditions.aspx?ID=58).

## Boundaries

The resolver is pure C# so EditMode tests can cover stacking behavior without scene setup. Damage dice, flat damage, weaknesses, and resistances are intentionally separate. They are not d20 check/DC modifiers and should only move to this model if a future rule requires typed bonuses or penalties to those calculations.