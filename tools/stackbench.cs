#:project ../src/Griff/Griff.csproj
#:property PublishAot=false
#:property TieredCompilation=false

// StackBench: what bodies that rotate cost a physics step when they're stacked up, measured in Release with nothing on the
// screen. Builds a pyramid of crates that rotate (and, with --tower, a tower of ten beside it) on a floor, lets them settle,
// then times steps of the pile awake (woken before each step, so every crate and contact is worked out, as while a pile
// settles or is knocked about) and asleep, printing the median, mean and worst step and the bytes allocated meanwhile.
// The numbers are this computer's: compare runs on the same one.
//   dotnet run -c Release tools/stackbench.cs -- [--rows 10] [--steps 600] [--tower]
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
for (var row = 0; row < rows; row++)
{
    for (var i = 0; i < rows - row; i++)
        bodies.Add(Crate(new Vector2((i - (rows - row - 1) / 2f) * 1.01f, 0.5f + row * 1.01f)));
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

Console.WriteLine($"{bodies.Count} crates that rotate ({rows} rows{(args.Contains("--tower") ? " and a tower of ten" : "")}); {settled} asleep after 3 s");
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
