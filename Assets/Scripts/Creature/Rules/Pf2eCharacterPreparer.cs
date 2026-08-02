using System;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Spells;
using Game.Creature;
using Game.Rules.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.Creature.Rules
{
    /// <summary>
    /// Builds the derived PF2e rule state from character choices and verbatim-ish Foundry data items.
    /// </summary>
    public static class Pf2eCharacterPreparer
    {
        private sealed class OwnedPf2eItem
        {
            internal OwnedPf2eItem(Pf2eItem item, string grantedBy)
            {
                Item = item ?? throw new ArgumentNullException(nameof(item));
                GrantedBy = grantedBy ?? string.Empty;
            }

            internal Pf2eItem Item { get; }
            internal string GrantedBy { get; }
        }

        /// <summary>
        /// Builds only quarantined spellcasting state after compiling migrated rules data.
        /// </summary>
        /// <remarks>
        /// This explicit non-enrollment entry point also projects implemented build defaults and
        /// class proficiency math for legacy Unity consumers. Encounter enrollment calls
        /// <see cref="Compile"/> and therefore performs no such mutation.
        /// </remarks>
        /// <param name="creature">The Unity creature receiving prepared math and derived options.</param>
        /// <param name="build">The saved choices that select class, subclass, feats, and rule selections.</param>
        /// <param name="catalog">Optional catalog override for tests; production uses the Resources-backed singleton.</param>
        /// <returns>Deferred spellcasting and persisted-effect state; it is not rules authority.</returns>
        public static PreparedCharacter Prepare(
            CreatureComponent creature,
            CharacterBuild build,
            Pf2eItemCatalog catalog = null
        )
        {
            PreparedRulePackage compiled = CompileCore(
                creature,
                build,
                catalog,
                out CharacterBuild normalizedBuild
            );
            ProjectLegacyPreparation(creature, build, normalizedBuild, compiled.Inputs);
            PreparedCharacter prepared = new();
            PrepareImplementedSpellcasting(creature, prepared, compiled);
            return prepared;
        }

        /// <summary>
        /// Deterministically compiles all migrated character facts and rule bindings without
        /// mutating the supplied Unity creature or saved build.
        /// </summary>
        /// <param name="creature">The Unity creature whose current inputs are copied.</param>
        /// <param name="build">The saved build choices copied for compiler-owned normalization.</param>
        /// <param name="catalog">Optional immutable catalog override used by tests.</param>
        /// <returns>A fully immutable prepared-rules package.</returns>
        public static PreparedRulePackage Compile(
            CreatureComponent creature,
            CharacterBuild build,
            Pf2eItemCatalog catalog = null
        )
        {
            return CompileCore(creature, build, catalog, out _);
        }

        private static PreparedRulePackage CompileCore(
            CreatureComponent creature,
            CharacterBuild build,
            Pf2eItemCatalog catalog,
            out CharacterBuild normalizedBuild
        )
        {
            catalog ??= Pf2eItemCatalog.Instance;
            normalizedBuild = CopyBuild(build);
            PreparedRulesBuilder prepared = new(creature, normalizedBuild);
            if (creature == null)
                return prepared.Freeze();

            ApplyImplementedBuildDefaults(normalizedBuild);
            prepared.RollOptions.Add($"self:level:{creature.level}");
            AddArmorRollOptions(creature, prepared);
            AddSavedSkillRanks(prepared);
            AddEquipmentOwnership(creature, prepared, catalog);

            if (catalog.TryResolveByNameOrSlug(normalizedBuild.ClassName, out Pf2eItem classItem))
            {
                prepared.AddOwnedItem(classItem);
                CompileClassBaseMath(classItem, prepared);
                GrantClassItems(creature.level, classItem, catalog, prepared);
            }

            if (
                catalog.TryResolveByNameOrSlug(
                    normalizedBuild.SubclassName,
                    out Pf2eItem subclassItem
                )
            )
                prepared.AddOwnedItem(subclassItem);

            if (
                catalog.TryResolveByNameOrSlug(
                    normalizedBuild.ClassFeatName,
                    out Pf2eItem classFeatItem
                )
            )
                prepared.AddOwnedItem(classFeatItem);

            for (int i = 0; i < 4; i++)
                ProcessGrantRules(prepared, catalog);

            CollectRuleSynthetics(prepared, catalog);
            return prepared.Freeze();
        }

        private static CharacterBuild CopyBuild(CharacterBuild source)
        {
            CharacterBuild copy = new()
            {
                ClassName = source?.ClassName,
                SubclassName = source?.SubclassName,
                ClassFeatName = source?.ClassFeatName,
            };
            if (source == null)
                return copy;
            foreach (KeyValuePair<string, string> selection in source.RuleSelections)
                copy.RuleSelections.Add(selection.Key, selection.Value);
            copy.TrainedSkills.AddRange(source.TrainedSkills);
            return copy;
        }

        /// <summary>Enumerates every catalog-backed definition before immutable registry construction.</summary>
        internal static IEnumerable<PreparedRuleDefinitionSpec> CompileDefinitionSpecs(
            Pf2eItemCatalog catalog
        )
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            foreach (
                Pf2eItem item in catalog.Items.OrderBy(value => value.Slug, StringComparer.Ordinal)
            )
            {
                yield return CreateDefinitionSpec(item, "owned", -1);
                JObject[] rules = item.Rules.ToArray();
                for (int index = 0; index < rules.Length; index++)
                {
                    JObject rule = rules[index];
                    string key = rule.Value<string>("key");
                    if (CreatesPreparedBinding(rule, key))
                        yield return CreateDefinitionSpec(item, key, index);
                }
            }
        }

        private static bool CreatesPreparedBinding(JObject rule, string key)
        {
            if (rule == null || string.IsNullOrWhiteSpace(key))
                return false;
            if (
                key == "FlatModifier"
                || key == "AdjustModifier"
                || key == "DamageDice"
                || key == "ItemAlteration"
            )
                return true;
            if (key != "RollOption" || rule["toggleable"] != null)
                return false;
            string option = rule.Value<string>("option");
            return !string.IsNullOrWhiteSpace(option)
                && !option.StartsWith("target:", StringComparison.OrdinalIgnoreCase);
        }

        private static PreparedRuleDefinitionSpec CreateDefinitionSpec(
            Pf2eItem source,
            string key,
            int index
        )
        {
            string stableKey =
                index < 0
                    ? $"{source.Slug}:owned"
                    : $"{source.Slug}:{index}:{Pf2eSlug.FromName(key)}";
            JObject rule = index < 0 ? null : source.Rules.ElementAt(index);
            RuleDefinitionId id = new($"prepared:{stableKey}");
            PreparedPredicate predicate = Pf2ePredicate.Compile(rule?["predicate"]);
            List<PreparedModifierSpec> modifiers = new();
            List<PreparedAdjustmentSpec> adjustments = new();
            List<PreparedDamageDiceSpec> damageDice = new();
            List<PreparedItemAlterationSpec> alterations = new();
            if (rule != null)
            {
                switch (key)
                {
                    case "FlatModifier":
                        modifiers.Add(
                            new PreparedModifierSpec(
                                id,
                                rule.Value<string>("selector"),
                                rule.Value<string>("slug") ?? source.Slug,
                                LiteralInt(rule["value"]),
                                rule.Value<string>("type"),
                                rule.Value<string>("ability"),
                                predicate
                            )
                        );
                        break;
                    case "AdjustModifier":
                        adjustments.Add(
                            new PreparedAdjustmentSpec(
                                id,
                                rule.Value<string>("selector"),
                                rule.Value<string>("slug"),
                                rule.Value<string>("mode"),
                                rule.Value<float?>("value") ?? 0,
                                rule.Value<int?>("priority") ?? 0,
                                predicate
                            )
                        );
                        break;
                    case "DamageDice":
                        string diceText = rule.Value<string>("diceNumber");
                        string sidesText = rule.Value<string>("dieSize");
                        damageDice.Add(
                            new PreparedDamageDiceSpec(
                                id,
                                rule.Value<string>("selector"),
                                rule.Value<string>("category"),
                                LiteralInt(rule["diceNumber"]),
                                ParseDieSize(sidesText),
                                predicate,
                                ActorFact(diceText),
                                ActorDieFact(sidesText)
                            )
                        );
                        break;
                    case "ItemAlteration":
                        alterations.Add(
                            new PreparedItemAlterationSpec(
                                id,
                                rule.Value<string>("itemType"),
                                rule.Value<string>("mode"),
                                rule.Value<string>("property"),
                                rule.Value<string>("value"),
                                predicate
                            )
                        );
                        break;
                }
            }
            return new PreparedRuleDefinitionSpec(
                id,
                RuleSource.FromSlug(source.Slug),
                key,
                index < 0 ? source.Name : $"{source.Slug}#rule-{index}",
                rule?.ToString(Formatting.None) ?? $"owned:{source.Slug}",
                modifiers,
                adjustments,
                damageDice,
                alterations
            );
        }

        private static int ParseDieSize(string value) =>
            !string.IsNullOrWhiteSpace(value)
            && value.StartsWith("d", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(value.Substring(1), out int sides)
                ? sides
                : 0;

        private static int LiteralInt(JToken value) =>
            value?.Type == JTokenType.Integer ? value.Value<int>() : 0;

        private static string ActorFact(string value) =>
            !string.IsNullOrWhiteSpace(value)
            && value.StartsWith("@actor.", StringComparison.OrdinalIgnoreCase)
                ? value.Substring("@actor.".Length)
                : string.Empty;

        private static string ActorDieFact(string value)
        {
            const string prefix = "d{actor|";
            return
                !string.IsNullOrWhiteSpace(value)
                && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && value.EndsWith("}", StringComparison.Ordinal)
                ? value.Substring(prefix.Length, value.Length - prefix.Length - 1)
                : string.Empty;
        }

        private static void ApplyImplementedBuildDefaults(CharacterBuild build)
        {
            if (
                build == null
                || !string.Equals(build.ClassName, "Cleric", StringComparison.OrdinalIgnoreCase)
            )
                return;

            if (
                string.IsNullOrWhiteSpace(build.SubclassName)
                || string.Equals(
                    build.SubclassName,
                    "Cloistered",
                    StringComparison.OrdinalIgnoreCase
                )
            )
                build.SubclassName = "Cloistered Cleric";
            if (string.IsNullOrWhiteSpace(build.ClassFeatName))
                build.ClassFeatName = "Domain Initiate";

            build.RuleSelections["doctrine"] =
                "Compendium.pf2e.classfeatures.Item.Cloistered Cleric";
            build.RuleSelections["divineFont"] = "heal";
            build.RuleSelections["sanctification"] = "none";
        }

        private static void PrepareImplementedSpellcasting(
            CreatureComponent creature,
            PreparedCharacter prepared,
            PreparedRulePackage compiled
        )
        {
            if (
                !compiled.Inputs.BoundOptions.Any(value =>
                    string.Equals(value.Option, "item:owned:cleric", StringComparison.Ordinal)
                )
            )
                return;

            PrepareLegacySpellcasting(creature, prepared);
            int spellAttackModifier = SpellcastingRuntime.SpellAttackModifier(creature);
            prepared.SpellBook = new PreparedSpellBook(
                new[]
                {
                    PreparedSpellEntry.Cantrip(Reference("light")),
                    PreparedSpellEntry.Cantrip(Reference("divine-lance")),
                },
                Array.Empty<PreparedSpellSlotPool>(),
                spellAttackModifier
            );
        }

        private static void PrepareLegacySpellcasting(
            CreatureComponent creature,
            PreparedCharacter prepared
        )
        {
            SpellcastingState spellcasting = new()
            {
                Tradition = "divine",
                Ability = "wis",
                SpellAttackModifier = SpellcastingRuntime.SpellAttackModifier(creature),
            };
            spellcasting.AddPool(new SpellSlotPool("rank-1-bless", SpellSlotKind.Prepared, 1, 1));
            spellcasting.AddPool(
                new SpellSlotPool("rank-1-infuse-vitality", SpellSlotKind.Prepared, 1, 1)
            );
            spellcasting.AddPool(new SpellSlotPool("font-heal", SpellSlotKind.Font, 1, 4));

            spellcasting.AddSpell(
                new PreparedSpell("Shield", 1, true, false, string.Empty, new[] { 1u })
            );
            spellcasting.AddSpell(
                new PreparedSpell("Guidance", 1, true, false, string.Empty, new[] { 1u })
            );
            spellcasting.AddSpell(
                new PreparedSpell("Haunting Hymn", 1, true, false, string.Empty, new[] { 2u })
            );
            spellcasting.AddSpell(
                new PreparedSpell("Bless", 1, false, false, "rank-1-bless", new[] { 2u })
            );
            spellcasting.AddSpell(
                new PreparedSpell(
                    "Infuse Vitality",
                    1,
                    false,
                    false,
                    "rank-1-infuse-vitality",
                    new[] { 1u, 2u, 3u }
                )
            );
            spellcasting.AddSpell(
                new PreparedSpell("Heal", 1, false, true, "font-heal", new[] { 1u, 2u, 3u })
            );

            prepared.Spellcasting = spellcasting;
        }

        private static SpellReference Reference(string slug) => new(new SpellId(slug), 1);

        private static void CompileClassBaseMath(Pf2eItem classItem, PreparedRulesBuilder prepared)
        {
            JObject system = classItem.System;
            if (system == null)
                return;

            StoreClassProficiency(
                prepared,
                "weapon",
                "unarmed",
                system.SelectToken("attacks.unarmed")?.Value<int>() ?? 0
            );
            StoreClassProficiency(
                prepared,
                "weapon",
                "simple",
                system.SelectToken("attacks.simple")?.Value<int>() ?? 0
            );
            StoreClassProficiency(
                prepared,
                "weapon",
                "martial",
                system.SelectToken("attacks.martial")?.Value<int>() ?? 0
            );
            StoreClassProficiency(
                prepared,
                "weapon",
                "advanced",
                system.SelectToken("attacks.advanced")?.Value<int>() ?? 0
            );

            StoreClassProficiency(
                prepared,
                "armor",
                "unarmored",
                system.SelectToken("defenses.unarmored")?.Value<int>() ?? 0
            );
            StoreClassProficiency(
                prepared,
                "armor",
                "light",
                system.SelectToken("defenses.light")?.Value<int>() ?? 0
            );
            StoreClassProficiency(
                prepared,
                "armor",
                "medium",
                system.SelectToken("defenses.medium")?.Value<int>() ?? 0
            );
            StoreClassProficiency(
                prepared,
                "armor",
                "heavy",
                system.SelectToken("defenses.heavy")?.Value<int>() ?? 0
            );

            foreach (
                JToken skill in system.SelectToken("trainedSkills.value") as JArray ?? new JArray()
            )
                UpgradeSkillRank(prepared, skill.Value<string>(), 1);
        }

        private static void StoreClassProficiency(
            PreparedRulesBuilder prepared,
            string domain,
            string category,
            int rank
        ) => prepared.RuleValues[ClassProficiencyPath(domain, category)] = rank * 2;

        private static string ClassProficiencyPath(string domain, string category) =>
            $"class.proficiency.{domain}.{category}";

        private static void ProjectLegacyPreparation(
            CreatureComponent creature,
            CharacterBuild destinationBuild,
            CharacterBuild normalizedBuild,
            PreparedCreatureInputs inputs
        )
        {
            if (destinationBuild != null)
            {
                destinationBuild.ClassName = normalizedBuild.ClassName;
                destinationBuild.SubclassName = normalizedBuild.SubclassName;
                destinationBuild.ClassFeatName = normalizedBuild.ClassFeatName;
                destinationBuild.RuleSelections.Clear();
                foreach (KeyValuePair<string, string> selection in normalizedBuild.RuleSelections)
                    destinationBuild.RuleSelections.Add(selection.Key, selection.Value);
                destinationBuild.TrainedSkills.Clear();
                destinationBuild.TrainedSkills.AddRange(normalizedBuild.TrainedSkills);
            }
            if (creature == null)
                return;
            foreach (string category in new[] { "unarmed", "simple", "martial", "advanced" })
                ProjectWeaponBonus(creature, inputs, category);
            foreach (string category in new[] { "unarmored", "light", "medium", "heavy" })
                ProjectArmorBonus(creature, inputs, category);
        }

        private static void ProjectWeaponBonus(
            CreatureComponent creature,
            PreparedCreatureInputs inputs,
            string category
        )
        {
            if (
                !inputs.RuleValues.TryGetValue(
                    ClassProficiencyPath("weapon", category),
                    out int bonus
                )
            )
                return;
            int index = creature.weaponBonuses.FindIndex(value => value.category == category);
            if (index < 0)
                creature.weaponBonuses.Add(new WeaponBonus { category = category, bonus = bonus });
            else
                creature.weaponBonuses[index] = new WeaponBonus
                {
                    category = category,
                    bonus = bonus,
                };
        }

        private static void ProjectArmorBonus(
            CreatureComponent creature,
            PreparedCreatureInputs inputs,
            string category
        )
        {
            if (
                !inputs.RuleValues.TryGetValue(
                    ClassProficiencyPath("armor", category),
                    out int bonus
                )
            )
                return;
            int index = creature.armorBonuses.FindIndex(value => value.category == category);
            if (index < 0)
                creature.armorBonuses.Add(new ArmorBonus { category = category, bonus = bonus });
            else
                creature.armorBonuses[index] = new ArmorBonus
                {
                    category = category,
                    bonus = bonus,
                };
        }

        private static void GrantClassItems(
            int level,
            Pf2eItem classItem,
            Pf2eItemCatalog catalog,
            PreparedRulesBuilder prepared
        )
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

        private static void ProcessGrantRules(
            PreparedRulesBuilder prepared,
            Pf2eItemCatalog catalog
        )
        {
            foreach (OwnedPf2eItem owned in prepared.OwnedItems.ToArray())
            {
                JObject[] rules = owned.Item.Rules.ToArray();
                for (int index = 0; index < rules.Length; index++)
                {
                    JObject rule = rules[index];
                    string key = rule.Value<string>("key");
                    if (key == "ChoiceSet" || key == "GrantItem" || key == "ActiveEffectLike")
                        ProcessRule(rule, owned.Item, index, prepared, catalog);
                }
            }
        }

        private static void CollectRuleSynthetics(
            PreparedRulesBuilder prepared,
            Pf2eItemCatalog catalog
        )
        {
            prepared.ResetContributions();

            foreach (OwnedPf2eItem owned in prepared.OwnedItems.ToArray())
            {
                JObject[] rules = owned.Item.Rules.ToArray();
                for (int index = 0; index < rules.Length; index++)
                {
                    JObject rule = rules[index];
                    string key = rule.Value<string>("key");
                    if (key != "ChoiceSet" && key != "GrantItem")
                        ProcessRule(rule, owned.Item, index, prepared, catalog);
                }
            }
        }

        private static void ProcessRule(
            JObject rule,
            Pf2eItem source,
            int ruleIndex,
            PreparedRulesBuilder prepared,
            Pf2eItemCatalog catalog
        )
        {
            string key = rule.Value<string>("key");
            if (string.IsNullOrWhiteSpace(key))
                return;

            PreparedPredicate predicate = Pf2ePredicate.Compile(rule["predicate"]);
            bool predicateMatches = prepared.EvaluateStatic(predicate);

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
                    RuleDefinitionId modifierDefinition = prepared.Define(source, key, ruleIndex);
                    prepared.Modifiers.Add(
                        new PreparedModifierSpec(
                            modifierDefinition,
                            rule.Value<string>("selector"),
                            rule.Value<string>("slug") ?? source.Slug,
                            ResolveRuleInt(rule["value"], prepared),
                            rule.Value<string>("type"),
                            rule.Value<string>("ability"),
                            predicate
                        )
                    );
                    break;
                case "AdjustModifier":
                    RuleDefinitionId adjustmentDefinition = prepared.Define(source, key, ruleIndex);
                    prepared.Adjustments.Add(
                        new PreparedAdjustmentSpec(
                            adjustmentDefinition,
                            rule.Value<string>("selector"),
                            rule.Value<string>("slug"),
                            rule.Value<string>("mode"),
                            rule.Value<float?>("value") ?? 0,
                            rule.Value<int?>("priority") ?? 0,
                            predicate
                        )
                    );
                    break;
                case "DamageDice":
                    RuleDefinitionId damageDiceDefinition = prepared.Define(source, key, ruleIndex);
                    prepared.DamageDice.Add(
                        new PreparedDamageDiceSpec(
                            damageDiceDefinition,
                            rule.Value<string>("selector"),
                            rule.Value<string>("category"),
                            ResolveRuleInt(rule["diceNumber"], prepared),
                            ResolveDieSize(rule["dieSize"], prepared),
                            predicate
                        )
                    );
                    break;
                case "ItemAlteration":
                    RuleDefinitionId alterationDefinition = prepared.Define(source, key, ruleIndex);
                    prepared.ItemAlterations.Add(
                        new PreparedItemAlterationSpec(
                            alterationDefinition,
                            rule.Value<string>("itemType"),
                            rule.Value<string>("mode"),
                            rule.Value<string>("property"),
                            rule.Value<string>("value"),
                            predicate
                        )
                    );
                    break;
                case "RollOption":
                    if (!CreatesPreparedBinding(rule, key))
                        break;
                    string option = rule.Value<string>("option");
                    RuleDefinitionId optionDefinition = prepared.Define(source, key, ruleIndex);
                    if (predicateMatches)
                        prepared.RollOptions.Add(option);
                    prepared.Options.Add(
                        new PreparedOptionSpec(optionDefinition, option, predicate)
                    );
                    break;
                case "TempHP":
                case "Resistance":
                    break;
                default:
                    prepared.AddUnsupported(key, source, ruleIndex);
                    break;
            }
        }

        private static void ApplyChoiceSet(
            JObject rule,
            PreparedRulesBuilder prepared,
            Pf2eItemCatalog catalog
        )
        {
            string flag = rule.Value<string>("flag");
            if (string.IsNullOrWhiteSpace(flag) || prepared.Build.RuleSelections.ContainsKey(flag))
                return;

            string selectedName = GetChoiceSetSelection(flag, prepared.Build);
            Pf2eItem selected = catalog.Resolve(selectedName);
            if (selected == null)
                return;

            string pack =
                selected.Type == "feat" && selected.System?.Value<string>("category") == "class"
                    ? "feats-srd"
                    : "classfeatures";
            prepared.Build.RuleSelections[flag] = $"Compendium.pf2e.{pack}.Item.{selected.Name}";
        }

        private static string GetChoiceSetSelection(string flag, CharacterBuild build)
        {
            if (
                string.Equals(flag, "roguesRacket", StringComparison.OrdinalIgnoreCase)
                || string.Equals(flag, "doctrine", StringComparison.OrdinalIgnoreCase)
            )
                return build.SubclassName;
            return build.ClassFeatName;
        }

        private static void ApplyGrantItem(
            JObject rule,
            Pf2eItem source,
            PreparedRulesBuilder prepared,
            Pf2eItemCatalog catalog
        )
        {
            string uuid = ResolveRuleReference(rule.Value<string>("uuid"), prepared);
            if (string.IsNullOrWhiteSpace(uuid))
                return;

            Pf2eItem granted = catalog.Resolve(uuid);
            prepared.AddOwnedItem(granted, source.Slug);
        }

        private static string ResolveRuleReference(string uuid, PreparedRulesBuilder prepared)
        {
            if (string.IsNullOrWhiteSpace(uuid))
                return null;
            if (uuid.StartsWith("{actor|", StringComparison.OrdinalIgnoreCase))
                return ResolveActorReference(uuid, prepared);
            if (!uuid.StartsWith("{item|flags.", StringComparison.OrdinalIgnoreCase))
                return uuid;

            const string rulesSelections = ".rulesSelections.";
            int index = uuid.IndexOf(rulesSelections, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return uuid;

            string flag = uuid.Substring(index + rulesSelections.Length).TrimEnd('}');
            return prepared.Build.RuleSelections.TryGetValue(flag, out string selection)
                ? selection
                : null;
        }

        private static string ResolveActorReference(string uuid, PreparedRulesBuilder prepared)
        {
            const string actorPrefix = "{actor|";
            if (
                !uuid.StartsWith(actorPrefix, StringComparison.OrdinalIgnoreCase)
                || !uuid.EndsWith("}")
            )
                return uuid;

            string path = uuid.Substring(actorPrefix.Length, uuid.Length - actorPrefix.Length - 1);
            return prepared.RuleReferences.TryGetValue(path, out string reference)
                ? reference
                : null;
        }

        private static void ApplyActiveEffectLike(JObject rule, PreparedRulesBuilder prepared)
        {
            string path = rule.Value<string>("path");
            if (string.IsNullOrWhiteSpace(path))
                return;

            string normalizedPath = path.StartsWith("@actor.", StringComparison.OrdinalIgnoreCase)
                ? path.Substring("@actor.".Length)
                : path;
            JToken valueToken = rule["value"];
            if (valueToken is JObject objectValue)
            {
                StoreRuleReferences(normalizedPath, objectValue, prepared);
                return;
            }

            int value = ResolveRuleInt(valueToken, prepared);
            if (
                path.StartsWith("system.skills.", StringComparison.OrdinalIgnoreCase)
                && path.EndsWith(".rank", StringComparison.OrdinalIgnoreCase)
            )
            {
                string skill = path.Substring(
                    "system.skills.".Length,
                    path.Length - "system.skills.".Length - ".rank".Length
                );
                UpgradeSkillRank(prepared, skill, value);
                return;
            }

            if (
                string.Equals(
                    rule.Value<string>("mode"),
                    "override",
                    StringComparison.OrdinalIgnoreCase
                )
            )
                prepared.RuleValues[normalizedPath] = value;
            else if (
                !prepared.RuleValues.TryGetValue(normalizedPath, out int current)
                || value > current
            )
                prepared.RuleValues[normalizedPath] = value;
        }

        private static void StoreRuleReferences(
            string path,
            JObject value,
            PreparedRulesBuilder prepared
        )
        {
            foreach (JProperty property in value.Properties())
            {
                string childPath = string.IsNullOrWhiteSpace(path)
                    ? property.Name
                    : path + "." + property.Name;
                if (property.Value.Type == JTokenType.String)
                    prepared.RuleReferences[childPath] = property.Value.Value<string>();
                else if (property.Value is JObject childObject)
                    StoreRuleReferences(childPath, childObject, prepared);
            }
        }

        private static void AddSavedSkillRanks(PreparedRulesBuilder prepared)
        {
            foreach (string skill in prepared.Build.TrainedSkills)
                UpgradeSkillRank(prepared, skill, 1);
        }

        private static void UpgradeSkillRank(PreparedRulesBuilder prepared, string skill, int rank)
        {
            if (string.IsNullOrWhiteSpace(skill) || rank <= 0)
                return;

            if (!prepared.SkillRanks.TryGetValue(skill, out int current) || rank > current)
                prepared.SkillRanks[skill] = rank;
        }

        private static int ResolveRuleInt(JToken token, PreparedRulesBuilder prepared)
        {
            if (token == null || token.Type == JTokenType.Null)
                return 0;
            if (token.Type == JTokenType.Integer)
                return token.Value<int>();
            if (token.Type != JTokenType.String)
                return 0;

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

        private static int ResolveDieSize(JToken token, PreparedRulesBuilder prepared)
        {
            if (token == null || token.Type == JTokenType.Null)
                return 0;
            if (token.Type == JTokenType.Integer)
                return token.Value<int>();
            if (token.Type != JTokenType.String)
                return 0;

            string value = token.Value<string>();
            if (string.IsNullOrWhiteSpace(value))
                return 0;
            if (
                value.StartsWith("d", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(value.Substring(1), out int sides)
            )
                return sides;
            if (int.TryParse(value, out sides))
                return sides;

            const string actorFlagPrefix = "d{actor|";
            if (
                value.StartsWith(actorFlagPrefix, StringComparison.OrdinalIgnoreCase)
                && value.EndsWith("}")
            )
            {
                string path = value.Substring(
                    actorFlagPrefix.Length,
                    value.Length - actorFlagPrefix.Length - 1
                );
                return prepared.RuleValues.TryGetValue(path, out int fact) ? fact : 0;
            }

            return 0;
        }

        private static int GetPreparedLevel(PreparedRulesBuilder prepared)
        {
            string levelOption = prepared.RollOptions.FirstOrDefault(option =>
                option.StartsWith("self:level:", StringComparison.OrdinalIgnoreCase)
            );
            if (
                levelOption != null
                && int.TryParse(levelOption.Substring("self:level:".Length), out int level)
            )
                return level;
            return 0;
        }

        private static void AddArmorRollOptions(
            CreatureComponent creature,
            PreparedRulesBuilder prepared
        )
        {
            string category = creature.equippedArmor?.category;
            if (!string.IsNullOrWhiteSpace(category))
                prepared.RollOptions.Add($"armor:category:{category}");
        }

        private static void AddEquipmentOwnership(
            CreatureComponent creature,
            PreparedRulesBuilder prepared,
            Pf2eItemCatalog catalog
        )
        {
            IEnumerable<string> references = (creature.equipment ?? new List<string>())
                .Concat(
                    new[]
                    {
                        creature.equippedArmor?.name,
                        creature.equippedRightHand?.name,
                        creature.equippedLeftHand?.name,
                    }
                )
                .Where(value => !string.IsNullOrWhiteSpace(value));
            foreach (string reference in references)
                prepared.AddOwnedItem(catalog.Resolve(reference));
        }

        /// <summary>
        /// The only mutable preparation representation. It is private to compilation and transfers
        /// copied values into one <see cref="PreparedRulePackage"/> exactly once.
        /// </summary>
        private sealed class PreparedRulesBuilder
        {
            private readonly CreatureComponent creature;
            private readonly List<PreparedRuleDefinitionSpec> definitions = new();
            private readonly List<PreparedBindingSeed> bindings = new();
            private readonly List<PreparedUnsupportedDiagnostic> diagnostics = new();
            private readonly HashSet<RuleDefinitionId> defined = new();
            private bool frozen;

            internal PreparedRulesBuilder(CreatureComponent creature, CharacterBuild build)
            {
                this.creature = creature;
                Build = build ?? new CharacterBuild();
            }

            internal CharacterBuild Build { get; }
            internal List<OwnedPf2eItem> OwnedItems { get; } = new();
            internal HashSet<string> RollOptions { get; } = new(StringComparer.OrdinalIgnoreCase);
            internal List<PreparedOptionSpec> Options { get; } = new();
            internal List<PreparedModifierSpec> Modifiers { get; } = new();
            internal List<PreparedAdjustmentSpec> Adjustments { get; } = new();
            internal List<PreparedDamageDiceSpec> DamageDice { get; } = new();
            internal List<PreparedItemAlterationSpec> ItemAlterations { get; } = new();
            internal Dictionary<string, int> SkillRanks { get; } =
                new(StringComparer.OrdinalIgnoreCase);
            internal Dictionary<string, int> RuleValues { get; } =
                new(StringComparer.OrdinalIgnoreCase);
            internal Dictionary<string, string> RuleReferences { get; } =
                new(StringComparer.OrdinalIgnoreCase);

            internal bool AddOwnedItem(Pf2eItem item, string grantedBy = null)
            {
                if (item == null || OwnedItems.Any(owned => owned.Item.Slug == item.Slug))
                    return false;
                OwnedItems.Add(new OwnedPf2eItem(item, grantedBy));
                RuleDefinitionId id = Define(item.Slug, "owned", -1, item.Name);
                AddOwnedOption(id, item, $"item:owned:{item.Slug}");
                if (item.Type == "class")
                    AddOwnedOption(id, item, $"class:{item.Slug}");
                if (item.Type == "feat")
                    AddOwnedOption(id, item, $"feat:{item.Slug}");
                string category = item.System?.SelectToken("category")?.Value<string>();
                if (
                    item.Type == "feat"
                    && string.Equals(category, "classfeature", StringComparison.OrdinalIgnoreCase)
                )
                    AddOwnedOption(id, item, $"feature:{item.Slug}");
                if (item.Type == "action")
                    AddOwnedOption(id, item, $"action:{item.Slug}");
                return true;
            }

            internal bool HasOwnedItem(string slug) =>
                OwnedItems.Any(item =>
                    string.Equals(item.Item.Slug, slug, StringComparison.OrdinalIgnoreCase)
                );

            internal RuleDefinitionId Define(Pf2eItem source, string key, int index) =>
                Define(source.Slug, key, index, source.Name);

            internal bool EvaluateStatic(PreparedPredicate predicate) =>
                Pf2ePredicate.EvaluateStatic(
                    predicate,
                    RollOptions,
                    SkillRanks,
                    creature?.level ?? 0
                );

            internal void ResetContributions()
            {
                Modifiers.Clear();
                Adjustments.Clear();
                DamageDice.Clear();
                ItemAlterations.Clear();
                diagnostics.Clear();
            }

            internal void AddUnsupported(string key, Pf2eItem source, int index)
            {
                if (
                    diagnostics.Any(value =>
                        value.Key == key
                        && value.Source == RuleSource.FromSlug(source.Slug)
                        && value.Provenance == $"{source.Slug}#rule-{index}"
                    )
                )
                    return;
                diagnostics.Add(
                    new PreparedUnsupportedDiagnostic(
                        key,
                        RuleSource.FromSlug(source.Slug),
                        $"{source.Slug}#rule-{index}"
                    )
                );
            }

            internal PreparedRulePackage Freeze()
            {
                if (frozen)
                    throw new InvalidOperationException("Prepared rules can only be frozen once.");
                frozen = true;
                int level = Math.Max(0, creature?.level ?? 0);
                List<string> staticOptions = RollOptions
                    .Where(option =>
                        option.StartsWith("self:level:", StringComparison.OrdinalIgnoreCase)
                        || option.StartsWith("armor:", StringComparison.OrdinalIgnoreCase)
                    )
                    .ToList();
                foreach (string trait in creature?.traits ?? new List<string>())
                    staticOptions.Add($"self:trait:{Pf2eSlug.FromName(trait)}");
                foreach (DamageValue value in creature?.weaknesses ?? new List<DamageValue>())
                    staticOptions.Add($"self:weakness:{Pf2eSlug.FromName(value.DamageType)}");
                foreach (DamageValue value in creature?.resistances ?? new List<DamageValue>())
                    staticOptions.Add($"self:resistance:{Pf2eSlug.FromName(value.DamageType)}");
                foreach (string immunity in creature?.immunities ?? new List<string>())
                    staticOptions.Add($"self:immunity:{Pf2eSlug.FromName(immunity)}");
                PreparedCreatureInputs inputs = new(
                    level,
                    new PreparedAbilityModifiers(
                        creature?.strMod ?? 0,
                        creature?.dexMod ?? 0,
                        creature?.conMod ?? 0,
                        creature?.intMod ?? 0,
                        creature?.wisMod ?? 0,
                        creature?.chaMod ?? 0
                    ),
                    SkillRanks,
                    creature?.equipment ?? new List<string>(),
                    creature?.equippedArmor?.category ?? string.Empty,
                    creature?.traits ?? new List<string>(),
                    (creature?.weaknesses ?? new List<DamageValue>()).Select(
                        value => new PreparedDefenseDescriptor(value.DamageType, value.DamageAmount)
                    ),
                    (creature?.resistances ?? new List<DamageValue>()).Select(
                        value => new PreparedDefenseDescriptor(value.DamageType, value.DamageAmount)
                    ),
                    (creature?.immunities ?? new List<string>()).SelectMany(CompileImmunity),
                    staticOptions,
                    Options.Select(value => new PreparedBoundOption(
                        value.DefinitionId,
                        value.Option,
                        value.Predicate
                    )),
                    RuleValues
                );
                PreparedRulePackage package = new(
                    inputs,
                    definitions.OrderBy(value => value.Id.Value, StringComparer.Ordinal),
                    bindings
                        .OrderBy(value => value.CreationOrder)
                        .ThenBy(value => value.StableKey, StringComparer.Ordinal),
                    diagnostics
                        .OrderBy(value => value.Source.Slug, StringComparer.Ordinal)
                        .ThenBy(value => value.Provenance, StringComparer.Ordinal)
                );
                return package;
            }

            private RuleDefinitionId Define(string sourceSlug, string key, int index, string name)
            {
                Pf2eItem sourceItem = OwnedItems
                    .Select(value => value.Item)
                    .FirstOrDefault(value => value.Slug == Pf2eSlug.FromName(sourceSlug));
                string stableKey =
                    index < 0
                        ? $"{Pf2eSlug.FromName(sourceSlug)}:owned"
                        : $"{Pf2eSlug.FromName(sourceSlug)}:{index}:{Pf2eSlug.FromName(key)}";
                RuleDefinitionId id = new($"prepared:{stableKey}");
                if (!defined.Add(id))
                    return id;
                RuleSource ruleSource = RuleSource.FromSlug(sourceSlug);
                definitions.Add(
                    sourceItem == null
                        ? new PreparedRuleDefinitionSpec(
                            id,
                            ruleSource,
                            key,
                            index < 0 ? name : $"{sourceSlug}#rule-{index}"
                        )
                        : CreateDefinitionSpec(sourceItem, key, index)
                );
                bindings.Add(
                    new PreparedBindingSeed(stableKey, id, ruleSource, 1000 + bindings.Count)
                );
                return id;
            }

            private void AddOwnedOption(RuleDefinitionId id, Pf2eItem item, string option)
            {
                RollOptions.Add(option);
                if (!Options.Any(value => value.DefinitionId == id && value.Option == option))
                    Options.Add(new PreparedOptionSpec(id, option, PreparedPredicate.Always));
            }
        }

        private static IEnumerable<PreparedImmunityDescriptor> CompileImmunity(string value)
        {
            string type = Pf2eSlug.FromName(value);
            if (type == "death" || type == "death-effect" || type == "death-effects")
            {
                yield return new PreparedImmunityDescriptor(type, PreparedImmunityKind.EffectTrait);
                yield break;
            }
            if (type == "disease")
            {
                yield return new PreparedImmunityDescriptor(type, PreparedImmunityKind.EffectTrait);
                yield break;
            }
            if (type == "poison")
            {
                yield return new PreparedImmunityDescriptor(type, PreparedImmunityKind.Damage);
                yield return new PreparedImmunityDescriptor(type, PreparedImmunityKind.EffectTrait);
                yield break;
            }
            if (DamageImmunityTypes.Contains(type))
            {
                yield return new PreparedImmunityDescriptor(type, PreparedImmunityKind.Damage);
                yield break;
            }
            if (ConditionImmunityTypes.Contains(type))
            {
                yield return new PreparedImmunityDescriptor(type, PreparedImmunityKind.Condition);
                yield break;
            }
            yield return new PreparedImmunityDescriptor(type, PreparedImmunityKind.Unclassified);
        }

        private static readonly HashSet<string> DamageImmunityTypes = new(
            new[]
            {
                "acid",
                "bleed",
                "bludgeoning",
                "cold",
                "electricity",
                "fire",
                "force",
                "mental",
                "piercing",
                "precision",
                "slashing",
                "sonic",
                "spirit",
                "vitality",
                "void",
            },
            StringComparer.Ordinal
        );

        private static readonly HashSet<string> ConditionImmunityTypes = new(
            new[]
            {
                "blinded",
                "clumsy",
                "controlled",
                "dazzled",
                "deafened",
                "doomed",
                "drained",
                "dying",
                "enfeebled",
                "fascinated",
                "fatigued",
                "fleeing",
                "frightened",
                "grabbed",
                "immobilized",
                "off-guard",
                "paralyzed",
                "prone",
                "restrained",
                "sickened",
                "slowed",
                "stunned",
                "stupefied",
                "unconscious",
                "wounded",
            },
            StringComparer.Ordinal
        );
    }
}
