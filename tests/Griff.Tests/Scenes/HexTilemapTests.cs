using System.Numerics;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Scenes;

/// <summary>Hex tilemaps: hexagon cells in rows, odd rows shifted half a tile right, drawn, painted and filled.</summary>
public class HexTilemapTests
{
    // 4-unit hexagons: rows are 3 apart, odd rows 2 further right, and a corner is 1 or 2 from the middle up and down.
    private static Tilemap Map(params string[] rows) => new() { Tiles = rows, Shape = TileShape.Hex, TileSize = 4 };

    private static Vector2 Center(SpriteDraw draw) => new(draw.World.M41, draw.World.M42);

    private static Vector2 Corner(SpriteDraw draw, float x, float y) => Vector2.Transform(new Vector2(x, y), draw.World);

    [Fact]
    public void Cells_AreHexagons_InRowsThreeQuartersOfATileApart_OddRowsShiftedRight()
    {
        var map = Map();

        Assert.Equal(new Vector2(2, -2), map.CellCenter(0, 0));
        Assert.Equal(new Vector2(6, -2), map.CellCenter(1, 0));
        Assert.Equal(new Vector2(4, -5), map.CellCenter(0, 1));
        Assert.Equal(new Vector2(2, -8), map.CellCenter(0, 2));
        Assert.Equal(new Vector2(0, -11), map.CellCenter(-1, 3));
        Assert.Equal(new Vector2(0, 1), map.CellCenter(-1, -1));

        // One cell: its six corners, from the top left one round clockwise.
        Assert.Equal([new Vector2(0, -1), new Vector2(2, 0), new Vector2(4, -1), new Vector2(4, -3), new Vector2(2, -4), new Vector2(0, -3)],
            map.OutlineOf(0, 0, 0, 0));
    }

    [Fact]
    public void OutlineOf_ABlockOfCells_FollowsTheHexagonsEdgesRoundIt()
    {
        var map = Map();

        // Two by two: along the top's peaks, down the right side (the odd row sticking out), back and up the left.
        Assert.Equal(
        [
            new Vector2(0, -1), new Vector2(2, 0), new Vector2(4, -1), new Vector2(6, 0), new Vector2(8, -1), new Vector2(8, -3),
            new Vector2(10, -4), new Vector2(10, -6), new Vector2(8, -7), new Vector2(6, -6), new Vector2(4, -7), new Vector2(2, -6),
            new Vector2(2, -4), new Vector2(0, -3),
        ], map.OutlineOf(0, 0, 1, 1));

        // Three rows of one: the even rows stick out on the left, the odd one on the right.
        var outline = map.OutlineOf(0, 0, 0, 2);
        Assert.Equal(14, outline.Length);
        Assert.Contains(new Vector2(0, -7), outline);
        Assert.Contains(new Vector2(6, -6), outline);
        Assert.Equal(outline.Length, outline.Distinct().Count());
    }

    [Fact]
    public void CellAt_FindsTheHexagonAPointIsIn()
    {
        var map = Map();

        for (var y = -3; y < 5; y++)
        {
            for (var x = -3; x < 5; x++)
                Assert.Equal((x, y), map.CellAt(map.CellCenter(x, y)));
        }

        // Cell 0, 0's middle is 2, -2. Just inside its corners and sides, and just past them into its neighbors.
        Assert.Equal((0, 0), map.CellAt(new Vector2(2, -0.1f)));
        Assert.Equal((0, 0), map.CellAt(new Vector2(0.1f, -2)));
        Assert.Equal((0, 0), map.CellAt(new Vector2(3.9f, -2.9f)));
        Assert.Equal((0, 0), map.CellAt(new Vector2(2, -3.9f)));
        Assert.Equal((-1, 0), map.CellAt(new Vector2(-0.1f, -2)));
        Assert.Equal((-1, -1), map.CellAt(new Vector2(0.5f, -0.5f)));
        Assert.Equal((0, -1), map.CellAt(new Vector2(3.5f, -0.5f)));
        Assert.Equal((-1, 1), map.CellAt(new Vector2(0.5f, -3.6f)));
        Assert.Equal((0, 1), map.CellAt(new Vector2(3.5f, -3.6f)));
    }

    [Fact]
    public void Neighbors_AreTheSixRoundAHexagon()
    {
        var map = Map();

        Assert.Equal([(-1, -1), (0, -1), (1, 0), (0, 1), (-1, 1), (-1, 0)], map.Neighbors(0, 0));
        Assert.Equal([(0, 0), (1, 0), (1, 1), (1, 2), (0, 2), (-1, 1)], map.Neighbors(0, 1));
        // Each shares a side (two corners) with the cell.
        var outline = map.OutlineOf(2, 3, 2, 3);
        foreach (var (x, y) in map.Neighbors(2, 3))
            Assert.Equal(2, map.OutlineOf(x, y, x, y).Count(c => outline.Any(o => Vector2.Distance(o, c) < 0.01f)));

        map.Shape = TileShape.Square;
        Assert.Equal([(0, -1), (1, 0), (0, 1), (-1, 0)], map.Neighbors(0, 0));
    }

    [Fact]
    public void FloodFill_SpreadsToTheSixNeighbors()
    {
        // Cell 1, 0's neighbors below are 0, 1 and 1, 1 (an even row's are its own column and the one to its left).
        var map = Map(".0", "0.");
        Assert.Equal(2, map.FillRegion(1, 0).Count);
        map.FloodFill(1, 0, 2);
        Assert.Equal([".2", "2"], map.Tiles);

        // On a square map those two only touch at a corner.
        var square = new Tilemap { Tiles = [".0", "0."] };
        square.FloodFill(1, 0, 2);
        Assert.Equal([".2", "0"], square.Tiles);

        // And on a hex map cell 0, 0 and 1, 1 aren't neighbors.
        var apart = Map("0.", ".0");
        apart.FloodFill(0, 0, 2);
        Assert.Equal(["2", ".0"], apart.Tiles);
    }

    [Fact]
    public void PaintingAboveTheGrid_GrowsItTwoRowsAtATime_SoOddRowsStayShifted()
    {
        var map = Map("1");

        var (tiles, shiftX, shiftY) = map.Painted(0, -1, 2);

        Assert.Equal(["", "2", "1"], tiles);
        Assert.Equal((0, 2), (shiftX, shiftY));
        // Moving the map back by the rows added (as the Scene view does) keeps the old tile where it was.
        Assert.Equal(new Vector2(0, 6), map.PointAt(-shiftX, -shiftY));
        Assert.Equal(map.CellCenter(0, 0), map.CellCenter(0, 2) + map.PointAt(-shiftX, -shiftY));
    }

    [Fact]
    public void Bounds_AndPictures_CoverTheHexagons()
    {
        var map = Map("11", "11", "1");

        Assert.Equal((new Vector2(0, -10), new Vector2(10, 0)), map.Bounds());
        Assert.Equal((new Vector2(8, -5), new Vector2(4)), map.PictureOf(1, 1));
        Assert.Equal([(0, 0, 1), (1, 0, 1), (0, 1, 1), (1, 1, 1), (0, 2, 1)], map.BackToFront());
        Assert.Equal(3, map.Depths);
        Assert.Equal(1, map.DepthOf(1, 1));
    }

    [Fact]
    public void ATileWithoutAPicture_IsDrawnAsItsHexagon_InThreePartsThatDontOverlap()
    {
        var map = Map(".1");
        new Entity("Level").AddComponent(map);

        var draws = map.TileDraws().ToList();

        Assert.Equal(3, draws.Count);
        var hexagon = map.OutlineOf(1, 0, 1, 0);
        var middle = map.CellCenter(1, 0);
        foreach (var draw in draws)
        {
            // Each is a parallelogram from the middle out to three of the corners in a row.
            var corners = new[] { Corner(draw, -0.5f, -0.5f), Corner(draw, 0.5f, -0.5f), Corner(draw, 0.5f, 0.5f), Corner(draw, -0.5f, 0.5f) };
            Assert.Contains(corners, c => Vector2.Distance(c, middle) < 0.05f);
            Assert.Equal(3, corners.Count(c => hexagon.Any(h => Vector2.Distance(c, h) < 0.05f)));
        }

        // Together they cover the whole hexagon once: their areas add up to its area (3/4 of the tile's square).
        var area = draws.Sum(d => MathF.Abs(d.World.M11 * d.World.M22 - d.World.M12 * d.World.M21));
        Assert.Equal(12, area, 0.2f);
        Assert.All(hexagon, h => Assert.Contains(draws, d => new[] { Corner(d, 0.5f, -0.5f), Corner(d, 0.5f, 0.5f), Corner(d, -0.5f, 0.5f) }.Any(c => Vector2.Distance(c, h) < 0.05f)));
    }

    [Fact]
    public void TileDraws_CullToTheView()
    {
        var map = Map(Enumerable.Repeat(new string('1', 30), 30).ToArray());
        map.Image = "Tiles/hex.png";
        new Entity("Level").AddComponent(map);
        var view = new Box(map.CellCenter(15, 15), new Vector2(4, 4));

        var drawn = map.TileDraws(view).Select(Center).ToList();

        Assert.InRange(drawn.Count, 4, 60);
        Assert.Contains(map.PictureOf(15, 15).Center, drawn);
        Assert.Contains(map.PictureOf(14, 14).Center, drawn);
        Assert.Contains(map.PictureOf(16, 16).Center, drawn);
    }

    [Fact]
    public void SortByY_PutsEachRowAmongTheSprites()
    {
        var scene = new Scene();
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().SortByY = true;
        scene.Add(camera);
        var level = new Entity("Level");
        var map = Map("1", "1", "1");
        (map.Image, map.SortByY, map.TileSize) = ("Tiles/hex.png", true, 1);
        level.AddComponent(map);
        scene.Add(level);
        var player = new Entity("Player");
        player.Transform.Position = new Vector3(map.CellCenter(0, 1) + new Vector2(0, -0.1f), 0);
        player.AddComponent<Sprite>();
        scene.Add(player);

        Assert.Equal(map.CellCenter(0, 1).Y, map.DepthY(1));
        var order = SceneRenderer.Build(scene, 1).Sprites.Where(d => d.Entity == level || d.Entity == player).Select(d => d.Entity == player ? "P" : "T");
        Assert.Equal(["T", "T", "P", "T"], order);
    }

    [Fact]
    public void HexMaps_CollideAsHexagons_NotBoxesOrDiamonds_AndCastShadows()
    {
        var scene = new Scene();
        var level = new Entity("Level");
        var map = Map("11", "11");
        level.AddComponent(map);
        scene.Add(level);

        Assert.Empty(Physics.TileShapes(map));
        Assert.Equal(2, Physics.WallShapes(map).Count());
        Assert.Equal(14, Shadows.Edges(scene).Count);
    }

    [Fact]
    public void A_cells_corners_and_neighbors_one_at_a_time_are_those_its_outline_and_neighbors_list()
    {
        var map = Map();
        foreach (var (x, y) in new[] { (0, 0), (1, 0), (0, 1), (3, 1), (2, 4), (-1, 3) })
        {
            Assert.Equal(map.OutlineOf(x, y, x, y), Enumerable.Range(0, 6).Select(i => map.HexCorner(x, y, i)));
            Assert.Equal(map.Neighbors(x, y), Enumerable.Range(0, 6).Select(i => Tilemap.NeighborOf(x, y, i)));
        }
    }

    [Fact]
    public void Shape_SavesAndLoads()
    {
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var scene = new Scene();
        var entity = new Entity("Level");
        entity.AddComponent(Map("1"));
        scene.Add(entity);

        var loaded = serializer.LoadFromString(serializer.SaveToString(scene)).Roots[0].GetComponent<Tilemap>()!;

        Assert.Equal(TileShape.Hex, loaded.Shape);
        Assert.Equal((0, 1), loaded.CellAt(new Vector2(4, -5)));
    }
}
