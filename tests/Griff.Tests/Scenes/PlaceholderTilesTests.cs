using System.Numerics;
using Griff.Rendering;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Scenes;

/// <summary>Placeholder colors: a tilemap with no image drawing each tile number in a color of its own, for blocking out a level before any art.</summary>
public class PlaceholderTilesTests
{
    private static readonly string[] Colors = ["#3A3F4B", "#343844", "#FF00FF", "#99009A"];

    // A map on an entity of its own, to draw.
    private static Tilemap On(Tilemap map)
    {
        new Entity("Level").AddComponent(map);
        return map;
    }

    private static Color Hex(string text) => Color.TryParse(text, out var color) ? color : throw new ArgumentException(text);

    // A floor (a checker of tiles 0 and 1) and a layer of walls inside it sorting by Y (tops, tile 2, over faces, tile 3),
    // neither with an image, and a camera sorting by Y.
    private static (Scene Scene, Tilemap Floor, Tilemap Walls) TwoLayerMap()
    {
        var scene = new Scene();
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().SortByY = true;
        scene.Add(camera);

        var level = new Entity("Level");
        var floor = level.AddComponent<Tilemap>();
        floor.PlaceholderColors = Colors;
        floor.Tiles = ["0101", "1010", "0101"];
        scene.Add(level);

        var wallsEntity = new Entity("Walls");
        var walls = wallsEntity.AddComponent<Tilemap>();
        walls.SortByY = true;
        walls.Tiles = ["2222", "3333", "...."];
        scene.Add(wallsEntity, level);
        return (scene, floor, walls);
    }

    [Fact]
    public void ATwoLayerMapWithNoImage_ShowsACheckeredFloor_AndDistinctWallTopsAndFaces()
    {
        var (scene, floor, walls) = TwoLayerMap();
        Assert.Same(floor, walls.LayerOf);
        Assert.Empty(walls.PlaceholderColors);

        var frame = SceneRenderer.Build(scene, 1).Sprites;
        var floorDraws = frame.Where(d => d.Entity == floor.Entity).ToList();
        var wallDraws = frame.Where(d => d.Entity == walls.Entity).ToList();

        // The floor alternates its two colors, cell by cell, along each row and down each column.
        Assert.Equal(12, floorDraws.Count);
        for (var y = 0; y < 3; y++)
        {
            for (var x = 0; x < 4; x++)
                Assert.Equal(Hex(Colors[(x + y) % 2]), floorDraws[y * 4 + x].Tint);
        }

        // The walls layer uses the floor's colors: its tops one color, its faces another.
        Assert.Equal([.. Enumerable.Repeat(Hex(Colors[2]), 4), .. Enumerable.Repeat(Hex(Colors[3]), 4)], wallDraws.Select(d => d.Tint));
        Assert.All(floorDraws.Concat(wallDraws), d => Assert.Null(d.Image));
    }

    [Fact]
    public void WithNoColors_TilesDrawInTheTint_AsBefore()
    {
        var map = On(new Tilemap { Tiles = ["0123"], Tint = Color.Orange });

        Assert.All(map.TileDraws(), d => Assert.Equal(Color.Orange, d.Tint));
        Assert.Equal(new Tileset(null, 1, 1, 0), map.Tilesets()[0]);
        Assert.Equal(Color.White, map.PlaceholderColorOf(2));
    }

    [Fact]
    public void ColorsGoRoundTheList_AndAreTimesTheTint()
    {
        var map = On(new Tilemap { Tiles = ["5"], PlaceholderColors = ["#FF0000", "#00FF00"], Tint = new Color(1, 1, 1, 0.5f) });

        Assert.Equal(new Color(0, 1, 0), map.PlaceholderColorOf(5));
        Assert.Equal(new Color(0, 1, 0, 0.5f), Assert.Single(map.TileDraws()).Tint);
    }

    [Fact]
    public void TheColors_MakeTheFirstTilesetATileEach_UntilTheMapHasAnImage()
    {
        var map = new Tilemap { PlaceholderColors = Colors };

        Assert.Equal(new Tileset(null, 4, 1, 0), map.Tilesets()[0]);
        Assert.True(map.Uses(nameof(Tilemap.PlaceholderColors)));
        Assert.True(map.Uses(nameof(Tilemap.PlaceholderGrid)));

        (map.Image, map.Columns, map.Rows) = ("Tiles/ground.png", 2, 2);
        Assert.Equal(new Tileset("Tiles/ground.png", 2, 2, 0), map.Tilesets()[0]);
        Assert.Equal(Color.White, map.PlaceholderColorOf(1));
        Assert.False(map.Uses(nameof(Tilemap.PlaceholderColors)));
        Assert.False(map.Uses(nameof(Tilemap.PlaceholderGrid)));
    }

    [Fact]
    public void ALayer_TakesItsMapsColorsAndGrid_SoTheInspectorHidesThem()
    {
        var (_, floor, walls) = TwoLayerMap();

        Assert.Equal(floor.Tilesets(), walls.Tilesets());
        Assert.Equal(Hex(Colors[3]), walls.PlaceholderColorOf(3));
        Assert.False(walls.Uses(nameof(Tilemap.PlaceholderColors)));
        Assert.False(walls.Uses(nameof(Tilemap.PlaceholderGrid)));

        floor.PlaceholderGrid = true;
        Assert.Equal(16, walls.TileDraws().Count());
    }

    [Fact]
    public void TheGrid_DrawsALineRoundEachTile_InAShadeOfItsColor()
    {
        var map = On(new Tilemap { Tiles = ["01"], PlaceholderColors = ["#C0C0C0", "#101010"], PlaceholderGrid = true });

        var draws = map.TileDraws().ToList();

        Assert.Equal(4, draws.Count);
        foreach (var (tile, line, inside) in new[] { (0, draws[0], draws[1]), (1, draws[2], draws[3]) })
        {
            var color = map.PlaceholderColorOf(tile);
            Assert.Equal(color, inside.Tint);
            Assert.NotEqual(color, line.Tint);
            Assert.True(line.World.M11 > inside.World.M11);
            Assert.Equal(new Vector2(line.World.M41, line.World.M42), new Vector2(inside.World.M41, inside.World.M42));
        }

        // Darker round a light tile, lighter round a dark one.
        Assert.True(draws[0].Tint.R < draws[1].Tint.R);
        Assert.True(draws[2].Tint.R > draws[3].Tint.R);
    }

    [Theory]
    [InlineData(TileShape.Isometric, 1)]
    [InlineData(TileShape.Hex, 3)]
    public void OtherShapes_DrawTheirColorsToo(TileShape shape, int quadsPerTile)
    {
        var map = On(new Tilemap { Tiles = ["01"], Shape = shape, PlaceholderColors = ["#FF0000", "#0000FF"] });

        var tints = map.TileDraws().Select(d => d.Tint).ToList();

        Assert.Equal([.. Enumerable.Repeat(Color.Red, quadsPerTile), .. Enumerable.Repeat(Color.Blue, quadsPerTile)], tints);
    }

    [Fact]
    public void PlaceholderColorsAndGrid_SaveAndLoad()
    {
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var (scene, _, _) = TwoLayerMap();
        scene.Entities.Single(e => e.Name == "Level").GetComponent<Tilemap>()!.PlaceholderGrid = true;

        var loaded = serializer.LoadFromString(serializer.SaveToString(scene));
        var floor = loaded.Entities.Single(e => e.Name == "Level").GetComponent<Tilemap>()!;

        Assert.Equal(Colors, floor.PlaceholderColors);
        Assert.True(floor.PlaceholderGrid);
        Assert.Equal(Hex(Colors[2]), loaded.Entities.Single(e => e.Name == "Walls").GetComponent<Tilemap>()!.PlaceholderColorOf(2));
    }
}
