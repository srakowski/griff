#if DEBUG
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Griff.Editing;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.DesignSystem;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Diagnostics;

/// <summary>
/// Debug-only visual check: set GRIFF_SNAPSHOT_DIR and the editor builds a sample project in a temp
/// folder, renders its windows to PNGs off-screen (nothing is shown or focused), and exits.
/// Pair with GRIFF_SETTINGS_PATH so the run doesn't touch real settings.
/// </summary>
internal static class SnapshotRunner
{
    public static bool TryRun(IServiceProvider services, Window main)
    {
        var outputDir = Environment.GetEnvironmentVariable("GRIFF_SNAPSHOT_DIR");
        if (outputDir is null)
            return false;

        _ = RunAsync(services, main, outputDir);
        return true;
    }

    private static async Task RunAsync(IServiceProvider services, Window main, string outputDir)
    {
        try
        {
            Show(main);
            Render(main, Path.Combine(outputDir, "start.png"));

            var workspace = services.GetRequiredService<IWorkspace>();
            var projectDir = Path.Combine(Path.GetTempPath(), "griff-snapshot-" + Guid.NewGuid().ToString("N")[..8]);
            await workspace.CreateProjectAsync(projectDir, "Snapshot Game");
            BuildSampleScene(workspace, services.GetRequiredService<ISelection>());
            Render(main, Path.Combine(outputDir, "editor.png"));

            var gallery = services.GetRequiredService<DesignSystemWindow>();
            Show(gallery);
            Render(gallery, Path.Combine(outputDir, "gallery.png"));

            // Save so shutdown doesn't stop at the unsaved-changes prompt, then remove the sample project.
            await workspace.SaveSceneAsync();
            TryDelete(projectDir);
        }
        finally
        {
            Application.Current.Shutdown();
        }
    }

    private static void BuildSampleScene(IWorkspace workspace, ISelection selection)
    {
        var document = workspace.Scene!;
        var scene = document.Scene;

        var player = new Entity("Player");
        player.Transform.Position = new Vector3(-2, -1, 0);
        player.AddComponent<Sprite>().Tint = Color.FromBytes(0xF0, 0x8C, 0x2E);
        document.Execute(new AddEntityEdit(scene, player));

        var sword = new Entity("Sword");
        sword.Transform.Position = new Vector3(0.8f, 0.3f, 0);
        sword.Transform.Rotation = new Vector3(0, 0, -30);
        var swordSprite = sword.AddComponent<Sprite>();
        swordSprite.Size = new Vector2(0.25f, 1.2f);
        swordSprite.Tint = Color.FromBytes(0xA1, 0xA6, 0xB2);
        document.Execute(new AddEntityEdit(scene, sword, player));

        var ground = new Entity("Ground");
        ground.Transform.Position = new Vector3(0, -2.5f, 0);
        var groundSprite = ground.AddComponent<Sprite>();
        groundSprite.Size = new Vector2(14, 1);
        groundSprite.Tint = Color.FromBytes(0x3C, 0x3F, 0x48);
        document.Execute(new AddEntityEdit(scene, ground));

        var spawn = new Entity("Enemy Spawn") { IsEnabled = true };
        spawn.Transform.Position = new Vector3(4, 1, 0);
        document.Execute(new AddEntityEdit(scene, spawn));

        selection.Select(player);
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static void Show(Window window)
    {
        window.ShowActivated = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -30000;
        if (!window.IsVisible)
            window.Show();
    }

    private static void Render(Window window, string path)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var content = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
        var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
#endif
