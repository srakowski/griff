using System.Globalization;
using System.Numerics;
using System.Reflection;
using Griff.Data;

namespace Griff.Scenes;

/// <summary>
/// Gives a property an editor of its own in the editor's Inspector, in place of the plain box its type gets, for a
/// value written in a little language of its own: dice, points, tile numbers, clips. Mark the property with one of
/// these (<c>[Dice]</c>, <c>[Points]</c>, <c>[TileNumbers]</c>...) and the Inspector finds the editor made for it by its
/// type, for Griff's components and scripts' alike; agents get the same through component_describe, which says what
/// it takes (<see cref="Describe"/>), and are told what's wrong with a value (<see cref="Problem"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public abstract class FieldEditorAttribute : Attribute
{
    /// <summary>
    /// What the field takes and how the Inspector edits it, as component_describe says it after the field's meaning, e.g.
    /// "Dice, like 2d6+3...".
    /// </summary>
    public abstract string Describe();

    /// <summary>
    /// What's wrong with <paramref name="value"/> on <paramref name="component"/>, e.g. "\"2d\" isn't dice", or null when
    /// it reads. An empty value is never wrong.
    /// </summary>
    public virtual string? Problem(Component component, object? value) => null;

    /// <summary>True for the types of property it goes on: text, unless it says otherwise.</summary>
    public virtual bool Fits(Type type) => type == typeof(string);

    /// <summary>What it goes on, for the message when it's put on something else: "a string".</summary>
    public virtual string FitsText => "a string (text)";

    // A string's parts split at commas (and semicolons), trimmed, empty ones left out.
    private protected static string[] Parts(object? value, params char[] separators) =>
        (value as string ?? "").Split(separators.Length == 0 ? [',', ';'] : separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>
/// Marks a string property as dice, as tabletop games write them ("2d6+3", "d20", "1d8+1d6-1" or a plain number; see
/// <see cref="Data.Dice"/>): the Inspector builds them from how many dice, how many sides and what's added, and says the
/// lowest, highest and average roll.
/// </summary>
public sealed class DiceAttribute : FieldEditorAttribute
{
    public override string Describe() =>
        "Dice: how many, \"d\", their sides, then anything added or taken away, e.g. \"2d6+3\", \"d20\", \"1d8+1d6-1\" or a plain number; empty for none. The Inspector builds them and shows the lowest, highest and average roll.";

    public override string? Problem(Component component, object? value) =>
        value is string { Length: > 0 } text && !string.IsNullOrWhiteSpace(text) && !Dice.TryParse(text, out _)
            ? $"\"{text.Trim()}\" isn't dice: write them like 2d6+3, d20 or 5"
            : null;
}

/// <summary>
/// Marks a string property as a list of points, "x y" pairs separated by semicolons ("4 0; 4 3; 0 3"): the Inspector lists
/// them a row each, with X and Y to set, and adds, removes and reorders them. The Scene view puts a handle on each where it
/// knows what they're relative to (a mover's path, a polygon's corners).
/// </summary>
public sealed class PointsAttribute : FieldEditorAttribute
{
    /// <summary>They're whole cells of a grid (a grid patrol's), so they take whole numbers.</summary>
    public bool Cells { get; init; }

    /// <summary>They're a path from where the entity is placed, whose start (0 0) comes first without being listed.</summary>
    public bool FromStart { get; init; }

    /// <summary>The fewest it can have, e.g. 3 for a polygon's corners. Removing stops there.</summary>
    public int Least { get; init; }

    public override string Describe() =>
        $"Points: \"x y\" pairs separated by semicolons, e.g. \"4 0; 4 3\"{(Cells ? ", in whole cells" : "")}{(FromStart ? "; the start (0 0) is always the first point and isn't listed" : "")}{(Least > 0 ? $"; at least {Least}" : "")}. The Inspector lists them a row each to set, add, remove and reorder.";

    public override string? Problem(Component component, object? value)
    {
        var bad = Parts(value, ';', '|', '\n').FirstOrDefault(p => Mover.ParsePoints(p).Count != 1);
        return bad is not null ? $"\"{bad}\" isn't a point: write each as two numbers, x then y, like 4 0" : null;
    }

    /// <summary>Reads "x y; x y" (see <see cref="Mover.ParsePoints"/>).</summary>
    public static IReadOnlyList<Vector2> Read(string? text) => Mover.ParsePoints(text);
}

/// <summary>
/// Marks a property as tiles of a tilemap's tilesets, by number: a string of numbers and ranges ("10-15, 20", see
/// <see cref="Tilemap.ParseTileSet"/>), or a whole number for one tile. The Inspector shows the tilesets' tiles to click,
/// each picked one marked. The tilemap is the component itself, or the one on its entity.
/// </summary>
public sealed class TileNumbersAttribute : FieldEditorAttribute
{
    public override bool Fits(Type type) => type == typeof(string) || type == typeof(int);

    public override string FitsText => "a string (tile numbers) or a whole number (one tile)";

    public override string Describe() =>
        "Tiles of the tilemap's tilesets by number (the first tileset's from 0, the next's from 100, then 200...): a string of numbers and ranges like \"10-15, 20\", or for a whole number one tile. The Inspector shows the tiles to click.";

    public override string? Problem(Component component, object? value)
    {
        var bad = Parts(value, ',', ' ', ';').FirstOrDefault(p => Tilemap.ParseTileSet(p).Length == 0);
        return bad is not null ? $"\"{bad}\" isn't a tile number or range: write them like 10-15, 20" : null;
    }

    /// <summary>The tile numbers a string holds, in order, ranges filled in.</summary>
    public static IReadOnlyList<int> Read(string? text)
    {
        var set = Tilemap.ParseTileSet(text);
        return [.. Enumerable.Range(0, set.Length).Where(i => set[i])];
    }
}

/// <summary>
/// Marks a string property as runs of tiles that animate ("16-19 @6, 30-31 @2", see <see cref="Tilemap.AnimatedTiles"/>):
/// the Inspector lists them a row each, from which tile to which and how fast, with the frames shown.
/// </summary>
public sealed class TileAnimationsAttribute : FieldEditorAttribute
{
    public override string Describe() =>
        "Runs of tiles that animate: \"first-last @speed\" in frames a second, separated by commas, e.g. \"16-19 @6, 30-31 @2\" (4 a second without @). The Inspector lists them a row each with their frames shown.";

    public override string? Problem(Component component, object? value)
    {
        var bad = Parts(value).FirstOrDefault(p => Tilemap.ParseAnimations(p).Count != 1);
        return bad is not null ? $"\"{bad}\" isn't a run of tiles: write it like 16-19 @6 (two tiles or more, then a speed)" : null;
    }
}

/// <summary>
/// Marks a string property as runs of 16 tiles that fit themselves to their neighbors ("16-31, 48", see
/// <see cref="Tilemap.AutoTiles"/>): the Inspector lists each run with its 16 pieces shown.
/// </summary>
public sealed class AutoTilesAttribute : FieldEditorAttribute
{
    public override string Describe() =>
        "Runs of 16 tiles that join up with their neighbors, each by its first tile or as first-last, separated by commas, e.g. \"16-31\" or \"16, 48\". The Inspector lists each run with its pieces shown.";

    public override string? Problem(Component component, object? value)
    {
        var bad = Parts(value).FirstOrDefault(p => Tilemap.ParseAutoTiles(p).Count != 1);
        return bad is not null ? $"\"{bad}\" isn't a run of 16 tiles: give its first tile, like 16, or 16-31" : null;
    }
}

/// <summary>
/// Marks a list of strings as tilesets, a line each: an image in Assets and its tiles across and down ("Tiles/props.png
/// 4x4", see <see cref="Tilemap.MoreTilesets"/>). The Inspector lists them a row each, with the image picked from the
/// project's and the numbers its tiles get.
/// </summary>
public sealed class TilesetsAttribute : FieldEditorAttribute
{
    public override bool Fits(Type type) => type == typeof(IReadOnlyList<string>);

    public override string FitsText => "a list of strings";

    public override string Describe() =>
        "Tilesets, one per line: an image in Assets and its tiles across and down, e.g. \"Tiles/props.png 4x4\". The Inspector lists them a row each, with the image picked from the project's.";
}

/// <summary>
/// Marks a string property as an <see cref="Animation"/>'s clips ("Idle 0-3, Run 4-9 @12, Die 11-15 once", see
/// <see cref="Animation.ParseClips"/>): the Inspector lists them a row each, with their frames, speed and whether they
/// play once, and opens the Clip Editor to see them play.
/// </summary>
public sealed class ClipsAttribute : FieldEditorAttribute
{
    public override string Describe() =>
        "Clips, separated by commas: a name, a frame or range of frames, then optionally @speed (frames a second) and once or loop, e.g. \"Idle 0-3, Run 4-9 @12, Die 11-15 @8 once\". The Inspector lists them a row each; the Clip Editor (Art > Animation Clips) plays them.";

    public override string? Problem(Component component, object? value)
    {
        var bad = Parts(value, ',', ';', '\n').FirstOrDefault(p => Animation.ParseClips(p).Count != 1);
        return bad is not null ? $"\"{bad}\" isn't a clip: write a name then its frames, like Run 4-9 @12" : null;
    }
}

/// <summary>
/// Marks a string property as naming one of what another member of its component lists, e.g. an animation's
/// <see cref="Animation.Clip"/> one of its clips: the Inspector picks from them, and flags a name that isn't one.
/// </summary>
/// <param name="member">A property (or method taking nothing) of the component giving the names, e.g. "ClipNames".</param>
public sealed class ChoiceOfAttribute(string member) : FieldEditorAttribute
{
    /// <summary>The member of the component that lists the choices.</summary>
    public string Member { get; } = member;

    /// <summary>What the choices are, for component_describe, e.g. "one of its clips (Clips)".</summary>
    public string? Noun { get; init; }

    /// <summary>
    /// A member of the component listing names that are right too but aren't offered (the former names of what it lists,
    /// see <see cref="FormerNameAttribute"/>), so a scene saved before a rename isn't flagged.
    /// </summary>
    public string? Also { get; init; }

    public override string Describe() =>
        $"Names {Noun ?? $"one of what {Member} lists"}; the Inspector picks from them and flags a name that isn't one. Empty for none.";

    /// <summary>What <paramref name="component"/> lists to pick from now.</summary>
    public IReadOnlyList<string> Choices(Component component) => Listed(component, Member);

    private static IReadOnlyList<string> Listed(Component component, string member)
    {
        var type = component.GetType();
        var found = (object?)type.GetProperty(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)?.GetValue(component)
            ?? type.GetMethod(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static, Type.EmptyTypes)?.Invoke(component, null);
        return found is IEnumerable<string> names ? [.. names] : [];
    }

    public override string? Problem(Component component, object? value) =>
        value is string { Length: > 0 } name && !string.IsNullOrWhiteSpace(name) &&
        !Choices(component).Contains(name.Trim(), StringComparer.OrdinalIgnoreCase) &&
        !(Also is { } also && Listed(component, also).Contains(name.Trim(), StringComparer.OrdinalIgnoreCase))
            ? $"There's no \"{name.Trim()}\" to pick: {(Choices(component) is { Count: > 0 } choices ? $"the choices are {string.Join(", ", choices)}" : "there's nothing to pick yet")}"
            : null;
}

/// <summary>
/// Marks a list of strings as prefabs and how many of each, a line each: "Enemy 4", "Potion 1-2" (a number picked between
/// them) or "Gold" (one). The Inspector lists them a row each, the prefab picked from the project's and how many set.
/// </summary>
public sealed class PrefabCountsAttribute : FieldEditorAttribute
{
    public override bool Fits(Type type) => type == typeof(IReadOnlyList<string>);

    public override string FitsText => "a list of strings";

    public override string Describe() =>
        "Prefabs and how many of each, one per line: the prefab's name then a count, e.g. \"Enemy 4\", \"Potion 1-2\" (a number picked between them) or \"Gold\" (one). The Inspector lists them a row each, picking the prefab from the project's.";

    /// <summary>A line read: the prefab and the fewest and most of it.</summary>
    public static (string Prefab, int Least, int Most) Read(string line)
    {
        var text = line.Trim();
        var space = text.LastIndexOf(' ');
        if (space > 0)
        {
            var count = text[(space + 1)..];
            var dash = count.IndexOf('-', 1);
            if (dash < 0 && int.TryParse(count, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                return (text[..space].Trim(), Math.Max(0, n), Math.Max(0, n));
            if (dash > 0 && int.TryParse(count[..dash], NumberStyles.Integer, CultureInfo.InvariantCulture, out var low) &&
                int.TryParse(count[(dash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var high))
                return (text[..space].Trim(), Math.Max(0, Math.Min(low, high)), Math.Max(0, Math.Max(low, high)));
        }

        return (text, 1, 1);
    }
}

/// <summary>
/// Marks a <see cref="Vector2"/> as a direction, e.g. which way a shooter fires or a bouncer launches: (0, 1) is up and
/// (1, 0) right. The Inspector adds a compass of the eight ways to pick from, and with Distance Handles (D) the Scene view
/// draws it as an arrow from the entity with a knob to drag round.
/// </summary>
public sealed class DirectionAttribute : FieldEditorAttribute
{
    public override bool Fits(Type type) => type == typeof(Vector2);

    public override string FitsText => "a Vector2";

    /// <summary>
    /// It's in the entity's own space, turned with it (a plunger's, a bumper's), rather than the world's: the Scene view draws
    /// it turned, and a drag sets it back in the entity's own.
    /// </summary>
    public bool Turned { get; init; }

    /// <summary>What (0, 0) means, for a direction that may be none, e.g. "straight away from its middle": the compass offers it.</summary>
    public string? None { get; init; }

    public override string Describe() =>
        $"A direction, x right and y up: [0, 1] is up, [1, 0] right, [1, 1] up and to the right{(Turned ? ", in its own space (turned with the entity)" : "")}{(None is null ? "" : $"; [0, 0] is {None}")}. The Inspector has a compass of the eight ways; with Distance Handles the Scene view draws it as an arrow to drag round.";
}

/// <summary>
/// Marks a <see cref="Vector2"/> as a point offset from the entity, e.g. where a light shines from or where a shooter's
/// shots appear: with Distance Handles (D) the Scene view puts a knob there to drag.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class OffsetAttribute : Attribute
{
    /// <summary>
    /// It's in world units, added to where the entity is (a shooter's), rather than in the entity's own units, before its
    /// scale and turned with it (a light's).
    /// </summary>
    public bool World { get; init; }

    /// <summary>
    /// Another offset of the component it's from, rather than the entity (a slingshot's pull, from its pouch): its knob is
    /// drawn and dragged from there.
    /// </summary>
    public string? Center { get; init; }
}

/// <summary>
/// Says plain text is right for a property that takes text or a list of it, and why: words it shows or says, a name it gives
/// itself that nothing else names, or what the game writes as it runs (<c>[PlainText("words it shows")]</c>). The Inspector
/// keeps a plain box for it, and a list keeps its lines as one block of text (a tilemap's rows) rather than a row each. Every
/// other field taking text has a picker or an editor of its own: Griff's tests fail for one with neither, and say what to
/// mark it. A whole number named like a tile ("FirstTile") that isn't one says so with it too.
/// </summary>
/// <param name="why">Why typing it is right, e.g. "words it shows" or "what the game writes".</param>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PlainTextAttribute(string why) : Attribute
{
    /// <summary>Why typing it is right.</summary>
    public string Why { get; } = why;

    /// <summary>True for the types it goes on: text, a list of strings, or a whole number named like a tile.</summary>
    public static bool Fits(Type type) => type == typeof(string) || type == typeof(IReadOnlyList<string>) || type == typeof(int);
}

/// <summary>
/// Marks a string property as text that shows an output when another property names one, with <c>{0}</c> for the output
/// and <c>{1}</c> what it's out of, each with a format after a colon if it says ("Gold: {0}", "{0} / {1}", "{0:0.0} m/s"):
/// a <see cref="Text"/>'s Value. While the output is set the Inspector offers what to put in and shows how it reads now.
/// </summary>
/// <param name="output">The property naming the output (marked <see cref="OutputNameAttribute"/>), e.g. "Output".</param>
public sealed class OutputFormatAttribute(string output) : FieldEditorAttribute
{
    /// <summary>The property of the component naming the output it shows.</summary>
    public string Output { get; } = output;

    public override string Describe() =>
        $"While {System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(Output)} names an output, how it reads: {{0}} is the output and {{1}} what it's out of, each with a number format after a colon if it says, e.g. \"Gold: {{0}}\", \"{{0}} / {{1}}\" or \"{{0:0.0}} m/s\"; without them it shows just the output.";

    /// <summary>
    /// How <paramref name="component"/>'s text reads with <paramref name="template"/> as its value, with what its output gives
    /// now; null when it names no output, or the output can't be found.
    /// </summary>
    public string? Preview(Component component, string? template)
    {
        if (component.Entity is not { } entity || ComponentModel.Of(component).Property(Output)?.GetValue(component) is not string { Length: > 0 } output)
            return null;
        var track = ComponentModel.Of(component).Saved.FirstOrDefault(p => p.EntityName is not null)?.GetValue(component) as string;
        var reader = new OutputReader();
        if (!reader.Find(entity, track, output) || reader.Output is not { } found)
            return null;
        return Readout.Fill(template, reader.Number, reader.Text, reader.OutOf, found.ValueType);
    }
}
