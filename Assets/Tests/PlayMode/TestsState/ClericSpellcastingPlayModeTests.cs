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
    private GameObject combatManagerObject;
    private GameObject opponentObject;
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
        if (combatManagerObject != null)
            Object.Destroy(combatManagerObject);
        if (opponentObject != null)
            Object.Destroy(opponentObject);

        Pf2eItemCatalog.ResetForTests();
    }

    [UnityTest]
    public IEnumerator ClericGetsOnlyUnmigratedLegacySpellActionsBeforeComposition()
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

        Assert.That(controller.GetActions().Any(action => action.ActionName == "Light"), Is.False);
        Assert.That(
            controller.GetActions().Any(action => action.ActionName == "Divine Lance"),
            Is.False
        );
        Assert.That(controller.GetActions().Any(action => action.ActionName == "Shield"), Is.True);

        controller.StartTurn();
        EntityAction shield = controller
            .GetActions()
            .First(action => action.ActionName == "Shield");
        controller.TakeAction(shield);
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

        PreparedSpell shield = cleric.Prepared.Spellcasting.PreparedSpells.First(spell =>
            spell.Slug == "shield"
        );
        controller.StartTurn();
        ConfirmableSpellDefinition cancelledDefinition = new(shouldCast: false);
        controller.TakeAction(new CastSpellAction(shield, 1, cancelledDefinition));
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
        controller.TakeAction(new CastSpellAction(shield, 1, successfulDefinition));
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
        GameManager automaticCombat = Object.FindFirstObjectByType<GameManager>();
        Assert.That(automaticCombat, Is.Not.Null);
        automaticCombat.StopAllCoroutines();
        automaticCombat.enabled = false;
        CombatManagerInterface existingCombatManager =
            Object.FindFirstObjectByType<CombatManagerInterface>();
        Assert.That(existingCombatManager, Is.Not.Null);
        Object.DestroyImmediate(existingCombatManager.gameObject);
        combatManagerObject = new GameObject("Self-Defeating Spell Combat Manager");
        CombatManagerInterface combatManager = combatManagerObject.AddComponent<CombatManager>();

        clericObject = new GameObject("Self-Defeating Animated Caster");
        CreatureComponent cleric = clericObject.AddComponent<CreatureComponent>();
        cleric.level = 1;
        cleric.initiative = 100;
        cleric.InitializeHealthBeforeEncounter(1, 1);
        cleric.wisMod = 4;
        cleric.Build = new CharacterBuild { ClassName = "Cleric" };
        cleric.Prepared = Pf2eCharacterPreparer.Prepare(cleric, cleric.Build);
        clericObject.AddComponent<Team>().Name = "players";
        PlayerActionController controller = clericObject.AddComponent<PlayerActionController>();
        clericController = controller;

        opponentObject = new GameObject("Self-Defeating Spell Opponent");
        CreatureComponent opponent = opponentObject.AddComponent<CreatureComponent>();
        opponent.name = "Self-Defeating Spell Opponent";
        opponent.initiative = -100;
        opponent.InitializeHealthBeforeEncounter(10, 10);
        opponentObject.AddComponent<Team>().Name = "enemies";
        TestActionController opponentController =
            opponentObject.AddComponent<TestActionController>();
        combatManager.AddCombatant(opponentController);

        GameObject visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/KayKit/Prefabs/Animated/MageStaffAnimated.prefab"
        );
        GameObject visual = Object.Instantiate(visualPrefab, clericObject.transform);
        CreatureAnimationController animationController =
            visual.GetComponent<CreatureAnimationController>();
        CreaturePresentation presentation = clericObject.AddComponent<CreaturePresentation>();
        presentation.Bind(animationController, visual.GetComponent<CreatureEquipmentVisuals>());
        yield return null;

        PreparedSpell shield = cleric.Prepared.Spellcasting.PreparedSpells.First(spell =>
            spell.Slug == "shield"
        );
        combatManager.StartDungeonCombat(new ActionController[] { controller, opponentController });
        Assert.That(combatManager.WhosTurn(), Is.SameAs(clericObject));
        SpellCastContext context = new(
            clericObject,
            shield,
            1,
            spendActions: false,
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

        public string Slug => "shield";
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
        public string Slug => "shield";

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

    private sealed class TestActionController : ActionController
    {
        public override void EndTurn() { }
    }
}
