using System.Text.Json;
using System.Text.Json.Nodes;
using Griff.Scenes.Serialization;

namespace Griff.Scenes;

/// <summary>
/// A component's saved values as an <see cref="UpgradeFromAttribute"/> step sees them, before they're read into the
/// component. Properties are named as they are in code ("MoveSpeed"); values are the same types the component's
/// properties have (numbers, text, vectors, colors, enums, lists of lines).
/// </summary>
public sealed class SavedData
{
    /// <summary>Wraps saved JSON, e.g. to try out an upgrade step in a test.</summary>
    public SavedData(JsonObject json) => Json = json;

    /// <summary>The raw JSON, for changes the other methods don't cover. Keys are camelCase ("moveSpeed").</summary>
    public JsonObject Json { get; }

    /// <summary>True if a value was saved for <paramref name="property"/>.</summary>
    public bool Has(string property) => Json.ContainsKey(Key(property));

    /// <summary>The saved value of <paramref name="property"/>, or <paramref name="fallback"/> if there's none or it isn't a <typeparamref name="T"/>.</summary>
    public T? Get<T>(string property, T? fallback = default)
    {
        if (Json[Key(property)] is not { } node)
            return fallback;
        try
        {
            return (T?)ValueCodec.Read(JsonSerializer.SerializeToElement(node), typeof(T));
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    /// <summary>Saves <paramref name="value"/> as <paramref name="property"/>, replacing what was there.</summary>
    public void Set<T>(string property, T value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            ValueCodec.Write(writer, value);
        Json[Key(property)] = JsonNode.Parse(stream.ToArray());
    }

    /// <summary>Moves the value of <paramref name="from"/> to <paramref name="to"/> (nothing happens if there's none).</summary>
    public void Rename(string from, string to)
    {
        if (!Json.Remove(Key(from), out var value))
            return;
        Json[Key(to)] = value;
    }

    /// <summary>Forgets the value of <paramref name="property"/>.</summary>
    public void Remove(string property) => Json.Remove(Key(property));

    /// <summary>
    /// In a saved game (as an <see cref="Runtime.ISaveUpgrader"/> sees it), a script's own fields: its private fields and
    /// the rest a saved game keeps besides its properties, or what it saves itself (<see cref="Runtime.ISavesState"/>),
    /// to read and change by name as properties are ("_wave" for a field called _wave, "Day" for an auto property's).
    /// Null if none were saved. A field holding an object or a list points to it in the save's objects (see
    /// <see cref="Runtime.SaveUpgrade.Objects"/>).
    /// </summary>
    public SavedData? Fields => Json[FieldsKey] is JsonObject fields ? new SavedData(fields) : null;

    // Where a component's fields are, in a saved game's scene.
    internal const string FieldsKey = "$fields";

    internal static string Key(string property) => JsonNamingPolicy.CamelCase.ConvertName(property);
}
