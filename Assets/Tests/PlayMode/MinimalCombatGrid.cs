using System.Collections;
using Game.Rules.Runtime;
using GridPrivate;
using GridPublic;
using UnityEngine;

/// <summary>Provides a real initialized grid/topology binding for combat PlayMode fixtures.</summary>
internal sealed class MinimalCombatGrid : GridAPI, GridAPIPrivate
{
    private const int Size = 32;
    private Tile[,] tiles;
    private bool[,] lineOfSightBlocks;
    private IPathfinder pathfinder;

    /// <inheritdoc/>
    protected override void Awake()
    {
        base.Awake();
        tiles = new Tile[Size, Size];
        lineOfSightBlocks = new bool[Size, Size];
        for (int x = 0; x < Size; x++)
        {
            for (int z = 0; z < Size; z++)
                tiles[x, z] = new Tile();
        }
        pathfinder = new Dijkstra(tiles);
    }

    /// <inheritdoc/>
    public Tile[,] GetTiles() => tiles;

    /// <inheritdoc/>
    public bool[,] GetLineOfSightBlocks() => lineOfSightBlocks;

    /// <inheritdoc/>
    public IPathfinder GetPathfinder() => pathfinder;

    /// <inheritdoc/>
    public bool AddToken(GameObject token)
    {
        if (token == null)
            return false;
        Vector3Int cell = Vector3Int.RoundToInt(token.transform.position);
        if (cell.x < 0 || cell.z < 0 || cell.x >= Size || cell.z >= Size)
            return false;
        if (!tiles[cell.x, cell.z].Occupants.Contains(token))
            tiles[cell.x, cell.z].Occupants.Add(token);
        return true;
    }

    /// <inheritdoc/>
    public override bool DestroyToken(GameObject token)
    {
        if (token == null)
            return false;
        bool removed = false;
        foreach (Tile tile in tiles)
            removed |= tile.Occupants.Remove(token);
        return removed;
    }

    /// <inheritdoc/>
    public override IEnumerator SelectStridePath(
        GameObject character,
        StridePathSelectionRequest request,
        CoroutineResult<SelectionOutcome<MovementPath>> selection
    )
    {
        selection.Value = SelectionOutcome<MovementPath>.Cancelled;
        yield break;
    }

    /// <inheritdoc/>
    public override IEnumerator GetStrikeTarget(
        GameObject attacker,
        StrikeTargetRequest request,
        CoroutineResult<StrikeTargetResult> target
    )
    {
        yield break;
    }

    /// <inheritdoc/>
    public override IEnumerator GetAreaTarget(
        AreaTargetSource source,
        AreaTargetRequest request,
        CoroutineResult<AreaTargetResult> target
    )
    {
        yield break;
    }
}
