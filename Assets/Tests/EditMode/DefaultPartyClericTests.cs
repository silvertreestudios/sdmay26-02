using System;
using System.Linq;
using System.Reflection;
using Game.Combat.Spells;
using Game.Creature;
using Game.DungeonPersistence.Actors;
using Game.KayKit;
using Game.KayKit.Editor;
using Game.Rules.Runtime;
using GridPublic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Protects the authored Maren prefab and three-member default party contract.</summary>
public sealed class DefaultPartyClericTests
{
    [Test]
    public void MarenPrefabMatchesResourcesBuildAndPlayerPresentation()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            DefaultPartyClericSetupTool.MarenPrefabPath
        );
        Assert.That(prefab, Is.Not.Null);
        GameObject instance = Object.Instantiate(prefab);
        GameObject resourcesObject = CreatureJsonConverter.CreateByName("Maren");
        Assert.That(resourcesObject, Is.Not.Null);

        try
        {
            CreatureComponent prefabCreature = instance.GetComponent<CreatureComponent>();
            CreatureComponent resourcesCreature = resourcesObject.GetComponent<CreatureComponent>();
            Assert.That(prefabCreature, Is.Not.Null);
            prefabCreature.InitializeRuntimeActions();
            Assert.That(prefabCreature.name, Is.EqualTo("Maren"));
            Assert.That(prefabCreature.level, Is.EqualTo(resourcesCreature.level));
            Assert.That(prefabCreature.maxHp, Is.EqualTo(resourcesCreature.maxHp));
            Assert.That(prefabCreature.Build.ClassName, Is.EqualTo("Cleric"));
            Assert.That(prefabCreature.Build.SubclassName, Is.EqualTo("Cloistered Cleric"));
            Assert.That(prefabCreature.Build.ClassFeatName, Is.EqualTo("Domain Initiate"));
            Assert.That(
                prefabCreature.weapons.Select(weapon => weapon.name),
                Is.EquivalentTo(resourcesCreature.weapons.Select(weapon => weapon.name))
            );
            Assert.That(prefabCreature.weapons.Single().name, Is.EqualTo("Mace"));

            PreparedSpellBook spellBook = prefabCreature.Prepared.SpellBook as PreparedSpellBook;
            Assert.That(spellBook, Is.Not.Null);
            Assert.That(spellBook.SpellAttackModifier, Is.EqualTo(7));
            Assert.That(spellBook.SpellDc, Is.EqualTo(17));
            Assert.That(spellBook.Entries.Count(entry => entry.IsCantrip), Is.EqualTo(5));
            Assert.That(
                spellBook
                    .CreateInitialSlotStates(new CreatureId("maren-prefab"))
                    .Select(slot => (slot.Id.Value, slot.Remaining, slot.Maximum)),
                Is.EquivalentTo(
                    new[]
                    {
                        ("maren-prefab:font-heal", 4, 4),
                        ("maren-prefab:rank-1-bless", 1, 1),
                        ("maren-prefab:rank-1-infuse-vitality", 1, 1),
                    }
                )
            );

            Assert.That(instance.GetComponent<PlayerActionController>(), Is.Not.Null);
            Assert.That(instance.GetComponent<Team>()?.Name, Is.EqualTo("Players"));
            Assert.That(instance.GetComponent<Token>(), Is.Not.Null);
            Assert.That(instance.GetComponent<Portrait>(), Is.Not.Null);
            Assert.That(instance.GetComponentInChildren<Camera>(true), Is.Not.Null);
            Assert.That(instance.GetComponent<CreaturePresentation>(), Is.Not.Null);
            DungeonPartyMemberIdentity identity =
                instance.GetComponent<DungeonPartyMemberIdentity>();
            Assert.That(identity, Is.Not.Null);
            Assert.That(identity.RosterSlotId, Is.EqualTo("party-slot-maren"));
            Assert.That(identity.CreatureContentId, Is.EqualTo("player-character-maren"));
        }
        finally
        {
            Object.DestroyImmediate(instance);
            Object.DestroyImmediate(resourcesObject);
        }
    }

    [Test]
    public void ProceduralDungeonAuthorsExactDefaultPartyOnDistinctWalkableCells()
    {
        Scene originalActiveScene = SceneManager.GetActiveScene();
        Scene partyScene = SceneManager.GetSceneByPath(ProceduralDungeonSceneTool.ScenePath);
        bool ownsPartyScene = !partyScene.isLoaded;

        try
        {
            if (ownsPartyScene)
            {
                partyScene = EditorSceneManager.OpenScene(
                    ProceduralDungeonSceneTool.ScenePath,
                    OpenSceneMode.Additive
                );
            }

            GameObject[] sceneRoots = partyScene.GetRootGameObjects();
            ActionController[] party = sceneRoots
                .SelectMany(root => root.GetComponentsInChildren<ActionController>(true))
                .Where(controller =>
                    string.Equals(
                        controller.GetComponent<Team>()?.Name,
                        "Players",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .ToArray();

            Assert.That(
                party.Select(member => member.name),
                Is.EquivalentTo(new[] { "Lena", "Torgrim", "Maren" })
            );
            Assert.That(
                party
                    .Select(member =>
                        member.GetComponent<DungeonPartyMemberIdentity>().RosterSlotId
                    )
                    .OrderBy(value => value, StringComparer.Ordinal),
                Is.EqualTo(new[] { "party-slot-lena", "party-slot-maren", "party-slot-torgrim" })
            );
            Assert.That(
                party
                    .Select(member => Vector3Int.RoundToInt(member.transform.position))
                    .Distinct()
                    .Count(),
                Is.EqualTo(3)
            );
            Assert.That(party.All(member => !member.gameObject.activeSelf), Is.True);

            Map map = sceneRoots
                .SelectMany(root => root.GetComponentsInChildren<Map>(true))
                .SingleOrDefault();
            Assert.That(map, Is.Not.Null);
            var document = map.ValidateSource().JsonMap.LevelDocument;
            foreach (ActionController member in party)
            {
                Vector3Int position = Vector3Int.RoundToInt(member.transform.position);
                char cell = document.Rows[document.Height - 1 - position.z][position.x];
                Assert.That(cell == '.' || cell == 'D', Is.True, member.name);
            }
        }
        finally
        {
            if (ownsPartyScene && partyScene.IsValid() && partyScene.isLoaded)
                EditorSceneManager.CloseScene(partyScene, true);
            if (originalActiveScene.IsValid() && originalActiveScene.isLoaded)
                SceneManager.SetActiveScene(originalActiveScene);
        }
    }

    [Test]
    public void RegenerationMenuGuardDoesNotRegenerateWhenSceneSaveIsCancelled()
    {
        MethodInfo method = typeof(DefaultPartyClericSetupTool).GetMethod(
            "TryRegenerateFromMenu",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        Assert.That(method, Is.Not.Null);
        int regenerationCalls = 0;

        bool result = (bool)
            method.Invoke(
                null,
                new object[] { (Func<bool>)(() => false), (Action)(() => regenerationCalls++) }
            );

        Assert.That(result, Is.False);
        Assert.That(regenerationCalls, Is.Zero);
    }
}
