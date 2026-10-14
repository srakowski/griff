namespace Griff.Scenes;

/// <summary>
/// A rectangle of a tilemap's tiles as numbers (-1 for none), from its top left, read from the map and written back to it
/// all at once: a match-3 or falling-block board's.
/// </summary>
internal sealed class TileGrid
{
    public required int[,] Cells { get; init; }

    public int Columns => Cells.GetLength(0);

    public int Rows => Cells.GetLength(1);

    public int this[int x, int y]
    {
        get => x >= 0 && y >= 0 && x < Columns && y < Rows ? Cells[x, y] : -1;
        set => Cells[x, y] = value;
    }

    // The cells from the tilemap's top left, columns by rows.
    public static TileGrid Read(int columns, int rows, Tilemap map)
    {
        var cells = new int[Math.Max(0, columns), Math.Max(0, rows)];
        for (var y = 0; y < cells.GetLength(1); y++)
        {
            for (var x = 0; x < cells.GetLength(0); x++)
                cells[x, y] = map.GetTile(x, y);
        }

        return new TileGrid { Cells = cells };
    }

    // Writes every cell at once (one change to the map, not one per cell), keeping whatever lies outside the board.
    public void Write(Tilemap map)
    {
        var rows = map.Tiles.ToList();
        while (rows.Count < Rows)
            rows.Add("");
        for (var y = 0; y < Rows; y++)
        {
            var row = rows[y].PadRight(Columns, Tilemap.Empty).ToCharArray();
            for (var x = 0; x < Columns; x++)
                row[x] = Cells[x, y] < 0 ? Tilemap.Empty : Tilemap.CharOf(Cells[x, y]);
            rows[y] = new string(row).TrimEnd(Tilemap.Empty);
        }

        while (rows.Count > 0 && rows[^1].Length == 0)
            rows.RemoveAt(rows.Count - 1);
        if (!rows.SequenceEqual(map.Tiles))
            map.Tiles = rows;
    }

    public TileGrid Copy() => new() { Cells = (int[,])Cells.Clone() };
}
