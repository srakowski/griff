using System.Reflection;

namespace Griff.Editing;

/// <summary>
/// The modules' editor parts this program has (each a <c>Griff.Modules.&lt;Genre&gt;.Editor</c> assembly beside its
/// module's, see <see cref="ModuleAssemblies"/>): their templates, Entity menu presets and actions, found in these and in
/// Griff.Editing rather than listed. The editor, the tests and the tools have them all; an exported game has none.
/// </summary>
public static class EditorModules
{
    private static readonly ModuleAssemblies.Cached<IReadOnlyList<Assembly>> Found = new(() =>
        [.. ModuleAssemblies.All.Select(m => Beside(m)).OfType<Assembly>()]);

    /// <summary>The editor parts of the modules this program has, in their modules' order.</summary>
    public static IReadOnlyList<Assembly> All => Found.Value;

    /// <summary>Griff.Editing and the modules' editor parts: where templates, presets and entity actions are found.</summary>
    public static IEnumerable<Assembly> WithEditing => All.Prepend(typeof(EditorModules).Assembly);

    /// <summary>Every concrete <typeparamref name="T"/> with a parameterless constructor in Griff.Editing and the modules' editor parts, one of each.</summary>
    public static IEnumerable<T> Instances<T>() where T : class =>
        WithEditing.SelectMany(a => a.GetTypes())
            .Where(t => t.IsSubclassOf(typeof(T)) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (T)Activator.CreateInstance(t)!);

    // A module's editor parts, by name beside it, if this program has them.
    private static Assembly? Beside(Assembly module)
    {
        try
        {
            return Assembly.Load(new AssemblyName(module.GetName().Name + ModuleAssemblies.EditorSuffix));
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            return null;
        }
    }
}
