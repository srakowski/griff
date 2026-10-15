using System.Text.RegularExpressions;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editing;

/// <summary>
/// The names a project's fields can name besides entities (see <see cref="NamesAttribute"/>): its prefabs and scenes,
/// its input actions, the collision layers its colliders and tilemaps are on, and the messages its components and
/// scripts send and listen for. The Inspector lists them in a field's picker and flags a name that matches none.
/// </summary>
public sealed partial class KnownNames
{
    private readonly HashSet<string> _prefabs, _scenes, _actions, _layers, _sent, _heard;
    private readonly List<Regex> _sentPatterns;

    public KnownNames(IEnumerable<string> prefabs, IEnumerable<string> scenes, IEnumerable<string> actions,
        IEnumerable<string> layers, IEnumerable<string> sent, IEnumerable<string> heard)
    {
        _prefabs = new(prefabs, StringComparer.OrdinalIgnoreCase);
        _scenes = new(scenes, StringComparer.OrdinalIgnoreCase);
        _actions = new(actions, StringComparer.OrdinalIgnoreCase);
        _layers = new(layers.Select(CollisionLayers.NameOf), StringComparer.OrdinalIgnoreCase) { CollisionLayers.Default };
        // A message with {0} in it is sent under many names ("{0}Turn": "PlayerTurn", "EnemyTurn"): listening for any
        // of them counts, but it isn't one to pick.
        _sent = new(sent.Where(m => !IsPattern(m)), StringComparer.Ordinal);
        _sentPatterns = [.. sent.Where(IsPattern).Distinct().Select(Pattern)];
        _heard = new(heard.Where(m => !IsPattern(m)), StringComparer.Ordinal);
    }

    /// <summary>The prefabs' names, e.g. "Bullet".</summary>
    public IReadOnlyCollection<string> Prefabs => _prefabs;

    /// <summary>The scenes' names, e.g. "Level2".</summary>
    public IReadOnlyCollection<string> Scenes => _scenes;

    /// <summary>The input actions: every game's own (Jump, Fire...) and the project's Controls.</summary>
    public IReadOnlyCollection<string> Actions => _actions;

    /// <summary>The collision layers something is on, "Default" always among them.</summary>
    public IReadOnlyCollection<string> Layers => _layers;

    /// <summary>The messages sent: by components' message fields, dialogue's [send] tags and scripts' <c>Send</c>.</summary>
    public IReadOnlyCollection<string> Sent => _sent;

    /// <summary>The messages listened for: by counters, triggers, sound players and scripts' <c>On</c>.</summary>
    public IReadOnlyCollection<string> Heard => _heard;

    /// <summary>
    /// What a field naming <paramref name="kind"/> can pick from, alphabetically, each with a word on it for messages
    /// ("sent", "heard", or both).
    /// </summary>
    public IReadOnlyList<NameChoice> Choices(NamesAttribute kind) => kind.Kind switch
    {
        NameKind.Prefab => Sorted(_prefabs),
        NameKind.Scene => Sorted(_scenes),
        NameKind.Action => Sorted(_actions),
        NameKind.Layer => Sorted(_layers),
        _ => [.. _sent.Union(_heard).Order(StringComparer.OrdinalIgnoreCase).Select(m => new NameChoice(m,
            (_sent.Contains(m), _heard.Contains(m)) switch { (true, true) => "sent and heard", (true, false) => "sent", _ => "heard" }))],
    };

    private static List<NameChoice> Sorted(IEnumerable<string> names) => [.. names.Order(StringComparer.OrdinalIgnoreCase).Select(n => new NameChoice(n, null))];

    /// <summary>The names a field's value holds: split at commas for one that names several, empty when it's unset.</summary>
    public static IReadOnlyList<string> Split(object? value, NamesAttribute kind)
    {
        var text = (value as string ?? "").Trim();
        if (text.Length == 0)
            return [];
        return kind.Many ? CollisionLayers.Parse(text) : [text];
    }

    /// <summary>
    /// The names <paramref name="value"/> holds that match nothing the project has (see <see cref="Problem"/>). A field
    /// that <see cref="NamesAttribute.Defines"/> its name never matches nothing.
    /// </summary>
    public IReadOnlyList<string> Unmatched(NamesAttribute kind, object? value)
    {
        if (kind.Defines)
            return [];
        return [.. Split(value, kind).Where(n => !Matches(kind, n)).Distinct()];
    }

    /// <summary>
    /// The names <paramref name="component"/>'s <paramref name="property"/> holds that match nothing (see
    /// <see cref="Unmatched(NamesAttribute, object?)"/>), or none for a field that doesn't name these. A message sent that
    /// nothing hears isn't flagged while it's the component's own default (a pickup's "Score"): it's there for whoever
    /// wants it.
    /// </summary>
    public IReadOnlyList<string> Unmatched(Component component, PropertyDescriptor property)
    {
        if (property.Names is not { } kind)
            return [];
        var value = property.GetValue(component);
        if (kind is { Kind: NameKind.Message, Hears: false } && Equals(value, DefaultOf(component.GetType(), property)))
            return [];
        return Unmatched(kind, value);
    }

    private static readonly Dictionary<Type, Component?> Defaults = [];

    private static object? DefaultOf(Type type, PropertyDescriptor property)
    {
        Component? sample;
        lock (Defaults)
        {
            if (!Defaults.TryGetValue(type, out sample))
            {
                try
                {
                    sample = (Component?)Activator.CreateInstance(type);
                }
                catch (Exception)
                {
                    sample = null;
                }

                Defaults[type] = sample;
            }
        }

        return sample is null ? null : property.GetValue(sample);
    }

    private bool Matches(NamesAttribute kind, string name) => kind.Kind switch
    {
        NameKind.Prefab => _prefabs.Contains(AssetName(name, Project.PrefabExtension)),
        NameKind.Scene => _scenes.Contains(AssetName(name, Project.SceneExtension)),
        NameKind.Action => _actions.Contains(name),
        NameKind.Layer => _layers.Contains(name),
        _ when IsPattern(name) => true,
        _ when kind.Hears => _sent.Contains(name) || _sentPatterns.Any(p => p.IsMatch(name)),
        _ => _heard.Contains(name),
    };

    // "Prefabs/Bullet.prefab" and "Bullet" both name Bullet, as Project.FindPrefab finds them.
    private static string AssetName(string name, string extension)
    {
        var file = name.Replace('\\', '/').Split('/')[^1];
        return file.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? file[..^extension.Length] : file;
    }

    /// <summary>
    /// Why the names are flagged, e.g. "There's no prefab called "Bulet" in the project", or "Nothing listens for
    /// "Score"".
    /// </summary>
    public static string Problem(NamesAttribute kind, IReadOnlyList<string> names)
    {
        var quoted = string.Join(" or ", names.Select(n => $"\"{n}\""));
        return kind.Kind switch
        {
            NameKind.Prefab => $"There's no prefab called {quoted} in the project",
            NameKind.Scene => $"There's no scene called {quoted} in the project",
            NameKind.Action => $"There's no input action called {quoted}: add one in Game > Controls",
            NameKind.Layer => $"Nothing is on the layer {quoted}: no collider or tilemap has it as its Layer",
            _ when kind.Hears => $"Nothing sends {quoted}: no component's message, dialogue's [send] or script's Game.Send",
            _ => $"Nothing listens for {quoted}: no Counter, Trigger, Sound Player or script's On",
        };
    }

    /// <summary>"a prefab", "a scene", "an input action", "a collision layer" or "a message": what a field names.</summary>
    public static string Noun(NamesAttribute kind) => kind.Kind switch
    {
        NameKind.Prefab => "a prefab",
        NameKind.Scene => "a scene",
        NameKind.Action => "an input action",
        NameKind.Layer => "a collision layer",
        _ => "a message",
    };

    private static bool IsPattern(string message) => message.Contains('{');

    // "{0}Turn" matches "PlayerTurn".
    private static Regex Pattern(string message) =>
        new("^" + string.Concat(PatternHole().Split(message).Select((part, i) => i % 2 == 0 ? Regex.Escape(part) : ".+")) + "$");

    [GeneratedRegex(@"(\{[^}]*\})")]
    private static partial Regex PatternHole();

    // ----- Gathering

    /// <summary>The layers, messages sent and messages heard that some entities' components name.</summary>
    public sealed record Found(IReadOnlyCollection<string> Layers, IReadOnlyCollection<string> Sent, IReadOnlyCollection<string> Heard)
    {
        public static Found Empty { get; } = new([], [], []);
    }

    /// <summary>
    /// What <paramref name="entities"/> say about layers and messages: the layers their colliders and tilemaps are on,
    /// the messages their fields send (and their dialogue's [send] tags) and those their counters and sound players hear.
    /// </summary>
    public static Found Collect(IEnumerable<Entity> entities, ComponentRegistry registry)
    {
        var (layers, sent, heard) = (new HashSet<string>(StringComparer.OrdinalIgnoreCase), new HashSet<string>(), new HashSet<string>());
        foreach (var component in entities.SelectMany(e => e.Components).Where(c => registry.IsRegistered(c.GetType())))
        {
            foreach (var property in registry.Get(component.GetType()).Properties)
            {
                if (property.Names is not { } kind)
                    continue;
                var names = Split(property.GetValue(component), kind);
                if (kind.Kind == NameKind.Layer && kind.Defines)
                    layers.UnionWith(names);
                else if (kind.Kind == NameKind.Message)
                    (kind.Hears ? heard : sent).UnionWith(names);
            }

            if (component is Dialogue dialogue)
            {
                for (var row = 0; row < dialogue.Lines.Count; row++)
                    sent.UnionWith(dialogue.Row(row).Tags.Where(t => t.Kind == Dialogue.TagKind.Send).Select(t => t.Name));
            }
        }

        return new(layers, sent, heard);
    }

    /// <summary>What a script's code sends (<c>Send("Score")</c>), listens for (<c>On("Score", ...)</c>) and puts things on (<c>Layer = "Bullet"</c>).</summary>
    public static Found Scan(string code) => new(
        [.. ScriptLayer().Matches(code).Select(m => m.Groups[1].Value)],
        [.. ScriptSend().Matches(code).Select(m => m.Groups[1].Value)],
        [.. ScriptOn().Matches(code).Select(m => m.Groups[1].Value)]);

    [GeneratedRegex("""\bSend\s*\(\s*"([^"\\]+)"\s*[,)]""")]
    private static partial Regex ScriptSend();

    [GeneratedRegex("""\bOn\s*(?:<[^>()]*>)?\s*\(\s*"([^"\\]+)"\s*,""")]
    private static partial Regex ScriptOn();

    [GeneratedRegex("""\bLayer\s*=\s*"([^"\\]+)"\s*[;,}]""")]
    private static partial Regex ScriptLayer();

    /// <summary>Everything a project's names come from put together: its files, and what was found in them.</summary>
    public static KnownNames From(Project project, IEnumerable<Found> found)
    {
        var all = found.ToList();
        var actions = InputState.BuiltInActions.Keys.Concat(project.Manifest.Controls?.Keys ?? Enumerable.Empty<string>());
        return new(
            project.FindPrefabs().Select(Path.GetFileNameWithoutExtension).OfType<string>(),
            project.FindScenes().Select(Path.GetFileNameWithoutExtension).OfType<string>(),
            actions,
            all.SelectMany(f => f.Layers),
            all.SelectMany(f => f.Sent),
            all.SelectMany(f => f.Heard));
    }
}

/// <summary>A name a field can pick, with a word on it ("sent" for a message only sent so far), or none.</summary>
public sealed record NameChoice(string Name, string? Detail);

/// <summary>
/// Keeps a project's <see cref="KnownNames"/> up to date cheaply: each scene, prefab and script is read again only
/// once it's been saved since it was last read, and the open scene's live entities stand in for its file.
/// </summary>
public sealed class KnownNameIndex(ComponentRegistry registry)
{
    private readonly Dictionary<string, (DateTime Written, KnownNames.Found Found)> _files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The project's names now. <paramref name="open"/> is the scene (or prefab) being edited, saved at
    /// <paramref name="openPath"/>: what it holds counts rather than what its file last saved.
    /// </summary>
    public KnownNames Get(Project project, IEnumerable<Entity>? open = null, string? openPath = null)
    {
        var openFull = openPath is null ? null : Path.GetFullPath(openPath);
        var found = new List<KnownNames.Found>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var serializer = new SceneSerializer(registry);

        void Read(string path, Func<string, KnownNames.Found> read)
        {
            seen.Add(path);
            if (openFull is not null && string.Equals(Path.GetFullPath(path), openFull, StringComparison.OrdinalIgnoreCase))
                return;
            DateTime written;
            try
            {
                written = File.GetLastWriteTimeUtc(path);
            }
            catch (IOException)
            {
                return;
            }

            if (!_files.TryGetValue(path, out var known) || known.Written != written)
            {
                KnownNames.Found result;
                try
                {
                    result = read(File.ReadAllText(path));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or FormatException)
                {
                    result = KnownNames.Found.Empty;
                }

                _files[path] = known = (written, result);
            }

            found.Add(known.Found);
        }

        foreach (var scene in project.FindScenes())
            Read(scene, text => KnownNames.Collect(serializer.LoadFromString(text).Entities, registry));
        foreach (var prefab in project.FindPrefabs())
            Read(prefab, text => KnownNames.Collect(serializer.LoadPrefabFromString(text).SelfAndDescendants(), registry));
        if (Directory.Exists(project.AssetsPath))
        {
            foreach (var script in Directory.EnumerateFiles(project.AssetsPath, "*.cs", SearchOption.AllDirectories))
                Read(script, KnownNames.Scan);
        }

        foreach (var gone in _files.Keys.Where(k => !seen.Contains(k)).ToList())
            _files.Remove(gone);
        if (open is not null)
            found.Add(KnownNames.Collect(open, registry));
        return KnownNames.From(project, found);
    }
}
