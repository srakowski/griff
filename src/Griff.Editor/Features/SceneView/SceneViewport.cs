using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Editor.Core.Workspace;
using Griff.Scenes;
using Point = System.Windows.Point;

namespace Griff.Editor.Features.SceneView;

/// <summary>
/// Where the Scene view looks: the world point at its middle (<see cref="Pan"/>) and how many pixels across a world unit
/// is (<see cref="Zoom"/>; 64 is 100%). Y is up in the world and down on screen. Each tab (the scene's, a prefab's)
/// keeps where it was left, and a scene first opens showing its camera's whole frame.
/// </summary>
public sealed class SceneViewport(IWorkspace workspace, ISelection selection) : ObservableObject
{
    public const double MinZoom = 4;
    public const double MaxZoom = 2048;
    public const double DefaultZoom = 64;

    // Where each tab's view was left (its pan and zoom), to come back to.
    private readonly ConditionalWeakTable<SceneDocument, StrongBox<(Vector2 Pan, double Zoom)>> _places = new();
    private Vector2 _pan;
    private double _zoom = DefaultZoom;

    /// <summary>The world point at the middle of the Scene view.</summary>
    public Vector2 Pan
    {
        get => _pan;
        set => SetProperty(ref _pan, value);
    }

    /// <summary>How many pixels across one world unit is drawn (64 is 100%), from 4 to 2048.</summary>
    public double Zoom
    {
        get => _zoom;
        set => SetProperty(ref _zoom, Math.Clamp(value, MinZoom, MaxZoom));
    }

    /// <summary>The Scene view's size on screen, in pixels.</summary>
    public Size Size { get; internal set; }

    /// <summary>True until the view has been fitted to what the open tab shows (its camera's frame, or a prefab's contents).</summary>
    public bool NeedsFit { get; set; } = true;

    /// <summary>Centers the view on the selection (the middle of several), or the origin.</summary>
    public void FrameSelection()
    {
        var selected = selection.Entities.Where(e => e.Scene == workspace.Scene?.Scene).ToList();
        Pan = selected.Count == 0 ? Vector2.Zero : selected.Aggregate(Vector2.Zero, (sum, e) => sum + Flat(e.Transform.WorldPosition)) / selected.Count;
    }

    /// <summary>Zooms by <paramref name="factor"/>, keeping the world point under <paramref name="anchor"/> (on screen) where it is.</summary>
    public void ZoomAt(Point anchor, double factor)
    {
        var before = ToWorld(anchor);
        Zoom *= factor;
        Pan += before - ToWorld(anchor);
    }

    public Point ToScreen(Vector2 world) => new(
        Size.Width / 2 + (world.X - _pan.X) * _zoom,
        Size.Height / 2 - (world.Y - _pan.Y) * _zoom);

    public Vector2 ToWorld(Point screen) => new(
        (float)((screen.X - Size.Width / 2) / _zoom + _pan.X),
        (float)(-(screen.Y - Size.Height / 2) / _zoom + _pan.Y));

    /// <summary>World-to-screen as a WPF matrix, for drawing in an entity's local space.</summary>
    public Matrix ScreenMatrix(Matrix4x4 world)
    {
        var toScreen = new Matrix(_zoom, 0, 0, -_zoom, Size.Width / 2 - _pan.X * _zoom, Size.Height / 2 + _pan.Y * _zoom);
        var local = new Matrix(world.M11, world.M12, world.M21, world.M22, world.M41, world.M42);
        return local * toScreen;
    }

    /// <summary>
    /// Moves from one tab's document to another's. Switching to or from the play-mode copy keeps the camera where the
    /// user put it (true is returned: it's the same place), and going back to a tab (the scene's or a prefab's) looks
    /// where it was left; a tab never seen before is fitted on the next draw.
    /// </summary>
    internal bool Follow(SceneDocument? leaving, SceneDocument? entering)
    {
        var samePlace = leaving is not null && entering is not null && (leaving.IsRuntime || entering.IsRuntime);
        if (leaving is { IsRuntime: false } && !NeedsFit)
            _places.AddOrUpdate(leaving, new StrongBox<(Vector2, double)>((_pan, _zoom)));
        if (!samePlace)
        {
            if (entering is not null && _places.TryGetValue(entering, out var place))
                (Pan, Zoom) = place.Value;
            else
                NeedsFit = true;
        }

        return samePlace;
    }

    /// <summary>
    /// Shows the whole frame of the first camera, so a scene opens looking at what the player sees. A prefab has none,
    /// so it opens with all of it in view.
    /// </summary>
    internal void Fit(SceneDocument document)
    {
        NeedsFit = false;
        (_pan, _zoom) = (Vector2.Zero, DefaultZoom);

        if (document.IsPrefab)
        {
            FitToContents(document.Scene);
        }
        else if ((document.Scene.Entities.FirstOrDefault(e => e.GetComponent<Camera>() is not null && e.GetComponent<Viewport>() is null) ??
                  document.Scene.Entities.FirstOrDefault(e => e.GetComponent<Camera>() is not null)) is { } camera)
        {
            const double margin = 1.15;
            var height = camera.GetComponent<Camera>()!.Size * 2 * margin;
            var width = height * SceneViewModel.CameraAspect;
            _zoom = Math.Clamp(Math.Min(Size.Width / width, Size.Height / height), MinZoom, MaxZoom);
            _pan = Flat(camera.Transform.WorldPosition);
        }

        OnPropertyChanged(nameof(Pan));
        OnPropertyChanged(nameof(Zoom));
    }

    // Everything in the scene in view, with room around it (at least a few units across, so a small prefab isn't huge).
    private void FitToContents(Scene scene)
    {
        var points = scene.Entities
            .SelectMany(e => SceneViewModel.Outline(e) ?? [Flat(e.Transform.WorldPosition)])
            .ToList();
        if (points.Count == 0)
            return;

        var min = points.Aggregate(Vector2.Min);
        var max = points.Aggregate(Vector2.Max);
        const double margin = 1.5;
        var width = Math.Max((max.X - min.X) * margin, 4 * SceneViewModel.CameraAspect);
        var height = Math.Max((max.Y - min.Y) * margin, 4);
        _zoom = Math.Clamp(Math.Min(Size.Width / width, Size.Height / height), MinZoom, MaxZoom);
        _pan = (min + max) / 2;
    }

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Y);
}
