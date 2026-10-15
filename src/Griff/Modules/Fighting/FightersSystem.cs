using System.Numerics;
using Griff.Data;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.Fighting;

/// <summary>
/// Runs every <see cref="FightingCharacter"/> each step, after the player movements and before scripts: reads its player's
/// direction (as it faces) and button presses into its input history, turns it to its opponent, and then, as hitstun,
/// blockstun and the move it's doing allow, starts the move its input (or a press buffered from a few steps before, or its
/// Perform action) makes, cancels into one, or walks, jumps and crouches. A move's clip is shown frame by frame over its
/// startup, active and recovery steps; its hitboxes hit only while it's active (<see cref="HitboxesSystem"/> asks
/// <see cref="CanStrike"/>). Where the game is run (offline, or the host); everything is counted in steps.
/// </summary>
internal sealed class FightersSystem : GameSystem
{
    // Within this far of each other along X, fighters don't turn (so two stacked don't flip every step).
    private const float TurnDeadZone = 0.05f;

    // A stick pushed this far counts as a direction.
    private const float StickThreshold = 0.5f;

    public override string Name => "Fighters";

    // Each fighter's moves, its own and its table's, with what they were made from.
    private readonly Dictionary<FightingCharacter, Moves> _moves = [];

    private sealed class Moves
    {
        public IReadOnlyList<string>? Lines;
        public string? Table;
        public int TablesVersion = -1;
        public IReadOnlyList<FighterMove> All = [];
    }

    internal void Run()
    {
        var game = Game;
        if (game.PausedThisFrame)
            return;
        var fighters = game.Active<FightingCharacter>();
        // Through a hit stop (or time held still) nothing moves, but what's pressed is still read: a cancel pressed as the
        // hit lands comes out after it.
        var frozen = game.Time.Delta <= 0;
        for (var i = 0; i < fighters.Count; i++)
        {
            var fighter = fighters[i];
            if (fighter.Entity!.IsPaused)
                continue;
            if (frozen)
                Read(fighter);
            else
                Step(fighter, fighters);
        }
    }

    // This step's input, as it faces, into its history: the direction held and the buttons pressed (the computer's, for
    // one it plays; nothing while it's held). Returns the direction.
    private int Read(FightingCharacter f)
    {
        var entity = f.Entity!;
        var facing = HitboxesSystem.FacingOf(entity);
        var computer = entity.GetComponent<ComputerFighter>();
        var input = f.Controllable && !f.Held && computer is null ? Game.InputOf(entity) : null;
        var (x, y) = computer is not null && !f.Held ? (Math.Sign(computer.HoldX), Math.Sign(computer.HoldY)) : input is null ? (0, 0) : Stick(input);
        var direction = 5 + x * (int)facing + 3 * y;
        var presses = 0;
        if (input is not null)
        {
            var buttons = f.ButtonList;
            for (var b = 0; b < buttons.Count; b++)
            {
                if (input.WasPressed(buttons[b].Action))
                    presses |= 1 << b;
            }
        }

        f.Input.Push(direction, presses);
        (f.HeldX, f.HeldDown) = (x, y < 0);
        return direction;
    }

    // After physics: who's on the ground, kept with each fighter for the next step (and a rewind).
    internal void Land()
    {
        // Physics only says so once it has run (not through a hit stop, nor a pause).
        if (Game.Time.Delta <= 0 || Game.PausedThisFrame)
            return;
        var fighters = Game.Active<FightingCharacter>();
        for (var i = 0; i < fighters.Count; i++)
        {
            var f = fighters[i];
            if (!f.Entity!.IsPaused && f.Entity.GetComponent<Body>() is { } body && f.OnGround != body.IsGrounded)
                f.OnGround = body.IsGrounded;
        }
    }

    private void Step(FightingCharacter f, ComponentList<FightingCharacter> fighters)
    {
        var game = Game;
        var entity = f.Entity!;
        var body = entity.GetComponent<Body>();
        var boxes = entity.GetComponent<Hitboxes>();
        var animation = entity.GetComponent<Animation>();
        var grounded = body is null || f.OnGround;
        var moves = MovesOf(f);
        if (f.JustBlocked)
            f.JustBlocked = false;
        // A step of blockstun goes by (a hit blocked for 10 holds it 10 steps, as hitstun does).
        var blocking = f.Blockstun > 0;
        if (blocking)
            f.Blockstun--;
        if (f.Current?.Name != f.Move)
            f.Current = Find(moves, f.Move);
        if (f.Current is null && !string.IsNullOrEmpty(f.Move))
            f.Move = null;

        // A combo is over once the one it's on has recovered.
        if (f.Combo > 0 && !StillStunned(f))
            (f.Combo, f.ComboDamage, f.ComboOn) = (0, 0, null);

        var stunned = boxes is { Hitstun: > 0 };
        var opponent = OpponentOf(f, fighters);
        var free = !stunned && !blocking && f.Current is null;
        var knockedOut = entity.GetComponent<Health>() is { IsDead: true };
        if (free && grounded && f.FaceOpponent && opponent is not null && !knockedOut)
            Face(entity, opponent);

        if (entity.GetComponent<ComputerFighter>() is { } computer)
            Think(f, computer, opponent, moves, grounded, free && !knockedOut);
        if (f.Held)
            f.Requested = null;
        var direction = Read(f);
        var facing = HitboxesSystem.FacingOf(entity);

        if (knockedOut)
        {
            // Knocked out: down, and nothing more.
            EndMove(f, animation);
            (f.Blocking, f.Crouching) = (false, false);
            PlayFirst(animation, "KO", "Hurt", "Idle");
            Slow(f, body, grounded);
            return;
        }

        if (stunned)
        {
            // Hit: whatever it was doing is over, and it can't act until it recovers.
            EndMove(f, animation);
            (f.Blocking, f.Crouching) = (false, false);
            PlayFirst(animation, "Hurt", "Idle");
            Slow(f, body, grounded);
            return;
        }

        if (blocking)
        {
            EndMove(f, animation);
            f.Blocking = true;
            PlayFirst(animation, f.Crouching ? "CrouchBlock" : "Block", f.Crouching ? "Crouch" : "Idle");
            Slow(f, body, grounded);
            return;
        }

        if (f.Current is { } move)
        {
            f.MoveFrame++;
            var timing = TimingOf(f, move, animation, boxes);
            var landed = move.Input.Air && grounded && f.MoveFrame > 1;
            if (f.MoveFrame >= timing.Total || landed)
            {
                EndMove(f, animation);
            }
            else
            {
                // Once it has connected, it can cancel into what it's allowed to, from its first active step on.
                if (f.Connected && f.MoveFrame >= timing.Startup && TryStart(f, moves, grounded, animation, boxes, body, cancelFrom: move))
                    return;
                Show(f, move, timing, animation, body, grounded);
                return;
            }
        }

        // Free: a move if one is pressed (or was, a few steps ago, or asked for), or else walking, jumping and crouching.
        f.Blocking = false;
        if (!f.Held && TryStart(f, moves, grounded, animation, boxes, body, cancelFrom: null))
            return;
        Move(f, direction, body, animation, grounded, facing);
    }

    // ----- Moves -----

    // Its own moves, then its table's (read again when the table or its lines change).
    private IReadOnlyList<FighterMove> MovesOf(FightingCharacter f)
    {
        if (!_moves.TryGetValue(f, out var cached))
            _moves[f] = cached = new Moves();
        var tables = Game.Tables;
        var table = string.IsNullOrWhiteSpace(f.MovesTable) ? null : f.MovesTable.Trim();
        if (ReferenceEquals(cached.Lines, f.Moves) && cached.Table == table && (table is null || cached.TablesVersion == tables.Version))
            return cached.All;
        (cached.Lines, cached.Table, cached.TablesVersion) = (f.Moves, table, tables.Version);
        var all = new List<FighterMove>(f.MoveList);
        var where = $"Fighter {f.Entity!.Name}";
        for (var i = 0; i < f.Moves.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(f.Moves[i]) && !FighterMove.TryParse(f.Moves[i], out _, out var problem))
                Game.Warn($"{where}: move \"{f.Moves[i]}\": {problem}.");
        }

        if (table is not null)
        {
            try
            {
                if (tables.Find(table) is { } found)
                {
                    foreach (var row in found.Rows)
                    {
                        if (FighterMove.TryFromRow(row, out var move, out var problem))
                            all.Add(move);
                        else
                            Game.Warn($"{where}: {table}'s row {row.Name}: {problem}.");
                    }
                }
                else
                {
                    Game.Warn($"{where}: {tables.NoTable(table)}");
                }
            }
            catch (InvalidDataException ex)
            {
                Game.Warn($"{where}: {ex.Message}");
            }
        }

        cached.All = all;
        return all;
    }

    protected override void SceneChanged() => _moves.Clear();

    private static FighterMove? Find(IReadOnlyList<FighterMove> moves, string? name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        for (var i = 0; i < moves.Count; i++)
        {
            if (string.Equals(moves[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return moves[i];
        }

        return null;
    }

    // The move a press in its buffer (oldest first) makes, or the one Perform asked for: started, and the press used.
    private bool TryStart(FightingCharacter f, IReadOnlyList<FighterMove> moves, bool grounded, Animation? animation, Hitboxes? boxes, Body? body, FighterMove? cancelFrom)
    {
        if (f.Requested is { } requested)
        {
            f.Requested = null;
            if (Find(moves, requested) is { } asked && Allowed(asked, grounded, cancelFrom))
            {
                Start(f, asked, animation, boxes, body, grounded);
                return true;
            }
        }

        var history = f.Input;
        var buffer = Math.Clamp(f.BufferFrames, 1, InputHistory.Capacity / 2);
        var buttons = f.ButtonNames;
        for (var age = Math.Min(buffer, history.Count) - 1; age >= 0; age--)
        {
            var presses = history.Presses(age);
            if (presses == 0)
                continue;
            for (var bit = 0; bit < buttons.Length; bit++)
            {
                if ((presses & (1 << bit)) == 0)
                    continue;
                FighterMove? best = null;
                var bestScore = -1;
                for (var i = 0; i < moves.Count; i++)
                {
                    var move = moves[i];
                    if (!string.Equals(move.Input.Button, buttons[bit], StringComparison.OrdinalIgnoreCase) || !Allowed(move, grounded, cancelFrom))
                        continue;
                    var score = move.Input.Score(history, age, Math.Max(2, f.MotionFrames), Math.Clamp(f.ChargeFrames, 1, InputHistory.Capacity - 24));
                    if (score > bestScore)
                        (best, bestScore) = (move, score);
                }

                if (best is null)
                    continue;
                history.Use(age, bit);
                Start(f, best, animation, boxes, body, grounded);
                return true;
            }
        }

        return false;
    }

    // In the air or on the ground as the move is, and a cancel the move under way allows.
    private static bool Allowed(FighterMove move, bool grounded, FighterMove? cancelFrom) =>
        move.Input.Air != grounded && (cancelFrom is null || cancelFrom.CancelsInto(move));

    private void Start(FightingCharacter f, FighterMove move, Animation? animation, Hitboxes? boxes, Body? body, bool grounded)
    {
        (f.Move, f.Current, f.MoveFrame, f.Connected, f.Blocking) = (move.Name, move, 0, false, false);
        f.Crouching = grounded && move.Input.Motion.Length == 1 && move.Input.Motion[0] is '1' or '2' or '3';
        var timing = TimingOf(f, move, animation, boxes);
        Show(f, move, timing, animation, body, grounded);
        var entity = f.Entity!;
        var at = entity.Transform.WorldPosition;
        if (!string.IsNullOrWhiteSpace(move.Sound))
            Game.PlaySoundAt(move.Sound.Trim(), new Vector2(at.X, at.Y));
        if (!string.IsNullOrWhiteSpace(move.Message))
            Game.Send(move.Message.Trim(), entity, from: entity);
        f.RaiseMoveStarted(move);
    }

    // The move's frame for this step, and its push as it starts to hit; on the ground it doesn't slide otherwise.
    private void Show(FightingCharacter f, FighterMove move, MoveTiming timing, Animation? animation, Body? body, bool grounded)
    {
        f.Phase = timing.PhaseAt(f.MoveFrame);
        if (animation is not null && timing.HasClip)
        {
            var clip = ClipOf(animation, move.ClipName)!;
            if (!string.Equals(animation.Clip, clip, StringComparison.Ordinal))
                animation.Clip = clip;
            animation.Playing = false;
            // The middle of the frame, so the time never rounds into the next.
            animation.Time = (timing.FrameAt(f.MoveFrame) + 0.5f) / (timing.FramesPerSecond > 0 ? timing.FramesPerSecond : 10);
            if (f.Entity!.GetComponent<Sprite>() is { } sprite)
            {
                var (first, _, _, _) = animation.ClipSettings(clip);
                sprite.Frame = first + timing.FrameAt(f.MoveFrame);
            }
        }

        if (body is null)
            return;
        if (move.Push != Vector2.Zero && f.MoveFrame == timing.Startup)
            body.Velocity = new Vector2(move.Push.X * HitboxesSystem.FacingOf(f.Entity!), move.Push.Y);
        else if (grounded && move.Push == Vector2.Zero)
            Slow(f, body, grounded);
    }

    private static void EndMove(FightingCharacter f, Animation? animation)
    {
        if (f.Current is null && string.IsNullOrEmpty(f.Move))
            return;
        (f.Move, f.Current, f.MoveFrame, f.Connected, f.Phase) = (null, null, 0, false, MovePhase.None);
        if (animation is not null)
            animation.Playing = true;
    }

    private MoveTiming TimingOf(FightingCharacter f, FighterMove move, Animation? animation, Hitboxes? boxes) =>
        FightingCharacter.TimingOf(move, animation, boxes, Game.Time.StepRate, animation is null ? null : ClipOf(animation, move.ClipName));

    // The clip by the name its animation lists it under, or null; by index, as every step asks.
    private static string? ClipOf(Animation animation, string name)
    {
        var clips = animation.ClipList;
        for (var i = 0; i < clips.Count; i++)
        {
            if (string.Equals(clips[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return clips[i].Name;
        }

        return null;
    }

    /// <summary>
    /// True if <paramref name="entity"/>'s hitboxes can hit now: it isn't a fighter, or it's a fighter doing a move's active
    /// part, or free (a clip with hitboxes it plays by itself).
    /// </summary>
    internal static bool CanStrike(Entity entity) =>
        entity.GetComponent<FightingCharacter>() is not { } f || f.Current is null || f.Phase == MovePhase.Active;

    // ----- Walking, jumping, crouching -----

    private void Move(FightingCharacter f, int direction, Body? body, Animation? animation, bool grounded, float facing)
    {
        f.Phase = MovePhase.None;
        if (body is null)
            return;
        if (!grounded)
        {
            f.Crouching = false;
            PlayFirst(animation, "Jump", "Idle");
            return;
        }

        var v = body.Velocity;
        if (direction >= 7 && f.JumpHeight > 0)
        {
            // Up jumps: straight, forward or back.
            var gravity = -Game.Physics.Gravity.Y * body.GravityScale;
            var up = gravity > 0 ? MathF.Sqrt(2 * gravity * f.JumpHeight) : f.JumpHeight * 4;
            var sideways = direction == 9 ? 1 : direction == 7 ? -1 : 0;
            body.Velocity = new Vector2(sideways * f.JumpSpeed * facing, up);
            f.Crouching = false;
            PlayFirst(animation, "Jump", "Idle");
            return;
        }

        if (direction <= 3)
        {
            f.Crouching = true;
            f.Blocking = f.HoldBackToBlock && direction == 1;
            body.Velocity = new Vector2(Toward(v.X, 0, f.Friction), v.Y);
            PlayFirst(animation, "Crouch", "Idle");
            return;
        }

        f.Crouching = false;
        f.Blocking = f.HoldBackToBlock && direction == 4;
        if (direction is 4 or 6)
        {
            var speed = direction == 6 ? f.WalkSpeed : -f.BackWalkSpeed;
            body.Velocity = new Vector2(speed * facing, v.Y);
            if (direction == 6)
                PlayFirst(animation, "Walk", "Run", "Idle");
            else
                PlayFirst(animation, "WalkBack", "Walk", "Run", "Idle");
            return;
        }

        body.Velocity = new Vector2(Toward(v.X, 0, f.Friction), v.Y);
        PlayFirst(animation, "Idle");
    }

    // On the ground it slows to a stop (after a knockback or a push); in the air it flies on.
    private void Slow(FightingCharacter f, Body? body, bool grounded)
    {
        if (body is null || !grounded)
            return;
        var v = body.Velocity;
        body.Velocity = new Vector2(Toward(v.X, 0, f.Friction), v.Y);
    }

    private float Toward(float value, float target, float rate)
    {
        var step = Math.Max(0, rate) * Game.Time.Delta;
        return rate <= 0 ? target : value > target ? Math.Max(target, value - step) : Math.Min(target, value + step);
    }

    // The first of the clips it has, played (carrying on if it's already playing).
    private static void PlayFirst(Animation? animation, string a, string? b = null, string? c = null, string? d = null)
    {
        if (animation is null)
            return;
        var clip = ClipOf(animation, a) ?? (b is null ? null : ClipOf(animation, b)) ?? (c is null ? null : ClipOf(animation, c)) ?? (d is null ? null : ClipOf(animation, d));
        if (clip is null)
            return;
        if (!string.Equals(animation.Clip, clip, StringComparison.Ordinal))
        {
            animation.Clip = clip;
            animation.Time = 0;
        }

        animation.Playing = true;
    }

    // The stick, the arrows or the D-pad as a direction each way: -1, 0 or 1 across (right is 1) and up (up is 1).
    private static (int X, int Y) Stick(IInput input)
    {
        var (h, v) = (input.Horizontal, input.Vertical);
        return (h > StickThreshold ? 1 : h < -StickThreshold ? -1 : 0, v > StickThreshold ? 1 : v < -StickThreshold ? -1 : 0);
    }

    // ----- The computer -----

    // What a fighter the computer plays holds and does this step: as a dummy, stands, crouches or jumps; fighting, guards
    // the attack coming (chosen once a move), follows a hit with a special it cancels into, closes in, attacks with a move
    // in range, backs off or waits, choosing again every so often by its seed.
    private static void Think(FightingCharacter f, ComputerFighter cpu, Entity? opponent, IReadOnlyList<FighterMove> moves, bool grounded, bool free)
    {
        if (f.Held)
        {
            (cpu.HoldX, cpu.HoldY, cpu.Guarding) = (0, 0, false);
            return;
        }

        switch (cpu.Behavior)
        {
            case ComputerBehavior.Stand:
                (cpu.HoldX, cpu.HoldY) = (0, 0);
                return;
            case ComputerBehavior.Crouch:
                (cpu.HoldX, cpu.HoldY) = (0, -1);
                return;
            case ComputerBehavior.Jump:
                (cpu.HoldX, cpu.HoldY) = (0, grounded && free ? 1 : 0);
                return;
        }

        if (opponent is null || opponent.GetComponent<Health>() is { IsDead: true })
        {
            (cpu.HoldX, cpu.HoldY, cpu.Guarding) = (0, 0, false);
            return;
        }

        var dx = opponent.Transform.WorldPosition.X - f.Entity!.Transform.WorldPosition.X;
        var toward = dx >= 0 ? 1 : -1;
        var distance = MathF.Abs(dx);
        var (reaction, guard, attack) = cpu.Difficulty switch
        {
            ComputerDifficulty.Easy => (28, 0.25f, 0.4f),
            ComputerDifficulty.Hard => (8, 0.85f, 0.75f),
            _ => (16, 0.55f, 0.55f),
        };

        // Their attack coming: guard it or not, chosen once for each move of theirs.
        var foe = opponent.GetComponent<FightingCharacter>();
        if (foe is { Current: { } theirs } && foe.Phase is MovePhase.Startup or MovePhase.Active && distance < cpu.Range + 2.5f)
        {
            if (!string.Equals(cpu.SawMove, theirs.Name, StringComparison.Ordinal))
            {
                cpu.SawMove = theirs.Name;
                cpu.Guarding = free && grounded && cpu.Next() < guard;
            }

            if (cpu.Guarding)
            {
                (cpu.HoldX, cpu.HoldY) = (-toward, theirs.Hits == MoveHeight.Low ? -1 : 0);
                return;
            }
        }
        else if (cpu.SawMove is not null || cpu.Guarding)
        {
            (cpu.SawMove, cpu.Guarding) = (null, false);
        }

        // Its own move has hit: perhaps a special it cancels into, once.
        if (f.Current is { } mine)
        {
            (cpu.HoldX, cpu.HoldY) = (0, 0);
            if (f.Connected && f.Requested is null && cpu.Wait >= 0 && mine.CancelList.Count > 0)
            {
                cpu.Wait = -1;
                if (cpu.Next() < attack && Pick(moves, cpu, air: false, cancelFrom: mine) is { } follow)
                    f.Perform(follow.Name);
            }

            return;
        }

        if (!free)
        {
            (cpu.HoldX, cpu.HoldY) = (0, 0);
            return;
        }

        // In the air: an air move once it's near.
        if (!grounded)
        {
            cpu.HoldY = 0;
            if (cpu.Wait >= 0 && distance < cpu.Range + 0.8f)
            {
                cpu.Wait = -1;
                if (Pick(moves, cpu, air: true, cancelFrom: null) is { } air)
                    f.Perform(air.Name);
            }

            return;
        }

        if (cpu.Wait < 0)
            cpu.Wait = 0;
        if (cpu.Wait > 0)
        {
            cpu.Wait--;
            // A jump is a single press of up.
            if (cpu.HoldY > 0)
                cpu.HoldY = 0;
            return;
        }

        var roll = cpu.Next();
        if (distance > cpu.Range)
        {
            // Out of reach: walk in, now and then jumping in.
            if (roll < attack * 0.15f)
                (cpu.HoldX, cpu.HoldY, cpu.Wait) = (toward, 1, 4);
            else
                (cpu.HoldX, cpu.HoldY, cpu.Wait) = (toward, 0, reaction / 2);
        }
        else if (roll < attack && Pick(moves, cpu, air: false, cancelFrom: null) is { } move)
        {
            f.Perform(move.Name);
            (cpu.HoldX, cpu.HoldY, cpu.Wait) = (0, 0, reaction);
        }
        else if (roll < attack + 0.2f)
        {
            (cpu.HoldX, cpu.HoldY, cpu.Wait) = (-toward, 0, reaction / 2);
        }
        else
        {
            (cpu.HoldX, cpu.HoldY, cpu.Wait) = (0, 0, reaction / 2);
        }

        cpu.Wait += (int)(cpu.Next() * (reaction / 2 + 1));
    }

    // One of its moves in the air or on the ground (and one the move it's doing cancels into, if any) that it uses, picked
    // by its seed; null if none.
    private static FighterMove? Pick(IReadOnlyList<FighterMove> moves, ComputerFighter cpu, bool air, FighterMove? cancelFrom)
    {
        bool Fits(FighterMove move) => move.Input.Air == air && (cancelFrom is null || cancelFrom.CancelsInto(move)) && Uses(cpu, move);

        var count = 0;
        for (var i = 0; i < moves.Count; i++)
        {
            if (Fits(moves[i]))
                count++;
        }

        if (count == 0)
            return null;
        var pick = Math.Min(count - 1, (int)(cpu.Next() * count));
        for (var i = 0; i < moves.Count; i++)
        {
            if (!Fits(moves[i]))
                continue;
            if (pick-- == 0)
                return moves[i];
        }

        return null;
    }

    // Whether the computer uses a move: every one, or those its Use Moves names.
    private static bool Uses(ComputerFighter cpu, FighterMove move)
    {
        var names = cpu.UseMoves;
        if (string.IsNullOrWhiteSpace(names))
            return true;
        foreach (var range in names.AsSpan().Split(','))
        {
            if (names.AsSpan(range).Trim().Equals(move.Name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    // ----- Facing and opponents -----

    private static void Face(Entity entity, Entity opponent)
    {
        var dx = opponent.Transform.WorldPosition.X - entity.Transform.WorldPosition.X;
        if (MathF.Abs(dx) < TurnDeadZone || entity.GetComponent<Sprite>() is not { } sprite)
            return;
        var left = dx < 0;
        // A mirrored transform faces the other way already.
        if (entity.Transform.WorldMatrix.M11 < 0)
            left = !left;
        if (sprite.FlipX != left)
            sprite.FlipX = left;
    }

    // Its named opponent, or the nearest other fighter not on its team.
    private Entity? OpponentOf(FightingCharacter f, ComponentList<FightingCharacter> fighters)
    {
        var entity = f.Entity!;
        if (!string.IsNullOrWhiteSpace(f.Opponent))
            return Game.Scene.FindNamed(f.Opponent.Trim(), activeOnly: true);
        var team = entity.GetComponent<Hitboxes>()?.Team;
        var at = entity.Transform.WorldPosition;
        Entity? nearest = null;
        var best = float.MaxValue;
        for (var i = 0; i < fighters.Count; i++)
        {
            var other = fighters[i].Entity!;
            if (other == entity)
                continue;
            var theirs = other.GetComponent<Hitboxes>()?.Team;
            if (!string.IsNullOrWhiteSpace(team) && string.Equals(team.Trim(), theirs?.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;
            var distance = Vector3.DistanceSquared(other.Transform.WorldPosition, at);
            if (distance < best)
                (nearest, best) = (other, distance);
        }

        return nearest;
    }

    // Whether the one its combo is on is still in hitstun.
    private bool StillStunned(FightingCharacter f)
    {
        if (f.ComboOn is not { Length: > 0 } id || !Guid.TryParse(id, out var guid))
            return false;
        return Game.Scene.Find(guid)?.GetComponent<Hitboxes>() is { Hitstun: > 0 };
    }

    // ----- Blocking and combos, as HitboxesSystem lands a hit -----

    /// <summary>
    /// True if <paramref name="defender"/> blocks a hit of <paramref name="height"/> from <paramref name="attacker"/>: on the
    /// ground, not in hitstun or a move, and holding away from the attacker (down-back for a low, not down for a high), or in
    /// blockstun already, or blocking by itself (<see cref="FightingCharacter.AutoBlock"/>).
    /// </summary>
    internal static bool Blocks(FightingCharacter defender, Entity attacker, MoveHeight height)
    {
        var entity = defender.Entity!;
        if (entity.GetComponent<Hitboxes>() is { Hitstun: > 0 } || defender.Current is not null)
            return false;
        if (entity.GetComponent<Body>() is not null && !defender.OnGround)
            return false;

        bool crouching;
        if (defender.Blockstun > 0)
            crouching = defender.Crouching;
        else if (defender.HoldBackToBlock && !defender.Held && (defender.Controllable || entity.GetComponent<ComputerFighter>() is not null) &&
                 HoldsAway(defender, attacker))
            crouching = defender.HeldDown;
        else if (defender.AutoBlock != AutoBlock.Never)
            crouching = defender.AutoBlock switch
            {
                AutoBlock.Crouching => true,
                AutoBlock.Standing => false,
                _ => height == MoveHeight.Low,
            };
        else
            return false;

        if (height == MoveHeight.Low && !crouching || height == MoveHeight.High && crouching)
            return false;
        defender.Crouching = crouching;
        return true;
    }

    // Holding the stick away from the attacker (from wherever it is: a cross-up is blocked the other way).
    private static bool HoldsAway(FightingCharacter defender, Entity attacker)
    {
        if (defender.HeldX == 0)
            return false;
        var dx = attacker.Transform.WorldPosition.X - defender.Entity!.Transform.WorldPosition.X;
        if (MathF.Abs(dx) < TurnDeadZone)
            return defender.HeldX == -(int)HitboxesSystem.FacingOf(defender.Entity!);
        return defender.HeldX == -MathF.Sign(dx);
    }

    /// <summary>A hit of <paramref name="attacker"/>'s landed on <paramref name="target"/>, which was in hitstun or not: the combo counts.</summary>
    internal static void CountCombo(FightingCharacter attacker, Entity target, bool wasStunned, float damage)
    {
        var id = target.Id.ToString("N");
        if (wasStunned && attacker.Combo > 0 && attacker.ComboOn == id)
            (attacker.Combo, attacker.ComboDamage) = (attacker.Combo + 1, attacker.ComboDamage + damage);
        else
            (attacker.Combo, attacker.ComboDamage, attacker.ComboOn) = (1, damage, id);
        attacker.BestCombo = Math.Max(attacker.BestCombo, attacker.Combo);
    }
}
