using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using Griff.Agent;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.SceneView;
using Griff.Editor.Features.Scripts;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// The Scene view's parts as services: where it looks (SceneViewport) and its tile palette (TilePalette), reached by
/// agents like any view model; and the editor's composition rules (modules register their own types, and services come
/// from dependency injection rather than static locators).
/// </summary>
public class SceneViewStructureTests
{
    // Level: a tilemap with a 4x3 tileset (12 tiles) and a second tileset of 2x1, and a Coin at 3, 4.
    private static (SceneDocument Document, ISelection Selection, Tilemap Map) Open(IServiceProvider services)
    {
        var scene = new Scene();
        var level = new Entity("Level");
        var map = level.AddComponent<Tilemap>();
        (map.Image, map.Columns, map.Rows) = ("Tiles/ground.png", 4, 3);
        map.MoreTilesets = ["Tiles/props.png 2x1"];
        map.Tiles = ["11"];
        scene.Add(level);
        var coin = new Entity("Coin");
        coin.Transform.Position = new Vector3(3, 4, 0);
        scene.Add(coin);

        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<Workspace>().EnterPlayMode(document);
        return (document, services.GetRequiredService<ISelection>(), map);
    }

    private static object Root(IServiceProvider services, string name) =>
        services.GetServices<IAgentToolSource>().OfType<SurfaceTools>().Single().All().Single(r => r.Name == name).Target;

    [Fact]
    public void ThePalette_ShowsTheSelectedMapsTilesetsTilesToolsAndLayers() => EditorHost.Run(services =>
    {
        var (_, selection, map) = Open(services);
        var palette = services.GetRequiredService<TilePalette>();
        Assert.False(palette.IsShowing);
        Assert.Empty(palette.Tiles);

        selection.Select(map.Entity);
        Assert.True(palette.IsShowing);
        Assert.Equal(["ground", "props"], palette.Tilesets);
        Assert.Equal(0, palette.Shown);
        Assert.Equal(Enumerable.Range(0, 12), palette.Tiles);
        Assert.Equal(2, palette.Rows);
        Assert.Equal([TileTool.Select, TileTool.Brush, TileTool.Rectangle, TileTool.Fill], palette.Tools);
        Assert.Equal(["Level"], palette.Layers);
        Assert.Equal(0, palette.Layer);
        Assert.StartsWith("Brush:", palette.Hint);

        // Its tabs, tools and tiles are the model's.
        palette.ShowTileset(1);
        Assert.Equal((1, 100), (palette.Shown, palette.BrushTile));
        Assert.Equal([100, 101], palette.Tiles);
        palette.Tool = TileTool.Fill;
        Assert.Equal(TileTool.Fill, services.GetRequiredService<SceneViewModel>().TileTool);
        Assert.StartsWith("Fill:", palette.Hint);
        palette.Pick(101);
        Assert.Equal(101, services.GetRequiredService<SceneViewModel>().BrushTile);
    });

    [Fact]
    public void ThePalette_Scrolls_AndSteppingWrapsRoundTheShownTileset() => EditorHost.Run(services =>
    {
        var (_, selection, map) = Open(services);
        (map.Columns, map.Rows) = (8, 8);
        selection.Select(map.Entity);
        var palette = services.GetRequiredService<TilePalette>();
        palette.AvailableRows = 3;
        Assert.Equal((8, 3, 0), (palette.Rows, palette.VisibleRows, palette.Scroll));
        Assert.Equal(Enumerable.Range(0, 24), palette.VisibleTiles);

        palette.Scroll = 99;
        Assert.Equal(5, palette.Scroll);
        palette.Scroll = -1;
        Assert.Equal(0, palette.Scroll);

        // ] and [ go round the shown tileset's tiles, scrolling to keep the brush's in sight.
        palette.Step(-1);
        Assert.Equal((63, 5), (palette.BrushTile, palette.Scroll));
        palette.Step(1);
        Assert.Equal((0, 0), (palette.BrushTile, palette.Scroll));

        // Too short for two rows, it still shows two.
        palette.AvailableRows = 0;
        Assert.Equal(2, palette.VisibleRows);
    });

    [Fact]
    public void Agents_InspectAndUseThePalette() => EditorHost.Run(services =>
    {
        var (_, selection, map) = Open(services);
        selection.Select(map.Entity);
        var palette = Root(services, nameof(TilePalette));

        var described = ObjectSurface.Describe(palette, null).ToJsonString();
        Assert.Contains("Tilesets", described);
        Assert.Contains("props", described);
        using (var args = JsonDocument.Parse("[1]"))
            ObjectSurface.InvokeAsync(palette, "ShowTileset", args.RootElement).GetAwaiter().GetResult();
        Assert.Equal(100, services.GetRequiredService<SceneViewModel>().BrushTile);
        using (var value = JsonDocument.Parse("\"Rectangle\""))
            ObjectSurface.Set(palette, "Tool", value.RootElement);
        Assert.Equal(TileTool.Rectangle, services.GetRequiredService<SceneViewModel>().TileTool);
    });

    [Fact]
    public void TheViewport_FramesTheSelection_AndAgentsCanMoveIt() => EditorHost.Run(services =>
    {
        var (document, selection, _) = Open(services);
        var viewport = services.GetRequiredService<SceneViewport>();
        viewport.Size = new Size(800, 450);
        selection.Select(document.Scene.Roots.Single(e => e.Name == "Coin"));

        services.GetRequiredService<CommandRegistry>().Get("View.FrameSelection").ExecuteAsync().GetAwaiter().GetResult();
        Assert.Equal(new Vector2(3, 4), viewport.Pan);
        Assert.Equal(new Point(400, 225), viewport.ToScreen(new Vector2(3, 4)));
        Assert.Equal(new Vector2(4, 4), viewport.ToWorld(new Point(400 + viewport.Zoom, 225)));

        var root = Root(services, nameof(SceneViewport));
        using (var value = JsonDocument.Parse("128"))
            ObjectSurface.Set(root, "Zoom", value.RootElement);
        Assert.Equal(128, viewport.Zoom);
        using (var value = JsonDocument.Parse("1e9"))
            ObjectSurface.Set(root, "Zoom", value.RootElement);
        Assert.Equal(SceneViewport.MaxZoom, viewport.Zoom);
    });

    [Fact]
    public void TheViewport_RemembersWhereEachTabWasLeft() => EditorHost.Run(services =>
    {
        var (document, _, _) = Open(services);
        var viewport = services.GetRequiredService<SceneViewport>();
        viewport.Size = new Size(800, 450);
        viewport.Fit(document);
        (viewport.Pan, viewport.Zoom) = (new Vector2(5, 6), 100);

        var other = new SceneDocument(new Scene(), "Other.scene");
        Assert.False(viewport.Follow(document, other));
        Assert.True(viewport.NeedsFit);
        viewport.Fit(other);

        Assert.False(viewport.Follow(other, document));
        Assert.Equal((new Vector2(5, 6), 100.0), (viewport.Pan, viewport.Zoom));
    });

    [Fact]
    public void ScriptTabs_ShareWhetherTheComponentShows() => EditorHost.Run(services =>
    {
        var editors = services.GetRequiredService<ScriptEditors>();
        Assert.True(editors.ShowComponent);
        var root = Root(services, nameof(ScriptEditors));
        using (var value = JsonDocument.Parse("false"))
            ObjectSurface.Set(root, "ShowComponent", value.RootElement);
        Assert.False(editors.ShowComponent);
        editors.ComponentPaneWidth = 100;
        Assert.Equal(220, editors.ComponentPaneWidth);
    });

    [Fact]
    public void TheShell_RegistersNoFeatureTypes_ModulesDo()
    {
        var services = new ServiceCollection();
        App.ConfigureCoreServices(services);
        var features = services
            .SelectMany(d => new[] { d.ServiceType, d.ImplementationType })
            .Where(t => t?.Namespace?.StartsWith("Griff.Editor.Features", StringComparison.Ordinal) == true)
            .Select(t => t!.FullName)
            .Distinct()
            .ToList();
        Assert.True(features.Count == 0, "Register these in their feature's module, not App.ConfigureCoreServices:\n" + string.Join("\n", features));
    }

    [Fact]
    public void EditorServices_HaveNoStaticLocators()
    {
        // A static property of a type's own kind that it sets (as a constructor setting Current = this) is a service
        // reached around dependency injection. Fixed, stateless ones (a converter's Instance) are fine, and so are the
        // Debug-only diagnostics, which drive the editor from outside.
        var locators = typeof(App).Assembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith("Griff.Editor.Diagnostics", StringComparison.Ordinal) != true)
            .SelectMany(t => t.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(p => p.PropertyType == t && p.SetMethod is not null)
                .Select(p => $"{t.FullName}.{p.Name}"))
            .ToList();
        Assert.True(locators.Count == 0, "Take these from the services instead:\n" + string.Join("\n", locators));
    }
}
