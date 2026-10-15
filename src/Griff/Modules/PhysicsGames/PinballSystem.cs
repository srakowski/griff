using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.PhysicsGames;

/// <summary>
/// Runs pinball tables with no code. Before each physics step: the tilt (nudges pushing the balls, tilting when they come too
/// often), the flippers (swung up and down on their hinges' motors by the buttons, the mouse, a finger or a wire, dropped while
/// tilted) and drop targets lying down out of the way. After it, from what the step found touching: bumpers and kickers
/// knocking balls away, targets hit, balls rolling through lanes, the plunger pulled and shot, and balls draining and served
/// again. The game's runner (offline, or the host) does it all, hearing the players who play each table (a player's own
/// table their keys; one nobody owns, any player's whose side it is); the table's state is kept with the scene, so rewinds
/// and network games see the same.
/// </summary>
internal sealed partial class PinballSystem : GameSystem
{
    // How long "DANGER" shows after a warning, in seconds.
    private const float WarningShows = 1.2f;

    // How soon a saved ball is shot again by itself once it's back on the plunger, in seconds.
    private const float SavedLaunchDelay = 0.6f;

    // A stick pulled less than this isn't pulling the plunger; one that springs back more than this in a step has let it go.
    private const float StickDeadZone = 0.08f;
    private const float StickLetGo = 0.2f;

    public override string Name => "Pinball";

    // The step's tilts and what each covers.
    private readonly List<(Tilt Tilt, Scope Scope)> _tilts = new(4);

    // What touched a piece this step, the controls that work one, and a set of lanes being changed: with room to start
    // with, so the first ball to touch something, the first flip and the first player don't grow them mid-game.
    private readonly List<Entity> _found = new(8);
    private readonly List<IInput> _inputs = new(8);
    private readonly List<Lane> _set = new(8);

    private Scene Scene => Game.Scene;

    // ----- Before physics -----

    internal void RunControls()
    {
        var seconds = Game.Time.Delta;
        if (seconds <= 0)
            return;
        GatherTilts();
        foreach (var (tilt, scope) in _tilts)
            RunTilt(tilt, scope, seconds);
        foreach (var flipper in Game.Showing<Flipper>())
            RunFlipper(flipper, seconds);

        // Drop targets lying down are out of the way for the step.
        var step = Scene.Step;
        foreach (var target in Game.Showing<Target>())
        {
            if (!target.Down)
                continue;
            var components = target.Entity!.ComponentList;
            for (var i = 0; i < components.Count; i++)
            {
                if (components[i] is Collider collider)
                    collider.HeldOutIn = step;
            }
        }
    }

    private void GatherTilts()
    {
        _tilts.Clear();
        foreach (var tilt in Game.Showing<Tilt>())
        {
            var entity = tilt.Entity!;
            if (Game.Runs(entity))
                _tilts.Add((tilt, Scope.Of(entity, tilt.Within)));
        }
    }

    // Whether a tilt covering `entity` has tilted: the table is dead.
    private bool Dead(Entity entity)
    {
        foreach (var (tilt, scope) in _tilts)
        {
            if (tilt.Tilted && scope.Covers(entity))
                return true;
        }

        return false;
    }

    private void RunTilt(Tilt tilt, Scope scope, float seconds)
    {
        var entity = tilt.Entity!;
        if (entity.IsPaused)
            return;
        if (tilt.ResetRequested)
        {
            tilt.ResetRequested = false;
            PutRight(tilt);
        }

        tilt.Danger = Math.Max(0, tilt.Danger - seconds / Math.Max(0.01f, tilt.Settle));
        tilt.WarningFor = Math.Max(0, tilt.WarningFor - seconds);
        if (tilt.TiltRequested)
        {
            tilt.TiltRequested = false;
            if (!tilt.Tilted)
                TiltOver(tilt);
        }

        IInput? by = null;
        // Not with a press that closed a menu (B going back out of the pause menu) the moment the game goes on.
        var nudged = tilt.NudgeRequested || (tilt.Controllable && !Game.PausedThisFrame && Pressed(entity, tilt.NudgeAction, out by));
        tilt.NudgeRequested = false;
        if (nudged)
            Nudge(tilt, scope, by?.Horizontal ?? 0);
    }

    // A nudge: every ball the tilt covers is pushed, and it's a nudge nearer tilting.
    private void Nudge(Tilt tilt, Scope scope, float sideways)
    {
        var entity = tilt.Entity!;
        tilt.Nudges++;
        var way = tilt.NudgeDirection.LengthSquared() > 1e-8f ? Vector2.Normalize(tilt.NudgeDirection) : Vector2.UnitY;
        if (MathF.Abs(sideways) > 0.3f)
            way = Vector2.Normalize(way + new Vector2(MathF.Sign(sideways), 0));
        var push = way * Math.Max(0, tilt.NudgeStrength);
        foreach (var body in Game.Moving<Body>())
        {
            var ball = body.Entity!;
            if (!IsBall(ball, body) || !scope.Covers(ball))
                continue;
            body.Velocity += push;
            if (body.Asleep)
                body.Asleep = false;
        }

        if (!string.IsNullOrWhiteSpace(tilt.NudgeSound))
            Game.PlaySoundAt(tilt.NudgeSound.Trim(), At(entity));
        if (tilt.Shake > 0)
            Game.ShakeCamera(tilt.Shake, 0.15f);
        if (tilt.Tilted)
            return;

        tilt.Danger += 1;
        if (tilt.Danger > tilt.Warnings + 1 + 1e-3f)
        {
            TiltOver(tilt);
        }
        else if (tilt.Danger > 1 + 1e-3f)
        {
            tilt.WarningFor = WarningShows;
            if (!string.IsNullOrWhiteSpace(tilt.WarningSound))
                Game.PlaySoundAt(tilt.WarningSound.Trim(), At(entity));
            if (!string.IsNullOrWhiteSpace(tilt.WarningMessage))
                Game.Send(tilt.WarningMessage.Trim(), entity, from: entity);
            tilt.RaiseWarned();
        }
    }

    private void TiltOver(Tilt tilt)
    {
        var entity = tilt.Entity!;
        (tilt.Tilted, tilt.WarningFor) = (true, 0);
        if (!string.IsNullOrWhiteSpace(tilt.TiltSound))
            Game.PlaySoundAt(tilt.TiltSound.Trim(), At(entity));
        if (!string.IsNullOrWhiteSpace(tilt.TiltMessage))
            Game.Send(tilt.TiltMessage.Trim(), entity, from: entity);
        tilt.RaiseTilted();
    }

    private static void PutRight(Tilt tilt) => (tilt.Tilted, tilt.Danger, tilt.WarningFor) = (false, 0, 0);

    // A flipper: up while it's pressed, powered or flipped and not tilted, its hinge's motor swinging it up or down.
    private void RunFlipper(Flipper flipper, float seconds)
    {
        var entity = flipper.Entity!;
        if (!Game.Runs(entity) || entity.IsPaused || entity.GetComponent<HingeJoint>() is not { } hinge)
            return;

        // A flipper swings: its body rotates, and a hinge without limits gets its side's (resting as it starts, 55 up).
        if (entity.GetComponent<Body>() is { Rotates: false } body)
            body.Rotates = true;
        if (!hinge.Limit)
            (hinge.Limit, hinge.LowerAngle, hinge.UpperAngle) = flipper.Side == FlipperSide.Left ? (true, 0f, 55f) : (true, -55f, 0f);

        var pressed = flipper.Controllable && (Held(entity, flipper.InputAction) || (flipper.Touch && Touched(flipper)));
        if (flipper.Pressed != pressed)
            flipper.Pressed = pressed;
        if (flipper.FlipFor > 0)
            flipper.FlipFor = Math.Max(0, flipper.FlipFor - seconds);
        var up = !Dead(entity) && (pressed || flipper.Raised || flipper.FlipFor > 0);
        if (up && !flipper.Up)
        {
            flipper.Flips++;
            if (!string.IsNullOrWhiteSpace(flipper.Sound))
                Game.PlaySoundAt(flipper.Sound.Trim(), At(entity));
            if (!string.IsNullOrWhiteSpace(flipper.Message))
                Game.Send(flipper.Message.Trim(), entity, from: entity);
            flipper.RaiseFlipped();
            ChangeLanes(flipper);
        }

        if (flipper.Up != up)
            flipper.Up = up;

        // Its hinge's motor, always on: up or down as fast as its Speed, as hard as its Strength, slowing to stop on the
        // hinge's limit within the step it gets there (rather than slamming into it and sinking past it).
        var way = (up ? 1 : -1) * flipper.UpWay;
        var stop = way > 0 ? Math.Max(hinge.LowerAngle, hinge.UpperAngle) : Math.Min(hinge.LowerAngle, hinge.UpperAngle);
        var left = (stop - hinge.Angle) * way;
        var speed = way * Math.Clamp(left / seconds, 0, Math.Abs(flipper.Speed));
        if (!hinge.Motor)
            hinge.Motor = true;
        if (hinge.MotorSpeed != speed)
            hinge.MotorSpeed = speed;
        if (hinge.MotorStrength != flipper.Strength)
            hinge.MotorStrength = flipper.Strength;
    }

    // The mouse or a finger held down on a flipper's side of the flippers of its table (anywhere, if they're all on one
    // side), but not on a plunger. Flippers at the top of the scene are a table together.
    private bool Touched(Flipper flipper)
    {
        var entity = flipper.Entity!;
        Controls(entity);
        if (_inputs.Count == 0)
            return false;
        var table = TableOf(entity);
        var (sum, count, left, right) = (0f, 0, false, false);
        foreach (var other in Game.Showing<Flipper>())
        {
            var otherEntity = other.Entity!;
            if (TableOf(otherEntity) != table)
                continue;
            sum += At(otherEntity).X;
            count++;
            (left, right) = (left || other.Side == FlipperSide.Left, right || other.Side == FlipperSide.Right);
        }

        var middle = count > 0 ? sum / count : At(entity).X;
        var onItsSide = flipper.Side == FlipperSide.Left;
        for (var i = 0; i < _inputs.Count; i++)
        {
            var input = _inputs[i];

            // Every finger on a touch screen, so one on each side holds both flippers up (the first is the mouse as well).
            var touches = input.Touches;
            for (var t = 0; t < touches.Count; t++)
            {
                var touch = touches[t];
                if (!touch.Released && !OnPlunger(touch.World) && (!(left && right) || (touch.World.X < middle) == onItsSide))
                    return true;
            }

            if (!input.IsDown(MouseButton.Left) || !input.IsMouseInView || (ReferenceEquals(input, Game.Input) && Game.MouseOnUi))
                continue;
            var mouse = input.MouseWorld;
            if (OnPlunger(mouse))
                continue;
            if (!(left && right) || (mouse.X < middle) == onItsSide)
                return true;
        }

        return false;
    }

    private bool OnPlunger(Vector2 point)
    {
        foreach (var plunger in Game.Showing<Plunger>())
        {
            if (plunger.Controllable && Vector2.Distance(point, plunger.Tip) <= plunger.GrabRadius)
                return true;
        }

        return false;
    }

    // ----- After physics -----

    internal void RunHits()
    {
        var seconds = Game.Time.Delta;
        if (seconds <= 0)
            return;
        GatherTilts();
        foreach (var bumper in Game.Showing<Bumper>())
            RunBumper(bumper, seconds);
        RunTargets(seconds);
        RunLanes(seconds);
        foreach (var plunger in Game.Showing<Plunger>())
            RunPlunger(plunger, seconds);
        foreach (var drain in Game.RunHere<Drain>())
            RunDrain(drain, seconds);
    }

    // A bumper: whatever touches it (its kicking side, for a kicker) knocked away at its Strength, lit, popped and scored.
    private void RunBumper(Bumper bumper, float seconds)
    {
        var entity = bumper.Entity!;
        if (!Game.Runs(entity) || entity.IsPaused)
            return;
        if (bumper.LitFor > 0)
            bumper.LitFor = Math.Max(0, bumper.LitFor - seconds);
        if (bumper.Resting > 0)
            bumper.Resting = Math.Max(0, bumper.Resting - seconds);
        if (bumper.FlashRequested)
        {
            bumper.FlashRequested = false;
            Light(bumper);
        }

        if (!bumper.On || bumper.Resting > 0 || Dead(entity) || Touchers(entity, bumper.Layers) == 0)
            return;
        var way = bumper.WorldDirection;
        var middle = Middle(entity);
        var knocked = false;
        foreach (var ball in _found)
        {
            var body = ball.GetComponent<Body>()!;
            var from = body.CenterOfMass - middle;
            Vector2 away;
            if (way == Vector2.Zero)
                away = from.LengthSquared() > 1e-8f ? Vector2.Normalize(from) : Vector2.UnitY;
            else if (Vector2.Dot(from, way) > 0)
                away = way;
            else
                continue;

            // Sent off that way at its Strength, keeping how it was going across it.
            var velocity = body.Velocity;
            body.Velocity = velocity - Vector2.Dot(velocity, away) * away + away * Math.Max(0, bumper.Strength);
            if (body.Asleep)
                body.Asleep = false;
            knocked = true;
            bumper.Hits++;
            if (!string.IsNullOrWhiteSpace(bumper.Message))
                Game.Send(bumper.Message.Trim(), bumper.PointsData(entity), from: entity);
            bumper.RaiseKnocked(ball);
        }

        if (!knocked)
            return;
        bumper.Resting = Math.Max(0, bumper.Recover);
        Light(bumper);
    }

    // A bumper lights up: its Lit output on, its sprites flashed, its sound.
    private void Light(Bumper bumper)
    {
        var entity = bumper.Entity!;
        bumper.LitFor = Math.Max(0, bumper.FlashTime);
        if (bumper.FlashColor.A > 0 && bumper.FlashTime > 0)
        {
            var components = entity.ComponentList;
            for (var i = 0; i < components.Count; i++)
            {
                if (components[i] is Sprite sprite)
                    sprite.Flash(bumper.FlashColor, bumper.FlashTime);
            }
        }

        if (!string.IsNullOrWhiteSpace(bumper.Sound))
            Game.PlaySoundAt(bumper.Sound.Trim(), Middle(entity));
    }

    // Targets: hits harder than their Min Hit from the step's knocks; their sets completing and coming back up.
    private void RunTargets(float seconds)
    {
        var targets = Game.Showing<Target>();
        if (targets.Count == 0)
            return;
        foreach (var target in targets)
        {
            var entity = target.Entity!;
            if (!Game.Runs(entity) || entity.IsPaused)
                continue;
            if (target.ResetRequested)
            {
                target.ResetRequested = false;
                (target.Lit, target.Down, target.ResetIn) = (false, false, 0);
            }

            if (target.ResetIn > 0)
            {
                target.ResetIn = Math.Max(0, target.ResetIn - seconds);
                if (target.ResetIn <= 0)
                    (target.Lit, target.Down) = (false, false);
            }

            if (target.LightRequested)
            {
                target.LightRequested = false;
                if (!target.Lit)
                {
                    target.Lit = true;
                    CompleteTargets(target);
                }
            }
        }

        var impacts = Game.Physics.ImpactList;
        for (var i = 0; i < impacts.Count; i++)
        {
            var impact = impacts[i];
            var entity = impact.Struck;
            if (entity.Scene != Scene || entity.GetComponent<Target>() is not { Down: false } target || !entity.IsActive || entity.IsPaused ||
                !Game.Runs(entity) || impact.Strength < target.MinHit || Dead(entity) || Ball(impact.By, target.Layers) is not { } ball)
                continue;
            target.Hits++;
            var lit = target.Lit;
            target.Lit = true;
            if (target.Drops)
                target.Down = true;
            if (!string.IsNullOrWhiteSpace(target.Sound))
                Game.PlaySoundAt(target.Sound.Trim(), impact.Point);
            if (!string.IsNullOrWhiteSpace(target.Message))
                Game.Send(target.Message.Trim(), target.PointsData(entity), from: entity);
            target.RaiseHit(ball);
            if (!lit)
                CompleteTargets(target);
        }
    }

    // Once every target of a set (inside the same entity) is lit: the set scores and comes back up a moment later.
    private void CompleteTargets(Target target)
    {
        var siblings = Siblings(target.Entity!);
        for (var i = 0; i < siblings.Count; i++)
        {
            if (siblings[i].GetComponent<Target>() is { } other && siblings[i].IsActive && (!other.Lit || other.ResetIn > 0))
                return;
        }

        var entity = target.Entity!;
        if (!string.IsNullOrWhiteSpace(target.CompleteMessage))
            Game.Send(target.CompleteMessage.Trim(), target.CompletePoints, from: entity);
        if (!(target.ResetAfter > 0))
            return;
        for (var i = 0; i < siblings.Count; i++)
        {
            if (siblings[i].GetComponent<Target>() is { } other && siblings[i].IsActive)
                other.ResetIn = target.ResetAfter;
        }
    }

    // Lanes: a ball rolling into one lights it and scores; their sets completing and going out.
    private void RunLanes(float seconds)
    {
        foreach (var lane in Game.Showing<Lane>())
        {
            var entity = lane.Entity!;
            if (!Game.Runs(entity) || entity.IsPaused)
                continue;
            if (lane.ResetRequested)
            {
                lane.ResetRequested = false;
                (lane.Lit, lane.ResetIn) = (false, 0);
            }

            if (lane.ResetIn > 0)
            {
                lane.ResetIn = Math.Max(0, lane.ResetIn - seconds);
                if (lane.ResetIn <= 0)
                    lane.Lit = false;
            }

            if (lane.LightRequested)
            {
                lane.LightRequested = false;
                if (!lane.Lit)
                {
                    lane.Lit = true;
                    CompleteLanes(lane);
                }
            }

            var occupied = Touchers(entity, lane.Layers) > 0;
            if (occupied && !lane.Occupied && !Dead(entity))
            {
                var ball = _found[0];
                lane.Passes++;
                var lit = lane.Lit;
                lane.Lit = true;
                if (!string.IsNullOrWhiteSpace(lane.Sound))
                    Game.PlaySoundAt(lane.Sound.Trim(), Middle(entity));
                if (!string.IsNullOrWhiteSpace(lane.Message))
                    Game.Send(lane.Message.Trim(), lane.PointsData(entity), from: entity);
                lane.RaisePassed(ball);
                if (!lit)
                    CompleteLanes(lane);
            }

            if (lane.Occupied != occupied)
                lane.Occupied = occupied;
        }
    }

    private void CompleteLanes(Lane lane)
    {
        var siblings = Siblings(lane.Entity!);
        for (var i = 0; i < siblings.Count; i++)
        {
            if (siblings[i].GetComponent<Lane>() is { } other && siblings[i].IsActive && (!other.Lit || other.ResetIn > 0))
                return;
        }

        var entity = lane.Entity!;
        if (!string.IsNullOrWhiteSpace(lane.CompleteMessage))
            Game.Send(lane.CompleteMessage.Trim(), lane.CompletePoints, from: entity);
        if (!(lane.ResetAfter > 0))
            return;
        for (var i = 0; i < siblings.Count; i++)
        {
            if (siblings[i].GetComponent<Lane>() is { } other && siblings[i].IsActive)
                other.ResetIn = lane.ResetAfter;
        }
    }

    // A flipper swinging up moves the lit lanes of its table along (those that change with flippers): the left one to the
    // left, the right one to the right, round from one end to the other. One at the top of the scene moves every set.
    private void ChangeLanes(Flipper flipper)
    {
        var table = TableOf(flipper.Entity!);
        foreach (var lane in Game.Showing<Lane>())
        {
            var entity = lane.Entity!;
            if (!lane.FlipperChanges || (table is not null && TableOf(entity) != table) || !FirstOfSet(lane))
                continue;

            // The set's lanes, left to right; moved along unless they're all lit or none are (or they're about to go out).
            _set.Clear();
            var siblings = Siblings(entity);
            var lit = 0;
            for (var i = 0; i < siblings.Count; i++)
            {
                if (siblings[i].GetComponent<Lane>() is not { FlipperChanges: true } member || !siblings[i].IsActive)
                    continue;
                if (member.ResetIn > 0)
                {
                    lit = -1;
                    break;
                }

                var at = At(siblings[i]).X;
                var index = _set.Count;
                while (index > 0 && At(_set[index - 1].Entity!).X > at)
                    index--;
                _set.Insert(index, member);
                lit += member.Lit ? 1 : 0;
            }

            if (lit <= 0 || lit >= _set.Count)
                continue;
            var count = _set.Count;
            if (flipper.Side == FlipperSide.Left)
            {
                var first = _set[0].Lit;
                for (var i = 0; i < count - 1; i++)
                    _set[i].Lit = _set[i + 1].Lit;
                _set[count - 1].Lit = first;
            }
            else
            {
                var last = _set[count - 1].Lit;
                for (var i = count - 1; i > 0; i--)
                    _set[i].Lit = _set[i - 1].Lit;
                _set[0].Lit = last;
            }
        }
    }

    // Whether a lane is the first of its set among its siblings (so a set is moved along once).
    private bool FirstOfSet(Lane lane)
    {
        var entity = lane.Entity!;
        var siblings = Siblings(entity);
        for (var i = 0; i < siblings.Count; i++)
        {
            if (siblings[i] == entity)
                return true;
            if (siblings[i].GetComponent<Lane>() is { FlipperChanges: true } && siblings[i].IsActive)
                return false;
        }

        return true;
    }
}
