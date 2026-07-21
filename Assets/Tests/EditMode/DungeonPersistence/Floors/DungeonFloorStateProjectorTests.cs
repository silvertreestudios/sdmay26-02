using System;
using System.Linq;
using Game.Combat.Encounters;
using Game.DungeonGeneration;
using Game.DungeonPersistence;
using NUnit.Framework;

public sealed class DungeonFloorStateProjectorTests
{
    [Test]
    public void ProjectValidated_MirrorsRuntimeFlagsWithoutMutatingSource()
    {
        DungeonLevelDocument source = CreateSource();
        DungeonDoor door = source.Doors[0];
        DungeonEncounterPlan encounter = source.EncounterPlans[0];
        string defeatedId = DungeonEncounterStateMachine.CreateCreatureInstanceId(encounter.Id, 0);
        DungeonRuntimeState runtime = new(
            new[] { door.Id },
            new[] { encounter.Id },
            new[] { defeatedId }
        );

        DungeonLevelDocument projected = DungeonFloorStateProjector.ProjectValidated(
            source,
            runtime
        );

        Assert.That(source.Doors.Single(candidate => candidate.Id == door.Id).IsOpen, Is.False);
        Assert.That(source.EncounterPlans[0].IsResolved, Is.False);
        Assert.That(projected.Doors.Single(candidate => candidate.Id == door.Id).IsOpen, Is.True);
        Assert.That(projected.EncounterPlans[0].IsResolved, Is.True);
        Assert.That(projected.RuntimeState.OpenDoorIds, Is.EqualTo(new[] { door.Id }));
        Assert.That(
            DungeonLevelJsonSerializer.Serialize(projected),
            Is.EqualTo(
                DungeonLevelJsonSerializer.Serialize(
                    DungeonFloorStateProjector.ProjectValidated(source, runtime)
                )
            )
        );
    }

    [Test]
    public void ProjectValidated_RejectsUnknownOrDuplicateRuntimeIds()
    {
        DungeonLevelDocument source = CreateSource();

        Assert.That(
            () =>
                DungeonFloorStateProjector.ProjectValidated(
                    source,
                    new DungeonRuntimeState(
                        new[] { "unknown-door" },
                        Array.Empty<string>(),
                        Array.Empty<string>()
                    )
                ),
            Throws.ArgumentException
        );
        Assert.That(
            () =>
                DungeonFloorStateProjector.ProjectValidated(
                    source,
                    new DungeonRuntimeState(
                        new[] { source.Doors[0].Id, source.Doors[0].Id },
                        Array.Empty<string>(),
                        Array.Empty<string>()
                    )
                ),
            Throws.ArgumentException
        );
    }

    private static DungeonLevelDocument CreateSource()
    {
        DungeonGenerationResult generated = new DeterministicDungeonGenerator().Generate(
            new DungeonGenerationRequest
            {
                RunSeed = 156,
                Width = 31,
                Height = 31,
                MinimumRoomSize = 5,
                MaximumRoomSize = 13,
                MinimumRoomCount = 3,
                StairCount = 2,
                DeadEndRemovalPercent = 100,
            }
        );
        Assert.That(generated.IsSuccess, Is.True);
        Assert.That(generated.Document.Doors, Is.Not.Empty);
        DungeonRoom room = generated.Document.Rooms[0];
        DungeonCell spawn = new(room.MinimumX, room.MinimumZ);
        DungeonEncounterPlan encounter = new(
            "encounter-save-test",
            room.Id,
            DungeonEncounterThreat.Trivial,
            40,
            new[] { spawn },
            new[] { "goblin-warrior" }
        );
        DungeonLevelDocument source = generated.Document;
        return new DungeonLevelDocument(
            source.Generation,
            source.Rows,
            source.Rooms,
            source.Doors,
            source.Stairs,
            source.StartCell,
            source.SafeCells,
            source.Objects,
            new[] { encounter }
        );
    }
}
