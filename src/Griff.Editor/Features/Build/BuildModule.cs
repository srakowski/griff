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
    public void ConfigureServices(IServiceCollection services) => services
        .AddCommand<BuildGameCommand>()
        .AddCommand<BuildSmallGameCommand>();
}

/// <summary>Exports the project as one standalone .exe that carries its own .NET: nothing for players to install.</summary>
public sealed class BuildGameCommand(IWorkspace workspace, IStatusService status, IDialogService dialogs, ILogger<BuildGameCommand> logger)
    : BuildCommandBase(workspace, status, dialogs, logger)
{
    public override string Id => "Game.Build";
    public override string Text => "_Build Game";
    public override string? Icon => Icons.Package;
    public override KeyGesture? Gesture { get; } = new(Key.B, ModifierKeys.Control);
    public override MenuPlacement? Placement { get; } = new(Menus.Game, Group: 1, Order: 0);
    protected override string PlayerKind => "Standalone";
    protected override string Suffix => "";
}

/// <summary>Exports the project as one small .exe for players who have the .NET Desktop Runtime installed.</summary>
public sealed class BuildSmallGameCommand(IWorkspace workspace, IStatusService status, IDialogService dialogs, ILogger<BuildSmallGameCommand> logger)
    : BuildCommandBase(workspace, status, dialogs, logger)
{
    public override string Id => "Game.BuildSmall";
    public override string Text => "Build _Small Game (Needs .NET)";
    public override string? Icon => Icons.Package;
    public override KeyGesture? Gesture { get; } = new(Key.B, ModifierKeys.Control | ModifierKeys.Shift);
    public override MenuPlacement? Placement { get; } = new(Menus.Game, Group: 1, Order: 1);
    protected override string PlayerKind => "Small";
    protected override string Suffix => " (needs .NET)";
}

/// <summary>Exports the project as a single .exe into &lt;project&gt;/Build and shows it in Explorer.</summary>
public abstract class BuildCommandBase(
    IWorkspace workspace,
    IStatusService status,
    IDialogService dialogs,
    ILogger logger) : EditorCommand
{
    public const string BuildFolderName = "Build";

    /// <summary>Which player under the editor's Player folder to build from.</summary>
    protected abstract string PlayerKind { get; }

    /// <summary>Added to the exe's name, so both kinds of build can sit side by side.</summary>
    protected abstract string Suffix { get; }

    protected override bool CanExecuteCore() => workspace.Project is not null && !workspace.IsPlaying;

    protected override async Task ExecuteCoreAsync()
    {
        if (workspace.Project is not { } project)
            return;

        // Export what's on disk, so save first.
        if (!await workspace.SaveAllAsync())
            return;

        status.Show("Building...");
        var player = Path.Combine(AppContext.BaseDirectory, "Player", PlayerKind, "Griff.Player.exe");
        var output = Path.Combine(project.RootPath, BuildFolderName);
        var result = await Task.Run(() => GameExporter.Export(project, player, output, Suffix));

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

        var size = new FileInfo(result.Executable!).Length / (1024.0 * 1024.0);
        logger.LogInformation("Built {Executable} ({Size:0.#} MB)", result.Executable, size);
        status.Show($"Built {Path.GetFileName(result.Executable)} ({size:0.#} MB)");
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{result.Executable}\"") { UseShellExecute = true });
    }
}