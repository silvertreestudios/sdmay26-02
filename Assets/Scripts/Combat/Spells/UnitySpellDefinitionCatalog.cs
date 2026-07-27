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
            foreach (
                JObject rule in system["rules"]?.Children<JObject>() ?? Enumerable.Empty<JObject>()
            )
            {
                if (
                    !string.Equals(
                        rule.Value<string>("key"),
                        "CreateActiveEffect",
                        StringComparison.Ordinal
                    )
                )
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
            return new RulesSpellDefinition(id, name, rank, ParseVariants(time), traits, effects);
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
                .SequenceEqual(right.Effects.Select(effect => effect.Target));
    }
}
