using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
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
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddMenu(Menus.Build, "_Build", order: 8);
        services.AddSingleton<WebPreview>();
        services
            .AddCommand<BuildGameCommand>()
            .AddCommand<BuildSmallGameCommand>()
            .AddCommand<BuildWebCommand>();
    }
}

/// <summary>Exports the project as one standalone .exe that carries its own .NET: nothing for players to install.</summary>
public sealed class BuildGameCommand(IWorkspace workspace, IStatusService status, IDialogService dialogs, IExternalPrograms programs, ILogger<BuildGameCommand> logger)
    : BuildCommandBase(workspace, status, dialogs, programs, logger)
{
    public override string Id => "Game.Build";
    public override string Text => "_Build Game";
    public override string? Icon => Icons.Package;
    public override KeyGesture? Gesture { get; } = new(Key.B, ModifierKeys.Control);
    public override MenuPlacement? Placement { get; } = new(Menus.Build, Group: 0, Order: 0);
    protected override string PlayerKind => "Standalone";
    protected override string Suffix => "";
}

/// <summary>Exports the project as one small .exe for players who have the .NET Desktop Runtime installed.</summary>
public sealed class BuildSmallGameCommand(IWorkspace workspace, IStatusService status, IDialogService dialogs, IExternalPrograms programs, ILogger<BuildSmallGameCommand> logger)
    : BuildCommandBase(workspace, status, dialogs, programs, logger)
{
    public override string Id => "Game.BuildSmall";
    public override string Text => "Build _Small Game (Needs .NET)";
    public override string? Icon => Icons.Package;
    public override KeyGesture? Gesture { get; } = new(Key.B, ModifierKeys.Control | ModifierKeys.Shift);
    public override MenuPlacement? Placement { get; } = new(Menus.Build, Group: 0, Order: 1);
    protected override string PlayerKind => "Small";
    protected override string Suffix => " (needs .NET)";
}

/// <summary>Exports the project as a single .exe into &lt;project&gt;/Build and shows it in Explorer.</summary>
public abstract class BuildCommandBase(
    IWorkspace workspace,
    IStatusService status,
    IDialogService dialogs,
    IExternalPrograms programs,
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

        foreach (var warning in result.Warnings)
            logger.LogWarning("{Warning}", warning);

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
        programs.ShowInExplorer(result.Executable!);
    }
}

/// <summary>
/// Exports the project to play in a browser, into a folder you pick (by default the project's Build folder): the folder
/// holds index.html and everything with it, ready for any static host such as GitHub Pages, and a zip of it beside it is
/// ready to upload to itch.io as an HTML game. Sound, saves (kept in the browser), controllers, touch and fullscreen work
/// there; network play doesn't (browsers have no UDP), which it warns of. It then offers to play it in your browser, served
/// on this computer only.
/// </summary>
public sealed class BuildWebCommand(
    IWorkspace workspace,
    IStatusService status,
    IDialogService dialogs,
    IExternalPrograms programs,
    WebPreview preview,
    ILogger<BuildWebCommand> logger) : EditorCommand
{
    /// <summary>The question asked once it's built, which agents answer by this title.</summary>
    public const string PlayQuestion = "Play in browser";

    public override string Id => "Game.BuildWeb";
    public override string Text => "Build for _Web...";
    public override string? Icon => Icons.Globe;
    public override MenuPlacement? Placement { get; } = new(Menus.Build, Group: 1, Order: 0);

    /// <summary>The published web player the editor ships, beside it (see Griff.Editor.csproj).</summary>
    public static string WebPlayerFolder => Path.Combine(AppContext.BaseDirectory, "Player", "Web", "wwwroot");

    protected override bool CanExecuteCore() => workspace.Project is not null && !workspace.IsPlaying;

    protected override async Task ExecuteCoreAsync()
    {
        if (workspace.Project is not { } project)
            return;

        // Export what's on disk, so save first.
        if (!await workspace.SaveAllAsync())
            return;
        var buildFolder = Path.Combine(project.RootPath, BuildCommandBase.BuildFolderName);
        Directory.CreateDirectory(buildFolder);
        if (dialogs.ShowFolderDialog(buildFolder) is not { Length: > 0 } picked)
            return;

        var output = WebExporter.OutputIn(picked, project);
        status.Show("Building for the web...");
        var stopwatch = Stopwatch.StartNew();
        var (result, zip) = await Task.Run(() =>
        {
            var exported = WebExporter.Export(project, WebPlayerFolder, output);
            return (exported, exported.Success ? WebExporter.Zip(exported.OutputFolder!) : null);
        });

        foreach (var diagnostic in result.Diagnostics)
        {
            if (diagnostic.IsError)
                logger.LogError("{Diagnostic}", diagnostic.ToString());
            else
                logger.LogWarning("{Diagnostic}", diagnostic.ToString());
        }

        foreach (var warning in result.Warnings)
            logger.LogWarning("{Warning}", warning);

        if (!result.Success)
        {
            logger.LogError("Web build failed: {Error}", result.Error);
            status.Show("Web build failed; see Output");
            dialogs.ShowError("Web build failed", result.Error ?? "Unknown error.");
            return;
        }

        var folder = result.OutputFolder!;
        var size = new FileInfo(zip!).Length / (1024.0 * 1024.0);
        logger.LogInformation("Built {Name} for the web in {Seconds:0.0} s: {Folder} (index.html and all it needs, for any static host) and {Zip} ({Size:0.#} MB, for itch.io)",
            project.Name, stopwatch.Elapsed.TotalSeconds, folder, zip, size);
        status.Show($"Built {project.Name} for the web: {Path.GetFileName(folder)} and {Path.GetFileName(zip)}");

        var network = result.Warnings.Any(w => w.Contains("network game", StringComparison.Ordinal) || w.Contains("network play", StringComparison.Ordinal))
            ? "\n\nNetwork play isn't available in a browser: there, hosting and joining say so, and the rest plays alone (see Output)."
            : "";
        if (dialogs.Confirm(PlayQuestion,
                $"{project.Name} is built for the web in {folder}, with {Path.GetFileName(zip)} beside it to upload to itch.io.{network}\n\n" +
                "Play it in your browser now? Griff serves it on this computer only."))
        {
            var url = preview.Serve(folder);
            logger.LogInformation("Playing {Name} at {Url} (this computer only)", project.Name, url);
            status.Show($"Playing {project.Name} at {url}");
            programs.OpenInBrowser(url);
        }
        else
        {
            programs.ShowInExplorer(zip!);
        }
    }
}

/// <summary>
/// Serves a web build on this computer so it can be tried in a browser (which won't run one opened straight from its
/// files): Build &gt; Web uses it. One build at a time, at <c>http://127.0.0.1:<see cref="Port"/>/</c>; the same port every
/// time, so a game's saves (kept in the browser, per address) are still there when it's played again. Stopped when the
/// editor closes.
/// </summary>
public sealed class WebPreview : IAsyncDisposable, IDisposable
{
    private WebServer? _server;

    /// <summary>The port it serves on (another is used if this one is taken).</summary>
    public int Port { get; set; } = 7680;

    /// <summary>The address of the build being served, or null when nothing is.</summary>
    public string? Url => _server?.Url;

    /// <summary>The folder being served, or null.</summary>
    public string? Folder => _server?.Folder;

    /// <summary>Serves <paramref name="folder"/> (a web build) in place of whatever was served, and returns its address.</summary>
    public string Serve(string folder)
    {
        if (!File.Exists(Path.Combine(folder, WebExporter.Page)))
            throw new InvalidOperationException($"\"{folder}\" isn't a web build: it has no {WebExporter.Page}.");
        Stop();
        var server = new WebServer(folder, Port);
        try
        {
            server.Start();
        }
        catch (SocketException)
        {
            server = new WebServer(folder);
            server.Start();
        }

        _server = server;
        return server.Url;
    }

    /// <summary>Stops serving.</summary>
    public void Stop()
    {
        var server = _server;
        _server = null;
        server?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public void Dispose() => Stop();

    public ValueTask DisposeAsync()
    {
        Stop();
        return ValueTask.CompletedTask;
    }
}
