using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
namespace Griff.Scenes.Serialization;

/// <summary>
/// What a game keeps besides its scene, as a rewind snapshot holds it (see <see cref="IKeepsState"/>): its parts' fields
/// as bytes, with what can't be bytes (text, the types and methods things are made of, the objects kept as they are)
/// beside them. Written by <see cref="StateWriter"/>, read back by <see cref="StateReader"/>.
/// </summary>
internal sealed class GameState
{
    public byte[] Data = [];
    public object?[] Refs = [];
    public int Length;
    public int RefCount;

    /// <summary>About the memory it takes.</summary>
    public long Bytes => Data.Length + Refs.Length * 8L;
}

// How a value is written: what follows each tag.
internal enum StateTag : byte
{
    Null,

    // An object written earlier in the same state: its number.
    Back,

    // An object kept as it is (a part of the game, a type, a method, something from outside the game): a ref.
    Same,

    // A string: a ref.
    Text,

    // An entity: its id, and the entity itself for when the scene put back hasn't one with that id (one destroyed).
    Entity,

    // A component: its entity's id, its place among the entity's components, and the component itself (as for Entity).
    Component,

    // The scene the game is in: the one it's put back with.
    TheScene,

    // A value in a box (something of type object holding a number or a struct): its type, then the value.
    Boxed,

    // An array: its type, length and elements.
    Array,

    // A List: its type, count and items.
    List,

    // A Dictionary (or an OrderedDictionary): its type, comparer, count and pairs, in order.
    Map,

    // A HashSet: its type, comparer, count and items, in order.
    Set,

    // Another collection (Queue, Stack, LinkedList, SortedSet, SortedDictionary): its type, comparer, count and items.
    Sequence,

    // A delegate: its type, then each method in it with what it's called on.
    Delegate,

    // Any other object of Griff's or a script's (a closure, a coroutine, a record): its type, then its fields.
    Object,
}

/// <summary>What the state codec knows of a type, worked out once.</summary>
internal sealed class StatePlan
{
    // Held weakly, so a script reloaded in the editor doesn't keep its last version's types alive.
    private static readonly ConditionalWeakTable<Type, StatePlan> s_plans = new();

    private static readonly Assembly s_griff = typeof(Entity).Assembly;
    private static readonly ConditionalWeakTable<Assembly, StrongBox<bool>> s_copyable = new();

    public static StatePlan Of(Type type) => s_plans.GetValue(type, static t => new StatePlan(t));

    private StatePlan(Type type)
    {
        Type = type;
        if (type.IsValueType)
        {
            Kind = StateTag.Boxed;
            Unmanaged = !(bool)typeof(RuntimeHelpers).GetMethod(nameof(RuntimeHelpers.IsReferenceOrContainsReferences))!
                .MakeGenericMethod(type).Invoke(null, null)!;
            Size = (int)typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf))!.MakeGenericMethod(type).Invoke(null, null)!;
            Fields = Unmanaged ? [] : FieldsOf(type);
            return;
        }

        if (typeof(System.Delegate).IsAssignableFrom(type))
        {
            Kind = StateTag.Delegate;
            return;
        }

        if (type.IsArray)
        {
            Kind = type.IsSZArray ? StateTag.Array : StateTag.Same;
            Element = type.GetElementType();
            ElementPlan = Element is { IsValueType: true } element ? Of(element) : null;
            return;
        }

        if (IsPart(type))
        {
            Kind = StateTag.Same;
            return;
        }

        if (type.IsGenericType && type.Namespace == "System.Collections.Generic")
        {
            var definition = type.GetGenericTypeDefinition();
            var arguments = type.GetGenericArguments();
            if (definition == typeof(List<>))
            {
                (Kind, Element) = (StateTag.List, arguments[0]);
                // A list of references, or of plain values, written straight from its items rather than one by one as objects.
                if (!Element.IsValueType)
                    (WriteItems, ReadItems) = (Make<Action<StateWriter, object>>(typeof(StateWriter), nameof(StateWriter.ReferenceItems)), null);
                else if (Nullable.GetUnderlyingType(Element) is null && Of(Element).Unmanaged)
                    (WriteItems, ReadItems) = (Make<Action<StateWriter, object>>(typeof(StateWriter), nameof(StateWriter.UnmanagedItems)),
                        Make<Func<StateReader, int, object>>(typeof(StateReader), nameof(StateReader.UnmanagedItems)));
            }
            else if (definition == typeof(Dictionary<,>) || definition == typeof(OrderedDictionary<,>))
            {
                (Kind, Element, Value) = (StateTag.Map, arguments[0], arguments[1]);
                WriteItems = typeof(StateWriter).GetMethod(definition == typeof(Dictionary<,>) ? nameof(StateWriter.MapItems) : nameof(StateWriter.OrderedMapItems),
                    BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(arguments).CreateDelegate<Action<StateWriter, object>>();
            }
            else if (definition == typeof(HashSet<>))
            {
                (Kind, Element) = (StateTag.Set, arguments[0]);
                WriteItems = Make<Action<StateWriter, object>>(typeof(StateWriter), nameof(StateWriter.SetItems));
            }
            else if (definition == typeof(Queue<>) || definition == typeof(Stack<>) || definition == typeof(LinkedList<>) ||
                     definition == typeof(SortedSet<>))
                (Kind, Element) = (StateTag.Sequence, arguments[0]);
            else if (definition == typeof(SortedDictionary<,>))
                (Kind, Element, Value) = (StateTag.Sequence, typeof(KeyValuePair<,>).MakeGenericType(arguments), null);
            else
                Kind = StateTag.Same;

            if (Kind is not StateTag.Same)
            {
                Comparer = type.GetProperty("Comparer", BindingFlags.Public | BindingFlags.Instance);
                Count = type.GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
                Add = definition == typeof(Queue<>) ? type.GetMethod("Enqueue")
                    : definition == typeof(Stack<>) ? type.GetMethod("Push")
                    : definition == typeof(LinkedList<>) ? type.GetMethod("AddLast", [arguments[0]])
                    : definition == typeof(SortedDictionary<,>) ? type.GetMethod("Add", arguments)
                    : type.GetMethod("Add", [arguments[0]]);
                Reversed = definition == typeof(Stack<>);
                ElementPlan = Element is { IsValueType: true } element ? Of(element) : null;
            }

            return;
        }

        // Griff's and scripts' own objects are copied field by field, as long as everything they're made of is theirs too
        // (or a Random, whose numbers they make themselves: the game's own random numbers, a board's).
        for (var t = type; t is not null && t != typeof(object) && (t != typeof(Random) || t == type); t = t.BaseType)
        {
            if (!IsCopyable(t.Assembly) || t.IsAbstract && t == type)
            {
                Kind = StateTag.Same;
                return;
            }
        }

        Kind = StateTag.Object;
        Fields = FieldsOf(type);
    }

    // Writes the fields of an object (or a boxed struct) of this type, compiled the first time it's needed.
    public Action<object, StateWriter> WriteFields => _writeFields ??= Compile(Type, Fields);

    private Action<object, StateWriter>? _writeFields;

    private static readonly MethodInfo s_value = typeof(StateWriter).GetMethod(nameof(StateWriter.Value))!;
    private static readonly MethodInfo s_write = typeof(StateWriter).GetMethod(nameof(StateWriter.Write))!;

    /// <summary>Code that writes <paramref name="fields"/> of a <paramref name="type"/> (boxed, if it's a struct).</summary>
    public static Action<object, StateWriter> Compile(Type type, FieldInfo[] fields)
    {
        if (fields.Length == 0)
            return static (_, _) => { };
        var target = System.Linq.Expressions.Expression.Parameter(typeof(object), "target");
        var writer = System.Linq.Expressions.Expression.Parameter(typeof(StateWriter), "writer");
        var typed = System.Linq.Expressions.Expression.Variable(type, "typed");
        var body = new List<System.Linq.Expressions.Expression>
        {
            System.Linq.Expressions.Expression.Assign(typed, type.IsValueType
                ? System.Linq.Expressions.Expression.Unbox(target, type)
                : System.Linq.Expressions.Expression.Convert(target, type)),
        };
        foreach (var field in fields)
        {
            var value = System.Linq.Expressions.Expression.Field(typed, field);
            var fieldType = field.FieldType;
            body.Add(fieldType.IsValueType && Nullable.GetUnderlyingType(fieldType) is null && Of(fieldType).Unmanaged
                ? System.Linq.Expressions.Expression.Call(writer, s_write.MakeGenericMethod(fieldType), value)
                : System.Linq.Expressions.Expression.Call(writer, s_value, System.Linq.Expressions.Expression.Convert(value, typeof(object)),
                    System.Linq.Expressions.Expression.Constant(fieldType, typeof(Type))));
        }

        return System.Linq.Expressions.Expression.Lambda<Action<object, StateWriter>>(
            System.Linq.Expressions.Expression.Block([typed], body), target, writer).Compile();
    }

    public Type Type { get; }

    public StateTag Kind { get; }

    // A value type with no references in it, written as its bytes.
    public bool Unmanaged { get; }

    public int Size { get; }

    public FieldInfo[] Fields { get; } = [];

    // A collection's items (or keys) and values.
    public Type? Element { get; }

    public Type? Value { get; }

    public StatePlan? ElementPlan { get; }

    // A List's items, written and read back all at once (see the constructor).
    public Action<StateWriter, object>? WriteItems { get; }

    public Func<StateReader, int, object>? ReadItems { get; }

    private T Make<T>(Type on, string method) where T : System.Delegate =>
        on.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(Element!).CreateDelegate<T>();

    public PropertyInfo? Comparer { get; }

    public PropertyInfo? Count { get; }

    public MethodInfo? Add { get; }

    public bool Reversed { get; }

    /// <summary>
    /// A part of the game, kept in place (see <see cref="IKeepsState"/>), or something marked as belonging to the game
    /// rather than its moving state (<see cref="NotStateAttribute"/>): pointing to one is kept as pointing to it.
    /// </summary>
    public static bool IsPart(Type type) =>
        typeof(IKeepsState).IsAssignableFrom(type) || type.IsDefined(typeof(NotStateAttribute), inherit: true) ||
        type.GetInterfaces().Any(i => i.IsDefined(typeof(NotStateAttribute)));

    // Griff's own assembly and its modules', or a game's scripts (anything else built on Griff that isn't one of Griff's own hosts: the
    // editor, the players, the script compiler). The framework's objects, and the hosts', are kept as they are.
    public static bool IsCopyable(Assembly assembly) => assembly == s_griff || ModuleAssemblies.IsModule(assembly) || s_copyable.GetValue(assembly, static a =>
    {
        var name = a.GetName().Name ?? "";
        var host = name.StartsWith("Griff.", StringComparison.Ordinal) && !name.EndsWith(".Tests", StringComparison.Ordinal);
        return new StrongBox<bool>(!host && a.GetReferencedAssemblies().Any(r => r.Name == "Griff"));
    }).Value;

    // Every instance field of `type` and the types it's built on, but those that aren't state.
    private static FieldInfo[] FieldsOf(Type type)
    {
        var fields = new List<FieldInfo>();
        for (var t = type; t is not null && t != typeof(object) && t != typeof(ValueType) && t != typeof(Random); t = t.BaseType)
        {
            foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!field.IsDefined(typeof(NotStateAttribute)))
                    fields.Add(field);
            }
        }

        return [.. fields];
    }

    /// <summary>
    /// The fields of <paramref name="type"/> a snapshot keeps, declared from it up to <paramref name="upTo"/> (included):
    /// a part's own state, all but those marked <see cref="NotStateAttribute"/>.
    /// </summary>
    public static FieldInfo[] KeptFields(Type type, Type upTo)
    {
        var fields = new List<FieldInfo>();
        for (var t = type; t is not null; t = t.BaseType)
        {
            foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!field.IsDefined(typeof(NotStateAttribute)))
                    fields.Add(field);
            }

            if (t == upTo)
                break;
        }

        return [.. fields];
    }

    /// <summary>
    /// Null if a field of type <paramref name="type"/> can be kept in a snapshot; else why not. Every value type and string,
    /// entities, components and the scene, the parts of the game (kept as they are), delegates and coroutines, the
    /// collections of those, and Griff's and scripts' own objects made of those can.
    /// </summary>
    public static string? WhyNotKeepable(Type type) => WhyNot(type, []);

    // A coroutine's steps: an iterator the compiler made, kept by what it holds (where it has got to, its locals).
    private static bool IsCoroutine(Type type) =>
        type == typeof(IEnumerator) || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerator<>);

    private static string? WhyNot(Type type, HashSet<Type> checking)
    {
        if (Nullable.GetUnderlyingType(type) is { } inner)
            return WhyNot(inner, checking);
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type.IsPointer)
            return null;
        if (typeof(Entity).IsAssignableFrom(type) || typeof(Component).IsAssignableFrom(type) || type == typeof(Scene) ||
            typeof(System.Delegate).IsAssignableFrom(type) || IsCoroutine(type) || IsPart(type))
            return null;
        if (!checking.Add(type))
            return null;
        if (type.IsArray)
            return WhyNot(type.GetElementType()!, checking);
        if (type.IsValueType)
        {
            if (Of(type).Unmanaged)
                return null;
            foreach (var field in FieldsOf(type))
            {
                if (WhyNot(field.FieldType, checking) is { } why)
                    return $"{type.Name}.{field.Name}: {why}";
            }

            return null;
        }

        // Something of any type is kept by what it holds (and the framework's collections by their items); an interface or
        // an abstract class of Griff's needs a decision: a part of the game is marked as one, or the field as not state.
        if (type == typeof(object))
            return null;
        if (type.IsInterface && type.IsGenericType && type.Namespace == "System.Collections.Generic")
        {
            foreach (var argument in type.GetGenericArguments())
            {
                if (WhyNot(argument, checking) is { } why)
                    return why;
            }

            return null;
        }

        if (type.IsInterface || type.IsAbstract)
            return $"{type.Name} could hold anything: give it a type of its own, or mark it [NotState] if it isn't state";
        var plan = Of(type);
        if (plan.Kind is StateTag.List or StateTag.Map or StateTag.Set or StateTag.Sequence)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                if (WhyNot(argument, checking) is { } why)
                    return why;
            }

            return null;
        }

        if (plan.Kind != StateTag.Object)
            return $"a {type.Name} can't be kept in a snapshot";
        foreach (var field in plan.Fields)
        {
            if (WhyNot(field.FieldType, checking) is { } why)
                return $"{type.Name}.{field.Name}: {why}";
        }

        return null;
    }
}

/// <summary>Writes parts of a game into a <see cref="GameState"/>; one is kept and reused, so writing makes little garbage.</summary>
internal sealed class StateWriter
{
    private byte[] _data = new byte[4096];
    private int _length;
    private object?[] _refs = new object?[256];
    private int _refCount;
    private readonly Dictionary<object, int> _memo = new(ReferenceEqualityComparer.Instance);
    private Scene? _scene;

    public void Begin(Scene scene)
    {
        _length = 0;
        _refCount = 0;
        _memo.Clear();
        _scene = scene;
    }

    /// <summary>What was written, into <paramref name="state"/> (reusing its arrays when they're big enough).</summary>
    public GameState Finish(GameState? state = null)
    {
        state ??= new GameState();
        if (state.Data.Length < _length)
            state.Data = new byte[_length];
        if (state.Refs.Length < _refCount)
            state.Refs = new object?[_refCount];
        _data.AsSpan(0, _length).CopyTo(state.Data);
        _refs.AsSpan(0, _refCount).CopyTo(state.Refs);
        state.Refs.AsSpan(_refCount).Clear();
        (state.Length, state.RefCount) = (_length, _refCount);
        _refs.AsSpan(0, _refCount).Clear();
        _memo.Clear();
        _scene = null;
        return state;
    }

    public void Write<T>(T value) where T : unmanaged
    {
        var size = Unsafe.SizeOf<T>();
        Ensure(size);
        Unsafe.WriteUnaligned(ref _data[_length], value);
        _length += size;
    }

    private void Tag(StateTag tag) => Write((byte)tag);

    public void Ref(object? value)
    {
        if (_refCount == _refs.Length)
            Array.Resize(ref _refs, _refs.Length * 2);
        _refs[_refCount++] = value;
    }

    private void Ensure(int more)
    {
        if (_length + more > _data.Length)
            Array.Resize(ref _data, Math.Max(_data.Length * 2, _length + more));
    }

    internal void Bytes(ref byte from, int count)
    {
        if (count == 0)
            return;
        Ensure(count);
        Unsafe.CopyBlockUnaligned(ref _data[_length], ref from, (uint)count);
        _length += count;
    }

    /// <summary>The fields of <paramref name="part"/> that <paramref name="fields"/> name.</summary>
    public void Fields(object part, FieldInfo[] fields)
    {
        foreach (var field in fields)
            Value(field.GetValue(part), field.FieldType);
    }

    /// <summary>A value of a field (or item) of type <paramref name="type"/>.</summary>
    public void Value(object? value, Type type)
    {
        if (!type.IsValueType)
        {
            Reference(value);
            return;
        }

        if (Nullable.GetUnderlyingType(type) is { } inner)
        {
            Write(value is null ? (byte)0 : (byte)1);
            if (value is not null)
                Struct(value, StatePlan.Of(inner));
            return;
        }

        Struct(value!, StatePlan.Of(type));
    }

    private void Struct(object boxed, StatePlan plan)
    {
        if (plan.Unmanaged)
        {
            Bytes(ref RawData(boxed), plan.Size);
            return;
        }

        plan.WriteFields(boxed, this);
    }

    internal void Reference(object? value)
    {
        switch (value)
        {
            case null:
                Tag(StateTag.Null);
                return;
            case string text:
                Tag(StateTag.Text);
                Ref(text);
                return;
            case Entity entity:
                Tag(StateTag.Entity);
                Write(entity.Id);
                Ref(entity);
                return;
            case Component component:
                if (component.Entity is { } owner && IndexOf(owner, component) is var index and >= 0)
                {
                    Tag(StateTag.Component);
                    Write(owner.Id);
                    Write(index);
                }
                else
                    Tag(StateTag.Same);

                Ref(component);
                return;
            case Scene scene when scene == _scene:
                Tag(StateTag.TheScene);
                return;
        }

        if (_memo.TryGetValue(value, out var seen))
        {
            Tag(StateTag.Back);
            Write(seen);
            return;
        }

        var type = value.GetType();
        var plan = StatePlan.Of(type);
        if (plan.Kind == StateTag.Same)
        {
            Tag(StateTag.Same);
            Ref(value);
            return;
        }

        _memo.Add(value, _memo.Count);
        Tag(plan.Kind);
        Ref(type);
        switch (plan.Kind)
        {
            case StateTag.Boxed:
                Struct(value, plan);
                break;
            case StateTag.Array:
                var array = (Array)value;
                Write(array.Length);
                if (plan.ElementPlan is { Unmanaged: true } items)
                    Bytes(ref MemoryMarshal.GetArrayDataReference(array), array.Length * items.Size);
                else if (!plan.Element!.IsValueType)
                {
                    foreach (var item in (object?[])array)
                        Reference(item);
                }
                else
                {
                    foreach (var item in array)
                        Value(item, plan.Element!);
                }

                break;
            case StateTag.List:
                var list = (IList)value;
                Write(list.Count);
                if (plan.WriteItems is { } writeItems)
                    writeItems(this, value);
                else
                {
                    for (var i = 0; i < list.Count; i++)
                        Value(list[i], plan.Element!);
                }

                break;
            case StateTag.Map:
            case StateTag.Set:
                plan.WriteItems!(this, value);
                break;
            case StateTag.Sequence:
                Ref(plan.Comparer?.GetValue(value));
                Write((int)plan.Count!.GetValue(value)!);
                if (plan.Reversed)
                {
                    var stack = ((IEnumerable)value).Cast<object?>().ToArray();
                    for (var i = stack.Length - 1; i >= 0; i--)
                        Value(stack[i], plan.Element!);
                }
                else
                {
                    foreach (var item in (IEnumerable)value)
                        Value(item, plan.Element!);
                }

                break;
            case StateTag.Delegate:
                var callback = (System.Delegate)value;
                if (callback.HasSingleTarget)
                {
                    Write(1);
                    Ref(callback.Method);
                    Reference(callback.Target);
                }
                else
                {
                    var calls = callback.GetInvocationList();
                    Write(calls.Length);
                    foreach (var call in calls)
                    {
                        Ref(call.Method);
                        Reference(call.Target);
                    }
                }

                break;
            default:
                plan.WriteFields(value, this);
                break;
        }
    }

    // What follows each component written: a script that hasn't started yet, or the fields a component keeps.
    internal const byte ComponentsEnd = 0, ComponentFields = 1, Waiting = 2;

    /// <summary>
    /// What <paramref name="component"/> (at <paramref name="index"/> among <paramref name="entity"/>'s) keeps besides its
    /// saved properties: whether it's a script still to start, and the fields <paramref name="kept"/> names unless it's at
    /// rest (see <see cref="Component.IsAtRest"/>).
    /// </summary>
    public void Component(Entity entity, int index, Component component, KeptFields kept)
    {
        if (component.IsWaitingToStart)
        {
            Write(Waiting);
            Write(entity.Id);
            Write(index);
        }

        if (kept.Fields.Length == 0 || component.IsAtRest)
            return;
        Write(ComponentFields);
        Write(entity.Id);
        Write(index);
        kept.Write(component, this);
    }

    // A value of type T, as Value writes it, without putting it in a box when it's plain bytes.
    private void ValueOf<T>(T value)
    {
        if (typeof(T).IsValueType && !RuntimeHelpers.IsReferenceOrContainsReferences<T>() && Nullable.GetUnderlyingType(typeof(T)) is null)
        {
            var size = Unsafe.SizeOf<T>();
            Ensure(size);
            Unsafe.WriteUnaligned(ref _data[_length], value);
            _length += size;
        }
        else if (typeof(T).IsValueType)
            Value(value, typeof(T));
        else
            Reference(value);
    }

    internal static void MapItems<TKey, TValue>(StateWriter writer, object map) where TKey : notnull
    {
        var items = (Dictionary<TKey, TValue>)map;
        writer.Ref(items.Comparer);
        writer.Write(items.Count);
        foreach (var (key, value) in items)
        {
            writer.ValueOf(key);
            writer.ValueOf(value);
        }
    }

    internal static void OrderedMapItems<TKey, TValue>(StateWriter writer, object map) where TKey : notnull
    {
        var items = (OrderedDictionary<TKey, TValue>)map;
        writer.Ref(items.Comparer);
        writer.Write(items.Count);
        foreach (var (key, value) in items)
        {
            writer.ValueOf(key);
            writer.ValueOf(value);
        }
    }

    internal static void SetItems<T>(StateWriter writer, object set)
    {
        var items = (HashSet<T>)set;
        writer.Ref(items.Comparer);
        writer.Write(items.Count);
        foreach (var item in items)
            writer.ValueOf(item);
    }

    internal static void ReferenceItems<T>(StateWriter writer, object list) where T : class
    {
        foreach (var item in CollectionsMarshal.AsSpan((List<T>)list))
            writer.Reference(item);
    }

    internal static void UnmanagedItems<T>(StateWriter writer, object list) where T : unmanaged
    {
        var bytes = MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan((List<T>)list));
        if (bytes.Length > 0)
            writer.Bytes(ref MemoryMarshal.GetReference(bytes), bytes.Length);
    }

    private static int IndexOf(Entity entity, Component component)
    {
        var components = entity.Components;
        for (var i = 0; i < components.Count; i++)
        {
            if (components[i] == component)
                return i;
        }

        return -1;
    }

    // The bytes of a boxed value (just past the box's header).
    internal static ref byte RawData(object boxed) => ref Unsafe.As<RawBox>(boxed).Data;

    private sealed class RawBox
    {
        public byte Data;
    }
}

/// <summary>Reads a <see cref="GameState"/> back into a game's parts, the entities and components it names being those of the scene put back.</summary>
internal sealed class StateReader(GameState state, Scene scene)
{
    private int _at;
    private int _ref;
    private readonly List<object?> _memo = [];

    public bool AtEnd => _at >= state.Length;

    public T Read<T>() where T : unmanaged
    {
        var value = Unsafe.ReadUnaligned<T>(ref state.Data[_at]);
        _at += Unsafe.SizeOf<T>();
        return value;
    }

    public object? Ref() => state.Refs[_ref++];

    internal static object UnmanagedItems<T>(StateReader reader, int count) where T : unmanaged
    {
        var list = new List<T>(count);
        CollectionsMarshal.SetCount(list, count);
        var bytes = MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(list));
        if (bytes.Length > 0)
            reader.Take(bytes.Length).CopyTo(bytes);
        return list;
    }

    private Span<byte> Take(int count)
    {
        var bytes = state.Data.AsSpan(_at, count);
        _at += count;
        return bytes;
    }

    /// <summary>Puts back the fields of <paramref name="part"/> that <paramref name="fields"/> name.</summary>
    public void Fields(object part, FieldInfo[] fields)
    {
        foreach (var field in fields)
            field.SetValue(part, Value(field.FieldType));
    }

    public object? Value(Type type)
    {
        if (!type.IsValueType)
            return Reference();
        if (Nullable.GetUnderlyingType(type) is { } inner)
            return Read<byte>() == 0 ? null : Struct(StatePlan.Of(inner));
        return Struct(StatePlan.Of(type));
    }

    private object Struct(StatePlan plan)
    {
        var boxed = RuntimeHelpers.GetUninitializedObject(plan.Type);
        if (plan.Unmanaged)
        {
            Unsafe.CopyBlockUnaligned(ref StateWriter.RawData(boxed), ref state.Data[_at], (uint)plan.Size);
            _at += plan.Size;
            return boxed;
        }

        foreach (var field in plan.Fields)
            field.SetValue(boxed, Value(field.FieldType));
        return boxed;
    }

    private object? Reference()
    {
        var tag = (StateTag)Read<byte>();
        switch (tag)
        {
            case StateTag.Null:
                return null;
            case StateTag.Back:
                return _memo[Read<int>()];
            case StateTag.Same:
            case StateTag.Text:
                return Ref();
            case StateTag.Entity:
            {
                var id = Read<Guid>();
                var original = Ref();
                return scene.Find(id) ?? original;
            }
            case StateTag.Component:
            {
                var id = Read<Guid>();
                var index = Read<int>();
                var original = (Component)Ref()!;
                return scene.Find(id) is { } entity && index < entity.Components.Count && entity.Components[index].GetType() == original.GetType()
                    ? entity.Components[index]
                    : original;
            }
            case StateTag.TheScene:
                return scene;
        }

        var at = _memo.Count;
        _memo.Add(null);
        var type = (Type)Ref()!;
        var plan = StatePlan.Of(type);
        switch (tag)
        {
            case StateTag.Boxed:
                return _memo[at] = Struct(plan);
            case StateTag.Array:
            {
                var length = Read<int>();
                var array = Array.CreateInstance(plan.Element!, length);
                _memo[at] = array;
                if (plan.ElementPlan is { Unmanaged: true } items)
                {
                    var size = length * items.Size;
                    if (size == 0)
                        return array;
                    Unsafe.CopyBlockUnaligned(ref MemoryMarshal.GetArrayDataReference(array), ref state.Data[_at], (uint)size);
                    _at += size;
                }
                else
                {
                    for (var i = 0; i < length; i++)
                        array.SetValue(Value(plan.Element!), i);
                }

                return array;
            }
            case StateTag.List when plan.ReadItems is { } readItems:
                return _memo[at] = readItems(this, Read<int>());
            case StateTag.List:
            {
                var count = Read<int>();
                var list = (IList)Activator.CreateInstance(type, count)!;
                _memo[at] = list;
                for (var i = 0; i < count; i++)
                    list.Add(Value(plan.Element!));
                return list;
            }
            case StateTag.Map:
            {
                var comparer = Ref();
                var count = Read<int>();
                var map = (IDictionary)Activator.CreateInstance(type, comparer)!;
                _memo[at] = map;
                for (var i = 0; i < count; i++)
                {
                    var key = Value(plan.Element!)!;
                    map.Add(key, Value(plan.Value!));
                }

                return map;
            }
            case StateTag.Set:
            case StateTag.Sequence:
            {
                var comparer = Ref();
                var count = Read<int>();
                var collection = comparer is null ? Activator.CreateInstance(type)! : Activator.CreateInstance(type, comparer)!;
                _memo[at] = collection;
                var add = plan.Add!;
                var pair = plan.Element!.IsGenericType && plan.Element.GetGenericTypeDefinition() == typeof(KeyValuePair<,>)
                    && add.GetParameters().Length == 2;
                for (var i = 0; i < count; i++)
                {
                    var item = Value(plan.Element!);
                    if (pair)
                    {
                        var keyValue = plan.Element!;
                        add.Invoke(collection, [keyValue.GetProperty("Key")!.GetValue(item), keyValue.GetProperty("Value")!.GetValue(item)]);
                    }
                    else
                        add.Invoke(collection, [item]);
                }

                return collection;
            }
            case StateTag.Delegate:
            {
                var count = Read<int>();
                System.Delegate? callback = null;
                for (var i = 0; i < count; i++)
                {
                    var method = (MethodInfo)Ref()!;
                    var target = Reference();
                    var call = target is null && method.IsStatic
                        ? System.Delegate.CreateDelegate(type, method)
                        : System.Delegate.CreateDelegate(type, target, method);
                    callback = System.Delegate.Combine(callback, call);
                }

                return _memo[at] = callback;
            }
            default:
            {
                var made = RuntimeHelpers.GetUninitializedObject(type);
                _memo[at] = made;
                foreach (var field in plan.Fields)
                    field.SetValue(made, Value(field.FieldType));
                return made;
            }
        }
    }
}

/// <summary>
/// The fields a snapshot keeps of one type of thing, with code made for them to write their values quickly: a part's
/// (a system's, the game's), or a component's own (those the scene file doesn't hold).
/// </summary>
internal sealed class KeptFields
{
    private static readonly ConditionalWeakTable<Type, KeptFields> s_parts = new();
    private static readonly ConditionalWeakTable<Type, KeptFields> s_components = new();
    private KeptFields(FieldInfo[] fields, Type type)
    {
        Fields = fields;
        Write = StatePlan.Compile(type, fields);
    }

    public FieldInfo[] Fields { get; }

    /// <summary>Writes the fields' values of a thing of this type.</summary>
    public Action<object, StateWriter> Write { get; }

    /// <summary>
    /// A part of the game's: every field of its type and the types it's built on, down to the first that's a part
    /// (<see cref="IKeepsState"/>: a system's down to GameSystem's), but those marked NotState.
    /// </summary>
    public static KeptFields OfPart(Type type) => s_parts.GetValue(type, static t =>
    {
        var upTo = t;
        while (upTo.BaseType is { } below && typeof(IKeepsState).IsAssignableFrom(below))
            upTo = below;
        return new KeptFields(StatePlan.KeptFields(t, upTo), t);
    });

    /// <summary>
    /// A component's own: the fields of its type and the types between it and Component but those the scene file holds
    /// already (behind its saved properties) and those marked NotState.
    /// </summary>
    public static KeptFields OfComponent(Type type) => s_components.GetValue(type, static t =>
    {
        // What's saved by the one rule its ComponentModel keeps (found without checking each can be saved, as a test's needn't).
        var saved = ComponentModel.Writable(t)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fields = new List<FieldInfo>();
        for (var at = t; at is not null && at != typeof(Component); at = at.BaseType)
        {
            foreach (var field in at.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.IsDefined(typeof(NotStateAttribute)) || saved.Contains(Backs(field.Name)))
                    continue;
                fields.Add(field);
            }
        }

        return new KeptFields([.. fields], t);
    });

    // The property a field is behind, by its name: an auto property's own (<Size>k__BackingField) or one named like it (_size).
    private static string Backs(string field) =>
        field.StartsWith('<') && field.IndexOf('>') is var end and > 1 ? field[1..end] : field.TrimStart('_');

}
