using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>Immutable numeric contribution after definition selection and adjustments.</summary>
    public sealed class PreparedModifierValue
    {
        public PreparedModifierValue(string slug, int value, string type, string ability)
        {
            Slug = slug ?? string.Empty;
            Value = value;
            Type = type ?? string.Empty;
            Ability = ability ?? string.Empty;
        }

        public string Slug { get; }
        public int Value { get; }
        public string Type { get; }
        public string Ability { get; }
    }

    /// <summary>
    /// Selects compiled contributions from the same immutable binding snapshot used by the rule
    /// registry. It does not resolve Unity items or mutate equipment.
    /// </summary>
    public static class PreparedRuleCollectors
    {
        public static IReadOnlyList<PreparedModifierValue> CollectModifiers(
            PreparedRulePackage package,
            PreparedPredicateContext context,
            string selector
        )
        {
            if (package == null)
                throw new ArgumentNullException(nameof(package));
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            List<PreparedModifierValue> values = package
                .Modifiers.Where(value =>
                    string.Equals(value.Selector, selector, StringComparison.OrdinalIgnoreCase)
                )
                .Where(value =>
                    context.IsDefinitionActive(value.DefinitionId)
                    && value.Predicate.Evaluate(context)
                )
                .GroupBy(value => value.Slug, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .Select(value => new PreparedModifierValue(
                    value.Slug,
                    value.Value,
                    value.Type,
                    value.Ability
                ))
                .ToList();
            foreach (
                PreparedAdjustmentSpec adjustment in package
                    .Adjustments.Where(value =>
                        string.Equals(value.Selector, selector, StringComparison.OrdinalIgnoreCase)
                    )
                    .Where(value =>
                        context.IsDefinitionActive(value.DefinitionId)
                        && value.Predicate.Evaluate(context)
                    )
                    .OrderBy(value => value.Priority)
            )
            {
                int index = values.FindLastIndex(value =>
                    string.Equals(value.Slug, adjustment.Slug, StringComparison.OrdinalIgnoreCase)
                );
                if (index < 0)
                    continue;
                PreparedModifierValue current = values[index];
                int amount = current.Value;
                if (string.Equals(adjustment.Mode, "upgrade", StringComparison.OrdinalIgnoreCase))
                    amount = Math.Max(
                        amount,
                        (int)Math.Round(adjustment.Value, MidpointRounding.AwayFromZero)
                    );
                else if (
                    string.Equals(adjustment.Mode, "multiply", StringComparison.OrdinalIgnoreCase)
                )
                    amount = (int)Math.Floor(amount * adjustment.Value);
                values[index] = new PreparedModifierValue(
                    current.Slug,
                    amount,
                    current.Type,
                    current.Ability
                );
            }
            return Array.AsReadOnly(values.ToArray());
        }

        public static IReadOnlyList<PreparedDamageDiceSpec> CollectDamageDice(
            PreparedRulePackage package,
            PreparedPredicateContext context,
            string selector
        ) =>
            Array.AsReadOnly(
                package
                    .DamageDice.Where(value =>
                        string.Equals(value.Selector, selector, StringComparison.OrdinalIgnoreCase)
                        && value.DiceNumber > 0
                        && value.DieSize > 0
                        && context.IsDefinitionActive(value.DefinitionId)
                        && value.Predicate.Evaluate(context)
                    )
                    .ToArray()
            );

        public static IReadOnlyList<PreparedItemAlterationSpec> CollectItemAlterations(
            PreparedRulePackage package,
            PreparedPredicateContext context,
            string itemType,
            string property
        ) =>
            Array.AsReadOnly(
                package
                    .ItemAlterations.Where(value =>
                        string.Equals(value.ItemType, itemType, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(
                            value.Property,
                            property,
                            StringComparison.OrdinalIgnoreCase
                        )
                        && context.IsDefinitionActive(value.DefinitionId)
                        && value.Predicate.Evaluate(context)
                    )
                    .ToArray()
            );
    }
}
