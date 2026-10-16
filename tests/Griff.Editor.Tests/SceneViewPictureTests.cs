using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.SceneView;
using Griff.Graphics;
using Griff.Platform.Windows;
using Griff.Projects;
using Griff.Rendering;
using Griff.Templates;
using Color = Griff.Color;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// The Scene view's picture of a scene is the renderer's frame of it (<see cref="SceneRenderer.BuildView"/>, drawn by the
/// OpenGL renderer the Game view draws with), pixel for pixel, for every scene of every template; what WPF draws is only
/// what goes over it. That frame being the game's is <c>EditorViewTests</c>' to check.
/// </summary>
public sealed class SceneViewPictureTests : IDisposable
{
    private const int Width = 320, Height = 180;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-scene-picture-" + Guid.NewGuid().ToString("N"));

    public static TheoryData<string> Templates => [.. ProjectTemplates.All.Select(t => t.Name)];

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void EveryScene_IsShownAsTheRenderersFrameOfIt(string template) => EditorHost.Run(services =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        EditorHost.Pump(workspace.CreateProjectAsync(_root, template, ProjectTemplates.Get(template)));
        var project = workspace.Project!;
        var view = services.GetRequiredService<SceneViewControl>();
        view.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(new Uri("/Griff.Editor;component/Themes/Theme.xaml", UriKind.Relative)));
        var viewport = services.GetRequiredService<SceneViewport>();
        view.Measure(new Size(Width, Height));
        view.Arrange(new Rect(0, 0, Width, Height));
        view.UpdateLayout();
        var scale = VisualTreeHelper.GetDpi(view).PixelsPerDip;
        var (width, height) = ((int)Math.Round(Width * scale), (int)Math.Round(Height * scale));
        using var gl = new OffscreenGL(width, height, project.AssetsPath);
        var surface = ((SolidColorBrush)view.FindResource("Brush.Surface.Document")).Color;
        var background = Color.FromBytes(surface.R, surface.G, surface.B);

        var scenes = project.FindScenes().ToList();
        Assert.NotEmpty(scenes);
        foreach (var path in scenes)
        {
            Assert.True(EditorHost.Pump(workspace.OpenSceneAsync(path)), path);
            var scene = workspace.Scene!.Scene;
            viewport.NeedsFit = true;

            // What the view shows, without what it draws over the scene.
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
                view.DrawInto(dc, overlays: false);
            var shown = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            shown.Render(visual);
            var bgra = new byte[width * height * 4];
            shown.CopyPixels(bgra, width * 4, 0);

            // The renderer's frame of the part of the world in view, drawn on its own.
            var frame = SceneRenderer.BuildView(scene, view.Picture.View(), (float)(2 / viewport.Zoom), background);
            gl.Render(frame);
            var rgba = gl.Capture();

            var different = 0;
            for (var i = 0; i < width * height; i++)
            {
                if (Math.Abs(bgra[i * 4] - rgba[i * 4 + 2]) > 1 || Math.Abs(bgra[i * 4 + 1] - rgba[i * 4 + 1]) > 1 || Math.Abs(bgra[i * 4 + 2] - rgba[i * 4]) > 1)
                    different++;
            }

            Assert.True(different == 0, $"{template} {Path.GetFileName(path)}: {different} of {width * height} pixels differ from the renderer's frame.");
            Assert.True(frame.Sprites.Count > 0 || template == "Empty", $"{template} {Path.GetFileName(path)}: nothing drawn.");
        }
    });

    [Fact]
    public void TheSceneView_DrawsNoComponentItself()
    {
        // Its picture comes from the renderer whole: no drawable is asked for its draws, or drawn a second way, here.
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "griff.slnx")))
            folder = folder.Parent;
        var sceneView = Path.Combine(folder!.FullName, "src", "Griff.Editor", "Features", "SceneView");
        foreach (var file in Directory.GetFiles(sceneView, "*.cs"))
        {
            var text = File.ReadAllText(file);
            foreach (var word in (string[])["IDrawable", "DrawsOf(", "TileRect(", ".Draw(drawing)"])
                Assert.False(text.Contains(word, StringComparison.Ordinal), $"{Path.GetFileName(file)} uses {word}.");
        }
    }
}
