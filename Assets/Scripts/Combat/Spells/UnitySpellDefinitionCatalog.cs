using System;
using System.Collections.Generic;
using System.Linq;
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
            if (TryParseAttack(id, system, traits, out SpellAttackDefinition attack))
                attacks.Add(attack);
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

        private static bool TryParseAttack(
            SpellId spell,
            JObject system,
            IReadOnlyCollection<Trait> traits,
            out SpellAttackDefinition attack
        )
        {
            attack = null;
            if (!traits.Contains(Trait.FromSlug("attack")))
                return false;
            if (
                !string.Equals(
                    system.SelectToken("target.value")?.Value<string>()?.Trim(),
                    "1 creature",
                    StringComparison.Ordinal
                )
            )
                return false;
            if (
                !DistanceValues.TryParseFeet(
                    system.SelectToken("range.value")?.Value<string>(),
                    out int rangeFeet
                )
            )
                return false;
            if (
                !system.TryGetValue("defense", out JToken defense)
                || defense.Type != JTokenType.Null
            )
                return false;
            if (system["overlays"] is JToken overlaysToken)
            {
                switch (overlaysToken)
                {
                    case JValue { Type: JTokenType.Null }:
                    case JObject overlays when !overlays.Properties().Any():
                        break;
                    default:
                        return false;
                }
            }
            if (system["damage"] is not JObject damage || !damage.Properties().Any())
                return false;

            List<TypedDamageDice> components = new();
            foreach (JProperty property in damage.Properties())
            {
                if (property.Value is not JObject component)
                    return false;
                bool parsedDice = DiceExpression.TryParse(
                    component.Value<string>("formula"),
                    out DiceExpression dice
                );
                string damageType = component.Value<string>("type")?.Trim() ?? string.Empty;
                string[] kinds =
                    component["kinds"]?.Values<string>().ToArray() ?? Array.Empty<string>();
                bool hasUnsupportedMaterials =
                    component["materials"] is JToken materials
                    && materials.Type != JTokenType.Null
                    && (materials is not JArray materialArray || materialArray.Count != 0);
                bool unsupported =
                    !parsedDice
                    || string.IsNullOrWhiteSpace(damageType)
                    || component.Value<bool?>("applyMod") != false
                    || (
                        component["category"] != null
                        && component["category"].Type != JTokenType.Null
                    )
                    || kinds.Length != 1
                    || !string.Equals(kinds[0], "damage", StringComparison.Ordinal)
                    || hasUnsupportedMaterials;
                if (unsupported)
                    return false;
                components.Add(new TypedDamageDice(dice, damageType, spell.Value));
            }
            attack = new SpellAttackDefinition(
                new OneCreatureSpellAttackTarget(rangeFeet),
                components
            );
            return true;
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
                    (component.Dice, component.DamageType, component.Source)
                )
                .SequenceEqual(
                    right.Damage.Select(component =>
                        (component.Dice, component.DamageType, component.Source)
                    )
                );
    }
}
