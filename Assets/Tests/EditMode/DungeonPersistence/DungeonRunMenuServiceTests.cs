using System;
using System.IO;
using Game.Creature;
using Game.DungeonGeneration;
using Game.DungeonPersistence;
using Game.DungeonPersistence.Repository;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>Verifies player-facing dungeon launch input and production scene routing.</summary>
public sealed class DungeonRunMenuServiceTests
{
    private string autosaveDirectory;

    [SetUp]
    public void SetUp()
    {
        autosaveDirectory = Path.GetFullPath(
            Path.Combine(".agent-temp", "menu-service-" + Guid.NewGuid().ToString("N"))
        );
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(autosaveDirectory))
            Directory.Delete(autosaveDirectory, recursive: true);
    }

    [TestCase("0", 0)]
    [TestCase("-2147483648", int.MinValue)]
    [TestCase("2147483647", int.MaxValue)]
    [TestCase("9223372036854775807", int.MinValue)]
    [TestCase("-9223372036854775808", int.MinValue)]
    public void ExplicitSignedDecimalSeedProducesStableNormalizedRequest(string text, int expected)
    {
        DungeonRunMenuService service = new(autosaveDirectory, () => 42L);

        bool accepted = service.TryCreateNewRunRequest(
            text,
            out DungeonRunLaunchRequest request,
            out string error
        );

        Assert.That(accepted, Is.True, error);
        Assert.That(request.Mode, Is.EqualTo(DungeonRunLaunchMode.NewRun));
        Assert.That(request.NormalizedSeed, Is.EqualTo(expected));
        Assert.That(request.AutosaveDirectory, Is.EqualTo(autosaveDirectory));
    }

    [Test]
    public void BlankSeedUsesInjectedSystemEntropyAndNormalizesIt()
    {
        const long entropy = 0x00000001FFFFFFFFL;
        DungeonRunMenuService service = new(autosaveDirectory, () => entropy);

        bool accepted = service.TryCreateNewRunRequest(
            "   ",
            out DungeonRunLaunchRequest request,
            out string error
        );

        Assert.That(accepted, Is.True, error);
        Assert.That(request.NormalizedSeed, Is.EqualTo(-2));
    }

    [TestCase("1.5")]
    [TestCase("0x10")]
    [TestCase("9223372036854775808")]
    [TestCase("--4")]
    public void InvalidSeedReturnsPlayerFacingRangeMessage(string text)
    {
        DungeonRunMenuService service = new(autosaveDirectory, () => 42L);

        bool accepted = service.TryCreateNewRunRequest(
            text,
            out DungeonRunLaunchRequest request,
            out string error
        );

        Assert.That(accepted, Is.False);
        Assert.That(request, Is.SameAs(DungeonRunLaunchRequest.None));
        Assert.That(error, Does.Contain("-9223372036854775808"));
        Assert.That(error, Does.Contain("9223372036854775807"));
    }

    [Test]
    public void MissingCorruptAndCompatibleAutosavesDriveContinueStatus()
    {
        DungeonRunMenuService service = new(autosaveDirectory, () => 42L);

        DungeonRunMenuStatus missing = service.InspectAutosave();
        Assert.That(missing.HasAutosave, Is.False);
        Assert.That(missing.CanContinue, Is.False);
        Assert.That(missing.Message, Does.Contain("No saved"));

        Directory.CreateDirectory(autosaveDirectory);
        File.WriteAllText(Path.Combine(autosaveDirectory, "autosave.json"), "{}");
        DungeonRunMenuStatus corrupt = service.InspectAutosave();
        Assert.That(corrupt.HasAutosave, Is.True);
        Assert.That(corrupt.CanContinue, Is.False);
        Assert.That(corrupt.Message, Does.Contain("corrupt"));

        FileSystemDungeonSaveRepository repository = new(autosaveDirectory);
        DungeonSaveResult<bool> saved = repository.Save(CreateValidSave(73, 0));
        Assert.That(
            saved.IsSuccess,
            Is.True,
            saved.IsSuccess
                ? string.Empty
                : $"{saved.Diagnostics[0].Code}: {saved.Diagnostics[0].Path}: {saved.Diagnostics[0].Message}"
        );

        DungeonRunMenuStatus compatible = service.InspectAutosave();
        Assert.That(compatible.HasAutosave, Is.True);
        Assert.That(compatible.CanContinue, Is.True);
        Assert.That(compatible.StartingSeed, Is.EqualTo(73));
        Assert.That(compatible.CurrentDepth, Is.Zero);
        Assert.That(compatible.Message, Does.Contain("seed 73"));

        string currentJson = File.ReadAllText(repository.AutosavePath);
        string incompatibleGeneratorJson = currentJson.Replace(
            DeterministicDungeonGenerator.AlgorithmId,
            "future-generator",
            StringComparison.Ordinal
        );
        Assert.That(incompatibleGeneratorJson, Is.Not.EqualTo(currentJson));
        File.WriteAllText(repository.AutosavePath, incompatibleGeneratorJson);
        DungeonRunMenuStatus incompatibleGenerator = service.InspectAutosave();
        Assert.That(incompatibleGenerator.HasAutosave, Is.True);
        Assert.That(incompatibleGenerator.CanContinue, Is.False);
        Assert.That(incompatibleGenerator.Message, Does.Contain("incompatible version"));

        File.WriteAllText(repository.AutosavePath, currentJson);
        string incompatibleJson = currentJson.Replace(
            "\"DocumentVersion\":3",
            "\"DocumentVersion\":99",
            StringComparison.Ordinal
        );
        Assert.That(incompatibleJson, Is.Not.EqualTo(currentJson));
        File.WriteAllText(repository.AutosavePath, incompatibleJson);
        DungeonRunMenuStatus incompatible = service.InspectAutosave();
        Assert.That(incompatible.HasAutosave, Is.True);
        Assert.That(incompatible.CanContinue, Is.False);
        Assert.That(incompatible.Message, Does.Contain("incompatible version"));
    }

    [Test]
    public void ProductionBuildUsesReusableDungeonAndKeepsReferenceScenesAsAssets()
    {
        string[] buildScenes = Array.ConvertAll(EditorBuildSettings.scenes, scene => scene.path);
        Assert.That(
            buildScenes,
            Is.EqualTo(
                new[]
                {
                    "Assets/Scenes/MainMenuScene.unity",
                    "Assets/Scenes/CharacterCreationScene.unity",
                    "Assets/Scenes/ProceduralDungeon.unity",
                    "Assets/Scenes/UnitTestingScene.unity",
                }
            )
        );
        Assert.That(buildScenes, Does.Not.Contain("Assets/Scenes/Level1.unity"));
        Assert.That(buildScenes, Does.Not.Contain("Assets/Scenes/Level2.unity"));
        Assert.That(buildScenes, Does.Not.Contain("Assets/Scenes/Level3.unity"));

        foreach (
            string referencePath in new[]
            {
                "Assets/Scenes/Level1.unity",
                "Assets/Scenes/Level2.unity",
                "Assets/Scenes/Level3.unity",
                "Assets/Scenes/KayKitDungeonExample.unity",
            }
        )
        {
            Assert.That(
                AssetDatabase.LoadAssetAtPath<SceneAsset>(referencePath),
                Is.Not.Null,
                referencePath
            );
        }
    }

    private static DungeonRunSave CreateValidSave(int seed, int depth, bool isDefeated = false)
    {
        DungeonGenerationResult generated = new DeterministicDungeonGenerator().Generate(
            new DungeonGenerationRequest
            {
                RunSeed = seed,
                Depth = depth,
                StairCount = 0,
            }
        );
        if (!generated.IsSuccess)
        {
            throw new InvalidOperationException(generated.Diagnostics[0].Message);
        }

        DungeonLevelDocument source = generated.Document;
        DungeonLevelDocument floor = new(
            source.Generation,
            source.Rows,
            source.Rooms,
            source.Doors,
            source.Stairs,
            source.StartCell,
            source.SafeCells,
            source.Objects,
            source.EncounterPlans,
            new DungeonRuntimeState(
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>()
            )
        );
        DungeonActorSaveState actorState = new()
        {
            TemporaryHitPoints = 0,
            TemporaryHitPointSource = string.Empty,
            TemporaryHitPointImmunities = Array.Empty<string>(),
            Conditions = Array.Empty<DungeonConditionSaveState>(),
            TimedEffects = Array.Empty<DungeonTimedEffectSaveState>(),
            RulesSpellEffects = Array.Empty<DungeonRulesSpellEffectSaveState>(),
            PreparedEffects = Array.Empty<DungeonPreparedEffectSaveState>(),
            Equipment = new DungeonEquipmentSaveState
            {
                LeftHandId = string.Empty,
                RightHandId = string.Empty,
                ArmorId = string.Empty,
                Ammunition = Array.Empty<AmmoCount>(),
                UnloadedWeaponIds = Array.Empty<string>(),
            },
        };
        DungeonPartyMemberSaveState party = new()
        {
            RosterSlotId = "party-slot",
            CreatureContentId = "party-content",
            CellX = floor.StartCell.X,
            CellZ = floor.StartCell.Z,
            CurrentHitPoints = isDefeated ? 0 : 12,
            IsDefeated = isDefeated,
            State = actorState,
        };
        return DungeonRunSave.CreateNew(new[] { party }, floor);
    }

    [Test]
    public void TotalPartyDefeatDisablesContinue()
    {
        DungeonRunMenuService service = new(autosaveDirectory, () => 42L);
        FileSystemDungeonSaveRepository repository = new(autosaveDirectory);
        DungeonSaveResult<bool> saved = repository.Save(CreateValidSave(73, 0, isDefeated: true));
        Assert.That(
            saved.IsSuccess,
            Is.True,
            saved.IsSuccess
                ? string.Empty
                : $"{saved.Diagnostics[0].Code}: {saved.Diagnostics[0].Path}: {saved.Diagnostics[0].Message}"
        );

        DungeonRunMenuStatus terminal = service.InspectAutosave();

        Assert.That(terminal.HasAutosave, Is.True);
        Assert.That(terminal.CanContinue, Is.False);
        Assert.That(terminal.Message, Does.Contain("defeated"));
    }
}
