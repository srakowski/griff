using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Status;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Themes;
using Griff.Scripting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Griff.Editor.Features.Build;

public sealed class BuildModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services) => services.AddCommand<BuildGameCommand>();
}

/// <summary>Exports the project as a standalone game into &lt;project&gt;/Build and shows it in Explorer.</summary>
public sealed class BuildGameCommand(
    IWorkspace workspace,
    IStatusService status,
    IDialogService dialogs,
    ILogger<BuildGameCommand> logger) : EditorCommand
{
    public const string BuildFolderName = "Build";

    public override string Id => "Game.Build";
    public override string Text => "_Build Game";
    public override string? Icon => Icons.Package;
    public override KeyGesture? Gesture { get; } = new(Key.B, ModifierKeys.Control);
    public override MenuPlacement? Placement { get; } = new(Menus.Game, Group: 1, Order: 0);

    protected override bool CanExecuteCore() => workspace.Project is not null && !workspace.IsPlaying;

    protected override async Task ExecuteCoreAsync()
    {
        if (workspace.Project is not { } project)
            return;

        // Export what's on disk, so save first.
        if (workspace.Scene is { IsDirty: true } && !await workspace.SaveSceneAsync())
            return;

        status.Show("Building...");
        var player = Path.Combine(AppContext.BaseDirectory, "Player");
        var output = Path.Combine(project.RootPath, BuildFolderName);
        var result = await Task.Run(() => GameExporter.Export(project, player, output));

        foreach (var diagnostic in result.Diagnostics)
        {
            if (diagnostic.IsError)
                logger.LogError("{Diagnostic}", diagnostic.ToString());
            else
                logger.LogWarning("{Diagnostic}", diagnostic.ToString());
        }

        if (!result.Success)
        {
            logger.LogError("Build failed: {Error}", result.Error);
            status.Show("Build failed; see Output");
            dialogs.ShowError("Build failed", result.Error ?? "Unknown error.");
            return;
        }

        logger.LogInformation("Built {Executable}", result.Executable);
        status.Show($"Built {Path.GetFileName(result.Executable)}");
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{result.OutputFolder}\"") { UseShellExecute = true });
    }
}
