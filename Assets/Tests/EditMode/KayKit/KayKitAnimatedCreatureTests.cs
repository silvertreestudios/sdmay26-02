using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.KayKit;
using Game.KayKit.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class KayKitAnimatedCreatureTests
{
    private CreatureVisualCatalog visualCatalog;
    private EquipmentVisualCatalog equipmentCatalog;
    private KayKitAnimationLibrary animationLibrary;

    [SetUp]
    public void SetUp()
    {
        visualCatalog = AssetDatabase.LoadAssetAtPath<CreatureVisualCatalog>(
            KayKitAnimatedCreatureSetupTool.CreatureVisualCatalogPath
        );
        equipmentCatalog = AssetDatabase.LoadAssetAtPath<EquipmentVisualCatalog>(
            KayKitAnimatedCreatureSetupTool.EquipmentVisualCatalogPath
        );
        animationLibrary = AssetDatabase.LoadAssetAtPath<KayKitAnimationLibrary>(
            KayKitSetupTool.AnimationLibraryPath
        );
        Assert.That(visualCatalog, Is.Not.Null);
        Assert.That(equipmentCatalog, Is.Not.Null);
        Assert.That(animationLibrary, Is.Not.Null);
    }

    [TestCase("Lena", "adventurers/ranger")]
    [TestCase("Torgrim", "adventurers/barbarian")]
    [TestCase("Zombie Shambler", "skeletons/skeleton_minion")]
    [TestCase("Zombie Shambler (Rotting Aura)", "skeletons/skeleton_minion")]
    [TestCase("Skeleton Guard", "skeletons/skeleton_warrior")]
    [TestCase("Fighter", "adventurers/knight")]
    [TestCase("Cleric", "adventurers/mage")]
    [TestCase("Rogue", "adventurers/rogue_hooded")]
    [TestCase("Sorcerer", "adventurers/mage")]
    [TestCase("Barbarian", "adventurers/barbarian")]
    public void CreatureVisualCatalog_ContainsApprovedMappings(string key, string expectedVisualId)
    {
        Assert.That(
            visualCatalog.TryResolve(key, out CreatureVisualCatalogEntry entry),
            Is.True,
            key
        );
        Assert.That(entry.VisualId, Is.EqualTo(expectedVisualId));
        Assert.That(entry.VisualPrefab, Is.Not.Null);
    }

    [TestCase("Goblin Warrior")]
    [TestCase("Kobold Warrior")]
    [TestCase("Unknown Creature")]
    public void CreatureVisualCatalog_UnmappedKeysUseLegacyFallback(string key)
    {
        Assert.That(visualCatalog.TryResolve(key, out _), Is.False);
    }

    [Test]
    public void TokenMeshSelection_MappedRefreshDoesNotDuplicateVisualsOrAnimators()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Creatures/Lena.prefab"
        );
        GameObject instance = Object.Instantiate(prefab);
        try
        {
            TokenMeshSelection selector = instance.GetComponentInChildren<TokenMeshSelection>(true);
            selector.RefreshVisual();
            Assert.That(selector.UsingAnimatedVisual, Is.True);
            Assert.That(
                selector.ActiveVisualInstance.GetComponentsInChildren<Animator>(true),
                Has.Length.EqualTo(1)
            );
            Assert.That(
                selector.ActiveVisualInstance.transform.localScale,
                Is.EqualTo(
                    Vector3.one * KayKitAnimatedCreatureSetupTool.AnimatedCreatureVisualScale
                )
            );
            Assert.That(
                selector.transform.GetChild(0).GetComponent<MeshRenderer>().enabled,
                Is.False
            );
            Assert.That(
                selector.transform.GetChild(1).GetComponent<MeshRenderer>().enabled,
                Is.False
            );
            foreach (
                Renderer renderer in selector.ActiveVisualInstance.GetComponentsInChildren<Renderer>(
                    true
                )
            )
                Assert.That(renderer.gameObject.layer, Is.EqualTo(instance.layer), renderer.name);

            selector.RefreshVisual();

            Transform root = instance.transform.Find("VisualRoot");
            Assert.That(root, Is.Not.Null);
            Assert.That(root.childCount, Is.EqualTo(1));
            Assert.That(
                selector.ActiveVisualInstance.GetComponentsInChildren<Animator>(true),
                Has.Length.EqualTo(1)
            );
            Assert.That(
                selector.ActiveVisualInstance.GetComponentsInChildren<CreatureEquipmentVisuals>(
                    true
                ),
                Has.Length.EqualTo(1)
            );
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void TokenMeshSelection_UnmappedGoblinKeepsLegacyRenderer()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Creatures/goblin-warrior.prefab"
        );
        GameObject instance = Object.Instantiate(prefab);
        try
        {
            TokenMeshSelection selector = instance.GetComponentInChildren<TokenMeshSelection>(true);
            selector.RefreshVisual();

            Assert.That(selector.UsingAnimatedVisual, Is.False);
            Assert.That(
                selector.transform.GetChild(0).GetComponent<MeshRenderer>().enabled,
                Is.True
            );
            Assert.That(
                selector.transform.GetChild(1).GetComponent<MeshRenderer>().enabled,
                Is.True
            );
            Assert.That(
                selector.transform.GetChild(0).GetComponent<MeshFilter>().sharedMesh,
                Is.Not.Null
            );
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void CharacterPreview_SwitchesAllApprovedClassesWithoutDuplicates()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/UI/ViewModel.prefab"
        );
        GameObject instance = Object.Instantiate(prefab);
        try
        {
            ViewModel viewModel = instance.GetComponentInChildren<ViewModel>(true);
            Assert.That(
                viewModel.transform.Find("VisualRoot").localScale,
                Is.EqualTo(
                    Vector3.one * KayKitAnimatedCreatureSetupTool.CharacterPreviewVisualScale
                )
            );
            foreach (string key in new[] { "Fighter", "Cleric", "Rogue", "Sorcerer", "Barbarian" })
            {
                viewModel.setMeshName(key);
                Assert.That(viewModel.UsingAnimatedVisual, Is.True, key);
                Assert.That(viewModel.transform.Find("VisualRoot").childCount, Is.EqualTo(1), key);
                Assert.That(
                    viewModel.ActiveVisualInstance.transform.localScale,
                    Is.EqualTo(
                        Vector3.one * KayKitAnimatedCreatureSetupTool.AnimatedCreatureVisualScale
                    ),
                    key
                );
                Assert.That(
                    viewModel.ActiveVisualInstance.GetComponentsInChildren<Animator>(true),
                    Has.Length.EqualTo(1),
                    key
                );
            }
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void AnimatedVisualPrefabs_UseApprovedReducedScale()
    {
        Assert.That(KayKitAnimatedCreatureSetupTool.AnimatedCreatureVisualScale, Is.EqualTo(0.50f));

        HashSet<GameObject> checkedPrefabs = new();
        foreach (CreatureVisualCatalogEntry entry in visualCatalog.Entries)
        {
            if (entry.VisualPrefab == null || !checkedPrefabs.Add(entry.VisualPrefab))
                continue;
            Assert.That(
                entry.VisualPrefab.transform.localScale,
                Is.EqualTo(
                    Vector3.one * KayKitAnimatedCreatureSetupTool.AnimatedCreatureVisualScale
                ),
                entry.VisualPrefab.name
            );
        }
        Assert.That(checkedPrefabs.Count, Is.EqualTo(8));
    }

    [Test]
    public void AnimatorController_ActionTransitionAllowsConsecutiveAnimations()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
            KayKitAnimatedCreatureSetupTool.AnimatorControllerPath
        );
        Assert.That(controller, Is.Not.Null);

        AnimatorStateTransition transition = controller
            .layers[0]
            .stateMachine.anyStateTransitions.Single(candidate =>
                candidate.destinationState != null && candidate.destinationState.name == "Action"
            );
        Assert.That(transition.canTransitionToSelf, Is.True);
    }

    [Test]
    public void CreaturePresentation_FaceTowardsUsesHorizontalTargetDirection()
    {
        GameObject owner = new("Facing test");
        try
        {
            owner.transform.position = new Vector3(2.0f, 4.0f, 3.0f);
            CreaturePresentation presentation = owner.AddComponent<CreaturePresentation>();

            Assert.That(presentation.FaceTowards(new Vector3(8.0f, 20.0f, -1.0f)), Is.True);

            Vector3 expected = new Vector3(6.0f, 0.0f, -4.0f).normalized;
            Assert.That(Vector3.Dot(owner.transform.forward, expected), Is.GreaterThan(0.999f));
            Assert.That(Mathf.Abs(owner.transform.forward.y), Is.LessThan(0.001f));
            Assert.That(presentation.FaceTowards(owner.transform.position), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void AnimationLibrary_LooksUpEveryPlayableEntryAndRequiredDefault()
    {
        Assert.That(animationLibrary.Entries, Is.Not.Empty);
        foreach (KayKitAnimationEntry entry in animationLibrary.Entries)
        {
            Assert.That(
                animationLibrary.TryGet(entry.Id, out KayKitAnimationEntry resolved),
                Is.True,
                entry.Id
            );
            Assert.That(resolved, Is.SameAs(entry));
            Assert.That(entry.Duration, Is.GreaterThan(0), entry.Id);
        }

        foreach (string required in CreatureAnimationController.RequiredDefaultClipIds())
            Assert.That(animationLibrary.TryGet(required, out _), Is.True, required);
        Assert.That(
            animationLibrary.TryGet("animation/general/idle_a", out KayKitAnimationEntry idle),
            Is.True
        );
        Assert.That(idle.Loop, Is.True);
        Assert.That(
            animationLibrary.TryGet(
                "animation/combatmelee/melee_1h_attack_chop",
                out KayKitAnimationEntry attack
            ),
            Is.True
        );
        Assert.That(attack.Loop, Is.False);
    }

    [Test]
    public void AnimationController_UnknownClipWarnsOnceAndReturnsFalse()
    {
        GameObject owner = new("Animation warning test");
        try
        {
            CreatureAnimationController controller =
                owner.AddComponent<CreatureAnimationController>();
            controller.Configure(null, null, animationLibrary, KayKitRigProfile.Adventurer);
            int warnings = 0;
            void Capture(string message, string stackTrace, LogType type)
            {
                if (type == LogType.Warning && message.Contains("missing/clip"))
                    warnings++;
            }
            Application.logMessageReceived += Capture;
            try
            {
                Assert.That(controller.PlayClip("missing/clip"), Is.False);
                Assert.That(controller.PlayClip("missing/clip"), Is.False);
            }
            finally
            {
                Application.logMessageReceived -= Capture;
            }
            Assert.That(warnings, Is.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void EquipmentChanged_RaisesFromEveryPropertySetter()
    {
        GameObject owner = new("Equipment property test");
        try
        {
            CreatureComponent creature = owner.AddComponent<CreatureComponent>();
            int deliveries = 0;
            creature.EquipmentChanged += () => deliveries++;
            creature.equippedLeftHand = Weapon("Dagger", "knife", 1, 0);
            creature.equippedRightHand = Weapon("Longsword", "sword", 1, 0);
            creature.equippedArmor = new EquipmentArmor { name = "Leather Armor" };
            Assert.That(deliveries, Is.EqualTo(3));
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void EquipmentChanged_RaisesFromEveryEquipAndUnequipMethod()
    {
        GameObject owner = new("Equipment method test");
        try
        {
            CreatureComponent creature = owner.AddComponent<CreatureComponent>();
            int deliveries = 0;
            creature.EquipmentChanged += () => deliveries++;
            creature.EquipWeaponLeft(Weapon("Dagger", "knife", 1, 0));
            creature.UnequipWeaponLeft();
            creature.EquipWeaponRight(Weapon("Longsword", "sword", 1, 0));
            creature.UnequipWeaponRight();
            creature.EquipArmor(new EquipmentArmor { name = "Leather Armor" });
            creature.UnequipArmor();
            Assert.That(deliveries, Is.EqualTo(6));
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    [TestCase("Dogslicer", "sword", 1, 0, "adventurer", "dogslicer", AnimationStyle.OneHandMelee)]
    [TestCase(
        "Scimitar",
        "sword",
        1,
        0,
        "skeleton",
        "skeleton-scimitar",
        AnimationStyle.OneHandMelee
    )]
    [TestCase(
        "Mystery Sword",
        "sword",
        1,
        0,
        "adventurer",
        "fallback-sword",
        AnimationStyle.OneHandMelee
    )]
    [TestCase("Mystery Bow", "bow", 2, 60, "adventurer", "fallback-bow", AnimationStyle.Bow)]
    [TestCase(
        "Mystery Axe",
        "axe",
        2,
        0,
        "skeleton",
        "fallback-skeleton-axe",
        AnimationStyle.TwoHandMelee
    )]
    public void EquipmentCatalog_ResolvesExactSpeciesAndFallbackMappings(
        string name,
        string group,
        int hands,
        int range,
        string species,
        string expectedId,
        AnimationStyle expectedStyle
    )
    {
        Assert.That(
            equipmentCatalog.TryResolve(
                Weapon(name, group, hands, range),
                species,
                out EquipmentVisualCatalogEntry entry
            ),
            Is.True
        );
        Assert.That(entry.Id, Is.EqualTo(expectedId));
        Assert.That(entry.AnimationStyle, Is.EqualTo(expectedStyle));
    }

    [Test]
    public void EquipmentCatalog_ResolvesUnarmedAndProxyMappings()
    {
        Assert.That(
            equipmentCatalog.TryResolve(
                null,
                "adventurer",
                out EquipmentVisualCatalogEntry unarmed
            ),
            Is.True
        );
        Assert.That(unarmed.Id, Is.EqualTo("unarmed"));
        Assert.That(unarmed.Attachments, Is.Empty);

        Assert.That(
            equipmentCatalog.TryResolve(
                Weapon("Halberd", "polearm", 2, 0),
                "adventurer",
                out EquipmentVisualCatalogEntry halberd
            ),
            Is.True
        );
        Assert.That(
            AssetDatabase.GetAssetPath(halberd.Attachments.Single().AccessoryPrefab),
            Does.EndWith("staff.fbx")
        );
        Assert.That(
            equipmentCatalog.TryResolve(
                Weapon("Sling", "sling", 1, 50),
                "adventurer",
                out EquipmentVisualCatalogEntry sling
            ),
            Is.True
        );
        Assert.That(
            AssetDatabase.GetAssetPath(sling.Attachments.Single().AccessoryPrefab),
            Does.EndWith("smokebomb.fbx")
        );
        Assert.That(
            equipmentCatalog.TryResolve(
                Weapon("Shortbow", "bow", 2, 60),
                "skeleton",
                out EquipmentVisualCatalogEntry bow
            ),
            Is.True
        );
        Assert.That(bow.Attachments, Has.Count.EqualTo(2));
        Assert.That(
            bow.Attachments.Any(attachment => attachment.Socket == EquipmentSocket.Quiver),
            Is.True
        );
    }

    [Test]
    public void ActiveStrikeWeapon_ReplacesAccessoriesWithoutDuplicates()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            KayKitAnimatedCreatureSetupTool.AnimatedPrefabRoot + "/RangerAnimated.prefab"
        );
        GameObject instance = Object.Instantiate(prefab);
        try
        {
            CreatureEquipmentVisuals visuals = instance.GetComponent<CreatureEquipmentVisuals>();
            EquipmentWeapon shortbow = Weapon("Shortbow", "bow", 2, 60);
            visuals.SetActiveWeapon(shortbow);
            Assert.That(visuals.ActiveWeapon, Is.SameAs(shortbow));
            Assert.That(visuals.CurrentEntry.Id, Is.EqualTo("shortbow"));
            Assert.That(visuals.AccessoryInstanceCount, Is.EqualTo(2));

            visuals.SetActiveWeapon(Weapon("Dogslicer", "sword", 1, 0));
            Assert.That(visuals.CurrentEntry.Id, Is.EqualTo("dogslicer"));
            Assert.That(visuals.AccessoryInstanceCount, Is.EqualTo(1));

            visuals.SetActiveWeapon(null);
            Assert.That(visuals.CurrentEntry.Id, Is.EqualTo("unarmed"));
            Assert.That(visuals.AccessoryInstanceCount, Is.Zero);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void EquipmentVisuals_UnknownMappingWarnsOnce()
    {
        GameObject owner = new("Equipment warning test");
        try
        {
            CreatureEquipmentVisuals visuals = owner.AddComponent<CreatureEquipmentVisuals>();
            visuals.Configure(null, equipmentCatalog, "adventurer", "unarmed", null, null);
            EquipmentWeapon unknown = Weapon("Impossible Implement", "unknown", 3, 0);
            int warnings = 0;
            void Capture(string message, string stackTrace, LogType type)
            {
                if (type == LogType.Warning && message.Contains("Impossible Implement"))
                    warnings++;
            }
            Application.logMessageReceived += Capture;
            try
            {
                visuals.SetActiveWeapon(unknown);
                visuals.SetActiveWeapon(unknown);
            }
            finally
            {
                Application.logMessageReceived -= Capture;
            }
            Assert.That(warnings, Is.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void GeneratedAnimatedPrefabs_DisableRootMotionAndUseOneAnimator()
    {
        string[] paths = AssetDatabase
            .FindAssets("t:Prefab", new[] { KayKitAnimatedCreatureSetupTool.AnimatedPrefabRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .ToArray();
        Assert.That(paths, Has.Length.EqualTo(8));
        foreach (string path in paths)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Animator[] animators = prefab.GetComponentsInChildren<Animator>(true);
            Assert.That(animators, Has.Length.EqualTo(1), path);
            Assert.That(animators[0].applyRootMotion, Is.False, path);
            Assert.That(animators[0].runtimeAnimatorController, Is.Not.Null, path);
        }
    }

    [Test]
    public void GeneratedAnimatedAssets_PassValidator()
    {
        Assert.That(KayKitAnimatedCreatureSetupTool.ValidateAnimatedAssets(), Is.Empty);
    }

    private static EquipmentWeapon Weapon(string name, string group, int hands, int range)
    {
        return new EquipmentWeapon
        {
            name = name,
            group = group,
            hands = hands,
            range = range,
            damage = new Dice(1, 6, "slashing"),
        };
    }
}
