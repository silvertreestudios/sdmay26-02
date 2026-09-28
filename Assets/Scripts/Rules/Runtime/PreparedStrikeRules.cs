using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>Immutable flat or ability-based damage definition captured during preparation.</summary>
    public sealed class PreparedStrikeModifier
    {
        /// <summary>Copies one prepared modifier without retaining its mutable source.</summary>
        public PreparedStrikeModifier(
            string selector,
            string slug,
            int value,
            string ability,
            PreparedPredicate predicate
        )
        {
            Selector = selector;
            Slug = slug;
            Value = value;
            Ability = ability;
            Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        }

        /// <summary>Gets the statistic selector from prepared content.</summary>
        public string Selector { get; }

        /// <summary>Gets the modifier identity used for replacement and adjustment.</summary>
        public string Slug { get; }

        /// <summary>Gets the unmodified content value.</summary>
        public int Value { get; }

        /// <summary>Gets the substitution ability slug, or an empty value.</summary>
        public string Ability { get; }

        /// <summary>Gets the immutable contextual eligibility predicate.</summary>
        public PreparedPredicate Predicate { get; }
    }

    /// <summary>Immutable contextual adjustment to a named prepared damage modifier.</summary>
    public sealed class PreparedStrikeAdjustment
    {
        /// <summary>Copies an adjustment; priority determines stable evaluation order.</summary>
        public PreparedStrikeAdjustment(
            string selector,
            string slug,
            string mode,
            float value,
            int priority,
            PreparedPredicate predicate
        )
        {
            Selector = selector;
            Slug = slug;
            Mode = mode;
            Value = value;
            Priority = priority;
            Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        }

        /// <summary>Gets the statistic selector from prepared content.</summary>
        public string Selector { get; }

        /// <summary>Gets the modifier identity used for replacement and adjustment.</summary>
        public string Slug { get; }

        /// <summary>Gets the content-defined adjustment mode.</summary>
        public string Mode { get; }

        /// <summary>Gets the unmodified content value.</summary>
        public float Value { get; }

        /// <summary>Gets the stable adjustment ordering priority.</summary>
        public int Priority { get; }

        /// <summary>Gets the immutable contextual eligibility predicate.</summary>
        public PreparedPredicate Predicate { get; }
    }

    /// <summary>Immutable conditional extra damage dice.</summary>
    public sealed class PreparedStrikeDice
    {
        /// <summary>Copies dice data; nonpositive counts and faces are ignored during evaluation.</summary>
        public PreparedStrikeDice(
            string selector,
            string category,
            int count,
            int sides,
            PreparedPredicate predicate
        )
        {
            Selector = selector;
            Category = category;
            Count = count;
            Sides = sides;
            Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        }

        /// <summary>Gets the statistic selector from prepared content.</summary>
        public string Selector { get; }

        /// <summary>Gets the content category, such as precision, independently of the Strike's damage type.</summary>
        public string Category { get; }

        /// <summary>Gets the number of additional dice.</summary>
        public int Count { get; }

        /// <summary>Gets the number of faces per additional die.</summary>
        public int Sides { get; }

        /// <summary>Gets the immutable contextual eligibility predicate.</summary>
        public PreparedPredicate Predicate { get; }
    }

    /// <summary>Immutable prepared item alteration used to construct contextual Strike tags.</summary>
    public sealed class PreparedStrikeAlteration
    {
        /// <summary>Copies a prepared alteration without evaluating its predicate.</summary>
        public PreparedStrikeAlteration(
            string itemType,
            string property,
            string mode,
            string value,
            PreparedPredicate predicate
        )
        {
            ItemType = itemType;
            Property = property;
            Mode = mode;
            Value = value;
            Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        }

        /// <summary>Gets the content item type matched by this alteration.</summary>
        public string ItemType { get; }

        /// <summary>Gets the altered content property.</summary>
        public string Property { get; }

        /// <summary>Gets the content-defined adjustment mode.</summary>
        public string Mode { get; }

        /// <summary>Gets the unmodified content value.</summary>
        public string Value { get; }

        /// <summary>Gets the immutable contextual eligibility predicate.</summary>
        public PreparedPredicate Predicate { get; }
    }

    /// <summary>Freezes supported prepared Strike inputs for an enrolled combatant.</summary>
    public sealed class PreparedStrikeDefinition
    {
        /// <summary>Copies all collections so later preparation or projection edits cannot change encounter rules.</summary>
        public PreparedStrikeDefinition(
            IEnumerable<string> options,
            IDictionary<string, int> abilities,
            IEnumerable<PreparedStrikeModifier> modifiers,
            IEnumerable<PreparedStrikeAdjustment> adjustments,
            IEnumerable<PreparedStrikeDice> dice,
            IEnumerable<PreparedStrikeAlteration> alterations
        )
        {
            Options = Array.AsReadOnly(options.ToArray());
            Abilities = new ReadOnlyDictionary<string, int>(
                new Dictionary<string, int>(abilities, StringComparer.OrdinalIgnoreCase)
            );
            Modifiers = Array.AsReadOnly(modifiers.ToArray());
            Adjustments = Array.AsReadOnly(adjustments.ToArray());
            Dice = Array.AsReadOnly(dice.ToArray());
            Alterations = Array.AsReadOnly(alterations.ToArray());
        }

        /// <summary>Gets copied permanent preparation options; active effects are selected from the rules snapshot.</summary>
        public IReadOnlyList<string> Options { get; }

        /// <summary>Gets immutable ability modifiers for damage substitutions.</summary>
        public IReadOnlyDictionary<string, int> Abilities { get; }

        /// <summary>Gets copied flat and ability damage definitions.</summary>
        public IReadOnlyList<PreparedStrikeModifier> Modifiers { get; }

        /// <summary>Gets copied contextual modifier adjustments.</summary>
        public IReadOnlyList<PreparedStrikeAdjustment> Adjustments { get; }

        /// <summary>Gets copied extra damage dice definitions.</summary>
        public IReadOnlyList<PreparedStrikeDice> Dice { get; }

        /// <summary>Gets copied item alteration definitions.</summary>
        public IReadOnlyList<PreparedStrikeAlteration> Alterations { get; }
    }

    /// <summary>Additional damage selected for exactly one Strike context.</summary>
    public sealed class PreparedStrikeContributions
    {
        internal PreparedStrikeContributions(List<TypedDamageDice> dice, List<TypedFlatDamage> flat)
        {
            DamageDice = dice.AsReadOnly();
            FlatDamage = flat.AsReadOnly();
        }

        /// <summary>Gets the additional dice for this evaluation only.</summary>
        public IReadOnlyList<TypedDamageDice> DamageDice { get; }

        /// <summary>Gets the additional flat damage, including ability replacement deltas.</summary>
        public IReadOnlyList<TypedFlatDamage> FlatDamage { get; }
    }

    /// <summary>Evaluates supported prepared damage rules from immutable definitions and authoritative effects.</summary>
    public static class PreparedStrikeRules
    {
        /// <summary>Reads the supported reload value, falling back to an imported reload trait.</summary>
        public static int ReloadActions(string reload, IEnumerable<string> traits)
        {
            if (!string.IsNullOrWhiteSpace(reload) && int.TryParse(reload, out int cost))
                return Math.Max(0, cost);
            foreach (string trait in traits)
                if (
                    trait != null
                    && trait.StartsWith("reload-", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(trait.Substring(7), out cost)
                )
                    return Math.Max(0, cost);
            return 0;
        }

        /// <summary>Builds the supported unarmed profile from imported passive and ability inputs.</summary>
        public static StrikeItemDefinition CreateUnarmed(
            ItemId item,
            IEnumerable<string> passiveSlugs,
            int attackModifier,
            int strengthModifier
        ) =>
            new(
                item,
                new ItemDefinitionId("unarmed"),
                "Unarmed Strike",
                "unarmed",
                "unarmed",
                new[]
                {
                    Trait.FromSlug("agile"),
                    Trait.FromSlug("finesse"),
                    Trait.FromSlug("nonlethal"),
                    Trait.FromSlug("unarmed"),
                },
                attackModifier,
                new[]
                {
                    new TypedDamageDice(
                        new DiceExpression(
                            1,
                            passiveSlugs.Contains("zombie-fist", StringComparer.OrdinalIgnoreCase)
                                ? 6
                                : 3
                        ),
                        "bludgeoning",
                        "Unarmed Strike"
                    ),
                },
                new[] { new TypedFlatDamage(strengthModifier, "bludgeoning", "Strength") },
                5,
                0,
                0,
                StrikeAmmunitionRequirement.None
            );

        /// <summary>Builds weapon reach and base damage policy from imported equipment values.</summary>
        /// <remarks>Ranged ammunition weapons omit the imported flat damage bonus; melee and thrown weapons retain it.</remarks>
        public static StrikeItemDefinition CreateWeapon(
            ItemId item,
            ItemDefinitionId definition,
            string label,
            string group,
            string category,
            IEnumerable<Trait> traits,
            int attackModifier,
            TypedDamageDice damage,
            int damageBonus,
            int rangeFeet,
            int reloadActions,
            StrikeAmmunitionRequirement ammunition
        )
        {
            Trait[] capturedTraits = traits.ToArray();
            return new StrikeItemDefinition(
                item,
                definition,
                label,
                group,
                category,
                capturedTraits,
                attackModifier,
                new[] { damage },
                rangeFeet <= 0 || ammunition is NoStrikeAmmunitionRequirement
                    ? new[] { new TypedFlatDamage(damageBonus, damage.DamageType, "Damage bonus") }
                    : Array.Empty<TypedFlatDamage>(),
                capturedTraits.Contains(Trait.FromSlug("reach")) ? 10 : 5,
                Math.Max(0, rangeFeet),
                reloadActions,
                ammunition
            );
        }

        /// <summary>Derives contextual damage anew; adjustments never modify the prepared definition.</summary>
        /// <remarks>Additional dice inherit the weapon's primary damage type. Category-specific defenses are not evaluated here.</remarks>
        public static PreparedStrikeContributions Evaluate(
            PreparedStrikeDefinition prepared,
            StrikeItemDefinition item,
            LegalStrikeTargetingOutcome targeting,
            RulesSnapshot snapshot,
            CreatureId actor,
            CreatureId target
        )
        {
            HashSet<string> options = new(prepared.Options, StringComparer.OrdinalIgnoreCase);
            // Active effects and conditions must come from this operation's snapshot, never a
            // cached prepared roll option left over from another encounter or target.
            options.RemoveWhere(option =>
                option.StartsWith("self:effect:", StringComparison.OrdinalIgnoreCase)
                || option.StartsWith("self:condition:", StringComparison.OrdinalIgnoreCase)
                || option.StartsWith("target:condition:", StringComparison.OrdinalIgnoreCase)
            );
            options.UnionWith(RageRules.GetActiveRollOptions(snapshot, actor));
            foreach (Trait trait in item.Traits)
            {
                options.Add($"item:trait:{trait.Slug}");
                if (trait.Slug == "ranged")
                    options.Add("item:ranged");
                if (trait.Slug.StartsWith("thrown", StringComparison.Ordinal))
                    options.Add("item:thrown");
            }
            options.Add($"item:slug:{item.Definition.Value}");
            if (!string.IsNullOrWhiteSpace(item.Category))
                options.Add($"item:category:{item.Category}");
            options.Add($"item:damage:die:faces:{item.DamageDice[0].Dice.Sides}");
            if (item.IsRanged)
                options.Add("item:ranged");
            if (targeting.OffGuard || OffGuardRules.IsOffGuard(snapshot, target))
                options.Add("target:condition:off-guard");
            AddConditions(options, snapshot, actor, "self");
            AddConditions(options, snapshot, target, "target");
            foreach (PreparedStrikeAlteration alteration in prepared.Alterations)
                if (
                    Same(alteration.ItemType, "weapon")
                    && Same(alteration.Property, "other-tags")
                    && Same(alteration.Mode, "add")
                    && alteration.Predicate.Matches(options)
                )
                    options.Add($"item:tag:{alteration.Value}");

            var selected = prepared
                .Modifiers.Where(modifier =>
                    Same(modifier.Selector, "strike-damage") && modifier.Predicate.Matches(options)
                )
                .GroupBy(modifier => modifier.Slug, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToArray();
            Dictionary<string, int> values = selected.ToDictionary(
                modifier => modifier.Slug,
                modifier => modifier.Value,
                StringComparer.OrdinalIgnoreCase
            );
            foreach (
                PreparedStrikeAdjustment adjustment in prepared
                    .Adjustments.Where(value =>
                        Same(value.Selector, "strike-damage") && value.Predicate.Matches(options)
                    )
                    .OrderBy(value => value.Priority)
            )
            {
                if (!values.TryGetValue(adjustment.Slug, out int value))
                    continue;
                if (Same(adjustment.Mode, "upgrade"))
                    values[adjustment.Slug] = Math.Max(
                        value,
                        (int)Math.Round(adjustment.Value, MidpointRounding.ToEven)
                    );
                else if (Same(adjustment.Mode, "multiply"))
                    values[adjustment.Slug] = (int)Math.Floor(value * adjustment.Value);
            }
            string primaryType = item.DamageDice[0].DamageType;
            List<TypedFlatDamage> flat = selected
                .Where(modifier => values[modifier.Slug] != 0)
                .Select(modifier => new TypedFlatDamage(
                    values[modifier.Slug],
                    primaryType,
                    modifier.Slug
                ))
                .ToList();
            if (!item.IsRanged)
            {
                PreparedStrikeModifier ability = prepared.Modifiers.LastOrDefault(modifier =>
                    Same(modifier.Selector, "melee-strike-damage")
                    && !string.IsNullOrWhiteSpace(modifier.Ability)
                    && modifier.Predicate.Matches(options)
                );
                if (ability != null)
                {
                    prepared.Abilities.TryGetValue(ability.Ability, out int desired);
                    int existing = item.FlatDamage.Count == 0 ? 0 : item.FlatDamage[0].Amount;
                    flat.Add(new TypedFlatDamage(desired - existing, primaryType, ability.Ability));
                }
            }
            List<TypedDamageDice> dice = prepared
                .Dice.Where(value =>
                    Same(value.Selector, "strike-damage")
                    && value.Count > 0
                    && value.Sides > 0
                    && value.Predicate.Matches(options)
                )
                .Select(value => new TypedDamageDice(
                    new DiceExpression(value.Count, value.Sides),
                    primaryType,
                    "Prepared damage dice"
                ))
                .ToList();
            return new PreparedStrikeContributions(dice, flat);
        }

        private static void AddConditions(
            HashSet<string> options,
            RulesSnapshot snapshot,
            CreatureId creature,
            string prefix
        )
        {
            foreach (
                ActiveEffectInstance effect in ConditionRules.GetApplications(snapshot, creature)
            )
                options.Add(
                    $"{prefix}:condition:{effect.GetState<ConditionState>().Condition.Value.ToLowerInvariant().Replace(' ', '-')}"
                );
        }

        private static bool Same(string left, string right) =>
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
