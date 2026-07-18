using System.Collections;
using System.Linq;
using System.Reflection;
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
            new LoadSceneParameters(LoadSceneMode.Single));
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

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(map.JsonSource.text);
        Assert.That(parsed.IsSuccess, Is.True);
        DungeonLevelDocument document = parsed.Document;
        Assert.That(grid.GridData.GetLength(0), Is.EqualTo(document.Width));
        Assert.That(grid.GridData.GetLength(1), Is.EqualTo(document.Height));

        DungeonDoorController[] doors = Object.FindObjectsByType<DungeonDoorController>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        DungeonStairMarker[] stairs = Object.FindObjectsByType<DungeonStairMarker>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        Assert.That(doors, Has.Length.EqualTo(document.Doors.Count));
        Assert.That(stairs, Has.Length.EqualTo(document.Stairs.Count));
        Assert.That(stairs.Select(stair => stair.Kind),
            Is.EquivalentTo(document.Stairs.Select(stair => stair.Kind)));
        Assert.That(stairs.All(stair =>
            Manhattan(stair.Cell, stair.ArrivalCell) == 1), Is.True);
        Assert.That(stairs.All(stair =>
            grid.GetTiles()[stair.Cell.X, stair.Cell.Z] != null), Is.True,
            "Semantic stair endpoints must remain walkable on their generated floor.");

        Transform objects = generated.transform.Find("Objects");
        Assert.That(objects, Is.Not.Null);
        Assert.That(objects.childCount, Is.EqualTo(document.Objects.Count));
        Assert.That(document.Objects, Is.Not.Empty);
        Assert.That(document.Objects.All(placement =>
            placement.AssetId.EndsWith("/banner_red") ||
            placement.AssetId.EndsWith("/torch_mounted")), Is.True);
        Assert.That(document.Objects.All(placement =>
            grid.GetTiles()[placement.Cell.X, placement.Cell.Z] != null), Is.True,
            "Wall decorations must retain their adjacent walkable floor cells.");

        DungeonDoorController door = doors.OrderBy(candidate => candidate.StableId).First();
        DungeonDoor record = document.Doors.Single(candidate => candidate.Id == door.StableId);
        (DungeonCell firstSide, DungeonCell secondSide) = OppositeWalkableSides(document, record.Cell);
        Vector3Int start = new(firstSide.X, 0, firstSide.Z);
        Vector3Int end = new(secondSide.X, 0, secondSide.Z);
        Transform structure = generated.transform.Find("Structure");
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
        Assert.That(structure.Find($"Floor_{record.Cell.X:D3}_{record.Cell.Z:D3}"), Is.SameAs(floor),
            "Opening a door must not rebuild its floor geometry.");

        Assert.That(door.TrySetOpen(false), Is.True);
        yield return null;

        Assert.That(door.IsOpen, Is.False);
        Assert.That(grid.GridData[record.Cell.X, record.Cell.Z], Is.EqualTo(TileType.ClosedDoor));
        Assert.That(grid.GetTiles()[record.Cell.X, record.Cell.Z], Is.Null);
        Assert.That(grid.GetLineOfSightBlocks()[record.Cell.X, record.Cell.Z], Is.True);
        Assert.That(grid.GetPathfinder().Pathfind(null, start, end), Is.Null.Or.Empty);
        Assert.That(door.transform.Find("ClosedVisual").gameObject.activeSelf, Is.True);
        Assert.That(door.transform.Find("OpenVisual").gameObject.activeSelf, Is.False);
    }

    [UnityTest]
    public IEnumerator RuntimeRepopulationRebindsLiveGridAndImmediatelyDeactivatesPriorGeometry()
    {
        AsyncOperation load = EditorSceneManager.LoadSceneAsyncInPlayMode(
            ScenePath,
            new LoadSceneParameters(LoadSceneMode.Single));
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
            cell != tokenCell && priorTiles[cell.X, cell.Z] != null);
        GameObject inactiveTokenObject = new("Inactive Runtime Repopulation Token");
        inactiveTokenObject.transform.position = new Vector3(inactiveCell.X, 0f, inactiveCell.Z);
        inactiveTokenObject.AddComponent<Token>();
        Assert.That(priorTiles[inactiveCell.X, inactiveCell.Z].Occupants,
            Contains.Item(inactiveTokenObject));
        inactiveTokenObject.SetActive(false);

        Assert.That(map.TryPopulateJson(
                map.JsonSource.text,
                map.DungeonCatalog,
                out MapSourceValidationResult validation),
            Is.True,
            string.Join(System.Environment.NewLine, validation.Errors));

        Assert.That(priorRoot.gameObject.activeInHierarchy, Is.False,
            "Deferred destruction must not leave the prior generation active for the rest of the frame.");
        Assert.That(grid.IsInitialized, Is.True);
        Assert.That(grid.GridData, Is.SameAs(map.GetMapData()));
        Assert.That(grid.GetLineOfSightBlocks(), Is.SameAs(map.GetLineOfSightBlocks()));
        Assert.That(grid.GetTiles(), Is.Not.SameAs(priorTiles));
        Assert.That(grid.Fsm, Is.SameAs(priorFsm));
        Assert.That(grid.GetPathfinder(), Is.Not.Null);
        Assert.That(grid.GetTiles()[tokenCell.X, tokenCell.Z].Occupants, Contains.Item(tokenObject));
        Assert.That(
            grid.GetTiles()[inactiveCell.X, inactiveCell.Z].Occupants.Contains(inactiveTokenObject),
            Is.False);
        inactiveTokenObject.SetActive(true);
        Assert.That(grid.GetTiles()[inactiveCell.X, inactiveCell.Z].Occupants,
            Contains.Item(inactiveTokenObject));
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
            new LoadSceneParameters(LoadSceneMode.Single));
        while (!load.isDone)
            yield return null;
        yield return null;

        Map map = Object.FindFirstObjectByType<Map>();
        GridBase grid = Object.FindFirstObjectByType<GridBase>();
        GeneratedMapRoot priorRoot = Object.FindFirstObjectByType<GeneratedMapRoot>();
        Tile[,] priorTiles = grid.GetTiles();
        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(map.JsonSource.text);
        DungeonCell highCell = parsed.Document.Rooms
            .SelectMany(room => Enumerable.Range(room.MinimumZ, room.MaximumZ - room.MinimumZ + 1)
                .SelectMany(z => Enumerable.Range(room.MinimumX, room.MaximumX - room.MinimumX + 1)
                    .Select(x => new DungeonCell(x, z))))
            .First(cell => cell.X >= 31 && priorTiles[cell.X, cell.Z] != null);
        GameObject tokenObject = new("Token Outside Replacement Bounds");
        tokenObject.transform.position = new Vector3(highCell.X, 0f, highCell.Z);
        tokenObject.AddComponent<Token>();
        Assert.That(priorTiles[highCell.X, highCell.Z].Occupants, Contains.Item(tokenObject));
        DungeonGenerationResult replacement = new DeterministicDungeonGenerator().Generate(
            new DungeonGenerationRequest
            {
                RunSeed = 15601,
                Width = 31,
                Height = 31,
                MinimumRoomCount = 3
            });
        Assert.That(replacement.IsSuccess, Is.True);

        Assert.That(map.TryPopulateJson(
                DungeonLevelJsonSerializer.Serialize(replacement.Document),
                map.DungeonCatalog,
                out MapSourceValidationResult validation),
            Is.False);

        Assert.That(validation.Errors, Is.Not.Empty);
        Assert.That(Object.FindFirstObjectByType<GeneratedMapRoot>(), Is.SameAs(priorRoot));
        Assert.That(grid.GetTiles(), Is.SameAs(priorTiles));
        Assert.That(priorTiles[highCell.X, highCell.Z].Occupants, Contains.Item(tokenObject));

        Object.Destroy(tokenObject);
        yield return null;
    }

    [UnityTest]
    public IEnumerator RuntimeRepopulationRejectsPendingAiWork()
    {
        AsyncOperation load = EditorSceneManager.LoadSceneAsyncInPlayMode(
            ScenePath,
            new LoadSceneParameters(LoadSceneMode.Single));
        while (!load.isDone)
            yield return null;
        yield return null;

        Map map = Object.FindFirstObjectByType<Map>();
        GridBase grid = Object.FindFirstObjectByType<GridBase>();
        GeneratedMapRoot priorRoot = Object.FindFirstObjectByType<GeneratedMapRoot>();
        Tile[,] priorTiles = grid.GetTiles();
        GameObject controllerObject = new("Pending Inactive AI");
        controllerObject.SetActive(false);
        MindlessController controller = controllerObject.AddComponent<MindlessController>();
        FieldInfo isTurn = typeof(ActionController).GetField(
            "IsTurn",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(isTurn, Is.Not.Null);
        isTurn.SetValue(controller, true);

        Assert.That(map.TryPopulateJson(
                map.JsonSource.text,
                map.DungeonCatalog,
                out MapSourceValidationResult validation),
            Is.False);

        Assert.That(validation.Errors, Is.Not.Empty);
        Assert.That(Object.FindFirstObjectByType<GeneratedMapRoot>(), Is.SameAs(priorRoot));
        Assert.That(grid.GetTiles(), Is.SameAs(priorTiles));

        Object.Destroy(controllerObject);
        yield return null;
    }

    [UnityTest]
    public IEnumerator RuntimeRepopulationConflictPreservesPriorOwnedHierarchy()
    {
        AsyncOperation load = EditorSceneManager.LoadSceneAsyncInPlayMode(
            ScenePath,
            new LoadSceneParameters(LoadSceneMode.Single));
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

        Assert.That(duplicateMap.TryPopulateJson(
                activeMap.JsonSource.text,
                activeMap.DungeonCatalog,
                out MapSourceValidationResult validation),
            Is.False);

        Assert.That(validation.Errors, Is.Not.Empty);
        Assert.That(priorRootObject.transform.parent, Is.SameAs(duplicateObject.transform));
        Assert.That(priorRootObject.activeSelf, Is.True);

        Object.Destroy(duplicateObject);
        yield return null;
    }

    private static (DungeonCell first, DungeonCell second) OppositeWalkableSides(
        DungeonLevelDocument document,
        DungeonCell door)
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

    private static int Manhattan(DungeonCell left, DungeonCell right)
    {
        return System.Math.Abs(left.X - right.X) + System.Math.Abs(left.Z - right.Z);
    }
}
