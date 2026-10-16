using Griff.Editing;
using Griff.Input;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Projects;

/// <summary>A project sets how many steps a second its game takes, every host starts the game with it, and setting it undoes.</summary>
public sealed class ProjectStepRateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-step-rate-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task A_new_project_steps_60_times_a_second_without_saying_so()
    {
        var project = await Project.CreateAsync(_root, "Game");

        Assert.Equal(60, project.StepRate);
        Assert.DoesNotContain("stepRate", await File.ReadAllTextAsync(project.ManifestPath));
    }

    [Fact]
    public async Task The_step_rate_is_saved_kept_in_range_and_loads_back()
    {
        var project = await Project.CreateAsync(_root, "Game");

        project.SetStepRate(120);
        Assert.Equal(120, (await Project.LoadAsync(project.RootPath)).StepRate);
        Assert.Contains("\"stepRate\": 120", await File.ReadAllTextAsync(project.ManifestPath));

        project.SetStepRate(1000);
        Assert.Equal(Time.MaxStepRate, project.StepRate);

        project.SetStepRate(60);
        Assert.DoesNotContain("stepRate", await File.ReadAllTextAsync(project.ManifestPath));
    }

    [Fact]
    public async Task A_game_starts_with_its_projects_step_rate_and_controls()
    {
        var project = await Project.CreateAsync(_root, "Game");
        project.SetStepRate(30);
        await project.SetControlsAsync(new Dictionary<string, IReadOnlyList<InputBinding>> { ["Shield"] = [Key.Q] });
        var game = new Game(new Scene(), new InputState());

        game.UseProjectSettings(project);

        Assert.Equal(30, game.Time.StepRate);
        Assert.Equal(1 / 30f, game.Time.FixedDelta, 6);
        Assert.Contains("Shield", game.HostInput.Actions);

        // A frame of a 30th of a second is one step of it.
        game.Advance(1 / 30.0);
        Assert.Equal(1, game.Time.Frame);

        // Exported, it loads with the game.
        var loaded = await GameLoader.LoadAsync(project.RootPath);
        var exported = loaded.NewGame();
        Assert.Equal(30, exported.Time.StepRate);
    }

    [Fact]
    public async Task Setting_the_step_rate_undoes_and_leaves_the_scene_saved()
    {
        var project = await Project.CreateAsync(_root, "Game");
        var history = new EditHistory();
        var heard = new List<int>();

        history.Execute(new SetStepRateEdit(project, 120, heard.Add));
        Assert.Equal(120, project.StepRate);
        Assert.False(history.IsDirty);
        Assert.Equal("Step Rate 120", history.UndoDescription);

        history.Undo();
        Assert.Equal(60, (await Project.LoadAsync(project.RootPath)).StepRate);
        history.Redo();
        Assert.Equal(120, project.StepRate);
        Assert.Equal([120, 60, 120], heard);
        Assert.False(history.IsDirty);
    }
}
