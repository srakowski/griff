using System.IO;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Themes;

namespace Griff.Editor.Features.Assets;

/// <summary>
/// Adds a prefab or an image from Assets to the open scene (or inside the prefab whose tab is showing), as the Assets
/// panel's right-click menu does: a prefab as a linked copy where it was saved from, an image as a sprite sized to its
/// shape at 0, 0. The new entity is selected. Pick the asset by its path in Assets, e.g. "Prefabs/Coin.prefab".
/// </summary>
public sealed class AddAssetToSceneCommand(IWorkspace workspace, AssetPlacement placement, IDialogService dialogs) : EditorCommand
{
    public override string Id => "Asset.AddToScene";
    public override string Text => "Add to Scene";
    public override string? Icon => Icons.Add;
    public override IReadOnlyList<string>? Choices => placement.Placeable();

    protected override bool CanExecuteCore() => workspace.Scene is not null;

    protected override Task ExecuteCoreAsync()
    {
        var choices = placement.Placeable();
        var asset = dialogs.Prompt("Add to Scene", "Prefab or image (in Assets)", choices.FirstOrDefault(c => c.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) ?? "",
            text => text.Trim().Length == 0 ? "Name a prefab or an image." : null);
        return asset is null ? Task.CompletedTask : ChooseCoreAsync(asset);
    }

    protected override Task ChooseCoreAsync(string choice)
    {
        placement.AddToScene(choice);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Opens a file in Assets with the program Windows has for it (an image editor, a sound editor, a code editor), given by
/// its path in Assets, e.g. "Sounds/jump.wav". Programs and scripts that would run instead of open are refused.
/// </summary>
public sealed class OpenAssetExternallyCommand(IWorkspace workspace, IExternalPrograms programs, IDialogService dialogs) : EditorCommand
{
    public override string Id => "Asset.OpenExternally";
    public override string Text => "Open in Another Program";
    public override string? Icon => Icons.Open;

    protected override bool CanExecuteCore() => workspace.Project is not null;

    protected override Task ExecuteCoreAsync()
    {
        var asset = dialogs.Prompt("Open in Another Program", "File (in Assets)", "",
            text => AssetFiles.Find(workspace, text) is { } file && File.Exists(file) ? null : "There's no such file in Assets.");
        return asset is null ? Task.CompletedTask : ChooseCoreAsync(asset);
    }

    protected override Task ChooseCoreAsync(string choice)
    {
        if (AssetFiles.Find(workspace, choice) is not { } file || !File.Exists(file))
            throw new ArgumentException($"There's no file \"{choice}\" in Assets.", nameof(choice));
        programs.Open(file);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Shows a file or folder in Assets in File Explorer, given by its path in Assets ("Sprites/hero.png", "Sprites"); with
/// none, the Assets folder itself.
/// </summary>
public sealed class ShowAssetInExplorerCommand(IWorkspace workspace, IExternalPrograms programs) : EditorCommand
{
    public override string Id => "Asset.ShowInExplorer";
    public override string Text => "Show in Explorer";
    public override string? Icon => Icons.Folder;

    protected override bool CanExecuteCore() => workspace.Project is not null;

    protected override Task ExecuteCoreAsync() => ChooseCoreAsync("");

    protected override Task ChooseCoreAsync(string choice)
    {
        if (AssetFiles.Find(workspace, choice) is not { } path || !(File.Exists(path) || Directory.Exists(path)))
            throw new ArgumentException($"There's no file or folder \"{choice}\" in Assets.", nameof(choice));
        programs.ShowInExplorer(path);
        return Task.CompletedTask;
    }
}

internal static class AssetFiles
{
    /// <summary>The full path of a path in Assets ("" for Assets itself), or null if it would lead outside Assets.</summary>
    public static string? Find(IWorkspace workspace, string assetPath)
    {
        if (workspace.Project is not { } project)
            return null;
        var root = Path.GetFullPath(project.AssetsPath);
        var full = Path.GetFullPath(Path.Combine(root, assetPath.Trim().Replace('/', Path.DirectorySeparatorChar)));
        return full == root || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}
