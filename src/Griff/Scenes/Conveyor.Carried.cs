using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Griff.Scenes;

// A conveyor with a Spacing: items glide along a path through each cell, several to a cell, each at least Spacing behind
// the one ahead. The path runs in from the side an item came from to the cell's middle and out the side it leaves by, a
// cell's length whether it's straight or a corner. An under cell is two paths, its own and the tunnel across it.
public sealed partial class Conveyor
{
    // One item on a path: what it is; how far along (0 where it came in, 0.5 the middle, 1 where it leaves); the way it
    // came in (one more than the way's number, 0 straight from behind); on a splitter, which of its ways on it takes (one
    // more than its place, 0 not picked yet); whether it couldn't move in the last fixed step; whether ReachedEnd has been
    // raised for it; the fixed step it last moved in; and where it was as the game's step began, for drawing.
    private struct Carried
    {
        public string Item;
        public float At;
        public byte Came;
        public byte Goes;
        public bool Still;
        public bool Told;
        public int Moved;
        public Vector2 From;
    }

    // What's on each path, the one furthest on first; the fixed steps taken, to tell what has moved in this one; and each
    // path's feeder a merge took from last and which of a splitter's ways on it tries next.
    private List<Carried>[] _carried = [];
    private int _tick;
    private byte[] _pathLastIn = [];
    private byte[] _pathNextOut = [];

    // The paths: a cell's own (numbered as its cell) and then each under cell's tunnel; the cell each is on and the way it
    // carries; each one's ways on, the paths that feed it, and every path with the ends first (each after those it carries
    // into). A loop is broken where it's first come to: the ways on that lead back round it are marked, a bit each.
    [NotState("worked out from the belts")]
    private int[] _crossOf = [];
    [NotState("worked out from the belts")]
    private int[] _nodeCell = [];
    [NotState("worked out from the belts")]
    private byte[] _nodeWay = [];
    [NotState("worked out from the belts")]
    private int[][] _nodeOuts = [];
    [NotState("worked out from the belts")]
    private int[][] _nodeFeeders = [];
    [NotState("worked out from the belts")]
    private int[] _nodeOrder = [];
    [NotState("worked out from the belts")]
    private byte[] _nodeBack = [];
    [NotState("worked out from the belts")]
    private bool _hasLoops;

    // How far each path carries an item in a fixed step: its cell's speed's worth, worked out as a step begins.
    [NotState("worked out from the speeds as a step begins")]
    private float[] _nodeStep = [];

    // Each path's loop (paths that lead round to each other), -1 for none; how many items each loop holds packed a spacing
    // apart, and (within a fixed step) how many it holds now. A loop takes no more in than that, or it couldn't go round.
    [NotState("worked out from the belts")]
    private int[] _loopOf = [];
    [NotState("worked out from the belts")]
    private int[] _loopRoom = [];
    [NotState("counted within a fixed step")]
    private int[] _loopHeld = [];

    // A loop's step is tried trusting that the items it comes back round to move on, and tried again from where it began
    // without trusting a way on that turned out wrong.
    [NotState("filled and emptied within a fixed step")]
    private byte[] _trust = [];
    [NotState("filled and emptied within a fixed step")]
    private readonly List<Carried> _saved = [];
    [NotState("filled and emptied within a fixed step")]
    private int[] _savedCounts = [];
    [NotState("filled and emptied within a fixed step")]
    private byte[] _savedLastIn = [];
    [NotState("filled and emptied within a fixed step")]
    private byte[] _savedNextOut = [];

    private bool IsSpaced => _spacing > 0;

    // Where the item at a belt's end waits: its own spacing short of the edge, so a full end packs a spacing apart.
    private float Rest => 1 - _spacing / 2;

    private bool IsSplit(int node) => node < _belts.Length && _belts[node].Kind == ConveyorCellKind.Split;

    // ---- Paths ----

    // Works out the paths, their ways on, feeders and order, from the cells.
    private void LinkNodes()
    {
        var n = _belts.Length;
        var crossOf = new int[n];
        Array.Fill(crossOf, -1);
        var cellOf = new List<int>(n);
        var wayOf = new List<byte>(n);
        for (var i = 0; i < n; i++)
        {
            cellOf.Add(i);
            wayOf.Add((byte)WayIndex(_belts[i].WayX, _belts[i].WayY));
        }

        for (var i = 0; i < n; i++)
        {
            if (_belts[i].Kind != ConveyorCellKind.Under)
                continue;
            crossOf[i] = cellOf.Count;
            cellOf.Add(i);
            wayOf.Add((byte)TunnelWay(i, wayOf[i]));
        }

        // The path an item going way `w` takes into cell `c`: an under cell's tunnel when it crosses it (if the tunnel runs
        // that way), else the cell's own, unless it points back.
        int Entering(int c, int w)
        {
            if (crossOf[c] >= 0 && wayOf[c] % 2 != w % 2)
                return wayOf[crossOf[c]] == w ? crossOf[c] : -1;
            return wayOf[c] == (w + 2) % 4 ? -1 : c;
        }

        var count = cellOf.Count;
        var outs = new int[count][];
        var feeders = new List<int>[count];
        for (var k = 0; k < count; k++)
            feeders[k] = [];
        var ways = new List<int>(3);
        for (var k = 0; k < count; k++)
        {
            var cell = _belts[cellOf[k]];
            var w = wayOf[k];
            ways.Clear();
            // A splitter's ways on: ahead, then to the left, then to the right, each a belt leading away.
            ReadOnlySpan<int> turns = k < n && cell.Kind == ConveyorCellKind.Split ? [0, 1, 3] : [0];
            foreach (var turn in turns)
            {
                var d = (w + turn) % 4;
                var (dx, dy) = Ways[d];
                if (_at.TryGetValue((cell.X + dx, cell.Y - dy), out var next) && Entering(next, d) is var to and >= 0 &&
                    (turn == 0 || wayOf[to] == d))
                    ways.Add(to);
            }

            outs[k] = [.. ways];
            foreach (var o in outs[k])
                feeders[o].Add(k);
        }

        // The ends first: each path after every path it carries into.
        var order = new List<int>(count);
        var state = new byte[count];
        var stack = new Stack<(int Node, int Next)>();
        for (var s = 0; s < count; s++)
        {
            if (state[s] != 0)
                continue;
            state[s] = 1;
            stack.Push((s, 0));
            while (stack.Count > 0)
            {
                var (c, k) = stack.Pop();
                if (k < outs[c].Length)
                {
                    stack.Push((c, k + 1));
                    var o = outs[c][k];
                    if (state[o] == 0)
                    {
                        state[o] = 1;
                        stack.Push((o, 0));
                    }
                }
                else
                {
                    state[c] = 2;
                    order.Add(c);
                }
            }
        }

        // A way on into a path that moves later in the order leads back round a loop.
        var rank = new int[count];
        for (var r = 0; r < order.Count; r++)
            rank[order[r]] = r;
        var back = new byte[count];
        for (var k = 0; k < count; k++)
        {
            for (var o = 0; o < outs[k].Length; o++)
            {
                if (rank[outs[k][o]] > rank[k])
                    back[k] |= (byte)(1 << o);
            }
        }

        // The loops: paths that lead round to each other, found back from the last to finish (each a group of them).
        var loopOf = new int[count];
        Array.Fill(loopOf, -1);
        var groups = new List<int>();
        var group = new int[count];
        Array.Fill(group, -1);
        var walk = new Stack<int>();
        for (var r = order.Count - 1; r >= 0; r--)
        {
            if (group[order[r]] >= 0)
                continue;
            var id = groups.Count;
            groups.Add(0);
            group[order[r]] = id;
            walk.Push(order[r]);
            while (walk.Count > 0)
            {
                var c = walk.Pop();
                groups[id]++;
                foreach (var f in feeders[c])
                {
                    if (group[f] < 0)
                    {
                        group[f] = id;
                        walk.Push(f);
                    }
                }
            }
        }

        var loops = new List<int>();
        var loopIds = new Dictionary<int, int>();
        for (var k = 0; k < count; k++)
        {
            if (groups[group[k]] < 2)
                continue;
            if (!loopIds.TryGetValue(group[k], out var loop))
            {
                loopIds[group[k]] = loop = loops.Count;
                loops.Add(groups[group[k]]);
            }

            loopOf[k] = loop;
        }

        (_crossOf, _nodeCell, _nodeWay) = (crossOf, [.. cellOf], [.. wayOf]);
        (_loopOf, _loopRoom, _loopHeld) = (loopOf, [.. loops], new int[loops.Count]);
        (_nodeOuts, _nodeFeeders, _nodeOrder, _nodeBack) = (outs, [.. feeders.Select(f => f.ToArray())], [.. order], back);
        _hasLoops = back.Any(b => b != 0);
        _trust = new byte[count];
        _savedCounts = new int[count];
    }

    // The way an under cell's tunnel runs: the way a belt at its side points into it, else the way one beyond it leads
    // away, else across to the left of its own way.
    private int TunnelWay(int i, int way)
    {
        var cell = _belts[i];
        foreach (var d in (ReadOnlySpan<int>)[(way + 1) % 4, (way + 3) % 4])
        {
            var (dx, dy) = Ways[d];
            if (_at.TryGetValue((cell.X - dx, cell.Y + dy), out var from) && _belts[from].Way == (dx, dy))
                return d;
        }

        foreach (var d in (ReadOnlySpan<int>)[(way + 1) % 4, (way + 3) % 4])
        {
            var (dx, dy) = Ways[d];
            if (_at.TryGetValue((cell.X + dx, cell.Y - dy), out var to) && _belts[to].Way != (-dx, -dy))
                return d;
        }

        return (way + 1) % 4;
    }

    // What's on each path before the cells change, by its cell, to put back on the same paths after.
    private List<((int X, int Y) Cell, bool Tunnel, List<Carried> Items, byte LastIn, byte NextOut)> KeepCarried()
    {
        var kept = new List<((int X, int Y), bool, List<Carried>, byte, byte)>();
        for (var k = 0; k < _carried.Length; k++)
            kept.Add((_belts[_nodeCell[k]].Cell, k >= _belts.Length, _carried[k], _pathLastIn[k], _pathNextOut[k]));
        return kept;
    }

    private void PutBackCarried(List<((int X, int Y) Cell, bool Tunnel, List<Carried> Items, byte LastIn, byte NextOut)> kept)
    {
        var count = _nodeCell.Length;
        var carried = new List<Carried>[count];
        for (var k = 0; k < count; k++)
            carried[k] = [];
        var (lastIn, nextOut) = (new byte[count], new byte[count]);
        foreach (var (cell, tunnel, items, last, next) in kept)
        {
            if (!_at.TryGetValue(cell, out var c))
                continue;
            var k = tunnel && _crossOf[c] >= 0 ? _crossOf[c] : c;
            var merged = carried[k].Count > 0;
            carried[k].AddRange(items);
            (lastIn[k], nextOut[k]) = (last, next);
            // A tunnel that's gone puts its items on the cell's own path, in order of how far on they are.
            if (merged)
            {
                carried[k] = [.. carried[k].OrderByDescending(x => x.At)];
            }
        }

        // A splitter's pick stands only while it's still a splitter with that way on.
        for (var k = 0; k < count; k++)
        {
            var items = CollectionsMarshal.AsSpan(carried[k]);
            for (var j = 0; j < items.Length; j++)
            {
                if (items[j].Goes > 0 && (!IsSplit(k) || items[j].Goes > _nodeOuts[k].Length))
                    items[j].Goes = 0;
            }
        }

        (_carried, _pathLastIn, _pathNextOut) = (carried, lastIn, nextOut);
        _savedLastIn = new byte[count];
        _savedNextOut = new byte[count];
    }

    private void ClearCarried()
    {
        foreach (var items in _carried)
            items.Clear();
    }

    private int CarriedCount()
    {
        var count = 0;
        foreach (var items in _carried)
            count += items.Count;
        return count;
    }

    // The path on cell `cell` with an item on it: its own, else its tunnel's; -1 for neither.
    private int FrontNode(int cell)
    {
        if (_carried[cell].Count > 0)
            return cell;
        return _crossOf[cell] >= 0 && _carried[_crossOf[cell]].Count > 0 ? _crossOf[cell] : -1;
    }

    // ---- Putting on and taking off ----

    // Puts `item` on path `node` at `at`, come in the way `came` (see Carried), if no item is nearer than the spacing.
    private bool PutCarried(int node, string item, float at, byte came, GridCells grid)
    {
        if (!RoomAt(node, at))
            return false;
        Insert(node, new Carried { Item = item, At = at, Came = came, Moved = _tick }, grid);
        _givenRows = null;
        return true;
    }

    private void Insert(int node, Carried carried, GridCells? grid)
    {
        if (grid is { } cells)
            carried.From = CarriedPosition(node, carried, cells);
        var items = _carried[node];
        var j = 0;
        while (j < items.Count && items[j].At >= carried.At)
            j++;
        items.Insert(j, carried);
        ItemsChanged();
    }

    private string? TakeCarried(int cell)
    {
        if (FrontNode(cell) is not (var node and >= 0))
            return null;
        var item = _carried[node][0].Item;
        _carried[node].RemoveAt(0);
        ItemsChanged();
        _givenRows = null;
        return item;
    }

    // Whether an item fits on path `node` at `at`: none on it, on the paths it leads into or those feeding it is nearer
    // than the spacing.
    private bool RoomAt(int node, float at)
    {
        if (_loopOf[node] is var loop and >= 0 && Held(loop) >= LoopRoom(loop))
            return false;
        var spacing = _spacing - Near;
        foreach (var other in _carried[node])
        {
            if (MathF.Abs(other.At - at) < spacing)
                return false;
        }

        foreach (var o in _nodeOuts[node])
        {
            if (_carried[o].Count > 0 && 1 - at + _carried[o][^1].At < spacing)
                return false;
        }

        foreach (var f in _nodeFeeders[node])
        {
            if (_carried[f].Count > 0 && 1 - _carried[f][0].At + at < spacing)
                return false;
        }

        return true;
    }

    // Puts an item read from a row at its column and row (a part of one off the middle): on the path through that cell
    // that runs where it is, as far along as it is. False while there's no belt there yet.
    private bool PlaceCarried(float x, float y, string item, GridCells? grid)
    {
        var cell = CellNear(x, y);
        if (!_at.TryGetValue(cell, out var c))
            return false;
        // Its offset from the middle, x right and y up as ways are.
        var (ox, oy) = (x - cell.X, cell.Y - y);
        var off = MathF.Max(MathF.Abs(ox), MathF.Abs(oy));
        var node = c;
        if (_crossOf[c] >= 0 && off > Near)
        {
            var (wx, wy) = Ways[_nodeWay[c]];
            if (MathF.Abs(ox * wx + oy * wy) < Near)
                node = _crossOf[c];
        }

        var carried = new Carried { Item = item, At = 0.5f, Moved = _tick };
        if (off > Near)
        {
            var (nx, ny) = Ways[_nodeWay[node]];
            var along = ox * nx + oy * ny;
            var toward = WayIndex(Math.Sign(ox), Math.Sign(oy));
            var goes = IsSplit(node) ? OutToward(node, toward) : -1;
            if (toward < 0)
                carried.At = 0.5f;
            else if (along > Near || goes >= 0)
                (carried.At, carried.Goes) = (0.5f + off, (byte)(goes + 1));
            else if (along < -Near)
                carried.At = 0.5f - off;
            else
                (carried.At, carried.Came) = (0.5f - off, (byte)((toward + 2) % 4 + 1));
            carried.At = Math.Clamp(carried.At, 0, 1);
        }

        Insert(node, carried, grid);
        return true;
    }

    // Which of a splitter's ways on goes way `way` from its cell, or -1.
    private int OutToward(int node, int way)
    {
        if (way < 0)
            return -1;
        var cell = _belts[_nodeCell[node]];
        var outs = _nodeOuts[node];
        for (var o = 0; o < outs.Length; o++)
        {
            var to = _belts[_nodeCell[outs[o]]];
            if (WayIndex(to.X - cell.X, cell.Y - to.Y) == way)
                return o;
        }

        return -1;
    }

    // ---- Where items are ----

    // The way an item leaves its path by: the way to the splitter's way on it picked, else the path's own.
    private int ExitWay(int node, in Carried carried)
    {
        if (carried.Goes == 0 || carried.Goes > _nodeOuts[node].Length)
            return _nodeWay[node];
        var (cell, to) = (_belts[_nodeCell[node]], _belts[_nodeCell[_nodeOuts[node][carried.Goes - 1]]]);
        return WayIndex(to.X - cell.X, cell.Y - to.Y);
    }

    // How far off its cell's middle an item is, in columns and rows (rows counting down).
    private (float X, float Y) OffsetOf(int node, in Carried carried)
    {
        if (carried.At < 0.5f)
        {
            var (ex, ey) = Ways[carried.Came == 0 ? _nodeWay[node] : carried.Came - 1];
            var back = 0.5f - carried.At;
            return (-ex * back, ey * back);
        }

        var (wx, wy) = Ways[ExitWay(node, carried)];
        var on = carried.At - 0.5f;
        return (wx * on, -wy * on);
    }

    private Vector2 CarriedPosition(int node, in Carried carried, GridCells grid)
    {
        var cell = _belts[_nodeCell[node]];
        var center = grid.Center(cell.Cell);
        var (ox, oy) = OffsetOf(node, carried);
        if (ox == 0 && oy == 0)
            return center;
        // Toward the cell it's coming from or going to, as far as it is off the middle.
        var (dx, dy) = (Math.Sign(ox), Math.Sign(oy));
        return Vector2.Lerp(center, grid.Center((cell.X + dx, cell.Y + dy)), MathF.Max(MathF.Abs(ox), MathF.Abs(oy)));
    }

    private List<(int X, int Y, string Item, Vector2 Position)> CarriedPositions()
    {
        var grid = Grid;
        var positions = new List<(int X, int Y, string Item, Vector2 Position)>();
        for (var i = 0; i < _belts.Length; i++)
        {
            foreach (var node in (ReadOnlySpan<int>)[i, _crossOf[i]])
            {
                if (node < 0)
                    continue;
                foreach (var carried in _carried[node])
                    positions.Add((_belts[i].X, _belts[i].Y, carried.Item, CarriedPosition(node, carried, grid)));
            }
        }

        return positions;
    }

    private List<string> CarriedRows()
    {
        var rows = new List<string>();
        for (var i = 0; i < _belts.Length; i++)
        {
            foreach (var node in (ReadOnlySpan<int>)[i, _crossOf[i]])
            {
                if (node < 0)
                    continue;
                foreach (var carried in _carried[node])
                {
                    // Kept off the edge, so the row reads back onto the same cell.
                    var (ox, oy) = OffsetOf(node, carried);
                    var (x, y) = (_belts[i].X + Math.Clamp(ox, -0.4999f, 0.4999f), _belts[i].Y + Math.Clamp(oy, -0.4999f, 0.4999f));
                    rows.Add(string.Create(CultureInfo.InvariantCulture, $"{x:0.####} {y:0.####} {carried.Item}"));
                }
            }
        }

        foreach (var (x, y, item) in _unplaced)
            rows.Add(string.Create(CultureInfo.InvariantCulture, $"{x:0.####} {y:0.####} {item}"));
        return rows;
    }

    // How many items loop `loop` holds packed a spacing apart, and how many it holds now.
    private int LoopRoom(int loop) => (int)MathF.Floor(_loopRoom[loop] / _spacing + 1e-3f);

    private int Held(int loop)
    {
        var held = 0;
        for (var k = 0; k < _carried.Length; k++)
            held += _loopOf[k] == loop ? _carried[k].Count : 0;
        return held;
    }

    // ---- Moving ----

    // Notes where every item is as a step of the game begins, to draw it gliding from there.
    private void StartCarrying(GridCells grid)
    {
        for (var k = 0; k < _carried.Length; k++)
        {
            var items = CollectionsMarshal.AsSpan(_carried[k]);
            for (var j = 0; j < items.Length; j++)
                items[j].From = CarriedPosition(k, items[j], grid);
        }
    }

    private void TickCarried(GridCells grid)
    {
        _tick++;

        // A loop moves trusting the items it comes back round to move on as far as they can; where that turns out wrong,
        // it moves again from where it began without trusting that way on.
        var loops = _hasLoops;
        if (loops)
        {
            Array.Copy(_nodeBack, _trust, _nodeBack.Length);
            SaveCarried();
        }

        while (true)
        {
            MoveCarried(loops);
            if (!loops || !Distrust())
                break;
            RestoreCarried();
        }

        TakeAtEnds();
        GiveCarried(grid);
    }

    // Every path, ends first, moves each of its items on as far as a step takes it, up to the spacing behind the one ahead
    // and, for the one furthest on, onto the path its way on leads into (going on at that path's speed once it's there).
    private void MoveCarried(bool loops)
    {
        var spacing = _spacing;
        Array.Clear(_loopHeld);
        for (var k = 0; k < _carried.Length; k++)
        {
            if (_loopOf[k] >= 0)
                _loopHeld[_loopOf[k]] += _carried[k].Count;
        }

        foreach (var k in _nodeOrder)
        {
            var items = _carried[k];
            var outs = _nodeOuts[k];
            var split = IsSplit(k);
            var step = _nodeStep[k];
            var j = 0;
            while (j < items.Count)
            {
                var carried = items[j];
                // One brought in from round a loop has moved already.
                if (carried.Moved == _tick)
                {
                    j++;
                    continue;
                }

                var was = carried.At;
                var limit = j > 0 ? items[j - 1].At - spacing : float.PositiveInfinity;

                // A splitter's item picks its way on as it reaches the middle, and waits there for one with room.
                if (split && carried.Goes == 0 && was + step >= 0.5f - Near)
                {
                    var pick = PickOut(k, carried.Item);
                    if (pick >= 0)
                        carried.Goes = (byte)(pick + 1);
                    else
                        limit = Math.Min(limit, 0.5f);
                }

                var target = -1;
                if (j == 0)
                {
                    var o = split ? carried.Goes - 1 : outs.Length > 0 ? 0 : -1;
                    if (outs.Length == 0)
                        limit = Math.Min(limit, Rest);
                    else if (o >= 0)
                    {
                        target = outs[o];
                        var ahead = _carried[target];
                        if (ahead.Count > 0)
                        {
                            var tail = ahead[^1];
                            var trusted = loops && (_trust[k] >> o & 1) != 0 && tail.Moved != _tick;
                            limit = Math.Min(limit, 1 + tail.At + (trusted ? _nodeStep[target] : 0) - spacing);
                        }

                        if (!MayEnter(target, k) || LoopShut(target, k))
                            limit = Math.Min(limit, 1);
                        limit = Math.Min(limit, 1 + EntryCap(target));
                    }
                }

                var reach = was + step;
                // Past the edge into a path of another speed, the rest of the step goes at that speed.
                if (reach > 1 && target >= 0 && _nodeStep[target] != step)
                    reach = 1 + (reach - 1) * _nodeStep[target] / step;
                var now = Math.Max(was, Math.Min(reach, limit));
                (carried.Still, carried.Moved) = (now <= was, _tick);
                if (now > 1 && target >= 0)
                {
                    // It leaves by its way on, coming into the next path the way it went.
                    (carried.At, carried.Came, carried.Goes, carried.Told) = (now - 1, (byte)(ExitWay(k, carried) + 1), 0, false);
                    items.RemoveAt(0);
                    _carried[target].Add(carried);
                    if (_loopOf[target] != _loopOf[k])
                    {
                        if (_loopOf[target] >= 0)
                            _loopHeld[_loopOf[target]]++;
                        if (_loopOf[k] >= 0)
                            _loopHeld[_loopOf[k]]--;
                    }

                    var feeders = _nodeFeeders[target];
                    if (feeders.Length > 1)
                        _pathLastIn[target] = (byte)(Array.IndexOf(feeders, k) + 1);
                    continue;
                }

                carried.At = now;
                items[j] = carried;
                j++;
            }
        }
    }

    // How far into a path an item can come in one go: to a splitter's middle (to pick its way on there), an end's resting
    // place, else the far edge.
    private float EntryCap(int node) => IsSplit(node) ? 0.5f : _nodeOuts[node].Length == 0 ? Rest : 1;

    // Which of splitter `node`'s ways on `item` takes next: the next in turn that its filters send it onto with room (empty,
    // its last item a spacing in, or moving), or -1.
    private int PickOut(int node, string item)
    {
        var outs = _nodeOuts[node];
        if (outs.Length == 0)
            return -1;
        var first = _pathNextOut[node] % outs.Length;
        for (var n = 0; n < outs.Length; n++)
        {
            var o = (first + n) % outs.Length;
            if (!Sends(outs, outs[o], item, p => _belts[_nodeCell[p]].Cell))
                continue;
            var ahead = _carried[outs[o]];
            if (ahead.Count > 0 && ahead[^1].At < _spacing - Near && ahead[^1].Still)
                continue;
            _pathNextOut[node] = (byte)((o + 1) % outs.Length);
            return o;
        }

        return -1;
    }

    // Whether path `from`'s item comes into `to` now: where two paths merge into it, the one it took from last waits while
    // another has one at its edge about to come in, so they take turns.
    private bool MayEnter(int to, int from)
    {
        var feeders = _nodeFeeders[to];
        if (feeders.Length < 2 || _pathLastIn[to] != Array.IndexOf(feeders, from) + 1)
            return true;
        foreach (var other in feeders)
        {
            if (other == from || _carried[other].Count == 0)
                continue;
            var front = _carried[other][0];
            if (front.At >= 1 - _nodeStep[other] - Near && Heads(other, front) == to && !LoopShut(to, other))
                return false;
        }

        return true;
    }

    // Whether path `to` is in a loop that `from` isn't, holding all it can: a full loop takes nothing more in, so it keeps
    // going round.
    private bool LoopShut(int to, int from) =>
        _loopOf[to] is var loop and >= 0 && loop != _loopOf[from] && _loopHeld[loop] >= LoopRoom(loop);

    // The path an item on `node` (the one furthest on) goes into next, or -1: a belt's way on, or the one a splitter picked.
    private int Heads(int node, in Carried carried)
    {
        var outs = _nodeOuts[node];
        if (IsSplit(node))
            return carried.Goes > 0 && carried.Goes <= outs.Length ? outs[carried.Goes - 1] : -1;
        return outs.Length > 0 ? outs[0] : -1;
    }

    // After a loop's step: false if every way on it trusted came out a spacing apart; else stops trusting those that
    // didn't (to move again from where the step began).
    private bool Distrust()
    {
        var spacing = _spacing - 1e-4f;
        var wrong = false;
        for (var k = 0; k < _trust.Length; k++)
        {
            if (_trust[k] == 0)
                continue;
            var outs = _nodeOuts[k];
            for (var o = 0; o < outs.Length; o++)
            {
                if ((_trust[k] >> o & 1) == 0)
                    continue;
                var ahead = _carried[outs[o]];
                var bad = false;
                for (var j = 1; j < ahead.Count && !bad; j++)
                    bad = ahead[j - 1].At - ahead[j].At < spacing;
                if (!bad && ahead.Count > 0 && _carried[k].Count > 0 && Heads(k, _carried[k][0]) == outs[o])
                    bad = 1 - _carried[k][0].At + ahead[^1].At < spacing;
                if (bad)
                {
                    _trust[k] &= (byte)~(1 << o);
                    wrong = true;
                }
            }
        }

        return wrong;
    }

    private void SaveCarried()
    {
        _saved.Clear();
        for (var k = 0; k < _carried.Length; k++)
        {
            _savedCounts[k] = _carried[k].Count;
            _saved.AddRange(_carried[k]);
        }

        Array.Copy(_pathLastIn, _savedLastIn, _pathLastIn.Length);
        Array.Copy(_pathNextOut, _savedNextOut, _pathNextOut.Length);
    }

    private void RestoreCarried()
    {
        var saved = CollectionsMarshal.AsSpan(_saved);
        var at = 0;
        for (var k = 0; k < _carried.Length; k++)
        {
            _carried[k].Clear();
            _carried[k].AddRange(saved.Slice(at, _savedCounts[k]));
            at += _savedCounts[k];
        }

        Array.Copy(_savedLastIn, _pathLastIn, _pathLastIn.Length);
        Array.Copy(_savedNextOut, _pathNextOut, _pathNextOut.Length);
    }

    // The item resting at each end goes into the port there if it takes it; one that's just got there with nothing taking
    // it is at an end.
    private void TakeAtEnds()
    {
        var rest = Rest - Near;
        for (var k = 0; k < _carried.Length; k++)
        {
            var items = _carried[k];
            if (_nodeOuts[k].Length > 0 || items.Count == 0 || items[0].At < rest)
                continue;
            var front = items[0];
            var cell = _belts[_nodeCell[k]];
            var (wx, wy) = Ways[_nodeWay[k]];
            if (_takers.TryGetValue((cell.X + wx, cell.Y - wy), out var port) || _takers.TryGetValue(cell.Cell, out port))
            {
                if (port.TryTake(front.Item))
                {
                    items.RemoveAt(0);
                    ItemsChanged();
                    _events.Add(new ConveyorEvent(ConveyorEventKind.Received, port, cell.Cell, front.Item));
                    continue;
                }

                port.Blocked = true;
            }

            if (front.Told)
                continue;
            front.Told = true;
            items[0] = front;
            _events.Add(new ConveyorEvent(ConveyorEventKind.ReachedEnd, null, cell.Cell, front.Item));
        }
    }

    // Ports put what their machines made on the belts they're at where there's room: in the middle of the cell they're
    // on, or at the near edge of the one leading away from them.
    private void GiveCarried(GridCells grid)
    {
        foreach (var (port, cell, from, node) in _givers)
        {
            if (!port.Open)
                continue;
            var at = from < 0 ? 0.5f : 0;
            if (!RoomAt(node, at))
            {
                port.Blocked |= port.HasToGive();
                continue;
            }

            if (port.NextToGive() is not { } item)
                continue;
            Insert(node, new Carried { Item = item, At = at, Came = (byte)(from < 0 ? 0 : from + 1), Moved = _tick }, grid);
            _events.Add(new ConveyorEvent(ConveyorEventKind.Sent, port, _belts[cell].Cell, item));
        }
    }

    // ---- Drawing ----

    // Each item a quad or an image where it is, gliding between where it was as the step began and where it is now. One
    // in a tunnel is under its belt, out of sight.
    private void DrawCarried(Drawing drawing, GridCells grid, StepBlend blend, Vector2 size, float z, Func<Vector2, bool> seen)
    {
        for (var k = 0; k < _belts.Length; k++)
        {
            foreach (var carried in _carried[k])
            {
                var at = CarriedPosition(k, carried, grid);
                if (blend.IsBetween)
                    at = blend.Between(carried.From, at);
                if (!seen(at))
                    continue;
                var (image, color) = LookOf(carried.Item);
                if (SortByY)
                    drawing.SortAt(at.Y - size.Y / 2);
                drawing.Image(Matrix4x4.CreateScale(size.X, size.Y, 1) * Matrix4x4.CreateTranslation(at.X, at.Y, z), image,
                    drawing.Tint(this, color));
            }
        }
    }
}
