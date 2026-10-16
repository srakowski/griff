using System.IO;
using System.Windows.Threading;
using Griff.Agent;
using Griff.Editing;
using Griff.Editor.Core.Settings;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Debugging;
using Griff.Editor.Features.Play;
using Griff.Editor.Features.Scripts;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// Breakpoints in scripts: one goes on a line with code, the game stops there in the middle of its frame with the values in
/// scope, and Resume carries on from that line (Stop and rewinding leave the frame). They follow their code as it's edited,
/// are kept with the project, and agents set them and read the values through the Debugger.
/// </summary>
[Collection(nameof(BreakpointTests))]
public sealed class BreakpointTests : IDisposable
{
    private const string Counter = """
        public class Counter : Behavior
        {
            public int Count { get; set; }

            private int _calls;

            public int Doubled => Count * 2;

            protected override void Update()
            {
                var step = 2;
                Count += step;
                _calls++;
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

    private static int LineOf(string text) =>
        Counter[..Counter.IndexOf(text, StringComparison.Ordinal)].Count(c => c == '\n') + 1;

    // A project with the Counter script on an entity of the open scene.
    private (Workspace Workspace, string Script, Entity Entity) Open(IServiceProvider services)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        Wait(workspace.CreateProjectAsync(_root, "Game"));
        var project = workspace.Project!;
        var script = Path.Combine(project.AssetsPath, "Scripts", "Counter.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        File.WriteAllText(script, Counter.ReplaceLineEndings());
        Assert.True(workspace.ReloadScripts()!.Success);

        var entity = new Entity("Clicker");
        workspace.Scene!.Execute(new AddEntityEdit(workspace.Scene.Scene, entity));
        workspace.Scene.Execute(new AddComponentEdit(entity, workspace.Components.Find("Counter")!.CreateInstance()));
        return (workspace, script, entity);
    }

    // Runs `during` while the game stands at a breakpoint (queued now, so it runs inside the hold).
    private static void WhileHeld(Action during) =>
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Normal, during);

    [Fact]
    public void A_breakpoint_goes_on_the_next_line_with_code_and_toggles_off() => EditorHost.Run(services =>
    {
        var (_, script, _) = Open(services);
        var debugger = services.GetRequiredService<Debugger>();

        var set = debugger.SetBreakpoint("Counter", LineOf("protected override void Update()"));
        Assert.Equal(LineOf("var step = 2;"), set.Line);
        Assert.Equal("Scripts/Counter.cs", set.Script);
        Assert.True(set.IsSet);
        Assert.Same(set, debugger.SetBreakpoint("Scripts/Counter.cs", LineOf("var step = 2;")));

        Assert.Null(debugger.ToggleBreakpoint(script, set.Line));
        Assert.Empty(debugger.Breakpoints);
        Assert.Throws<ArgumentException>(() => debugger.SetBreakpoint("Counter", 200));
        Assert.Throws<ArgumentException>(() => debugger.SetBreakpoint("Nothing", 1));

        debugger.SetBreakpoint("Counter.cs", LineOf("_calls++;"));
        debugger.SetBreakpoint("Counter", LineOf("Count += step;"));
        debugger.RemoveAllBreakpoints();
        Assert.Empty(debugger.Breakpoints);
        Assert.False(Griff.Debugging.ScriptProbes.Armed);
    });

    [Fact]
    public void The_game_stops_at_the_line_with_the_values_in_scope_and_resume_carries_on() => EditorHost.Run(services =>
    {
        var (workspace, _, entity) = Open(services);
        var debugger = services.GetRequiredService<Debugger>();
        var play = services.GetRequiredService<PlayMode>();
        debugger.SetBreakpoint("Counter", LineOf("Count += step;"));
        Assert.True(Griff.Debugging.ScriptProbes.Armed);

        play.Play();
        var counter = workspace.Scene!.Scene.Find(entity.Id)!.Components.Single(c => c.GetType().Name == "Counter");
        var held = 0;
        WhileHeld(() =>
        {
            held++;
            Assert.True(play.IsAtBreakpoint);
            Assert.True(play.IsPaused);
            Assert.True(debugger.IsPaused);
            Assert.Equal(LineOf("Count += step;"), debugger.PausedLine);
            Assert.Equal("Counter.Update", debugger.PausedIn);
            Assert.Equal("Scripts/Counter.cs line " + LineOf("Count += step;") + ", in Counter.Update", debugger.Location);
            Assert.Equal(["this", "step"], debugger.Values.Select(v => v.Variable));
            Assert.Equal("2", debugger.Value("step")!.Value);
            Assert.Equal("int", debugger.Value("step")!.Type);
            Assert.Equal("0", debugger.Value("this.Count")!.Value);
            Assert.Equal("0", debugger.Value("this._calls")!.Value);
            Assert.Equal("Entity \"Clicker\"", debugger.Value("this.base.Entity")!.Value);
            Assert.Equal("Entity \"Clicker\"", debugger.Value("this.Entity")!.Value);
            Assert.Equal("Behavior", debugger.Value("this.base")!.Type);
            Assert.NotNull(debugger.Value("this.Transform"));
            Assert.Contains("step = 2 (int)", debugger.ValuesText);

            // The script is open at that line, which the editor marks.
            var editors = services.GetRequiredService<ScriptEditors>();
            Assert.Equal("Counter.cs", Assert.Single(editors.Documents).FileName);

            // Reading the game for the editor (a property with a breakpoint on, say) doesn't stop it again.
            Assert.Equal(0, counter.GetType().GetProperty("Doubled")!.GetValue(counter));

            // An agent reads them the way it reads any of the editor's services.
            var described = ObjectSurface.Describe(debugger, "Values").ToJsonString();
            Assert.Contains("step = 2 (int)", described);

            debugger.Resume();
        });
        play.Step();

        Assert.Equal(1, held);
        Assert.False(play.IsAtBreakpoint);
        Assert.False(debugger.IsPaused);
        Assert.Empty(debugger.Values);
        Assert.Equal(2, counter.GetType().GetProperty("Count")!.GetValue(counter));
        Assert.False(play.IsPaused);
        play.Stop();
    });

    [Fact]
    public void Stopping_or_rewinding_at_a_breakpoint_leaves_the_frame() => EditorHost.Run(services =>
    {
        var (workspace, _, entity) = Open(services);
        var debugger = services.GetRequiredService<Debugger>();
        var play = services.GetRequiredService<PlayMode>();
        debugger.SetBreakpoint("Counter", LineOf("_calls++;"));

        play.Play();
        WhileHeld(() => play.Seek(0));
        play.Step();
        Assert.True(play.IsPlaying);
        Assert.False(play.IsAtBreakpoint);
        Assert.True(play.IsPaused);
        // Back to before the frame: the rewound game has counted nothing.
        var counter = workspace.Scene!.Scene.Find(entity.Id)!.Components.Single(c => c.GetType().Name == "Counter");
        Assert.Equal(0, counter.GetType().GetProperty("Count")!.GetValue(counter));

        WhileHeld(play.Stop);
        play.Step();
        Assert.False(play.IsPlaying);
        Assert.False(debugger.IsPaused);
        Assert.False(workspace.IsPlaying);
    });

    [Fact]
    public void Breakpoints_follow_their_code_as_its_edited_and_stop_there_before_its_saved() => EditorHost.Run(services =>
    {
        var (workspace, script, _) = Open(services);
        var debugger = services.GetRequiredService<Debugger>();
        var editors = services.GetRequiredService<ScriptEditors>();
        var play = services.GetRequiredService<PlayMode>();
        var document = editors.Open(script)!;
        var breakpoint = debugger.SetBreakpoint("Counter", LineOf("_calls++;"));

        // Two lines above it: it moves down with its code.
        document.Text.Insert(0, "// A counter." + Environment.NewLine + "// It counts." + Environment.NewLine);
        Assert.Equal(LineOf("_calls++;") + 2, breakpoint.Line);
        Assert.True(breakpoint.IsSet);

        // Unsaved, the game still runs the scripts as built: it stops at that code, shown where it is now.
        play.Play();
        int? stoppedAt = null;
        WhileHeld(() =>
        {
            stoppedAt = debugger.PausedLine;
            debugger.Resume();
        });
        play.Step();
        Assert.Equal(breakpoint.Line, stoppedAt);
        play.Stop();

        // Kept with the project.
        debugger.Dispose();
        var saved = Assert.Single(services.GetRequiredService<ISettingsStore>().Settings.Breakpoints);
        Assert.Equal(Path.Combine("Scripts", "Counter.cs"), saved.Script);
        Assert.Equal(breakpoint.Line, saved.Line);
        Assert.Equal(workspace.Project!.RootPath, saved.Project);
    });
}
