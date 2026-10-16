using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.PhysicsGames;

/// <summary>
/// Physics puzzlers: a <see cref="Slingshot"/> to pull back and launch shots from, and <see cref="Breakable"/> things (planks,
/// panes, blocks, pigs) that crack and break on hard knocks; a <see cref="LevelGoal"/> that wins a level once its targets
/// are down (with a bonus for the shots left, and one to three stars) or loses it once the shots run out; a Slingshot and a
/// Tower to make from the Entity menu (Genres, Physics Games); and the Physics template, levels of wood, glass and stone to
/// knock down. And pinball: <see cref="Flipper"/>s, <see cref="Bumper"/>s and kickers, a <see cref="Plunger"/>, a
/// <see cref="Tilt"/> to nudge, <see cref="Target"/>s and <see cref="Lane"/>s that score, and a <see cref="Drain"/> that loses,
/// saves and serves balls, with a Pinball Table and each piece to make from the Entity menu, and the Pinball template, a
/// whole table.
/// </summary>
public sealed class PhysicsGamesModule : GameModule
{
    public override string Name => "Physics Games";

    protected override void AddTo(GameSetup game)
    {
        // Slingshots after scripts and dragging (so a launch a script asks for flies this step), before physics; breaking
        // straight after physics, from the knocks it found.
        var slingshots = game.AddSystem(new SlingshotSystem());
        game.StepAfter(Steps.Dragging, "Slingshots", slingshots, slingshots.Run);
        var breakables = game.AddSystem(new BreakablesSystem());
        var breakableStep = game.StepAfter(Steps.Physics, "Breakables", breakables, breakables.Run);
        var goals = game.AddSystem(new LevelGoalSystem());
        game.StepAfter(breakableStep, "Level Goals", goals, goals.Run);
        game.OnNetCommand(SlingshotSystem.Command, slingshots.RunCommand);

        // Pinball: the tilt and the flippers before physics (so a flipper swings this step); bumpers, targets, lanes, the
        // plunger and the drain after it, from what it found touching.
        var pinball = game.AddSystem(new PinballSystem());
        game.StepAfter(Steps.Dragging, "Flippers", pinball, pinball.RunControls);
        game.StepAfter(Steps.Physics, "Pinball", pinball, pinball.RunHits);
    }

    // With bodies that turn: a tower of posts and planks, two storeys, asleep, that break, and a heavy ball thrown through it.
    public override void AddToWarmUp(Scene scene, bool turning)
    {
        if (!turning)
            return;
        for (var i = 0; i < 6; i++)
        {
            var (storey, part) = (i / 3, i % 3);
            var (at, size) = part switch
            {
                0 => (new Vector2(-1.1f, storey * 2.35f + 1), new Vector2(0.35f, 2)),
                1 => (new Vector2(1.1f, storey * 2.35f + 1), new Vector2(0.35f, 2)),
                _ => (new Vector2(0, storey * 2.35f + 2.175f), new Vector2(3, 0.35f)),
            };
            var entity = Add(scene, part == 2 ? "Plank" : "Post", at);
            entity.AddComponent<Sprite>().Size = size;
            entity.AddComponent<BoxCollider>().Size = size;
            var body = entity.AddComponent<Body>();
            (body.Rotates, body.Asleep, body.Mass) = (true, true, size.X * size.Y);
            var piece = entity.AddComponent<Breakable>();
            (piece.Strength, piece.MinHit) = (6, 1.5f);
        }

        var ball = Add(scene, "Ball", new Vector2(-3, 2));
        ball.AddComponent<CircleCollider>().Radius = 0.6f;
        ball.AddComponent<Sprite>().Size = new Vector2(1.2f, 1.2f);
        var thrown = ball.AddComponent<Body>();
        (thrown.Rotates, thrown.Mass, thrown.Velocity) = (true, 40, new Vector2(30, 2));

        static Entity Add(Scene scene, string name, Vector2 at)
        {
            var entity = new Entity(name);
            entity.Transform.Position = new Vector3(at, 0);
            scene.Add(entity);
            return entity;
        }
    }
}
