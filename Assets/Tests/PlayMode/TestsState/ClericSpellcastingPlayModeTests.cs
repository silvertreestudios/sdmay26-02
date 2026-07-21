using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game.Combat.Spells;
using Game.Creature;
using Game.Creature.Rules;
using Game.KayKit;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

public class ClericSpellcastingPlayModeTests : PlayModeBase
{
    private GameObject clericObject;
    private GameObject spellTargetObject;
    private ActionController clericController;

    [TearDown]
    public void CleanupCleric()
    {
        if (clericController != null)
        {
            CombatManagerInterface manager = Object.FindFirstObjectByType<CombatManagerInterface>();
            manager?.Remove(clericController);
            clericController = null;
        }

        if (clericObject != null)
            Object.Destroy(clericObject);
        if (spellTargetObject != null)
            Object.Destroy(spellTargetObject);

        Pf2eItemCatalog.ResetForTests();
    }

    [UnityTest]
    public IEnumerator ClericWithPlayerControllerGetsSpellActionsAndLightSpendsActions()
    {
        clericObject = new GameObject("PlayMode Cleric");
        CreatureComponent cleric = clericObject.AddComponent<CreatureComponent>();
        cleric.level = 1;
        cleric.wisMod = 4;
        cleric.Build = new CharacterBuild { ClassName = "Cleric" };
        cleric.Prepared = Pf2eCharacterPreparer.Prepare(cleric, cleric.Build);
        clericObject.AddComponent<Team>().Name = "players";
        PlayerActionController controller = clericObject.AddComponent<PlayerActionController>();
        clericController = controller;

        yield return null;

        Assert.That(controller.GetActions().Any(action => action.ActionName == "Light"), Is.True);
        Assert.That(controller.GetActions().Any(action => action.ActionName == "Shield"), Is.True);

        controller.StartTurn();
        EntityAction light = controller.GetActions().First(action => action.ActionName == "Light");
        controller.TakeAction(light);
        yield return null;

        Assert.That(controller.ActionPoints, Is.EqualTo(2));
        Assert.That(controller.IsTakingAction, Is.False);
    }

    [UnityTest]
    public IEnumerator HealthOnlyCompositionAllowsCantripPreparedAndFontCasts()
    {
        clericObject = new GameObject("Health-Only PlayMode Cleric");
        CreatureComponent cleric = clericObject.AddComponent<CreatureComponent>();
        cleric.InitializeHealthBeforeEncounter(10, 10);
        cleric.level = 1;
        cleric.wisMod = 4;
        cleric.Build = new CharacterBuild { ClassName = "Cleric" };
        cleric.Prepared = Pf2eCharacterPreparer.Prepare(cleric, cleric.Build);
        clericObject.AddComponent<Conditions>();
        clericObject.AddComponent<Team>().Name = "players";
        PlayerActionController controller = clericObject.AddComponent<PlayerActionController>();
        clericController = controller;

        spellTargetObject = new GameObject("Health-Only PlayMode Ally");
        CreatureComponent ally = spellTargetObject.AddComponent<CreatureComponent>();
        ally.InitializeHealthBeforeEncounter(3, 20);
        spellTargetObject.AddComponent<Conditions>();
        Game.Rules.Unity.UnityEncounterRulesBridge.CreateHealthTestComposition(
            new[] { cleric, ally }
        );
        UnityEngine.Random.State randomState = UnityEngine.Random.state;
        UnityEngine.Random.InitState(12);

        try
        {
            yield return null;

            controller.StartTurn();
            CoroutineResult<CastSpellResult> cantrip = new();
            yield return CoroutineRunner.Await(
                SpellcastingRuntime.CastAsync(
                    clericObject,
                    cleric.Prepared.Spellcasting.GetSpell("shield"),
                    1
                ),
                cantrip
            );
            Assert.That(cantrip.Value.Success, Is.True);
            Assert.That(controller.ActionPoints, Is.EqualTo(2));
            Assert.That(controller.IsTakingAction, Is.False);

            controller.StartTurn();
            CoroutineResult<CastSpellResult> prepared = new();
            yield return CoroutineRunner.Await(
                SpellcastingRuntime.CastAsync(
                    clericObject,
                    cleric.Prepared.Spellcasting.GetSpell("bless"),
                    2,
                    new[] { spellTargetObject }
                ),
                prepared
            );
            Assert.That(prepared.Value.Success, Is.True);
            Assert.That(controller.ActionPoints, Is.EqualTo(1));
            Assert.That(cleric.Prepared.Spellcasting.Pools["rank-1-bless"].UsesRemaining, Is.Zero);
            Assert.That(controller.IsTakingAction, Is.False);

            controller.StartTurn();
            CoroutineResult<CastSpellResult> font = new();
            yield return CoroutineRunner.Await(
                SpellcastingRuntime.CastAsync(
                    clericObject,
                    cleric.Prepared.Spellcasting.GetSpell("heal"),
                    2,
                    new[] { spellTargetObject }
                ),
                font
            );
            Assert.That(font.Value.Success, Is.True);
            Assert.That(controller.ActionPoints, Is.EqualTo(1));
            Assert.That(
                cleric.Prepared.Spellcasting.Pools["font-heal"].UsesRemaining,
                Is.EqualTo(3)
            );
            Assert.That(ally.Health.Current, Is.GreaterThan(3));
            Assert.That(controller.IsTakingAction, Is.False);
        }
        finally
        {
            UnityEngine.Random.state = randomState;
        }
    }

    [UnityTest]
    public IEnumerator SpellAnimationWaitsForSelectionAndOnlyPlaysAfterSuccessfulCast()
    {
        clericObject = new GameObject("Animated PlayMode Cleric");
        CreatureComponent cleric = clericObject.AddComponent<CreatureComponent>();
        cleric.level = 1;
        cleric.wisMod = 4;
        cleric.Build = new CharacterBuild { ClassName = "Cleric" };
        cleric.Prepared = Pf2eCharacterPreparer.Prepare(cleric, cleric.Build);
        clericObject.AddComponent<Team>().Name = "players";
        PlayerActionController controller = clericObject.AddComponent<PlayerActionController>();
        clericController = controller;

        GameObject visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/KayKit/Prefabs/Animated/MageStaffAnimated.prefab"
        );
        GameObject visual = Object.Instantiate(visualPrefab, clericObject.transform);
        CreatureAnimationController animationController =
            visual.GetComponent<CreatureAnimationController>();
        CreaturePresentation presentation = clericObject.AddComponent<CreaturePresentation>();
        presentation.Bind(animationController, visual.GetComponent<CreatureEquipmentVisuals>());
        yield return null;

        PreparedSpell light = cleric.Prepared.Spellcasting.PreparedSpells.First(spell =>
            spell.Slug == "light"
        );
        controller.StartTurn();
        ConfirmableSpellDefinition cancelledDefinition = new(shouldCast: false);
        controller.TakeAction(new CastSpellAction(light, 1, cancelledDefinition));
        yield return null;

        Assert.That(cancelledDefinition.SelectionStarted, Is.True);
        Assert.That(
            animationController.CurrentClipId,
            Is.Null,
            "Opening targeting must not start the cast animation."
        );

        cancelledDefinition.CompleteSelection();
        yield return null;
        yield return null;

        Assert.That(
            animationController.CurrentClipId,
            Is.Null,
            "Cancelling targeting must not show a cast animation."
        );
        Assert.That(controller.IsTakingAction, Is.False);

        controller.StartTurn();
        ConfirmableSpellDefinition successfulDefinition = new(shouldCast: true);
        controller.TakeAction(new CastSpellAction(light, 1, successfulDefinition));
        yield return null;

        Assert.That(successfulDefinition.SelectionStarted, Is.True);
        Assert.That(animationController.CurrentClipId, Is.Null);

        successfulDefinition.CompleteSelection();
        float deadline = Time.realtimeSinceStartup + 1.0f;
        while (animationController.CurrentClipId == null && Time.realtimeSinceStartup < deadline)
            yield return null;

        Assert.That(
            animationController.CurrentClipId,
            Is.EqualTo("animation/combatranged/ranged_magic_shoot")
        );
        Assert.That(controller.IsTakingAction, Is.False);
    }

    [UnityTest]
    public IEnumerator SuccessfulSelfDefeatingSpellKeepsDeathAnimationUntilDeactivation()
    {
        clericObject = new GameObject("Self-Defeating Animated Caster");
        CreatureComponent cleric = clericObject.AddComponent<CreatureComponent>();
        cleric.level = 1;
        cleric.InitializeHealthBeforeEncounter(1, 1);
        cleric.wisMod = 4;
        cleric.Build = new CharacterBuild { ClassName = "Cleric" };
        cleric.Prepared = Pf2eCharacterPreparer.Prepare(cleric, cleric.Build);
        clericObject.AddComponent<Team>().Name = "players";
        PlayerActionController controller = clericObject.AddComponent<PlayerActionController>();
        Game.Rules.Unity.UnityEncounterRulesBridge.CreateHealthTestComposition(new[] { cleric });
        clericController = controller;

        GameObject visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/KayKit/Prefabs/Animated/MageStaffAnimated.prefab"
        );
        GameObject visual = Object.Instantiate(visualPrefab, clericObject.transform);
        CreatureAnimationController animationController =
            visual.GetComponent<CreatureAnimationController>();
        CreaturePresentation presentation = clericObject.AddComponent<CreaturePresentation>();
        presentation.Bind(animationController, visual.GetComponent<CreatureEquipmentVisuals>());
        yield return null;

        PreparedSpell light = cleric.Prepared.Spellcasting.PreparedSpells.First(spell =>
            spell.Slug == "light"
        );
        controller.StartTurn();
        SpellCastContext context = new(
            clericObject,
            light,
            1,
            spendActions: true,
            new SelfDefeatingSpellDefinition()
        );

        CoroutineResult<CastSpellResult> completed = new CoroutineResult<CastSpellResult>();
        yield return CoroutineRunner.Await(context.CastAsync(SpellTargetSelection.None), completed);
        CastSpellResult result = completed.Value;

        Assert.That(result.Success, Is.True);
        Assert.That(
            animationController.IsDeathPlaying,
            Is.True,
            "A successful cast must not replace a death animation started by its own spell effects."
        );

        float deadline = Time.realtimeSinceStartup + 6.0f;
        while (clericObject.activeSelf && Time.realtimeSinceStartup < deadline)
            yield return null;

        Assert.That(
            clericObject.activeSelf,
            Is.False,
            "The death animation completion callback must still deactivate the defeated caster."
        );
    }

    private sealed class ConfirmableSpellDefinition : ISpellDefinition
    {
        private readonly bool shouldCast;
        private bool selectionComplete;

        public ConfirmableSpellDefinition(bool shouldCast)
        {
            this.shouldCast = shouldCast;
        }

        public string Slug => "light";
        public bool SelectionStarted { get; private set; }

        public IReadOnlyList<uint> GetActionCosts(PreparedSpell spell) => new[] { 1u };

        public IEnumerator SelectAndCast(SpellCastContext context)
        {
            SelectionStarted = true;
            while (!selectionComplete)
                yield return null;

            if (shouldCast)
                yield return CoroutineRunner.Await(context.CastAsync(SpellTargetSelection.None));
            else
                SpellcastingRuntime.Fail(new CastSpellResult(), "Spell targeting was cancelled.");
        }

        public bool IsSelectionValid(SpellCastContext context, SpellTargetSelection selection) =>
            true;

        public ValueTask<bool> Cast(
            SpellCastContext context,
            SpellTargetSelection selection,
            CastSpellResult result
        )
        {
            result.Targets.Add(context.Caster);
            return new ValueTask<bool>(true);
        }

        public bool AppliesMultipleAttackPenalty(SpellCastContext context) => false;

        public void CompleteSelection()
        {
            selectionComplete = true;
        }
    }

    private sealed class SelfDefeatingSpellDefinition : ISpellDefinition
    {
        public string Slug => "light";

        public IReadOnlyList<uint> GetActionCosts(PreparedSpell spell) => new[] { 1u };

        public IEnumerator SelectAndCast(SpellCastContext context)
        {
            yield break;
        }

        public bool IsSelectionValid(SpellCastContext context, SpellTargetSelection selection) =>
            true;

        public async ValueTask<bool> Cast(
            SpellCastContext context,
            SpellTargetSelection selection,
            CastSpellResult result
        )
        {
            await context.CasterCreature.ApplyFinalDamageAsync(
                1,
                Game.Rules.Runtime.RuleSource.FromSlug("test-spell")
            );
            result.Targets.Add(context.Caster);
            return true;
        }

        public bool AppliesMultipleAttackPenalty(SpellCastContext context) => false;
    }
}
