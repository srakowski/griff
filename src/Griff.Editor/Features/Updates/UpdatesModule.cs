using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Griff.Editor.Features.Updates;

/// <summary>The installer's updates: Help > Check for Updates, and a check a little after the editor starts.</summary>
public sealed class UpdatesModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IUpdater, VelopackUpdater>();
        services.AddSingleton<EditorUpdates>();
        services.AddHostedService<CheckForUpdatesAtStart>();
        services
            .AddCommand<CheckForUpdatesCommand>()
            .AddCommand<CheckForUpdatesAutomaticallyCommand>();
    }
}

/// <summary>Starts the check after the editor starts, without holding up the start.</summary>
internal sealed class CheckForUpdatesAtStart(EditorUpdates updates) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = updates.CheckAfterStartAsync(EditorUpdates.StartupDelay);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Checks Griff's releases for a newer version of the editor, in the Updates window: it shows the check and the download
/// (with how far it's got) as they go, then what came of it: up to date, or why it couldn't check. When there's a newer
/// version it's downloaded, then asked: restart now to use it ("Update ready": true; unsaved changes are asked about first,
/// as closing asks), or let it be put in place the next time Griff starts (false). A Griff installed from a
/// pre-release takes newer pre-releases and the first newer release; one installed from a release takes releases only.
/// Only a Griff installed with the installer updates itself; a build from source or the portable zip says so and does nothing.
/// </summary>
public sealed class CheckForUpdatesCommand(EditorUpdates updates) : EditorCommand
{
    public override string Id => "Help.CheckForUpdates";
    public override string Text => "Check for _Updates...";
    public override MenuPlacement? Placement { get; } = new(Menus.Help, Group: 2, Order: 0);

    protected override async Task ExecuteCoreAsync() => Outcome = await updates.CheckAsync(userAsked: true);
}

/// <summary>
/// Turns the check for a newer Griff a little after the editor starts on or off (on at first). It's remembered. Help > Check
/// for Updates still checks when it's off.
/// </summary>
public sealed class CheckForUpdatesAutomaticallyCommand(EditorUpdates updates, Core.Status.IStatusService status) : EditorCommand
{
    public override string Id => "Help.CheckForUpdatesAutomatically";
    public override string Text => "Check for Updates _Automatically";
    public override MenuPlacement? Placement { get; } = new(Menus.Help, Group: 2, Order: 1);
    public override bool? IsChecked => updates.ChecksAutomatically;

    protected override Task ExecuteCoreAsync()
    {
        updates.ChecksAutomatically = !updates.ChecksAutomatically;
        Outcome = updates.ChecksAutomatically
            ? "Griff looks for updates a little after it starts."
            : "Griff no longer looks for updates by itself; Help > Check for Updates still does.";
        status.Show(Outcome);
        return Task.CompletedTask;
    }
}
