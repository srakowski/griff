namespace Griff.Editor.Core.Modules;

/// <summary>
/// The genres' parts of the editor, each in its own folder and namespace (Genres/&lt;Genre&gt;, Griff.Editor.Genres.&lt;Genre&gt;):
/// a genre's windows and panels (the Fighting Move List, the Rhythm Chart, the Visual Novels Dialogue Editor), its field
/// editors, what it adds to the Clip Editor (<see cref="Features.Art.ClipOverlay"/>) and the Game menu, and its parts of
/// the Debug demo player. They're found here rather than listed, so the rest of the editor names no genre: only a genre's
/// folder names its module (Griff.Modules.&lt;Genre&gt;), and a genre's editor module registers the rest of its parts.
/// </summary>
public static class GenreEditors
{
    /// <summary>The namespace every genre's editor parts are in, one namespace below it for each genre.</summary>
    public const string Namespace = "Griff.Editor.Genres";

    private static readonly Lazy<Type[]> Types = new(() =>
        [.. typeof(GenreEditors).Assembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith(Namespace + ".", StringComparison.Ordinal) == true && t is { IsAbstract: false, IsClass: true })
            .OrderBy(t => t.FullName, StringComparer.Ordinal)]);

    /// <summary>Each genre's editor module, which registers its windows, panels, commands and Clip Editor overlays.</summary>
    public static IReadOnlyList<IEditorModule> Modules => [.. Instances<IEditorModule>()];

    /// <summary>One of every genre type that is a <typeparamref name="T"/> and can be made with nothing: its modules, its demo parts.</summary>
    public static IEnumerable<T> Instances<T>() =>
        Types.Value.Where(t => typeof(T).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null).Select(t => (T)Activator.CreateInstance(t)!);
}
