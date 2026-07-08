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

    private sealed class TestActionController : ActionController
    {
        public override void EndTurn()
        {
        }
    }
}
