using Game.AbilityActions;
using Game.Creature;
using Game.Creature.Rules;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Pf2eRulesTests
{
    private readonly List<GameObject> created = new();

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

        Assert.That(catalog.Resolve("Compendium.pf2e.classes.Item.Barbarian")?.Slug, Is.EqualTo("barbarian"));
        Assert.That(catalog.Resolve("Compendium.pf2e.actionspf2e.Item.Rage")?.Slug, Is.EqualTo("rage"));
        Assert.That(catalog.Resolve("Compendium.pf2e.feat-effects.Item.Effect: Rage")?.Slug, Is.EqualTo("effect-rage"));
        Assert.That(catalog.Resolve("Raging Intimidation")?.Slug, Is.EqualTo("raging-intimidation"));
        Assert.That(catalog.Resolve("Compendium.pf2e.classes.Item.Rogue")?.Slug, Is.EqualTo("rogue"));
        Assert.That(catalog.Resolve("Compendium.pf2e.classfeatures.Item.Sneak Attack")?.Slug, Is.EqualTo("sneak-attack"));
        Assert.That(catalog.Resolve("Compendium.pf2e.classfeatures.Item.Thief")?.Slug, Is.EqualTo("thief"));
        Assert.That(catalog.Resolve("Nimble Dodge")?.Slug, Is.EqualTo("nimble-dodge"));
    }

    [Test]
    public void TorgrimPreparesBarbarianFeaturesFromBuildData()
    {
        GameObject torgrim = CreatureJsonConverter.CreateFromFile("DataFiles/playerCharacters/Torgrim");
        created.Add(torgrim);
        CreatureComponent creature = torgrim.GetComponent<CreatureComponent>();

        Assert.That(creature.Build.ClassName, Is.EqualTo("Barbarian"));
        Assert.That(creature.Build.SubclassName, Is.EqualTo("Fury Instinct"));
        Assert.That(creature.Prepared.HasOwnedItem("barbarian"), Is.True);
        Assert.That(creature.Prepared.HasOwnedItem("quick-tempered"), Is.True);
        Assert.That(creature.Prepared.HasOwnedItem("rage"), Is.True);
        Assert.That(creature.Prepared.HasOwnedItem("fury-instinct"), Is.True);
        Assert.That(creature.Prepared.HasOwnedItem("raging-intimidation"), Is.True);
        Assert.That(creature.Prepared.Build.RuleSelections["furyInstinct"], Does.Contain("Raging Intimidation"));
    }

    [Test]
    public void PredicateSupportsAtomicCompoundAndNumericChecks()
    {
        PreparedCharacter prepared = new(new CharacterBuild());
        prepared.RollOptions.Add("class:barbarian");
        prepared.RollOptions.Add("self:effect:rage");
        prepared.RollOptions.Add("self:level:7");
        prepared.SkillRanks["intimidation"] = 4;

        Assert.That(Pf2ePredicate.Evaluate(JToken.Parse("[\"class:barbarian\", {\"not\": \"item:ranged\"}]"), prepared), Is.True);
        Assert.That(Pf2ePredicate.Evaluate(JToken.Parse("[{\"or\": [\"item:ranged\", \"self:effect:rage\"]}]"), prepared), Is.True);
        Assert.That(Pf2ePredicate.Evaluate(JToken.Parse("[{\"gte\": [\"self:level\", 7]}, {\"gte\": [\"skill:intimidation:rank\", 4]}]"), prepared), Is.True);
        Assert.That(Pf2ePredicate.Evaluate(JToken.Parse("[{\"and\": [\"class:barbarian\", {\"not\": \"item:ranged\"}]}]"), prepared), Is.True);
    }

    [Test]
    public void RageRuleAppliesActiveEffectAndEmitsTempHpEffects()
    {
        CreatureComponent creature = CreatePreparedBarbarian();
        CreatureRulesState state = UnityCreatureRulesAdapter.From(creature.gameObject);

        RageRuleResult result = RageRule.Apply(new RageRequest { Creature = state, ActionCost = 0 });

        Assert.That(result.Applied, Is.True);
        Assert.That(creature.Prepared.HasActiveEffect("effect-rage"), Is.True);
        Assert.That(creature.Prepared.RollOptions.Contains("self:effect:effect-rage"), Is.True);
        Assert.That(result.Effects.Any(e => e.Type == RuleEffectType.GainSourceTempHp && e.Source == "rage" && e.Amount == 2), Is.True);
    }

    [Test]
    public void RageRuleBlocksInvalidRageStates()
    {
        CreatureComponent creature = CreatePreparedBarbarian();

        Assert.That(RageRule.CanApply(new RageRequest
        {
            Creature = new CreatureRulesState
            {
                Prepared = creature.Prepared,
                Conditions = new[] { "Fatigued" }
            }
        }), Is.False);

        Assert.That(RageRule.CanApply(new RageRequest
        {
            Creature = new CreatureRulesState
            {
                Prepared = creature.Prepared,
                Conditions = new[] { "Encumbered" }
            }
        }), Is.False);

        Assert.That(RageRule.CanApply(new RageRequest
        {
            Creature = new CreatureRulesState
            {
                Prepared = creature.Prepared,
                ArmorCategory = "heavy"
            }
        }), Is.False);
    }

    [Test]
    public void RageRuleEndRemovesActiveEffectAndEmitsCleanupEffects()
    {
        CreatureComponent creature = CreatePreparedBarbarian();
        RageRule.Apply(new RageRequest { Creature = UnityCreatureRulesAdapter.From(creature.gameObject), ActionCost = 0 });

        RageRuleResult end = RageRule.End(UnityCreatureRulesAdapter.From(creature.gameObject));

        Assert.That(end.Applied, Is.True);
        Assert.That(creature.Prepared.HasActiveEffect("effect-rage"), Is.False);
        Assert.That(creature.Prepared.HasActiveEffect("rage-temp-hp-immunity"), Is.True);
        Assert.That(end.Effects.Any(e => e.Type == RuleEffectType.RemoveSourceTempHp && e.Source == "rage"), Is.True);
        Assert.That(end.Effects.Any(e => e.Type == RuleEffectType.AddTempHpImmunity && e.Source == "rage"), Is.True);
    }

    [Test]
    public void RageUnityActionAppliesRuleEffectsToUnityComponents()
    {
        CreatureComponent creature = CreatePreparedBarbarian();
        TestActionController actionController = creature.gameObject.AddComponent<TestActionController>();
        actionController.ActionPoints = 3;
        actionController.IsTakingAction = true;

        Rage rage = new(1);
        Assert.That(rage.UseRage(creature.gameObject), Is.True);

        Assert.That(creature.tempHp, Is.EqualTo(2));
        Assert.That(actionController.ActionPoints, Is.EqualTo(2));
        Assert.That(actionController.IsTakingAction, Is.False);

        rage.EndRage(creature.gameObject);
        Assert.That(creature.tempHp, Is.EqualTo(0));
        Assert.That(creature.HasTempHpImmunity("rage"), Is.True);

        Assert.That(rage.UseRage(creature.gameObject), Is.True);
        Assert.That(creature.tempHp, Is.EqualTo(0));
    }

    [Test]
    public void RageDamageUsesRuleModifiersAndFuryInstinctAdjustments()
    {
        CreatureComponent creature = CreatePreparedBarbarian();
        Assert.That(new Rage(0).UseRage(creature.gameObject), Is.True);

        Strike greataxe = new(new List<Dice> { new Dice(1, 12, "Slashing") }, new List<DamageValue> { new DamageValue("Slashing", 4) });
        Pf2eRulesEngine.ApplyStrikeDamageModifiers(creature, greataxe);
        Assert.That(greataxe.FlatDamages.Last().DamageAmount, Is.EqualTo(3));

        Strike agile = new(new List<Dice> { new Dice(1, 4, "Bludgeoning") }, new List<DamageValue> { new DamageValue("Bludgeoning", 4) });
        agile.Traits.Add("agile");
        Pf2eRulesEngine.ApplyStrikeDamageModifiers(creature, agile);
        Assert.That(agile.FlatDamages.Last().DamageAmount, Is.EqualTo(1));

        new Rage(0).EndRage(creature.gameObject);
        Strike notRaging = new(new List<Dice> { new Dice(1, 12, "Slashing") }, new List<DamageValue> { new DamageValue("Slashing", 4) });
        Pf2eRulesEngine.ApplyStrikeDamageModifiers(creature, notRaging);
        Assert.That(notRaging.FlatDamages.Count, Is.EqualTo(1));
    }

    [Test]
    public void RagingIntimidationItemAlterationAddsRageTraitOnlyWhileRaging()
    {
        CreatureComponent creature = CreatePreparedBarbarian();

        List<string> beforeRage = Pf2eRulesEngine.GetAlteredTraits(creature.Prepared, "action", "demoralize", new List<string>());
        Assert.That(beforeRage, Does.Not.Contain("rage"));

        Assert.That(new Rage(0).UseRage(creature.gameObject), Is.True);
        List<string> duringRage = Pf2eRulesEngine.GetAlteredTraits(creature.Prepared, "action", "demoralize", new List<string>());
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
        Assert.That(creature.weaponBonuses.First(b => b.category == "martial").bonus, Is.EqualTo(2));
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

        Strike finesseStrike = new(new List<Dice> { new Dice(1, 6, "slashing") }, new List<DamageValue> { new DamageValue("slashing", creature.strMod) })
        {
            Traits = new List<string> { "agile", "finesse" },
            ItemSlug = "dogslicer",
            WeaponCategory = "martial"
        };
        Pf2eRulesEngine.ApplyStrikeDamageModifiers(creature, finesseStrike);
        Assert.That(finesseStrike.FlatDamages[0].DamageAmount, Is.EqualTo(creature.dexMod));

        Strike nonFinesseStrike = new(new List<Dice> { new Dice(1, 6, "slashing") }, new List<DamageValue> { new DamageValue("slashing", creature.strMod) })
        {
            Traits = new List<string> { "forceful" },
            ItemSlug = "scimitar",
            WeaponCategory = "martial"
        };
        Pf2eRulesEngine.ApplyStrikeDamageModifiers(creature, nonFinesseStrike);
        Assert.That(nonFinesseStrike.FlatDamages[0].DamageAmount, Is.EqualTo(creature.strMod));
    }

    [Test]
    public void SneakAttackAddsPrecisionDamageOnlyAgainstOffGuardTargets()
    {
        CreatureComponent rogue = CreatePreparedRogue();
        CreatureComponent target = CreateTarget("Target");

        Strike normalTarget = CreateDogslicerStrike(rogue);
        Pf2eRulesEngine.ApplyStrikeDamageModifiers(rogue, normalTarget, target);
        Assert.That(normalTarget.Damages.Count, Is.EqualTo(1));

        target.GetComponent<Conditions>().Add("Off-Guard", new ConditionSource());
        Strike offGuardTarget = CreateDogslicerStrike(rogue);
        Pf2eRulesEngine.ApplyStrikeDamageModifiers(rogue, offGuardTarget, target);
        Assert.That(offGuardTarget.Damages.Count, Is.EqualTo(2));
        Assert.That(offGuardTarget.Damages.Last().numberOfDice, Is.EqualTo(1));
        Assert.That(offGuardTarget.Damages.Last().sidesPerDie, Is.EqualTo(6));
        Assert.That(offGuardTarget.Damages.Last().damageType, Is.EqualTo("precision"));

        Strike ineligibleWeapon = new(new List<Dice> { new Dice(1, 6, "slashing") }, new List<DamageValue> { new DamageValue("slashing", rogue.strMod) })
        {
            Traits = new List<string> { "forceful" },
            ItemSlug = "scimitar",
            WeaponCategory = "martial"
        };
        Pf2eRulesEngine.ApplyStrikeDamageModifiers(rogue, ineligibleWeapon, target);
        Assert.That(ineligibleWeapon.Damages.Count, Is.EqualTo(1));
    }

    [Test]
    public void SneakAttackSupportsRangedWeaponsAndFlatFootedAlias()
    {
        CreatureComponent rogue = CreatePreparedRogue();
        CreatureComponent target = CreateTarget("Flat-Footed Target");
        target.GetComponent<Conditions>().Add("Flat-Footed", new ConditionSource());

        Strike shortbowStrike = new(new List<Dice> { new Dice(1, 6, "piercing") }, new List<DamageValue>())
        {
            Traits = new List<string> { "deadly-d10" },
            ItemSlug = "shortbow",
            WeaponCategory = "martial",
            IsRangedAttack = true
        };

        Pf2eRulesEngine.ApplyStrikeDamageModifiers(rogue, shortbowStrike, target);

        Assert.That(shortbowStrike.Damages.Count, Is.EqualTo(2));
        Assert.That(shortbowStrike.Damages.Last().numberOfDice, Is.EqualTo(1));
        Assert.That(shortbowStrike.Damages.Last().sidesPerDie, Is.EqualTo(6));
        Assert.That(shortbowStrike.Damages.Last().damageType, Is.EqualTo("precision"));
    }

    private CreatureComponent CreatePreparedBarbarian()
    {
        GameObject go = new("Prepared Barbarian");
        created.Add(go);
        CreatureComponent creature = go.AddComponent<CreatureComponent>();
        go.AddComponent<Conditions>();
        creature.level = 1;
        creature.conMod = 1;
        creature.Build = new CharacterBuild
        {
            ClassName = "Barbarian",
            SubclassName = "Fury Instinct",
            ClassFeatName = "Raging Intimidation"
        };
        creature.Prepared = Pf2eCharacterPreparer.Prepare(creature, creature.Build);
        return creature;
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
            ClassFeatName = "Nimble Dodge"
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
        creature.hp = 10;
        creature.maxHp = 10;
        return creature;
    }

    private static Strike CreateDogslicerStrike(CreatureComponent rogue)
    {
        return new Strike(new List<Dice> { new Dice(1, 6, "slashing") }, new List<DamageValue> { new DamageValue("slashing", rogue.strMod) })
        {
            Traits = new List<string> { "agile", "finesse" },
            ItemSlug = "dogslicer",
            WeaponCategory = "martial"
        };
    }
    private sealed class TestActionController : ActionController
    {
        public override void EndTurn()
        {
        }
    }
}
