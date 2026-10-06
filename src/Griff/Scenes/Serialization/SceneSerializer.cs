using System.Text;
using System.Text.Json;

namespace Griff.Scenes.Serialization;

/// <summary>
/// Saves and loads scenes as JSON. Output is deterministic (same scene, same bytes) and nested by
/// hierarchy so files read naturally and merge well. Loading is forgiving: problems with individual
/// components or values are reported as warnings instead of failing the whole scene.
/// </summary>
public sealed class SceneSerializer(ComponentRegistry registry)
{
    public const int FormatVersion = 1;

    private static readonly JsonWriterOptions WriterOptions = new() { Indented = true };

    public void Save(Scene scene, Stream stream)
    {
        using var writer = new Utf8JsonWriter(stream, WriterOptions);
        writer.WriteStartObject();
        writer.WriteNumber("formatVersion", FormatVersion);
        writer.WriteStartArray("entities");
        foreach (var root in scene.Roots)
            WriteEntity(writer, root);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    public string SaveToString(Scene scene)
    {
        using var stream = new MemoryStream();
        Save(scene, stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <param name="warnings">Receives a message for each problem that was skipped over.</param>
    /// <exception cref="InvalidDataException">The file isn't a scene, or is from a newer version.</exception>
    public Scene Load(Stream stream, ICollection<string>? warnings = null)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(stream);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The scene file isn't valid JSON: {ex.Message}", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("entities", out var entities))
                throw new InvalidDataException("The file isn't a Griff scene.");

            var version = root.TryGetProperty("formatVersion", out var v) ? v.GetInt32() : FormatVersion;
            if (version > FormatVersion)
                throw new InvalidDataException("The scene was saved by a newer version of Griff.");

            var scene = new Scene();
            var context = new LoadContext(warnings);
            foreach (var element in entities.EnumerateArray())
                scene.Add(ReadEntity(element, context));
            return scene;
        }
    }

    public Scene LoadFromString(string json, ICollection<string>? warnings = null) =>
        Load(new MemoryStream(Encoding.UTF8.GetBytes(json)), warnings);

    /// <summary>
    /// Deep-copies <paramref name="source"/> and its descendants with new ids, ready to add to a scene.
    /// Goes through the file format so copies follow exactly the same rules as save and load.
    /// </summary>
    public Entity Clone(Entity source)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteEntity(writer, source);

        using var document = JsonDocument.Parse(stream.ToArray());
        return ReadEntity(document.RootElement, new LoadContext(warnings: null, newIds: true));
    }

    /// <summary>One component as JSON, exactly as it appears in a scene file.</summary>
    public JsonElement SerializeComponent(Component component)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteComponent(writer, component);
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Creates a detached component from JSON: the registered type if known, otherwise a
    /// <see cref="MissingComponent"/> that keeps the data. Transform data isn't accepted here.
    /// </summary>
    public Component DeserializeComponent(JsonElement data, ICollection<string>? warnings = null)
    {
        var typeName = data.TryGetProperty("type", out var t) ? t.GetString() : null;
        if (typeName is null || registry.Find(typeName) is not { } descriptor)
            return new MissingComponent(typeName ?? "", data);
        if (descriptor.Type == typeof(Transform))
            throw new ArgumentException("Transforms belong to their entity and can't be created separately.", nameof(data));

        var component = descriptor.CreateInstance();
        ReadProperties(component, descriptor, data, descriptor.DisplayName, new LoadContext(warnings));
        return component;
    }

    private void WriteEntity(Utf8JsonWriter writer, Entity entity)
    {
        writer.WriteStartObject();
        writer.WriteString("id", entity.Id);
        writer.WriteString("name", entity.Name);
        if (!entity.IsEnabled)
            writer.WriteBoolean("enabled", false);

        writer.WriteStartArray("components");
        foreach (var component in entity.Components)
            WriteComponent(writer, component);
        writer.WriteEndArray();

        if (entity.Children.Count > 0)
        {
            writer.WriteStartArray("children");
            foreach (var child in entity.Children)
                WriteEntity(writer, child);
            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    private void WriteComponent(Utf8JsonWriter writer, Component component)
    {
        if (component is MissingComponent missing)
        {
            missing.Data.WriteTo(writer);
            return;
        }

        var descriptor = registry.Get(component.GetType());
        writer.WriteStartObject();
        writer.WriteString("type", descriptor.Name);
        foreach (var property in descriptor.Properties)
        {
            writer.WritePropertyName(property.SerializedName);
            ValueCodec.Write(writer, property.GetValue(component));
        }

        writer.WriteEndObject();
    }

    private Entity ReadEntity(JsonElement element, LoadContext context)
    {
        var name = element.TryGetProperty("name", out var n) ? n.GetString() ?? "Entity" : "Entity";

        Guid id;
        if (context.NewIds)
        {
            id = Guid.NewGuid();
        }
        else if (!element.TryGetProperty("id", out var idElement) || !idElement.TryGetGuid(out id) || !context.SeenIds.Add(id))
        {
            id = Guid.NewGuid();
            context.Warn($"\"{name}\" had a missing or duplicate id; it was given a new one.");
        }

        var transform = new Transform();
        var entity = new Entity(id, name, transform)
        {
            IsEnabled = !element.TryGetProperty("enabled", out var enabled) || enabled.GetBoolean(),
        };

        if (element.TryGetProperty("components", out var components))
        {
            foreach (var data in components.EnumerateArray())
                ReadComponent(entity, data, context);
        }

        if (element.TryGetProperty("children", out var children))
        {
            foreach (var childElement in children.EnumerateArray())
            {
                var child = ReadEntity(childElement, context);
                entity.ChildList.Add(child);
                child.Parent = entity;
            }
        }

        return entity;
    }

    private void ReadComponent(Entity entity, JsonElement data, LoadContext context)
    {
        var typeName = data.TryGetProperty("type", out var t) ? t.GetString() : null;
        if (typeName is null)
        {
            context.Warn($"A component on \"{entity.Name}\" has no type and was skipped.");
            return;
        }

        var descriptor = registry.Find(typeName);
        if (descriptor is null)
        {
            entity.AddComponent(new MissingComponent(typeName, data));
            context.Warn($"\"{entity.Name}\" uses unknown component \"{typeName}\"; its data was kept.");
            return;
        }

        var component = descriptor.Type == typeof(Transform) ? entity.Transform : descriptor.CreateInstance();
        ReadProperties(component, descriptor, data, $"{entity.Name}.{descriptor.DisplayName}", context);

        if (component != entity.Transform)
            entity.AddComponent(component);
    }

    private static void ReadProperties(Component component, ComponentDescriptor descriptor, JsonElement data, string label, LoadContext context)
    {
        foreach (var property in descriptor.Properties)
        {
            if (!data.TryGetProperty(property.SerializedName, out var value))
                continue;

            try
            {
                property.SetValue(component, ValueCodec.Read(value, property.Type));
            }
            catch (FormatException ex)
            {
                context.Warn($"{label}.{property.Name}: {ex.Message} Kept the default.");
            }
        }
    }

    private sealed class LoadContext(ICollection<string>? warnings, bool newIds = false)
    {
        public bool NewIds { get; } = newIds;

        public HashSet<Guid> SeenIds { get; } = [];

        public void Warn(string message) => warnings?.Add(message);
    }
}
