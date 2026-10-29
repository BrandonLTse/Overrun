using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// Row-only render copies preserve the original Walls tilemap/collider contract.
/// Doors disable the whole hierarchy; no extra collision or invisible blockers.
public sealed class OverrunWallDepth : MonoBehaviour
{
    public int RenderedTiles { get; private set; }
    readonly List<Tilemap> rows = new List<Tilemap>();
    public void Build(Tilemap source)
    {
        var original = source.GetComponent<TilemapRenderer>(); original.enabled = false;
        foreach (int y in RowNumbers(source))
        {
            var root = new GameObject("Wall visual row " + y); root.transform.SetParent(source.transform, false);
            var row = root.AddComponent<Tilemap>(); row.tileAnchor = source.tileAnchor;
            var renderer = root.AddComponent<TilemapRenderer>(); renderer.sortingLayerID = original.sortingLayerID;
            foreach (var cell in source.cellBounds.allPositionsWithin)
            {
                if (cell.y != y || !source.HasTile(cell)) continue;
                row.SetTile(cell, source.GetTile(cell)); RenderedTiles++;
            }
            row.CompressBounds();
            rows.Add(row);
        }
        Refresh();
    }
    static IEnumerable<int> RowNumbers(Tilemap source)
    {
        for (int y = source.cellBounds.yMin; y < source.cellBounds.yMax; y++)
            for (int x = source.cellBounds.xMin; x < source.cellBounds.xMax; x++)
                if (source.HasTile(new Vector3Int(x, y, 0))) { yield return y; break; }
    }
    void OnEnable()
    {
        // Cloned templates reconstruct the nonserialized row cache.
        if (rows.Count == 0)
        {
            foreach (Transform child in transform) if (child.TryGetComponent<Tilemap>(out var row)) rows.Add(row);
            RenderedTiles = 0;
            foreach (var row in rows) foreach (var cell in row.cellBounds.allPositionsWithin) if (row.HasTile(cell)) RenderedTiles++;
        }
        Refresh();
    }
    public void Refresh()
    {
        foreach (var row in rows)
        {
            var cell = new Vector3Int(0, row.cellBounds.yMin, 0);
            row.GetComponent<TilemapRenderer>().sortingOrder = OverrunDepthSort.OrderAt(row.GetCellCenterWorld(cell).y);
        }
    }
}
