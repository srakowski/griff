namespace Griff.Scenes;

/// <summary>
/// What a project's tables say, as a field's editor asks while it's edited (see <see cref="RowsAttribute.Note"/> and
/// <see cref="RowsAttribute.MoreChoices"/>): the editor knows the project's names and hands them over through this, so a
/// component's rows can say what a cell names without the scene's data knowing the editor.
/// </summary>
public interface IProjectNames
{
    /// <summary>True if there's a table called <paramref name="table"/>.</summary>
    bool HasTable(string? table);

    /// <summary>The rows of the table called <paramref name="table"/> (or none, if there's no such table).</summary>
    IReadOnlyList<string> RowsOf(string? table);

    /// <summary>
    /// What the column called <paramref name="column"/> of the table called <paramref name="table"/> can say, when it's a
    /// column of choices; none otherwise.
    /// </summary>
    IReadOnlyList<string> ChoicesOf(string? table, string column);
}
