#if UNITY_EDITOR
using System.Collections;
using Game.KayKit;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class KayKitShowcasePlayModeTests
{
    [UnityTest]
    public IEnumerator ShowcaseController_PlaysAndSwitchesImportedClips()
    {
        KayKitAnimationLibrary library = AssetDatabase.LoadAssetAtPath<KayKitAnimationLibrary>(
            "Assets/KayKit/Catalogs/KayKitAnimationLibrary.asset");
        GameObject adventurerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/KayKit/Prefabs/RepresentativeAdventurer.prefab");
        GameObject skeletonPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/KayKit/Prefabs/RepresentativeSkeleton.prefab");
        GameObject adventurer = Object.Instantiate(adventurerPrefab);
        GameObject skeleton = Object.Instantiate(skeletonPrefab);
        GameObject environment = new("Test Environment");
        GameObject accessory = new("Test Accessory");
        GameObject cameraObject = new("Test Camera");
        GameObject focusObject = new("Test Focus");
        GameObject controllerObject = new("Test Showcase Controller");
        accessory.transform.SetParent(environment.transform);

        try
        {
            KayKitShowcaseController controller =
                controllerObject.AddComponent<KayKitShowcaseController>();
            controller.Configure(
                library,
                adventurer,
                skeleton,
                environment,
                accessory,
                cameraObject.AddComponent<Camera>(),
                focusObject.transform);
            yield return null;

            Assert.That(controller.HasRequiredReferences, Is.True);
            Assert.That(controller.IsPlaying, Is.True);
            Assert.That(controller.SelectedEntry.Id, Is.EqualTo("animation/general/idle_a"));
            Assert.That(controller.TrySelectClip("animation/movementbasic/walking_a"), Is.True);

            float before = controller.NormalizedTime;
            yield return new WaitForSeconds(0.25f);

            Assert.That(controller.IsPlaying, Is.True);
            Assert.That(controller.SelectedEntry.Id, Is.EqualTo(
                "animation/movementbasic/walking_a"));
            Assert.That(controller.NormalizedTime, Is.GreaterThan(before));
        }
        finally
        {
            Object.Destroy(controllerObject);
            Object.Destroy(cameraObject);
            Object.Destroy(focusObject);
            Object.Destroy(environment);
            Object.Destroy(adventurer);
            Object.Destroy(skeleton);
        }
    }
}
#endif
