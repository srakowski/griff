using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Griff.Agent;

/// <summary>
/// One thing an agent can do, as the editor's MCP server offers it: a name, what it does, the arguments it takes (a JSON
/// schema) and the code that does it. Tools aren't written one by one; <see cref="IAgentToolSource"/>s project them
/// from what the editor already has (its commands, its components, its services), so a new feature shows up by itself.
/// </summary>
public sealed partial class AgentTool
{
    /// <summary>Letters, digits, '_' and '-', 1 to 64 long: what MCP clients accept.</summary>
    public required string Name { get; init; }

    /// <summary>A short human-readable name, e.g. "Create Platformer Player".</summary>
    public string? Title { get; init; }

    public required string Description { get; init; }

    /// <summary>A JSON schema for the arguments object.</summary>
    public JsonObject InputSchema { get; init; } = Schema.Object();

    /// <summary>True if it only looks: an agent can call it freely.</summary>
    public bool ReadOnly { get; init; }

    /// <summary>True if it can throw work away (delete, close, overwrite).</summary>
    public bool Destructive { get; init; }

    public required Func<ToolArgs, Task<ToolResult>> Invoke { get; init; }

    public static bool IsValidName(string name) => ValidName().IsMatch(name);

    /// <summary>A tool name from anything, e.g. "Entity.CreatePlatformerPlayer" becomes "entity_create_platformer_player".</summary>
    public static string NameFrom(string text)
    {
        var words = Words().Matches(text).Select(m => m.Value.ToLowerInvariant());
        var name = string.Join("_", words);
        return name.Length > 64 ? name[..64] : name;
    }

    [GeneratedRegex("^[a-zA-Z0-9_-]{1,64}$")]
    private static partial Regex ValidName();

    // Runs of capitals (an acronym), capitalized words, lowercase words and numbers.
    [GeneratedRegex("[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+")]
    private static partial Regex Words();
}

/// <summary>Something that offers tools. Asked again each time the tools are listed, so what it offers can change.</summary>
public interface IAgentToolSource
{
    IEnumerable<AgentTool> GetTools();
}

/// <summary>A tool failed in a way the agent can fix: a bad argument, nothing selected, a missing entity.</summary>
public class AgentException(string message) : Exception(message);

/// <summary>A tool's arguments, with readers that explain what's wrong when something is.</summary>
public sealed class ToolArgs(JsonElement root)
{
    public static ToolArgs Empty { get; } = new(JsonDocument.Parse("{}").RootElement);

    public static ToolArgs Parse(string json) => new(JsonDocument.Parse(json).RootElement.Clone());

    public JsonElement Root { get; } = root.ValueKind == JsonValueKind.Object ? root : Empty.Root;

    public bool Has(string name) => TryGet(name, out _);

    public bool TryGet(string name, out JsonElement value)
    {
        if (Root.TryGetProperty(name, out value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            return true;
        value = default;
        return false;
    }

    public JsonElement Required(string name) =>
        TryGet(name, out var value) ? value : throw new AgentException($"\"{name}\" is required.");

    public string String(string name) => Text(Required(name), name);

    public string? OptionalString(string name) => TryGet(name, out var value) ? Text(value, name) : null;

    public bool Bool(string name, bool fallback = false) =>
        !TryGet(name, out var value) ? fallback
        : value.ValueKind == JsonValueKind.True ? true
        : value.ValueKind == JsonValueKind.False ? false
        : value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) ? parsed
        : throw new AgentException($"\"{name}\" should be true or false.");

    public double Number(string name, double fallback) =>
        !TryGet(name, out var value) ? fallback
        : value.ValueKind == JsonValueKind.Number ? value.GetDouble()
        : value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed
        : throw new AgentException($"\"{name}\" should be a number.");

    public int Int(string name, int fallback) => (int)Math.Round(Number(name, fallback));

    /// <summary>A list of strings; a single string counts as a list of one.</summary>
    public IReadOnlyList<string> Strings(string name) =>
        !TryGet(name, out var value) ? []
        : value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(e => Text(e, name)).ToList()
        : [Text(value, name)];

    private static string Text(JsonElement value, string name) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        _ => throw new AgentException($"\"{name}\" should be text."),
    };
}

/// <summary>What a tool gives back: text and pictures, or an error the agent should read and act on.</summary>
public sealed class ToolResult
{
    private ToolResult(IReadOnlyList<JsonObject> content, bool isError)
    {
        Content = content;
        IsError = isError;
    }

    public IReadOnlyList<JsonObject> Content { get; }

    public bool IsError { get; }

    /// <summary>All the text blocks, joined; handy in tests.</summary>
    public string Text => string.Join("\n", Content.Where(c => (string?)c["type"] == "text").Select(c => (string?)c["text"]));

    public static ToolResult Ok(string text) => new([TextBlock(text)], false);

    public static ToolResult Error(string text) => new([TextBlock(text)], true);

    public static ToolResult Json(JsonNode? node) => Ok(node?.ToJsonString(Pretty) ?? "null");

    /// <summary>A PNG picture, with a caption.</summary>
    public static ToolResult Image(byte[] png, string caption) =>
        new([TextBlock(caption), new JsonObject { ["type"] = "image", ["data"] = Convert.ToBase64String(png), ["mimeType"] = "image/png" }], false);

    public JsonObject ToJson() => new()
    {
        ["content"] = new JsonArray(Content.Select(c => (JsonNode)c.DeepClone()).ToArray()),
        ["isError"] = IsError,
    };

    /// <summary>Indented, with quotes and arrows left readable (agents read this, not browsers).</summary>
    public static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() };

    /// <summary>On one line, readable.</summary>
    public static readonly JsonSerializerOptions Compact = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() };

    private static JsonObject TextBlock(string text) => new() { ["type"] = "text", ["text"] = text };
}

/// <summary>Small helpers for writing JSON schemas.</summary>
public static class Schema
{
    /// <summary>An object schema; <paramref name="properties"/> are (name, schema) pairs and <paramref name="required"/> lists names.</summary>
    public static JsonObject Object(IEnumerable<(string Name, JsonObject Schema)>? properties = null, params string[] required)
    {
        var props = new JsonObject();
        foreach (var (name, schema) in properties ?? [])
            props[name] = schema.Parent is null ? schema : schema.DeepClone(); // a schema can be shared between tools
        var result = new JsonObject { ["type"] = "object", ["properties"] = props };
        if (required.Length > 0)
            result["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray());
        return result;
    }

    public static JsonObject String(string description, IEnumerable<string>? choices = null)
    {
        var schema = new JsonObject { ["type"] = "string", ["description"] = description };
        if (choices is not null)
            schema["enum"] = new JsonArray(choices.Select(c => (JsonNode)c).ToArray());
        return schema;
    }

    public static JsonObject Number(string description) => new() { ["type"] = "number", ["description"] = description };

    public static JsonObject Integer(string description) => new() { ["type"] = "integer", ["description"] = description };

    public static JsonObject Bool(string description) => new() { ["type"] = "boolean", ["description"] = description };

    public static JsonObject Array(JsonObject items, string description) => new() { ["type"] = "array", ["items"] = items, ["description"] = description };

    /// <summary>Any JSON value (a value whose kind depends on something else, like a component property).</summary>
    public static JsonObject Any(string description) => new() { ["description"] = description };

    /// <summary>An object with free-form keys.</summary>
    public static JsonObject Map(string description) => new() { ["type"] = "object", ["description"] = description, ["additionalProperties"] = true };
}
