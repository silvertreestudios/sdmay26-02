using System;
using System.Linq;
using Game.KayKit;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class KayKitShowcaseTests
{
    private const string ShowcaseScenePath = "Assets/KayKit/Scenes/KayKitShowcase.unity";

    [Test]
    public void ShowcaseScene_HasModelsAnimationLibraryAndReviewControls()
    {
        Scene scene = EditorSceneManager.OpenScene(ShowcaseScenePath, OpenSceneMode.Additive);
        try
        {
            Transform[] sceneTransforms = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .ToArray();
            KayKitShowcaseController controller = sceneTransforms
                .Select(transform => transform.GetComponent<KayKitShowcaseController>())
                .Single(component => component != null);

            Assert.That(controller.HasRequiredReferences, Is.True);
            Assert.That(controller.AvailableClipCount, Is.GreaterThan(0));
            Assert.That(controller.AdventurerAnimator.avatar, Is.Not.Null);
            Assert.That(controller.AdventurerAnimator.avatar.isHuman, Is.True);
            Assert.That(controller.AdventurerAnimator.avatar.isValid, Is.True);
            Assert.That(controller.SkeletonAnimator.avatar, Is.Not.Null);
            Assert.That(controller.SkeletonAnimator.avatar.isHuman, Is.True);
            Assert.That(controller.SkeletonAnimator.avatar.isValid, Is.True);
            Assert.That(controller.ShowcaseCamera.CompareTag("MainCamera"), Is.True);
            Assert.That(controller.TrySelectClip("animation/general/idle_a"), Is.True);
            Assert.That(controller.SelectedEntry.Id, Is.EqualTo("animation/general/idle_a"));

            string[] expectedPrefabPaths =
            {
                "Assets/KayKit/Prefabs/RepresentativeAdventurer.prefab",
                "Assets/KayKit/Prefabs/RepresentativeSkeleton.prefab",
                "Assets/KayKit/Prefabs/RepresentativeAccessory.prefab",
                "Assets/KayKit/Prefabs/RepresentativeDungeonPiece.prefab",
                "Assets/KayKit/Prefabs/RepresentativeDungeonWall.prefab",
                "Assets/KayKit/Prefabs/RepresentativeDungeonProp.prefab"
            };
            string[] scenePrefabPaths = sceneTransforms
                .Select(transform => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    transform.gameObject))
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            foreach (string path in expectedPrefabPaths)
                Assert.That(scenePrefabPaths, Does.Contain(path), path);

            Assert.That(
                sceneTransforms.Sum(transform =>
                    GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject)),
                Is.Zero);
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }
}
