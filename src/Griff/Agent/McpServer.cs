using System.Text.Json;
using System.Text.Json.Nodes;

namespace Griff.Agent;

/// <summary>
/// The Model Context Protocol (MCP), the way agents like Claude drive tools: JSON-RPC messages in, responses out.
/// It knows nothing about the transport (see <see cref="McpHttpTransport"/>) or about Griff; it serves whatever tools its
/// sources offer at the moment they're asked for.
/// </summary>
public sealed class McpServer
{
    /// <summary>The newest protocol version this server speaks; older ones it also accepts are answered in kind.</summary>
    public const string LatestProtocolVersion = "2025-06-18";

    private static readonly string[] SupportedVersions = ["2025-06-18", "2025-03-26", "2024-11-05"];

    private readonly IReadOnlyList<IAgentToolSource> _sources;
    private readonly Func<AgentTool, ToolArgs, Func<Task<ToolResult>>, Task<ToolResult>> _dispatch;

    /// <param name="dispatch">
    /// Runs each tool call, given the tool, its arguments and the call itself: e.g. on the UI thread, inside some scope.
    /// Defaults to running it where it's called.
    /// </param>
    public McpServer(IEnumerable<IAgentToolSource> sources, string name, string version, string? instructions = null,
        Func<AgentTool, ToolArgs, Func<Task<ToolResult>>, Task<ToolResult>>? dispatch = null)
    {
        _sources = sources.ToList();
        Name = name;
        Version = version;
        Instructions = instructions;
        _dispatch = dispatch ?? ((_, _, call) => call());
    }

    public string Name { get; }

    public string Version { get; }

    public string? Instructions { get; }

    /// <summary>Raised after each tool call, with the tool's name and whether it failed. For the editor's status line.</summary>
    public event Action<string, bool>? ToolCalled;

    /// <summary>Every tool on offer now, by name. Two tools can't share a name.</summary>
    public IReadOnlyDictionary<string, AgentTool> Tools()
    {
        var tools = new Dictionary<string, AgentTool>(StringComparer.Ordinal);
        foreach (var tool in _sources.SelectMany(s => s.GetTools()))
        {
            if (!AgentTool.IsValidName(tool.Name))
                throw new InvalidOperationException($"\"{tool.Name}\" isn't a valid tool name.");
            if (!tools.TryAdd(tool.Name, tool))
                throw new InvalidOperationException($"Two tools are called \"{tool.Name}\".");
        }

        return tools;
    }

    /// <summary>Handles one JSON-RPC message (or a batch). Returns the response, or null for a notification.</summary>
    public async Task<string?> HandleAsync(string json)
    {
        JsonNode? message;
        try
        {
            message = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            return ErrorResponse(null, -32700, "Parse error: " + ex.Message).ToJsonString();
        }

        if (message is JsonArray batch)
        {
            var responses = new JsonArray();
            foreach (var item in batch)
            {
                if (await HandleMessageAsync(item) is { } response)
                    responses.Add(response);
            }

            return responses.Count == 0 ? null : responses.ToJsonString();
        }

        return (await HandleMessageAsync(message))?.ToJsonString();
    }

    private async Task<JsonObject?> HandleMessageAsync(JsonNode? message)
    {
        if (message is not JsonObject request || request["method"]?.GetValue<string>() is not { } method)
        {
            // A response to something we asked (we don't ask anything), or nonsense.
            return message is JsonObject { } o && o.ContainsKey("id") && !o.ContainsKey("result") && !o.ContainsKey("error")
                ? ErrorResponse(o["id"], -32600, "Invalid request")
                : null;
        }

        var id = request["id"];
        var isNotification = !request.ContainsKey("id");
        var parameters = request["params"] as JsonObject ?? [];

        try
        {
            JsonNode? result = method switch
            {
                "initialize" => Initialize(parameters),
                "ping" => new JsonObject(),
                "tools/list" => ListTools(),
                "tools/call" => await CallToolAsync(parameters),
                _ when method.StartsWith("notifications/", StringComparison.Ordinal) => null,
                _ => throw new RpcException(-32601, $"Method not found: {method}"),
            };

            if (isNotification)
                return null;
            return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = result ?? new JsonObject() };
        }
        catch (RpcException ex)
        {
            return isNotification ? null : ErrorResponse(id, ex.Code, ex.Message);
        }
    }

    private JsonObject Initialize(JsonObject parameters)
    {
        var requested = parameters["protocolVersion"]?.GetValue<string>();
        var version = requested is not null && SupportedVersions.Contains(requested) ? requested : LatestProtocolVersion;
        var result = new JsonObject
        {
            ["protocolVersion"] = version,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject { ["name"] = Name, ["version"] = Version },
        };
        if (Instructions is not null)
            result["instructions"] = Instructions;
        return result;
    }

    private JsonObject ListTools()
    {
        var tools = new JsonArray();
        foreach (var tool in Tools().Values)
        {
            var entry = new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = tool.InputSchema.DeepClone(),
            };
            if (tool.Title is not null)
                entry["title"] = tool.Title;
            entry["annotations"] = new JsonObject
            {
                ["title"] = tool.Title ?? tool.Name,
                ["readOnlyHint"] = tool.ReadOnly,
                ["destructiveHint"] = tool.Destructive,
                ["openWorldHint"] = false,
            };
            tools.Add(entry);
        }

        return new JsonObject { ["tools"] = tools };
    }

    private async Task<JsonObject> CallToolAsync(JsonObject parameters)
    {
        var name = parameters["name"]?.GetValue<string>() ?? throw new RpcException(-32602, "tools/call needs a \"name\".");
        AgentTool? tool;
        try
        {
            tool = Tools().GetValueOrDefault(name);
        }
        catch (Exception ex)
        {
            throw new RpcException(-32603, ex.Message);
        }

        if (tool is null)
            throw new RpcException(-32602, $"Unknown tool: {name}");

        var args = parameters["arguments"] is JsonObject arguments ? ToolArgs.Parse(arguments.ToJsonString()) : ToolArgs.Empty;
        var result = await _dispatch(tool, args, async () =>
        {
            try
            {
                return await tool.Invoke(args);
            }
            catch (AgentException ex)
            {
                return ToolResult.Error(ex.Message);
            }
            catch (Exception ex)
            {
                return ToolResult.Error($"{ex.GetType().Name}: {ex.Message}");
            }
        });

        ToolCalled?.Invoke(name, result.IsError);
        return result.ToJson();
    }

    private static JsonObject ErrorResponse(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    };

    private sealed class RpcException(int code, string message) : Exception(message)
    {
        public int Code { get; } = code;
    }
}
