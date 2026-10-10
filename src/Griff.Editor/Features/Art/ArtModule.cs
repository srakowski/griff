using Griff.Art;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Themes;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.Art;

// Art menu: the Sprite Painter, for sprites and tilesets.
public sealed class ArtModule : IEditorModule
{
    public const string Menu = "Art";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<EditorArt>();
        services.AddMenu(Menu, "A_rt", order: 6);
        services
            .AddCommand<NewSpriteCommand>()
            .AddCommand<NewTilesetCommand>()
            .AddCommand<PaintSelectedCommand>();
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

    protected override bool CanExecuteCore() => Target() is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (Target() is { } component)
            art.PaintFor(component);
        return Task.CompletedTask;
    }

    private Component? Target() =>
        (Component?)selection.Entity?.GetComponent<Tilemap>() ?? selection.Entity?.GetComponent<Sprite>();
}
