using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using GridPublic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class LenaRogueSceneFixtureTests
{
    private static readonly string[] ScenePaths =
    {
        "Assets/Scenes/UnitTestingScene.unity",
        "Assets/Scenes/Level1.unity",
        "Assets/Scenes/Level2.unity",
        "Assets/Scenes/Level3.unity"
    };

    [Test]
    public void LenaPrefabIsPlayableRogueFixture()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/Lena.prefab");
        Assert.IsNotNull(prefab, "Expected Lena creature prefab to be generated.");

        CreatureComponent creature = prefab.GetComponent<CreatureComponent>();
        Assert.IsNotNull(creature);
        Assert.AreEqual("Lena", creature.name);
        Assert.IsNotNull(prefab.GetComponent<PlayerActionController>());
        Assert.IsNotNull(prefab.GetComponent<Team>());
        Assert.IsNotNull(prefab.GetComponent<Conditions>());
        Assert.IsNotNull(prefab.GetComponent<Token>());
        AssertHasVisibleMesh(prefab);
    }

    [Test]
    public void PlayableScenesContainOneLenaOnPlayersTeam()
    {
        foreach (string scenePath in ScenePaths)
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            CreatureComponent[] creatures = Object.FindObjectsByType<CreatureComponent>(FindObjectsSortMode.None);
            List<CreatureComponent> lenas = creatures.Where(c => c.name == "Lena").ToList();
            Assert.AreEqual(1, lenas.Count, scenePath + " should contain exactly one Lena instance.");

            GameObject lena = lenas[0].gameObject;
            Assert.IsNotNull(lena.GetComponent<PlayerActionController>(), scenePath + " Lena should be player-controlled.");
            Assert.AreEqual("Players", lena.GetComponent<Team>()?.Name, scenePath + " Lena should be on Players team.");
            AssertNoCreaturePositionOverlap(scenePath, creatures);
        }
    }


    [Test]
    public void Level2ContainsOneRottingAuraZombieOnZombiesTeam()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Level2.unity", OpenSceneMode.Single);
        CreatureComponent[] creatures = Object.FindObjectsByType<CreatureComponent>(FindObjectsSortMode.None);
        List<CreatureComponent> auraZombies = creatures
            .Where(c => c.name == "Zombie Shambler (Rotting Aura)" || c.auras.Any(a => a.slug == Game.Creature.Rules.RottingAuraRule.RuleSlug))
            .ToList();

        Assert.AreEqual(1, auraZombies.Count, "Level2 should contain exactly one rotting-aura zombie variant.");
        GameObject zombie = auraZombies[0].gameObject;
        Assert.IsNotNull(zombie.GetComponent<MindlessController>(), "Rotting-aura zombie should remain AI-controlled.");
        Assert.AreEqual("Zombies", zombie.GetComponent<Team>()?.Name, "Rotting-aura zombie should remain on Level2 hostile team.");
        CollectionAssert.DoesNotContain(auraZombies[0].actions, "Jaws");
        CollectionAssert.Contains(auraZombies[0].passives, "Rotting Aura");
        AssertNoCreaturePositionOverlap("Assets/Scenes/Level2.unity", creatures);
    }
    private static void AssertHasVisibleMesh(GameObject root)
    {
        bool hasMesh = false;
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh != null)
            {
                hasMesh = true;
                break;
            }
        }

        Assert.IsTrue(hasMesh, root.name + " should have a visible token or base mesh.");
    }

    private static void AssertNoCreaturePositionOverlap(string scenePath, IEnumerable<CreatureComponent> creatures)
    {
        HashSet<Vector3Int> occupied = new();
        foreach (CreatureComponent creature in creatures)
        {
            Vector3Int cell = Vector3Int.RoundToInt(creature.transform.position);
            Assert.IsTrue(occupied.Add(cell), scenePath + " has overlapping creature position at " + cell + ".");
        }
    }
}