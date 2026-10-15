using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using Griff.Debugging;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scripting;

namespace Griff.Tests.Scripting;

/// <summary>
/// The editor's build of the scripts has guards: filters that catch nothing, around each stretch of a method's code, so an
/// exception thrown there is noted by the innermost guard around the line that threw, with the values in scope there, while
/// nothing has unwound yet. The scripts do what they did without them; nothing is noted unless exceptions are watched.
/// </summary>
[Collection(nameof(BreakpointProbeTests))]
public sealed class ExceptionGuardTests : IDisposable
{
    private const string Path = @"C:\Game\Assets\Scripts\Thrower.cs";

    private const string Source = """
        public class Thrower
        {
            public int Field = 7;

            public int Locals(int n)
            {
                var a = n + 1;
                var list = new List<int> { a };
                if (n > 0)
                {
                    var inner = a * 2;
                    return list[inner];
                }

                return 0;
            }

            public int InLambda(int n)
            {
                Func<int, int> f = x =>
                {
                    var y = x * 3;
                    return y / (x - x);
                };
                return f(n);
            }

            public string Arrow(string? s) => s!.ToUpperInvariant();

            public int Caught()
            {
                var r = 0;
                try
                {
                    r = Locals(5);
                }
                catch (ArgumentOutOfRangeException)
                {
                    r = -1;
                }

                return r;
            }

            public int Local(int n)
            {
                var before = Helper(n);
                var after = before + 1;
                return after / (n - n);

                int Helper(int h) => h * 2;
            }

            public int Captures(int n)
            {
                var k = n + 4;
                var twice = Twice();
                return twice / (n - n);

                int Twice() => k * 2;
            }

            public int Labels(int n)
            {
                var i = 0;
                again:
                i++;
                if (i < n)
                    goto again;
                var done = i;
                return done / (n - n);
            }

            public IEnumerable<int> Iterate(object? o)
            {
                var count = 2;
                yield return count;
                var text = o!.ToString();
                yield return text!.Length;
            }

            public IEnumerable<int> IterateMore(int n)
            {
                var (a, b) = (n, n + 1);
                yield return a;
                Field = b / (n - n);
                yield return Field;
            }
        }
        """;

    private bool _watching = Watch();

    private static bool Watch()
    {
        ScriptProbes.WatchThrows(true);
        return true;
    }

    private void StopWatching()
    {
        if (_watching)
            ScriptProbes.WatchThrows(false);
        _watching = false;
    }

    public void Dispose() => StopWatching();

    private static (Assembly Assembly, ProbeTable Probes) Build()
    {
        var compiled = ScriptCompiler.Compile([new ScriptSource(Path, Source)], probes: true);
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var assembly = new AssemblyLoadContext("GuardTest", isCollectible: true).LoadFromStream(new MemoryStream(compiled.Assembly!), new MemoryStream(compiled.Symbols!));
        compiled.Probes.Attach(assembly);
        return (assembly, compiled.Probes);
    }

    private static int LineOf(string text) => Source[..Source.IndexOf(text, StringComparison.Ordinal)].Count(c => c == '\n') + 1;

    // Calls a Thrower method, returning what it threw (unwrapped).
    private static Exception Throws(Assembly assembly, string method, params object?[] args)
    {
        var type = assembly.GetType("Thrower")!;
        var target = Activator.CreateInstance(type);
        try
        {
            var result = type.GetMethod(method)!.Invoke(target, args);
            if (result is IEnumerable sequence)
            {
                foreach (var _ in sequence)
                {
                }
            }
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            return ex.InnerException;
        }
        catch (Exception ex)
        {
            return ex;
        }

        throw new InvalidOperationException($"{method} didn't throw.");
    }

    // The innermost line of the scripts it came up through, and that frame's method.
    private static (int Line, MethodBase? Method) Innermost(Exception exception, Assembly scripts)
    {
        var frame = new StackTrace(exception, true).GetFrames().First(f => f.GetMethod()?.Module.Assembly == scripts);
        return (frame.GetFileLineNumber(), frame.GetMethod());
    }

    // The guard that noted it: the innermost frame's, with its names and values.
    private static Dictionary<string, object?> Noted(Exception exception, Assembly scripts, ProbeTable probes, out ScriptGuard guard)
    {
        var thrown = ScriptProbes.ThrownAt(exception);
        Assert.NotNull(thrown);
        Assert.Equal(probes.Switches, thrown.Probes);
        Assert.Equal(Innermost(exception, scripts).Method, thrown.Method);
        guard = probes.Guard(thrown.Guard)!;
        Assert.NotNull(guard);
        var values = new Dictionary<string, object?>();
        for (var i = 0; i < guard.Names.Count; i++)
            values[guard.Names[i]] = thrown.Values[i];
        if (guard.HasThis)
            values["this"] = thrown.This;
        return values;
    }

    [Fact]
    public void The_innermost_guard_notes_the_values_in_scope_where_it_was_thrown_and_the_line_stays_as_written()
    {
        var (assembly, probes) = Build();
        var thrown = Throws(assembly, "Locals", 3);
        Assert.IsType<ArgumentOutOfRangeException>(thrown);
        Assert.Equal(LineOf("return list[inner];"), Innermost(thrown, assembly).Line);
        var values = Noted(thrown, assembly, probes, out var guard);
        Assert.Equal(["n", "a", "list", "inner"], guard.Names);
        Assert.Equal(3, values["n"]);
        Assert.Equal(4, values["a"]);
        Assert.Equal(8, values["inner"]);
        Assert.Equal("Thrower.Locals", guard.Where);
        Assert.NotNull(values["this"]);
    }

    [Fact]
    public void A_lambda_and_an_expression_body_note_their_own_values()
    {
        var (assembly, probes) = Build();
        var inLambda = Throws(assembly, "InLambda", 5);
        Assert.IsType<DivideByZeroException>(inLambda);
        Assert.Equal(LineOf("return y / (x - x);"), Innermost(inLambda, assembly).Line);
        var values = Noted(inLambda, assembly, probes, out var guard);
        Assert.Equal(["x", "y"], guard.Names);
        Assert.Equal(15, values["y"]);

        var arrow = Throws(assembly, "Arrow", [null]);
        Assert.IsType<NullReferenceException>(arrow);
        values = Noted(arrow, assembly, probes, out guard);
        Assert.Equal(["s"], guard.Names);
        Assert.Null(values["s"]);
    }

    [Fact]
    public void Local_functions_and_labels_keep_working_with_the_variables_before_them_noted()
    {
        var (assembly, probes) = Build();
        var values = Noted(Throws(assembly, "Local", 3), assembly, probes, out var guard);
        Assert.Equal(["n", "before", "after"], guard.Names);
        Assert.Equal(7, values["after"]);

        // A local function using a variable of the block stays in its guards (which pass what's in scope at their start).
        Assert.IsType<DivideByZeroException>(Throws(assembly, "Captures", 1));
        Noted(Throws(assembly, "Captures", 1), assembly, probes, out _);

        values = Noted(Throws(assembly, "Labels", 3), assembly, probes, out guard);
        Assert.Equal(3, values["i"]);
    }

    [Fact]
    public void In_an_iterator_statements_and_declarations_around_its_yields_are_guarded()
    {
        var (assembly, probes) = Build();
        var iterated = Throws(assembly, "Iterate", [null]);
        Assert.IsType<NullReferenceException>(iterated);
        Assert.Equal(LineOf("var text = o!.ToString();"), Innermost(iterated, assembly).Line);
        var values = Noted(iterated, assembly, probes, out var guard);
        Assert.Equal(["o", "count"], guard.Names);
        Assert.Equal(2, values["count"]);

        var more = Throws(assembly, "IterateMore", 4);
        Assert.IsType<DivideByZeroException>(more);
        Assert.Equal(LineOf("Field = b / (n - n);"), Innermost(more, assembly).Line);
        values = Noted(more, assembly, probes, out _);
        Assert.Equal(4, values["n"]);
    }

    [Fact]
    public void Guards_change_nothing_the_scripts_do_and_note_nothing_unless_watched()
    {
        var (assembly, _) = Build();
        var type = assembly.GetType("Thrower")!;
        var target = Activator.CreateInstance(type);
        // A script's own catch still catches; a method that doesn't throw returns as before.
        Assert.Equal(-1, type.GetMethod("Caught")!.Invoke(target, []));
        Assert.Equal(0, type.GetMethod("Locals")!.Invoke(target, [0]));
        Assert.Equal("HI", type.GetMethod("Arrow")!.Invoke(target, ["hi"]));

        StopWatching();
        var thrown = Throws(assembly, "Locals", 3);
        Assert.IsType<ArgumentOutOfRangeException>(thrown);
        Assert.Null(ScriptProbes.ThrownAt(thrown));
    }

    [Fact]
    public async Task A_crowds_script_throwing_on_another_thread_is_noted_there()
    {
        var (assembly, probes) = Build();
        var thrown = await Task.Run(() => Throws(assembly, "Locals", 2));
        var values = Noted(thrown, assembly, probes, out _);
        Assert.Equal(6, values["inner"]);
    }

    [Fact]
    public void Every_templates_scripts_build_with_guards_kept()
    {
        foreach (var template in Griff.Projects.ProjectTemplates.All)
        {
            var sources = template.Scripts().Select(s => new ScriptSource(@"C:\Game\Assets\" + s.Key, s.Value)).ToList();
            if (sources.Count == 0)
                continue;
            var compiled = ScriptCompiler.Compile(sources, probes: true);
            Assert.True(compiled.Success, $"{template.Name}: {string.Join("\n", compiled.Diagnostics)}");
            Assert.NotEmpty(compiled.Probes.Guards);
            // Every method with code has a guard.
            Assert.True(compiled.Probes.Guards.Count >= compiled.Probes.Probes.Count(p => p.Entry) / 2, template.Name);
        }
    }

    // A routine a coroutine runs: its variables are fields of the iterator's object.
    private sealed class Spinner : Behavior
    {
        public int Spins { get; set; }

        public IEnumerator Spin(object? target)
        {
            var turns = 3;
            yield return null;
            while (target!.GetHashCode() != 0)
            {
                Spins += turns;
                yield return null;
            }
        }
    }

    [Fact]
    public void A_coroutine_that_throws_says_which_routine_was_running()
    {
        var scene = new Scene();
        var entity = new Entity("Spinner");
        var spinner = new Spinner();
        entity.AddComponent(spinner);
        scene.Add(entity);
        var game = new Game(scene, new InputState());
        var routine = spinner.Spin(null);
        game.StartCoroutine(routine);
        var thrown = Assert.ThrowsAny<NullReferenceException>(() => Ticks(game));
        Assert.Same(routine, ScriptProbes.RoutineOf(thrown));

        StopWatching();
        var quiet = new Game(scene, new InputState());
        quiet.StartCoroutine(spinner.Spin(null));
        Assert.Null(ScriptProbes.RoutineOf(Assert.ThrowsAny<NullReferenceException>(() => Ticks(quiet))));
    }

    private static void Ticks(Game game)
    {
        for (var i = 0; i < 5; i++)
            game.Tick(game.Time.FixedDelta);
    }
}
