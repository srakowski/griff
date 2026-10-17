using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Griff.Editor.Core.Settings;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Assets;
using Griff.Projects;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// The Assets panel's Split view: folders as a tree, the picked folder's contents as tiles with thumbnails of pictures,
/// and what the rows did working on tiles too.
/// </summary>
public sealed class AssetsPanelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // A project with Sprites (a hero picture and an Enemies folder in it), Sounds (a sound) and a Coin prefab.
    private (Workspace Workspace, Project Project, AssetsPanel Assets) Open(IServiceProvider services)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        EditorHost.Pump(workspace.CreateProjectAsync(_root, "Game"));
        var project = workspace.Project!;
        Directory.CreateDirectory(Path.Combine(project.AssetsPath, "Sprites", "Enemies"));
        WritePng(Path.Combine(project.AssetsPath, "Sprites", "hero.png"), 8, 8);
        Directory.CreateDirectory(Path.Combine(project.AssetsPath, "Sounds"));
        File.WriteAllBytes(Path.Combine(project.AssetsPath, "Sounds", "jump.wav"), [1, 2, 3]);
        var coin = new Entity("Coin");
        Directory.CreateDirectory(Path.GetDirectoryName(project.DefaultPrefabPath("Coin"))!);
        File.WriteAllText(project.DefaultPrefabPath("Coin"), workspace.Serializer.SavePrefabToString(coin));

        var assets = services.GetRequiredService<AssetsPanel>();
        assets.Rebuild();
        return (workspace, project, assets);
    }

    private static void WritePng(string path, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        Array.Fill(pixels, (byte)200);
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static string[] Names(AssetsPanel assets) => [.. assets.Items.Select(i => i.Name)];

    [Fact]
    public void TheFolderTree_HoldsOnlyFolders_AndAFolderPickedShowsItsContentsAsTiles_FoldersFirst() => EditorHost.Run(services =>
    {
        var (_, _, assets) = Open(services);

        // The tree starts at Assets itself, open, and lists only folders.
        var root = Assert.Single(assets.FolderTree);
        Assert.Equal("", root.AssetPath);
        Assert.True(root.IsExpanded);
        Assert.All(root.Folders, f => Assert.Equal(AssetKind.Folder, f.Kind));
        Assert.Contains(root.Folders, f => f.Name == "Sprites");
        Assert.DoesNotContain(root.Folders, f => f.Kind != AssetKind.Folder);

        // Assets itself shows first.
        Assert.Equal("", assets.SelectedFolder);
        Assert.Equal("Assets", assets.FolderTitle);
        Assert.False(assets.CanGoUp);
        Assert.True(root.IsSelected);

        assets.SelectedFolder = "Sprites";
        Assert.Equal(["Enemies", "hero.png"], Names(assets));
        Assert.Equal("Assets / Sprites", assets.FolderTitle);
        Assert.True(assets.CanGoUp);
        Assert.False(root.IsSelected);
        Assert.True(root.Folders.Single(f => f.Name == "Sprites").IsSelected);

        // Picking a folder deep inside opens the folders it's in.
        assets.SelectedFolder = "sprites/enemies/";
        Assert.Equal("Sprites/Enemies", assets.SelectedFolder);
        Assert.Empty(assets.Items);
        Assert.True(root.Folders.Single(f => f.Name == "Sprites").IsExpanded);

        assets.GoUp();
        Assert.Equal("Sprites", assets.SelectedFolder);
        assets.GoUp();
        Assert.Equal("", assets.SelectedFolder);

        Assert.Throws<ArgumentException>(() => assets.SelectedFolder = "Nowhere");
        Assert.Equal("", assets.SelectedFolder);
    });

    [Fact]
    public void TheTiles_FollowTheDisk_AndAFolderThatGoesShowsTheOneItWasIn() => EditorHost.Run(services =>
    {
        var (_, project, assets) = Open(services);
        assets.SelectedFolder = "Sprites/Enemies";

        WritePng(Path.Combine(project.AssetsPath, "Sprites", "Enemies", "slime.png"), 4, 4);
        assets.Rebuild();
        Assert.Equal("Sprites/Enemies", assets.SelectedFolder);
        Assert.Equal(["slime.png"], Names(assets));

        Directory.Delete(Path.Combine(project.AssetsPath, "Sprites", "Enemies"), recursive: true);
        assets.Rebuild();
        Assert.Equal("Sprites", assets.SelectedFolder);
        Assert.Equal(["hero.png"], Names(assets));
    });

    [Fact]
    public void Pictures_HaveThumbnails_KeptUntilTheirFileChanges() => EditorHost.Run(services =>
    {
        var (_, project, assets) = Open(services);
        assets.SelectedFolder = "Sprites";
        var hero = assets.Items.Single(i => i.Name == "hero.png");
        var file = Path.Combine(project.AssetsPath, "Sprites", "hero.png");

        var thumbnail = Assert.IsAssignableFrom<BitmapSource>(hero.Thumbnail);
        Assert.Equal(8, thumbnail.PixelWidth);
        var reads = assets.Thumbnails.Reads;
        Assert.Same(thumbnail, hero.Thumbnail);
        Assert.Equal(reads, assets.Thumbnails.Reads);

        // Painted over (bigger): the panel hears of it, the tile shows the new picture, shrunk to fit.
        WritePng(file, 300, 150);
        var changed = new List<string?>();
        hero.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        assets.Touched(file);
        assets.Repaint();
        Assert.Contains(nameof(AssetNode.Thumbnail), changed);
        var repainted = Assert.IsAssignableFrom<BitmapSource>(hero.Thumbnail);
        Assert.Equal(AssetThumbnails.MaxSize, repainted.PixelWidth);
        Assert.Equal(AssetThumbnails.MaxSize / 2, repainted.PixelHeight);
        Assert.True(assets.Thumbnails.Reads > reads);

        // Anything else shows its icon; a sound, a prefab and a folder have one each.
        assets.SelectedFolder = "Sounds";
        var jump = assets.Items.Single();
        Assert.Null(jump.Thumbnail);
        Assert.Equal(AssetKind.Audio, jump.Kind);
        Assert.Equal(AssetKind.Font, AssetNode.KindOf("Fonts/Pixel.ttf"));
    });

    [Fact]
    public void ATilesFolder_OpensIntoIt_AndAScene_OpensInATab() => EditorHost.Run(services =>
    {
        var (workspace, project, assets) = Open(services);

        EditorHost.Pump(assets.OpenAsync("Sprites"));
        Assert.Equal("Sprites", assets.SelectedFolder);

        File.WriteAllText(project.CreateScene("Level 2"), workspace.Serializer.SaveToString(new Scene()));
        assets.Rebuild();
        var scene = PrefabFiles.AssetPathOf(project, project.FindScene("Level 2")!);
        EditorHost.Pump(assets.OpenAsync(scene));
        Assert.Equal("Level 2", workspace.Scene!.Name);

        Assert.Throws<ArgumentException>(() => EditorHost.Pump(assets.OpenAsync("Nowhere/else.png")));
    });

    [Fact]
    public void RenamingAndMovingAPrefab_ShowInTheTiles() => EditorHost.Run(services =>
    {
        var (_, _, assets) = Open(services);
        assets.SelectedFolder = "Prefabs";
        Assert.Equal(["Coin.prefab"], Names(assets));

        using (AgentDialogs.Answering(JsonDocument.Parse("""{"Prefab name": "Gold Coin"}""").RootElement))
            assets.RenamePrefab("Prefabs/Coin.prefab");
        Assert.Equal("Prefabs", assets.SelectedFolder);
        Assert.Equal(["Gold Coin.prefab"], Names(assets));

        // Dropped on the Sprites folder's tile (or row): it leaves these tiles for that folder's.
        assets.MovePrefab("Prefabs/Gold Coin.prefab", "Sprites");
        Assert.Empty(assets.Items);
        assets.SelectedFolder = "Sprites";
        Assert.Contains("Gold Coin.prefab", Names(assets));

        // A new folder made from a tile's menu goes in the folder showing, which is opened in the tree.
        using (AgentDialogs.Answering(JsonDocument.Parse("""{"Folder name": "Pickups"}""").RootElement))
            Assert.Equal("Sprites/Pickups", assets.NewFolder(assets.SelectedFolder));
        Assert.Equal(["Enemies", "Pickups", "Gold Coin.prefab", "hero.png"], Names(assets));
    });

    [Fact]
    public void TileSizeAndView_AreKeptInTheSettings() => EditorHost.Run(services =>
    {
        var (_, _, assets) = Open(services);
        var settings = services.GetRequiredService<ISettingsStore>().Settings;

        Assert.Equal(AssetsPanel.DefaultTileSize, assets.TileSize);
        assets.TileSize = 100.4;
        Assert.Equal(100, assets.TileSize);
        Assert.Equal(100, settings.AssetTileSize);
        assets.TileSize = 1000;
        Assert.Equal(AssetsPanel.MaxTileSize, assets.TileSize);

        Assert.Equal(AssetsViewMode.Split, assets.ViewMode);
        Assert.True(assets.ShowsTiles);
        assets.ShowsTree = true;
        Assert.Equal(AssetsViewMode.Tree, settings.AssetsView);
        Assert.False(assets.ShowsTiles);
        // Clicking the button that's down leaves it as it is.
        assets.ShowsTree = false;
        Assert.Equal(AssetsViewMode.Tree, assets.ViewMode);
        assets.ShowsTiles = true;
        Assert.Equal(AssetsViewMode.Split, assets.ViewMode);

        // The Tree view's tree is everything at the top of Assets, files too.
        Assert.Contains(assets.Roots, n => n.Kind != AssetKind.Folder);
    });
}
