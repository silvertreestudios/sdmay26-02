using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Game.Combat.Spells;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using GridPrivate;
using GridPublic;
using NUnit.Framework;
using UnityEngine;

public class Pf2eClericSpellcastingTests
{
    private readonly List<GameObject> created = new();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in created)
            if (go != null)
                UnityEngine.Object.DestroyImmediate(go);
        created.Clear();
        Pf2eItemCatalog.ResetForTests();
    }

    [Test]
    public void CatalogResolvesClericItemsAndRequestedSpells()
    {
        Pf2eItemCatalog catalog = Pf2eItemCatalog.Instance;

        Assert.That(
            catalog.Resolve("Compendium.pf2e.classes.Item.Cleric")?.Slug,
            Is.EqualTo("cleric")
        );
        Assert.That(catalog.Resolve("Domain Initiate")?.Slug, Is.EqualTo("domain-initiate"));
        Assert.That(catalog.Resolve("Haunting Hymn")?.Slug, Is.EqualTo("haunting-hymn"));
        Assert.That(catalog.Resolve("Infuse Vitality")?.Slug, Is.EqualTo("infuse-vitality"));
        Assert.That(
            catalog.Resolve("Cleric Spellcasting")?.Slug,
            Is.EqualTo("cleric-spellcasting")
        );
    }

    [Test]
    public void LevelOneClericPreparesCloisteredDivineFontAndNoSanctification()
    {
        CreatureComponent cleric = CreatePreparedCleric();
        PreparedCharacter prepared = cleric.Prepared;

        Assert.That(cleric.Build.SubclassName, Is.EqualTo("Cloistered Cleric"));
        Assert.That(cleric.Build.ClassFeatName, Is.EqualTo("Domain Initiate"));
        Assert.That(prepared.HasOwnedItem("cleric"), Is.True);
        Assert.That(prepared.HasOwnedItem("cleric-spellcasting"), Is.True);
        Assert.That(prepared.HasOwnedItem("cloistered-cleric"), Is.True);
        Assert.That(prepared.HasOwnedItem("first-doctrine-cloistered-cleric"), Is.True);
        Assert.That(prepared.HasOwnedItem("divine-font"), Is.True);
        Assert.That(prepared.HasOwnedItem("healing-domain"), Is.False);
        Assert.That(prepared.HasOwnedItem("healers-blessing"), Is.False);
        Assert.That(prepared.Build.RuleSelections["divineFont"], Is.EqualTo("heal"));
        Assert.That(prepared.Build.RuleSelections["sanctification"], Is.EqualTo("none"));
        Assert.That(prepared.RollOptions, Does.Not.Contain("holy"));
        Assert.That(prepared.RollOptions, Does.Not.Contain("unholy"));
    }

    [Test]
    public void ClericSpellStateHasRequestedSpellsSlotsAndWisdomMath()
    {
        CreatureComponent cleric = CreatePreparedCleric();
        Assert.That(cleric.Prepared.SpellBook, Is.TypeOf<PreparedSpellBook>());
        PreparedSpellBook state = (PreparedSpellBook)cleric.Prepared.SpellBook;

        CollectionAssert.AreEquivalent(
            new[]
            {
                "shield",
                "guidance",
                "divine-lance",
                "haunting-hymn",
                "light",
                "bless",
                "infuse-vitality",
                "heal",
            },
            state.CastableSpells.Select(spell => spell.Spell.Value)
        );
        Assert.That(state.Entries.Count(spell => spell.IsCantrip), Is.EqualTo(5));
        AssertRemaining(state, "rank-1-bless", 1);
        AssertRemaining(state, "rank-1-infuse-vitality", 1);
        AssertRemaining(state, "font-heal", 4);
        Assert.That(state.SpellAttackModifier, Is.EqualTo(7));
        Assert.That(state.SpellDc, Is.EqualTo(17));
        string[] legacySpells =
        {
            "shield",
            "guidance",
            "divine-lance",
            "haunting-hymn",
            "bless",
            "infuse-vitality",
            "heal",
        };
        Assert.That(SpellRegistry.TryGet("light", out _), Is.False);
        foreach (string legacySpell in legacySpells)
            Assert.That(
                SpellRegistry.TryGet(legacySpell, out _),
                Is.True,
                legacySpell + " should retain its legacy runtime definition"
            );
    }

    [Test]
    public void LightCastProfileUsesCurrentDataBackedTraits()
    {
        UnitySpellDefinitionCatalog catalog = UnitySpellDefinitionCatalog.Load();
        CastSpellActionOp operation = new(
            new CreatureId("profile-actor"),
            Reference("light"),
            new SpellActionVariant(2),
            SpellCastAuthorization.Cantrip
        );

        ActionProfile profile = operation.GetBaseProfile(catalog);

        Assert.That(profile.Cost, Is.EqualTo(ActionCost.Two));
        Assert.That(
            profile.Traits.Select(trait => trait.Slug),
            Is.EqualTo(new[] { "cantrip", "concentrate", "light", "manipulate" })
        );
    }

    [Test]
    public void ShieldAddsCircumstanceAcAndExpiresOnCasterNextTurn()
    {
        CreatureComponent cleric = CreatePreparedCleric();
        TestActionController controller = cleric.gameObject.AddComponent<TestActionController>();
        controller.ActionPoints = 3;
        controller.IsTakingAction = true;

        Cast("shield", cleric, 1);

        Assert.That(cleric.ResolveArmorClass().Total, Is.EqualTo(12));
        Assert.That(controller.ActionPoints, Is.EqualTo(2));

        controller.StartTurn();

        Assert.That(cleric.ResolveArmorClass().Total, Is.EqualTo(11));
    }

    [Test]
    public void GuidanceAppliesOnceThenRecordsEncounterImmunity()
    {
        CreatureComponent cleric = CreatePreparedCleric();
        CreatureComponent ally = CreateCreature("Ally");

        CastSpellResult result = Cast("guidance", cleric, 1, ally.gameObject);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(ally.ResolveAttackRoll().Total, Is.EqualTo(1));
        Assert.That(ally.ResolveAttackRoll().Total, Is.EqualTo(0));
        Assert.That(
            ally.GetComponent<SpellEffectController>().HasEffect<GuidanceImmunitySpellEffect>(),
            Is.True
        );

        CastSpellResult second = SpellcastingRuntime.Cast(
            cleric.gameObject,
            Reference("guidance"),
            1,
            new[] { ally.gameObject },
            spendActions: false
        );
        Assert.That(second.Success, Is.False);
    }

    [Test]
    public void BlessGrantsStatusAttackBonusAndConsumesPreparedSlot()
    {
        CreatureComponent cleric = CreatePreparedCleric();
        CreatureComponent ally = CreateCreature("Ally");
        ally.transform.position = new Vector3(2, 0, 0);

        CastSpellResult result = Cast("bless", cleric, 2, ally.gameObject);

        Assert.That(result.Success, Is.True);
        Assert.That(ally.ResolveAttackRoll().Total, Is.EqualTo(1));
        AssertRemaining((PreparedSpellBook)cleric.Prepared.SpellBook, "rank-1-bless", 0);
    }

    [Test]
    public void InfuseVitalityAddsVitalityDamageToWeaponAndUnarmedStrikes()
    {
        CreatureComponent cleric = CreatePreparedCleric();
        CreatureComponent ally = CreateCreature("Ally");

        CastSpellResult result = Cast("infuse-vitality", cleric, 1, ally.gameObject);

        Assert.That(result.Success, Is.True);
        CreatureComponent target = CreateCreature("Target");
        StrikeResolutionContext context = StrikeResolutionContext.FromRequest(
            new StrikeResolutionRequest
            {
                Attacker = ally.gameObject,
                Target = target.gameObject,
                Profile = new StrikeProfile(
                    new List<Dice> { new Dice(1, 6, "slashing") },
                    new List<DamageValue>()
                )
                {
                    WeaponCategory = "martial",
                },
                TargetingResult = new StrikeTargetResult
                {
                    Target = target.gameObject,
                    LineOfEffect = StrikeLineOfEffect.Clear,
                    Cover = StrikeCover.None,
                },
            }
        );
        foreach (
            IStrikeAdjustment adjustment in ally.GetComponent<SpellEffectController>()
                .GetStrikeAdjustments(context)
        )
            adjustment.Apply(context);

        Assert.That(
            context.DamageDice.Any(die =>
                die.damageType == "vitality" && die.numberOfDice == 1 && die.sidesPerDie == 4
            ),
            Is.True
        );
        AssertRemaining((PreparedSpellBook)cleric.Prepared.SpellBook, "rank-1-infuse-vitality", 0);
    }

    [Test]
    public void HealUsesFontPoolAndCanHealLivingTargets()
    {
        CreatureComponent cleric = CreatePreparedCleric();
        CreatureComponent ally = CreateCreature("Ally", 3, 20);
        UnityEngine.Random.InitState(12);

        CastSpellResult result = Cast("heal", cleric, 2, ally.gameObject);

        Assert.That(result.Success, Is.True);
        Assert.That(ally.hp, Is.GreaterThan(3));
        Assert.That(ally.Health.Current, Is.EqualTo(ally.hp));
        AssertRemaining((PreparedSpellBook)cleric.Prepared.SpellBook, "font-heal", 3);
    }

    [Test]
    public void DivineLanceUsesSpellAttackAndMultipleAttackPenalty()
    {
        CreatureComponent cleric = CreatePreparedCleric();
        TestActionController controller = cleric.gameObject.AddComponent<TestActionController>();
        controller.IsTakingAction = true;
        CreatureComponent target = CreateCreature("Target", 100, 100);
        TestActionController targetController =
            target.gameObject.AddComponent<TestActionController>();
        target.transform.position = new Vector3(6, 0, 0);
        target.ac = 12;
        Tile[,] tiles = new Tile[7, 1];
        for (int x = 0; x < tiles.GetLength(0); x++)
            tiles[x, 0] = new Tile();
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { controller, targetController },
            tiles
        );
        bridge.BeginTurn(bridge.GetCreatureId(cleric), 3);
        UnityEngine.Random.InitState(3);
        InstallTestCombatLog();

        CastSpellResult result = Cast("divine-lance", cleric, 2, target.gameObject);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(controller.ActionPoints, Is.EqualTo(1));
        Assert.That(controller.StrikePenalty, Is.EqualTo(1));
    }

    [Test]
    public void DivineLanceClearsTakingActionWhenMapAdvancementThrows()
    {
        CreatureComponent cleric = CreatePreparedCleric();
        TestActionController controller = cleric.gameObject.AddComponent<TestActionController>();
        controller.IsTakingAction = true;
        CreatureComponent target = CreateCreature("Target", 100, 100);
        TestActionController targetController =
            target.gameObject.AddComponent<TestActionController>();
        target.transform.position = new Vector3(1, 0, 0);
        Tile[,] tiles = new Tile[2, 1];
        for (int x = 0; x < tiles.GetLength(0); x++)
            tiles[x, 0] = new Tile();
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { controller, targetController },
            tiles
        );
        bridge.BeginTurn(bridge.GetCreatureId(cleric), 3);
        InvalidOperationException expected = new("Injected MAP advancement failure.");
        GetDispatcher(bridge).RegisterFactObserver(new ThrowingMapObserver(expected));
        InstallTestCombatLog();
        UnityEngine.Random.State randomState = UnityEngine.Random.state;

        try
        {
            UnityEngine.Random.InitState(3);
            InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() =>
                Cast("divine-lance", cleric, 2, target.gameObject)
            );

            Assert.That(actual, Is.SameAs(expected));
            Assert.That(controller.IsTakingAction, Is.False);
        }
        finally
        {
            UnityEngine.Random.state = randomState;
        }
    }

    private void InstallTestCombatLog()
    {
        GameObject logObject = new("Test Combat Log");
        created.Add(logObject);
        TestCombatLog log = logObject.AddComponent<TestCombatLog>();
        FieldInfo field = typeof(SingletonMonoBehaviour<CombatLogInterface>).GetField(
            "Instance",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        field.SetValue(null, log);
    }

    private CastSpellResult Cast(
        string slug,
        CreatureComponent caster,
        uint actionCost,
        params GameObject[] targets
    )
    {
        return SpellcastingRuntime.Cast(
            caster.gameObject,
            Reference(slug),
            actionCost,
            targets,
            spendActions: true
        );
    }

    private static SpellReference Reference(string slug) => new(new SpellId(slug), 1);

    private static void AssertRemaining(PreparedSpellBook book, string pool, int expected)
    {
        Assert.That(book.TryGetRemaining(new SpellSlotPoolId(pool), out int uses), Is.True);
        Assert.That(uses, Is.EqualTo(expected));
    }

    private CreatureComponent CreatePreparedCleric()
    {
        CreatureComponent creature = CreateCreature("Prepared Cleric");
        creature.level = 1;
        creature.wisMod = 4;
        creature.ac = 11;
        creature.Build = new CharacterBuild { ClassName = "Cleric" };
        creature.Prepared = Pf2eCharacterPreparer.Prepare(creature, creature.Build);
        return creature;
    }

    private CreatureComponent CreateCreature(
        string name,
        int currentHitPoints = 10,
        int maximumHitPoints = 10
    )
    {
        GameObject go = new(name);
        created.Add(go);
        CreatureComponent creature = go.AddComponent<CreatureComponent>();
        go.AddComponent<Conditions>();
        creature.InitializeHealthBeforeEncounter(currentHitPoints, maximumHitPoints);
        Game.Rules.Unity.UnityCombatRulesBridge.CreateHealthTestComposition(new[] { creature });
        return creature;
    }

    private static RuleDispatcher GetDispatcher(UnityCombatRulesBridge bridge)
    {
        FieldInfo field = typeof(UnityCombatRulesBridge).GetField(
            "dispatcher",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null);
        return (RuleDispatcher)field.GetValue(bridge);
    }

    private sealed class ThrowingMapObserver : IFactObserver<MultipleAttackPenaltyAdvancedFact>
    {
        private readonly Exception failure;

        public ThrowingMapObserver(Exception failure) => this.failure = failure;

        public ValueTask OnFactCommitted(
            MultipleAttackPenaltyAdvancedFact fact,
            RulesSnapshot currentSnapshot
        ) => throw failure;
    }

    private sealed class TestCombatLog : CombatLogInterface
    {
        public readonly List<string> Messages = new();

        public override void DevMode() { }

        public override void ReleaseMode() { }

        public override void AddWhiteList(string tag) { }

        public override void AddBlackList(string tag) { }

        public override void DevLog(string msg) => Messages.Add(msg);

        public override void DevLog(string msg, string tag) => Messages.Add(msg);

        public override void DevLog(string msg, List<string> tags) => Messages.Add(msg);

        public override void Log(string msg) => Messages.Add(msg);

        public override void Log(string msg, string tag) => Messages.Add(msg);

        public override void Log(string msg, List<string> tags) => Messages.Add(msg);

        public override List<string> GetMessages() => new(Messages);
    }

    private sealed class TestActionController : ActionController
    {
        public override void EndTurn() { }
    }
}
