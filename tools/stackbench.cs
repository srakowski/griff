#:project ../src/Griff/Griff.csproj
#:property PublishAot=false
#:property TieredCompilation=false

// StackBench: what bodies that rotate cost a physics step when they're stacked up, measured in Release with nothing on the
// screen. Builds a pyramid of crates that rotate (and, with --tower, a tower of ten beside it; with --balls 60, that many
// balls heaped in a bin instead; with --shapes 60, that many polygons, triangles, pentagons, hexagons and concave Ls, heaped
// in a bin) on a floor, lets them settle, then times steps of the pile awake (woken before each step,
// so every body and contact is worked out, as while a pile settles or is knocked about) and asleep, printing the median,
// mean and worst step and the bytes allocated meanwhile. The numbers are this computer's: compare runs on the same one.
//   dotnet run -c Release tools/stackbench.cs -- [--rows 10] [--steps 600] [--tower] [--balls 60] [--shapes 60]
using System.Diagnostics;
using System.Numerics;
using Griff.Input;
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
if (balls > 0 || shapes > 0)
{
    // A bin ten balls (or shapes) wide, filled a row of ten (a little off true, so they tumble) at a time.
    var count = Math.Max(balls, shapes);
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
        bodies.Add(balls > 0 ? Ball(at) : Shape(at, i));
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

Console.WriteLine(shapes > 0
    ? $"{shapes} polygons that rotate in a bin{(args.Contains("--tower") ? " and a tower of ten crates" : "")}; {settled} asleep after 3 s"
    : balls > 0
    ? $"{balls} balls that rotate in a bin{(args.Contains("--tower") ? " and a tower of ten crates" : "")}; {settled} asleep after 3 s"
    : $"{bodies.Count} crates that rotate ({rows} rows{(args.Contains("--tower") ? " and a tower of ten" : "")}); {settled} asleep after 3 s");
Console.WriteLine($"awake:  {awake}");
Console.WriteLine($"asleep: {asleep}");
return;

Body Crate(Vector2 at)
{
    var entity = new Entity("Crate");
    entity.Transform.Position = new Vector3(at, 0);
    entity.AddComponent<BoxCollider>();
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
