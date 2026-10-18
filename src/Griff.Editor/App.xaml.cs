using Griff.Editor.Core;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Panels;
using Griff.Editor.Core.Settings;
using Griff.Editor.Core.Status;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Art;
using Griff.Editor.Features.Assets;
using Griff.Editor.Features.Audio;
using Griff.Editor.Features.Build;
using Griff.Editor.Features.Controls;
using Griff.Editor.Features.Data;
using Griff.Editor.Features.Debugging;
using Griff.Editor.Features.DesignSystem;
using Griff.Editor.Features.Editing;
using Griff.Editor.Features.Entities;
using Griff.Editor.Features.Hierarchy;
using Griff.Editor.Features.Inspector;
using Griff.Editor.Features.Output;
using Griff.Editor.Features.Play;
using Griff.Editor.Features.Prefabs;
using Griff.Editor.Features.Profiler;
using Griff.Editor.Features.Projects;
using Griff.Editor.Features.Scenes;
using Griff.Editor.Features.SceneView;
using Griff.Editor.Features.Scripts;
using Griff.Editor.Features.Updates;
using Griff.Editor.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Griff.Editor;

/// <summary>Composition root: builds the host, loads modules and shows the shell.</summary>
public partial class App : Application
{
    // Add new feature modules here; a genre's (Genres/<Genre>) is found by itself (see GenreEditors).
    internal static readonly IEditorModule[] Modules =
    [
        new ProjectsModule(),
        new ScenesModule(),
        new SceneViewModule(),
        new EditingModule(),
        new EntitiesModule(),
        new PrefabsModule(),
        new PlayModule(),
        new ScriptsModule(),
        new DebuggingModule(),
        new BuildModule(),
        new HierarchyModule(),
        new InspectorModule(),
        new AssetsModule(),
        new AudioModule(),
        new ArtModule(),
        new ControlsModule(),
        new OutputModule(),
        new ProfilerModule(),
        new DataModule(),
        .. GenreEditors.Modules,
        new DesignSystemModule(),
        new UpdatesModule(),
        new AgentModule(),
    ];

    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var builder = Host.CreateApplicationBuilder(e.Args);
        builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
        // The editor starting and closing reach the Windows Application log too, so an editor that ended without its
        // closing line (and why) was ended from outside, or crashed.
        builder.Logging.AddFilter<Microsoft.Extensions.Logging.EventLog.EventLogLoggerProvider>(typeof(App).FullName, LogLevel.Information);
        ConfigureCoreServices(builder.Services);
        foreach (var module in Modules)
            module.ConfigureServices(builder.Services);

        _host = builder.Build();
        _host.Start();

        MainWindow = _host.Services.GetRequiredService<MainWindow>();
#if DEBUG
        if (Diagnostics.SnapshotRunner.TryRun(_host.Services, MainWindow))
            return;
        if (Diagnostics.Demo.DemoRunner.TryRun(_host.Services, MainWindow))
            return;
#endif
        MainWindow.Show();

        var logger = _host.Services.GetRequiredService<ILogger<App>>();
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            logger.LogCritical(args.ExceptionObject as Exception, "The editor is ending on an error outside the UI: {Message}", (args.ExceptionObject as Exception)?.Message);
        TaskScheduler.UnobservedTaskException += (_, args) => logger.LogError(args.Exception, "A background task failed unnoticed");
        var version = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0];
        logger.LogInformation("Griff Editor {Version} started ({Modules} modules)", version, Modules.Length);
        await ReopenLastProjectAsync(logger);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            _host.Services.GetService<ILogger<App>>()?.LogInformation("Griff Editor closed: {Reason}",
                _host.Services.GetService<EditorWindow>()?.CloseReason ?? "the application ended");
            _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            _host.Dispose();
        }

        base.OnExit(e);
    }

    internal static void ConfigureCoreServices(IServiceCollection services)
    {
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<IStatusService, StatusService>();
        services.AddSingleton<Workspace>();
        services.AddSingleton<IProjectSession>(sp => sp.GetRequiredService<Workspace>());
        services.AddSingleton<ISceneDocuments>(sp => sp.GetRequiredService<Workspace>());
        services.AddSingleton<IPlaySession>(sp => sp.GetRequiredService<Workspace>());
        services.AddSingleton<IScriptSession>(sp => sp.GetRequiredService<Workspace>());
        services.AddSingleton<PrefabFiles>();
        services.AddSingleton<ISelection, Selection>();
        services.AddSingleton<IExternalPrograms, ExternalPrograms>();
        services.AddSingleton<IClipboard, WindowsClipboard>();
        services.AddSingleton<Core.EditorWindow>();
        services.AddLazyServices();
        services.AddSingleton<CommandRegistry>();
        services.AddSingleton<Core.Undo.ActiveDocument>();
        services.AddSingleton<Core.Documents.EditorDocuments>();
        services.AddSingleton<DocumentTabsHost>();

        services.AddMenu(Menus.File, "_File", order: 0);
        services.AddMenu(Menus.Edit, "_Edit", order: 1);
        services.AddMenu(Menus.View, "_View", order: 2);
        services.AddMenu(Menus.Help, "_Help", order: 100);

        services.AddSingleton<MenuBuilder>();
        services.AddSingleton<ToolbarBuilder>();
        services.AddSingleton<PanelLayout>();
        services.AddSingleton<PanelHost>();
        services.AddCommand<SaveLayoutCommand>();
        services.AddCommand<LoadLayoutCommand>();
        services.AddCommand<DeleteLayoutCommand>();
        services.AddCommand<ResetLayoutCommand>();
        services.AddCommand<ShowPanelCommand>();
        services.AddSingleton<StartPage>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
    }

    // Jams are short: pick up exactly where you left off.
    private async Task ReopenLastProjectAsync(ILogger logger)
    {
        var projectSession = _host!.Services.GetRequiredService<IProjectSession>();
        if (projectSession.RecentProjects is not [{ } last, ..])
            return;

        try
        {
            await projectSession.OpenProjectAsync(last);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            logger.LogWarning("Couldn't reopen {Path}: {Message}", last, ex.Message);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var services = _host?.Services;
        services?.GetService<ILogger<App>>()?.LogError(e.Exception, "Unhandled exception");

        if (services?.GetService<IDialogService>() is { } dialogs)
        {
            dialogs.ShowError("Unexpected error", e.Exception.Message);
            e.Handled = true;
        }
    }
}
