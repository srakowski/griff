using Griff.Art;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Themes;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.Art;

// Art menu: the Sprite Painter, for sprites and tilesets, and the Clip Editor, for the clips of their animations.
public sealed class ArtModule : IEditorModule
{
    public const string Menu = "Art";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<EditorArt>();
        services.AddSingleton<ClipEditor>();
        services.AddMenu(Menu, "A_rt", order: 6);
        services
            .AddCommand<NewSpriteCommand>()
            .AddCommand<NewTilesetCommand>()
            .AddCommand<PaintSelectedCommand>()
            .AddCommand<AddTilesetCommand>()
            .AddCommand<AddTilemapLayerCommand>()
            .AddCommand<OpenClipEditorCommand>()
            .AddCommand<AddFreeFontCommand>();
    }
}

/// <summary>Opens the Sprite Painter on a new sprite.</summary>
public sealed class NewSpriteCommand(EditorArt art, IWorkspace workspace) : EditorCommand
{
    public override string Id => "Art.NewSprite";
    public override string Text => "New _Sprite...";
    public override string? Icon => Icons.Image;
    public override MenuPlacement? Placement { get; } = new(ArtModule.Menu, Group: 0, Order: 0);

    protected override bool CanExecuteCore() => workspace.Project is not null;

    protected override Task ExecuteCoreAsync()
    {
        art.OpenPainter(layout: SheetLayout.Sprite());
        return Task.CompletedTask;
    }
}

/// <summary>Opens the Sprite Painter on a new tileset.</summary>
public sealed class NewTilesetCommand(EditorArt art, IWorkspace workspace) : EditorCommand
{
    public override string Id => "Art.NewTileset";
    public override string Text => "New _Tileset...";
    public override string? Icon => Icons.Palette;
    public override MenuPlacement? Placement { get; } = new(ArtModule.Menu, Group: 0, Order: 1);

    protected override bool CanExecuteCore() => workspace.Project is not null;

    protected override Task ExecuteCoreAsync()
    {
        art.OpenPainter(layout: SheetLayout.Tileset());
        return Task.CompletedTask;
    }
}

/// <summary>
/// Paints the selected entity's sprite or tilemap: opens its picture in the Sprite Painter (or a new one shaped for
/// it), and each save puts the picture on it.
/// </summary>
public sealed class PaintSelectedCommand(EditorArt art, ISelection selection) : EditorCommand
{
    public override string Id => "Art.PaintSelected";
    public override string Text => "_Paint Selected";
    public override string? Icon => Icons.Palette;
    public override MenuPlacement? Placement { get; } = new(ArtModule.Menu, Group: 1, Order: 0);

    protected override bool CanExecuteCore() => Painted() is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (Painted() is { } component)
            art.PaintFor(component);
        return Task.CompletedTask;
    }

    // A tilemap layer paints its map's tileset: a picture of its own would make it a map of its own.
    private Component? Painted() =>
        (Component?)selection.Entity?.GetComponent<Tilemap>()?.BaseMap ?? selection.Entity?.GetComponent<Sprite>();
}

/// <summary>
/// Adds a tileset to the selected tilemap, so it paints with more tiles than one picture holds: the submenu lists the
/// project's images. A picture painted in Griff keeps its own tiles across and down; any other is cut into tiles the
/// size of the map's first tileset's (16 pixels without one). The new tileset's tiles are numbered from the next
/// hundred, and the Scene view's palette switches to it. A map with no image yet takes it as its first.
/// </summary>
public sealed class AddTilesetCommand(EditorArt art, SceneView.SceneViewModel scene, IDialogService dialogs) : EditorCommand
{
    public override string Id => "Art.AddTileset";
    public override string Text => "Add _Tileset to Tilemap";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(ArtModule.Menu, Group: 1, Order: 1);
    public override IReadOnlyList<string>? Choices => art.Images();

    protected override bool CanExecuteCore() => scene.PaintTarget is not null;

    protected override Task ExecuteCoreAsync()
    {
        var images = art.Images();
        var image = dialogs.Prompt("Add Tileset", "Image (in Assets)", images.FirstOrDefault(i => i.StartsWith("Tiles/", StringComparison.OrdinalIgnoreCase)) ?? (images.Count > 0 ? images[0] : ""),
            text => art.Resolve(text) is null ? "There's no such image in Assets." : null);
        return image is null ? Task.CompletedTask : ChooseCoreAsync(image);
    }

    protected override Task ChooseCoreAsync(string choice)
    {
        if (scene.PaintTarget is not { } tilemap)
            throw new InvalidOperationException("Select a tilemap (and nothing else) to add a tileset to it.");
        if (art.Resolve(choice) is null)
            throw new ArgumentException($"There's no image \"{choice}\" in Assets.", nameof(choice));
        var (columns, rows) = art.TileGrid(choice, tilemap.BaseMap);
        scene.AddTileset(choice, columns, rows);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Adds a layer to the selected tilemap: another grid on top of it, painted on its own, with its own collision (Solid,
/// Layer, Ignore Layers) and draw order, using the map's tilesets. It's an entity inside the map's, named after it
/// ("Level Layer 2"), and is selected so painting goes there; the Scene view's palette lists the layers to switch
/// between. It draws over the others and doesn't collide until Solid is turned on.
/// </summary>
public sealed class AddTilemapLayerCommand(SceneView.SceneViewModel scene) : EditorCommand
{
    public override string Id => "Art.AddTilemapLayer";
    public override string Text => "Add _Layer to Tilemap";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(ArtModule.Menu, Group: 1, Order: 2);

    protected override bool CanExecuteCore() => scene.PaintTarget is not null;

    protected override Task ExecuteCoreAsync()
    {
        scene.AddLayer();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Opens the Clip Editor: the selected entity's animation clips, with the one picked playing in a preview. Clips are
/// added, renamed, removed and picked to start with there, and given their frames (picked off the sprite sheet), speed
/// and looping. It follows the selection.
/// </summary>
public sealed class OpenClipEditorCommand(ClipEditor clips, IWorkspace workspace) : EditorCommand
{
    public override string Id => "Art.Clips";
    public override string Text => "Animation _Clips...";
    public override string? Icon => Icons.Play;
    public override MenuPlacement? Placement { get; } = new(ArtModule.Menu, Group: 1, Order: 3);

    protected override bool CanExecuteCore() => workspace.Project is not null;

    protected override Task ExecuteCoreAsync()
    {
        clips.Open();
        return Task.CompletedTask;
    }
}
