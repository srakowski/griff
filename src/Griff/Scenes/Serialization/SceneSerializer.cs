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
        if (!element.TryGetProperty("id", out var idElement) || !idElement.TryGetGuid(out id) || !context.SeenIds.Add(id))
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
                context.Warn($"{entity.Name}.{descriptor.DisplayName}.{property.Name}: {ex.Message} Kept the default.");
            }
        }

        if (component != entity.Transform)
            entity.AddComponent(component);
    }

    private sealed class LoadContext(ICollection<string>? warnings)
    {
        public HashSet<Guid> SeenIds { get; } = [];

        public void Warn(string message) => warnings?.Add(message);
    }
}
