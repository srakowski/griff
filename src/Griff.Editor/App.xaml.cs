using System.IO;
using System.Windows;
using System.Windows.Threading;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Settings;
using Griff.Editor.Core.Status;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Art;
using Griff.Editor.Features.Assets;
using Griff.Editor.Features.Audio;
using Griff.Editor.Features.Build;
using Griff.Editor.Features.Controls;
using Griff.Editor.Features.DesignSystem;
using Griff.Editor.Features.Editing;
using Griff.Editor.Features.Entities;
using Griff.Editor.Features.Hierarchy;
using Griff.Editor.Features.Inspector;
using Griff.Editor.Features.Output;
using Griff.Editor.Features.Play;
using Griff.Editor.Features.Projects;
using Griff.Editor.Features.Scenes;
using Griff.Editor.Features.SceneView;
using Griff.Editor.Features.Scripts;
using Griff.Editor.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Griff.Editor;

/// <summary>Composition root: builds the host, loads modules and shows the shell.</summary>
public partial class App : Application
{
    // Add new feature modules here.
    internal static readonly IEditorModule[] Modules =
    [
        new ProjectsModule(),
        new ScenesModule(),
        new EditingModule(),
        new EntitiesModule(),
        new PlayModule(),
        new ScriptsModule(),
        new BuildModule(),
        new HierarchyModule(),
        new InspectorModule(),
        new AssetsModule(),
        new AudioModule(),
        new ArtModule(),
        new ControlsModule(),
        new OutputModule(),
        new DesignSystemModule(),
        new AgentModule(),
    ];

    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var builder = Host.CreateApplicationBuilder(e.Args);
        builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
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
        logger.LogInformation("Griff Editor started ({Modules} modules)", Modules.Length);
        await ReopenLastProjectAsync(logger);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
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
        services.AddSingleton<IWorkspace, Workspace>();
        services.AddSingleton<ISelection, Selection>();
        services.AddSingleton<CommandRegistry>();

        services.AddMenu(Menus.File, "_File", order: 0);
        services.AddMenu(Menus.Edit, "_Edit", order: 1);
        services.AddMenu(Menus.View, "_View", order: 2);
        services.AddMenu(Menus.Help, "_Help", order: 100);

        services.AddSingleton<MenuBuilder>();
        services.AddSingleton<ToolbarBuilder>();
        services.AddSingleton<PanelHost>();
        services.AddSingleton<SceneViewModel>();
        services.AddSingleton<SceneViewControl>();
        services.AddCommand<FrameSelectionCommand>();
        services.AddSingleton<StartPage>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
    }

    // Jams are short: pick up exactly where you left off.
    private async Task ReopenLastProjectAsync(ILogger logger)
    {
        var workspace = _host!.Services.GetRequiredService<IWorkspace>();
        if (workspace.RecentProjects.FirstOrDefault() is not { } last)
            return;

        try
        {
            await workspace.OpenProjectAsync(last);
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
