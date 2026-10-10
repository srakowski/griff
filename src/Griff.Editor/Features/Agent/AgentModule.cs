using System.Windows;
using Griff.Agent;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Status;
using Griff.Editor.Themes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Griff.Editor.Features.Agent;

/// <summary>
/// Lets agents (Claude Code and other MCP clients) drive the editor. Nothing here lists the editor's features: the tool
/// sources read them from where every feature already has to register (commands, components, services, panels, windows,
/// dialogs), so the server grows with the editor. A module can still add an <see cref="IAgentToolSource"/> of its own.
/// </summary>
public sealed class AgentModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(new ServiceCatalog(services));
        services.AddSingleton<IAgentScene, EditorAgentScene>();
        services.AddSingleton<IAgentProject, EditorAgentProject>();

        services.AddSingleton<IAgentToolSource, EditorTools>();
        services.AddSingleton<IAgentToolSource>(sp => new SceneTools(sp.GetRequiredService<IAgentScene>()));
        services.AddSingleton<IAgentToolSource>(sp => new AssetTools(sp.GetRequiredService<IAgentProject>()));
        services.AddSingleton<IAgentToolSource, CommandTools>();
        services.AddSingleton<IAgentToolSource, SurfaceTools>();

        services.AddSingleton<AgentServer>();
        services.AddHostedService(sp => sp.GetRequiredService<AgentServer>());

        // Every question the editor asks can be answered by an agent's call.
        Decorate<IDialogService>(services, (sp, inner) => new AgentDialogs(inner));

        services
            .AddCommand<ToggleAgentServerCommand>()
            .AddCommand<CopyAgentSetupCommand>();
    }

    /// <summary>Wraps the registered <typeparamref name="T"/> in <paramref name="decorate"/>.</summary>
    private static void Decorate<T>(IServiceCollection services, Func<IServiceProvider, T, T> decorate) where T : class
    {
        var original = services.Last(d => d.ServiceType == typeof(T));
        services.Remove(original);
        services.AddSingleton<T>(sp =>
        {
            var inner = original.ImplementationInstance as T
                ?? (original.ImplementationFactory is { } factory ? (T)factory(sp) : (T)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!));
            return decorate(sp, inner);
        });
    }
}

/// <summary>
/// Turns the MCP server on or off: while it's on, agents like Claude Code can drive the editor (on this computer only).
/// It's remembered.
/// </summary>
public sealed class ToggleAgentServerCommand(AgentServer server) : EditorCommand
{
    public override string Id => "Agent.Server";
    public override string Text => "_MCP Server";
    public override string? Icon => Icons.Play;
    public override MenuPlacement? Placement { get; } = new(Menus.Help, Group: 1, Order: 0);
    public override bool? IsChecked => server.IsRunning;

    protected override Task ExecuteCoreAsync()
    {
        server.Enabled = !server.IsRunning;
        return Task.CompletedTask;
    }
}

/// <summary>Copies the command that adds the editor's MCP server to Claude Code, to paste into a terminal.</summary>
public sealed class CopyAgentSetupCommand(AgentServer server, IStatusService status) : EditorCommand
{
    public override string Id => "Agent.CopySetup";
    public override string Text => "Copy MCP _Setup Command";
    public override MenuPlacement? Placement { get; } = new(Menus.Help, Group: 1, Order: 1);

    protected override bool CanExecuteCore() => server.IsRunning;

    protected override Task ExecuteCoreAsync()
    {
        Clipboard.SetText(server.SetupCommand);
        status.Show($"Copied: {server.SetupCommand}");
        return Task.CompletedTask;
    }
}
