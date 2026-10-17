using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using Griff.Agent;

namespace Griff.Tests.Agent;

public class McpServerTests
{
    private sealed class Source(params AgentTool[] tools) : IAgentToolSource
    {
        public List<AgentTool> Tools { get; } = [.. tools];

        public IEnumerable<AgentTool> GetTools() => Tools;
    }

    private static AgentTool Echo(string name = "echo") => new()
    {
        Name = name,
        Description = "Says it back.",
        InputSchema = Schema.Object([("text", Schema.String("What to say."))], "text"),
        Invoke = args => Task.FromResult(ToolResult.Ok(args.String("text"))),
    };

    private static async Task<JsonObject> Call(McpServer server, string method, JsonObject? parameters = null, int id = 1)
    {
        var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method };
        if (parameters is not null)
            request["params"] = parameters;
        return JsonNode.Parse((await server.HandleAsync(request.ToJsonString()))!)!.AsObject();
    }

    [Fact]
    public async Task Initialize_AnswersWithTheClientsVersionAndTheTools()
    {
        var server = new McpServer([new Source(Echo())], "griff", "1.0", "Use the tools.");

        var response = await Call(server, "initialize", new JsonObject { ["protocolVersion"] = "2025-03-26", ["capabilities"] = new JsonObject() });

        Assert.Equal("2025-03-26", (string?)response["result"]!["protocolVersion"]);
        Assert.Equal("griff", (string?)response["result"]!["serverInfo"]!["name"]);
        Assert.Equal("Use the tools.", (string?)response["result"]!["instructions"]);
        Assert.NotNull(response["result"]!["capabilities"]!["tools"]);
    }

    [Fact]
    public async Task Initialize_WithAnUnknownVersion_OffersTheLatest()
    {
        var server = new McpServer([], "griff", "1.0");

        var response = await Call(server, "initialize", new JsonObject { ["protocolVersion"] = "1999-01-01" });

        Assert.Equal(McpServer.LatestProtocolVersion, (string?)response["result"]!["protocolVersion"]);
    }

    [Fact]
    public async Task ToolsList_DescribesEveryTool()
    {
        var server = new McpServer([new Source(Echo())], "griff", "1.0");

        var tools = (await Call(server, "tools/list"))["result"]!["tools"]!.AsArray();

        var echo = Assert.Single(tools)!;
        Assert.Equal("echo", (string?)echo["name"]);
        Assert.Equal("Says it back.", (string?)echo["description"]);
        Assert.Equal("object", (string?)echo["inputSchema"]!["type"]);
        Assert.Equal("text", (string?)echo["inputSchema"]!["required"]![0]);
    }

    [Fact]
    public async Task ToolsList_AsksTheSourcesEachTime_SoNewToolsAppear()
    {
        var source = new Source(Echo());
        var server = new McpServer([source], "griff", "1.0");
        Assert.Single((await Call(server, "tools/list"))["result"]!["tools"]!.AsArray());

        source.Tools.Add(Echo("echo_again"));

        Assert.Equal(2, (await Call(server, "tools/list"))["result"]!["tools"]!.AsArray().Count);
    }

    [Fact]
    public async Task ToolsCall_RunsTheTool()
    {
        var server = new McpServer([new Source(Echo())], "griff", "1.0");

        var result = (await Call(server, "tools/call", new JsonObject { ["name"] = "echo", ["arguments"] = new JsonObject { ["text"] = "hi" } }))["result"]!;

        Assert.False((bool)result["isError"]!);
        Assert.Equal("hi", (string?)result["content"]![0]!["text"]);
    }

    [Fact]
    public async Task ToolsCall_ReportsAMistakeAsAToolError()
    {
        var server = new McpServer([new Source(Echo())], "griff", "1.0");

        var result = (await Call(server, "tools/call", new JsonObject { ["name"] = "echo", ["arguments"] = new JsonObject() }))["result"]!;

        Assert.True((bool)result["isError"]!);
        Assert.Contains("\"text\" is required", (string?)result["content"]![0]!["text"]);
    }

    [Fact]
    public async Task ToolsCall_WithAnArgumentTheToolDoesntTake_IsRefused_NotDropped()
    {
        var called = false;
        var echo = new AgentTool
        {
            Name = "echo",
            Description = "Says it back.",
            InputSchema = Schema.Object([("text", Schema.String("What to say."))], "text"),
            Invoke = args =>
            {
                called = true;
                return Task.FromResult(ToolResult.Ok(args.String("text")));
            },
        };
        var server = new McpServer([new Source(echo)], "griff", "1.0");

        var result = (await Call(server, "tools/call", new JsonObject
        {
            ["name"] = "echo",
            ["arguments"] = new JsonObject { ["text"] = "hi", ["method"] = "Shout" },
        }))["result"]!;

        Assert.True((bool)result["isError"]!);
        Assert.Equal("echo doesn't take \"method\": it takes \"text\".", (string?)result["content"]![0]!["text"]);
        Assert.False(called);
    }

    [Fact]
    public async Task ToolsCall_AnUnknownTool_IsAProtocolError()
    {
        var server = new McpServer([new Source(Echo())], "griff", "1.0");

        var response = await Call(server, "tools/call", new JsonObject { ["name"] = "nope" });

        Assert.Equal(-32602, (int)response["error"]!["code"]!);
    }

    [Fact]
    public async Task ToolsCall_GoesThroughTheDispatcher()
    {
        var seen = new List<string>();
        var server = new McpServer([new Source(Echo())], "griff", "1.0", dispatch: (tool, args, call) =>
        {
            seen.Add($"{tool.Name}:{args.String("text")}");
            return call();
        });

        await Call(server, "tools/call", new JsonObject { ["name"] = "echo", ["arguments"] = new JsonObject { ["text"] = "x" } });

        Assert.Equal(["echo:x"], seen);
    }

    [Fact]
    public async Task Notifications_GetNoAnswer_AndUnknownMethodsAreErrors()
    {
        var server = new McpServer([], "griff", "1.0");

        Assert.Null(await server.HandleAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));
        Assert.Equal(-32601, (int)(await Call(server, "resources/subscribe"))["error"]!["code"]!);
        Assert.Equal(-32700, (int)JsonNode.Parse((await server.HandleAsync("{not json"))!)!["error"]!["code"]!);
    }

    [Fact]
    public void Tools_WithTheSameName_AreRefused()
    {
        var server = new McpServer([new Source(Echo()), new Source(Echo())], "griff", "1.0");

        Assert.Throws<InvalidOperationException>(() => server.Tools());
    }

    [Theory]
    [InlineData("Entity.CreatePlatformerPlayer", "entity_create_platformer_player")]
    [InlineData("Game.PlayWith2", "game_play_with_2")]
    [InlineData("UI Roots", "ui_roots")]
    public void NameFrom_MakesValidToolNames(string text, string expected)
    {
        Assert.Equal(expected, AgentTool.NameFrom(text));
        Assert.True(AgentTool.IsValidName(expected));
    }

    [Fact]
    public async Task Http_ServesJsonRpcOnLoopback_AndTurnsAwayWebPages()
    {
        var server = new McpServer([new Source(Echo())], "griff", "1.0");
        await using var transport = new McpHttpTransport(server, port: 0);
        transport.Start();
        using var http = new HttpClient();

        var body = new StringContent("""{"jsonrpc":"2.0","id":7,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}""", Encoding.UTF8, "application/json");
        var response = await http.PostAsync(transport.Url, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Mcp-Session-Id"));
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal(7, (int)json["id"]!);

        // A second request on the same connection, then a tool call.
        var call = await http.PostAsync(transport.Url, new StringContent(
            """{"jsonrpc":"2.0","id":8,"method":"tools/call","params":{"name":"echo","arguments":{"text":"over http"}}}""", Encoding.UTF8, "application/json"));
        Assert.Contains("over http", await call.Content.ReadAsStringAsync());

        var notification = await http.PostAsync(transport.Url, new StringContent("""{"jsonrpc":"2.0","method":"notifications/initialized"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Accepted, notification.StatusCode);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await http.GetAsync(transport.Url)).StatusCode);

        var fromAPage = new HttpRequestMessage(HttpMethod.Post, transport.Url) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        fromAPage.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(fromAPage)).StatusCode);
    }
}
