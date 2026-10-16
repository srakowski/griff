using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Griff.Debugging;
using Griff.Editor.Features.Debugging.Probes;
using Griff.Scripting;

namespace Griff.Editor.Tests;

/// <summary>
/// Stepping from a line a script stopped at: over runs the line and stops at the next of the same method, into stops at
/// the next line of any script (inside what the line calls), out stops in the method that called this one. A step that
/// leaves the scripts stops at the next line of script that runs, and stepping changes nothing the scripts do.
/// </summary>
[Collection(nameof(BreakpointProbeTests))]
public class ScriptStepTests
{
    private const string GamePath = @"C:\Game\Assets\Scripts\Outer.cs";
    private const string HelperPath = @"C:\Game\Assets\Scripts\Helper.cs";

    private const string Game = """
        public class Outer
        {
            public int Total;

            public int Run()
            {
                var a = 1;
                var b = Helper.Twice(a);
                Total = a + b;
                Add(5);
                return Total;
            }

            private void Add(int n)
            {
                Total += n;
                Total += 1;
            }

            public int Depth(int n)
            {
                if (n == 0)
                    return 0;
                var below = Depth(n - 1);
                return below + 1;
            }

            public int Sum;

            public int Go()
            {
                var items = new List<int> { 1, 2 };
                items.ForEach(i =>
                {
                    Sum += i;
                });
                Sum *= 10;
                return Sum;
            }

            public int Safe()
            {
                var r = 0;
                try
                {
                    r = Boom();
                }
                catch (InvalidOperationException)
                {
                    r = -1;
                }
                return r;
            }

            private static int Boom() { throw new InvalidOperationException("boom"); }
        }
        """;

    private const string Helper = """
        public static class Helper
        {
            public static int Twice(int x)
            {
                var y = x * 2;
                return y;
            }
        }
        """;

    // Plays the debugger: stops at breakpoints and where a step ends, noting the line (and the values there), then takes
    // the next action in its plan: a step, or null to carry on.
    private sealed class Debugger(ProbeTable probes) : IScriptBreakHandler
    {
        private ScriptStep? _step;

        public HashSet<int> Breakpoints { get; } = [];

        public List<(string Script, int Line, object?[] Values)> Stopped { get; } = [];

        public Queue<StepKind?> Plan { get; } = new();

        public bool Stops(in ScriptBreak hit) =>
            _step is not { } step || Breakpoints.Contains(hit.Probe) || step.EndsAt(probes[hit.Probe]!, hit.Stack, ScriptProbes.CallStack);

        public bool Break(in ScriptBreak hit)
        {
            var probe = probes[hit.Probe]!;
            Stopped.Add((Path.GetFileNameWithoutExtension(probe.Path), probe.Line, hit.Values));
            _step = null;
            probes.SetAll(false);
            if (Plan.TryDequeue(out var next) && next is { } kind)
            {
                _step = new ScriptStep(kind, probe, hit.Stack, ScriptProbes.CallStack());
                probes.SetAll(true);
            }
            else
            {
                foreach (var id in Breakpoints)
                    probes.Set(probes[id]!, true);
            }

            return false;
        }

        public void Break(string path, string source, string code)
        {
            var probe = probes.AtOrAfter(path, LineOf(source, code))!;
            Breakpoints.Add(probe.Id);
            probes.Set(probe, true);
        }
    }

    private static (Assembly Assembly, ProbeTable Probes) Build()
    {
        var compiled = ProbeCompiler.Compile([new ScriptSource(GamePath, Game), new ScriptSource(HelperPath, Helper)]);
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var assembly = new AssemblyLoadContext("StepTest", isCollectible: true).LoadFromStream(new MemoryStream(compiled.Assembly!), new MemoryStream(compiled.Symbols!));
        compiled.Probes.Attach(assembly);
        return (assembly, compiled.Probes);
    }

    private static int LineOf(string source, string text) =>
        source[..source.IndexOf(text, StringComparison.Ordinal)].Count(c => c == '\n') + 1;

    private static int G(string code) => LineOf(Game, code);

    private static int H(string code) => LineOf(Helper, code);

    // Runs `method` of a new Outer, as Griff calls a script's method.
    private static object? Call(Assembly assembly, string method, params object?[] args)
    {
        var type = assembly.GetType("Outer")!;
        try
        {
            return type.GetMethod(method)!.Invoke(Activator.CreateInstance(type), args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(ex.InnerException);
            throw;
        }
    }

    private static void With(Action<Assembly, ProbeTable, Debugger> test)
    {
        var (assembly, probes) = Build();
        var debugger = new Debugger(probes);
        ScriptProbes.Handler = debugger;
        try
        {
            test(assembly, probes, debugger);
        }
        finally
        {
            ScriptProbes.Handler = null;
            probes.Clear();
        }
    }

    [Fact]
    public void Each_line_tells_which_function_it_is_in_and_whether_a_call_begins_there()
    {
        var (_, probes) = Build();
        var first = probes.AtOrAfter(GamePath, G("var a = 1;"))!;
        var second = probes.AtOrAfter(GamePath, G("var b = Helper"))!;
        var add = probes.AtOrAfter(GamePath, G("Total += n;"))!;
        var lambda = probes.AtOrAfter(GamePath, G("Sum += i;"))!;
        Assert.True(first.Entry);
        Assert.False(second.Entry);
        Assert.True(first.InSameFunction(second));
        Assert.True(add.Entry);
        Assert.False(add.InSameFunction(first));
        Assert.True(lambda.Entry);
        Assert.False(lambda.InSameFunction(probes.AtOrAfter(GamePath, G("Sum *= 10;"))!));
        Assert.True(probes.AtOrAfter(GamePath, G("throw new"))!.Entry);
        Assert.False(probes.AtOrAfter(GamePath, G("r = -1;"))!.Entry);
    }

    [Fact]
    public void Step_over_goes_line_by_line_through_a_method_without_stopping_in_what_it_calls()
    {
        With((assembly, _, debugger) =>
        {
            debugger.Break(GamePath, Game, "var a = 1;");
            for (var i = 0; i < 5; i++)
                debugger.Plan.Enqueue(StepKind.Over);
            Assert.Equal(9, Call(assembly, "Run"));
            Assert.Equal([G("var a = 1;"), G("var b = Helper"), G("Total = a + b;"), G("Add(5);"), G("return Total;")],
                debugger.Stopped.Select(s => s.Line));
            Assert.All(debugger.Stopped, s => Assert.Equal("Outer", s.Script));

            // Stepping over the end of Run returns into Griff: the next line of script that runs, wherever it is, stops.
            debugger.Breakpoints.Clear();
            Assert.Equal(9, Call(assembly, "Run"));
            Assert.Equal(G("var a = 1;"), debugger.Stopped[^1].Line);
            Assert.Equal(6, debugger.Stopped.Count);
        });
    }

    [Fact]
    public void Step_into_goes_into_another_scripts_method_and_back_out_to_the_next_line()
    {
        With((assembly, _, debugger) =>
        {
            debugger.Break(GamePath, Game, "var b = Helper");
            foreach (var kind in new[] { StepKind.Into, StepKind.Into, StepKind.Into, StepKind.Into, StepKind.Into, StepKind.Into })
                debugger.Plan.Enqueue(kind);
            Assert.Equal(9, Call(assembly, "Run"));
            Assert.Equal(
                [("Outer", G("var b = Helper")), ("Helper", H("var y = x * 2;")), ("Helper", H("return y;")), ("Outer", G("Total = a + b;")),
                    ("Outer", G("Add(5);")), ("Outer", G("Total += n;")), ("Outer", G("Total += 1;"))],
                debugger.Stopped.Select(s => (s.Script, s.Line)));
            // The values in Helper are its own.
            Assert.Equal([1], debugger.Stopped[1].Values);
            Assert.Equal([1, 2], debugger.Stopped[2].Values);
        });
    }

    [Fact]
    public void Step_out_finishes_the_method_and_stops_at_the_next_line_of_its_caller()
    {
        With((assembly, _, debugger) =>
        {
            debugger.Break(HelperPath, Helper, "var y = x * 2;");
            debugger.Break(GamePath, Game, "Total += n;");
            debugger.Plan.Enqueue(StepKind.Out);
            debugger.Plan.Enqueue(null);
            debugger.Plan.Enqueue(StepKind.Out);
            Assert.Equal(9, Call(assembly, "Run"));
            Assert.Equal(
                [("Helper", H("var y = x * 2;")), ("Outer", G("Total = a + b;")), ("Outer", G("Total += n;")), ("Outer", G("return Total;"))],
                debugger.Stopped.Select(s => (s.Script, s.Line)));
        });
    }

    [Fact]
    public void A_breakpoint_inside_a_call_stops_a_step_over_there()
    {
        With((assembly, _, debugger) =>
        {
            debugger.Break(GamePath, Game, "var b = Helper");
            debugger.Break(HelperPath, Helper, "return y;");
            debugger.Plan.Enqueue(StepKind.Over);
            Assert.Equal(9, Call(assembly, "Run"));
            Assert.Equal([("Outer", G("var b = Helper")), ("Helper", H("return y;"))], debugger.Stopped.Select(s => (s.Script, s.Line)));
        });
    }

    [Fact]
    public void Step_over_skips_every_recursive_call_under_it()
    {
        With((assembly, probes, debugger) =>
        {
            debugger.Break(GamePath, Game, "var below = Depth");
            debugger.Plan.Enqueue(StepKind.Over);
            debugger.Plan.Enqueue(StepKind.Over);
            var stopping = probes.AtOrAfter(GamePath, G("var below = Depth"))!;
            // The breakpoint comes off as soon as it's hit, so only the steps stop after it.
            var handler = (IScriptBreakHandler)debugger;
            ScriptProbes.Handler = new Once(debugger, () => debugger.Breakpoints.Remove(stopping.Id));
            Assert.Equal(3, Call(assembly, "Depth", 3));
            ScriptProbes.Handler = handler;
            Assert.Equal([(G("var below = Depth"), [3]), (G("return below + 1;"), [3, 2])],
                debugger.Stopped.Select(s => (s.Line, s.Values)).Select(s => (s.Line, s.Values.Cast<int>().ToArray())).ToList(),
                new StopComparer());
        });
    }

    private sealed class Once(IScriptBreakHandler inner, Action first) : IScriptBreakHandler
    {
        private bool _done;

        public bool Stops(in ScriptBreak hit) => inner.Stops(hit);

        public bool Break(in ScriptBreak hit)
        {
            if (!_done)
            {
                _done = true;
                first();
            }

            return inner.Break(hit);
        }
    }

    private sealed class StopComparer : IEqualityComparer<(int Line, int[] Values)>
    {
        public bool Equals((int Line, int[] Values) x, (int Line, int[] Values) y) => x.Line == y.Line && x.Values.SequenceEqual(y.Values);

        public int GetHashCode((int Line, int[] Values) obj) => obj.Line;
    }

    [Fact]
    public void Steps_through_a_lambda_that_dot_net_calls_go_into_it_and_out_to_its_next_call()
    {
        With((assembly, _, debugger) =>
        {
            debugger.Break(GamePath, Game, "var items = new");
            debugger.Plan.Enqueue(StepKind.Over);
            debugger.Plan.Enqueue(StepKind.Over);
            Assert.Equal(30, Call(assembly, "Go"));
            Assert.Equal([G("var items = new"), G("items.ForEach"), G("Sum *= 10;")], debugger.Stopped.Select(s => s.Line));

            debugger.Stopped.Clear();
            debugger.Plan.Enqueue(StepKind.Over);
            debugger.Plan.Enqueue(StepKind.Into);
            debugger.Plan.Enqueue(StepKind.Out);
            debugger.Plan.Enqueue(StepKind.Out);
            Assert.Equal(30, Call(assembly, "Go"));
            // Out of the lambda goes back through List.ForEach, which calls it again for the next item, and then out to Go.
            Assert.Equal([(G("var items = new"), 0), (G("items.ForEach"), 0), (G("Sum += i;"), 1), (G("Sum += i;"), 2), (G("Sum *= 10;"), 0)],
                debugger.Stopped.Select(s => (s.Line, s.Line == G("Sum += i;") ? (int)s.Values[0]! : 0)));
        });
    }

    [Fact]
    public void Step_over_a_line_that_throws_stops_in_the_catch_block_then_after_it()
    {
        With((assembly, _, debugger) =>
        {
            debugger.Break(GamePath, Game, "r = Boom();");
            debugger.Plan.Enqueue(StepKind.Over);
            debugger.Plan.Enqueue(StepKind.Over);
            Assert.Equal(-1, Call(assembly, "Safe"));
            Assert.Equal([G("r = Boom();"), G("r = -1;"), G("return r;")], debugger.Stopped.Select(s => s.Line));
        });
    }

    [Fact]
    public void A_step_from_between_frames_stops_at_the_first_line_that_runs()
    {
        With((_, probes, _) =>
        {
            var step = new ScriptStep(StepKind.Over, null, 0, []);
            Assert.Equal(StepKind.Into, step.Kind);
            Assert.True(step.EndsAt(probes.AtOrAfter(GamePath, G("var a = 1;"))!, 100, () => []));
        });
    }

    [Fact]
    public void Stepping_changes_nothing_the_scripts_do()
    {
        With((assembly, _, debugger) =>
        {
            var plain = (Call(assembly, "Run"), Call(assembly, "Depth", 4), Call(assembly, "Go"), Call(assembly, "Safe"));
            debugger.Break(GamePath, Game, "var a = 1;");
            debugger.Break(GamePath, Game, "if (n == 0)");
            debugger.Break(GamePath, Game, "var items = new");
            debugger.Break(GamePath, Game, "var r = 0;");
            for (var i = 0; i < 200; i++)
                debugger.Plan.Enqueue((StepKind)(i % 3));
            Assert.Equal(plain, (Call(assembly, "Run"), Call(assembly, "Depth", 4), Call(assembly, "Go"), Call(assembly, "Safe")));
            Assert.True(debugger.Stopped.Count > 10);
        });
    }
}
