using System.Numerics;
using Griff.Input;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.Match3;

/// <summary>
/// Runs match-3 boards (<see cref="MatchBoard"/>) with no code: gems swapped with the mouse, keys or a gamepad, lines of a
/// kind that clear, the gems above falling into the gaps and new ones dropping in, chains, and a shuffle when no swap is
/// left. The host runs the boards for everyone; players' machines draw the slides and falls it sends them.
/// </summary>
internal sealed class Match3System : GameSystem
{
    public override string Name => "Match Boards";

    // Seconds a shuffle takes, the gems sliding to their new cells.
    private const float ShuffleTime = 0.4f;

    // After scripts (so a swap a script asks for is made this frame) and after dragging.
    internal void RunBoards()
    {
        foreach (var board in Game.Showing<MatchBoard>())
        {
            if (board.Entity!.GetComponent<Tilemap>() is not { } map)
                continue;
            board.LocalPlayer = Game.Net.LocalPlayer?.Id ?? 0;
            // Paused itself, or with a group it's in (see Entity.IsPaused).
            if (board.Paused || board.Entity.IsPaused)
                continue;
            AdvanceGems(board);
            if (Game.Runs(board.Entity))
                RunBoard(board, map);
        }
    }

    // The slides, falls and clears under way go on, on every machine, each remembering how far along it was as the step
    // began (and what came to rest or cleared away in it), so the board is drawn between steps.
    private void AdvanceGems(MatchBoard board)
    {
        var seconds = Time.Delta;
        board.MovedIn = board.Entity!.Scene?.Step ?? 0;
        board.ClearingFrom.Clear();
        board.Landed.Clear();
        board.Vanished.Clear();
        foreach (var motion in board.Motions.Values)
        {
            motion.ElapsedFrom = motion.Elapsed;
            motion.Elapsed += seconds;
        }

        foreach (var (cell, cleared) in board.Clearing.ToList())
        {
            board.ClearingFrom[cell] = cleared;
            board.Clearing[cell] = cleared + seconds;
        }

        if (Game.Runs(board.Entity!))
        {
            foreach (var cell in board.Motions.Where(m => m.Value.IsDone).Select(m => m.Key).ToList())
            {
                board.Landed[cell] = board.Motions[cell];
                board.Motions.Remove(cell);
            }
        }
    }

    private void RunBoard(MatchBoard board, Tilemap map)
    {
        board.Random ??= new Random(board.Seed != 0 ? board.Seed : Rand.Range(1, int.MaxValue));
        if (!board.Started)
        {
            board.Started = true;
            if (board.Deal)
            {
                board.DealInto(map, board.Random);
                board.Deal = false;
            }
        }

        if (board.RestartRequested)
            Restart(board, map);

        // Matched gems gone, what's above falls.
        if (board.Clearing.Count > 0)
        {
            if (board.Clearing.Values.Min() < board.ClearTime)
            {
                ReadHands(board, map, swaps: false);
                return;
            }

            var grid = TileGrid.Read(board.Columns, board.Rows, map);
            foreach (var ((x, y), cleared) in board.Clearing)
            {
                board.Vanished.Add(new VanishedGem(x, y, grid[x, y], board.ClearingFrom.GetValueOrDefault((x, y), cleared), cleared));
                grid[x, y] = -1;
            }

            board.Clearing.Clear();
            Collapse(board, grid);
            grid.Write(map);
        }

        if (board.Swapping is { } swap && !board.Motions.ContainsKey(swap.A) && !board.Motions.ContainsKey(swap.B))
            board.Swapping = null;

        if (board.IsBusy || board.Swapping is not null)
        {
            ReadHands(board, map, swaps: false);
            return;
        }

        Settle(board, map);
        if (board.IsBusy)
        {
            ReadHands(board, map, swaps: false);
            return;
        }

        // Once the level is over the gems can't be swapped any more.
        ReadHands(board, map, swaps: !board.IsOver);
        if (board.SwapRequests.Count > 0)
        {
            var (a, b) = board.SwapRequests[0];
            board.SwapRequests.Clear();
            if (!board.IsOver)
                StartSwap(board, map, a, b);
        }
    }

    // A still board falls into its gaps, then clears what lines up, then says it has come to rest and shuffles if it's stuck.
    private void Settle(MatchBoard board, Tilemap map)
    {
        var grid = TileGrid.Read(board.Columns, board.Rows, map);
        if (Collapse(board, grid))
        {
            grid.Write(map);
            return;
        }

        var matches = MatchRules.Find(grid, board.MatchLength, board.IsGem);
        if (matches.Count > 0)
        {
            Clear(board, matches);
            return;
        }

        if (board.AwaitingSettle)
        {
            board.AwaitingSettle = false;
            if (!string.IsNullOrWhiteSpace(board.SettledMessage))
                Game.Send(board.SettledMessage.Trim(), board.Chain, from: board.Entity);
            board.RaiseSettled(board.Chain);
            if (board.IsOver)
                Finish(board);
        }

        if (board.IsOver)
            return;
        var stuck = board.ShuffleWhenStuck && !ReferenceEquals(map.Tiles, board.StuckTiles) &&
                    MatchRules.FindMove(grid, board.MatchLength, board.IsGem) is null;
        if (board.ShuffleRequested || stuck)
        {
            board.ShuffleRequested = false;
            ShuffleBoard(board, map, grid);
        }
    }

    // Gems fall down their column onto the gem or block below, and new ones fill what's left at the top (dropping in from
    // above the board, or growing just under a block). True if anything moved.
    private static bool Collapse(MatchBoard board, TileGrid grid)
    {
        var kinds = board.Kinds();
        var moved = false;
        for (var x = 0; x < grid.Columns; x++)
        {
            var bottom = grid.Rows - 1;
            while (bottom >= 0)
            {
                // A stretch of the column between blocks (or the board's edges), from top to bottom.
                if (grid[x, bottom] >= 0 && !board.IsGem(grid[x, bottom]))
                {
                    bottom--;
                    continue;
                }

                var top = bottom;
                while (top > 0 && (grid[x, top - 1] < 0 || board.IsGem(grid[x, top - 1])))
                    top--;

                var landing = bottom;
                for (var y = bottom; y >= top; y--)
                {
                    if (grid[x, y] < 0)
                        continue;
                    if (y != landing)
                    {
                        (grid[x, landing], grid[x, y]) = (grid[x, y], -1);
                        board.Motions[(x, landing)] = Fall(board, landing - y);
                        board.Motions.Remove((x, y));
                        moved = true;
                    }

                    landing--;
                }

                if (board.Refill && kinds.Count > 0)
                {
                    var empty = landing - top + 1;
                    for (var y = top; y <= landing; y++)
                    {
                        grid[x, y] = kinds[board.Random!.Next(kinds.Count)];
                        board.Motions[(x, y)] = top == 0
                            ? Fall(board, empty)
                            : new GemMotion(Vector2.Zero, Math.Max(board.ClearTime, 0.05f), falls: false, grows: true);
                        moved = true;
                    }
                }

                bottom = top - 1;
            }
        }

        return moved;

        static GemMotion Fall(MatchBoard board, int cells) =>
            new(new Vector2(0, -cells), Math.Max(board.FallTime, 0) * MathF.Sqrt(cells), falls: true);
    }

    // Its Restart action: a fresh deal (the blocks stay), with nothing under way and no swaps, score or gems cleared.
    private static void Restart(MatchBoard board, Tilemap map)
    {
        board.RestartRequested = false;
        board.Motions.Clear();
        board.Clearing.Clear();
        board.SwapRequests.Clear();
        board.Swapping = null;
        (board.ShuffleRequested, board.AwaitingSettle, board.StuckTiles, board.Chain) = (false, false, null, 0);
        (board.Swaps, board.Score, board.Cleared) = (0, 0, null);
        foreach (var hand in board.Hands.Values)
            hand.Picked = null;
        board.DealInto(map, board.Random!);
    }

    // The level is over, the board at rest: won if its goals are met, otherwise out of moves.
    private void Finish(MatchBoard board)
    {
        var won = board.GoalsMet;
        foreach (var hand in board.Hands.Values)
            hand.Picked = null;
        Game.PlayIfSet(won ? board.WinSound : board.OutOfMovesSound);
        var message = won ? board.WinMessage : board.OutOfMovesMessage;
        if (!string.IsNullOrWhiteSpace(message))
            Game.Send(message.Trim(), board.Entity, from: board.Entity);
        board.RaiseFinished(won);
    }

    // What lined up starts to shrink away: one more in the chain, scoring its gems' points times the chain.
    private void Clear(MatchBoard board, List<(int Kind, List<(int X, int Y)> Cells)> matches)
    {
        board.Chain++;
        foreach (var (kind, cells) in matches)
        {
            foreach (var cell in cells)
                board.Clearing[cell] = 0;
            board.AddCleared(kind, cells.Count);
        }

        var points = board.Clearing.Count * Math.Max(0, board.Points) * Math.Max(1, board.Chain);
        board.Score += points;
        Game.PlayIfSet(board.MatchSound);
        if (!string.IsNullOrWhiteSpace(board.MatchMessage))
            Game.Send(board.MatchMessage.Trim(), board.Clearing.Count, from: board.Entity);
        if (points > 0 && !string.IsNullOrWhiteSpace(board.ScoreMessage))
            Game.Send(board.ScoreMessage.Trim(), points, from: board.Entity);
        if (board.Chain >= 2 && !string.IsNullOrWhiteSpace(board.ChainMessage))
            Game.Send(board.ChainMessage.Trim(), board.Chain, from: board.Entity);
        foreach (var (kind, cells) in matches)
            board.RaiseMatched(new MatchInfo(kind, cells, board.Chain));
    }

    // A swap that lines something up is made there and then (and sets the chain off). One that doesn't, the gems only
    // nudge into each other's cells and slide back, the tiles as they were, unless any two neighbors may swap.
    private void StartSwap(MatchBoard board, Tilemap map, (int X, int Y) a, (int X, int Y) b)
    {
        var grid = TileGrid.Read(board.Columns, board.Rows, map);
        if (!Neighbors(a, b) || !board.IsGem(grid[a.X, a.Y]) || !board.IsGem(grid[b.X, b.Y]))
            return;
        (grid[a.X, a.Y], grid[b.X, b.Y]) = (grid[b.X, b.Y], grid[a.X, a.Y]);
        var seconds = Math.Max(board.SwapTime, 0);
        if (board.OnlyMatchingSwaps && MatchRules.Find(grid, board.MatchLength, board.IsGem).Count == 0)
        {
            board.Motions[a] = new GemMotion(new Vector2(b.X - a.X, b.Y - a.Y), seconds * 2, falls: false) { Returns = true };
            board.Motions[b] = new GemMotion(new Vector2(a.X - b.X, a.Y - b.Y), seconds * 2, falls: false) { Returns = true };
            board.Swapping = (a, b, true);
            Game.PlayIfSet(board.NoMatchSound);
            return;
        }

        // Each gem slides from its old cell into the other's.
        grid.Write(map);
        board.Motions[a] = new GemMotion(new Vector2(b.X - a.X, b.Y - a.Y), seconds, falls: false);
        board.Motions[b] = new GemMotion(new Vector2(a.X - b.X, a.Y - b.Y), seconds, falls: false);
        board.Swapping = (a, b, false);
        board.Swaps++;
        (board.Chain, board.AwaitingSettle) = (0, true);
        Game.PlayIfSet(board.SwapSound);
        board.RaiseSwapped(a, b);
    }

    // The gems trade places, each sliding from its old cell to its new one.
    private void ShuffleBoard(MatchBoard board, Tilemap map, TileGrid grid)
    {
        var from = MatchRules.Shuffle(grid, board.MatchLength, board.IsGem, board.Random!);
        if (from.Count == 0)
            return;
        foreach (var (cell, was) in from)
        {
            if (cell != was)
                board.Motions[cell] = new GemMotion(new Vector2(was.X - cell.X, was.Y - cell.Y), ShuffleTime, falls: false);
        }

        grid.Write(map);
        board.StuckTiles = MatchRules.FindMove(grid, board.MatchLength, board.IsGem) is null ? map.Tiles : null;
        foreach (var hand in board.Hands.Values)
            hand.Picked = null;
        Game.PlayIfSet(board.ShuffleSound);
        if (!string.IsNullOrWhiteSpace(board.ShuffleMessage))
            Game.Send(board.ShuffleMessage.Trim(), board.Entity, from: board.Entity);
    }

    // ----- Hands: the mouse, keys and gamepads -----

    // Everyone playing (not while a panel pauses the game): this machine's mouse and keys, and on the host every other
    // player's. While the board moves, gems can still be picked up and the cursor moved, but swaps wait.
    private void ReadHands(MatchBoard board, Tilemap map, bool swaps)
    {
        // Without focus it ignores everyone's input (another part of the scene has it), and puts down what was picked up.
        if (!board.Controllable)
        {
            foreach (var hand in board.Hands.Values)
                (hand.Picked, hand.Pressed, hand.Cursor) = (null, null, null);
            return;
        }

        if (Game.PausedThisFrame || Game.Net.IsClient)
            return;
        if (Game.TopPanel is null)
            Play(board, map, Game.Net.LocalPlayer?.Id ?? 0, Game.LocalInput, swaps);
        if (Game.Net.IsHost)
        {
            foreach (var player in Game.Net.Players.Where(p => !p.IsLocal && p.Input is not null).ToList())
                Play(board, map, player.Id, player.Input!, swaps);
        }
    }

    private void Play(MatchBoard board, Tilemap map, int player, IInput input, bool swaps)
    {
        if (!board.Hands.TryGetValue(player, out var hand))
            board.Hands[player] = hand = new MatchHand();

        PlayMouse(board, map, hand, input, swaps);
        PlayKeys(board, hand, input, swaps);

        // A gem picked up that isn't one any more (it cleared) is let go.
        if (hand.Picked is { } picked && !board.IsGem(board.GemAt(picked.X, picked.Y)))
            hand.Picked = null;
    }

    // Press on a gem and drag it onto a neighbor; or click one, then a neighbor. Clicking it again puts it down.
    private static void PlayMouse(MatchBoard board, Tilemap map, MatchHand hand, IInput input, bool swaps)
    {
        // Dragged more than half a cell (held, or let go just now: a quick flick), it swaps with the neighbor that way.
        if (hand.Pressed is { } from && swaps && input.IsMouseInView && (input.IsDown(MouseButton.Left) || input.WasReleased(MouseButton.Left)) &&
            DraggedTo(map, from, input.MouseWorld) is { } to)
        {
            board.Swap(from.X, from.Y, to.X, to.Y);
            (hand.Picked, hand.Pressed) = (null, null);
            return;
        }

        if (hand.Pressed is { } held && !input.IsDown(MouseButton.Left))
        {
            if (!hand.PickedByPress && hand.Picked == held)
                hand.Picked = null;
            hand.Pressed = null;
        }

        if (!input.IsMouseInView)
            return;
        var under = CellUnder(board, map, input.MouseWorld);
        if (input.WasPressed(MouseButton.Left))
        {
            hand.Cursor = null;
            if (under is not { } cell || !board.IsGem(board.GemAt(cell.X, cell.Y)))
            {
                hand.Picked = null;
                return;
            }

            if (hand.Picked is { } picked && Neighbors(picked, cell) && swaps)
            {
                board.Swap(picked.X, picked.Y, cell.X, cell.Y);
                (hand.Picked, hand.Pressed) = (null, null);
                return;
            }

            (hand.PickedByPress, hand.Picked, hand.Pressed) = (hand.Picked != cell, cell, cell);
        }
    }

    // The neighbor of a cell that the mouse has been dragged more than half a cell towards, or null.
    private static (int X, int Y)? DraggedTo(Tilemap map, (int X, int Y) from, Vector2 mouse)
    {
        if (!Matrix4x4.Invert(map.Entity!.Transform.WorldMatrix, out var toLocal))
            return null;
        var away = (Vector2.Transform(mouse, toLocal) - map.CellCenter(from.X, from.Y)) / (map.TileSize > 0 ? map.TileSize : 1);
        if (MathF.Max(MathF.Abs(away.X), MathF.Abs(away.Y)) < 0.5f)
            return null;
        return MathF.Abs(away.X) >= MathF.Abs(away.Y) ? (from.X + MathF.Sign(away.X), from.Y) : (from.X, from.Y - MathF.Sign(away.Y));
    }

    // The arrows move a cursor (the first press just shows it); the pick action picks the gem under it up or puts it down,
    // and an arrow swaps a picked-up gem that way.
    private void PlayKeys(MatchBoard board, MatchHand hand, IInput input, bool swaps)
    {
        var middle = (Math.Max(0, board.Columns - 1) / 2, Math.Max(0, board.Rows - 1) / 2);
        var way = BoardDirection(hand, input);
        if (way != default)
        {
            if (hand.Picked is { } picked)
            {
                if (!swaps)
                    return;
                var to = (picked.X + way.X, picked.Y + way.Y);
                board.Swap(picked.X, picked.Y, to.Item1, to.Item2);
                hand.Picked = null;
                hand.Cursor = OnBoard(board, to) ? to : picked;
            }
            else if (hand.Cursor is not { } cursor)
            {
                hand.Cursor = middle;
            }
            else
            {
                var next = (cursor.X + way.X, cursor.Y + way.Y);
                if (OnBoard(board, next))
                    hand.Cursor = next;
            }
        }

        var action = board.PickAction?.Trim();
        if (string.IsNullOrEmpty(action) || !input.WasPressed(action))
            return;
        if (hand.Cursor is not { } at)
        {
            hand.Cursor = hand.Picked ?? middle;
            return;
        }

        hand.Picked = hand.Picked == at || !board.IsGem(board.GemAt(at.X, at.Y)) ? null : at;
    }

    // An arrow (WASD, the d-pad or the stick) pressed this frame, or repeating while it's held.
    private (int X, int Y) BoardDirection(MatchHand hand, IInput input)
    {
        var stick = input.LeftStick;
        var x = (input.IsDown(Key.Right) || input.IsDown(Key.D) || input.IsDown(GamepadButton.DPadRight) || stick.X > Game.NavigateStick ? 1 : 0)
                - (input.IsDown(Key.Left) || input.IsDown(Key.A) || input.IsDown(GamepadButton.DPadLeft) || stick.X < -Game.NavigateStick ? 1 : 0);
        var up = (input.IsDown(Key.Up) || input.IsDown(Key.W) || input.IsDown(GamepadButton.DPadUp) || stick.Y > Game.NavigateStick ? 1 : 0)
                 - (input.IsDown(Key.Down) || input.IsDown(Key.S) || input.IsDown(GamepadButton.DPadDown) || stick.Y < -Game.NavigateStick ? 1 : 0);
        // Rows count down the board.
        (int X, int Y) held = up != 0 && (x == 0 || MathF.Abs(stick.Y) >= MathF.Abs(stick.X)) ? (0, -up) : (x, 0);
        if (held != hand.Held)
        {
            (hand.Held, hand.HeldFor, hand.NextRepeat) = (held, 0, Game.CursorRepeatDelay);
            return held;
        }

        if (held == default)
            return default;
        hand.HeldFor += Time.UnscaledDelta;
        if (hand.HeldFor < hand.NextRepeat)
            return default;
        hand.NextRepeat += Game.CursorRepeatEvery;
        return held;
    }

    // The board's cell under a point in the world, or null off the board.
    private static (int X, int Y)? CellUnder(MatchBoard board, Tilemap map, Vector2 world)
    {
        if (!Matrix4x4.Invert(map.Entity!.Transform.WorldMatrix, out var toLocal))
            return null;
        var cell = map.CellAt(Vector2.Transform(world, toLocal));
        return OnBoard(board, cell) ? cell : null;
    }

    private static bool OnBoard(MatchBoard board, (int X, int Y) cell) =>
        cell.X >= 0 && cell.Y >= 0 && cell.X < board.Columns && cell.Y < board.Rows;

    private static bool Neighbors((int X, int Y) a, (int X, int Y) b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) == 1;
}
