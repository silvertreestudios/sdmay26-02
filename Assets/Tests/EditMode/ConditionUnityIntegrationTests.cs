using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game.Combat.Encounters;
using Game.Combat.Spells;
using Game.Creature;
using Game.Creature.Rules;
using Game.DungeonPersistence.Actors;
using Game.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Composition;
using Game.Rules.Unity.Spells;
using GridPrivate;
using GridPublic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class ConditionUnityIntegrationTests
{
    private readonly List<GameObject> created = new List<GameObject>();
    private UnityEngine.Random.State randomState;

    [SetUp]
    public void SetUp() => randomState = UnityEngine.Random.state;

    [TearDown]
    public void TearDown()
    {
        UnityEngine.Random.state = randomState;
        foreach (GameObject gameObject in created)
        {
            if (gameObject != null)
                Object.DestroyImmediate(gameObject);
        }
        created.Clear();
    }

    [Test]
    public void HauntingHymnCombatCastUsesRulesDamageCostsAndOneMinuteDeafened()
    {
        CreatureFixture caster = CreateCreature("Caster", "Heroes", 100);
        CreatureFixture target = CreateCreature("Target", "Enemies", 0);
        PreparedSpell spell = PrepareHauntingHymn(caster);
        AreaTargetResult area = PrepareHauntingArea(caster, target, out Tile[,] tiles);
        ScriptedRollService spellRolls = new(1, 4);
        EncounterThenSpellRollService rolls = new(spellRolls);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { caster.Controller, target.Controller },
            tiles,
            rolls
        );
        CreatureId casterId = bridge.GetCreatureId(caster.Creature);
        CreatureId targetId = bridge.GetCreatureId(target.Creature);
        Assert.That(
            caster
                .Controller.GetActions()
                .OfType<RulesCastSpellAction>()
                .Any(action => action.Spell == new SpellReference(new SpellId("haunting-hymn"), 1)),
            Is.True
        );
        bridge.BeginTurn(casterId, 3);
        AssertEncounterSetupRolls(rolls);
        rolls.BeginSpellResolution();

        CastSpellResult result = SpellcastingRuntime.Cast(caster.GameObject, spell, 2, area: area);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Targets, Is.EqualTo(new[] { target.GameObject }));
        Assert.That(
            result.Rolls.Single().degree,
            Is.EqualTo(Game.Creature.DegreeOfSuccess.CriticalFail)
        );
        Assert.That(result.Amount, Is.EqualTo(8));
        Assert.That(bridge.Snapshot.Health[targetId].Current, Is.EqualTo(12));
        Assert.That(bridge.Snapshot.ActionEconomy[casterId].ActionsRemaining, Is.EqualTo(1));
        ConditionSelection<IEffectState> deafened = ConditionSelectors
            .GetActiveInstances(bridge.Snapshot, targetId, ConditionRuleDefinitions.Deafened)
            .Single();
        Assert.That(
            ConditionSelectors.HasMarker(
                bridge.Snapshot,
                targetId,
                ConditionRuleDefinitions.Deafened
            ),
            Is.True
        );
        Assert.That(deafened.Effect.Duration, Is.EqualTo(EffectDuration.OneMinute));
        Assert.That(deafened.Effect.Duration, Is.Not.EqualTo(EffectDuration.Rounds(1)));
        Assert.That(
            bridge.Snapshot.ActiveEffectTimings[deafened.Effect.Id].RemainingBoundaries,
            Is.EqualTo(10)
        );
        Assert.That(target.Conditions.ActiveConditionNames, Does.Contain("deafened"));
        AssertSingleHauntingHymnResolution(result, rolls, spellRolls);
    }

    [Test]
    public void DivineLanceCombatCastProjectsAuthoritativeAttackDamageAndRoll()
    {
        CreatureFixture caster = CreateCreature("Divine Lance Caster", "Heroes", 100);
        CreatureFixture target = CreateCreature("Divine Lance Target", "Enemies", 0);
        target.Creature.ac = 18;
        caster.GameObject.transform.position = Vector3Int.zero;
        target.GameObject.transform.position = Vector3Int.right;
        PreparedSpell spell = PrepareDivineLance(caster);
        Tile[,] tiles = new[,]
        {
            { new Tile() },
            { new Tile() },
        };
        ScriptedRollService spellRolls = new(12, 2, 3);
        EncounterThenSpellRollService rolls = new(spellRolls);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { caster.Controller, target.Controller },
            tiles,
            rolls
        );
        CreatureId casterId = bridge.GetCreatureId(caster.Creature);
        CreatureId targetId = bridge.GetCreatureId(target.Creature);
        bridge.BeginTurn(casterId, 3);
        AssertEncounterSetupRolls(rolls);
        rolls.BeginSpellResolution();
        CapturingCastObserver observer = new();
        using IDisposable registration = GetDispatcher(bridge)
            .RegisterResolvedOpObserver<CastSpellActionOp, CastSpellOutcome>(observer);
        int healthBefore = bridge.Snapshot.Health[targetId].Current;

        CastSpellResult result = SpellcastingRuntime.Cast(
            caster.GameObject,
            spell,
            2,
            new[] { target.GameObject }
        );

        CastSpellOutcome outcome = observer.Outcomes.Single();
        SpellAttackResolution attack = outcome.Attacks.Single();
        D20Result roll = result.Rolls.Single();
        int healthAfter = bridge.Snapshot.Health[targetId].Current;
        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Targets, Is.EqualTo(new[] { target.GameObject }));
        Assert.That(outcome.CreatedEffects, Is.Empty);
        Assert.That(outcome.Saves, Is.Empty);
        Assert.That(attack.Hit, Is.True);
        Assert.That(attack.Degree, Is.EqualTo(Game.Rules.Runtime.DegreeOfSuccess.Success));
        Assert.That(result.Amount, Is.EqualTo(attack.FinalDamage));
        Assert.That(result.Amount, Is.EqualTo(healthBefore - healthAfter));
        Assert.That(healthAfter, Is.EqualTo(15));
        Assert.That(roll.roll, Is.EqualTo(attack.AttackRoll.Values.Single()));
        Assert.That(roll.total, Is.EqualTo(attack.AttackRoll.Total + attack.AttackModifier));
        Assert.That(roll.degree, Is.EqualTo(Game.Creature.DegreeOfSuccess.Success));
        Assert.That(bridge.Snapshot.ActionEconomy[casterId].ActionsRemaining, Is.EqualTo(1));
        Assert.That(bridge.Snapshot.MultipleAttackPenalty[casterId].AttackCount, Is.EqualTo(1));
        Assert.That(
            rolls.SpellRequests,
            Is.EqualTo(new[] { DiceExpressions.D20, new DiceExpression(2, 4) })
        );
        Assert.That(spellRolls.Remaining, Is.Zero);
        bridge.ReleaseOwnership();
    }

    [Test]
    public void EncounterAdvancementOfTimedConditionEmitsGenericExpirationFact()
    {
        CreatureFixture source = CreateCreature("Timed Source", "Heroes", 100);
        CreatureFixture target = CreateCreature("Timed Target", "Enemies", 0);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new[] { source.Controller, target.Controller },
            CreateTiles()
        );
        CreatureId sourceId = bridge.GetCreatureId(source.Creature);
        CreatureId targetId = bridge.GetCreatureId(target.Creature);
        bridge.BeginTurn(sourceId, 3);
        ResolvedOpResult<ConditionApplicationOutcome> applied =
            (ResolvedOpResult<ConditionApplicationOutcome>)
                bridge.Dispatch(
                    new ApplyConditionOp(
                        "fatigued",
                        targetId,
                        sourceId,
                        RuleSource.FromSlug("timed-condition-test"),
                        EffectDuration.Rounds(1),
                        ConditionMarkerState.Instance
                    )
                );
        CountingFactObserver<ActiveEffectExpiredFact> expired = new();
        using IDisposable registration = GetDispatcher(bridge)
            .RegisterFactObserver<ActiveEffectExpiredFact>(expired);

        bridge.BeginTurn(sourceId, 3);

        Assert.That(expired.Count, Is.EqualTo(1));
        Assert.That(expired.Last.EffectId, Is.EqualTo(applied.Value.EffectId));
        Assert.That(expired.Last.DefinitionId, Is.EqualTo(ConditionRuleDefinitions.Fatigued));
        bridge.ReleaseOwnership();
    }

    [Test]
    public void HauntingHymnCriticalFailureAgainstDeafenedImmunityStillResolvesDamageAndCosts()
    {
        CreatureFixture caster = CreateCreature("Immune Hymn Caster", "Heroes", 100);
        CreatureFixture target = CreateCreature("Immune Hymn Target", "Enemies", 0);
        target.Creature.immunities.Add("deafened");
        PreparedSpell spell = PrepareHauntingHymn(caster);
        AreaTargetResult area = PrepareHauntingArea(caster, target, out Tile[,] tiles);
        ScriptedRollService spellRolls = new(1, 4);
        EncounterThenSpellRollService rolls = new(spellRolls);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { caster.Controller, target.Controller },
            tiles,
            rolls
        );
        CreatureId casterId = bridge.GetCreatureId(caster.Creature);
        CreatureId targetId = bridge.GetCreatureId(target.Creature);
        bridge.BeginTurn(casterId, 3);
        AssertEncounterSetupRolls(rolls);
        rolls.BeginSpellResolution();

        CastSpellResult result = SpellcastingRuntime.Cast(caster.GameObject, spell, 2, area: area);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Amount, Is.EqualTo(8));
        Assert.That(bridge.Snapshot.Health[targetId].Current, Is.EqualTo(12));
        Assert.That(bridge.Snapshot.ActionEconomy[casterId].ActionsRemaining, Is.EqualTo(1));
        Assert.That(
            ConditionSelectors.GetActiveInstances(
                bridge.Snapshot,
                targetId,
                ConditionRuleDefinitions.Deafened
            ),
            Is.Empty
        );
        Assert.That(target.Conditions.ActiveConditionNames, Does.Not.Contain("deafened"));
        AssertSingleHauntingHymnResolution(result, rolls, spellRolls);
    }

    [Test]
    public void EncounterCastAttemptReplaysCallerOwnedInvocationAfterObserverFailure()
    {
        CreatureFixture caster = CreateCreature("Retry Hymn Caster", "Heroes", 100);
        CreatureFixture target = CreateCreature("Retry Hymn Target", "Enemies", 0);
        PreparedSpell spell = PrepareHauntingHymn(caster);
        AreaTargetResult area = PrepareHauntingArea(caster, target, out Tile[,] tiles);
        ScriptedRollService spellRolls = new(1, 4);
        EncounterThenSpellRollService rolls = new(spellRolls);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { caster.Controller, target.Controller },
            tiles,
            rolls
        );
        CreatureId casterId = bridge.GetCreatureId(caster.Creature);
        CreatureId targetId = bridge.GetCreatureId(target.Creature);
        bridge.BeginTurn(casterId, 3);
        AssertEncounterSetupRolls(rolls);
        rolls.BeginSpellResolution();
        InvalidOperationException expected = new("injected cast presentation failure");
        ThrowOnceFactObserver<ActiveEffectCreatedFact> observer = new(expected);
        using IDisposable registration = GetDispatcher(bridge)
            .RegisterFactObserver<ActiveEffectCreatedFact>(observer);
        ActionInvocationId invocation = new("unity-hymn-exact-retry");

        InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() =>
            SpellcastingRuntime.CastEncounterAttempt(
                invocation,
                caster.GameObject,
                spell,
                2,
                area: area
            )
        );

        Assert.That(actual, Is.SameAs(expected));
        Assert.That(bridge.Snapshot.Health[targetId].Current, Is.EqualTo(12));
        Assert.That(bridge.Snapshot.ActionEconomy[casterId].ActionsRemaining, Is.EqualTo(1));
        Assert.That(bridge.Snapshot.MultipleAttackPenalty[casterId].AttackCount, Is.Zero);
        long committedVersion = bridge.Snapshot.Version;

        CastSpellResult retry = SpellcastingRuntime.CastEncounterAttempt(
            invocation,
            caster.GameObject,
            spell,
            2,
            area: area
        );

        Assert.That(retry.Success, Is.True, retry.Message);
        Assert.That(retry.Amount, Is.EqualTo(8));
        Assert.That(bridge.Snapshot.Version, Is.EqualTo(committedVersion));
        Assert.That(bridge.Snapshot.Health[targetId].Current, Is.EqualTo(12));
        Assert.That(bridge.Snapshot.ActionEconomy[casterId].ActionsRemaining, Is.EqualTo(1));
        Assert.That(observer.Count, Is.EqualTo(1));
        AssertSingleHauntingHymnResolution(retry, rolls, spellRolls);
        bridge.ReleaseOwnership();
    }

    [Test]
    public void EncounterCastAttemptRejectsDuplicateCreatureSelectionBeforeDispatch()
    {
        CreatureFixture caster = CreateCreature("Duplicate Target Caster", "Heroes", 100);
        CreatureFixture target = CreateCreature("Duplicate Target", "Enemies", 0);
        PreparedSpell spell = PrepareHauntingHymn(caster);
        PrepareHauntingArea(caster, target, out Tile[,] tiles);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { caster.Controller, target.Controller },
            tiles
        );
        CreatureId casterId = bridge.GetCreatureId(caster.Creature);
        CreatureId targetId = bridge.GetCreatureId(target.Creature);
        bridge.BeginTurn(casterId, 3);
        RulesSnapshot before = bridge.Snapshot;
        ActionInvocationId invocation = new("unity-duplicate-selection");

        CastSpellResult result = SpellcastingRuntime.CastEncounterAttempt(
            invocation,
            caster.GameObject,
            spell,
            2,
            new[] { target.GameObject, target.GameObject }
        );

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("same creature more than once"));
        Assert.That(bridge.Snapshot.Version, Is.EqualTo(before.Version));
        Assert.That(bridge.Snapshot.ActionEconomy[casterId].ActionsRemaining, Is.EqualTo(3));
        Assert.That(bridge.Snapshot.Health[targetId].Current, Is.EqualTo(20));
        bridge.ReleaseOwnership();
    }

    [Test]
    public void EncounterCastAttemptRejectsMixedCreatureAndAreaSelectionBeforeDispatch()
    {
        CreatureFixture caster = CreateCreature("Mixed Target Caster", "Heroes", 100);
        CreatureFixture target = CreateCreature("Mixed Target", "Enemies", 0);
        PreparedSpell spell = PrepareHauntingHymn(caster);
        AreaTargetResult area = PrepareHauntingArea(caster, target, out Tile[,] tiles);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { caster.Controller, target.Controller },
            tiles
        );
        CreatureId casterId = bridge.GetCreatureId(caster.Creature);
        CreatureId targetId = bridge.GetCreatureId(target.Creature);
        bridge.BeginTurn(casterId, 3);
        RulesSnapshot before = bridge.Snapshot;
        ActionInvocationId invocation = new("unity-mixed-selection");

        CastSpellResult result = SpellcastingRuntime.CastEncounterAttempt(
            invocation,
            caster.GameObject,
            spell,
            2,
            new[] { target.GameObject },
            area
        );

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("combine creature targets with an area"));
        Assert.That(bridge.Snapshot.Version, Is.EqualTo(before.Version));
        Assert.That(bridge.Snapshot.ActionEconomy[casterId].ActionsRemaining, Is.EqualTo(3));
        Assert.That(bridge.Snapshot.Health[targetId].Current, Is.EqualTo(20));
        bridge.ReleaseOwnership();
    }

    [Test]
    public void RulesCastSpellActionRetainsCompleteIntentUntilStructuralCompletion()
    {
        CreatureFixture caster = CreateCreature("Pending Cast Caster", "Heroes", 100);
        CreatureFixture target = CreateCreature("Pending Cast Target", "Enemies", 0);
        PrepareHauntingHymn(caster);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { caster.Controller, target.Controller },
            CreateTiles()
        );
        CreatureId casterId = bridge.GetCreatureId(caster.Creature);
        CreatureId targetId = bridge.GetCreatureId(target.Creature);
        RulesCastSpellAction action = caster
            .Controller.GetActions()
            .OfType<RulesCastSpellAction>()
            .Single(value => value.Spell == new SpellReference(new SpellId("haunting-hymn"), 1));
        SpellCastSelection originalSelection = new(new[] { targetId });
        SpellCastSelection laterSelection = SpellCastSelection.Empty;

        CastSpellActionOp original = action.RetainPendingOperation(casterId, originalSelection);
        CastSpellActionOp replay = action.RetainPendingOperation(casterId, laterSelection);

        Assert.That(replay, Is.SameAs(original));
        Assert.That(replay.InvocationId, Is.EqualTo(original.InvocationId));
        Assert.That(replay.Selection, Is.SameAs(originalSelection));
        Assert.That(action.TryGetPendingOperation(out CastSpellActionOp pending), Is.True);
        Assert.That(pending, Is.SameAs(original));

        action.ClearPendingOperation();
        Assert.That(action.TryGetPendingOperation(out _), Is.False);
        CastSpellActionOp next = action.RetainPendingOperation(casterId, laterSelection);
        Assert.That(next, Is.Not.SameAs(original));
        Assert.That(next.InvocationId, Is.Not.EqualTo(original.InvocationId));
        Assert.That(next.Selection, Is.SameAs(laterSelection));
        action.ClearPendingOperation();
        bridge.ReleaseOwnership();
    }

    [Test]
    public void ProductionSpellCatalogInstallsOnlyExplicitlyRulesReadyDefinitions()
    {
        UnitySpellDefinitionCatalog catalog = UnitySpellDefinitionCatalog.Load();

        Assert.That(
            catalog.Definitions.Select(value => value.Id.Value).OrderBy(value => value),
            Is.EqualTo(new[] { "divine-lance", "haunting-hymn", "light" })
        );
        Assert.That(
            catalog.TryGetSpell(
                new SpellReference(new SpellId("caustic-blast"), 1),
                out Game.Rules.Runtime.SpellDefinition _
            ),
            Is.False
        );
        Assert.That(
            catalog.TryGetSpell(
                new SpellReference(new SpellId("divine-lance"), 1),
                out Game.Rules.Runtime.SpellDefinition divineLance
            ),
            Is.True
        );
        Assert.That(divineLance.Attacks, Has.Count.EqualTo(1));
        Assert.That(divineLance.Saves, Is.Empty);
        Assert.That(divineLance.Effects, Is.Empty);
        Assert.That(
            catalog.TryGetSpell(
                new SpellReference(new SpellId("haunting-hymn"), 1),
                out Game.Rules.Runtime.SpellDefinition hauntingHymn
            ),
            Is.True
        );
        Assert.That(hauntingHymn.Attacks, Is.Empty);
        Assert.That(hauntingHymn.Saves, Has.Count.EqualTo(1));
        Assert.That(hauntingHymn.Effects, Is.Empty);
        Assert.That(
            catalog.TryGetSpell(
                new SpellReference(new SpellId("light"), 1),
                out Game.Rules.Runtime.SpellDefinition light
            ),
            Is.True
        );
        Assert.That(light.Attacks, Is.Empty);
        Assert.That(light.Saves, Is.Empty);
        Assert.That(light.Effects, Has.Count.EqualTo(1));
        Assert.That(light.Effects.Single().MaximumActiveInstances, Is.EqualTo(4));
    }

    [TestCase("attack")]
    [TestCase("save")]
    public void SpellCatalogCountsPartialAuthoredCategoriesAlongsideSupportedEffects(
        string category
    )
    {
        JObject root = JObject.Parse(CreateCatalogEffectSpellJson("2", "1 minute"));
        JObject system = (JObject)root["system"];
        if (category == "attack")
            ((JArray)system.SelectToken("traits.value")).Add("attack");
        else
            system["defense"] = new JObject { ["save"] = JValue.CreateNull() };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            UnitySpellDefinitionCatalog.Parse(root.ToString())
        );

        Assert.That(
            error.Message,
            Is.EqualTo(
                "Spell 'Catalog Audit Spell' requires exactly one authored resolution category."
            )
        );
    }

    [Test]
    public void SpellCatalogRejectsIncompleteSingleAuthoredAttackCategory()
    {
        JObject root = JObject.Parse(CreateCatalogEffectSpellJson("2", "1 minute"));
        JObject system = (JObject)root["system"];
        system["rules"] = new JArray();
        ((JArray)system.SelectToken("traits.value")).Add("attack");

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            UnitySpellDefinitionCatalog.Parse(root.ToString())
        );

        Assert.That(
            error.Message,
            Is.EqualTo(
                "Spell 'Catalog Audit Spell' has an incomplete or unsupported authored attack category."
            )
        );
    }

    [Test]
    public void SpellCatalogRejectsUnsupportedSingleAuthoredSaveCategory()
    {
        JObject root = JObject.Parse(CreateCatalogEffectSpellJson("2", "1 minute"));
        JObject system = (JObject)root["system"];
        system["rules"] = new JArray();
        system["defense"] = new JObject
        {
            ["save"] = new JObject { ["basic"] = false, ["statistic"] = "fortitude" },
        };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            UnitySpellDefinitionCatalog.Parse(root.ToString())
        );

        Assert.That(
            error.Message,
            Is.EqualTo(
                "Spell 'Catalog Audit Spell' has an incomplete or unsupported authored save category."
            )
        );
    }

    [Test]
    public void SpellCatalogAcceptsOnlySupportedCastingTimeForms()
    {
        (string Authored, int[] Actions)[] supported =
        {
            ("1", new[] { 1 }),
            ("2", new[] { 2 }),
            ("3", new[] { 3 }),
            ("1 to 3", new[] { 1, 2, 3 }),
        };

        foreach ((string authored, int[] actions) in supported)
        {
            Game.Rules.Runtime.SpellDefinition definition = UnitySpellDefinitionCatalog.Parse(
                CreateCatalogEffectSpellJson(authored, "1 minute")
            );

            Assert.That(
                definition.Variants.Select(variant => variant.Actions),
                Is.EqualTo(actions),
                authored
            );
        }
    }

    [TestCase(null, false)]
    [TestCase("two", true)]
    [TestCase("reaction", true)]
    [TestCase("1 minute", true)]
    public void SpellCatalogRejectsMissingTypoAndUnsupportedCastingTimes(
        string authored,
        bool includeCastingTime
    )
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            UnitySpellDefinitionCatalog.Parse(
                CreateCatalogEffectSpellJson(
                    authored,
                    "1 minute",
                    includeCastingTime: includeCastingTime
                )
            )
        );

        Assert.That(
            error.Message,
            Is.EqualTo(
                includeCastingTime
                    ? $"Spell 'Catalog Audit Spell' has unsupported casting time '{authored}'."
                    : "Spell 'Catalog Audit Spell' requires a casting-time value."
            )
        );
    }

    [Test]
    public void SpellCatalogMapsSupportedFiniteAndLightDurationsExplicitly()
    {
        (string Authored, EffectDuration Duration)[] supported =
        {
            ("1 minute", EffectDuration.OneMinute),
            ("10 minutes", EffectDuration.Minutes(10)),
            ("until your next daily preparations", EffectDuration.Indefinite),
        };

        foreach ((string authored, EffectDuration duration) in supported)
        {
            Game.Rules.Runtime.SpellDefinition definition = UnitySpellDefinitionCatalog.Parse(
                CreateCatalogEffectSpellJson("2", authored)
            );

            Assert.That(definition.Effects.Single().Duration, Is.EqualTo(duration), authored);
        }
    }

    [Test]
    public void SpellCatalogTreatsMissingMaximumActiveInstancesAsUnlimited()
    {
        Game.Rules.Runtime.SpellDefinition definition = UnitySpellDefinitionCatalog.Parse(
            CreateCatalogEffectSpellJson("2", "1 minute")
        );

        Assert.That(definition.Effects.Single().MaximumActiveInstances, Is.Null);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void SpellCatalogRejectsNonPositiveMaximumActiveInstances(int authored)
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            UnitySpellDefinitionCatalog.Parse(
                CreateCatalogEffectSpellJson(
                    "2",
                    "1 minute",
                    maximumActiveInstances: new JValue(authored)
                )
            )
        );

        Assert.That(
            error.Message,
            Is.EqualTo(
                "Spell 'Catalog Audit Spell' requires maximumActiveInstances to be a positive integer when supplied."
            )
        );
    }

    [Test]
    public void SpellCatalogRejectsNonIntegerMaximumActiveInstances()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            UnitySpellDefinitionCatalog.Parse(
                CreateCatalogEffectSpellJson(
                    "2",
                    "1 minute",
                    maximumActiveInstances: new JValue("4")
                )
            )
        );

        Assert.That(
            error.Message,
            Is.EqualTo(
                "Spell 'Catalog Audit Spell' requires maximumActiveInstances to be a positive integer when supplied."
            )
        );
    }

    [TestCase(null, false)]
    [TestCase("1 mintue", true)]
    [TestCase("1 minutes", true)]
    [TestCase("until your next daily preparation", true)]
    [TestCase("unlimited", true)]
    public void SpellCatalogRejectsMissingUnknownAndMisspelledDurations(
        string authored,
        bool includeDuration
    )
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            UnitySpellDefinitionCatalog.Parse(
                CreateCatalogEffectSpellJson("2", authored, includeDuration: includeDuration)
            )
        );

        Assert.That(
            error.Message,
            Is.EqualTo(
                includeDuration
                    ? $"Spell 'Catalog Audit Spell' has unsupported effect duration '{authored}'."
                    : "Spell 'Catalog Audit Spell' requires an effect-duration value."
            )
        );
    }

    [Test]
    public void ProductionAreaProviderRevalidatesConeOriginDirectionAndLineOfEffect()
    {
        CreatureFixture caster = CreateCreature("Area Provider Caster", "Heroes", 100);
        CreatureFixture target = CreateCreature("Area Provider Target", "Enemies", 0);
        PrepareHauntingHymn(caster);
        AreaTargetResult area = PrepareHauntingArea(caster, target, out Tile[,] tiles);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { caster.Controller, target.Controller },
            tiles
        );
        CreatureId casterId = bridge.GetCreatureId(caster.Creature);
        CreatureId targetId = bridge.GetCreatureId(target.Creature);
        UnitySpellAttackContext provider = new(
            new Dictionary<CreatureId, CreatureComponent>
            {
                [casterId] = caster.Creature,
                [targetId] = target.Creature,
            },
            tiles
        );
        Assert.That(
            SpellcastingRuntime.TryCreateRulesAreaSelection(
                bridge,
                area,
                out SpellCastSelection legal,
                out string reason
            ),
            Is.True,
            reason
        );
        UnitySpellDefinitionCatalog catalog = UnitySpellDefinitionCatalog.Load();
        Assert.That(
            catalog.TryGetSpell(
                new SpellReference(new SpellId("haunting-hymn"), 1),
                out Game.Rules.Runtime.SpellDefinition definition
            ),
            Is.True
        );
        SpellSaveDefinition save = definition.Saves.Single();

        Assert.That(
            provider.Validate(
                bridge.Snapshot,
                casterId,
                save,
                legal.AreaPlacement,
                legal.Creatures
            ),
            Is.TypeOf<ActionValidationResult.ValidActionValidationResult>()
        );
        SpellAreaPlacement offCone = new(
            SpellAreaShape.Cone,
            legal.AreaPlacement.OriginCell,
            legal.AreaPlacement.OriginCornerX,
            legal.AreaPlacement.OriginCornerZ,
            SpellAreaDirection.East
        );
        Assert.That(
            provider.Validate(bridge.Snapshot, casterId, save, offCone, legal.Creatures),
            Is.TypeOf<ActionValidationResult.InvalidActionValidationResult>()
        );
        SpellAreaPlacement displaced = new(
            SpellAreaShape.Cone,
            new GridPosition(0, 0, 3),
            0,
            3,
            SpellAreaDirection.North
        );
        Assert.That(
            provider.Validate(bridge.Snapshot, casterId, save, displaced, legal.Creatures),
            Is.TypeOf<ActionValidationResult.InvalidActionValidationResult>()
        );

        bool[,] blockers = new bool[tiles.GetLength(0), tiles.GetLength(1)];
        blockers[0, 1] = true;
        GridLineOfSightData.Register(tiles, blockers);
        try
        {
            Assert.That(
                provider.Validate(
                    bridge.Snapshot,
                    casterId,
                    save,
                    legal.AreaPlacement,
                    legal.Creatures
                ),
                Is.TypeOf<ActionValidationResult.InvalidActionValidationResult>()
            );
        }
        finally
        {
            GridLineOfSightData.Unregister(tiles);
            bridge.ReleaseOwnership();
        }
    }

    [Test]
    public void DetachedHauntingHymnFailsClosedWithoutUnityDamageOrActionSpending()
    {
        CreatureFixture caster = CreateCreature("Detached Hymn Caster", "Heroes", 100);
        CreatureFixture target = CreateCreature("Detached Hymn Target", "Enemies", 0);
        PreparedSpell spell = PrepareHauntingHymn(caster);
        int targetHealth = target.Creature.hp;
        uint actions = caster.Controller.ActionPoints;

        CastSpellResult result = SpellcastingRuntime.Cast(
            caster.GameObject,
            spell,
            2,
            new[] { target.GameObject }
        );

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("not implemented"));
        Assert.That(target.Creature.hp, Is.EqualTo(targetHealth));
        Assert.That(caster.Controller.ActionPoints, Is.EqualTo(actions));
    }

    [TestCase("DataFiles/pathfinder-monster-core/zombie-shambler")]
    [TestCase("DataFiles/pathfinder-monster-core/zombie-shambler-rotting-aura")]
    public void AuthoredZombieSlowImportsEnrollsReplaysAndSuppressesReactions(string path)
    {
        CreatureFixture zombie = CreateCreatureFromJson(path, "Enemies", 100);
        CreatureFixture opponent = CreateCreature("Zombie Opponent", "Heroes", 0);
        Assert.That(zombie.Creature.passives.Count(value => value == "Slow"), Is.EqualTo(1));
        zombie.Conditions.RestoreApplications(
            new[]
            {
                Persisted(
                    zombie.GameObject,
                    ConditionRuleDefinitions.Slowed,
                    "saved-zombie-slowed",
                    new SlowedConditionState(1),
                    zombie.DurableActorId
                ),
            }
        );

        UnityCombatRulesBridge first = UnityCombatRulesBridge.Create(
            new ActionController[] { zombie.Controller, opponent.Controller },
            CreateTiles()
        );
        CreatureId zombieId = first.GetCreatureId(zombie.Creature);
        string authoredIdentity =
            $"authored-passive-slow-{DurableActorSourceIdentity.Reserve(zombie.DurableActorId).Value}";
        ConditionSelection<IEffectState>[] slowed = ConditionSelectors
            .GetActiveInstances(first.Snapshot, zombieId, ConditionRuleDefinitions.Slowed)
            .ToArray();

        Assert.That(slowed, Has.Length.EqualTo(2));
        ConditionSelection<IEffectState> authored = slowed.Single(value =>
            value.Source == RuleSource.FromSlug("authored-passive-slow")
        );
        Assert.That(authored.Effect.Id.Value, Is.EqualTo($"{authoredIdentity}-effect"));
        Assert.That(authored.Binding.Id.Value, Is.EqualTo($"{authoredIdentity}-binding"));
        Assert.That(authored.Effect.Duration, Is.EqualTo(EffectDuration.Indefinite));
        Assert.That(authored.Effect.GetState<SlowedConditionState>().Value, Is.EqualTo(1));
        first.BeginTurn(zombieId, 2);
        Assert.That(first.GetActionEconomy(zombieId).ReactionAvailable, Is.False);
        Assert.That(zombie.Controller.Reacted, Is.True);

        first.ReleaseOwnership();
        Assert.That(zombie.Conditions.CaptureApplications(), Has.Count.EqualTo(2));
        UnityCombatRulesBridge replay = UnityCombatRulesBridge.Create(
            new ActionController[] { opponent.Controller, zombie.Controller },
            CreateTiles()
        );
        CreatureId replayId = replay.GetCreatureId(zombie.Creature);
        ConditionSelection<IEffectState>[] replayed = ConditionSelectors
            .GetActiveInstances(replay.Snapshot, replayId, ConditionRuleDefinitions.Slowed)
            .ToArray();
        Assert.That(replayed, Has.Length.EqualTo(2));
        Assert.That(
            replayed
                .Single(value => value.Source == RuleSource.FromSlug("authored-passive-slow"))
                .Effect.Id.Value,
            Is.EqualTo($"{authoredIdentity}-effect")
        );
        replay.BeginTurn(replayId, 2);
        Assert.That(replay.GetActionEconomy(replayId).ReactionAvailable, Is.False);
    }

    [TestCase("DataFiles/pathfinder-monster-core/zombie-shambler")]
    [TestCase("DataFiles/pathfinder-monster-core/zombie-shambler-rotting-aura")]
    public void ReinforcementZombieSlowUsesTheSameEnrollmentAndTurnAuthority(string path)
    {
        CreatureFixture initial = CreateCreature("Reinforcement Initial", "Heroes", 100);
        CreatureFixture opponent = CreateCreature("Reinforcement Opponent", "Enemies", 50);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { initial.Controller, opponent.Controller },
            CreateTiles()
        );
        bridge.StartEncounter("Heroes");
        CreatureFixture zombie = CreateCreatureFromJson(path, "Enemies", 0);

        bridge.RegisterCombatants(new[] { zombie.Controller });

        CreatureId zombieId = bridge.GetCreatureId(zombie.Creature);
        ConditionSelection<IEffectState> authored = ConditionSelectors
            .GetActiveInstances(bridge.Snapshot, zombieId, ConditionRuleDefinitions.Slowed)
            .Single();
        Assert.That(authored.Source, Is.EqualTo(RuleSource.FromSlug("authored-passive-slow")));
        Assert.That(authored.Effect.GetState<SlowedConditionState>().Value, Is.EqualTo(1));
        bridge.BeginTurn(zombieId, 2);
        Assert.That(bridge.GetActionEconomy(zombieId).ReactionAvailable, Is.False);
        Assert.That(zombie.Controller.Reacted, Is.True);
    }

    [TestCase("ordinary-slowed")]
    [TestCase("authored-passive-slow")]
    public void OrdinaryOrFakeIdentitySlowedReducesActionsWithoutSuppressingReaction(string source)
    {
        CreatureFixture actor = CreateCreature("Ordinary Slowed", "Heroes", 100);
        CreatureFixture opponent = CreateCreature("Ordinary Slowed Opponent", "Enemies", 0);
        actor.Conditions.RestoreApplications(
            new[]
            {
                Persisted(
                    actor.GameObject,
                    ConditionRuleDefinitions.Slowed,
                    source,
                    new SlowedConditionState(1),
                    actor.DurableActorId
                ),
            }
        );
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { actor.Controller, opponent.Controller },
            CreateTiles()
        );
        CreatureId actorId = bridge.GetCreatureId(actor.Creature);

        bridge.BeginTurn(actorId, 2);

        Assert.That(bridge.GetActionEconomy(actorId).ActionsRemaining, Is.EqualTo(2));
        Assert.That(bridge.GetActionEconomy(actorId).ReactionAvailable, Is.True);
        Assert.That(actor.Controller.Reacted, Is.False);
        bridge.ReleaseOwnership();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AuthoredSlowStableIdentityCollisionRejectsEnrollmentAtomically(bool reinforcement)
    {
        CreatureFixture zombie = CreateCreatureFromJson(
            "DataFiles/pathfinder-monster-core/zombie-shambler",
            "Enemies",
            reinforcement ? -1 : 100
        );
        CreatureFixture initial = CreateCreature("Slow Collision Initial", "Heroes", 100);
        string stable =
            $"authored-passive-slow-{DurableActorSourceIdentity.Reserve(zombie.DurableActorId).Value}";
        zombie.Conditions.RestoreApplications(
            new[]
            {
                new ConditionApplicationSnapshot(
                    new ActiveEffectId($"{stable}-effect"),
                    new BindingId($"{stable}-binding"),
                    ConditionRuleDefinitions.Slowed,
                    zombie.DurableActorId,
                    RuleSource.FromSlug("conflicting-authored-passive-slow"),
                    EffectDuration.Indefinite,
                    EffectStateVersion.Initial,
                    new SlowedConditionState(1),
                    ActiveEffectStatus.Active,
                    1,
                    true,
                    null
                ),
            }
        );

        if (!reinforcement)
        {
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
                UnityCombatRulesBridge.Create(
                    new ActionController[] { zombie.Controller, initial.Controller },
                    CreateTiles()
                )
            );
            Assert.That(failure.Message, Does.Contain("collide"));
            Assert.That(zombie.Conditions.HasPendingRestore, Is.True);
            Assert.That(zombie.Controller.HasTurnAuthority, Is.False);
            return;
        }

        CreatureFixture opponent = CreateCreature("Slow Collision Opponent", "Enemies", 0);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { initial.Controller, opponent.Controller },
            CreateTiles()
        );
        bridge.StartEncounter("Heroes");
        long version = bridge.Snapshot.Version;
        int rosterCount = bridge.GetEncounter().Roster.Count;

        InvalidOperationException reinforcementFailure = Assert.Throws<InvalidOperationException>(
            () =>
                bridge.RegisterCombatants(new[] { zombie.Controller })
        );

        Assert.That(reinforcementFailure.Message, Does.Contain("collide"));
        Assert.That(bridge.Snapshot.Version, Is.EqualTo(version));
        Assert.That(bridge.GetEncounter().Roster, Has.Count.EqualTo(rosterCount));
        Assert.That(zombie.Conditions.HasPendingRestore, Is.True);
        Assert.That(zombie.Controller.HasTurnAuthority, Is.False);
        bridge.ReleaseOwnership();
    }

    [Test]
    public void DuplicateAuthoredSlowPassiveFailsEnrollmentWithoutPartialConditionState()
    {
        CreatureFixture zombie = CreateCreatureFromJson(
            "DataFiles/pathfinder-monster-core/zombie-shambler",
            "Enemies",
            100
        );
        zombie.Creature.passives.Add("Slow");
        CreatureFixture opponent = CreateCreature("Malformed Zombie Opponent", "Heroes", 0);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            UnityCombatRulesBridge.Create(
                new ActionController[] { zombie.Controller, opponent.Controller },
                CreateTiles()
            )
        );

        Assert.That(error.Message, Does.Contain("must occur exactly once"));
        Assert.That(zombie.Conditions.ActiveConditionNames, Is.Empty);
        Assert.That(zombie.Controller.TryGetCombatRules(out _, out _), Is.False);
    }

    [Test]
    public void PersistenceSeedsInitialAndReinforcementConditionsIntoOneStore()
    {
        CreatureFixture initial = CreateCreature("Initial", "Heroes", 100);
        CreatureFixture opponent = CreateCreature("Opponent", "Enemies", 0);
        initial.Conditions.RestoreApplications(
            new[]
            {
                Persisted(
                    initial.GameObject,
                    ConditionRuleDefinitions.Fatigued,
                    "initial-fatigue",
                    ConditionMarkerState.Instance
                ),
            }
        );
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { initial.Controller, opponent.Controller },
            CreateTiles()
        );
        bridge.StartEncounter("Heroes");
        CreatureId initialId = bridge.GetCreatureId(initial.Creature);

        CreatureFixture reinforcement = CreateCreature("Reinforcement", "Enemies", -1);
        reinforcement.Conditions.RestoreApplications(
            new[]
            {
                Persisted(
                    reinforcement.GameObject,
                    ConditionRuleDefinitions.Encumbered,
                    "reinforcement-load",
                    ConditionMarkerState.Instance
                ),
            }
        );
        bridge.RegisterCombatants(new[] { reinforcement.Controller });
        CreatureId reinforcementId = bridge.GetCreatureId(reinforcement.Creature);

        Assert.That(
            ConditionSelectors.HasMarker(
                bridge.Snapshot,
                initialId,
                ConditionRuleDefinitions.Fatigued
            ),
            Is.True
        );
        Assert.That(
            ConditionSelectors.HasMarker(
                bridge.Snapshot,
                reinforcementId,
                ConditionRuleDefinitions.Encumbered
            ),
            Is.True
        );
        Assert.That(
            ConditionSelectors
                .GetActiveInstances(
                    bridge.Snapshot,
                    reinforcementId,
                    ConditionRuleDefinitions.Encumbered
                )
                .Count,
            Is.EqualTo(1)
        );
    }

    [Test]
    public void InitialRestoreRejectsCompiledFlatFootedImmunityBeforeAuthorityAttaches()
    {
        CreatureFixture immune = CreateCreature("Immune Initial", "Heroes", 100);
        CreatureFixture opponent = CreateCreature("Immune Initial Opponent", "Enemies", 0);
        immune.Creature.immunities.Add("Flat-Footed");
        immune.Conditions.RestoreApplications(
            new[]
            {
                Persisted(
                    immune.GameObject,
                    ConditionRuleDefinitions.OffGuard,
                    "immune-initial-off-guard",
                    ConditionMarkerState.Instance
                ),
            }
        );

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            UnityCombatRulesBridge.Create(
                new[] { immune.Controller, opponent.Controller },
                CreateTiles()
            )
        );

        Assert.That(failure.Message, Does.Contain("immune to off-guard"));
        Assert.That(immune.Conditions.HasPendingRestore, Is.True);
        Assert.That(immune.Controller.HasTurnAuthority, Is.False);
        Assert.That(opponent.Controller.HasTurnAuthority, Is.False);
    }

    [Test]
    public void ReinforcementRestoreRejectsCompiledFlatFootedImmunityAtomically()
    {
        CreatureFixture initial = CreateCreature("Immune Join Initial", "Heroes", 100);
        CreatureFixture opponent = CreateCreature("Immune Join Opponent", "Enemies", 0);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new[] { initial.Controller, opponent.Controller },
            CreateTiles()
        );
        bridge.StartEncounter("Heroes");
        CreatureFixture reinforcement = CreateCreature("Immune Join", "Enemies", -1);
        reinforcement.Creature.immunities.Add("Flat-Footed");
        reinforcement.Conditions.RestoreApplications(
            new[]
            {
                Persisted(
                    reinforcement.GameObject,
                    ConditionRuleDefinitions.OffGuard,
                    "immune-join-off-guard",
                    ConditionMarkerState.Instance
                ),
            }
        );
        long version = bridge.Snapshot.Version;
        int rosterCount = bridge.GetEncounter().Roster.Count;

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            bridge.RegisterCombatants(new[] { reinforcement.Controller })
        );

        Assert.That(failure.Message, Does.Contain("immune to off-guard"));
        Assert.That(bridge.Snapshot.Version, Is.EqualTo(version));
        Assert.That(bridge.GetEncounter().Roster, Has.Count.EqualTo(rosterCount));
        Assert.That(bridge.Snapshot.ActiveEffects, Is.Empty);
        Assert.That(reinforcement.Conditions.HasPendingRestore, Is.True);
        Assert.That(reinforcement.Controller.HasTurnAuthority, Is.False);
        bridge.ReleaseOwnership();
    }

    [Test]
    public void FreshApplicationReportsCompiledFlatFootedImmunityWithoutMutation()
    {
        CreatureFixture source = CreateCreature("Immune Fresh Source", "Heroes", 100);
        CreatureFixture target = CreateCreature("Immune Fresh Target", "Enemies", 0);
        target.Creature.immunities.Add("Flat-Footed");
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new[] { source.Controller, target.Controller },
            CreateTiles()
        );
        CreatureId sourceId = bridge.GetCreatureId(source.Creature);
        CreatureId targetId = bridge.GetCreatureId(target.Creature);
        long version = bridge.Snapshot.Version;

        OpResult<ConditionApplicationOutcome> result = bridge.Dispatch(
            new ApplyConditionOp(
                "Flat-Footed",
                targetId,
                sourceId,
                RuleSource.FromSlug("immune-fresh-source"),
                EffectDuration.Indefinite,
                ConditionMarkerState.Instance
            )
        );

        Assert.That(result, Is.TypeOf<ResolvedOpResult<ConditionApplicationOutcome>>());
        ConditionApplicationOutcome blocked = (
            (ResolvedOpResult<ConditionApplicationOutcome>)result
        ).Value;
        Assert.That(blocked.Status, Is.EqualTo(ConditionApplicationStatus.Blocked));
        Assert.That(blocked.BlockedReason, Does.Contain("immune to off-guard"));
        Assert.That(result.Facts, Is.Empty);
        Assert.That(bridge.Snapshot.Version, Is.EqualTo(version));
        Assert.That(bridge.Snapshot.ActiveEffects, Is.Empty);
        Assert.That(bridge.Snapshot.RuleBindings.All(pair => !pair.Value.EffectId.HasValue));
        bridge.ReleaseOwnership();
    }

    [Test]
    public void ConditionAndRestoredSpellJoinInOneAtomicVersionWithAdoptionFacts()
    {
        CreatureFixture source = CreateCreature("Atomic Source", "Heroes", 100);
        CreatureFixture opponent = CreateCreature("Atomic Opponent", "Enemies", 0);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new[] { source.Controller, opponent.Controller },
            CreateTiles()
        );
        bridge.StartEncounter("Heroes");
        CreatureFixture reinforcement = CreateCreature("Atomic Reinforcement", "Enemies", -1);
        ActiveEffectId conditionId = new ActiveEffectId("atomic-condition-effect");
        reinforcement.Conditions.RestoreApplications(
            new[]
            {
                RestoredCondition(
                    reinforcement,
                    conditionId,
                    new BindingId("atomic-condition-binding"),
                    40,
                    true
                ),
            }
        );
        BlessSpellEffect restoredSpell = new BlessSpellEffect(source.GameObject)
        {
            RemainingTargetTurnStarts = 2,
        };
        SpellEffectController
            .GetOrAdd(reinforcement.GameObject)
            .RestoreEffects(new[] { restoredSpell });
        CountingFactObserver<EncounterJoinedFact> joined = new();
        CountingFactObserver<ActiveEffectAdoptedFact> adopted = new();
        CountingFactObserver<ActiveEffectCreatedFact> created = new();
        RuleDispatcher dispatcher = GetDispatcher(bridge);
        using IDisposable joinedRegistration = dispatcher.RegisterFactObserver<EncounterJoinedFact>(
            joined
        );
        using IDisposable adoptedRegistration =
            dispatcher.RegisterFactObserver<ActiveEffectAdoptedFact>(adopted);
        using IDisposable createdRegistration =
            dispatcher.RegisterFactObserver<ActiveEffectCreatedFact>(created);
        long beforeVersion = bridge.Snapshot.Version;

        bridge.RegisterCombatants(new[] { reinforcement.Controller });

        CreatureId reinforcementId = bridge.GetCreatureId(reinforcement.Creature);
        ActiveEffectInstance spell = bridge
            .Snapshot.ActiveEffects.Select(pair => pair.Value)
            .Single(effect =>
                effect.DefinitionId
                == UnitySpellcastingEncounterModule.RestoredTimedEffectDefinitionId
            );
        Assert.That(bridge.GetEncounter().Roster.Any(entry => entry.Creature == reinforcementId));
        Assert.That(
            bridge.Snapshot.ActiveEffectTimings[conditionId].RemainingBoundaries,
            Is.EqualTo(2)
        );
        Assert.That(
            bridge.Snapshot.ActiveEffectTimings[spell.Id].RemainingBoundaries,
            Is.EqualTo(2)
        );
        Assert.That(adopted.Count, Is.EqualTo(2));
        Assert.That(created.Count, Is.Zero);
        Assert.That(joined.Count, Is.EqualTo(1));
        Assert.That(adopted.Versions.Distinct(), Is.EqualTo(new[] { beforeVersion + 1 }));
        Assert.That(joined.Versions.Single(), Is.EqualTo(beforeVersion + 1));
        bridge.ReleaseOwnership();
    }

    [Test]
    public void InitialConditionAndRestoredSpellCollisionRollsBackPreparedUnityState()
    {
        CreatureFixture source = CreateCreature("Cross Feature Source", "Heroes", 100);
        CreatureFixture target = CreateCreature("Cross Feature Target", "Enemies", 0);
        ActiveEffectId collision = new ActiveEffectId(
            "restored-spell-effect-combat-creature-2-bless-0"
        );
        target.Conditions.RestoreApplications(
            new[]
            {
                RestoredCondition(
                    target,
                    collision,
                    new BindingId("cross-feature-condition-binding"),
                    1,
                    false
                ),
            }
        );
        SpellEffectController effects = SpellEffectController.GetOrAdd(target.GameObject);
        effects.RestoreEffects(
            new[] { new BlessSpellEffect(source.GameObject) { RemainingTargetTurnStarts = 2 } }
        );

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            UnityCombatRulesBridge.Create(
                new[] { source.Controller, target.Controller },
                CreateTiles()
            )
        );

        Assert.That(
            failure.Message,
            Is.EqualTo(
                "Prepared active-effect identities collide with different registration state."
            )
        );
        Assert.That(source.Controller.TryGetCombatRules(out _, out _), Is.False);
        Assert.That(target.Controller.TryGetCombatRules(out _, out _), Is.False);
        Assert.That(target.Conditions.HasPendingRestore, Is.True);
        Assert.That(effects.HasEffect<BlessSpellEffect>(), Is.True);
    }

    [TestCase("effect", "Active effect")]
    [TestCase("binding", "Rule binding")]
    [TestCase("timing", "Active effect")]
    public void InitialEnrollmentRejectsRestoredIdentitiesAcrossCompleteBatch(
        string collision,
        string expectedMessage
    )
    {
        CreatureFixture first = CreateCreature("Initial Collision One", "Heroes", 100);
        CreatureFixture second = CreateCreature("Initial Collision Two", "Enemies", 0);
        bool timed = collision == "timing";
        ActiveEffectId firstEffect = new ActiveEffectId("initial-collision-effect");
        BindingId firstBinding = new BindingId("initial-collision-binding");
        ActiveEffectId secondEffect =
            collision == "effect" || timed
                ? firstEffect
                : new ActiveEffectId("initial-collision-effect-two");
        BindingId secondBinding =
            collision == "binding" ? firstBinding : new BindingId("initial-collision-binding-two");
        first.Conditions.RestoreApplications(
            new[] { RestoredCondition(first, firstEffect, firstBinding, 10, timed) }
        );
        second.Conditions.RestoreApplications(
            new[] { RestoredCondition(second, secondEffect, secondBinding, 11, timed) }
        );

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            UnityCombatRulesBridge.Create(
                new[] { first.Controller, second.Controller },
                CreateTiles()
            )
        );

        Assert.That(failure.Message, Does.Contain(expectedMessage));
        Assert.That(first.Controller.TryGetCombatRules(out _, out _), Is.False);
        Assert.That(second.Controller.TryGetCombatRules(out _, out _), Is.False);
        Assert.That(first.Conditions.HasPendingRestore, Is.True);
        Assert.That(second.Conditions.HasPendingRestore, Is.True);
    }

    [TestCase("effect", "adopted active-effect identity")]
    [TestCase("binding", "adopted active-effect identity")]
    [TestCase("timing", "adopted active-effect identity")]
    public void ReinforcementCollisionRollsBackAndCorrectedRetrySucceeds(
        string collision,
        string expectedMessage
    )
    {
        CreatureFixture initial = CreateCreature("Collision Initial", "Heroes", 100);
        CreatureFixture opponent = CreateCreature("Collision Opponent", "Enemies", 0);
        bool timed = collision == "timing";
        ActiveEffectId existingEffect = new ActiveEffectId("store-collision-effect");
        BindingId existingBinding = new BindingId("store-collision-binding");
        initial.Conditions.RestoreApplications(
            new[] { RestoredCondition(initial, existingEffect, existingBinding, 20, timed) }
        );
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new[] { initial.Controller, opponent.Controller },
            CreateTiles()
        );
        bridge.StartEncounter("Heroes");
        CreatureFixture reinforcement = CreateCreature("Collision Reinforcement", "Enemies", -1);
        ActiveEffectId incomingEffect =
            collision == "effect" || timed
                ? existingEffect
                : new ActiveEffectId("incoming-collision-effect");
        BindingId incomingBinding =
            collision == "binding" ? existingBinding : new BindingId("incoming-collision-binding");
        reinforcement.Conditions.RestoreApplications(
            new[] { RestoredCondition(reinforcement, incomingEffect, incomingBinding, 21, timed) }
        );
        long versionBefore = bridge.Snapshot.Version;
        int rosterBefore = bridge.GetEncounter().Roster.Count;
        int effectsBefore = bridge.Snapshot.ActiveEffects.Count;
        int bindingsBefore = bridge.Snapshot.RuleBindings.Count;
        int timingsBefore = bridge.Snapshot.ActiveEffectTimings.Count;

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            bridge.RegisterCombatants(new[] { reinforcement.Controller })
        );

        Assert.That(failure.Message, Does.Contain(expectedMessage));
        Assert.That(bridge.Snapshot.Version, Is.EqualTo(versionBefore));
        Assert.That(bridge.GetEncounter().Roster.Count, Is.EqualTo(rosterBefore));
        Assert.That(bridge.Snapshot.ActiveEffects.Count, Is.EqualTo(effectsBefore));
        Assert.That(bridge.Snapshot.RuleBindings.Count, Is.EqualTo(bindingsBefore));
        Assert.That(bridge.Snapshot.ActiveEffectTimings.Count, Is.EqualTo(timingsBefore));
        Assert.That(bridge.TryGetCreatureId(reinforcement.Creature, out _), Is.False);
        Assert.That(reinforcement.Controller.TryGetCombatRules(out _, out _), Is.False);
        Assert.That(reinforcement.Conditions.HasPendingRestore, Is.True);

        ActiveEffectId correctedEffect = new ActiveEffectId($"corrected-{collision}-effect");
        BindingId correctedBinding = new BindingId($"corrected-{collision}-binding");
        reinforcement.Conditions.RestoreApplications(
            new[] { RestoredCondition(reinforcement, correctedEffect, correctedBinding, 22, timed) }
        );

        Assert.DoesNotThrow(() => bridge.RegisterCombatants(new[] { reinforcement.Controller }));
        Assert.That(bridge.GetEncounter().Roster.Count, Is.EqualTo(rosterBefore + 1));
        Assert.That(bridge.Snapshot.ActiveEffects.Contains(correctedEffect), Is.True);
        Assert.That(bridge.Snapshot.RuleBindings.Contains(correctedBinding), Is.True);
        Assert.That(reinforcement.Conditions.HasPendingRestore, Is.False);
        bridge.ReleaseOwnership();
    }

    [Test]
    public void ReinforcementBatchCollisionRollsBackEveryMapAndAllowsCorrectedRetry()
    {
        CreatureFixture initial = CreateCreature("Batch Collision Initial", "Heroes", 100);
        CreatureFixture opponent = CreateCreature("Batch Collision Opponent", "Enemies", 0);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new[] { initial.Controller, opponent.Controller },
            CreateTiles()
        );
        bridge.StartEncounter("Heroes");
        CreatureFixture first = CreateCreature("Batch Collision One", "Enemies", -1);
        CreatureFixture second = CreateCreature("Batch Collision Two", "Enemies", -2);
        ActiveEffectId sharedEffect = new ActiveEffectId("incoming-batch-shared-effect");
        first.Conditions.RestoreApplications(
            new[]
            {
                RestoredCondition(
                    first,
                    sharedEffect,
                    new BindingId("incoming-batch-binding-one"),
                    30,
                    false
                ),
            }
        );
        second.Conditions.RestoreApplications(
            new[]
            {
                RestoredCondition(
                    second,
                    sharedEffect,
                    new BindingId("incoming-batch-binding-two"),
                    31,
                    false
                ),
            }
        );
        long versionBefore = bridge.Snapshot.Version;
        int rosterBefore = bridge.GetEncounter().Roster.Count;

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            bridge.RegisterCombatants(new[] { first.Controller, second.Controller })
        );

        Assert.That(failure.Message, Does.Contain("adoption batch contains duplicate"));
        Assert.That(bridge.Snapshot.Version, Is.EqualTo(versionBefore));
        Assert.That(bridge.GetEncounter().Roster.Count, Is.EqualTo(rosterBefore));
        Assert.That(bridge.TryGetCreatureId(first.Creature, out _), Is.False);
        Assert.That(bridge.TryGetCreatureId(second.Creature, out _), Is.False);
        Assert.That(first.Controller.TryGetCombatRules(out _, out _), Is.False);
        Assert.That(second.Controller.TryGetCombatRules(out _, out _), Is.False);
        Assert.That(first.Conditions.HasPendingRestore, Is.True);
        Assert.That(second.Conditions.HasPendingRestore, Is.True);

        ActiveEffectId correctedEffect = new ActiveEffectId("incoming-batch-corrected-effect");
        second.Conditions.RestoreApplications(
            new[]
            {
                RestoredCondition(
                    second,
                    correctedEffect,
                    new BindingId("incoming-batch-corrected-binding"),
                    32,
                    false
                ),
            }
        );

        Assert.DoesNotThrow(() =>
            bridge.RegisterCombatants(new[] { first.Controller, second.Controller })
        );
        Assert.That(bridge.GetEncounter().Roster.Count, Is.EqualTo(rosterBefore + 2));
        Assert.That(bridge.GetCreatureId(first.Creature).Value, Is.EqualTo("combat-creature-3"));
        Assert.That(bridge.GetCreatureId(second.Creature).Value, Is.EqualTo("combat-creature-4"));
        Assert.That(bridge.Snapshot.ActiveEffects.Contains(sharedEffect), Is.True);
        Assert.That(bridge.Snapshot.ActiveEffects.Contains(correctedEffect), Is.True);
        Assert.That(first.Conditions.HasPendingRestore, Is.False);
        Assert.That(second.Conditions.HasPendingRestore, Is.False);
        bridge.ReleaseOwnership();
    }

    [Test]
    public void InitialInstallationFailurePreservesRestoreUntilWholeBatchRetryFinalizes()
    {
        CreatureFixture actor = CreateCreature("Initial Restore Actor", "Heroes", 100);
        CreatureFixture opponent = CreateCreature("Initial Restore Opponent", "Enemies", 0);
        actor.Conditions.RestoreApplications(
            new[]
            {
                Persisted(
                    actor.GameObject,
                    ConditionRuleDefinitions.Stunned,
                    "initial-failed-stunned",
                    new ValuedStunnedConditionState(2)
                ),
            }
        );
        ControllableInstallationModule installer = new ControllableInstallationModule
        {
            TargetName = actor.GameObject.name,
            FailuresRemaining = 1,
        };

        Assert.Throws<InvalidOperationException>(() =>
            UnityCombatRulesBridge.CreateForTests(
                new[] { actor.Controller, opponent.Controller },
                CreateTiles(),
                new RandomRollService(),
                new IUnityEncounterModule[] { installer }
            )
        );

        Assert.That(actor.Conditions.HasPendingRestore, Is.True);
        UnityCombatRulesBridge retry = UnityCombatRulesBridge.CreateForTests(
            new[] { actor.Controller, opponent.Controller },
            CreateTiles(),
            new RandomRollService(),
            new IUnityEncounterModule[] { installer }
        );
        CreatureId actorId = retry.GetCreatureId(actor.Creature);
        Assert.That(actor.Conditions.HasPendingRestore, Is.False);
        Assert.That(
            ConditionSelectors.TryGetStunned(retry.Snapshot, actorId, out var stunned),
            Is.True
        );
        Assert.That(stunned.State, Is.TypeOf<ValuedStunnedConditionState>());
        Assert.That(((ValuedStunnedConditionState)stunned.State).Value, Is.EqualTo(2));
        retry.ReleaseOwnership();
    }

    [Test]
    public void ReinforcementInstallationFailureRetriesCommittedBatchWithoutDuplicateStateOrFacts()
    {
        CreatureFixture initial = CreateCreature("Retry Initial", "Heroes", 100);
        CreatureFixture opponent = CreateCreature("Retry Opponent", "Enemies", 0);
        ControllableInstallationModule installer = new ControllableInstallationModule();
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.CreateForTests(
            new[] { initial.Controller, opponent.Controller },
            CreateTiles(),
            new RandomRollService(),
            new IUnityEncounterModule[] { installer }
        );
        bridge.StartEncounter("Heroes");
        CreatureFixture reinforcement = CreateCreature("Retry Reinforcement", "Enemies", -1);
        reinforcement.Conditions.RestoreApplications(
            new[]
            {
                Persisted(
                    reinforcement.GameObject,
                    ConditionRuleDefinitions.Fatigued,
                    "reinforcement-failed-fatigue",
                    ConditionMarkerState.Instance,
                    "historical-reinforcement-source"
                ),
            }
        );
        installer.TargetName = reinforcement.GameObject.name;
        installer.FailuresRemaining = 1;
        CountingFactObserver<EncounterJoinedFact> joined =
            new CountingFactObserver<EncounterJoinedFact>();
        CountingFactObserver<ActiveEffectAdoptedFact> adopted =
            new CountingFactObserver<ActiveEffectAdoptedFact>();
        CountingFactObserver<ActiveEffectCreatedFact> created =
            new CountingFactObserver<ActiveEffectCreatedFact>();
        RuleDispatcher dispatcher = GetDispatcher(bridge);
        using IDisposable joinedRegistration = dispatcher.RegisterFactObserver<EncounterJoinedFact>(
            joined
        );
        using IDisposable adoptedRegistration =
            dispatcher.RegisterFactObserver<ActiveEffectAdoptedFact>(adopted);
        using IDisposable createdRegistration =
            dispatcher.RegisterFactObserver<ActiveEffectCreatedFact>(created);

        Assert.Throws<InvalidOperationException>(() =>
            bridge.RegisterCombatants(new[] { reinforcement.Controller })
        );

        ActiveEffectId effectId = new ActiveEffectId("effect-reinforcement-failed-fatigue");
        Assert.That(reinforcement.Conditions.HasPendingRestore, Is.True);
        Assert.That(bridge.Snapshot.ActiveEffects.Contains(effectId), Is.True);
        CreatureId reinforcementId = bridge.GetCreatureId(reinforcement.Creature);
        CreatureId reservedSource = bridge.Snapshot.ActiveEffects[effectId].SourceCreature;
        Assert.That(reservedSource, Is.Not.EqualTo(reinforcementId));
        Assert.That(bridge.Snapshot.Creatures.Contains(reservedSource), Is.False);
        Assert.That(
            reinforcement.Conditions.CaptureApplications().Single().SourceActorId,
            Is.EqualTo("historical-reinforcement-source")
        );
        Assert.That(joined.Count, Is.EqualTo(1));
        Assert.That(adopted.Count, Is.EqualTo(1));
        Assert.That(created.Count, Is.Zero);
        long committedVersion = bridge.Snapshot.Version;

        Assert.DoesNotThrow(() => bridge.RegisterCombatants(new[] { reinforcement.Controller }));

        Assert.That(reinforcement.Conditions.HasPendingRestore, Is.False);
        Assert.That(
            reinforcement.Conditions.CaptureApplications().Single().SourceActorId,
            Is.EqualTo("historical-reinforcement-source")
        );
        Assert.That(
            bridge.Snapshot.ActiveEffects.Count(pair => pair.Key == effectId),
            Is.EqualTo(1)
        );
        Assert.That(bridge.Snapshot.Version, Is.EqualTo(committedVersion));
        Assert.That(bridge.GetEncounter().Roster, Has.Count.EqualTo(3));
        Assert.That(joined.Count, Is.EqualTo(1));
        Assert.That(adopted.Count, Is.EqualTo(1));
        Assert.That(created.Count, Is.Zero);
        bridge.ReleaseOwnership();
    }

    [Test]
    public void ReinforcementAdoptionAdvancesNextConditionIdentityWithoutCollision()
    {
        CreatureFixture source = CreateCreature("Rebase Source", "Heroes", 100);
        CreatureFixture target = CreateCreature("Rebase Target", "Enemies", 0);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new[] { source.Controller, target.Controller },
            CreateTiles()
        );
        bridge.StartEncounter("Heroes");
        CreatureFixture reinforcement = CreateCreature("Rebase Reinforcement", "Enemies", -1);
        RuleSource restoredSource = RuleSource.FromSlug("restored-rebase-source");
        const long adoptedCreationOrder = 1000;
        ActiveEffectId adoptedEffectId = new ActiveEffectId("condition-effect-1000");
        BindingId adoptedBindingId = new BindingId("condition-binding-1000");
        reinforcement.Conditions.RestoreApplications(
            new[]
            {
                new ConditionApplicationSnapshot(
                    adoptedEffectId,
                    adoptedBindingId,
                    ConditionRuleDefinitions.Fatigued,
                    source.DurableActorId,
                    restoredSource,
                    EffectDuration.Indefinite,
                    EffectStateVersion.Initial,
                    ConditionMarkerState.Instance,
                    ActiveEffectStatus.Active,
                    adoptedCreationOrder,
                    true,
                    null
                ),
            }
        );
        bridge.RegisterCombatants(new[] { reinforcement.Controller });
        CreatureId sourceId = bridge.GetCreatureId(source.Creature);
        CreatureId targetId = bridge.GetCreatureId(target.Creature);

        ResolvedOpResult<ConditionApplicationOutcome> applied =
            (ResolvedOpResult<ConditionApplicationOutcome>)
                bridge.Dispatch(
                    new ApplyConditionOp(
                        "deafened",
                        targetId,
                        sourceId,
                        RuleSource.FromSlug("new-condition-source"),
                        EffectDuration.Indefinite,
                        ConditionMarkerState.Instance
                    )
                );

        const string effectPrefix = "condition-effect-";
        const string bindingPrefix = "condition-binding-";
        string effectIdentity = applied.Value.EffectId.Value;
        string bindingIdentity = applied.Value.BindingId.Value;
        Assert.That(applied.Value.EffectId, Is.Not.EqualTo(adoptedEffectId));
        Assert.That(applied.Value.BindingId, Is.Not.EqualTo(adoptedBindingId));
        Assert.That(effectIdentity, Does.StartWith(effectPrefix));
        Assert.That(bindingIdentity, Does.StartWith(bindingPrefix));
        string effectSuffixText = effectIdentity.Substring(effectPrefix.Length);
        string bindingSuffixText = bindingIdentity.Substring(bindingPrefix.Length);
        Assert.That(effectSuffixText, Does.Match("^[0-9]+$"));
        Assert.That(bindingSuffixText, Does.Match("^[0-9]+$"));
        long effectSuffix = long.Parse(effectSuffixText);
        long bindingSuffix = long.Parse(bindingSuffixText);
        Assert.That(effectSuffix, Is.GreaterThan(adoptedCreationOrder));
        Assert.That(bindingSuffix, Is.EqualTo(effectSuffix));
        Assert.That(bridge.Snapshot.ActiveEffects.Contains(applied.Value.EffectId), Is.True);
        Assert.That(
            bridge.Snapshot.RuleBindings[applied.Value.BindingId].EffectId,
            Is.EqualTo(applied.Value.EffectId)
        );
        Assert.That(
            bridge.Snapshot.RuleBindings[applied.Value.BindingId].CreationOrder,
            Is.EqualTo(effectSuffix)
        );
        Assert.That(bridge.Snapshot.ActiveEffects.Contains(adoptedEffectId), Is.True);
        Assert.That(bridge.Snapshot.RuleBindings.Contains(adoptedBindingId), Is.True);
        Assert.That(
            bridge.Snapshot.RuleBindings[adoptedBindingId].EffectId,
            Is.EqualTo(adoptedEffectId)
        );
        bridge.ReleaseOwnership();
    }

    [Test]
    public void DurableConditionSourceSurvivesReorderedEnrollmentAndAbsentSourceNormalization()
    {
        CreatureFixture source = CreateCreature("Durable Source", "Heroes", 100);
        CreatureFixture target = CreateCreature("Durable Target", "Enemies", 0);
        UnityCombatRulesBridge first = UnityCombatRulesBridge.Create(
            new[] { source.Controller, target.Controller },
            CreateTiles()
        );
        CreatureId firstSource = first.GetCreatureId(source.Creature);
        CreatureId firstTarget = first.GetCreatureId(target.Creature);
        ResolvedOpResult<ConditionApplicationOutcome> created =
            (ResolvedOpResult<ConditionApplicationOutcome>)
                first.Dispatch(
                    new ApplyConditionOp(
                        "fatigued",
                        firstTarget,
                        firstSource,
                        RuleSource.FromSlug("durable-source-test"),
                        EffectDuration.Indefinite,
                        ConditionMarkerState.Instance
                    )
                );
        first.ReleaseOwnership();

        Assert.That(
            target.Conditions.CaptureApplications().Single().SourceActorId,
            Is.EqualTo(source.DurableActorId)
        );
        UnityCombatRulesBridge reordered = UnityCombatRulesBridge.Create(
            new[] { target.Controller, source.Controller },
            CreateTiles()
        );
        ActiveEffectInstance restored = reordered.Snapshot.ActiveEffects[created.Value.EffectId];
        Assert.That(restored.SourceCreature, Is.EqualTo(reordered.GetCreatureId(source.Creature)));
        reordered.ReleaseOwnership();
        Assert.That(
            target.Conditions.CaptureApplications().Single().SourceActorId,
            Is.EqualTo(source.DurableActorId)
        );

        ConditionApplicationSnapshot durable = target.Conditions.CaptureApplications().Single();
        target.Conditions.RestoreApplications(
            new[]
            {
                new ConditionApplicationSnapshot(
                    durable.EffectId,
                    durable.BindingId,
                    durable.DefinitionId,
                    "defeated-source-actor",
                    durable.Source,
                    durable.Duration,
                    durable.Version,
                    durable.State,
                    durable.Status,
                    durable.CreationOrder,
                    durable.BindingEnabled,
                    durable.Timing
                ),
            }
        );
        UnityCombatRulesBridge absent = UnityCombatRulesBridge.Create(
            new[] { target.Controller, source.Controller },
            CreateTiles()
        );
        CreatureId absentOwner = absent.GetCreatureId(target.Creature);
        CreatureId reservedSource = absent.Snapshot.ActiveEffects[durable.EffectId].SourceCreature;
        Assert.That(reservedSource, Is.Not.EqualTo(absentOwner));
        Assert.That(absent.Snapshot.Creatures.Contains(reservedSource), Is.False);
        absent.StartEncounter("Heroes");
        Assert.That(
            absent.GetEncounter().Roster.Any(entry => entry.Creature == reservedSource),
            Is.False
        );
        Assert.That(
            ConditionSelectors.HasMarker(
                absent.Snapshot,
                absentOwner,
                ConditionRuleDefinitions.Fatigued
            ),
            Is.True
        );
        absent.ReleaseOwnership();
        Assert.That(
            target.Conditions.CaptureApplications().Single().SourceActorId,
            Is.EqualTo("defeated-source-actor")
        );
    }

    [Test]
    public void DuplicateDurableActorIdentityRejectsAndRollsBackEnrollmentMaps()
    {
        CreatureFixture first = CreateCreatureWithDurableId(
            "Duplicate One",
            "Heroes",
            100,
            "duplicate-actor"
        );
        CreatureFixture second = CreateCreatureWithDurableId(
            "Duplicate Two",
            "Enemies",
            0,
            "duplicate-actor"
        );

        Assert.Throws<InvalidOperationException>(() =>
            UnityCombatRulesBridge.Create(
                new[] { first.Controller, second.Controller },
                CreateTiles()
            )
        );
        Assert.That(first.Controller.TryGetCombatRules(out _, out _), Is.False);
        Assert.That(second.Controller.TryGetCombatRules(out _, out _), Is.False);

        CreatureFixture opponent = CreateCreature("Rollback Opponent", "Enemies", 0);
        UnityCombatRulesBridge retry = UnityCombatRulesBridge.Create(
            new[] { first.Controller, opponent.Controller },
            CreateTiles()
        );
        Assert.That(retry.GetCreatureId(first.Creature).IsEmpty, Is.False);
        retry.ReleaseOwnership();
    }

    [Test]
    public void FloorScopedEnemySourcesResolveOnlyExactCurrentDurableIdentity()
    {
        CreatureFixture enemy = CreateCreatureWithoutDurableIdentity(
            "Depth One Enemy",
            "Enemies",
            0
        );
        const string localInstanceId = "encounter-1/creature-0000";
        DungeonEncounterMember member = enemy.GameObject.AddComponent<DungeonEncounterMember>();
        member.Configure(
            "encounter-1",
            localInstanceId,
            1,
            "condition-test-creature",
            string.Empty
        );
        CreatureFixture target = CreateCreature("Historical Target", "Heroes", 100);
        string currentFloorSource = member.DurableActorId;
        string anotherFloorSource = DungeonEnemyDurableActorIdentity.Create(0, localInstanceId);
        string rawUnscopedSource = localInstanceId;
        target.Conditions.RestoreApplications(
            new[]
            {
                Persisted(
                    target.GameObject,
                    ConditionRuleDefinitions.Fatigued,
                    "floor-scoped-current-source",
                    ConditionMarkerState.Instance,
                    currentFloorSource
                ),
                Persisted(
                    target.GameObject,
                    ConditionRuleDefinitions.Fatigued,
                    "floor-scoped-other-floor-source",
                    ConditionMarkerState.Instance,
                    anotherFloorSource
                ),
                Persisted(
                    target.GameObject,
                    ConditionRuleDefinitions.Fatigued,
                    "raw-unscoped-historical-source",
                    ConditionMarkerState.Instance,
                    rawUnscopedSource
                ),
            }
        );

        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new[] { target.Controller, enemy.Controller },
            CreateTiles()
        );
        CreatureId enemyId = bridge.GetCreatureId(enemy.Creature);
        ActiveEffectInstance currentFloor = bridge.Snapshot.ActiveEffects[
            new ActiveEffectId("effect-floor-scoped-current-source")
        ];
        ActiveEffectInstance anotherFloor = bridge.Snapshot.ActiveEffects[
            new ActiveEffectId("effect-floor-scoped-other-floor-source")
        ];
        ActiveEffectInstance rawUnscoped = bridge.Snapshot.ActiveEffects[
            new ActiveEffectId("effect-raw-unscoped-historical-source")
        ];

        Assert.That(currentFloorSource, Is.EqualTo(member.DurableActorId));
        Assert.That(bridge.GetDurableActorId(enemyId), Is.EqualTo(currentFloorSource));
        Assert.That(currentFloor.SourceCreature, Is.EqualTo(enemyId));
        Assert.That(bridge.Snapshot.Creatures.Contains(currentFloor.SourceCreature), Is.True);
        Assert.That(anotherFloor.SourceCreature, Is.Not.EqualTo(enemyId));
        Assert.That(bridge.Snapshot.Creatures.Contains(anotherFloor.SourceCreature), Is.False);
        Assert.That(rawUnscoped.SourceCreature, Is.Not.EqualTo(enemyId));
        Assert.That(bridge.Snapshot.Creatures.Contains(rawUnscoped.SourceCreature), Is.False);
        bridge.ReleaseOwnership();
        Dictionary<ActiveEffectId, ConditionApplicationSnapshot> persisted = target
            .Conditions.CaptureApplications()
            .ToDictionary(application => application.EffectId);
        Assert.That(
            persisted[new ActiveEffectId("effect-floor-scoped-current-source")].SourceActorId,
            Is.EqualTo(currentFloorSource)
        );
        Assert.That(
            persisted[new ActiveEffectId("effect-floor-scoped-other-floor-source")].SourceActorId,
            Is.EqualTo(anotherFloorSource)
        );
        Assert.That(
            persisted[new ActiveEffectId("effect-raw-unscoped-historical-source")].SourceActorId,
            Is.EqualTo(rawUnscopedSource)
        );
    }

    [Test]
    public void MissingDungeonIdentityComponentsRemainIntentionallyNondurable()
    {
        CreatureFixture actor = CreateCreatureWithoutDurableIdentity(
            "Nondurable Actor",
            "Heroes",
            100
        );
        CreatureFixture opponent = CreateCreatureWithoutDurableIdentity(
            "Nondurable Opponent",
            "Enemies",
            0
        );
        actor.Creature.passives.Add("Slow");

        UnityCombatRulesBridge first = UnityCombatRulesBridge.Create(
            new[] { actor.Controller, opponent.Controller },
            CreateTiles()
        );
        CreatureId actorId = first.GetCreatureId(actor.Creature);
        ConditionSelection<IEffectState> authored = ConditionSelectors
            .GetActiveInstances(first.Snapshot, actorId, ConditionRuleDefinitions.Slowed)
            .Single();

        Assert.That(first.GetDurableActorId(actorId), Is.Empty);
        Assert.That(first.GetDurableActorId(first.GetCreatureId(opponent.Creature)), Is.Empty);
        Assert.That(authored.Source, Is.EqualTo(RuleSource.FromSlug("authored-passive-slow")));
        Assert.That(authored.Effect.GetState<SlowedConditionState>().Value, Is.EqualTo(1));
        Assert.That(actor.Conditions.ActiveConditionNames, Does.Contain("slowed"));
        first.BeginTurn(actorId, 2);
        Assert.That(first.GetActionEconomy(actorId).ReactionAvailable, Is.False);
        Assert.That(actor.Controller.Reacted, Is.True);
        Assert.That(actor.Conditions.CaptureApplications(), Is.Empty);

        Assert.DoesNotThrow(() => first.ReleaseOwnership());

        Assert.That(actor.Conditions.HasPendingRestore, Is.True);
        Assert.That(actor.Conditions.CaptureApplications(), Is.Empty);
        CreatureFixture anchor = CreateCreatureWithoutDurableIdentity(
            "Nondurable Heroes Anchor",
            "Heroes",
            100
        );
        UnityCombatRulesBridge second = UnityCombatRulesBridge.Create(
            new[] { anchor.Controller, opponent.Controller },
            CreateTiles()
        );
        second.StartEncounter("Enemies");
        second.RegisterCombatants(new[] { actor.Controller });
        CreatureId reinforcementId = second.GetCreatureId(actor.Creature);
        ConditionSelection<IEffectState> reenrolled = ConditionSelectors
            .GetActiveInstances(second.Snapshot, reinforcementId, ConditionRuleDefinitions.Slowed)
            .Single();
        Assert.That(reenrolled.Source, Is.EqualTo(RuleSource.FromSlug("authored-passive-slow")));
        Assert.That(reenrolled.Effect.GetState<SlowedConditionState>().Value, Is.EqualTo(1));
        Assert.That(actor.Conditions.CaptureApplications(), Is.Empty);
        Assert.DoesNotThrow(() => second.ReleaseOwnership());
        Assert.That(actor.Conditions.CaptureApplications(), Is.Empty);
    }

    [Test]
    public void DurableOwnerPersistenceRejectsNondurableLiveConditionSource()
    {
        CreatureFixture source = CreateCreatureWithoutDurableIdentity(
            "Nondurable Live Source",
            "Heroes",
            100
        );
        CreatureFixture target = CreateCreature("Durable Persistence Target", "Enemies", 0);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new[] { source.Controller, target.Controller },
            CreateTiles()
        );
        CreatureId sourceId = bridge.GetCreatureId(source.Creature);
        CreatureId targetId = bridge.GetCreatureId(target.Creature);
        RuleSource ruleSource = RuleSource.FromSlug("nondurable-live-source");
        ResolvedOpResult<ConditionApplicationOutcome> applied =
            (ResolvedOpResult<ConditionApplicationOutcome>)
                bridge.Dispatch(
                    new ApplyConditionOp(
                        "fatigued",
                        targetId,
                        sourceId,
                        ruleSource,
                        EffectDuration.Indefinite,
                        ConditionMarkerState.Instance
                    )
                );

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            target.Conditions.CaptureApplications()
        );
        Assert.That(
            error.Message,
            Is.EqualTo(
                $"Condition {applied.Value.EffectId.Value} has no canonical durable source actor provenance."
            )
        );

        OpResult<ConditionCleanupOutcome> cleanup = bridge.Dispatch(
            new CleanupConditionsFromSourceOp(
                ruleSource,
                ConditionCleanupKind.Remove,
                targetId,
                ConditionRuleDefinitions.Fatigued
            )
        );
        Assert.That(cleanup, Is.TypeOf<ResolvedOpResult<ConditionCleanupOutcome>>());
        Assert.That(
            ((ResolvedOpResult<ConditionCleanupOutcome>)cleanup).Value.Affected,
            Is.EqualTo(new[] { applied.Value.EffectId })
        );
        Assert.That(target.Conditions.CaptureApplications(), Is.Empty);
        Assert.DoesNotThrow(() => bridge.ReleaseOwnership());
    }

    [TestCase(" padded-party-slot ", "condition-test-creature")]
    [TestCase("party-slot", " padded-condition-test-creature ")]
    [TestCase("dungeon-enemy-v1/7/encounter-1/creature-0000", "condition-test-creature")]
    public void PartyConfigureRejectsInvalidIdentityBeforeMutationAndAllowsCorrectedRetry(
        string rosterId,
        string contentId
    )
    {
        CreatureFixture actor = CreateCreatureWithoutDurableIdentity(
            "Public Party Configure",
            "Heroes",
            100
        );
        DungeonPartyMemberIdentity identity =
            actor.GameObject.AddComponent<DungeonPartyMemberIdentity>();

        Assert.Throws<ArgumentException>(() => identity.Configure(rosterId, contentId));

        Assert.That(identity.IsConfigured, Is.False);
        Assert.That(identity.RosterSlotId, Is.Empty);
        Assert.That(identity.CreatureContentId, Is.Empty);
        Assert.DoesNotThrow(() =>
            identity.Configure("corrected-party-slot", "corrected-creature-content")
        );
        Assert.That(identity.IsConfigured, Is.True);
        Assert.That(identity.RosterSlotId, Is.EqualTo("corrected-party-slot"));
        Assert.That(identity.CreatureContentId, Is.EqualTo("corrected-creature-content"));
    }

    [TestCase(" padded-encounter ", "encounter-1/creature-0000", 0, "condition-test-creature")]
    [TestCase("encounter-1", " padded-encounter-instance ", 0, "condition-test-creature")]
    [TestCase("encounter-1", "encounter-1/creature-0000", 0, " padded-condition-test-creature ")]
    [TestCase("encounter-1", "encounter-1/creature-0000", -1, "condition-test-creature")]
    public void EncounterConfigureRejectsInvalidIdentityBeforeMutationAndAllowsCorrectedRetry(
        string encounterId,
        string instanceId,
        int floorDepth,
        string contentId
    )
    {
        CreatureFixture actor = CreateCreatureWithoutDurableIdentity(
            "Public Encounter Configure",
            "Enemies",
            0
        );
        DungeonEncounterMember member = actor.GameObject.AddComponent<DungeonEncounterMember>();

        Assert.Catch<ArgumentException>(() =>
            member.Configure(
                encounterId,
                instanceId,
                floorDepth,
                contentId,
                "rejected-persistent-state"
            )
        );

        Assert.That(member.IsConfigured, Is.False);
        Assert.That(member.EncounterId, Is.Empty);
        Assert.That(member.InstanceId, Is.Empty);
        Assert.That(member.FloorDepth, Is.EqualTo(-1));
        Assert.That(member.DurableActorId, Is.Empty);
        Assert.That(member.CreatureContentId, Is.Empty);
        Assert.That(member.PersistentState, Is.Empty);
        Assert.DoesNotThrow(() =>
            member.Configure(
                "corrected-encounter",
                "corrected-encounter/creature-0000",
                2,
                "corrected-creature-content",
                "corrected-persistent-state"
            )
        );
        Assert.That(member.IsConfigured, Is.True);
        Assert.That(member.EncounterId, Is.EqualTo("corrected-encounter"));
        Assert.That(member.InstanceId, Is.EqualTo("corrected-encounter/creature-0000"));
        Assert.That(member.FloorDepth, Is.EqualTo(2));
        Assert.That(member.CreatureContentId, Is.EqualTo("corrected-creature-content"));
        Assert.That(member.PersistentState, Is.EqualTo("corrected-persistent-state"));
    }

    [Test]
    public void EnemyDurableIdentityCreateRejectsNoncanonicalInstanceId()
    {
        Assert.Throws<ArgumentException>(() =>
            DungeonEnemyDurableActorIdentity.Create(0, " padded-encounter-instance ")
        );
        Assert.Throws<ArgumentException>(() =>
            DungeonEnemyDurableActorIdentity.Create(0, string.Empty)
        );
    }

    [Test]
    public void PresentUnconfiguredPartyIdentityRejectsAndRollsBackEnrollmentMaps()
    {
        CreatureFixture initial = CreateCreature("Rollback Initial", "Heroes", 100);
        CreatureFixture opponent = CreateCreature("Rollback Opponent", "Enemies", 0);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new[] { initial.Controller, opponent.Controller },
            CreateTiles()
        );
        bridge.StartEncounter("Heroes");
        CreatureFixture valid = CreateCreature("Rollback Valid", "Enemies", -1);
        CreatureFixture invalid = CreateCreatureWithoutDurableIdentity(
            "Rollback Invalid",
            "Enemies",
            -2
        );
        DungeonPartyMemberIdentity invalidIdentity =
            invalid.GameObject.AddComponent<DungeonPartyMemberIdentity>();

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            bridge.RegisterCombatants(new[] { valid.Controller, invalid.Controller })
        );

        Assert.That(failure.Message, Does.Contain(nameof(DungeonPartyMemberIdentity)));
        Assert.That(bridge.TryGetCreatureId(valid.Creature, out _), Is.False);
        Assert.That(bridge.TryGetCreatureId(invalid.Creature, out _), Is.False);
        Assert.That(valid.Controller.TryGetCombatRules(out _, out _), Is.False);
        Assert.That(invalid.Controller.TryGetCombatRules(out _, out _), Is.False);

        invalidIdentity.Configure("rollback-fixed", "condition-test-creature");
        Assert.DoesNotThrow(() =>
            bridge.RegisterCombatants(new[] { valid.Controller, invalid.Controller })
        );
        Assert.That(bridge.GetCreatureId(valid.Creature).Value, Is.EqualTo("combat-creature-3"));
        Assert.That(
            bridge.GetDurableActorId(bridge.GetCreatureId(invalid.Creature)),
            Is.EqualTo("rollback-fixed")
        );
        bridge.ReleaseOwnership();
    }

    [Test]
    public void PartyEnemyNamespaceIdentityRejectsRollsBackAndAllowsCorrectedRetry()
    {
        CreatureFixture initial = CreateCreature("Reserved Prefix Initial", "Heroes", 100);
        CreatureFixture opponent = CreateCreature("Reserved Prefix Opponent", "Enemies", 0);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new[] { initial.Controller, opponent.Controller },
            CreateTiles()
        );
        bridge.StartEncounter("Heroes");
        CreatureFixture valid = CreateCreature("Reserved Prefix Valid", "Enemies", -1);
        CreatureFixture invalid = CreateCreatureWithoutDurableIdentity(
            "Reserved Prefix Invalid",
            "Enemies",
            -2
        );
        DungeonPartyMemberIdentity invalidIdentity =
            invalid.GameObject.AddComponent<DungeonPartyMemberIdentity>();
        SetSerializedField(
            invalidIdentity,
            "rosterSlotId",
            "dungeon-enemy-v1/7/encounter-1/creature-0000"
        );
        SetSerializedField(invalidIdentity, "creatureContentId", "condition-test-creature");

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            bridge.RegisterCombatants(new[] { valid.Controller, invalid.Controller })
        );

        Assert.That(failure.Message, Does.Contain("enemy-only"));
        Assert.That(failure.Message, Does.Contain(nameof(DungeonPartyMemberIdentity)));
        Assert.That(bridge.TryGetCreatureId(valid.Creature, out _), Is.False);
        Assert.That(bridge.TryGetCreatureId(invalid.Creature, out _), Is.False);
        Assert.That(valid.Controller.TryGetCombatRules(out _, out _), Is.False);
        Assert.That(invalid.Controller.TryGetCombatRules(out _, out _), Is.False);

        SetSerializedField(invalidIdentity, "rosterSlotId", "corrected-party-slot");
        Assert.DoesNotThrow(() =>
            bridge.RegisterCombatants(new[] { valid.Controller, invalid.Controller })
        );
        Assert.That(bridge.GetCreatureId(valid.Creature).Value, Is.EqualTo("combat-creature-3"));
        Assert.That(bridge.GetCreatureId(invalid.Creature).Value, Is.EqualTo("combat-creature-4"));
        Assert.That(
            bridge.GetDurableActorId(bridge.GetCreatureId(invalid.Creature)),
            Is.EqualTo("corrected-party-slot")
        );
        bridge.ReleaseOwnership();
    }

    [Test]
    public void PaddedSerializedPartyIdentityRejectsRatherThanNormalizes()
    {
        CreatureFixture actor = CreateCreature("Padded Party", "Heroes", 100);
        CreatureFixture opponent = CreateCreature("Padded Party Opponent", "Enemies", 0);
        DungeonPartyMemberIdentity identity =
            actor.GameObject.GetComponent<DungeonPartyMemberIdentity>();
        SetSerializedField(identity, "rosterSlotId", " padded-party ");

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            UnityCombatRulesBridge.Create(
                new[] { actor.Controller, opponent.Controller },
                CreateTiles()
            )
        );

        Assert.That(failure.Message, Does.Contain("noncanonical"));
        Assert.That(failure.Message, Does.Contain(nameof(DungeonPartyMemberIdentity)));
        Assert.That(failure.InnerException, Is.TypeOf<ArgumentException>());
        Assert.That(actor.Controller.TryGetCombatRules(out _, out _), Is.False);
    }

    [Test]
    public void PresentUnconfiguredEncounterIdentityRejects()
    {
        CreatureFixture actor = CreateCreatureWithoutDurableIdentity(
            "Unconfigured Encounter",
            "Heroes",
            100
        );
        CreatureFixture opponent = CreateCreature("Encounter Opponent", "Enemies", 0);
        actor.GameObject.AddComponent<DungeonEncounterMember>();

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            UnityCombatRulesBridge.Create(
                new[] { actor.Controller, opponent.Controller },
                CreateTiles()
            )
        );

        Assert.That(failure.Message, Does.Contain(nameof(DungeonEncounterMember)));
        Assert.That(failure.Message, Does.Contain("unconfigured"));
    }

    [Test]
    public void ConfiguredPaddedEncounterInstanceIdentityRejectsRatherThanNormalizes()
    {
        CreatureFixture actor = CreateCreatureWithoutDurableIdentity(
            "Padded Encounter",
            "Heroes",
            100
        );
        CreatureFixture opponent = CreateCreature("Padded Encounter Opponent", "Enemies", 0);
        DungeonEncounterMember identity = actor.GameObject.AddComponent<DungeonEncounterMember>();
        SetSerializedField(identity, "encounterId", "condition-test-encounter");
        SetSerializedField(identity, "instanceId", " padded-encounter-instance ");
        SetSerializedField(identity, "floorDepth", 0);
        SetSerializedField(identity, "creatureContentId", "condition-test-creature");
        SetSerializedField(identity, "isConfigured", true);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            UnityCombatRulesBridge.Create(
                new[] { actor.Controller, opponent.Controller },
                CreateTiles()
            )
        );

        Assert.That(failure.Message, Does.Contain("noncanonical"));
        Assert.That(failure.Message, Does.Contain(nameof(DungeonEncounterMember.InstanceId)));
        Assert.That(failure.InnerException, Is.TypeOf<ArgumentException>());
    }

    [Test]
    public void ConflictingIdentityComponentsRejectWhenPartyIdentityIsUnconfigured()
    {
        CreatureFixture actor = CreateCreatureWithoutDurableIdentity(
            "Conflicting Identity",
            "Heroes",
            100
        );
        CreatureFixture opponent = CreateCreature("Conflict Opponent", "Enemies", 0);
        actor.GameObject.AddComponent<DungeonPartyMemberIdentity>();
        actor
            .GameObject.AddComponent<DungeonEncounterMember>()
            .Configure(
                "condition-test-encounter",
                "condition-test-instance",
                0,
                "condition-test-creature",
                string.Empty
            );

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            UnityCombatRulesBridge.Create(
                new[] { actor.Controller, opponent.Controller },
                CreateTiles()
            )
        );

        Assert.That(failure.Message, Does.Contain(nameof(DungeonPartyMemberIdentity)));
        Assert.That(failure.Message, Does.Contain(nameof(DungeonEncounterMember)));
        Assert.That(failure.Message, Does.Contain("mutually exclusive"));
    }

    [Test]
    public void AbsentFiniteReinforcementConditionAdoptsOneNormalizedExpiredTombstone()
    {
        CreatureFixture initial = CreateCreature("Finite Initial", "Heroes", 100);
        CreatureFixture opponent = CreateCreature("Finite Opponent", "Enemies", 0);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new[] { initial.Controller, opponent.Controller },
            CreateTiles()
        );
        bridge.StartEncounter("Heroes");
        CreatureFixture reinforcement = CreateCreature("Finite Reinforcement", "Enemies", -1);
        ActiveEffectId effectId = new ActiveEffectId("finite-absent-effect");
        BindingId bindingId = new BindingId("finite-absent-binding");
        reinforcement.Conditions.RestoreApplications(
            new[]
            {
                new ConditionApplicationSnapshot(
                    effectId,
                    bindingId,
                    ConditionRuleDefinitions.Fatigued,
                    "historical-finite-source",
                    RuleSource.FromSlug("historical-finite-condition"),
                    EffectDuration.Rounds(2),
                    EffectStateVersion.Initial,
                    ConditionMarkerState.Instance,
                    ActiveEffectStatus.Active,
                    7,
                    true,
                    new ConditionTimingSnapshot(2, false)
                ),
            }
        );
        CountingFactObserver<ActiveEffectAdoptedFact> adopted = new();
        CountingFactObserver<ActiveEffectExpiredFact> expired = new();
        RuleDispatcher dispatcher = GetDispatcher(bridge);
        using IDisposable adoptedRegistration =
            dispatcher.RegisterFactObserver<ActiveEffectAdoptedFact>(adopted);
        using IDisposable expiredRegistration =
            dispatcher.RegisterFactObserver<ActiveEffectExpiredFact>(expired);

        bridge.RegisterCombatants(new[] { reinforcement.Controller });

        ActiveEffectInstance effect = bridge.Snapshot.ActiveEffects[effectId];
        ActiveRuleBinding binding = bridge.Snapshot.RuleBindings[bindingId];
        CreatureId reinforcementId = bridge.GetCreatureId(reinforcement.Creature);
        Assert.That(effect.Status, Is.EqualTo(ActiveEffectStatus.Expired));
        Assert.That(effect.EffectStateVersion, Is.EqualTo(EffectStateVersion.Initial.Next()));
        Assert.That(binding.IsEnabled, Is.False);
        Assert.That(bridge.Snapshot.ActiveEffectTimings.Contains(effectId), Is.False);
        Assert.That(effect.SourceCreature, Is.Not.EqualTo(reinforcementId));
        Assert.That(bridge.Snapshot.Creatures.Contains(effect.SourceCreature), Is.False);
        Assert.That(adopted.Count, Is.EqualTo(1));
        Assert.That(adopted.Last.Effect, Is.EqualTo(effect));
        Assert.That(adopted.Last.Binding, Is.EqualTo(binding));
        Assert.That(expired.Count, Is.Zero);
        bridge.ReleaseOwnership();
    }

    [Test]
    public void NewFiniteConditionRejectsReservedHistoricalSource()
    {
        AssertNewConditionRejectsReservedHistoricalSource(EffectDuration.Rounds(1), "finite");
    }

    [Test]
    public void NewIndefiniteConditionRejectsReservedHistoricalSource()
    {
        AssertNewConditionRejectsReservedHistoricalSource(EffectDuration.Indefinite, "indefinite");
    }

    private void AssertNewConditionRejectsReservedHistoricalSource(
        EffectDuration duration,
        string identity
    )
    {
        CreatureFixture target = CreateCreature($"{identity} Creation Target", "Heroes", 100);
        CreatureFixture opponent = CreateCreature($"{identity} Creation Opponent", "Enemies", 0);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new[] { target.Controller, opponent.Controller },
            CreateTiles()
        );
        bridge.StartEncounter("Heroes");
        CreatureId targetId = bridge.GetCreatureId(target.Creature);
        CreatureId reservedSource = DurableActorSourceIdentity.Reserve(
            $"historical-new-{identity}-condition-source"
        );
        long versionBefore = bridge.Snapshot.Version;
        int effectsBefore = bridge.Snapshot.ActiveEffects.Count;
        int bindingsBefore = bridge.Snapshot.RuleBindings.Count;
        int timingsBefore = bridge.Snapshot.ActiveEffectTimings.Count;

        OpResult<ConditionApplicationOutcome> result = bridge.Dispatch(
            new ApplyConditionOp(
                "fatigued",
                targetId,
                reservedSource,
                RuleSource.FromSlug($"new-{identity}-historical-source"),
                duration,
                ConditionMarkerState.Instance
            )
        );

        Assert.That(result, Is.TypeOf<InvalidOpResult<ConditionApplicationOutcome>>());
        Assert.That(
            ((InvalidOpResult<ConditionApplicationOutcome>)result).Reason,
            Is.EqualTo("A freshly applied condition requires a registered source creature.")
        );
        Assert.That(result.Facts, Is.Empty);
        Assert.That(bridge.Snapshot.Version, Is.EqualTo(versionBefore));
        Assert.That(bridge.Snapshot.ActiveEffects.Count, Is.EqualTo(effectsBefore));
        Assert.That(bridge.Snapshot.RuleBindings.Count, Is.EqualTo(bindingsBefore));
        Assert.That(bridge.Snapshot.ActiveEffectTimings.Count, Is.EqualTo(timingsBefore));
        bridge.ReleaseOwnership();
    }

    [Test]
    public void AbsentExpiredConditionIsIdempotentAcrossEnrollment()
    {
        CreatureFixture actor = CreateCreature("Expired Actor", "Heroes", 100);
        CreatureFixture opponent = CreateCreature("Expired Opponent", "Enemies", 0);
        ActiveEffectId effectId = new ActiveEffectId("already-expired-effect");
        actor.Conditions.RestoreApplications(
            new[]
            {
                new ConditionApplicationSnapshot(
                    effectId,
                    new BindingId("already-expired-binding"),
                    ConditionRuleDefinitions.Fatigued,
                    "historical-expired-source",
                    RuleSource.FromSlug("historical-expired-condition"),
                    EffectDuration.Rounds(1),
                    new EffectStateVersion(5),
                    ConditionMarkerState.Instance,
                    ActiveEffectStatus.Expired,
                    5,
                    false,
                    null
                ),
            }
        );
        UnityCombatRulesBridge first = UnityCombatRulesBridge.Create(
            new[] { actor.Controller, opponent.Controller },
            CreateTiles()
        );

        Assert.That(
            first.Snapshot.ActiveEffects[effectId].EffectStateVersion,
            Is.EqualTo(new EffectStateVersion(5))
        );
        Assert.That(first.Snapshot.ActiveEffectTimings.Contains(effectId), Is.False);
        first.ReleaseOwnership();
        UnityCombatRulesBridge second = UnityCombatRulesBridge.Create(
            new[] { opponent.Controller, actor.Controller },
            CreateTiles()
        );

        Assert.That(
            second.Snapshot.ActiveEffects[effectId].EffectStateVersion,
            Is.EqualTo(new EffectStateVersion(5))
        );
        Assert.That(
            second.Snapshot.RuleBindings[new BindingId("already-expired-binding")].IsEnabled,
            Is.False
        );
        Assert.That(second.Snapshot.ActiveEffectTimings.Contains(effectId), Is.False);
        second.ReleaseOwnership();
    }

    [Test]
    public void AdversarialAbsentDurableIdsRoundTripWithoutAliasing()
    {
        string[] durableIds =
        {
            "source/A+B_C-dash",
            "源/actor|one",
            "CaseSensitive",
            "casesensitive",
            "é",
            "é",
        };
        CreatureFixture actor = CreateCreature("Adversarial Actor", "Heroes", 100);
        CreatureFixture opponent = CreateCreature("Adversarial Opponent", "Enemies", 0);
        actor.Conditions.RestoreApplications(
            durableIds
                .Select(
                    (durableId, index) =>
                        new ConditionApplicationSnapshot(
                            new ActiveEffectId($"adversarial-effect-{index}"),
                            new BindingId($"adversarial-binding-{index}"),
                            ConditionRuleDefinitions.Fatigued,
                            durableId,
                            RuleSource.FromSlug($"adversarial-source-{index}"),
                            EffectDuration.Indefinite,
                            EffectStateVersion.Initial,
                            ConditionMarkerState.Instance,
                            ActiveEffectStatus.Active,
                            index,
                            true,
                            null
                        )
                )
                .ToArray()
        );
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new[] { actor.Controller, opponent.Controller },
            CreateTiles()
        );
        CreatureId owner = bridge.GetCreatureId(actor.Creature);
        CreatureId[] reserved = actor
            .Conditions.CaptureApplications()
            .Select(application =>
                bridge.Snapshot.ActiveEffects[application.EffectId].SourceCreature
            )
            .ToArray();

        Assert.That(reserved.Distinct().Count(), Is.EqualTo(durableIds.Length));
        Assert.That(reserved, Has.None.EqualTo(owner));
        Assert.That(reserved.All(source => !bridge.Snapshot.Creatures.Contains(source)), Is.True);
        bridge.ReleaseOwnership();
        Assert.That(
            actor.Conditions.CaptureApplications().Select(application => application.SourceActorId),
            Is.EqualTo(durableIds)
        );
    }

    [Test]
    public void DestroyedConditionSourceRoundTripsThroughReservedProvenance()
    {
        CreatureFixture source = CreateCreature("Destroyed Source", "Heroes", 100);
        CreatureFixture target = CreateCreature("Destroyed Target", "Enemies", 0);
        UnityCombatRulesBridge first = UnityCombatRulesBridge.Create(
            new[] { source.Controller, target.Controller },
            CreateTiles()
        );
        CreatureId sourceId = first.GetCreatureId(source.Creature);
        CreatureId targetId = first.GetCreatureId(target.Creature);
        ResolvedOpResult<ConditionApplicationOutcome> created =
            (ResolvedOpResult<ConditionApplicationOutcome>)
                first.Dispatch(
                    new ApplyConditionOp(
                        "fatigued",
                        targetId,
                        sourceId,
                        RuleSource.FromSlug("destroyed-source-condition"),
                        EffectDuration.Indefinite,
                        ConditionMarkerState.Instance
                    )
                );
        string durableSource = source.DurableActorId;
        first.ReleaseOwnership();
        Object.DestroyImmediate(source.GameObject);
        CreatureFixture replacement = CreateCreature("Destroyed Source Replacement", "Heroes", 50);
        UnityCombatRulesBridge second = UnityCombatRulesBridge.Create(
            new[] { replacement.Controller, target.Controller },
            CreateTiles()
        );
        CreatureId reserved = second.Snapshot.ActiveEffects[created.Value.EffectId].SourceCreature;

        Assert.That(reserved, Is.Not.EqualTo(second.GetCreatureId(target.Creature)));
        Assert.That(second.Snapshot.Creatures.Contains(reserved), Is.False);
        second.ReleaseOwnership();
        Assert.That(
            target.Conditions.CaptureApplications().Single().SourceActorId,
            Is.EqualTo(durableSource)
        );
    }

    [Test]
    public void AtomicJoinAdoptionObserverFailureRetriesWithoutDuplicateStateOrFacts()
    {
        CreatureFixture initial = CreateCreature("Adoption Retry Initial", "Heroes", 100);
        CreatureFixture opponent = CreateCreature("Adoption Retry Opponent", "Enemies", 0);
        ScriptedRollService rolls = new ScriptedRollService(20, 10, 1);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new[] { initial.Controller, opponent.Controller },
            CreateTiles(),
            rolls
        );
        bridge.StartEncounter("Heroes");
        CreatureFixture reinforcement = CreateCreature(
            "Adoption Retry Reinforcement",
            "Enemies",
            -1
        );
        reinforcement.Conditions.RestoreApplications(
            new[]
            {
                Persisted(
                    reinforcement.GameObject,
                    ConditionRuleDefinitions.Fatigued,
                    "condition-post-commit-retry",
                    ConditionMarkerState.Instance
                ),
            }
        );
        InvalidOperationException expected = new InvalidOperationException(
            "Injected condition adoption observer failure."
        );
        ThrowOnceFactObserver<ActiveEffectAdoptedFact> observer = new(expected);
        using IDisposable registration = GetDispatcher(bridge)
            .RegisterFactObserver<ActiveEffectAdoptedFact>(observer);

        InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() =>
            bridge.RegisterCombatants(new[] { reinforcement.Controller })
        );

        Assert.That(actual, Is.SameAs(expected));
        Assert.That(observer.Count, Is.EqualTo(1));
        Assert.That(reinforcement.Conditions.HasPendingRestore, Is.True);
        Assert.That(rolls.Remaining, Is.Zero);
        long failedVersion = bridge.Snapshot.Version;

        Assert.DoesNotThrow(() => bridge.RegisterCombatants(new[] { reinforcement.Controller }));

        ActiveEffectId effect = new ActiveEffectId("effect-condition-post-commit-retry");
        Assert.That(observer.Count, Is.EqualTo(1));
        Assert.That(bridge.Snapshot.ActiveEffects.Count(pair => pair.Key == effect), Is.EqualTo(1));
        Assert.That(
            bridge.Snapshot.ActiveEffects[effect].SourceCreature,
            Is.EqualTo(bridge.GetCreatureId(reinforcement.Creature))
        );
        Assert.That(
            reinforcement.Conditions.CaptureApplications().Single().SourceActorId,
            Is.EqualTo(reinforcement.DurableActorId)
        );
        // Join adoption already committed; retry adds only the pending Strike contribution.
        Assert.That(bridge.Snapshot.Version, Is.EqualTo(failedVersion + 1));
        Assert.That(reinforcement.Conditions.HasPendingRestore, Is.False);
        Assert.That(rolls.Remaining, Is.Zero);
        bridge.ReleaseOwnership();
    }

    [Test]
    public void ReleaseProjectsDetachedAuthorityAndNextEnrollmentConsumesExactMutation()
    {
        CreatureFixture actor = CreateCreature("Actor", "Heroes", 100);
        CreatureFixture opponent = CreateCreature("Opponent", "Enemies", 0);
        actor.Conditions.RestoreApplications(
            new[]
            {
                Persisted(
                    actor.GameObject,
                    ConditionRuleDefinitions.Slowed,
                    "restored-slowed",
                    new SlowedConditionState(1)
                ),
            }
        );
        UnityCombatRulesBridge first = UnityCombatRulesBridge.Create(
            new[] { actor.Controller, opponent.Controller },
            CreateTiles()
        );
        CreatureId actorId = first.GetCreatureId(actor.Creature);
        Assert.That(
            ConditionSelectors.TryGetSlowed(first.Snapshot, actorId, out var slowed),
            Is.True
        );
        Assert.That(
            first.Dispatch(
                new CleanupConditionsFromSourceOp(
                    slowed.Source,
                    ConditionCleanupKind.Expire,
                    actorId,
                    ConditionRuleDefinitions.Slowed
                )
            ),
            Is.TypeOf<ResolvedOpResult<ConditionCleanupOutcome>>()
        );
        ConditionApplicationSnapshot[] live = actor.Conditions.CaptureApplications().ToArray();

        first.ReleaseOwnership();

        Assert.That(actor.Conditions.ActiveConditionNames, Is.Empty);
        ConditionApplicationSnapshot detached = actor.Conditions.CaptureApplications().Single();
        Assert.That(detached.EffectId, Is.EqualTo(live[0].EffectId));
        Assert.That(detached.BindingId, Is.EqualTo(live[0].BindingId));
        Assert.That(detached.Status, Is.EqualTo(live[0].Status));
        Assert.That(detached.Version, Is.EqualTo(live[0].Version));
        Assert.That(actor.Conditions.HasPendingRestore, Is.True);
        UnityCombatRulesBridge second = UnityCombatRulesBridge.Create(
            new[] { actor.Controller, opponent.Controller },
            CreateTiles()
        );
        Assert.That(actor.Conditions.HasPendingRestore, Is.False);
        CreatureId reenrolledId = second.GetCreatureId(actor.Creature);
        Assert.That(
            ConditionSelectors.TryGetSlowed(second.Snapshot, reenrolledId, out _),
            Is.False
        );
        ActiveEffectInstance restored = second.Snapshot.ActiveEffects[slowed.EffectId];
        Assert.That(restored.Status, Is.EqualTo(ActiveEffectStatus.Expired));
        Assert.That(restored.EffectStateVersion.Value, Is.EqualTo(1));
        second.ReleaseOwnership();
    }

    [Test]
    public void ReleaseProjectsAuthoritativeEmptySetAndNextEnrollmentConsumesIt()
    {
        CreatureFixture actor = CreateCreature("Empty Actor", "Heroes", 100);
        CreatureFixture opponent = CreateCreature("Empty Opponent", "Enemies", 0);
        UnityCombatRulesBridge first = UnityCombatRulesBridge.Create(
            new[] { actor.Controller, opponent.Controller },
            CreateTiles()
        );

        first.ReleaseOwnership();

        Assert.That(actor.Conditions.HasPendingRestore, Is.True);
        Assert.That(actor.Conditions.CaptureApplications(), Is.Empty);
        UnityCombatRulesBridge exploration = UnityCombatRulesBridge.CreateExplorationStride(
            actor.Controller,
            CreateTiles()
        );
        exploration.ReleaseOwnership();
        Assert.That(actor.Conditions.HasPendingRestore, Is.True);
        Assert.That(actor.Conditions.CaptureApplications(), Is.Empty);
        UnityCombatRulesBridge second = UnityCombatRulesBridge.Create(
            new[] { actor.Controller, opponent.Controller },
            CreateTiles()
        );
        Assert.That(actor.Conditions.HasPendingRestore, Is.False);
        Assert.That(actor.Conditions.CaptureApplications(), Is.Empty);
        second.ReleaseOwnership();
    }

    [Test]
    public void IndefiniteConditionSurvivesReleaseAndFailedNextEnrollment()
    {
        CreatureFixture actor = CreateCreature("Detached Actor", "Heroes", 100);
        CreatureFixture opponent = CreateCreature("Detached Opponent", "Enemies", 0);
        actor.Conditions.RestoreApplications(
            new[]
            {
                Persisted(
                    actor.GameObject,
                    ConditionRuleDefinitions.Fatigued,
                    "detached-fatigued",
                    ConditionMarkerState.Instance
                ),
            }
        );
        UnityCombatRulesBridge first = UnityCombatRulesBridge.Create(
            new[] { actor.Controller, opponent.Controller },
            CreateTiles()
        );
        first.ReleaseOwnership();
        Assert.That(actor.Conditions.HasPendingRestore, Is.True);
        UnityCombatRulesBridge exploration = UnityCombatRulesBridge.CreateExplorationStride(
            actor.Controller,
            CreateTiles()
        );
        CreatureId explorationId = exploration.GetCreatureId(actor.Creature);
        Assert.That(
            ConditionSelectors.HasMarker(
                exploration.Snapshot,
                explorationId,
                ConditionRuleDefinitions.Fatigued
            ),
            Is.False
        );
        exploration.ReleaseOwnership();
        Assert.That(actor.Conditions.HasPendingRestore, Is.True);
        Assert.That(actor.Conditions.CaptureApplications(), Has.Count.EqualTo(1));

        ControllableInstallationModule installer = new ControllableInstallationModule
        {
            TargetName = actor.GameObject.name,
            FailuresRemaining = 1,
        };
        Assert.Throws<InvalidOperationException>(() =>
            UnityCombatRulesBridge.CreateForTests(
                new[] { actor.Controller, opponent.Controller },
                CreateTiles(),
                new RandomRollService(),
                new IUnityEncounterModule[] { installer }
            )
        );
        Assert.That(actor.Conditions.HasPendingRestore, Is.True);
        Assert.That(actor.Conditions.CaptureApplications(), Has.Count.EqualTo(1));

        UnityCombatRulesBridge retry = UnityCombatRulesBridge.CreateForTests(
            new[] { actor.Controller, opponent.Controller },
            CreateTiles(),
            new RandomRollService(),
            new IUnityEncounterModule[] { installer }
        );
        CreatureId actorId = retry.GetCreatureId(actor.Creature);
        Assert.That(actor.Conditions.HasPendingRestore, Is.False);
        Assert.That(
            ConditionSelectors.HasMarker(
                retry.Snapshot,
                actorId,
                ConditionRuleDefinitions.Fatigued
            ),
            Is.True
        );
        retry.ReleaseOwnership();
    }

    private static PreparedSpell PrepareHauntingHymn(CreatureFixture caster)
    {
        return PrepareCantrip(caster, "Haunting Hymn", "haunting-hymn");
    }

    private static PreparedSpell PrepareDivineLance(CreatureFixture caster)
    {
        return PrepareCantrip(caster, "Divine Lance", "divine-lance");
    }

    private static PreparedSpell PrepareCantrip(CreatureFixture caster, string name, string slug)
    {
        SpellReference reference = new(new SpellId(slug), 1);
        PreparedSpell spell = new(name, 1, true, false, string.Empty, new[] { 2u });
        caster.Creature.level = 1;
        caster.Creature.wisMod = 4;
        caster.Creature.Build = new CharacterBuild { ClassName = "Cleric" };
        caster.Creature.Prepared = Pf2eCharacterPreparer.Prepare(
            caster.Creature,
            caster.Creature.Build
        );
        Assert.That(caster.Creature.Prepared.SpellBook.CastableSpells, Does.Contain(reference));
        Assert.That(
            caster.Creature.Prepared.Spellcasting.PreparedSpells.Any(value => value.Slug == slug),
            Is.False
        );
        return spell;
    }

    private static string CreateCatalogEffectSpellJson(
        string castingTime,
        string duration,
        bool includeCastingTime = true,
        bool includeDuration = true,
        JToken maximumActiveInstances = null
    )
    {
        JObject activeEffectRule = new()
        {
            ["key"] = "CreateActiveEffect",
            ["definition"] = "spell-effect-catalog-audit",
            ["target"] = "self",
        };
        if (maximumActiveInstances != null)
            activeEffectRule["maximumActiveInstances"] = maximumActiveInstances;
        JObject system = new()
        {
            ["rulesNativeReady"] = true,
            ["level"] = new JObject { ["value"] = 1 },
            ["traits"] = new JObject { ["value"] = new JArray() },
            ["rules"] = new JArray { activeEffectRule },
        };
        if (includeCastingTime)
            system["time"] = new JObject { ["value"] = castingTime };
        if (includeDuration)
            system["duration"] = new JObject { ["value"] = duration };
        return new JObject { ["name"] = "Catalog Audit Spell", ["system"] = system }.ToString();
    }

    private static AreaTargetResult PrepareHauntingArea(
        CreatureFixture caster,
        CreatureFixture target,
        out Tile[,] tiles
    )
    {
        tiles = new Tile[1, 4];
        for (int z = 0; z < tiles.GetLength(1); z++)
            tiles[0, z] = new Tile();
        caster.GameObject.transform.position = Vector3Int.zero;
        target.GameObject.transform.position = new Vector3Int(0, 0, 2);
        tiles[0, 0].Occupants.Add(caster.GameObject);
        tiles[0, 2].Occupants.Add(target.GameObject);
        AreaTargetResult result = AreaTargeting.Evaluate(
            caster.GameObject,
            tiles,
            new AreaTargetRequest
            {
                Shape = AreaShape.Cone,
                SizeFeet = 15,
                RequiresLineOfEffect = true,
            },
            new AreaPlacement
            {
                Shape = AreaShape.Cone,
                OriginCell = Vector3Int.zero,
                OriginCorner = Vector2Int.zero,
                Direction = AreaDirection.North,
            }
        );
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Creatures.Count(value => value.IsAffected), Is.EqualTo(1));
        return result;
    }

    private static ConditionApplicationSnapshot Persisted(
        GameObject sourceCreature,
        RuleDefinitionId definition,
        string identity,
        IEffectState state,
        string sourceActorId = null
    )
    {
        RuleSource source = RuleSource.FromSlug(identity);
        return new ConditionApplicationSnapshot(
            new ActiveEffectId($"effect-{identity}"),
            new BindingId($"binding-{identity}"),
            definition,
            sourceActorId
                ?? sourceCreature.GetComponent<DungeonPartyMemberIdentity>()?.RosterSlotId
                ?? string.Empty,
            source,
            EffectDuration.Indefinite,
            EffectStateVersion.Initial,
            state,
            ActiveEffectStatus.Active,
            1,
            true,
            null
        );
    }

    private static ConditionApplicationSnapshot RestoredCondition(
        CreatureFixture source,
        ActiveEffectId effectId,
        BindingId bindingId,
        long creationOrder,
        bool timed
    ) =>
        new ConditionApplicationSnapshot(
            effectId,
            bindingId,
            ConditionRuleDefinitions.Fatigued,
            source.DurableActorId,
            RuleSource.FromSlug($"restored-{effectId.Value}"),
            timed ? EffectDuration.Rounds(2) : EffectDuration.Indefinite,
            EffectStateVersion.Initial,
            ConditionMarkerState.Instance,
            ActiveEffectStatus.Active,
            creationOrder,
            true,
            timed ? new ConditionTimingSnapshot(2, false) : null
        );

    private CreatureFixture CreateCreature(string name, string teamName, int initiative)
    {
        string durableActorId = $"condition-actor-{created.Count + 1}";
        return CreateCreatureWithDurableId(name, teamName, initiative, durableActorId);
    }

    private CreatureFixture CreateCreatureFromJson(string path, string teamName, int initiative)
    {
        GameObject gameObject = CreatureJsonConverter.CreateFromFile(path);
        Assert.That(gameObject, Is.Not.Null);
        created.Add(gameObject);
        string durableActorId = $"condition-json-actor-{created.Count}";
        gameObject
            .AddComponent<DungeonPartyMemberIdentity>()
            .Configure(durableActorId, "condition-json-test-creature");
        CreatureComponent creature = gameObject.GetComponent<CreatureComponent>();
        creature.initiative = initiative;
        Conditions conditions = gameObject.AddComponent<Conditions>();
        Team team = gameObject.AddComponent<Team>();
        team.Name = teamName;
        ConditionTestActionController controller =
            gameObject.AddComponent<ConditionTestActionController>();
        return new CreatureFixture(gameObject, creature, conditions, controller, durableActorId);
    }

    private CreatureFixture CreateCreatureWithDurableId(
        string name,
        string teamName,
        int initiative,
        string durableActorId
    )
    {
        return CreateCreatureFixture(name, teamName, initiative, durableActorId, true);
    }

    private CreatureFixture CreateCreatureWithoutDurableIdentity(
        string name,
        string teamName,
        int initiative
    )
    {
        return CreateCreatureFixture(name, teamName, initiative, string.Empty, false);
    }

    private CreatureFixture CreateCreatureFixture(
        string name,
        string teamName,
        int initiative,
        string durableActorId,
        bool configurePartyIdentity
    )
    {
        GameObject gameObject = new GameObject(name);
        created.Add(gameObject);
        if (configurePartyIdentity)
        {
            gameObject
                .AddComponent<DungeonPartyMemberIdentity>()
                .Configure(durableActorId, "condition-test-creature");
        }
        CreatureComponent creature = gameObject.AddComponent<CreatureComponent>();
        creature.initiative = initiative;
        creature.InitializeHealthBeforeEncounter(20, 20);
        Conditions conditions = gameObject.AddComponent<Conditions>();
        Team team = gameObject.AddComponent<Team>();
        team.Name = teamName;
        ConditionTestActionController controller =
            gameObject.AddComponent<ConditionTestActionController>();
        return new CreatureFixture(gameObject, creature, conditions, controller, durableActorId);
    }

    private static void SetSerializedField<TComponent>(
        TComponent component,
        string fieldName,
        object value
    )
        where TComponent : Component
    {
        var field = typeof(TComponent).GetField(
            fieldName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null);
        field.SetValue(component, value);
    }

    private static void AssertEncounterSetupRolls(EncounterThenSpellRollService rolls) =>
        Assert.That(
            rolls.EncounterRequests,
            Is.EqualTo(new[] { DiceExpressions.D20, DiceExpressions.D20 })
        );

    private static void AssertSingleHauntingHymnResolution(
        CastSpellResult result,
        EncounterThenSpellRollService rolls,
        ScriptedRollService spellRolls
    )
    {
        Assert.That(result.Targets, Has.Count.EqualTo(1));
        Assert.That(result.Rolls, Has.Count.EqualTo(1));
        Assert.That(
            rolls.SpellRequests,
            Is.EqualTo(new[] { DiceExpressions.D20, new DiceExpression(1, 8) })
        );
        Assert.That(spellRolls.Remaining, Is.Zero);
    }

    private static Tile[,] CreateTiles() =>
        new[,]
        {
            { new Tile() },
        };

    private static RuleDispatcher GetDispatcher(UnityCombatRulesBridge bridge)
    {
        var field = typeof(UnityCombatRulesBridge).GetField(
            "dispatcher",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null);
        return (RuleDispatcher)field.GetValue(bridge);
    }

    private sealed class EncounterThenSpellRollService : IRollService
    {
        private readonly ScriptedRollService spellRolls;
        private readonly List<DiceExpression> encounterRequests = new();
        private readonly List<DiceExpression> spellRequests = new();
        private bool resolvingSpell;

        internal EncounterThenSpellRollService(ScriptedRollService spellRolls) =>
            this.spellRolls = spellRolls ?? throw new ArgumentNullException(nameof(spellRolls));

        internal IReadOnlyList<DiceExpression> EncounterRequests => encounterRequests;
        internal IReadOnlyList<DiceExpression> SpellRequests => spellRequests;

        internal void BeginSpellResolution()
        {
            if (resolvingSpell)
                throw new InvalidOperationException("Spell resolution already began.");
            resolvingSpell = true;
        }

        public RollResult Roll(DiceExpression dice)
        {
            if (resolvingSpell)
            {
                spellRequests.Add(dice);
                return spellRolls.Roll(dice);
            }

            encounterRequests.Add(dice);
            return new RollResult(dice, Enumerable.Repeat(1, dice.Count));
        }
    }

    private sealed class CapturingCastObserver
        : IResolvedOpObserver<CastSpellActionOp, CastSpellOutcome>
    {
        internal List<CastSpellOutcome> Outcomes { get; } = new();

        public ValueTask OnOperationResolved(
            CastSpellActionOp operation,
            CastSpellOutcome result,
            RulesSnapshot currentSnapshot
        )
        {
            Outcomes.Add(result);
            return default;
        }
    }

    private sealed class ControllableInstallationModule : IUnityCombatantEnrollmentModule
    {
        internal string TargetName { get; set; } = string.Empty;
        internal int FailuresRemaining { get; set; }

        public void PrepareCombatant(UnityCombatantEnrollmentBuilder builder)
        {
            bool targeted = builder.Controller.gameObject.name == TargetName;
            builder.AddInstallation(new ControllableInstallation(this, targeted));
        }

        private sealed class ControllableInstallation : IUnityCombatantInstallationContribution
        {
            private readonly ControllableInstallationModule owner;
            private readonly bool targeted;

            internal ControllableInstallation(ControllableInstallationModule owner, bool targeted)
            {
                this.owner = owner;
                this.targeted = targeted;
            }

            public void Reconcile()
            {
                if (!targeted || owner.FailuresRemaining == 0)
                    return;
                owner.FailuresRemaining--;
                throw new InvalidOperationException("Injected late installation failure.");
            }
        }
    }

    private sealed class CountingFactObserver<TFact> : IFactObserver<TFact>
        where TFact : RuleFact
    {
        private readonly List<long> versions = new();

        internal int Count { get; private set; }
        internal TFact Last { get; private set; }
        internal IReadOnlyList<long> Versions => versions;

        public ValueTask OnFactCommitted(TFact fact, RulesSnapshot currentSnapshot)
        {
            Count++;
            Last = fact;
            versions.Add(currentSnapshot.Version);
            return default;
        }
    }

    private sealed class ThrowOnceFactObserver<TFact> : IFactObserver<TFact>
        where TFact : RuleFact
    {
        private readonly Exception failure;

        internal ThrowOnceFactObserver(Exception failure) => this.failure = failure;

        internal int Count { get; private set; }

        public ValueTask OnFactCommitted(TFact fact, RulesSnapshot currentSnapshot)
        {
            Count++;
            if (Count == 1)
                throw failure;
            return default;
        }
    }

    private sealed class ConditionTestActionController : ActionController
    {
        public override void EndTurn() { }
    }

    private sealed class CreatureFixture
    {
        internal CreatureFixture(
            GameObject gameObject,
            CreatureComponent creature,
            Conditions conditions,
            ConditionTestActionController controller,
            string durableActorId
        )
        {
            GameObject = gameObject;
            Creature = creature;
            Conditions = conditions;
            Controller = controller;
            DurableActorId = durableActorId;
        }

        internal GameObject GameObject { get; }
        internal CreatureComponent Creature { get; }
        internal Conditions Conditions { get; }
        internal ConditionTestActionController Controller { get; }
        internal string DurableActorId { get; }
    }
}
