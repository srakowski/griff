using System.Globalization;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Griff.Editing;
using Griff.Editor.Core.Workspace;
using Griff.Rendering;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Point = System.Windows.Point;
using Transform = Griff.Scenes.Transform;

namespace Griff.Editor.Features.SceneView;

/// <summary>
/// 2D view of the open scene. Y is up, one world unit is <see cref="_zoom"/> pixels.
/// Wheel zooms at the cursor; middle/right/Alt-drag pans; click selects; drag moves (Ctrl snaps); F frames.
/// </summary>
public sealed class SceneViewControl : FrameworkElement
{
    private const double MinZoom = 4;
    private const double MaxZoom = 2048;
    private const float SnapStep = 0.5f;
    private const double MarkerRadius = 7;
    private const double CameraAspect = 16.0 / 9.0;

    private readonly IWorkspace _workspace;
    private readonly ISelection _selection;
    private readonly Dictionary<string, BitmapSource?> _images = new(StringComparer.OrdinalIgnoreCase);
    private SceneDocument? _document;

    private Vector2 _pan;
    private double _zoom = 64;
    private bool _needsFit = true;

    private DragMode _drag;
    private Point _dragStartScreen;
    private Vector2 _dragStartPan;
    private Vector3 _dragStartPosition;
    private Matrix4x4 _dragParentInverse = Matrix4x4.Identity;

    public SceneViewControl(IWorkspace workspace, ISelection selection)
    {
        _workspace = workspace;
        _selection = selection;
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);

        workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IWorkspace.Scene))
                Track(workspace.Scene);
        };
        selection.PropertyChanged += (_, _) => InvalidateVisual();
        workspace.ScriptsReloaded += (_, _) => InvalidateVisual();
        Track(workspace.Scene);
    }

    private enum DragMode
    {
        None,
        Pan,
        Move,
    }

    private PropertyDescriptor PositionProperty =>
        _workspace.Components.Get(typeof(Transform)).Properties.Single(p => p.Name == nameof(Transform.Position));

    /// <summary>Centers the view on the selection (or the origin).</summary>
    public void FrameSelection()
    {
        var position = _selection.Entity?.Transform.WorldPosition ?? Vector3.Zero;
        _pan = new Vector2(position.X, position.Y);
        InvalidateVisual();
    }

    private void Track(SceneDocument? document)
    {
        if (_document is not null)
        {
            _document.Changed -= OnDocumentChanged;
            _document.RuntimeTicked -= OnRuntimeTicked;
        }

        // Switching to or from the play-mode copy keeps the camera where the user put it.
        var samePlace = _document is not null && document is not null && (_document.IsRuntime || document.IsRuntime);
        _document = document;
        if (_document is not null)
        {
            _document.Changed += OnDocumentChanged;
            _document.RuntimeTicked += OnRuntimeTicked;
        }

        if (!samePlace)
        {
            _images.Clear();
            _needsFit = true;
        }

        InvalidateVisual();
    }

    /// <summary>Shows the whole frame of the first camera, so a scene opens looking at what the player sees.</summary>
    private void FitToCamera()
    {
        _needsFit = false;
        _pan = Vector2.Zero;
        _zoom = 64;

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

        foreach (var entity in DrawOrder())
        {
            if (entity.GetComponent<Sprite>() is { } sprite)
                DrawSprite(dc, entity, sprite);
            if (entity.GetComponent<Camera>() is { } camera)
                DrawCameraFrame(dc, entity, camera, entity == _selection.Entity);
            if (entity.GetComponent<Sprite>() is null && entity.GetComponent<Camera>() is null)
                DrawMarker(dc, entity);
        }

        if (_selection.Entity is { } selected && selected.Scene == _document.Scene)
            DrawSelection(dc, selected);

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

        // WPF can't multiply-tint images; the editor view shows images untinted (alpha still applies).
        if (LoadImage(sprite.Image) is { } image)
        {
            dc.DrawImage(image, rect);
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

    private void DrawMarker(DrawingContext dc, Entity entity)
    {
        var center = ToScreen(ToVector2(entity.Transform.WorldPosition));
        var pen = new Pen(entity.IsEnabled ? Brush("Brush.Text.Secondary") : Brush("Brush.Text.Disabled"), 1.5);
        dc.DrawEllipse(null, pen, center, MarkerRadius, MarkerRadius);
        dc.DrawLine(pen, new Point(center.X - 3, center.Y), new Point(center.X + 3, center.Y));
        dc.DrawLine(pen, new Point(center.X, center.Y - 3), new Point(center.X, center.Y + 3));
    }

    private void DrawSelection(DrawingContext dc, Entity entity)
    {
        var accent = Brush("Brush.Accent");
        if (Bounds(entity) is { } bounds)
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

        // Pivot handle with axis arms.
        var pivot = ToScreen(ToVector2(entity.Transform.WorldPosition));
        dc.DrawLine(new Pen(Brush("Brush.Axis.X"), 2), pivot, new Point(pivot.X + 28, pivot.Y));
        dc.DrawLine(new Pen(Brush("Brush.Axis.Y"), 2), pivot, new Point(pivot.X, pivot.Y - 28));
        dc.DrawEllipse(accent, null, pivot, 3.5, 3.5);
    }

    private void DrawZoomLabel(DrawingContext dc)
    {
        var text = Text($"{_zoom / 64:P0}", "Brush.Text.Tertiary");
        dc.DrawText(text, new Point(ActualWidth - text.Width - 10, ActualHeight - text.Height - 8));
    }

    // ----- Scene queries -----

    /// <summary>Everything drawn, back to front: sprites in render order, then cameras and bare entities on top.</summary>
    private IEnumerable<Entity> DrawOrder()
    {
        var scene = _document!.Scene;
        return SceneRenderer.DrawOrder(scene)
            .Concat(scene.Entities.Where(e => e.GetComponent<Sprite>() is null && SceneRenderer.IsActive(e)));
    }

    /// <summary>World-space outline of the entity's sprite or camera frame, or null for bare entities.</summary>
    private static Vector2[]? Bounds(Entity entity)
    {
        Vector2 half;
        if (entity.GetComponent<Sprite>() is { } sprite)
            half = sprite.Size / 2;
        else if (entity.GetComponent<Camera>() is { } camera)
            half = new Vector2((float)(camera.Size * CameraAspect), camera.Size);
        else
            return null;

        var world = entity.Transform.WorldMatrix;
        return
        [
            ToVector2(Vector3.Transform(new Vector3(-half.X, -half.Y, 0), world)),
            ToVector2(Vector3.Transform(new Vector3(half.X, -half.Y, 0), world)),
            ToVector2(Vector3.Transform(new Vector3(half.X, half.Y, 0), world)),
            ToVector2(Vector3.Transform(new Vector3(-half.X, half.Y, 0), world)),
        ];
    }

    /// <summary>Topmost entity under the point. Sprites hit by area; bare entities and cameras by their pivot.</summary>
    private Entity? HitTest(Point screen)
    {
        if (_document is null)
            return null;

        var world = ToWorld(screen);
        foreach (var entity in DrawOrder().Reverse())
        {
            var pivot = ToScreen(ToVector2(entity.Transform.WorldPosition));
            if ((pivot - screen).Length <= MarkerRadius + 3)
                return entity;

            if (entity.GetComponent<Sprite>() is { } sprite && Matrix4x4.Invert(entity.Transform.WorldMatrix, out var inverse))
            {
                var local = Vector3.Transform(new Vector3(world, 0), inverse);
                if (Math.Abs(local.X) <= sprite.Size.X / 2 && Math.Abs(local.Y) <= sprite.Size.Y / 2)
                    return entity;
            }
        }

        return null;
    }

    // ----- Input -----

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
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
        else if (e.ChangedButton == MouseButton.Left)
        {
            var hit = HitTest(position);
            _selection.Select(hit);
            if (hit is not null)
            {
                _dragStartPosition = hit.Transform.Position;
                _dragParentInverse = hit.Parent is { } parent && Matrix4x4.Invert(parent.Transform.WorldMatrix, out var inv)
                    ? inv
                    : Matrix4x4.Identity;
                BeginDrag(DragMode.Move, position);
            }
        }

        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag == DragMode.None)
            return;

        var position = e.GetPosition(this);
        if (_drag == DragMode.Pan)
        {
            var delta = position - _dragStartScreen;
            _pan = _dragStartPan - new Vector2((float)(delta.X / _zoom), (float)(-delta.Y / _zoom));
            InvalidateVisual();
            return;
        }

        if (_selection.Entity is not { } entity || _document is null)
            return;

        // Convert the world-space drag into the parent's space so nested entities move under the cursor.
        var worldDelta = ToWorld(position) - ToWorld(_dragStartScreen);
        var localDelta = Vector3.TransformNormal(new Vector3(worldDelta, 0), _dragParentInverse);
        var target = _dragStartPosition + localDelta;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            target = new Vector3(Snap(target.X), Snap(target.Y), target.Z);

        if (target != entity.Transform.Position)
            _document.Execute(new SetPropertyEdit(entity.Transform, PositionProperty, target));
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (_drag != DragMode.None)
            EndDrag();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (_drag != DragMode.None)
            EndDrag();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.None)
        {
            FrameSelection();
            e.Handled = true;
        }
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
            _document?.History.Seal();
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

    private static float Snap(float value) => MathF.Round(value / SnapStep) * SnapStep;

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
