using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.FallingBlocks;

/// <summary>
/// Runs falling-block boards (<see cref="FallingBoard"/>) with no code: pieces that come in at the top, slide, turn and
/// drop at the player's keys, lock where they land, and full rows that clear, speeding the game up. The host runs the
/// boards for everyone; players' machines draw the piece it sends them.
/// </summary>
internal sealed class FallingBlocksSystem : GameSystem
{
    public override string Name => "Falling Boards";

    // How many times sliding or turning a resting piece starts its lock wait again, so it can't be kept up forever.
    private const int MaxLockResets = 15;

    // Where a turned piece tries to go when it doesn't fit where it is: beside, then up, then further beside.
    private static readonly (int X, int Y)[] Kicks = [(0, 0), (-1, 0), (1, 0), (0, -1), (-2, 0), (2, 0), (-1, -1), (1, -1)];

    // Rows cleared at once, and how many times a single row's points they score.
    private static readonly int[] LinesScore = [0, 1, 3, 5, 8];

    // After scripts (so a move a script asks for is made this frame).
    internal void RunBoards()
    {
        foreach (var board in Game.Showing<FallingBoard>())
        {
            // Held still with a group it's in (see Entity.IsPaused).
            if (board.Entity!.GetComponent<Tilemap>() is not { } map || !Game.Runs(board.Entity) || board.Entity.IsPaused)
                continue;
            RunFalling(board, map);
        }
    }

    private void RunFalling(FallingBoard board, Tilemap map)
    {
        board.Random ??= new Random(board.Seed != 0 ? board.Seed : Rand.Range(1, int.MaxValue));
        board.Started = true;
        board.StartLevel ??= board.Level;
        if (board.RestartRequested)
            Restart(board, map);
        if (board.Paused)
        {
            board.Requests.Clear();
            return;
        }

        TopUp(board);
        if (board.GameOver)
        {
            board.Falling = null;
            board.Requests.Clear();
            return;
        }

        // Full rows flash, then go, and the next piece comes in.
        if (board.ClearingRows.Count > 0)
        {
            var clearedFor = board.ClearingFor;
            board.ClearingFor += Time.Delta;
            board.Requests.Clear();
            if (board.ClearingFor < board.ClearTime)
                return;
            RemoveRows(board, map, (clearedFor, board.ClearingFor));
        }

        if (board.Falling is null && !Spawn(board))
            return;

        var softDrop = ReadFallingKeys(board);
        foreach (var request in board.Requests.ToList())
        {
            if (board.Falling is null)
                break;
            switch (request)
            {
                case FallingRequest.Left or FallingRequest.Right:
                    SlidePiece(board, request == FallingRequest.Left ? -1 : 1);
                    break;
                case FallingRequest.Turn or FallingRequest.TurnBack:
                    TurnPiece(board, request == FallingRequest.Turn ? 1 : -1);
                    break;
                case FallingRequest.Drop:
                    board.Falling = board.Falling with { Y = board.Falling.Y + board.DropDistance() };
                    LockPiece(board, map);
                    break;
            }
        }

        board.Requests.Clear();
        if (board.Falling is null || Time.Delta <= 0)
            return;

        // It falls a row every so often (faster held down), and once it rests on something for long enough, it locks.
        var every = FallInterval(board);
        if (softDrop)
            every = Math.Min(every, Math.Max(board.SoftDropTime, 1e-3f));
        board.FallFor += Time.Delta;
        while (board.FallFor >= every)
        {
            board.FallFor -= every;
            if (!FallRow(board))
            {
                board.FallFor = 0;
                break;
            }
        }

        var piece = board.Falling;
        if (board.Fits(piece.Shape, piece.Rotation, piece.X, piece.Y + 1))
        {
            board.RestingFor = 0;
            return;
        }

        board.RestingFor += Time.Delta;
        if (board.RestingFor >= board.LockTime)
            LockPiece(board, map);
    }

    // Its Restart action: an empty board at the level it started at, with no score or lines, and a new piece coming in.
    private static void Restart(FallingBoard board, Tilemap map)
    {
        board.RestartRequested = false;
        if (board.Columns > 0 && board.Rows > 0)
            map.FillRect(0, 0, board.Columns - 1, board.Rows - 1, -1);
        board.Falling = null;
        board.ClearingRows.Clear();
        board.ClearingFor = 0;
        board.Requests.Clear();
        board.Held.Clear();
        (board.FallFor, board.RestingFor, board.LockResets) = (0, 0, 0);
        (board.Score, board.Lines, board.Level, board.GameOver) = (0, 0, board.StartLevel ?? 1, false);
    }

    // Seconds a row at the board's level: each level's fall takes Speedup of the one before's.
    private static float FallInterval(FallingBoard board) =>
        Math.Max(board.FallTime * MathF.Pow(Math.Clamp(board.Speedup, 0.01f, 1), Math.Max(0, board.Level - 1)), 1 / 120f);

    // Keeps enough shapes coming to show, a shuffled round of them at a time.
    private static void TopUp(FallingBoard board)
    {
        while (board.Coming.Count <= Math.Max(board.Preview, 1))
        {
            var round = FallingShapes.Parse(board.Pieces).OrderBy(_ => board.Random!.Next()).ToList();
            board.Coming.AddRange(round);
        }
    }

    // The next piece comes in at the top, in the middle. If there's no room for it, the game is over.
    private bool Spawn(FallingBoard board)
    {
        TopUp(board);
        var shape = board.Coming[0];
        board.Coming.RemoveAt(0);
        TopUp(board);
        var top = FallingShapes.Cells(shape, 0, 0, 0).Min(c => c.Y);
        var piece = new FallingPiece(shape, (board.Columns - FallingShapes.Size(shape)) / 2, -top, 0);
        (board.FallFor, board.RestingFor, board.LockResets) = (0, 0, 0);
        if (!board.Fits(piece.Shape, 0, piece.X, piece.Y))
        {
            board.Falling = null;
            EndFalling(board);
            return false;
        }

        board.Falling = piece;
        return true;
    }

    private void EndFalling(FallingBoard board)
    {
        board.GameOver = true;
        Game.PlayIfSet(board.GameOverSound);
        if (!string.IsNullOrWhiteSpace(board.GameOverMessage))
            Game.Send(board.GameOverMessage.Trim(), board.Entity, from: board.Entity);
        board.RaiseEnded();
    }

    private static bool FallRow(FallingBoard board)
    {
        var piece = board.Falling!;
        if (!board.Fits(piece.Shape, piece.Rotation, piece.X, piece.Y + 1))
            return false;
        board.Falling = piece with { Y = piece.Y + 1 };
        return true;
    }

    private void SlidePiece(FallingBoard board, int way)
    {
        var piece = board.Falling!;
        if (!board.Fits(piece.Shape, piece.Rotation, piece.X + way, piece.Y))
            return;
        board.Falling = piece with { X = piece.X + way };
        Moved(board);
        Game.PlayIfSet(board.MoveSound);
    }

    // Turns a quarter, nudged beside or up if it doesn't fit where it is (off a wall, out of the floor).
    private void TurnPiece(FallingBoard board, int way)
    {
        var piece = board.Falling!;
        if (piece.Shape == 'O')
            return;
        var rotation = ((piece.Rotation + way) % 4 + 4) % 4;
        foreach (var (dx, dy) in Kicks)
        {
            if (!board.Fits(piece.Shape, rotation, piece.X + dx, piece.Y + dy))
                continue;
            board.Falling = piece with { X = piece.X + dx, Y = piece.Y + dy, Rotation = rotation };
            Moved(board);
            Game.PlayIfSet(board.RotateSound);
            return;
        }
    }

    // Sliding or turning a resting piece gives it a fresh moment before it locks, a few times over.
    private static void Moved(FallingBoard board)
    {
        if (board.RestingFor <= 0 || board.LockResets >= MaxLockResets)
            return;
        board.RestingFor = 0;
        board.LockResets++;
    }

    // The piece becomes blocks on the board; full rows start to clear (scoring them) or the next piece comes in.
    private void LockPiece(FallingBoard board, Tilemap map)
    {
        var piece = board.Falling!;
        var cells = FallingShapes.Cells(piece.Shape, piece.Rotation, piece.X, piece.Y);
        board.Falling = null;
        var grid = TileGrid.Read(board.Columns, board.Rows, map);
        foreach (var (x, y) in cells.Where(c => c.Y >= 0))
            grid[x, y] = board.TileOf(piece.Shape);
        grid.Write(map);
        Game.PlayIfSet(board.LockSound);
        board.RaiseLocked(cells);

        var full = Enumerable.Range(0, grid.Rows).Where(y => grid.Columns > 0 && Enumerable.Range(0, grid.Columns).All(x => grid[x, y] >= 0)).ToList();
        if (full.Count == 0)
        {
            // A piece that locked partly above the board has buried the top: that's the end too.
            if (cells.Any(c => c.Y < 0))
                EndFalling(board);
            else
                Spawn(board);
            return;
        }

        board.ClearingRows.AddRange(full);
        board.ClearingFor = 0;
        var points = Math.Max(0, board.Points) * LinesScore[Math.Min(full.Count, LinesScore.Length - 1)] * Math.Max(1, board.Level);
        var levels = board.LinesPerLevel > 0 ? (board.Lines + full.Count) / board.LinesPerLevel - board.Lines / board.LinesPerLevel : 0;
        board.Lines += full.Count;
        board.Score += points;
        Game.PlayIfSet(board.ClearSound);
        if (!string.IsNullOrWhiteSpace(board.LinesMessage))
            Game.Send(board.LinesMessage.Trim(), full.Count, from: board.Entity);
        if (points > 0 && !string.IsNullOrWhiteSpace(board.ScoreMessage))
            Game.Send(board.ScoreMessage.Trim(), points, from: board.Entity);
        board.RaiseCleared(full.Count);
        if (levels <= 0)
            return;
        board.Level += levels;
        Game.PlayIfSet(board.LevelUpSound);
        if (!string.IsNullOrWhiteSpace(board.LevelMessage))
            Game.Send(board.LevelMessage.Trim(), levels, from: board.Entity);
        board.RaiseLeveledUp(board.Level);
    }

    // The full rows go and everything above them drops down. What went, and how far each row dropped, is kept for a frame
    // drawn between this step and the last: the rows shrink the rest of the way away as the rest drops into their place.
    private static void RemoveRows(FallingBoard board, Tilemap map, (float From, float To) clearing)
    {
        var grid = TileGrid.Read(board.Columns, board.Rows, map);
        var kept = Enumerable.Range(0, grid.Rows).Where(y => !board.ClearingRows.Contains(y)).ToList();
        var dropped = new TileGrid { Cells = new int[grid.Columns, grid.Rows] };
        var gap = grid.Rows - kept.Count;
        board.RemovedIn = board.Entity!.Scene?.Step ?? 0;
        board.RemovedClearing = clearing;
        board.Removed.Clear();
        foreach (var y in board.ClearingRows.Where(y => y >= 0 && y < grid.Rows))
        {
            for (var x = 0; x < grid.Columns; x++)
            {
                if (grid[x, y] >= 0)
                    board.Removed.Add((x, y, grid[x, y]));
            }
        }

        board.DroppedBy = new int[grid.Rows];
        for (var y = 0; y < grid.Rows; y++)
        {
            if (y >= gap)
                board.DroppedBy[y] = y - kept[y - gap];
            for (var x = 0; x < grid.Columns; x++)
                dropped[x, y] = y < gap ? -1 : grid[x, kept[y - gap]];
        }

        dropped.Write(map);
        board.ClearingRows.Clear();
        board.ClearingFor = 0;
    }

    // ----- The keys: this machine's, and on the host every other player's -----

    // Left and right slide (repeating while held), up or the Rotate Action turns, the Drop Action drops; true while
    // anyone holds down. Nobody's keys reach it while a panel is open.
    private bool ReadFallingKeys(FallingBoard board)
    {
        // Without focus it ignores everyone's keys (another part of the scene has them).
        if (Game.PausedThisFrame || Game.Net.IsClient || !board.Controllable)
            return false;
        var soft = false;
        if (Game.TopPanel is null)
            soft |= ReadFallingKeys(board, Game.Net.LocalPlayer?.Id ?? 0, Game.LocalInput);
        if (Game.Net.IsHost)
        {
            foreach (var player in Game.Net.Players.Where(p => !p.IsLocal && p.Input is not null).ToList())
                soft |= ReadFallingKeys(board, player.Id, player.Input!);
        }

        return soft;
    }

    private bool ReadFallingKeys(FallingBoard board, int player, IInput input)
    {
        var stick = input.LeftStick;
        var way = (input.IsDown(Key.Right) || input.IsDown(Key.D) || input.IsDown(GamepadButton.DPadRight) || stick.X > Game.NavigateStick ? 1 : 0)
                  - (input.IsDown(Key.Left) || input.IsDown(Key.A) || input.IsDown(GamepadButton.DPadLeft) || stick.X < -Game.NavigateStick ? 1 : 0);
        var held = board.Held.GetValueOrDefault(player);
        if (way != held.Way)
        {
            held = (way, 0, Math.Max(board.RepeatDelay, 0));
            if (way != 0)
                board.Move(way);
        }
        else if (way != 0)
        {
            held.HeldFor += Time.UnscaledDelta;
            var every = Math.Max(board.RepeatEvery, 1 / 120f);
            while (held.HeldFor >= held.NextRepeat)
            {
                held.NextRepeat += every;
                board.Move(way);
            }
        }

        board.Held[player] = held;

        var rotate = board.RotateAction?.Trim();
        if (input.WasPressed(Key.Up) || input.WasPressed(Key.W) || input.WasPressed(GamepadButton.DPadUp) ||
            (!string.IsNullOrEmpty(rotate) && input.WasPressed(rotate)))
            board.Rotate();
        var drop = board.DropAction?.Trim();
        if (!string.IsNullOrEmpty(drop) && input.WasPressed(drop))
            board.Drop();

        var down = input.IsDown(Key.Down) || input.IsDown(Key.S) || input.IsDown(GamepadButton.DPadDown) || stick.Y < -Game.NavigateStick;
        // Pressing down falls a row straight away.
        if (down && (input.WasPressed(Key.Down) || input.WasPressed(Key.S) || input.WasPressed(GamepadButton.DPadDown)))
            board.FallFor = Math.Max(board.FallFor, Math.Max(board.SoftDropTime, 1e-3f));
        return down;
    }
}
