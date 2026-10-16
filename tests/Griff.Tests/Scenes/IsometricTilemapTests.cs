using System.Numerics;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Scenes;

/// <summary>Isometric tilemaps: diamond cells, tiles drawn back to front, standing up, and sorted among the sprites.</summary>
public class IsometricTilemapTests
{
    // A 2-unit-wide diamond is 1 unit tall: a step down a column is (1, -0.5) and a step down a row is (-1, -0.5).
    private static Tilemap Map(params string[] rows) => new() { Tiles = rows, Shape = TileShape.Isometric, TileSize = 2 };

    private static Vector2 Center(SpriteDraw draw) => new(draw.World.M41, draw.World.M42);

    private static Vector2 Corner(SpriteDraw draw, float x, float y) => Vector2.Transform(new Vector2(x, y), draw.World);

    [Fact]
    public void Cells_AreDiamonds_RunningDownRightAndDownLeft()
    {
        var map = Map();

        Assert.Equal(new Vector2(0, -0.5f), map.CellCenter(0, 0));
        Assert.Equal(new Vector2(1, -1), map.CellCenter(1, 0));
        Assert.Equal(new Vector2(-1, -1), map.CellCenter(0, 1));
        Assert.Equal(new Vector2(0, -1.5f), map.CellCenter(1, 1));
        Assert.Equal([new Vector2(0, 0), new Vector2(1, -0.5f), new Vector2(0, -1), new Vector2(-1, -0.5f)], map.OutlineOf(0, 0, 0, 0));
        Assert.Equal([new Vector2(0, 0), new Vector2(2, -1), new Vector2(1, -1.5f), new Vector2(-1, -0.5f)], map.OutlineOf(0, 0, 1, 0));
    }

    [Fact]
    public void CellAt_FindsTheDiamondAPointIsIn()
    {
        var map = Map();

        for (var y = -2; y < 4; y++)
        {
            for (var x = -2; x < 4; x++)
                Assert.Equal((x, y), map.CellAt(map.CellCenter(x, y)));
        }

        // Just inside each corner of cell 0, 0, and just past them into its neighbors.
        Assert.Equal((0, 0), map.CellAt(new Vector2(0, -0.05f)));
        Assert.Equal((0, 0), map.CellAt(new Vector2(0.9f, -0.5f)));
        Assert.Equal((1, 0), map.CellAt(new Vector2(1.1f, -0.6f)));
        Assert.Equal((0, 1), map.CellAt(new Vector2(-1.1f, -0.6f)));
        Assert.Equal((-1, -1), map.CellAt(new Vector2(0, 0.05f)));
        Assert.Equal((1, 1), map.CellAt(new Vector2(0, -1.05f)));
    }

    [Fact]
    public void SquareMaps_KeepTheirGeometry()
    {
        var map = new Tilemap { TileSize = 2, Tiles = ["11", "1"] };

        Assert.Equal(new Vector2(3, -1), map.CellCenter(1, 0));
        Assert.Equal((1, 0), map.CellAt(new Vector2(3, -1)));
        Assert.Equal([new Vector2(0, 0), new Vector2(4, 0), new Vector2(4, -4), new Vector2(0, -4)], map.OutlineOf(0, 0, 1, 1));
        Assert.Equal((new Vector2(0, -4), new Vector2(4, 0)), map.Bounds());
        Assert.Equal((map.CellCenter(1, 0), new Vector2(2)), map.PictureOf(1, 0));
        Assert.Equal([(0, 0, 1), (1, 0, 1), (0, 1, 1)], map.BackToFront());
    }

    [Fact]
    public void Tiles_StandOnTheirCellsBottomCorner_AsTallAsTileHeightSays()
    {
        var map = Map("1");

        Assert.Equal((new Vector2(0, -0.5f), new Vector2(2, 1)), map.PictureOf(0, 0));

        map.TileHeight = 1;
        Assert.Equal((new Vector2(0, 0), new Vector2(2, 2)), map.PictureOf(0, 0));
        Assert.Equal((new Vector2(1, -0.5f), new Vector2(2, 2)), map.PictureOf(1, 0));

        // The grid's box takes in the pictures standing up from it.
        map.Tiles = ["11", "11"];
        Assert.Equal((new Vector2(-2, -2), new Vector2(2, 1)), map.Bounds());
    }

    [Fact]
    public void Tiles_AreListedAndDrawn_DiagonalByDiagonal_BackToFront()
    {
        var map = Map("012", "345");
        var entity = new Entity("Level");
        entity.AddComponent(map);

        Assert.Equal([(0, 0, 0), (0, 1, 3), (1, 0, 1), (1, 1, 4), (2, 0, 2), (2, 1, 5)], map.BackToFront());
        Assert.Equal(4, map.Depths);
        Assert.Equal([(1, 1, 4), (2, 0, 2)], map.CellsAtDepth(2));
        Assert.Equal([(2, 1, 5)], map.CellsAtDepth(3));
        Assert.Empty(map.CellsAtDepth(4));

        var draws = map.TileDraws().ToList();
        Assert.Equal([map.PictureOf(0, 0).Center, map.PictureOf(0, 1).Center, map.PictureOf(1, 0).Center], draws.Take(3).Select(Center));
    }

    [Fact]
    public void ATileWithoutAPicture_IsDrawnAsItsDiamond()
    {
        var map = Map("1");
        new Entity("Level").AddComponent(map);

        var draw = map.TileDraws().Single();

        // The unit quad's corners land on the diamond's (a hair outside, so neighbors meet without a seam).
        Assert.Equal(new Vector2(0, -0.5f), Center(draw));
        var corners = new[] { Corner(draw, -0.5f, -0.5f), Corner(draw, 0.5f, -0.5f), Corner(draw, 0.5f, 0.5f), Corner(draw, -0.5f, 0.5f) };
        foreach (var expected in map.OutlineOf(0, 0, 0, 0))
            Assert.Contains(corners, c => Vector2.Distance(c, expected) < 0.01f);
    }

    [Fact]
    public void TileDraws_CullToTheView_KeepingTallTilesBelowItThatReachUpIntoIt()
    {
        var rows = Enumerable.Repeat(new string('1', 20), 20).ToArray();
        var map = Map(rows);
        map.Image = "Tiles/iso.png";
        new Entity("Level").AddComponent(map);
        var view = new Box(map.CellCenter(10, 10), new Vector2(1, 0.5f));

        var flat = map.TileDraws(view).Count();
        Assert.InRange(flat, 4, 60);
        Assert.Contains(map.TileDraws(view), d => Center(d) == map.PictureOf(10, 10).Center);

        // Tiles three widths tall: the ones up to six rows below the view poke up into it.
        map.TileHeight = 3;
        var tall = map.TileDraws(view).Select(Center).ToList();
        Assert.True(tall.Count > flat);
        Assert.Contains(map.PictureOf(13, 13).Center, tall);
    }

    [Fact]
    public void Layers_TakeTheirMapsShapeAndTileHeight_AndTheInspectorShowsTileHeightOnlyOnIsometricMaps()
    {
        var scene = new Scene();
        var level = new Entity("Level");
        var map = level.AddComponent<Tilemap>();
        map.Image = "Tiles/iso.png";
        scene.Add(level);
        var walls = new Entity("Walls");
        var layer = walls.AddComponent<Tilemap>();
        scene.Add(walls, level);

        Assert.False(map.Uses(nameof(Tilemap.TileHeight)));
        map.Shape = TileShape.Isometric;
        map.TileHeight = 1;

        Assert.Equal(TileShape.Isometric, layer.Shape);
        Assert.Equal(1, layer.TileHeight);
        Assert.True(map.Uses(nameof(Tilemap.TileHeight)));
        Assert.True(map.Uses(nameof(Tilemap.Shape)));
        Assert.False(layer.Uses(nameof(Tilemap.Shape)));
        Assert.False(layer.Uses(nameof(Tilemap.TileHeight)));
        Assert.True(layer.Uses(nameof(Tilemap.SortByY)));
    }

    [Fact]
    public void SortByY_OnTheMap_PutsEachRowAmongTheSprites_WhenTheCameraSortsByY()
    {
        var (scene, camera, map) = SceneWithMap(Map("11", "11"));
        (map.Image, map.SortByY, map.TileHeight) = ("Tiles/iso.png", true, 1);
        // Standing on cell 1, 0, the second diagonal: in front of the first and behind the third.
        var player = AddSprite(scene, "Player", map.CellCenter(1, 0));

        Assert.Equal(map.DepthY(1), map.CellCenter(1, 0).Y);
        Assert.Equal([0, 1, 1, 2], Order(scene, map.Entity!, player).Where(d => d.Name == "T").Select(d => d.Depth));
        Assert.Equal(["T", "T", "T", "P", "T"], Order(scene, map.Entity!, player).Select(d => d.Name));

        // A step further down the screen, it's in front of the last row too.
        player.Transform.Position = new Vector3(map.CellCenter(1, 1), 0);
        Assert.Equal(["T", "T", "T", "T", "P"], Order(scene, map.Entity!, player).Select(d => d.Name));

        // Without the camera sorting by Y, the map draws as one (still back to front), in hierarchy order.
        camera.GetComponent<Camera>()!.SortByY = false;
        Assert.Equal(["T", "T", "T", "T", "P"], Order(scene, map.Entity!, player).Select(d => d.Name));
        Assert.Equal("Turn on the camera's Sort By Y too, so its rows sort among the sprites.", map.SetupNote());
    }

    [Fact]
    public void WithoutSortByY_AMapIsAFloor_UnderEverythingOnIt()
    {
        var (scene, _, map) = SceneWithMap(Map("11", "11"));
        var player = AddSprite(scene, "Player", map.CellCenter(0, 0));
        scene.Remove(map.Entity!);
        scene.Add(map.Entity!);

        // Last in the hierarchy, but the map's own place (its top corner) is higher up than anything standing on it.
        Assert.Equal(["T", "T", "T", "T", "P"], Order(scene, map.Entity!, player).Select(d => d.Name));
        Assert.Null(map.SetupNote());
    }

    [Fact]
    public void SortByY_WorksOnSquareMapsToo_RowByRow()
    {
        var (scene, _, map) = SceneWithMap(new Tilemap { Tiles = ["1", "1", "1"], SortByY = true });
        var player = AddSprite(scene, "Player", map.CellCenter(0, 1) + new Vector2(0, -0.1f));

        Assert.Equal(["T", "T", "P", "T"], Order(scene, map.Entity!, player).Select(d => d.Name));
    }

    [Fact]
    public void DrawParts_ListsASortedMapsRows_WhereTheGameDrawsThem()
    {
        var (scene, _, map) = SceneWithMap(Map("11", "11"));
        map.SortByY = true;
        var player = AddSprite(scene, "Player", map.CellCenter(1, 0));

        var parts = Views.DrawParts(scene).Where(p => p.Entity != scene.Entities[0]).ToList();

        Assert.Equal(new (Entity, int?)[] { (map.Entity!, null), (map.Entity!, 0), (map.Entity!, 1), (player, null), (map.Entity!, 2) }, parts);
    }

    [Fact]
    public void IsometricMaps_CollideAsDiamonds_ButDontCastShadowsYet()
    {
        var (scene, _, map) = SceneWithMap(Map("11", "11"));

        Assert.Empty(Physics.TileShapes(map));
        Assert.Equal(2, Physics.IsometricShapes(map).Count());
        Assert.Empty(Shadows.Edges(scene));
        map.Shape = TileShape.Square;
        Assert.NotEmpty(Physics.TileShapes(map));
    }

    [Fact]
    public void Shape_TileHeight_AndSortByY_SaveAndLoad()
    {
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var scene = new Scene();
        var entity = new Entity("Level");
        var map = Map("1");
        map.TileHeight = 1;
        map.SortByY = true;
        entity.AddComponent(map);
        scene.Add(entity);

        var loaded = serializer.LoadFromString(serializer.SaveToString(scene)).Roots[0].GetComponent<Tilemap>()!;

        Assert.Equal(TileShape.Isometric, loaded.Shape);
        Assert.Equal(1, loaded.TileHeight);
        Assert.True(loaded.SortByY);
        Assert.Equal((0, 0), loaded.CellAt(new Vector2(0, -0.5f)));
    }

    private static (Scene Scene, Entity Camera, Tilemap Map) SceneWithMap(Tilemap map)
    {
        var scene = new Scene();
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().SortByY = true;
        camera.Transform.Position = new Vector3(0, -1, 0);
        scene.Add(camera);
        var level = new Entity("Level");
        level.AddComponent(map);
        scene.Add(level);
        return (scene, camera, map);
    }

    private static Entity AddSprite(Scene scene, string name, Vector2 position)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        entity.AddComponent<Sprite>().Size = new Vector2(0.5f);
        scene.Add(entity);
        return entity;
    }

    // The frame's draws of the map and the player, back to front: "T" for a tile, with the row it's in, or "P".
    private static List<(string Name, int Depth)> Order(Scene scene, Entity level, Entity player)
    {
        var map = level.GetComponent<Tilemap>()!;
        return SceneRenderer.Build(scene, 1).Sprites.Where(d => d.Entity == level || d.Entity == player)
            .Select(d => d.Entity == player ? ("P", -1) : ("T", DepthOf(map, d)))
            .ToList();
    }

    // The row a tile's draw is in: its picture's middle, back down to its cell's.
    private static int DepthOf(Tilemap map, SpriteDraw draw)
    {
        var (_, size) = map.PictureOf(0, 0);
        var (x, y) = map.CellAt(Center(draw) - new Vector2(0, size.Y / 2 - (map.Shape == TileShape.Isometric ? map.TileSize / 4 : size.Y / 2)));
        return map.DepthOf(x, y);
    }
}
