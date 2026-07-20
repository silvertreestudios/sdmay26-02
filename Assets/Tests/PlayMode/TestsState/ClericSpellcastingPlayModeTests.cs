using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
        cleric.InitializeHealth(1, 1);
        Game.Rules.Unity.UnityHealthRulesBridge.Create(new[] { cleric });
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
        SpellCastContext context = new(
            clericObject,
            light,
            1,
            spendActions: true,
            new SelfDefeatingSpellDefinition()
        );

        CastSpellResult result = context.Cast(SpellTargetSelection.None);

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
                context.Cast(SpellTargetSelection.None);
            else
                SpellcastingRuntime.Fail(
                    new CastSpellResult(),
                    "Spell targeting was cancelled.",
                    context.ActionController
                );
        }

        public bool Cast(
            SpellCastContext context,
            SpellTargetSelection selection,
            CastSpellResult result
        )
        {
            result.Targets.Add(context.Caster);
            return true;
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

        public bool Cast(
            SpellCastContext context,
            SpellTargetSelection selection,
            CastSpellResult result
        )
        {
            context.CasterCreature.ApplyFinalDamage(
                1,
                Game.Rules.Runtime.RuleSource.FromSlug("test-spell")
            );
            result.Targets.Add(context.Caster);
            return true;
        }

        public bool AppliesMultipleAttackPenalty(SpellCastContext context) => false;
    }
}
