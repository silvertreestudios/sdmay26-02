using System;
using System.Collections.Generic;
using System.Linq;
using Game.DungeonGeneration;
using NUnit.Framework;

public sealed class DungeonDoorPostProcessorTests
{
    [Test]
    public void SelectRequiredDoors_RemovesSecondEntranceToNearbyOutsidePath()
    {
        string[] rows =
        {
            "#######",
            "#######",
            ".D...##",
            ".#...##",
            ".D...##",
            "#######",
            "#######",
        };
        DungeonRoom room = new(1, 2, 2, 4, 4);
        DungeonDoor[] doors = Doors(new DungeonCell(1, 2), new DungeonCell(1, 4));

        IReadOnlyList<DungeonCell> retained = DungeonDoorPostProcessor.SelectRequiredDoors(
            rows,
            new[] { room },
            doors,
            DungeonDoorPostProcessor.MinimumLoopPathLengthCells
        );

        Assert.That(retained, Is.EqualTo(new[] { new DungeonCell(1, 2) }));
    }

    [Test]
    public void SelectRequiredDoors_KeepsLoopAtExactFortyFootThreshold()
    {
        string[] rows =
        {
            "##########",
            "##########",
            "....D...##",
            ".####...##",
            "....D...##",
            "##########",
            "##########",
        };
        DungeonRoom room = new(1, 5, 2, 7, 4);
        DungeonDoor[] doors = Doors(new DungeonCell(4, 2), new DungeonCell(4, 4));

        IReadOnlyList<DungeonCell> retained = DungeonDoorPostProcessor.SelectRequiredDoors(
            rows,
            new[] { room },
            doors,
            DungeonDoorPostProcessor.MinimumLoopPathLengthCells
        );

        Assert.That(retained, Is.EqualTo(doors.Select(door => door.Cell)));
    }

    [Test]
    public void SelectRequiredDoors_KeepsEntrancesToDisconnectedOutsideComponents()
    {
        string[] rows =
        {
            "#######",
            "#######",
            ".D...##",
            "##...##",
            ".D...##",
            "#######",
            "#######",
        };
        DungeonRoom room = new(1, 2, 2, 4, 4);
        DungeonDoor[] doors = Doors(new DungeonCell(1, 2), new DungeonCell(1, 4));

        IReadOnlyList<DungeonCell> retained = DungeonDoorPostProcessor.SelectRequiredDoors(
            rows,
            new[] { room },
            doors,
            DungeonDoorPostProcessor.MinimumLoopPathLengthCells
        );

        Assert.That(retained, Is.EqualTo(doors.Select(door => door.Cell)));
    }

    [Test]
    public void GeneratedFixtureSettings_ContainOnlyRequiredStableDoors()
    {
        DungeonGenerationResult result = new DeterministicDungeonGenerator().Generate(
            new DungeonGenerationRequest
            {
                RunSeed = 156,
                Width = 31,
                Height = 31,
                Layout = DungeonLayout.Box,
                RoomLayout = DungeonRoomLayout.Packed,
                CorridorLayout = DungeonCorridorLayout.Straight,
                MinimumRoomSize = 5,
                MaximumRoomSize = 13,
                MinimumRoomCount = 3,
                StairCount = 2,
                DeadEndRemovalPercent = 100,
            }
        );

        Assert.That(result.IsSuccess, Is.True, Diagnostics(result));
        IReadOnlyList<DungeonCell> retained = DungeonDoorPostProcessor.SelectRequiredDoors(
            result.Document.Rows,
            result.Document.Rooms,
            result.Document.Doors,
            DungeonDoorPostProcessor.MinimumLoopPathLengthCells
        );
        Assert.That(retained, Is.EqualTo(result.Document.Doors.Select(door => door.Cell)));
        Assert.That(
            result.Document.Doors.Select(door => door.Id),
            Is.EqualTo(
                Enumerable.Range(1, result.Document.Doors.Count).Select(index => $"door-{index:D4}")
            )
        );
    }

    private static DungeonDoor[] Doors(params DungeonCell[] cells)
    {
        return cells
            .Select((cell, index) => new DungeonDoor($"door-{index + 1:D4}", cell))
            .ToArray();
    }

    private static string Diagnostics(DungeonGenerationResult result)
    {
        return string.Join(
            Environment.NewLine,
            result.Diagnostics.Select(diagnostic => diagnostic.Message)
        );
    }
}
