using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.SceneView;
using Griff.Projects;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Templates;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Griff.Editor.Tests;

/// <summary>
/// The Scene view stays quick to pan and zoom in a big scene: a 256 by 256 tilemap (65,536 tiles) and 4,000 sprites, with
/// the template's own art, drawn 1280 by 720 as the view pans close up, with tiles 8 pixels across (their walls
/// outlined) and zoomed out to the whole of it. Each frame is
/// timed from the view being told to draw again until its picture is finished (drawn, then put into a bitmap's pixels).
/// It runs on its own, with no other tests alongside, so they don't slow it.
/// </summary>
[Collection(nameof(SceneViewSpeedTests))]
public sealed class SceneViewSpeedTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-scene-speed-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void PanningAndZooming_ABigScene_StaysQuick() => EditorHost.Run(services =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        EditorHost.Pump(workspace.CreateProjectAsync(_root, "Speed", ProjectTemplates.Get("Roguelike")));
        var level = Griff.Runtime.GameLoader.LoadScene(workspace.Project!, ComponentRegistry.CreateDefault(), "Crypt");
        var tiles = level.Entities.Select(e => e.GetComponent<Tilemap>()).First(t => t is { Image.Length: > 0 })!;
        var images = level.Entities.Select(e => e.GetComponent<Sprite>()?.Image).Where(i => !string.IsNullOrEmpty(i)).Distinct().ToList();

        var scene = new Scene();
        var camera = new Entity("Main Camera");
        camera.AddComponent<Camera>();
        scene.Add(camera);
        var ground = new Entity("Ground");
        var map = ground.AddComponent<Tilemap>();
        (map.Image, map.Columns, map.Rows) = (tiles.Image, tiles.Columns, tiles.Rows);
        const int side = 256;
        var random = new Random(1);
        var kinds = tiles.Tiles.SelectMany(row => row).Distinct().ToArray();
        map.Tiles = Enumerable.Range(0, side).Select(_ => new string(Enumerable.Range(0, side).Select(_ => kinds[random.Next(kinds.Length)]).ToArray())).ToList();
        scene.Add(ground);
        for (var i = 0; i < 4000; i++)
        {
            var thing = new Entity("Thing " + i);
            thing.Transform.Position = new Vector3(random.Next(0, side), -random.Next(0, side), 0);
            thing.AddComponent<Sprite>().Image = images[i % images.Count];
            scene.Add(thing);
        }

        workspace.EnterPlayMode(new SceneDocument(scene, "Big.scene"));
        var view = services.GetRequiredService<SceneViewControl>();
        view.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(new Uri("/Griff.Editor;component/Themes/Theme.xaml", UriKind.Relative)));
        var viewport = services.GetRequiredService<SceneViewport>();
        var size = new Size(1280, 720);
        view.Measure(size);
        view.Arrange(new Rect(size));
        view.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32);

        var middle = new Vector2(side / 2f, -side / 2f);
        var (close, closeDrawing) = Frames(middle, 64);
        var (between, betweenDrawing) = Frames(middle, 8);
        var (whole, wholeDrawing) = Frames(middle, 4);
        output.WriteLine($"Close up: {close:0.0} ms a frame ({closeDrawing:0.0} ms of it drawing). Tiles 8 pixels across: {between:0.0} ms ({betweenDrawing:0.0} ms). " +
                         $"Zoomed out to all of it: {whole:0.0} ms ({wholeDrawing:0.0} ms).");
        Assert.True(close < 250, $"Panning close up took {close:0.0} ms a frame.");
        Assert.True(between < 250, $"Panning with tiles 8 pixels across took {between:0.0} ms a frame.");
        Assert.True(whole < 250, $"Panning zoomed out took {whole:0.0} ms a frame.");

        // The average frame over a pan of 12 frames (after 3 to warm up), each a unit further along and zoomed a little,
        // and how much of it the view's own drawing took (the rest is WPF putting what it drew into pixels).
        (double Frame, double Drawing) Frames(Vector2 from, double zoom)
        {
            var (frame, drawing) = (new Stopwatch(), new Stopwatch());
            for (var i = 0; i < 15; i++)
            {
                (viewport.Pan, viewport.Zoom) = (from + new Vector2(i, 0), zoom * (1 + i % 2 * 0.05));
                if (i >= 3)
                {
                    frame.Start();
                    drawing.Start();
                }

                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                    view.DrawInto(dc, overlays: true);
                drawing.Stop();
                bitmap.Clear();
                bitmap.Render(visual);
                frame.Stop();
            }

            return (frame.Elapsed.TotalMilliseconds / 12, drawing.Elapsed.TotalMilliseconds / 12);
        }
    });
}

/// <summary>Runs <see cref="SceneViewSpeedTests"/> with no other test alongside it.</summary>
[CollectionDefinition(nameof(SceneViewSpeedTests), DisableParallelization = true)]
public sealed class SceneViewSpeedCollection;
