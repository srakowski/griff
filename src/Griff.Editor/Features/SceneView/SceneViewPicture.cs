using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Griff.Editor.Core.Workspace;
using Griff.Platform.Windows;
using Griff.Rendering;
using Griff.Scenes;

namespace Griff.Editor.Features.SceneView;

/// <summary>
/// The Scene view's picture of the scene: the frame <see cref="SceneRenderer.BuildView"/> builds of the part of the world in
/// view, drawn off the screen by the OpenGL renderer the Game view draws with, and shown as a bitmap under what WPF draws
/// over it (the handles, gizmos and selection; see <see cref="SceneViewDrawing"/>). So everything a game draws looks the
/// same here, from the one implementation each drawable component has. The grid goes under the scene in the same frame;
/// entities hidden in the editor are left out, and while one layer of a map is painted its other layers fade back. It's
/// drawn again only when something it shows has changed (see <see cref="Invalidate"/>).
/// </summary>
internal sealed class SceneViewPicture(SceneViewport viewport, SceneViewModel model, IProjectSession projectSession, SceneViewStyle style) : IDisposable
{
    // How opaque the other layers of a map being painted are drawn.
    internal const float OtherLayerOpacity = 0.35f;

    // Stands in for the grid's lines, which belong to no entity.
    private static readonly Entity GridEntity = new("Grid");

    private OffscreenGL? _gl;
    private Griff.Projects.IAssetSource? _assets;
    private WriteableBitmap? _bitmap;
    private RenderFrame? _frame;
    private bool _stale = true;
    private readonly List<SpriteDraw> _grid = [];

    /// <summary>Why the picture can't be drawn (no OpenGL), or null.</summary>
    public string? Problem { get; private set; }

    /// <summary>Marks the picture out of date: it's drawn again the next time the view draws.</summary>
    public void Invalidate() => _stale = true;

    /// <summary>
    /// Draws the picture over the whole view (first drawing it again if it's out of date or the view's size changed), with
    /// the grid under the scene when <paramref name="grid"/> is set.
    /// </summary>
    public void Draw(DrawingContext dc, SceneDocument document, bool grid)
    {
        var size = viewport.Size;
        var scale = style.PixelsPerDip;
        var (width, height) = ((int)Math.Round(size.Width * scale), (int)Math.Round(size.Height * scale));
        if (width <= 0 || height <= 0)
            return;

        if (_stale || _bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
            Redraw(document, width, height, scale, grid);
        if (_bitmap is null)
            return;

        // The rows come up from OpenGL bottom first: shown upside down, they're the right way up.
        dc.PushTransform(new MatrixTransform(1, 0, 0, -1, 0, size.Height));
        dc.DrawImage(_bitmap, new Rect(size));
        dc.Pop();
    }

    /// <summary>
    /// The frame the picture is drawn from, for the view as it is now at <paramref name="width"/> by
    /// <paramref name="height"/> pixels (<paramref name="scale"/> to a WPF unit): the scene from
    /// <see cref="SceneRenderer.BuildView"/>, with the grid under it if <paramref name="grid"/> is set.
    /// </summary>
    internal RenderFrame Frame(SceneDocument document, int width, int height, double scale, bool grid, RenderFrame? recycle = null)
    {
        var view = View();
        _grid.Clear();
        if (grid)
            AddGrid(view, width, height, scale);
        return SceneRenderer.BuildView(document.Scene, view, (float)(2 / viewport.Zoom), ColorOf("Brush.Surface.Document"), _grid, Opacity, recycle);
    }

    /// <summary>The part of the world in view: the middle of the view and half its size, in world units.</summary>
    internal Box View() =>
        new(viewport.Pan, new Vector2((float)(viewport.Size.Width / 2 / viewport.Zoom), (float)(viewport.Size.Height / 2 / viewport.Zoom)));

    private void Redraw(SceneDocument document, int width, int height, double scale, bool grid)
    {
        try
        {
            var assets = projectSession.Assets ?? Griff.Projects.AssetFolder.None;
            if (_gl is null || _assets != assets)
            {
                _gl?.Dispose();
                (_gl, _assets, _frame) = (null, null, null);
                _gl = new OffscreenGL(width, height, assets);
                _assets = assets;
            }

            _gl.Resize(width, height);
            _frame = Frame(document, width, height, scale, grid, _frame);
            _gl.Render(_frame);
            if (_bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
                _bitmap = new WriteableBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Bgr32, null);
            _bitmap.Lock();
            try
            {
                _gl.ReadBgra(_bitmap.BackBuffer);
                _bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
            }
            finally
            {
                _bitmap.Unlock();
            }

            (_stale, Problem) = (false, null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            // No OpenGL to draw with: the view says so rather than failing each time it draws.
            (_bitmap, _stale, Problem) = (null, false, ex.Message);
        }
    }

    // Hidden entities are left out; while a layer of a map is painted, the map's other layers fade back.
    private float Opacity(Entity entity)
    {
        if (entity.IsHiddenInHierarchy)
            return 0;
        return model.PaintTarget is { } painting && entity.GetComponent<Tilemap>() is { } map && map != painting && map.BaseMap == painting.BaseMap
            ? OtherLayerOpacity
            : 1;
    }

    // Grid lines a pixel wide on whole pixels, through every power of ten of world units that keeps them 8 WPF units or more
    // apart (each tenth one stronger), and the axes over them.
    private void AddGrid(Box view, int width, int height, double scale)
    {
        var (minor, major) = (ColorOf("Brush.Border.Subtle"), ColorOf("Brush.Selection.Inactive"));
        var left = view.Center.X - view.HalfSize.X;
        var top = view.Center.Y + view.HalfSize.Y;
        var (perX, perY) = (width / (view.HalfSize.X * 2), height / (view.HalfSize.Y * 2));
        var step = Math.Pow(10, Math.Ceiling(Math.Log10(8 / viewport.Zoom)));
        for (var x = Math.Floor(left / step) * step; x <= view.Center.X + view.HalfSize.X; x += step)
            Column(x, IsMajor(x) ? major : minor);
        for (var y = Math.Floor((view.Center.Y - view.HalfSize.Y) / step) * step; y <= top; y += step)
            Row(y, IsMajor(y) ? major : minor);
        Row(0, Faded("Brush.Axis.X"));
        Column(0, Faded("Brush.Axis.Y"));

        bool IsMajor(double at) => Math.Abs(Math.IEEERemainder(at, step * 10)) < step / 2;

        Color Faded(string key) => ColorOf(key) with { A = 0.6f };

        // The pixel column the world's X falls in, as a quad from its left edge to its right.
        void Column(double x, Color color)
        {
            var column = Math.Round((x - left) * perX);
            var from = left + (float)(column / perX);
            var middle = from + 0.5f / perX;
            _grid.Add(new SpriteDraw(Matrix4x4.CreateScale(1 / perX, view.HalfSize.Y * 2, 1) * Matrix4x4.CreateTranslation(middle, view.Center.Y, 0), null, color, GridEntity));
        }

        // The pixel row, counted down from the top, the world's Y falls in.
        void Row(double y, Color color)
        {
            var row = Math.Round((top - y) * perY);
            var from = top - (float)(row / perY);
            var middle = from - 0.5f / perY;
            _grid.Add(new SpriteDraw(Matrix4x4.CreateScale(view.HalfSize.X * 2, 1 / perY, 1) * Matrix4x4.CreateTranslation(view.Center.X, middle, 0), null, color, GridEntity));
        }
    }

    // A theme color, as the renderer takes it.
    private Color ColorOf(string key)
    {
        var color = style.Brush(key) is SolidColorBrush solid ? solid.Color : Colors.Gray;
        return Color.FromBytes(color.R, color.G, color.B) with { A = color.A / 255f * (float)style.Brush(key).Opacity };
    }

    public void Dispose()
    {
        _gl?.Dispose();
        _gl = null;
    }
}
