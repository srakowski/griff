using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using Griff.Agent;
using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Settings;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Debugging;
using Griff.Editor.Features.Play;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Griff.Editor.Tests;

/// <summary>
/// A script that throws an exception the game doesn't catch stops the game at the line that threw, in the middle of its
/// frame, with the values in scope there (crowd scripts on the cores and coroutines too). Continue lets the exception end
/// the frame and the game play on; a step ends the frame and pauses after it; Stop leaves it; Rewind to Before the
/// Exception goes back to the moment the step that threw began, from where it happens again. Agents see all of it.
/// </summary>
[Collection(nameof(BreakpointTests))]
public sealed class ExceptionPauseTests : IDisposable
{
    private const string Thrower = """
        public class Thrower : Behavior
        {
            public int Count { get; set; }

            public int ThrowAt { get; set; } = 3;

            protected override void Update()
            {
                Count++;
                var left = ThrowAt - Count;
                if (left == 0)
                {
                    var empty = new int[0];
                    empty[left] = Count;
                }
            }
        }

        """;

    private const string Swarm = """
        [Crowd]
        public class Swarm : Behavior
        {
            public int Number { get; set; }

            public int Steps { get; set; }

            protected override void Update()
            {
                Steps++;
                var due = Steps * 10000 + Number;
                if (Number == 1500 && Steps == 2)
                {
                    var empty = new int[0];
                    empty[due] = 1;
                }
            }
        }

        """;

    private const string Spinner = """
        public class Spinner : Behavior
        {
            public int Spins { get; set; }

            protected override void Start() => StartCoroutine(Spin());

            private IEnumerator Spin()
            {
                var turns = 2;
                var limit = new int[2];
                while (limit[Spins / turns] >= 0)
                {
                    Spins += turns;
                    yield return null;
                }
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
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }

    private static int LineOf(string script, string text) => script[..script.IndexOf(text, StringComparison.Ordinal)].Count(c => c == '\n') + 1;

    // A project with the scripts, and `count` entities with the named script on (set up by `setup`).
    private (IWorkspace Workspace, List<Entity> Entities) Open(IServiceProvider services, string script, int count = 1, Action<Component, int>? setup = null)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<IWorkspace>();
        Wait(workspace.CreateProjectAsync(_root, "Game"));
        var scripts = Path.Combine(workspace.Project!.AssetsPath, "Scripts");
        Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(scripts, "Thrower.cs"), Thrower.ReplaceLineEndings());
        File.WriteAllText(Path.Combine(scripts, "Swarm.cs"), Swarm.ReplaceLineEndings());
        File.WriteAllText(Path.Combine(scripts, "Spinner.cs"), Spinner.ReplaceLineEndings());
        Assert.True(workspace.ReloadScripts()!.Success);

        var list = new List<Entity>();
        for (var i = 0; i < count; i++)
        {
            var entity = new Entity($"{script} {i + 1}");
            var component = workspace.Components.Find(script)!.CreateInstance();
            setup?.Invoke(component, i);
            entity.AddComponent(component);
            workspace.Scene!.Scene.Add(entity);
            list.Add(entity);
        }

        return (workspace, list);
    }

    // Runs one action each time the game stops (inside the hold, as the editor would), in turn; what they throw is thrown
    // again by Check.
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

    private static object? Get(IWorkspace workspace, Entity entity, string property)
    {
        var component = workspace.Scene!.Scene.Find(entity.Id)!.Components.Single(c => c.GetType().Assembly != typeof(Entity).Assembly);
        return component.GetType().GetProperty(property)!.GetValue(component);
    }

    private static int Errors(IServiceProvider services, string text) =>
        services.GetRequiredService<CapturedLog>().Errors.Count(e => e.Contains(text, StringComparison.Ordinal));

    // The errors logged (what the Output panel shows), kept for a test to count.
    private static void CaptureLog(IServiceCollection services)
    {
        services.AddSingleton<CapturedLog>();
        services.AddSingleton<ILoggerProvider>(provider => provider.GetRequiredService<CapturedLog>());
    }

    private sealed class CapturedLog : ILoggerProvider, ILogger
    {
        public List<string> Errors { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;

        public void Dispose()
        {
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
                Errors.Add(formatter(state, exception));
        }
    }

    [Fact]
    public void The_game_stops_at_the_line_that_threw_with_the_values_in_scope_and_continue_plays_on() => EditorHost.Run(CaptureLog, services =>
    {
        var (workspace, throwers) = Open(services, "Thrower");
        var debugger = services.GetRequiredService<Debugger>();
        var play = services.GetRequiredService<PlayMode>();
        var commands = services.GetRequiredService<CommandRegistry>();
        Assert.True(debugger.PauseOnExceptions);
        Assert.True(commands.Get("Debug.PauseOnExceptions").IsChecked);

        play.Play();
        Assert.False(commands.Get("Debug.RewindBeforeException").CanExecute());
        using var stops = new AtEachStop(play, () =>
        {
            Assert.True(debugger.IsPaused);
            Assert.True(debugger.StoppedByException);
            Assert.Equal("an exception", debugger.StoppedBy);
            Assert.True(play.IsAtBreakpoint);
            Assert.Equal("Scripts/Thrower.cs", debugger.PausedScript);
            Assert.Equal(LineOf(Thrower, "empty[left] = Count;"), debugger.PausedLine);
            Assert.Equal("Thrower.Update", debugger.PausedIn);
            Assert.StartsWith("IndexOutOfRangeException: ", debugger.ExceptionMessage);
            // In the middle of its frame: the line before it ran, the line that threw didn't finish.
            Assert.Equal("0", debugger.Value("left")!.Value);
            Assert.Equal("3", debugger.Value("this.Count")!.Value);
            Assert.NotNull(debugger.Value("empty"));
            Assert.Contains("Paused on IndexOutOfRangeException", debugger.ValuesText);
            Assert.Equal(1, Errors(services, "IndexOutOfRangeException"));
            Assert.True(commands.Get("Debug.Continue").CanExecute());
            Assert.True(commands.Get("Debug.RewindBeforeException").CanExecute());
            Assert.True(commands.Get("Debug.StepOver").CanExecute());
            Assert.False(commands.Get("Game.Step").CanExecute());
            debugger.Continue();
        });
        for (var i = 0; i < 3; i++)
            play.Step();
        stops.Check();
        Assert.Equal(1, stops.Stops);

        // The exception ended that frame, reported once, and the game plays on from there.
        Assert.False(debugger.IsPaused);
        Assert.False(play.IsPaused);
        Assert.Equal(3, Get(workspace, throwers[0], "Count"));
        Assert.Equal(1, Errors(services, "IndexOutOfRangeException"));
        play.Step();
        Assert.Equal(4, Get(workspace, throwers[0], "Count"));
        Assert.Equal(1, stops.Stops);
        // Its moment is still there to go back to.
        Assert.True(play.CanRewindBeforeException);
        play.Stop();
        Assert.False(play.CanRewindBeforeException);
    });

    [Fact]
    public void Rewind_goes_back_to_the_start_of_the_step_that_threw_where_it_happens_again() => EditorHost.Run(CaptureLog, services =>
    {
        var (workspace, throwers) = Open(services, "Thrower");
        var debugger = services.GetRequiredService<Debugger>();
        var play = services.GetRequiredService<PlayMode>();

        play.Play();
        using (var stops = new AtEachStop(play, () => Assert.True(debugger.RewindBeforeException())))
        {
            for (var i = 0; i < 3; i++)
                play.Step();
            stops.Check();
        }

        // Paused as the third step began: two steps counted.
        Assert.True(play.IsPlaying);
        Assert.True(play.IsPaused);
        Assert.False(play.IsAtBreakpoint);
        Assert.True(play.ExceptionSnapshotExact);
        Assert.Equal(play.ExceptionSnapshot, play.TimelinePosition);
        Assert.Equal(2, Get(workspace, throwers[0], "Count"));

        // From there, with a breakpoint before the line that threw, it happens again: first the breakpoint, then the exception.
        debugger.SetBreakpoint("Thrower", LineOf(Thrower, "var empty = new int[0];"));
        using (var stops = new AtEachStop(play,
            () =>
            {
                Assert.Equal("a breakpoint", debugger.StoppedBy);
                Assert.Equal("0", debugger.Value("left")!.Value);
                debugger.Continue();
            },
            () =>
            {
                Assert.True(debugger.StoppedByException);
                Assert.Equal(LineOf(Thrower, "empty[left] = Count;"), debugger.PausedLine);
                Assert.Equal("3", debugger.Value("this.Count")!.Value);
                play.Stop();
            }))
        {
            play.Step();
            stops.Check();
        }

        Assert.False(play.IsPlaying);
    });

    [Fact]
    public void A_step_from_the_exception_ends_the_frame_and_stop_leaves_it() => EditorHost.Run(CaptureLog, services =>
    {
        var (workspace, throwers) = Open(services, "Thrower");
        var debugger = services.GetRequiredService<Debugger>();
        var play = services.GetRequiredService<PlayMode>();

        play.Play();
        Task? step = null;
        using (var stops = new AtEachStop(play, () => step = debugger.StepOver()))
        {
            for (var i = 0; i < 3; i++)
                play.Step();
            stops.Check();
        }

        Assert.True(step!.IsCompleted);
        Assert.True(play.IsPaused);
        Assert.False(play.IsAtBreakpoint);
        Assert.False(debugger.IsStepping);
        Assert.Contains("the exception ended the frame", debugger.StepEnded);
        Assert.Equal(3, Get(workspace, throwers[0], "Count"));

        // Step Into from there plays the next frame to its first line of script.
        using (var stops = new AtEachStop(play, () =>
        {
            Assert.Equal("Step Into", debugger.StoppedBy);
            Assert.Equal(LineOf(Thrower, "Count++;"), debugger.PausedLine);
            debugger.Continue();
        }))
        {
            Wait(debugger.StepInto());
            stops.Check();
        }

        // Thrown again later, Stop leaves the frame and the game.
        var thrower = workspace.Scene!.Scene.Find(throwers[0].Id)!.Components.Single(c => c.GetType().Name == "Thrower");
        thrower.GetType().GetProperty("ThrowAt")!.SetValue(thrower, 6);
        using (var stops = new AtEachStop(play, () => play.Stop()))
        {
            play.Step();
            play.Step();
            stops.Check();
        }

        Assert.False(play.IsPlaying);
        Assert.Equal(2, Errors(services, "IndexOutOfRangeException"));
    });

    [Fact]
    public void Turned_off_an_exception_ends_the_frame_and_pauses_the_game_as_before() => EditorHost.Run(CaptureLog, services =>
    {
        var (workspace, throwers) = Open(services, "Thrower");
        var debugger = services.GetRequiredService<Debugger>();
        var play = services.GetRequiredService<PlayMode>();
        var commands = services.GetRequiredService<CommandRegistry>();
        Wait(commands.Get("Debug.PauseOnExceptions").ExecuteAsync());
        Assert.False(debugger.PauseOnExceptions);
        Assert.False(services.GetRequiredService<ISettingsStore>().Settings.PauseOnExceptions);

        play.Play();
        using var stops = new AtEachStop(play);
        for (var i = 0; i < 3; i++)
            play.Step();
        stops.Check();
        Assert.Equal(0, stops.Stops);
        Assert.True(play.IsPaused);
        Assert.False(play.CanRewindBeforeException);
        Assert.Equal(3, Get(workspace, throwers[0], "Count"));
        Assert.Equal(1, Errors(services, "Game error"));
        play.Stop();
        debugger.PauseOnExceptions = true;
    });

    [Fact]
    public void A_crowd_script_on_the_cores_stops_at_its_line_with_its_values() => EditorHost.Run(CaptureLog, services =>
    {
        var (workspace, swarm) = Open(services, "Swarm", 2600, (component, i) => component.GetType().GetProperty("Number")!.SetValue(component, i));
        var debugger = services.GetRequiredService<Debugger>();
        var play = services.GetRequiredService<PlayMode>();

        play.Play();
        using var stops = new AtEachStop(play, () =>
        {
            Assert.True(debugger.StoppedByException);
            Assert.Equal("Scripts/Swarm.cs", debugger.PausedScript);
            Assert.Equal(LineOf(Swarm, "empty[due] = 1;"), debugger.PausedLine);
            Assert.Equal("21500", debugger.Value("due")!.Value);
            Assert.Equal("1500", debugger.Value("this.Number")!.Value);
            debugger.Continue();
        });
        play.Step();
        play.Step();
        stops.Check();
        Assert.Equal(1, stops.Stops);
        Assert.False(Griff.Debugging.ScriptProbes.Armed);
        play.Stop();
    });

    [Fact]
    public void A_coroutine_stops_at_its_line_with_its_variables() => EditorHost.Run(CaptureLog, services =>
    {
        Open(services, "Spinner");
        var debugger = services.GetRequiredService<Debugger>();
        var play = services.GetRequiredService<PlayMode>();

        play.Play();
        using var stops = new AtEachStop(play, () =>
        {
            Assert.True(debugger.StoppedByException);
            Assert.Equal(LineOf(Spinner, "while (limit[Spins / turns] >= 0)"), debugger.PausedLine);
            Assert.Equal("2", debugger.Value("turns")!.Value);
            Assert.NotNull(debugger.Value("limit"));
            Assert.Equal("4", debugger.Value("this.Spins")!.Value);
            play.Stop();
        });
        for (var i = 0; i < 4 && play.IsPlaying; i++)
            play.Step();
        stops.Check();
        Assert.Equal(1, stops.Stops);
    });

    [Fact]
    public void Agents_hear_where_it_threw_and_can_rewind_to_before_it() => EditorHost.Run(CaptureLog, services =>
    {
        var (workspace, throwers) = Open(services, "Thrower");
        var play = services.GetRequiredService<PlayMode>();
        var debugger = services.GetRequiredService<Debugger>();
        var tools = services.GetServices<IAgentToolSource>().SelectMany(s => s.GetTools()).ToList();
        AgentTool Tool(string name) => tools.Single(t => t.Name == name);
        Assert.Contains("just before the step in which a script last threw", Tool(CommandTools.ToolName("Debug.RewindBeforeException")).Description, StringComparison.Ordinal);
        Assert.Contains("on/off", Tool(CommandTools.ToolName("Debug.PauseOnExceptions")).Description, StringComparison.Ordinal);
        Assert.Contains("Stopped where a script threw", Tool(CommandTools.ToolName("Debug.Continue")).Description, StringComparison.Ordinal);

        play.Play();
        string? stopped = null, state = null;
        using (var stops = new AtEachStop(play, () =>
        {
            // What a command or game_input that ran into it returns, and what editor_state says.
            stopped = debugger.StoppedText();
            var reading = Tool("editor_state").Invoke(ToolArgs.Empty);
            Wait(reading);
            state = reading.Result.Text;
            Assert.True(debugger.RewindBeforeException());
        }))
        {
            for (var i = 0; i < 3; i++)
                play.Step();
            stops.Check();
        }

        Assert.Contains("Stopped on an exception a script threw (IndexOutOfRangeException", stopped, StringComparison.Ordinal);
        Assert.Contains("left = 0", stopped, StringComparison.Ordinal);
        Assert.Contains("Debug_RewindBeforeException", stopped, StringComparison.Ordinal);
        Assert.Contains("Game: paused on an exception a script threw (IndexOutOfRangeException", state, StringComparison.Ordinal);
        Assert.Contains("Scripts/Thrower.cs line " + LineOf(Thrower, "empty[left] = Count;"), state, StringComparison.Ordinal);
        Assert.Equal(2, Get(workspace, throwers[0], "Count"));
        play.Stop();
    });
}
