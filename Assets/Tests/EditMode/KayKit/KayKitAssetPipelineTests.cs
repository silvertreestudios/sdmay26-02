using System;
using System.IO;
using System.Linq;
using Game.KayKit;
using Game.KayKit.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class KayKitAssetPipelineTests
{
    [Test]
    public void GeneratedSetup_PassesValidator()
    {
        KayKitValidationReport report = KayKitSetupTool.Validate();

        Assert.That(report.Errors, Is.Empty, report.Format());
    }

    [Test]
    public void AnimationLibrary_ContainsEightCategoriesAndNoSetupPose()
    {
        KayKitAnimationLibrary library =
            AssetDatabase.LoadAssetAtPath<KayKitAnimationLibrary>(KayKitSetupTool.AnimationLibraryPath);
        int expectedPlayableClipCount = AssetDatabase.FindAssets(
                string.Empty,
                new[] { KayKitPathUtility.AnimationsRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(KayKitPathUtility.IsAnimationSource)
            .SelectMany(path => AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
            .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
            .Count(clip => KayKitPathUtility.ClassifyClip(clip.name) != KayKitClipSemantics.SetupPose);

        Assert.That(library, Is.Not.Null);
        Assert.That(library.Entries.Count, Is.EqualTo(expectedPlayableClipCount));
        Assert.That(
            library.Entries.Select(entry => entry.SourceCategory)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            Is.EqualTo(8));
        Assert.That(
            library.Entries.Any(entry =>
                KayKitPathUtility.ClassifyClip(entry.Clip.name) == KayKitClipSemantics.SetupPose),
            Is.False);
    }

    [Test]
    public void DungeonCatalog_ReferencesEveryImportedDungeonFbx()
    {
        KayKitDungeonCatalog catalog =
            AssetDatabase.LoadAssetAtPath<KayKitDungeonCatalog>(KayKitSetupTool.DungeonCatalogPath);
        int fbxCount = AssetDatabase.FindAssets(string.Empty, new[] { KayKitPathUtility.DungeonRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Count(path => string.Equals(Path.GetExtension(path), ".fbx", StringComparison.OrdinalIgnoreCase));

        Assert.That(catalog, Is.Not.Null);
        Assert.That(catalog.Entries.Count, Is.EqualTo(fbxCount));
        Assert.That(catalog.Entries.Select(entry => entry.Id).Distinct().Count(), Is.EqualTo(fbxCount));
        Assert.That(catalog.Entries.All(entry => entry.Model != null), Is.True);
    }

    [Test]
    public void GeneratedMaterials_ReferenceEveryRetainedAtlasExactlyOnce()
    {
        string[] texturePaths = AssetDatabase.FindAssets("t:Texture2D", new[]
            {
                KayKitPathUtility.DungeonRoot,
                KayKitPathUtility.AdventurersRoot,
                KayKitPathUtility.SkeletonsRoot
            })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        string[] materialTexturePaths = AssetDatabase.FindAssets("t:Material", new[]
            {
                KayKitSetupTool.MaterialRoot
            })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<Material>)
            .Select(material => AssetDatabase.GetAssetPath(material.mainTexture))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.That(texturePaths, Has.Length.EqualTo(7));
        Assert.That(materialTexturePaths, Is.EqualTo(texturePaths));
    }

    [Test]
    public void HumanoidModels_HaveValidGeneratedAvatars()
    {
        string[] paths = AssetDatabase.FindAssets(string.Empty, KayKitPathUtility.PackRoots)
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => KayKitPathUtility.IsCharacterModel(path) ||
                KayKitPathUtility.IsAnimationSource(path))
            .ToArray();

        Assert.That(paths, Is.Not.Empty);
        foreach (string path in paths)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Animator animator = model.GetComponent<Animator>();
            Assert.That(animator, Is.Not.Null, path);
            Assert.That(animator.avatar, Is.Not.Null, path);
            Assert.That(animator.avatar.isHuman, Is.True, path);
            Assert.That(animator.avatar.isValid, Is.True, path);
        }
    }

    [Test]
    public void RepresentativeWrappers_UseProjectMaterialsAndDisableRootMotion()
    {
        string[] prefabPaths =
        {
            KayKitSetupTool.PrefabRoot + "/RepresentativeAdventurer.prefab",
            KayKitSetupTool.PrefabRoot + "/RepresentativeSkeleton.prefab",
            KayKitSetupTool.PrefabRoot + "/RepresentativeAccessory.prefab",
            KayKitSetupTool.PrefabRoot + "/RepresentativeDungeonPiece.prefab",
            KayKitSetupTool.PrefabRoot + "/RepresentativeDungeonWall.prefab",
            KayKitSetupTool.PrefabRoot + "/RepresentativeDungeonProp.prefab"
        };

        foreach (string path in prefabPaths)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty, path);
            Assert.That(
                renderers.SelectMany(renderer => renderer.sharedMaterials)
                    .Where(material => material != null)
                    .All(material => AssetDatabase.GetAssetPath(material)
                        .StartsWith(KayKitSetupTool.MaterialRoot, StringComparison.Ordinal)),
                Is.True,
                path);
            Assert.That(
                prefab.GetComponentsInChildren<Animator>(true)
                    .All(animator => !animator.applyRootMotion),
                Is.True,
                path);
        }
    }

    [Test]
    public void Regeneration_IsIdempotent()
    {
        KayKitSetupTool.RegenerateProjectAssets();
        string[] paths = AssetDatabase.FindAssets(string.Empty, new[] { KayKitSetupTool.ProjectRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(File.Exists)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Hash128[] before = paths.Select(AssetDatabase.GetAssetDependencyHash).ToArray();

        KayKitSetupTool.RegenerateProjectAssets();
        Hash128[] after = paths.Select(AssetDatabase.GetAssetDependencyHash).ToArray();

        Assert.That(after, Is.EqualTo(before));
    }
}
