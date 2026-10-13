using System.Numerics;
using Griff.Editing;
using Griff.Rendering;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Scenes;

public class TilemapTests
{
    private static Tilemap Map(params string[] rows) => new() { Tiles = rows };

    [Fact]
    public void GetTile_ReadsCharactersAsTileNumbers()
    {
        var map = Map("01.", "a", "Z");

        Assert.Equal(0, map.GetTile(0, 0));
        Assert.Equal(1, map.GetTile(1, 0));
        Assert.Equal(-1, map.GetTile(2, 0));
        Assert.Equal(10, map.GetTile(0, 1));
        Assert.Equal(61, map.GetTile(0, 2));
        Assert.Equal(-1, map.GetTile(5, 5));
        Assert.Equal(-1, map.GetTile(-1, 0));
        Assert.Equal((3, 3), (map.Width, map.Height));
    }

    [Fact]
    public void SetTile_GrowsAndTrims()
    {
        var map = new Tilemap();

        map.SetTile(3, 1, 2);
        Assert.Equal(["", "...2"], map.Tiles);

        map.SetTile(0, 0, 0);
        map.SetTile(3, 1, -1);
        Assert.Equal(["0"], map.Tiles);

        Assert.Throws<ArgumentOutOfRangeException>(() => map.SetTile(0, 0, Tilemap.MaxTiles));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.SetTile(-1, 0, 1));
    }

    [Fact]
    public void SetTile_ReplacesTheArray_SoEarlierValuesStayIntact()
    {
        var map = Map("00");
        var before = map.Tiles;

        map.SetTile(0, 0, 1);

        Assert.Equal(["00"], before);
        Assert.Equal(["10"], map.Tiles);
    }

    [Fact]
    public void Painted_LeftOrAbove_ShiftsTheGrid()
    {
        var map = Map("11");

        var (tiles, shiftX, shiftY) = map.Painted(-2, -1, 3);

        Assert.Equal((2, 1), (shiftX, shiftY));
        Assert.Equal(["3", "..11"], tiles);
        Assert.Equal(["11"], map.Tiles); // the map itself is untouched
    }

    [Fact]
    public void Painted_ErasingOutside_ChangesNothing()
    {
        var map = Map("11");

        var (tiles, shiftX, shiftY) = map.Painted(-3, 4, -1);

        Assert.Same(map.Tiles, tiles);
        Assert.Equal((0, 0), (shiftX, shiftY));
    }

    [Fact]
    public void PaintedRect_FillsBetweenCorners_EitherWayRound()
    {
        var map = Map("1");

        var (tiles, shiftX, shiftY) = map.PaintedRect(3, 2, 1, 1, 2);

        Assert.Equal((0, 0), (shiftX, shiftY));
        Assert.Equal(["1", ".222", ".222"], tiles);
        Assert.Equal(["1"], map.Tiles);
    }

    [Fact]
    public void PaintedRect_LeftOrAbove_ShiftsTheGrid()
    {
        var map = Map("11");

        var (tiles, shiftX, shiftY) = map.PaintedRect(-1, -1, 0, 0, 3);

        Assert.Equal((1, 1), (shiftX, shiftY));
        Assert.Equal(["33", "331"], tiles);
    }

    [Fact]
    public void PaintedRect_Erasing_OnlyReachesTheGrid_AndTrims()
    {
        var map = Map("111", "111", "1");

        var (tiles, shiftX, shiftY) = map.PaintedRect(-5, 1, 9, 9, -1);
        Assert.Equal((0, 0), (shiftX, shiftY));
        Assert.Equal(["111"], tiles);

        Assert.Same(map.Tiles, map.PaintedRect(-5, -5, -1, -1, -1).Tiles);
        // Nothing changes when every cell already holds the tile.
        Assert.Same(map.Tiles, map.PaintedRect(0, 0, 2, 1, 1).Tiles);
    }

    [Fact]
    public void FillRegion_FollowsTheSameTile_SideBySide_WithinTheGrid()
    {
        // A ring of 1s with a hole of empty cells inside, and empty cells outside it; diagonals don't join.
        var map = Map(
            "11111",
            "1...1",
            "1.2.1",
            "11111",
            ".....1");

        Assert.Equal(5, map.FillRegion(1, 1).Count); // the hole, round the 2
        Assert.Equal(14, map.FillRegion(0, 0).Count); // the ring
        Assert.Equal(5, map.FillRegion(0, 4).Count); // the empty bottom row, up to the 1 at its end
        Assert.Equal(4, map.FillRegion(5, 0).Count); // the empty column on the right, down to that 1
        Assert.Equal([(2, 2)], map.FillRegion(2, 2));
        Assert.Empty(map.FillRegion(6, 0));
        Assert.Empty(map.FillRegion(-1, 0));
    }

    [Fact]
    public void Filled_PaintsTheRegion_OrClearsIt()
    {
        var map = Map(
            "111",
            "1.1",
            "111");

        Assert.Equal(["111", "131", "111"], map.Filled(1, 1, 3));
        Assert.Equal(["222", "2.2", "222"], map.Filled(0, 0, 2));
        Assert.Empty(map.Filled(0, 0, -1));
        Assert.Same(map.Tiles, map.Filled(0, 0, 1));
        Assert.Same(map.Tiles, map.Filled(9, 9, 1));
    }

    [Fact]
    public void FillRect_And_FloodFill_ChangeTheMap_FromScripts()
    {
        var map = new Tilemap();

        map.FillRect(0, 0, 3, 2, 1);
        Assert.Equal(["1111", "1111", "1111"], map.Tiles);
        map.FillRect(1, 1, 2, 1, -1);
        Assert.Equal(["1111", "1..1", "1111"], map.Tiles);
        map.FloodFill(1, 1, 5);
        Assert.Equal(["1111", "1551", "1111"], map.Tiles);

        Assert.Throws<ArgumentOutOfRangeException>(() => map.FillRect(-1, 0, 2, 2, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.FloodFill(0, 0, Tilemap.MaxTiles));
    }

    [Fact]
    public void CellAt_And_CellCenter_AreInverse()
    {
        var map = new Tilemap { TileSize = 2 };

        Assert.Equal((0, 0), map.CellAt(new Vector2(0.1f, -0.1f)));
        Assert.Equal((1, 2), map.CellAt(new Vector2(3, -5)));
        Assert.Equal((-1, -1), map.CellAt(new Vector2(-0.5f, 0.5f)));
        Assert.Equal(new Vector2(3, -5), map.CellCenter(1, 2));
    }

    [Fact]
    public void Runs_MergeNeighboursInARow()
    {
        var map = Map("11.1", "", ".222");

        Assert.Equal([(0, 0, 2), (3, 0, 1), (1, 2, 3)], map.Runs());
    }

    [Fact]
    public void TileRect_PicksTheSheetCell()
    {
        var map = new Tilemap { Columns = 4, Rows = 2 };

        Assert.Equal(new Vector4(0.25f, 0.5f, 0.25f, 0.5f), map.TileRect(5));
    }

    [Fact]
    public void TileChars_GoPastTheFirst62_OneCharacterPerTile()
    {
        int[] tiles = [0, 10, 61, 62, 84, 85, 115, 116, 459, 460];
        Assert.Equal(['0', 'a', 'Z', 'À', 'Ö', 'Ø', 'ö', 'ø', 'ɏ', '一'], tiles.Select(Tilemap.CharOf));
        Assert.Equal(Tilemap.Empty, Tilemap.CharOf(Tilemap.MaxTiles));
        Assert.Equal(-1, Tilemap.TileOf('×'));
        Assert.Equal(-1, Tilemap.TileOf('÷'));

        var all = Enumerable.Range(0, Tilemap.MaxTiles).Select(Tilemap.CharOf).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.All(Enumerable.Range(0, Tilemap.MaxTiles), tile => Assert.Equal(tile, Tilemap.TileOf(Tilemap.CharOf(tile))));
    }

    [Fact]
    public void SetTile_PastTheFirst62_KeepsOneCharacterPerCell()
    {
        var map = new Tilemap();

        map.SetTile(0, 0, 3);
        map.SetTile(1, 0, 105);
        map.SetTile(2, 0, 2000);

        Assert.Equal(3, map.Tiles[0].Length);
        int[] columns = [0, 1, 2];
        Assert.Equal([3, 105, 2000], columns.Select(x => map.GetTile(x, 0)));
        Assert.Equal([(0, 0, 3)], map.Runs().Select(r => (r.X, r.Y, r.Length)));
    }

    [Fact]
    public void Tilesets_NumberEachOneFromTheNextHundred()
    {
        var map = new Tilemap
        {
            Image = "Tiles/ground.png", Columns = 4, Rows = 4,
            MoreTilesets = ["Tiles/props.png 4x2", "", "Tiles/big world.png 16x16", "Tiles/one.png"],
        };

        var tilesets = map.Tilesets();

        Assert.Equal(
            [("Tiles/ground.png", 4, 4, 0), ("Tiles/props.png", 4, 2, 100), ("Tiles/big world.png", 16, 16, 200), ("Tiles/one.png", 1, 1, 500)],
            tilesets.Select(t => (t.Image, t.Columns, t.Rows, t.First)));
        Assert.Equal(["ground", "props", "big world", "one"], tilesets.Select(t => t.Name));
        Assert.Equal("Tiles/props.png", map.TilesetOf(105).Image);
        Assert.Equal("Tiles/ground.png", map.TilesetOf(15).Image);
        Assert.Equal("Tiles/big world.png", map.ImageOf(455));
        Assert.Equal("Tiles/props.png 4x2", tilesets[1].ToLine());
    }

    [Fact]
    public void Tilesets_GrowingOne_LeavesTheNextOnesNumbersAlone()
    {
        var map = new Tilemap { Image = "a.png", Columns = 4, Rows = 4, MoreTilesets = ["b.png 2x2"] };
        Assert.Equal(100, map.Tilesets()[1].First);

        map.Rows = 8;

        Assert.Equal(100, map.Tilesets()[1].First);
        Assert.Equal(32, map.Tilesets()[0].Count);
    }

    [Fact]
    public void TileRect_CutsEachTileFromItsOwnTileset()
    {
        var map = new Tilemap { Image = "a.png", Columns = 4, Rows = 2, MoreTilesets = ["b.png 2x2"] };

        Assert.Equal(new Vector4(0.25f, 0.5f, 0.25f, 0.5f), map.TileRect(5));
        Assert.Equal(new Vector4(0.5f, 0.5f, 0.5f, 0.5f), map.TileRect(103));
        Assert.Equal(map.TileRect(100), map.TileRect(104));
    }

    [Fact]
    public void TileDraws_UseTheImageOfEachCellsTileset()
    {
        var entity = new Entity("Level");
        var map = entity.AddComponent<Tilemap>();
        (map.Image, map.Columns, map.Rows, map.MoreTilesets) = ("a.png", 2, 2, ["b.png 2x1"]);
        map.SetTile(0, 0, 1);
        map.SetTile(1, 0, 101);

        var draws = SceneRenderer.TileDraws(entity, map).ToList();

        Assert.Equal(["a.png", "b.png"], draws.Select(d => d.Image));
        Assert.Equal([new Vector4(0.5f, 0, 0.5f, 0.5f), new Vector4(0.5f, 0, 0.5f, 1)], draws.Select(d => d.Source));
    }

    [Fact]
    public void TileNumberFields_ReachTilesInLaterTilesets()
    {
        var map = new Tilemap { BackgroundTiles = "100-103", LadderTiles = "250", OneWayTiles = "3", Tiles = ["3"] };

        Assert.True(map.IsBackground(102));
        Assert.False(map.IsBackground(104));
        Assert.True(map.IsLadder(250));
        Assert.True(map.IsOneWay(3));
        Assert.False(map.IsOneWay(-1));
        Assert.Equal([116], Tilemap.ParseAutoTiles("116-131"));
    }

    [Fact]
    public void Tiles_PastTheFirst62_SaveAsThemselves_AndRoundTrip()
    {
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var scene = new Scene();
        var entity = new Entity("Level");
        var map = entity.AddComponent<Tilemap>();
        map.MoreTilesets = ["Tiles/props.png 4x4"];
        map.SetTile(0, 0, 1);
        map.SetTile(1, 0, 104);
        map.SetTile(2, 0, 600);
        scene.Add(entity);

        var json = serializer.SaveToString(scene);
        var loaded = serializer.LoadFromString(json).Roots[0].GetComponent<Tilemap>()!;

        Assert.Contains($"\"1{Tilemap.CharOf(104)}{Tilemap.CharOf(600)}\"", json);
        Assert.Contains("\"Tiles/props.png 4x4\"", json);
        Assert.Equal(map.Tiles, loaded.Tiles);
        Assert.Equal(104, loaded.GetTile(1, 0));
        Assert.Equal(map.MoreTilesets, loaded.MoreTilesets);
    }

    [Fact]
    public void Tiles_SaveOneRowPerLine_AndRoundTrip()
    {
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var scene = new Scene();
        var entity = new Entity("Level");
        entity.AddComponent<Tilemap>().Tiles = ["..00", "1111"];
        scene.Add(entity);

        var json = serializer.SaveToString(scene);
        var loaded = serializer.LoadFromString(json).Roots[0].GetComponent<Tilemap>()!;

        Assert.Contains("\"..00\",", json);
        Assert.Contains("\"1111\"", json);
        Assert.Equal(["..00", "1111"], loaded.Tiles);
    }

    [Fact]
    public void CopyValues_TreatsEqualTileListsAsUnchanged()
    {
        var registry = ComponentRegistry.CreateDefault();
        var a = new Entity("A");
        a.AddComponent<Tilemap>().Tiles = ["01"];
        var b = new Entity("A");
        b.AddComponent<Tilemap>().Tiles = ["01"];

        Assert.Empty(CopyValues.Between(a, b, registry));

        a.GetComponent<Tilemap>()!.SetTile(0, 0, 5);
        Assert.Single(CopyValues.Between(a, b, registry));
    }

    [Fact]
    public void Renderer_DrawsOneQuadPerTile_InTheMapsSpace()
    {
        var scene = new Scene();
        var entity = new Entity("Level");
        entity.Transform.Position = new Vector3(10, 0, 0);
        var map = entity.AddComponent<Tilemap>();
        map.Image = "tiles.png";
        map.Columns = 2;
        map.TileSize = 2;
        map.Tiles = ["0.", ".1"];
        scene.Add(entity);
        // Only what's in view is drawn, so the camera looks at the map.
        var camera = new Entity("Camera");
        camera.Transform.Position = new Vector3(12, -2, 0);
        camera.AddComponent<Camera>();
        scene.Add(camera);

        var draws = SceneRenderer.Build(scene, 1).Sprites;

        Assert.Equal(2, draws.Count);
        Assert.Equal(new Vector3(11, -1, 0), draws[0].World.Translation);
        Assert.Equal(new Vector3(13, -3, 0), draws[1].World.Translation);
        // A cell wide, plus a hair so neighbors overlap and no seam shows between them.
        Assert.InRange(draws[0].World.M11, 2f, 2.02f);
        Assert.Equal(new Vector4(0.5f, 0, 0.5f, 1), draws[1].Source);
        Assert.All(draws, d => Assert.Equal("tiles.png", d.Image));
    }

    [Fact]
    public void Renderer_WithoutImage_DrawsSolidTiles()
    {
        var scene = new Scene();
        var entity = new Entity("Level");
        entity.AddComponent<Tilemap>().Tiles = ["5"];
        scene.Add(entity);

        var draw = Assert.Single(SceneRenderer.Build(scene, 1).Sprites);

        Assert.Null(draw.Image);
        Assert.Equal(new Vector4(0, 0, 1, 1), draw.Source);
    }
}
