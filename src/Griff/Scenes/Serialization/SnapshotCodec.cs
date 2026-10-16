using System.Buffers;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Griff.Scenes.Serialization;

/// <summary>
/// Entities as compact binary records, for rewind (<see cref="Runtime.Timeline"/>): what <see cref="SceneSerializer"/>
/// saves, value for value, written without garbage, and read back as a scene file loads (the same ids, and the same rules
/// for values a file can't hold: a color keeps its 8 bits a channel, and a number that isn't finite leaves the
/// property's default). A record is its bytes (numbers, flags, colors, counts) and the strings it holds, kept as they are
/// (a string never changes, so keeping it is keeping its value). Each entity's record is its own (its children aren't in
/// it), so a timeline can tell which entities changed from one snapshot to the next. A component type's values are
/// written by code compiled for it the first time it's seen, so writing a record is little more than reading its values,
/// even in a Debug build (which runs Griff's own code unoptimized).
/// </summary>
internal sealed class SnapshotCodec(SceneSerializer serializer)
{
    private const int Missing = -1;

    // An entity's id, flags and how many components it has.
    private const int HeaderSize = 16 + 1 + 4;

    private readonly ComponentRegistry _registry = serializer.Components;
    private readonly Dictionary<Type, Plan> _plans = [];
    private readonly List<Plan> _byIndex = [];

    // The plan of the component at each place in the last entity written: entities mostly come in runs of the same kind.
    private readonly Plan?[] _recent = new Plan?[8];

    /// <summary>Writes <paramref name="entity"/>'s record (its id first, then its name, flags, prefab and components; not its children).</summary>
    public void Write(Entity entity, SnapshotBuffer buffer) => Write(entity, buffer, null);

    /// <summary>
    /// <see cref="Write(Entity, SnapshotBuffer)"/>, and with <paramref name="state"/> (a running game's, see
    /// <see cref="StateWriter"/>), what each component keeps besides its saved values, written there as it goes.
    /// </summary>
    public void Write(Entity entity, SnapshotBuffer buffer, StateWriter? state)
    {
        var components = entity.Components;
        var at = buffer.Reserve(HeaderSize);
        var data = buffer.Data;
        Unsafe.WriteUnaligned(ref data[at], entity.Id);
        data[at + 16] = (byte)((entity.IsEnabled ? 1 : 0) | (entity.IsLocked ? 2 : 0) | (entity.IsHidden ? 4 : 0));
        Unsafe.WriteUnaligned(ref data[at + 17], components.Count);
        buffer.AddRef(entity.Name);
        buffer.AddRef(entity.Prefab);
        for (var i = 0; i < components.Count; i++)
        {
            var component = components[i];
            if (component is MissingComponent missing)
            {
                buffer.Write(Missing);
                buffer.AddRef(missing.Data.GetRawText());
                continue;
            }

            var type = component.GetType();
            var plan = i < _recent.Length && _recent[i] is { } recent && recent.Type == type ? recent : PlanFor(type);
            if (i < _recent.Length)
                _recent[i] = plan;
            plan.Write(component, buffer);
            state?.Component(entity, i, component, plan.Kept);
        }
    }

    /// <summary>The id a record starts with.</summary>
    public static Guid IdOf(ReadOnlySpan<byte> record) => MemoryMarshal.Read<Guid>(record);

    /// <summary>A new entity from a record <see cref="Write"/> wrote, with its components but not its children.</summary>
    /// <param name="seen">Ids read so far: one read again gets a new id, as a scene file's would.</param>
    public Entity Read(SnapshotReader reader, HashSet<Guid> seen)
    {
        var id = reader.Read<Guid>();
        var flags = reader.Read<byte>();
        var count = reader.Read<int>();
        var name = reader.ReadString() ?? "Entity";
        if (!seen.Add(id))
            id = Guid.NewGuid();
        var entity = new Entity(id, name, new Transform())
        {
            IsEnabled = (flags & 1) != 0,
            IsLocked = (flags & 2) != 0,
            IsHidden = (flags & 4) != 0,
            Prefab = reader.ReadString(),
        };

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
        plan = new Plan(_byIndex.Count, _registry.Get(type));
        _plans.Add(type, plan);
        _byIndex.Add(plan);
        return plan;
    }

    // The values a component keeps that no property took, as saving would write them and loading would keep them again:
    // their keys and values go with the record's strings, and how many there are with its bytes.
    private static int WriteKept(Component component, SnapshotBuffer buffer, HashSet<string> used)
    {
        if (component.Kept is not { Count: > 0 } kept)
            return 0;

        var count = 0;
        foreach (var (key, value) in kept)
        {
            if (used.Contains(key))
                continue;
            buffer.AddRef(key);
            buffer.AddRef(value.GetRawText());
            count++;
        }

        return count;
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

    // How a component type is written: its plan's index, a field for each of its descriptor's properties, in order, then
    // how many kept values follow.
    private sealed class Plan
    {
        private static readonly MethodInfo WriteKeptMethod = typeof(SnapshotCodec).GetMethod(nameof(WriteKept), BindingFlags.NonPublic | BindingFlags.Static)!;
        private static readonly PropertyInfo KeptProperty = typeof(Component).GetProperty(nameof(Component.Kept), BindingFlags.Public | BindingFlags.Instance)!;

        public Plan(int index, ComponentDescriptor descriptor)
        {
            Type = descriptor.Type;
            Index = index;
            Descriptor = descriptor;
            Fields = [.. descriptor.Properties.Select(SnapshotField.For)];
            Used = [.. descriptor.Properties.SelectMany(p => p.FormerSerializedNames.Prepend(p.SerializedName)), "type", "version"];
            Write = Compile();
            Kept = KeptFields.OfComponent(Type);
        }

        // What it keeps besides its saved values, for a running game's state.
        public readonly KeptFields Kept;

        public readonly Type Type;

        public readonly Action<Component, SnapshotBuffer> Write;

        public int Index { get; }

        public ComponentDescriptor Descriptor { get; }

        public SnapshotField[] Fields { get; }

        // Saved names loading takes as properties, so a kept value under one of them is dropped on loading.
        public HashSet<string> Used { get; }

        // (component, buffer) => { var owner = (T)component; var at = buffer.Reserve(size); var data = buffer.Data;
        //   data[at..] = index; each field's value; how many kept values (adding them to the strings) }
        // It calls nothing but the properties' getters (and helpers for what's rare: lines, kept values, growing the
        // buffer), and it's compiled with the runtime's optimizations, which Griff's own code goes without in a Debug build.
        private Action<Component, SnapshotBuffer> Compile()
        {
            var component = Expression.Parameter(typeof(Component), "component");
            var buffer = Expression.Parameter(typeof(SnapshotBuffer), "buffer");
            var owner = Expression.Variable(Descriptor.Type, "owner");
            var at = Expression.Variable(typeof(int), "at");
            var data = Expression.Variable(typeof(byte[]), "data");
            var size = sizeof(int) + Fields.Sum(f => f.Size) + sizeof(int);
            var body = new List<Expression>
            {
                Expression.Assign(owner, Expression.Convert(component, Descriptor.Type)),
                SnapshotBuffer.ReserveCode(buffer, at, data, size),
                SnapshotField.Store(data, at, 0, Expression.Constant(Index)),
            };
            var offset = sizeof(int);
            foreach (var field in Fields)
            {
                body.Add(field.Write(owner, buffer, data, at, offset));
                offset += field.Size;
            }

            var kept = Expression.Property(component, KeptProperty);
            body.Add(SnapshotField.Store(data, at, offset, Expression.Condition(
                Expression.Equal(kept, Expression.Constant(null, kept.Type)),
                Expression.Constant(0),
                Expression.Call(WriteKeptMethod, component, buffer, Expression.Constant(Used)))));
            return Expression.Lambda<Action<Component, SnapshotBuffer>>(Expression.Block([owner, at, data], body), component, buffer).Compile();
        }
    }
}

/// <summary>
/// A growing buffer records are written into: their bytes, and the strings they hold. It's kept and reused, so writing
/// makes no garbage. Its counts and arrays are fields, so the code compiled to write records works on them directly.
/// </summary>
internal sealed class SnapshotBuffer
{
    private static readonly FieldInfo DataField = typeof(SnapshotBuffer).GetField(nameof(Data))!;
    private static readonly FieldInfo RefsField = typeof(SnapshotBuffer).GetField(nameof(Refs))!;
    private static readonly FieldInfo LengthField = typeof(SnapshotBuffer).GetField(nameof(Length))!;
    private static readonly FieldInfo RefCountField = typeof(SnapshotBuffer).GetField(nameof(RefCount))!;

    /// <summary>The bytes, of which the first <see cref="Length"/> are written.</summary>
    public byte[] Data = new byte[4096];

    /// <summary>The strings, of which the first <see cref="RefCount"/> are written.</summary>
    public string?[] Refs = new string?[256];

    public int Length;

    public int RefCount;

    public ReadOnlySpan<byte> Span(int offset, int length) => Data.AsSpan(offset, length);

    /// <summary>Forgets everything written.</summary>
    public void Clear()
    {
        Length = 0;
        RefCount = 0;
    }

    /// <summary>Makes room for <paramref name="size"/> more bytes and returns where they start.</summary>
    public int Reserve(int size)
    {
        var at = Length;
        if (at + size > Data.Length)
            Grow(size);
        Length = at + size;
        return at;
    }

    /// <summary>Makes <see cref="Data"/> big enough for <paramref name="more"/> bytes after <see cref="Length"/>.</summary>
    public void Grow(int more) => Array.Resize(ref Data, Math.Max(Data.Length * 2, Length + more));

    /// <summary>Makes <see cref="Refs"/> big enough for <paramref name="more"/> strings after <see cref="RefCount"/>.</summary>
    public void GrowRefs(int more) => Array.Resize(ref Refs, Math.Max(Refs.Length * 2, RefCount + more));

    public void Write<T>(T value) where T : unmanaged
    {
        var at = Reserve(Unsafe.SizeOf<T>());
        Unsafe.WriteUnaligned(ref Data[at], value);
    }

    public void AddRef(string? value)
    {
        if (RefCount == Refs.Length)
            GrowRefs(1);
        Refs[RefCount++] = value;
    }

    public void AddRefs(ReadOnlySpan<string?> values)
    {
        if (RefCount + values.Length > Refs.Length)
            GrowRefs(values.Length);
        values.CopyTo(Refs.AsSpan(RefCount));
        RefCount += values.Length;
    }

    /// <summary><c>at = buffer.Reserve(size); data = buffer.Data</c>, as compiled code.</summary>
    public static Expression ReserveCode(Expression buffer, ParameterExpression at, ParameterExpression data, int size)
    {
        var length = Expression.Field(buffer, LengthField);
        var array = Expression.Field(buffer, DataField);
        return Expression.Block(
            Expression.Assign(at, length),
            Expression.IfThen(
                Expression.GreaterThan(Expression.Add(at, Expression.Constant(size)), Expression.ArrayLength(array)),
                Expression.Call(buffer, nameof(Grow), null, Expression.Constant(size))),
            Expression.Assign(length, Expression.Add(at, Expression.Constant(size))),
            Expression.Assign(data, array));
    }

    /// <summary><c>buffer.AddRef(value)</c>, as compiled code.</summary>
    public static Expression AddRefCode(Expression buffer, Expression value)
    {
        var count = Expression.Field(buffer, RefCountField);
        var refs = Expression.Field(buffer, RefsField);
        return Expression.Block(
            Expression.IfThen(Expression.Equal(count, Expression.ArrayLength(refs)), Expression.Call(buffer, nameof(GrowRefs), null, Expression.Constant(1))),
            Expression.Assign(Expression.ArrayAccess(refs, count), value),
            Expression.Assign(count, Expression.Add(count, Expression.Constant(1))));
    }
}

/// <summary>Reads a record back: its bytes in order, and its strings in order.</summary>
internal sealed class SnapshotReader
{
    private byte[] _data = [];
    private string?[] _refs = [];
    private int _position;
    private int _ref;

    public void Reset(byte[] data, int offset, string?[] refs, int refStart)
    {
        _data = data;
        _position = offset;
        _refs = refs;
        _ref = refStart;
    }

    public T Read<T>() where T : unmanaged
    {
        var value = Unsafe.ReadUnaligned<T>(ref _data[_position]);
        _position += Unsafe.SizeOf<T>();
        return value;
    }

    public string? ReadString() => _refs[_ref++];
}

/// <summary>One property of a component type: how it's written (as code compiled into its type's writer) and read back.</summary>
internal abstract class SnapshotField
{
    private static readonly MethodInfo WriteUnaligned = typeof(Unsafe).GetMethods()
        .Single(m => m.Name == nameof(Unsafe.WriteUnaligned) && m.GetParameters()[0].ParameterType == typeof(byte).MakeByRefType());

    /// <summary>The bytes it takes in a record.</summary>
    public abstract int Size { get; }

    /// <summary>Writes the value of <paramref name="owner"/>'s property: its bytes at <c>data[at + offset]</c>, any strings to <paramref name="buffer"/>.</summary>
    public abstract Expression Write(Expression owner, Expression buffer, Expression data, Expression at, int offset);

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

    /// <summary><c>Unsafe.WriteUnaligned(ref data[at + offset], value)</c>.</summary>
    public static Expression Store(Expression data, Expression at, int offset, Expression value) =>
        Expression.Call(WriteUnaligned.MakeGenericMethod(value.Type), Expression.ArrayAccess(data, Expression.Add(at, Expression.Constant(offset))), value);
}

// Numbers, flags, enums (by their value: a scene file's name for one reads back as the same value) and vectors.
internal sealed class ValueField<TOwner, TValue>(PropertyInfo property) : SnapshotField
    where TOwner : class
    where TValue : unmanaged
{
    private readonly Action<TOwner, TValue> _set = property.SetMethod!.CreateDelegate<Action<TOwner, TValue>>();

    public override int Size => Unsafe.SizeOf<TValue>();

    public override Expression Write(Expression owner, Expression buffer, Expression data, Expression at, int offset) =>
        Store(data, at, offset, Expression.Property(owner, property));

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

// A string is kept as it is, with the record's strings.
internal sealed class TextField<TOwner>(PropertyInfo property) : SnapshotField
    where TOwner : class
{
    private readonly Action<TOwner, string?> _set = property.SetMethod!.CreateDelegate<Action<TOwner, string?>>();

    public override int Size => 0;

    public override Expression Write(Expression owner, Expression buffer, Expression data, Expression at, int offset) =>
        SnapshotBuffer.AddRefCode(buffer, Expression.Property(owner, property));

    public override void Read(Component component, SnapshotReader reader) => _set(Unsafe.As<TOwner>(component), reader.ReadString());

    public override object? ReadBoxed(SnapshotReader reader) => reader.ReadString();
}

// A scene file keeps a color as #RRGGBBAA, so a record keeps its four bytes.
internal sealed class ColorField<TOwner>(PropertyInfo property) : SnapshotField
    where TOwner : class
{
    private static readonly MethodInfo BitCast = typeof(Unsafe).GetMethod(nameof(Unsafe.BitCast))!.MakeGenericMethod(typeof(Color), typeof(Vector4));
    private static readonly MethodInfo Clamp = typeof(Math).GetMethod(nameof(Math.Clamp), [typeof(float), typeof(float), typeof(float)])!;
    private static readonly MethodInfo Round = typeof(MathF).GetMethod(nameof(MathF.Round), [typeof(float)])!;

    private readonly Action<TOwner, Color> _set = property.SetMethod!.CreateDelegate<Action<TOwner, Color>>();

    public override int Size => sizeof(uint);

    // Each channel as Color.RByte and the rest give it, worked out here rather than through them (calls a Debug build
    // doesn't optimize).
    public override Expression Write(Expression owner, Expression buffer, Expression data, Expression at, int offset)
    {
        var channels = Expression.Variable(typeof(Vector4), "channels");
        Expression Channel(string name, int shift) => Expression.LeftShift(
            Expression.Convert(Expression.Convert(Expression.Call(Round, Expression.Multiply(
                Expression.Call(Clamp, Expression.Field(channels, name), Expression.Constant(0f), Expression.Constant(1f)),
                Expression.Constant(255f))), typeof(byte)), typeof(uint)),
            Expression.Constant(shift));
        return Expression.Block([channels],
            Expression.Assign(channels, Expression.Call(BitCast, Expression.Property(owner, property))),
            Store(data, at, offset, Expression.Or(Expression.Or(Channel("X", 24), Channel("Y", 16)), Expression.Or(Channel("Z", 8), Channel("W", 0)))));
    }

    public override void Read(Component component, SnapshotReader reader) => _set(Unsafe.As<TOwner>(component), Unpack(reader.Read<uint>()));

    public override object? ReadBoxed(SnapshotReader reader) => Unpack(reader.Read<uint>());

    private static Color Unpack(uint value) => Color.FromBytes((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
}

// Lines of text (a tilemap's rows): how many with the bytes (-1 for none), and each line with the strings. A missing list
// leaves the default, and a missing line reads back empty, as in a scene file. The lines are copied out as they are now,
// so a list changed later doesn't change the snapshot.
internal sealed class LinesField<TOwner>(PropertyInfo property) : SnapshotField
    where TOwner : class
{
    private static readonly MethodInfo WriteLinesMethod = typeof(LinesField<TOwner>).GetMethod(nameof(WriteLines), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly Action<TOwner, IReadOnlyList<string>?> _set = property.SetMethod!.CreateDelegate<Action<TOwner, IReadOnlyList<string>?>>();

    public override int Size => sizeof(int);

    public override Expression Write(Expression owner, Expression buffer, Expression data, Expression at, int offset) =>
        Store(data, at, offset, Expression.Call(WriteLinesMethod, buffer, Expression.Property(owner, property)));

    public override void Read(Component component, SnapshotReader reader)
    {
        if (ReadLines(reader) is { } lines)
            _set(Unsafe.As<TOwner>(component), lines);
    }

    public override object? ReadBoxed(SnapshotReader reader) => ReadLines(reader);

    // Adds the lines to the strings and returns how many there are.
    private static int WriteLines(SnapshotBuffer buffer, IReadOnlyList<string>? lines)
    {
        if (lines is null)
            return -1;
        for (var i = 0; i < lines.Count; i++)
            buffer.AddRef(lines[i] ?? "");
        return lines.Count;
    }

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
