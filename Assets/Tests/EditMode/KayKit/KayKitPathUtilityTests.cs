using Game.KayKit.Editor;
using NUnit.Framework;

public sealed class KayKitPathUtilityTests
{
    [Test]
    public void DungeonId_IsLowercasePackRelativePath()
    {
        string path =
            @"Assets\ThirdParty\KayKit\DungeonRemastered_1.1\Models\FBX\Environment\Floor_A.fbx";

        Assert.That(
            KayKitPathUtility.GetDungeonId(path),
            Is.EqualTo("dungeon/models/fbx/environment/floor_a"));
    }

    [TestCase("Idle_A", KayKitClipSemantics.Loop)]
    [TestCase("Walking_A", KayKitClipSemantics.Loop)]
    [TestCase("Melee_Blocking", KayKitClipSemantics.Loop)]
    [TestCase("Ranged_1H_Shooting", KayKitClipSemantics.Loop)]
    [TestCase("Push_Ups", KayKitClipSemantics.Loop)]
    [TestCase("Attack_1H_A", KayKitClipSemantics.OneShot)]
    [TestCase("Death_A", KayKitClipSemantics.OneShot)]
    [TestCase("Ranged_Magic_Spellcasting_Long", KayKitClipSemantics.OneShot)]
    [TestCase("Skeletons_Awaken_Floor", KayKitClipSemantics.OneShot)]
    [TestCase("T-Pose", KayKitClipSemantics.SetupPose)]
    [TestCase("Unclassified_Action", KayKitClipSemantics.Ambiguous)]
    public void ClipClassification_IsExplicit(string clipName, KayKitClipSemantics expected)
    {
        Assert.That(KayKitPathUtility.ClassifyClip(clipName), Is.EqualTo(expected));
    }

    [Test]
    public void VendorPathCheck_DoesNotAffectExistingAssets()
    {
        Assert.That(
            KayKitPathUtility.IsVendorAsset(
                "Assets/ThirdParty/KayKit/DungeonRemastered_1.1/Models/Floor.fbx"),
            Is.True);
        Assert.That(KayKitPathUtility.IsVendorAsset("Assets/Models/Tree.fbx"), Is.False);
        Assert.That(KayKitPathUtility.IsVendorAsset("Assets/Textures/Portrait.png"), Is.False);
    }
}
