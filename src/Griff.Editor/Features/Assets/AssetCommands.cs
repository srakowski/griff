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
public sealed class AddAssetToSceneCommand(ISceneDocuments sceneDocuments, AssetPlacement placement, IDialogService dialogs) : EditorCommand
{
    public override string Id => "Asset.AddToScene";
    public override string Text => "Add to Scene";
    public override string? Icon => Icons.Add;
    public override IReadOnlyList<string>? Choices => placement.Placeable();

    protected override bool CanExecuteCore() => sceneDocuments.Scene is not null;

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
public sealed class OpenAssetExternallyCommand(IProjectSession projectSession, IExternalPrograms programs, IDialogService dialogs) : EditorCommand
{
    public override string Id => "Asset.OpenExternally";
    public override string Text => "Open in Another Program";
    public override string? Icon => Icons.Open;

    protected override bool CanExecuteCore() => projectSession.Project is not null;

    protected override Task ExecuteCoreAsync()
    {
        var asset = dialogs.Prompt("Open in Another Program", "File (in Assets)", "",
            text => AssetFiles.Find(projectSession, text) is { } file && File.Exists(file) ? null : "There's no such file in Assets.");
        return asset is null ? Task.CompletedTask : ChooseCoreAsync(asset);
    }

    protected override Task ChooseCoreAsync(string choice)
    {
        if (AssetFiles.Find(projectSession, choice) is not { } file || !File.Exists(file))
            throw new ArgumentException($"There's no file \"{choice}\" in Assets.", nameof(choice));
        programs.Open(file);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Shows a file or folder in Assets in File Explorer, given by its path in Assets ("Sprites/hero.png", "Sprites"); with
/// none, the Assets folder itself.
/// </summary>
public sealed class ShowAssetInExplorerCommand(IProjectSession projectSession, IExternalPrograms programs) : EditorCommand
{
    public override string Id => "Asset.ShowInExplorer";
    public override string Text => "Show in Explorer";
    public override string? Icon => Icons.Folder;

    protected override bool CanExecuteCore() => projectSession.Project is not null;

    protected override Task ExecuteCoreAsync() => ChooseCoreAsync("");

    protected override Task ChooseCoreAsync(string choice)
    {
        if (AssetFiles.Find(projectSession, choice) is not { } path || !(File.Exists(path) || Directory.Exists(path)))
            throw new ArgumentException($"There's no file or folder \"{choice}\" in Assets.", nameof(choice));
        programs.ShowInExplorer(path);
        return Task.CompletedTask;
    }
}

internal static class AssetFiles
{
    /// <summary>The full path of a path in Assets ("" for Assets itself), or null if it would lead outside Assets.</summary>
    public static string? Find(IProjectSession projectSession, string assetPath)
    {
        if (projectSession.Project is not { } project)
            return null;
        var root = Path.GetFullPath(project.AssetsPath);
        var full = Path.GetFullPath(Path.Combine(root, assetPath.Trim().Replace('/', Path.DirectorySeparatorChar)));
        return full == root || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}

/// <summary>
/// Opens every folder in the Assets panel, in its folder tree and its Tree view alike, so everything in Assets shows: the
/// Expand All button on its toolbar. Agents can list Assets with assets_list whether it's open or not.
/// </summary>
public sealed class ExpandAllAssetsCommand(AssetsPanel assets) : FoldAllCommand("Assets", expand: true)
{
    protected override bool CanExecuteCore() => assets.HasProject;

    protected override void Fold() => assets.ExpandAll();
}

/// <summary>
/// Folds every folder in the Assets panel shut but Assets itself, in its folder tree and its Tree view alike, leaving
/// what's at the top of Assets showing: the Collapse All button on its toolbar.
/// </summary>
public sealed class CollapseAllAssetsCommand(AssetsPanel assets) : FoldAllCommand("Assets", expand: false)
{
    protected override bool CanExecuteCore() => assets.HasProject;

    protected override void Fold() => assets.CollapseAll();
}
