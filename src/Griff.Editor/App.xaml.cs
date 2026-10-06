using System.Windows;
using System.Windows.Threading;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Documents;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Status;
using Griff.Editor.Features.DesignSystem;
using Griff.Editor.Features.Files;
using Griff.Editor.Features.Output;
using Griff.Editor.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Griff.Editor;

/// <summary>Composition root: builds the host, loads modules and shows the shell.</summary>
public partial class App : Application
{
    // Add new feature modules here.
    private static readonly IEditorModule[] Modules =
    [
        new FilesModule(),
        new OutputModule(),
        new DesignSystemModule(),
    ];

    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
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
        MainWindow.Show();

        _host.Services.GetRequiredService<ILogger<App>>()
            .LogInformation("Griff Editor started ({Modules} modules)", Modules.Length);
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

    private static void ConfigureCoreServices(IServiceCollection services)
    {
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IStatusService, StatusService>();
        services.AddSingleton<IDocumentService, DocumentService>();
        services.AddSingleton<CommandRegistry>();

        services.AddMenu(Menus.File, "_File", order: 0);
        services.AddMenu(Menus.Edit, "_Edit", order: 1);
        services.AddMenu(Menus.View, "_View", order: 2);
        services.AddMenu(Menus.Help, "_Help", order: 100);

        services.AddSingleton<MenuBuilder>();
        services.AddSingleton<ToolbarBuilder>();
        services.AddSingleton<PanelHost>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
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
