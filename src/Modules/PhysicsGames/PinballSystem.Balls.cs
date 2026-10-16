using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.PhysicsGames;

// The plunger and the drain: balls shot, lost, saved and served again; and what the pinball pieces share: who plays a table,
// what's a ball, and what touched a piece.
internal sealed partial class PinballSystem
{
    // A plunger: pulled back while its button (the mouse, a finger) is held or as far as a stick is pushed down, and shot as
    // it's let go; the balls resting on its tip are what it shoots.
    private void RunPlunger(Plunger plunger, float seconds)
    {
        var entity = plunger.Entity!;
        if (!Game.Runs(entity) || entity.IsPaused)
            return;
        var way = plunger.WorldDirection;
        var tip = plunger.Tip;
        var resting = Touchers(entity, plunger.Layers);
        for (var i = resting - 1; i >= 0; i--)
        {
            if (Vector2.Dot(_found[i].GetComponent<Body>()!.CenterOfMass - tip, way) <= 0)
                _found.RemoveAt(i);
        }

        var loaded = _found.Count > 0;
        if (plunger.Loaded != loaded)
            plunger.Loaded = loaded;

        if (plunger.LaunchRequested)
        {
            plunger.LaunchRequested = false;
            Shoot(plunger, 1);
            return;
        }

        if (plunger.LaunchIn > 0)
        {
            plunger.LaunchIn = Math.Max(0, plunger.LaunchIn - seconds);
            if (plunger.LaunchIn <= 0)
            {
                Shoot(plunger, 1);
                return;
            }
        }

        if (!plunger.Controllable)
        {
            if (plunger.Pulling)
                (plunger.Pulling, plunger.Pull, plunger.Grabbed) = (false, 0, false);
            return;
        }

        // A stick pushed down pulls it as far as it's pushed; springing back, it shoots at the pull it had.
        var stick = plunger.Analog ? Stick(entity) : 0;
        if (stick > StickDeadZone || (plunger.Analog && plunger.StickPeak > 0))
        {
            if (stick < plunger.Pull - StickLetGo)
            {
                plunger.StickPeak = 0;
                Shoot(plunger, plunger.Pull);
                return;
            }

            if (stick > StickDeadZone)
            {
                if (plunger.Pull == 0)
                    PullStarted(plunger);
                (plunger.Pull, plunger.StickPeak) = (stick, Math.Max(plunger.StickPeak, stick));
                return;
            }

            // Let back slowly: no shot.
            (plunger.Pull, plunger.StickPeak) = (0, 0);
            return;
        }

        // The button, or the mouse or a finger held on it: pulled further the longer it's held. A pull starts with a press
        // while the table's being played, so a button still held from a menu (Space or A clicking Resume) doesn't pull it.
        var grabbed = Grab(plunger);
        var held = grabbed || (plunger.Pulling ? Held(entity, plunger.Action) : !Game.PausedThisFrame && Pressed(entity, plunger.Action, out _));
        if (held)
        {
            if (!plunger.Pulling)
            {
                plunger.Pulling = true;
                PullStarted(plunger);
            }

            plunger.Pull = Math.Min(1, plunger.Pull + seconds / Math.Max(0.01f, plunger.PullTime));
            return;
        }

        if (plunger.Pulling)
            Shoot(plunger, plunger.Pull);
    }

    private void PullStarted(Plunger plunger)
    {
        if (!string.IsNullOrWhiteSpace(plunger.PullSound))
            Game.PlaySoundAt(plunger.PullSound.Trim(), plunger.Tip);
    }

    // How far a stick pushes the plunger down: the right stick of those who play it, from 0 to 1.
    private float Stick(Entity entity)
    {
        Controls(entity);
        var most = 0f;
        for (var i = 0; i < _inputs.Count; i++)
            most = Math.Max(most, -_inputs[i].RightStick.Y);
        return Math.Clamp(most, 0, 1);
    }

    // The mouse or a finger pressed on the plunger takes hold of it, until it's let go.
    private bool Grab(Plunger plunger)
    {
        Controls(plunger.Entity!);
        var held = false;
        for (var i = 0; i < _inputs.Count; i++)
        {
            var input = _inputs[i];

            // A finger (of several on a touch screen) pressed on it holds it until that finger's lifted.
            var touches = input.Touches;
            for (var t = 0; t < touches.Count && !held; t++)
            {
                var touch = touches[t];
                if (touch.Released)
                    continue;
                if (plunger.Grabbed && plunger.GrabbedTouch == touch.Id ||
                    !plunger.Grabbed && touch.Pressed && Vector2.Distance(touch.World, plunger.Tip) <= plunger.GrabRadius)
                    (held, plunger.GrabbedTouch) = (true, touch.Id);
            }

            if (held)
                break;
            if (!input.IsDown(MouseButton.Left))
                continue;
            if (plunger.Grabbed && plunger.GrabbedTouch < 0)
            {
                held = true;
                break;
            }

            if (input.WasPressed(MouseButton.Left) && input.IsMouseInView && !(ReferenceEquals(input, Game.Input) && Game.MouseOnUi) &&
                Vector2.Distance(input.MouseWorld, plunger.Tip) <= plunger.GrabRadius)
            {
                (held, plunger.GrabbedTouch) = (true, -1);
                break;
            }
        }

        plunger.Grabbed = held;
        if (!held)
            plunger.GrabbedTouch = -1;
        return held;
    }

    // Lets go: the balls on its tip are shot its way, as fast as the pull says.
    private void Shoot(Plunger plunger, float pull)
    {
        var entity = plunger.Entity!;
        (plunger.Pulling, plunger.Pull, plunger.Grabbed, plunger.StickPeak, plunger.LaunchIn) = (false, 0, false, 0, 0);
        var way = plunger.WorldDirection;
        var speed = plunger.SpeedFor(pull);
        if (!string.IsNullOrWhiteSpace(plunger.LaunchSound))
            Game.PlaySoundAt(plunger.LaunchSound.Trim(), plunger.Tip);
        if (_found.Count == 0)
            return;
        Game.UseAction(entity);
        foreach (var ball in _found)
        {
            var body = ball.GetComponent<Body>()!;
            body.Velocity = way * speed;
            if (body.Asleep)
                body.Asleep = false;
            plunger.Launches++;
            if (!string.IsNullOrWhiteSpace(plunger.LaunchMessage))
                Game.Send(plunger.LaunchMessage.Trim(), ball, from: entity);
            plunger.RaiseLaunched(ball);
        }

        plunger.Loaded = false;

        // A ball shot after it was served starts its drain's ball save.
        foreach (var drain in Game.RunHere<Drain>())
        {
            if (drain.SaveReady && !drain.GameOver && PlungerOf(drain) == plunger)
                (drain.SaveReady, drain.SaveLeft) = (false, Math.Max(drain.SaveLeft, Math.Max(0, drain.BallSave)));
        }
    }

    // A drain: balls falling into it lost (or saved, or just gone while others play on), and the next served on the plunger.
    private void RunDrain(Drain drain, float seconds)
    {
        var entity = drain.Entity!;
        if (!Game.Runs(entity) || entity.IsPaused)
            return;
        if (drain.ExtraRequested > 0)
        {
            if (!drain.GameOver)
                drain.Balls += drain.ExtraRequested;
            drain.ExtraRequested = 0;
        }

        if (drain.SaveRequested)
        {
            drain.SaveRequested = false;
            if (!drain.GameOver)
                drain.SaveLeft = drain.BallSave > 0 ? drain.BallSave : 5;
        }

        if (drain.SaveLeft > 0)
            drain.SaveLeft = Math.Max(0, drain.SaveLeft - seconds);

        if (!string.IsNullOrEmpty(drain.Serving))
        {
            drain.ServeIn = Math.Max(0, drain.ServeIn - seconds);
            if (drain.ServeIn <= 0 && !drain.GameOver)
                Serve(drain);
        }

        if (!entity.IsActive || Touchers(entity, drain.Layers) == 0)
            return;
        for (var i = 0; i < _found.Count; i++)
            Lose(drain, _found[i]);
    }

    // A ball has fallen in: gone if others are still on the table; otherwise given back by a ball save, or lost, the next
    // served (or the game over).
    private void Lose(Drain drain, Entity ball)
    {
        var entity = drain.Entity!;
        foreach (var (tilt, scope) in _tilts)
        {
            if (scope.Covers(entity))
                PutRight(tilt);
        }

        if (!string.IsNullOrWhiteSpace(drain.Sound))
            Game.PlaySoundAt(drain.Sound.Trim(), Middle(entity));
        if (BallsInPlay(drain, ball) > 0)
        {
            Game.Destroy(ball);
            drain.RaiseDrained(ball, false);
            return;
        }

        var saved = drain.SaveLeft > 0 && !drain.GameOver;
        Hold(drain, ball);
        if (saved)
        {
            (drain.SaveLeft, drain.Saved) = (0, true);
            if (!string.IsNullOrWhiteSpace(drain.SaveSound))
                Game.PlaySoundAt(drain.SaveSound.Trim(), Middle(entity));
            if (!string.IsNullOrWhiteSpace(drain.SaveMessage))
                Game.Send(drain.SaveMessage.Trim(), ball, from: entity);
            drain.RaiseDrained(ball, true);
            return;
        }

        if (drain.Ball >= drain.Balls || drain.GameOver)
        {
            drain.GameOver = true;
            (drain.Serving, drain.ServeIn) = (null, 0);
            if (!string.IsNullOrWhiteSpace(drain.DrainMessage))
                Game.Send(drain.DrainMessage.Trim(), 0, from: entity);
            if (!string.IsNullOrWhiteSpace(drain.GameOverMessage))
                Game.Send(drain.GameOverMessage.Trim(), entity, from: entity);
            drain.RaiseDrained(ball, false);
            return;
        }

        drain.Ball++;
        if (!string.IsNullOrWhiteSpace(drain.DrainMessage))
            Game.Send(drain.DrainMessage.Trim(), drain.BallsLeft, from: entity);
        drain.RaiseDrained(ball, false);
    }

    // The ball out of play, waiting to be served.
    private static void Hold(Drain drain, Entity ball)
    {
        if (ball.GetComponent<Body>() is { } body)
            (body.Velocity, body.Spin) = (Vector2.Zero, 0);
        ball.IsEnabled = false;
        // The same ball served again keeps the text of its id it had (no new string each time).
        var id = drain.ServingEntity == ball && drain.ServingKey is { } key ? key : ball.Id.ToString();
        (drain.Serving, drain.ServeIn, drain.Saved) = (id, Math.Max(0, drain.ServeDelay), false);
        (drain.ServingKey, drain.ServingEntity) = (drain.Serving, ball);
    }

    // The ball waiting is put on the plunger's tip (and shot again by itself, if it was saved).
    private void Serve(Drain drain)
    {
        var id = drain.Serving;
        if (!ReferenceEquals(id, drain.ServingKey) || drain.ServingEntity?.Scene != Scene)
            (drain.ServingKey, drain.ServingEntity) = (id, Guid.TryParse(id, out var guid) ? Scene.Find(guid) : null);
        var ball = drain.ServingEntity;
        (drain.Serving, drain.ServeIn) = (null, 0);
        if (ball is null)
            return;
        if (PlungerOf(drain) is not { } plunger)
        {
            Game.Warn($"Drain {drain.Entity!.Name}: there's no plunger to serve the next ball onto.");
            return;
        }

        var radius = ball.GetComponent<Collider>() is { } collider ? MathF.Max(collider.WorldBounds.HalfSize.X, collider.WorldBounds.HalfSize.Y) : 0.25f;
        var at = plunger.Tip + plunger.WorldDirection * (radius + 0.01f);
        var transform = ball.Transform;
        var position = new Vector3(at, transform.WorldPosition.Z);
        if (ball.Parent is { } parent && System.Numerics.Matrix4x4.Invert(parent.Transform.WorldMatrix, out var inverse))
            position = Vector3.Transform(position, inverse) with { Z = transform.Position.Z };
        transform.Position = position;
        transform.Rotation = transform.Rotation with { Z = 0 };
        if (ball.GetComponent<Body>() is { } body)
            (body.Velocity, body.Spin, body.Asleep, body.StillFor) = (Vector2.Zero, 0, false, 0);
        ball.IsEnabled = true;

        // A saved ball is shot again by itself, without a ball save of its own: a ball is saved once.
        var saved = drain.Saved;
        if (saved)
        {
            drain.Saved = false;
            plunger.LaunchIn = SavedLaunchDelay;
        }

        drain.SaveReady = !saved;
    }

    // The plunger a drain serves onto: the one its Plunger names, or the nearest.
    private Plunger? PlungerOf(Drain drain)
    {
        var entity = drain.Entity!;
        var name = drain.Plunger?.Trim();
        if (!string.IsNullOrEmpty(name))
            return HingeJoint.Nearest(Scene, entity, name)?.GetComponent<Plunger>();
        Plunger? best = null;
        var bestDistance = float.PositiveInfinity;
        var here = At(entity);
        foreach (var plunger in Game.Showing<Plunger>())
        {
            var distance = Vector2.DistanceSquared(plunger.Tip, here);
            if (distance < bestDistance)
                (best, bestDistance) = (plunger, distance);
        }

        return best;
    }

    // How many other balls are still on the table a drain looks after (not `but`, not one served or waiting).
    private int BallsInPlay(Drain drain, Entity but)
    {
        var scope = Scope.Of(drain.Entity!, drain.Within);
        var count = 0;
        foreach (var body in Game.Present<Body>())
        {
            var ball = body.Entity!;
            if (ball != but && ball.IsActive && IsBall(ball, body) && Matches(drain.Layers, Game.LayerOf(ball)) && scope.Covers(ball) &&
                !Lost(ball))
                count++;
        }

        return count;
    }

    // Whether a ball is falling into a drain this step too.
    private bool Lost(Entity ball)
    {
        foreach (var drain in Game.Showing<Drain>())
        {
            var entity = drain.Entity!;
            if (Game.Touches.Contains((entity, ball)))
                return true;
        }

        return false;
    }

    // ----- What they share -----

    // A ball: a round body that moves (not a flipper).
    private static bool IsBall(Entity entity, Body body) =>
        !body.HeldStill && entity.GetComponent<CircleCollider>() is { IsTrigger: false } && entity.GetComponent<Flipper>() is null;

    // The body that touched a piece, if it's one the piece works on: it moves here, isn't held still, isn't a flipper, and is
    // on its layers.
    private static Entity? Ball(Entity touched, string? layers)
    {
        if (Game.BodyOwner(touched) is not { } owner || owner.GetComponent<Body>() is not { } body || body.HeldStill || !owner.IsActive ||
            owner.IsPaused || !Game.Moves(owner) || owner.GetComponent<Flipper>() is not null || !Matches(layers, Game.LayerOf(touched)))
            return null;
        return owner;
    }

    // What touched `entity` in the step that's just been (or is in it, for a trigger) and it works on, into _found in a
    // fixed order (left to right, then bottom to top); how many.
    private int Touchers(Entity entity, string? layers)
    {
        _found.Clear();
        foreach (var (source, other) in Game.Touches)
        {
            if (source != entity || other.Scene != Scene || Ball(other, layers) is not { } ball || ball == entity || entity.IsSelfOrDescendantOf(ball) ||
                _found.Contains(ball))
                continue;
            var at = At(ball);
            var index = _found.Count;
            while (index > 0 && Before(at, At(_found[index - 1])))
                index--;
            _found.Insert(index, ball);
        }

        return _found.Count;

        static bool Before(Vector2 a, Vector2 b) => a.X < b.X || (a.X == b.X && a.Y < b.Y);
    }

    // Whether a collider's layer is one of `layers` ("Ball, Pinball"); any, with none.
    private static bool Matches(string? layers, string? layer)
    {
        if (string.IsNullOrWhiteSpace(layers))
            return true;
        var name = CollisionLayers.NameOf(layer).AsSpan();
        var rest = layers.AsSpan();
        while (rest.Length > 0)
        {
            var end = rest.IndexOfAny(',', ';');
            var part = (end < 0 ? rest : rest[..end]).Trim();
            if (part.Length > 0 && part.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;
            rest = end < 0 ? default : rest[(end + 1)..];
        }

        return false;
    }

    // The controls that work `entity`, into _inputs: its owner's (a player's own table), or with nobody owning it, this
    // machine's player's and, on the host of a network game, every other player's whose side it is (by its turn taker and
    // their team). None while it waits for its team's turn.
    private void Controls(Entity entity)
    {
        _inputs.Clear();
        if (Game.Waiting(entity))
            return;
        var net = Game.Network;
        if (!net.IsOnline || Networked.OwnerIdOf(entity) != 0)
        {
            _inputs.Add(Game.InputOf(entity));
            return;
        }

        if (!Game.LocalInputBlocked && Allowed(entity, net.LocalPlayer))
            _inputs.Add(Game.Input);
        var players = net.Players;
        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (!player.IsLocal && player.Input is { } remote && Allowed(entity, player))
                _inputs.Add(remote);
        }
    }

    // Whether a player may play a table nobody owns: not one the computer plays, and with teams only their team's.
    private bool Allowed(Entity entity, NetPlayer? player)
    {
        var side = TurnTaker.Of(entity)?.Team?.Trim() ?? "";
        if (side.Length == 0)
            return true;
        if (Game.TurnOrderOf(entity) is { } order && order.IsComputer(side))
            return false;
        return player is not { Team.Length: > 0 } || Teams.Same(side, player.Team);
    }

    // Whether those who play `entity` hold `action` down.
    private bool Held(Entity entity, string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
            return false;
        Controls(entity);
        var name = action.Trim();
        for (var i = 0; i < _inputs.Count; i++)
        {
            if (_inputs[i].IsDown(name))
                return true;
        }

        return false;
    }

    // Whether one of those who play `entity` pressed `action` this step, and whose controls.
    private bool Pressed(Entity entity, string? action, out IInput? by)
    {
        by = null;
        if (string.IsNullOrWhiteSpace(action))
            return false;
        Controls(entity);
        var name = action.Trim();
        for (var i = 0; i < _inputs.Count; i++)
        {
            if (_inputs[i].WasPressed(name))
            {
                by = _inputs[i];
                return true;
            }
        }

        return false;
    }

    // The table something is part of: the outermost entity it's inside, or null for something at the top of the scene.
    private static Entity? TableOf(Entity entity)
    {
        if (entity.Parent is null)
            return null;
        var top = entity;
        while (top.Parent is { } parent)
            top = parent;
        return top;
    }

    // The entities beside it: inside the same entity, or at the top of the scene.
    private IReadOnlyList<Entity> Siblings(Entity entity) => entity.Parent is { } parent ? parent.Children : Scene.Roots;

    private static Vector2 At(Entity entity) => new(entity.Transform.WorldPosition.X, entity.Transform.WorldPosition.Y);

    // The middle of what it's made of in the world.
    private static Vector2 Middle(Entity entity) => entity.GetComponent<Collider>() switch
    {
        PolygonCollider polygon => polygon.WorldCentroid,
        { } collider => collider.WorldBounds.Center,
        _ => At(entity),
    };
}
