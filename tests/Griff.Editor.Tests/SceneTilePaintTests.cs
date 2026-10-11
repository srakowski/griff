using System.Numerics;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.SceneView;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>Painting a tilemap in the Scene view: the brush, the rectangle and the fill, through the view model.</summary>
public class SceneTilePaintTests
{
    // Level, a tilemap at 0,0 with one-unit tiles: a ring of 1s around an empty hole. Box, a sprite.
    private static (SceneDocument Document, ISelection Selection, SceneViewModel Model, Tilemap Map) Open(IServiceProvider services)
    {
        var scene = new Scene();
        var level = new Entity("Level");
        var map = level.AddComponent<Tilemap>();
        map.Columns = 4;
        map.Rows = 2;
        map.Tiles = ["1111", "1..1", "1111"];
        scene.Add(level);
        var box = new Entity("Box");
        box.AddComponent<Sprite>();
        scene.Add(box);

        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<IWorkspace>().EnterPlayMode(document);
        var selection = services.GetRequiredService<ISelection>();
        selection.Select(level);
        return (document, selection, services.GetRequiredService<SceneViewModel>(), map);
    }

    [Fact]
    public void PaintTarget_IsTheOneSelectedTilemap()
    {
        EditorHost.Run(services =>
        {
            var (document, selection, model, map) = Open(services);
            Assert.Same(map, model.PaintTarget);
            Assert.Equal((2, 1), model.CellAt(new Vector2(2.5f, -1.5f)));

            selection.SelectEntities([map.Entity!, document.Scene.Entities.Single(e => e.Name == "Box")]);
            Assert.Null(model.PaintTarget);
            Assert.Null(model.CellAt(Vector2.Zero));
            Assert.Throws<InvalidOperationException>(() => model.FillTiles(0, 0));
        });
    }

    [Fact]
    public void Rectangle_FillsABox_AsOneUndoStep_AndGrowsLeftAndUp()
    {
        EditorHost.Run(services =>
        {
            var (document, _, model, map) = Open(services);
            model.BrushTile = 2;

            model.PaintRectangle(5, 4, 4, 3);
            Assert.Equal(["1111", "1..1", "1111", "....22", "....22"], map.Tiles);
            document.History.Undo();
            Assert.Equal(["1111", "1..1", "1111"], map.Tiles);

            // Past the top-left corner the grid grows, and the entity moves back so the old tiles stay where they were.
            model.PaintRectangle(-1, -1, 0, 0);
            Assert.Equal(["22", "22111", ".1..1", ".1111"], map.Tiles);
            Assert.Equal(new Vector3(-1, 1, 0), map.Entity!.Transform.Position);
            document.History.Undo();
            Assert.Equal(["1111", "1..1", "1111"], map.Tiles);
            Assert.Equal(Vector3.Zero, map.Entity.Transform.Position);

            model.PaintRectangle(0, 1, 3, 2, erase: true);
            Assert.Equal(["1111"], map.Tiles);
        });
    }

    [Fact]
    public void Fill_FillsTheArea_OrClearsIt_AsOneUndoStep()
    {
        EditorHost.Run(services =>
        {
            var (document, _, model, map) = Open(services);
            model.BrushTile = 3;

            Assert.Equal(2, model.FillTiles(1, 1));
            Assert.Equal(["1111", "1331", "1111"], map.Tiles);
            Assert.Equal(0, model.FillTiles(2, 1)); // already that tile
            Assert.Equal(0, model.FillTiles(9, 9)); // outside the grid

            Assert.Equal(10, model.FillTiles(0, 0, erase: true));
            Assert.Equal(["", ".33"], map.Tiles);
            document.History.Undo();
            Assert.Equal(["1111", "1331", "1111"], map.Tiles);
            document.History.Undo();
            Assert.Equal(["1111", "1..1", "1111"], map.Tiles);
        });
    }

    [Fact]
    public void Brush_PaintsCells_AndAStrokeIsOneUndoStep()
    {
        EditorHost.Run(services =>
        {
            var (document, _, model, map) = Open(services);
            model.BrushTile = 2;

            model.PaintTile(1, 1);
            Assert.Equal(["1111", "12.1", "1111"], map.Tiles);

            // A stroke paints every cell on the way, so a fast drag leaves no gaps, even as it grows the grid left.
            var stroke = model.BeginTileStroke()!;
            stroke.To(0, 3);
            stroke.To(-2, 3);
            stroke.End();
            Assert.Equal(["..1111", "..12.1", "..1111", "222"], map.Tiles);
            Assert.Equal((0, 3), stroke.Last);
            Assert.Equal(new Vector3(-2, 0, 0), map.Entity!.Transform.Position);

            document.History.Undo();
            Assert.Equal(["1111", "12.1", "1111"], map.Tiles);
            Assert.Equal(Vector3.Zero, map.Entity.Transform.Position);
        });
    }

    [Fact]
    public void Tool_And_Tile_AreKept_AndPickTakesTheTileUnderACell()
    {
        EditorHost.Run(services =>
        {
            var (_, _, model, _) = Open(services);
            Assert.Equal(TileTool.Brush, model.TileTool);

            model.TileTool = TileTool.Fill;
            Assert.Equal(TileTool.Fill, model.TileTool);
            Assert.True(model.PickTile(0, 0));
            Assert.Equal(1, model.BrushTile);
            Assert.False(model.PickTile(1, 1));
            Assert.Equal(1, model.BrushTile);
            Assert.Throws<ArgumentOutOfRangeException>(() => model.BrushTile = Tilemap.MaxTiles);
        });
    }

    [Fact]
    public void AddTileset_GoesOnFromTheNextHundred_AsOneUndoStep_AndThePaletteShowsIt()
    {
        EditorHost.Run(services =>
        {
            var (document, _, model, map) = Open(services);

            // The map has no picture yet: the first one added becomes its own.
            Assert.Equal(0, model.AddTileset("Tiles/ground.png", 4, 4));
            Assert.Equal(("Tiles/ground.png", 4, 4), (map.Image, map.Columns, map.Rows));
            Assert.Equal(Griff.Color.White, map.Tint);

            Assert.Equal(100, model.AddTileset("Tiles/props.png", 2, 3));
            Assert.Equal(["Tiles/props.png 2x3"], map.MoreTilesets);
            Assert.Equal("Add Tileset Tiles/props.png", document.History.UndoDescription);
            Assert.Equal(100, model.BrushTile);
            Assert.Equal("props", model.PaletteTileset!.Name);

            // Painting with a tile from it, then undoing the tileset, leaves the ground tiles alone.
            model.BrushTile = 105;
            model.PaintTile(1, 1);
            Assert.Equal(105, map.GetTile(1, 1));
            document.History.Undo();
            document.History.Undo();
            Assert.Empty(map.MoreTilesets);
            Assert.Equal("Tiles/ground.png", map.Image);

            // Adding one it already has just shows it.
            Assert.Equal(0, model.AddTileset("Tiles/ground.png", 8, 8));
            Assert.Equal(4, map.Columns);
            Assert.Throws<ArgumentException>(() => model.AddTileset(" ", 1, 1));
        });
    }

    [Fact]
    public void ShowTileset_PicksTheTileLastPickedFromIt()
    {
        EditorHost.Run(services =>
        {
            var (_, _, model, map) = Open(services);
            map.MoreTilesets = ["Tiles/props.png 4x4", "Tiles/water.png 2x2"];

            model.BrushTile = 3;
            model.ShowTileset(2);
            Assert.Equal(200, model.BrushTile);
            model.ShowTileset(1);
            model.BrushTile = 107;
            model.ShowTileset(0);
            Assert.Equal(3, model.BrushTile);
            model.ShowTileset(1);
            Assert.Equal(107, model.BrushTile);
            Assert.Equal(100, model.PaletteTileset!.First);
            Assert.Throws<ArgumentOutOfRangeException>(() => model.ShowTileset(3));
        });
    }
}
