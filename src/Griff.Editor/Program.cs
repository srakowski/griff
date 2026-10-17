using Griff.Editor.Features.Updates;
using Velopack;

namespace Griff.Editor;

/// <summary>
/// Where the editor starts. Velopack (the installer and its updates) has to see every start first: the installer and the
/// updater start the editor to tell it it was installed, updated or is being removed, which it handles and quits, and an
/// update downloaded earlier and left for the next start is put in place here. For a Griff that wasn't installed (a build
/// from source, the portable zip) it does nothing.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main()
    {
        VelopackApp.Build()
            .OnRestarted(version => EditorUpdates.RestartedInto = version.ToString())
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();

        // Restarting into an update: the updater is waiting for this process to end, so nothing (a thread still running)
        // may keep it going once the editor has closed.
        if (EditorUpdates.Restarting)
            Environment.Exit(0);
    }
}
