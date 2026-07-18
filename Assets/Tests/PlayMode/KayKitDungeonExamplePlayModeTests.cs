using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Creature;
using Game.KayKit;
using GridPrivate;
using GridPublic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class KayKitDungeonExamplePlayModeTests
{
    private const string ScenePath = "Assets/Scenes/KayKitDungeonExample.unity";
    private static int cleanupSceneIndex;

    [UnitySetUp]
    public IEnumerator LoadStandaloneExample()
    {
        Time.timeScale = 0f;
        AsyncOperation load = EditorSceneManager.LoadSceneAsyncInPlayMode(
            ScenePath,
            new LoadSceneParameters(LoadSceneMode.Single));
        Assert.That(load, Is.Not.Null, $"Could not start loading {ScenePath}.");
        while (!load.isDone)
            yield return null;

        float deadline = Time.realtimeSinceStartup + 10f;
        while ((Object.FindFirstObjectByType<GridBase>() == null ||
                Object.FindObjectsByType<ActionController>(FindObjectsSortMode.None).Length != 6) &&
               Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        GridInput input = Object.FindFirstObjectByType<GridInput>();
        if (input != null)
            input.enabled = false;
    }

    [UnityTearDown]
    public IEnumerator UnloadStandaloneExample()
    {
        Time.timeScale = 1f;
        Scene gameplayScene = SceneManager.GetActiveScene();
        if (!gameplayScene.IsValid() || !gameplayScene.isLoaded)
            yield break;

        Scene cleanupScene = SceneManager.CreateScene("KayKitDungeonCleanup" + cleanupSceneIndex++);
        SceneManager.SetActiveScene(cleanupScene);
        AsyncOperation unload = SceneManager.UnloadSceneAsync(gameplayScene);
        while (unload != null && !unload.isDone)
            yield return null;
        yield return null;
    }

    [UnityTest]
    public IEnumerator SceneInitializesGeneratedMapAndAuthoredEncounter()
    {
        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("KayKitDungeonExample"));
        Assert.That(
            EditorBuildSettings.scenes.Any(scene => scene.path == ScenePath),
            Is.False,
            "The standalone example must not enter the campaign build sequence.");

        Map map = Object.FindFirstObjectByType<Map>();
        GridBase grid = Object.FindFirstObjectByType<GridBase>();
        GeneratedMapRoot generated = Object.FindFirstObjectByType<GeneratedMapRoot>();
        Assert.That(map, Is.Not.Null);
        Assert.That(map.SourceMode, Is.EqualTo(MapSourceMode.Json));
        Assert.That(map.JsonSource, Is.Not.Null);
        Assert.That(map.JsonSource.name, Is.EqualTo("KayKitDungeonExample"));
        Assert.That(map.DungeonCatalog, Is.Not.Null);
        Assert.That(grid, Is.Not.Null);
        Assert.That(grid.GridData.GetLength(0), Is.EqualTo(50));
        Assert.That(grid.GridData.GetLength(1), Is.EqualTo(50));
        Assert.That(generated, Is.Not.Null);
        Assert.That(generated.transform.Find("Structure"), Is.Not.Null);
        Assert.That(generated.transform.Find("Objects"), Is.Not.Null);
        Assert.That(generated.transform.Find("Objects").childCount, Is.EqualTo(10));

        ActionController[] combatants = Object.FindObjectsByType<ActionController>(FindObjectsSortMode.None);
        Assert.That(combatants, Has.Length.EqualTo(6));
        Assert.That(combatants.Count(controller => TeamName(controller) == "Players"), Is.EqualTo(2));
        Assert.That(combatants.Count(controller => TeamName(controller) == "Enemies"), Is.EqualTo(4));
        Assert.That(combatants.Count(controller => controller is PlayerActionController), Is.EqualTo(2));
        Assert.That(combatants.Count(controller => controller is MindlessController), Is.EqualTo(4));
        CollectionAssert.AreEquivalent(
            new[]
            {
                "Lena",
                "Torgrim",
                "Zombie Shambler A",
                "Zombie Shambler B",
                "Skeleton Guard A",
                "Skeleton Guard B"
            },
            combatants.Select(controller => controller.name));

        Tile[,] tiles = grid.GetTiles();
        HashSet<Vector3Int> occupiedCells = new();
        foreach (ActionController combatant in combatants)
        {
            Vector3Int cell = Vector3Int.RoundToInt(combatant.transform.position);
            Assert.That(cell.x, Is.InRange(0, tiles.GetLength(0) - 1), combatant.name);
            Assert.That(cell.z, Is.InRange(0, tiles.GetLength(1) - 1), combatant.name);
            Assert.That(tiles[cell.x, cell.z], Is.Not.Null, $"{combatant.name} must spawn on a walkable tile.");
            Assert.That(tiles[cell.x, cell.z].Occupants, Does.Contain(combatant.gameObject), combatant.name);
            Assert.That(occupiedCells.Add(cell), Is.True, $"Duplicate combatant spawn at {cell}.");
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator GameplayCameraPreservesPortraitsFramesEncounterAndAcceptsZoom()
    {
        Camera gameplayCamera = Camera.main;
        CameraManager cameraManager = Object.FindFirstObjectByType<CameraManager>();
        Assert.That(gameplayCamera, Is.Not.Null);
        Assert.That(cameraManager, Is.Not.Null);
        Assert.That(gameplayCamera.enabled, Is.True);
        Assert.That(gameplayCamera.targetTexture, Is.Null);
        Assert.That(gameplayCamera.orthographic, Is.False);
        Assert.That(gameplayCamera.fieldOfView, Is.EqualTo(30f));
        Assert.That(gameplayCamera.transform.position.y,
            Is.GreaterThan(cameraManager.minCamearYLimit)
                .And.LessThan(cameraManager.maxCameraYLimit));

        Camera[] gameplayCandidates = Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None)
            .Where(candidate =>
                candidate.enabled &&
                candidate.gameObject.activeInHierarchy &&
                candidate.targetTexture == null &&
                candidate.CompareTag("MainCamera"))
            .ToArray();
        Assert.That(gameplayCandidates, Is.EqualTo(new[] { gameplayCamera }));

        Portrait[] portraits = Object.FindObjectsByType<Portrait>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        Assert.That(portraits, Has.Length.EqualTo(6));
        foreach (Portrait portrait in portraits)
        {
            Camera portraitCamera = portrait.GetComponentInChildren<Camera>(true);
            Assert.That(portraitCamera, Is.Not.Null, portrait.name);
            GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PortraitPrefabPath(portrait.name));
            Camera sourceCamera = sourcePrefab.GetComponentInChildren<Camera>(true);
            Assert.That(sourceCamera, Is.Not.Null, portrait.name);
            Assert.That(portraitCamera.enabled, Is.False, portrait.name);
            Assert.That(portraitCamera.CompareTag("MainCamera"), Is.False, portrait.name);
            Assert.That(portraitCamera.orthographic, Is.EqualTo(sourceCamera.orthographic), portrait.name);
            Assert.That(portraitCamera.fieldOfView, Is.EqualTo(sourceCamera.fieldOfView), portrait.name);
            Assert.That(portraitCamera.nearClipPlane, Is.EqualTo(sourceCamera.nearClipPlane), portrait.name);
            Assert.That(portraitCamera.farClipPlane, Is.EqualTo(sourceCamera.farClipPlane), portrait.name);
            Assert.That(portraitCamera.transform.localPosition,
                Is.EqualTo(sourceCamera.transform.localPosition), portrait.name);
            Assert.That(Quaternion.Angle(
                portraitCamera.transform.localRotation,
                sourceCamera.transform.localRotation), Is.LessThan(0.001f), portrait.name);
        }

        ActionController[] encounter = Object.FindObjectsByType<ActionController>(FindObjectsSortMode.None);
        Assert.That(encounter, Is.Not.Empty);

        cameraManager.StopFollowing();
        gameplayCamera.transform.position = new Vector3(24.5f, 6f, 6f);
        gameplayCamera.transform.rotation = Quaternion.Euler(35f, 0f, 0f);

        float originalAspect = gameplayCamera.aspect;
        try
        {
            foreach (float aspect in new[] { 16f / 10f, 16f / 9f })
            {
                AssertCombatantPositionsVisible(
                    gameplayCamera,
                    new[] { FindCombatant("Lena").transform.position },
                    aspect);
                Assert.That(
                    encounter
                        .Where(combatant => TeamName(combatant) == "Enemies")
                        .Any(combatant => IsPositionVisible(
                            gameplayCamera,
                            combatant.transform.position,
                            aspect)),
                    Is.True,
                    $"Aspect {aspect}: the starting camera must show at least one enemy.");
            }
        }
        finally
        {
            gameplayCamera.aspect = originalAspect;
        }

        MethodInfo tryApplyZoom = typeof(CameraManager).GetMethod(
            "TryApplyZoom",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(tryApplyZoom, Is.Not.Null);
        Vector3 initialPosition = gameplayCamera.transform.position;
        Assert.That((bool)tryApplyZoom.Invoke(cameraManager, new object[] { 0.5f }), Is.True);
        Assert.That(gameplayCamera.transform.position.y, Is.LessThan(initialPosition.y));
        gameplayCamera.transform.position = initialPosition;
        Assert.That((bool)tryApplyZoom.Invoke(cameraManager, new object[] { -0.5f }), Is.True);
        Assert.That(gameplayCamera.transform.position.y, Is.GreaterThan(initialPosition.y));
        gameplayCamera.transform.position = initialPosition;

        yield return null;
    }

    [UnityTest]
    public IEnumerator DoorsObstaclesPathsAndLineOfSightUseRuntimeGridRules()
    {
        GridBase grid = Object.FindFirstObjectByType<GridBase>();
        Tile[,] tiles = grid.GetTiles();
        bool[,] lineOfSight = grid.GetLineOfSightBlocks();

        Assert.That(grid.GridData[0, 0], Is.EqualTo(TileType.Wall));
        Assert.That(grid.GridData[25, 49], Is.EqualTo(TileType.Door));
        Assert.That(grid.GridData[18, 25], Is.EqualTo(TileType.Obstacle));
        Assert.That(tiles[0, 0], Is.Null);
        Assert.That(tiles[25, 49], Is.Not.Null, "An open doorway remains walkable.");
        Assert.That(tiles[18, 25], Is.Null, "A blocking prop is not a structural wall, but is unwalkable.");
        Assert.That(lineOfSight[0, 0], Is.True);
        Assert.That(lineOfSight[25, 49], Is.False);
        Assert.That(lineOfSight[18, 25], Is.True, "Stacked crates block line of sight.");
        Assert.That(lineOfSight[19, 25], Is.False, "The chest blocks movement without blocking line of sight.");

        Assert.That(
            StrikeTargeting.CountClearRays(tiles, new Vector3Int(17, 0, 25), new Vector3Int(20, 0, 25)),
            Is.Zero,
            "The collider-backed stacked crates must block the lane behind them.");
        Assert.That(
            StrikeTargeting.CountClearRays(tiles, new Vector3Int(22, 0, 24), new Vector3Int(27, 0, 24)),
            Is.EqualTo(16),
            "An open central ranged lane remains clear.");

        ActionController[] players = CombatantsForTeam("Players");
        ActionController[] enemies = CombatantsForTeam("Enemies");
        foreach (ActionController player in players)
        {
            foreach (ActionController enemy in enemies)
            {
                Vector3Int start = Vector3Int.RoundToInt(player.transform.position);
                Vector3Int end = Vector3Int.RoundToInt(enemy.transform.position);
                List<PathNode> path = grid.GetPathfinder().Pathfind(null, start, end);
                Assert.That(path, Is.Not.Null.And.Not.Empty, $"No path from {player.name} to {enemy.name}.");
                Assert.That(path[0].Location, Is.EqualTo(start));
                Assert.That(path[^1].Location, Is.EqualTo(end));
            }
        }

        List<PathNode> meleeRoute = grid.GetPathfinder().Pathfind(
            null,
            new Vector3Int(21, 0, 22),
            new Vector3Int(28, 0, 27));
        Assert.That(
            meleeRoute.All(node => GridBase.IsWalkableTile(grid.GridData[node.Location.x, node.Location.z])),
            Is.True,
            "The opposing sides must connect across the open room without crossing a wall or obstacle.");

        yield return null;
    }

    [UnityTest]
    public IEnumerator StrideRangeHighlightRendersAboveTheDungeonFloor()
    {
        GridBase grid = Object.FindFirstObjectByType<GridBase>();
        GridVisuals visuals = grid.GetComponent<GridVisuals>();
        ActionController lena = FindCombatant("Lena");
        Assert.That(grid.Fsm.ChangeState(new StateStride(lena.gameObject, grid.Fsm)), Is.True);
        yield return null;

        FieldInfo rangePoolField = typeof(GridVisuals).GetField(
            "RangePool",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(rangePoolField, Is.Not.Null);
        GameObjectPool rangePool = (GameObjectPool)rangePoolField.GetValue(visuals);
        List<GameObject> highlights = rangePool.CurrentlyActiveList();
        Assert.That(highlights, Is.Not.Empty, "Stride must display reachable floor tiles.");

        GeneratedMapRoot generated = Object.FindFirstObjectByType<GeneratedMapRoot>();
        Renderer floor = generated.GetComponentsInChildren<Renderer>(false).First(renderer =>
            renderer.transform.parent != null &&
            renderer.transform.parent.name == "Floor_021_022");
        float floorTop = floor.bounds.max.y;
        Assert.That(highlights.All(highlight => highlight.transform.position.y > floorTop), Is.True,
            "Stride highlights must sit above the visible dungeon floor surface.");

        grid.Fsm.ChangeState(new StateIdle());
    }

    [UnityTest]
    public IEnumerator EveryEncounterArchetypeCanStartATurnWithExpectedAttacks()
    {
        string[] representatives =
        {
            "Lena",
            "Torgrim",
            "Zombie Shambler A",
            "Skeleton Guard A"
        };

        foreach (string name in representatives)
        {
            ActionController controller = FindCombatant(name);
            Assert.That(controller.GetMovements(), Is.Not.Empty, $"{name} needs Stride.");
            Assert.That(controller.GetActions(), Is.Not.Empty, $"{name} needs at least one combat action.");
            controller.StartTurn();
            Assert.That(controller.ActionPoints, Is.GreaterThan(0), $"{name} failed to initialize a turn.");
        }

        ActionController skeleton = FindCombatant("Skeleton Guard A");
        CollectionAssert.IsSubsetOf(
            new[] { "Scimitar", "Shortbow" },
            skeleton.GetActions().Select(action => action.ActionName).ToArray());
        CreatureComponent skeletonCreature = skeleton.GetComponent<CreatureComponent>();
        Assert.That(skeletonCreature.weapons.Any(weapon => weapon.name == "Scimitar" && weapon.range == 0), Is.True);
        Assert.That(skeletonCreature.weapons.Any(weapon => weapon.name == "Shortbow" && weapon.range > 0), Is.True);

        ActionController zombie = FindCombatant("Zombie Shambler A");
        CreatureComponent zombieCreature = zombie.GetComponent<CreatureComponent>();
        Assert.That(zombieCreature.weapons, Is.Empty, "Zombies are intentionally unarmed.");
        Assert.That(
            zombie.GetActions().Any(action => action.ActionName == "Unarmed Strike"),
            Is.True,
            "The unarmed zombie still needs a usable strike.");

        yield return null;
    }

    [UnityTest]
    public IEnumerator RemovingDefeatedEnemyTeamCompletesEncounterAsPlayerVictory()
    {
        CombatManagerInterface manager = Object.FindFirstObjectByType<CombatManager>();
        Assert.That(manager, Is.Not.Null);

        bool? playerVictory = null;
        UnityAction<bool> outcomeListener = result => playerVictory = result;
        OnCombatOutcome.AddListener(outcomeListener);
        try
        {
            foreach (ActionController enemy in CombatantsForTeam("Enemies"))
                manager.Remove(enemy);

            Assert.That(manager.CheckForEndOfGame(), Is.True);
            Assert.That(playerVictory, Is.True);
            Assert.That(manager.GetCombatants().All(combatant => combatant.GetComponent<Team>().Name == "Players"), Is.True);
        }
        finally
        {
            OnCombatOutcome.RemoveListener(outcomeListener);
        }

        yield return null;
    }

    private static ActionController FindCombatant(string name)
    {
        ActionController result = Object.FindObjectsByType<ActionController>(FindObjectsSortMode.None)
            .SingleOrDefault(controller => controller.name == name);
        Assert.That(result, Is.Not.Null, $"Missing combatant {name}.");
        return result;
    }

    private static ActionController[] CombatantsForTeam(string teamName)
    {
        return Object.FindObjectsByType<ActionController>(FindObjectsSortMode.None)
            .Where(controller => TeamName(controller) == teamName)
            .ToArray();
    }

    private static string TeamName(ActionController controller)
    {
        return controller.GetComponent<Team>()?.Name;
    }

    private static bool IsPositionVisible(Camera camera, Vector3 position, float aspect)
    {
        camera.aspect = aspect;
        Vector3 viewport = camera.WorldToViewportPoint(position);
        return viewport.z > 0f &&
               viewport.x >= 0f &&
               viewport.x <= 1f &&
               viewport.y >= 0f &&
               viewport.y <= 1f;
    }

    private static void AssertCombatantPositionsVisible(
        Camera camera,
        IEnumerable<Vector3> positions,
        float aspect)
    {
        camera.aspect = aspect;
        foreach (Vector3 position in positions)
        {
            Vector3 viewport = camera.WorldToViewportPoint(position);
            Assert.That(viewport.z, Is.GreaterThan(0f), $"Aspect {aspect}: {position} is behind the camera.");
            Assert.That(viewport.x, Is.InRange(0f, 1f), $"Aspect {aspect}: {position} is horizontally clipped.");
            Assert.That(viewport.y, Is.InRange(0f, 1f), $"Aspect {aspect}: {position} is vertically clipped.");
        }
    }

    private static string PortraitPrefabPath(string instanceName)
    {
        if (instanceName == "Lena")
            return "Assets/Prefabs/Creatures/Lena.prefab";
        if (instanceName == "Torgrim")
            return "Assets/Prefabs/Creatures/Torgrim.prefab";
        if (instanceName.StartsWith("Zombie Shambler", System.StringComparison.Ordinal))
            return "Assets/Prefabs/Creatures/zombie-shambler.prefab";
        if (instanceName.StartsWith("Skeleton Guard", System.StringComparison.Ordinal))
            return "Assets/Prefabs/Creatures/skeleton-guard.prefab";

        throw new AssertionException($"No portrait source prefab is registered for {instanceName}.");
    }
}

public sealed class InvalidGridInitializationPlayModeTests
{
    [Test]
    public void InvalidMapLeavesGridDisabledAndUnregistered()
    {
        FieldInfo singletonField = typeof(SingletonMonoBehaviour<GridAPI>)
            .GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(singletonField, Is.Not.Null);

        object previousSingleton = singletonField.GetValue(null);
        GameObject gridObject = null;
        GameObject combatantObject = null;
        TextAsset source = null;
        KayKitDungeonCatalog catalog = null;
        try
        {
            singletonField.SetValue(null, null);
            gridObject = new GameObject("Invalid Grid");
            gridObject.SetActive(false);
            source = new TextAsset(@"{""version"":99,""rows"":["".""]}");
            catalog = ScriptableObject.CreateInstance<KayKitDungeonCatalog>();

            Map map = gridObject.AddComponent<Map>();
            map.ConfigureJson(source, catalog);
            LogAssert.Expect(
                LogType.Error,
                "Map data is invalid: JSON map version must be one of 1 or 2; found 99.");
            LogAssert.Expect(
                LogType.Error,
                "Grid initialization failed: Map did not provide valid grid and line-of-sight data.");

            GridBase grid = gridObject.AddComponent<GridBase>();
            combatantObject = new GameObject("Invalid Grid Combatant");
            combatantObject.SetActive(false);
            combatantObject.AddComponent<CreatureComponent>();
            Token token = combatantObject.AddComponent<Token>();
            gridObject.SetActive(true);
            combatantObject.SetActive(true);

            Assert.That(grid.enabled, Is.False);
            Assert.That(grid.GetComponent<GridInput>().enabled, Is.False);
            Assert.That(grid.GridData, Is.Null);
            Assert.That(grid.GetTiles(), Is.Null);
            Assert.That(grid.GetPathfinder(), Is.Null);
            Assert.That(singletonField.GetValue(null), Is.Null);
            Assert.That(token.enabled, Is.True);
            LogAssert.NoUnexpectedReceived();
        }
        finally
        {
            if (combatantObject != null)
                Object.DestroyImmediate(combatantObject);
            if (gridObject != null)
                Object.DestroyImmediate(gridObject);
            if (source != null)
                Object.DestroyImmediate(source);
            if (catalog != null)
                Object.DestroyImmediate(catalog);
            singletonField.SetValue(null, previousSingleton);
        }
    }

    [Test]
    public void ValidMapStillRegistersCombatantToken()
    {
        FieldInfo singletonField = typeof(SingletonMonoBehaviour<GridAPI>)
            .GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(singletonField, Is.Not.Null);

        object previousSingleton = singletonField.GetValue(null);
        GameObject gridObject = null;
        GameObject combatantObject = null;
        TextAsset source = null;
        try
        {
            singletonField.SetValue(null, null);
            gridObject = new GameObject("Valid Grid");
            gridObject.SetActive(false);
            source = new TextAsset(@"{""version"":1,""rows"":["".""],""objects"":[]}");
            KayKitDungeonCatalog catalog = AssetDatabase.LoadAssetAtPath<KayKitDungeonCatalog>(
                "Assets/KayKit/Catalogs/KayKitDungeonCatalog.asset");

            Map map = gridObject.AddComponent<Map>();
            map.ConfigureJson(source, catalog);
            GridBase grid = gridObject.AddComponent<GridBase>();
            gridObject.SetActive(true);

            combatantObject = new GameObject("Valid Grid Combatant");
            combatantObject.SetActive(false);
            combatantObject.AddComponent<CreatureComponent>();
            combatantObject.AddComponent<Token>();
            combatantObject.SetActive(true);

            Tile[,] tiles = grid.GetTiles();
            Assert.That(singletonField.GetValue(null), Is.SameAs(grid));
            Assert.That(tiles, Is.Not.Null);
            Assert.That(tiles[0, 0].Occupants, Does.Contain(combatantObject));
            LogAssert.NoUnexpectedReceived();
        }
        finally
        {
            if (combatantObject != null)
                Object.DestroyImmediate(combatantObject);
            if (gridObject != null)
                Object.DestroyImmediate(gridObject);
            if (source != null)
                Object.DestroyImmediate(source);
            singletonField.SetValue(null, previousSingleton);
        }
    }

    [Test]
    public void OutOfBoundsToken_LogsWarningWithoutThrowing()
    {
        FieldInfo singletonField = typeof(SingletonMonoBehaviour<GridAPI>)
            .GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(singletonField, Is.Not.Null);

        object previousSingleton = singletonField.GetValue(null);
        GameObject gridObject = null;
        GameObject tokenObject = null;
        TextAsset source = null;
        try
        {
            singletonField.SetValue(null, null);
            gridObject = new GameObject("Bounds Grid");
            gridObject.SetActive(false);
            source = new TextAsset(@"{""version"":1,""rows"":["".""],""objects"":[]}");
            KayKitDungeonCatalog catalog = AssetDatabase.LoadAssetAtPath<KayKitDungeonCatalog>(
                "Assets/KayKit/Catalogs/KayKitDungeonCatalog.asset");
            Map map = gridObject.AddComponent<Map>();
            map.ConfigureJson(source, catalog);
            GridBase grid = gridObject.AddComponent<GridBase>();
            gridObject.SetActive(true);

            tokenObject = new GameObject("Out of Bounds Combatant");
            tokenObject.transform.position = Vector3.right;
            tokenObject.SetActive(false);
            tokenObject.AddComponent<Token>();
            LogAssert.Expect(
                LogType.Warning,
                "Failed to register token 'Out of Bounds Combatant' at grid cell (1, 0). The cell is outside the grid bounds.");
            LogAssert.Expect(
                LogType.Warning,
                "Failed to register token 'Out of Bounds Combatant' at grid cell (1, 0). The cell is outside the grid bounds.");
            tokenObject.SetActive(true);

            Assert.That(grid.GetTiles()[0, 0].Occupants, Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }
        finally
        {
            if (tokenObject != null)
                Object.DestroyImmediate(tokenObject);
            if (gridObject != null)
                Object.DestroyImmediate(gridObject);
            if (source != null)
                Object.DestroyImmediate(source);
            singletonField.SetValue(null, previousSingleton);
        }
    }

    [Test]
    public void TokenBeforeGrid_RegistersExactlyOnceWhenGridBecomesReady()
    {
        FieldInfo singletonField = typeof(SingletonMonoBehaviour<GridAPI>)
            .GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(singletonField, Is.Not.Null);

        object previousSingleton = singletonField.GetValue(null);
        GameObject gridObject = null;
        GameObject tokenObject = null;
        TextAsset source = null;
        try
        {
            singletonField.SetValue(null, null);
            tokenObject = new GameObject("Early Token");
            Token token = tokenObject.AddComponent<Token>();
            Assert.That(singletonField.GetValue(null), Is.Null);

            gridObject = new GameObject("Later Valid Grid");
            gridObject.SetActive(false);
            source = new TextAsset(@"{""version"":1,""rows"":["".""],""objects"":[]}");
            KayKitDungeonCatalog catalog = AssetDatabase.LoadAssetAtPath<KayKitDungeonCatalog>(
                "Assets/KayKit/Catalogs/KayKitDungeonCatalog.asset");
            Map map = gridObject.AddComponent<Map>();
            map.ConfigureJson(source, catalog);
            GridBase grid = gridObject.AddComponent<GridBase>();
            gridObject.SetActive(true);

            Tile tile = grid.GetTiles()[0, 0];
            Assert.That(tile.Occupants, Has.Count.EqualTo(1));
            Assert.That(tile.Occupants[0], Is.SameAs(tokenObject));

            token.enabled = false;
            token.enabled = true;

            Assert.That(tile.Occupants, Has.Count.EqualTo(1));
            Assert.That(tile.Occupants[0], Is.SameAs(tokenObject));
            LogAssert.NoUnexpectedReceived();
        }
        finally
        {
            if (tokenObject != null)
                Object.DestroyImmediate(tokenObject);
            if (gridObject != null)
                Object.DestroyImmediate(gridObject);
            if (source != null)
                Object.DestroyImmediate(source);
            singletonField.SetValue(null, previousSingleton);
        }
    }

    [Test]
    public void InvalidGridThenValidGrid_RegistersWaitingTokenWithoutLogCascade()
    {
        FieldInfo singletonField = typeof(SingletonMonoBehaviour<GridAPI>)
            .GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(singletonField, Is.Not.Null);

        object previousSingleton = singletonField.GetValue(null);
        GameObject invalidGridObject = null;
        GameObject validGridObject = null;
        GameObject tokenObject = null;
        TextAsset invalidSource = null;
        TextAsset validSource = null;
        try
        {
            singletonField.SetValue(null, null);
            tokenObject = new GameObject("Waiting Token");
            tokenObject.AddComponent<Token>();

            invalidGridObject = new GameObject("Invalid Early Grid");
            invalidGridObject.SetActive(false);
            invalidSource = new TextAsset(@"{""version"":99,""rows"":["".""],""objects"":[]}");
            KayKitDungeonCatalog catalog = AssetDatabase.LoadAssetAtPath<KayKitDungeonCatalog>(
                "Assets/KayKit/Catalogs/KayKitDungeonCatalog.asset");
            Map invalidMap = invalidGridObject.AddComponent<Map>();
            invalidMap.ConfigureJson(invalidSource, catalog);
            GridBase invalidGrid = invalidGridObject.AddComponent<GridBase>();
            LogAssert.Expect(
                LogType.Error,
                "Map data is invalid: JSON map version must be one of 1 or 2; found 99.");
            LogAssert.Expect(
                LogType.Error,
                "Grid initialization failed: Map did not provide valid grid and line-of-sight data.");
            invalidGridObject.SetActive(true);

            Assert.That(invalidGrid.enabled, Is.False);
            Assert.That(singletonField.GetValue(null), Is.Null);

            validGridObject = new GameObject("Valid Later Grid");
            validGridObject.SetActive(false);
            validSource = new TextAsset(@"{""version"":1,""rows"":["".""],""objects"":[]}");
            Map validMap = validGridObject.AddComponent<Map>();
            validMap.ConfigureJson(validSource, catalog);
            GridBase validGrid = validGridObject.AddComponent<GridBase>();
            validGridObject.SetActive(true);

            Assert.That(validGrid.GetTiles()[0, 0].Occupants, Has.Count.EqualTo(1));
            Assert.That(validGrid.GetTiles()[0, 0].Occupants[0], Is.SameAs(tokenObject));
            LogAssert.NoUnexpectedReceived();
        }
        finally
        {
            if (tokenObject != null)
                Object.DestroyImmediate(tokenObject);
            if (validGridObject != null)
                Object.DestroyImmediate(validGridObject);
            if (invalidGridObject != null)
                Object.DestroyImmediate(invalidGridObject);
            if (validSource != null)
                Object.DestroyImmediate(validSource);
            if (invalidSource != null)
                Object.DestroyImmediate(invalidSource);
            singletonField.SetValue(null, previousSingleton);
        }
    }

    [UnityTest]
    public IEnumerator DisabledAndDestroyedWaitingTokens_DoNotLeakGridReadyCallbacks()
    {
        FieldInfo singletonField = typeof(SingletonMonoBehaviour<GridAPI>)
            .GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(singletonField, Is.Not.Null);

        object previousSingleton = singletonField.GetValue(null);
        GameObject gridObject = null;
        GameObject destroyedTokenObject = null;
        GameObject disabledTokenObject = null;
        TextAsset source = null;
        try
        {
            singletonField.SetValue(null, null);
            destroyedTokenObject = new GameObject("Destroyed Waiting Token");
            destroyedTokenObject.AddComponent<Token>();
            disabledTokenObject = new GameObject("Disabled Waiting Token");
            Token disabledToken = disabledTokenObject.AddComponent<Token>();
            disabledToken.enabled = false;

            Object.Destroy(destroyedTokenObject);
            yield return null;
            Assert.That(destroyedTokenObject == null, Is.True);

            gridObject = new GameObject("Grid After Token Cleanup");
            gridObject.SetActive(false);
            source = new TextAsset(@"{""version"":1,""rows"":["".""],""objects"":[]}");
            KayKitDungeonCatalog catalog = AssetDatabase.LoadAssetAtPath<KayKitDungeonCatalog>(
                "Assets/KayKit/Catalogs/KayKitDungeonCatalog.asset");
            Map map = gridObject.AddComponent<Map>();
            map.ConfigureJson(source, catalog);
            GridBase grid = gridObject.AddComponent<GridBase>();
            gridObject.SetActive(true);

            Tile tile = grid.GetTiles()[0, 0];
            Assert.That(tile.Occupants, Is.Empty);

            disabledToken.enabled = true;
            Assert.That(tile.Occupants, Has.Count.EqualTo(1));
            Assert.That(tile.Occupants[0], Is.SameAs(disabledTokenObject));
            LogAssert.NoUnexpectedReceived();
        }
        finally
        {
            if (disabledTokenObject != null)
                Object.DestroyImmediate(disabledTokenObject);
            if (destroyedTokenObject != null)
                Object.DestroyImmediate(destroyedTokenObject);
            if (gridObject != null)
                Object.DestroyImmediate(gridObject);
            if (source != null)
                Object.DestroyImmediate(source);
            singletonField.SetValue(null, previousSingleton);
        }
    }
}

public sealed class MapGenerationLifecyclePlayModeTests
{
    [UnityTest]
    public IEnumerator CatalogWallPlacement_HasConsistentGridTargetingAndPhysicsBlocking()
    {
        GameObject mapObject = null;
        TextAsset source = null;
        Tile[,] tiles = null;
        try
        {
            KayKitDungeonCatalog catalog = AssetDatabase.LoadAssetAtPath<KayKitDungeonCatalog>(
                "Assets/KayKit/Catalogs/KayKitDungeonCatalog.asset");
            KayKitDungeonCatalogEntry wall = catalog.Entries.Single(entry =>
                entry.Id.EndsWith("/wall", System.StringComparison.Ordinal));
            source = new TextAsset(
                $"{{\"version\":1,\"rows\":[\"...\",\"...\",\"...\"],\"objects\":[{{\"assetId\":\"{wall.Id}\",\"x\":1,\"z\":1}}]}}");
            mapObject = new GameObject("Wall Placement Semantics");
            Map map = mapObject.AddComponent<Map>();
            map.ConfigureJson(source, catalog);

            Assert.That(map.TryGenerate(out MapSourceValidationResult validation), Is.True,
                string.Join("\n", validation.Errors));
            TileType[,] gridData = map.GetMapData();
            bool[,] lineOfSightBlocks = map.GetLineOfSightBlocks();
            tiles = new[,]
            {
                { new Tile(), new Tile(), new Tile() },
                { new Tile(), null, new Tile() },
                { new Tile(), new Tile(), new Tile() }
            };
            GridLineOfSightData.Register(tiles, lineOfSightBlocks, gridData);
            Transform placedWall = mapObject.transform.Find(
                "GeneratedMap/Objects/Object_000_dungeon_assets_fbx_unity__wall");

            Assert.That(gridData[1, 1], Is.EqualTo(TileType.Obstacle));
            Assert.That(GridBase.IsWalkableTile(gridData[1, 1]), Is.False);
            Assert.That(lineOfSightBlocks[1, 1], Is.True);
            Assert.That(GridTargeting.IsBlocking(tiles, new Vector3Int(1, 0, 1)), Is.True);
            Assert.That(
                GridTargeting.CountClearRays(tiles, Vector3Int.zero, new Vector3Int(2, 0, 2)),
                Is.Zero);
            Assert.That(placedWall, Is.Not.Null);
            Assert.That(placedWall.GetComponent<BoxCollider>(), Is.Not.Null);
            Assert.That(placedWall.GetComponent<MapLineOfSightBlocker>(), Is.Not.Null);

            Physics.SyncTransforms();
            RaycastHit[] hits = Physics.RaycastAll(
                new Vector3(0f, 0.75f, 0f),
                new Vector3(1f, 0f, 1f).normalized,
                Mathf.Sqrt(8f),
                ~0,
                QueryTriggerInteraction.Collide);
            Assert.That(hits.Any(hit =>
                hit.collider.GetComponentInParent<MapLineOfSightBlocker>() != null), Is.True);
            yield return null;
        }
        finally
        {
            GridLineOfSightData.Unregister(tiles);
            if (mapObject != null)
                Object.DestroyImmediate(mapObject);
            if (source != null)
                Object.DestroyImmediate(source);
        }
    }

    [UnityTest]
    public IEnumerator RegenerationAndClear_DetachOwnedContentBeforeDeferredDestroy()
    {
        GameObject mapObject = null;
        Texture2D image = null;
        TextAsset source = null;
        try
        {
            mapObject = new GameObject("Play Mode Map Migration");
            Map map = mapObject.AddComponent<Map>();
            image = new Texture2D(2, 1);
            image.SetPixels(new[] { Color.red, Color.red });
            image.Apply();
            Material floor = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Dirt.mat");
            ConfigureBitmapSource(map, image, floor, 2f);
            SetLegacyBitmapMigrationPending(map);

            GameObject legacyFloor = GameObject.CreatePrimitive(PrimitiveType.Quad);
            legacyFloor.name = "Quad";
            legacyFloor.GetComponent<MeshRenderer>().sharedMaterial = floor;
            legacyFloor.transform.SetParent(mapObject.transform, true);
            GameObject spacedLegacyFloor = GameObject.CreatePrimitive(PrimitiveType.Quad);
            spacedLegacyFloor.name = "Quad";
            spacedLegacyFloor.transform.position = new Vector3(2f, 0f, 0f);
            spacedLegacyFloor.GetComponent<MeshRenderer>().sharedMaterial = floor;
            spacedLegacyFloor.transform.SetParent(mapObject.transform, true);
            GameObject manual = new("Manual Infrastructure");
            manual.transform.SetParent(mapObject.transform, false);

            KayKitDungeonCatalog catalog = AssetDatabase.LoadAssetAtPath<KayKitDungeonCatalog>(
                "Assets/KayKit/Catalogs/KayKitDungeonCatalog.asset");
            source = new TextAsset(@"{""version"":1,""rows"":[""..""],""objects"":[]}");
            map.ConfigureJson(source, catalog);

            Assert.That(map.TryGenerate(out MapSourceValidationResult firstResult), Is.True,
                string.Join("\n", firstResult.Errors));
            Transform firstGenerated = mapObject.transform.Find("GeneratedMap");
            int firstGeneratedId = firstGenerated.GetInstanceID();
            Assert.That(mapObject.transform.childCount, Is.EqualTo(2));
            Assert.That(manual.transform.parent, Is.SameAs(mapObject.transform));

            GameObject manualLookalike = GameObject.CreatePrimitive(PrimitiveType.Quad);
            manualLookalike.name = "Quad";
            manualLookalike.GetComponent<MeshRenderer>().sharedMaterial = floor;
            manualLookalike.transform.SetParent(mapObject.transform, true);
            GameObject spacedManualLookalike = GameObject.CreatePrimitive(PrimitiveType.Quad);
            spacedManualLookalike.name = "Quad";
            spacedManualLookalike.transform.position = new Vector3(2f, 0f, 0f);
            spacedManualLookalike.GetComponent<MeshRenderer>().sharedMaterial = floor;
            spacedManualLookalike.transform.SetParent(mapObject.transform, true);

            Assert.That(map.TryGenerate(out MapSourceValidationResult secondResult), Is.True,
                string.Join("\n", secondResult.Errors));
            Transform secondGenerated = mapObject.transform.Find("GeneratedMap");
            Assert.That(secondGenerated.GetInstanceID(), Is.Not.EqualTo(firstGeneratedId));
            Assert.That(mapObject.transform.childCount, Is.EqualTo(4));
            Assert.That(manual.transform.parent, Is.SameAs(mapObject.transform));
            Assert.That(manualLookalike.transform.parent, Is.SameAs(mapObject.transform));
            Assert.That(spacedManualLookalike.transform.parent, Is.SameAs(mapObject.transform));

            yield return null;

            map.ClearGeneratedContent();
            Assert.That(mapObject.transform.Find("GeneratedMap"), Is.Null);
            Assert.That(mapObject.transform.childCount, Is.EqualTo(3));
            Assert.That(manual.transform.parent, Is.SameAs(mapObject.transform));
            Assert.That(manualLookalike.transform.parent, Is.SameAs(mapObject.transform));
            Assert.That(spacedManualLookalike.transform.parent, Is.SameAs(mapObject.transform));

            yield return null;
        }
        finally
        {
            if (mapObject != null)
                Object.DestroyImmediate(mapObject);
            if (image != null)
                Object.DestroyImmediate(image);
            if (source != null)
                Object.DestroyImmediate(source);
        }
    }

    private static void ConfigureBitmapSource(
        Map map,
        Texture2D image,
        Material floor,
        float tileSpacing)
    {
        FieldInfo settingsField = typeof(Map).GetField(
            "Settings",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(settingsField, Is.Not.Null);
        settingsField.SetValue(map, new TileSettings());
        SerializedObject serialized = new(map);
        serialized.FindProperty("ImageMap").objectReferenceValue = image;
        serialized.FindProperty("spacing").floatValue = tileSpacing;
        SerializedProperty definitions = serialized.FindProperty("Settings.TileDefinitions");
        definitions.arraySize = 1;
        SerializedProperty definition = definitions.GetArrayElementAtIndex(0);
        definition.FindPropertyRelative("Color").colorValue = Color.red;
        definition.FindPropertyRelative("Tile").enumValueIndex = (int)TileType.Ground;
        definition.FindPropertyRelative("Floor").objectReferenceValue = floor;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetLegacyBitmapMigrationPending(Map map)
    {
        SerializedObject serialized = new(map);
        serialized.FindProperty("legacyBitmapMigrationVersion").intValue = 0;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
