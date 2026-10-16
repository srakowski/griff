using System.Text;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editor.Features.Inspector;

/// <summary>
/// The rule that keeps fields from landing as boxes to type code in: every field that takes text or a list of it (names,
/// lists, paths, a little language) gets a picker or an editor of its own in the Inspector, or says why plain text is right
/// for it with <see cref="PlainTextAttribute"/>; and a whole number named like a tile ("FirstTile") is picked off the tileset
/// (<see cref="TileNumbersAttribute"/>) or says why not. The editor's tests hold every component to it, Griff's and the
/// templates' scripts' alike, and say for each one that breaks it what to mark it.
/// </summary>
public static class FieldCoverage
{
    /// <summary>A field that breaks the rule: which, and what to do about it.</summary>
    /// <param name="Field">"Component.Property".</param>
    /// <param name="Type">What it holds, as C# writes it: "string", "IReadOnlyList&lt;string&gt;", "int".</param>
    /// <param name="Advice">What to mark it, from what its name suggests it takes.</param>
    public sealed record Gap(string Field, string Type, string Advice)
    {
        public override string ToString() => $"{Field} ({Type}): {Advice}";
    }

    /// <summary>The fields of <paramref name="descriptor"/> that break the rule, as the Inspector shows them through <paramref name="host"/>.</summary>
    public static IEnumerable<Gap> Gaps(ComponentDescriptor descriptor, IPropertyHost host)
    {
        Component? component = null;
        foreach (var property in descriptor.Properties)
        {
            if (property.PlainText is not null)
                continue;
            var name = $"{descriptor.DisplayName}.{property.Name}";
            if (property.Type == typeof(int))
            {
                if (IsTileName(property.Name) && property.Editor is not TileNumbersAttribute)
                    yield return new(name, "int", "its name says it's a tile: mark it [TileNumbers] so it's clicked off the tilemap's tilesets, or [PlainText(\"why\")] if it isn't a tile's number.");
                continue;
            }

            if (property.Type != typeof(string) && property.Type != typeof(IReadOnlyList<string>))
                continue;
            component ??= descriptor.CreateInstance();
            var field = new PropertyField(component, property, host);
            // A list's rows without an editor of its own (GenericFields) are what scripts' lists fall back on, not a choice.
            var generic = field.Kind == FieldKind.Custom && property.Editor is null;
            if (field.Kind is FieldKind.Text or FieldKind.Lines || generic)
                yield return new(name, property.Type == typeof(string) ? "string" : "IReadOnlyList<string>", Advice(property));
        }
    }

    /// <summary>What every gap's advice comes down to, and where to read more: said once, after the list of them.</summary>
    public const string HowTo =
        "Each of these takes text typed from memory, with no picker or editor in the Inspector. For each one, either:\n" +
        "- If it names something the project has, mark it with the picker for it: [Names(NameKind.Prefab)] (or Scene, Message, Layer, Action, Item, Flag, Table, Kind, Saved...), [EntityName], [ActionName], [OutputName] or [AssetFile(AssetFileKind.Image)].\n" +
        "- If it's written in a little language of its own (a list, names and values, points, dice, a format), mark it with the FieldEditorAttribute for it: [NameList], [ListOf], [Counts], [Pairs], [ListRows], [Points], [Dice], [TileNumbers], [Format], [NumberFormat]... or a RowsAttribute or FieldEditorAttribute of your own, with a FieldEditor<TAttribute> for it in src/Griff.Editor/Features/Inspector/FieldEditors and its DataTemplate there.\n" +
        "- If typing it is right (words it shows or says, a name it gives itself that nothing else names, what the game writes as it runs), mark it [PlainText(\"why\")], saying why.\n" +
        "See \"Fields with editors of their own\" in site/src/content/docs/docs/editor.md.";

    /// <summary>The message for <paramref name="gaps"/>: each with what to mark it, then <see cref="HowTo"/>.</summary>
    public static string Message(IReadOnlyCollection<Gap> gaps)
    {
        var text = new StringBuilder().AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"{gaps.Count} field{(gaps.Count == 1 ? "" : "s")} with no editor in the Inspector:");
        foreach (var gap in gaps)
            text.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- {gap}");
        return text.AppendLine().Append(HowTo).ToString();
    }

    // A whole number named like a tile's number: "WallTile", "FirstTile" (not "Tiles" counts or "TileSize").
    private static bool IsTileName(string name) => name.EndsWith("Tile", StringComparison.Ordinal);

    // What its name suggests it takes, and the mark for that.
    private static readonly (string[] Endings, string Says, string Mark)[] Hints =
    [
        (["Prefab", "Prefabs", "Effect", "Projectile", "Spawn"], "names a prefab", "[Names(NameKind.Prefab)]"),
        (["Scene", "Scenes", "Level", "Room"], "names a scene", "[Names(NameKind.Scene)]"),
        (["Message", "Messages", "Event"], "names a message", "[Names(NameKind.Message, Hears = true)] for one it listens for, or [Names(NameKind.Message)] for one it sends"),
        (["Layer", "Layers"], "names collision layers", "[Names(NameKind.Layer, Many = true)] (or without Many for one)"),
        (["Action", "Actions", "Input", "Button"], "names an input action or something a target does", "[Names(NameKind.Action)] for an input action, or [ActionName] for what the entities it's done on can do"),
        (["Item", "Items", "Key"], "names an item", "[Names(NameKind.Item)], or [Counts(NameKind.Item)] for items and how many"),
        (["Flag", "Flags"], "names a flag", "[Names(NameKind.Flag)] (Condition = true to take \"not\" too)"),
        (["Target", "Targets", "Entity", "Entities", "Follow", "Track"], "names entities", "[EntityName] (Many = true for several)"),
        (["Table"], "names a table", "[Names(NameKind.Table)]"),
        (["Column"], "names a table's column", "[Names(NameKind.Column, Table = nameof(...))]"),
        (["Row"], "names a table's row", "[Names(NameKind.Row, Table = nameof(...))]"),
        (["Output"], "names an output", "[OutputName]"),
        (["Points", "Path", "Route", "Corners"], "is a list of points", "[Points]"),
        (["Tiles"], "is tile numbers", "[TileNumbers]"),
        (["Roll", "Dice", "Damage"], "is dice", "[Dice]"),
        (["Format", "Template"], "is text with holes", "[Format(\"{0} ...\")] (or [NumberFormat] for how a number reads)"),
        (["Clips"], "is an animation's clips", "[Clips]"),
        (["Clip"], "names one of its clips", "[ChoiceOf(nameof(...))]"),
        (["Picture", "Icon", "Sprite", "Background", "Texture"], "is a picture", "[AssetFile(AssetFileKind.Image)]"),
        (["Song", "Audio"], "is music or a sound", "[AssetFile(AssetFileKind.Music)] (or Sound)"),
        (["Color", "Colors", "Colour", "Tint"], "is colors", "a Color property, or [ListOf(Colors = true)] for several"),
        (["Names", "Options", "Sides", "Teams", "Choices"], "is a list of names it gives", "[NameList]"),
        (["Saved", "SaveAs", "SavedAs"], "names something in the save data", "[Names(NameKind.Saved)]"),
        (["Kind", "Type", "Accepts"], "names a kind of thing", "[Names(NameKind.Kind, Scope = \"...\")]"),
    ];

    private static string Advice(PropertyDescriptor property)
    {
        foreach (var (endings, says, mark) in Hints)
        {
            if (endings.Any(e => property.Name.EndsWith(e, StringComparison.Ordinal)))
                return $"its name says it {says}: mark it {mark}; or see below.";
        }

        return property.Type == typeof(string)
            ? "give it a picker or an editor, or mark it [PlainText(\"why\")]: see below."
            : "a list of strings: mark it with the editor for what its lines are ([NameList], [Pairs], [ListRows], [Counts], [ListOf]...), or [PlainText(\"why\")] to keep its lines as text: see below.";
    }
}
