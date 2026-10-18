using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Assets;
using Griff.Scenes;
using Point = System.Windows.Point;
using Vector = System.Windows.Vector;
using Griff.Editor.Features.Art;
using Griff.Editor.Features.Entities;

namespace Griff.Editor.Features.SceneView;

/// <summary>What a press in the Scene view started, until the mouse lets go.</summary>
internal enum SceneDrag
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
    Vector,
}

/// <summary>
/// The Scene view's mouse and keys, turned into calls on its view models: the wheel zooms (or scrolls the tile palette),
/// presses start drags (panning, moving, a box, the handles, painting) that the mouse carries on and lets go of, and keys
/// pick tools and handles, nudge and frame. What it's in the middle of (the drag, the handle under the mouse, the cell
/// being painted) is kept here for drawing.
/// </summary>
internal sealed class SceneViewInput(FrameworkElement view, SceneViewport viewport, SceneViewModel model, TilePalette palette,
    ISelection selection, ISceneDocuments sceneDocuments, SceneHandleGeometry handles, SceneViewStyle style,
    AddTilesetCommand addTileset, HideEntityCommand hide, AssetPlacement placement, PrefabFiles prefabFiles)
{
    public const double DragThreshold = 3;
    private const float SnapStep = 0.5f;

    private Vector2 _dragStartPan;
    private SceneViewModel.SelectionMove? _move;
    // A click on one of several selected entities picks it alone, unless the mouse moves (then they all move).
    private Entity? _pressed;
    private bool _boxAdds;
    private double _rotateLast;
    private double _rotateTotal;
    private Vector2 _scalePress;
    private SceneViewModel.SelectionScale? _scale;
    private Vector2 _resizePress;
    private float _distanceGrab;
    private SceneViewModel.PathPointDrag? _path;
    private Vector2 _pathGrab;
    private Vector2 _vectorGrab;

    public SceneDrag Drag { get; private set; }

    /// <summary>Where the drag started, on screen.</summary>
    public Point DragStart { get; private set; }

    /// <summary>The far corner of the box being dragged out, on screen.</summary>
    public Point BoxEnd { get; private set; }

    // The handle under the mouse, when nothing is being dragged.
    public bool OverRotate { get; private set; }

    public Vector2? OverScale { get; private set; }

    public (Component Shape, Vector2 Spot)? OverResize { get; private set; }

    public string? OverDistance { get; private set; }

    public string? OverPath { get; private set; }

    public string? OverVector { get; private set; }

    // The handle being dragged.
    public SceneViewModel.SelectionRotate? Rotate { get; private set; }

    public Vector2 ScaleSpot { get; private set; }

    public SceneViewModel.ShapeResize? Resize { get; private set; }

    public Vector2 ResizeSpot { get; private set; }

    public SceneViewModel.DistanceDrag? Distance { get; private set; }

    public SceneViewModel.VectorDrag? VectorKnob { get; private set; }

    public string? PathKey { get; private set; }

    // Tile painting: the cell under the mouse, a brush stroke under way, or the corner a rectangle is being dragged from.
    public (int X, int Y)? HoverCell { get; private set; }

    public SceneViewModel.TileStroke? Stroke { get; private set; }

    public (int X, int Y)? RectangleFrom { get; private set; }

    public bool RectangleErases { get; private set; }

    // The line being dragged out with the Line tool (it erases as a rectangle does, by RectangleErases).
    public LineDrag? Line { get; private set; }

    /// <summary>Whether painting now erases: the stroke's, rectangle's or line's way, or Shift held.</summary>
    public bool Erasing => Stroke?.Erasing ?? (RectangleFrom is not null || Line is not null ? RectangleErases : Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));

    /// <summary>The cells of the line being dragged out with the Line tool (joined onto the map's lines, if its Line Tiles join), or empty.</summary>
    public IReadOnlyList<LineCell> LineCells => Line is null ? [] : RectangleErases ? Line.Cells(model.SingleWay) : model.JoinLine(Line.Cells(model.SingleWay));

    // The tilemap whose palette shows, and the one clicks paint (none with the Select tool).
    private Tilemap? PaintTarget => model.PaintTarget;

    private Tilemap? Painting => model.IsPainting ? model.PaintTarget : null;

    private TilePaletteLayout Layout(Tilemap tilemap) => TilePaletteLayout.Of(palette, tilemap, style.Measure);

    /// <summary>Forgets the handle under the mouse (the selection or what the handles do changed).</summary>
    public void ForgetHover() => (OverScale, OverResize, OverDistance, OverPath, OverVector) = (null, null, null, null, null);

    // ----- Mouse -----

    public void Wheel(MouseWheelEventArgs e)
    {
        // Over the tile palette, the wheel scrolls its tiles.
        if (PaintTarget is { } tilemap && Layout(tilemap).Panel.Contains(e.GetPosition(view)))
        {
            palette.Scroll -= Math.Sign(e.Delta);
            e.Handled = true;
            return;
        }

        viewport.ZoomAt(e.GetPosition(view), Math.Pow(1.0015, e.Delta));
        e.Handled = true;
    }

    public void Down(MouseButtonEventArgs e)
    {
        view.Focus();
        if (sceneDocuments.Scene is null)
            return;

        var position = e.GetPosition(view);
        var wantsPan = e.ChangedButton is MouseButton.Middle or MouseButton.Right ||
                       (e.ChangedButton == MouseButton.Left && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));

        if (wantsPan)
        {
            BeginDrag(SceneDrag.Pan, position);
        }
        else if (e.ChangedButton == MouseButton.Left && PaintTarget is { } map && ClickPalette(map, position))
        {
            // A tile, tool, tab or layer picked from the palette.
        }
        else if (e.ChangedButton == MouseButton.Left && Painting is { } tilemap)
        {
            BeginPaint(tilemap, position);
        }
        else if (e.ChangedButton == MouseButton.Left && handles.IsOverRotateHandle(position) && model.BeginRotate() is { } rotate)
        {
            Rotate = rotate;
            _rotateLast = AngleAround(rotate.Pivot, position);
            _rotateTotal = 0;
            BeginDrag(SceneDrag.Rotate, position);
        }
        else if (e.ChangedButton == MouseButton.Left && handles.PathHandleAt(position) is { } pathHandle)
        {
            BeginPathDrag(pathHandle, position, e.ClickCount);
        }
        else if (e.ChangedButton == MouseButton.Left && handles.VectorHandleAt(position) is { } vectorKnob && model.BeginVector(vectorKnob) is { } vector)
        {
            // Grabbing the knob a little off it doesn't make the value jump.
            VectorKnob = vector;
            _vectorGrab = vectorKnob.World - viewport.ToWorld(position);
            BeginDrag(SceneDrag.Vector, position);
            view.Cursor = Cursors.SizeAll;
        }
        else if (e.ChangedButton == MouseButton.Left && handles.DistanceHandleAt(position) is { } knob && model.BeginDistance(knob) is { } distance)
        {
            // Grabbing the knob a little off its circle doesn't make the distance jump.
            Distance = distance;
            _distanceGrab = knob.Value - Vector2.Distance(knob.Center, viewport.ToWorld(position));
            BeginDrag(SceneDrag.Distance, position);
            view.Cursor = DistanceCursor(knob, position);
        }
        else if (e.ChangedButton == MouseButton.Left && handles.ResizeHandleAt(position) is var (shape, resizeSpot) && model.BeginResize(shape) is { } resize)
        {
            Resize = resize;
            ResizeSpot = resizeSpot;
            _resizePress = viewport.ToWorld(position);
            BeginDrag(SceneDrag.Resize, position);
            view.Cursor = ResizeCursor(shape, resizeSpot);
        }
        else if (e.ChangedButton == MouseButton.Left && handles.ScaleHandleAt(position) is { } spot && model.BeginScale() is { } scale)
        {
            _scale = scale;
            ScaleSpot = spot;
            _scalePress = viewport.ToWorld(position);
            BeginDrag(SceneDrag.Scale, position);
            view.Cursor = ScaleCursor(spot);
        }
        else if (e.ChangedButton == MouseButton.Left)
        {
            var hit = handles.HitTest(position);
            var adds = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if (hit is null)
            {
                // Empty space: a click selects nothing (Ctrl or Shift keep the selection), a drag draws a box.
                if (!adds)
                    selection.Select(null);
                _boxAdds = adds;
                BoxEnd = position;
                BeginDrag(SceneDrag.Box, position);
            }
            else
            {
                if (adds)
                    selection.Toggle(hit);
                else if (!selection.IsSelected(hit))
                    selection.Select(hit);
                else if (selection.Entities.Count > 1)
                    _pressed = hit;

                if (selection.IsSelected(hit) && model.BeginMove() is { } move)
                {
                    _move = move;
                    BeginDrag(SceneDrag.Move, position);
                }
            }
        }

        e.Handled = true;
    }

    public void Move(MouseEventArgs e)
    {
        var position = e.GetPosition(view);
        if (Painting is { } tilemap && Drag is SceneDrag.None or SceneDrag.Paint)
        {
            UpdateHoverCell(tilemap, position);
            if (Stroke is not null)
                PaintTo(tilemap, position);
            else if (Line is not null && CellUnder(tilemap, position) is { } cell && Line.MoveTo(cell))
                view.InvalidateVisual();
            return;
        }

        var snap = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        switch (Drag)
        {
            case SceneDrag.None:
                Hover(position);
                return;
            case SceneDrag.Scale:
                _scale?.To(ScaleFactor(_scale, viewport.ToWorld(position)), snap);
                return;
            case SceneDrag.Path:
                _path?.To(viewport.ToWorld(position) + _pathGrab, snap);
                return;
            case SceneDrag.Distance:
                Distance?.To(Vector2.Distance(Distance.Distance.Center, viewport.ToWorld(position)) + _distanceGrab, snap);
                return;
            case SceneDrag.Vector:
                VectorKnob?.To(viewport.ToWorld(position) + _vectorGrab, snap);
                return;
            case SceneDrag.Resize:
                // The handle follows the cursor from where it was pressed, so grabbing it a little off doesn't jump.
                if (Resize?.Frame is { } frame)
                    Resize.Drag(ResizeSpot, frame.HandleAt(ResizeSpot) + frame.ToLocal(viewport.ToWorld(position)) - frame.ToLocal(_resizePress),
                        proportional: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), snap: snap);
                return;
            case SceneDrag.Rotate:
                // Add up the small turns between mouse moves, so going round more than once keeps counting.
                if (Rotate is null || (viewport.ToWorld(position) - Rotate.Pivot).Length() < 1e-6f)
                    return;
                var angle = AngleAround(Rotate.Pivot, position);
                _rotateTotal += Math.IEEERemainder(angle - _rotateLast, 360);
                _rotateLast = angle;
                Rotate.To((float)_rotateTotal, snap);
                return;
            case SceneDrag.Pan:
                var delta = position - DragStart;
                viewport.Pan = _dragStartPan - new Vector2((float)(delta.X / viewport.Zoom), (float)(-delta.Y / viewport.Zoom));
                return;
            case SceneDrag.Box:
                BoxEnd = position;
                view.InvalidateVisual();
                return;
        }

        if (_move is null)
            return;
        if ((position - DragStart).Length >= DragThreshold)
            _pressed = null;
        _move.To(viewport.ToWorld(position) - viewport.ToWorld(DragStart), snap);
    }

    // The handle under the mouse grows and sets the cursor.
    private void Hover(Point position)
    {
        var over = handles.IsOverRotateHandle(position);
        var overScale = over ? null : handles.ScaleHandleAt(position);
        var overResize = over ? null : handles.ResizeHandleAt(position);
        var overPath = over ? null : handles.PathHandleAt(position);
        var overVector = over || overPath is not null ? null : handles.VectorHandleAt(position);
        var overDistance = over || overPath is not null || overVector is not null ? null : handles.DistanceHandleAt(position);
        if (over == OverRotate && overScale == OverScale && overResize == OverResize && overDistance?.Key == OverDistance &&
            overPath?.Key == OverPath && overVector?.Key == OverVector)
            return;

        OverRotate = over;
        OverScale = overScale;
        OverResize = overResize;
        OverDistance = overDistance?.Key;
        OverPath = overPath?.Key;
        OverVector = overVector?.Key;
        view.Cursor = over ? Cursors.Hand
            : overPath is not null ? PathCursor(overPath)
            : overVector is not null ? Cursors.SizeAll
            : overDistance is not null ? DistanceCursor(overDistance, position)
            : overScale is { } spot ? ScaleCursor(spot)
            : overResize is var (shape, resizeSpot) ? ResizeCursor(shape, resizeSpot)
            : null;
        view.InvalidateVisual();
    }

    public void Up(MouseButtonEventArgs e)
    {
        var position = e.GetPosition(view);
        if (Drag == SceneDrag.Box && (position - DragStart).Length >= DragThreshold)
            model.SelectInBox(viewport.ToWorld(DragStart), viewport.ToWorld(position), _boxAdds);
        else if (Drag == SceneDrag.Move && _pressed is { } pressed)
            selection.Select(pressed);
        else if (Drag == SceneDrag.Paint && RectangleFrom is var (fromX, fromY) && Painting is { } tilemap && CellUnder(tilemap, position) is var (toX, toY))
            model.PaintRectangle(fromX, fromY, toX, toY, RectangleErases);
        else if (Drag == SceneDrag.Paint && Line is not null && Painting is not null)
            model.PaintLine(LineCells, RectangleErases);

        if (Drag != SceneDrag.None)
            EndDrag();
    }

    public void LostCapture()
    {
        if (Drag != SceneDrag.None)
            EndDrag();
    }

    public void Leave()
    {
        if (Drag != SceneDrag.Paint)
            ClearHoverCell();
    }

    // ----- Tile painting -----

    // A click on the palette picks a tile, a tool, a tileset or a layer (with any tool, Select too). False off it.
    private bool ClickPalette(Tilemap tilemap, Point position)
    {
        var layout = Layout(tilemap);
        if (layout.ToolAt(position) is { } tool)
        {
            palette.Tool = tool;
            ClearHoverCell();
            return true;
        }

        if (layout.LayerAt(position) is { } layer)
        {
            if (layer < tilemap.Layers().Count)
                palette.ShowLayer(layer);
            else
                palette.AddLayer();
            return true;
        }

        if (layout.AddTab.Contains(position))
        {
            ShowAddTilesetMenu(layout.AddTab);
            return true;
        }

        if (layout.TabAt(position) is { } tab)
        {
            palette.ShowTileset(tab);
            return true;
        }

        if (layout.TileAt(position, palette.BrushTile) is { } chosen)
        {
            palette.Pick(chosen);
            return true;
        }

        return false;
    }

    // Ctrl+click the map to pick the tile under the cursor, otherwise use the tool (erasing with Shift): drag the brush,
    // drag out a rectangle, or click to fill. Each is one undo step.
    private void BeginPaint(Tilemap tilemap, Point position)
    {

        if (CellUnder(tilemap, position) is not var (x, y))
            return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            model.PickTile(x, y);
            return;
        }

        var erase = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (model.TileTool)
        {
            case TileTool.Fill:
                model.FillTiles(x, y, erase);
                view.InvalidateVisual();
                return;
            case TileTool.Rectangle:
                (RectangleFrom, RectangleErases, HoverCell) = ((x, y), erase, (x, y));
                BeginDrag(SceneDrag.Paint, position);
                view.InvalidateVisual();
                return;
            case TileTool.Line:
                (Line, RectangleErases, HoverCell) = (new LineDrag((x, y)), erase, (x, y));
                BeginDrag(SceneDrag.Paint, position);
                view.InvalidateVisual();
                return;
            default:
                BeginDrag(SceneDrag.Paint, position);
                Stroke = model.BeginTileStroke(erase);
                PaintTo(tilemap, position);
                return;
        }
    }

    // Paints every cell on the line from the previous point, so fast drags don't leave gaps.
    private void PaintTo(Tilemap tilemap, Point position)
    {
        if (Stroke is null || CellUnder(tilemap, position) is not var (x, y))
            return;
        Stroke.To(x, y);
        HoverCell = Stroke.Last;
        view.InvalidateVisual();
    }

    private (int X, int Y)? CellUnder(Tilemap tilemap, Point position)
    {
        if (!Matrix4x4.Invert(tilemap.Entity!.Transform.WorldMatrix, out var inverse))
            return null;
        var local = Vector3.Transform(new Vector3(viewport.ToWorld(position), 0), inverse);
        return tilemap.CellAt(new Vector2(local.X, local.Y));
    }

    private void UpdateHoverCell(Tilemap tilemap, Point position)
    {
        var layout = Layout(tilemap);
        var overPalette = Drag == SceneDrag.None &&
            (layout.TileAt(position, palette.BrushTile) is not null || layout.ToolAt(position) is not null || layout.LayerAt(position) is not null);
        var cell = overPalette ? null : CellUnder(tilemap, position);
        if (cell != HoverCell)
        {
            HoverCell = cell;
            view.InvalidateVisual();
        }
    }

    // No cell outlined (the mouse left, or the tool became Select).
    private void ClearHoverCell()
    {
        if (HoverCell is null)
            return;
        HoverCell = null;
        view.InvalidateVisual();
    }

    // The + tab lists the project's images to add as a tileset, as Art > Add Tileset to Tilemap does.
    private void ShowAddTilesetMenu(Rect tab)
    {
        var command = addTileset;
        var menu = new ContextMenu { PlacementTarget = view, Placement = System.Windows.Controls.Primitives.PlacementMode.Relative, HorizontalOffset = tab.Left, VerticalOffset = tab.Bottom };
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

    // ----- Dropping assets -----

    // Dropping an image or a prefab from the Assets panel is its Add to Scene, placed where it lands.
    public void DragOver(DragEventArgs e)
    {
        e.Effects = sceneDocuments.Scene is { } document && DroppedAsset(e) is var (assetPath, _) && !prefabFiles.IsOwnTab(document, assetPath)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    public void Drop(DragEventArgs e)
    {
        e.Handled = true;
        if (sceneDocuments.Scene is not { } document || DroppedAsset(e) is not var (assetPath, _) || prefabFiles.IsOwnTab(document, assetPath))
            return;

        try
        {
            placement.AddToScene(assetPath, viewport.ToWorld(e.GetPosition(view)));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // A prefab that can't be read: nothing lands.
            return;
        }

        view.Focus();
    }

    private static (string Path, AssetKind Kind)? DroppedAsset(DragEventArgs e)
    {
        if (e.Data.GetData(AssetNode.DataFormat) is not string path)
            return null;
        var kind = AssetNode.KindOf(path);
        return kind is AssetKind.Image or AssetKind.Prefab ? (path, kind) : null;
    }

    // ----- Keys -----

    public void KeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.None)
        {
            viewport.FrameSelection();
            e.Handled = true;
            return;
        }

        // While a tilemap is selected (painting hides the handles), B, R, G and L pick the brush, rectangle, fill and line, as in
        // the Sprite Painter, and V the cursor, to select and move things without painting.
        if (e.Key is Key.V or Key.B or Key.R or Key.G or Key.L && Keyboard.Modifiers == ModifierKeys.None && Drag == SceneDrag.None && PaintTarget is not null)
        {
            palette.Tool = e.Key switch
            {
                Key.V => TileTool.Select,
                Key.R => TileTool.Rectangle,
                Key.G => TileTool.Fill,
                Key.L => TileTool.Line,
                _ => TileTool.Brush,
            };
            ClearHoverCell();
            e.Handled = true;
            return;
        }

        // H hides the selection here (or shows it again), as Entity > Hide does.
        if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.None && Drag == SceneDrag.None)
        {
            if (hide.CanExecute())
                _ = hide.ExecuteAsync();
            e.Handled = true;
            return;
        }

        // S, R, C and D pick what the handles do: scale, resize the sprite, resize the colliders, or change distances.
        if (e.Key is Key.S or Key.R or Key.C or Key.D && Keyboard.Modifiers == ModifierKeys.None && Drag == SceneDrag.None)
        {
            model.Handles = e.Key switch
            {
                Key.R => SceneHandles.Resize,
                Key.C => SceneHandles.Collider,
                Key.D => SceneHandles.Distance,
                _ => SceneHandles.Scale,
            };
            e.Handled = true;
            return;
        }

        if (PaintTarget is not null)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    // Esc lets go of a rectangle or line being dragged; otherwise it stops painting.
                    if (RectangleFrom is not null || Line is not null)
                        EndDrag();
                    else
                        selection.Select(null);
                    e.Handled = true;
                    return;
                case Key.Z or Key.X when Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift:
                    // Z turns the brush a quarter clockwise (Shift+Z back), X flips it left to right (Shift+X upside down).
                    // While a line is dragged out, Z turns its corner the other way round instead.
                    var shift = Keyboard.Modifiers == ModifierKeys.Shift;
                    if (e.Key == Key.Z && Line is { IsSingle: false } line)
                        line.SwapCorner();
                    else if (e.Key == Key.Z)
                        model.TurnBrush(shift ? -1 : 1);
                    else
                        model.FlipBrush(upsideDown: shift);
                    view.InvalidateVisual();
                    e.Handled = true;
                    return;
                case Key.OemOpenBrackets or Key.OemCloseBrackets:
                    // Steps through the tiles of the tileset the palette shows.
                    palette.Step(e.Key == Key.OemCloseBrackets ? 1 : -1);
                    view.InvalidateVisual();
                    e.Handled = true;
                    return;
                case Key.LeftShift or Key.RightShift:
                    view.InvalidateVisual(); // the hover outline turns red for erasing
                    break;
            }
        }

        // Arrows nudge everything selected: half a unit, or a tenth with Shift. Each press is its own undo step.
        var direction = e.Key switch
        {
            Key.Left => new Vector2(-1, 0),
            Key.Right => new Vector2(1, 0),
            Key.Up => new Vector2(0, 1),
            Key.Down => new Vector2(0, -1),
            _ => Vector2.Zero,
        };
        if (direction != Vector2.Zero && selection.Entity is not null && (Keyboard.Modifiers & ~ModifierKeys.Shift) == ModifierKeys.None)
        {
            var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.1f : SnapStep;
            model.MoveSelection(direction * step);
            e.Handled = true;
        }
    }

    public void KeyUp(KeyEventArgs e)
    {
        if (e.Key is Key.LeftShift or Key.RightShift && PaintTarget is not null)
            view.InvalidateVisual();
    }

    // ----- Drags -----

    // A press on a path handle: a double-click takes its point off (a patrol's turn clears); otherwise it drags the
    // point, or for a plus, a new point added there.
    private void BeginPathDrag(PathHandle handle, Point position, int clicks)
    {
        if (handle.Point is { } point && clicks == 2)
        {
            try
            {
                model.RemovePathPoint(point.Key);
            }
            catch (InvalidOperationException)
            {
                // A mover's last point stays: it needs somewhere to go.
            }

            OverPath = null;
            view.InvalidateVisual();
            return;
        }

        var drag = handle.Point is { } existing ? model.BeginPathPoint(existing)
            : handle.Path is { } path ? model.BeginInsertPathPoint(path, handle.Index)
            : null;
        if (drag is null)
            return;

        // The handle follows the cursor from where it was pressed, so grabbing it a little off doesn't jump. A plus adds
        // its point straight away, so a click adds one there.
        BeginDrag(SceneDrag.Path, position);
        (_path, PathKey) = (drag, handle.Point?.Key ?? $"{handle.Path!.GetType().Name}.{handle.Index}");
        _pathGrab = viewport.ToWorld(handle.Screen) - viewport.ToWorld(position);
        if (handle.Point is null)
            drag.To(viewport.ToWorld(handle.Screen), snap: false);
        view.Cursor = PathCursor(handle);
    }

    private void BeginDrag(SceneDrag mode, Point position)
    {
        Drag = mode;
        handles.DraggingPathPoint = mode == SceneDrag.Path;
        DragStart = position;
        _dragStartPan = viewport.Pan;
        sceneDocuments.Scene?.History.Seal();
        view.CaptureMouse();
        if (mode == SceneDrag.Pan)
            view.Cursor = Cursors.SizeAll;
    }

    public void EndDrag()
    {
        var mode = Drag;
        Drag = SceneDrag.None;
        handles.DraggingPathPoint = false;
        view.Cursor = null;
        if (view.IsMouseCaptured)
            view.ReleaseMouseCapture();
        switch (mode)
        {
            case SceneDrag.Move:
                _move?.End();
                _move = null;
                _pressed = null;
                break;
            case SceneDrag.Rotate:
                Rotate?.End();
                Rotate = null;
                OverRotate = false;
                break;
            case SceneDrag.Scale:
                _scale?.End();
                _scale = null;
                OverScale = null;
                break;
            case SceneDrag.Resize:
                Resize?.End();
                Resize = null;
                OverResize = null;
                break;
            case SceneDrag.Path:
                _path?.End();
                (_path, PathKey, OverPath) = (null, null, null);
                break;
            case SceneDrag.Distance:
                Distance?.End();
                Distance = null;
                OverDistance = null;
                break;
            case SceneDrag.Vector:
                VectorKnob?.End();
                VectorKnob = null;
                OverVector = null;
                break;
            case SceneDrag.Paint:
                Stroke?.End();
                Stroke = null;
                RectangleFrom = null;
                Line = null;
                break;
        }

        if (mode is not (SceneDrag.Pan or SceneDrag.None))
            view.InvalidateVisual();
    }

    // How much a drag of a scale handle, from where it was pressed to the cursor, scales by along the primary's own
    // axes: a corner scales both, an edge one. With Shift both scale alike, by how far the cursor has gone along the
    // line from the pivot through the handle, so proportions stay.
    private Vector2 ScaleFactor(SceneViewModel.SelectionScale scale, Vector2 cursor)
    {
        var from = scale.Along(_scalePress);
        var to = scale.Along(cursor);
        // A handle pressed right on the pivot can't say how much; it leaves that axis alone.
        var near = (float)(4 / viewport.Zoom);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            var line = new Vector2(ScaleSpot.X == 0 ? 0 : from.X, ScaleSpot.Y == 0 ? 0 : from.Y);
            return new Vector2(line.Length() < near ? 1 : Vector2.Dot(to, line) / line.LengthSquared());
        }

        float Ratio(float start, float now) => MathF.Abs(start) < near ? 1 : now / start;
        return new Vector2(ScaleSpot.X == 0 ? 1 : Ratio(from.X, to.X), ScaleSpot.Y == 0 ? 1 : Ratio(from.Y, to.Y));
    }

    // The angle of a point around the turn's pivot, in degrees counterclockwise (the world is Y up).
    private double AngleAround(Vector2 pivot, Point screen)
    {
        var offset = viewport.ToWorld(screen) - pivot;
        return Math.Atan2(offset.Y, offset.X) * 180 / Math.PI;
    }

    // ----- Cursors -----

    // A resize cursor along the line from the frame's middle out through the handle.
    private Cursor ScaleCursor(Vector2 spot) =>
        handles.HandleFrame() is { } frame
            ? SizeCursor(SceneHandleGeometry.FramePoint(frame, spot) - SceneHandleGeometry.FramePoint(frame, Vector2.Zero))
            : Cursors.SizeAll;

    // A resize cursor out from the shape's middle through the handle.
    private Cursor ResizeCursor(Component shape, Vector2 spot) =>
        SceneViewModel.ShapeFrame.Of(shape) is { } frame
            ? SizeCursor(viewport.ToScreen(frame.ToWorld(frame.HandleAt(spot))) - viewport.ToScreen(frame.ToWorld(frame.Center)))
            : Cursors.SizeAll;

    // Patrol turns slide left and right; points and new points go anywhere.
    private static Cursor PathCursor(PathHandle handle) => handle.Point?.Component is Patrol ? Cursors.SizeWE : Cursors.SizeAll;

    // A resize cursor out from the distance's middle toward the cursor.
    private Cursor DistanceCursor(SceneDistance distance, Point screen) => SizeCursor(screen - viewport.ToScreen(distance.Center));

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
}
