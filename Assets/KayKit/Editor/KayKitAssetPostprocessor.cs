using System;
using UnityEditor;
using UnityEngine;

namespace Game.KayKit.Editor
{
    public sealed class KayKitAssetPostprocessor : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!KayKitPathUtility.IsVendorAsset(assetPath))
                return;

            ModelImporter importer = (ModelImporter)assetImporter;
            importer.globalScale = 1.0f;
            importer.addCollider = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.isReadable = false;

            if (KayKitPathUtility.IsAnimationSource(assetPath))
            {
                ConfigureHumanoid(importer, true);
            }
            else if (KayKitPathUtility.IsCharacterModel(assetPath))
            {
                ConfigureHumanoid(importer, false);
            }
            else
            {
                importer.animationType = ModelImporterAnimationType.None;
                importer.importAnimation = false;
            }
        }

        private void OnPreprocessAnimation()
        {
            if (!KayKitPathUtility.IsAnimationSource(assetPath))
                return;

            ConfigureAnimationClips((ModelImporter)assetImporter);
        }

        private void OnPreprocessTexture()
        {
            if (!KayKitPathUtility.IsVendorAsset(assetPath))
                return;

            TextureImporter importer = (TextureImporter)assetImporter;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 1024;
            importer.isReadable = false;
            importer.textureType = TextureImporterType.Default;
        }

        private static void ConfigureHumanoid(ModelImporter importer, bool importAnimation)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = importAnimation;
        }

        private static void ConfigureAnimationClips(ModelImporter importer)
        {
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                KayKitClipSemantics semantics = KayKitPathUtility.ClassifyClip(clip.name);
                clip.loopTime = semantics == KayKitClipSemantics.Loop;
                clip.loopPose = semantics == KayKitClipSemantics.Loop;
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
            }

            if (clips.Length > 0)
                importer.clipAnimations = clips;
        }
    }
}
