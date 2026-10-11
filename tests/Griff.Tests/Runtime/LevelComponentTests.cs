using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>Movers, patrols, chasers, shooters, checkpoints, doors and dialogue: levels with no code.</summary>
public class LevelComponentTests
{
    private const float Step = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;
    private readonly Dictionary<string, Func<Entity>> _prefabs = [];

    public LevelComponentTests()
    {
        _game = new Game(_scene, _input) { PrefabLoader = name => _prefabs[name]() };
        _game.Physics.Gravity = Vector2.Zero;
    }

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Step)
        {
            _game.Tick(Step);
            _input.EndFrame();
        }
    }

    private void Press(Key key)
    {
        _input.SetKey(key, true);
        _game.Tick(Step);
        _input.EndFrame();
        _input.SetKey(key, false);
        _game.Tick(Step);
        _input.EndFrame();
    }

    private Entity Add(string name, Vector2 position, Entity? parent = null)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        _scene.Add(entity, parent);
        return entity;
    }

    private Entity AddBox(string name, Vector2 position, Vector2 size, bool trigger = false)
    {
        var entity = Add(name, position);
        var collider = entity.AddComponent<BoxCollider>();
        collider.Size = size;
        collider.IsTrigger = trigger;
        return entity;
    }

    private Entity AddPlayer(Vector2 position)
    {
        var player = AddBox("Player", position, new Vector2(0.8f, 1));
        player.GetComponent<BoxCollider>()!.Layer = "Player";
        player.AddComponent<Body>();
        player.AddComponent<Sprite>();
        return player;
    }

    private static Vector2 Position(Entity entity) => new(entity.Transform.WorldPosition.X, entity.Transform.WorldPosition.Y);

    // ---- Mover ----

    [Fact]
    public void Mover_PingPongs_BetweenItsPoints_WaitingAtEach()
    {
        var platform = Add("Platform", new Vector2(1, 1));
        var mover = platform.AddComponent<Mover>();
        mover.Points = "4 0";
        mover.Speed = 2;
        mover.Wait = 0.5f;
        mover.Smooth = false;

        Run(1);
        Assert.Equal(3, Position(platform).X, 0.05f);
        Run(1.25f); // reaches the end at 2 s and waits there
        Assert.Equal(5, Position(platform).X, 0.05f);
        Run(1.25f); // waited till 2.5 s, then a second's way back
        Assert.Equal(3, Position(platform).X, 0.05f);
        Run(1.25f); // back at the start at 4.5 s, waiting there
        Assert.Equal(1, Position(platform).X, 0.05f);
        Assert.Equal(1, Position(platform).Y, 0.001f);
    }

    [Fact]
    public void Mover_Once_StopsAtTheLastPoint()
    {
        var gate = Add("Gate", Vector2.Zero);
        var mover = gate.AddComponent<Mover>();
        mover.Points = "0 2; 3 2";
        mover.Mode = MoverMode.Once;
        mover.Speed = 5;

        Run(3);

        Assert.True(mover.IsFinished);
        Assert.Equal(new Vector2(3, 2), Position(gate));
    }

    [Fact]
    public void Mover_Loop_GoesStraightBackToTheStart()
    {
        var mover = new Mover { Points = "2 0; 2 2", Mode = MoverMode.Loop, Speed = 1, Wait = 0, Smooth = false };

        Assert.Equal(new Vector2(2, 0), mover.OffsetAt(2));
        Assert.Equal(new Vector2(2, 2), mover.OffsetAt(4));
        Assert.Equal(new Vector2(1, 1), mover.OffsetAt(4 + MathF.Sqrt(2)), new Vector2Comparer(0.01f));
        Assert.Equal(Vector2.Zero, mover.OffsetAt(4 + 2 * MathF.Sqrt(2)), new Vector2Comparer(0.01f));
    }

    [Fact]
    public void Mover_StartedPartWayRound_KeepsToTheSamePath()
    {
        // A rewound game (or a staggered platform) has Time already set and stands where that puts it.
        var platform = Add("Platform", new Vector2(2, 0));
        var mover = platform.AddComponent<Mover>();
        mover.Points = "4 0";
        mover.Speed = 2;
        mover.Wait = 0;
        mover.Smooth = false;
        mover.Time = 1;

        Run(1);

        Assert.Equal(4, Position(platform).X, 0.05f);
        Run(2);
        Assert.Equal(0, Position(platform).X, 0.05f);
    }

    [Fact]
    public void Mover_MovedBySomethingElse_TakesItsPathAlong()
    {
        var platform = Add("Platform", Vector2.Zero);
        var mover = platform.AddComponent<Mover>();
        mover.Points = "0 4";
        mover.Speed = 1;
        mover.Smooth = false;
        Run(1);

        platform.Transform.Position += new Vector3(10, 0, 0);
        Run(1);

        Assert.Equal(new Vector2(10, 2), Position(platform), new Vector2Comparer(0.05f));
    }

    [Fact]
    public void Mover_CarriesWhatStandsOnIt()
    {
        _game.Physics.Gravity = new Vector2(0, -25);
        var platform = AddBox("Platform", new Vector2(0, 0), new Vector2(4, 0.5f));
        var mover = platform.AddComponent<Mover>();
        mover.Points = "6 0";
        mover.Speed = 2;
        mover.Smooth = false;
        var player = AddPlayer(new Vector2(0, 0.75f));

        Run(1.5f);

        Assert.True(player.GetComponent<Body>()!.IsGrounded);
        Assert.Equal(Position(platform).X, Position(player).X, 0.1f);
        Assert.True(Position(player).X > 2);
    }

    [Fact]
    public void Mover_ParsesPoints_WithCommasOrSpaces_SkippingJunk()
    {
        var points = Mover.ParsePoints("4 0; 4,3 ;nonsense; -1.5, 2");

        Assert.Equal([new Vector2(4, 0), new Vector2(4, 3), new Vector2(-1.5f, 2)], points);
    }

    // ---- Patrol ----

    private Entity AddWalker(Vector2 position)
    {
        var walker = AddBox("Walker", position, new Vector2(0.8f, 0.8f));
        walker.GetComponent<BoxCollider>()!.Layer = "Enemy";
        walker.AddComponent<Body>();
        walker.AddComponent<Sprite>();
        walker.AddComponent<Patrol>().Speed = 3;
        return walker;
    }

    [Fact]
    public void Patrol_TurnsAtWalls()
    {
        _game.Physics.Gravity = new Vector2(0, -25);
        AddBox("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        AddBox("Wall", new Vector2(4, 1), new Vector2(1, 2));
        var walker = AddWalker(new Vector2(0, 0.4f));
        var patrol = walker.GetComponent<Patrol>()!;
        var turns = 0;
        patrol.Turned += _ => turns++;

        Run(2);

        Assert.True(patrol.GoingLeft);
        Assert.Equal(1, turns);
        Assert.True(walker.GetComponent<Sprite>()!.FlipX);
        Assert.True(Position(walker).X < 3);
    }

    [Fact]
    public void Patrol_TurnsAtTheEdge_RatherThanWalkingOff()
    {
        _game.Physics.Gravity = new Vector2(0, -25);
        AddBox("Ledge", new Vector2(0, -0.5f), new Vector2(6, 1));
        var walker = AddWalker(new Vector2(0, 0.4f));

        Run(6);

        Assert.True(walker.GetComponent<Body>()!.IsGrounded);
        Assert.InRange(Position(walker).X, -3, 3);
    }

    [Fact]
    public void Patrol_TurnsAtItsLeftAndRight_OnALongFloor()
    {
        _game.Physics.Gravity = new Vector2(0, -25);
        AddBox("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        var walker = AddWalker(new Vector2(0, 0.4f));
        var patrol = walker.GetComponent<Patrol>()!;
        (patrol.Left, patrol.Right) = (2, 1);

        var (least, most) = (float.MaxValue, float.MinValue);
        for (var t = 0f; t < 6; t += Step)
        {
            Run(Step);
            least = MathF.Min(least, Position(walker).X);
            most = MathF.Max(most, Position(walker).X);
        }

        // Out to each turn (a frame's walk past it at most), and no further.
        Assert.InRange(least, -2.2f, -1.9f);
        Assert.InRange(most, 0.9f, 1.2f);
        Assert.Equal(Position(walker).X, patrol.Walked, 2);
    }

    [Fact]
    public void Patrol_Walked_CountsHowFarItIsFromWhereItWasPlaced_HoweverItGotThere()
    {
        _game.Physics.Gravity = new Vector2(0, -25);
        AddBox("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        var walker = AddWalker(new Vector2(0, 0.4f));
        var patrol = walker.GetComponent<Patrol>()!;
        patrol.Right = 3;

        Run(0.5f);
        // Knocked (or dragged) further along: past its Right turn, so it heads back.
        walker.Transform.Position += new Vector3(4, 0, 0);
        Run(0.5f);

        Assert.True(patrol.GoingLeft);
        Assert.Equal(Position(walker).X, patrol.Walked, 2);

        // Starting part of the way along: it's already at its Right turn, so it turns straight away.
        var other = AddWalker(new Vector2(10, 0.4f));
        var started = other.GetComponent<Patrol>()!;
        (started.Right, started.Walked) = (1, 1);
        Run(Step * 2);
        Assert.True(started.GoingLeft);
    }

    [Fact]
    public void Patrol_WithoutTurningAtEdges_WalksOff()
    {
        _game.Physics.Gravity = new Vector2(0, -25);
        AddBox("Ledge", new Vector2(0, -0.5f), new Vector2(6, 1));
        var walker = AddWalker(new Vector2(0, 0.4f));
        walker.GetComponent<Patrol>()!.TurnAtEdges = false;

        Run(2);

        Assert.True(Position(walker).Y < -1);
    }

    [Fact]
    public void Body_WallSide_SaysWhichSideAWallStoppedIt()
    {
        AddBox("Wall", new Vector2(-2, 0), new Vector2(1, 4));
        var body = AddPlayer(Vector2.Zero).GetComponent<Body>()!;

        // Walking into it: a wall stops a body, so it has to keep pushing (as a player holding left does).
        for (var i = 0; i < 30; i++)
        {
            body.Velocity = new Vector2(-5, 0);
            Run(Step);
        }

        Assert.Equal(-1, body.WallSide);

        body.Velocity = new Vector2(5, 0);
        Run(Step);
        Assert.Equal(0, body.WallSide);
    }

    // ---- Chase ----

    private Chase AddChaser(Vector2 position, float range = 5)
    {
        var chaser = AddBox("Chaser", position, new Vector2(0.6f, 0.6f));
        chaser.GetComponent<BoxCollider>()!.Layer = "Enemy";
        chaser.AddComponent<Body>().GravityScale = 0;
        chaser.AddComponent<Sprite>();
        var chase = chaser.AddComponent<Chase>();
        chase.Range = range;
        chase.Speed = 4;
        return chase;
    }

    [Fact]
    public void Chase_GoesAfterATargetInRange_AndWaitsForOneOutOfIt()
    {
        var player = AddPlayer(new Vector2(4, 0));
        var near = AddChaser(Vector2.Zero);
        var far = AddChaser(new Vector2(-20, 0));
        var spotted = 0;
        near.Spotted += _ => spotted++;

        Run(0.5f);

        Assert.True(near.IsChasing);
        Assert.Equal(1, spotted);
        Assert.True(Position(near.Entity!).X > 1);
        Assert.False(far.IsChasing);
        Assert.Equal(-20, Position(far.Entity!).X, 0.001f);
        Assert.Same(player, near.TargetEntity);
    }

    [Fact]
    public void Chase_GivesUp_OnceTheTargetIsWellOutOfRange()
    {
        var player = AddPlayer(new Vector2(4, 0));
        var chase = AddChaser(Vector2.Zero);
        chase.GiveUp = 2;
        Run(Step);
        Assert.True(chase.IsChasing);

        player.Transform.Position = new Vector3(Position(chase.Entity!).X + 6.5f, 0, 0);
        Run(Step);
        Assert.True(chase.IsChasing); // still within Range + GiveUp

        player.Transform.Position = new Vector3(Position(chase.Entity!).X + 8, 0, 0);
        Run(Step);
        Assert.False(chase.IsChasing);
        Assert.Equal(Vector2.Zero, chase.Entity!.GetComponent<Body>()!.Velocity);
    }

    [Fact]
    public void Chase_NeedsSight_IgnoresATargetBehindAWall()
    {
        AddPlayer(new Vector2(4, 0));
        AddBox("Wall", new Vector2(2, 0), new Vector2(0.5f, 4));
        var chase = AddChaser(Vector2.Zero);
        chase.NeedsSight = true;

        Run(0.2f);

        Assert.False(chase.IsChasing);
    }

    [Fact]
    public void Chase_WithGravity_OnlyRunsSideways_AndFlipsToFace()
    {
        _game.Physics.Gravity = new Vector2(0, -25);
        AddBox("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        AddPlayer(new Vector2(-4, 3));
        var chase = AddChaser(new Vector2(0, 0.3f));
        chase.Entity!.GetComponent<Body>()!.GravityScale = 1;

        Run(0.5f);

        var body = chase.Entity.GetComponent<Body>()!;
        Assert.True(body.IsGrounded);
        Assert.Equal(-4, body.Velocity.X, 0.001f);
        Assert.True(chase.Entity.GetComponent<Sprite>()!.FlipX);
    }

    [Fact]
    public void Chase_Around_FindsItsWayRoundATilemapsWalls()
    {
        var level = Add("Level", new Vector2(0, 0));
        var map = level.AddComponent<Tilemap>();
        map.Tiles =
        [
            "##########".Replace('#', '0'),
            "0........0",
            "0...0....0",
            "0...0....0",
            "0...0....0",
            "0........0",
            "0000000000",
        ];
        var player = AddPlayer(new Vector2(6.5f, -3.5f));
        player.GetComponent<Body>()!.GravityScale = 0;
        var chase = AddChaser(new Vector2(2.5f, -3.5f), range: 0);
        chase.Around = "Level";
        chase.StopDistance = 0.8f;

        Run(4);

        Assert.True(Vector2.Distance(Position(chase.Entity!), Position(player)) < 1.2f,
            $"The chaser got stuck at {Position(chase.Entity!)}.");
    }

    [Fact]
    public void Chase_AndPatrol_PatrolsUntilItSpotsTheTarget()
    {
        _game.Physics.Gravity = new Vector2(0, -25);
        AddBox("Floor", new Vector2(0, -0.5f), new Vector2(60, 1));
        var walker = AddWalker(new Vector2(0, 0.4f));
        var chase = walker.AddComponent<Chase>();
        chase.Range = 4;
        chase.Speed = 5;
        var player = AddPlayer(new Vector2(-20, 0.5f));

        Run(0.5f);
        Assert.False(chase.IsChasing);
        Assert.True(walker.GetComponent<Body>()!.Velocity.X > 0); // patrolling right

        player.Transform.Position = new Vector3(Position(walker).X - 3, 0.5f, 0);
        Run(0.2f);
        Assert.True(chase.IsChasing);
        Assert.Equal(-5, walker.GetComponent<Body>()!.Velocity.X, 0.001f);
    }

    // ---- Shooter ----

    private Entity Bullet()
    {
        var bullet = new Entity("Bullet");
        bullet.AddComponent<Body>().GravityScale = 0;
        return bullet;
    }

    [Fact]
    public void Shooter_FiresAtItsTarget_EveryInterval_OnlyInRange()
    {
        _prefabs["Bullet"] = Bullet;
        var turret = Add("Turret", Vector2.Zero);
        var shooter = turret.AddComponent<Shooter>();
        shooter.Target = "Player";
        shooter.Interval = 0.5f;
        shooter.Speed = 10;
        shooter.Range = 8;
        var player = AddPlayer(new Vector2(0, 5));
        var fired = new List<Entity>();
        shooter.Fired += fired.Add;

        Run(1.2f);

        Assert.Equal(3, fired.Count); // at 0, 0.5 and 1 s
        var velocity = fired[0].GetComponent<Body>()!.Velocity;
        Assert.Equal(new Vector2(0, 10), velocity, new Vector2Comparer(0.01f));
        Assert.Equal(90, fired[0].Transform.Rotation.Z, 0.01f);

        player.Transform.Position = new Vector3(0, 50, 0);
        fired.Clear();
        Run(1);
        Assert.Empty(fired);
    }

    [Fact]
    public void Shooter_WithAnAction_FiresOnlyWhileItsHeld()
    {
        _prefabs["Bullet"] = Bullet;
        var ship = Add("Ship", Vector2.Zero);
        var shooter = ship.AddComponent<Shooter>();
        shooter.Action = "Fire";
        shooter.Interval = 0.25f;
        var fired = 0;
        shooter.Fired += _ => fired++;

        Run(1);
        Assert.Equal(0, fired);

        _input.SetKey(Key.J, true);
        Run(0.6f);
        Assert.Equal(3, fired); // at once, then every quarter second
    }

    [Fact]
    public void Shooter_HoldsFire_WhileGameTimeStandsStill()
    {
        _prefabs["Bullet"] = Bullet;
        var shooter = Add("Turret", Vector2.Zero).AddComponent<Shooter>();
        shooter.Interval = 0.1f;
        var fired = 0;
        shooter.Fired += _ => fired++;
        Run(Step);
        Assert.Equal(1, fired);

        _game.HitStop(0.5f);
        Run(0.4f);
        Assert.Equal(1, fired);

        _game.Time.Scale = 0;
        Run(0.4f);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Shooter_Volley_FansAcrossItsSpread()
    {
        var shooter = new Shooter { Count = 3, Spread = 90 };

        var directions = shooter.VolleyAround(Vector2.UnitX).ToList();

        var comparer = new Vector2Comparer(0.001f);
        Assert.Equal(Vector2.Normalize(new Vector2(1, -1)), directions[0], comparer);
        Assert.Equal(Vector2.UnitX, directions[1], comparer);
        Assert.Equal(Vector2.Normalize(new Vector2(1, 1)), directions[2], comparer);
    }

    // ---- Checkpoints and respawning ----

    private Checkpoint AddCheckpoint(Vector2 position)
    {
        var flag = AddBox("Flag", position, new Vector2(1, 2), trigger: true);
        flag.AddComponent<Sprite>().Tint = Color.White;
        return flag.AddComponent<Checkpoint>();
    }

    [Fact]
    public void Checkpoint_ReachedByThePlayer_BecomesTheOnlyActiveOne()
    {
        var first = AddCheckpoint(new Vector2(0, 0));
        var second = AddCheckpoint(new Vector2(10, 0));
        second.Active = true;
        var player = AddPlayer(new Vector2(-3, 0));
        var counter = Add("Count", Vector2.Zero).AddComponent<Counter>();
        counter.Message = "Checkpoint";
        first.Message = "Checkpoint";
        Entity? reachedBy = null;
        first.Reached += by => reachedBy = by;

        Run(Step);
        Assert.Equal(Color.White, first.Entity!.GetComponent<Sprite>()!.Tint);
        Assert.Equal(second.ActiveTint, second.Entity!.GetComponent<Sprite>()!.Tint);

        player.Transform.Position = Vector3.Zero;
        Run(0.1f);

        Assert.True(first.Active);
        Assert.False(second.Active);
        Assert.Same(player, reachedBy);
        Assert.Equal(1, counter.Value);
        Assert.Equal(first.ActiveTint, first.Entity.GetComponent<Sprite>()!.Tint);
        Assert.Equal(Color.White, second.Entity.GetComponent<Sprite>()!.Tint);
    }

    [Fact]
    public void Health_Respawn_ComesBackAtTheActiveCheckpoint_AfterItsDelay()
    {
        var checkpoint = AddCheckpoint(new Vector2(5, 0));
        checkpoint.Active = true;
        var player = AddPlayer(new Vector2(-5, 0));
        var health = player.AddComponent<Health>();
        health.Respawn = true;
        health.RespawnDelay = 0.5f;
        health.DeathMessage = "PlayerDied";
        var deaths = 0;
        _game.On("PlayerDied", _ => deaths++);
        var respawned = 0;
        health.Respawned += () => respawned++;
        Run(Step);

        health.Kill();
        Run(0.25f);

        Assert.Equal(1, deaths);
        Assert.False(player.IsEnabled);
        Assert.Same(_scene, player.Scene);

        Run(0.35f);

        Assert.True(player.IsEnabled);
        Assert.Equal(1, respawned);
        Assert.Equal(health.Max, health.Current);
        Assert.True(health.IsInvincible);
        Assert.Equal(new Vector2(5, 0), Position(player), new Vector2Comparer(0.01f));
    }

    [Fact]
    public void Health_Respawn_WithoutACheckpoint_ComesBackWhereItStarted()
    {
        var player = AddPlayer(new Vector2(-5, 2));
        var health = player.AddComponent<Health>();
        health.Respawn = true;
        health.RespawnDelay = 0;
        Run(Step);
        player.Transform.Position = new Vector3(30, -10, 0);
        player.GetComponent<Body>()!.Velocity = new Vector2(3, -20);

        health.Kill();
        Run(3 * Step);

        Assert.True(player.IsEnabled);
        Assert.False(health.IsDead);
        Assert.Equal(new Vector2(-5, 2), Position(player), new Vector2Comparer(0.01f));
    }

    [Fact]
    public void Health_Respawn_SnapsTheCameraFollowingIt()
    {
        var player = AddPlayer(new Vector2(0, 0));
        var health = player.AddComponent<Health>();
        health.Respawn = true;
        health.RespawnDelay = 0;
        var camera = Add("Camera", Vector2.Zero);
        camera.AddComponent<Camera>();
        camera.AddComponent<Follow>().Smoothing = 1;
        Run(Step);

        player.Transform.Position = new Vector3(40, 0, 0);
        Run(Step);
        health.Kill();
        Run(3 * Step);

        Assert.Equal(0, camera.Transform.Position.X, 0.01f);
    }

    [Fact]
    public void DamageOnTouch_Kill_KillsThroughInvincibility()
    {
        var player = AddPlayer(new Vector2(0, 0));
        var health = player.AddComponent<Health>();
        health.Damage(1);
        Assert.True(health.IsInvincible);
        var pit = AddBox("Pit", new Vector2(0, 0), new Vector2(4, 1), trigger: true);
        pit.AddComponent<DamageOnTouch>().Kill = true;

        Run(Step);

        Assert.True(health.IsDead);
    }

    // ---- Doors ----

    [Fact]
    public void Door_WithAScene_LoadsIt_WhenThePlayerWalksIn()
    {
        var next = new Scene();
        _game.SceneLoader = name => name == "Level2" ? next : throw new FileNotFoundException(name);
        var exit = AddBox("Exit", new Vector2(0, 0), new Vector2(1, 2), trigger: true).AddComponent<Door>();
        exit.Scene = "Level2";
        exit.Fade = 0;
        AddPlayer(new Vector2(0, 0));

        Run(3 * Step);

        Assert.Same(next, _game.Scene);
    }

    [Fact]
    public void Door_WithATarget_Teleports_WithoutBouncingStraightBack()
    {
        var a = AddBox("DoorA", new Vector2(0, 0), new Vector2(1, 2), trigger: true).AddComponent<Door>();
        a.Target = "DoorB";
        a.Fade = 0;
        var b = AddBox("DoorB", new Vector2(20, 0), new Vector2(1, 2), trigger: true).AddComponent<Door>();
        b.Target = "DoorA";
        b.Fade = 0;
        var player = AddPlayer(new Vector2(0, 0));
        var uses = 0;
        a.Used += _ => uses++;
        b.Used += _ => uses++;

        Run(0.5f);

        Assert.Equal(new Vector2(20, 0), Position(player), new Vector2Comparer(0.01f));
        Assert.Equal(1, uses);

        // Step off and back on: now it goes back.
        player.Transform.Position = new Vector3(23, 0, 0);
        Run(3 * Step);
        player.Transform.Position = new Vector3(20, 0, 0);
        Run(3 * Step);
        Assert.Equal(new Vector2(0, 0), Position(player), new Vector2Comparer(0.01f));
    }

    [Fact]
    public void Door_WithAFade_TeleportsWhileTheScreenIsCovered()
    {
        var door = AddBox("Door", new Vector2(0, 0), new Vector2(1, 2), trigger: true).AddComponent<Door>();
        door.Target = "Exit";
        door.Fade = 0.2f;
        Add("Exit", new Vector2(10, 0));
        var player = AddPlayer(new Vector2(0, 0));

        Run(0.1f);
        Assert.Equal(0, Position(player).X, 0.01f);
        Assert.InRange(_game.FadeAmount, 0.1f, 0.9f);
        Assert.False(_game.IsChangingScene);

        Run(0.15f);
        Assert.Equal(10, Position(player).X, 0.01f);
        Run(0.3f);
        Assert.Equal(0, _game.FadeAmount);
    }

    [Fact]
    public void Door_NeedsInteract_WaitsForThePress_AndShowsItsPrompt()
    {
        var door = AddBox("Door", new Vector2(0, 0), new Vector2(1, 2), trigger: true);
        var prompt = Add("Prompt", new Vector2(0, 1.5f), door);
        var component = door.AddComponent<Door>();
        component.Target = "Exit";
        component.Fade = 0;
        component.NeedsInteract = true;
        component.Prompt = "Prompt";
        Add("Exit", new Vector2(10, 0));
        var player = AddPlayer(new Vector2(-5, 0));

        Run(3 * Step);
        Assert.False(prompt.IsEnabled);

        player.Transform.Position = Vector3.Zero;
        Run(0.2f);
        Assert.True(prompt.IsEnabled);
        Assert.Equal(0, Position(player).X, 0.01f);

        Press(Key.E);
        Assert.Equal(10, Position(player).X, 0.01f);
        Run(3 * Step);
        Assert.False(prompt.IsEnabled);
    }

    [Fact]
    public void FadeThrough_CallsItsAction_OnceTheScreenIsCovered()
    {
        var called = 0f;
        _game.FadeThrough(0.1f, () => called = _game.FadeAmount);

        Run(0.05f);
        Assert.Equal(0, called);
        Run(0.1f);
        Assert.Equal(1, called);
    }

    // ---- Dialogue ----

    private (Dialogue Dialogue, Entity Box, Text Text) AddTalker(Vector2 position, params string[] lines)
    {
        var box = Add("Dialogue Box", new Vector2(0, -4));
        var label = Add("Label", Vector2.Zero, box);
        var text = label.AddComponent<Text>();
        var npc = AddBox("Npc", position, new Vector2(1, 1), trigger: true);
        var dialogue = npc.AddComponent<Dialogue>();
        dialogue.Lines = lines;
        return (dialogue, box, text);
    }

    [Fact]
    public void Dialogue_StartsOnInteract_ShowsEachLine_AndHidesTheBoxAfter()
    {
        var (dialogue, box, text) = AddTalker(Vector2.Zero, "Hi.", "Bye.");
        dialogue.Message = "Talked";
        var heard = 0;
        _game.On("Talked", _ => heard++);
        AddPlayer(Vector2.Zero);

        Run(0.1f);
        Assert.False(box.IsEnabled);
        Assert.False(dialogue.IsTalking);

        Press(Key.E);
        Assert.True(dialogue.IsTalking);
        Assert.Same(dialogue, _game.Talking);
        Assert.True(box.IsEnabled);
        Assert.Equal("Hi.", text.Value);
        Assert.Equal(0, _game.Time.Scale);

        Press(Key.E);
        Assert.Equal("Bye.", text.Value);

        Press(Key.E);
        Assert.False(dialogue.IsTalking);
        Assert.True(dialogue.IsDone);
        Assert.False(box.IsEnabled);
        Assert.Equal(1, heard);
        Assert.Equal(1, _game.Time.Scale);

        // The press that ended it doesn't start it again.
        Run(0.1f);
        Assert.False(dialogue.IsTalking);
    }

    [Fact]
    public void Dialogue_Interact_FinishesTypingTheLine_BeforeMovingOn()
    {
        var (dialogue, _, text) = AddTalker(Vector2.Zero, "A long line that takes a while.", "Next.");
        text.Entity!.AddComponent<Typewriter>().Speed = 5;
        AddPlayer(Vector2.Zero);
        Run(0.1f);

        Press(Key.E);
        var typewriter = text.Entity.GetComponent<Typewriter>()!;
        Assert.False(typewriter.IsDone);

        Press(Key.E);
        Assert.True(typewriter.IsDone);
        Assert.Equal(0, dialogue.Line);

        Press(Key.E);
        Assert.Equal(1, dialogue.Line);
        Assert.Equal("Next.", text.Value);
    }

    [Fact]
    public void Dialogue_OnTouch_StartsByItself_OnceEachTimeItsTouched()
    {
        var (dialogue, _, _) = AddTalker(Vector2.Zero, "Danger ahead!");
        dialogue.Start = DialogueStart.Touch;
        dialogue.PauseGame = false;
        var player = AddPlayer(new Vector2(-5, 0));
        var started = 0;
        dialogue.LineShown += _ => started++;

        player.Transform.Position = Vector3.Zero;
        Run(0.1f);
        Assert.Equal(1, started);

        Press(Key.E);
        Assert.False(dialogue.IsTalking);
        Run(0.2f);
        Assert.Equal(1, started); // still standing in it

        player.Transform.Position = new Vector3(-5, 0, 0);
        Run(0.1f);
        player.Transform.Position = Vector3.Zero;
        Run(0.1f);
        Assert.Equal(2, started);
    }

    [Fact]
    public void Dialogue_Once_IsOnlySaidOnce()
    {
        var (dialogue, _, _) = AddTalker(Vector2.Zero, "Once upon a time.");
        dialogue.Once = true;
        AddPlayer(Vector2.Zero);
        Run(0.1f);

        Press(Key.E);
        Press(Key.E);
        Assert.True(dialogue.IsDone);

        Run(0.1f);
        Press(Key.E);
        Assert.False(dialogue.IsTalking);
    }

    [Fact]
    public void StartDialogue_FromAScript_StartsItWithNobodyTouching_ButNotOverAnother()
    {
        var (first, box, text) = AddTalker(new Vector2(30, 0), "A cutscene line.");
        var second = AddBox("Other", new Vector2(60, 0), Vector2.One, trigger: true).AddComponent<Dialogue>();
        second.Lines = ["Me too!"];
        Run(Step);

        _game.StartDialogue(first);
        _game.StartDialogue(second);
        Run(Step);

        Assert.Same(first, _game.Talking);
        Assert.True(box.IsEnabled);
        Assert.Equal("A cutscene line.", text.Value);
    }

    // ---- Saving ----

    [Fact]
    public void LevelComponents_RoundTripThroughTheSceneFile()
    {
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var scene = new Scene();
        var entity = new Entity("Everything");
        entity.AddComponent<Mover>().Points = "1 2; 3 4";
        entity.AddComponent<Patrol>().GoingLeft = true;
        entity.AddComponent<Chase>().Around = "Level";
        entity.AddComponent<Shooter>().Count = 5;
        entity.AddComponent<Checkpoint>().Active = true;
        entity.AddComponent<Door>().Scene = "Level2";
        entity.AddComponent<Dialogue>().Lines = ["One.", "Two."];
        var health = entity.AddComponent<Health>();
        health.Respawn = true;
        scene.Add(entity);

        var loaded = serializer.LoadFromString(serializer.SaveToString(scene)).Roots.Single();

        Assert.Equal([new Vector2(1, 2), new Vector2(3, 4)], loaded.GetComponent<Mover>()!.Path.Skip(1));
        Assert.True(loaded.GetComponent<Patrol>()!.GoingLeft);
        Assert.Equal("Level", loaded.GetComponent<Chase>()!.Around);
        Assert.Equal(5, loaded.GetComponent<Shooter>()!.Count);
        Assert.True(loaded.GetComponent<Checkpoint>()!.Active);
        Assert.Equal("Level2", loaded.GetComponent<Door>()!.Scene);
        Assert.Equal(["One.", "Two."], loaded.GetComponent<Dialogue>()!.Lines);
        Assert.True(loaded.GetComponent<Health>()!.Respawn);
    }

    private sealed class Vector2Comparer(float tolerance) : IEqualityComparer<Vector2>
    {
        public bool Equals(Vector2 a, Vector2 b) => Vector2.Distance(a, b) <= tolerance;

        public int GetHashCode(Vector2 v) => 0;
    }
}
