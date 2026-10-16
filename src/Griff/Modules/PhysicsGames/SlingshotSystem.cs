using System.Numerics;
using Griff.Input;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.PhysicsGames;

/// <summary>
/// Runs slingshots with no code: loads their shots, holds the loaded one in the pouch and the rest in a row behind, launches
/// them (as a player lets go, or on the Launch action), watches each shot until it comes to rest and says when one is out
/// of shots. The game's runner (offline, or the host) does all that; each machine reads its own player's mouse, keys and
/// gamepad, shows the pull and the path, points the camera at the shot in flight, and sends what its player does to the host.
/// </summary>
internal sealed class SlingshotSystem : GameSystem
{
    // A player's machine asks whoever runs the game to pull a slingshot, or to let go of it and launch.
    internal const string Command = "griff.slingshot";

    private const int PullCommand = 0;
    private const int LaunchCommand = 1;

    // A shot slower than this (world units a second) for this long (seconds) has come to rest.
    private const float RestSpeed = 0.3f;
    private const float RestFor = 0.75f;

    // A shot still going after this many seconds has gone for good (off the edge of the world): the next is loaded.
    private const float FlightLimit = 10;

    // How far apart the shots waiting in a row are, and how far behind its foot the row starts.
    private const float QueueGap = 0.2f;
    private const float QueueStart = 0.6f;

    // A player's pull changed by less than this isn't sent again.
    private const float SendStep = 0.02f;

    public override string Name => "Slingshots";

    // This machine's player: the slingshot they're pulling and whether with the mouse (or the keys), how far, and the pull
    // last sent to the host.
    private Slingshot? _held;
    private bool _byMouse;
    private Vector2 _pull;
    private Vector2 _sent;

    private Scene Scene => Game.Scene;

    protected override void SceneChanged() => _held = null;

    // After scripts and dragging, before physics: what a script asked of a slingshot this frame happens now, and a launched
    // shot flies from this step.
    internal void Run()
    {
        var slingshots = Game.Showing<Slingshot>();
        if (slingshots.Count == 0)
        {
            _held = null;
            return;
        }

        var seconds = Game.Time.Delta;
        foreach (var slingshot in slingshots)
        {
            var entity = slingshot.Entity!;
            if (seconds > 0 && Game.Runs(entity) && !entity.IsPaused)
                Work(slingshot, seconds);
        }

        if (!Game.PausedThisFrame && !Game.LocalInputBlocked && Game.TopPanel is null)
            Control(slingshots);
        else
            _held = null;

        foreach (var slingshot in slingshots)
            Present(slingshot);
    }

    // ----- Where the game is run -----

    private void Work(Slingshot slingshot, float seconds)
    {
        if (slingshot.LoadRequested)
        {
            slingshot.LoadRequested = false;
            LoadNext(slingshot);
        }

        // The shot in flight, until it comes to rest or goes (or has flown for long enough: off the edge of the world).
        if (!string.IsNullOrEmpty(slingshot.Flying))
        {
            slingshot.FlyingFor += seconds;
            if (FlyingShot(slingshot) is not { } shot || shot.GetComponent<Body>() is not { } body || slingshot.FlyingFor >= FlightLimit)
            {
                Landed(slingshot);
            }
            else
            {
                var still = body.Asleep || body.Velocity.LengthSquared() < RestSpeed * RestSpeed;
                slingshot.FlyingStill = still ? slingshot.FlyingStill + seconds : 0;
                if (slingshot.FlyingStill >= RestFor)
                    Landed(slingshot);
            }
        }

        // The next shot, its Load Delay after the last came to rest (or straight away, the first).
        if (!slingshot.Loaded && slingshot.AutoLoad && string.IsNullOrEmpty(slingshot.Flying))
        {
            if (slingshot.LoadIn > 0)
                slingshot.LoadIn = Math.Max(0, slingshot.LoadIn - seconds);
            if (slingshot.LoadIn <= 0)
                LoadNext(slingshot);
        }

        if (slingshot.LaunchRequested)
        {
            slingshot.LaunchRequested = false;
            if (slingshot.Loaded)
                Fire(slingshot, slingshot.Pull != Vector2.Zero ? slingshot.Pull : slingshot.DefaultPull);
        }

        LayOut(slingshot, Scene.Step);

        var spent = !slingshot.Loaded && string.IsNullOrEmpty(slingshot.Flying) && slingshot.ShotsLeft == 0;
        if (spent && !slingshot.OutOfShots)
        {
            slingshot.OutOfShots = true;
            if (!string.IsNullOrWhiteSpace(slingshot.OutOfShotsMessage))
                Game.Send(slingshot.OutOfShotsMessage.Trim(), slingshot.Entity, from: slingshot.Entity);
        }
        else if (!spent && slingshot.OutOfShots)
        {
            slingshot.OutOfShots = false;
        }
    }

    // Its next shot into the pouch: the first waiting inside it, or a new copy of its prefab while it has some to make.
    private void LoadNext(Slingshot slingshot)
    {
        if (slingshot.Loaded)
            return;
        if (slingshot.FirstShot is null && !string.IsNullOrWhiteSpace(slingshot.Shot) && slingshot.Made < slingshot.Shots)
        {
            var name = slingshot.Shot.Trim();
            try
            {
                var shot = Game.Spawn(name, slingshot.Entity);
                if (shot.GetComponent<Body>() is null)
                    Game.Warn($"Slingshot {slingshot.Entity!.Name}: its Shot \"{name}\" has no Body, so it can't be launched.");
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or InvalidDataException or KeyNotFoundException or ArgumentException)
            {
                Game.Warn($"Slingshot {slingshot.Entity!.Name}: its Shot \"{name}\" won't load ({ex.Message}).");
            }

            slingshot.Made++;
        }

        if (slingshot.FirstShot is null)
            return;
        (slingshot.Loaded, slingshot.Pull, slingshot.LoadIn) = (true, Vector2.Zero, 0);

        // The camera comes back for the next shot.
        (slingshot.Flying, slingshot.FlyingStill, slingshot.FlyingFor) = (null, 0, 0);
    }

    // Its shot in flight has come to rest (or gone): the next is loaded its Load Delay on.
    private static void Landed(Slingshot slingshot) =>
        (slingshot.Flying, slingshot.FlyingStill, slingshot.FlyingFor, slingshot.LoadIn) = (null, 0, 0, Math.Max(0, slingshot.LoadDelay));

    // The shot it launched last, while it's still about.
    private Entity? FlyingShot(Slingshot slingshot)
    {
        var id = slingshot.Flying;
        if (string.IsNullOrEmpty(id))
            return null;
        if (!ReferenceEquals(id, slingshot.FlyingKey) || slingshot.FlyingEntity?.Scene != Scene)
            (slingshot.FlyingKey, slingshot.FlyingEntity) = (id, Guid.TryParse(id, out var guid) ? Scene.Find(guid) : null);
        return slingshot.FlyingEntity is { IsActive: true } shot ? shot : null;
    }

    // Its shots held still: the loaded one in the pouch, pulled as it is, and the rest in a row behind its foot, standing on
    // what it stands on.
    private static void LayOut(Slingshot slingshot, long step)
    {
        var entity = slingshot.Entity!;
        var children = entity.ChildList;
        var first = true;
        var x = -QueueStart;
        Matrix4x4 inverse = default;
        var inverted = false;
        for (var i = 0; i < children.Count; i++)
        {
            var shot = children[i];
            if (!Slingshot.IsShot(shot))
                continue;
            HoldOut(shot, step);
            var body = shot.GetComponent<Body>()!;
            if (!body.Frozen)
                body.Frozen = true;
            if (body.Velocity != Vector2.Zero)
                body.Velocity = Vector2.Zero;
            if (body.Spin != 0)
                body.Spin = 0;
            if (body.Asleep)
                body.Asleep = false;

            Vector2 at;
            if (first && slingshot.Loaded)
            {
                if (!inverted)
                    inverted = Matrix4x4.Invert(entity.Transform.WorldMatrix, out inverse);
                at = slingshot.Pouch;
                if (inverted && slingshot.Pull != Vector2.Zero)
                    at = Vector2.Transform(slingshot.PouchInWorld + slingshot.Pull, inverse);
            }
            else
            {
                var half = shot.GetComponent<Collider>() is { } collider ? collider.WorldBounds.HalfSize : new Vector2(0.4f);
                x -= half.X;
                at = new Vector2(x, half.Y);
                x -= half.X + QueueGap;
            }

            first = false;
            var transform = shot.Transform;
            var position = transform.Position;
            if (position.X != at.X || position.Y != at.Y)
                transform.Position = new Vector3(at, position.Z);
            if (transform.Rotation.Z != 0)
                transform.Rotation = transform.Rotation with { Z = 0 };
        }
    }

    // A shot held in the slingshot is out of the world for the step (`step`; -1 back in it): nothing bumps into it, nor it
    // into anything, so the one in the pouch passes the others as it's pulled back and let go.
    private static void HoldOut(Entity shot, long step)
    {
        var components = shot.ComponentList;
        for (var i = 0; i < components.Count; i++)
        {
            if (components[i] is Collider collider)
                collider.HeldOutIn = step;
        }
    }

    // Lets go of the loaded shot pulled back `pull`: out of the slingshot (beside it), flying the other way.
    private void Fire(Slingshot slingshot, Vector2 pull)
    {
        if (slingshot.LoadedShot is not { } shot || shot.GetComponent<Body>() is not { } body)
            return;
        var entity = slingshot.Entity!;
        pull = Limit(slingshot, pull);
        var from = slingshot.PouchInWorld + pull;
        var parent = entity.Parent;
        Scene.Move(shot, parent, Scene.IndexOf(entity) + 1);
        var at = new Vector3(from, shot.Transform.Position.Z);
        if (parent is not null && Matrix4x4.Invert(parent.Transform.WorldMatrix, out var inverse))
            at = Vector3.Transform(at, inverse) with { Z = at.Z };
        shot.Transform.Position = at;

        HoldOut(shot, -1);
        (body.Frozen, body.Asleep, body.StillFor) = (false, false, 0);
        (body.Velocity, body.Spin) = (slingshot.LaunchVelocity(pull), 0);
        (slingshot.Loaded, slingshot.Pull, slingshot.LoadIn) = (false, Vector2.Zero, 0);
        (slingshot.Flying, slingshot.FlyingStill, slingshot.FlyingFor) = (shot.Id.ToString(), 0, 0);
        Game.UseAction(entity);

        if (!string.IsNullOrWhiteSpace(slingshot.LaunchSound))
            Game.PlaySoundAt(slingshot.LaunchSound.Trim(), from);
        if (!string.IsNullOrWhiteSpace(slingshot.LaunchMessage))
            Game.Send(slingshot.LaunchMessage.Trim(), shot, from: entity);
        slingshot.RaiseLaunched(shot);
    }

    // Whether a player may pull a slingshot: not one the computer plays, in a network game with teams only their team's;
    // and, with `now`, only on its team's turn (with actions left).
    private bool MayUse(Slingshot slingshot, NetPlayer? player, bool now)
    {
        var entity = slingshot.Entity!;
        var side = TurnTaker.Of(entity)?.Team?.Trim() ?? "";
        if (side.Length > 0 && Game.TurnOrderOf(entity) is { } order && order.IsComputer(side))
            return false;
        if (Game.Net.IsOnline && player is { Team.Length: > 0 } && side.Length > 0 && !Teams.Same(side, player.Team))
            return false;
        return !now || !Game.Waiting(entity);
    }

    // A pull or a launch a player asked for, carried out where the game is run if it's theirs to make.
    internal void RunCommand(NetPlayer? player, object? args)
    {
        if (args is not object?[] { Length: 4 } a || a[0] is not int kind || a[1] is not Entity entity || entity.Scene != Scene ||
            !Game.Runs(entity) || entity.IsPaused || entity.GetComponent<Slingshot>() is not { Loaded: true } slingshot ||
            a[2] is not float x || a[3] is not float y || !float.IsFinite(x) || !float.IsFinite(y) || !MayUse(slingshot, player, now: true))
            return;
        var pull = Limit(slingshot, new Vector2(x, y));
        if (kind == LaunchCommand && pull.Length() >= slingshot.MinPull)
            Fire(slingshot, pull);
        else
            Pulled(slingshot, pull);
    }

    // Pulled to `pull`: a stretch sound as it's first pulled back.
    private void Pulled(Slingshot slingshot, Vector2 pull)
    {
        if (slingshot.Pull == Vector2.Zero && pull != Vector2.Zero && !string.IsNullOrWhiteSpace(slingshot.StretchSound))
            Game.PlaySoundAt(slingshot.StretchSound.Trim(), slingshot.PouchInWorld);
        slingshot.Pull = pull;
    }

    // ----- Each machine: its player's input, and how things look here -----

    private void Control(ComponentList<Slingshot> slingshots)
    {
        var input = Game.Input;
        var me = Game.Net.LocalPlayer;
        if (_held is { } held && (held.Entity?.Scene != Scene || !Usable(held, me)))
            _held = null;

        // The mouse (or a finger): press near a loaded pouch, drag it back, let go.
        var mouse = input.MouseWorld;
        if (input.WasPressed(MouseButton.Left) && input.IsMouseInView && !Game.MouseOnUi && (_held is null || !_byMouse))
        {
            foreach (var slingshot in slingshots)
            {
                if (Usable(slingshot, me) && Vector2.Distance(mouse, slingshot.PouchInWorld) <= slingshot.GrabRadius)
                {
                    (_held, _byMouse, _sent) = (slingshot, true, slingshot.Pull);
                    break;
                }
            }
        }

        if (_held is { } pulled && _byMouse)
        {
            _pull = Limit(pulled, mouse - pulled.PouchInWorld);
            if (!input.IsDown(MouseButton.Left))
            {
                LetGo(pulled, _pull.Length() >= pulled.MinPull);
                return;
            }

            Send(pulled, _pull);
            return;
        }

        // The keys or a gamepad: pull the pouch about, then the Launch Action lets go (or the Cancel Action lets it back).
        Slingshot? target = _held;
        if (target is null)
        {
            foreach (var slingshot in slingshots)
            {
                if (Usable(slingshot, me))
                {
                    target = slingshot;
                    break;
                }
            }
        }

        if (target is null)
            return;
        var axis = new Vector2(input.Horizontal, input.Vertical);
        if (_held is null)
        {
            if (axis == Vector2.Zero)
                return;
            (_held, _byMouse, _pull, _sent) = (target, false, target.Pull, target.Pull);
        }

        _pull = Limit(target, _pull + axis * target.PullSpeed * Game.Time.UnscaledDelta);
        if (Pressed(input, target.CancelAction))
        {
            _pull = Vector2.Zero;
            LetGo(target, launch: false);
            return;
        }

        if (Pressed(input, target.LaunchAction) && _pull.Length() >= target.MinPull)
        {
            LetGo(target, launch: true);
            return;
        }

        Send(target, _pull);
    }

    private static bool Pressed(IInput input, string? action) => !string.IsNullOrWhiteSpace(action) && input.WasPressed(action.Trim());

    // Whether this machine's player can take hold of a slingshot now.
    private bool Usable(Slingshot slingshot, NetPlayer? me) =>
        slingshot.Controllable && slingshot.Loaded && !slingshot.Entity!.IsPaused && MayUse(slingshot, me, now: true);

    // This player's pull, to the game: straight on where it's run, else sent to the host when it has changed.
    private void Send(Slingshot slingshot, Vector2 pull)
    {
        if (!Game.Net.IsClient)
        {
            if (slingshot.Pull != pull)
                Pulled(slingshot, pull);
            _sent = pull;
            return;
        }

        if (Vector2.Distance(pull, _sent) < SendStep)
            return;
        _sent = pull;
        Game.Net.SendCommand(Command, new object?[] { PullCommand, slingshot.Entity, pull.X, pull.Y });
    }

    // This player lets go: launching at the pull they had, or letting it back.
    private void LetGo(Slingshot slingshot, bool launch)
    {
        var pull = launch ? _pull : Vector2.Zero;
        _held = null;
        if (!Game.Net.IsClient)
        {
            if (launch)
                Fire(slingshot, pull);
            else
                Pulled(slingshot, Vector2.Zero);
            return;
        }

        Game.Net.SendCommand(Command, new object?[] { launch ? LaunchCommand : PullCommand, slingshot.Entity, pull.X, pull.Y });
    }

    // A pull no further than its Max Pull, and not so low the shot in the pouch would go below its foot (into the ground).
    private static Vector2 Limit(Slingshot slingshot, Vector2 pull)
    {
        var (length, most) = (pull.Length(), Math.Max(0, slingshot.MaxPull));
        if (length > most && length > 0)
            pull *= most / length;
        var entity = slingshot.Entity!;
        var half = slingshot.LoadedShot?.GetComponent<Collider>() is { } collider ? collider.WorldBounds.HalfSize.Y : 0;
        var lowest = entity.Transform.WorldPosition.Y + half - slingshot.PouchInWorld.Y;
        return pull.Y < lowest && lowest <= 0 ? pull with { Y = lowest } : pull;
    }

    // How it's drawn here (the pull, this player's own while they pull it, and the path a shot pulled so takes), and the
    // camera on its shot in flight.
    private void Present(Slingshot slingshot)
    {
        var pull = _held == slingshot ? _pull : slingshot.Pull;
        slingshot.ShownPull = slingshot.Loaded ? pull : Vector2.Zero;
        slingshot.DotCount = 0;
        if (slingshot.Preview > 0 && slingshot.Loaded && pull.Length() >= slingshot.MinPull * 0.5f)
            Plot(slingshot, pull);

        if (!slingshot.CameraFollows)
            return;
        if (FlyingShot(slingshot) is { } shot)
        {
            if (SceneRenderer.FindCamera(Scene)?.GetComponent<Follow>() is { } follow)
            {
                if (follow.TargetEntity != shot)
                    follow.TargetEntity = shot;
                slingshot.Following = follow;
            }
        }
        else if (slingshot.Following is { } following)
        {
            // Back to what it followed before (looked up by its Target again).
            if (following.TargetEntity is { } target && target.GetComponent<Body>() is not null && target != slingshot.Entity)
                following.TargetEntity = null;
            slingshot.Following = null;
        }
    }

    // The path a shot pulled back `pull` takes as it flies, as the game moves it (gravity a part of a step at a time for one
    // that turns), with nothing in its way: a dot every few steps, for the Preview's seconds. The same every time.
    private void Plot(Slingshot slingshot, Vector2 pull)
    {
        var body = slingshot.LoadedShot?.GetComponent<Body>();
        var gravity = Game.Physics.Gravity * (body?.GravityScale ?? 1);
        var turns = body?.Rotates ?? true;
        var step = Game.Time.FixedDelta;
        var steps = (int)MathF.Ceiling(slingshot.Preview / step);
        var every = Math.Max(1, (int)MathF.Ceiling(steps / (float)Slingshot.MaxDots));
        var (at, velocity) = (slingshot.PouchInWorld + pull, slingshot.LaunchVelocity(pull));
        var part = step / 4;
        for (var i = 1; i <= steps && slingshot.DotCount < Slingshot.MaxDots; i++)
        {
            if (turns)
            {
                for (var p = 0; p < 4; p++)
                {
                    velocity += gravity * part;
                    at += velocity * part;
                }
            }
            else
            {
                velocity += gravity * step;
                at += velocity * step;
            }

            if (i % every == 0)
                slingshot.Dots[slingshot.DotCount++] = at;
        }
    }
}
