using Game.Combat.Spells;
using Game.Creature;
using Game.Creature.Rules;
using NUnit.Framework;
using System.Collections;
using System.Linq;
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
}
