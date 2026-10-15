using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Agent;
using Griff.Editor.Core.Settings;
using Griff.Editor.Core.Status;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Griff.Editor.Features.Agent;

/// <summary>
/// The editor's MCP server: lets agents like Claude Code drive the editor at <c>http://127.0.0.1:7650/mcp</c> (the port
/// can be changed with GRIFF_MCP_PORT). Its tools come from every <see cref="IAgentToolSource"/> the modules register;
/// the built-in ones project the editor's commands, components, services, panels and windows, so features don't add
/// tools by hand. Calls run on the UI thread, one undo step each, with the editor's dialogs answered by the call.
/// </summary>
public sealed partial class AgentServer : ObservableObject, IHostedService
{
    public const string Instructions =
        """
        This is Griff, a 2D game engine and editor, running on the user's machine; you're driving it live.
        - Start with editor_state. Project_New (a template, answering its window) or Project_Open gets a project going.
        - scene_tree shows the open scene; entity_get shows an entity in full. entity_create / entity_update / entity_delete change it, one undo step per call (Edit_Undo undoes).
        - components_list and component_describe say what every component does and what its properties take. Any component (scripts too) can be added through entity_create/entity_update.
        - Tools named like Entity_CreatePlatformerPlayer are the editor's own commands (menus and toolbar). Ones that act on the selection take "entity". If a command asks a question, it says so; call again with "answers". The Entity_Create ones make ready-made things (players, enemies, UI, level pieces, sounds, lights, a genre module's boards and units), each saying its Entity menu category (Menu: Entity > Characters > Grid Enemy).
        - Scripts, sprites, sounds and scenes are files: asset_write writes them (scripts compile at once and report errors), asset_read reads them.
        - Anything else the editor can do (the Sprite Painter, Sound Maker, Controls window, the workspace's methods...) is reachable with ui_roots, ui_inspect, ui_set and ui_invoke.
        - Check your work with editor_screenshot. Playtest with Game_Play, game_input and Game_Stop (play-mode changes are thrown away on stop). Scene_Save saves.
        """;

    private readonly McpServer _server;
    private readonly ISettingsStore _settings;
    private readonly IStatusService _status;
    private readonly ILogger<AgentServer> _logger;
    private McpHttpTransport? _transport;

    public AgentServer(IEnumerable<IAgentToolSource> sources, ISettingsStore settings, IStatusService status, ILogger<AgentServer> logger)
    {
        _settings = settings;
        _status = status;
        _logger = logger;
        var version = typeof(AgentServer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0";
        _server = new McpServer(sources, "griff", version.Split('+')[0], Instructions, Dispatch);
        _server.ToolCalled += (tool, failed) => _status.Show(failed ? $"Agent: {tool} failed" : $"Agent: {tool}");
    }

    /// <summary>The server's tools as they are now.</summary>
    public IReadOnlyDictionary<string, AgentTool> Tools => _server.Tools();

    /// <summary>
    /// Calls a tool the way an agent's request does (on the UI thread, answering the editor's questions from the call),
    /// without needing the server to be listening: what the demo player's <c>agent:</c> command uses to show a call and check it works.
    /// </summary>
    public async Task<(bool Failed, string Text)> CallAsync(string tool, string argumentsJson)
    {
        var request = new System.Text.Json.Nodes.JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = "tools/call",
            ["params"] = new System.Text.Json.Nodes.JsonObject
            {
                ["name"] = tool,
                ["arguments"] = System.Text.Json.Nodes.JsonNode.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson),
            },
        };
        var answer = System.Text.Json.Nodes.JsonNode.Parse(await _server.HandleAsync(request.ToJsonString()) ?? "{}")!;
        if (answer["error"] is { } error)
            return (true, (string?)error["message"] ?? "error");
        var content = answer["result"]?["content"] as System.Text.Json.Nodes.JsonArray;
        var text = string.Join(Environment.NewLine, (content ?? []).Where(c => (string?)c?["type"] == "text").Select(c => (string?)c!["text"]));
        return ((bool?)answer["result"]?["isError"] == true, text);
    }

    public bool IsRunning => _transport?.IsRunning == true;

    /// <summary>Where agents connect, while running.</summary>
    public string? Url => _transport?.Url;

    /// <summary>The command that adds this server to Claude Code.</summary>
    public string SetupCommand => $"claude mcp add --transport http griff {Url ?? $"http://127.0.0.1:{Port}/mcp"}";

    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("GRIFF_MCP_PORT"), out var port) ? port : McpHttpTransport.DefaultPort;

    /// <summary>Starts or stops the server, and remembers the choice for next time.</summary>
    public bool Enabled
    {
        get => IsRunning;
        set
        {
            _settings.Settings.AgentServer = value;
            _settings.Save();
            if (value)
                Start();
            else
                _ = StopAsync();
        }
    }

    Task IHostedService.StartAsync(CancellationToken cancellationToken)
    {
        if (_settings.Settings.AgentServer && Environment.GetEnvironmentVariable("GRIFF_MCP") != "0")
            Start();
        return Task.CompletedTask;
    }

    Task IHostedService.StopAsync(CancellationToken cancellationToken) => StopAsync();

    private void Start()
    {
        if (IsRunning)
            return;
        var transport = new McpHttpTransport(_server, Port);
        try
        {
            transport.Start();
        }
        catch (SocketException ex)
        {
            _logger.LogWarning("The MCP server couldn't listen on port {Port}: {Message}. Another Griff may have it; set GRIFF_MCP_PORT to use another.", Port, ex.Message);
            return;
        }

        _transport = transport;
        _logger.LogInformation("MCP server listening on {Url}. Add it to Claude Code with: {Setup}", transport.Url, SetupCommand);
        Raise();
    }

    private async Task StopAsync()
    {
        if (_transport is not { } transport)
            return;
        _transport = null;
        // The host stops services from the UI thread while blocking it, so don't come back to it to finish.
        await transport.DisposeAsync().ConfigureAwait(false);
        _logger.LogInformation("MCP server stopped");
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.HasShutdownStarted)
            _ = dispatcher.InvokeAsync(Raise);
    }

    private void Raise()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(Url));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    // A call that waited this long for the UI thread was held up by something else the editor was doing.
    private static readonly TimeSpan SlowQueue = TimeSpan.FromSeconds(2);

    // Every call runs on the UI thread, where the editor lives, with its questions answered by the call's "answers".
    private Task<ToolResult> Dispatch(AgentTool tool, ToolArgs args, Func<Task<ToolResult>> call)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
            return Task.FromResult(ToolResult.Error("The editor is closing."));

        var queued = Stopwatch.StartNew();
        return dispatcher.InvokeAsync(async () =>
        {
            // Said in the Output panel, so a call that takes long can be told apart: waiting behind the editor's own work, or its own.
            if (queued.Elapsed > SlowQueue)
                _logger.LogWarning("{Tool} waited {Seconds:0.0} s for the editor to be free before it could start.", tool.Name, queued.Elapsed.TotalSeconds);
            using (AgentDialogs.Answering(args.TryGet("answers", out var answers) ? answers : null))
                return await call();
        }).Task.Unwrap();
    }
}
