using System.Globalization;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Griff.Editing;
using Griff.Editor.Core.Workspace;
using Griff.Fonts;
using Griff.Rendering;
using Griff.Scenes;
using Point = System.Windows.Point;
using Vector = System.Windows.Vector;

namespace Griff.Editor.Features.SceneView;

/// <summary>
/// Draws the Scene view: the grid, what each entity shows (sprites, tilemaps, text, camera frames, particles, or a marker),
/// the lights, colliders, rooms, level guides and links, the selection with its handles, the tile palette while
/// painting, and the zoom. Where the handles go is <see cref="SceneHandleGeometry"/>'s; what the mouse is doing (a drag,
/// the handle under it) is <see cref="SceneViewInput"/>'s.
/// </summary>
internal sealed class SceneViewDrawing(SceneViewport viewport, SceneViewModel model, TilePalette palette, ISelection selection,
    IWorkspace workspace, SceneHandleGeometry handles, SceneViewInput input, SceneViewStyle style)
{
    private readonly Dictionary<string, BitmapSource?> _images = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Image, int Tile, int Columns, int Rows), BitmapSource?> _tileImages = [];
    private readonly Dictionary<(BitmapSource Image, int Rgb), BitmapSource> _tintedImages = [];
    private readonly Dictionary<(string Text, string? Font, TextAlign Align), (BitmapSource Image, Rect Rect)?> _textImages = [];
    private GlyphAtlas? _glyphs;
    private string? _glyphsRoot;
    private SceneDocument? _document;

    // The area a fill would cover, for the map's tiles and the cell it was worked out for.
    private (IReadOnlyList<string> Tiles, (int X, int Y) Cell)? _fillPreviewOf;
    private IReadOnlyList<(int X, int Y)> _fillPreview = [];

    /// <summary>Forgets the pictures read so far (another scene or prefab is showing).</summary>
    public void ForgetImages()
    {
        _images.Clear();
        _tileImages.Clear();
        _tintedImages.Clear();
        _textImages.Clear();
    }

    /// <summary>Draws the open tab's scene (or just the background, with none open).</summary>
    public void Draw(DrawingContext drawingContext, SceneDocument? document)
    {
        drawingContext.DrawRectangle(style.Brush("Brush.Surface.Document"), null, new Rect(viewport.Size));
        _document = document;
        if (_document is null)
            return;

        if (viewport.NeedsFit && viewport.Size.Width > 0)
            viewport.Fit(_document);

        DrawGrid(drawingContext);

        var parts = SceneViewModel.DrawParts(_document.Scene).ToList();
        // A map that sorts by Y has its tiles drawn a row at a time, each among the rest where the game draws it.
        var byRow = parts.Where(p => p.Depth is not null).Select(p => p.Entity).ToHashSet();
        foreach (var (entity, depth) in parts)
        {
            if (depth is { } row)
            {
                DrawTilemap(drawingContext, entity, entity.GetComponent<Tilemap>()!, row);
                continue;
            }

            if (entity.GetComponent<Tilemap>() is { } tilemap && !byRow.Contains(entity))
                DrawTilemap(drawingContext, entity, tilemap);
            if (entity.GetComponent<Sprite>() is { } sprite)
                DrawSprite(drawingContext, entity, sprite);
            if (entity.GetComponent<Text>() is { Value.Length: > 0 } text)
                DrawText(drawingContext, entity, text);
            if (entity.GetComponent<Camera>() is { } camera)
                DrawCameraFrame(drawingContext, entity, camera, selection.IsSelected(entity));
            if (entity.GetComponent<ParticleEmitter>() is { Particles.Count: > 0 } emitter)
                DrawParticles(drawingContext, entity, emitter);
            DrawOtherDrawables(drawingContext, entity, overWorld: false);

            // Entities with nothing solid to see (including emitters) get a marker so they can be found and clicked.
            if (entity.GetComponent<Sprite>() is null && entity.GetComponent<Tilemap>() is null &&
                entity.GetComponent<Text>() is null && entity.GetComponent<Camera>() is null)
                DrawMarker(drawingContext, entity);
        }

        foreach (var entity in _document.Scene.Entities.Where(SceneViewModel.IsShown))
            DrawOtherDrawables(drawingContext, entity, overWorld: true);

        DrawLights(drawingContext);
        DrawColliders(drawingContext);
        DrawRooms(drawingContext);
        DrawLevelGuides(drawingContext);
        DrawLinks(drawingContext);

        foreach (var selected in selection.Entities)
        {
            if (selected.Scene == _document.Scene)
                DrawSelection(drawingContext, selected, primary: selected == selection.Entity);
        }

        if (input.Drag == SceneDrag.Box)
            DrawBoxSelection(drawingContext);
        else
            DrawHandles(drawingContext);

        if (model.PaintTarget is { } painting)
            DrawPaintOverlay(drawingContext, painting);

        DrawZoomLabel(drawingContext);
    }

    private void DrawGrid(DrawingContext dc)
    {
        var minor = new Pen(style.Brush("Brush.Border.Subtle"), 1);
        var major = new Pen(style.Brush("Brush.Selection.Inactive"), 1);
        var topLeft = viewport.ToWorld(new Point(0, 0));
        var bottomRight = viewport.ToWorld(new Point(viewport.Size.Width, viewport.Size.Height));

        // Step through powers of ten so lines stay 8px or more apart at any zoom.
        var step = Math.Pow(10, Math.Ceiling(Math.Log10(8 / viewport.Zoom)));
        for (var x = Math.Floor(topLeft.X / step) * step; x <= bottomRight.X; x += step)
        {
            var sx = Math.Round(viewport.ToScreen(new Vector2((float)x, 0)).X) + 0.5;
            var isMajor = Math.Abs(Math.IEEERemainder(x, step * 10)) < step / 2;
            dc.DrawLine(isMajor ? major : minor, new Point(sx, 0), new Point(sx, viewport.Size.Height));
        }

        for (var y = Math.Floor(bottomRight.Y / step) * step; y <= topLeft.Y; y += step)
        {
            var sy = Math.Round(viewport.ToScreen(new Vector2(0, (float)y)).Y) + 0.5;
            var isMajor = Math.Abs(Math.IEEERemainder(y, step * 10)) < step / 2;
            dc.DrawLine(isMajor ? major : minor, new Point(0, sy), new Point(viewport.Size.Width, sy));
        }

        var origin = viewport.ToScreen(Vector2.Zero);
        dc.DrawLine(new Pen(style.Faded("Brush.Axis.X", 0.6), 1), new Point(0, Math.Round(origin.Y) + 0.5), new Point(viewport.Size.Width, Math.Round(origin.Y) + 0.5));
        dc.DrawLine(new Pen(style.Faded("Brush.Axis.Y", 0.6), 1), new Point(Math.Round(origin.X) + 0.5, 0), new Point(Math.Round(origin.X) + 0.5, viewport.Size.Height));
    }

    private void DrawSprite(DrawingContext dc, Entity entity, Sprite sprite)
    {
        var size = sprite.Size;
        var rect = new Rect(-size.X / 2, -size.Y / 2, size.X, size.Y);
        var matrix = viewport.ScreenMatrix(entity.Transform.WorldMatrix);

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
                var pen = new Pen(style.Brush("Brush.Status.Error"), 2 / viewport.Zoom);
                dc.DrawLine(pen, rect.TopLeft, rect.BottomRight);
                dc.DrawLine(pen, rect.TopRight, rect.BottomLeft);
            }
        }

        dc.Pop();
        dc.Pop();
    }

    // A map's tiles back to front, or with `depth` just that row of them (for a map that sorts by Y).
    private void DrawTilemap(DrawingContext dc, Entity entity, Tilemap tilemap, int? depth = null)
    {
        // A hair of overlap hides the anti-aliased seams WPF would draw between neighbouring tiles.
        var bleed = 0.5 / viewport.Zoom;
        var fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, tilemap.Tint.RByte, tilemap.Tint.GByte, tilemap.Tint.BByte));
        dc.PushTransform(new MatrixTransform(viewport.ScreenMatrix(entity.Transform.WorldMatrix)));
        // While one layer of a map is painted, its other layers fade back.
        var otherLayer = model.PaintTarget is { } painting && painting != tilemap && painting.BaseMap == tilemap.BaseMap;
        dc.PushOpacity(tilemap.Tint.A * (otherLayer ? 0.35 : 1));

        foreach (var (x, y, tile) in depth is { } row ? tilemap.CellsAtDepth(row) : tilemap.BackToFront())
        {
            var (center, size) = tilemap.PictureOf(x, y);
            var rect = new Rect(center.X - size.X / 2 - bleed, center.Y - size.Y / 2 - bleed, size.X + bleed * 2, size.Y + bleed * 2);
            // Autotiles show the piece that joins their neighbors, as in the game.
            if (TileImage(tilemap, tilemap.ShownTile(x, y, tile)) is { } image)
            {
                // Images are stored top-down; flip each tile about its own center.
                dc.PushTransform(new MatrixTransform(1, 0, 0, -1, 0, 2 * center.Y));
                dc.DrawImage(Tinted(image, tilemap.Tint), rect);
                dc.Pop();
            }
            else if (tilemap.Shape != TileShape.Square)
            {
                // A diamond or hexagon, from its corners.
                dc.DrawGeometry(fill, new Pen(fill, bleed * 2), Polygon(tilemap.OutlineOf(x, y, x, y)));
            }
            else
            {
                dc.DrawRectangle(fill, null, rect);
            }
        }

        dc.Pop();
        dc.Pop();
    }

    // Whatever else an entity draws (a bar, a slider, a rope or wire over the world, a script's shapes) as the game draws it,
    // from its quads. Sprites, tilemaps, text and particles have their own drawing above, and a component that draws a
    // tilemap in place of its tiles (a match-3 board) is left to the game: here you paint the tiles.
    private void DrawOtherDrawables(DrawingContext dc, Entity entity, bool overWorld)
    {
        foreach (var component in entity.Components)
        {
            if (component is not IDrawable drawable || component is Sprite or Tilemap or Text or ParticleEmitter or IDrawsTilemap ||
                drawable.DrawStep >= DrawSteps.OverWorld != overWorld)
                continue;
            IReadOnlyList<SpriteDraw> draws;
            try
            {
                var (min, max) = (viewport.ToWorld(new Point(0, viewport.Size.Height)), viewport.ToWorld(new Point(viewport.Size.Width, 0)));
                draws = SceneRenderer.DrawsOf(drawable, new Box((min + max) / 2, (max - min) / 2), (float)(2 / viewport.Zoom));
            }
            catch (Exception)
            {
                // A script's Draw that fails in the editor draws nothing here; the game reports it when it runs.
                continue;
            }

            DrawQuads(dc, draws);
        }
    }

    // Quads as the game draws them: images (or their part of one) tinted, solid colors, and text.
    private void DrawQuads(DrawingContext dc, IReadOnlyList<SpriteDraw> draws)
    {
        foreach (var draw in draws)
        {
            if (draw.HoleRadius > 0 || draw.Tint.A <= 0)
                continue;
            dc.PushTransform(new MatrixTransform(viewport.ScreenMatrix(draw.World)));
            if (draw.Text is { Length: > 0 } text)
            {
                DrawTextImage(dc, text, draw.Font, draw.Align, draw.Tint);
            }
            else if (LoadImage(draw.Image) is { } loaded)
            {
                // Images are stored top-down; flip Y back since world space is Y-up.
                dc.PushTransform(new MatrixTransform(1, 0, 0, -1, 0, 0));
                dc.PushOpacity(draw.Tint.A);
                var brush = new ImageBrush(Tinted(loaded, draw.Tint))
                {
                    Viewbox = new Rect(draw.Source.X, draw.Source.Y, draw.Source.Z, draw.Source.W),
                    ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                };
                RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
                dc.DrawRectangle(brush, null, new Rect(-0.5, -0.5, 1, 1));
                dc.Pop();
                dc.Pop();
            }
            else
            {
                var color = System.Windows.Media.Color.FromArgb(draw.Tint.AByte, draw.Tint.RByte, draw.Tint.GByte, draw.Tint.BByte);
                dc.DrawRectangle(new SolidColorBrush(color), null, new Rect(-0.5, -0.5, 1, 1));
            }

            dc.Pop();
        }
    }

    // Particle images are tinted like sprites; without one, particles are tinted squares.
    private void DrawParticles(DrawingContext dc, Entity entity, ParticleEmitter emitter)
    {
        var image = LoadImage(emitter.Image);
        foreach (var draw in emitter.ParticleDraws())
        {
            var center = viewport.ToScreen(new Vector2(draw.World.M41, draw.World.M42));
            var half = draw.World.M11 * viewport.Zoom / 2;
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

    // As the game draws it: the text's glyphs, from the same fonts and laid out the same way, scaled to its world size.
    private void DrawText(DrawingContext dc, Entity entity, Text text)
    {
        var world = Matrix4x4.CreateScale(text.Size, text.Size, 1) * entity.Transform.WorldMatrix;
        dc.PushTransform(new MatrixTransform(viewport.ScreenMatrix(world)));
        DrawTextImage(dc, text.Value!, string.IsNullOrWhiteSpace(text.Font) ? null : text.Font, text.Align, text.Color);
        dc.Pop();
    }

    // Text one line high at the origin, tinted, from an image of its glyphs as the game lays them out (see GlyphAtlas).
    private void DrawTextImage(DrawingContext dc, string text, string? font, TextAlign align, Griff.Color tint)
    {
        if (TextImage(text, font, align) is not var (image, rect))
            return;

        // Images are stored top-down; flip Y back since world space is Y-up.
        dc.PushTransform(new MatrixTransform(1, 0, 0, -1, 0, 0));
        dc.PushOpacity(tint.A);
        dc.DrawImage(Tinted(image, tint), rect);
        dc.Pop();
        dc.Pop();
    }

    // The image of some text's glyphs and where it goes (in lines, Y down as images are), made once and kept; null for
    // text with nothing to draw.
    private (BitmapSource Image, Rect Rect)? TextImage(string text, string? font, TextAlign align)
    {
        var root = workspace.Project?.AssetsPath;
        if (_glyphs is null || _glyphsRoot != root)
        {
            (_glyphs, _glyphsRoot) = (new GlyphAtlas(root), root);
            _textImages.Clear();
        }

        if (_glyphs.RefreshChanged())
            _textImages.Clear();
        var key = (text, font, align);
        if (_textImages.TryGetValue(key, out var cached))
            return cached;
        if (_textImages.Count >= 256)
            _textImages.Clear();

        var (rgba, width, height, topLeft) = _glyphs.Rasterize(text, font, align);
        _glyphs.EndFrame();
        (BitmapSource, Rect)? made = null;
        if (width > 0 && height > 0)
        {
            var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, rgba, width * 4);
            image.Freeze();
            made = (image, new Rect(topLeft.X, -topLeft.Y, (double)width / TextRaster.LinePixels, (double)height / TextRaster.LinePixels));
        }

        _textImages[key] = made;
        return made;
    }

    private void DrawCameraFrame(DrawingContext dc, Entity entity, Camera camera, bool selected)
    {
        var height = camera.Size * 2;
        var width = height * SceneViewModel.AspectOf(entity);
        var rect = new Rect(-width / 2, -height / 2, width, height);
        var pen = new Pen(selected ? style.Brush("Brush.Accent") : style.Brush("Brush.Text.Tertiary"), 1.5 / viewport.Zoom)
        {
            DashStyle = new DashStyle([6, 4], 0),
        };

        dc.PushTransform(new MatrixTransform(viewport.ScreenMatrix(entity.Transform.WorldMatrix)));
        dc.DrawRectangle(null, pen, rect);
        dc.Pop();

        var corner = viewport.ToScreen(ToVector2(Vector3.Transform(new Vector3((float)rect.Left, (float)rect.Bottom, 0), entity.Transform.WorldMatrix)));
        dc.DrawText(style.Text(entity.Name, "Brush.Text.Tertiary"), new Point(corner.X + 4, corner.Y - 18));
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
                var radius = light.Radius * viewport.Zoom;
                dc.DrawEllipse(glow, null, viewport.ToScreen(light.WorldCenter), radius, radius);
            }
        }
    }

    // Rooms: long-dashed outlines with their names, so a level's screens can be laid out.
    private void DrawRooms(DrawingContext dc)
    {
        var pen = new Pen(style.Brush("Brush.Status.Info"), 1.5) { DashStyle = new DashStyle([10, 5], 0) };
        foreach (var entity in _document!.Scene.Entities.Where(SceneViewModel.IsShown))
        {
            foreach (var room in entity.GetComponents<Room>())
            {
                var area = room.Area;
                var topLeft = viewport.ToScreen(new Vector2(area.Min.X, area.Max.Y));
                dc.DrawRectangle(null, pen, new Rect(topLeft, viewport.ToScreen(new Vector2(area.Max.X, area.Min.Y))));
                dc.DrawText(style.Text(entity.Name, "Brush.Status.Info"), new Point(topLeft.X + 4, topLeft.Y + 2));
            }
        }
    }

    // Physics shapes in green; triggers dashed; polygons whose points don't make a proper shape red.
    private void DrawColliders(DrawingContext dc)
    {
        var solid = new Pen(style.Brush("Brush.Status.Success"), 1.5);
        var trigger = new Pen(style.Brush("Brush.Status.Success"), 1.5) { DashStyle = new DashStyle([4, 3], 0) };
        var clickable = new Pen(style.Brush("Brush.Status.Info"), 1.5) { DashStyle = new DashStyle([2, 2], 0) };
        var cut = new Pen(style.Brush("Brush.Status.Success"), 1) { DashStyle = new DashStyle([1, 3], 0) };
        var broken = new Pen(style.Brush("Brush.Status.Error"), 1.5);
        var brokenGiven = new Pen(style.Brush("Brush.Status.Error"), 1) { DashStyle = new DashStyle([4, 3], 0) };

        // With walls on an isometric map, a body meets them with the diamond under its collider: drawn inside it.
        var isometric = _document!.Scene.Entities.Any(e => e.IsActive && e.GetComponent<Tilemap>() is { Solid: true, Shape: TileShape.Isometric });
        foreach (var entity in _document!.Scene.Entities.Where(SceneViewModel.IsShown))
        {
            if (isometric && entity.GetComponent<Body>() is not null && entity.GetComponent<Collider>() is { IsTrigger: false } footed)
            {
                var bounds = Griff.Runtime.Physics.BoundsOf(footed);
                var (across, down) = (new Vector2(bounds.HalfSize.X, 0), new Vector2(0, bounds.HalfSize.X / 2));
                DrawOutline([bounds.Center + down, bounds.Center + across, bounds.Center - down, bounds.Center - across], solid);
            }

            // Buttons and sliders: where they can be clicked and dragged.
            foreach (var button in entity.GetComponents<Griff.Scenes.Button>())
                DrawBox(Griff.Runtime.Game.AreaOf(button), clickable);
            foreach (var slider in entity.GetComponents<Griff.Scenes.Slider>())
                DrawBox(Griff.Runtime.Game.AreaOf(slider), clickable);

            foreach (var collider in entity.GetComponents<BoxCollider>())
            {
                // Turning with a body that rotates, it's drawn as it's turned.
                if (collider.WorldShape.Axis != Vector2.UnitX)
                {
                    var (a, b, c, d) = collider.WorldCorners;
                    DrawOutline([a, b, c, d], collider.IsTrigger || collider.OneWay ? trigger : solid);
                    if (collider.OneWay && !collider.IsTrigger)
                        dc.DrawLine(new Pen(solid.Brush, 3), viewport.ToScreen(d), viewport.ToScreen(c));
                    continue;
                }

                if (collider.OneWay && !collider.IsTrigger)
                    DrawOneWay(Griff.Runtime.Physics.BoundsOf(collider));
                else
                    DrawBox(Griff.Runtime.Physics.BoundsOf(collider), collider.IsTrigger ? trigger : solid);
            }

            foreach (var collider in entity.GetComponents<CircleCollider>())
            {
                var circle = Griff.Runtime.Physics.BoundsOf(collider);
                var radius = circle.Radius * viewport.Zoom;
                dc.DrawEllipse(null, collider.IsTrigger ? trigger : solid, viewport.ToScreen(circle.Center), radius, radius);
            }

            // A polygon as the shape it is, with faint lines where a concave one is cut into convex pieces. One whose points
            // don't make a proper shape is drawn in red as it's fixed up, its points as written dashed over it.
            foreach (var collider in entity.GetComponents<PolygonCollider>())
            {
                if (collider.PieceCount > 1)
                {
                    for (var piece = 0; piece < collider.PieceCount; piece++)
                        DrawOutline(collider.PieceCorners(piece).ToArray(), cut);
                }

                var bad = collider.Problem is not null;
                DrawOutline(collider.WorldPoints.ToArray(), bad ? broken : collider.IsTrigger ? trigger : solid);
                if (bad && collider.GivenPoints.Count >= 2)
                    DrawOutline(collider.GivenPoints.Select(collider.ToWorld).ToArray(), brokenGiven);
            }

            // A hinge: a pin where it is.
            foreach (var hinge in entity.GetComponents<HingeJoint>())
            {
                var pin = viewport.ToScreen(hinge.WorldAnchor);
                dc.DrawEllipse(null, solid, pin, 5, 5);
                dc.DrawEllipse(solid.Brush, null, pin, 1.5, 1.5);
            }

            // A rope: a ring where it's tied on, and one at its other end (tied to something or the world; not a loose one).
            foreach (var rope in entity.GetComponents<Rope>())
            {
                dc.DrawEllipse(null, solid, viewport.ToScreen(rope.WorldAnchor), 3.5, 3.5);
                if (!rope.EndIsLoose)
                    dc.DrawEllipse(null, solid, viewport.ToScreen(rope.WorldOtherAnchor), 3.5, 3.5);
            }

            // An isometric map's walls: a run of solid diamonds along each row; a hex map's, a run of solid hexagons.
            foreach (var tilemap in entity.GetComponents<Tilemap>().Where(t => t.Shape == TileShape.Isometric))
            {
                foreach (var outline in Griff.Runtime.Physics.IsometricShapes(tilemap))
                    DrawOutline(outline, solid);
            }

            foreach (var tilemap in entity.GetComponents<Tilemap>().Where(t => t.Shape == TileShape.Hex))
            {
                foreach (var outline in Griff.Runtime.Physics.HexShapes(tilemap))
                    DrawOutline(outline, solid);
            }

            foreach (var tilemap in entity.GetComponents<Tilemap>().Where(t => t.Shape == TileShape.Square))
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
                        context.BeginFigure(viewport.ToScreen(low), false, true);
                        context.LineTo(viewport.ToScreen(high), true, false);
                        context.LineTo(viewport.ToScreen(new Vector2(high.X, low.Y)), true, false);
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
                    DrawBox(new Griff.Box((min + max) / 2, Vector2.Abs(max - min) / 2), trigger);
                }
            }
        }

        void DrawOutline(Vector2[] world, Pen pen) =>
            dc.DrawGeometry(null, pen, Polygon(world.Select(p => viewport.ToScreen(p)).Select(p => new Vector2((float)p.X, (float)p.Y)).ToArray()));

        // One-way platforms: a solid top (the side that blocks) over a dashed outline.
        void DrawOneWay(Griff.Box box)
        {
            DrawBox(box, trigger);
            dc.DrawLine(new Pen(solid.Brush, 3), viewport.ToScreen(new Vector2(box.Min.X, box.Max.Y)), viewport.ToScreen(box.Max));
        }

        void DrawBox(Griff.Box box, Pen pen)
        {
            var topLeft = viewport.ToScreen(new Vector2(box.Min.X, box.Max.Y));
            var bottomRight = viewport.ToScreen(new Vector2(box.Max.X, box.Min.Y));
            dc.DrawRectangle(null, pen, new Rect(topLeft, bottomRight));
        }
    }

    // Where movers and grid patrols go and where patrols turn, always; for the selection, its distances (how far a light reaches, a chaser notices, a unit sees),
    // which way a shooter fires, and how far a player jumps. Where a door leads and what a switch turns on are links.
    private void DrawLevelGuides(DrawingContext dc)
    {
        var guide = style.Brush("Brush.Status.Warning");
        var dashed = new Pen(guide, 1.5) { DashStyle = new DashStyle([4, 3], 0) };
        var dotted = new Pen(guide, 1.5) { DashStyle = new DashStyle([1, 3], 0) };
        foreach (var entity in _document!.Scene.Entities.Where(SceneViewModel.IsShown))
        {
            foreach (var mover in entity.GetComponents<Mover>())
            {
                var points = SceneViewModel.PathOf(mover).Select(viewport.ToScreen).ToList();
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
                var points = SceneViewModel.RouteOf(patrol).Select(viewport.ToScreen).ToList();
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
                var isPrimary = entity == selection.Entity;
                if (patrol.Left <= 0 && patrol.Right <= 0 && !isPrimary)
                    continue;
                var route = SceneViewModel.RouteOf(patrol);
                var home = viewport.ToScreen(route.Home);
                foreach (var (end, open) in new[] { (route.Left, route.LeftOpen), (route.Right, route.RightOpen) })
                {
                    var tip = viewport.ToScreen(end);
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

        if (selection.Entity is not { } selected || selected.Scene != _document.Scene)
            return;
        var here = ToVector2(selected.Transform.WorldPosition);
        foreach (var distance in model.Distances())
            dc.DrawEllipse(null, dashed, viewport.ToScreen(distance.Center), distance.Value * viewport.Zoom, distance.Value * viewport.Zoom);
        foreach (var shooter in selected.GetComponents<Shooter>().Where(s => string.IsNullOrWhiteSpace(s.Target) && s.Direction != Vector2.Zero))
            dc.DrawLine(dashed, viewport.ToScreen(here + shooter.Offset), viewport.ToScreen(here + shooter.Offset + Vector2.Normalize(shooter.Direction) * 2));

        // How far a player jumps, each way, and where a bouncer sends things: under the default gravity.
        foreach (var movement in selected.GetComponents<PlatformerMovement>())
        {
            var gravity = -Griff.Runtime.Physics.DefaultGravity.Y * (selected.GetComponent<Body>()?.GravityScale ?? 1);
            var jump = movement.JumpSpeedFor(gravity);
            foreach (var side in new[] { -1, 1 })
                DrawArc(dc, here, new Vector2(side * movement.Speed, jump), gravity, movement.FallMultiplier, movement.MaxFallSpeed);
            dc.DrawLine(dashed, viewport.ToScreen(here + new Vector2(-0.6f, movement.JumpHeight)), viewport.ToScreen(here + new Vector2(0.6f, movement.JumpHeight)));
        }

        foreach (var bouncer in selected.GetComponents<Bouncer>())
            DrawArc(dc, here, bouncer.Way * bouncer.Strength, -Griff.Runtime.Physics.DefaultGravity.Y, 1, 0);

        // How far a hinge with limits swings: a wedge round its anchor from its lower limit to its upper, and a line the
        // way it points now.
        foreach (var hinge in selected.GetComponents<HingeJoint>())
        {
            var arc = SceneViewModel.ArcOf(hinge);
            var center = viewport.ToScreen(arc.Anchor);
            dc.DrawLine(dashed, center, viewport.ToScreen(arc.At(hinge.Angle)));
            if (!hinge.Limit)
                continue;
            var (lower, upper) = (Math.Clamp(hinge.LowerAngle, -180, 180), Math.Clamp(hinge.UpperAngle, -180, 180));
            if (lower > upper)
                (lower, upper) = (upper, lower);
            var wedge = new StreamGeometry();
            using (var context = wedge.Open())
            {
                context.BeginFigure(center, true, true);
                context.LineTo(viewport.ToScreen(arc.At(lower)), true, false);
                var steps = Math.Max(2, (int)MathF.Ceiling((upper - lower) / 5));
                for (var i = 1; i <= steps; i++)
                    context.LineTo(viewport.ToScreen(arc.At(lower + (upper - lower) * i / steps)), true, false);
            }

            var fill = guide.Clone();
            fill.Opacity = 0.15;
            dc.DrawGeometry(fill, new Pen(guide, 1.5), wedge);
        }
    }

    // The selection's links (see SceneViewModel.Links): a dashed arrow from each field's entity to the one it names,
    // with the fields named halfway along.
    private void DrawLinks(DrawingContext dc)
    {
        var accent = style.Brush("Brush.Accent");
        var chrome = style.Brush("Brush.Surface.Chrome");
        var pen = new Pen(accent, 1.5) { DashStyle = new DashStyle([5, 3], 0) };
        var border = new Pen(accent, 1);
        foreach (var arrow in handles.LinkArrows())
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

            var label = style.Text(arrow.Fields, "Brush.Text.Secondary");
            var place = new Point(arrow.Middle.X - label.Width / 2, arrow.Middle.Y - label.Height / 2);
            dc.DrawRoundedRectangle(chrome, border, new Rect(place.X - 4, place.Y - 1, label.Width + 8, label.Height + 2), 3, 3);
            dc.DrawText(label, place);
        }
    }

    // A dashed arc: where something leaving `from` at `velocity` goes under `gravity` (heavier on the way down), for 1.5 seconds
    // or until it has fallen well below where it started.
    private void DrawArc(DrawingContext dc, Vector2 from, Vector2 velocity, float gravity, float fallMultiplier, float maxFall)
    {
        var pen = new Pen(style.Brush("Brush.Status.Warning"), 1.5) { DashStyle = new DashStyle([4, 3], 0) };
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(viewport.ToScreen(from), false, false);
            var (position, v) = (from, velocity);
            const float dt = 1 / 30f;
            for (var t = 0f; t < 1.5f && position.Y > from.Y - 4; t += dt)
            {
                v.Y -= gravity * (v.Y < 0 ? Math.Max(1, fallMultiplier) : 1) * dt;
                if (maxFall > 0)
                    v.Y = Math.Max(v.Y, -maxFall);
                position += v * dt;
                context.LineTo(viewport.ToScreen(position), true, false);
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    // A closed shape through points in whatever space is pushed (a tilemap's own, for its cells).
    private static StreamGeometry Polygon(Vector2[] points)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(points[0].X, points[0].Y), true, true);
            for (var i = 1; i < points.Length; i++)
                context.LineTo(new Point(points[i].X, points[i].Y), true, false);
        }

        geometry.Freeze();
        return geometry;
    }

    // While painting: the grid's extent, the cell under the cursor (or the rectangle being dragged, or the area a fill
    // would cover), a palette of the tileset's tiles, and the tools.
    private void DrawPaintOverlay(DrawingContext dc, Tilemap tilemap)
    {
        var accent = style.Brush("Brush.Accent");
        dc.PushTransform(new MatrixTransform(viewport.ScreenMatrix(tilemap.Entity!.Transform.WorldMatrix)));

        // Squares on a square map, diamonds on an isometric one, hexagons on a hex one.
        var extent = Polygon(tilemap.OutlineOf(0, 0, Math.Max(1, tilemap.Width) - 1, Math.Max(1, tilemap.Height) - 1));
        dc.DrawGeometry(null, new Pen(style.Brush("Brush.Text.Tertiary"), 1 / viewport.Zoom) { DashStyle = new DashStyle([4, 4], 0) }, extent);

        var color = input.Erasing ? "Brush.Status.Error" : "Brush.Accent";
        var outline = new Pen(style.Brush(color), 2 / viewport.Zoom);
        Geometry Cells(int left, int top, int right, int bottom) => Polygon(tilemap.OutlineOf(left, top, right, bottom));
        if (input.RectangleFrom is var (fx, fy) && input.HoverCell is var (tx, ty))
        {
            var box = Cells(Math.Min(fx, tx), Math.Min(fy, ty), Math.Max(fx, tx), Math.Max(fy, ty));
            dc.DrawGeometry(style.Faded(color, 0.2), outline, box);
        }
        else if (input.HoverCell is var (hx, hy))
        {
            if (model.TileTool == TileTool.Fill && input.Drag == SceneDrag.None)
            {
                // The area a click would fill, when it's small enough to draw cell by cell.
                if (_fillPreviewOf is not var (previewTiles, previewCell) || previewTiles != tilemap.Tiles || previewCell != (hx, hy))
                {
                    var region = tilemap.FillRegion(hx, hy);
                    (_fillPreviewOf, _fillPreview) = ((tilemap.Tiles, (hx, hy)), region.Count <= 4096 ? region : []);
                }

                var shade = style.Faded(color, 0.2);
                foreach (var (x, y) in _fillPreview)
                    dc.DrawGeometry(shade, null, Cells(x, y, x, y));
            }

            dc.DrawGeometry(null, outline, Cells(hx, hy, hx, hy));
        }

        dc.Pop();

        // Palette, top left: a tab for each tileset (the one shown picked out) and + to add one, then its tiles.
        var layout = TilePaletteLayout.Of(palette, tilemap, style.Measure);
        var panel = layout.Panel;
        dc.DrawRoundedRectangle(style.Brush("Brush.Surface.Chrome"), new Pen(style.Brush("Brush.Border.Default"), 1), panel, 4, 4);
        foreach (var (box, tileset) in layout.Tabs)
        {
            var shown = tileset == layout.Shown;
            dc.DrawRoundedRectangle(shown ? accent : null, new Pen(shown ? accent : style.Brush("Brush.Border.Default"), 1), box, 3, 3);
            var name = style.Text(tileset.Name, shown ? "Brush.Text.OnAccent" : "Brush.Text.Primary");
            dc.DrawText(name, new Point(box.X + (box.Width - name.Width) / 2, box.Y + (box.Height - name.Height) / 2));
        }

        dc.DrawRoundedRectangle(null, new Pen(style.Brush("Brush.Border.Default"), 1), layout.AddTab, 3, 3);
        var plus = style.Text("+", "Brush.Text.Primary");
        dc.DrawText(plus, new Point(layout.AddTab.X + (layout.AddTab.Width - plus.Width) / 2, layout.AddTab.Y + (layout.AddTab.Height - plus.Height) / 2));

        var fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, tilemap.Tint.RByte, tilemap.Tint.GByte, tilemap.Tint.BByte));
        for (var tile = layout.Shown.First; tile < layout.Shown.First + layout.Count; tile++)
        {
            if (layout.CellBounds(tile) is not { } cell)
                continue;
            var rect = cell;
            rect.Inflate(-2, -2);
            if (TileImage(tilemap, tile) is { } image)
                dc.DrawImage(image, rect);
            else
                dc.DrawRectangle(fill, null, rect);
            if (tile == model.BrushTile)
                dc.DrawRectangle(null, new Pen(accent, 2), cell);
        }

        // A thin bar down the right of the tiles when they scroll: how far down the ones in sight are.
        if (layout.Rows > layout.VisibleRows)
        {
            var track = new Rect(panel.Right - 5, layout.CellsTop, 3, layout.VisibleRows * TilePaletteLayout.Cell);
            var thumb = new Rect(track.X, track.Y + track.Height * layout.Scroll / layout.Rows, track.Width, track.Height * layout.VisibleRows / layout.Rows);
            dc.DrawRectangle(style.Brush("Brush.Border.Default"), null, track);
            dc.DrawRectangle(style.Brush("Brush.Text.Secondary"), null, thumb);
        }

        // The tools, under the layout.
        foreach (var tool in Enum.GetValues<TileTool>())
        {
            var button = layout.ToolBounds(tool);
            var picked = tool == model.TileTool;
            dc.DrawRoundedRectangle(picked ? accent : style.Brush("Brush.Surface.Chrome"), new Pen(picked ? accent : style.Brush("Brush.Border.Default"), 1), button, 3, 3);
            var label = style.Text($"{tool}  {TilePalette.KeyOf(tool)}", picked ? "Brush.Text.OnAccent" : "Brush.Text.Primary");
            dc.DrawText(label, new Point(button.X + (button.Width - label.Width) / 2, button.Y + (button.Height - label.Height) / 2));
        }

        // The map's layers under the tools, the one being painted picked out, and + Layer to add one.
        foreach (var (box, layer) in layout.LayerButtons)
        {
            var picked = layer == tilemap;
            dc.DrawRoundedRectangle(picked ? accent : style.Brush("Brush.Surface.Chrome"), new Pen(picked ? accent : style.Brush("Brush.Border.Default"), 1), box, 3, 3);
            var label = style.Text(layer.Entity!.Name, picked ? "Brush.Text.OnAccent" : "Brush.Text.Primary");
            dc.DrawText(label, new Point(box.X + (box.Width - label.Width) / 2, box.Y + (box.Height - label.Height) / 2));
        }

        dc.DrawRoundedRectangle(style.Brush("Brush.Surface.Chrome"), new Pen(style.Brush("Brush.Border.Default"), 1), layout.AddLayer, 3, 3);
        var addLayer = style.Text(TilePaletteLayout.AddLayerLabel, "Brush.Text.Primary");
        dc.DrawText(addLayer, new Point(layout.AddLayer.X + (layout.AddLayer.Width - addLayer.Width) / 2, layout.AddLayer.Y + (layout.AddLayer.Height - addLayer.Height) / 2));

        var hint = style.Text(palette.Hint, "Brush.Text.Secondary");
        dc.DrawText(hint, new Point(panel.Left + 6, layout.AddLayer.Bottom + 4));
    }

    private void DrawMarker(DrawingContext dc, Entity entity)
    {
        var center = viewport.ToScreen(ToVector2(entity.Transform.WorldPosition));
        var pen = new Pen(entity.IsEnabled ? style.Brush("Brush.Text.Secondary") : style.Brush("Brush.Text.Disabled"), 1.5);
        dc.DrawEllipse(null, pen, center, SceneHandleGeometry.MarkerRadius, SceneHandleGeometry.MarkerRadius);
        dc.DrawLine(pen, new Point(center.X - 3, center.Y), new Point(center.X + 3, center.Y));
        dc.DrawLine(pen, new Point(center.X, center.Y - 3), new Point(center.X, center.Y + 3));
    }

    /// <summary>Outlines a selected entity; the primary one also gets its pivot handle.</summary>
    private void DrawSelection(DrawingContext dc, Entity entity, bool primary)
    {
        var accent = style.Brush("Brush.Accent");
        if (SceneViewModel.Outline(entity) is { } bounds)
        {
            var corners = bounds.Select(p => viewport.ToScreen(p)).ToArray();
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(corners[0], isFilled: false, isClosed: true);
                g.PolyLineTo(corners[1..], isStroked: true, isSmoothJoin: false);
            }

            // Dark halo under the accent line keeps the outline visible on any sprite color.
            dc.DrawGeometry(null, new Pen(style.Brush("Brush.Surface.Chrome"), 4), geometry);
            dc.DrawGeometry(null, new Pen(accent, 2), geometry);
        }
        else
        {
            // Entities with nothing to outline get a ring around their marker.
            var center = viewport.ToScreen(ToVector2(entity.Transform.WorldPosition));
            dc.DrawEllipse(null, new Pen(style.Brush("Brush.Surface.Chrome"), 4), center, SceneHandleGeometry.MarkerRadius + 3, SceneHandleGeometry.MarkerRadius + 3);
            dc.DrawEllipse(null, new Pen(accent, 2), center, SceneHandleGeometry.MarkerRadius + 3, SceneHandleGeometry.MarkerRadius + 3);
        }

        if (!primary)
            return;

        // Pivot handle with axis arms.
        var pivot = viewport.ToScreen(ToVector2(entity.Transform.WorldPosition));
        dc.DrawLine(new Pen(style.Brush("Brush.Axis.X"), 2), pivot, new Point(pivot.X + 28, pivot.Y));
        dc.DrawLine(new Pen(style.Brush("Brush.Axis.Y"), 2), pivot, new Point(pivot.X, pivot.Y - 28));
        dc.DrawEllipse(accent, null, pivot, 3.5, 3.5);
    }

    // The primary selection's handles: the rotate knob, then the scale or resize squares over the root of its stem, the
    // knobs on its distances' circles, and those on its path.
    private void DrawHandles(DrawingContext dc)
    {
        if (handles.RotateHandle() is var (root, knob))
            DrawRotateHandle(dc, root, knob);
        DrawScaleHandles(dc);
        DrawResizeHandles(dc);
        DrawDistanceHandles(dc);
        DrawVectorHandles(dc);
        DrawPathHandles(dc);
    }

    // With Distance handles out, each offset as a dashed line out to its knob and each direction as an arrow, in the
    // guides' color, named by the knob; while dragging, what it's set to.
    private void DrawVectorHandles(DrawingContext dc)
    {
        var color = style.Brush("Brush.Status.Warning");
        var chrome = style.Brush("Brush.Surface.Chrome");
        var halo = new Pen(chrome, 4);
        foreach (var (vector, point, origin) in handles.VectorHandles())
        {
            var dragging = input.Drag == SceneDrag.Vector && input.VectorKnob?.Vector.Key == vector.Key;
            var active = dragging || input.Drag == SceneDrag.None && input.OverVector == vector.Key;
            var line = new Pen(color, 1.5);
            if (!vector.IsDirection)
                line.DashStyle = new DashStyle([3, 3], 0);
            dc.DrawLine(halo, origin, point);
            dc.DrawLine(line, origin, point);
            if (vector.IsDirection && (point - origin).Length > 1)
            {
                // An arrowhead just short of the knob.
                var way = point - origin;
                way.Normalize();
                var tip = point - way * (SceneHandleGeometry.HandleRadius + 2);
                var side = new System.Windows.Vector(-way.Y, way.X);
                var head = new StreamGeometry();
                using (var g = head.Open())
                {
                    g.BeginFigure(tip, true, true);
                    g.LineTo(tip - way * 8 + side * 4, true, false);
                    g.LineTo(tip - way * 8 - side * 4, true, false);
                }

                head.Freeze();
                dc.DrawGeometry(color, null, head);
            }

            var radius = SceneHandleGeometry.HandleRadius + (active ? 1.5 : 0);
            dc.DrawEllipse(chrome, halo, point, radius, radius);
            dc.DrawEllipse(active ? color : chrome, new Pen(color, 2), point, radius, radius);

            var name = $"{vector.Component.GetType().Name} {vector.Property.Name}";
            var label = style.Text(dragging && vector.Property.GetValue(vector.Component) is System.Numerics.Vector2 v
                ? $"{name} {v.X.ToString("0.##", CultureInfo.CurrentCulture)}, {v.Y.ToString("0.##", CultureInfo.CurrentCulture)}"
                : name, active ? "Brush.Text.Primary" : "Brush.Text.Secondary");
            var place = new Point(point.X + 12, point.Y - label.Height / 2);
            dc.DrawRoundedRectangle(chrome, null, new Rect(place.X - 4, place.Y - 1, label.Width + 8, label.Height + 2), 3, 3);
            dc.DrawText(label, place);
        }
    }

    // A knob on each of the primary selection's mover and grid patrol points (numbered) and patrol turns (hollow while it walks on to a
    // wall), and a small plus between points and past the last to drag out a new one, in the guides' color. Hovering says
    // what a knob does; dragging, where it is.
    private void DrawPathHandles(DrawingContext dc)
    {
        var color = style.Brush("Brush.Status.Warning");
        var chrome = style.Brush("Brush.Surface.Chrome");
        var halo = new Pen(chrome, 4);
        string Number(float value) => value.ToString("0.##", CultureInfo.CurrentCulture);
        foreach (var handle in handles.PathHandles())
        {
            var dragging = input.Drag == SceneDrag.Path && input.PathKey == handle.Key;
            var hovered = input.Drag == SceneDrag.None && input.OverPath == handle.Key;
            var active = dragging || hovered;
            if (handle.Point is not { } point)
            {
                var small = SceneHandleGeometry.HandleSquare + (active ? 1 : 0);
                dc.DrawEllipse(chrome, halo, handle.Screen, small, small);
                dc.DrawEllipse(active ? color : chrome, new Pen(color, 1.5), handle.Screen, small, small);
                var plus = new Pen(active ? chrome : color, 1.5);
                dc.DrawLine(plus, handle.Screen + new Vector(-2, 0), handle.Screen + new Vector(2, 0));
                dc.DrawLine(plus, handle.Screen + new Vector(0, -2), handle.Screen + new Vector(0, 2));
                if (hovered)
                    PathLabel(style.Text("Drag to add a point", "Brush.Text.Primary"), handle.Screen);
                continue;
            }

            var radius = SceneHandleGeometry.HandleRadius + (active ? 1.5 : 0);
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
            else if (point.Component is HingeJoint hinge)
            {
                if (point.Index == 0)
                    text = dragging ? $"Hinge  ·  {Number(hinge.Anchor.X)}, {Number(hinge.Anchor.Y)}" : hovered ? "Hinge  ·  drag to move" : "Hinge";
                else
                {
                    var (side, value) = point.Index < 0 ? ("Lower", hinge.LowerAngle) : ("Upper", hinge.UpperAngle);
                    text = dragging ? $"{side} {Number(value)}°" : hovered ? $"{side} {Number(value)}°  ·  double-click to clear limits" : side;
                }
            }
            else if (point.Component is Rope rope)
            {
                var tied = rope.EndIsLoose ? "loose" : string.IsNullOrWhiteSpace(rope.To) ? "to the world" : $"to {rope.To}";
                text = point.Index switch
                {
                    0 => dragging ? $"Rope  ·  {Number(rope.Anchor.X)}, {Number(rope.Anchor.Y)}" : hovered ? "Rope  ·  drag to move" : "Rope",
                    1 => dragging ? $"End {tied}  ·  {Number(rope.OtherAnchor.X)}, {Number(rope.OtherAnchor.Y)}" : hovered ? $"End {tied}  ·  drag to move" : "End",
                    _ => dragging ? $"Length {Number(rope.Length)}" : hovered ? $"Length {Number(rope.PlacedLength)}  ·  drag to lengthen, double-click for as far apart as its ends" : $"Length {Number(rope.PlacedLength)}",
                };
            }
            else
            {
                var offset = point.Component switch
                {
                    GridPatrol grid => new Vector2(grid.Path[point.Index].X, grid.Path[point.Index].Y),
                    IPointOutline outline => outline.GivenPoints[point.Index - 1],
                    _ => ((Mover)point.Component).Path[point.Index],
                };
                text = dragging ? $"{point.Index}  ·  {Number(offset.X)}, {Number(offset.Y)}"
                    : hovered ? $"{point.Index}  ·  double-click to remove" : $"{point.Index}";
            }

            PathLabel(style.Text(text, active ? "Brush.Text.Primary" : "Brush.Text.Secondary"), handle.Screen);
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
        var color = style.Brush("Brush.Status.Warning");
        var chrome = style.Brush("Brush.Surface.Chrome");
        var halo = new Pen(chrome, 4);
        foreach (var (distance, point, outward) in handles.DistanceHandles())
        {
            var dragging = input.Drag == SceneDrag.Distance && input.Distance?.Distance.Key == distance.Key;
            var active = dragging || input.Drag == SceneDrag.None && input.OverDistance == distance.Key;
            var radius = SceneHandleGeometry.HandleRadius + (active ? 1.5 : 0);
            dc.DrawEllipse(chrome, halo, point, radius, radius);
            dc.DrawEllipse(active ? color : chrome, new Pen(color, 2), point, radius, radius);

            var name = $"{distance.Component.GetType().Name} {distance.Property.Name}";
            var label = dragging
                ? style.Text($"{name} {distance.Value.ToString("0.##", CultureInfo.CurrentCulture)}", "Brush.Text.Primary")
                : style.Text(name, active ? "Brush.Text.Primary" : "Brush.Text.Secondary");
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
        var accent = style.Brush("Brush.Accent");
        var halo = new Pen(style.Brush("Brush.Surface.Chrome"), 4);
        if (input.Drag == SceneDrag.Rotate && input.Rotate is not null)
        {
            var pivot = viewport.ToScreen(input.Rotate.Pivot);
            var radius = (knob - pivot).Length;
            dc.DrawEllipse(null, new Pen(style.Faded("Brush.Accent", 0.5), 1) { DashStyle = new DashStyle([4, 4], 0) }, pivot, radius, radius);
        }

        dc.DrawLine(halo, root, knob);
        dc.DrawLine(new Pen(accent, 1.5), root, knob);
        var grown = input.OverRotate || input.Drag == SceneDrag.Rotate ? 1.5 : 0;
        dc.DrawEllipse(style.Brush("Brush.Surface.Chrome"), halo, knob, SceneHandleGeometry.HandleRadius + grown, SceneHandleGeometry.HandleRadius + grown);
        dc.DrawEllipse(input.OverRotate || input.Drag == SceneDrag.Rotate ? accent : style.Brush("Brush.Surface.Chrome"), new Pen(accent, 2), knob, SceneHandleGeometry.HandleRadius + grown, SceneHandleGeometry.HandleRadius + grown);

        if (input.Drag == SceneDrag.Rotate && selection.Entity is { } turning)
        {
            var label = style.Text($"{turning.Transform.Rotation.Z.ToString("0.#", CultureInfo.CurrentCulture)}°", "Brush.Text.Primary");
            var at = new Point(knob.X + 12, knob.Y - label.Height / 2);
            dc.DrawRoundedRectangle(style.Brush("Brush.Surface.Chrome"), null, new Rect(at.X - 4, at.Y - 1, label.Width + 8, label.Height + 2), 3, 3);
            dc.DrawText(label, at);
        }
    }

    // Squares on the corners and edges of the primary selection; while scaling, its scale by the one being dragged.
    private void DrawScaleHandles(DrawingContext dc)
    {
        var accent = style.Brush("Brush.Accent");
        var chrome = style.Brush("Brush.Surface.Chrome");
        var halo = new Pen(chrome, 4);
        Point? dragged = null;
        foreach (var (spot, point) in handles.ScaleHandles())
        {
            var active = input.Drag == SceneDrag.Scale ? spot == input.ScaleSpot : input.Drag == SceneDrag.None && spot == input.OverScale;
            var half = SceneHandleGeometry.HandleSquare + (active ? 1 : 0);
            var square = new Rect(point.X - half, point.Y - half, half * 2, half * 2);
            dc.DrawRectangle(chrome, halo, square);
            dc.DrawRectangle(active ? accent : chrome, new Pen(accent, 1.5), square);
            if (active && input.Drag == SceneDrag.Scale)
                dragged = point;
        }

        if (dragged is { } at && selection.Entity is { } scaling)
        {
            var scale = scaling.Transform.Scale;
            var label = style.Text($"{scale.X.ToString("0.##", CultureInfo.CurrentCulture)} × {scale.Y.ToString("0.##", CultureInfo.CurrentCulture)}", "Brush.Text.Primary");
            var place = new Point(at.X + 12, at.Y - label.Height / 2);
            dc.DrawRoundedRectangle(chrome, null, new Rect(place.X - 4, place.Y - 1, label.Width + 8, label.Height + 2), 3, 3);
            dc.DrawText(label, place);
        }
    }

    // Squares on the sprite's or colliders' corners and edges (colliders' in their green); while resizing, the size by the
    // one being dragged.
    private void DrawResizeHandles(DrawingContext dc)
    {
        var color = style.Brush(model.Handles == SceneHandles.Collider ? "Brush.Status.Success" : "Brush.Accent");
        var chrome = style.Brush("Brush.Surface.Chrome");
        var halo = new Pen(chrome, 4);
        Point? dragged = null;

        // A UI part's area (a button's clickable area) has nothing else drawn round it: outline it.
        if (model.Handles == SceneHandles.Resize)
        {
            var outline = new Pen(color, 1) { DashStyle = DashStyles.Dash };
            foreach (var area in model.ResizableShapes().Where(c => SceneViewModel.ShapeFrame.AreaOf(c) is not null))
            {
                if (SceneViewModel.ShapeFrame.Of(area) is not { } frame)
                    continue;
                Point Corner(float x, float y) => viewport.ToScreen(frame.ToWorld(frame.HandleAt(new Vector2(x, y))));
                var figure = new StreamGeometry();
                using (var g = figure.Open())
                {
                    g.BeginFigure(Corner(-1, -1), false, true);
                    g.PolyLineTo([Corner(1, -1), Corner(1, 1), Corner(-1, 1)], true, false);
                }

                dc.DrawGeometry(null, outline, figure);
            }
        }

        foreach (var (shape, spot, point) in handles.ResizeHandles())
        {
            var active = input.Drag == SceneDrag.Resize
                ? shape == input.Resize?.Shape && spot == input.ResizeSpot
                : input.Drag == SceneDrag.None && input.OverResize is var (overShape, overSpot) && overShape == shape && overSpot == spot;
            var half = SceneHandleGeometry.HandleSquare + (active ? 1 : 0);
            var square = new Rect(point.X - half, point.Y - half, half * 2, half * 2);
            dc.DrawRectangle(chrome, halo, square);
            dc.DrawRectangle(active ? color : chrome, new Pen(color, 1.5), square);
            if (active && input.Drag == SceneDrag.Resize)
                dragged = point;
        }

        if (dragged is { } at && input.Resize?.Shape is { } resizing)
        {
            string Number(float value) => value.ToString("0.##", CultureInfo.CurrentCulture);
            var size = resizing switch
            {
                Sprite sprite => sprite.Size,
                BoxCollider box => box.Size,
                _ when SceneViewModel.ShapeFrame.AreaOf(resizing) is var (sized, _) => (Vector2)sized.GetValue(resizing)!,
                _ => Vector2.Zero,
            };
            var label = style.Text(resizing is CircleCollider circle ? $"r {Number(circle.Radius)}" : $"{Number(size.X)} × {Number(size.Y)}", "Brush.Text.Primary");
            var place = new Point(at.X + 12, at.Y - label.Height / 2);
            dc.DrawRoundedRectangle(chrome, null, new Rect(place.X - 4, place.Y - 1, label.Width + 8, label.Height + 2), 3, 3);
            dc.DrawText(label, place);
        }
    }

    // The box being dragged, and outlines on what it will catch.
    private void DrawBoxSelection(DrawingContext dc)
    {
        if ((input.BoxEnd - input.DragStart).Length < SceneViewInput.DragThreshold)
            return;
        foreach (var caught in model.EntitiesInBox(viewport.ToWorld(input.DragStart), viewport.ToWorld(input.BoxEnd)).Where(e => !selection.IsSelected(e)))
            DrawSelection(dc, caught, primary: false);

        var accent = style.Brush("Brush.Accent");
        dc.DrawRectangle(style.Faded("Brush.Accent", 0.12), new Pen(accent, 1), new Rect(input.DragStart, input.BoxEnd));
    }

    // The zoom, and which handles are out when they aren't the scale ones.
    private void DrawZoomLabel(DrawingContext dc)
    {
        var mode = model.Handles switch
        {
            SceneHandles.Resize => "Resize handles  ·  ",
            SceneHandles.Collider => "Collider handles  ·  ",
            SceneHandles.Distance => "Distance handles  ·  ",
            _ => "",
        };
        var text = style.Text($"{mode}{viewport.Zoom / 64:P0}", "Brush.Text.Tertiary");
        dc.DrawText(text, new Point(viewport.Size.Width - text.Width - 10, viewport.Size.Height - text.Height - 8));

        // In a prefab's tab, a reminder of what's being edited.
        if (_document is { IsPrefab: true } prefab)
        {
            var label = style.Text($"Editing the {prefab.Name} prefab  ·  saving it updates every copy", "Brush.Accent");
            dc.DrawText(label, new Point(10, 8));
        }
    }

    private BitmapSource? LoadImage(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || workspace.Project is not { } project)
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
}
