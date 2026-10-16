using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Griff.Debugging;
using Griff.Editor.Features.Debugging.Probes;
using Griff.Projects;
using Griff.Scripting;
using Griff.Templates;

namespace Griff.Editor.Tests;

/// <summary>
/// The editor builds scripts with a breakpoint probe on each line of code: a line stops only once its switch is on, with
/// the values of the variables in scope there, and the scripts do what they did without probes.
/// </summary>
[Collection(nameof(BreakpointProbeTests))]
public class BreakpointProbeTests
{
    private const string Path = @"C:\Game\Assets\Scripts\Probe.cs";

    private sealed class Recorder : IScriptBreakHandler
    {
        public List<ScriptBreak> Hits { get; } = [];

        public bool Abort { get; set; }

        public Action? During { get; set; }

        public bool Break(in ScriptBreak hit)
        {
            Hits.Add(hit);
            During?.Invoke();
            return Abort;
        }
    }

    private static (Assembly Assembly, ProbeTable Probes) Build(string source)
    {
        var compiled = ProbeCompiler.Compile([new ScriptSource(Path, source)]);
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var assembly = new AssemblyLoadContext("ProbeTest", isCollectible: true).LoadFromStream(new MemoryStream(compiled.Assembly!), new MemoryStream(compiled.Symbols!));
        compiled.Probes.Attach(assembly);
        return (assembly, compiled.Probes);
    }

    private static Recorder Listen()
    {
        var recorder = new Recorder();
        ScriptProbes.Handler = recorder;
        return recorder;
    }

    private static object? Call(Assembly assembly, string type, string method, params object?[] args)
    {
        var target = assembly.GetType(type)!;
        var instance = Activator.CreateInstance(target);
        try
        {
            return target.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)!.Invoke(instance, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(ex.InnerException);
            throw;
        }
    }

    private static int LineOf(string source, string text) =>
        source[..source.IndexOf(text, StringComparison.Ordinal)].Count(c => c == '\n') + 1;

    private const string Counter = """
        public class Counter
        {
            public int Total;

            public int Count(int to)
            {
                var sum = 0;
                for (var i = 0; i < to; i++)
                {
                    sum += i;
                }

                string label;
                label = "done";
                Total = sum;
                return sum;
            }
        }
        """;

    [Fact]
    public void A_line_stops_only_once_its_switch_is_on_with_the_values_in_scope()
    {
        var (assembly, probes) = Build(Counter);
        var recorder = Listen();
        try
        {
            Assert.Equal(10, Call(assembly, "Counter", "Count", 5));
            Assert.Empty(recorder.Hits);

            var line = LineOf(Counter, "sum += i;");
            var probe = probes.AtOrAfter(Path, line)!;
            Assert.Equal(line, probe.Line);
            Assert.Equal("Counter.Count", probe.Where);
            Assert.True(probe.HasThis);
            probes.Set(probe, true);

            Assert.Equal(10, Call(assembly, "Counter", "Count", 5));
            Assert.Equal(5, recorder.Hits.Count);
            var third = recorder.Hits[2];
            Assert.Equal(probe.Id, third.Probe);
            Assert.Same(probes.Switches, third.Probes);
            Assert.IsType(assembly.GetType("Counter")!, third.This);
            Assert.Equal(["to", "sum", "i"], probe.Names);
            Assert.Equal([5, 1, 2], third.Values);

            // `label` isn't set yet where it's declared, so it isn't passed there; it is once it's set.
            probes.Set(probe, false);
            var total = probes.AtOrAfter(Path, LineOf(Counter, "Total = sum;"))!;
            Assert.Equal(["to", "sum", "label"], total.Names);
            Assert.DoesNotContain("label", probes.AtOrAfter(Path, LineOf(Counter, "label = \"done\";"))!.Names);
        }
        finally
        {
            ScriptProbes.Handler = null;
        }
    }

    [Fact]
    public void Probes_keep_every_line_where_it_was_so_errors_point_at_the_code_as_written()
    {
        const string source = """
            public class Thrower
            {
                public void Go(int n)
                {
                    var x = n;
                    if (x > 0)
                        throw new InvalidOperationException("boom");
                }
            }
            """;
        var (assembly, _) = Build(source);
        var error = Assert.Throws<InvalidOperationException>(() => Call(assembly, "Thrower", "Go", 1));
        var trace = new System.Diagnostics.StackTrace(error, fNeedFileInfo: true);
        Assert.Contains(trace.GetFrames(), f => f.GetFileLineNumber() == LineOf(source, "throw new"));
    }

    [Fact]
    public void Expression_bodies_lambdas_and_statements_on_their_own_lines_get_probes_too()
    {
        const string source = """
            public class Shapes
            {
                public int Size { get; set; } = 3;

                public int Area =>
                    Size * Size;

                public int Double(int n) => n * 2;

                public void Nothing(int n) => Size = n;

                public int Sum(int[] items)
                {
                    var total = 0;
                    foreach (var item in items)
                        total += item;
                    if (total > 100)
                        total = 100;
                    else if (total < 0)
                        total = 0;
                    var doubled = items.Select(x =>
                        x * 2).Sum();
                    Func<int, int> add = y => { var z = y + total; return z; };
                    return total + doubled - doubled + Double(0) + add(0) - total;
                }
            }
            """;
        var (assembly, probes) = Build(source);
        var recorder = Listen();
        try
        {
            foreach (var probe in probes.Probes)
                probes.Set(probe, true);
            int[] items = [1, 2, 3];
            Assert.Equal(6, Call(assembly, "Shapes", "Sum", items));

            var lines = recorder.Hits.Select(h => probes[h.Probe]!.Line).ToHashSet();
            Assert.Contains(LineOf(source, "total += item;"), lines);
            Assert.Contains(LineOf(source, "x * 2"), lines);
            Assert.Contains(LineOf(source, "public int Double"), lines);
            Assert.DoesNotContain(LineOf(source, "total = 100;"), lines);

            // The lambda passes its own parameter, never a variable from around it (which it would then have to capture).
            var lambda = probes.AtOrAfter(Path, LineOf(source, "x * 2"))!;
            Assert.Equal(["x"], lambda.Names);
            Assert.False(lambda.HasThis);
            Assert.Equal("Shapes.Sum (in a lambda)", lambda.Where);

            var area = probes.AtOrAfter(Path, LineOf(source, "Size * Size"))!;
            Assert.Equal("Shapes.Area", area.Where);
            Assert.Equal(9, Call(assembly, "Shapes", "get_Area"));
            Assert.Contains(recorder.Hits, h => h.Probe == area.Id);

            Call(assembly, "Shapes", "Nothing", 4);
            Assert.Equal(["n"], probes.AtOrAfter(Path, LineOf(source, "public void Nothing"))!.Names);
        }
        finally
        {
            ScriptProbes.Handler = null;
        }
    }

    [Fact]
    public void A_stopped_game_ends_the_script_where_it_was_and_quiet_code_never_stops()
    {
        var (assembly, probes) = Build(Counter);
        var recorder = Listen();
        try
        {
            probes.Set(probes.AtOrAfter(Path, LineOf(Counter, "Total = sum;"))!, true);
            using (ScriptProbes.Quiet())
                Assert.Equal(10, Call(assembly, "Counter", "Count", 5));
            Assert.Empty(recorder.Hits);

            recorder.Abort = true;
            Assert.Throws<ScriptAbortedException>(() => Call(assembly, "Counter", "Count", 5));
            Assert.Single(recorder.Hits);
        }
        finally
        {
            ScriptProbes.Handler = null;
        }
    }

    [Fact]
    public void Held_at_a_breakpoint_the_editor_draws_no_numbers_from_the_game_and_its_scripts_dont_stop()
    {
        const string source = """
            public class Roller
            {
                public float Roll()
                {
                    var first = Rand.Value;
                    return first + Rand.Value;
                }
            }
            """;
        var (assembly, probes) = Build(source);
        Rand.Seed(7);
        var expected = (float)Call(assembly, "Roller", "Roll")!;

        var recorder = Listen();
        try
        {
            probes.Set(probes.AtOrAfter(Path, LineOf(source, "return first"))!, true);
            var nested = 0;
            recorder.During = () =>
            {
                // The editor at work while it's held: its numbers aren't the game's, and a script it runs doesn't stop.
                _ = Rand.Value;
                _ = Rand.Range(0, 100);
                Call(assembly, "Roller", "Roll");
                nested = recorder.Hits.Count;
            };
            Rand.Seed(7);
            Assert.Equal(expected, (float)Call(assembly, "Roller", "Roll")!);
            Assert.Equal(1, nested);
        }
        finally
        {
            ScriptProbes.Handler = null;
        }
    }

    [Fact]
    public void Every_templates_scripts_build_with_a_probe_on_every_line_of_code()
    {
        foreach (var template in ProjectTemplates.All)
        {
            var sources = template.Scripts().Select(s => new ScriptSource(@"C:\Game\Assets\" + s.Key, s.Value)).ToList();
            if (sources.Count == 0)
                continue;
            var compiled = ProbeCompiler.Compile(sources);
            Assert.True(compiled.Success, $"{template.Name}: {string.Join("\n", compiled.Diagnostics)}");
            var expected = sources.Sum(s => ScriptInstrumenter.ProbeLines(s.Text).Count);
            Assert.Equal(expected, compiled.Probes.Probes.Count);
        }
    }

    [Fact]
    public void Tricky_code_gets_a_probe_on_every_line_with_only_the_variables_it_can_pass()
    {
        const string source = """
            using System.Threading.Tasks;

            public struct Point
            {
                public int X;

                public int Twice() => X * 2;

                public static Point operator +(Point a, Point b) => new() { X = a.X + b.X };
            }

            public ref struct Cursor
            {
                public int At;

                public void Move() { At++; }
            }

            public class Tricky
            {
                private readonly Dictionary<string, int> _scores = new();
                private int _value;

                public event Action? Changed { add { _value++; } remove { _value--; } }

                public int this[int index] { get => index + _value; set => _value = value + index; }

                public int Value
                {
                    get { return _value; }
                    set
                    {
                        _value = value;
                    }
                }

                public static int Static(int n)
                {
                    var m = n * 2;
                    return m;
                }

                public int Patterns(object thing)
                {
                    if (!_scores.TryGetValue("a", out var score))
                        score = 1;
                    var total = score;
                    if (thing is string text && text.Length > 0)
                    {
                        total += text.Length;
                    }
                    else
                    {
                        total--;
                    }

                    switch (thing)
                    {
                        case int number when number > 2:
                            total += number;
                            break;
                        default:
                            total++;
                            break;
                    }

                    {
                        var inner = 5;
                        total += inner;
                    }

                    int later;
                    later = total;
                    Span<int> span = stackalloc int[2];
                    span[0] = later;
                    var also = span.Slice(0, 1);
                    also[0] = span[0];
                    ref var first = ref span[0];
                    first++;
                    var cursor = new Cursor();
                    cursor.Move();
                    try
                    {
                        total += int.Parse("x");
                    }
                    catch (FormatException error)
                    {
                        total += error.Message.Length > 0 ? 0 : 1;
                    }

                    using (var reader = new System.IO.StringReader("hi"))
                        total += reader.Read() > 0 ? 0 : 1;
                    foreach (var (key, value) in _scores)
                        total += value;
                    var doubled = new[] { 1, 2 }.Select((x, _) =>
                        x * 2).Where(_ =>
                        true).Sum();
                    Func<int, int, int> add = (_, _) =>
                        42;
                    return total + doubled + Helper(1) + add(1, 2) + Static(0) + this[0] + span[0] + also[0] + first;

                    int Helper(int h) => h + 1;
                }

                public async Task<int> LaterAsync(int wait)
                {
                    await Task.Yield();
                    var after = wait;
                    return after;
                }

                public async Task WaitAsync() =>
                    await Task.Yield();

                public IEnumerable<int> Count(int to)
                {
                    for (var i = 0; i < to; i++)
                        yield return i;
                }

                public bool Find(out int found)
                {
                    found = 3;
                    return found > 0;
                }
            }
            """;
        var compiled = ProbeCompiler.Compile([new ScriptSource(Path, source)]);
        Assert.True(compiled.Success, string.Join(Environment.NewLine, compiled.Diagnostics));
        Assert.Equal(ScriptInstrumenter.ProbeLines(source).Count, compiled.Probes.Probes.Count);

        ScriptProbe At(string text) => compiled.Probes.AtOrAfter(Path, LineOf(source, text))!;
        Assert.Equal(["thing", "score"], At("var total = score;").Names);
        Assert.Equal(["thing", "score", "total", "text"], At("total += text.Length;").Names);
        Assert.Equal(["thing", "score", "total"], At("total--;").Names);
        Assert.Equal(["thing", "score", "total", "number"], At("total += number;").Names);
        Assert.Equal(["thing", "score", "total", "inner"], At("total += inner;").Names);
        Assert.Equal(["thing", "score", "total"], At("int later;").Names);
        Assert.Equal(["thing", "score", "total", "later"], At("span[0] = later;").Names);
        // A Span (or a ref struct, or a stackalloc) can't be handed over as an object; a ref local is, by its value.
        Assert.Equal(["thing", "score", "total", "later", "first", "error"], At("total += error.Message").Names);
        Assert.Contains("reader", At("total += reader.Read()").Names);
        Assert.Equal(["h"], At("int Helper(int h)").Names);
        Assert.Equal(["n", "m"], At("return m;").Names);
        Assert.False(At("return m;").HasThis);
        Assert.Equal(["wait", "after"], At("return after;").Names);
        Assert.Equal(["to", "i"], At("yield return i;").Names);
        Assert.Equal(["found"], At("return found > 0;").Names);
        Assert.Equal(["value"], At("_value = value;").Names);
        Assert.Equal("Tricky.Value", At("_value = value;").Where);
        Assert.Equal(["x"], At("x * 2).Where").Names);
        Assert.Empty(At("true).Sum();").Names);
        Assert.Empty(At("42;").Names);
        Assert.True(At("public int Twice()").HasThis);
        Assert.False(At("At++;").HasThis);
        Assert.Equal(["a", "b"], At("public static Point operator +").Names);
        Assert.Equal("Point.operator +", At("public static Point operator +").Where);
    }

    [Fact]
    public void A_breakpoint_on_a_line_without_code_stops_at_the_next_line_with_some()
    {
        Assert.Equal(LineOf(Counter, "var sum = 0;"), ScriptInstrumenter.ProbeLineAtOrAfter(Counter, LineOf(Counter, "public int Count")));
        Assert.Equal(LineOf(Counter, "string label;"), ScriptInstrumenter.ProbeLineAtOrAfter(Counter, LineOf(Counter, "string label;") - 1));
        Assert.Null(ScriptInstrumenter.ProbeLineAtOrAfter(Counter, LineOf(Counter, "return sum;") + 1));
    }

    [Fact]
    public void An_exported_game_has_no_probes()
    {
        var compiled = ScriptCompiler.Compile([new ScriptSource(Path, Counter)], optimize: true);
        Assert.Empty(compiled.Probes.Probes);
        var assembly = new AssemblyLoadContext("NoProbes", isCollectible: true).LoadFromStream(new MemoryStream(compiled.Assembly!));
        Assert.Null(assembly.GetType(ScriptInstrumenter.SwitchesClass));
    }
}

/// <summary>
/// Runs the probe tests (<see cref="BreakpointProbeTests"/>, <see cref="ExceptionGuardTests"/>, <see cref="ScriptStepTests"/>)
/// with no other test alongside them: they set what every build's probes report to (<c>ScriptProbes.Handler</c>, whether
/// exceptions are watched), which an editor's debugger in a test running beside them sets too.
/// </summary>
[CollectionDefinition(nameof(BreakpointProbeTests), DisableParallelization = true)]
public sealed class BreakpointProbeCollection;
