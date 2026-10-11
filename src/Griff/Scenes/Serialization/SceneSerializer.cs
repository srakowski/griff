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
    public Entity Clone(Entity source) => DeserializeEntity(SerializeEntity(source));

    /// <summary>An entity and its descendants as indented JSON (the clipboard format).</summary>
    public string SerializeEntity(Entity entity)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
            WriteEntity(writer, entity);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Several entities (each with its descendants) for the clipboard: one entity's JSON for one, a JSON array of
    /// them for more. <see cref="DeserializeEntities"/> reads either.
    /// </summary>
    public string SerializeEntities(IReadOnlyList<Entity> entities)
    {
        if (entities.Count == 1)
            return SerializeEntity(entities[0]);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartArray();
            foreach (var entity in entities)
                WriteEntity(writer, entity);
            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Detached entities from <see cref="SerializeEntities"/> (or <see cref="SerializeEntity"/>) output, with fresh ids.</summary>
    /// <exception cref="InvalidDataException">The text isn't entities.</exception>
    public IReadOnlyList<Entity> DeserializeEntities(string json, ICollection<string>? warnings = null)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
                return [DeserializeEntity(json, warnings)];
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0 ||
                root.EnumerateArray().Any(e => e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("components", out _)))
                throw new InvalidDataException("The text isn't Griff entities.");
            var context = new LoadContext(warnings, newIds: true);
            return root.EnumerateArray().Select(e => ReadEntity(e, context)).ToList();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The text isn't Griff entities.", ex);
        }
    }

    /// <summary>A detached entity from <see cref="SerializeEntity"/> output, with fresh ids.</summary>
    /// <exception cref="InvalidDataException">The text isn't an entity.</exception>
    public Entity DeserializeEntity(string json, ICollection<string>? warnings = null)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("components", out _))
                throw new InvalidDataException("The text isn't a Griff entity.");
            return ReadEntity(document.RootElement, new LoadContext(warnings, newIds: true));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The text isn't a Griff entity.", ex);
        }
    }

    /// <summary>Just <paramref name="entity"/> (not its children), ids and all, compact: how a network game sends one. The editor's prefab link is left out.</summary>
    internal string SerializeEntityAlone(Entity entity)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteEntity(writer, entity, writeIds: true, children: false, link: false);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>A detached entity from <see cref="SerializeEntityAlone"/> or <see cref="SerializeEntity"/>, keeping its ids.</summary>
    internal Entity DeserializeEntityKeepingIds(string json, ICollection<string>? warnings = null)
    {
        using var document = JsonDocument.Parse(json);
        return ReadEntity(document.RootElement, new LoadContext(warnings, newIds: false));
    }

    /// <summary>
    /// <paramref name="entity"/> and its descendants as a prefab file. Ids are left out: every spawn gets
    /// fresh ones, and re-saving a prefab from an instance doesn't change lines that didn't change.
    /// </summary>
    public string SavePrefabToString(Entity entity)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("formatVersion", FormatVersion);
            writer.WritePropertyName("prefab");
            WriteEntity(writer, entity, writeIds: false, link: false);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>A new, detached entity from a prefab file, with fresh ids.</summary>
    /// <exception cref="InvalidDataException">The text isn't a prefab, or is from a newer version.</exception>
    public Entity LoadPrefabFromString(string json, ICollection<string>? warnings = null)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("prefab", out var prefab) || prefab.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("The file isn't a Griff prefab.");
            if (root.TryGetProperty("formatVersion", out var v) && v.GetInt32() > FormatVersion)
                throw new InvalidDataException("The prefab was saved by a newer version of Griff.");
            return ReadEntity(prefab, new LoadContext(warnings, newIds: true));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The prefab file isn't valid JSON: {ex.Message}", ex);
        }
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

    // A prefab's own root isn't a copy of anything (link: false), though copies of other prefabs inside it are.
    private void WriteEntity(Utf8JsonWriter writer, Entity entity, bool writeIds = true, bool children = true, bool link = true)
    {
        writer.WriteStartObject();
        if (writeIds)
            writer.WriteString("id", entity.Id);
        writer.WriteString("name", entity.Name);
        if (!entity.IsEnabled)
            writer.WriteBoolean("enabled", false);
        if (entity.IsLocked)
            writer.WriteBoolean("locked", true);
        if (link && entity.Prefab is { } prefab)
            writer.WriteString("prefab", prefab);

        writer.WriteStartArray("components");
        foreach (var component in entity.Components)
            WriteComponent(writer, component);
        writer.WriteEndArray();

        if (children && entity.Children.Count > 0)
        {
            writer.WriteStartArray("children");
            foreach (var child in entity.Children)
                WriteEntity(writer, child, writeIds);
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
            IsLocked = element.TryGetProperty("locked", out var locked) && locked.GetBoolean(),
            Prefab = element.TryGetProperty("prefab", out var prefab) && prefab.ValueKind == JsonValueKind.String ? prefab.GetString() : null,
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
