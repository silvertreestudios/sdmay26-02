using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Creature;
using Game.Creature.Rules;
using Game.DungeonGeneration;
using Game.KayKit;
using GridPrivate;
using GridPublic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class ProceduralDungeonScenePlayModeTests
{
    private const string ScenePath = "Assets/Scenes/ProceduralDungeon.unity";

    [UnityTest]
    public IEnumerator SceneInitializesNavigationDoorsStairsDecorationsAndLineOfSight()
    {
        AsyncOperation load = EditorSceneManager.LoadSceneAsyncInPlayMode(
            ScenePath,
            new LoadSceneParameters(LoadSceneMode.Single)
        );
        while (!load.isDone)
            yield return null;
        yield return null;

        Map map = Object.FindFirstObjectByType<Map>();
        GridBase grid = Object.FindFirstObjectByType<GridBase>();
        GeneratedMapRoot generated = Object.FindFirstObjectByType<GeneratedMapRoot>();
        Assert.That(map, Is.Not.Null);
        Assert.That(grid, Is.Not.Null);
        Assert.That(generated, Is.Not.Null);
        Assert.That(map.JsonSource.name, Is.EqualTo("GeneratedDungeonFixture"));
        Assert.That(grid.IsInitialized, Is.True);
        Transform structure = generated.transform.Find("Structure");
        Assert.That(structure, Is.Not.Null);

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(map.JsonSource.text);
        Assert.That(parsed.IsSuccess, Is.True);
        DungeonLevelDocument document = parsed.Document;
        Assert.That(
            document.EncounterPlans,
            Is.Not.Empty,
            "The reusable generated-floor fixture must exercise room-scoped encounters."
        );
        Assert.That(grid.GridData.GetLength(0), Is.EqualTo(document.Width));
        Assert.That(grid.GridData.GetLength(1), Is.EqualTo(document.Height));
        Assert.That(
            CountGeneratedWalls(structure),
            Is.EqualTo(CountExposedWalls(grid.GridData)),
            "The scene must contain the wall shell and masked boundary around walkable cells."
        );
        Assert.That(
            CountGeneratedFloors(structure),
            Is.EqualTo(CountFloorBearingCells(grid.GridData) + CountExposedWalls(grid.GridData)),
            "Every visible wall shell cell must have a floor tile underneath it."
        );
        Assert.That(
            structure
                .GetComponentsInChildren<Wall>(true)
                .All(wall => wall.SelectedVariant != WallVariant.Endcap),
            Is.True,
            "A wall with one structural neighbor must use a full straight segment, not a pillar-like endcap."
        );
        Assert.That(
            TryFindInteriorWall(grid.GridData, out Vector2Int interiorWall),
            Is.True,
            "The fixture must retain at least one solid interior cell for regression coverage."
        );
        Assert.That(grid.GridData[interiorWall.x, interiorWall.y], Is.EqualTo(TileType.Wall));
        Assert.That(
            structure.Find($"Wall_{interiorWall.x:D3}_{interiorWall.y:D3}"),
            Is.Null,
            "Solid interior cells remain blocked data but must not create scene geometry."
        );
        Assert.That(
            structure.Find($"Floor_{interiorWall.x:D3}_{interiorWall.y:D3}"),
            Is.Null,
            "Solid interior cells must not create hidden floor geometry."
        );

        DungeonRoom[] roomsAtMaskedBoundary = document
            .Rooms.Where(room => MaskedBoundaryCells(room, grid.GridData).Count > 0)
            .ToArray();
        Assert.That(
            roomsAtMaskedBoundary,
            Has.Length.EqualTo(2),
            "The fixture must retain both rooms bordering the center mask for regression coverage."
        );
        foreach (DungeonRoom room in roomsAtMaskedBoundary)
        {
            IReadOnlyList<DungeonCell> boundary = MaskedBoundaryCells(room, grid.GridData);
            Assert.That(boundary, Is.Not.Empty);
            foreach (DungeonCell cell in boundary)
            {
                Assert.That(grid.GridData[cell.X, cell.Z], Is.EqualTo(TileType.Empty));
                Assert.That(
                    structure.Find($"Wall_{cell.X:D3}_{cell.Z:D3}"),
                    Is.Not.Null,
                    $"Room {room.Id} must have a wall against masked cell ({cell.X},{cell.Z})."
                );
                Assert.That(
                    structure.Find($"Floor_{cell.X:D3}_{cell.Z:D3}"),
                    Is.Not.Null,
                    $"Room {room.Id}'s masked boundary wall must have a floor beneath it."
                );
            }
        }

        int centerX = (document.Width - 1) / 2;
        int centerZ = (document.Height - 1) / 2;
        Assert.That(grid.GridData[centerX, centerZ], Is.EqualTo(TileType.Empty));
        Assert.That(
            structure.Find($"Wall_{centerX:D3}_{centerZ:D3}"),
            Is.Null,
            "The interior of the center mask must remain visually empty."
        );
        Assert.That(
            structure.Find($"Floor_{centerX:D3}_{centerZ:D3}"),
            Is.Null,
            "The interior of the center mask must not receive floor geometry."
        );

        Transform firstStraightWall = structure.Find("Wall_011_000");
        Transform secondStraightWall = structure.Find("Wall_012_000");
        Assert.That(firstStraightWall, Is.Not.Null);
        Assert.That(secondStraightWall, Is.Not.Null);
        Renderer firstStraightRenderer = firstStraightWall
            .GetComponentsInChildren<Renderer>(false)
            .Single();
        Renderer secondStraightRenderer = secondStraightWall
            .GetComponentsInChildren<Renderer>(false)
            .Single();
        Assert.That(firstStraightRenderer.bounds.size.x, Is.EqualTo(1f).Within(0.001f));
        Assert.That(firstStraightRenderer.bounds.size.y, Is.EqualTo(1f).Within(0.001f));
        Assert.That(
            firstStraightRenderer.bounds.max.x,
            Is.EqualTo(secondStraightRenderer.bounds.min.x).Within(0.001f),
            "Adjacent one-unit wall segments must touch without overlapping."
        );

        DungeonDoorController[] doors = Object.FindObjectsByType<DungeonDoorController>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );
        DungeonStairMarker[] stairs = Object.FindObjectsByType<DungeonStairMarker>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );
        Assert.That(doors, Has.Length.EqualTo(document.Doors.Count));
        Assert.That(stairs, Has.Length.EqualTo(document.Stairs.Count));
        Assert.That(
            stairs.Select(stair => stair.Kind),
            Is.EquivalentTo(document.Stairs.Select(stair => stair.Kind))
        );
        Assert.That(stairs.All(stair => Manhattan(stair.Cell, stair.ArrivalCell) == 1), Is.True);
        Assert.That(
            stairs.All(stair => grid.GetTiles()[stair.Cell.X, stair.Cell.Z] != null),
            Is.True,
            "Semantic stair endpoints must remain walkable on their generated floor."
        );
        foreach (DungeonStairMarker stair in stairs)
        {
            Transform visual = stair.transform.Find("Visual");
            Transform model = visual.Find("Model");
            Assert.That(visual.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(
                Mathf.DeltaAngle(visual.localEulerAngles.y, 180f),
                Is.EqualTo(0f).Within(0.001f)
            );
            Assert.That(model.localPosition, Is.EqualTo(new Vector3(0f, 0f, -0.85f)));
            Assert.That(model.localScale, Is.EqualTo(Vector3.one * 0.25f));
        }

        Transform objects = generated.transform.Find("Objects");
        Assert.That(objects, Is.Not.Null);
        Assert.That(objects.childCount, Is.EqualTo(document.Objects.Count));
        Assert.That(document.Objects, Is.Not.Empty);
        Assert.That(
            document.Objects.All(placement =>
                placement.AssetId.EndsWith("/banner_red")
                || placement.AssetId.EndsWith("/torch_mounted")
            ),
            Is.True
        );
        Assert.That(
            document.Objects.All(placement =>
                grid.GetTiles()[placement.Cell.X, placement.Cell.Z] != null
            ),
            Is.True,
            "Wall decorations must retain their adjacent walkable floor cells."
        );
        KayKitDungeonMapParseResult projected = KayKitDungeonMapParser.Parse(
            map.JsonSource.text,
            map.DungeonCatalog
        );
        Assert.That(projected.IsValid, Is.True, string.Join("\n", projected.Errors));
        for (int index = 0; index < projected.Map.Objects.Count; index++)
        {
            KayKitDungeonObjectPlacement placement = projected.Map.Objects[index];
            string namePrefix = $"Object_{index:D3}_";
            Transform instance = objects
                .Cast<Transform>()
                .Single(candidate =>
                    candidate.name.StartsWith(namePrefix, System.StringComparison.Ordinal)
                );
            DungeonPlacementOffset placementOffset =
                instance.GetComponent<DungeonPlacementOffset>();
            Quaternion rotation = Quaternion.Euler(0f, placement.Rotation, 0f);
            Vector3 expectedPosition =
                new Vector3(placement.X, placement.YOffset, placement.Z)
                + rotation * placementOffset.LocalOffset;
            AssertVector3(instance.position, expectedPosition, instance.name + " root position");
            Assert.That(Quaternion.Angle(instance.rotation, rotation), Is.LessThan(0.001f));
            Assert.That(instance.localScale, Is.EqualTo(Vector3.one));

            Transform model = instance.Find("Model");
            Assert.That(model.localPosition, Is.EqualTo(Vector3.zero));
            if (placement.AssetId.EndsWith("/torch_mounted"))
            {
                Assert.That(
                    placementOffset.LocalOffset,
                    Is.EqualTo(new Vector3(0f, 0.35f, -0.925f))
                );
                Assert.That(model.localScale, Is.EqualTo(Vector3.one * 0.5f));
                Assert.That(
                    instance.Find("TorchLight").localPosition,
                    Is.EqualTo(new Vector3(0f, 1.5f, 0.2f))
                );
            }
            else
            {
                Assert.That(placementOffset.LocalOffset, Is.EqualTo(new Vector3(0f, -0.25f, -1f)));
                Assert.That(model.localScale, Is.EqualTo(Vector3.one * 0.25f));
            }
        }

        DungeonDoorController door = doors.OrderBy(candidate => candidate.StableId).First();
        DungeonDoor record = document.Doors.Single(candidate => candidate.Id == door.StableId);
        (DungeonCell firstSide, DungeonCell secondSide) = OppositeWalkableSides(
            document,
            record.Cell
        );
        Vector3Int start = new(firstSide.X, 0, firstSide.Z);
        Vector3Int end = new(secondSide.X, 0, secondSide.Z);
        Transform floor = structure.Find($"Floor_{record.Cell.X:D3}_{record.Cell.Z:D3}");
        Assert.That(floor, Is.Not.Null);

        Assert.That(door.IsOpen, Is.False);
        Assert.That(grid.GridData[record.Cell.X, record.Cell.Z], Is.EqualTo(TileType.ClosedDoor));
        Assert.That(grid.GetTiles()[record.Cell.X, record.Cell.Z], Is.Null);
        Assert.That(grid.GetLineOfSightBlocks()[record.Cell.X, record.Cell.Z], Is.True);
        Assert.That(grid.GetPathfinder().Pathfind(null, start, end), Is.Null.Or.Empty);
        Assert.That(door.transform.Find("ClosedVisual").gameObject.activeSelf, Is.True);
        Assert.That(door.transform.Find("OpenVisual").gameObject.activeSelf, Is.False);
        Assert.That(door.GetComponentInChildren<MapLineOfSightBlocker>(true), Is.Not.Null);

        Assert.That(door.TryOpen(), Is.True);
        yield return null;

        Assert.That(door.IsOpen, Is.True);
        Assert.That(grid.GridData[record.Cell.X, record.Cell.Z], Is.EqualTo(TileType.Door));
        Assert.That(grid.GetTiles()[record.Cell.X, record.Cell.Z], Is.Not.Null);
        Assert.That(grid.GetLineOfSightBlocks()[record.Cell.X, record.Cell.Z], Is.False);
        Assert.That(grid.GetPathfinder().Pathfind(null, start, end), Is.Not.Null.And.Not.Empty);
        Assert.That(door.transform.Find("ClosedVisual").gameObject.activeSelf, Is.False);
        Assert.That(door.transform.Find("OpenVisual").gameObject.activeSelf, Is.True);
        Assert.That(
            structure.Find($"Floor_{record.Cell.X:D3}_{record.Cell.Z:D3}"),
            Is.SameAs(floor),
            "Opening a door must not rebuild its floor geometry."
        );
        Assert.That(door.TryOpen(), Is.True, "Opening an open V1 door remains idempotent.");
    }

    [UnityTest]
    public IEnumerator RuntimeRepopulationRebindsLiveGridAndImmediatelyDeactivatesPriorGeometry()
    {
        AsyncOperation load = EditorSceneManager.LoadSceneAsyncInPlayMode(
            ScenePath,
            new LoadSceneParameters(LoadSceneMode.Single)
        );
        while (!load.isDone)
            yield return null;
        yield return null;

        Map map = Object.FindFirstObjectByType<Map>();
        GridBase grid = Object.FindFirstObjectByType<GridBase>();
        GeneratedMapRoot priorRoot = Object.FindFirstObjectByType<GeneratedMapRoot>();
        Tile[,] priorTiles = grid.GetTiles();
        GridFSM priorFsm = grid.Fsm;
        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(map.JsonSource.text);
        DungeonCell tokenCell = parsed.Document.StartCell;
        GameObject tokenObject = new("Runtime Repopulation Token");
        tokenObject.transform.position = new Vector3(tokenCell.X, 0f, tokenCell.Z);
        tokenObject.AddComponent<Token>();
        Assert.That(priorTiles[tokenCell.X, tokenCell.Z].Occupants, Contains.Item(tokenObject));
        DungeonCell inactiveCell = parsed.Document.SafeCells.First(cell =>
            cell != tokenCell && priorTiles[cell.X, cell.Z] != null
        );
        GameObject inactiveTokenObject = new("Inactive Runtime Repopulation Token");
        inactiveTokenObject.transform.position = new Vector3(inactiveCell.X, 0f, inactiveCell.Z);
        inactiveTokenObject.AddComponent<Token>();
        Assert.That(
            priorTiles[inactiveCell.X, inactiveCell.Z].Occupants,
            Contains.Item(inactiveTokenObject)
        );
        inactiveTokenObject.SetActive(false);

        Assert.That(
            map.TryPopulateJson(
                map.JsonSource.text,
                map.DungeonCatalog,
                out MapSourceValidationResult validation
            ),
            Is.True,
            string.Join(System.Environment.NewLine, validation.Errors)
        );

        Assert.That(
            priorRoot.gameObject.activeInHierarchy,
            Is.False,
            "Deferred destruction must not leave the prior generation active for the rest of the frame."
        );
        Assert.That(grid.IsInitialized, Is.True);
        Assert.That(grid.GridData, Is.SameAs(map.GetMapData()));
        Assert.That(grid.GetLineOfSightBlocks(), Is.SameAs(map.GetLineOfSightBlocks()));
        Assert.That(grid.GetTiles(), Is.Not.SameAs(priorTiles));
        Assert.That(grid.Fsm, Is.SameAs(priorFsm));
        Assert.That(grid.GetPathfinder(), Is.Not.Null);
        Assert.That(
            grid.GetTiles()[tokenCell.X, tokenCell.Z].Occupants,
            Contains.Item(tokenObject)
        );
        Assert.That(
            grid.GetTiles()[inactiveCell.X, inactiveCell.Z].Occupants.Contains(inactiveTokenObject),
            Is.False
        );
        inactiveTokenObject.SetActive(true);
        Assert.That(
            grid.GetTiles()[inactiveCell.X, inactiveCell.Z].Occupants,
            Contains.Item(inactiveTokenObject)
        );
        Assert.That(grid.GetComponent<GridInput>().enabled, Is.True);

        Object.Destroy(tokenObject);
        Object.Destroy(inactiveTokenObject);
        yield return null;
    }

    [UnityTest]
    public IEnumerator RuntimeRepopulationRejectsAReplacementThatCannotPlaceLiveTokens()
    {
        AsyncOperation load = EditorSceneManager.LoadSceneAsyncInPlayMode(
            ScenePath,
            new LoadSceneParameters(LoadSceneMode.Single)
        );
        while (!load.isDone)
            yield return null;
        yield return null;

        Map map = Object.FindFirstObjectByType<Map>();
        GridBase grid = Object.FindFirstObjectByType<GridBase>();
        GeneratedMapRoot priorRoot = Object.FindFirstObjectByType<GeneratedMapRoot>();
        TileType[,] priorGridData = grid.GridData;
        bool[,] priorLineOfSightBlocks = grid.GetLineOfSightBlocks();
        Tile[,] priorTiles = grid.GetTiles();
        IPathfinder priorPathfinder = grid.GetPathfinder();
        GridFSM priorFsm = grid.Fsm;
        MapSourceMode priorSourceMode = map.SourceMode;
        bool priorUsesRuntimeSource = map.UsesRuntimeJsonSource;
        float priorSpacing = map.Spacing;
        const int replacementSize = 15;
        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(map.JsonSource.text);
        DungeonCell highCell = parsed
            .Document.Rooms.SelectMany(room =>
                Enumerable
                    .Range(room.MinimumZ, room.MaximumZ - room.MinimumZ + 1)
                    .SelectMany(z =>
                        Enumerable
                            .Range(room.MinimumX, room.MaximumX - room.MinimumX + 1)
                            .Select(x => new DungeonCell(x, z))
                    )
            )
            .First(cell =>
                (cell.X >= replacementSize || cell.Z >= replacementSize)
                && priorTiles[cell.X, cell.Z] != null
            );
        GameObject tokenObject = new("Token Outside Replacement Bounds");
        tokenObject.transform.position = new Vector3(highCell.X, 0f, highCell.Z);
        tokenObject.AddComponent<Token>();
        Assert.That(priorTiles[highCell.X, highCell.Z].Occupants, Contains.Item(tokenObject));
        DungeonGenerationResult replacement = new DeterministicDungeonGenerator().Generate(
            new DungeonGenerationRequest
            {
                RunSeed = 15601,
                Width = replacementSize,
                Height = replacementSize,
                MinimumRoomCount = 0,
                StairCount = 0,
            }
        );
        Assert.That(replacement.IsSuccess, Is.True);

        Assert.That(
            map.TryPopulateJson(
                DungeonLevelJsonSerializer.Serialize(replacement.Document),
                map.DungeonCatalog,
                out MapSourceValidationResult validation
            ),
            Is.False
        );

        Assert.That(validation.Errors, Is.Not.Empty);
        Assert.That(Object.FindFirstObjectByType<GeneratedMapRoot>(), Is.SameAs(priorRoot));
        Assert.That(map.transform.Find("GeneratedMap"), Is.SameAs(priorRoot.transform));
        Assert.That(priorRoot.gameObject.activeInHierarchy, Is.True);
        Assert.That(map.GetMapData(), Is.SameAs(priorGridData));
        Assert.That(map.GetLineOfSightBlocks(), Is.SameAs(priorLineOfSightBlocks));
        Assert.That(map.SourceMode, Is.EqualTo(priorSourceMode));
        Assert.That(map.UsesRuntimeJsonSource, Is.EqualTo(priorUsesRuntimeSource));
        Assert.That(map.Spacing, Is.EqualTo(priorSpacing));
        Assert.That(grid.GridData, Is.SameAs(priorGridData));
        Assert.That(grid.GetLineOfSightBlocks(), Is.SameAs(priorLineOfSightBlocks));
        Assert.That(grid.GetTiles(), Is.SameAs(priorTiles));
        Assert.That(grid.GetPathfinder(), Is.SameAs(priorPathfinder));
        Assert.That(grid.Fsm, Is.SameAs(priorFsm));
        Assert.That(priorTiles[highCell.X, highCell.Z].Occupants, Contains.Item(tokenObject));

        yield return null;
        Assert.That(
            Object.FindObjectsByType<GeneratedMapRoot>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            ),
            Is.EqualTo(new[] { priorRoot })
        );

        Object.Destroy(tokenObject);
        yield return null;
    }

    [UnityTest]
    public IEnumerator RuntimeRepopulationRetainsInactiveTokenReservationAcrossRebinds()
    {
        AsyncOperation load = EditorSceneManager.LoadSceneAsyncInPlayMode(
            ScenePath,
            new LoadSceneParameters(LoadSceneMode.Single)
        );
        while (!load.isDone)
            yield return null;
        yield return null;

        Map map = Object.FindFirstObjectByType<Map>();
        GridBase grid = Object.FindFirstObjectByType<GridBase>();
        const int replacementSize = 15;
        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(map.JsonSource.text);
        DungeonCell highCell = parsed
            .Document.Rooms.SelectMany(room =>
                Enumerable
                    .Range(room.MinimumZ, room.MaximumZ - room.MinimumZ + 1)
                    .SelectMany(z =>
                        Enumerable
                            .Range(room.MinimumX, room.MaximumX - room.MinimumX + 1)
                            .Select(x => new DungeonCell(x, z))
                    )
            )
            .First(cell =>
                (cell.X >= replacementSize || cell.Z >= replacementSize)
                && grid.GetTiles()[cell.X, cell.Z] != null
            );
        GameObject tokenObject = new("Inactive Token Reserved Across Rebinds");
        tokenObject.transform.position = new Vector3(highCell.X, 0f, highCell.Z);
        tokenObject.AddComponent<Token>();
        Assert.That(grid.GetTiles()[highCell.X, highCell.Z].Occupants, Contains.Item(tokenObject));
        tokenObject.SetActive(false);

        Assert.That(
            map.TryPopulateJson(
                map.JsonSource.text,
                map.DungeonCatalog,
                out MapSourceValidationResult firstValidation
            ),
            Is.True,
            string.Join(System.Environment.NewLine, firstValidation.Errors)
        );
        Tile[,] fullSizeTiles = grid.GetTiles();
        Assert.That(fullSizeTiles[highCell.X, highCell.Z].Occupants, Has.No.Member(tokenObject));

        DungeonGenerationResult replacement = new DeterministicDungeonGenerator().Generate(
            new DungeonGenerationRequest
            {
                RunSeed = 15603,
                Width = replacementSize,
                Height = replacementSize,
                MinimumRoomCount = 0,
                StairCount = 0,
            }
        );
        Assert.That(replacement.IsSuccess, Is.True);

        Assert.That(
            map.TryPopulateJson(
                DungeonLevelJsonSerializer.Serialize(replacement.Document),
                map.DungeonCatalog,
                out MapSourceValidationResult rejected
            ),
            Is.False
        );
        Assert.That(
            rejected.Errors,
            Is.EqualTo(
                new[]
                {
                    "Runtime JSON population could not rebind GridBase: "
                        + $"Token '{tokenObject.name}' at cell ({highCell.X}, {highCell.Z}) is outside "
                        + $"replacement bounds {replacementSize}x{replacementSize}.",
                }
            )
        );
        Assert.That(grid.GetTiles(), Is.SameAs(fullSizeTiles));

        tokenObject.SetActive(true);
        Assert.That(fullSizeTiles[highCell.X, highCell.Z].Occupants, Contains.Item(tokenObject));

        Object.Destroy(tokenObject);
        yield return null;
    }

    [UnityTest]
    public IEnumerator RuntimeRepopulationIgnoresTokensCompletelyRemovedFromTheGrid()
    {
        AsyncOperation load = EditorSceneManager.LoadSceneAsyncInPlayMode(
            ScenePath,
            new LoadSceneParameters(LoadSceneMode.Single)
        );
        while (!load.isDone)
            yield return null;
        yield return null;

        Map map = Object.FindFirstObjectByType<Map>();
        GridBase grid = Object.FindFirstObjectByType<GridBase>();
        Tile[,] priorTiles = grid.GetTiles();
        const int replacementSize = 15;
        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(map.JsonSource.text);
        DungeonCell highCell = parsed
            .Document.Rooms.SelectMany(room =>
                Enumerable
                    .Range(room.MinimumZ, room.MaximumZ - room.MinimumZ + 1)
                    .SelectMany(z =>
                        Enumerable
                            .Range(room.MinimumX, room.MaximumX - room.MinimumX + 1)
                            .Select(x => new DungeonCell(x, z))
                    )
            )
            .First(cell =>
                (cell.X >= replacementSize || cell.Z >= replacementSize)
                && priorTiles[cell.X, cell.Z] != null
            );
        GameObject tokenObject = new("Removed Token Outside Replacement Bounds");
        tokenObject.transform.position = new Vector3(highCell.X, 0f, highCell.Z);
        tokenObject.AddComponent<Token>();
        Assert.That(priorTiles[highCell.X, highCell.Z].Occupants, Contains.Item(tokenObject));
        Assert.That(grid.DestroyToken(tokenObject), Is.True);
        tokenObject.SetActive(false);

        DungeonGenerationResult replacement = new DeterministicDungeonGenerator().Generate(
            new DungeonGenerationRequest
            {
                RunSeed = 15602,
                Width = replacementSize,
                Height = replacementSize,
                MinimumRoomCount = 0,
                StairCount = 0,
            }
        );
        Assert.That(replacement.IsSuccess, Is.True);

        Assert.That(
            map.TryPopulateJson(
                DungeonLevelJsonSerializer.Serialize(replacement.Document),
                map.DungeonCatalog,
                out MapSourceValidationResult validation
            ),
            Is.True,
            string.Join(System.Environment.NewLine, validation.Errors)
        );
        Assert.That(grid.GridData.GetLength(0), Is.EqualTo(replacementSize));
        Assert.That(grid.GridData.GetLength(1), Is.EqualTo(replacementSize));

        Object.Destroy(tokenObject);
        yield return null;
    }

    [UnityTest]
    public IEnumerator RuntimeRepopulationRefreshesAuraCellsAgainstReplacementTiles()
    {
        AsyncOperation load = EditorSceneManager.LoadSceneAsyncInPlayMode(
            ScenePath,
            new LoadSceneParameters(LoadSceneMode.Single)
        );
        while (!load.isDone)
            yield return null;
        yield return null;

        Map map = Object.FindFirstObjectByType<Map>();
        GridBase grid = Object.FindFirstObjectByType<GridBase>();
        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(map.JsonSource.text);
        DungeonCell firstCell = parsed.Document.StartCell;
        DungeonCell secondCell = parsed.Document.SafeCells.First(cell =>
            Manhattan(firstCell, cell) > 6
        );
        GameObject auraSource = new("Runtime Rebind Aura Source");
        auraSource.transform.position = new Vector3(firstCell.X, 0f, firstCell.Z);
        CreatureComponent creature = auraSource.AddComponent<CreatureComponent>();
        creature.auras = new List<CreatureAura>
        {
            new()
            {
                name = "Runtime Rebind Aura",
                slug = RottingAuraRule.RuleSlug,
                radiusFeet = 10,
                traits = new List<string> { "disease", "void" },
            },
        };
        auraSource.AddComponent<RebindAuraActionController>();

        AuraGridVisuals auraVisuals = grid.GetComponent<AuraGridVisuals>();
        Assert.That(auraVisuals, Is.Not.Null);
        auraVisuals.Refresh();
        Assert.That(
            auraVisuals.CurrentCells,
            Does.Contain(new Vector3Int(firstCell.X, 0, firstCell.Z))
        );

        auraSource.transform.position = new Vector3(secondCell.X, 0f, secondCell.Z);
        Assert.That(
            auraVisuals.CurrentCells,
            Has.No.Member(new Vector3Int(secondCell.X, 0, secondCell.Z)),
            "Moving the source alone should leave the prior visualization intact until a refresh."
        );

        Assert.That(
            map.TryPopulateJson(
                map.JsonSource.text,
                map.DungeonCatalog,
                out MapSourceValidationResult validation
            ),
            Is.True,
            string.Join(System.Environment.NewLine, validation.Errors)
        );

        Assert.That(
            auraVisuals.CurrentCells,
            Does.Contain(new Vector3Int(secondCell.X, 0, secondCell.Z))
        );
        Assert.That(
            auraVisuals.CurrentCells,
            Has.No.Member(new Vector3Int(firstCell.X, 0, firstCell.Z))
        );

        Object.Destroy(auraSource);
        yield return null;
    }

    [UnityTest]
    public IEnumerator RuntimeRepopulationRejectsPendingAiWork()
    {
        AsyncOperation load = EditorSceneManager.LoadSceneAsyncInPlayMode(
            ScenePath,
            new LoadSceneParameters(LoadSceneMode.Single)
        );
        while (!load.isDone)
            yield return null;
        yield return null;

        Map map = Object.FindFirstObjectByType<Map>();
        GridBase grid = Object.FindFirstObjectByType<GridBase>();
        GeneratedMapRoot priorRoot = Object.FindFirstObjectByType<GeneratedMapRoot>();
        TileType[,] priorGridData = grid.GridData;
        bool[,] priorLineOfSightBlocks = grid.GetLineOfSightBlocks();
        Tile[,] priorTiles = grid.GetTiles();
        IPathfinder priorPathfinder = grid.GetPathfinder();
        GridFSM priorFsm = grid.Fsm;
        GameObject controllerObject = new("Pending Inactive AI");
        controllerObject.SetActive(false);
        MindlessController controller = controllerObject.AddComponent<MindlessController>();
        FieldInfo isTurn = typeof(ActionController).GetField(
            "IsTurn",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(isTurn, Is.Not.Null);
        isTurn.SetValue(controller, true);

        Assert.That(
            map.TryPopulateJson(
                map.JsonSource.text,
                map.DungeonCatalog,
                out MapSourceValidationResult validation
            ),
            Is.False
        );

        Assert.That(
            validation.Errors,
            Is.EqualTo(
                new[]
                {
                    "Runtime JSON population could not rebind GridBase: "
                        + "AI controller 'Pending Inactive AI' has pending turn or action work "
                        + "and cannot rebind to the replacement grid.",
                }
            )
        );
        Assert.That(Object.FindFirstObjectByType<GeneratedMapRoot>(), Is.SameAs(priorRoot));
        Assert.That(map.transform.Find("GeneratedMap"), Is.SameAs(priorRoot.transform));
        Assert.That(priorRoot.gameObject.activeInHierarchy, Is.True);
        Assert.That(map.GetMapData(), Is.SameAs(priorGridData));
        Assert.That(map.GetLineOfSightBlocks(), Is.SameAs(priorLineOfSightBlocks));
        Assert.That(grid.GridData, Is.SameAs(priorGridData));
        Assert.That(grid.GetLineOfSightBlocks(), Is.SameAs(priorLineOfSightBlocks));
        Assert.That(grid.GetTiles(), Is.SameAs(priorTiles));
        Assert.That(grid.GetPathfinder(), Is.SameAs(priorPathfinder));
        Assert.That(grid.Fsm, Is.SameAs(priorFsm));

        Object.Destroy(controllerObject);
        yield return null;
    }

    [UnityTest]
    public IEnumerator RuntimeRepopulationConflictPreservesPriorOwnedHierarchy()
    {
        AsyncOperation load = EditorSceneManager.LoadSceneAsyncInPlayMode(
            ScenePath,
            new LoadSceneParameters(LoadSceneMode.Single)
        );
        while (!load.isDone)
            yield return null;
        yield return null;

        Map activeMap = Object.FindFirstObjectByType<Map>();
        GameObject duplicateObject = new("Inactive Duplicate Runtime Map");
        duplicateObject.SetActive(false);
        Map duplicateMap = duplicateObject.AddComponent<Map>();
        duplicateObject.AddComponent<GridBase>();
        GameObject priorRootObject = new("GeneratedMap");
        priorRootObject.AddComponent<GeneratedMapRoot>();
        priorRootObject.transform.SetParent(duplicateObject.transform, false);

        Assert.That(
            duplicateMap.TryPopulateJson(
                activeMap.JsonSource.text,
                activeMap.DungeonCatalog,
                out MapSourceValidationResult validation
            ),
            Is.False
        );

        Assert.That(validation.Errors, Is.Not.Empty);
        Assert.That(priorRootObject.transform.parent, Is.SameAs(duplicateObject.transform));
        Assert.That(priorRootObject.activeSelf, Is.True);

        yield return null;
        Assert.That(
            duplicateObject == null,
            Is.False,
            "A rejected runtime population must not queue the duplicate map for destruction."
        );
        Assert.That(priorRootObject.transform.parent, Is.SameAs(duplicateObject.transform));
        Assert.That(priorRootObject.activeSelf, Is.True);

        Object.Destroy(duplicateObject);
        yield return null;
    }

    private static (DungeonCell first, DungeonCell second) OppositeWalkableSides(
        DungeonLevelDocument document,
        DungeonCell door
    )
    {
        DungeonCell north = new(door.X, door.Z + 1);
        DungeonCell south = new(door.X, door.Z - 1);
        if (IsWalkable(document, north) && IsWalkable(document, south))
            return (north, south);
        DungeonCell east = new(door.X + 1, door.Z);
        DungeonCell west = new(door.X - 1, door.Z);
        Assert.That(IsWalkable(document, east) && IsWalkable(document, west), Is.True);
        return (east, west);
    }

    private static bool IsWalkable(DungeonLevelDocument document, DungeonCell cell)
    {
        if (cell.X < 0 || cell.Z < 0 || cell.X >= document.Width || cell.Z >= document.Height)
            return false;
        char value = document.Rows[document.Height - 1 - cell.Z][cell.X];
        return value == '.' || value == 'D';
    }

    private static int CountGeneratedWalls(Transform structure)
    {
        int count = 0;
        for (int index = 0; index < structure.childCount; index++)
        {
            if (structure.GetChild(index).name.StartsWith("Wall_"))
                count++;
        }

        return count;
    }

    private static int CountGeneratedFloors(Transform structure)
    {
        int count = 0;
        for (int index = 0; index < structure.childCount; index++)
        {
            if (structure.GetChild(index).name.StartsWith("Floor_"))
                count++;
        }

        return count;
    }

    private static int CountFloorBearingCells(TileType[,] grid)
    {
        int count = 0;
        for (int z = 0; z < grid.GetLength(1); z++)
        {
            for (int x = 0; x < grid.GetLength(0); x++)
            {
                TileType tile = grid[x, z];
                if (
                    tile == TileType.Ground
                    || tile == TileType.Door
                    || tile == TileType.ClosedDoor
                    || tile == TileType.Obstacle
                )
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int CountExposedWalls(TileType[,] grid)
    {
        int count = 0;
        for (int z = 0; z < grid.GetLength(1); z++)
        {
            for (int x = 0; x < grid.GetLength(0); x++)
            {
                TileType tile = grid[x, z];
                if (
                    (tile == TileType.Wall || tile == TileType.Empty)
                    && BordersFloorBearingCell(grid, x, z)
                )
                    count++;
            }
        }

        return count;
    }

    private static bool TryFindInteriorWall(TileType[,] grid, out Vector2Int cell)
    {
        for (int z = 0; z < grid.GetLength(1); z++)
        {
            for (int x = 0; x < grid.GetLength(0); x++)
            {
                if (grid[x, z] != TileType.Wall || BordersFloorBearingCell(grid, x, z))
                    continue;

                cell = new Vector2Int(x, z);
                return true;
            }
        }

        cell = default;
        return false;
    }

    private static bool BordersFloorBearingCell(TileType[,] grid, int x, int z)
    {
        for (int zOffset = -1; zOffset <= 1; zOffset++)
        {
            for (int xOffset = -1; xOffset <= 1; xOffset++)
            {
                if (xOffset == 0 && zOffset == 0)
                    continue;

                int neighborX = x + xOffset;
                int neighborZ = z + zOffset;
                if (
                    neighborX < 0
                    || neighborZ < 0
                    || neighborX >= grid.GetLength(0)
                    || neighborZ >= grid.GetLength(1)
                )
                {
                    continue;
                }

                TileType neighbor = grid[neighborX, neighborZ];
                if (
                    neighbor == TileType.Ground
                    || neighbor == TileType.Door
                    || neighbor == TileType.ClosedDoor
                    || neighbor == TileType.Obstacle
                )
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IReadOnlyList<DungeonCell> MaskedBoundaryCells(
        DungeonRoom room,
        TileType[,] grid
    )
    {
        HashSet<DungeonCell> cells = new();
        for (int z = room.MinimumZ; z <= room.MaximumZ; z++)
        {
            AddIfMasked(room.MinimumX - 1, z);
            AddIfMasked(room.MaximumX + 1, z);
        }
        for (int x = room.MinimumX; x <= room.MaximumX; x++)
        {
            AddIfMasked(x, room.MinimumZ - 1);
            AddIfMasked(x, room.MaximumZ + 1);
        }

        return cells.ToArray();

        void AddIfMasked(int x, int z)
        {
            if (
                x < 0
                || z < 0
                || x >= grid.GetLength(0)
                || z >= grid.GetLength(1)
                || grid[x, z] != TileType.Empty
            )
            {
                return;
            }

            cells.Add(new DungeonCell(x, z));
        }
    }

    private static int Manhattan(DungeonCell left, DungeonCell right)
    {
        return System.Math.Abs(left.X - right.X) + System.Math.Abs(left.Z - right.Z);
    }

    private static void AssertVector3(Vector3 actual, Vector3 expected, string context)
    {
        Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.001f), context + " x");
        Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.001f), context + " y");
        Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.001f), context + " z");
    }

    private sealed class RebindAuraActionController : ActionController
    {
        public override void EndTurn() { }
    }
}
