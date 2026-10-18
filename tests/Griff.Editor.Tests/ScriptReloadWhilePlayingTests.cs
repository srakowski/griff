using System.IO;
using System.Windows.Threading;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Play;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// A script saved while the game plays starts the game over with the new version, as a fresh Play would: what a script
/// spawns at Start is there once, its Start runs once, and the game runs the new code, rather than going on part old and
/// part new.
/// </summary>
[Collection(nameof(BreakpointTests))]
public sealed class ScriptReloadWhilePlayingTests : IDisposable
{
    private const string Hirer = """
        public class Hirer : Behavior
        {
            public int Hired { get; set; }

            protected override void Start()
            {
                Game.Spawn(new Entity("Employee"));
                Hired++;
            }
        }

        """;

    // The same, changed mid-playtest: it counts its steps as well.
    private const string HirerThatCounts = """
        public class Hirer : Behavior
        {
            public int Hired { get; set; }

            public int Steps { get; set; }

            protected override void Start()
            {
                Game.Spawn(new Entity("Employee"));
                Hired++;
            }

            protected override void Update() => Steps++;
        }

        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static Component HirerIn(Workspace workspace) =>
        workspace.Scene!.Scene.Entities.Single(e => e.Name == "Office").Components.Single(c => c.GetType().Name == "Hirer");

    private static int Employees(Workspace workspace) => workspace.Scene!.Scene.Entities.Count(e => e.Name == "Employee");

    [Fact]
    public void Recompiling_mid_play_starts_the_game_over_with_the_new_script() => EditorHost.Run(services =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        var play = services.GetRequiredService<PlayMode>();
        EditorHost.Pump(workspace.CreateProjectAsync(_root, "Game"));
        var script = Path.Combine(workspace.Project!.AssetsPath, "Scripts", "Hirer.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        File.WriteAllText(script, Hirer.ReplaceLineEndings());
        Assert.True(workspace.ReloadScripts()!.Success);
        var office = new Entity("Office");
        office.AddComponent(workspace.Components.Find("Hirer")!.CreateInstance());
        workspace.Scene!.Scene.Add(office);

        play.Play();
        for (var i = 0; i < 3; i++)
            play.Step();
        Assert.Equal(1, Employees(workspace));
        var game = play.Game;

        File.WriteAllText(script, HirerThatCounts.ReplaceLineEndings());
        Assert.True(workspace.ReloadScripts()!.Success);

        // One employee, hired once (swapped into the game as it stood, the new version started again and hired a second),
        // in a new game from the scene as it was when Play was pressed, running the new version.
        Assert.True(play.IsPlaying);
        for (var i = 0; i < 3; i++)
            play.Step();
        Assert.Equal(1, Employees(workspace));
        var hirer = HirerIn(workspace);
        Assert.Equal(1, hirer.GetType().GetProperty("Hired")!.GetValue(hirer));
        Assert.Equal(3, hirer.GetType().GetProperty("Steps")!.GetValue(hirer));
        Assert.NotSame(game, play.Game);

        play.Stop();
        // The scene being edited has the new script too, and nothing the game spawned.
        Assert.Equal(0, Employees(workspace));
        Assert.NotNull(HirerIn(workspace).GetType().GetProperty("Steps"));
    });
}
