using System.Numerics;
using Griff.Networking;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Runtime;

/// <summary>Component property values and message data in their compact network form.</summary>
internal static class NetCodec
{
    public static bool Same(object? a, object? b) => ValueCodec.AreEqual(a, b);

    /// <summary>True if a value of <paramref name="type"/> can be sent (see <see cref="Write"/>).</summary>
    public static bool CanSend(Type type) =>
        type == typeof(bool) || type == typeof(int) || type == typeof(float) || type == typeof(double) || type == typeof(string) || type.IsEnum ||
        type == typeof(Color) || type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4) || type == typeof(Quaternion) ||
        type == typeof(IReadOnlyList<string>);

    public static void Write(NetWriter w, Type type, object? value)
    {
        if (type == typeof(bool)) w.Bool((bool)value!);
        else if (type == typeof(int)) w.VarInt((int)value!);
        else if (type == typeof(float)) w.Float((float)value!);
        else if (type == typeof(double)) w.Double((double)value!);
        else if (type == typeof(string)) w.String((string?)value);
        else if (type.IsEnum) w.VarInt(Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture));
        else if (type == typeof(Color)) WriteColor(w, (Color)value!);
        else if (type == typeof(Vector2)) w.Vector2((Vector2)value!);
        else if (type == typeof(Vector3)) w.Vector3((Vector3)value!);
        else if (type == typeof(Vector4))
        {
            var v = (Vector4)value!;
            w.Float(v.X); w.Float(v.Y); w.Float(v.Z); w.Float(v.W);
        }
        else if (type == typeof(Quaternion))
        {
            var q = (Quaternion)value!;
            w.Float(q.X); w.Float(q.Y); w.Float(q.Z); w.Float(q.W);
        }
        else if (type == typeof(IReadOnlyList<string>)) WriteLines(w, (IReadOnlyList<string>?)value, []);
        else throw new NotSupportedException($"Can't send a {type.Name}.");
    }

    public static object? Read(ref NetReader r, Type type, object? current = null)
    {
        if (type == typeof(bool)) return r.Bool();
        if (type == typeof(int)) return r.VarInt();
        if (type == typeof(float)) return r.Float();
        if (type == typeof(double)) return r.Double();
        if (type == typeof(string)) return r.String();
        if (type.IsEnum) return Enum.ToObject(type, r.VarInt());
        if (type == typeof(Color)) return ReadColor(ref r);
        if (type == typeof(Vector2)) return r.Vector2();
        if (type == typeof(Vector3)) return r.Vector3();
        if (type == typeof(Vector4)) return new Vector4(r.Float(), r.Float(), r.Float(), r.Float());
        if (type == typeof(Quaternion)) return new Quaternion(r.Float(), r.Float(), r.Float(), r.Float());
        if (type == typeof(IReadOnlyList<string>)) return ReadLines(ref r, current as IReadOnlyList<string>);
        throw new NotSupportedException($"Can't read a {type.Name}.");
    }

    /// <summary>
    /// A list of lines (a tilemap's rows), as only the lines that differ from every version the other side might have
    /// (<paramref name="bases"/>), when that's shorter than the whole list: digging one block sends one row.
    /// </summary>
    public static void WriteLines(NetWriter w, IReadOnlyList<string>? value, IReadOnlyList<IReadOnlyList<string>?> bases)
    {
        if (value is null)
        {
            w.Byte(0);
            return;
        }

        if (bases.Count > 0 && bases.All(b => b is not null))
        {
            var changed = new List<int>();
            for (var i = 0; i < value.Count; i++)
            {
                if (bases.Any(b => i >= b!.Count || b[i] != value[i]))
                    changed.Add(i);
            }

            if (changed.Count * 2 < value.Count)
            {
                w.Byte(2);
                w.VarUInt((uint)value.Count);
                w.VarUInt((uint)changed.Count);
                foreach (var i in changed)
                {
                    w.VarUInt((uint)i);
                    w.String(value[i]);
                }

                return;
            }
        }

        w.Byte(1);
        w.VarUInt((uint)value.Count);
        foreach (var line in value)
            w.String(line);
    }

    public static IReadOnlyList<string>? ReadLines(ref NetReader r, IReadOnlyList<string>? current)
    {
        var kind = r.Byte();
        if (kind == 0)
            return null;
        var count = checked((int)r.VarUInt());
        if (kind == 1)
        {
            var lines = new string[count];
            for (var i = 0; i < count; i++)
                lines[i] = r.String() ?? "";
            return lines;
        }

        var result = new string[count];
        for (var i = 0; i < count; i++)
            result[i] = current is not null && i < current.Count ? current[i] : "";
        var changed = r.VarUInt();
        for (var c = 0; c < changed; c++)
        {
            var index = checked((int)r.VarUInt());
            var line = r.String() ?? "";
            if (index < count)
                result[index] = line;
        }

        return result;
    }

    private static void WriteColor(NetWriter w, Color c)
    {
        w.Float(c.R);
        w.Float(c.G);
        w.Float(c.B);
        w.Float(c.A);
    }

    private static Color ReadColor(ref NetReader r) => new(r.Float(), r.Float(), r.Float(), r.Float());

    // ----- Message data: what Game.Network.Send can carry -----

    private enum DataKind : byte
    {
        Null, Bool, Int, Float, Double, String, Vector2, Vector3, Color, Entity, Player, Strings, Long, List,
    }

    public static bool CanSend(object? data) => data is null or bool or int or float or double or string or Vector2 or Vector3 or Color
        or Entity or NetPlayer or string[] or long or IReadOnlyList<string> || (data is object?[] list && list.All(CanSend));

    public static void WriteData(NetWriter w, object? data, Func<Entity, uint> netIdOf)
    {
        switch (data)
        {
            case null: w.Byte((byte)DataKind.Null); break;
            case bool b: w.Byte((byte)DataKind.Bool); w.Bool(b); break;
            case int i: w.Byte((byte)DataKind.Int); w.VarInt(i); break;
            case long l: w.Byte((byte)DataKind.Long); w.Double(l); break;
            case float f: w.Byte((byte)DataKind.Float); w.Float(f); break;
            case double d: w.Byte((byte)DataKind.Double); w.Double(d); break;
            case string s: w.Byte((byte)DataKind.String); w.String(s); break;
            case Vector2 v: w.Byte((byte)DataKind.Vector2); w.Vector2(v); break;
            case Vector3 v: w.Byte((byte)DataKind.Vector3); w.Vector3(v); break;
            case Color c: w.Byte((byte)DataKind.Color); WriteColor(w, c); break;
            case Entity e: w.Byte((byte)DataKind.Entity); w.VarUInt(netIdOf(e)); break;
            case NetPlayer p: w.Byte((byte)DataKind.Player); w.VarInt(p.Id); break;
            case IReadOnlyList<string> lines:
                w.Byte((byte)DataKind.Strings);
                w.VarUInt((uint)lines.Count);
                foreach (var line in lines)
                    w.String(line);
                break;
            case object?[] list:
                w.Byte((byte)DataKind.List);
                w.VarUInt((uint)list.Length);
                foreach (var item in list)
                    WriteData(w, item, netIdOf);
                break;
            default:
                throw new ArgumentException(
                    $"A {data.GetType().Name} can't be sent to other players. Send a number, text, a vector, a color, an entity or a player.",
                    nameof(data));
        }
    }

    public static object? ReadData(ref NetReader r, Func<uint, Entity?> entityOf, Func<int, NetPlayer?> playerOf)
    {
        switch ((DataKind)r.Byte())
        {
            case DataKind.Null: return null;
            case DataKind.Bool: return r.Bool();
            case DataKind.Int: return r.VarInt();
            case DataKind.Long: return (long)r.Double();
            case DataKind.Float: return r.Float();
            case DataKind.Double: return r.Double();
            case DataKind.String: return r.String();
            case DataKind.Vector2: return r.Vector2();
            case DataKind.Vector3: return r.Vector3();
            case DataKind.Color: return ReadColor(ref r);
            case DataKind.Entity: return entityOf(r.VarUInt());
            case DataKind.Player: return playerOf(r.VarInt());
            case DataKind.List:
            {
                var count = checked((int)r.VarUInt());
                if (count > 4096)
                    throw new NetFormatException("A list in the message is too long.");
                var list = new object?[count];
                for (var i = 0; i < count; i++)
                    list[i] = ReadData(ref r, entityOf, playerOf);
                return list;
            }

            case DataKind.Strings:
            {
                var count = checked((int)r.VarUInt());
                var lines = new string[count];
                for (var i = 0; i < count; i++)
                    lines[i] = r.String() ?? "";
                return lines;
            }

            default:
                throw new NetFormatException("Unknown message data.");
        }
    }
}
