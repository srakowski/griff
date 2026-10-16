using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Griff.Input;
using Griff.Projects;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editing;

/// <summary>
/// The names a project's fields can name besides entities (see <see cref="NamesAttribute"/>): its prefabs and scenes,
/// its input actions, the collision layers its colliders and tilemaps are on, the messages its components and scripts
/// send and listen for, its tables and their rows, and the fonts it can draw text in (<see cref="FontNames"/>, when
/// they're known). The Inspector lists them in a field's picker and flags a name that matches none.
/// </summary>
public sealed partial class KnownNames : IProjectNames
{
    private readonly HashSet<string> _prefabs, _scenes, _actions, _layers, _sent, _heard, _items, _flags, _saved;
    private readonly Dictionary<string, IReadOnlyList<string>> _places = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _kinds = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Regex> _sentPatterns;
    private readonly Dictionary<string, TableNames> _tables;
    private readonly Lazy<FontNames?> _fonts;

    /// <param name="tables">The project's tables by name, each with its rows' names (and the names its rows had before they were renamed).</param>
    /// <param name="fonts">The fonts it can name, worked out the first time they're asked for; null when they can't be known (font names then match).</param>
    /// <param name="items">The items something gives (a pickup's Item, a script's <c>Add("Key")</c>).</param>
    /// <param name="flags">The flags something sets or clears (a trigger's Set Flag, a dialogue's [set], a script's <c>SetFlag</c>).</param>
    /// <param name="kinds">The kinds things say they are, each with its scope ("Crate", "Red").</param>
    /// <param name="saved">The names things keep in the save data under (a song select's Save As).</param>
    /// <param name="places">Each scene's entities' names, hotspots first ("Garden": "Gate", "Well"...).</param>
    public KnownNames(IEnumerable<string> prefabs, IEnumerable<string> scenes, IEnumerable<string> actions,
        IEnumerable<string> layers, IEnumerable<string> sent, IEnumerable<string> heard, IEnumerable<TableNames>? tables = null,
        Func<FontNames>? fonts = null, IEnumerable<string>? items = null, IEnumerable<string>? flags = null,
        IEnumerable<(string Scope, string Name)>? kinds = null, IEnumerable<string>? saved = null,
        IEnumerable<(string Scene, IReadOnlyList<string> Entities)>? places = null)
    {
        foreach (var (scene, entities) in places ?? [])
            _places[scene] = entities;
        _items = new(items ?? [], StringComparer.OrdinalIgnoreCase);
        _saved = new(saved ?? [], StringComparer.Ordinal);
        _flags = new(flags ?? [], StringComparer.OrdinalIgnoreCase);
        foreach (var (scope, name) in kinds ?? [])
        {
            if (!_kinds.TryGetValue(scope, out var names))
                _kinds[scope] = names = new(StringComparer.OrdinalIgnoreCase);
            names.Add(name);
        }

        _fonts = new(() => fonts?.Invoke());
        _tables = new(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables ?? [])
            _tables.TryAdd(table.Name, table);
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

    /// <summary>The tables' names, e.g. "Enemies".</summary>
    public IReadOnlyCollection<string> Tables => _tables.Keys;

    /// <summary>The items something gives: a pickup's Item, a script's <c>Add("Key")</c>.</summary>
    public IReadOnlyCollection<string> Items => _items;

    /// <summary>The flags something sets or clears: a trigger's Set Flag, a dialogue's [set], a script's <c>SetFlag</c>.</summary>
    public IReadOnlyCollection<string> Flags => _flags;

    /// <summary>
    /// The names values are kept under in the save data: a Save As, a counter's Save Best As, a script's
    /// <c>SaveData.Set</c>. Flags, saved there too, aren't among them (see <see cref="Flags"/>).
    /// </summary>
    public IReadOnlyCollection<string> Saved => _saved;

    /// <summary>
    /// The names of the entities in the scene called <paramref name="scene"/>, hotspots and the like first; none when there's
    /// no such scene.
    /// </summary>
    public IReadOnlyList<string> EntitiesIn(string? scene) =>
        scene is not null && _places.TryGetValue(AssetName(scene.Trim(), Project.SceneExtension), out var names) ? names : [];

    /// <summary>The kinds things of <paramref name="scope"/> say they are ("Red" for a crate), or none.</summary>
    public IReadOnlyCollection<string> KindsOf(string? scope) => _kinds.TryGetValue(scope ?? "", out var names) ? names : [];

    /// <summary>The columns of the table called <paramref name="table"/>, or null if there's no such table.</summary>
    public IReadOnlyList<string>? ColumnsOf(string? table) =>
        table is not null && _tables.TryGetValue(AssetName(table, Data.TableFile.Extension), out var names) ? names.Columns : null;

    /// <summary>
    /// What the column called <paramref name="column"/> of the table called <paramref name="table"/> can say, when it's a column
    /// of choices (see <see cref="TableNames.Choices"/>); none otherwise.
    /// </summary>
    public IReadOnlyList<string> ChoicesOf(string? table, string column) =>
        table is not null && _tables.TryGetValue(AssetName(table, Data.TableFile.Extension), out var names) &&
        names.Choices.TryGetValue(column, out var choices) ? choices : [];

    /// <summary>True if there's a table called <paramref name="table"/>.</summary>
    public bool HasTable(string? table) => table is not null && _tables.ContainsKey(AssetName(table.Trim(), Data.TableFile.Extension));

    /// <summary>The rows of the table called <paramref name="table"/> (or none, if there's no such table).</summary>
    public IReadOnlyList<string> RowsOf(string? table) =>
        table is not null && _tables.TryGetValue(AssetName(table, Data.TableFile.Extension), out var names) ? names.Rows : [];

    /// <summary>The fonts text can be set in, or null when they can't be known.</summary>
    public FontNames? Fonts => _fonts.Value;

    /// <summary>
    /// What a field naming <paramref name="kind"/> can pick from, alphabetically, each with a word on it for messages
    /// ("sent", "heard", or both); fonts in their groups (see <see cref="FontNames"/>).
    /// </summary>
    public IReadOnlyList<NameChoice> Choices(NamesAttribute kind) => Choices(kind, null);

    /// <summary>
    /// As <see cref="Choices(NamesAttribute)"/>, for a field of <paramref name="component"/>: a row field lists the rows
    /// of the table the component's other field names, in the table's order.
    /// </summary>
    public IReadOnlyList<NameChoice> Choices(NamesAttribute kind, Component? component) => kind.Kind switch
    {
        NameKind.Font => Fonts?.Choices ?? [],
        NameKind.Prefab => Sorted(_prefabs),
        NameKind.Scene => Sorted(_scenes),
        NameKind.Action => Sorted(_actions),
        NameKind.Layer => Sorted(_layers),
        NameKind.Table => Sorted(_tables.Keys),
        NameKind.Row => [.. RowsOf(TableOf(component, kind)).Select(r => new NameChoice(r, null))],
        NameKind.Column => [.. (ColumnsOf(TableOf(component, kind)) ?? []).Select(c => new NameChoice(c, null))],
        NameKind.Item => Sorted(_items),
        NameKind.Kind => Sorted(KindsOf(kind.Scope)),
        NameKind.Flag when kind is { Condition: true, Defines: true } => [.. _flags.Order(StringComparer.OrdinalIgnoreCase)
            .SelectMany(f => new[] { new NameChoice(f, "sets it"), new NameChoice($"not {f}", "clears it") })],
        NameKind.Flag when kind.Condition => [.. _flags.Order(StringComparer.OrdinalIgnoreCase)
            .SelectMany(f => new[] { new NameChoice(f, "while it's set"), new NameChoice($"not {f}", "while it isn't set") })],
        NameKind.Flag => Sorted(_flags),
        NameKind.Saved => [.. _saved.Order(StringComparer.OrdinalIgnoreCase).Select(n => new NameChoice(n, null)),
            .. _flags.Where(f => !_saved.Contains(f)).Order(StringComparer.OrdinalIgnoreCase).Select(f => new NameChoice(f, "a flag"))],
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
        if (kind.Kind == NameKind.Row)
        {
            // A row of a table that isn't there isn't flagged here: the table's field is.
            if (TableOf(component, kind) is not { } table || !_tables.TryGetValue(AssetName(table, Data.TableFile.Extension), out var names))
                return [];
            var rows = Split(value, kind);
            // Table Values with no Row takes the row named like its entity.
            if (rows.Count == 0 && component is TableValues { RowName: { Length: > 0 } own })
                rows = [own];
            return [.. rows.Where(r => !names.Has(r)).Distinct()];
        }

        if (kind.Kind == NameKind.Column)
        {
            // A column of a table that isn't there isn't flagged here: the table's field is. Nor is the component's own
            // default (a deck's "Count"), which a table can do without.
            if (TableOf(component, kind) is not { } table || ColumnsOf(table) is not { } columns || Equals(value, DefaultOf(component.GetType(), property)))
                return [];
            return [.. Split(value, kind).Where(c => !columns.Contains(c, StringComparer.OrdinalIgnoreCase)).Distinct()];
        }

        if (kind is { Kind: NameKind.Message, Hears: false } && Equals(value, DefaultOf(component.GetType(), property)))
            return [];
        return Unmatched(kind, value);
    }

    // A component of each type as it's made, to read its fields' defaults from. Held only while its type is: a script's
    // types go when their scripts are compiled again, and the samples with them, so a reloaded script's assembly unloads.
    private static readonly ConditionalWeakTable<Type, Sample> Defaults = [];

    private sealed class Sample(Component? component)
    {
        public Component? Component { get; } = component;
    }

    private static object? DefaultOf(Type type, PropertyDescriptor property)
    {
        var sample = Defaults.GetValue(type, static type =>
        {
            try
            {
                return new Sample((Component?)Activator.CreateInstance(type));
            }
            catch (Exception)
            {
                return new Sample(null);
            }
        }).Component;
        return sample is null ? null : property.GetValue(sample);
    }

    private bool Matches(NamesAttribute kind, string name) => kind.Kind switch
    {
        NameKind.Prefab => _prefabs.Contains(AssetName(name, Project.PrefabExtension)),
        NameKind.Scene => _scenes.Contains(AssetName(name, Project.SceneExtension)),
        NameKind.Action => _actions.Contains(name),
        NameKind.Layer => _layers.Contains(name),
        NameKind.Table => _tables.ContainsKey(AssetName(name, Data.TableFile.Extension)),
        // A row is matched against its table, which takes the component (see Unmatched(Component, ...)).
        NameKind.Row or NameKind.Column => true,
        NameKind.Font => Fonts is not { } fonts || fonts.Draws(name),
        NameKind.Item => _items.Contains(name),
        NameKind.Flag => _flags.Contains(FlagOf(name)),
        NameKind.Kind => KindsOf(kind.Scope).Contains(name),
        // Save data keys are case-sensitive, as SaveData keeps them; flags are kept there too.
        NameKind.Saved => _saved.Contains(name) || _flags.Contains(name) || kind is { Defines: false, Suffixes: { Length: > 0 } read } && read.Any(r => _saved.Contains(name + r)),
        _ when IsPattern(name) => true,
        _ when kind.Hears => _sent.Contains(name) || _sentPatterns.Any(p => p.IsMatch(name)),
        _ => _heard.Contains(name),
    };

    /// <summary>The flag a condition names: "MetKing" for "not MetKing" or "!MetKing", as <c>Game.FlagHolds</c> reads it.</summary>
    public static string FlagOf(string condition)
    {
        var name = condition.Trim();
        if (name.StartsWith('!'))
            return name[1..].Trim();
        return name.Length > 4 && name.StartsWith("not ", StringComparison.OrdinalIgnoreCase) ? name[4..].Trim() : name;
    }

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
            NameKind.Table => $"There's no table called {quoted} in the project: make one with Data > New Table",
            NameKind.Row => $"The table has no row called {quoted}: add it in the Tables panel",
            NameKind.Column => $"The table has no column called {quoted}: add it in the Tables panel",
            NameKind.Font => $"There's no font file in Assets or font installed on this PC called {quoted}, so the built-in font draws " +
                             "instead (Griff's free fonts go into Assets when picked, or with Art > Add Free Font)",
            NameKind.Item => $"Nothing gives the item {quoted}: no pickup's Item or script's Add",
            NameKind.Flag => $"Nothing sets the flag {string.Join(" or ", names.Select(n => $"\"{FlagOf(n)}\""))}: no trigger's Set Flag, dialogue's [set] or script's SetFlag",
            NameKind.Kind when IsTeam(kind) => $"There's no team called {quoted}: name it in a turn order's Teams, a strategy faction, a Multiplayer's Teams or a Team",
            NameKind.Kind when kind.From is { } from => $"There's no {ScopeWord(kind)} called {quoted} in {from}",
            NameKind.Kind => $"No {ScopeWord(kind)} is of the kind {quoted}: give one that Kind",
            NameKind.Saved => $"Nothing keeps a value under {quoted} in the save data: no Save As field or script's SaveData.Set does",
            _ when kind.Hears => $"Nothing sends {quoted}: no component's message, dialogue's [send] or script's Game.Send",
            _ => $"Nothing listens for {quoted}: no Counter, Trigger, Sound Player or script's On",
        };
    }

    /// <summary>"a prefab", "a scene", "an input action", "a collision layer", "a font" or "a message": what a field names.</summary>
    public static string Noun(NamesAttribute kind) => kind.Kind switch
    {
        NameKind.Font => "a font",
        NameKind.Prefab => "a prefab",
        NameKind.Scene => "a scene",
        NameKind.Action => "an input action",
        NameKind.Layer => "a collision layer",
        NameKind.Table => "a table",
        NameKind.Row => "a row of a table",
        NameKind.Column => "a column of a table",
        NameKind.Item => "an item",
        NameKind.Flag => "a flag",
        NameKind.Kind when kind.From is not null || IsTeam(kind) => $"a {ScopeWord(kind)}",
        NameKind.Kind => $"a {ScopeWord(kind)} kind",
        NameKind.Saved => "a name in the save data",
        _ => "a message",
    };

    // Every genre's teams are one list of names, whoever gives them (see Teams).
    private static bool IsTeam(NamesAttribute kind) => string.Equals(kind.Scope?.Trim(), Teams.Scope, StringComparison.OrdinalIgnoreCase);

    // "crate" for a Crate kind, "thing" without a scope.
    private static string ScopeWord(NamesAttribute kind) => string.IsNullOrWhiteSpace(kind.Scope) ? "thing" : kind.Scope.Trim().ToLowerInvariant();

    // The table a row field's component names in its other field (see NamesAttribute.Table).
    private static string? TableOf(Component? component, NamesAttribute kind)
    {
        if (component is null || kind.Table is not { } property)
            return null;
        return ComponentModel.Of(component).Property(property)?.GetValue(component) is string table && table.Trim().Length > 0 ? table.Trim() : null;
    }

    private static bool IsPattern(string message) => message.Contains('{');

    // "{0}Turn" matches "PlayerTurn".
    private static Regex Pattern(string message) =>
        new("^" + string.Concat(PatternHole().Split(message).Select((part, i) => i % 2 == 0 ? Regex.Escape(part) : ".+")) + "$");

    [GeneratedRegex(@"(\{[^}]*\})")]
    private static partial Regex PatternHole();

    // ----- Gathering

    /// <summary>
    /// The layers, messages sent and messages heard that some entities' components name, and the items they give, the
    /// flags they set and the kinds they say they are.
    /// </summary>
    public sealed record Found(IReadOnlyCollection<string> Layers, IReadOnlyCollection<string> Sent, IReadOnlyCollection<string> Heard)
    {
        /// <summary>Items given: a pickup's Item, a script's <c>Add("Key")</c>.</summary>
        public IReadOnlyCollection<string> Items { get; init; } = [];

        /// <summary>Flags set or cleared: a trigger's Set Flag, a dialogue's [set] and [clear], a script's <c>SetFlag</c>.</summary>
        public IReadOnlyCollection<string> Flags { get; init; } = [];

        /// <summary>Kinds things say they are, with their scope: ("Crate", "Red").</summary>
        public IReadOnlyCollection<(string Scope, string Name)> Kinds { get; init; } = [];

        /// <summary>Names values are kept under in the save data: a Save As, a script's <c>SaveData.Set("best", ...)</c>.</summary>
        public IReadOnlyCollection<string> Saved { get; init; } = [];

        /// <summary>For a scene: its name and its entities' names, hotspots first; null for anything else.</summary>
        public (string Scene, IReadOnlyList<string> Entities)? Place { get; init; }

        public static Found Empty { get; } = new([], [], []);
    }

    /// <summary>
    /// The names of <paramref name="entities"/>, as a field naming something in their scene picks from: the places things
    /// arrive at (see <see cref="IArrivalSpot"/>: an adventure's hotspots) first, then the rest, each once.
    /// </summary>
    public static IReadOnlyList<string> PlacesIn(IEnumerable<Entity> entities)
    {
        var all = entities.SelectMany(e => e.SelfAndDescendants()).Where(e => !string.IsNullOrWhiteSpace(e.Name)).ToList();
        return [.. all.Where(e => e.Components.Any(c => c is IArrivalSpot)).Concat(all).Select(e => e.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// What <paramref name="entities"/> say about layers and messages: the layers their colliders and tilemaps are on,
    /// the messages their fields send (and their dialogue's [send] tags) and those their counters and sound players hear.
    /// </summary>
    public static Found Collect(IEnumerable<Entity> entities, ComponentRegistry registry)
    {
        var (layers, sent, heard) = (new HashSet<string>(StringComparer.OrdinalIgnoreCase), new HashSet<string>(), new HashSet<string>());
        var (items, flags, kinds) = (new HashSet<string>(StringComparer.OrdinalIgnoreCase), new HashSet<string>(StringComparer.OrdinalIgnoreCase), new HashSet<(string, string)>());
        var saved = new HashSet<string>(StringComparer.Ordinal);

        void Gather(NamesAttribute kind, IEnumerable<string> names)
        {
            if (kind.Kind == NameKind.Layer && kind.Defines)
                layers.UnionWith(names);
            else if (kind.Kind == NameKind.Message)
                (kind.Hears ? heard : sent).UnionWith(names);
            else if (kind.Kind == NameKind.Item && kind.Defines)
                items.UnionWith(names);
            else if (kind.Kind == NameKind.Flag && kind.Defines)
                flags.UnionWith(names.Select(FlagOf));
            else if (kind.Kind == NameKind.Kind && kind.Defines)
                kinds.UnionWith(names.Select(n => (kind.Scope ?? "", n)));
            else if (kind.Kind == NameKind.Saved && kind.Defines)
                saved.UnionWith(kind.Suffixes is { Length: > 0 } suffixes ? names.SelectMany(n => suffixes.Select(s => n + s)) : names);
        }

        foreach (var component in entities.SelectMany(e => e.Components).Where(c => registry.IsRegistered(c.GetType())))
        {
            foreach (var property in registry.Get(component.GetType()).Properties)
            {
                // An inventory's own items are items it has to give.
                if (property.Editor is ItemCountsAttribute && property.GetValue(component) is IReadOnlyList<string> held)
                    items.UnionWith(held.Select(l => ItemCountsAttribute.Read(l).Item).Where(i => i.Length > 0));
                // What items do when they're used sends messages too ("send Teleport").
                if (property.Editor is ItemUsesAttribute && property.GetValue(component) is IReadOnlyList<string> uses)
                    sent.UnionWith(uses.Select(ItemUsesAttribute.Read).SelectMany(u => u?.Effects ?? []).Where(e => e.Verb == "send" && e.Argument.Length > 0).Select(e => e.Argument));
                if (property.Names is { } kind)
                    Gather(kind, Split(property.GetValue(component), kind));
                // Rows of names (an item and how many, a recipe) give theirs too.
                if (property.Editor is RowsAttribute rows)
                {
                    foreach (var (named, name) in rows.Named(property.GetValue(component)))
                    {
                        if (!(named.Kind == NameKind.Item && name.Equals("nothing", StringComparison.OrdinalIgnoreCase)))
                            Gather(named, [name]);
                    }
                }
            }

            if (component is Dialogue dialogue)
            {
                for (var row = 0; row < dialogue.Lines.Count; row++)
                {
                    foreach (var tag in dialogue.Row(row).Tags)
                    {
                        if (tag.Kind == Dialogue.TagKind.Send)
                            sent.Add(tag.Name);
                        else if (tag.Kind is Dialogue.TagKind.Set or Dialogue.TagKind.Clear)
                            flags.Add(tag.Name);
                    }
                }
            }

            // A trigger whose Do is Set Flag or Clear Flag names its flag in With.
            if (component is Trigger { With: { Length: > 0 } with } trigger && IsFlagAction(trigger.Do))
                flags.Add(with.Trim().Trim('"'));
        }

        return new(layers, sent, heard) { Items = items, Flags = flags, Kinds = kinds, Saved = saved };
    }

    // "SetFlag", "Set Flag", "Trigger.SetFlag" and the same for Clear.
    private static bool IsFlagAction(string? action)
    {
        var name = (action ?? "").Replace(" ", "", StringComparison.Ordinal);
        name = name[(name.LastIndexOf('.') + 1)..];
        return name.Equals("SetFlag", StringComparison.OrdinalIgnoreCase) || name.Equals("ClearFlag", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// What a script's code sends (<c>Send("Score")</c>), listens for (<c>On("Score", ...)</c>), puts things on
    /// (<c>Layer = "Bullet"</c>), gives (<c>Add("Key")</c>), sets (<c>SetFlag("MetKing")</c>) and saves (<c>SaveData.Set("best", ...)</c>).
    /// </summary>
    public static Found Scan(string code) => new(
        [.. ScriptLayer().Matches(code).Select(m => m.Groups[1].Value)],
        [.. ScriptSend().Matches(code).Select(m => m.Groups[1].Value)],
        [.. ScriptOn().Matches(code).Select(m => m.Groups[1].Value)])
    {
        Items = [.. ScriptItem().Matches(code).Select(m => m.Groups[1].Value)],
        Flags = [.. ScriptFlag().Matches(code).Select(m => m.Groups[1].Value)],
        Saved = [.. ScriptSaved().Matches(code).Select(m => m.Groups[1].Value)],
    };

    [GeneratedRegex("""\bSaveData\s*\.\s*Set\s*\(\s*"([^"\\]+)"\s*,""")]
    private static partial Regex ScriptSaved();

    [GeneratedRegex("""\bAdd\s*\(\s*"([^"\\]+)"\s*(?:,\s*\d+\s*)?\)""")]
    private static partial Regex ScriptItem();

    [GeneratedRegex("""\bSetFlag\s*\(\s*"([^"\\]+)"\s*[,)]""")]
    private static partial Regex ScriptFlag();

    [GeneratedRegex("""\bSend\s*\(\s*"([^"\\]+)"\s*[,)]""")]
    private static partial Regex ScriptSend();

    [GeneratedRegex("""\bOn\s*(?:<[^>()]*>)?\s*\(\s*"([^"\\]+)"\s*,""")]
    private static partial Regex ScriptOn();

    [GeneratedRegex("""\bLayer\s*=\s*"([^"\\]+)"\s*[;,}]""")]
    private static partial Regex ScriptLayer();

    /// <summary>
    /// Everything a project's names come from put together: its files, and what was found in them; its tables; and its
    /// fonts, from <paramref name="fonts"/> (null when they can't be known).
    /// </summary>
    public static KnownNames From(Project project, IEnumerable<Found> found, IEnumerable<TableNames>? tables = null, Func<Project, FontNames>? fonts = null)
    {
        var all = found.ToList();
        var actions = InputActions.BuiltIn.Keys.Concat(project.Manifest.Controls?.Keys ?? Enumerable.Empty<string>());
        return new(
            project.FindPrefabs().Select(Path.GetFileNameWithoutExtension).OfType<string>(),
            project.FindScenes().Select(Path.GetFileNameWithoutExtension).OfType<string>(),
            actions,
            all.SelectMany(f => f.Layers),
            all.SelectMany(f => f.Sent),
            all.SelectMany(f => f.Heard),
            tables,
            fonts is null ? null : () => fonts(project),
            all.SelectMany(f => f.Items),
            all.SelectMany(f => f.Flags),
            all.SelectMany(f => f.Kinds),
            all.SelectMany(f => f.Saved),
            all.Where(f => f.Place is not null).Select(f => f.Place!.Value));
    }
}

/// <summary>
/// A table's name, its rows' names (with the names its rows had before they were renamed, which still find them) and its
/// columns' names.
/// </summary>
public sealed class TableNames(string name, IReadOnlyList<string> rows, IEnumerable<string>? formerRows = null, IReadOnlyList<string>? columns = null)
{
    private readonly HashSet<string> _all = new(rows.Concat(formerRows ?? []), StringComparer.OrdinalIgnoreCase);

    public string Name { get; } = name;

    /// <summary>Its rows' names, top to bottom.</summary>
    public IReadOnlyList<string> Rows { get; } = rows;

    /// <summary>Its columns' names, left to right.</summary>
    public IReadOnlyList<string> Columns { get; } = columns ?? [];

    /// <summary>True if a row is (or was) called <paramref name="row"/>.</summary>
    public bool Has(string row) => _all.Contains(row.Trim());

    /// <summary>
    /// What a column of choices can say, by the column's name: its choices, then any other value its rows hold (a chart's
    /// Difficulty: "Easy", "Normal", "Hard").
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Choices { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The names in <paramref name="table"/>.</summary>
    public static TableNames Of(Data.Table table)
    {
        var choices = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < table.Columns.Count; i++)
        {
            if (table.Columns[i] is not { Choices: { Count: > 0 } offered } column)
                continue;
            var at = i;
            choices[column.Name] = [.. offered.Concat(table.Rows.Select(r => r[at]).Where(v => !v.IsEmpty)
                .SelectMany(v => v.AsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
                .Distinct(StringComparer.OrdinalIgnoreCase)];
        }

        return new(table.Name, [.. table.Rows.Select(r => r.Name)], table.RenamedRows.Keys, [.. table.Columns.Select(c => c.Name)]) { Choices = choices };
    }
}

/// <summary>
/// A name a field can pick, with a word on it ("sent" for a message only sent so far), or none; for a font, the group it's
/// listed in and how it reads when its name is a path ("Press Start 2P" for "Fonts/PressStart2P-Regular.ttf").
/// </summary>
public sealed record NameChoice(string Name, string? Detail, NameGroup? Group = null, string? Label = null)
{
    /// <summary>How it reads: its <see cref="Label"/>, or its name.</summary>
    public string Text => Label ?? Name;
}

/// <summary>A group of choices in a picker, with what to know about them (e.g. that installed fonts aren't shipped).</summary>
public sealed record NameGroup(string Title, string? Warning = null);

/// <summary>
/// The fonts a project's text can be set in, as the font picker groups them: those safe to ship first (the built-in
/// font, Griff's free fonts and the project's font files), then, apart and with a warning, fonts only installed on this
/// PC. Built in Griff.Fonts (<c>FontChoices</c>), which knows them.
/// </summary>
/// <param name="Choices">Every font, in the picker's order, each with its group.</param>
/// <param name="Usable">The names that draw in a font of their own now (the built-in font's, the project's font files, installed fonts).</param>
public sealed record FontNames(IReadOnlyList<NameChoice> Choices, IReadOnlyCollection<string> Usable)
{
    /// <summary>True when text set in <paramref name="font"/> draws in it (or in the built-in font, for none).</summary>
    public bool Draws(string font) => string.IsNullOrWhiteSpace(font) || Usable.Contains(font.Trim().Replace('\\', '/'));
}

/// <summary>
/// Keeps a project's <see cref="KnownNames"/> up to date cheaply: each scene, prefab, script and table is read again only
/// once it's been saved since it was last read, and the open scene's live entities stand in for its file.
/// </summary>
/// <param name="fonts">Works out a project's fonts (Griff.Fonts' <c>FontChoices.For</c>); null leaves fonts unknown.</param>
public sealed class KnownNameIndex(ComponentRegistry registry, Func<Project, FontNames>? fonts = null)
{
    private readonly Dictionary<string, (DateTime Written, KnownNames.Found Found)> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DateTime Written, TableNames Names)> _tables = new(StringComparer.OrdinalIgnoreCase);

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
        {
            var name = Path.GetFileNameWithoutExtension(scene);
            Read(scene, text => serializer.LoadFromString(text).Entities is var entities
                ? KnownNames.Collect(entities, registry) with { Place = (name, KnownNames.PlacesIn(entities)) }
                : KnownNames.Found.Empty);
        }

        foreach (var prefab in project.FindPrefabs())
            Read(prefab, text => KnownNames.Collect(serializer.LoadPrefabFromString(text).SelfAndDescendants(), registry));
        if (Directory.Exists(project.AssetsPath))
        {
            foreach (var script in Directory.EnumerateFiles(project.AssetsPath, "*.cs", SearchOption.AllDirectories))
                Read(script, KnownNames.Scan);
        }

        var tables = new List<TableNames>();
        if (Directory.Exists(project.AssetsPath))
        {
            foreach (var file in Directory.EnumerateFiles(project.AssetsPath, "*" + Data.TableFile.Extension, SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
            {
                seen.Add(file);
                DateTime written;
                try
                {
                    written = File.GetLastWriteTimeUtc(file);
                }
                catch (IOException)
                {
                    continue;
                }

                if (!_tables.TryGetValue(file, out var known) || known.Written != written)
                {
                    TableNames names;
                    try
                    {
                        names = TableNames.Of(Data.TableFile.Load(file));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                    {
                        // A table that doesn't read is there, with no rows to pick.
                        names = new TableNames(Data.TableFile.NameOf(file), []);
                    }

                    _tables[file] = known = (written, names);
                }

                tables.Add(known.Names);
            }
        }

        foreach (var gone in _files.Keys.Where(k => !seen.Contains(k)).ToList())
            _files.Remove(gone);
        foreach (var gone in _tables.Keys.Where(k => !seen.Contains(k)).ToList())
            _tables.Remove(gone);
        if (open is not null)
        {
            var entities = open.ToList();
            var isScene = openPath is not null && openPath.EndsWith(Project.SceneExtension, StringComparison.OrdinalIgnoreCase);
            found.Add(KnownNames.Collect(entities, registry) with
            {
                Place = isScene ? (Path.GetFileNameWithoutExtension(openPath!), KnownNames.PlacesIn(entities.Where(e => e.Parent is null))) : null,
            });
        }
        return KnownNames.From(project, found, tables, fonts);
    }
}
