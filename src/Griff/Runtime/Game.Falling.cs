using Griff.Scenes;

namespace Griff.Runtime;

// Falling-block boards with no code: pieces that come in at the top, slide, turn and drop at the player's keys, lock where
// they land, and full rows that clear, speeding the game up. The host runs the boards for everyone; players' machines
// draw the piece it sends them.
public sealed partial class Game
{
    // How many times sliding or turning a resting piece starts its lock wait again, so it can't be kept up forever.
    private const int MaxLockResets = 15;

    // Where a turned piece tries to go when it doesn't fit where it is: beside, then up, then further beside.
    private static readonly (int X, int Y)[] Kicks = [(0, 0), (-1, 0), (1, 0), (0, -1), (-2, 0), (2, 0), (-1, -1), (1, -1)];

    // Rows cleared at once, and how many times a single row's points they score.
    private static readonly int[] LinesScore = [0, 1, 3, 5, 8];

    // After scripts (so a move a script asks for is made this frame).
    private void RunFallingBoards()
    {
        foreach (var board in Showing<FallingBoard>())
        {
            if (board.Entity!.GetComponent<Tilemap>() is not { } map || !Runs(board.Entity))
                continue;
            RunFalling(board, map);
        }
    }

    private void RunFalling(FallingBoard board, Tilemap map)
    {
        board.Random ??= new Random(board.Seed != 0 ? board.Seed : Rand.Range(1, int.MaxValue));
        board.Started = true;
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
            board.ClearingFor += Time.Delta;
            board.Requests.Clear();
            if (board.ClearingFor < board.ClearTime)
                return;
            RemoveRows(board, map);
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
        PlayIfSet(board.GameOverSound);
        if (!string.IsNullOrWhiteSpace(board.GameOverMessage))
            Send(board.GameOverMessage.Trim(), board.Entity);
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
        PlayIfSet(board.MoveSound);
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
            PlayIfSet(board.RotateSound);
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
        var grid = MatchBoard.Grid.Read(board.Columns, board.Rows, map);
        foreach (var (x, y) in cells.Where(c => c.Y >= 0))
            grid[x, y] = board.TileOf(piece.Shape);
        grid.Write(map);
        PlayIfSet(board.LockSound);
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
        PlayIfSet(board.ClearSound);
        if (!string.IsNullOrWhiteSpace(board.LinesMessage))
            Send(board.LinesMessage.Trim(), full.Count);
        if (points > 0 && !string.IsNullOrWhiteSpace(board.ScoreMessage))
            Send(board.ScoreMessage.Trim(), points);
        board.RaiseCleared(full.Count);
        if (levels <= 0)
            return;
        board.Level += levels;
        PlayIfSet(board.LevelUpSound);
        if (!string.IsNullOrWhiteSpace(board.LevelMessage))
            Send(board.LevelMessage.Trim(), levels);
        board.RaiseLeveledUp(board.Level);
    }

    // The full rows go and everything above them drops down.
    private static void RemoveRows(FallingBoard board, Tilemap map)
    {
        var grid = MatchBoard.Grid.Read(board.Columns, board.Rows, map);
        var kept = Enumerable.Range(0, grid.Rows).Where(y => !board.ClearingRows.Contains(y)).ToList();
        var dropped = new MatchBoard.Grid { Cells = new int[grid.Columns, grid.Rows] };
        var gap = grid.Rows - kept.Count;
        for (var y = 0; y < grid.Rows; y++)
        {
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
        if (PausedThisFrame || Net.IsClient)
            return false;
        var soft = false;
        if (TopPanel is null)
            soft |= ReadFallingKeys(board, Net.LocalPlayer?.Id ?? 0, _input);
        if (Net.IsHost)
        {
            foreach (var player in Net.Players.Where(p => !p.IsLocal && p.Input is not null).ToList())
                soft |= ReadFallingKeys(board, player.Id, player.Input!);
        }

        return soft;
    }

    private bool ReadFallingKeys(FallingBoard board, int player, IInput input)
    {
        var stick = input.LeftStick;
        var way = (input.IsDown(Key.Right) || input.IsDown(Key.D) || input.IsDown(GamepadButton.DPadRight) || stick.X > NavigateStick ? 1 : 0)
                  - (input.IsDown(Key.Left) || input.IsDown(Key.A) || input.IsDown(GamepadButton.DPadLeft) || stick.X < -NavigateStick ? 1 : 0);
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

        var down = input.IsDown(Key.Down) || input.IsDown(Key.S) || input.IsDown(GamepadButton.DPadDown) || stick.Y < -NavigateStick;
        // Pressing down falls a row straight away.
        if (down && (input.WasPressed(Key.Down) || input.WasPressed(Key.S) || input.WasPressed(GamepadButton.DPadDown)))
            board.FallFor = Math.Max(board.FallFor, Math.Max(board.SoftDropTime, 1e-3f));
        return down;
    }
}
