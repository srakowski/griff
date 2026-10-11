using System.Numerics;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Scenes;

/// <summary>Layers: tilemaps inside a tilemap's entity, with no image of their own, painted with its tilesets.</summary>
public class TilemapLayerTests
{
    private const float Step = 1 / 60f;
    private readonly Scene _scene = new();

    // Level, a 4x4 tileset of 2-unit tiles, and one layer inside it.
    private (Tilemap Map, Tilemap Layer) AddMap(string[] tiles, string[] layerTiles)
    {
        var level = new Entity("Level");
        var map = level.AddComponent<Tilemap>();
        (map.Image, map.Columns, map.Rows, map.TileSize) = ("Tiles/ground.png", 4, 4, 2);
        map.MoreTilesets = ["Tiles/props.png 2x2"];
        map.Tiles = tiles;
        _scene.Add(level);

        var decor = new Entity("Decor");
        var layer = decor.AddComponent<Tilemap>();
        layer.Tiles = layerTiles;
        _scene.Add(decor, level);
        return (map, layer);
    }

    [Fact]
    public void ATilemapWithNoImage_InsideATilemap_IsALayerOfIt_UsingItsTilesets()
    {
        var (map, layer) = AddMap(["00"], ["1."]);

        Assert.Same(map, layer.LayerOf);
        Assert.Same(map, layer.BaseMap);
        Assert.Null(map.LayerOf);
        Assert.Equal([map, layer], map.Layers());
        Assert.Equal([map, layer], layer.Layers());

        Assert.Equal(2, layer.TileSize);
        Assert.Equal(map.Tilesets(), layer.Tilesets());
        Assert.Equal("props", layer.TilesetOf(101).Name);
        Assert.Equal("Tiles/ground.png", layer.ImageOf(1));
        Assert.Equal((1, 0), layer.CellAt(new Vector2(3, -1)));

        // An image of its own makes it a map of its own again.
        layer.Image = "Tiles/water.png";
        Assert.Null(layer.LayerOf);
        Assert.Equal(1, layer.TileSize);
        Assert.Equal([map], map.Layers());
    }

    [Fact]
    public void LayersOfLayers_ComeAfterTheirLayer_AndUseTheBaseMapsTilesets()
    {
        var (map, layer) = AddMap(["0"], ["1"]);
        var inner = new Entity("Inner");
        var innerLayer = inner.AddComponent<Tilemap>();
        _scene.Add(inner, layer.Entity);
        var other = new Entity("Other");
        var otherLayer = other.AddComponent<Tilemap>();
        _scene.Add(other, map.Entity);
        // Something else inside the map isn't a layer.
        _scene.Add(new Entity("Coin"), map.Entity);

        Assert.Equal([map, layer, innerLayer, otherLayer], innerLayer.Layers());
        Assert.Same(map, innerLayer.BaseMap);
        Assert.Same(layer, innerLayer.LayerOf);
        Assert.Equal(2, innerLayer.TileSize);
    }

    [Fact]
    public void ALayer_UsesItsMapsTileSettings_AndSeesThemChange()
    {
        var (map, layer) = AddMap(["0"], ["0123"]);
        map.AnimatedTiles = "2-3 @1";

        Assert.Equal([(0, 0, 4, false)], layer.CollisionRuns());
        map.BackgroundTiles = "1";
        map.OneWayTiles = "3";
        map.LadderTiles = "2";
        Assert.Equal([(0, 0, 1, false), (3, 0, 1, true)], layer.CollisionRuns());
        Assert.True(layer.IsLadder(2));
        Assert.True(layer.IsBackground(1));

        map.SlopeUpTiles = "0";
        Assert.Equal([(0, 0, true)], layer.SlopeCells());

        layer.AnimationTime = 1;
        Assert.Equal(3, layer.ShownTile(2));
    }

    [Fact]
    public void EachLayer_CollidesByItsOwnSettings()
    {
        // The map's floor is solid; the layer's ledge above it isn't, so the player falls through it to the floor.
        var (map, layer) = AddMap([".....", ".....", "....."], ["", "000"]);
        map.Tiles = ["", "", "", "00000"];
        layer.Solid = false;
        var player = new Entity("Player");
        player.Transform.Position = new Vector3(3, 0, 0);
        player.AddComponent<BoxCollider>().Size = new Vector2(0.8f, 0.8f);
        player.AddComponent<Body>();
        _scene.Add(player);
        var game = new Game(_scene, new InputState());

        for (var t = 0f; t < 2; t += Step)
            game.Tick(Step);
        // The floor (row 3, 2-unit tiles) tops out at y -6.
        Assert.Equal(-5.6f, player.Transform.Position.Y, 2);

        // Made solid, the layer's ledge (row 1) catches the player, tops out at y -2.
        layer.Solid = true;
        player.Transform.Position = new Vector3(3, 0, 0);
        player.GetComponent<Body>()!.Velocity = Vector2.Zero;
        for (var t = 0f; t < 2; t += Step)
            game.Tick(Step);
        Assert.Equal(-1.6f, player.Transform.Position.Y, 2);

        // On a collision layer the player ignores, it lets the player through again.
        layer.Layer = "Ghostly";
        player.GetComponent<BoxCollider>()!.IgnoreLayers = "Ghostly";
        for (var t = 0f; t < 2; t += Step)
            game.Tick(Step);
        Assert.Equal(-5.6f, player.Transform.Position.Y, 2);
    }

    [Fact]
    public void Layers_DrawWithTheMapsTilesets_InTheirOwnOrder()
    {
        var (map, layer) = AddMap(["0"], [".", "1"]);
        layer.Order = 3;
        layer.Tint = new Color(1, 0, 0);
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>();
        _scene.Add(camera);

        var draws = SceneRenderer.Build(_scene, 1).Sprites.Where(s => s.Image is not null).ToList();

        Assert.Equal(2, draws.Count);
        Assert.All(draws, d => Assert.Equal("Tiles/ground.png", d.Image));
        var top = draws.Single(d => d.Entity == layer.Entity);
        Assert.Equal(new Color(1, 0, 0), top.Tint);
        // Cell (0, 1) of 2-unit tiles: centered at (1, -3).
        Assert.Equal(new Vector2(1, -3), new Vector2(top.World.M41, top.World.M42));
        // Drawn after the map's, with a higher Order.
        Assert.Equal(layer.Entity, draws[^1].Entity);
    }

    [Fact]
    public void ALayer_IsSavedAndLoadedAsOne()
    {
        var (_, layer) = AddMap(["0"], ["11"]);
        (layer.Solid, layer.Layer, layer.Order) = (false, "Decor", 2);
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        using var stream = new MemoryStream();
        serializer.Save(_scene, stream);
        stream.Position = 0;

        var loaded = serializer.Load(stream);
        var copy = loaded.Entities.Single(e => e.Name == "Decor").GetComponent<Tilemap>()!;

        Assert.Equal("Level", copy.LayerOf?.Entity?.Name);
        Assert.Equal(["11"], copy.Tiles);
        Assert.Equal((false, "Decor", 2), (copy.Solid, copy.Layer, copy.Order));
        Assert.Equal(2, copy.TileSize);
    }

    [Fact]
    public void ALayer_HidesTheSettingsItTakesFromItsMap()
    {
        var (map, layer) = AddMap(["0"], ["1"]);

        Assert.True(map.Uses(nameof(Tilemap.TileSize)));
        Assert.Null(map.SetupNote());
        Assert.False(layer.Uses(nameof(Tilemap.TileSize)));
        Assert.False(layer.Uses(nameof(Tilemap.MoreTilesets)));
        Assert.False(layer.Uses(nameof(Tilemap.BackgroundTiles)));
        Assert.True(layer.Uses(nameof(Tilemap.Image)));
        Assert.True(layer.Uses(nameof(Tilemap.Tiles)));
        Assert.True(layer.Uses(nameof(Tilemap.Solid)));
        Assert.True(layer.Uses(nameof(Tilemap.Layer)));
        Assert.StartsWith("A layer of Level", layer.SetupNote());
    }
}
