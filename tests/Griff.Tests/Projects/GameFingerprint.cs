using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Projects;

/// <summary>
/// What a game is doing, as text and as a short hash: the scene it's in, then every entity in order (its depth, name,
/// flags and prefab) with each component's values, numbers to the last bit. It reads the components' values themselves
/// rather than how a scene file writes them, so a change to the file format (a component's version, a key's spelling)
/// leaves it alone while a change to what any value is doesn't. Ids are left out (spawned entities get new ones each
/// run): a value naming an entity by id says which entity, by its place in the scene.
/// </summary>
internal static partial class GameFingerprint
{
    public static string Of(string? sceneName, Scene scene, ComponentRegistry registry) => Hash(Describe(sceneName, scene, registry));

    public static string Hash(string description) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(description)))[..16];

    public static string Describe(string? sceneName, Scene scene, ComponentRegistry registry)
    {
        var places = new Dictionary<Guid, int>();
        foreach (var root in scene.Roots)
        {
            foreach (var entity in root.SelfAndDescendants())
                places[entity.Id] = places.Count;
        }

        var text = new StringBuilder();
        text.Append("scene ").Append(sceneName).Append('\n');
        foreach (var root in scene.Roots)
            Write(root, 0);
        return text.ToString();

        void Write(Entity entity, int depth)
        {
            text.Append(' ', depth * 2).Append(Quote(entity.Name))
                .Append(entity.IsEnabled ? "" : " off").Append(entity.IsHidden ? " hidden" : "")
                .Append(entity.Prefab is { } prefab ? " from " + Quote(prefab) : "").Append('\n');
            foreach (var component in entity.Components)
            {
                text.Append(' ', depth * 2 + 1).Append('[');
                if (component is MissingComponent missing)
                {
                    text.Append("missing ").Append(Ids().Replace(missing.Data.GetRawText(), "#")).Append("]\n");
                    continue;
                }

                var descriptor = registry.Get(component.GetType());
                text.Append(descriptor.Name).Append(']');
                foreach (var property in descriptor.Properties)
                    text.Append(' ').Append(property.Name).Append('=').Append(Value(property.GetValue(component)));
                text.Append('\n');
            }

            foreach (var child in entity.Children)
                Write(child, depth + 1);
        }

        string Value(object? value) => value switch
        {
            null => "null",
            string s => Quote(Ids().Replace(s, m => Guid.TryParse(m.Value, out var id) && places.TryGetValue(id, out var at) ? "#" + at : "#")),
            Guid id => places.TryGetValue(id, out var at) ? "#" + at : "#",
            bool b => b ? "true" : "false",
            float f => Bits(f),
            double d => BitConverter.DoubleToInt64Bits(d).ToString("X16", CultureInfo.InvariantCulture),
            Vector2 v => $"({Bits(v.X)},{Bits(v.Y)})",
            Vector3 v => $"({Bits(v.X)},{Bits(v.Y)},{Bits(v.Z)})",
            Vector4 v => $"({Bits(v.X)},{Bits(v.Y)},{Bits(v.Z)},{Bits(v.W)})",
            Quaternion q => $"({Bits(q.X)},{Bits(q.Y)},{Bits(q.Z)},{Bits(q.W)})",
            Color c => $"rgba({Bits(c.R)},{Bits(c.G)},{Bits(c.B)},{Bits(c.A)})",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            IEnumerable items => "[" + string.Join(", ", items.Cast<object?>().Select(Value)) + "]",
            _ => Quote(Ids().Replace(value.ToString() ?? "", "#")),
        };
    }

    private static string Bits(float value) => BitConverter.SingleToInt32Bits(value).ToString("X8", CultureInfo.InvariantCulture);

    private static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex Ids();
}
