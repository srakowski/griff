using Griff.Editing;

namespace Griff.Editor.Core.Commands;

/// <summary>
/// How commands that make things are laid out by their <see cref="EditorCommand.Category"/>: the Entity menu's submenus,
/// the Hierarchy's right-click menu and the Add Entity palette all list them this way. Categories are found on the
/// commands (and a module's presets), not listed here; this only says which come first.
/// </summary>
public static class CommandCategories
{
    /// <summary>
    /// The order the core's categories are listed in. Any others (a genre module's, named after it) follow alphabetically,
    /// set apart from these.
    /// </summary>
    public static IReadOnlyList<string> Order { get; } =
        ["Basics", "Characters", "Gameplay", "Level", "Physics", "UI", "Story", "Audio", "Lights and Effects"];

    /// <summary>Where <paramref name="category"/> goes: its place in <see cref="Order"/>, or after all of them.</summary>
    public static int Rank(string category)
    {
        for (var i = 0; i < Order.Count; i++)
        {
            if (Order[i].Equals(category, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return Order.Count;
    }

    /// <summary>True for a category that isn't one of the core's: a genre module's.</summary>
    public static bool IsModule(string category) => Rank(category) == Order.Count;

    /// <summary>
    /// The submenu the genre modules' categories are in, so a new module never makes the Entity menu taller: Entity &gt;
    /// Genres &gt; Match-3 &gt; Match Board.
    /// </summary>
    public const string Genres = "Genres";

    /// <summary>Where a category's submenu is under the Entity menu: "Characters", or "Genres &gt; Match-3" for a module's.</summary>
    public static string MenuPath(string category) => IsModule(category) ? $"{Genres} > {category}" : category;

    /// <summary>
    /// The commands among <paramref name="commands"/> that have a category, by category (in <see cref="Rank"/> order, then
    /// by name), each category's in their menu order.
    /// </summary>
    public static IReadOnlyList<IGrouping<string, EditorCommand>> Group(IEnumerable<EditorCommand> commands) =>
        commands.Where(c => c.Category is not null)
            .OrderBy(c => c.Placement?.Order ?? 0)
            .ThenBy(c => c.DisplayText, StringComparer.OrdinalIgnoreCase)
            .GroupBy(c => c.Category!)
            .OrderBy(g => Rank(g.Key))
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// What its item says inside its category: its text without "Create ", which the category already says ("_Sprite"
    /// under Basics for "Create _Sprite"); '_' still marks the access key.
    /// </summary>
    public static string ItemText(EditorCommand command) =>
        command.Category is not null && command.Text.StartsWith("Create ", StringComparison.Ordinal) ? command.Text["Create ".Length..] : command.Text;

    /// <summary>
    /// One line saying what it makes: the first sentence of the /// summary that describes it to agents (see
    /// <see cref="EditorCommand.DescribedBy"/>), so the menu, the palette and the agents' tool never say different things.
    /// </summary>
    public static string Description(EditorCommand command) =>
        DocComments.Summary(command.DescribedBy) is { IsEmpty: false } summary ? summary.FirstSentence : command.DisplayText.TrimEnd('.') + ".";
}
