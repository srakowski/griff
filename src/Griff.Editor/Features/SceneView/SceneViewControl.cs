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
/// With a tilemap selected, left-drag paints tiles instead (Shift erases, Ctrl picks, [ and ] change tile, Esc stops).
/// </summary>
public sealed class SceneViewControl : FrameworkElement
{
    private const double MinZoom = 4;
    private const double MaxZoom = 2048;
    private const float SnapStep = 0.5f;
    private const double MarkerRadius = 7;
    private const double CameraAspect = 16.0 / 9.0;
    private const double PaletteCell = 28;
    private const int PaletteColumns = 8;

    private readonly IWorkspace _workspace;
    private readonly ISelection _selection;
    private readonly Dictionary<string, BitmapSource?> _images = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Image, int Tile, int Columns, int Rows), BitmapSource?> _tileImages = [];
    private readonly Dictionary<(BitmapSource Image, int Rgb), BitmapSource> _tintedImages = [];
    private SceneDocument? _document;

    private Vector2 _pan;
    private double _zoom = 64;
    private bool _needsFit = true;

    private DragMode _drag;
    private Point _dragStartScreen;
    private Vector2 _dragStartPan;
    private Vector3 _dragStartPosition;
    private Matrix4x4 _dragParentInverse = Matrix4x4.Identity;

    // Tile painting.
    private int _brushTile;
    private bool _erasing;
    private (int X, int Y)? _hoverCell;
    private (int X, int Y)? _lastPaintCell;
    private IDisposable? _stroke;

    public SceneViewControl(IWorkspace workspace, ISelection selection)
    {
        _workspace = workspace;
        _selection = selection;
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
        workspace.ScriptsReloaded += (_, _) => InvalidateVisual();
        Track(workspace.Scene);
    }

    private enum DragMode
    {
        None,
        Pan,
        Move,
        Paint,
    }

    private PropertyDescriptor PositionProperty =>
        _workspace.Components.Get(typeof(Transform)).Properties.Single(p => p.Name == nameof(Transform.Position));

    private PropertyDescriptor TilesProperty =>
        _workspace.Components.Get(typeof(Tilemap)).Properties.Single(p => p.Name == nameof(Tilemap.Tiles));

    /// <summary>The tilemap being painted: the selected entity's, while it's in the shown scene and enabled.</summary>
    private Tilemap? PaintTarget =>
        _selection.Entity is { } entity && entity.Scene == _document?.Scene && SceneRenderer.IsActive(entity)
            ? entity.GetComponent<Tilemap>()
            : null;

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
            _tileImages.Clear();
            _tintedImages.Clear();
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
            if (entity.GetComponent<Tilemap>() is { } tilemap)
                DrawTilemap(dc, entity, tilemap);
            if (entity.GetComponent<Sprite>() is { } sprite)
                DrawSprite(dc, entity, sprite);
            if (entity.GetComponent<Text>() is { Value.Length: > 0 } text)
                DrawText(dc, entity, text);
            if (entity.GetComponent<Camera>() is { } camera)
                DrawCameraFrame(dc, entity, camera, entity == _selection.Entity);
            if (entity.GetComponent<ParticleEmitter>() is { Particles.Count: > 0 } emitter)
                DrawParticles(dc, entity, emitter);

            // Entities with nothing solid to see (including emitters) get a marker so they can be found and clicked.
            if (entity.GetComponent<Sprite>() is null && entity.GetComponent<Tilemap>() is null &&
                entity.GetComponent<Text>() is null && entity.GetComponent<Camera>() is null)
                DrawMarker(dc, entity);
        }

        DrawColliders(dc);

        if (_selection.Entity is { } selected && selected.Scene == _document.Scene)
            DrawSelection(dc, selected);

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
        dc.PushOpacity(tilemap.Tint.A);

        foreach (var (x, y, tile) in tilemap.Cells())
        {
            var center = tilemap.CellCenter(x, y);
            var rect = new Rect(center.X - size / 2 - bleed, center.Y - size / 2 - bleed, size + bleed * 2, size + bleed * 2);
            if (TileImage(tilemap, tile) is { } image)
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

    // Physics shapes in green; triggers dashed.
    private void DrawColliders(DrawingContext dc)
    {
        var solid = new Pen(Brush("Brush.Status.Success"), 1.5);
        var trigger = new Pen(Brush("Brush.Status.Success"), 1.5) { DashStyle = new DashStyle([4, 3], 0) };
        var clickable = new Pen(Brush("Brush.Status.Info"), 1.5) { DashStyle = new DashStyle([2, 2], 0) };
        foreach (var entity in _document!.Scene.Entities.Where(SceneRenderer.IsActive))
        {
            // Buttons: where they can be clicked.
            foreach (var button in entity.GetComponents<Griff.Scenes.Button>())
                DrawBox(Griff.Runtime.Game.AreaOf(button), clickable);

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

    // While painting: the grid's extent, the cell under the cursor, and a palette of the tileset's tiles.
    private void DrawPaintOverlay(DrawingContext dc, Tilemap tilemap)
    {
        var accent = Brush("Brush.Accent");
        var size = tilemap.TileSize;
        dc.PushTransform(new MatrixTransform(ScreenMatrix(tilemap.Entity!.Transform.WorldMatrix)));

        var extent = new Rect(0, -Math.Max(1, tilemap.Height) * size, Math.Max(1, tilemap.Width) * size, Math.Max(1, tilemap.Height) * size);
        dc.DrawRectangle(null, new Pen(Brush("Brush.Text.Tertiary"), 1 / _zoom) { DashStyle = new DashStyle([4, 4], 0) }, extent);

        if (_hoverCell is var (hx, hy))
        {
            var center = tilemap.CellCenter(hx, hy);
            var cell = new Rect(center.X - size / 2, center.Y - size / 2, size, size);
            dc.DrawRectangle(null, new Pen(_erasing || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? Brush("Brush.Status.Error") : accent, 2 / _zoom), cell);
        }

        dc.Pop();

        // Palette, top left.
        var count = PaletteCount(tilemap);
        var panel = PaletteBounds(count);
        dc.DrawRoundedRectangle(Brush("Brush.Surface.Chrome"), new Pen(Brush("Brush.Border.Default"), 1), panel, 4, 4);
        var fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, tilemap.Tint.RByte, tilemap.Tint.GByte, tilemap.Tint.BByte));
        for (var tile = 0; tile < count; tile++)
        {
            var rect = PaletteCellBounds(tile);
            rect.Inflate(-2, -2);
            if (TileImage(tilemap, tile) is { } image)
                dc.DrawImage(image, rect);
            else
                dc.DrawRectangle(fill, null, rect);
            if (tile == _brushTile)
                dc.DrawRectangle(null, new Pen(accent, 2), PaletteCellBounds(tile));
        }

        var hint = Text("Paint: drag · Shift erase · Ctrl pick · [ ] tile · Esc done", "Brush.Text.Secondary");
        dc.DrawText(hint, new Point(panel.Left + 6, panel.Bottom + 4));
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

    /// <summary>Everything drawn, back to front: drawables in render order, then cameras and bare entities on top.</summary>
    private IEnumerable<Entity> DrawOrder()
    {
        var scene = _document!.Scene;
        return SceneRenderer.DrawOrder(scene)
            .Concat(scene.Entities.Where(e => SceneRenderer.OrderOf(e) is null && SceneRenderer.IsActive(e)));
    }

    /// <summary>World-space outline of the entity's sprite, tilemap or camera frame, or null for bare entities.</summary>
    private static Vector2[]? Bounds(Entity entity)
    {
        Vector2 min, max;
        if (entity.GetComponent<Sprite>() is { } sprite)
            (min, max) = (-sprite.Size / 2, sprite.Size / 2);
        else if (entity.GetComponent<Tilemap>() is { Height: > 0 } tilemap)
            (min, max) = (new Vector2(0, -tilemap.Height * tilemap.TileSize), new Vector2(tilemap.Width * tilemap.TileSize, 0));
        else if (entity.GetComponent<Camera>() is { } camera)
        {
            max = new Vector2((float)(camera.Size * CameraAspect), camera.Size);
            min = -max;
        }
        else
            return null;

        var world = entity.Transform.WorldMatrix;
        return
        [
            ToVector2(Vector3.Transform(new Vector3(min.X, min.Y, 0), world)),
            ToVector2(Vector3.Transform(new Vector3(max.X, min.Y, 0), world)),
            ToVector2(Vector3.Transform(new Vector3(max.X, max.Y, 0), world)),
            ToVector2(Vector3.Transform(new Vector3(min.X, max.Y, 0), world)),
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

            if (!Matrix4x4.Invert(entity.Transform.WorldMatrix, out var inverse))
                continue;
            var local = Vector3.Transform(new Vector3(world, 0), inverse);
            if (entity.GetComponent<Sprite>() is { } sprite && Math.Abs(local.X) <= sprite.Size.X / 2 && Math.Abs(local.Y) <= sprite.Size.Y / 2)
                return entity;

            // Tilemaps are hit where there's a tile, so the empty parts don't block clicks on what's behind.
            if (entity.GetComponent<Tilemap>() is { } tilemap && tilemap.CellAt(new Vector2(local.X, local.Y)) is var (x, y) && tilemap.GetTile(x, y) >= 0)
                return entity;
        }

        return null;
    }

    // ----- Tile palette -----

    private static int PaletteCount(Tilemap tilemap) =>
        string.IsNullOrEmpty(tilemap.Image) ? 1 : Math.Clamp(Math.Max(1, tilemap.Columns) * Math.Max(1, tilemap.Rows), 1, Tilemap.MaxTiles);

    private static Rect PaletteCellBounds(int tile) =>
        new(12 + tile % PaletteColumns * PaletteCell, 12 + tile / PaletteColumns * PaletteCell, PaletteCell, PaletteCell);

    private static Rect PaletteBounds(int count)
    {
        var columns = Math.Min(count, PaletteColumns);
        var rows = (count + PaletteColumns - 1) / PaletteColumns;
        return new Rect(8, 8, columns * PaletteCell + 8, rows * PaletteCell + 8);
    }

    private int? PaletteTileAt(Tilemap tilemap, Point screen)
    {
        var count = PaletteCount(tilemap);
        for (var tile = 0; tile < count; tile++)
        {
            if (PaletteCellBounds(tile).Contains(screen))
                return tile;
        }

        return PaletteBounds(count).Contains(screen) ? _brushTile : null;
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
        else if (e.ChangedButton == MouseButton.Left && PaintTarget is { } tilemap)
        {
            BeginPaint(tilemap, position);
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
        var position = e.GetPosition(this);
        if (PaintTarget is { } tilemap && _drag is DragMode.None or DragMode.Paint)
        {
            UpdateHoverCell(tilemap, position);
            if (_drag == DragMode.Paint)
                PaintTo(tilemap, position);
            return;
        }

        if (_drag == DragMode.None)
            return;

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

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_hoverCell is not null && _drag != DragMode.Paint)
        {
            _hoverCell = null;
            InvalidateVisual();
        }
    }

    // ----- Tile painting -----

    // Click the palette to pick a tile, Ctrl+click the map to pick the tile under the cursor, otherwise paint
    // (or erase with Shift). One stroke is one undo step.
    private void BeginPaint(Tilemap tilemap, Point position)
    {
        if (PaletteTileAt(tilemap, position) is { } chosen)
        {
            _brushTile = chosen;
            InvalidateVisual();
            return;
        }

        var cell = CellUnder(tilemap, position);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (cell is var (x, y) && tilemap.GetTile(x, y) is var picked and >= 0)
                _brushTile = picked;
            InvalidateVisual();
            return;
        }

        _erasing = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        _lastPaintCell = null;
        BeginDrag(DragMode.Paint, position);
        _stroke = _document!.History.BeginGroup(_erasing ? "Erase Tiles" : "Paint Tiles");
        PaintTo(tilemap, position);
    }

    // Paints every cell on the line from the previous point, so fast drags don't leave gaps.
    private void PaintTo(Tilemap tilemap, Point position)
    {
        if (CellUnder(tilemap, position) is not var (x, y))
            return;

        var (fromX, fromY) = _lastPaintCell ?? (x, y);
        var steps = Math.Max(Math.Abs(x - fromX), Math.Abs(y - fromY));
        var shiftX = 0;
        var shiftY = 0;
        for (var i = 0; i <= steps; i++)
        {
            var t = steps == 0 ? 1 : (double)i / steps;
            var cx = (int)Math.Round(fromX + (x - fromX) * t) + shiftX;
            var cy = (int)Math.Round(fromY + (y - fromY) * t) + shiftY;
            var (sx, sy) = PaintCell(tilemap, cx, cy);
            shiftX += sx;
            shiftY += sy;
        }

        _lastPaintCell = (x + shiftX, y + shiftY);
        _hoverCell = _lastPaintCell;
        InvalidateVisual();
    }

    // Sets one cell. Painting left of or above the grid grows it and moves the entity so existing tiles stay put.
    private (int ShiftX, int ShiftY) PaintCell(Tilemap tilemap, int x, int y)
    {
        var tile = _erasing ? -1 : _brushTile;
        if (_document is not { } document || tilemap.GetTile(x, y) == tile)
            return (0, 0);

        var (tiles, shiftX, shiftY) = tilemap.Painted(x, y, tile);
        document.Execute(new SetPropertyEdit(tilemap, TilesProperty, tiles));
        if (shiftX != 0 || shiftY != 0)
        {
            var transform = tilemap.Entity!.Transform;
            var offset = Vector3.TransformNormal(new Vector3(-shiftX * tilemap.TileSize, shiftY * tilemap.TileSize, 0), transform.LocalMatrix);
            document.Execute(new SetPropertyEdit(transform, PositionProperty, transform.Position + offset));
        }

        return (shiftX, shiftY);
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
        var cell = PaletteTileAt(tilemap, position) is null ? CellUnder(tilemap, position) : null;
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
        e.Effects = _document is not null && DroppedAsset(e) is not null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        e.Handled = true;
        if (_document is not { } document || DroppedAsset(e) is not var (assetPath, kind))
            return;

        var position = ToWorld(e.GetPosition(this));
        Entity entity;
        if (kind == Griff.Editor.Features.Assets.AssetKind.Prefab)
        {
            if (Griff.Editor.Features.Entities.PrefabFiles.Instantiate(_workspace, assetPath) is not { } instance)
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

        document.Execute(new AddEntityEdit(document.Scene, entity));
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

        if (PaintTarget is { } tilemap)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    _selection.Select(null);
                    e.Handled = true;
                    return;
                case Key.OemOpenBrackets or Key.OemCloseBrackets:
                    var count = PaletteCount(tilemap);
                    _brushTile = ((_brushTile + (e.Key == Key.OemCloseBrackets ? 1 : -1)) % count + count) % count;
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                case Key.LeftShift or Key.RightShift:
                    InvalidateVisual(); // the hover outline turns red for erasing
                    break;
            }
        }

        // Arrows nudge the selection: half a unit, or a tenth with Shift. Each press is its own undo step.
        var direction = e.Key switch
        {
            Key.Left => new Vector3(-1, 0, 0),
            Key.Right => new Vector3(1, 0, 0),
            Key.Up => new Vector3(0, 1, 0),
            Key.Down => new Vector3(0, -1, 0),
            _ => Vector3.Zero,
        };
        if (direction != Vector3.Zero && _selection.Entity is { } entity && _document is { } document &&
            (Keyboard.Modifiers & ~ModifierKeys.Shift) == ModifierKeys.None)
        {
            var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.1f : SnapStep;
            document.History.Seal();
            document.Execute(new SetPropertyEdit(entity.Transform, PositionProperty, entity.Transform.Position + direction * step));
            document.History.Seal();
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key is Key.LeftShift or Key.RightShift && PaintTarget is not null)
            InvalidateVisual();
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
        if (mode == DragMode.Paint)
        {
            _stroke?.Dispose();
            _stroke = null;
            _lastPaintCell = null;
            _erasing = false;
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

    /// <summary>One tile of a tilemap's image, cut out for drawing; null when the map has no (loadable) image.</summary>
    private BitmapSource? TileImage(Tilemap tilemap, int tile)
    {
        if (LoadImage(tilemap.Image) is not { } image)
            return null;

        var key = (tilemap.Image!, tile, tilemap.Columns, tilemap.Rows);
        if (_tileImages.TryGetValue(key, out var cached))
            return cached;

        var rect = tilemap.TileRect(tile);
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
