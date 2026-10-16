using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Griff.Editing;
using Griff.Editor.Core.Workspace;

namespace Griff.Editor.Features.SceneView;

/// <summary>
/// 2D view of the open scene. Y is up; where it looks is <see cref="SceneViewport"/>.
/// Wheel zooms at the cursor; middle/right/Alt-drag pans; click selects (Ctrl or Shift adds or takes away); dragging
/// from empty space selects everything in the box; dragging a selected entity moves the whole selection (Ctrl snaps);
/// dragging the rotate handle above the primary selection turns the selection around it (Ctrl snaps to 15 degrees);
/// dragging the square handles on its corners and edges scales it (Shift keeps proportions, Ctrl snaps to tenths), or,
/// with Resize or Collider handles picked (R and C; S goes back), resizes its sprite or its colliders (Ctrl snaps to
/// half units); with Distance handles picked (D), the round knobs on its circles change its light radii, ranges and sight
/// (Ctrl snaps to half units); the knobs on its path move a mover's points and a patrol's turns (Ctrl snaps to half units;
/// drag a plus to add a point, double-click a point to take it off); F frames. The selection's actions live in
/// <see cref="SceneViewModel"/>, where agents reach them too.
/// With a tilemap selected, left-drag paints tiles instead, with the tool picked from the palette (<see cref="TilePalette"/>)
/// or with B (brush), R (rectangle) and G (fill); Shift erases, Ctrl picks, [ and ] change tile, Esc stops. Painting lives in
/// <see cref="SceneViewModel"/> too (<see cref="SceneViewModel.PaintTile"/> and the rest).
/// Arrows link the selection to the entities its fields name, and to it from the fields that name it.
/// The control only puts the pieces together: <see cref="SceneViewPicture"/> draws the scene with the game's renderer,
/// <see cref="SceneViewDrawing"/> draws it and what goes over it, <see cref="SceneHandleGeometry"/> says
/// where the handles are, and <see cref="SceneViewInput"/> turns the mouse and keys into calls on the view models.
/// </summary>
public sealed class SceneViewControl : FrameworkElement, IDisposable
{
    private readonly SceneViewport _viewport;
    private readonly TilePalette _palette;
    private readonly SceneViewStyle _style;
    private readonly SceneViewInput _input;
    private readonly SceneViewPicture _picture;
    private readonly SceneViewDrawing _drawing;
    private SceneDocument? _document;

    // Commands are reached through the services on demand, as some are built with this view.
    public SceneViewControl(IWorkspace workspace, ISelection selection, SceneViewModel model, SceneViewport viewport, TilePalette palette,
        IServiceProvider services)
    {
        _viewport = viewport;
        _palette = palette;
        _style = new SceneViewStyle(this);
        Handles = new SceneHandleGeometry(viewport, model, selection, workspace, _style.Measure);
        _input = new SceneViewInput(this, viewport, model, palette, selection, workspace, Handles, _style, services);
        _picture = new SceneViewPicture(viewport, model, workspace, _style);
        _drawing = new SceneViewDrawing(viewport, model, palette, selection, workspace, Handles, _input, _style, _picture);

        Focusable = true;
        AllowDrop = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);

        // The scene's picture is drawn again when what it shows changes; the rest (the selection, what's under the mouse)
        // only redraws what goes over it.
        workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IWorkspace.Scene))
                Track(workspace.Scene);
            else if (e.PropertyName == nameof(IWorkspace.Project))
                Redraw();
        };
        selection.PropertyChanged += (_, _) => InvalidateVisual();
        model.PropertyChanged += (_, _) =>
        {
            _input.ForgetHover();
            Redraw();
        };
        viewport.PropertyChanged += (_, _) => Redraw();
        palette.PropertyChanged += (_, _) => Redraw();
        workspace.ScriptsReloaded += (_, _) => Redraw();
        Track(workspace.Scene);
    }

    /// <summary>Where the handles are on screen (for demos to point at them).</summary>
    internal SceneHandleGeometry Handles { get; }

    /// <summary>Where the view looks.</summary>
    internal SceneViewport Viewport => _viewport;

    /// <summary>The scene's picture under what's drawn over it.</summary>
    internal SceneViewPicture Picture => _picture;

    /// <summary>
    /// The screen box of a part of the tile painting overlay, for demos to point at (see
    /// <see cref="TilePaletteLayout.Bounds"/>). Null when nothing is being painted (or the tile is scrolled out of sight).
    /// </summary>
    internal Rect? PaintOverlayBounds(string part) =>
        _palette.Target is { } tilemap ? TilePaletteLayout.Of(_palette, tilemap, _style.Measure).Bounds(part) : null;

    /// <summary>Draws what the view shows into <paramref name="drawingContext"/>; with <paramref name="overlays"/> off, only the scene's picture.</summary>
    internal void DrawInto(DrawingContext drawingContext, bool overlays) => _drawing.Draw(drawingContext, _document, overlays);

    private void Track(SceneDocument? document)
    {
        if (_document is not null)
        {
            _document.Changed -= OnDocumentChanged;
            _document.RuntimeTicked -= OnRuntimeTicked;
        }

        var leaving = _document;
        _document = document;
        if (_document is not null)
        {
            _document.Changed += OnDocumentChanged;
            _document.RuntimeTicked += OnRuntimeTicked;
        }

        if (!_viewport.Follow(leaving, document))
            _drawing.ForgetImages();
        Redraw();
    }

    // The scene's picture is out of date: the whole view draws again.
    private void Redraw()
    {
        _picture.Invalidate();
        InvalidateVisual();
    }

    private void OnDocumentChanged(object? sender, EditEventArgs e) => Redraw();

    private void OnRuntimeTicked(object? sender, EventArgs e) => Redraw();

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        _viewport.Size = sizeInfo.NewSize;
        _palette.AvailableRows = (int)((sizeInfo.NewSize.Height - TilePaletteLayout.TilesTop - TilePaletteLayout.RoomBelow) / TilePaletteLayout.Cell);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        _viewport.Size = RenderSize;
        _drawing.Draw(drawingContext, _document);
    }

    /// <summary>Lets go of the OpenGL context the scene's picture is drawn with.</summary>
    public void Dispose() => _picture.Dispose();

    protected override void OnMouseWheel(MouseWheelEventArgs e) => _input.Wheel(e);

    protected override void OnMouseDown(MouseButtonEventArgs e) => _input.Down(e);

    protected override void OnMouseMove(MouseEventArgs e) => _input.Move(e);

    protected override void OnMouseUp(MouseButtonEventArgs e) => _input.Up(e);

    protected override void OnLostMouseCapture(MouseEventArgs e) => _input.LostCapture();

    protected override void OnMouseLeave(MouseEventArgs e) => _input.Leave();

    protected override void OnDragOver(DragEventArgs e) => _input.DragOver(e);

    protected override void OnDrop(DragEventArgs e) => _input.Drop(e);

    protected override void OnKeyDown(KeyEventArgs e) => _input.KeyDown(e);

    protected override void OnKeyUp(KeyEventArgs e) => _input.KeyUp(e);
}
