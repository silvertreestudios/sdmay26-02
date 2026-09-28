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

    private static SpellReference Spell(string slug) => new(new SpellId(slug), 1);

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
    public void MarenLoadsCompleteClericRulesFromCheckedInData()
    {
        GameObject maren = CreatureJsonConverter.CreateByName("Maren");
        Assert.That(maren, Is.Not.Null);
        created.Add(maren);
        CreatureComponent creature = maren.GetComponent<CreatureComponent>();
        SpellReference[] migrated =
        {
            Spell("shield"),
            Spell("guidance"),
            Spell("haunting-hymn"),
            Spell("bless"),
            Spell("infuse-vitality"),
            Spell("heal"),
        };

        Assert.That(creature.Build.ClassName, Is.EqualTo("Cleric"));
        Assert.That(creature.Build.SubclassName, Is.EqualTo("Cloistered Cleric"));
        Assert.That(creature.Prepared.HasOwnedItem("cleric"), Is.True);
        Assert.That(creature.willSave, Is.EqualTo(9));
        Assert.That(
            creature.skills.Single(value => value.skillName == "religion").skillMod,
            Is.EqualTo(7)
        );
        EquipmentWeapon mace = creature.weapons.Single(value => value.name == "Mace");
        Assert.That(mace.category, Is.EqualTo("simple"));
        Assert.That(mace.group, Is.EqualTo("club"));
        Assert.That(mace.hands, Is.EqualTo(1));
        Assert.That(mace.damage.numberOfDice, Is.EqualTo(1));
        Assert.That(mace.damage.sidesPerDie, Is.EqualTo(6));
        Assert.That(mace.damage.damageType, Is.EqualTo("bludgeoning"));
        Assert.That(mace.traits, Does.Contain("shove"));
        Assert.That(
            migrated.All(spell => creature.Prepared.SpellBook.CastableSpells.Contains(spell)),
            Is.True
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
        bridge.AdvanceEncounter();
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
        bridge.AdvanceEncounter();
        Assert.That(actionController.ActionPoints, Is.EqualTo(2));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ApplyingSlowedInstallsMissingConditionDisplay(bool attached)
    {
        TestActionController controller = CreateCombatant("Missing condition display", "Players");
        Object.DestroyImmediate(controller.GetComponent<Conditions>());
        UnityCombatRulesBridge bridge;
        if (attached)
        {
            bridge = CreateActiveEncounter(controller);
            ApplySlowed(controller.gameObject, 2);
        }
        else
        {
            ApplySlowed(controller.gameObject, 2);
            bridge = CreateActiveEncounter(controller);
        }
        Assert.That(controller.GetComponent<Conditions>().Contains("Slowed"), Is.True);
        bridge.AdvanceEncounter();
        Assert.That(controller.ActionPoints, Is.EqualTo(1));
        bridge.ReleaseOwnership();
    }

    [Test]
    public void RulesConditionApplicationInstallsMissingDisplayDuringProjection()
    {
        TestActionController controller = CreateCombatant(
            "Rules-only condition application",
            "Players"
        );
        Object.DestroyImmediate(controller.GetComponent<Conditions>());
        UnityCombatRulesBridge bridge = CreateActiveEncounter(controller);
        ApplyTimedSlowed(bridge, controller, 2, EffectDuration.OneMinute);
        Assert.That(controller.GetComponent<Conditions>().Contains("Slowed"), Is.True);
        bridge.ReleaseOwnership();
    }

    [Test]
    public void ZombiePassiveRemainsIdempotentAfterProjectionAndReenrollment()
    {
        GameObject zombie = CreatureJsonConverter.CreateFromFile(
            "DataFiles/pathfinder-monster-core/zombie-shambler"
        );
        created.Add(zombie);
        TestActionController controller = zombie.AddComponent<TestActionController>();
        zombie.AddComponent<Team>().Name = "Players";
        Pf2eRulesEngine.ApplyCombatStartRules(new[] { controller });
        UnityCombatRulesBridge first = CreateActiveEncounter(controller);
        first.AdvanceEncounter();
        ApplyTimedSlowed(first, controller, 2, EffectDuration.Rounds(1));
        EndRound(first);
        Pf2eRulesEngine.ApplyCombatStartRules(new[] { controller });
        Assert.That(
            ConditionRules.GetApplications(first.Snapshot, first.GetCreatureId(controller)).Count(),
            Is.EqualTo(1)
        );
        first.ReleaseOwnership();

        Pf2eRulesEngine.ApplyCombatStartRules(new[] { controller });
        UnityCombatRulesBridge second = CreateActiveEncounter(controller);
        Assert.That(
            ConditionRules
                .GetApplications(second.Snapshot, second.GetCreatureId(controller))
                .Count(),
            Is.EqualTo(1)
        );
        second.ReleaseOwnership();
    }

    [TestCase(1, 2)]
    [TestCase(2, 1)]
    [TestCase(3, 0)]
    [TestCase(5, 0)]
    public void SlowedTierReducesTheCommittedTurnAllowance(int tier, int expectedActions)
    {
        TestActionController controller = CreateSlowedCombatant("Tiered Slowed", tier);
        UnityCombatRulesBridge bridge = CreateActiveEncounter(controller);

        bridge.AdvanceEncounter();

        CreatureId actor = bridge.GetCreatureId(controller);
        Assert.That(
            ConditionRules.GetValue(bridge.Snapshot, actor, SlowedRules.ConditionId),
            Is.EqualTo(tier)
        );
        Assert.That(controller.ActionPoints, Is.EqualTo((uint)expectedActions));
        bridge.ReleaseOwnership();
    }

    [Test]
    public void SlowedApplicationsUseTheHighestValueInsteadOfAddingValues()
    {
        TestActionController controller = CreateSlowedCombatant("Valued Slowed", 1);
        UnityCombatRulesBridge bridge = CreateActiveEncounter(controller);
        bridge.AdvanceEncounter();

        ApplySlowed(controller.gameObject, 3);
        ApplySlowed(controller.gameObject, 2);
        CreatureId actor = bridge.GetCreatureId(controller);
        Assert.That(
            ConditionRules.GetValue(bridge.Snapshot, actor, SlowedRules.ConditionId),
            Is.EqualTo(3)
        );

        bridge.EndTurn(actor);
        bridge.EndTurn(bridge.GetEncounter().CurrentTurn.Value.Actor);
        Assert.That(controller.ActionPoints, Is.Zero);
        bridge.ReleaseOwnership();
    }

    [Test]
    public void SlowedSeedEnrollsIntoEachEncounterWithoutDuplicatingItsEffect()
    {
        TestActionController controller = CreateSlowedCombatant("Repeated Slowed", 2);

        UnityCombatRulesBridge first = CreateActiveEncounter(controller);
        first.AdvanceEncounter();
        Assert.That(controller.ActionPoints, Is.EqualTo(1));
        first.ReleaseOwnership();

        UnityCombatRulesBridge second = CreateActiveEncounter(controller);
        second.AdvanceEncounter();
        CreatureId actor = second.GetCreatureId(controller);
        Assert.That(ConditionRules.GetApplications(second.Snapshot, actor).Count(), Is.EqualTo(1));
        Assert.That(controller.ActionPoints, Is.EqualTo(1));
        second.ReleaseOwnership();
    }

    [Test]
    public void RemovingSlowedClearsItsSeedBeforeImmediateReenrollment()
    {
        TestActionController controller = CreateCombatant("Removed Slowed", "Players");
        UnityCombatRulesBridge first = CreateActiveEncounter(controller);
        first.AdvanceEncounter();

        ApplyTimedSlowed(first, controller, 2, EffectDuration.Rounds(1));
        EndRound(first);
        Assert.That(controller.GetComponent<ConditionSeed>().Applications, Is.Empty);
        Assert.That(controller.GetComponent<Conditions>().Contains("Slowed"), Is.False);
        first.ReleaseOwnership();

        UnityCombatRulesBridge second = CreateActiveEncounter(controller);
        second.AdvanceEncounter();
        CreatureId actor = second.GetCreatureId(controller);
        Assert.That(ConditionRules.GetApplications(second.Snapshot, actor).Any(), Is.False);
        Assert.That(controller.ActionPoints, Is.EqualTo(3));
        second.ReleaseOwnership();
    }

    [Test]
    public void SlowedCanBeReappliedImmediatelyAfterRemoval()
    {
        TestActionController controller = CreateCombatant("Reapplied Slowed", "Players");
        UnityCombatRulesBridge first = CreateActiveEncounter(controller);
        first.AdvanceEncounter();

        ApplyTimedSlowed(first, controller, 2, EffectDuration.Rounds(1));
        EndRound(first);
        first.ReleaseOwnership();
        ApplySlowed(controller.gameObject, 1);

        UnityCombatRulesBridge second = CreateActiveEncounter(controller);
        second.AdvanceEncounter();
        CreatureId actor = second.GetCreatureId(controller);
        Assert.That(
            ConditionRules.GetValue(second.Snapshot, actor, SlowedRules.ConditionId),
            Is.EqualTo(1)
        );
        Assert.That(
            controller.GetComponent<ConditionSeed>().Applications.Single().State.Value,
            Is.EqualTo(1)
        );
        Assert.That(controller.ActionPoints, Is.EqualTo(2));
        second.ReleaseOwnership();
    }

    [Test]
    public void SlowedReinforcementUsesTheCommonEnrollmentPath()
    {
        TestActionController initial = CreateCombatant("Initial", "Players");
        TestActionController reinforcement = CreateSlowedCombatant("Slowed Reinforcement", 2);
        reinforcement.GetComponent<Team>().Name = "Enemies";
        UnityCombatRulesBridge bridge = CreateActiveEncounter(initial);
        bridge.AdvanceEncounter();

        bridge.AddCombatants(new ActionController[] { reinforcement });

        CreatureId actor = bridge.GetCreatureId(reinforcement);
        Assert.That(
            ConditionRules.GetValue(bridge.Snapshot, actor, SlowedRules.ConditionId),
            Is.EqualTo(2)
        );
        Assert.That(
            bridge.Snapshot.RuleBindings.Contains(SlowedRules.CreateBinding(actor).Id),
            Is.True
        );
        bridge.ReleaseOwnership();
    }

    [Test]
    public void TimedSlowedExpiresBackToPermanentApplication()
    {
        TestActionController controller = CreateSlowedCombatant("Permanent and temporary", 1);
        UnityCombatRulesBridge bridge = CreateActiveEncounter(controller);
        bridge.AdvanceEncounter();
        CreatureId actor = bridge.GetCreatureId(controller);

        ApplyTimedSlowed(bridge, controller, 2, EffectDuration.OneMinute);
        Assert.That(ConditionRules.GetApplications(bridge.Snapshot, actor).Count(), Is.EqualTo(2));
        Assert.That(
            controller.ActionPoints,
            Is.EqualTo(2),
            "Applying Slowed does not consume current actions."
        );
        EndRound(bridge);
        Assert.That(
            controller.ActionPoints,
            Is.EqualTo(1),
            "Two applications must apply only one maximum penalty."
        );
        for (int i = 1; i < 10; i++)
            EndRound(bridge);

        Assert.That(
            ConditionRules.GetValue(bridge.Snapshot, actor, SlowedRules.ConditionId),
            Is.EqualTo(1)
        );
        Assert.That(ConditionRules.GetApplications(bridge.Snapshot, actor).Count(), Is.EqualTo(1));
        Assert.That(controller.ActionPoints, Is.EqualTo(2));
        Assert.That(controller.GetComponent<Conditions>().Contains("Slowed"), Is.True);
        Assert.That(
            controller.GetComponent<ConditionSeed>().Applications.Single().State.Value,
            Is.EqualTo(1)
        );
        bridge.ReleaseOwnership();
    }

    [Test]
    public void IndependentIndefiniteApplicationsSurviveReenrollment()
    {
        TestActionController controller = CreateSlowedCombatant("Independent seeds", 1);
        ApplySlowed(controller.gameObject, 2);
        UnityCombatRulesBridge first = CreateActiveEncounter(controller);
        first.AdvanceEncounter();
        ApplySlowed(controller.gameObject, 5);
        first.ReleaseOwnership();

        UnityCombatRulesBridge second = CreateActiveEncounter(controller);
        CreatureId actor = second.GetCreatureId(controller);
        Assert.That(
            ConditionRules
                .GetApplications(second.Snapshot, actor)
                .Select(effect => effect.GetState<ConditionState>().Value),
            Is.EquivalentTo(new[] { 1, 2, 5 })
        );
        second.AdvanceEncounter();
        Assert.That(controller.ActionPoints, Is.Zero);
        EndRound(second);
        Assert.That(
            ConditionRules.GetValue(second.Snapshot, actor, SlowedRules.ConditionId),
            Is.EqualTo(5),
            "Slowed is not consumed by lost actions."
        );
        second.ReleaseOwnership();
    }

    [Test]
    public void PredicateSupportsAtomicCompoundAndNumericChecks()
    {
        PreparedCharacter prepared = new(new CharacterBuild());
        prepared.RollOptions.Add("class:barbarian");
        prepared.RollOptions.Add("self:effect:rage");
        prepared.RollOptions.Add("self:level:7");
        prepared.SkillRanks["intimidation"] = 4;

        Assert.That(
            Pf2ePredicate.Evaluate(
                JToken.Parse("[\"class:barbarian\", {\"not\": \"item:ranged\"}]"),
                prepared
            ),
            Is.True
        );
        Assert.That(
            Pf2ePredicate.Evaluate(
                JToken.Parse("[{\"or\": [\"item:ranged\", \"self:effect:rage\"]}]"),
                prepared
            ),
            Is.True
        );
        Assert.That(
            Pf2ePredicate.Evaluate(
                JToken.Parse(
                    "[{\"gte\": [\"self:level\", 7]}, {\"gte\": [\"skill:intimidation:rank\", 4]}]"
                ),
                prepared
            ),
            Is.True
        );
        Assert.That(
            Pf2ePredicate.Evaluate(
                JToken.Parse("[{\"and\": [\"class:barbarian\", {\"not\": \"item:ranged\"}]}]"),
                prepared
            ),
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
        Assert.That(
            bridge.Dispatch(new RageActionOp(actor)),
            Is.TypeOf<ResolvedOpResult<RageStartOutcome>>()
        );

        TestStrikeProfile greataxe = new(
            new List<Dice> { new Dice(1, 12, "Slashing") },
            new List<DamageValue> { new DamageValue("Slashing", 4) }
        );
        PreparedStrikeResult greataxeContext = PrepareStrike(creature, greataxe);
        Assert.That(greataxeContext.FlatDamages.Last().DamageAmount, Is.EqualTo(3));

        TestStrikeProfile agile = new(
            new List<Dice> { new Dice(1, 4, "Bludgeoning") },
            new List<DamageValue> { new DamageValue("Bludgeoning", 4) }
        );
        agile.Traits.Add("agile");
        PreparedStrikeResult agileContext = PrepareStrike(creature, agile);
        Assert.That(agileContext.FlatDamages.Last().DamageAmount, Is.EqualTo(1));

        bridge.Dispatch(new EndRageOp(actor));
        TestStrikeProfile notRaging = new(
            new List<Dice> { new Dice(1, 12, "Slashing") },
            new List<DamageValue> { new DamageValue("Slashing", 4) }
        );
        PreparedStrikeResult notRagingContext = PrepareStrike(creature, notRaging);
        Assert.That(notRagingContext.FlatDamages.Count, Is.EqualTo(1));
    }

    [Test]
    public void RagingIntimidationItemAlterationAddsRageTraitOnlyWhileRaging()
    {
        CreatureComponent creature = CreatePreparedBarbarian();
        UnityCombatRulesBridge bridge = CreateCombatRules(creature);
        CreatureId actor = bridge.GetCreatureId(creature);
        bridge.BeginTurn(actor, 3);

        List<string> beforeRage = Pf2eRulesEngine.GetAlteredTraits(
            creature,
            "action",
            "demoralize",
            new List<string>()
        );
        Assert.That(beforeRage, Does.Not.Contain("rage"));

        Assert.That(
            bridge.Dispatch(new RageActionOp(actor)),
            Is.TypeOf<ResolvedOpResult<RageStartOutcome>>()
        );
        List<string> duringRage = Pf2eRulesEngine.GetAlteredTraits(
            creature,
            "action",
            "demoralize",
            new List<string>()
        );
        Assert.That(duringRage, Does.Contain("rage"));
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
    public void ThiefUsesDexterityForFinesseMeleeDamage()
    {
        CreatureComponent creature = CreatePreparedRogue();

        TestStrikeProfile finesseStrike = new(
            new List<Dice> { new Dice(1, 6, "slashing") },
            new List<DamageValue> { new DamageValue("slashing", creature.strMod) }
        )
        {
            Traits = new List<string> { "agile", "finesse" },
            ItemSlug = "dogslicer",
            WeaponCategory = "martial",
        };
        PreparedStrikeResult finesseContext = PrepareStrike(creature, finesseStrike);
        Assert.That(
            finesseContext.FlatDamages.Sum(value => value.DamageAmount),
            Is.EqualTo(creature.dexMod)
        );

        TestStrikeProfile nonFinesseStrike = new(
            new List<Dice> { new Dice(1, 6, "slashing") },
            new List<DamageValue> { new DamageValue("slashing", creature.strMod) }
        )
        {
            Traits = new List<string> { "forceful" },
            ItemSlug = "scimitar",
            WeaponCategory = "martial",
        };
        PreparedStrikeResult nonFinesseContext = PrepareStrike(creature, nonFinesseStrike);
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

        TestStrikeProfile normalTarget = CreateDogslicerStrike(rogue);
        PreparedStrikeResult normalContext = PrepareStrike(rogue, normalTarget, target);
        Assert.That(normalContext.DamageDice.Count, Is.EqualTo(1));

        target.GetComponent<Conditions>().Add("Off-Guard", new ConditionSource());
        TestStrikeProfile offGuardTarget = CreateDogslicerStrike(rogue);
        PreparedStrikeResult offGuardContext = PrepareStrike(rogue, offGuardTarget, target);
        Assert.That(offGuardContext.DamageDice.Count, Is.EqualTo(2));
        Assert.That(offGuardContext.DamageDice.Last().numberOfDice, Is.EqualTo(1));
        Assert.That(offGuardContext.DamageDice.Last().sidesPerDie, Is.EqualTo(6));
        Assert.That(offGuardContext.DamageDice.Last().damageType, Is.EqualTo("slashing"));

        TestStrikeProfile ineligibleWeapon = new(
            new List<Dice> { new Dice(1, 6, "slashing") },
            new List<DamageValue> { new DamageValue("slashing", rogue.strMod) }
        )
        {
            Traits = new List<string> { "forceful" },
            ItemSlug = "scimitar",
            WeaponCategory = "martial",
        };
        PreparedStrikeResult ineligibleContext = PrepareStrike(rogue, ineligibleWeapon, target);
        Assert.That(ineligibleContext.DamageDice.Count, Is.EqualTo(1));
    }

    [Test]
    public void SneakAttackSupportsRangedWeaponsAndFlatFootedAlias()
    {
        CreatureComponent rogue = CreatePreparedRogue();
        CreatureComponent target = CreateTarget("Flat-Footed Target");
        target.GetComponent<Conditions>().Add("Flat-Footed", new ConditionSource());

        TestStrikeProfile shortbowStrike = new(
            new List<Dice> { new Dice(1, 6, "piercing") },
            new List<DamageValue>()
        )
        {
            Traits = new List<string> { "deadly-d10" },
            ItemSlug = "shortbow",
            WeaponCategory = "martial",
            IsRangedAttack = true,
        };

        PreparedStrikeResult shortbowContext = PrepareStrike(rogue, shortbowStrike, target);

        Assert.That(shortbowContext.DamageDice.Count, Is.EqualTo(2));
        Assert.That(shortbowContext.DamageDice.Last().numberOfDice, Is.EqualTo(1));
        Assert.That(shortbowContext.DamageDice.Last().sidesPerDie, Is.EqualTo(6));
        Assert.That(shortbowContext.DamageDice.Last().damageType, Is.EqualTo("piercing"));
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
        DisableQuickTempered(creature.Prepared);
        return creature;
    }

    private static void DisableQuickTempered(PreparedCharacter prepared)
    {
        prepared.OwnedItems.RemoveAll(item =>
            string.Equals(
                item.Item.Slug,
                "quick-tempered",
                System.StringComparison.OrdinalIgnoreCase
            )
        );
        prepared.RollOptions.Remove("feat:quick-tempered");
    }

    private UnityCombatRulesBridge CreateCombatRules(CreatureComponent creature)
    {
        Team creatureTeam =
            creature.GetComponent<Team>() ?? creature.gameObject.AddComponent<Team>();
        creatureTeam.Name = "Players";
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
            new ScriptedRollService(20, 10),
            "Players"
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

    private PreparedStrikeResult PrepareStrike(
        CreatureComponent attacker,
        TestStrikeProfile profile,
        CreatureComponent target = null
    )
    {
        CreatureComponent resolvedTarget = target ?? CreateTarget("Prepared Strike Target");
        var actor = new CreatureId("prepared-actor");
        var targetId = new CreatureId("prepared-target");
        RulesSnapshot snapshot;
        var controller = attacker.GetComponent<ActionController>();
        if (
            controller != null
            && controller.TryGetCombatRules(
                out UnityCombatRulesBridge bridge,
                out CreatureId attachedActor
            )
        )
        {
            snapshot = bridge.Snapshot;
            actor = attachedActor;
        }
        else
        {
            RulesStateSeed seed = new();
            seed.SeedCreature(new CreatureState(actor, new PlayerId("players")));
            seed.SeedCreature(new CreatureState(targetId, new PlayerId("enemies")));
            int index = 0;
            foreach (
                string condition in resolvedTarget.GetComponent<Conditions>().GetConditionNames()
            )
            {
                var effect = new ActiveEffectInstance(
                    new ActiveEffectId($"condition-{index}"),
                    ConditionRules.DefinitionId,
                    actor,
                    RuleSource.FromSlug("fixture"),
                    EffectDuration.Indefinite,
                    new ConditionState(new ConditionId(condition), 1)
                );
                seed.SeedActiveEffect(effect);
                seed.SeedRuleBinding(
                    new ActiveRuleBinding(
                        new BindingId($"condition-{index++}"),
                        ConditionRules.DefinitionId,
                        targetId,
                        effect.Id,
                        effect.Source,
                        0
                    )
                );
            }
            snapshot = new InMemoryRulesStore(seed).Snapshot;
        }
        var item = new StrikeItemDefinition(
            new ItemId("fixture-item"),
            new ItemDefinitionId(profile.ItemSlug),
            "Fixture",
            "",
            profile.WeaponCategory,
            profile.Traits.Select(Trait.FromSlug),
            0,
            profile.DamageDice.Select(value => new TypedDamageDice(
                new DiceExpression(value.numberOfDice, value.sidesPerDie),
                value.damageType,
                "Fixture"
            )),
            profile.FlatDamages.Select(value => new TypedFlatDamage(
                value.DamageAmount,
                value.DamageType,
                "Base"
            )),
            5,
            profile.IsRangedAttack ? 60 : 0,
            0,
            StrikeAmmunitionRequirement.None
        );
        PreparedStrikeContributions result = PreparedStrikeRules.Evaluate(
            UnityPreparedStrikeDataAdapter.Capture(attacker),
            item,
            StrikeTargetingOutcome.Legal(5, 0, 0, false),
            snapshot,
            actor,
            targetId
        );
        return new PreparedStrikeResult
        {
            FlatDamages = profile
                .FlatDamages.Concat(
                    result.FlatDamage.Select(value => new DamageValue(
                        value.DamageType,
                        value.Amount
                    ))
                )
                .ToList(),
            DamageDice = profile
                .DamageDice.Concat(
                    result.DamageDice.Select(value => new Dice(
                        value.Dice.Count,
                        value.Dice.Sides,
                        value.DamageType
                    ))
                )
                .ToList(),
        };
    }

    private sealed class PreparedStrikeResult
    {
        internal List<DamageValue> FlatDamages;
        internal List<Dice> DamageDice;
    }

    private sealed class TestStrikeProfile
    {
        internal TestStrikeProfile(List<Dice> dice, List<DamageValue> flat)
        {
            DamageDice = dice;
            FlatDamages = flat;
        }

        internal List<Dice> DamageDice;
        internal List<DamageValue> FlatDamages;
        internal List<string> Traits = new();
        internal string ItemSlug = "fixture-item";
        internal string WeaponCategory = "martial";
        internal bool IsRangedAttack;
    }

    private static TestStrikeProfile CreateDogslicerStrike(CreatureComponent rogue)
    {
        return new TestStrikeProfile(
            new List<Dice> { new Dice(1, 6, "slashing") },
            new List<DamageValue> { new DamageValue("slashing", rogue.strMod) }
        )
        {
            Traits = new List<string> { "agile", "finesse" },
            ItemSlug = "dogslicer",
            WeaponCategory = "martial",
        };
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
        return UnityCombatRulesBridge.Create(
            new[] { protagonist, controller },
            CreateTiles(),
            "Players"
        );
    }

    private TestActionController CreateSlowedCombatant(string name, int tier)
    {
        TestActionController controller = CreateCombatant(name, "Players");
        ApplySlowed(controller.gameObject, tier);
        return controller;
    }

    private TestActionController CreateCombatant(string name, string teamName)
    {
        GameObject actor = new(name);
        created.Add(actor);
        CreatureComponent creature = actor.AddComponent<CreatureComponent>();
        creature.InitializeHealthBeforeEncounter(10, 10);
        actor.AddComponent<Conditions>();
        Team team = actor.AddComponent<Team>();
        team.Name = teamName;
        return actor.AddComponent<TestActionController>();
    }

    private static void ApplySlowed(GameObject target, int tier)
    {
        Condition slowed = new Slowed(tier);
        Assert.That(slowed, Is.Not.Null);
        slowed.Apply(new ConditionSource(), target);
    }

    private static void ApplyTimedSlowed(
        UnityCombatRulesBridge bridge,
        ActionController controller,
        int value,
        EffectDuration duration
    )
    {
        CreatureId actor = bridge.GetCreatureId(controller);
        var result = bridge.Dispatch(
            new ApplyConditionOp(
                actor,
                SlowedRules.ConditionId,
                value,
                actor,
                RuleSource.FromSlug("test-slow-spell"),
                duration
            )
        );
        Assert.That(result, Is.TypeOf<ResolvedOpResult<ActiveEffectCreationOutcome>>());
    }

    private static void EndRound(UnityCombatRulesBridge bridge)
    {
        bridge.EndTurn(bridge.GetEncounter().CurrentTurn.Value.Actor);
        bridge.EndTurn(bridge.GetEncounter().CurrentTurn.Value.Actor);
    }

    private sealed class TestActionController : ActionController
    {
        public override void EndTurn() { }
    }
}
