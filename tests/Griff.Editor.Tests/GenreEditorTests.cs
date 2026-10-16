using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Griff.Editor.Core.Modules;
using Griff.Editor.Features.Art;
using Griff.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// Keeps the genres out of the generic editor: a genre's editor parts (its windows and panels, field editors, Clip Editor
/// overlay, Game menu toggle, demo and snapshot parts) are in its own folder (Genres/&lt;Genre&gt;, namespace
/// Griff.Editor.Genres.&lt;Genre&gt;), the only place in the editor that names its module, and they reach the rest of the editor
/// through its extension points (editor modules found by <see cref="GenreEditors"/>, <see cref="ClipOverlay"/>,
/// <see cref="GenreTemplates"/>, the core's IDebugDrawing, entity actions in the module's own editor parts), so nothing
/// outside the genre folders names a genre's types or its folder's.
/// </summary>
public sealed partial class GenreEditorTests
{
    private static readonly string Editor = Path.Combine(RepoRoot(), "src", "Griff.Editor");

    // "Griff.Modules.Fighting" or "Griff.Editor.Genres.Fighting", as a using, a qualified name or a clr-namespace.
    [GeneratedRegex(@"Griff\.(?:Modules|Editor\.Genres)\.([A-Z][A-Za-z0-9]*)")]
    private static partial Regex GenreName();

    private static IEnumerable<string> Sources() =>
        Directory.EnumerateFiles(Editor, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".xaml", StringComparison.Ordinal))
            .Where(f => !Path.GetRelativePath(Editor, f).Split(Path.DirectorySeparatorChar)[0].Equals("obj", StringComparison.OrdinalIgnoreCase) &&
                        !Path.GetRelativePath(Editor, f).Split(Path.DirectorySeparatorChar)[0].Equals("bin", StringComparison.OrdinalIgnoreCase));

    // The genre a file is in ("Fighting" for Genres/Fighting/...), or null outside the genre folders.
    private static string? GenreOf(string file) =>
        Path.GetRelativePath(Editor, file).Split(Path.DirectorySeparatorChar) is ["Genres", var genre, _, ..] ? genre : null;

    [Fact]
    public void OnlyAGenresFolder_NamesItsModule_AndNoOtherGenre()
    {
        var problems = new List<string>();
        foreach (var file in Sources())
        {
            var genre = GenreOf(file);
            var named = GenreName().Matches(File.ReadAllText(file)).Select(m => m.Groups[1].Value).Distinct();
            foreach (var other in named.Where(n => n != genre))
            {
                problems.Add(genre is null
                    ? $"{Path.GetRelativePath(Editor, file)} names {other}: a genre's editor parts go in Genres/{other}, reached through an extension point."
                    : $"{Path.GetRelativePath(Editor, file)} is the {genre} genre's, but names {other}.");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void EachGenreFolder_IsAModulesName()
    {
        var modules = Directory.GetDirectories(Path.Combine(RepoRoot(), "src", "Modules")).Select(Path.GetFileName).ToHashSet();
        var genres = Directory.GetDirectories(Path.Combine(Editor, "Genres")).Select(Path.GetFileName).ToList();
        Assert.NotEmpty(genres);
        Assert.All(genres, g => Assert.Contains(g, modules));
    }

    [Fact]
    public void TheEditorsGenericTypes_NameNoModulesTypes()
    {
        // What a type's members are declared as: a field's, property's or event's type, a method's return and parameters, what
        // it's made from. (What a method's body names, the source rule above holds to its genre's folder.)
        static IEnumerable<Type> Named(Type type)
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            if (type.BaseType is { } baseType)
                yield return baseType;
            foreach (var face in type.GetInterfaces())
                yield return face;
            foreach (var field in type.GetFields(all))
                yield return field.FieldType;
            foreach (var property in type.GetProperties(all))
                yield return property.PropertyType;
            foreach (var e in type.GetEvents(all))
                yield return e.EventHandlerType!;
            foreach (var method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
            {
                if (method is MethodInfo info)
                    yield return info.ReturnType;
                foreach (var parameter in method.GetParameters())
                    yield return parameter.ParameterType;
            }
        }

        static IEnumerable<Type> Within(Type type) =>
            type.IsGenericType ? type.GetGenericArguments().SelectMany(Within).Prepend(type)
            : type.HasElementType ? Within(type.GetElementType()!)
            : [type];

        var problems = typeof(App).Assembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith(GenreEditors.Namespace + ".", StringComparison.Ordinal) != true)
            .SelectMany(t => Named(t).SelectMany(Within)
                .Where(n => n.Assembly.GetName().Name!.StartsWith("Griff.Modules.", StringComparison.Ordinal))
                .Select(n => $"{t.FullName} names {n.FullName} ({n.Assembly.GetName().Name})"))
            .Distinct()
            .ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void TheGenresEditorParts_AreFound_NotListed() => EditorHost.Run(services =>
    {
        // Their editor modules are the app's.
        var genres = GenreEditors.Modules.Select(m => m.GetType().Namespace!.Split('.')[^1]).ToList();
        Assert.Contains("Fighting", genres);
        Assert.Contains("Rhythm", genres);
        Assert.Contains("VisualNovels", genres);
        Assert.All(GenreEditors.Modules, m => Assert.Contains(App.Modules, a => a.GetType() == m.GetType()));

        // Their templates are merged where they're drawn.
        Assert.Contains(GenreTemplates.Sources, s => s.OriginalString.EndsWith("genres/fighting/templates.xaml", StringComparison.Ordinal));
        Assert.True(new GenreTemplates().MergedDictionaries.Count >= 1);

        // The Clip Editor has the Fighting genre's overlay, by its name, as agents reach it.
        var clips = services.GetRequiredService<ClipEditor>();
        Assert.Equal(["Boxes"], clips.Overlays.Select(o => o.Name));
    });

    [Fact]
    public void AGamesDebugDrawings_AreSwitchedByName()
    {
        var game = new Game(new Griff.Scenes.Scene(), new InputState(), []);
        game.AddSystem(new Drawn());
        var drawn = Assert.Single(game.DebugDrawings);
        game.ShowDebugDrawing("lines", true);
        Assert.True(drawn.Show);
        game.ShowDebugDrawing("Other", false);
        Assert.True(drawn.Show);
        game.ShowDebugDrawing("Lines", false);
        Assert.False(drawn.Show);
    }

    private sealed class Drawn : GameSystem, IDebugDrawing
    {
        public string DrawingName => "Lines";

        public bool Show { get; set; }
    }

    private static string RepoRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "griff.slnx")))
            folder = folder.Parent;
        return folder?.FullName ?? throw new DirectoryNotFoundException("The tests run from inside the Griff repository.");
    }
}
