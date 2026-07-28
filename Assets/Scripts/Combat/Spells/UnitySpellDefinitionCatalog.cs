using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Creature.Rules;
using Game.Rules.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;
using RulesSpellDefinition = Game.Rules.Runtime.SpellDefinition;

namespace Game.Combat.Spells
{
    /// <summary>Loads immutable generic spell definitions from the project spell JSON.</summary>
    public sealed class UnitySpellDefinitionCatalog : IActionCatalog, ISpellDefinitionCatalog
    {
        private static readonly Regex NumericFootRange = new(
            @"^(?<feet>[1-9]\d*)\s+feet$",
            RegexOptions.CultureInvariant
        );
        private static readonly Regex ImmediateDiceFormula = new(
            @"^(?<dice>[1-9]\d*)d(?<sides>[1-9]\d*)$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
        );
        private readonly IReadOnlyDictionary<SpellId, RulesSpellDefinition> definitions;

        /// <summary>Loads every data-backed spell definition currently available through Resources.</summary>
        public static UnitySpellDefinitionCatalog Load()
        {
            Dictionary<SpellId, RulesSpellDefinition> values = new();
            foreach (TextAsset asset in Resources.LoadAll<TextAsset>("DataFiles/spells"))
            {
                RulesSpellDefinition definition = Parse(asset.text);
                if (
                    values.TryGetValue(definition.Id, out RulesSpellDefinition existing)
                    && !Equivalent(existing, definition)
                )
                {
                    throw new InvalidOperationException(
                        $"Conflicting spell definition '{definition.Id}'."
                    );
                }
                values[definition.Id] = definition;
            }
            return new UnitySpellDefinitionCatalog(values.Values);
        }

        /// <summary>Creates a catalog from already validated immutable definitions.</summary>
        public UnitySpellDefinitionCatalog(IEnumerable<RulesSpellDefinition> definitions)
        {
            if (definitions == null)
                throw new ArgumentNullException(nameof(definitions));
            this.definitions = definitions.ToDictionary(definition => definition.Id);
        }

        /// <summary>Enumerates every distinct data-backed definition in the catalog.</summary>
        public IEnumerable<RulesSpellDefinition> Definitions => definitions.Values;

        /// <inheritdoc/>
        public bool TryGetSpell(SpellReference reference, out RulesSpellDefinition definition)
        {
            if (
                definitions.TryGetValue(reference.Spell, out definition)
                && reference.Rank >= definition.MinimumRank
                && (definition.Attacks.Count == 0 || reference.Rank == definition.MinimumRank)
            )
                return true;
            definition = null;
            return false;
        }

        /// <inheritdoc/>
        public ActionProfile GetBaseProfile(ActionDefinitionId definitionId) =>
            throw new KeyNotFoundException(
                $"Spell profiles require a selected SpellReference and variant, not '{definitionId}'."
            );

        private static RulesSpellDefinition Parse(string json)
        {
            JObject root = JObject.Parse(json);
            string name =
                root.Value<string>("name")
                ?? throw new InvalidOperationException("A spell JSON entry requires a name.");
            JObject system =
                root["system"] as JObject
                ?? throw new InvalidOperationException($"Spell '{name}' requires system data.");
            SpellId id = new(Game.Creature.Rules.Pf2eSlug.FromName(name));
            int rank = system.SelectToken("level.value")?.Value<int>() ?? 0;
            if (rank <= 0)
                throw new InvalidOperationException($"Spell '{name}' requires a positive rank.");
            string time = system.SelectToken("time.value")?.Value<string>() ?? string.Empty;
            Trait[] traits = (
                system.SelectToken("traits.value")?.Values<string>() ?? Enumerable.Empty<string>()
            )
                .Select(Trait.FromSlug)
                .ToArray();
            List<SpellEffectDirective> effects = new();
            List<SpellAttackDefinition> attacks = new();
            foreach (
                JObject rule in system["rules"]?.Children<JObject>() ?? Enumerable.Empty<JObject>()
            )
            {
                string key = rule.Value<string>("key") ?? string.Empty;
                if (string.Equals(key, "ResolveSpellAttack", StringComparison.Ordinal))
                {
                    if (attacks.Count > 0)
                        throw new InvalidOperationException(
                            $"Spell '{name}' contains multiple spell-attack directives."
                        );
                    attacks.Add(ParseAttack(name, system, traits));
                    continue;
                }
                if (!string.Equals(key, "CreateActiveEffect", StringComparison.Ordinal))
                    continue;
                if (!string.Equals(rule.Value<string>("target"), "self", StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Spell '{name}' contains an unsupported active-effect target."
                    );
                string definition = rule.Value<string>("definition");
                effects.Add(
                    new SpellEffectDirective(
                        new RuleDefinitionId(definition),
                        ParseDuration(system.SelectToken("duration.value")?.Value<string>()),
                        rule.Value<string>("target")
                    )
                );
            }
            return new RulesSpellDefinition(
                id,
                name,
                rank,
                ParseVariants(time),
                traits,
                effects,
                attacks
            );
        }

        private static SpellAttackDefinition ParseAttack(
            string name,
            JObject system,
            IReadOnlyCollection<Trait> traits
        )
        {
            if (!traits.Contains(Trait.FromSlug("attack")))
                throw new InvalidOperationException(
                    $"Spell '{name}' opts into spell-attack resolution without the attack trait."
                );
            if (
                !string.Equals(
                    system.SelectToken("target.value")?.Value<string>()?.Trim(),
                    "1 creature",
                    StringComparison.Ordinal
                )
            )
                throw new InvalidOperationException(
                    $"Spell '{name}' requires the unsupported spell-attack target shape."
                );
            string range =
                system.SelectToken("range.value")?.Value<string>()?.Trim() ?? string.Empty;
            Match rangeMatch = NumericFootRange.Match(range);
            if (!rangeMatch.Success)
                throw new InvalidOperationException(
                    $"Spell '{name}' requires a numeric-foot spell-attack range."
                );
            if (system["defense"]?.Type is JTokenType defenseType && defenseType != JTokenType.Null)
                throw new InvalidOperationException(
                    $"Spell '{name}' contains an unsupported spell-attack defense."
                );
            if (system["overlays"] is JObject overlays && overlays.Properties().Any())
                throw new InvalidOperationException(
                    $"Spell '{name}' contains unsupported spell overlays."
                );
            if (system["damage"] is not JObject damage || !damage.Properties().Any())
                throw new InvalidOperationException(
                    $"Spell '{name}' requires immediate typed spell-attack damage."
                );

            List<SpellAttackDamageComponent> components = new();
            foreach (JProperty property in damage.Properties())
            {
                if (property.Value is not JObject component)
                    throw new InvalidOperationException(
                        $"Spell '{name}' contains malformed spell-attack damage."
                    );
                Match formula = ImmediateDiceFormula.Match(
                    component.Value<string>("formula")?.Trim() ?? string.Empty
                );
                string damageType = component.Value<string>("type")?.Trim() ?? string.Empty;
                string[] kinds =
                    component["kinds"]?.Values<string>().ToArray() ?? Array.Empty<string>();
                bool unsupported =
                    !formula.Success
                    || string.IsNullOrWhiteSpace(damageType)
                    || component.Value<bool?>("applyMod") != false
                    || (
                        component["category"] != null
                        && component["category"].Type != JTokenType.Null
                    )
                    || kinds.Length != 1
                    || !string.Equals(kinds[0], "damage", StringComparison.Ordinal)
                    || (component["materials"]?.Any() ?? false);
                if (unsupported)
                    throw new InvalidOperationException(
                        $"Spell '{name}' contains unsupported spell-attack damage."
                    );
                components.Add(
                    new SpellAttackDamageComponent(
                        int.Parse(formula.Groups["dice"].Value),
                        int.Parse(formula.Groups["sides"].Value),
                        damageType
                    )
                );
            }
            return new SpellAttackDefinition(
                new OneCreatureSpellAttackTarget(int.Parse(rangeMatch.Groups["feet"].Value)),
                components
            );
        }

        private static IReadOnlyList<SpellActionVariant> ParseVariants(string time)
        {
            if (int.TryParse(time, out int actions))
                return new[] { new SpellActionVariant(actions) };
            if (string.Equals(time?.Trim(), "1 to 3", StringComparison.Ordinal))
            {
                return new[]
                {
                    new SpellActionVariant(1),
                    new SpellActionVariant(2),
                    new SpellActionVariant(3),
                };
            }
            return new[] { new SpellActionVariant(1) };
        }

        private static EffectDuration ParseDuration(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return EffectDuration.Indefinite;
            string normalized = value.Trim().ToLowerInvariant();
            if (normalized == "1 minute")
                return EffectDuration.OneMinute;
            if (normalized.EndsWith(" minutes", StringComparison.Ordinal))
            {
                string count = normalized.Substring(0, normalized.Length - " minutes".Length);
                if (int.TryParse(count, out int minutes) && minutes > 0)
                    return EffectDuration.Minutes(minutes);
            }
            return EffectDuration.Indefinite;
        }

        private static bool Equivalent(RulesSpellDefinition left, RulesSpellDefinition right) =>
            left.DisplayName == right.DisplayName
            && left.MinimumRank == right.MinimumRank
            && left.Variants.SequenceEqual(right.Variants)
            && left.Traits.SequenceEqual(right.Traits)
            && left.Effects.Select(effect => effect.DefinitionId)
                .SequenceEqual(right.Effects.Select(effect => effect.DefinitionId))
            && left.Effects.Select(effect => effect.Duration)
                .SequenceEqual(right.Effects.Select(effect => effect.Duration))
            && left.Effects.Select(effect => effect.Target)
                .SequenceEqual(right.Effects.Select(effect => effect.Target))
            && left.Attacks.Count == right.Attacks.Count
            && left.Attacks.Zip(
                    right.Attacks,
                    (leftAttack, rightAttack) => Equivalent(leftAttack, rightAttack)
                )
                .All(value => value);

        private static bool Equivalent(SpellAttackDefinition left, SpellAttackDefinition right) =>
            left.Target is OneCreatureSpellAttackTarget leftTarget
            && right.Target is OneCreatureSpellAttackTarget rightTarget
            && leftTarget.RangeFeet == rightTarget.RangeFeet
            && left.Damage.Select(component =>
                    (component.Dice, component.Sides, component.DamageType)
                )
                .SequenceEqual(
                    right.Damage.Select(component =>
                        (component.Dice, component.Sides, component.DamageType)
                    )
                );
    }
}
