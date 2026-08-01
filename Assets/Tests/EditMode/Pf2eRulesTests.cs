using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Strike;
using GridPublic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

public class Pf2eRulesTests
{
    private readonly List<GameObject> created = new();

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
        Assert.That(creature.Prepared.HasOwnedItem("barbarian"), Is.True);
        Assert.That(creature.Prepared.HasOwnedItem("quick-tempered"), Is.True);
        Assert.That(creature.Prepared.HasOwnedItem("rage"), Is.True);
        Assert.That(creature.Prepared.HasOwnedItem("fury-instinct"), Is.True);
        Assert.That(creature.Prepared.HasOwnedItem("raging-intimidation"), Is.True);
        Assert.That(
            creature.Prepared.Build.RuleSelections["furyInstinct"],
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
    public void ZombiePassiveSlowAppliesAtCombatStart()
    {
        GameObject zombie = CreatureJsonConverter.CreateFromFile(
            "DataFiles/pathfinder-monster-core/zombie-shambler"
        );
        created.Add(zombie);
        TestActionController actionController = zombie.AddComponent<TestActionController>();

        Assert.That(zombie.GetComponent<CreatureComponent>().passives, Does.Contain("Slow"));

        Pf2eRulesEngine.ApplyCombatStartRules(new[] { actionController });

        Assert.That(zombie.GetComponent<Conditions>().Contains("Slowed"), Is.True);
        Team team = zombie.AddComponent<Team>();
        team.Name = "Players";
        UnityCombatRulesBridge bridge = CreateActiveEncounter(actionController);
        bridge.StartEncounter("Players");
        Assert.That(actionController.ActionPoints, Is.EqualTo(2));
    }

    [Test]
    public void ZombiePassiveSlowDoesNotStackWhenCombatStartRulesRunAgain()
    {
        GameObject zombie = CreatureJsonConverter.CreateFromFile(
            "DataFiles/pathfinder-monster-core/zombie-shambler"
        );
        created.Add(zombie);
        TestActionController actionController = zombie.AddComponent<TestActionController>();

        Pf2eRulesEngine.ApplyCombatStartRules(new[] { actionController });
        Pf2eRulesEngine.ApplyCombatStartRules(new[] { actionController });

        Team team = zombie.AddComponent<Team>();
        team.Name = "Players";
        UnityCombatRulesBridge bridge = CreateActiveEncounter(actionController);
        bridge.StartEncounter("Players");
        Assert.That(actionController.ActionPoints, Is.EqualTo(2));
    }

    [Test]
    public void PredicateSupportsAtomicCompoundAndNumericChecks()
    {
        CreatureComponent creature = CreatePreparedBarbarian();
        creature.level = 7;
        creature.Build.TrainedSkills.Add("intimidation");
        creature.Prepared = Pf2eCharacterPreparer.Prepare(creature, creature.Build);
        PreparedRulePackage package = creature.Prepared.Rules;
        CreatureId actor = new("predicate-actor");
        RulesStateSeed seed = new();
        foreach (PreparedBindingSeed binding in package.Bindings)
            seed.SeedRuleBinding(binding.Create(actor));
        PreparedPredicateContext context = new(
            package,
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
        if (!RageRules.GetActiveRollOptions(bridge.Snapshot, actor).Contains("self:effect:rage"))
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

        PreparedRulePackage package = creature.Prepared.Rules;
        if (!RageRules.GetActiveRollOptions(bridge.Snapshot, actor).Contains("self:effect:rage"))
            Assert.That(
                bridge.Dispatch(new RageActionOp(actor)),
                Is.TypeOf<ResolvedOpResult<RageStartOutcome>>()
            );
        PreparedPredicateContext duringContext = new(
            package,
            bridge.Snapshot,
            actor,
            new[] { "item:slug:demoralize" }
        );
        Assert.That(
            PreparedRuleCollectors
                .CollectItemAlterations(package, duringContext, "action", "traits")
                .Select(value => value.Value),
            Does.Contain("rage")
        );
    }

    [Test]
    public void LenaPreparesRogueFeaturesFromBuildData()
    {
        GameObject lena = CreatureJsonConverter.CreateFromFile("DataFiles/playerCharacters/Lena");
        created.Add(lena);
        CreatureComponent creature = lena.GetComponent<CreatureComponent>();

        Assert.That(creature.Build.ClassName, Is.EqualTo("Rogue"));
        Assert.That(creature.Build.SubclassName, Is.EqualTo("Thief"));
        Assert.That(creature.Prepared.HasOwnedItem("rogue"), Is.True);
        Assert.That(creature.Prepared.HasOwnedItem("rogues-racket"), Is.True);
        Assert.That(creature.Prepared.HasOwnedItem("sneak-attack"), Is.True);
        Assert.That(creature.Prepared.HasOwnedItem("surprise-attack"), Is.True);
        Assert.That(creature.Prepared.HasOwnedItem("thief"), Is.True);
        Assert.That(creature.Prepared.HasOwnedItem("nimble-dodge"), Is.True);
        Assert.That(creature.Prepared.SkillRanks["stealth"], Is.EqualTo(1));
        Assert.That(creature.Prepared.SkillRanks["thievery"], Is.EqualTo(1));
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

        Assert.That(creature.Prepared.RollOptions, Does.Not.Contain("target:condition:off-guard"));
        Assert.That(creature.Prepared.RollOptions, Does.Not.Contain("nimble-dodge"));
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
            string first = PackageFingerprint(Pf2eCharacterPreparer.Prepare(creature, build).Rules);
            string second = PackageFingerprint(
                Pf2eCharacterPreparer.Prepare(creature, build).Rules
            );
            Assert.That(second, Is.EqualTo(first));
        }
        Assert.That(
            Pf2eCharacterPreparer
                .Prepare(zombie, zombie.Build ?? new CharacterBuild())
                .Rules.Inputs.Immunities.Any(value => value.IsDeathEffect),
            Is.True
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

        target.GetComponent<Conditions>().Add("Off-Guard", new ConditionSource());
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
        target.GetComponent<Conditions>().Add("Flat-Footed", new ConditionSource());

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
        PreparedRulePackage package = attacker.Prepared.Rules;
        RulesSnapshot snapshot;
        CreatureId actor;
        ActionController controller = attacker.GetComponent<ActionController>();
        if (
            controller != null
            && controller.TryGetCombatRules(out UnityCombatRulesBridge bridge, out actor)
        )
        {
            snapshot = bridge.Snapshot;
        }
        else
        {
            actor = new CreatureId($"prepared-test-{created.Count}");
            RulesStateSeed seed = new();
            foreach (PreparedBindingSeed binding in package.Bindings)
                seed.SeedRuleBinding(binding.Create(actor));
            snapshot = new InMemoryRulesStore(seed).Snapshot;
        }
        StrikeItemDefinition item = new(
            new ItemId($"prepared-item-{created.Count}"),
            new ItemDefinitionId(profile.ItemSlug ?? "test-item"),
            profile.ItemSlug ?? "Test Item",
            string.Empty,
            profile.WeaponCategory ?? string.Empty,
            profile.Traits.Select(Trait.FromSlug),
            0,
            profile.DamageDice.Select(die => new TypedDamageDice(
                new DiceExpression(die.numberOfDice, die.sidesPerDie),
                die.damageType,
                "Test"
            )),
            profile.FlatDamages.Select(value => new TypedFlatDamage(
                value.DamageAmount,
                value.DamageType,
                "Test"
            )),
            5,
            profile.IsRangedAttack ? 60 : 0,
            0,
            StrikeAmmunitionRequirement.None
        );
        bool offGuard = resolvedTarget
            .GetComponent<Conditions>()
            .GetConditionNames()
            .Any(value => value == "Off-Guard" || value == "Flat-Footed");
        PreparedStrikeContributions contribution = UnityPreparedStrikeDataAdapter.Capture(
            attacker,
            resolvedTarget,
            item,
            StrikeTargetingOutcome.Legal(5, 0, 0, offGuard),
            snapshot,
            actor
        );
        foreach (TypedDamageDice die in contribution.DamageDice)
            context.DamageDice.Add(new Dice(die.Dice.Count, die.Dice.Sides, die.DamageType));
        foreach (TypedFlatDamage value in contribution.FlatDamage)
            context.FlatDamages.Add(new DamageValue(value.DamageType, value.Amount));
        return context;
    }

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
                    package
                        .Inputs.SkillRanks.OrderBy(value => value.Key)
                        .Select(value => $"{value.Key}:{value.Value}")
                ),
                string.Join(
                    ",",
                    package.Definitions.Select(value =>
                        $"{value.Id.Value}:{value.Source.Slug}:{value.RuleKey}:{value.Provenance}"
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
