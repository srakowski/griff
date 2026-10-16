using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Debugging;
using Griff.Editor.Features.Play;
using Griff.Editor.Features.Scripts;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// Stepping from a line the game stopped at: Step Over, Into and Out go on a line at a time, into another script's method
/// and back, from one script's Update to the next one's through Griff, and to the end of the frame, where the game stays
/// paused; Stop and rewinding still leave the frame, and once a step is over only breakpoints stop the game.
/// </summary>
[Collection(nameof(BreakpointTests))]
public sealed class SteppingTests : IDisposable
{
    private const string Walker = """
        public class Walker : Behavior
        {
            public int Count { get; set; }

            protected override void Update()
            {
                var step = 2;
                Count += Rules.Twice(step);
                Count++;
            }
        }

        """;

    private const string Rules = """
        public static class Rules
        {
            public static int Twice(int n)
            {
                var doubled = n * 2;
                return doubled;
            }
        }

        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static void Wait(Task task)
    {
        EditorHost.Pump(task);
    }

    private static int W(string text) => Walker[..Walker.IndexOf(text, StringComparison.Ordinal)].Count(c => c == '\n') + 1;

    private static int R(string text) => Rules[..Rules.IndexOf(text, StringComparison.Ordinal)].Count(c => c == '\n') + 1;

    // A project with the Walker script on `walkers` entities of the open scene.
    private (Workspace Workspace, List<Entity> Walkers) Open(IServiceProvider services, int walkers = 1)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        Wait(workspace.CreateProjectAsync(_root, "Game"));
        var scripts = Path.Combine(workspace.Project!.AssetsPath, "Scripts");
        Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(scripts, "Walker.cs"), Walker.ReplaceLineEndings());
        File.WriteAllText(Path.Combine(scripts, "Rules.cs"), Rules.ReplaceLineEndings());
        Assert.True(workspace.ReloadScripts()!.Success);

        var list = new List<Entity>();
        for (var i = 0; i < walkers; i++)
        {
            var entity = new Entity($"Walker {i + 1}");
            workspace.Scene!.Execute(new AddEntityEdit(workspace.Scene.Scene, entity));
            workspace.Scene.Execute(new AddComponentEdit(entity, workspace.Components.Find("Walker")!.CreateInstance()));
            list.Add(entity);
        }

        return (workspace, list);
    }

    // Runs one action each time the game stops at a line (inside the hold, as the editor would), in turn; what they throw
    // is thrown again by Check.
    private sealed class AtEachStop : IDisposable
    {
        private readonly PlayMode _play;
        private readonly Queue<Action> _actions;
        private ExceptionDispatchInfo? _failure;

        public AtEachStop(PlayMode play, params Action[] actions)
        {
            _play = play;
            _actions = new Queue<Action>(actions);
            play.BreakpointReached += OnStopped;
        }

        public int Stops { get; private set; }

        public void Dispose() => _play.BreakpointReached -= OnStopped;

        private void OnStopped(object? sender, EventArgs e)
        {
            Stops++;
            var action = _actions.Count > 0 ? _actions.Dequeue() : () => throw new InvalidOperationException("Stopped once more than expected.");
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    _failure ??= ExceptionDispatchInfo.Capture(ex);
                    _play.Stop();
                }
            });
        }

        public void Check()
        {
            _failure?.Throw();
            Assert.Empty(_actions);
        }
    }

    private static object? Count(Workspace workspace, Entity entity) =>
        workspace.Scene!.Scene.Find(entity.Id)!.Components.Single(c => c.GetType().Name == "Walker").GetType().GetProperty("Count")!
            .GetValue(workspace.Scene.Scene.Find(entity.Id)!.Components.Single(c => c.GetType().Name == "Walker"));

    [Fact]
    public void Step_over_into_and_out_go_through_another_scripts_method_and_on_to_the_end_of_the_frame() => EditorHost.Run(services =>
    {
        var (workspace, walkers) = Open(services);
        var debugger = services.GetRequiredService<Debugger>();
        var play = services.GetRequiredService<PlayMode>();
        var editors = services.GetRequiredService<ScriptEditors>();
        var commands = services.GetRequiredService<CommandRegistry>();
        debugger.SetBreakpoint("Walker", W("var step = 2;"));

        play.Play();
        using var stops = new AtEachStop(play,
            () =>
            {
                Assert.Equal(W("var step = 2;"), debugger.PausedLine);
                Assert.Equal("a breakpoint", debugger.StoppedBy);
                Assert.False(debugger.StoppedByStep);
                Assert.True(commands.Get("Debug.StepOver").CanExecute());
                Assert.True(commands.Get("Debug.StepOut").CanExecute());
                Assert.True(commands.Get("Debug.Continue").CanExecute());
                Assert.False(commands.Get("Game.Step").CanExecute());
                _ = debugger.StepOver();
                Assert.True(debugger.IsStepping);
            },
            () =>
            {
                Assert.Equal(W("Count += Rules.Twice(step);"), debugger.PausedLine);
                Assert.Equal("Step Over", debugger.StoppedBy);
                Assert.True(debugger.StoppedByStep);
                Assert.False(debugger.IsStepping);
                Assert.Equal("2", debugger.Value("step")!.Value);
                _ = debugger.StepInto();
            },
            () =>
            {
                // Into the other script's method, which opens at that line.
                Assert.Equal("Scripts/Rules.cs", debugger.PausedScript);
                Assert.Equal(R("var doubled = n * 2;"), debugger.PausedLine);
                Assert.Equal("Rules.Twice", debugger.PausedIn);
                Assert.Equal("2", debugger.Value("n")!.Value);
                Assert.Contains(editors.Documents, d => d.FileName == "Rules.cs");
                _ = debugger.StepOver();
            },
            () =>
            {
                Assert.Equal(R("return doubled;"), debugger.PausedLine);
                Assert.Equal("4", debugger.Value("doubled")!.Value);
                _ = debugger.StepOut();
            },
            () =>
            {
                // Back in Update, at the line after the call, with what it returned added.
                Assert.Equal("Scripts/Walker.cs", debugger.PausedScript);
                Assert.Equal(W("Count++;"), debugger.PausedLine);
                Assert.Equal("Step Out", debugger.StoppedBy);
                Assert.Equal("4", debugger.Value("this.Count")!.Value);
                _ = debugger.StepOver();
            });

        // Over the end of Update, into Griff: no more script runs this frame, so it pauses after it.
        play.Step();
        stops.Check();
        Assert.Equal(5, stops.Stops);
        Assert.False(debugger.IsPaused);
        Assert.False(debugger.IsStepping);
        Assert.False(play.IsAtBreakpoint);
        Assert.True(play.IsPaused);
        Assert.Contains("end of the frame", debugger.StepEnded);
        Assert.Equal(5, Count(workspace, walkers[0]));
        Assert.True(Griff.Debugging.ScriptProbes.Armed);
        play.Stop();
    });

    [Fact]
    public void Stepping_off_the_end_of_one_scripts_update_stops_in_the_next_one_griff_runs() => EditorHost.Run(services =>
    {
        var (workspace, walkers) = Open(services, walkers: 2);
        var debugger = services.GetRequiredService<Debugger>();
        var play = services.GetRequiredService<PlayMode>();
        debugger.SetBreakpoint("Walker", W("Count++;"));

        play.Play();
        string? first = null;
        using var stops = new AtEachStop(play,
            () =>
            {
                first = debugger.Value("this.Entity")!.Value;
                Assert.Equal("Entity \"Walker 1\"", first);
                debugger.RemoveAllBreakpoints();
                _ = debugger.StepOut();
            },
            () =>
            {
                // The other walker's Update, from its first line: Griff called it after the first returned.
                Assert.Equal(W("var step = 2;"), debugger.PausedLine);
                Assert.Equal("Entity \"Walker 2\"", debugger.Value("this.Entity")!.Value);
                _ = debugger.StepOver();
            },
            () =>
            {
                Assert.Equal(W("Count += Rules.Twice(step);"), debugger.PausedLine);
                debugger.Continue();
            });
        play.Step();
        stops.Check();
        Assert.False(play.IsPaused);
        Assert.All(walkers, w => Assert.Equal(5, Count(workspace, w)));

        // With no breakpoints left, nothing stops: every switch went off with the step.
        Assert.False(Griff.Debugging.ScriptProbes.Armed);
        play.Step();
        Assert.Equal(3, stops.Stops);
        play.Stop();
    });

    [Fact]
    public void Step_into_from_between_frames_plays_to_the_first_line_of_script() => EditorHost.Run(services =>
    {
        var (workspace, walkers) = Open(services);
        var debugger = services.GetRequiredService<Debugger>();
        var play = services.GetRequiredService<PlayMode>();
        var commands = services.GetRequiredService<CommandRegistry>();

        play.Play();
        Assert.False(commands.Get("Debug.StepInto").CanExecute());
        play.TogglePause();
        Assert.True(commands.Get("Debug.StepInto").CanExecute());
        Assert.False(commands.Get("Debug.StepOver").CanExecute());
        using var stops = new AtEachStop(play,
            () =>
            {
                Assert.Equal(W("var step = 2;"), debugger.PausedLine);
                Assert.Equal("Step Into", debugger.StoppedBy);
                debugger.Continue();
            });
        // The frame stepped into is played once: counted as it ends, since the game plays on from there in real time (as
        // many frames as the test takes to notice the step is over).
        object? afterFrame = null;
        play.Runner!.Loop.FrameEnded += (_, _) => afterFrame ??= Count(workspace, walkers[0]);
        Wait(debugger.StepInto());
        stops.Check();
        Assert.Equal(1, stops.Stops);
        Assert.Equal(5, afterFrame);
        play.Stop();
    });

    [Fact]
    public void Stop_and_rewind_still_leave_the_frame_after_a_step() => EditorHost.Run(services =>
    {
        var (workspace, walkers) = Open(services);
        var debugger = services.GetRequiredService<Debugger>();
        var play = services.GetRequiredService<PlayMode>();
        debugger.SetBreakpoint("Walker", W("var step = 2;"));

        play.Play();
        using (var stops = new AtEachStop(play, () => _ = debugger.StepInto(), () => play.Seek(0)))
        {
            play.Step();
            stops.Check();
        }

        Assert.True(play.IsPlaying);
        Assert.True(play.IsPaused);
        Assert.False(play.IsAtBreakpoint);
        Assert.False(debugger.IsStepping);
        Assert.Equal(0, Count(workspace, walkers[0]));

        using (var stops = new AtEachStop(play, () => _ = debugger.StepOver(), play.Stop))
        {
            play.Step();
            stops.Check();
        }

        Assert.False(play.IsPlaying);
        Assert.False(debugger.IsPaused);
        Assert.False(debugger.IsStepping);
    });

    private const string Drifter = """
        [Crowd]
        public class Drifter : Behavior
        {
            public float X { get; set; }

            protected override void Update()
            {
                var push = Rand.Range(0f, 1f);
                X += push;
                X *= 0.5f + Rand.Value;
            }
        }

        """;

    [Fact]
    public void A_crowd_stepped_through_plays_out_the_same() => EditorHost.Run(services =>
    {
        var (workspace, _) = Open(services, walkers: 0);
        var debugger = services.GetRequiredService<Debugger>();
        var play = services.GetRequiredService<PlayMode>();
        File.WriteAllText(Path.Combine(workspace.Project!.AssetsPath, "Scripts", "Drifter.cs"), Drifter.ReplaceLineEndings());
        Assert.True(workspace.ReloadScripts()!.Success);
        for (var i = 0; i < 40; i++)
        {
            var entity = new Entity($"Drifter {i}");
            workspace.Scene!.Execute(new AddEntityEdit(workspace.Scene.Scene, entity));
            workspace.Scene.Execute(new AddComponentEdit(entity, workspace.Components.Find("Drifter")!.CreateInstance()));
        }

        List<object?> Run()
        {
            play.Play();
            Griff.Rand.Seed(5);
            for (var i = 0; i < 3; i++)
                play.Step();
            var xs = workspace.Scene!.Scene.Entities.SelectMany(e => e.Components).Where(c => c.GetType().Name == "Drifter")
                .Select(c => c.GetType().GetProperty("X")!.GetValue(c)).ToList();
            play.Stop();
            return xs;
        }

        var plain = Run();
        var line = Drifter[..Drifter.IndexOf("var push", StringComparison.Ordinal)].Count(c => c == '\n') + 1;
        debugger.SetBreakpoint("Drifter", line);
        using var stops = new AtEachStop(play,
            () => _ = debugger.StepOver(),
            () => _ = debugger.StepOver(),
            () => _ = debugger.StepOver(),
            () => _ = debugger.StepInto(),
            () => _ = debugger.StepOut(),
            () =>
            {
                debugger.RemoveAllBreakpoints();
                _ = debugger.StepOver();
            },
            () => debugger.Continue());
        var stepped = Run();
        stops.Check();
        Assert.Equal(40, plain.Count);
        Assert.Equal(plain, stepped);
    });
}
