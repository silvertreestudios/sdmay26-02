using Game.Creature;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;

namespace Game.Creature.Rules
{
    /// <summary>
    /// Builds the derived PF2e rule state from character choices and verbatim-ish Foundry data items.
    /// </summary>
    public static class Pf2eCharacterPreparer
    {
        /// <summary>
        /// Resolves build choices, grants, roll options, and supported rule elements into a prepared character snapshot.
        /// </summary>
        /// <param name="creature">The Unity creature receiving prepared math and derived options.</param>
        /// <param name="build">The saved choices that select class, subclass, feats, and rule selections.</param>
        /// <param name="catalog">Optional catalog override for tests; production uses the Resources-backed singleton.</param>
        /// <returns>A prepared character that rules can query without reparsing item JSON.</returns>
        public static PreparedCharacter Prepare(CreatureComponent creature, CharacterBuild build, Pf2eItemCatalog catalog = null)
        {
            catalog ??= Pf2eItemCatalog.Instance;
            PreparedCharacter prepared = new(build);
            if (creature == null)
                return prepared;

            prepared.RollOptions.Add($"self:level:{creature.level}");
            AddArmorRollOptions(creature, prepared);
            AddSavedSkillRanks(prepared);

            if (catalog.TryResolveByNameOrSlug(build.ClassName, out Pf2eItem classItem))
            {
                prepared.AddOwnedItem(classItem);
                ApplyClassBaseMath(creature, classItem, prepared);
                GrantClassItems(creature.level, classItem, catalog, prepared);
            }

            if (catalog.TryResolveByNameOrSlug(build.SubclassName, out Pf2eItem subclassItem))
                prepared.AddOwnedItem(subclassItem);

            if (catalog.TryResolveByNameOrSlug(build.ClassFeatName, out Pf2eItem classFeatItem))
                prepared.AddOwnedItem(classFeatItem);

            for (int i = 0; i < 4; i++)
                ProcessGrantRules(prepared, catalog);

            CollectRuleSynthetics(prepared, catalog);
            return prepared;
        }

        /// <summary>
        /// Returns an existing prepared snapshot or prepares one lazily for legacy call sites that only have a CreatureComponent.
        /// </summary>
        /// <param name="creature">The Unity creature that owns the cached prepared character.</param>
        /// <returns>The cached or newly prepared PF2e character state.</returns>
        public static PreparedCharacter EnsurePrepared(CreatureComponent creature)
        {
            if (creature.Prepared == null)
                creature.Prepared = Prepare(creature, creature.Build ?? new CharacterBuild());
            return creature.Prepared;
        }

        private static void ApplyClassBaseMath(CreatureComponent creature, Pf2eItem classItem, PreparedCharacter prepared)
        {
            JObject system = classItem.System;
            if (system == null)
                return;

            ApplyWeaponBonus(creature, "unarmed", system.SelectToken("attacks.unarmed")?.Value<int>() ?? 0);
            ApplyWeaponBonus(creature, "simple", system.SelectToken("attacks.simple")?.Value<int>() ?? 0);
            ApplyWeaponBonus(creature, "martial", system.SelectToken("attacks.martial")?.Value<int>() ?? 0);
            ApplyWeaponBonus(creature, "advanced", system.SelectToken("attacks.advanced")?.Value<int>() ?? 0);

            ApplyArmorBonus(creature, "unarmored", system.SelectToken("defenses.unarmored")?.Value<int>() ?? 0);
            ApplyArmorBonus(creature, "light", system.SelectToken("defenses.light")?.Value<int>() ?? 0);
            ApplyArmorBonus(creature, "medium", system.SelectToken("defenses.medium")?.Value<int>() ?? 0);
            ApplyArmorBonus(creature, "heavy", system.SelectToken("defenses.heavy")?.Value<int>() ?? 0);

            foreach (JToken skill in system.SelectToken("trainedSkills.value") as JArray ?? new JArray())
                UpgradeSkillRank(prepared, skill.Value<string>(), 1);
        }

        private static void ApplyWeaponBonus(CreatureComponent creature, string category, int rank)
        {
            int index = creature.weaponBonuses.FindIndex(b => b.category == category);
            if (index < 0)
                creature.weaponBonuses.Add(new WeaponBonus { category = category, bonus = rank * 2 });
            else
                creature.weaponBonuses[index] = new WeaponBonus { category = category, bonus = rank * 2 };
        }

        private static void ApplyArmorBonus(CreatureComponent creature, string category, int rank)
        {
            int index = creature.armorBonuses.FindIndex(b => b.category == category);
            if (index < 0)
                creature.armorBonuses.Add(new ArmorBonus { category = category, bonus = rank * 2 });
            else
                creature.armorBonuses[index] = new ArmorBonus { category = category, bonus = rank * 2 };
        }

        private static void GrantClassItems(int level, Pf2eItem classItem, Pf2eItemCatalog catalog, PreparedCharacter prepared)
        {
            JObject items = classItem.System?["items"] as JObject;
            if (items == null)
                return;

            foreach (JProperty property in items.Properties())
            {
                JObject grant = property.Value as JObject;
                if (grant == null || grant.Value<int?>("level").GetValueOrDefault(1) > level)
                    continue;

                string uuid = grant.Value<string>("uuid");
                string name = grant.Value<string>("name");
                Pf2eItem granted = catalog.Resolve(uuid) ?? catalog.Resolve(name);
                prepared.AddOwnedItem(granted, classItem.Slug);
            }
        }

        private static void ProcessGrantRules(PreparedCharacter prepared, Pf2eItemCatalog catalog)
        {
            foreach (OwnedPf2eItem owned in prepared.OwnedItems.ToArray())
            {
                foreach (JObject rule in owned.Item.Rules)
                {
                    string key = rule.Value<string>("key");
                    if (key == "ChoiceSet" || key == "GrantItem")
                        ProcessRule(rule, owned.Item, prepared, catalog);
                }
            }
        }

        private static void CollectRuleSynthetics(PreparedCharacter prepared, Pf2eItemCatalog catalog)
        {
            prepared.Modifiers.Clear();
            prepared.Adjustments.Clear();
            prepared.DamageDice.Clear();
            prepared.ItemAlterations.Clear();
            prepared.UnsupportedRuleKeys.Clear();

            foreach (OwnedPf2eItem owned in prepared.OwnedItems.ToArray())
            {
                foreach (JObject rule in owned.Item.Rules)
                {
                    string key = rule.Value<string>("key");
                    if (key != "ChoiceSet" && key != "GrantItem")
                        ProcessRule(rule, owned.Item, prepared, catalog);
                }
            }
        }

        private static void ProcessRule(JObject rule, Pf2eItem source, PreparedCharacter prepared, Pf2eItemCatalog catalog)
        {
            string key = rule.Value<string>("key");
            if (string.IsNullOrWhiteSpace(key))
                return;

            bool predicateMatches = Pf2ePredicate.Evaluate(rule["predicate"], prepared);

            switch (key)
            {
                case "ChoiceSet":
                    if (predicateMatches)
                        ApplyChoiceSet(rule, prepared, catalog);
                    break;
                case "GrantItem":
                    if (predicateMatches)
                        ApplyGrantItem(rule, source, prepared, catalog);
                    break;
                case "ActiveEffectLike":
                    if (predicateMatches)
                        ApplyActiveEffectLike(rule, prepared);
                    break;
                case "FlatModifier":
                    prepared.Modifiers.Add(new RuleModifier
                    {
                        Selector = rule.Value<string>("selector"),
                        Slug = rule.Value<string>("slug") ?? source.Slug,
                        Value = ResolveRuleInt(rule["value"], prepared),
                        Type = rule.Value<string>("type"),
                        Ability = rule.Value<string>("ability"),
                        Predicate = rule["predicate"]?.DeepClone()
                    });
                    break;
                case "AdjustModifier":
                    prepared.Adjustments.Add(new RuleAdjustment
                    {
                        Selector = rule.Value<string>("selector"),
                        Slug = rule.Value<string>("slug"),
                        Mode = rule.Value<string>("mode"),
                        Value = rule.Value<float?>("value") ?? 0,
                        Priority = rule.Value<int?>("priority") ?? 0,
                        Predicate = rule["predicate"]?.DeepClone()
                    });
                    break;
                case "DamageDice":
                    prepared.DamageDice.Add(new RuleDamageDice
                    {
                        Selector = rule.Value<string>("selector"),
                        Category = rule.Value<string>("category"),
                        DiceNumber = ResolveRuleInt(rule["diceNumber"], prepared),
                        DieSize = ResolveDieSize(rule["dieSize"], prepared),
                        Predicate = rule["predicate"]?.DeepClone()
                    });
                    break;
                case "ItemAlteration":
                    prepared.ItemAlterations.Add(new ItemAlterationRule
                    {
                        ItemType = rule.Value<string>("itemType"),
                        Mode = rule.Value<string>("mode"),
                        Property = rule.Value<string>("property"),
                        Value = rule.Value<string>("value"),
                        Predicate = rule["predicate"]?.DeepClone()
                    });
                    break;
                case "RollOption":
                    if (!predicateMatches || rule["toggleable"] != null)
                        break;
                    string option = rule.Value<string>("option");
                    if (!string.IsNullOrWhiteSpace(option) && !option.StartsWith("target:", StringComparison.OrdinalIgnoreCase))
                        prepared.RollOptions.Add(option);
                    break;
                case "TempHP":
                case "Resistance":
                    break;
                default:
                    if (!prepared.UnsupportedRuleKeys.Contains(key))
                        prepared.UnsupportedRuleKeys.Add(key);
                    break;
            }
        }

        private static void ApplyChoiceSet(JObject rule, PreparedCharacter prepared, Pf2eItemCatalog catalog)
        {
            string flag = rule.Value<string>("flag");
            if (string.IsNullOrWhiteSpace(flag) || prepared.Build.RuleSelections.ContainsKey(flag))
                return;

            string selectedName = string.Equals(flag, "roguesRacket", StringComparison.OrdinalIgnoreCase)
                ? prepared.Build.SubclassName
                : prepared.Build.ClassFeatName;
            Pf2eItem selected = catalog.Resolve(selectedName);
            if (selected == null)
                return;

            string pack = selected.Type == "feat" && selected.System?.Value<string>("category") == "class"
                ? "feats-srd"
                : "classfeatures";
            prepared.Build.RuleSelections[flag] = $"Compendium.pf2e.{pack}.Item.{selected.Name}";
        }

        private static void ApplyGrantItem(JObject rule, Pf2eItem source, PreparedCharacter prepared, Pf2eItemCatalog catalog)
        {
            string uuid = ResolveRulesSelectionReference(rule.Value<string>("uuid"), prepared);
            if (string.IsNullOrWhiteSpace(uuid))
                return;

            Pf2eItem granted = catalog.Resolve(uuid);
            prepared.AddOwnedItem(granted, source.Slug);
        }

        private static string ResolveRulesSelectionReference(string uuid, PreparedCharacter prepared)
        {
            if (string.IsNullOrWhiteSpace(uuid) || !uuid.StartsWith("{item|flags.", StringComparison.OrdinalIgnoreCase))
                return uuid;

            const string rulesSelections = ".rulesSelections.";
            int index = uuid.IndexOf(rulesSelections, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return uuid;

            string flag = uuid.Substring(index + rulesSelections.Length).TrimEnd('}');
            return prepared.Build.RuleSelections.TryGetValue(flag, out string selection) ? selection : null;
        }

        private static void ApplyActiveEffectLike(JObject rule, PreparedCharacter prepared)
        {
            string path = rule.Value<string>("path");
            if (string.IsNullOrWhiteSpace(path))
                return;

            int value = ResolveRuleInt(rule["value"], prepared);
            if (path.StartsWith("system.skills.", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".rank", StringComparison.OrdinalIgnoreCase))
            {
                string skill = path.Substring("system.skills.".Length, path.Length - "system.skills.".Length - ".rank".Length);
                UpgradeSkillRank(prepared, skill, value);
                return;
            }

            string normalizedPath = path.StartsWith("@actor.", StringComparison.OrdinalIgnoreCase) ? path.Substring("@actor.".Length) : path;
            if (string.Equals(rule.Value<string>("mode"), "override", StringComparison.OrdinalIgnoreCase))
                prepared.RuleValues[normalizedPath] = value;
            else if (!prepared.RuleValues.TryGetValue(normalizedPath, out int current) || value > current)
                prepared.RuleValues[normalizedPath] = value;
        }

        private static void AddSavedSkillRanks(PreparedCharacter prepared)
        {
            foreach (string skill in prepared.Build.TrainedSkills)
                UpgradeSkillRank(prepared, skill, 1);
        }

        private static void UpgradeSkillRank(PreparedCharacter prepared, string skill, int rank)
        {
            if (string.IsNullOrWhiteSpace(skill) || rank <= 0)
                return;

            if (!prepared.SkillRanks.TryGetValue(skill, out int current) || rank > current)
                prepared.SkillRanks[skill] = rank;
        }

        private static int ResolveRuleInt(JToken token, PreparedCharacter prepared)
        {
            if (token == null || token.Type == JTokenType.Null)
                return 0;
            if (token.Type == JTokenType.Integer)
                return token.Value<int>();

            string value = token.Value<string>();
            if (int.TryParse(value, out int parsed))
                return parsed;
            if (string.IsNullOrWhiteSpace(value))
                return 0;

            if (value.StartsWith("@actor.", StringComparison.OrdinalIgnoreCase))
            {
                string path = value.Substring("@actor.".Length);
                return prepared.RuleValues.TryGetValue(path, out int fact) ? fact : 0;
            }

            if (value.Contains("lt(@actor.level, 5)", StringComparison.OrdinalIgnoreCase))
            {
                int level = GetPreparedLevel(prepared);
                if (level < 5)
                    return 1;
                if (level < 11)
                    return 2;
                if (level < 17)
                    return 3;
                return 4;
            }

            return 0;
        }

        private static int ResolveDieSize(JToken token, PreparedCharacter prepared)
        {
            if (token == null || token.Type == JTokenType.Null)
                return 0;
            if (token.Type == JTokenType.Integer)
                return token.Value<int>();

            string value = token.Value<string>();
            if (string.IsNullOrWhiteSpace(value))
                return 0;
            if (value.StartsWith("d", StringComparison.OrdinalIgnoreCase) && int.TryParse(value.Substring(1), out int sides))
                return sides;
            if (int.TryParse(value, out sides))
                return sides;

            const string actorFlagPrefix = "d{actor|";
            if (value.StartsWith(actorFlagPrefix, StringComparison.OrdinalIgnoreCase) && value.EndsWith("}"))
            {
                string path = value.Substring(actorFlagPrefix.Length, value.Length - actorFlagPrefix.Length - 1);
                return prepared.RuleValues.TryGetValue(path, out int fact) ? fact : 0;
            }

            return 0;
        }

        private static int GetPreparedLevel(PreparedCharacter prepared)
        {
            string levelOption = prepared.RollOptions.FirstOrDefault(option => option.StartsWith("self:level:", StringComparison.OrdinalIgnoreCase));
            if (levelOption != null && int.TryParse(levelOption.Substring("self:level:".Length), out int level))
                return level;
            return 0;
        }

        private static void AddArmorRollOptions(CreatureComponent creature, PreparedCharacter prepared)
        {
            string category = creature.equippedArmor?.category;
            if (!string.IsNullOrWhiteSpace(category))
                prepared.RollOptions.Add($"armor:category:{category}");
        }
    }
}
