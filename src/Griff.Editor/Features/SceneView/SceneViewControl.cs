using System.Globalization;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Rendering;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Point = System.Windows.Point;
using Vector = System.Windows.Vector;
using Transform = Griff.Scenes.Transform;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.SceneView;

/// <summary>
/// 2D view of the open scene. Y is up, one world unit is <see cref="_zoom"/> pixels.
/// Wheel zooms at the cursor; middle/right/Alt-drag pans; click selects (Ctrl or Shift adds or takes away); dragging
/// from empty space selects everything in the box; dragging a selected entity moves the whole selection (Ctrl snaps);
/// dragging the rotate handle above the primary selection turns the selection around it (Ctrl snaps to 15 degrees);
/// dragging the square handles on its corners and edges scales it (Shift keeps proportions, Ctrl snaps to tenths), or,
/// with Resize or Collider handles picked (R and C; S goes back), resizes its sprite or its colliders (Ctrl snaps to
/// half units); with Distance handles picked (D), the round knobs on its circles change its light radii, ranges and sight
/// (Ctrl snaps to half units); the knobs on its path move a mover's points and a patrol's turns (Ctrl snaps to half units;
/// drag a plus to add a point, double-click a point to take it off); F frames. The selection's actions live in
/// <see cref="SceneViewModel"/>, where agents reach them too.
/// With a tilemap selected, left-drag paints tiles instead, with the tool picked from the palette or with B (brush), R
/// (rectangle) and G (fill); Shift erases, Ctrl picks, [ and ] change tile, Esc stops. Painting lives in
/// <see cref="SceneViewModel"/> too (<see cref="SceneViewModel.PaintTile"/> and the rest).
/// Arrows link the selection to the entities its fields name, and to it from the fields that name it.
/// </summary>
public sealed class SceneViewControl : FrameworkElement
{
    private const double MinZoom = 4;
    private const double MaxZoom = 2048;
    private const float SnapStep = 0.5f;
    private const double MarkerRadius = 7;
    private const double CameraAspect = SceneViewModel.CameraAspect;
    private const double DragThreshold = 3;
    private const double PaletteCell = 28;
    private const int PaletteColumns = 8;
    private const double ToolButtonWidth = 84;
    private const double HandleStem = 24;
    private const double HandleRadius = 5;
    private const double HandleSquare = 3.5;
    private const double MarkerFrame = MarkerRadius + 8;
    private const double EdgeHandleRoom = 28;
    private const double ScaleHandleRoom = 16;
    private const double PathGap = 28;

    // Where the scale handles sit on the primary selection's frame, in its own axes: corners and edge middles.
    private static readonly Vector2[] ScaleSpots =
        [new(-1, -1), new(0, -1), new(1, -1), new(1, 0), new(1, 1), new(0, 1), new(-1, 1), new(-1, 0)];

    private readonly IWorkspace _workspace;
    private readonly ISelection _selection;
    private readonly SceneViewModel _model;
    private readonly IServiceProvider _services;
    private readonly Dictionary<string, BitmapSource?> _images = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Image, int Tile, int Columns, int Rows), BitmapSource?> _tileImages = [];
    private readonly Dictionary<(BitmapSource Image, int Rgb), BitmapSource> _tintedImages = [];
    private SceneDocument? _document;

    // Where each tab's view was left (its pan and zoom), to come back to.
    private readonly ConditionalWeakTable<SceneDocument, StrongBox<(Vector2 Pan, double Zoom)>> _places = new();

    private Vector2 _pan;
    private double _zoom = 64;
    private bool _needsFit = true;

    private DragMode _drag;
    private Point _dragStartScreen;
    private Vector2 _dragStartPan;
    private SceneViewModel.SelectionMove? _move;
    // A click on one of several selected entities picks it alone, unless the mouse moves (then they all move).
    private Entity? _pressed;
    private Point _boxEnd;
    private bool _boxAdds;
    private SceneViewModel.SelectionRotate? _rotate;
    private double _rotateLast;
    private double _rotateTotal;
    private bool _overHandle;
    private SceneViewModel.SelectionScale? _scale;
    private Vector2 _scaleSpot;
    private Vector2 _scalePress;
    private Vector2? _overScale;
    private SceneViewModel.ShapeResize? _resize;
    private Vector2 _resizeSpot;
    private Vector2 _resizePress;
    private (Component Shape, Vector2 Spot)? _overResize;
    private SceneViewModel.DistanceDrag? _distance;
    private float _distanceGrab;
    private string? _overDistance;
    private SceneViewModel.PathPointDrag? _path;
    private string? _pathKey;
    private Vector2 _pathGrab;
    private string? _overPath;

    // Tile painting: a brush stroke under way, or the corner a rectangle is being dragged from.
    private (int X, int Y)? _hoverCell;
    private SceneViewModel.TileStroke? _stroke;
    private (int X, int Y)? _rectangleFrom;
    private bool _rectangleErases;
    private (IReadOnlyList<string> Tiles, (int X, int Y) Cell)? _fillPreviewOf;
    private IReadOnlyList<(int X, int Y)> _fillPreview = [];

    // Commands are reached through the services on demand: some (Frame Selection) are built with this view.
    public SceneViewControl(IWorkspace workspace, ISelection selection, SceneViewModel model, IServiceProvider services)
    {
        _workspace = workspace;
        _selection = selection;
        _model = model;
        _services = services;
        Focusable = true;
        AllowDrop = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);

        workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IWorkspace.Scene))
                Track(workspace.Scene);
        };
        selection.PropertyChanged += (_, _) => InvalidateVisual();
        model.PropertyChanged += (_, _) =>
        {
            (_overScale, _overResize, _overDistance, _overPath) = (null, null, null, null);
            InvalidateVisual();
        };
        workspace.ScriptsReloaded += (_, _) => InvalidateVisual();
        Track(workspace.Scene);
    }

    // A handle on the primary selection's path: on one of its points, or (with no Point) a plus that adds point Index to
    // its Mover.
    internal sealed record PathHandle(string Key, Point Screen, ScenePathPoint? Point, Mover? Mover, int Index);

    private enum DragMode
    {
        None,
        Pan,
        Move,
        Box,
        Paint,
        Rotate,
        Scale,
        Resize,
        Distance,
        Path,
    }

    private PropertyDescriptor PositionProperty =>
        _workspace.Components.Get(typeof(Transform)).Properties.Single(p => p.Name == nameof(Transform.Position));

    /// <summary>The tilemap being painted (see <see cref="SceneViewModel.PaintTarget"/>).</summary>
    private Tilemap? PaintTarget => _model.PaintTarget;

    /// <summary>Centers the view on the selection (the middle of several), or the origin.</summary>
    public void FrameSelection()
    {
        var selected = _selection.Entities.Where(e => e.Scene == _document?.Scene).ToList();
        _pan = selected.Count == 0 ? Vector2.Zero : selected.Aggregate(Vector2.Zero, (sum, e) => sum + ToVector2(e.Transform.WorldPosition)) / selected.Count;
        InvalidateVisual();
    }

    private void Track(SceneDocument? document)
    {
        if (_document is not null)
        {
            _document.Changed -= OnDocumentChanged;
            _document.RuntimeTicked -= OnRuntimeTicked;
        }

        // Switching to or from the play-mode copy keeps the camera where the user put it, and going back to a tab (the
        // scene's or a prefab's) looks where it was left.
        var samePlace = _document is not null && document is not null && (_document.IsRuntime || document.IsRuntime);
        if (_document is { IsRuntime: false } leaving && !_needsFit)
            _places.AddOrUpdate(leaving, new StrongBox<(Vector2, double)>((_pan, _zoom)));
        _document = document;
        if (_document is not null)
        {
            _document.Changed += OnDocumentChanged;
            _document.RuntimeTicked += OnRuntimeTicked;
        }

        if (!samePlace)
        {
            _images.Clear();
            _tileImages.Clear();
            _tintedImages.Clear();
            if (document is not null && _places.TryGetValue(document, out var place))
                (_pan, _zoom) = place.Value;
            else
                _needsFit = true;
        }

        InvalidateVisual();
    }

    /// <summary>
    /// Shows the whole frame of the first camera, so a scene opens looking at what the player sees. A prefab has none,
    /// so it opens with all of it in view.
    /// </summary>
    private void FitToCamera()
    {
        _needsFit = false;
        _pan = Vector2.Zero;
        _zoom = 64;

        if (_document is { IsPrefab: true } prefab)
        {
            FitToContents(prefab.Scene);
            return;
        }

        var camera = _document?.Scene.Entities.FirstOrDefault(e => e.GetComponent<Camera>() is not null);
        if (camera?.GetComponent<Camera>() is not { } component)
            return;

        const double margin = 1.15;
        var height = component.Size * 2 * margin;
        var width = height * CameraAspect;
        _zoom = Math.Clamp(Math.Min(ActualWidth / width, ActualHeight / height), MinZoom, MaxZoom);
        var position = camera.Transform.WorldPosition;
        _pan = new Vector2(position.X, position.Y);
    }

    // Everything in the scene in view, with room around it (at least a few units across, so a small prefab isn't huge).
    private void FitToContents(Scene scene)
    {
        var points = scene.Entities
            .SelectMany(e => SceneViewModel.Outline(e) ?? [ToVector2(e.Transform.WorldPosition)])
            .ToList();
        if (points.Count == 0)
            return;

        var min = points.Aggregate(Vector2.Min);
        var max = points.Aggregate(Vector2.Max);
        const double margin = 1.5;
        var width = Math.Max((max.X - min.X) * margin, 4 * CameraAspect);
        var height = Math.Max((max.Y - min.Y) * margin, 4);
        _zoom = Math.Clamp(Math.Min(ActualWidth / width, ActualHeight / height), MinZoom, MaxZoom);
        _pan = (min + max) / 2;
    }

    private void OnDocumentChanged(object? sender, EditEventArgs e) => InvalidateVisual();

    private void OnRuntimeTicked(object? sender, EventArgs e) => InvalidateVisual();

    // ----- Coordinates -----

    private Point ToScreen(Vector2 world) => new(
        ActualWidth / 2 + (world.X - _pan.X) * _zoom,
        ActualHeight / 2 - (world.Y - _pan.Y) * _zoom);

    private Vector2 ToWorld(Point screen) => new(
        (float)((screen.X - ActualWidth / 2) / _zoom + _pan.X),
        (float)(-(screen.Y - ActualHeight / 2) / _zoom + _pan.Y));

    /// <summary>World-to-screen as a WPF matrix, for drawing in an entity's local space.</summary>
    private Matrix ScreenMatrix(Matrix4x4 world)
    {
        var toScreen = new Matrix(_zoom, 0, 0, -_zoom, ActualWidth / 2 - _pan.X * _zoom, ActualHeight / 2 + _pan.Y * _zoom);
        var local = new Matrix(world.M11, world.M12, world.M21, world.M22, world.M41, world.M42);
        return local * toScreen;
    }

    // ----- Rendering -----

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brush("Brush.Surface.Document"), null, new Rect(RenderSize));
        if (_document is null)
            return;

        if (_needsFit && ActualWidth > 0)
            FitToCamera();

        DrawGrid(dc);

        foreach (var entity in SceneViewModel.DrawOrder(_document.Scene))
        {
            if (entity.GetComponent<Tilemap>() is { } tilemap)
                DrawTilemap(dc, entity, tilemap);
            if (entity.GetComponent<Sprite>() is { } sprite)
                DrawSprite(dc, entity, sprite);
            if (entity.GetComponent<Text>() is { Value.Length: > 0 } text)
                DrawText(dc, entity, text);
            if (entity.GetComponent<Camera>() is { } camera)
                DrawCameraFrame(dc, entity, camera, _selection.IsSelected(entity));
            if (entity.GetComponent<ParticleEmitter>() is { Particles.Count: > 0 } emitter)
                DrawParticles(dc, entity, emitter);

            // Entities with nothing solid to see (including emitters) get a marker so they can be found and clicked.
            if (entity.GetComponent<Sprite>() is null && entity.GetComponent<Tilemap>() is null &&
                entity.GetComponent<Text>() is null && entity.GetComponent<Camera>() is null)
                DrawMarker(dc, entity);
        }

        DrawLights(dc);
        DrawColliders(dc);
        DrawRooms(dc);
        DrawLevelGuides(dc);
        DrawLinks(dc);

        foreach (var selected in _selection.Entities)
        {
            if (selected.Scene == _document.Scene)
                DrawSelection(dc, selected, primary: selected == _selection.Entity);
        }

        if (_drag == DragMode.Box)
            DrawBoxSelection(dc);
        else
            DrawHandles(dc);

        if (PaintTarget is { } painting)
            DrawPaintOverlay(dc, painting);

        DrawZoomLabel(dc);
    }

    private void DrawGrid(DrawingContext dc)
    {
        var minor = new Pen(Brush("Brush.Border.Subtle"), 1);
        var major = new Pen(Brush("Brush.Selection.Inactive"), 1);
        var topLeft = ToWorld(new Point(0, 0));
        var bottomRight = ToWorld(new Point(ActualWidth, ActualHeight));

        // Step through powers of ten so lines stay 8px or more apart at any zoom.
        var step = Math.Pow(10, Math.Ceiling(Math.Log10(8 / _zoom)));
        for (var x = Math.Floor(topLeft.X / step) * step; x <= bottomRight.X; x += step)
        {
            var sx = Math.Round(ToScreen(new Vector2((float)x, 0)).X) + 0.5;
            var isMajor = Math.Abs(Math.IEEERemainder(x, step * 10)) < step / 2;
            dc.DrawLine(isMajor ? major : minor, new Point(sx, 0), new Point(sx, ActualHeight));
        }

        for (var y = Math.Floor(bottomRight.Y / step) * step; y <= topLeft.Y; y += step)
        {
            var sy = Math.Round(ToScreen(new Vector2(0, (float)y)).Y) + 0.5;
            var isMajor = Math.Abs(Math.IEEERemainder(y, step * 10)) < step / 2;
            dc.DrawLine(isMajor ? major : minor, new Point(0, sy), new Point(ActualWidth, sy));
        }

        var origin = ToScreen(Vector2.Zero);
        dc.DrawLine(new Pen(Faded("Brush.Axis.X", 0.6), 1), new Point(0, Math.Round(origin.Y) + 0.5), new Point(ActualWidth, Math.Round(origin.Y) + 0.5));
        dc.DrawLine(new Pen(Faded("Brush.Axis.Y", 0.6), 1), new Point(Math.Round(origin.X) + 0.5, 0), new Point(Math.Round(origin.X) + 0.5, ActualHeight));
    }

    private void DrawSprite(DrawingContext dc, Entity entity, Sprite sprite)
    {
        var size = sprite.Size;
        var rect = new Rect(-size.X / 2, -size.Y / 2, size.X, size.Y);
        var matrix = ScreenMatrix(entity.Transform.WorldMatrix);

        // Images are stored top-down; flip Y back since world space is Y-up.
        var flip = new Matrix(sprite.FlipX ? -1 : 1, 0, 0, sprite.FlipY ? 1 : -1, 0, 0);
        dc.PushTransform(new MatrixTransform(flip * matrix));
        dc.PushOpacity(sprite.Tint.A);

        if (LoadImage(sprite.Image) is { } loaded)
        {
            var image = Tinted(loaded, sprite.Tint);
            if (sprite.Columns > 1 || sprite.Rows > 1)
            {
                var source = sprite.FrameRect;
                var brush = new ImageBrush(image)
                {
                    Viewbox = new Rect(source.X, source.Y, source.Z, source.W),
                    ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                };
                RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
                dc.DrawRectangle(brush, null, rect);
            }
            else
            {
                dc.DrawImage(image, rect);
            }
        }
        else
        {
            var fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, sprite.Tint.RByte, sprite.Tint.GByte, sprite.Tint.BByte));
            dc.DrawRectangle(fill, null, rect);
            if (!string.IsNullOrEmpty(sprite.Image))
            {
                // Image set but not found: cross it out.
                var pen = new Pen(Brush("Brush.Status.Error"), 2 / _zoom);
                dc.DrawLine(pen, rect.TopLeft, rect.BottomRight);
                dc.DrawLine(pen, rect.TopRight, rect.BottomLeft);
            }
        }

        dc.Pop();
        dc.Pop();
    }

    private void DrawTilemap(DrawingContext dc, Entity entity, Tilemap tilemap)
    {
        var size = tilemap.TileSize;
        // A hair of overlap hides the anti-aliased seams WPF would draw between neighbouring tiles.
        var bleed = 0.5 / _zoom;
        var fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, tilemap.Tint.RByte, tilemap.Tint.GByte, tilemap.Tint.BByte));
        dc.PushTransform(new MatrixTransform(ScreenMatrix(entity.Transform.WorldMatrix)));
        // While one layer of a map is painted, its other layers fade back.
        var otherLayer = PaintTarget is { } painting && painting != tilemap && painting.BaseMap == tilemap.BaseMap;
        dc.PushOpacity(tilemap.Tint.A * (otherLayer ? 0.35 : 1));

        foreach (var (x, y, tile) in tilemap.Cells())
        {
            var center = tilemap.CellCenter(x, y);
            var rect = new Rect(center.X - size / 2 - bleed, center.Y - size / 2 - bleed, size + bleed * 2, size + bleed * 2);
            // Autotiles show the piece that joins their neighbors, as in the game.
            if (TileImage(tilemap, tilemap.ShownTile(x, y, tile)) is { } image)
            {
                // Images are stored top-down; flip each tile about its own center.
                dc.PushTransform(new MatrixTransform(1, 0, 0, -1, 0, 2 * center.Y));
                dc.DrawImage(Tinted(image, tilemap.Tint), rect);
                dc.Pop();
            }
            else
            {
                dc.DrawRectangle(fill, null, rect);
            }
        }

        dc.Pop();
        dc.Pop();
    }

    // Particle images are tinted like sprites; without one, particles are tinted squares.
    private void DrawParticles(DrawingContext dc, Entity entity, ParticleEmitter emitter)
    {
        var image = LoadImage(emitter.Image);
        foreach (var draw in SceneRenderer.ParticleDraws(entity, emitter))
        {
            var center = ToScreen(new Vector2(draw.World.M41, draw.World.M42));
            var half = draw.World.M11 * _zoom / 2;
            var rect = new Rect(center.X - half, center.Y - half, half * 2, half * 2);
            if (image is not null)
            {
                dc.PushOpacity(draw.Tint.A);
                dc.DrawImage(Tinted(image, draw.Tint), rect);
                dc.Pop();
            }
            else
            {
                var color = System.Windows.Media.Color.FromArgb(draw.Tint.AByte, draw.Tint.RByte, draw.Tint.GByte, draw.Tint.BByte);
                dc.DrawRectangle(new SolidColorBrush(color), null, rect);
            }
        }
    }

    // Same font and line height as the game's text rasterizer, scaled to the text's world size.
    private void DrawText(DrawingContext dc, Entity entity, Text text)
    {
        var lines = text.Value!.Split('\n').Length;
        var formatted = Griff.Platform.Windows.WpfText.Format(text.Value,
            Griff.Platform.Windows.WpfText.TypefaceFor(text.Font, _workspace.Project?.AssetsPath), text.Align,
            new SolidColorBrush(System.Windows.Media.Color.FromArgb(text.Color.AByte, text.Color.RByte, text.Color.GByte, text.Color.BByte)),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        // Same block width as the rasterized texture, placed like the game places it for the alignment.
        var width = Math.Ceiling(formatted.WidthIncludingTrailingWhitespace) + 8;
        formatted.MaxTextWidth = width;
        var left = text.Align switch { TextAlign.Left => 0, TextAlign.Right => -width, _ => -width / 2 };

        var blockHeight = Griff.Platform.Windows.WpfText.LineHeight * lines;
        var scale = text.Size / Griff.Platform.Windows.WpfText.LineHeight;
        var matrix = new Matrix(scale, 0, 0, -scale, 0, 0) * ScreenMatrix(entity.Transform.WorldMatrix);
        dc.PushTransform(new MatrixTransform(matrix));
        dc.DrawText(formatted, new Point(left, -blockHeight / 2));
        dc.Pop();
    }

    private void DrawCameraFrame(DrawingContext dc, Entity entity, Camera camera, bool selected)
    {
        var height = camera.Size * 2;
        var width = height * CameraAspect;
        var rect = new Rect(-width / 2, -height / 2, width, height);
        var pen = new Pen(selected ? Brush("Brush.Accent") : Brush("Brush.Text.Tertiary"), 1.5 / _zoom)
        {
            DashStyle = new DashStyle([6, 4], 0),
        };

        dc.PushTransform(new MatrixTransform(ScreenMatrix(entity.Transform.WorldMatrix)));
        dc.DrawRectangle(null, pen, rect);
        dc.Pop();

        var corner = ToScreen(ToVector2(Vector3.Transform(new Vector3((float)rect.Left, (float)rect.Bottom, 0), entity.Transform.WorldMatrix)));
        dc.DrawText(Text(entity.Name, "Brush.Text.Tertiary"), new Point(corner.X + 4, corner.Y - 18));
    }

    // The Scene view isn't lit, so each light shows as a soft glow of its color out to its radius.
    private void DrawLights(DrawingContext dc)
    {
        foreach (var entity in _document!.Scene.Entities.Where(SceneViewModel.IsShown))
        {
            foreach (var light in entity.GetComponents<Light>().Where(l => l.Radius > 0))
            {
                var color = System.Windows.Media.Color.FromRgb(light.Color.RByte, light.Color.GByte, light.Color.BByte);
                var glow = new RadialGradientBrush(System.Windows.Media.Color.FromArgb(90, color.R, color.G, color.B),
                    System.Windows.Media.Color.FromArgb(0, color.R, color.G, color.B));
                var radius = light.Radius * _zoom;
                dc.DrawEllipse(glow, null, ToScreen(light.WorldCenter), radius, radius);
            }
        }
    }

    // Rooms: long-dashed outlines with their names, so a level's screens can be laid out.
    private void DrawRooms(DrawingContext dc)
    {
        var pen = new Pen(Brush("Brush.Status.Info"), 1.5) { DashStyle = new DashStyle([10, 5], 0) };
        foreach (var entity in _document!.Scene.Entities.Where(SceneViewModel.IsShown))
        {
            foreach (var room in entity.GetComponents<Room>())
            {
                var area = room.Area;
                var topLeft = ToScreen(new Vector2(area.Min.X, area.Max.Y));
                dc.DrawRectangle(null, pen, new Rect(topLeft, ToScreen(new Vector2(area.Max.X, area.Min.Y))));
                dc.DrawText(Text(entity.Name, "Brush.Status.Info"), new Point(topLeft.X + 4, topLeft.Y + 2));
            }
        }
    }

    // Physics shapes in green; triggers dashed.
    private void DrawColliders(DrawingContext dc)
    {
        var solid = new Pen(Brush("Brush.Status.Success"), 1.5);
        var trigger = new Pen(Brush("Brush.Status.Success"), 1.5) { DashStyle = new DashStyle([4, 3], 0) };
        var clickable = new Pen(Brush("Brush.Status.Info"), 1.5) { DashStyle = new DashStyle([2, 2], 0) };
        foreach (var entity in _document!.Scene.Entities.Where(SceneViewModel.IsShown))
        {
            // Buttons and sliders: where they can be clicked and dragged.
            foreach (var button in entity.GetComponents<Griff.Scenes.Button>())
                DrawBox(Griff.Runtime.Game.AreaOf(button), clickable);
            foreach (var slider in entity.GetComponents<Griff.Scenes.Slider>())
                DrawBox(Griff.Runtime.Game.AreaOf(slider), clickable);

            foreach (var collider in entity.GetComponents<BoxCollider>())
            {
                if (collider.OneWay && !collider.IsTrigger)
                    DrawOneWay(Griff.Runtime.Physics.BoundsOf(collider));
                else
                    DrawBox(Griff.Runtime.Physics.BoundsOf(collider), collider.IsTrigger ? trigger : solid);
            }

            foreach (var collider in entity.GetComponents<CircleCollider>())
            {
                var circle = Griff.Runtime.Physics.BoundsOf(collider);
                var radius = circle.Radius * _zoom;
                dc.DrawEllipse(null, collider.IsTrigger ? trigger : solid, ToScreen(circle.Center), radius, radius);
            }

            foreach (var tilemap in entity.GetComponents<Tilemap>())
            {
                foreach (var (box, oneWay) in Griff.Runtime.Physics.TileShapes(tilemap))
                {
                    if (oneWay)
                        DrawOneWay(box);
                    else
                        DrawBox(box, solid);
                }

                foreach (var (cell, rising) in Griff.Runtime.Physics.SlopeShapes(tilemap))
                {
                    var (low, high) = rising
                        ? (new Vector2(cell.Min.X, cell.Min.Y), new Vector2(cell.Max.X, cell.Max.Y))
                        : (new Vector2(cell.Max.X, cell.Min.Y), new Vector2(cell.Min.X, cell.Max.Y));
                    var triangle = new StreamGeometry();
                    using (var context = triangle.Open())
                    {
                        context.BeginFigure(ToScreen(low), false, true);
                        context.LineTo(ToScreen(high), true, false);
                        context.LineTo(ToScreen(new Vector2(high.X, low.Y)), true, false);
                    }

                    dc.DrawGeometry(null, solid, triangle);
                }

                // Ladder tiles don't collide; they're climbed, like a trigger.
                var world = entity.Transform.WorldMatrix;
                foreach (var (x, y, tile) in tilemap.Cells().Where(c => tilemap.IsLadder(c.Tile)))
                {
                    var half = new Vector2(tilemap.TileSize / 2);
                    var center = tilemap.CellCenter(x, y);
                    var min = Vector2.Transform(center - half, world);
                    var max = Vector2.Transform(center + half, world);
                    DrawBox(new Griff.Runtime.Box((min + max) / 2, Vector2.Abs(max - min) / 2), trigger);
                }
            }
        }

        // One-way platforms: a solid top (the side that blocks) over a dashed outline.
        void DrawOneWay(Griff.Runtime.Box box)
        {
            DrawBox(box, trigger);
            dc.DrawLine(new Pen(solid.Brush, 3), ToScreen(new Vector2(box.Min.X, box.Max.Y)), ToScreen(box.Max));
        }

        void DrawBox(Griff.Runtime.Box box, Pen pen)
        {
            var topLeft = ToScreen(new Vector2(box.Min.X, box.Max.Y));
            var bottomRight = ToScreen(new Vector2(box.Max.X, box.Min.Y));
            dc.DrawRectangle(null, pen, new Rect(topLeft, bottomRight));
        }
    }

    // Where movers and grid patrols go and where patrols turn, always; for the selection, its distances (how far a light reaches, a chaser notices, a unit sees),
    // which way a shooter fires, and how far a player jumps. Where a door leads and what a switch turns on are links.
    private void DrawLevelGuides(DrawingContext dc)
    {
        var guide = Brush("Brush.Status.Warning");
        var dashed = new Pen(guide, 1.5) { DashStyle = new DashStyle([4, 3], 0) };
        var dotted = new Pen(guide, 1.5) { DashStyle = new DashStyle([1, 3], 0) };
        foreach (var entity in _document!.Scene.Entities.Where(SceneViewModel.IsShown))
        {
            foreach (var mover in entity.GetComponents<Mover>())
            {
                var points = SceneViewModel.PathOf(mover).Select(ToScreen).ToList();
                for (var i = 1; i < points.Count; i++)
                    dc.DrawLine(dashed, points[i - 1], points[i]);
                if (mover.Mode == MoverMode.Loop && points.Count > 2)
                    dc.DrawLine(dashed, points[^1], points[0]);
                foreach (var point in points)
                    dc.DrawEllipse(guide, null, point, 3, 3);
            }

            // A grid patrol's points, in cell middles, the way a mover's are.
            foreach (var patrol in entity.GetComponents<GridPatrol>())
            {
                var points = SceneViewModel.RouteOf(patrol).Select(ToScreen).ToList();
                for (var i = 1; i < points.Count; i++)
                    dc.DrawLine(dashed, points[i - 1], points[i]);
                if (patrol.Mode == MoverMode.Loop && points.Count > 2)
                    dc.DrawLine(dashed, points[^1], points[0]);
                foreach (var point in points)
                    dc.DrawEllipse(guide, null, point, 3, 3);
            }

            // A patrol's route out to its turns, each end marked across; a side with no turn (it walks on until a wall
            // or an edge) is a dotted arrow, shown only on the selection.
            foreach (var patrol in entity.GetComponents<Patrol>())
            {
                var isPrimary = entity == _selection.Entity;
                if (patrol.Left <= 0 && patrol.Right <= 0 && !isPrimary)
                    continue;
                var route = SceneViewModel.RouteOf(patrol);
                var home = ToScreen(route.Home);
                foreach (var (end, open) in new[] { (route.Left, route.LeftOpen), (route.Right, route.RightOpen) })
                {
                    var tip = ToScreen(end);
                    var way = tip - home;
                    if (way.Length < 1 || open && !isPrimary)
                        continue;
                    way.Normalize();
                    var across = new Vector(-way.Y, way.X) * 6;
                    if (open)
                    {
                        dc.DrawLine(dotted, home, tip);
                        dc.DrawLine(new Pen(guide, 1.5), tip, tip - way * 6 + across);
                        dc.DrawLine(new Pen(guide, 1.5), tip, tip - way * 6 - across);
                    }
                    else
                    {
                        dc.DrawLine(dashed, home, tip);
                        dc.DrawLine(new Pen(guide, 2), tip - across, tip + across);
                    }
                }

                dc.DrawEllipse(guide, null, home, 3, 3);
            }
        }

        if (_selection.Entity is not { } selected || selected.Scene != _document.Scene)
            return;
        var here = ToVector2(selected.Transform.WorldPosition);
        foreach (var distance in _model.Distances())
            dc.DrawEllipse(null, dashed, ToScreen(distance.Center), distance.Value * _zoom, distance.Value * _zoom);
        foreach (var shooter in selected.GetComponents<Shooter>().Where(s => string.IsNullOrWhiteSpace(s.Target) && s.Direction != Vector2.Zero))
            dc.DrawLine(dashed, ToScreen(here + shooter.Offset), ToScreen(here + shooter.Offset + Vector2.Normalize(shooter.Direction) * 2));

        // How far a player jumps, each way, and where a bouncer sends things: under the default gravity.
        foreach (var movement in selected.GetComponents<PlatformerMovement>())
        {
            var gravity = -Griff.Runtime.Physics.DefaultGravity.Y * (selected.GetComponent<Body>()?.GravityScale ?? 1);
            var jump = movement.JumpSpeedFor(gravity);
            foreach (var side in new[] { -1, 1 })
                DrawArc(dc, here, new Vector2(side * movement.Speed, jump), gravity, movement.FallMultiplier, movement.MaxFallSpeed);
            dc.DrawLine(dashed, ToScreen(here + new Vector2(-0.6f, movement.JumpHeight)), ToScreen(here + new Vector2(0.6f, movement.JumpHeight)));
        }

        foreach (var bouncer in selected.GetComponents<Bouncer>())
            DrawArc(dc, here, bouncer.Way * bouncer.Strength, -Griff.Runtime.Physics.DefaultGravity.Y, 1, 0);
    }

    // The selection's links (see SceneViewModel.Links): a dashed arrow from each field's entity to the one it names,
    // with the fields named halfway along.
    private void DrawLinks(DrawingContext dc)
    {
        var accent = Brush("Brush.Accent");
        var chrome = Brush("Brush.Surface.Chrome");
        var pen = new Pen(accent, 1.5) { DashStyle = new DashStyle([5, 3], 0) };
        var border = new Pen(accent, 1);
        foreach (var arrow in LinkArrows())
        {
            var curve = new StreamGeometry();
            using (var context = curve.Open())
            {
                context.BeginFigure(arrow.Start, false, false);
                context.QuadraticBezierTo(arrow.Bend, arrow.Tip, true, false);
            }

            curve.Freeze();
            dc.DrawGeometry(null, pen, curve);
            dc.DrawEllipse(accent, null, arrow.Start, 2.5, 2.5);

            var into = arrow.Tip - arrow.Bend;
            into.Normalize();
            var across = new Vector(-into.Y, into.X) * 4.5;
            var head = new StreamGeometry();
            using (var context = head.Open())
            {
                context.BeginFigure(arrow.Tip, true, true);
                context.LineTo(arrow.Tip - into * 10 + across, true, false);
                context.LineTo(arrow.Tip - into * 10 - across, true, false);
            }

            head.Freeze();
            dc.DrawGeometry(accent, null, head);

            var label = Text(arrow.Fields, "Brush.Text.Secondary");
            var place = new Point(arrow.Middle.X - label.Width / 2, arrow.Middle.Y - label.Height / 2);
            dc.DrawRoundedRectangle(chrome, border, new Rect(place.X - 4, place.Y - 1, label.Width + 8, label.Height + 2), 3, 3);
            dc.DrawText(label, place);
        }
    }

    /// <summary>
    /// The arrows for the selection's links, in screen space: one from each entity to each it names (several fields
    /// naming the same one share it, their names joined, as "Switch Targets"), bowed to one side so links both ways
    /// between two entities stay apart, its tip a little short of the entity so its marker stays clear, and
    /// <c>Middle</c> where the fields are named: halfway along, or past the tip when the arrow is too short.
    /// </summary>
    private List<(Entity From, Entity To, string Fields, Point Start, Point Bend, Point Tip, Point Middle)> LinkArrows()
    {
        var arrows = new List<(Entity, Entity, string, Point, Point, Point, Point)>();
        foreach (var pair in _model.Links().GroupBy(l => (l.From, l.To)))
        {
            var from = ToScreen(ToVector2(pair.Key.From.Transform.WorldPosition));
            var to = ToScreen(ToVector2(pair.Key.To.Transform.WorldPosition));
            var along = to - from;
            if (along.Length < 1)
                continue;
            var side = new Vector(-along.Y, along.X) / along.Length;
            var bend = from + along / 2 + side * Math.Min(along.Length * 0.15, 40);
            var into = to - bend;
            into.Normalize();
            var fields = string.Join(", ", pair.Select(l => $"{l.Reference.Component.GetType().Name} {l.Reference.Property.Name}"));

            // Too short to hold its label (a lever's own hint, a fighter's hitbox), the label goes past the tip instead,
            // off the selection.
            var label = Text(fields, "Brush.Text.Secondary");
            var middle = from + along / 4 + (bend - from) / 2;
            if (along.Length < label.Width + 40)
            {
                var way = along / along.Length;
                middle = to + way * (Math.Abs(way.X) * (label.Width / 2 + 4) + Math.Abs(way.Y) * (label.Height / 2 + 1) + 8);
            }

            arrows.Add((pair.Key.From, pair.Key.To, fields, from, bend, to - into * 4, middle));
        }

        return arrows;
    }

    // A dashed arc: where something leaving `from` at `velocity` goes under `gravity` (heavier on the way down), for 1.5 seconds
    // or until it has fallen well below where it started.
    private void DrawArc(DrawingContext dc, Vector2 from, Vector2 velocity, float gravity, float fallMultiplier, float maxFall)
    {
        var pen = new Pen(Brush("Brush.Status.Warning"), 1.5) { DashStyle = new DashStyle([4, 3], 0) };
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(ToScreen(from), false, false);
            var (position, v) = (from, velocity);
            const float dt = 1 / 30f;
            for (var t = 0f; t < 1.5f && position.Y > from.Y - 4; t += dt)
            {
                v.Y -= gravity * (v.Y < 0 ? Math.Max(1, fallMultiplier) : 1) * dt;
                if (maxFall > 0)
                    v.Y = Math.Max(v.Y, -maxFall);
                position += v * dt;
                context.LineTo(ToScreen(position), true, false);
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    // While painting: the grid's extent, the cell under the cursor (or the rectangle being dragged, or the area a fill
    // would cover), a palette of the tileset's tiles, and the tools.
    private void DrawPaintOverlay(DrawingContext dc, Tilemap tilemap)
    {
        var accent = Brush("Brush.Accent");
        var size = tilemap.TileSize;
        dc.PushTransform(new MatrixTransform(ScreenMatrix(tilemap.Entity!.Transform.WorldMatrix)));

        var extent = new Rect(0, -Math.Max(1, tilemap.Height) * size, Math.Max(1, tilemap.Width) * size, Math.Max(1, tilemap.Height) * size);
        dc.DrawRectangle(null, new Pen(Brush("Brush.Text.Tertiary"), 1 / _zoom) { DashStyle = new DashStyle([4, 4], 0) }, extent);

        var erasing = _stroke?.Erasing ?? (_rectangleFrom is not null ? _rectangleErases : Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        var color = erasing ? "Brush.Status.Error" : "Brush.Accent";
        var outline = new Pen(Brush(color), 2 / _zoom);
        Rect Cells(int left, int top, int right, int bottom) => new(left * size, -(bottom + 1) * size, (right - left + 1) * size, (bottom - top + 1) * size);
        if (_rectangleFrom is var (fx, fy) && _hoverCell is var (tx, ty))
        {
            var box = Cells(Math.Min(fx, tx), Math.Min(fy, ty), Math.Max(fx, tx), Math.Max(fy, ty));
            dc.DrawRectangle(Faded(color, 0.2), outline, box);
        }
        else if (_hoverCell is var (hx, hy))
        {
            if (_model.TileTool == TileTool.Fill && _drag == DragMode.None)
            {
                // The area a click would fill, when it's small enough to draw cell by cell.
                if (_fillPreviewOf is not var (previewTiles, previewCell) || previewTiles != tilemap.Tiles || previewCell != (hx, hy))
                {
                    var region = tilemap.FillRegion(hx, hy);
                    (_fillPreviewOf, _fillPreview) = ((tilemap.Tiles, (hx, hy)), region.Count <= 4096 ? region : []);
                }

                var shade = Faded(color, 0.2);
                foreach (var (x, y) in _fillPreview)
                    dc.DrawRectangle(shade, null, Cells(x, y, x, y));
            }

            dc.DrawRectangle(null, outline, Cells(hx, hy, hx, hy));
        }

        dc.Pop();

        // Palette, top left: a tab for each tileset (the one shown picked out) and + to add one, then its tiles.
        var palette = Palette(tilemap);
        var panel = palette.Panel;
        dc.DrawRoundedRectangle(Brush("Brush.Surface.Chrome"), new Pen(Brush("Brush.Border.Default"), 1), panel, 4, 4);
        foreach (var (box, tileset) in palette.Tabs)
        {
            var shown = tileset == palette.Shown;
            dc.DrawRoundedRectangle(shown ? accent : null, new Pen(shown ? accent : Brush("Brush.Border.Default"), 1), box, 3, 3);
            var name = Text(tileset.Name, shown ? "Brush.Text.OnAccent" : "Brush.Text.Primary");
            dc.DrawText(name, new Point(box.X + (box.Width - name.Width) / 2, box.Y + (box.Height - name.Height) / 2));
        }

        dc.DrawRoundedRectangle(null, new Pen(Brush("Brush.Border.Default"), 1), palette.AddTab, 3, 3);
        var plus = Text("+", "Brush.Text.Primary");
        dc.DrawText(plus, new Point(palette.AddTab.X + (palette.AddTab.Width - plus.Width) / 2, palette.AddTab.Y + (palette.AddTab.Height - plus.Height) / 2));

        var fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, tilemap.Tint.RByte, tilemap.Tint.GByte, tilemap.Tint.BByte));
        for (var tile = palette.Shown.First; tile < palette.Shown.First + palette.Count; tile++)
        {
            if (palette.CellBounds(tile) is not { } cell)
                continue;
            var rect = cell;
            rect.Inflate(-2, -2);
            if (TileImage(tilemap, tile) is { } image)
                dc.DrawImage(image, rect);
            else
                dc.DrawRectangle(fill, null, rect);
            if (tile == _model.BrushTile)
                dc.DrawRectangle(null, new Pen(accent, 2), cell);
        }

        // A thin bar down the right of the tiles when they scroll: how far down the ones in sight are.
        if (palette.Rows > palette.VisibleRows)
        {
            var track = new Rect(panel.Right - 5, palette.CellsTop, 3, palette.VisibleRows * PaletteCell);
            var thumb = new Rect(track.X, track.Y + track.Height * palette.Scroll / palette.Rows, track.Width, track.Height * palette.VisibleRows / palette.Rows);
            dc.DrawRectangle(Brush("Brush.Border.Default"), null, track);
            dc.DrawRectangle(Brush("Brush.Text.Secondary"), null, thumb);
        }

        // The tools, under the palette.
        foreach (var tool in Enum.GetValues<TileTool>())
        {
            var button = palette.ToolBounds(tool);
            var picked = tool == _model.TileTool;
            dc.DrawRoundedRectangle(picked ? accent : Brush("Brush.Surface.Chrome"), new Pen(picked ? accent : Brush("Brush.Border.Default"), 1), button, 3, 3);
            var label = Text($"{tool}  {ToolKey(tool)}", picked ? "Brush.Text.OnAccent" : "Brush.Text.Primary");
            dc.DrawText(label, new Point(button.X + (button.Width - label.Width) / 2, button.Y + (button.Height - label.Height) / 2));
        }

        // The map's layers under the tools, the one being painted picked out, and + Layer to add one.
        foreach (var (box, layer) in palette.LayerButtons)
        {
            var picked = layer == tilemap;
            dc.DrawRoundedRectangle(picked ? accent : Brush("Brush.Surface.Chrome"), new Pen(picked ? accent : Brush("Brush.Border.Default"), 1), box, 3, 3);
            var label = Text(layer.Entity!.Name, picked ? "Brush.Text.OnAccent" : "Brush.Text.Primary");
            dc.DrawText(label, new Point(box.X + (box.Width - label.Width) / 2, box.Y + (box.Height - label.Height) / 2));
        }

        dc.DrawRoundedRectangle(Brush("Brush.Surface.Chrome"), new Pen(Brush("Brush.Border.Default"), 1), palette.AddLayer, 3, 3);
        var addLayer = Text(AddLayerLabel, "Brush.Text.Primary");
        dc.DrawText(addLayer, new Point(palette.AddLayer.X + (palette.AddLayer.Width - addLayer.Width) / 2, palette.AddLayer.Y + (palette.AddLayer.Height - addLayer.Height) / 2));

        var hint = Text(_model.TileTool switch
        {
            TileTool.Rectangle => "Rectangle: drag a box to fill · Shift erase · Ctrl pick · [ ] tile · Esc done",
            TileTool.Fill => "Fill: click an area to fill it · Shift erase · Ctrl pick · [ ] tile · Esc done",
            _ => "Brush: drag to paint · Shift erase · Ctrl pick · [ ] tile · Esc done",
        }, "Brush.Text.Secondary");
        dc.DrawText(hint, new Point(panel.Left + 6, palette.AddLayer.Bottom + 4));
    }

    private void DrawMarker(DrawingContext dc, Entity entity)
    {
        var center = ToScreen(ToVector2(entity.Transform.WorldPosition));
        var pen = new Pen(entity.IsEnabled ? Brush("Brush.Text.Secondary") : Brush("Brush.Text.Disabled"), 1.5);
        dc.DrawEllipse(null, pen, center, MarkerRadius, MarkerRadius);
        dc.DrawLine(pen, new Point(center.X - 3, center.Y), new Point(center.X + 3, center.Y));
        dc.DrawLine(pen, new Point(center.X, center.Y - 3), new Point(center.X, center.Y + 3));
    }

    /// <summary>Outlines a selected entity; the primary one also gets its pivot handle.</summary>
    private void DrawSelection(DrawingContext dc, Entity entity, bool primary)
    {
        var accent = Brush("Brush.Accent");
        if (SceneViewModel.Outline(entity) is { } bounds)
        {
            var corners = bounds.Select(p => ToScreen(p)).ToArray();
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(corners[0], isFilled: false, isClosed: true);
                g.PolyLineTo(corners[1..], isStroked: true, isSmoothJoin: false);
            }

            // Dark halo under the accent line keeps the outline visible on any sprite color.
            dc.DrawGeometry(null, new Pen(Brush("Brush.Surface.Chrome"), 4), geometry);
            dc.DrawGeometry(null, new Pen(accent, 2), geometry);
        }
        else
        {
            // Entities with nothing to outline get a ring around their marker.
            var center = ToScreen(ToVector2(entity.Transform.WorldPosition));
            dc.DrawEllipse(null, new Pen(Brush("Brush.Surface.Chrome"), 4), center, MarkerRadius + 3, MarkerRadius + 3);
            dc.DrawEllipse(null, new Pen(accent, 2), center, MarkerRadius + 3, MarkerRadius + 3);
        }

        if (!primary)
            return;

        // Pivot handle with axis arms.
        var pivot = ToScreen(ToVector2(entity.Transform.WorldPosition));
        dc.DrawLine(new Pen(Brush("Brush.Axis.X"), 2), pivot, new Point(pivot.X + 28, pivot.Y));
        dc.DrawLine(new Pen(Brush("Brush.Axis.Y"), 2), pivot, new Point(pivot.X, pivot.Y - 28));
        dc.DrawEllipse(accent, null, pivot, 3.5, 3.5);
    }

    // The primary selection's handles: the rotate knob, then the scale or resize squares over the root of its stem, the
    // knobs on its distances' circles, and those on its path.
    private void DrawHandles(DrawingContext dc)
    {
        if (RotateHandle() is var (root, knob))
            DrawRotateHandle(dc, root, knob);
        DrawScaleHandles(dc);
        DrawResizeHandles(dc);
        DrawDistanceHandles(dc);
        DrawPathHandles(dc);
    }

    // A knob on each of the primary selection's mover points (numbered) and patrol turns (hollow while it walks on to a
    // wall), and a small plus between points and past the last to drag out a new one, in the guides' color. Hovering says
    // what a knob does; dragging, where it is.
    private void DrawPathHandles(DrawingContext dc)
    {
        var color = Brush("Brush.Status.Warning");
        var chrome = Brush("Brush.Surface.Chrome");
        var halo = new Pen(chrome, 4);
        string Number(float value) => value.ToString("0.##", CultureInfo.CurrentCulture);
        foreach (var handle in PathHandles())
        {
            var dragging = _drag == DragMode.Path && _pathKey == handle.Key;
            var hovered = _drag == DragMode.None && _overPath == handle.Key;
            var active = dragging || hovered;
            if (handle.Point is not { } point)
            {
                var small = HandleSquare + (active ? 1 : 0);
                dc.DrawEllipse(chrome, halo, handle.Screen, small, small);
                dc.DrawEllipse(active ? color : chrome, new Pen(color, 1.5), handle.Screen, small, small);
                var plus = new Pen(active ? chrome : color, 1.5);
                dc.DrawLine(plus, handle.Screen + new Vector(-2, 0), handle.Screen + new Vector(2, 0));
                dc.DrawLine(plus, handle.Screen + new Vector(0, -2), handle.Screen + new Vector(0, 2));
                if (hovered)
                    PathLabel(Text("Drag to add a point", "Brush.Text.Primary"), handle.Screen);
                continue;
            }

            var radius = HandleRadius + (active ? 1.5 : 0);
            dc.DrawEllipse(chrome, halo, handle.Screen, radius, radius);
            dc.DrawEllipse(active || !point.Open ? color : chrome, new Pen(color, 2), handle.Screen, radius, radius);

            string text;
            if (point.Component is Patrol patrol)
            {
                var side = point.Index < 0 ? "Left" : "Right";
                var value = point.Index < 0 ? patrol.Left : patrol.Right;
                text = point.Open
                    ? dragging ? $"{side}: on to a wall" : hovered ? $"{side}: on to a wall or an edge  ·  drag to set" : side
                    : dragging ? $"{side} {Number(value)}" : hovered ? $"{side} {Number(value)}  ·  double-click to clear" : side;
            }
            else
            {
                var offset = ((Mover)point.Component).Path[point.Index];
                text = dragging ? $"{point.Index}  ·  {Number(offset.X)}, {Number(offset.Y)}"
                    : hovered ? $"{point.Index}  ·  double-click to remove" : $"{point.Index}";
            }

            PathLabel(Text(text, active ? "Brush.Text.Primary" : "Brush.Text.Secondary"), handle.Screen);
        }

        void PathLabel(FormattedText label, Point at)
        {
            var place = new Point(at.X + 10, at.Y - label.Height - 4);
            dc.DrawRoundedRectangle(chrome, null, new Rect(place.X - 4, place.Y - 1, label.Width + 8, label.Height + 2), 3, 3);
            dc.DrawText(label, place);
        }
    }

    // A knob on each distance's circle in the guides' color, named on its outer side; while dragging, its value too.
    private void DrawDistanceHandles(DrawingContext dc)
    {
        var color = Brush("Brush.Status.Warning");
        var chrome = Brush("Brush.Surface.Chrome");
        var halo = new Pen(chrome, 4);
        foreach (var (distance, point, outward) in DistanceHandles())
        {
            var dragging = _drag == DragMode.Distance && _distance?.Distance.Key == distance.Key;
            var active = dragging || _drag == DragMode.None && _overDistance == distance.Key;
            var radius = HandleRadius + (active ? 1.5 : 0);
            dc.DrawEllipse(chrome, halo, point, radius, radius);
            dc.DrawEllipse(active ? color : chrome, new Pen(color, 2), point, radius, radius);

            var name = $"{distance.Component.GetType().Name} {distance.Property.Name}";
            var label = dragging
                ? Text($"{name} {distance.Value.ToString("0.##", CultureInfo.CurrentCulture)}", "Brush.Text.Primary")
                : Text(name, active ? "Brush.Text.Primary" : "Brush.Text.Secondary");
            // Out to the side for knobs on the left or right, above or below for those on the top or bottom.
            var place = outward.X > 0.5 ? new Point(point.X + 12, point.Y - label.Height / 2)
                : outward.X < -0.5 ? new Point(point.X - 12 - label.Width, point.Y - label.Height / 2)
                : new Point(point.X - label.Width / 2, outward.Y > 0 ? point.Y + 10 : point.Y - 10 - label.Height);
            dc.DrawRoundedRectangle(chrome, null, new Rect(place.X - 4, place.Y - 1, label.Width + 8, label.Height + 2), 3, 3);
            dc.DrawText(label, place);
        }
    }

    // A stem out of the top of the primary selection with a knob to drag; while turning, the circle the knob follows and
    // the angle it's at.
    private void DrawRotateHandle(DrawingContext dc, Point root, Point knob)
    {
        var accent = Brush("Brush.Accent");
        var halo = new Pen(Brush("Brush.Surface.Chrome"), 4);
        if (_drag == DragMode.Rotate && _rotate is not null)
        {
            var pivot = ToScreen(_rotate.Pivot);
            var radius = (knob - pivot).Length;
            dc.DrawEllipse(null, new Pen(Faded("Brush.Accent", 0.5), 1) { DashStyle = new DashStyle([4, 4], 0) }, pivot, radius, radius);
        }

        dc.DrawLine(halo, root, knob);
        dc.DrawLine(new Pen(accent, 1.5), root, knob);
        var grown = _overHandle || _drag == DragMode.Rotate ? 1.5 : 0;
        dc.DrawEllipse(Brush("Brush.Surface.Chrome"), halo, knob, HandleRadius + grown, HandleRadius + grown);
        dc.DrawEllipse(_overHandle || _drag == DragMode.Rotate ? accent : Brush("Brush.Surface.Chrome"), new Pen(accent, 2), knob, HandleRadius + grown, HandleRadius + grown);

        if (_drag == DragMode.Rotate && _selection.Entity is { } turning)
        {
            var label = Text($"{turning.Transform.Rotation.Z.ToString("0.#", CultureInfo.CurrentCulture)}°", "Brush.Text.Primary");
            var at = new Point(knob.X + 12, knob.Y - label.Height / 2);
            dc.DrawRoundedRectangle(Brush("Brush.Surface.Chrome"), null, new Rect(at.X - 4, at.Y - 1, label.Width + 8, label.Height + 2), 3, 3);
            dc.DrawText(label, at);
        }
    }

    // Squares on the corners and edges of the primary selection; while scaling, its scale by the one being dragged.
    private void DrawScaleHandles(DrawingContext dc)
    {
        var accent = Brush("Brush.Accent");
        var chrome = Brush("Brush.Surface.Chrome");
        var halo = new Pen(chrome, 4);
        Point? dragged = null;
        foreach (var (spot, point) in ScaleHandles())
        {
            var active = _drag == DragMode.Scale ? spot == _scaleSpot : _drag == DragMode.None && spot == _overScale;
            var half = HandleSquare + (active ? 1 : 0);
            var square = new Rect(point.X - half, point.Y - half, half * 2, half * 2);
            dc.DrawRectangle(chrome, halo, square);
            dc.DrawRectangle(active ? accent : chrome, new Pen(accent, 1.5), square);
            if (active && _drag == DragMode.Scale)
                dragged = point;
        }

        if (dragged is { } at && _selection.Entity is { } scaling)
        {
            var scale = scaling.Transform.Scale;
            var label = Text($"{scale.X.ToString("0.##", CultureInfo.CurrentCulture)} × {scale.Y.ToString("0.##", CultureInfo.CurrentCulture)}", "Brush.Text.Primary");
            var place = new Point(at.X + 12, at.Y - label.Height / 2);
            dc.DrawRoundedRectangle(chrome, null, new Rect(place.X - 4, place.Y - 1, label.Width + 8, label.Height + 2), 3, 3);
            dc.DrawText(label, place);
        }
    }

    // Squares on the sprite's or colliders' corners and edges (colliders' in their green); while resizing, the size by the
    // one being dragged.
    private void DrawResizeHandles(DrawingContext dc)
    {
        var color = Brush(_model.Handles == SceneHandles.Collider ? "Brush.Status.Success" : "Brush.Accent");
        var chrome = Brush("Brush.Surface.Chrome");
        var halo = new Pen(chrome, 4);
        Point? dragged = null;
        foreach (var (shape, spot, point) in ResizeHandles())
        {
            var active = _drag == DragMode.Resize
                ? shape == _resize?.Shape && spot == _resizeSpot
                : _drag == DragMode.None && _overResize is var (overShape, overSpot) && overShape == shape && overSpot == spot;
            var half = HandleSquare + (active ? 1 : 0);
            var square = new Rect(point.X - half, point.Y - half, half * 2, half * 2);
            dc.DrawRectangle(chrome, halo, square);
            dc.DrawRectangle(active ? color : chrome, new Pen(color, 1.5), square);
            if (active && _drag == DragMode.Resize)
                dragged = point;
        }

        if (dragged is { } at && _resize?.Shape is { } resizing)
        {
            string Number(float value) => value.ToString("0.##", CultureInfo.CurrentCulture);
            var size = resizing switch
            {
                Sprite sprite => sprite.Size,
                BoxCollider box => box.Size,
                _ => Vector2.Zero,
            };
            var label = Text(resizing is CircleCollider circle ? $"r {Number(circle.Radius)}" : $"{Number(size.X)} × {Number(size.Y)}", "Brush.Text.Primary");
            var place = new Point(at.X + 12, at.Y - label.Height / 2);
            dc.DrawRoundedRectangle(chrome, null, new Rect(place.X - 4, place.Y - 1, label.Width + 8, label.Height + 2), 3, 3);
            dc.DrawText(label, place);
        }
    }

    // The box being dragged, and outlines on what it will catch.
    private void DrawBoxSelection(DrawingContext dc)
    {
        if ((_boxEnd - _dragStartScreen).Length < DragThreshold)
            return;
        foreach (var caught in _model.EntitiesInBox(ToWorld(_dragStartScreen), ToWorld(_boxEnd)).Where(e => !_selection.IsSelected(e)))
            DrawSelection(dc, caught, primary: false);

        var accent = Brush("Brush.Accent");
        dc.DrawRectangle(Faded("Brush.Accent", 0.12), new Pen(accent, 1), new Rect(_dragStartScreen, _boxEnd));
    }

    // The zoom, and which handles are out when they aren't the scale ones.
    private void DrawZoomLabel(DrawingContext dc)
    {
        var handles = _model.Handles switch
        {
            SceneHandles.Resize => "Resize handles  ·  ",
            SceneHandles.Collider => "Collider handles  ·  ",
            SceneHandles.Distance => "Distance handles  ·  ",
            _ => "",
        };
        var text = Text($"{handles}{_zoom / 64:P0}", "Brush.Text.Tertiary");
        dc.DrawText(text, new Point(ActualWidth - text.Width - 10, ActualHeight - text.Height - 8));

        // In a prefab's tab, a reminder of what's being edited.
        if (_document is { IsPrefab: true } prefab)
        {
            var label = Text($"Editing the {prefab.Name} prefab  ·  saving it updates every copy", "Brush.Accent");
            dc.DrawText(label, new Point(10, 8));
        }
    }

    // ----- Scene queries -----

    /// <summary>Topmost unlocked entity under the point (see <see cref="SceneViewModel.EntityAt"/>); pivots are hit within a few pixels.</summary>
    private Entity? HitTest(Point screen) =>
        _document is null ? null : _model.EntityAt(ToWorld(screen), (float)((MarkerRadius + 3) / _zoom));

    /// <summary>
    /// The frame the primary selection's handles sit on, in screen space: its outline's corners, or a box around its
    /// marker, as (-1, -1), (1, -1), (1, 1) and (-1, 1) in its own axes. Null when there's nothing to handle, or while
    /// painting.
    /// </summary>
    private Point[]? HandleFrame()
    {
        if (_document is null || _selection.Entity is not { } entity || entity.Scene != _document.Scene || PaintTarget is not null)
            return null;
        if (SceneViewModel.Outline(entity) is { } corners)
            return corners.Select(c => ToScreen(c)).ToArray();

        var world = entity.Transform.WorldMatrix;
        var right = ScreenDirection(Vector3.UnitX, world, new Vector(1, 0)) * MarkerFrame;
        var up = ScreenDirection(Vector3.UnitY, world, new Vector(0, -1)) * MarkerFrame;
        var pivot = ToScreen(ToVector2(entity.Transform.WorldPosition));
        return [pivot - right - up, pivot + right - up, pivot + right + up, pivot - right + up];
    }

    // A point on the frame from its spot in the entity's own axes, each from -1 to 1.
    private static Point FramePoint(Point[] frame, Vector2 spot)
    {
        var (u, v) = ((spot.X + 1) / 2, (spot.Y + 1) / 2);
        var bottom = frame[0] + (frame[1] - frame[0]) * u;
        var top = frame[3] + (frame[2] - frame[3]) * u;
        return bottom + (top - bottom) * v;
    }

    // Which way one of an entity's own axes points on screen (Y down), or the fallback when it's squashed flat.
    private static Vector ScreenDirection(Vector3 axis, Matrix4x4 world, Vector fallback)
    {
        var v = Vector3.TransformNormal(axis, world);
        var direction = new Vector(v.X, -v.Y);
        if (direction.Length < 1e-6)
            return fallback;
        direction.Normalize();
        return direction;
    }

    /// <summary>
    /// Where the rotate handle is, in screen space: a stem from the top edge of the primary selection's frame out along
    /// its up direction, ending in the knob. Null when there's nothing to turn, or while painting.
    /// </summary>
    private (Point Root, Point Knob)? RotateHandle()
    {
        if (HandleFrame() is not { } frame)
            return null;
        var root = FramePoint(frame, new Vector2(0, 1));
        return (root, root + ScreenDirection(Vector3.UnitY, _selection.Entity!.Transform.WorldMatrix, new Vector(0, -1)) * HandleStem);
    }

    /// <summary>
    /// The scale handles, in screen space, with their spots on the frame (see <see cref="ScaleSpots"/>). Edges too short
    /// to hold a handle in their middle go without, and something drawn too small for handles to stay clear of the
    /// middle, where it's grabbed to move it, gets none until the view zooms in.
    /// </summary>
    private List<(Vector2 Spot, Point Screen)> ScaleHandles()
    {
        if (_model.Handles != SceneHandles.Scale || HandleFrame() is not { } frame)
            return [];
        var (width, height) = ((frame[1] - frame[0]).Length, (frame[3] - frame[0]).Length);
        if (Math.Min(width, height) < ScaleHandleRoom)
            return [];
        return ScaleSpots
            .Where(spot => (spot.X == 0 ? width : spot.Y == 0 ? height : double.MaxValue) >= EdgeHandleRoom)
            .Select(spot => (spot, FramePoint(frame, spot)))
            .ToList();
    }

    // The scale handle under a point (the nearest, where they crowd), if any.
    private Vector2? ScaleHandleAt(Point screen) =>
        ScaleHandles().Where(h => (screen - h.Screen).Length <= HandleSquare + 4)
            .OrderBy(h => (screen - h.Screen).Length)
            .Select(h => (Vector2?)h.Spot)
            .FirstOrDefault();

    // A resize cursor along the line from the frame's middle out through the handle.
    private Cursor ScaleCursor(Vector2 spot) =>
        HandleFrame() is { } frame ? SizeCursor(FramePoint(frame, spot) - FramePoint(frame, Vector2.Zero)) : Cursors.SizeAll;

    /// <summary>
    /// The resize handles on the primary selection's sprite or colliders (see <see cref="SceneViewModel.ResizableShapes"/>),
    /// in screen space, with their spots on each shape. As with the scale handles, a shape drawn too small to hold them
    /// clear of its middle gets none until the view zooms in, and edges too short go without a middle one.
    /// </summary>
    private List<(Component Shape, Vector2 Spot, Point Screen)> ResizeHandles()
    {
        if (_model.Handles == SceneHandles.Scale || HandleFrame() is null)
            return [];
        var handles = new List<(Component, Vector2, Point)>();
        foreach (var shape in _model.ResizableShapes())
        {
            if (SceneViewModel.ShapeFrame.Of(shape) is not { } frame)
                continue;
            Point At(Vector2 spot) => ToScreen(frame.ToWorld(frame.HandleAt(spot)));
            var (width, height) = ((At(new Vector2(1, 0)) - At(new Vector2(-1, 0))).Length, (At(new Vector2(0, 1)) - At(new Vector2(0, -1))).Length);
            if (Math.Min(width, height) < ScaleHandleRoom)
                continue;
            handles.AddRange(frame.Spots
                .Where(spot => frame.IsCircle || (spot.X == 0 ? width : spot.Y == 0 ? height : double.MaxValue) >= EdgeHandleRoom)
                .Select(spot => (shape, spot, At(spot))));
        }

        return handles;
    }

    // The resize handle under a point (the nearest, where they crowd), if any.
    private (Component Shape, Vector2 Spot)? ResizeHandleAt(Point screen) =>
        ResizeHandles().Where(h => (screen - h.Screen).Length <= HandleSquare + 4)
            .OrderBy(h => (screen - h.Screen).Length)
            .Select(h => ((Component, Vector2)?)(h.Shape, h.Spot))
            .FirstOrDefault();

    // A resize cursor out from the shape's middle through the handle.
    private Cursor ResizeCursor(Component shape, Vector2 spot) =>
        SceneViewModel.ShapeFrame.Of(shape) is { } frame
            ? SizeCursor(ToScreen(frame.ToWorld(frame.HandleAt(spot))) - ToScreen(frame.ToWorld(frame.Center)))
            : Cursors.SizeAll;

    /// <summary>
    /// The knobs on the primary selection's distances (see <see cref="SceneViewModel.Distances"/>), in Distance mode, in
    /// screen space, with the way out from their circle's middle (Y down). Each sits at its own angle, the first on the
    /// right and the next ones an eighth further round clockwise, so circles of the same size keep their knobs (and
    /// names) apart.
    /// </summary>
    private List<(SceneDistance Distance, Point Screen, Vector Outward)> DistanceHandles()
    {
        if (_model.Handles != SceneHandles.Distance || HandleFrame() is null)
            return [];
        return _model.Distances()
            .Select((distance, i) =>
            {
                var angle = -i * MathF.PI / 4;
                var way = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                return (distance, ToScreen(distance.Center + way * distance.Value), new Vector(way.X, -way.Y));
            })
            .ToList();
    }

    // The distance knob under a point (the nearest, where they crowd), if any.
    private SceneDistance? DistanceHandleAt(Point screen) =>
        DistanceHandles().Where(h => (screen - h.Screen).Length <= HandleRadius + 4)
            .OrderBy(h => (screen - h.Screen).Length)
            .Select(h => h.Distance)
            .FirstOrDefault();

    /// <summary>
    /// The handles on the primary selection's path, in screen space: one on each of its points (see
    /// <see cref="SceneViewModel.PathPoints"/>), and for a mover a plus between each two points far enough apart (and on
    /// the way back to the start of a loop) and one past its last point, to drag out a new point. The pluses hide while a
    /// point is dragged, and everything while painting.
    /// </summary>
    private List<PathHandle> PathHandles()
    {
        if (_document is null || PaintTarget is not null)
            return [];
        var handles = _model.PathPoints().Select(p => new PathHandle(p.Key, ToScreen(p.World), p, null, p.Index)).ToList();
        if (_selection.Entity?.GetComponent<Mover>() is not { } mover || _drag == DragMode.Path || mover.Entity?.Scene != _document.Scene)
            return handles;

        var path = SceneViewModel.PathOf(mover).Select(ToScreen).ToList();
        for (var i = 1; i < path.Count; i++)
        {
            if ((path[i] - path[i - 1]).Length >= PathGap)
                handles.Add(new PathHandle($"+{i}", path[i - 1] + (path[i] - path[i - 1]) / 2, null, mover, i));
        }

        // A loop adds on the way back to its start; anything else adds past its end, on along its last stretch.
        if (mover.Mode == MoverMode.Loop && path.Count > 2)
        {
            if ((path[0] - path[^1]).Length >= PathGap)
                handles.Add(new PathHandle($"+{path.Count}", path[^1] + (path[0] - path[^1]) / 2, null, mover, path.Count));
        }
        else
        {
            var way = path.Count > 1 ? path[^1] - path[^2] : new Vector(1, 0);
            way = way.Length < 1 ? new Vector(1, 0) : way / way.Length;
            handles.Add(new PathHandle($"+{path.Count}", path[^1] + way * PathGap, null, mover, path.Count));
        }

        return handles;
    }

    // The path handle under a point (the nearest, where they crowd), if any.
    private PathHandle? PathHandleAt(Point screen) =>
        PathHandles().Where(h => (screen - h.Screen).Length <= HandleRadius + 4)
            .OrderBy(h => (screen - h.Screen).Length)
            .FirstOrDefault();

    // Patrol turns slide left and right; points and new points go anywhere.
    private static Cursor PathCursor(PathHandle handle) => handle.Point?.Component is Patrol ? Cursors.SizeWE : Cursors.SizeAll;

    // A resize cursor out from the distance's middle toward the cursor.
    private Cursor DistanceCursor(SceneDistance distance, Point screen) => SizeCursor(screen - ToScreen(distance.Center));

    // The resize cursor closest to a direction on screen (Y down).
    private static Cursor SizeCursor(Vector outward)
    {
        var angle = (Math.Atan2(-outward.Y, outward.X) * 180 / Math.PI + 360) % 180;
        return angle switch
        {
            < 22.5 or >= 157.5 => Cursors.SizeWE,
            < 67.5 => Cursors.SizeNESW,
            < 112.5 => Cursors.SizeNS,
            _ => Cursors.SizeNWSE,
        };
    }

    private bool IsOverRotateHandle(Point screen) =>
        RotateHandle() is var (_, knob) && (screen - knob).Length <= HandleRadius + 4;

    // How much a drag of a scale handle, from where it was pressed to the cursor, scales by along the primary's own
    // axes: a corner scales both, an edge one. With Shift both scale alike, by how far the cursor has gone along the
    // line from the pivot through the handle, so proportions stay.
    private Vector2 ScaleFactor(SceneViewModel.SelectionScale scale, Vector2 cursor)
    {
        var from = scale.Along(_scalePress);
        var to = scale.Along(cursor);
        // A handle pressed right on the pivot can't say how much; it leaves that axis alone.
        var near = (float)(4 / _zoom);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            var line = new Vector2(_scaleSpot.X == 0 ? 0 : from.X, _scaleSpot.Y == 0 ? 0 : from.Y);
            return new Vector2(line.Length() < near ? 1 : Vector2.Dot(to, line) / line.LengthSquared());
        }

        float Ratio(float start, float now) => MathF.Abs(start) < near ? 1 : now / start;
        return new Vector2(_scaleSpot.X == 0 ? 1 : Ratio(from.X, to.X), _scaleSpot.Y == 0 ? 1 : Ratio(from.Y, to.Y));
    }

    // The angle of a point around the turn's pivot, in degrees counterclockwise (the world is Y up).
    private double AngleAround(Vector2 pivot, Point screen)
    {
        var offset = ToWorld(screen) - pivot;
        return Math.Atan2(offset.Y, offset.X) * 180 / Math.PI;
    }

    // ----- Tile palette -----

    /// <summary>
    /// Where the palette's parts go: a tab for each of the map's tilesets (and a + to add one) along the top, the tiles
    /// of the one <see cref="Shown"/> under them in rows (scrolled down <see cref="Scroll"/> rows when they don't all
    /// fit), the tools below, and a button for each of the map's layers (and + Layer) under those.
    /// </summary>
    private sealed record PaletteLayout(Tileset Shown, int Count, int Scroll, int VisibleRows, Rect Panel, double CellsTop,
        IReadOnlyList<(Rect Box, Tileset Tileset)> Tabs, Rect AddTab, IReadOnlyList<(Rect Box, Tilemap Layer)> LayerButtons, Rect AddLayer)
    {
        public int Rows => (Count + PaletteColumns - 1) / PaletteColumns;

        /// <summary>The box of the shown tileset's tile <paramref name="tile"/>, or null when it isn't one of them or is scrolled out of sight.</summary>
        public Rect? CellBounds(int tile)
        {
            var index = tile - Shown.First;
            var row = index / PaletteColumns - Scroll;
            if (index < 0 || index >= Count || row < 0 || row >= VisibleRows)
                return null;
            return new Rect(12 + index % PaletteColumns * PaletteCell, CellsTop + row * PaletteCell, PaletteCell, PaletteCell);
        }

        public Rect ToolBounds(TileTool tool) => new(8 + (int)tool * (ToolButtonWidth + 4), Panel.Bottom + 4, ToolButtonWidth, 22);
    }

    private const double TabHeight = 20;
    private const string AddLayerLabel = "+ Layer";

    private int _paletteScroll;
    private int? _paletteScrollOf;

    private PaletteLayout Palette(Tilemap tilemap)
    {
        var shown = tilemap.TilesetOf(_model.BrushTile);
        var count = shown.Image is null ? 1 : Math.Clamp(shown.Count, 1, Tilemap.MaxTiles - shown.First);

        var tabs = new List<(Rect, Tileset)>();
        var x = 12.0;
        foreach (var tileset in tilemap.Tilesets())
        {
            var width = Text(tileset.Name, "Brush.Text.Primary").Width + 14;
            tabs.Add((new Rect(x, 12, width, TabHeight), tileset));
            x += width + 2;
        }

        var add = new Rect(x, 12, TabHeight + 4, TabHeight);
        var cellsTop = 12 + TabHeight + 4;

        // As many rows as fit above the tools, layers and hint; the rest scroll with the mouse wheel.
        var rows = (count + PaletteColumns - 1) / PaletteColumns;
        var visible = Math.Min(rows, Math.Max(2, (int)((ActualHeight - cellsTop - 90) / PaletteCell)));
        if (_paletteScrollOf != shown.First)
            (_paletteScroll, _paletteScrollOf) = ((_model.BrushTile - shown.First) / PaletteColumns - visible / 2, shown.First);
        _paletteScroll = Math.Clamp(_paletteScroll, 0, rows - visible);

        var panelWidth = Math.Max(Math.Min(count, PaletteColumns) * PaletteCell, add.Right - 12) + 8;
        var panel = new Rect(8, 8, panelWidth, cellsTop - 8 + visible * PaletteCell + 4);

        var layerButtons = new List<(Rect, Tilemap)>();
        var (layerX, layerY) = (8.0, panel.Bottom + 4 + 22 + 4);
        foreach (var layer in tilemap.Layers())
        {
            var width = Text(layer.Entity!.Name, "Brush.Text.Primary").Width + 14;
            layerButtons.Add((new Rect(layerX, layerY, width, 22), layer));
            layerX += width + 4;
        }

        var addLayer = new Rect(layerX, layerY, Text(AddLayerLabel, "Brush.Text.Primary").Width + 14, 22);
        return new PaletteLayout(shown, count, _paletteScroll, visible, panel, cellsTop, tabs, add, layerButtons, addLayer);
    }

    // Scrolls the palette to show the brush's tile, when it's from the tileset shown.
    private void ScrollPaletteTo(Tilemap tilemap, int tile)
    {
        var palette = Palette(tilemap);
        var row = (tile - palette.Shown.First) / PaletteColumns;
        if (row < palette.Scroll)
            _paletteScroll = row;
        else if (row >= palette.Scroll + palette.VisibleRows)
            _paletteScroll = row - palette.VisibleRows + 1;
    }

    private int? PaletteTileAt(Tilemap tilemap, Point screen)
    {
        var palette = Palette(tilemap);
        for (var tile = palette.Shown.First; tile < palette.Shown.First + palette.Count; tile++)
        {
            if (palette.CellBounds(tile) is { } box && box.Contains(screen))
                return tile;
        }

        return palette.Panel.Contains(screen) ? _model.BrushTile : null;
    }

    private TileTool? ToolButtonAt(Tilemap tilemap, Point screen)
    {
        var palette = Palette(tilemap);
        return Enum.GetValues<TileTool>().Cast<TileTool?>().FirstOrDefault(t => palette.ToolBounds(t!.Value).Contains(screen));
    }

    // Which layer button is at a point: its place in the map's layers, the count of them for + Layer, or null.
    private int? LayerButtonAt(Tilemap tilemap, Point screen)
    {
        var palette = Palette(tilemap);
        for (var i = 0; i < palette.LayerButtons.Count; i++)
        {
            if (palette.LayerButtons[i].Box.Contains(screen))
                return i;
        }

        return palette.AddLayer.Contains(screen) ? palette.LayerButtons.Count : null;
    }

    // The + tab lists the project's images to add as a tileset, as Art > Add Tileset to Tilemap does.
    private void ShowAddTilesetMenu(Rect tab)
    {
        var command = _services.GetRequiredService<CommandRegistry>().Get("Art.AddTileset");
        var menu = new ContextMenu { PlacementTarget = this, Placement = System.Windows.Controls.Primitives.PlacementMode.Relative, HorizontalOffset = tab.Left, VerticalOffset = tab.Bottom };
        var images = command.Choices ?? [];
        foreach (var image in images)
        {
            var item = new MenuItem { Header = image.Replace("_", "__") };
            item.Click += async (_, _) => await command.ChooseAsync(image);
            menu.Items.Add(item);
        }

        if (images.Count == 0)
            menu.Items.Add(new MenuItem { Header = "No images in Assets yet: paint one with Art > New Tileset", IsEnabled = false });
        menu.IsOpen = true;
    }

    private static char ToolKey(TileTool tool) => tool switch
    {
        TileTool.Rectangle => 'R',
        TileTool.Fill => 'G',
        _ => 'B',
    };

    /// <summary>
    /// The screen box of a part of the tile painting overlay, for demos to point at: a tool's button ("Rectangle"), a
    /// tile of the tileset the palette shows by its number ("3", "104"), a tileset's tab by its name ("props"), the
    /// + tab that adds one ("+"), a layer's button by its entity's name ("layer:Level Layer 2"), or the button that adds
    /// one ("layer:+"). Null when nothing is being painted (or the tile is scrolled out of sight).
    /// </summary>
    internal Rect? PaintOverlayBounds(string part)
    {
        if (PaintTarget is not { } tilemap)
            return null;
        var palette = Palette(tilemap);
        // By name only: Enum.TryParse would read "1" as the second tool.
        if (Enum.GetValues<TileTool>().Where(t => t.ToString().Equals(part, StringComparison.OrdinalIgnoreCase)).Cast<TileTool?>().FirstOrDefault() is { } tool)
            return palette.ToolBounds(tool);
        if (part.Trim() == "+")
            return palette.AddTab;
        if (part.Trim().StartsWith("layer:", StringComparison.OrdinalIgnoreCase))
        {
            var name = part.Trim()["layer:".Length..].Trim();
            return name == "+"
                ? palette.AddLayer
                : palette.LayerButtons.Where(l => l.Layer.Entity!.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(l => (Rect?)l.Box).FirstOrDefault();
        }

        if (int.TryParse(part, out var tile))
            return palette.CellBounds(tile);
        return palette.Tabs.Where(t => t.Tileset.Name.Equals(part.Trim(), StringComparison.OrdinalIgnoreCase)).Select(t => (Rect?)t.Box).FirstOrDefault();
    }

    // ----- Input -----

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        // Over the tile palette, the wheel scrolls its tiles.
        if (PaintTarget is { } tilemap && Palette(tilemap) is var palette && palette.Panel.Contains(e.GetPosition(this)))
        {
            _paletteScroll = Math.Clamp(_paletteScroll - Math.Sign(e.Delta), 0, Math.Max(0, palette.Rows - palette.VisibleRows));
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        var anchor = e.GetPosition(this);
        var before = ToWorld(anchor);
        _zoom = Math.Clamp(_zoom * Math.Pow(1.0015, e.Delta), MinZoom, MaxZoom);
        var after = ToWorld(anchor);
        _pan += before - after;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        if (_document is null)
            return;

        var position = e.GetPosition(this);
        var wantsPan = e.ChangedButton is MouseButton.Middle or MouseButton.Right ||
                       (e.ChangedButton == MouseButton.Left && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));

        if (wantsPan)
        {
            BeginDrag(DragMode.Pan, position);
        }
        else if (e.ChangedButton == MouseButton.Left && PaintTarget is { } tilemap)
        {
            BeginPaint(tilemap, position);
        }
        else if (e.ChangedButton == MouseButton.Left && IsOverRotateHandle(position) && _model.BeginRotate() is { } rotate)
        {
            _rotate = rotate;
            _rotateLast = AngleAround(rotate.Pivot, position);
            _rotateTotal = 0;
            BeginDrag(DragMode.Rotate, position);
        }
        else if (e.ChangedButton == MouseButton.Left && PathHandleAt(position) is { } pathHandle)
        {
            BeginPathDrag(pathHandle, position, e.ClickCount);
        }
        else if (e.ChangedButton == MouseButton.Left && DistanceHandleAt(position) is { } knob && _model.BeginDistance(knob) is { } distance)
        {
            // Grabbing the knob a little off its circle doesn't make the distance jump.
            _distance = distance;
            _distanceGrab = knob.Value - Vector2.Distance(knob.Center, ToWorld(position));
            BeginDrag(DragMode.Distance, position);
            Cursor = DistanceCursor(knob, position);
        }
        else if (e.ChangedButton == MouseButton.Left && ResizeHandleAt(position) is var (shape, resizeSpot) && _model.BeginResize(shape) is { } resize)
        {
            _resize = resize;
            _resizeSpot = resizeSpot;
            _resizePress = ToWorld(position);
            BeginDrag(DragMode.Resize, position);
            Cursor = ResizeCursor(shape, resizeSpot);
        }
        else if (e.ChangedButton == MouseButton.Left && ScaleHandleAt(position) is { } spot && _model.BeginScale() is { } scale)
        {
            _scale = scale;
            _scaleSpot = spot;
            _scalePress = ToWorld(position);
            BeginDrag(DragMode.Scale, position);
            Cursor = ScaleCursor(spot);
        }
        else if (e.ChangedButton == MouseButton.Left)
        {
            var hit = HitTest(position);
            var adds = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if (hit is null)
            {
                // Empty space: a click selects nothing (Ctrl or Shift keep the selection), a drag draws a box.
                if (!adds)
                    _selection.Select(null);
                _boxAdds = adds;
                _boxEnd = position;
                BeginDrag(DragMode.Box, position);
            }
            else
            {
                if (adds)
                    _selection.Toggle(hit);
                else if (!_selection.IsSelected(hit))
                    _selection.Select(hit);
                else if (_selection.Entities.Count > 1)
                    _pressed = hit;

                if (_selection.IsSelected(hit) && _model.BeginMove() is { } move)
                {
                    _move = move;
                    BeginDrag(DragMode.Move, position);
                }
            }
        }

        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var position = e.GetPosition(this);
        if (PaintTarget is { } tilemap && _drag is DragMode.None or DragMode.Paint)
        {
            UpdateHoverCell(tilemap, position);
            if (_stroke is not null)
                PaintTo(tilemap, position);
            return;
        }

        if (_drag == DragMode.None)
        {
            var over = IsOverRotateHandle(position);
            var overScale = over ? null : ScaleHandleAt(position);
            var overResize = over ? null : ResizeHandleAt(position);
            var overPath = over ? null : PathHandleAt(position);
            var overDistance = over || overPath is not null ? null : DistanceHandleAt(position);
            if (over != _overHandle || overScale != _overScale || overResize != _overResize || overDistance?.Key != _overDistance ||
                overPath?.Key != _overPath)
            {
                _overHandle = over;
                _overScale = overScale;
                _overResize = overResize;
                _overDistance = overDistance?.Key;
                _overPath = overPath?.Key;
                Cursor = over ? Cursors.Hand
                    : overPath is not null ? PathCursor(overPath)
                    : overDistance is not null ? DistanceCursor(overDistance, position)
                    : overScale is { } spot ? ScaleCursor(spot)
                    : overResize is var (shape, resizeSpot) ? ResizeCursor(shape, resizeSpot)
                    : null;
                InvalidateVisual();
            }

            return;
        }

        if (_drag == DragMode.Scale)
        {
            _scale?.To(ScaleFactor(_scale, ToWorld(position)), snap: Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
            return;
        }

        if (_drag == DragMode.Path)
        {
            _path?.To(ToWorld(position) + _pathGrab, snap: Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
            return;
        }

        if (_drag == DragMode.Distance)
        {
            if (_distance is not null)
                _distance.To(Vector2.Distance(_distance.Distance.Center, ToWorld(position)) + _distanceGrab, snap: Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
            return;
        }

        if (_drag == DragMode.Resize)
        {
            // The handle follows the cursor from where it was pressed, so grabbing it a little off doesn't jump.
            if (_resize?.Frame is { } frame)
                _resize.Drag(_resizeSpot, frame.HandleAt(_resizeSpot) + frame.ToLocal(ToWorld(position)) - frame.ToLocal(_resizePress),
                    proportional: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), snap: Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
            return;
        }

        if (_drag == DragMode.Rotate)
        {
            // Add up the small turns between mouse moves, so going round more than once keeps counting.
            if (_rotate is null || (ToWorld(position) - _rotate.Pivot).Length() < 1e-6f)
                return;
            var angle = AngleAround(_rotate.Pivot, position);
            _rotateTotal += Math.IEEERemainder(angle - _rotateLast, 360);
            _rotateLast = angle;
            _rotate.To((float)_rotateTotal, snap: Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
            return;
        }

        if (_drag == DragMode.Pan)
        {
            var delta = position - _dragStartScreen;
            _pan = _dragStartPan - new Vector2((float)(delta.X / _zoom), (float)(-delta.Y / _zoom));
            InvalidateVisual();
            return;
        }

        if (_drag == DragMode.Box)
        {
            _boxEnd = position;
            InvalidateVisual();
            return;
        }

        if (_move is null)
            return;
        if ((position - _dragStartScreen).Length >= DragThreshold)
            _pressed = null;
        _move.To(ToWorld(position) - ToWorld(_dragStartScreen), snap: Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        var position = e.GetPosition(this);
        if (_drag == DragMode.Box && (position - _dragStartScreen).Length >= DragThreshold)
            _model.SelectInBox(ToWorld(_dragStartScreen), ToWorld(position), _boxAdds);
        else if (_drag == DragMode.Move && _pressed is { } pressed)
            _selection.Select(pressed);
        else if (_drag == DragMode.Paint && _rectangleFrom is var (fromX, fromY) && PaintTarget is { } tilemap && CellUnder(tilemap, position) is var (toX, toY))
            _model.PaintRectangle(fromX, fromY, toX, toY, _rectangleErases);

        if (_drag != DragMode.None)
            EndDrag();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (_drag != DragMode.None)
            EndDrag();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_hoverCell is not null && _drag != DragMode.Paint)
        {
            _hoverCell = null;
            InvalidateVisual();
        }
    }

    // ----- Tile painting -----

    // Click the palette to pick a tile or a tool, Ctrl+click the map to pick the tile under the cursor, otherwise use the
    // tool (erasing with Shift): drag the brush, drag out a rectangle, or click to fill. Each is one undo step.
    private void BeginPaint(Tilemap tilemap, Point position)
    {
        if (ToolButtonAt(tilemap, position) is { } tool)
        {
            _model.TileTool = tool;
            return;
        }

        if (LayerButtonAt(tilemap, position) is { } layer)
        {
            if (layer < tilemap.Layers().Count)
                _model.ShowLayer(layer);
            else
                _model.AddLayer();
            return;
        }

        var palette = Palette(tilemap);
        if (palette.AddTab.Contains(position))
        {
            ShowAddTilesetMenu(palette.AddTab);
            return;
        }

        for (var i = 0; i < palette.Tabs.Count; i++)
        {
            if (palette.Tabs[i].Box.Contains(position))
            {
                _model.ShowTileset(i);
                return;
            }
        }

        if (PaletteTileAt(tilemap, position) is { } chosen)
        {
            _model.BrushTile = chosen;
            return;
        }

        if (CellUnder(tilemap, position) is not var (x, y))
            return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _model.PickTile(x, y);
            return;
        }

        var erase = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (_model.TileTool)
        {
            case TileTool.Fill:
                _model.FillTiles(x, y, erase);
                InvalidateVisual();
                return;
            case TileTool.Rectangle:
                (_rectangleFrom, _rectangleErases, _hoverCell) = ((x, y), erase, (x, y));
                BeginDrag(DragMode.Paint, position);
                InvalidateVisual();
                return;
            default:
                BeginDrag(DragMode.Paint, position);
                _stroke = _model.BeginTileStroke(erase);
                PaintTo(tilemap, position);
                return;
        }
    }

    // Paints every cell on the line from the previous point, so fast drags don't leave gaps.
    private void PaintTo(Tilemap tilemap, Point position)
    {
        if (_stroke is null || CellUnder(tilemap, position) is not var (x, y))
            return;
        _stroke.To(x, y);
        _hoverCell = _stroke.Last;
        InvalidateVisual();
    }

    private (int X, int Y)? CellUnder(Tilemap tilemap, Point position)
    {
        if (!Matrix4x4.Invert(tilemap.Entity!.Transform.WorldMatrix, out var inverse))
            return null;
        var local = Vector3.Transform(new Vector3(ToWorld(position), 0), inverse);
        return tilemap.CellAt(new Vector2(local.X, local.Y));
    }

    private void UpdateHoverCell(Tilemap tilemap, Point position)
    {
        var overPalette = _drag == DragMode.None &&
            (PaletteTileAt(tilemap, position) is not null || ToolButtonAt(tilemap, position) is not null || LayerButtonAt(tilemap, position) is not null);
        var cell = overPalette ? null : CellUnder(tilemap, position);
        if (cell != _hoverCell)
        {
            _hoverCell = cell;
            InvalidateVisual();
        }
    }

    // Dropping an image from the Assets panel creates a sprite where it lands, sized to the image's aspect;
    // dropping a prefab places a copy of it there.
    protected override void OnDragOver(DragEventArgs e)
    {
        e.Effects = _document is { } document && DroppedAsset(e) is var (assetPath, _) && !PrefabFiles.IsOwnTab(_workspace, document, assetPath)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    // In a prefab's tab, what's dropped goes inside the prefab, so where it lands is turned into a place inside it.
    protected override void OnDrop(DragEventArgs e)
    {
        e.Handled = true;
        if (_document is not { } document || DroppedAsset(e) is not var (assetPath, kind) || PrefabFiles.IsOwnTab(_workspace, document, assetPath))
            return;

        var parent = document.ParentFor(null);
        var position = ToWorld(e.GetPosition(this));
        if (parent is not null && Matrix4x4.Invert(parent.Transform.WorldMatrix, out var toParent))
            position = ToVector2(Vector3.Transform(new Vector3(position, 0), toParent));
        Entity entity;
        if (kind == Griff.Editor.Features.Assets.AssetKind.Prefab)
        {
            if (PrefabFiles.Instantiate(_workspace, assetPath) is not { } instance)
                return;
            entity = instance;
            entity.Transform.Position = new Vector3(position, entity.Transform.Position.Z);
        }
        else
        {
            var image = LoadImage(assetPath);
            var aspect = image is { PixelHeight: > 0 } ? (float)image.PixelWidth / image.PixelHeight : 1f;
            entity = new Entity(Path.GetFileNameWithoutExtension(assetPath));
            entity.Transform.Position = new Vector3(position, 0);
            var sprite = entity.AddComponent<Sprite>();
            sprite.Image = assetPath;
            sprite.Size = new Vector2(aspect, 1);
        }

        document.Execute(new AddEntityEdit(document.Scene, entity, parent));
        _selection.Select(entity);
        Focus();
    }

    private static (string Path, Griff.Editor.Features.Assets.AssetKind Kind)? DroppedAsset(DragEventArgs e)
    {
        if (e.Data.GetData(Griff.Editor.Features.Assets.AssetNode.DataFormat) is not string path)
            return null;
        var kind = Griff.Editor.Features.Assets.AssetNode.KindOf(path);
        return kind is Griff.Editor.Features.Assets.AssetKind.Image or Griff.Editor.Features.Assets.AssetKind.Prefab ? (path, kind) : null;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.None)
        {
            FrameSelection();
            e.Handled = true;
            return;
        }

        // While painting a tilemap (which hides the handles), B, R and G pick the brush, rectangle and fill, as in the
        // Sprite Painter.
        if (e.Key is Key.B or Key.R or Key.G && Keyboard.Modifiers == ModifierKeys.None && _drag == DragMode.None && PaintTarget is not null)
        {
            _model.TileTool = e.Key switch
            {
                Key.R => TileTool.Rectangle,
                Key.G => TileTool.Fill,
                _ => TileTool.Brush,
            };
            e.Handled = true;
            return;
        }

        // H hides the selection here (or shows it again), as Entity > Hide does.
        if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.None && _drag == DragMode.None)
        {
            var hide = _services.GetRequiredService<CommandRegistry>().Get("Entity.Hide");
            if (hide.CanExecute())
                _ = hide.ExecuteAsync();
            e.Handled = true;
            return;
        }

        // S, R, C and D pick what the handles do: scale, resize the sprite, resize the colliders, or change distances.
        if (e.Key is Key.S or Key.R or Key.C or Key.D && Keyboard.Modifiers == ModifierKeys.None && _drag == DragMode.None)
        {
            _model.Handles = e.Key switch
            {
                Key.R => SceneHandles.Resize,
                Key.C => SceneHandles.Collider,
                Key.D => SceneHandles.Distance,
                _ => SceneHandles.Scale,
            };
            e.Handled = true;
            return;
        }

        if (PaintTarget is { } tilemap)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    // Esc lets go of a rectangle being dragged; otherwise it stops painting.
                    if (_rectangleFrom is not null)
                        EndDrag();
                    else
                        _selection.Select(null);
                    e.Handled = true;
                    return;
                case Key.OemOpenBrackets or Key.OemCloseBrackets:
                    // Steps through the tiles of the tileset the palette shows.
                    var palette = Palette(tilemap);
                    var (first, count) = (palette.Shown.First, palette.Count);
                    _model.BrushTile = first + ((_model.BrushTile - first + (e.Key == Key.OemCloseBrackets ? 1 : -1)) % count + count) % count;
                    ScrollPaletteTo(tilemap, _model.BrushTile);
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                case Key.LeftShift or Key.RightShift:
                    InvalidateVisual(); // the hover outline turns red for erasing
                    break;
            }
        }

        // Arrows nudge everything selected: half a unit, or a tenth with Shift. Each press is its own undo step.
        var direction = e.Key switch
        {
            Key.Left => new Vector3(-1, 0, 0),
            Key.Right => new Vector3(1, 0, 0),
            Key.Up => new Vector3(0, 1, 0),
            Key.Down => new Vector3(0, -1, 0),
            _ => Vector3.Zero,
        };
        if (direction != Vector3.Zero && _selection.Entity is not null && (Keyboard.Modifiers & ~ModifierKeys.Shift) == ModifierKeys.None)
        {
            var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.1f : SnapStep;
            _model.MoveSelection(new Vector2(direction.X, direction.Y) * step);
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key is Key.LeftShift or Key.RightShift && PaintTarget is not null)
            InvalidateVisual();
    }

    // A press on a path handle: a double-click takes its point off (a patrol's turn clears); otherwise it drags the
    // point, or for a plus, a new point added there.
    private void BeginPathDrag(PathHandle handle, Point position, int clicks)
    {
        if (handle.Point is { } point && clicks == 2)
        {
            try
            {
                _model.RemovePathPoint(point.Key);
            }
            catch (InvalidOperationException)
            {
                // A mover's last point stays: it needs somewhere to go.
            }

            _overPath = null;
            InvalidateVisual();
            return;
        }

        var drag = handle.Point is { } existing ? _model.BeginPathPoint(existing)
            : handle.Mover is { } mover ? _model.BeginInsertPathPoint(mover, handle.Index)
            : null;
        if (drag is null)
            return;

        // The handle follows the cursor from where it was pressed, so grabbing it a little off doesn't jump. A plus adds
        // its point straight away, so a click adds one there.
        BeginDrag(DragMode.Path, position);
        (_path, _pathKey) = (drag, handle.Point?.Key ?? $"{nameof(Mover)}.{handle.Index}");
        _pathGrab = ToWorld(handle.Screen) - ToWorld(position);
        if (handle.Point is null)
            drag.To(ToWorld(handle.Screen), snap: false);
        Cursor = PathCursor(handle);
    }

    private void BeginDrag(DragMode mode, Point position)
    {
        _drag = mode;
        _dragStartScreen = position;
        _dragStartPan = _pan;
        _document?.History.Seal();
        CaptureMouse();
        if (mode == DragMode.Pan)
            Cursor = Cursors.SizeAll;
    }

    private void EndDrag()
    {
        var mode = _drag;
        _drag = DragMode.None;
        Cursor = null;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        if (mode == DragMode.Move)
        {
            _move?.End();
            _move = null;
            _pressed = null;
        }

        if (mode == DragMode.Rotate)
        {
            _rotate?.End();
            _rotate = null;
            _overHandle = false;
            InvalidateVisual();
        }

        if (mode == DragMode.Scale)
        {
            _scale?.End();
            _scale = null;
            _overScale = null;
            InvalidateVisual();
        }

        if (mode == DragMode.Resize)
        {
            _resize?.End();
            _resize = null;
            _overResize = null;
            InvalidateVisual();
        }

        if (mode == DragMode.Path)
        {
            _path?.End();
            (_path, _pathKey, _overPath) = (null, null, null);
            InvalidateVisual();
        }

        if (mode == DragMode.Distance)
        {
            _distance?.End();
            _distance = null;
            _overDistance = null;
            InvalidateVisual();
        }

        if (mode == DragMode.Box)
            InvalidateVisual();
        if (mode == DragMode.Paint)
        {
            _stroke?.End();
            _stroke = null;
            _rectangleFrom = null;
            InvalidateVisual();
        }
    }

    // ----- Helpers -----

    private BitmapSource? LoadImage(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || _workspace.Project is not { } project)
            return null;

        var path = Path.Combine(project.AssetsPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (_images.TryGetValue(path, out var cached))
            return cached;

        BitmapSource? image = null;
        try
        {
            if (File.Exists(path))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();
                bitmap.Freeze();
                image = bitmap;
            }
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            image = null;
        }

        _images[path] = image;
        return image;
    }

    /// <summary>
    /// <paramref name="image"/> multiplied by the tint's color, as the game draws it; alpha is left to the caller. Tints are
    /// rounded (see <see cref="ImageTint.Quantize"/>) and the cache is emptied when it grows large, so animated colors
    /// like particles fading through a gradient don't pile up images.
    /// </summary>
    private BitmapSource Tinted(BitmapSource image, Griff.Color tint)
    {
        if (ImageTint.IsIdentity(tint))
            return image;

        var key = (image, ImageTint.Quantize(tint));
        if (_tintedImages.TryGetValue(key, out var cached))
            return cached;
        if (_tintedImages.Count >= 256)
            _tintedImages.Clear();

        var source = image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var stride = source.PixelWidth * 4;
        var pixels = new byte[stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);
        ImageTint.ApplyBgra(pixels, key.Item2);

        var tinted = BitmapSource.Create(source.PixelWidth, source.PixelHeight, image.DpiX, image.DpiY, PixelFormats.Bgra32, null, pixels, stride);
        tinted.Freeze();
        _tintedImages[key] = tinted;
        return tinted;
    }

    /// <summary>One tile of a tilemap, cut out of its tileset's image for drawing; null when that tileset has no (loadable) image.</summary>
    private BitmapSource? TileImage(Tilemap tilemap, int tile)
    {
        var tileset = tilemap.TilesetOf(tile);
        if (LoadImage(tileset.Image) is not { } image)
            return null;

        var key = (tileset.Image!, tile - tileset.First, tileset.Columns, tileset.Rows);
        if (_tileImages.TryGetValue(key, out var cached))
            return cached;

        var rect = tileset.Rect(tile);
        var area = new Int32Rect(
            (int)Math.Round(rect.X * image.PixelWidth), (int)Math.Round(rect.Y * image.PixelHeight),
            Math.Max(1, (int)Math.Round(rect.Z * image.PixelWidth)), Math.Max(1, (int)Math.Round(rect.W * image.PixelHeight)));
        BitmapSource? cropped = null;
        if (area.X + area.Width <= image.PixelWidth && area.Y + area.Height <= image.PixelHeight)
        {
            cropped = new CroppedBitmap(image, area);
            cropped.Freeze();
        }

        _tileImages[key] = cropped;
        return cropped;
    }

    private static Vector2 ToVector2(Vector3 v) => new(v.X, v.Y);

    private Brush Brush(string key) => (Brush)FindResource(key);

    private Brush Faded(string key, double opacity)
    {
        var brush = Brush(key).Clone();
        brush.Opacity = opacity;
        brush.Freeze();
        return brush;
    }

    private FormattedText Text(string text, string brushKey) => new(
        text,
        CultureInfo.CurrentUICulture,
        FlowDirection.LeftToRight,
        new Typeface((FontFamily)FindResource("Font.UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
        (double)FindResource("FontSize.Caption"),
        Brush(brushKey),
        VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
