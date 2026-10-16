#:project ../src/Griff/Griff.csproj
#:project ../src/Griff.Editing/Griff.Editing.csproj
#:project ../src/Modules/Griff.Modules.csproj
#:property PublishAot=false
#:property TieredCompilation=false

// StackBench: what bodies that rotate cost a physics step when they're stacked up, measured in Release with nothing on the
// screen. Builds a pyramid of crates that rotate (and, with --tower, a tower of ten beside it; with --balls 60, that many
// balls heaped in a bin instead; with --shapes 60, that many polygons, triangles, pentagons, hexagons and concave Ls, heaped
// in a bin; with --hinges 60, that many planks hinged into chains of five hanging from the ceiling, let go from the side so
// they swing; with --welded 60, that many crates welded in pairs into Ls, heaped in a bin) on a floor, lets them settle,
// then times steps of the pile awake (woken before each step, so every body and contact is worked out, as while a pile
// settles or is knocked about) and asleep. With --ropes 40, that many ropes 3 long instead, swinging down from hooks over
// pegs, every other one with a crate on its end, timed as they swing from the start and again ten seconds on. With
// --breakable 150, that many Breakable posts and planks stacked into towers five storeys high, asleep, and a heavy ball
// thrown through them, timed step by step as they crack, break into pieces and collapse (once untimed first, so nothing is
// compiled while it's timed); with --sturdy too, too strong to break, so they only crack and fall (a big collapse, nothing
// added to the scene). With --pinball 20, that many pinball tables (Create Pinball Table) side by side, each with its ball
// shot from the plunger at full power and two more dropped in from the top, the flippers flipping now and then, timed as
// they play, and watched for a ball that gets out of its table (through a wall or a flipper) and for the fastest one. It prints the median, mean and worst step and the bytes allocated meanwhile (for a collapse, the
// scene's first step, which sets everything up, apart from the rest). The numbers are this computer's: compare runs on the
// same one.
//   dotnet run -c Release tools/stackbench.cs -- [--rows 10] [--steps 600] [--tower] [--balls 60] [--shapes 60] [--hinges 60]
//     [--welded 60] [--ropes 40] [--breakable 150 [--sturdy]] [--pinball 20]
using System.Diagnostics;
using System.Numerics;
using Griff.Input;
using Griff.Modules.Contraptions;
using Griff.Modules.PhysicsGames;
using Griff.Runtime;
using Griff.Scenes;

var rows = Arg("--rows", 10);
var steps = Arg("--steps", 600);
const float Frame = 1 / 60f;

var scene = new Scene();
var game = new Game(scene, new InputState());
var floor = new Entity("Floor");
floor.Transform.Position = new Vector3(0, -0.5f, 0);
floor.AddComponent<BoxCollider>().Size = new Vector2(200, 1);
scene.Add(floor);
var bodies = new List<Body>();
var balls = Arg("--balls", 0);
var shapes = Arg("--shapes", 0);
var hinges = Arg("--hinges", 0);
var welded = Arg("--welded", 0);
var ropes = Arg("--ropes", 0);
if (ropes > 0)
{
    // Ropes 3 long from hooks along the ceiling, laid out level so they swing down: every other one with a crate on its end,
    // the rest loose, each over a peg below its hook it drapes over and wraps round.
    for (var i = 0; i < ropes; i++)
    {
        var at = new Vector2(i * 4 - ropes * 2, 12);
        var hook = new Entity($"Hook {i}");
        hook.Transform.Position = new Vector3(at, 0);
        scene.Add(hook);
        var peg = new Entity("Peg");
        peg.Transform.Position = new Vector3(at + new Vector2(1.2f, -1.8f), 0);
        peg.AddComponent<BoxCollider>().Size = new Vector2(0.3f, 0.3f);
        scene.Add(peg);
        if (i % 2 == 0)
        {
            var body = Crate(at + new Vector2(3, 0), new Vector2(0.6f, 0.6f));
            var rope = body.Entity!.AddComponent<Rope>();
            rope.To = hook.Name;
            bodies.Add(body);
        }
        else
        {
            var rope = hook.AddComponent<Rope>();
            (rope.LooseEnd, rope.OtherAnchor) = (true, at + new Vector2(3, 0));
        }
    }

    var swinging = Time(() => game.Tick(Frame));
    for (var i = 0; i < 600; i++)
        game.Tick(Frame);
    var hanging = Time(() => game.Tick(Frame));
    Console.WriteLine($"{ropes} ropes 3 long ({ropes / 2 + ropes % 2} with a crate on the end) swinging over pegs");
    Console.WriteLine($"swinging: {swinging}");
    Console.WriteLine($"later:    {hanging}");
    return;
}

var pinball = Arg("--pinball", 0);
if (pinball > 0)
{
    // Tables 12 apart, each with three balls: the one on its plunger, and two dropped in over the bumpers.
    (Scene Scene, Game Game, List<Entity> Balls) Tables()
    {
        var tables = new Scene();
        var preset = Griff.Editing.EntityPreset.All.Single(p => p.Id == "Entity.CreatePinballTable");
        var balls = new List<Entity>();
        for (var i = 0; i < pinball; i++)
        {
            var table = new Entity(preset.EntityName(tables));
            preset.Configure(table, tables);
            var parts = preset.CreateChildren(tables).ToList();
            table.Transform.Position = new Vector3(i * 12, 0, 0);
            tables.Add(table);
            foreach (var part in parts)
                tables.Add(part, table);
            var ball = table.Children.Single(e => e.Name == "Ball");
            balls.Add(ball);
            for (var extra = 0; extra < 2; extra++)
            {
                var more = new Entity("Ball");
                more.Transform.Position = new Vector3(-2 + extra * 2.5f, 6.5f, 0);
                more.AddComponent<CircleCollider>().Radius = 0.3f;
                var body = more.AddComponent<Body>();
                (body.Rotates, body.Bounciness, body.RollingResistance, body.Grip, body.Velocity) = (true, 0.3f, 0.02f, 0.3f, new Vector2(extra == 0 ? 3 : -4, 0));
                tables.Add(more, table);
                balls.Add(more);
            }
        }

        return (tables, new Game(tables, new InputState()), balls);
    }

    // Shot at full power a moment in; each table's flippers flipped every so often, the tables out of step.
    void Play(Game played, Scene tables, int step)
    {
        if (step == 10)
        {
            foreach (var plunger in tables.All<Plunger>())
                played.Do(plunger.Entity!, "Launch");
        }

        foreach (var flipper in tables.All<Flipper>())
        {
            var table = flipper.Entity!.Parent!;
            if ((step + (int)(table.Transform.Position.X / 12) * 7) % 45 == 0)
                played.Do(flipper.Entity!, "Flip");
        }
    }

    var (warmScene, warm, _) = Tables();
    for (var i = 0; i < steps; i++)
    {
        Play(warm, warmScene, i);
        warm.Tick(Frame);
    }

    var (timedScene, playing, played) = Tables();
    var times = new double[steps];
    var (fastest, escaped, allocatedAll, firstStep) = (0f, new HashSet<Entity>(), 0L, 0L);
    for (var i = 0; i < steps; i++)
    {
        Play(playing, timedScene, i);
        var start = Stopwatch.GetTimestamp();
        var before = GC.GetAllocatedBytesForCurrentThread();
        playing.Tick(Frame);
        var made = GC.GetAllocatedBytesForCurrentThread() - before;
        if (i == 0)
            firstStep = made;
        else
            allocatedAll += made;
        times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        foreach (var ball in played)
        {
            if (ball.Scene is null || ball.Parent is null || !ball.IsActive || ball.GetComponent<Body>() is not { } body)
                continue;
            fastest = MathF.Max(fastest, body.Velocity.Length());
            var at = ball.Transform.WorldPosition - ball.Parent!.Transform.WorldPosition;
            if (MathF.Abs(at.X) > 5.3f || at.Y > 9.3f || at.Y < -10.7f)
                escaped.Add(ball);
        }
    }

    var allocated = allocatedAll / Math.Max(1, steps - 1);
    Array.Sort(times);
    var score = timedScene.All<Bumper>().Sum(b => b.Hits);
    Console.WriteLine($"{pinball} pinball tables, three balls each, shot and flipped: fastest ball {fastest:0.0} units a second, " +
        $"{escaped.Count} got out of their table, {score} bumper hits, {timedScene.All<Drain>().Sum(d => d.Ball - 1)} balls lost");
    Console.WriteLine($"play: median {times[steps / 2]:0.000} ms, mean {times.Average():0.000} ms, worst {times[^1]:0.000} ms a step, " +
        $"{allocated} bytes a step after the first (taking balls lost off the table and serving them), {firstStep / 1024} KB the first");
    return;
}

var breakable = Arg("--breakable", 0);
var sturdy = args.Contains("--sturdy");
if (breakable > 0)
{
    // Towers of posts and planks (three a storey, five storeys a tower) side by side, asleep, and a heavy ball thrown low
    // through them all: what a big collapse costs, from the first knock to the pieces settling.
    (Scene Scene, Game Game, List<Breakable> Pieces) Towers()
    {
        var towers = new Scene();
        var floor = new Entity("Floor");
        floor.Transform.Position = new Vector3(0, -0.5f, 0);
        floor.AddComponent<BoxCollider>().Size = new Vector2(400, 1);
        towers.Add(floor);
        var pieces = new List<Breakable>();
        for (var i = 0; i < breakable; i++)
        {
            var (tower, storey, part) = (i / 15, i / 3 % 5, i % 3);
            var (x, y) = (tower * 4f, storey * 2.35f);
            var (at, size) = part switch
            {
                0 => (new Vector2(x - 1.1f, y + 1), new Vector2(0.35f, 2)),
                1 => (new Vector2(x + 1.1f, y + 1), new Vector2(0.35f, 2)),
                _ => (new Vector2(x, y + 2.175f), new Vector2(3, 0.35f)),
            };
            var entity = new Entity(part == 2 ? "Plank" : "Post");
            entity.Transform.Position = new Vector3(at, 0);
            entity.AddComponent<Sprite>().Size = size;
            entity.AddComponent<BoxCollider>().Size = size;
            var body = entity.AddComponent<Body>();
            (body.Rotates, body.Asleep, body.Mass) = (true, true, size.X * size.Y);
            var piece = entity.AddComponent<Breakable>();
            (piece.Strength, piece.MinHit) = (sturdy ? float.MaxValue : 6, 1.5f);
            towers.Add(entity);
            pieces.Add(piece);
        }

        var ball = new Entity("Ball");
        ball.Transform.Position = new Vector3(-6, 3, 0);
        ball.AddComponent<CircleCollider>().Radius = 0.6f;
        var thrown = ball.AddComponent<Body>();
        (thrown.Rotates, thrown.Mass, thrown.Velocity) = (true, 40, new Vector2(30, 2));
        towers.Add(ball);
        return (towers, new Game(towers, new InputState()), pieces);
    }

    var (_, warm, _) = Towers();
    for (var i = 0; i < steps; i++)
        warm.Tick(Frame);

    var (timed, collapsing, all) = Towers();
    var times = new double[steps];
    var (most, first, allocated) = (0, 0L, 0L);
    for (var i = 0; i < steps; i++)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        collapsing.Tick(Frame);
        times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (i == 0)
            first = bytes;
        else
            allocated += bytes;
        most = Math.Max(most, timed.All<Body>().Count);
    }

    Array.Sort(times);
    Console.WriteLine($"{breakable} {(sturdy ? "sturdy" : "breakable")} posts and planks in towers of five storeys, a heavy ball thrown through them: " +
        $"{all.Count(b => b.Broken)} broke, {all.Count(b => !b.Broken && b.Damage > 0)} cracked, up to {most} bodies at once");
    Console.WriteLine($"collapse: median {times[steps / 2]:0.000} ms, mean {times.Average():0.000} ms, worst {times[^1]:0.000} ms a step; " +
        $"the scene's first step {first} bytes, then {allocated / (steps - 1)} bytes a step{(sturdy ? "" : " (the pieces things break into)")}");
    return;
}

if (hinges > 0)
{
    // Chains of five planks, each hinged by its left end to the right end of the one before, the first to the ceiling; laid
    // out level, so they swing down.
    for (var chain = 0; chain < hinges / 5; chain++)
    {
        for (var link = 0; link < 5; link++)
        {
            var body = Crate(new Vector2(chain * 7 - hinges / 5 * 3.5f + link + 0.5f, 12), new Vector2(1, 0.2f));
            var entity = body.Entity!;
            entity.Name = $"Link {chain} {link}";
            var hinge = entity.AddComponent<HingeJoint>();
            (hinge.Anchor, hinge.To, hinge.Friction) = (new Vector2(-0.5f, 0), link == 0 ? null : $"Link {chain} {link - 1}", 0.5f);
            bodies.Add(body);
        }
    }
}
else if (balls > 0 || shapes > 0 || welded > 0)
{
    // A bin ten balls (or shapes) wide, filled a row of ten (a little off true, so they tumble) at a time.
    var count = Math.Max(Math.Max(balls, shapes), welded);
    foreach (var x in new[] { -5.5f, 5.5f })
    {
        var side = new Entity("Side");
        side.Transform.Position = new Vector3(x, count / 10f + 2, 0);
        side.AddComponent<BoxCollider>().Size = new Vector2(1, count / 5f + 4);
        scene.Add(side);
    }

    for (var i = 0; i < count; i++)
    {
        var at = new Vector2(-4.5f + i % 10 + (i / 10 % 2) * 0.3f, 0.5f + i / 10 * 1.1f);
        bodies.Add(balls > 0 ? Ball(at) : shapes > 0 ? Shape(at, i) : Welded(at, i));
    }
}
else
{
    for (var row = 0; row < rows; row++)
    {
        for (var i = 0; i < rows - row; i++)
            bodies.Add(Crate(new Vector2((i - (rows - row - 1) / 2f) * 1.01f, 0.5f + row * 1.01f)));
    }
}

if (args.Contains("--tower"))
{
    for (var i = 0; i < 10; i++)
        bodies.Add(Crate(new Vector2(rows + 5, 0.5f + i * 1.01f)));
}

for (var i = 0; i < 180; i++)
    game.Tick(Frame);
var settled = bodies.Count(b => b.Asleep);

var awake = Time(() =>
{
    foreach (var body in bodies)
        body.Asleep = false;
    game.Tick(Frame);
});
for (var i = 0; i < 120; i++)
    game.Tick(Frame);
var asleep = Time(() => game.Tick(Frame));

Console.WriteLine(hinges > 0
    ? $"{hinges} planks that rotate hinged in chains of five; {settled} asleep after 3 s"
    : welded > 0
    ? $"{welded} crates that rotate welded in pairs in a bin; {settled} asleep after 3 s"
    : shapes > 0
    ? $"{shapes} polygons that rotate in a bin{(args.Contains("--tower") ? " and a tower of ten crates" : "")}; {settled} asleep after 3 s"
    : balls > 0
    ? $"{balls} balls that rotate in a bin{(args.Contains("--tower") ? " and a tower of ten crates" : "")}; {settled} asleep after 3 s"
    : $"{bodies.Count} crates that rotate ({rows} rows{(args.Contains("--tower") ? " and a tower of ten" : "")}); {settled} asleep after 3 s");
Console.WriteLine($"awake:  {awake}");
Console.WriteLine($"asleep: {asleep}");
return;

Body Crate(Vector2 at, Vector2? size = null)
{
    var entity = new Entity("Crate");
    entity.Transform.Position = new Vector3(at, 0);
    entity.AddComponent<BoxCollider>().Size = size ?? Vector2.One;
    var body = entity.AddComponent<Body>();
    body.Rotates = true;
    scene.Add(entity);
    return body;
}

Body Ball(Vector2 at)
{
    var entity = new Entity("Ball");
    entity.Transform.Position = new Vector3(at, 0);
    entity.AddComponent<CircleCollider>().Radius = 0.45f;
    var body = entity.AddComponent<Body>();
    body.Rotates = true;
    scene.Add(entity);
    return body;
}

// A crate 0.9 wide, every other one welded to the one before as the foot of an L (a little smaller, beside its bottom).
Body Welded(Vector2 at, int i)
{
    var body = Crate(at, i % 2 == 0 ? new Vector2(0.9f, 0.9f) : new Vector2(0.45f, 0.45f));
    body.Entity!.Name = $"Crate {i}";
    if (i % 2 == 1)
    {
        body.Entity.Transform.Position = new Vector3(at - new Vector2(1 - 0.675f, 0.225f), 0);
        body.Entity.AddComponent<Weld>().To = $"Crate {i - 1}";
    }

    return body;
}

// One of four polygons in turn: a triangle, a pentagon, a hexagon or an L (concave: two convex pieces), about 0.9 across.
Body Shape(Vector2 at, int i)
{
    var entity = new Entity("Shape");
    entity.Transform.Position = new Vector3(at, 0);
    entity.Transform.Rotation = new Vector3(0, 0, i * 37 % 360);
    entity.AddComponent<PolygonCollider>().Points = (i % 4) switch
    {
        0 => Regular(3, 0.5f),
        1 => Regular(5, 0.45f),
        2 => Regular(6, 0.45f),
        _ => "-0.45 -0.45; 0.45 -0.45; 0.45 -0.1; -0.1 -0.1; -0.1 0.45; -0.45 0.45",
    };
    var body = entity.AddComponent<Body>();
    body.Rotates = true;
    scene.Add(entity);
    return body;
}

static string Regular(int sides, float radius) => string.Join("; ", Enumerable.Range(0, sides).Select(i =>
    FormattableString.Invariant($"{radius * MathF.Cos(MathF.Tau * i / sides):0.###} {radius * MathF.Sin(MathF.Tau * i / sides):0.###}")));

string Time(Action step)
{
    for (var i = 0; i < 60; i++)
        step();
    var times = new double[steps];
    var before = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < steps; i++)
    {
        var start = Stopwatch.GetTimestamp();
        step();
        times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    var allocated = (GC.GetAllocatedBytesForCurrentThread() - before) / steps;
    Array.Sort(times);
    return $"median {times[steps / 2]:0.000} ms, mean {times.Average():0.000} ms, worst {times[^1]:0.000} ms a step, {allocated} bytes a step";
}

int Arg(string name, int fallback)
{
    var at = Array.IndexOf(args, name);
    return at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out var value) ? value : fallback;
}
