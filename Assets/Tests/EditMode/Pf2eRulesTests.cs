using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.Creature.Rules;
using Game.DungeonPersistence.Actors;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Strike;
using GridPublic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public class Pf2eRulesTests
{
    private readonly List<GameObject> created = new();
    private int nextTargetIdentity;

    private static GridPrivate.Tile[,] CreateTiles() =>
        new[,]
        {
            { new GridPrivate.Tile() },
        };

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in created)
            if (go != null)
                Object.DestroyImmediate(go);
        created.Clear();
        nextTargetIdentity = 0;
        Pf2eItemCatalog.ResetForTests();
    }

    [Test]
    public void CatalogResolvesFoundryStyleReferences()
    {
        Pf2eItemCatalog catalog = Pf2eItemCatalog.Instance;

        Assert.That(
            catalog.Resolve("Compendium.pf2e.classes.Item.Barbarian")?.Slug,
            Is.EqualTo("barbarian")
        );
        Assert.That(
            catalog.Resolve("Compendium.pf2e.actionspf2e.Item.Rage")?.Slug,
            Is.EqualTo("rage")
        );
        Assert.That(
            catalog.Resolve("Compendium.pf2e.feat-effects.Item.Effect: Rage")?.Slug,
            Is.EqualTo("effect-rage")
        );
        Assert.That(
            catalog.Resolve("Raging Intimidation")?.Slug,
            Is.EqualTo("raging-intimidation")
        );
        Assert.That(
            catalog.Resolve("Compendium.pf2e.classes.Item.Rogue")?.Slug,
            Is.EqualTo("rogue")
        );
        Assert.That(
            catalog.Resolve("Compendium.pf2e.classfeatures.Item.Sneak Attack")?.Slug,
            Is.EqualTo("sneak-attack")
        );
        Assert.That(
            catalog.Resolve("Compendium.pf2e.classfeatures.Item.Thief")?.Slug,
            Is.EqualTo("thief")
        );
        Assert.That(catalog.Resolve("Nimble Dodge")?.Slug, Is.EqualTo("nimble-dodge"));
    }

    [Test]
    public void TorgrimPreparesBarbarianFeaturesFromBuildData()
    {
        GameObject torgrim = CreatureJsonConverter.CreateFromFile(
            "DataFiles/playerCharacters/Torgrim"
        );
        created.Add(torgrim);
        CreatureComponent creature = torgrim.GetComponent<CreatureComponent>();

        Assert.That(creature.Build.ClassName, Is.EqualTo("Barbarian"));
        Assert.That(creature.Build.SubclassName, Is.EqualTo("Fury Instinct"));
        PreparedRulePackage compilation = Compile(creature);
        Assert.That(HasOwned(compilation, "barbarian"), Is.True);
        Assert.That(HasOwned(compilation, "quick-tempered"), Is.True);
        Assert.That(HasOwned(compilation, "rage"), Is.True);
        Assert.That(HasOwned(compilation, "fury-instinct"), Is.True);
        Assert.That(HasOwned(compilation, "raging-intimidation"), Is.True);
        Assert.That(
            creature.Build.RuleSelections["furyInstinct"],
            Does.Contain("Raging Intimidation")
        );
    }

    [Test]
    public void ValerosImportsCompleteCurrentAndMaximumHealth()
    {
        GameObject valeros = CreatureJsonConverter.CreateFromFile(
            "DataFiles/iconics/valeros-level-1"
        );
        created.Add(valeros);

        CreatureComponent creature = valeros.GetComponent<CreatureComponent>();

        Assert.That(creature.hp, Is.EqualTo(20));
        Assert.That(creature.maxHp, Is.EqualTo(20));
        Assert.That(creature.tempHp, Is.Zero);
    }

    [Test]
    public void PredicateSupportsAtomicCompoundAndNumericChecks()
    {
        CreatureComponent creature = CreatePreparedBarbarian();
        creature.level = 7;
        creature.Build.TrainedSkills.Add("intimidation");
        PreparedRulePackage package = Compile(creature);
        CreatureId actor = new("predicate-actor");
        RulesStateSeed seed = new RulesStateSeed().SeedPreparedInputs(actor, package.Inputs);
        foreach (PreparedBindingSeed binding in package.Bindings)
            seed.SeedRuleBinding(binding.Create(actor));
        PreparedPredicateContext context = new(
            new InMemoryRulesStore(seed).Snapshot,
            actor,
            new[] { "self:effect:rage", "skill:intimidation:rank:4" }
        );

        Assert.That(
            Pf2ePredicate
                .Compile(JToken.Parse("[\"class:barbarian\", {\"not\": \"item:ranged\"}]"))
                .Evaluate(context),
            Is.True
        );
        Assert.That(
            Pf2ePredicate
                .Compile(JToken.Parse("[{\"or\": [\"item:ranged\", \"self:effect:rage\"]}]"))
                .Evaluate(context),
            Is.True
        );
        Assert.That(
            Pf2ePredicate
                .Compile(
                    JToken.Parse(
                        "[{\"gte\": [\"self:level\", 7]}, {\"gte\": [\"skill:intimidation:rank\", 1]}]"
                    )
                )
                .Evaluate(context),
            Is.True
        );
        Assert.That(
            Pf2ePredicate
                .Compile(
                    JToken.Parse("[{\"and\": [\"class:barbarian\", {\"not\": \"item:ranged\"}]}]")
                )
                .Evaluate(context),
            Is.True
        );
    }

    [Test]
    public void RageDamageUsesRuleModifiersAndFuryInstinctAdjustments()
    {
        CreatureComponent creature = CreatePreparedBarbarian();
        UnityCombatRulesBridge bridge = CreateCombatRules(creature);
        CreatureId actor = bridge.GetCreatureId(creature);
        bridge.BeginTurn(actor, 3);
        if (
            !RuntimeOptionResolver
                .Resolve(bridge.Snapshot, actor, Array.Empty<string>())
                .Contains("self:effect:rage")
        )
            Assert.That(
                bridge.Dispatch(new RageActionOp(actor)),
                Is.TypeOf<ResolvedOpResult<RageStartOutcome>>()
            );

        StrikeProfile greataxe = new(
            new List<Dice> { new Dice(1, 12, "Slashing") },
            new List<DamageValue> { new DamageValue("Slashing", 4) }
        );
        StrikeResolutionContext greataxeContext = PrepareStrike(creature, greataxe);
        Assert.That(greataxeContext.FlatDamages.Last().DamageAmount, Is.EqualTo(3));

        PreparedRulePackage package = Compile(creature);
        RuleDefinitionId agileAdjustmentDefinition = package
            .Definitions.Single(definition =>
                definition.Adjustments.Any(adjustment =>
                    adjustment.Priority == 95
                    && string.Equals(adjustment.Slug, "rage", StringComparison.OrdinalIgnoreCase)
                )
            )
            .Id;
        ActiveRuleBinding agileAdjustmentBinding = bridge
            .Snapshot.RuleBindings.Single(pair =>
                pair.Value.Owner == actor && pair.Value.DefinitionId == agileAdjustmentDefinition
            )
            .Value;
        RuleDispatcher reorderedCollector = CreatePreparedDispatcher(
            package,
            actor,
            bridge.Snapshot,
            binding =>
                binding.Id == agileAdjustmentBinding.Id
                    ? new ActiveRuleBinding(
                        binding.Id,
                        binding.DefinitionId,
                        binding.Owner,
                        binding.EffectId,
                        binding.Source,
                        1_000_000,
                        binding.IsEnabled
                    )
                    : binding
        );
        PreparedContributionContext agilePreparedContext = new(
            "test-item",
            string.Empty,
            false,
            4,
            new[] { "agile" },
            Array.Empty<string>(),
            Array.Empty<string>()
        );
        Assert.That(
            Resolve(
                    reorderedCollector.Dispatch(
                        new CollectPreparedModifiersOp(actor, "strike-damage", agilePreparedContext)
                    )
                )
                .Single(value => value.Slug == "rage")
                .Value,
            Is.EqualTo(1)
        );

        StrikeProfile agile = new(
            new List<Dice> { new Dice(1, 4, "Bludgeoning") },
            new List<DamageValue> { new DamageValue("Bludgeoning", 4) }
        );
        agile.Traits.Add("agile");
        StrikeResolutionContext agileContext = PrepareStrike(creature, agile);
        Assert.That(agileContext.FlatDamages.Last().DamageAmount, Is.EqualTo(1));

        bridge.Dispatch(new EndRageOp(actor));
        StrikeProfile notRaging = new(
            new List<Dice> { new Dice(1, 12, "Slashing") },
            new List<DamageValue> { new DamageValue("Slashing", 4) }
        );
        StrikeResolutionContext notRagingContext = PrepareStrike(creature, notRaging);
        Assert.That(notRagingContext.FlatDamages.Count, Is.EqualTo(1));
    }

    [Test]
    public void RagingIntimidationItemAlterationAddsRageTraitOnlyWhileRaging()
    {
        CreatureComponent creature = CreatePreparedBarbarian();
        UnityCombatRulesBridge bridge = CreateCombatRules(creature);
        CreatureId actor = bridge.GetCreatureId(creature);
        bridge.BeginTurn(actor, 3);

        PreparedRulePackage package = Compile(creature);
        if (
            !RuntimeOptionResolver
                .Resolve(bridge.Snapshot, actor, Array.Empty<string>())
                .Contains("self:effect:rage")
        )
            Assert.That(
                bridge.Dispatch(new RageActionOp(actor)),
                Is.TypeOf<ResolvedOpResult<RageStartOutcome>>()
            );
        RuleDispatcher collector = CreatePreparedDispatcher(package, actor, bridge.Snapshot);
        ResolvedOpResult<IReadOnlyList<PreparedItemAlterationSpec>> alterations =
            (ResolvedOpResult<IReadOnlyList<PreparedItemAlterationSpec>>)
                collector
                    .Dispatch(
                        new CollectPreparedItemAlterationsOp(
                            actor,
                            "action",
                            "traits",
                            new PreparedContributionContext(
                                "demoralize",
                                string.Empty,
                                false,
                                0,
                                Array.Empty<string>(),
                                Array.Empty<string>(),
                                Array.Empty<string>()
                            )
                        )
                    )
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
        Assert.That(alterations.Value.Select(value => value.Value), Does.Contain("rage"));
    }

    [Test]
    public void LenaPreparesRogueFeaturesFromBuildData()
    {
        GameObject lena = CreatureJsonConverter.CreateFromFile("DataFiles/playerCharacters/Lena");
        created.Add(lena);
        CreatureComponent creature = lena.GetComponent<CreatureComponent>();

        Assert.That(creature.Build.ClassName, Is.EqualTo("Rogue"));
        Assert.That(creature.Build.SubclassName, Is.EqualTo("Thief"));
        PreparedRulePackage compilation = Compile(creature);
        Assert.That(HasOwned(compilation, "rogue"), Is.True);
        Assert.That(HasOwned(compilation, "rogues-racket"), Is.True);
        Assert.That(HasOwned(compilation, "sneak-attack"), Is.True);
        Assert.That(HasOwned(compilation, "surprise-attack"), Is.True);
        Assert.That(HasOwned(compilation, "thief"), Is.True);
        Assert.That(HasOwned(compilation, "nimble-dodge"), Is.True);
        Assert.That(compilation.Inputs.SkillRanks["stealth"], Is.EqualTo(1));
        Assert.That(compilation.Inputs.SkillRanks["thievery"], Is.EqualTo(1));
        Assert.That(
            creature.weaponBonuses.First(b => b.category == "martial").bonus,
            Is.EqualTo(2)
        );
        Assert.That(creature.armorBonuses.First(b => b.category == "light").bonus, Is.EqualTo(2));
    }

    [Test]
    public void RogueToggleableRollOptionsAreNotAlwaysActive()
    {
        CreatureComponent creature = CreatePreparedRogue();

        PreparedRulePackage compilation = Compile(creature);
        Assert.That(
            compilation.Inputs.StaticOptions,
            Does.Not.Contain("target:condition:off-guard")
        );
        Assert.That(compilation.Inputs.StaticOptions, Does.Not.Contain("nimble-dodge"));
    }

    [Test]
    public void UnsupportedAndDeferredRulesDoNotCreatePreparedBindings()
    {
        const string json =
            "{\"name\":\"Fixture Rules\",\"type\":\"feat\",\"system\":{"
            + "\"slug\":\"fixture-rules\",\"category\":\"class\",\"rules\":["
            + "{\"key\":\"FlatModifier\",\"selector\":\"ac\",\"slug\":\"fixture\",\"value\":1},"
            + "{\"key\":\"TempHP\",\"value\":3},"
            + "{\"key\":\"Resistance\",\"type\":\"fire\",\"value\":2},"
            + "{\"key\":\"MysteryRule\"}]}}";
        Assert.That(Pf2eItem.TryParse("fixture-rules", json, out Pf2eItem item), Is.True);
        Pf2eItemCatalog catalog = new Pf2eItemCatalog();
        catalog.Add(item);
        GameObject gameObject = new GameObject("Unsupported Prepared Rules");
        created.Add(gameObject);
        CreatureComponent creature = gameObject.AddComponent<CreatureComponent>();
        creature.Build = new CharacterBuild { ClassFeatName = "Fixture Rules" };

        PreparedRulePackage package = Pf2eCharacterPreparer.Compile(
            creature,
            creature.Build,
            catalog
        );

        Assert.That(
            package.Bindings.Select(value => value.StableKey),
            Is.EquivalentTo(new[] { "fixture-rules:owned", "fixture-rules:0:flatmodifier" })
        );
        Assert.That(
            package.Definitions.Select(value => value.RuleKey),
            Is.EquivalentTo(new[] { "owned", "FlatModifier" })
        );
        Assert.That(package.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(package.Diagnostics[0].Key, Is.EqualTo("MysteryRule"));
        Assert.That(
            Pf2eCharacterPreparer.CompileDefinitionSpecs(catalog).Select(value => value.RuleKey),
            Is.EquivalentTo(new[] { "owned", "FlatModifier" })
        );
    }

    [Test]
    public void RollOptionPredicatesArePreservedForRuntimeDependencyEvaluation()
    {
        const string json =
            "{\"name\":\"Option Fixture\",\"type\":\"feat\",\"system\":{"
            + "\"slug\":\"option-fixture\",\"category\":\"class\",\"rules\":["
            + "{\"key\":\"RollOption\",\"option\":\"feature:prerequisite\"},"
            + "{\"key\":\"RollOption\",\"option\":\"feature:dependent\","
            + "\"predicate\":[\"feature:prerequisite\"]}]}}";
        Assert.That(Pf2eItem.TryParse("option-fixture", json, out Pf2eItem item), Is.True);
        Pf2eItemCatalog catalog = new Pf2eItemCatalog();
        catalog.Add(item);
        GameObject gameObject = new GameObject("Prepared Option Fixture");
        created.Add(gameObject);
        CreatureComponent creature = gameObject.AddComponent<CreatureComponent>();
        creature.Build = new CharacterBuild { ClassFeatName = "Option Fixture" };

        PreparedRulePackage package = Pf2eCharacterPreparer.Compile(
            creature,
            creature.Build,
            catalog
        );
        PreparedBoundOption dependent = package.Inputs.BoundOptions.Single(value =>
            value.Option == "feature:dependent"
        );
        CreatureId actor = new CreatureId("option-fixture");
        RulesStateSeed seed = new RulesStateSeed().SeedPreparedInputs(actor, package.Inputs);
        foreach (PreparedBindingSeed binding in package.Bindings)
            seed.SeedRuleBinding(binding.Create(actor));

        PreparedPredicateContext context = new PreparedPredicateContext(
            new InMemoryRulesStore(seed).Snapshot,
            actor,
            Array.Empty<string>()
        );

        Assert.That(dependent.Predicate, Is.Not.SameAs(PreparedPredicate.Always));
        Assert.That(context.HasOption("feature:prerequisite"), Is.True);
        Assert.That(context.HasOption("feature:dependent"), Is.True);
    }

    [Test]
    public void ImmunityCompilationKeepsConditionDamageAndEffectTraitDomainsDistinct()
    {
        GameObject gameObject = new GameObject("Typed Immunities");
        created.Add(gameObject);
        CreatureComponent creature = gameObject.AddComponent<CreatureComponent>();
        creature.immunities = new List<string>
        {
            "death",
            "death-effects",
            "disease",
            "poison",
            "Flat-Footed",
            "paralyzed",
            "fire",
            "bleed",
            "unmapped-immunity",
        };

        IReadOnlyList<PreparedImmunityDescriptor> immunities = Pf2eCharacterPreparer
            .Compile(creature, new CharacterBuild(), new Pf2eItemCatalog())
            .Inputs.Immunities;

        Assert.That(
            immunities
                .Where(value => value.Kind == PreparedImmunityKind.Condition)
                .Select(value => value.Type),
            Is.EqualTo(new[] { "off-guard", "paralyzed" })
        );
        Assert.That(
            immunities
                .Where(value => value.Kind == PreparedImmunityKind.Damage)
                .Select(value => value.Type),
            Is.EquivalentTo(new[] { "bleed", "fire", "poison" })
        );
        Assert.That(
            immunities
                .Where(value => value.Kind == PreparedImmunityKind.EffectTrait)
                .Select(value => value.Type),
            Is.EquivalentTo(new[] { "death", "death-effects", "disease", "poison" })
        );
        Assert.That(
            immunities.Single(value => value.Type == "unmapped-immunity").Kind,
            Is.EqualTo(PreparedImmunityKind.Unclassified)
        );
        Assert.That(immunities.Count(value => value.IsDeathEffect), Is.EqualTo(2));
    }

    [Test]
    public void CurrentFixtureCompilationIsDeterministicAndCapturesZombieImmunities()
    {
        CreatureComponent torgrim = CreatureJsonConverter
            .CreateFromFile("DataFiles/playerCharacters/Torgrim")
            .GetComponent<CreatureComponent>();
        created.Add(torgrim.gameObject);
        CreatureComponent lena = CreatureJsonConverter
            .CreateFromFile("DataFiles/playerCharacters/Lena")
            .GetComponent<CreatureComponent>();
        created.Add(lena.gameObject);
        CreatureComponent zombie = CreatureJsonConverter
            .CreateFromFile("DataFiles/pathfinder-monster-core/zombie-shambler")
            .GetComponent<CreatureComponent>();
        created.Add(zombie.gameObject);
        GameObject clericObject = new("Prepared Cleric");
        created.Add(clericObject);
        CreatureComponent cleric = clericObject.AddComponent<CreatureComponent>();
        cleric.level = 1;
        cleric.Build = new CharacterBuild { ClassName = "Cleric" };

        foreach (CreatureComponent creature in new[] { torgrim, lena, cleric, zombie })
        {
            CharacterBuild build = creature.Build ?? new CharacterBuild();
            string first = PackageFingerprint(Pf2eCharacterPreparer.Compile(creature, build));
            string second = PackageFingerprint(Pf2eCharacterPreparer.Compile(creature, build));
            Assert.That(second, Is.EqualTo(first));
        }
        Assert.That(
            Pf2eCharacterPreparer
                .Compile(zombie, zombie.Build ?? new CharacterBuild())
                .Inputs.Immunities.Any(value => value.IsDeathEffect),
            Is.True
        );
    }

    [Test]
    public void CompileCopiesMutableInputsAndOwnsImplementedDefaultsAndClassMath()
    {
        GameObject gameObject = new("Pure Prepared Cleric");
        created.Add(gameObject);
        CreatureComponent creature = gameObject.AddComponent<CreatureComponent>();
        creature.level = 1;
        creature.attackBonus = 7;
        creature.damageBonus = 4;
        creature.ac = 15;
        creature.weaponBonuses = new List<WeaponBonus>
        {
            new WeaponBonus { category = "simple", bonus = 91 },
        };
        creature.weaponActionBonuses = new List<WeaponActionBonus>
        {
            new WeaponActionBonus { weaponName = "mace", bonus = 92 },
        };
        creature.armorBonuses = new List<ArmorBonus>
        {
            new ArmorBonus { category = "unarmored", bonus = 93 },
        };
        CharacterBuild build = new() { ClassName = "Cleric" };
        build.RuleSelections.Add("existing", "kept");
        build.TrainedSkills.Add("arcana");
        creature.Build = build;
        PreparedCharacter cached = new();
        creature.Prepared = cached;
        List<WeaponBonus> originalWeaponBonuses = creature.weaponBonuses;
        List<WeaponActionBonus> originalWeaponActionBonuses = creature.weaponActionBonuses;
        List<ArmorBonus> originalArmorBonuses = creature.armorBonuses;

        PreparedRulePackage package = Pf2eCharacterPreparer.Compile(creature, build);
        string fingerprint = PackageFingerprint(package);

        Assert.That(creature.Build, Is.SameAs(build));
        Assert.That(build.SubclassName, Is.Null);
        Assert.That(build.ClassFeatName, Is.Null);
        Assert.That(
            build.RuleSelections,
            Is.EqualTo(new Dictionary<string, string> { ["existing"] = "kept" })
        );
        Assert.That(build.TrainedSkills, Is.EqualTo(new[] { "arcana" }));
        Assert.That(creature.weaponBonuses, Is.SameAs(originalWeaponBonuses));
        Assert.That(
            creature.weaponBonuses,
            Is.EqualTo(
                new[]
                {
                    new WeaponBonus { category = "simple", bonus = 91 },
                }
            )
        );
        Assert.That(creature.weaponActionBonuses, Is.SameAs(originalWeaponActionBonuses));
        Assert.That(
            creature.weaponActionBonuses,
            Is.EqualTo(
                new[]
                {
                    new WeaponActionBonus { weaponName = "mace", bonus = 92 },
                }
            )
        );
        Assert.That(creature.armorBonuses, Is.SameAs(originalArmorBonuses));
        Assert.That(
            creature.armorBonuses,
            Is.EqualTo(
                new[]
                {
                    new ArmorBonus { category = "unarmored", bonus = 93 },
                }
            )
        );
        Assert.That(creature.attackBonus, Is.EqualTo(7));
        Assert.That(creature.damageBonus, Is.EqualTo(4));
        Assert.That(creature.ac, Is.EqualTo(15));
        Assert.That(creature.Prepared, Is.SameAs(cached));
        Assert.That(HasOwned(package, "cloistered-cleric"), Is.True);
        Assert.That(HasOwned(package, "domain-initiate"), Is.True);
        Assert.That(package.Inputs.SkillRanks["arcana"], Is.EqualTo(1));
        Assert.That(package.Inputs.SkillRanks["religion"], Is.EqualTo(1));
        Assert.That(package.Inputs.RuleValues["class.proficiency.weapon.simple"], Is.EqualTo(2));
        Assert.That(package.Inputs.RuleValues["class.proficiency.weapon.martial"], Is.Zero);
        Assert.That(package.Inputs.RuleValues["class.proficiency.armor.unarmored"], Is.EqualTo(2));
        Assert.That(package.Inputs.RuleValues["class.proficiency.armor.light"], Is.Zero);

        build.RuleSelections["late"] = "mutation";
        build.TrainedSkills.Add("athletics");
        creature.weaponBonuses[0] = new WeaponBonus { category = "simple", bonus = 1 };
        Assert.That(PackageFingerprint(package), Is.EqualTo(fingerprint));
    }

    [Test]
    public void ExplicitPrepareProjectsImplementedDefaultsAndClassMathForLegacyConsumers()
    {
        GameObject gameObject = new("Explicit Prepared Cleric");
        created.Add(gameObject);
        CreatureComponent creature = gameObject.AddComponent<CreatureComponent>();
        creature.level = 1;
        creature.weaponBonuses.Add(new WeaponBonus { category = "simple", bonus = 99 });
        creature.armorBonuses.Add(new ArmorBonus { category = "unarmored", bonus = 99 });
        CharacterBuild build = new() { ClassName = "Cleric" };
        creature.Build = build;

        PreparedCharacter prepared = Pf2eCharacterPreparer.Prepare(creature, build);

        Assert.That(prepared.SpellBook.CastableSpells, Is.Not.Empty);
        Assert.That(build.SubclassName, Is.EqualTo("Cloistered Cleric"));
        Assert.That(build.ClassFeatName, Is.EqualTo("Domain Initiate"));
        Assert.That(build.RuleSelections["divineFont"], Is.EqualTo("heal"));
        Assert.That(build.RuleSelections["sanctification"], Is.EqualTo("none"));
        Assert.That(
            creature.weaponBonuses.Single(value => value.category == "simple").bonus,
            Is.EqualTo(2)
        );
        Assert.That(
            creature.weaponBonuses.Single(value => value.category == "martial").bonus,
            Is.Zero
        );
        Assert.That(
            creature.armorBonuses.Single(value => value.category == "unarmored").bonus,
            Is.EqualTo(2)
        );
        Assert.That(
            creature.armorBonuses.Single(value => value.category == "light").bonus,
            Is.Zero
        );
    }

    [Test]
    public void ThiefUsesDexterityForFinesseMeleeDamage()
    {
        CreatureComponent creature = CreatePreparedRogue();

        StrikeProfile finesseStrike = new(
            new List<Dice> { new Dice(1, 6, "slashing") },
            new List<DamageValue> { new DamageValue("slashing", creature.strMod) }
        )
        {
            Traits = new List<string> { "agile", "finesse" },
            ItemSlug = "dogslicer",
            WeaponCategory = "martial",
        };
        StrikeResolutionContext finesseContext = PrepareStrike(creature, finesseStrike);
        Assert.That(
            finesseContext.FlatDamages.Sum(value => value.DamageAmount),
            Is.EqualTo(creature.dexMod)
        );

        StrikeProfile nonFinesseStrike = new(
            new List<Dice> { new Dice(1, 6, "slashing") },
            new List<DamageValue> { new DamageValue("slashing", creature.strMod) }
        )
        {
            Traits = new List<string> { "forceful" },
            ItemSlug = "scimitar",
            WeaponCategory = "martial",
        };
        StrikeResolutionContext nonFinesseContext = PrepareStrike(creature, nonFinesseStrike);
        Assert.That(
            nonFinesseContext.FlatDamages.Sum(value => value.DamageAmount),
            Is.EqualTo(creature.strMod)
        );
    }

    [Test]
    public void SneakAttackAddsPrecisionDamageOnlyAgainstOffGuardTargets()
    {
        CreatureComponent rogue = CreatePreparedRogue();
        CreatureComponent target = CreateTarget("Target");

        StrikeProfile normalTarget = CreateDogslicerStrike(rogue);
        StrikeResolutionContext normalContext = PrepareStrike(rogue, normalTarget, target);
        Assert.That(normalContext.DamageDice.Count, Is.EqualTo(1));

        target
            .GetComponent<Conditions>()
            .RestoreApplications(
                new[] { PersistedOffGuard(target.gameObject, "sneak-attack-test") }
            );
        AttachPreparedStrikeBridge(rogue, target);
        StrikeProfile offGuardTarget = CreateDogslicerStrike(rogue);
        StrikeResolutionContext offGuardContext = PrepareStrike(rogue, offGuardTarget, target);
        Assert.That(offGuardContext.DamageDice.Count, Is.EqualTo(2));
        Assert.That(offGuardContext.DamageDice.Last().numberOfDice, Is.EqualTo(1));
        Assert.That(offGuardContext.DamageDice.Last().sidesPerDie, Is.EqualTo(6));
        Assert.That(offGuardContext.DamageDice.Last().damageType, Is.EqualTo("precision"));

        StrikeProfile ineligibleWeapon = new(
            new List<Dice> { new Dice(1, 6, "slashing") },
            new List<DamageValue> { new DamageValue("slashing", rogue.strMod) }
        )
        {
            Traits = new List<string> { "forceful" },
            ItemSlug = "scimitar",
            WeaponCategory = "martial",
        };
        StrikeResolutionContext ineligibleContext = PrepareStrike(rogue, ineligibleWeapon, target);
        Assert.That(ineligibleContext.DamageDice.Count, Is.EqualTo(1));
    }

    [Test]
    public void SneakAttackSupportsRangedWeaponsAndFlatFootedAlias()
    {
        CreatureComponent rogue = CreatePreparedRogue();
        CreatureComponent target = CreateTarget("Flat-Footed Target");
        target
            .GetComponent<Conditions>()
            .RestoreApplications(new[] { PersistedOffGuard(target.gameObject, "alias-test") });
        AttachPreparedStrikeBridge(rogue, target);

        StrikeProfile shortbowStrike = new(
            new List<Dice> { new Dice(1, 6, "piercing") },
            new List<DamageValue>()
        )
        {
            Traits = new List<string> { "deadly-d10" },
            ItemSlug = "shortbow",
            WeaponCategory = "martial",
            IsRangedAttack = true,
        };

        StrikeResolutionContext shortbowContext = PrepareStrike(rogue, shortbowStrike, target);

        Assert.That(shortbowContext.DamageDice.Count, Is.EqualTo(2));
        Assert.That(shortbowContext.DamageDice.Last().numberOfDice, Is.EqualTo(1));
        Assert.That(shortbowContext.DamageDice.Last().sidesPerDie, Is.EqualTo(6));
        Assert.That(shortbowContext.DamageDice.Last().damageType, Is.EqualTo("precision"));
    }

    private static ConditionApplicationSnapshot PersistedOffGuard(
        GameObject sourceCreature,
        string identity
    )
    {
        DungeonPartyMemberIdentity sourceIdentity =
            sourceCreature.GetComponent<DungeonPartyMemberIdentity>();
        if (sourceIdentity == null || !sourceIdentity.IsConfigured)
            throw new System.InvalidOperationException(
                $"Persisted Off-Guard source '{sourceCreature.name}' requires a configured dungeon party identity."
            );
        RuleSource source = RuleSource.FromSlug(identity);
        return new ConditionApplicationSnapshot(
            new ActiveEffectId($"effect-{identity}"),
            new BindingId($"binding-{identity}"),
            ConditionRuleDefinitions.OffGuard,
            sourceIdentity.RosterSlotId,
            source,
            EffectDuration.Indefinite,
            EffectStateVersion.Initial,
            ConditionMarkerState.Instance,
            ActiveEffectStatus.Active,
            1,
            true,
            null
        );
    }

    private CreatureComponent CreatePreparedBarbarian()
    {
        GameObject go = new("Prepared Barbarian");
        created.Add(go);
        CreatureComponent creature = go.AddComponent<CreatureComponent>();
        go.AddComponent<Conditions>();
        creature.level = 1;
        creature.conMod = 1;
        creature.InitializeHealthBeforeEncounter(10, 10);
        creature.Build = new CharacterBuild
        {
            ClassName = "Barbarian",
            SubclassName = "Fury Instinct",
            ClassFeatName = "Raging Intimidation",
        };
        creature.Prepared = Pf2eCharacterPreparer.Prepare(creature, creature.Build);
        return creature;
    }

    private UnityCombatRulesBridge CreateCombatRules(CreatureComponent creature)
    {
        TestActionController controller = creature.gameObject.AddComponent<TestActionController>();
        GameObject opponentObject = new("Encounter Opponent");
        created.Add(opponentObject);
        Team opponentTeam = opponentObject.AddComponent<Team>();
        opponentTeam.Name = "enemies";
        CreatureComponent opponent = opponentObject.AddComponent<CreatureComponent>();
        opponent.InitializeHealthBeforeEncounter(10, 10);
        TestActionController opponentController =
            opponentObject.AddComponent<TestActionController>();
        creature.transform.position = Vector3.zero;
        opponentObject.transform.position = Vector3.right;
        GridPrivate.Tile[,] tiles = new GridPrivate.Tile[2, 1];
        tiles[0, 0] = new GridPrivate.Tile();
        tiles[1, 0] = new GridPrivate.Tile();
        return UnityCombatRulesBridge.Create(
            new ActionController[] { controller, opponentController },
            tiles,
            new ScriptedRollService(20, 10)
        );
    }

    private CreatureComponent CreatePreparedRogue()
    {
        GameObject go = new("Prepared Rogue");
        created.Add(go);
        CreatureComponent creature = go.AddComponent<CreatureComponent>();
        go.AddComponent<Conditions>();
        creature.level = 1;
        creature.strMod = 1;
        creature.dexMod = 4;
        creature.Build = new CharacterBuild
        {
            ClassName = "Rogue",
            SubclassName = "Thief",
            ClassFeatName = "Nimble Dodge",
        };
        creature.Build.TrainedSkills.Add("stealth");
        creature.Prepared = Pf2eCharacterPreparer.Prepare(creature, creature.Build);
        return creature;
    }

    private CreatureComponent CreateTarget(string name)
    {
        GameObject go = new(name);
        created.Add(go);
        int identity = nextTargetIdentity++;
        go.AddComponent<DungeonPartyMemberIdentity>()
            .Configure($"pf2e-target-{identity}", $"pf2e-target-content-{identity}");
        CreatureComponent creature = go.AddComponent<CreatureComponent>();
        go.AddComponent<Conditions>();
        creature.ac = 15;
        creature.InitializeHealthBeforeEncounter(10, 10);
        return creature;
    }

    private StrikeResolutionContext PrepareStrike(
        CreatureComponent attacker,
        StrikeProfile profile,
        CreatureComponent target = null
    )
    {
        CreatureComponent resolvedTarget = target ?? CreateTarget("Prepared Strike Target");
        StrikeResolutionContext context = StrikeResolutionContext.FromRequest(
            new StrikeResolutionRequest
            {
                Attacker = attacker.gameObject,
                Target = resolvedTarget.gameObject,
                Profile = profile,
                TargetingResult = new StrikeTargetResult
                {
                    Target = resolvedTarget.gameObject,
                    LineOfEffect = StrikeLineOfEffect.Clear,
                    Cover = StrikeCover.None,
                },
            }
        );
        PreparedRulePackage package = Compile(attacker);
        RulesSnapshot snapshot;
        CreatureId actor;
        CreatureId targetId = default;
        ActionController controller = attacker.GetComponent<ActionController>();
        if (
            controller != null
            && controller.TryGetCombatRules(out UnityCombatRulesBridge bridge, out actor)
        )
        {
            snapshot = bridge.Snapshot;
            bridge.TryGetCreatureId(resolvedTarget, out targetId);
        }
        else
        {
            actor = new CreatureId($"prepared-test-{created.Count}");
            RulesStateSeed seed = new RulesStateSeed().SeedPreparedInputs(actor, package.Inputs);
            foreach (PreparedBindingSeed binding in package.Bindings)
                seed.SeedRuleBinding(binding.Create(actor));
            snapshot = new InMemoryRulesStore(seed).Snapshot;
        }
        IReadOnlyList<string> targetConditions = targetId.IsEmpty
            ? Array.Empty<string>()
            : RuntimeOptionResolver.ResolveTargetConditions(
                snapshot,
                targetId,
                Array.Empty<string>()
            );
        RuleDispatcher dispatcher = CreatePreparedDispatcher(package, actor, snapshot);
        PreparedContributionContext baseContext = new(
            profile.ItemSlug ?? "test-item",
            profile.WeaponCategory ?? string.Empty,
            profile.IsRangedAttack,
            profile.DamageDice[0].sidesPerDie,
            profile.Traits,
            Array.Empty<string>(),
            targetConditions
        );
        string[] tags = Resolve(
                dispatcher.Dispatch(
                    new CollectPreparedItemAlterationsOp(actor, "weapon", "other-tags", baseContext)
                )
            )
            .Where(value => string.Equals(value.Mode, "add", StringComparison.OrdinalIgnoreCase))
            .Select(value => value.Value)
            .ToArray();
        PreparedContributionContext preparedContext = new(
            profile.ItemSlug ?? "test-item",
            profile.WeaponCategory ?? string.Empty,
            profile.IsRangedAttack,
            profile.DamageDice[0].sidesPerDie,
            profile.Traits,
            tags,
            targetConditions
        );
        IReadOnlyList<PreparedModifierValue> flat = Resolve(
            dispatcher.Dispatch(
                new CollectPreparedModifiersOp(actor, "strike-damage", preparedContext)
            )
        );
        foreach (PreparedModifierValue value in flat.Where(value => value.Value != 0))
            context.FlatDamages.Add(new DamageValue(profile.DamageDice[0].damageType, value.Value));
        if (!profile.IsRangedAttack)
        {
            PreparedModifierValue ability = Resolve(
                    dispatcher.Dispatch(
                        new CollectPreparedModifiersOp(
                            actor,
                            "melee-strike-damage",
                            preparedContext
                        )
                    )
                )
                .LastOrDefault(value => !string.IsNullOrWhiteSpace(value.Ability));
            if (ability != null)
            {
                int current =
                    profile.FlatDamages.Count == 0 ? 0 : profile.FlatDamages[0].DamageAmount;
                context.FlatDamages.Add(
                    new DamageValue(
                        profile.DamageDice[0].damageType,
                        package.Inputs.Abilities.Get(ability.Ability) - current
                    )
                );
            }
        }
        foreach (
            PreparedDamageDiceSpec die in Resolve(
                dispatcher.Dispatch(
                    new CollectPreparedDamageDiceOp(actor, "strike-damage", preparedContext)
                )
            )
        )
        {
            context.DamageDice.Add(new Dice(die.DiceNumber, die.DieSize, die.Category));
        }
        return context;
    }

    private static IReadOnlyList<T> Resolve<T>(
        System.Threading.Tasks.ValueTask<OpResult<IReadOnlyList<T>>> pending
    ) => ((ResolvedOpResult<IReadOnlyList<T>>)pending.AsTask().GetAwaiter().GetResult()).Value;

    private static RuleDispatcher CreatePreparedDispatcher(
        PreparedRulePackage package,
        CreatureId actor,
        RulesSnapshot sourceSnapshot
    ) => CreatePreparedDispatcher(package, actor, sourceSnapshot, binding => binding);

    private static RuleDispatcher CreatePreparedDispatcher(
        PreparedRulePackage package,
        CreatureId actor,
        RulesSnapshot sourceSnapshot,
        Func<ActiveRuleBinding, ActiveRuleBinding> mapBinding
    )
    {
        RulesStateSeed seed = new RulesStateSeed().SeedPreparedInputs(actor, package.Inputs);
        foreach (KeyValuePair<BindingId, ActiveRuleBinding> binding in sourceSnapshot.RuleBindings)
        {
            if (binding.Value.Owner == actor)
            {
                seed.SeedRuleBinding(mapBinding(binding.Value));
                if (
                    binding.Value.EffectId.HasValue
                    && sourceSnapshot.ActiveEffects.TryGet(
                        binding.Value.EffectId.Value,
                        out ActiveEffectInstance effect
                    )
                )
                    seed.SeedActiveEffect(effect);
            }
        }
        RuleRegistryBuilder registryBuilder = new();
        foreach (PreparedRuleDefinitionSpec definition in package.Definitions)
            registryBuilder.Define(definition);
        foreach (KeyValuePair<BindingId, ActiveRuleBinding> binding in sourceSnapshot.RuleBindings)
        {
            if (!package.Definitions.Any(value => value.Id == binding.Value.DefinitionId))
                registryBuilder.Define(binding.Value.DefinitionId);
        }
        RuleRegistry registry = registryBuilder.Build();
        return new RuleDispatcherBuilder(new InMemoryRulesStore(seed))
            .UseRuleRegistry(registry)
            .UsePreparedContributions()
            .Build();
    }

    private static PreparedRulePackage Compile(CreatureComponent creature) =>
        Pf2eCharacterPreparer.Compile(creature, creature.Build ?? new CharacterBuild());

    private static bool HasOwned(PreparedRulePackage package, string slug) =>
        package.Inputs.BoundOptions.Any(option => option.Option == $"item:owned:{slug}");

    private static StrikeProfile CreateDogslicerStrike(CreatureComponent rogue)
    {
        return new StrikeProfile(
            new List<Dice> { new Dice(1, 6, "slashing") },
            new List<DamageValue> { new DamageValue("slashing", rogue.strMod) }
        )
        {
            Traits = new List<string> { "agile", "finesse" },
            ItemSlug = "dogslicer",
            WeaponCategory = "martial",
        };
    }

    private static string PackageFingerprint(PreparedRulePackage package) =>
        string.Join(
            "|",
            new[]
            {
                package.Inputs.Level.ToString(),
                string.Join(
                    ",",
                    new[]
                    {
                        package.Inputs.Abilities.Strength,
                        package.Inputs.Abilities.Dexterity,
                        package.Inputs.Abilities.Constitution,
                        package.Inputs.Abilities.Intelligence,
                        package.Inputs.Abilities.Wisdom,
                        package.Inputs.Abilities.Charisma,
                    }
                ),
                string.Join(
                    ",",
                    package
                        .Inputs.SkillRanks.OrderBy(value => value.Key)
                        .Select(value => $"{value.Key}:{value.Value}")
                ),
                string.Join(",", package.Inputs.Equipment),
                package.Inputs.ArmorCategory,
                string.Join(",", package.Inputs.Traits),
                string.Join(",", package.Inputs.StaticOptions),
                string.Join(
                    ",",
                    package.Inputs.BoundOptions.Select(value =>
                        $"{value.DefinitionId.Value}:{value.Option}:{PredicateFingerprint(value.Predicate)}"
                    )
                ),
                string.Join(
                    ",",
                    package
                        .Inputs.RuleValues.OrderBy(value => value.Key)
                        .Select(value => $"{value.Key}:{value.Value}")
                ),
                string.Join(
                    ",",
                    package.Inputs.Weaknesses.Select(value => $"{value.Type}:{value.Value}")
                ),
                string.Join(
                    ",",
                    package.Inputs.Resistances.Select(value => $"{value.Type}:{value.Value}")
                ),
                string.Join(
                    ",",
                    package.Inputs.Immunities.Select(value => $"{value.Type}:{value.Kind}")
                ),
                string.Join(
                    ",",
                    package.Definitions.Select(value =>
                        $"{value.Id.Value}:{value.Source.Slug}:{value.RuleKey}:{value.Provenance}:{value.Signature}"
                    )
                ),
                string.Join(
                    ",",
                    package.Bindings.Select(value =>
                        $"{value.StableKey}:{value.DefinitionId.Value}:{value.CreationOrder}"
                    )
                ),
                string.Join(
                    ",",
                    package.Diagnostics.Select(value =>
                        $"{value.Source.Slug}:{value.Key}:{value.Provenance}"
                    )
                ),
            }
        );

    private static string PredicateFingerprint(PreparedPredicate predicate) =>
        predicate switch
        {
            PreparedConstantPredicate constant => $"constant:{constant.Value}",
            PreparedOptionPredicate option => $"option:{option.Option}",
            PreparedNumericAtLeastPredicate numeric =>
                $"numeric:{numeric.Kind}:{numeric.Key}:{numeric.Minimum}",
            PreparedAllPredicate all =>
                $"all({string.Join(",", all.Children.Select(PredicateFingerprint))})",
            PreparedAnyPredicate any =>
                $"any({string.Join(",", any.Children.Select(PredicateFingerprint))})",
            PreparedNotPredicate not => $"not({PredicateFingerprint(not.Child)})",
            _ => throw new InvalidOperationException(
                $"Unknown prepared predicate {predicate.GetType().Name}."
            ),
        };

    private void AttachPreparedStrikeBridge(CreatureComponent attacker, CreatureComponent target)
    {
        TestActionController attackerController =
            attacker.GetComponent<TestActionController>()
            ?? attacker.gameObject.AddComponent<TestActionController>();
        TestActionController targetController =
            target.GetComponent<TestActionController>()
            ?? target.gameObject.AddComponent<TestActionController>();
        UnityCombatRulesBridge.Create(
            new ActionController[] { attackerController, targetController },
            CreateTiles()
        );
    }

    private UnityCombatRulesBridge CreateActiveEncounter(ActionController protagonist)
    {
        protagonist.GetComponent<CreatureComponent>().initiative = 100;
        GameObject opposition = new("Rules Test Opposition");
        created.Add(opposition);
        CreatureComponent creature = opposition.AddComponent<CreatureComponent>();
        creature.InitializeHealthBeforeEncounter(1, 1);
        Team team = opposition.AddComponent<Team>();
        team.Name = "Enemies";
        TestActionController controller = opposition.AddComponent<TestActionController>();
        return UnityCombatRulesBridge.Create(new[] { protagonist, controller }, CreateTiles());
    }

    private sealed class TestActionController : ActionController
    {
        public override void EndTurn() { }
    }
}
