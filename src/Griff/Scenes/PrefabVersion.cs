using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Griff.Scenes;

/// <summary>
/// One version of a prefab file: its text as it was then, and an id that says which it is. A placed copy of a prefab
/// remembers the version it was made from (<see cref="Entity.PrefabVersion"/>), so what the copy changed is what differs
/// from that version, and is kept when the prefab changes, however it changed (saved in the editor, written by an agent,
/// brought in by a pull or a text editor). Scene files keep the versions their copies came from.
/// </summary>
[NotState("a prefab file as it was, which never changes")]
public sealed class PrefabVersion : IEquatable<PrefabVersion>
{
    private static readonly JsonWriterOptions Compact = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
    };

    private PrefabVersion(string id, string json) => (Id, Json) = (id, json);

    /// <summary>Which version it is: the same text (however it's spaced or its lines end) is always the same id.</summary>
    public string Id { get; }

    /// <summary>The prefab file's text, as it was in this version (without its spacing).</summary>
    public string Json { get; }

    /// <summary>The version a prefab file's text is.</summary>
    /// <exception cref="InvalidDataException">The text isn't JSON.</exception>
    public static PrefabVersion Of(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return Of(document.RootElement);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The prefab file isn't valid JSON: {ex.Message}", ex);
        }
    }

    /// <summary>The version a prefab file's JSON is; with <paramref name="id"/>, as a scene file kept it under that id.</summary>
    internal static PrefabVersion Of(JsonElement element, string? id = null)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, Compact))
            element.WriteTo(writer);
        return new PrefabVersion(id ?? Hash(buffer.WrittenSpan), Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    // FNV-1a over the compact text, 64 bits: the same everywhere a game runs (a browser too), and plenty to tell a prefab's
    // versions apart.
    private static string Hash(ReadOnlySpan<byte> bytes)
    {
        var hash = 14695981039346656037UL;
        foreach (var b in bytes)
            hash = (hash ^ b) * 1099511628211UL;
        return hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
    }

    public bool Equals(PrefabVersion? other) => other is not null && other.Id == Id;

    public override bool Equals(object? obj) => Equals(obj as PrefabVersion);

    public override int GetHashCode() => Id.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Id;
}
