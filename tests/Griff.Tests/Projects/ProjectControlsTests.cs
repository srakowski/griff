using Griff.Input;
using Griff.Projects;
using Griff.Runtime;

namespace Griff.Tests.Projects;

public sealed class ProjectControlsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-controls-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task NewProject_HasNoControlsInItsManifest()
    {
        var project = await Project.CreateAsync(_root, "Game");

        Assert.Empty(project.Controls);
        Assert.DoesNotContain("controls", await File.ReadAllTextAsync(project.ManifestPath));
    }

    [Fact]
    public async Task SetControls_SavesOnlyWhatDiffersFromBuiltIn_AndLoadsBack()
    {
        var project = await Project.CreateAsync(_root, "Game");
        var controls = InputState.BuiltInActions.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        controls["Jump"] = [Key.Space, Key.W, GamepadButton.A];
        controls["Shield"] = [Key.Q, MouseButton.Middle, GamepadButton.LeftShoulder];

        await project.SetControlsAsync(controls);
        var reloaded = await Project.LoadAsync(project.RootPath);

        Assert.Equal(["Jump", "Shield"], reloaded.Controls.Keys.Order());
        Assert.Equal([Key.Space, Key.W, GamepadButton.A], reloaded.Controls["Jump"]);
        Assert.Equal([Key.Q, MouseButton.Middle, GamepadButton.LeftShoulder], reloaded.Controls["Shield"]);
        Assert.Equal(["Space", "W", "Gamepad A"], reloaded.Manifest.Controls!["Jump"]);
    }

    [Fact]
    public async Task SetControls_BackToBuiltIn_RemovesThemFromTheManifest()
    {
        var project = await Project.CreateAsync(_root, "Game");
        await project.SetControlsAsync(new Dictionary<string, IReadOnlyList<InputBinding>> { ["Jump"] = [Key.W] });

        await project.SetControlsAsync(InputState.BuiltInActions);

        Assert.Null(project.Manifest.Controls);
        Assert.DoesNotContain("controls", await File.ReadAllTextAsync(project.ManifestPath));
    }

    [Fact]
    public void UseControls_RebindsBuiltInAndAddsActions()
    {
        var input = new InputState();

        input.UseControls(new Dictionary<string, IReadOnlyList<InputBinding>> { ["Jump"] = [Key.W], ["Shield"] = [Key.Q] });
        input.SetKey(Key.W, true);
        input.SetKey(Key.Q, true);

        Assert.True(input.WasPressed("Jump"));
        Assert.True(input.IsDown("Shield"));
        Assert.Contains("Shield", input.Actions);
    }

    [Fact]
    public void UseControls_Again_RestoresBuiltInAndDropsRemoved_ButKeepsScriptBindings()
    {
        var input = new InputState();
        input.UseControls(new Dictionary<string, IReadOnlyList<InputBinding>> { ["Jump"] = [Key.W], ["Shield"] = [Key.Q] });
        input.Bind("Shoot", Key.F);

        // Edited while the game runs: Jump back to normal, Shield deleted.
        input.UseControls(new Dictionary<string, IReadOnlyList<InputBinding>>());

        Assert.Equal(InputState.BuiltInActions["Jump"], input.BindingsOf("Jump"));
        Assert.DoesNotContain("Shield", input.Actions);
        Assert.Equal([Key.F], input.BindingsOf("Shoot"));
    }

    [Fact]
    public void PlayersRebind_StillWinsOverProjectControls()
    {
        var input = new InputState();
        input.UseControls(new Dictionary<string, IReadOnlyList<InputBinding>> { ["Jump"] = [Key.W] });

        input.Rebind("Jump", Key.K);

        Assert.Equal([Key.K], input.BindingsOf("Jump"));
    }
}
