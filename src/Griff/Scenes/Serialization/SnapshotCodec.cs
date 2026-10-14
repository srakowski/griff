using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Griff.Scenes.Serialization;

/// <summary>
/// Entities as compact binary records, for rewind (<see cref="Runtime.Timeline"/>): what <see cref="SceneSerializer"/>
/// saves, value for value, written without text or garbage, and read back as a scene file loads (the same ids, and the
/// same rules for values a file can't hold: a color keeps its 8 bits a channel, and a number that isn't finite leaves
/// the property's default). Each entity's record is its own (its children aren't in it), so a timeline can keep an
/// unchanged entity's record from one snapshot to the next.
/// </summary>
internal sealed class SnapshotCodec(SceneSerializer serializer)
{
    private const int Missing = -1;

    private readonly ComponentRegistry _registry = serializer.Components;
    private readonly Dictionary<Type, Plan> _plans = [];
    private readonly List<Plan> _byIndex = [];

    /// <summary>Writes <paramref name="entity"/>'s record (its id first, then its name, flags, prefab and components; not its children).</summary>
    public void Write(Entity entity, SnapshotBuffer buffer)
    {
        buffer.Write(entity.Id);
        buffer.WriteString(entity.Name);
        buffer.Write((byte)((entity.IsEnabled ? 1 : 0) | (entity.IsLocked ? 2 : 0) | (entity.IsHidden ? 4 : 0)));
        buffer.WriteString(entity.Prefab);
        var components = entity.Components;
        buffer.Write(components.Count);
        for (var i = 0; i < components.Count; i++)
        {
            var component = components[i];
            if (component is MissingComponent missing)
            {
                buffer.Write(Missing);
                buffer.WriteString(missing.Data.GetRawText());
                continue;
            }

            var plan = PlanFor(component.GetType());
            buffer.Write(plan.Index);
            foreach (var field in plan.Fields)
                field.Write(component, buffer);
            WriteKept(plan, component, buffer);
        }
    }

    /// <summary>The id a record starts with.</summary>
    public static Guid IdOf(ReadOnlySpan<byte> record) => MemoryMarshal.Read<Guid>(record);

    /// <summary>A new entity from a record <see cref="Write"/> wrote, with its components but not its children.</summary>
    /// <param name="seen">Ids read so far: one read again gets a new id, as a scene file's would.</param>
    public Entity Read(SnapshotReader reader, HashSet<Guid> seen)
    {
        var id = reader.Read<Guid>();
        var name = reader.ReadString() ?? "Entity";
        if (!seen.Add(id))
            id = Guid.NewGuid();
        var flags = reader.Read<byte>();
        var entity = new Entity(id, name, new Transform())
        {
            IsEnabled = (flags & 1) != 0,
            IsLocked = (flags & 2) != 0,
            IsHidden = (flags & 4) != 0,
            Prefab = reader.ReadString(),
        };

        var count = reader.Read<int>();
        for (var i = 0; i < count; i++)
        {
            var kind = reader.Read<int>();
            if (kind == Missing)
            {
                // Its type may have come back (a script added while the game ran): read it as the scene file would.
                using var document = JsonDocument.Parse(reader.ReadString()!);
                serializer.ReadComponentInto(entity, document.RootElement);
                continue;
            }

            var plan = _byIndex[kind];
            if (_registry.Find(plan.Descriptor.Name) != plan.Descriptor)
            {
                // Its type has changed since (a script rebuilt while the game ran): read it by name, as the scene file would.
                serializer.ReadComponentInto(entity, ToJson(plan, reader));
                continue;
            }

            var component = plan.Descriptor.Type == typeof(Transform) ? entity.Transform : plan.Descriptor.CreateInstance();
            foreach (var field in plan.Fields)
                field.Read(component, reader);
            component.Kept = ReadKept(reader);
            if (component != entity.Transform)
                entity.AddComponent(component);
        }

        return entity;
    }

    private Plan PlanFor(Type type)
    {
        if (_plans.TryGetValue(type, out var plan))
            return plan;
        var descriptor = _registry.Get(type);
        plan = new Plan(_byIndex.Count, descriptor);
        _plans.Add(type, plan);
        _byIndex.Add(plan);
        return plan;
    }

    // The values a component keeps that no property took, as saving would write them and loading would keep them again.
    private static void WriteKept(Plan plan, Component component, SnapshotBuffer buffer)
    {
        if (component.Kept is not { Count: > 0 } kept)
        {
            buffer.Write(0);
            return;
        }

        var at = buffer.Length;
        buffer.Write(0);
        var count = 0;
        foreach (var (key, value) in kept)
        {
            if (plan.Used.Contains(key))
                continue;
            buffer.WriteString(key);
            buffer.WriteString(value.GetRawText());
            count++;
        }

        buffer.Patch(at, count);
    }

    private static List<KeyValuePair<string, JsonElement>>? ReadKept(SnapshotReader reader)
    {
        var count = reader.Read<int>();
        if (count == 0)
            return null;
        var kept = new List<KeyValuePair<string, JsonElement>>(count);
        for (var i = 0; i < count; i++)
        {
            var key = reader.ReadString()!;
            using var document = JsonDocument.Parse(reader.ReadString()!);
            kept.Add(KeyValuePair.Create(key, document.RootElement.Clone()));
        }

        return kept;
    }

    // The component as the scene file has it, from its record.
    private static JsonElement ToJson(Plan plan, SnapshotReader reader)
    {
        var output = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WriteString("type", plan.Descriptor.Name);
            if (plan.Descriptor.Version > 1)
                writer.WriteNumber("version", plan.Descriptor.Version);
            for (var i = 0; i < plan.Fields.Length; i++)
            {
                writer.WritePropertyName(plan.Descriptor.Properties[i].SerializedName);
                ValueCodec.Write(writer, plan.Fields[i].ReadBoxed(reader));
            }

            var kept = reader.Read<int>();
            for (var i = 0; i < kept; i++)
            {
                writer.WritePropertyName(reader.ReadString()!);
                writer.WriteRawValue(reader.ReadString()!, skipInputValidation: true);
            }

            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(output.WrittenMemory);
        return document.RootElement.Clone();
    }

    // How a component type is written: a field for each of its descriptor's properties, in order.
    private sealed class Plan(int index, ComponentDescriptor descriptor)
    {
        public int Index { get; } = index;

        public ComponentDescriptor Descriptor { get; } = descriptor;

        public SnapshotField[] Fields { get; } = [.. descriptor.Properties.Select(SnapshotField.For)];

        // Saved names loading takes as properties, so a kept value under one of them is dropped on loading.
        public HashSet<string> Used { get; } =
            [.. descriptor.Properties.SelectMany(p => p.FormerSerializedNames.Prepend(p.SerializedName)), "type", "version"];
    }
}

/// <summary>A growing byte buffer records are written into, kept and reused so writing makes no garbage.</summary>
internal sealed class SnapshotBuffer
{
    private byte[] _data = new byte[4096];

    public int Length { get; private set; }

    public ReadOnlySpan<byte> Span(int offset, int length) => _data.AsSpan(offset, length);

    /// <summary>Forgets everything from <paramref name="length"/> on.</summary>
    public void Truncate(int length) => Length = length;

    public byte[] ToArray() => _data.AsSpan(0, Length).ToArray();

    public void Write<T>(T value) where T : unmanaged
    {
        var size = Unsafe.SizeOf<T>();
        Ensure(size);
        Unsafe.WriteUnaligned(ref _data[Length], value);
        Length += size;
    }

    /// <summary>Writes over an int written earlier at <paramref name="offset"/>.</summary>
    public void Patch(int offset, int value) => Unsafe.WriteUnaligned(ref _data[offset], value);

    public void WriteString(string? text)
    {
        if (text is null)
        {
            Write(-1);
            return;
        }

        // As UTF-8: names and paths are mostly ASCII, so half the bytes.
        Ensure(sizeof(int) + Encoding.UTF8.GetMaxByteCount(text.Length));
        var length = Encoding.UTF8.GetBytes(text, _data.AsSpan(Length + sizeof(int)));
        Write(length);
        Length += length;
    }

    private void Ensure(int more)
    {
        if (Length + more > _data.Length)
            Array.Resize(ref _data, Math.Max(_data.Length * 2, Length + more));
    }
}

/// <summary>Reads a record back.</summary>
internal sealed class SnapshotReader
{
    private byte[] _data = [];
    private int _position;

    public void Reset(byte[] data, int offset)
    {
        _data = data;
        _position = offset;
    }

    public T Read<T>() where T : unmanaged
    {
        var value = Unsafe.ReadUnaligned<T>(ref _data[_position]);
        _position += Unsafe.SizeOf<T>();
        return value;
    }

    public string? ReadString()
    {
        var length = Read<int>();
        if (length < 0)
            return null;
        var text = Encoding.UTF8.GetString(_data, _position, length);
        _position += length;
        return text;
    }
}

/// <summary>One property of a component type, written and read without boxing.</summary>
internal abstract class SnapshotField
{
    public abstract void Write(Component component, SnapshotBuffer buffer);

    /// <summary>Reads the value and sets it, or leaves the property as it is where a scene file couldn't have held the value.</summary>
    public abstract void Read(Component component, SnapshotReader reader);

    /// <summary>Reads the value as the property's getter would have given it, for writing it the scene file's way.</summary>
    public abstract object? ReadBoxed(SnapshotReader reader);

    public static SnapshotField For(PropertyDescriptor property)
    {
        var info = property.Info;
        var owner = info.DeclaringType!;
        var type = info.PropertyType;
        var field = type == typeof(string) ? typeof(TextField<>).MakeGenericType(owner)
            : type == typeof(Color) ? typeof(ColorField<>).MakeGenericType(owner)
            : type == typeof(IReadOnlyList<string>) ? typeof(LinesField<>).MakeGenericType(owner)
            : typeof(ValueField<,>).MakeGenericType(owner, type);
        return (SnapshotField)Activator.CreateInstance(field, info)!;
    }
}

// Numbers, flags, enums (by their value: a scene file's name for one reads back as the same value) and vectors.
internal sealed class ValueField<TOwner, TValue>(System.Reflection.PropertyInfo property) : SnapshotField
    where TOwner : class
    where TValue : unmanaged
{
    private readonly Func<TOwner, TValue> _get = property.GetMethod!.CreateDelegate<Func<TOwner, TValue>>();
    private readonly Action<TOwner, TValue> _set = property.SetMethod!.CreateDelegate<Action<TOwner, TValue>>();

    public override void Write(Component component, SnapshotBuffer buffer) => buffer.Write(_get(Unsafe.As<TOwner>(component)));

    public override void Read(Component component, SnapshotReader reader)
    {
        var value = reader.Read<TValue>();
        if (IsFinite(value))
            _set(Unsafe.As<TOwner>(component), value);
    }

    public override object? ReadBoxed(SnapshotReader reader) => reader.Read<TValue>();

    // A scene file has no number that isn't finite (it writes null, which loads as the property's default).
    private static bool IsFinite(TValue value)
    {
        if (typeof(TValue) == typeof(float))
            return float.IsFinite(Unsafe.As<TValue, float>(ref value));
        if (typeof(TValue) == typeof(double))
            return double.IsFinite(Unsafe.As<TValue, double>(ref value));
        if (typeof(TValue) == typeof(Vector2))
        {
            var v = Unsafe.As<TValue, Vector2>(ref value);
            return float.IsFinite(v.X) && float.IsFinite(v.Y);
        }

        if (typeof(TValue) == typeof(Vector3))
        {
            var v = Unsafe.As<TValue, Vector3>(ref value);
            return float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
        }

        if (typeof(TValue) == typeof(Vector4) || typeof(TValue) == typeof(Quaternion))
        {
            var v = Unsafe.As<TValue, Vector4>(ref value);
            return float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) && float.IsFinite(v.W);
        }

        return true;
    }
}

internal sealed class TextField<TOwner>(System.Reflection.PropertyInfo property) : SnapshotField
    where TOwner : class
{
    private readonly Func<TOwner, string?> _get = property.GetMethod!.CreateDelegate<Func<TOwner, string?>>();
    private readonly Action<TOwner, string?> _set = property.SetMethod!.CreateDelegate<Action<TOwner, string?>>();

    public override void Write(Component component, SnapshotBuffer buffer) => buffer.WriteString(_get(Unsafe.As<TOwner>(component)));

    public override void Read(Component component, SnapshotReader reader) => _set(Unsafe.As<TOwner>(component), reader.ReadString());

    public override object? ReadBoxed(SnapshotReader reader) => reader.ReadString();
}

// A scene file keeps a color as #RRGGBBAA, so a record keeps its four bytes.
internal sealed class ColorField<TOwner>(System.Reflection.PropertyInfo property) : SnapshotField
    where TOwner : class
{
    private readonly Func<TOwner, Color> _get = property.GetMethod!.CreateDelegate<Func<TOwner, Color>>();
    private readonly Action<TOwner, Color> _set = property.SetMethod!.CreateDelegate<Action<TOwner, Color>>();

    public override void Write(Component component, SnapshotBuffer buffer)
    {
        var color = _get(Unsafe.As<TOwner>(component));
        buffer.Write((uint)((color.RByte << 24) | (color.GByte << 16) | (color.BByte << 8) | color.AByte));
    }

    public override void Read(Component component, SnapshotReader reader) => _set(Unsafe.As<TOwner>(component), Unpack(reader.Read<uint>()));

    public override object? ReadBoxed(SnapshotReader reader) => Unpack(reader.Read<uint>());

    private static Color Unpack(uint value) => Color.FromBytes((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
}

// Lines of text (a tilemap's rows): a missing list leaves the default, and a missing line reads back empty, as in a scene file.
internal sealed class LinesField<TOwner>(System.Reflection.PropertyInfo property) : SnapshotField
    where TOwner : class
{
    private readonly Func<TOwner, IReadOnlyList<string>?> _get = property.GetMethod!.CreateDelegate<Func<TOwner, IReadOnlyList<string>?>>();
    private readonly Action<TOwner, IReadOnlyList<string>?> _set = property.SetMethod!.CreateDelegate<Action<TOwner, IReadOnlyList<string>?>>();

    public override void Write(Component component, SnapshotBuffer buffer)
    {
        var lines = _get(Unsafe.As<TOwner>(component));
        if (lines is null)
        {
            buffer.Write(-1);
            return;
        }

        buffer.Write(lines.Count);
        for (var i = 0; i < lines.Count; i++)
            buffer.WriteString(lines[i] ?? "");
    }

    public override void Read(Component component, SnapshotReader reader)
    {
        if (ReadLines(reader) is { } lines)
            _set(Unsafe.As<TOwner>(component), lines);
    }

    public override object? ReadBoxed(SnapshotReader reader) => ReadLines(reader);

    private static string[]? ReadLines(SnapshotReader reader)
    {
        var count = reader.Read<int>();
        if (count < 0)
            return null;
        var lines = new string[count];
        for (var i = 0; i < count; i++)
            lines[i] = reader.ReadString()!;
        return lines;
    }
}
