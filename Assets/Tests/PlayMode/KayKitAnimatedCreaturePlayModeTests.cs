using System.Collections;
using Game.Creature;
using Game.KayKit;
using GridPrivate;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class KayKitAnimatedCreaturePlayModeTests
{
    private const string RangerPrefabPath = "Assets/KayKit/Prefabs/Animated/RangerAnimated.prefab";
    private static int cleanupSceneIndex;

    [UnitySetUp]
    public IEnumerator ResetGlobalTimeScaleBeforeTest()
    {
        Time.timeScale = 1.0f;
        yield break;
    }

    [UnityTearDown]
    public IEnumerator ResetGlobalTimeScaleAfterTest()
    {
        Time.timeScale = 1.0f;
        Scene gameplayScene = SceneManager.GetSceneByName("UnitTestingScene");
        if (gameplayScene.IsValid() && gameplayScene.isLoaded)
        {
            Scene cleanupScene = SceneManager.CreateScene("Issue108Cleanup" + cleanupSceneIndex++);
            SceneManager.SetActiveScene(cleanupScene);
            AsyncOperation unload = SceneManager.UnloadSceneAsync(gameplayScene);
            while (unload != null && !unload.isDone)
                yield return null;
        }

        // Allow deferred Object.Destroy calls from the test body to finish before
        // another test binds the same imported clips to a different animation graph.
        yield return null;
    }

    [UnityTest]
    public IEnumerator LocomotionAndEveryAttackStyleReturnToIdleWithoutRootMotion()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RangerPrefabPath);
        GameObject instance = Object.Instantiate(prefab);
        try
        {
            yield return null;
            CreatureAnimationController controller = instance.GetComponent<CreatureAnimationController>();
            Assert.That(controller.Animator.applyRootMotion, Is.False);
            Assert.That(controller.IsMoving, Is.False);

            controller.SetMoving(true, 25.0f);
            Assert.That(controller.IsMoving, Is.True);
            controller.SetMoving(false, 0.0f);
            Assert.That(controller.IsMoving, Is.False);

            foreach (AnimationStyle style in new[]
                     {
                         AnimationStyle.Unarmed,
                         AnimationStyle.OneHandMelee,
                         AnimationStyle.TwoHandMelee,
                         AnimationStyle.Bow,
                         AnimationStyle.OneHandRanged,
                         AnimationStyle.TwoHandRanged,
                         AnimationStyle.Magic,
                         AnimationStyle.Tool
                     })
            {
                controller.PlayAttack(style);
                Assert.That(controller.IsActionPlaying, Is.True, style.ToString());
                float deadline = Time.realtimeSinceStartup + 5.0f;
                while (controller.IsActionPlaying && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(controller.IsActionPlaying, Is.False, style.ToString());
                Assert.That(controller.IsMoving, Is.False, style.ToString());
            }
        }
        finally
        {
            Object.Destroy(instance);
        }
    }

    [UnityTest]
    public IEnumerator HitReturnsToIdleAndLoopingArbitraryClipRequiresStopAction()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RangerPrefabPath);
        GameObject instance = Object.Instantiate(prefab);
        try
        {
            yield return null;
            CreatureAnimationController controller = instance.GetComponent<CreatureAnimationController>();
            controller.PlayHit();
            Assert.That(controller.CurrentClipId, Is.EqualTo("animation/general/hit_a"));
            float deadline = Time.realtimeSinceStartup + 5.0f;
            while (controller.IsActionPlaying && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(controller.IsActionPlaying, Is.False);

            Assert.That(controller.PlayClip("animation/general/idle_a"), Is.True);
            yield return new WaitForSeconds(0.2f);
            Assert.That(controller.IsActionPlaying, Is.True);
            controller.StopAction();
            Assert.That(controller.IsActionPlaying, Is.False);

            KayKitAnimationLibrary library = AssetDatabase.LoadAssetAtPath<KayKitAnimationLibrary>(
                "Assets/KayKit/Catalogs/KayKitAnimationLibrary.asset");
            foreach (KayKitAnimationEntry entry in library.Entries)
            {
                Assert.That(controller.PlayClip(entry.Id), Is.True, entry.Id);
                controller.StopAction();
                yield return null;
            }

        }
        finally
        {
            Object.Destroy(instance);
        }
    }

    [UnityTest]
    public IEnumerator EquipmentReplacementUsesActiveStrikeWeaponWithoutDuplicates()
    {
        GameObject creatureObject = new("Equipment owner");
        CreatureComponent creature = creatureObject.AddComponent<CreatureComponent>();
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RangerPrefabPath);
        GameObject visual = Object.Instantiate(prefab, creatureObject.transform);
        try
        {
            yield return null;
            CreatureEquipmentVisuals equipment = visual.GetComponent<CreatureEquipmentVisuals>();
            CreaturePresentation presentation = creatureObject.AddComponent<CreaturePresentation>();
            presentation.Bind(visual.GetComponent<CreatureAnimationController>(), equipment);
            EquipmentWeapon shortbow = Weapon("Shortbow", "bow", 2, 60);

            presentation.PlayAttack(shortbow);

            Assert.That(equipment.ActiveWeapon, Is.SameAs(shortbow));
            Assert.That(equipment.CurrentEntry.Id, Is.EqualTo("shortbow"));
            Assert.That(equipment.AccessoryInstanceCount, Is.EqualTo(2));
            equipment.Refresh();
            Assert.That(equipment.AccessoryInstanceCount, Is.EqualTo(2));

            presentation.PlayAttack(Weapon("Dogslicer", "sword", 1, 0));
            Assert.That(equipment.CurrentEntry.Id, Is.EqualTo("dogslicer"));
            Assert.That(equipment.AccessoryInstanceCount, Is.EqualTo(1));

            presentation.PlayAttack(AnimationStyle.Unarmed);
            Assert.That(equipment.CurrentEntry.Id, Is.EqualTo("unarmed"));
            Assert.That(equipment.AccessoryInstanceCount, Is.Zero);
        }
        finally
        {
            Object.Destroy(creatureObject);
        }
    }

    [UnityTest]
    public IEnumerator CharacterPreviewReplacementCleansPreviousVisual()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/ViewModel.prefab");
        GameObject instance = Object.Instantiate(prefab);
        try
        {
            yield return null;
            ViewModel viewModel = instance.GetComponentInChildren<ViewModel>(true);
            Quaternion beforeRotation = viewModel.transform.rotation;
            foreach (string key in new[] { "Fighter", "Cleric", "Rogue", "Sorcerer", "Barbarian" })
            {
                viewModel.setMeshName(key);
                yield return null;
                Assert.That(viewModel.transform.Find("VisualRoot").childCount, Is.EqualTo(1), key);
                Assert.That(viewModel.ActiveVisualInstance.GetComponentsInChildren<Animator>(true), Has.Length.EqualTo(1), key);
            }
            Assert.That(viewModel.rotate, Is.True);
            Assert.That(viewModel.rotationSpeed, Is.EqualTo(20.0f));
            Assert.That(Quaternion.Angle(beforeRotation, viewModel.transform.rotation), Is.GreaterThan(0.0f));
        }
        finally
        {
            Object.Destroy(instance);
        }
    }

    [UnityTest]
    public IEnumerator DefeatRemovesGridAndInteractionImmediatelyThenFinishesPresentation()
    {
        yield return SceneManager.LoadSceneAsync("UnitTestingScene", LoadSceneMode.Single);
        yield return null;

        CreatureComponent lena = null;
        float setupDeadline = Time.realtimeSinceStartup + 10.0f;
        while (Time.realtimeSinceStartup < setupDeadline)
        {
            foreach (CreatureComponent candidate in Object.FindObjectsByType<CreatureComponent>(FindObjectsSortMode.None))
            {
                if (candidate.name == "Lena" &&
                    candidate.GetComponent<CreaturePresentation>()?.AnimationController != null)
                {
                    lena = candidate;
                    break;
                }
            }
            if (lena != null)
                break;
            yield return null;
        }
        Assert.That(lena, Is.Not.Null, "Timed out waiting for mapped Lena presentation.");

        GridBase grid = Object.FindFirstObjectByType<GridBase>();
        Vector3Int location = Vector3Int.RoundToInt(lena.transform.position);
        Tile tile = grid.GetTiles()[location.x, location.z];
        Assert.That(tile.Occupants.Contains(lena.gameObject), Is.True);

        lena.TakeDamage((uint)(lena.hp + lena.tempHp));

        Assert.That(tile.Occupants.Contains(lena.gameObject), Is.False);
        foreach (Collider targetCollider in lena.GetComponentsInChildren<Collider>(true))
            Assert.That(targetCollider.enabled, Is.False);
        Assert.That(lena.GetComponent<ActionController>().enabled, Is.False);
        Assert.That(lena.gameObject.activeSelf, Is.True, "Animated death should keep presentation active briefly.");

        float deathDeadline = Time.realtimeSinceStartup + 6.0f;
        while (lena.gameObject.activeSelf && Time.realtimeSinceStartup < deathDeadline)
            yield return null;
        Assert.That(lena.gameObject.activeSelf, Is.False);
    }

    private static EquipmentWeapon Weapon(string name, string group, int hands, int range)
    {
        return new EquipmentWeapon
        {
            name = name,
            group = group,
            hands = hands,
            range = range,
            damage = new Dice(1, 6, "slashing")
        };
    }
}
