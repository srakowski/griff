using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>
/// Laying a directed line by dragging: the shared rule (the way the drag started, or the longer way, round one corner,
/// each cell pointing on), tiles turned to follow it, and a Line Tool in a game laying belts and tiles, its ghost red
/// where walls, a script's rule or the money say no, R turning a single cell (and rebinding), a right-click dropping the
/// line, and Selection Controls leaving the mouse to it.
/// </summary>
public class LineToolTests
{
    private const double Frame = 1 / 60.0;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Tilemap _floor;
    private readonly Conveyor _belts;
    private readonly LineTool _tool;
    private readonly Entity _camera;

    public LineToolTests()
    {
        // A floor 12 cells across and 6 down, its top-left corner at (-6, 3) so it's all in the camera's view (20 by 10
        // round the origin): cell (x, y)'s middle is at (x - 5.5, 2.5 - y). A wall on a map of its own, at (3, 2).
        _camera = new Entity("Main Camera");
        _camera.Transform.Position = new Vector3(0, 0, -10);
        _camera.AddComponent<Camera>().Size = 5;
        _tool = _camera.AddComponent<LineTool>();
        _tool.Conveyor = "Line";
        _scene.Add(_camera);

        var floor = new Entity("Floor");
        floor.Transform.Position = new Vector3(-6, 3, 0);
        _floor = floor.AddComponent<Tilemap>();
        (_floor.Tiles, _floor.Solid) = ([.. Enumerable.Repeat(new string('0', 12), 6)], false);
        _scene.Add(floor);
        var walls = new Entity("Walls");
        walls.Transform.Position = floor.Transform.Position;
        walls.AddComponent<Tilemap>().SetTile(3, 2, 1);
        _scene.Add(walls);

        var line = new Entity("Line");
        _belts = line.AddComponent<Conveyor>();
        _belts.Map = "Floor";
        _scene.Add(line);
    }

    private static Vector2 At(int x, int y) => new(x - 5.5f, 2.5f - y);

    private Game CreateGame() => new(_scene, _input) { ViewAspect = 2 };

    private void Tick(Game game, int frames = 1)
    {
        for (var i = 0; i < frames; i++)
        {
            game.Tick(Frame);
            _input.EndFrame();
        }
    }

    // Presses on the first cell, moves through the others, and lets go on the last.
    private void Drag(Game game, params (int X, int Y)[] cells)
    {
        _input.MouseWorld = At(cells[0].X, cells[0].Y);
        _input.SetMouseButton(MouseButton.Left, true);
        Tick(game);
        foreach (var cell in cells.Skip(1))
        {
            _input.MouseWorld = At(cell.X, cell.Y);
            Tick(game);
        }

        _input.SetMouseButton(MouseButton.Left, false);
        Tick(game);
    }

    private void Press(Game game, Key key)
    {
        _input.SetKey(key, true);
        Tick(game);
        _input.SetKey(key, false);
        Tick(game);
    }

    // ----- The rule -----

    [Fact]
    public void ALine_RunsTheWayTheDragStarted_RoundOneCorner_EachCellPointingOn()
    {
        var drag = new LineDrag((2, 4));
        Assert.True(drag.IsSingle);
        drag.MoveTo((3, 4));
        drag.MoveTo((5, 7));
        Assert.True(drag.AcrossFirst);
        var cells = drag.Cells();
        Assert.Equal([(2, 4), (3, 4), (4, 4), (5, 4), (5, 5), (5, 6), (5, 7)], cells.Select(c => c.Cell));
        Assert.All(cells.Take(3), c => Assert.Equal((1, 0), c.Way));
        Assert.Equal(new LineCell(5, 4, 0, -1, 1, 0), cells[3]);
        Assert.True(cells[3].IsCorner);
        Assert.All(cells.Skip(4), c => Assert.Equal((0, -1), c.Way));
        Assert.Equal((0, -1), cells[^1].In);
        Assert.Single(cells, c => c.IsCorner);

        // Started down the column, it goes down first, even dragged further across after.
        drag = new LineDrag((2, 4));
        drag.MoveTo((2, 5));
        drag.MoveTo((9, 6));
        Assert.False(drag.AcrossFirst);
        Assert.Equal((2, 6), drag.Cells().Single(c => c.IsCorner).Cell);

        // Back on the start cell it starts again; Swap Corner turns the corner the other way.
        drag.MoveTo((2, 4));
        drag.MoveTo((3, 4));
        drag.MoveTo((9, 6));
        Assert.True(drag.AcrossFirst);
        drag.SwapCorner();
        Assert.Equal((2, 6), drag.Cells().Single(c => c.IsCorner).Cell);
    }

    [Fact]
    public void TheLongerWay_AStraightLine_AndASingleCell()
    {
        var longer = new LineDrag((0, 0), LineFirstLeg.Longer);
        longer.MoveTo((1, 0));
        longer.MoveTo((2, 5));
        Assert.False(longer.AcrossFirst);
        longer.MoveTo((6, 5));
        Assert.True(longer.AcrossFirst);

        // Without corners it stays on the longer axis.
        var straight = new LineDrag((0, 0), corners: false);
        straight.MoveTo((5, 2));
        Assert.Equal((5, 0), straight.End);
        straight.MoveTo((1, 4));
        Assert.Equal([(0, 0), (0, 1), (0, 2), (0, 3), (0, 4)], straight.Cells().Select(c => c.Cell));

        // A single cell points the way it's given; a line's last cell can be told to turn.
        Assert.Equal(new LineCell(3, 3, 0, 1, 0, 1), Assert.Single(new LineDrag((3, 3)).Cells((0, 1))));
        var path = LineDrag.Path((0, 0), (2, 0), lastWay: (0, 1));
        Assert.Equal(new LineCell(2, 0, 0, 1, 1, 0), path[^1]);
        Assert.Equal((0, 1), LineDrag.StepOf(LineWay.Up));
    }

    [Fact]
    public void TilesTurn_ToPointTheWayALineRuns_AndCornersTurnOrMirror()
    {
        Assert.Equal(TileTurn.None, TileTurns.Pointing(1, 0));
        Assert.Equal(TileTurn.Turn90, TileTurns.Pointing(0, -1));
        Assert.Equal(TileTurn.Turn180, TileTurns.Pointing(-1, 0));
        Assert.Equal(TileTurn.Turn270, TileTurns.Pointing(0, 1));
        Assert.Equal(TileTurn.None, TileTurns.Corner((1, 0), (0, -1)));
        Assert.Equal(TileTurn.FlipY, TileTurns.Corner((1, 0), (0, 1)));
        Assert.Equal(TileTurn.Turn90, TileTurns.Corner((0, -1), (-1, 0)));

        // Every corner: the drawn one (in moving right, out moving down) turned takes it in and out the right ways.
        (int, int)[] ways = [(1, 0), (0, 1), (-1, 0), (0, -1)];
        foreach (var @in in ways)
        {
            foreach (var @out in ways.Where(w => w != @in && w != (-@in.Item1, -@in.Item2)))
            {
                var turn = TileTurns.Corner(@in, @out);
                Assert.Equal(new Vector2(@in.Item1, @in.Item2), Round(turn.Apply(Vector2.UnitX)));
                Assert.Equal(new Vector2(@out.Item1, @out.Item2), Round(turn.Apply(-Vector2.UnitY)));
            }
        }

        static Vector2 Round(Vector2 v) => new(MathF.Round(v.X), MathF.Round(v.Y));
    }

    [Fact]
    public void LineTiles_LayStraightPiecesCornersAndEnds_Turned()
    {
        var entity = new Entity("Belts");
        var map = entity.AddComponent<Tilemap>();
        var tiles = entity.AddComponent<LineTiles>();
        (tiles.StraightTile, tiles.CornerTile, tiles.StartTile, tiles.EndTile) = (1, 2, 3, 4);
        tiles.Lay(LineDrag.Path((0, 0), (2, 2)));
        Assert.Equal((3, TileTurn.None), (map.GetTile(0, 0), map.GetTurn(0, 0)));
        Assert.Equal((1, TileTurn.None), (map.GetTile(1, 0), map.GetTurn(1, 0)));
        Assert.Equal((2, TileTurn.None), (map.GetTile(2, 0), map.GetTurn(2, 0)));
        Assert.Equal((1, TileTurn.Turn90), (map.GetTile(2, 1), map.GetTurn(2, 1)));
        Assert.Equal((4, TileTurn.Turn90), (map.GetTile(2, 2), map.GetTurn(2, 2)));

        // Up then left: an anticlockwise corner is the corner mirrored; with no ends, straight pieces end it.
        (tiles.StartTile, tiles.EndTile) = (-1, -1);
        tiles.Lay(LineDrag.Path((5, 4), (3, 2), acrossFirst: false));
        Assert.Equal((1, TileTurn.Turn270), (map.GetTile(5, 4), map.GetTurn(5, 4)));
        Assert.Equal(2, map.GetTile(5, 2));
        Assert.Equal(TileTurns.Corner((0, 1), (-1, 0)), map.GetTurn(5, 2));
        Assert.Equal((1, TileTurn.Turn180), (map.GetTile(3, 2), map.GetTurn(3, 2)));
        Assert.Same(tiles, LineTiles.For(map));
    }

    // ----- In the game -----

    [Fact]
    public void DraggingLaysBelts_OnTheConveyor_AndSaysSo()
    {
        var game = CreateGame();
        var placed = new List<IReadOnlyList<LineCell>>();
        var heard = new List<object?>();
        var dragging = 0;
        _tool.Placed += (_, cells) => placed.Add(cells);
        _tool.Dragging += (_, _) => dragging++;
        _tool.Message = "BeltsLaid";
        game.On("BeltsLaid", heard.Add);

        Drag(game, (1, 0), (2, 0), (4, 0), (4, 1), (4, 3));
        Assert.Equal(7, _belts.Cells.Count);
        Assert.Equal([(1, 0), (1, 0), (1, 0), (0, -1), (0, -1), (0, -1), (0, -1)], _belts.Cells.Select(c => c.Way));
        Assert.Equal((4, 3), _belts.Cells[^1].Cell);
        Assert.Equal(7, Assert.Single(placed).Count);
        Assert.Equal(7, Assert.Single(heard));
        Assert.True(dragging >= 3);
        Assert.False(_tool.IsDragging);

        // Laid again over the corner, the belts there are turned to follow the new line.
        Drag(game, (4, 0), (5, 0), (6, 0));
        Assert.Equal((1, 0), _belts.Cells.Single(c => c.Cell == (4, 0)).Way);
    }

    [Fact]
    public void TheGhost_IsRedOnWalls_WhereAScriptSaysNo_AndPastWhatTheMoneyCovers_AndOnlyWhatFitsIsLaid()
    {
        var game = CreateGame();
        Tick(game);

        // Hovering, the ghost is the single cell under the mouse.
        _input.MouseWorld = At(0, 2);
        Tick(game);
        Assert.Equal((0, 2), Assert.Single(_tool.Cells).Cell);
        Assert.Equal((0, 2), _tool.Hover);

        _input.SetMouseButton(MouseButton.Left, true);
        Tick(game);
        _input.MouseWorld = At(5, 2);
        Tick(game);
        Assert.True(_tool.IsDragging);
        Assert.Equal(6, _tool.Cells.Count);
        Assert.Equal([(3, 2)], _tool.Cells.Where(c => !c.Fits).Select(c => c.Cell));
        _input.SetMouseButton(MouseButton.Left, false);
        Tick(game);
        Assert.Equal(5, _belts.Cells.Count);
        Assert.DoesNotContain(_belts.Cells, c => c.Cell == (3, 2));

        // A script's rule, and at most three cells.
        _tool.CanLay = cell => cell.X != 1;
        _tool.MaxCells = 3;
        Assert.Equal([true, false, true, false, false], _tool.Line((0, 4), (4, 4)).Select(c => c.Fits));

        // Off the map is no good either.
        _tool.CanLay = null;
        _tool.MaxCells = 0;
        Assert.False(_tool.Line((12, 0), (12, 0))[0].Fits);
        _tool.StayOnMap = false;
        Assert.True(_tool.Line((12, 0), (12, 0))[0].Fits);
    }

    [Fact]
    public void Rotate_TurnsASingleCell_AndRebinds_AndARightClickDropsTheLine()
    {
        var game = CreateGame();
        _input.MouseWorld = At(6, 4);
        Tick(game);
        Press(game, Key.R);
        Assert.Equal(LineWay.Down, _tool.Way);
        Assert.Equal((0, -1), Assert.Single(_tool.Cells).Way);
        Drag(game, (6, 4));
        Assert.Equal(new ConveyorCell(6, 4, 0, -1, ConveyorCellKind.Belt), Assert.Single(_belts.Cells));

        _input.Rebind("Rotate", Key.T);
        Press(game, Key.R);
        Assert.Equal(LineWay.Down, _tool.Way);
        Press(game, Key.T);
        Assert.Equal(LineWay.Left, _tool.Way);

        // A right-click while dragging drops the line: letting go lays nothing.
        _input.MouseWorld = At(0, 5);
        _input.SetMouseButton(MouseButton.Left, true);
        Tick(game);
        _input.MouseWorld = At(4, 5);
        Tick(game);
        _input.SetMouseButton(MouseButton.Right, true);
        Tick(game);
        Assert.False(_tool.IsDragging);
        _input.SetMouseButton(MouseButton.Right, false);
        _input.SetMouseButton(MouseButton.Left, false);
        Tick(game);
        Assert.Single(_belts.Cells);
    }

    [Fact]
    public void ItLaysLineTiles_AndSelectionControlsLeaveItTheMouse_WhileItsInHand()
    {
        var layer = new Entity("Belt Tiles");
        var tilemap = layer.AddComponent<Tilemap>();
        tilemap.Solid = false;
        var tiles = layer.AddComponent<LineTiles>();
        (tiles.StraightTile, tiles.CornerTile) = (5, 6);
        layer.Transform.Position = _floor.Entity!.Transform.Position;
        _scene.Add(layer);
        (_tool.Conveyor, _tool.Tiles, _tool.Map) = (null, "Belt Tiles", "Floor");

        var controls = _camera.AddComponent<SelectionControls>();
        var worker = new Entity("Worker");
        worker.Transform.Position = new Vector3(At(8, 0), 0);
        worker.AddComponent<Selectable>().Size = Vector2.One;
        _scene.Add(worker);

        var game = CreateGame();
        Drag(game, (8, 0), (9, 0), (9, 1));
        Assert.Empty(controls.Selected);
        Assert.Equal((5, TileTurn.None), (tilemap.GetTile(8, 0), tilemap.GetTurn(8, 0)));
        Assert.Equal((6, TileTurn.None), (tilemap.GetTile(9, 0), tilemap.GetTurn(9, 0)));
        Assert.Equal((5, TileTurn.Turn90), (tilemap.GetTile(9, 1), tilemap.GetTurn(9, 1)));
        Assert.Empty(_belts.Cells);

        // Put down, the tool leaves the mouse to selecting, and its ghost goes.
        _tool.Controllable = false;
        Drag(game, (8, 0));
        Assert.Equal([worker], controls.Selected);
        Assert.Empty(_tool.Cells);
    }

    // ----- Joining lines already laid -----

    // Belt tiles under the conveyor: straight 1, corner 2, start 3, end 4, join 5, split 6; the tool joins lines.
    private Tilemap JoiningTiles()
    {
        var layer = new Entity("Belt Tiles");
        layer.Transform.Position = _floor.Entity!.Transform.Position;
        var map = layer.AddComponent<Tilemap>();
        map.Solid = false;
        var tiles = layer.AddComponent<LineTiles>();
        (tiles.StraightTile, tiles.CornerTile, tiles.StartTile, tiles.EndTile, tiles.JoinTile, tiles.SplitTile) = (1, 2, 3, 4, 5, 6);
        _scene.Add(layer);
        (_tool.Tiles, _tool.Joins) = ("Belt Tiles", true);
        return map;
    }

    private void Carry(Game game, string item, (int X, int Y) from, int frames = 300)
    {
        Assert.True(_belts.Put(from.X, from.Y, item));
        Tick(game, frames);
    }

    [Fact]
    public void DraggedFromALinesEnd_ItCarriesItOn_OneLineWithOneStartAndOneEnd_ThatCarriesAnItemAllTheWay()
    {
        var map = JoiningTiles();
        var game = CreateGame();
        Drag(game, (0, 0), (2, 0));
        Drag(game, (2, 0), (5, 0));
        Assert.Equal(6, _belts.Cells.Count);
        Assert.All(_belts.Cells, c => Assert.Equal((1, 0), c.Way));
        Assert.Equal([3, 1, 1, 1, 1, 4], Enumerable.Range(0, 6).Select(x => map.GetTile(x, 0)));
        Carry(game, "Ore", (0, 0));
        Assert.Equal("Ore", _belts.ItemAt(5, 0));

        // On round a corner: the old end turns down to feed it, keeping its speed and kind, and its tile is the corner.
        _belts.SetSpeed(5, 0, 0.5f);
        _tool.BeltKind = ConveyorCellKind.Crossing;
        Drag(game, (5, 0), (5, 2));
        Assert.Equal(((0, -1), ConveyorCellKind.Belt, 0.5f), (_belts.CellAt(5, 0)!.Value.Way, _belts.CellAt(5, 0)!.Value.Kind, _belts.SpeedAt(5, 0)));
        Assert.Equal(ConveyorCellKind.Crossing, _belts.CellAt(5, 1)!.Value.Kind);
        Assert.Equal((2, TileTurn.None), (map.GetTile(5, 0), map.GetTurn(5, 0)));
        Assert.Equal((4, TileTurn.Turn90), (map.GetTile(5, 2), map.GetTurn(5, 2)));
        Assert.Single(Enumerable.Range(0, 6).SelectMany(y => Enumerable.Range(0, 12).Select(x => map.GetTile(x, y))), t => t == 3);

        // Dragged on from the cell an end runs into, that old end is an end no more.
        Drag(game, (5, 3), (7, 3));
        Assert.Equal((2, TileTurns.Corner((0, -1), (1, 0))), (map.GetTile(5, 3), map.GetTurn(5, 3)));
        Assert.Equal((1, TileTurn.Turn90), (map.GetTile(5, 2), map.GetTurn(5, 2)));
    }

    [Fact]
    public void EndedOnALinesSide_ItMergesIn_ThatBeltStaying_AndTheGhostShowsIt()
    {
        var map = JoiningTiles();
        var game = CreateGame();
        var placed = new List<IReadOnlyList<LineCell>>();
        _tool.Placed += (_, cells) => placed.Add(cells);
        Drag(game, (0, 4), (6, 4));

        // While dragged, the ghost's last cell is the belt it merges into, pointing its own way.
        _input.MouseWorld = At(5, 1);
        _input.SetMouseButton(MouseButton.Left, true);
        Tick(game);
        _input.MouseWorld = At(5, 4);
        Tick(game);
        Assert.Equal((LineJoin.Merge, (1, 0)), (_tool.Cells[^1].Join, _tool.Cells[^1].Way));
        Assert.Equal(LineJoin.Feeds, _tool.Cells[^2].Join);
        Assert.All(_tool.Cells, c => Assert.True(c.Fits));
        _input.SetMouseButton(MouseButton.Left, false);
        Tick(game);

        Assert.Equal(10, _belts.Cells.Count);
        Assert.Equal((1, 0), _belts.CellAt(5, 4)!.Value.Way);
        Assert.Equal((0, -1), _belts.CellAt(5, 3)!.Value.Way);
        Assert.Equal(3, placed[^1].Count);
        Assert.Equal((5, TileTurn.FlipY), (map.GetTile(5, 4), map.GetTurn(5, 4)));
        Assert.Equal((1, TileTurn.Turn90), (map.GetTile(5, 3), map.GetTurn(5, 3)));
        Carry(game, "Ore", (5, 1));
        Assert.Equal("Ore", _belts.ItemAt(6, 4));

        // Ended beside it, pointing in, the last cell feeds it: a straight piece, not an end.
        Drag(game, (1, 1), (1, 3));
        Assert.Equal((1, TileTurn.Turn90), (map.GetTile(1, 3), map.GetTurn(1, 3)));
        Assert.Equal((5, TileTurn.FlipY), (map.GetTile(1, 4), map.GetTurn(1, 4)));

        // Into a line's start, from its side, that start becomes a corner.
        Drag(game, (8, 1), (8, 3));
        Drag(game, (10, 1), (8, 1));
        Assert.Equal((0, -1), _belts.CellAt(8, 1)!.Value.Way);
        Assert.Equal((2, TileTurns.Corner((-1, 0), (0, -1))), (map.GetTile(8, 1), map.GetTurn(8, 1)));
    }

    [Fact]
    public void DraggedOutOfALinesSide_ItSplitsOff_ThatBeltBecomingASplitterFeedingBoth()
    {
        var map = JoiningTiles();
        var game = CreateGame();
        Drag(game, (0, 4), (6, 4));
        Drag(game, (2, 4), (2, 1));

        var splitter = _belts.CellAt(2, 4)!.Value;
        Assert.Equal(((1, 0), ConveyorCellKind.Split), (splitter.Way, splitter.Kind));
        Assert.Equal((0, 1), _belts.CellAt(2, 3)!.Value.Way);
        Assert.Equal((6, TileTurn.FlipY), (map.GetTile(2, 4), map.GetTurn(2, 4)));
        Assert.Equal((1, TileTurn.Turn270), (map.GetTile(2, 3), map.GetTurn(2, 3)));
        Assert.Equal((4, TileTurn.Turn270), (map.GetTile(2, 1), map.GetTurn(2, 1)));

        Carry(game, "Ore", (0, 4), 60);
        Carry(game, "Gem", (0, 4));
        Assert.Equal(["Gem", "Ore"], new[] { _belts.ItemAt(6, 4), _belts.ItemAt(2, 1) }.Order());
    }

    [Fact]
    public void Joining_ALineLaidAcrossOneStillTurnsItsCells_AndWithoutJoinsNothingJoins()
    {
        var map = JoiningTiles();
        var game = CreateGame();
        Drag(game, (0, 4), (6, 4));
        _input.MouseWorld = At(4, 5);
        _input.SetMouseButton(MouseButton.Left, true);
        Tick(game);
        _input.MouseWorld = At(4, 1);
        Tick(game);
        Assert.All(_tool.Cells, c => Assert.Equal((true, LineJoin.None), (c.Fits, c.Join)));
        _input.SetMouseButton(MouseButton.Left, false);
        Tick(game);
        Assert.Equal((0, 1), _belts.CellAt(4, 4)!.Value.Way);

        // Unticked, a line from an end is laid on its own: a start piece over the old end.
        _tool.Joins = false;
        Drag(game, (6, 4), (8, 4));
        Assert.Equal(3, map.GetTile(6, 4));
        Assert.All(_tool.Line((6, 4), (8, 4)), c => Assert.Equal(LineJoin.None, c.Join));
    }

    [Fact]
    public void LineTiles_JoinALineOntoTheirPieces_WithoutAConveyor()
    {
        var entity = new Entity("Pipes");
        var map = entity.AddComponent<Tilemap>();
        var tiles = entity.AddComponent<LineTiles>();
        (tiles.StraightTile, tiles.CornerTile, tiles.StartTile, tiles.EndTile) = (1, 2, 3, 4);
        tiles.Lay(LineDrag.Path((0, 0), (2, 0)));
        Assert.Equal((1, 0), tiles.WayOf(2, 0));
        Assert.Null(tiles.WayOf(3, 0));

        var line = tiles.Join(LineDrag.Path((2, 0), (2, 2)));
        Assert.Equal((LineJoin.Continues, (1, 0)), (line[0].Join, line[0].In));
        tiles.Lay(line);
        Assert.Equal([3, 1, 2], Enumerable.Range(0, 3).Select(x => map.GetTile(x, 0)));
        Assert.Equal((4, TileTurn.Turn90), (map.GetTile(2, 2), map.GetTurn(2, 2)));
    }
}
