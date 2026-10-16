using System.Reflection;
using System.Runtime.Loader;

namespace Griff;

/// <summary>
/// The assemblies Griff's modules run from (see <c>Griff.Runtime.GameModule</c>). A module's running parts are an assembly of their
/// own, <c>Griff.Modules.&lt;Genre&gt;</c>, and its editor parts another beside it (<c>Griff.Modules.&lt;Genre&gt;.Editor</c>),
/// so Griff names none of them: a program plays with the module assemblies it has, found by their names beside it (the
/// editor, the tests and the tools have them all), and an exported game's host loads those its export carries
/// (<see cref="Load(string)"/>: the modules its project uses, in its game folder's <c>ExportLayout.ModulesFolder</c>)
/// before it reads the game. What's worked out from them (<c>GameModule.All</c>, the components every game knows,
/// the dialogue tags modules do) follows <see cref="Revision"/>.
/// </summary>
public static class ModuleAssemblies
{
    /// <summary>What a module's running assembly is called before its genre: "Griff.Modules.".</summary>
    public const string Prefix = "Griff.Modules.";

    /// <summary>What a module's editor parts' assembly is called after its module's: ".Editor".</summary>
    public const string EditorSuffix = ".Editor";

    private static readonly Lock Gate = new();

    // The module assemblies loaded into the process, found beside it the first time they're asked for and added to as a
    // host loads a game's (see StaticStateTests: they're the process's, like the assemblies it has loaded).
    private static Assembly[]? s_loaded;
    private static int s_revision;

    /// <summary>The module assemblies this program plays with: those beside it, and any a host has loaded since.</summary>
    public static IReadOnlyList<Assembly> All
    {
        get
        {
            if (Volatile.Read(ref s_loaded) is { } loaded)
                return loaded;
            lock (Gate)
                return s_loaded ??= [.. Beside(AppContext.BaseDirectory)];
        }
    }

    /// <summary>Changes whenever a host loads more module assemblies, so what was worked out from them is again.</summary>
    public static int Revision => Volatile.Read(ref s_revision);

    /// <summary>Whether <paramref name="assembly"/> is a module's running parts (not its editor parts).</summary>
    public static bool IsModule(Assembly assembly) => IsModuleName(assembly.GetName().Name);

    /// <summary>Whether an assembly called <paramref name="name"/> is a module's running parts: "Griff.Modules.Match3".</summary>
    public static bool IsModuleName(string? name) =>
        name is not null && name.StartsWith(Prefix, StringComparison.Ordinal) && name.Length > Prefix.Length &&
        !name.EndsWith(EditorSuffix, StringComparison.Ordinal);

    /// <summary>Whether <paramref name="type"/> is Griff's own or a module's, rather than a script's.</summary>
    public static bool IsGriffs(Type type) => type.Assembly == typeof(ModuleAssemblies).Assembly || IsModule(type.Assembly);

    /// <summary>
    /// Loads the module assemblies in <paramref name="folder"/> (an exported game's <c>ExportLayout.ModulesFolder</c>)
    /// that aren't loaded yet, each once, from their bytes (so the files aren't held while the game runs). Returns them all,
    /// those that were already loaded too. A folder that isn't there has none.
    /// </summary>
    public static IReadOnlyList<Assembly> Load(string folder)
    {
        if (!Directory.Exists(folder))
            return [];
        return [.. Directory.EnumerateFiles(folder, Prefix + "*.dll").Order(StringComparer.Ordinal)
            .Where(path => IsModuleName(Path.GetFileNameWithoutExtension(path)))
            .Select(path => Load(Path.GetFileNameWithoutExtension(path), () => File.ReadAllBytes(path)))];
    }

    /// <summary>
    /// Loads the module assembly called <paramref name="name"/> from <paramref name="image"/> (one a browser fetched with its
    /// game, "Griff.Modules.Match3"), unless one of that name is loaded already, which it returns.
    /// </summary>
    public static Assembly Load(string name, byte[] image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return Load(name, () => image);
    }

    private static Assembly Load(string? name, Func<byte[]> image)
    {
        if (!IsModuleName(name))
            throw new BadImageFormatException($"\"{name}\" isn't a module's assembly (Griff.Modules.<Genre>).");
        lock (Gate)
        {
            var loaded = s_loaded ??= [.. Beside(AppContext.BaseDirectory)];
            if (loaded.FirstOrDefault(a => a.GetName().Name == name) is { } known)
                return known;
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name)
                           ?? AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(image()));
            if (s_resolving is false)
            {
                // A game's scripts, loaded on their own, find the modules they were built against here.
                AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
                    All.FirstOrDefault(a => a.GetName().Name == new AssemblyName(args.Name).Name);
                s_resolving = true;
            }

            Volatile.Write(ref s_loaded, [.. loaded, assembly]);
            Interlocked.Increment(ref s_revision);
            return assembly;
        }
    }

    private static bool s_resolving;

    // The module assemblies beside the program, by their names (so a program that references them gets the ones it has).
    private static IEnumerable<Assembly> Beside(string folder)
    {
        string[] files;
        try
        {
            files = Directory.Exists(folder) ? Directory.GetFiles(folder, Prefix + "*.dll") : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or PlatformNotSupportedException)
        {
            files = [];
        }

        foreach (var name in files.Select(Path.GetFileNameWithoutExtension).Where(IsModuleName).Order(StringComparer.Ordinal))
        {
            Assembly? assembly;
            try
            {
                assembly = Assembly.Load(new AssemblyName(name!));
            }
            catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
            {
                assembly = null;
            }

            if (assembly is not null)
                yield return assembly;
        }
    }

    /// <summary>
    /// A value worked out from the module assemblies (the modules, the components every game knows), worked out again when
    /// a host loads more.
    /// </summary>
    public sealed class Cached<T>(Func<T> make) where T : class
    {
        private sealed record Made(int Revision, T Value);

        private readonly Lock _making = new();
        private Made? _made;

        /// <summary>
        /// The value, worked out again if modules were loaded since: one value for everyone who asks meanwhile, however many
        /// threads ask at once (each preset, template and module is made once).
        /// </summary>
        public T Value
        {
            get
            {
                if (Volatile.Read(ref _made) is { } made && made.Revision == Revision)
                    return made.Value;
                lock (_making)
                {
                    var revision = Revision;
                    if (_made is { } again && again.Revision == revision)
                        return again.Value;
                    var value = make();
                    Volatile.Write(ref _made, new Made(revision, value));
                    return value;
                }
            }
        }
    }
}
