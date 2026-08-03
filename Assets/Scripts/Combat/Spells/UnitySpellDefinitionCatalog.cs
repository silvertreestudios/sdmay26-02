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
                if (definition == null)
                    continue;
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
                && (
                    definition.Attacks.Count == 0 && definition.Saves.Count == 0
                    || reference.Rank == definition.MinimumRank
                )
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

        internal static RulesSpellDefinition Parse(string json)
        {
            JObject root = JObject.Parse(json);
            string name =
                root.Value<string>("name")
                ?? throw new InvalidOperationException("A spell JSON entry requires a name.");
            JObject system =
                root["system"] as JObject
                ?? throw new InvalidOperationException($"Spell '{name}' requires system data.");
            if (system.Value<bool?>("rulesNativeReady") != true)
                return null;
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
            List<SpellSaveConditionDirective> saveConditions = new();
            List<SpellSaveDefinition> saves = new();
            foreach (
                JObject rule in system["rules"]?.Children<JObject>() ?? Enumerable.Empty<JObject>()
            )
            {
                string key = rule.Value<string>("key") ?? string.Empty;
                if (string.Equals(key, "ApplyConditionOnSave", StringComparison.Ordinal))
                {
                    saveConditions.Add(ParseSaveCondition(name, rule));
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
                        ParseDuration(name, system.SelectToken("duration.value")?.Value<string>()),
                        rule.Value<string>("target"),
                        ParseMaximumActiveInstances(name, rule)
                    )
                );
            }
            bool authorsAttack = traits.Contains(Trait.FromSlug("attack"));
            bool authorsSave = HasAuthoredSave(system);
            int authoredResolutionCategories =
                (effects.Count > 0 ? 1 : 0) + (authorsAttack ? 1 : 0) + (authorsSave ? 1 : 0);
            if (authoredResolutionCategories != 1)
                throw new InvalidOperationException(
                    $"Spell '{name}' requires exactly one authored resolution category."
                );
            if (authorsAttack)
            {
                if (!TryParseAttack(id, system, traits, out SpellAttackDefinition attack))
                    throw new InvalidOperationException(
                        $"Spell '{name}' has an incomplete or unsupported authored attack category."
                    );
                attacks.Add(attack);
            }
            if (authorsSave)
            {
                if (!TryParseSave(id, system, saveConditions, out SpellSaveDefinition save))
                    throw new InvalidOperationException(
                        $"Spell '{name}' has an incomplete or unsupported authored save category."
                    );
                saves.Add(save);
            }
            return new RulesSpellDefinition(
                id,
                name,
                rank,
                ParseVariants(name, time),
                traits,
                effects,
                attacks,
                saves
            );
        }

        private static bool HasAuthoredSave(JObject system) =>
            system["defense"] is JObject defense && defense.Property("save") != null;

        private static SpellSaveConditionDirective ParseSaveCondition(
            string spellName,
            JObject rule
        )
        {
            string condition = rule.Value<string>("condition");
            if (!ConditionInputNormalizer.TryNormalize(condition, out RuleDefinitionId definition))
                throw new InvalidOperationException(
                    $"Spell '{spellName}' contains an unsupported save condition."
                );
            if (
                !Enum.TryParse(
                    rule.Value<string>("degree"),
                    ignoreCase: true,
                    out DegreeOfSuccess degree
                )
            )
                throw new InvalidOperationException(
                    $"Spell '{spellName}' contains an unsupported save degree."
                );
            return new SpellSaveConditionDirective(
                definition,
                degree,
                ParseDuration(spellName, rule.Value<string>("duration")),
                ConditionMarkerState.Instance
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
            if (!TryParseDamage(spell, system, out IReadOnlyList<TypedDamageDice> components))
                return false;
            attack = new SpellAttackDefinition(
                new OneCreatureSpellAttackTarget(rangeFeet),
                components
            );
            return true;
        }

        private static bool TryParseSave(
            SpellId spell,
            JObject system,
            IReadOnlyList<SpellSaveConditionDirective> conditions,
            out SpellSaveDefinition save
        )
        {
            save = null;
            JToken saveToken = system.SelectToken("defense.save");
            if (saveToken == null || saveToken.Type == JTokenType.Null)
                return false;
            if (saveToken is not JObject saveObject || saveObject.Value<bool?>("basic") != true)
                return false;
            if (
                !Enum.TryParse(
                    saveObject.Value<string>("statistic"),
                    ignoreCase: true,
                    out SaveKind saveKind
                )
                || system["area"] is not JObject area
                || !TryParseAreaShape(area.Value<string>("type"), out SpellAreaShape shape)
                || area.Value<int?>("value") is not int sizeFeet
                || sizeFeet <= 0
                || !TryParseDamage(spell, system, out IReadOnlyList<TypedDamageDice> damage)
            )
                return false;
            int rangeFeet = 0;
            if (
                shape == SpellAreaShape.Burst
                && !DistanceValues.TryParseFeet(
                    system.SelectToken("range.value")?.Value<string>(),
                    out rangeFeet
                )
            )
                return false;
            save = new SpellSaveDefinition(
                saveKind,
                isBasic: true,
                new SpellAreaTarget(shape, sizeFeet, rangeFeet),
                damage,
                conditions
            );
            return true;
        }

        private static bool TryParseAreaShape(string value, out SpellAreaShape shape)
        {
            switch (value?.Trim().ToLowerInvariant())
            {
                case "cone":
                    shape = SpellAreaShape.Cone;
                    return true;
                case "burst":
                    shape = SpellAreaShape.Burst;
                    return true;
                case "emanation":
                    shape = SpellAreaShape.Emanation;
                    return true;
                case "line":
                    shape = SpellAreaShape.Line;
                    return true;
                default:
                    shape = default;
                    return false;
            }
        }

        private static bool TryParseDamage(
            SpellId spell,
            JObject system,
            out IReadOnlyList<TypedDamageDice> components
        )
        {
            List<TypedDamageDice> parsed = new();
            components = parsed;
            if (system["damage"] is not JObject damage || !damage.Properties().Any())
                return false;
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
                parsed.Add(new TypedDamageDice(dice, damageType, spell.Value));
            }
            return true;
        }

        private static IReadOnlyList<SpellActionVariant> ParseVariants(
            string spellName,
            string time
        )
        {
            string normalized = time?.Trim() ?? string.Empty;
            switch (normalized)
            {
                case "1":
                    return new[] { new SpellActionVariant(1) };
                case "2":
                    return new[] { new SpellActionVariant(2) };
                case "3":
                    return new[] { new SpellActionVariant(3) };
                case "1 to 3":
                    return new[]
                    {
                        new SpellActionVariant(1),
                        new SpellActionVariant(2),
                        new SpellActionVariant(3),
                    };
                case "":
                    throw new InvalidOperationException(
                        $"Spell '{spellName}' requires a casting-time value."
                    );
                default:
                    throw new InvalidOperationException(
                        $"Spell '{spellName}' has unsupported casting time '{normalized}'."
                    );
            }
        }

        private static EffectDuration ParseDuration(string spellName, string value)
        {
            string normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
            switch (normalized)
            {
                case "1 minute":
                    return EffectDuration.OneMinute;
                case "until your next daily preparations":
                    return EffectDuration.Indefinite;
                case "":
                    throw new InvalidOperationException(
                        $"Spell '{spellName}' requires an effect-duration value."
                    );
            }
            const string minutesSuffix = " minutes";
            if (
                normalized.EndsWith(minutesSuffix, StringComparison.Ordinal)
                && int.TryParse(
                    normalized.Substring(0, normalized.Length - minutesSuffix.Length),
                    out int minutes
                )
                && minutes > 1
            )
                return EffectDuration.Minutes(minutes);
            throw new InvalidOperationException(
                $"Spell '{spellName}' has unsupported effect duration '{normalized}'."
            );
        }

        private static int? ParseMaximumActiveInstances(string spellName, JObject rule)
        {
            JToken token = rule["maximumActiveInstances"];
            if (token == null)
                return null;
            if (
                token.Type != JTokenType.Integer
                || !long.TryParse(token.ToString(), out long parsed)
                || parsed <= 0
                || parsed > int.MaxValue
            )
                throw new InvalidOperationException(
                    $"Spell '{spellName}' requires maximumActiveInstances to be a positive integer when supplied."
                );
            return (int)parsed;
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
            && left.Effects.Select(effect => effect.MaximumActiveInstances)
                .SequenceEqual(right.Effects.Select(effect => effect.MaximumActiveInstances))
            && left.Attacks.Count == right.Attacks.Count
            && left.Attacks.Zip(
                    right.Attacks,
                    (leftAttack, rightAttack) => Equivalent(leftAttack, rightAttack)
                )
                .All(value => value)
            && left.Saves.Count == right.Saves.Count
            && left.Saves.Zip(right.Saves, Equivalent).All(value => value);

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

        private static bool Equivalent(SpellSaveDefinition left, SpellSaveDefinition right) =>
            left.Save == right.Save
            && left.IsBasic == right.IsBasic
            && left.Target.Shape == right.Target.Shape
            && left.Target.SizeFeet == right.Target.SizeFeet
            && left.Target.RangeFeet == right.Target.RangeFeet
            && left.Damage.Select(component =>
                    (component.Dice, component.DamageType, component.Source)
                )
                .SequenceEqual(
                    right.Damage.Select(component =>
                        (component.Dice, component.DamageType, component.Source)
                    )
                )
            && left.Conditions.Select(condition =>
                    (condition.DefinitionId, condition.Degree, condition.Duration, condition.State)
                )
                .SequenceEqual(
                    right.Conditions.Select(condition =>
                        (
                            condition.DefinitionId,
                            condition.Degree,
                            condition.Duration,
                            condition.State
                        )
                    )
                );
    }
}
