using System.Numerics;
using System.Windows.Threading;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Inspector;
using Griff.Editor.Features.SceneView;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>Tilemap layers in the editor: adding them, switching between them, painting each, and the Inspector on one.</summary>
public class SceneTileLayerTests
{
    // Level, a tilemap at 0,0 with one-unit tiles from a 4x2 tileset: a floor.
    private static (SceneDocument Document, ISelection Selection, SceneViewModel Model, Tilemap Map) Open(IServiceProvider services)
    {
        var scene = new Scene();
        var level = new Entity("Level");
        var map = level.AddComponent<Tilemap>();
        (map.Image, map.Columns, map.Rows) = ("Tiles/ground.png", 4, 2);
        map.Tiles = ["", "1111"];
        scene.Add(level);

        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<IWorkspace>().EnterPlayMode(document);
        var selection = services.GetRequiredService<ISelection>();
        selection.Select(level);
        return (document, selection, services.GetRequiredService<SceneViewModel>(), map);
    }

    [Fact]
    public void AddLayer_MakesALayerInsideTheMap_AsOneUndoStep_AndPaintsThere() => EditorHost.Run(services =>
    {
        var (document, selection, model, map) = Open(services);
        Assert.Equal(["Level"], model.PaintLayers);
        Assert.Equal(0, model.PaintLayer);

        Assert.Equal("Level Layer 2", model.AddLayer());
        var layer = Assert.Single(map.Entity!.Children).GetComponent<Tilemap>()!;
        Assert.Same(map, layer.LayerOf);
        Assert.Same(layer, model.PaintTarget);
        Assert.Equal((false, 1), (layer.Solid, layer.Order));
        Assert.Equal(["Level", "Level Layer 2"], model.PaintLayers);
        Assert.Equal(1, model.PaintLayer);

        // Painting goes on the layer, with the map's tiles.
        model.BrushTile = 5;
        model.PaintTile(2, 0);
        Assert.Equal(["..5"], layer.Tiles);
        Assert.Equal(["", "1111"], map.Tiles);

        // Back on the map, painting goes there.
        model.ShowLayer(0);
        Assert.Same(map.Entity, selection.Entity);
        model.PaintTile(0, 0);
        Assert.Equal(["5", "1111"], map.Tiles);
        Assert.Throws<ArgumentOutOfRangeException>(() => model.ShowLayer(2));

        // A third goes on top of both, with the next free name.
        model.ShowLayer(1);
        Assert.Equal("Level Layer 3", model.AddLayer());
        Assert.Equal(2, model.PaintTarget!.Order);
        Assert.Same(map.Entity, model.PaintTarget.Entity!.Parent);

        document.History.Undo();
        document.History.Undo();
        document.History.Undo();
        document.History.Undo();
        Assert.Empty(map.Entity.Children);
    });

    [Fact]
    public void PaintingPastTheMapsTopLeft_KeepsItsLayersInPlace() => EditorHost.Run(services =>
    {
        var (document, _, model, map) = Open(services);
        model.AddLayer();
        var layer = model.PaintTarget!;
        model.BrushTile = 2;
        model.PaintTile(1, 1);

        model.ShowLayer(0);
        model.PaintRectangle(-2, -1, -2, -1);
        Assert.Equal(new Vector3(-2, 1, 0), map.Entity!.Transform.Position);
        Assert.Equal(new Vector3(2, -1, 0), layer.Entity!.Transform.Position);
        Assert.Equal(Vector3.Zero, layer.Entity.Transform.WorldPosition);
        Assert.Equal(2, layer.GetTile(1, 1));

        document.History.Undo();
        Assert.Equal(Vector3.Zero, map.Entity.Transform.Position);
        Assert.Equal(Vector3.Zero, layer.Entity.Transform.Position);
    });

    [Fact]
    public void AddTileset_OnALayer_GoesOnItsMap() => EditorHost.Run(services =>
    {
        var (_, _, model, map) = Open(services);
        model.AddLayer();
        var layer = model.PaintTarget!;

        Assert.Equal(100, model.AddTileset("Tiles/props.png", 2, 2));
        Assert.Equal(["Tiles/props.png 2x2"], map.MoreTilesets);
        Assert.Null(layer.Image);
        Assert.Empty(layer.MoreTilesets);
        Assert.Equal("props", model.PaletteTileset!.Name);
    });

    [Fact]
    public void TheInspector_HidesWhatALayerTakesFromItsMap_AndSaysWhichMap() => EditorHost.Run(services =>
    {
        var (_, selection, model, map) = Open(services);
        var inspector = services.GetRequiredService<InspectorPanel>();
        model.AddLayer();

        var section = inspector.Components.Single(c => c.Title == nameof(Tilemap));
        var shown = section.Properties.Select(p => p.PropertyName).ToList();
        Assert.Contains(nameof(Tilemap.Solid), shown);
        Assert.Contains(nameof(Tilemap.Layer), shown);
        Assert.Contains(nameof(Tilemap.Image), shown);
        Assert.DoesNotContain(nameof(Tilemap.TileSize), shown);
        Assert.DoesNotContain(nameof(Tilemap.MoreTilesets), shown);
        Assert.Equal("A layer of Level: drawn with its tilesets, tile size and tile settings.", section.SetupNote);

        // Given an image of its own, it's a map of its own: everything shows.
        section.Properties.Single(p => p.PropertyName == nameof(Tilemap.Image)).TextValue = "Tiles/props.png";
        section = inspector.Components.Single(c => c.Title == nameof(Tilemap));
        Assert.Contains(nameof(Tilemap.TileSize), section.Properties.Select(p => p.PropertyName));
        Assert.Null(section.SetupNote);

        selection.Select(map.Entity);
        Assert.Null(inspector.Components.Single(c => c.Title == nameof(Tilemap)).SetupNote);
    });

    [Fact]
    public void TheArtMenuAddsALayer() => EditorHost.Run(services =>
    {
        var (_, selection, _, map) = Open(services);
        var command = services.GetRequiredService<CommandRegistry>().Get("Art.AddTilemapLayer");
        Assert.Equal("Add _Layer to Tilemap", command.Text);

        Wait(command.ExecuteAsync());
        Assert.Equal("Level Layer 2", selection.Entity!.Name);
        Assert.Same(map, selection.Entity.GetComponent<Tilemap>()!.LayerOf);

        selection.Select(null);
        Assert.False(command.CanExecute());
    });

    private static void Wait(Task task)
    {
        EditorHost.Pump(task);
    }
}
