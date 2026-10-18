using System.IO;
using System.Windows.Threading;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Controls;
using Griff.Editor.Features.Play;
using Griff.Input;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// Taking a key off an action in the Controls window: the × on a key does it with nothing more to say (as an agent's
/// ui_invoke presses it), and RemoveBinding does it by the key's name.
/// </summary>
public sealed class ControlsRemoveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void A_keys_own_remove_takes_it_off_and_so_does_its_name() => EditorHost.Run(services =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        EditorHost.Pump(workspace.CreateProjectAsync(_root, "Game"));
        var controls = new ControlsViewModel(workspace, services.GetRequiredService<PlayMode>());
        controls.Selected = controls.Actions.First(a => a.Name == "Jump");
        controls.Add(Key.Escape);
        controls.Add(Key.F2);
        var before = controls.Selected.Bindings.Count;

        // The × on Esc, pressed with no parameter.
        controls.Selected.Keys.Single(k => k.Binding == Key.Escape).RemoveCommand.Execute(null);
        Assert.DoesNotContain((InputBinding)Key.Escape, controls.Selected.Bindings);
        Assert.Equal(before - 1, controls.Selected.Bindings.Count);

        // By its name, as the window shows it or project.griff saves it.
        controls.RemoveBinding("F2");
        Assert.DoesNotContain((InputBinding)Key.F2, controls.Selected.Bindings);
        Assert.Throws<InvalidOperationException>(() => controls.RemoveBinding("F2"));
    });
}
