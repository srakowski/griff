using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Griff.Tests.Architecture;

/// <summary>
/// Guards the core's layers: Scenes holds entities and components (each holding its own state and advancing it when its
/// system asks), Runtime runs them and Rendering draws them, so nothing in Griff.Scenes may use Griff.Runtime or
/// Griff.Rendering, and nothing in Griff.Runtime (or a module) may use Griff.Rendering. What both sides need lives below
/// them, in Griff (Color, Box, Ease, Rand) and Griff.Input (Key, the built-in actions); a component that needs the game
/// asks for a service by type (an IGameService), which the one system that owns that part of the game provides, and
/// keeps none; and what the runtime asks of a view (the main camera, its size, the view's projection, what's drawn on
/// top) it asks Griff.Runtime.Views, which the renderer reads too. A use counts however far round it goes: every type a
/// type uses is followed through the layers, so a Scenes type using an Editing type that uses the Runtime uses the Runtime.
/// </summary>
public sealed class LayerTests
{
    [Fact]
    public void Scenes_DoesNotUseRuntimeOrRendering()
    {
        var uses = Graph().Reaching(IsScenes, used => InLayer(used, "Griff.Runtime") || InLayer(used, "Griff.Rendering"));

        Assert.True(uses.Count == 0,
            "Griff.Scenes holds components, each its own state, and mustn't use Griff.Runtime or Griff.Rendering, by way of another namespace either (ask for " +
            "the service a component needs, or move the code up a layer):\n" + string.Join("\n", uses));
    }

    // The runtime runs a game without knowing how it's drawn: the renderer reads the game, not the other way round.
    [Fact]
    public void Runtime_DoesNotUseRendering()
    {
        var uses = Graph().Reaching(t => InLayer(t, "Griff.Runtime") || InLayer(t, "Griff.Modules"), used => InLayer(used, "Griff.Rendering"));

        Assert.True(uses.Count == 0,
            "Griff.Runtime and the modules mustn't use Griff.Rendering, by way of another namespace either (ask " +
            "Griff.Runtime.Views about the view, or let the renderer read the game):\n" + string.Join("\n", uses));
    }

    // A module keeps the same layers: the files its components are declared in (with the types that go with them, such
    // as a board's rules) hold their own state, and its systems and drawing use them, not the other way round.
    [Fact]
    public void ModuleComponents_DoNotUseRuntimeOrRendering()
    {
        var graph = Graph();
        var component = graph.Compilation.GetTypeByMetadataName("Griff.Scenes.Component")!;
        var system = graph.Compilation.GetTypeByMetadataName("Griff.Runtime.GameSystem")!;
        var data = graph.Trees.Where(t => t.GetRoot().DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>()
                .Any(n => n.Name.ToString().StartsWith("Griff.Modules.", StringComparison.Ordinal)))
            .Select(graph.DeclaredIn)
            .Where(types => types.Any(t => DerivesFrom(t, component)))
            .SelectMany(types => types)
            .ToHashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var uses = graph.Reaching(data.Contains,
            used => InLayer(used, "Griff.Runtime") || InLayer(used, "Griff.Rendering") || DerivesFrom(used, system));

        Assert.True(uses.Count == 0,
            "A module's components hold their own state and mustn't use Griff.Runtime, Griff.Rendering or its systems, by way of another " +
            "namespace either (move the code to its system):\n" + string.Join("\n", uses));
    }

    // A component reaches the game through services asked for by type, each the one system's that owns that part of the
    // game: no interface the game implements whole that grows with every action, and nothing provides a service but a system.
    [Fact]
    public void Services_AreEachProvidedByTheOneSystemThatOwnsThem()
    {
        var compilation = Graph().Compilation;
        var service = compilation.GetTypeByMetadataName("Griff.Scenes.IGameService")!;
        var system = compilation.GetTypeByMetadataName("Griff.Runtime.GameSystem")!;
        var types = AllTypes(compilation.Assembly.GlobalNamespace).ToList();
        var services = types.Where(t => t.TypeKind == TypeKind.Interface && t.AllInterfaces.Contains(service, SymbolEqualityComparer.Default)).ToList();
        var wrong = new List<string>();
        foreach (var each in services)
        {
            var providers = types.Where(t => t.TypeKind == TypeKind.Class && t.AllInterfaces.Contains(each, SymbolEqualityComparer.Default)).ToList();
            if (providers.Count != 1 || !DerivesFrom(providers[0], system))
                wrong.Add($"{Name(each)}: provided by {(providers.Count == 0 ? "nothing" : string.Join(", ", providers.Select(p => Name(p))))}");
        }

        Assert.NotEmpty(services);
        Assert.True(wrong.Count == 0,
            "Each service a component asks for (an IGameService) is provided by the one system that owns that part of the game " +
            "(not the Game, not a component):\n" + string.Join("\n", wrong));
    }

    // A component holds its own state and the steps it advances when its system asks: the game's services are handed to
    // the action or step that needs one, never kept, so a component (Griff's or a module's) has no field of one.
    [Fact]
    public void Components_KeepNoServiceOfTheGame()
    {
        var compilation = Graph().Compilation;
        var component = compilation.GetTypeByMetadataName("Griff.Scenes.Component")!;
        var service = compilation.GetTypeByMetadataName("Griff.Scenes.IGameService")!;
        var services = compilation.GetTypeByMetadataName("Griff.Scenes.IGameServices")!;
        bool IsService(ITypeSymbol type) => type switch
        {
            IArrayTypeSymbol array => IsService(array.ElementType),
            INamedTypeSymbol named => SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, services) ||
                                      SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, service) ||
                                      named.AllInterfaces.Contains(service, SymbolEqualityComparer.Default) ||
                                      named.TypeArguments.Any(IsService),
            _ => false,
        };
        var kept = AllTypes(compilation.Assembly.GlobalNamespace)
            .Where(t => DerivesFrom(t, component) && (IsScenes(t) || InLayer(t, "Griff.Modules")))
            .SelectMany(t => t.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsConst && IsService(f.Type))
                .Select(f => $"{Name(t)}.{(f.AssociatedSymbol ?? f).Name}"))
            .ToList();

        Assert.True(kept.Count == 0,
            "A component holds only its own state: pass the service an action or step needs in (its first parameter, or " +
            "what its system hands it) rather than keeping it:\n" + string.Join("\n", kept));
    }

    // No interface of Griff.Scenes serves only to get past these layers to the runtime (a script told of its health's
    // damage, the save data a carried value is kept in): what a component asks of the game, or tells it, it asks a
    // service, and the system providing it does the rest. Nor does the game implement one whole.
    [Fact]
    public void ScenesInterfaces_ReachTheRuntimeOnlyAsServices()
    {
        var compilation = Graph().Compilation;
        var service = compilation.GetTypeByMetadataName("Griff.Scenes.IGameService")!;
        var services = compilation.GetTypeByMetadataName("Griff.Scenes.IGameServices")!;
        var types = AllTypes(compilation.Assembly.GlobalNamespace).ToList();
        var only = types
            .Where(i => i.TypeKind == TypeKind.Interface && IsScenes(i) && i.DeclaredAccessibility != Accessibility.Public &&
                        !i.AllInterfaces.Contains(service, SymbolEqualityComparer.Default))
            .Select(i => (Interface: i, By: types.Where(t => t.TypeKind != TypeKind.Interface && t.AllInterfaces.Contains(i, SymbolEqualityComparer.Default)).ToList()))
            .Where(x => x.By.Count > 0 && x.By.All(t => InLayer(t, "Griff.Runtime")))
            .Select(x => $"{Name(x.Interface)}: implemented only by {string.Join(", ", x.By.Select(t => Name(t)))}")
            .ToList();
        var game = compilation.GetTypeByMetadataName("Griff.Runtime.Game")!;

        Assert.True(only.Count == 0,
            "An interface of Griff.Scenes that only the runtime implements is a way past the layers: make it a service " +
            "(an IGameService) a system provides:\n" + string.Join("\n", only));
        Assert.DoesNotContain(game.AllInterfaces, i => IsScenes(i));
    }

    private static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            yield return type;
            foreach (var nested in Nested(type))
                yield return nested;
        }

        foreach (var inner in ns.GetNamespaceMembers())
        {
            foreach (var type in AllTypes(inner))
                yield return type;
        }

        static IEnumerable<INamedTypeSymbol> Nested(INamedTypeSymbol type) => type.GetTypeMembers().SelectMany(t => Nested(t).Prepend(t));
    }

    // The rules see through a detour: a type reaching a layer by way of another namespace is caught, however it names it.
    [Fact]
    public void Graph_FollowsUsesThroughOtherNamespaces()
    {
        var tree = CSharpSyntaxTree.ParseText("""
            namespace Griff.Scenes { public sealed class Data { public Griff.Editing.Middle? Through; } }
            namespace Griff.Editing
            {
                using Griff.Runtime;
                public sealed class Middle { public void Run() => Simulation.Step(); }
            }
            namespace Griff.Runtime { public static class Simulation { public static void Step() { } } }
            namespace Griff.Scenes { public sealed class Alone { public int Count; } }
            """);
        var graph = TypeGraph.Of(CSharpCompilation.Create("Detour", [tree], PlatformReferences()), [tree]);

        Assert.Equal(["Griff.Scenes.Data -> Griff.Editing.Middle -> Griff.Runtime.Simulation"],
            graph.Reaching(IsScenes, used => InLayer(used, "Griff.Runtime")));
    }

    private static bool IsScenes(INamedTypeSymbol type) => InLayer(type, "Griff.Scenes");

    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var t = type.BaseType; t is not null; t = t.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(t, baseType))
                return true;
        }

        return false;
    }

    /// <summary>Every type the core declares and the types each uses, by any name in it: for tests about what reaches what.</summary>
    internal static TypeGraph Graph() => GraphSource.Value;

    private static readonly Lazy<TypeGraph> GraphSource = new(() => TypeGraph.Of(Core().Compilation, Core().Trees));

    /// <summary>
    /// The types declared in some source and the source types each one names (a nested type counts as the type it's in),
    /// so what a type uses can be followed as far as it goes.
    /// </summary>
    internal sealed class TypeGraph
    {
        private readonly Dictionary<INamedTypeSymbol, HashSet<INamedTypeSymbol>> _uses;
        private readonly Dictionary<SyntaxTree, List<INamedTypeSymbol>> _declared;

        private TypeGraph(CSharpCompilation compilation, IReadOnlyList<SyntaxTree> trees,
            Dictionary<INamedTypeSymbol, HashSet<INamedTypeSymbol>> uses, Dictionary<SyntaxTree, List<INamedTypeSymbol>> declared)
        {
            (Compilation, Trees, _uses, _declared) = (compilation, trees, uses, declared);
        }

        public CSharpCompilation Compilation { get; }

        public IReadOnlyList<SyntaxTree> Trees { get; }

        /// <summary>The types declared (outermost) in <paramref name="tree"/>.</summary>
        public IReadOnlyList<INamedTypeSymbol> DeclaredIn(SyntaxTree tree) => _declared.TryGetValue(tree, out var types) ? types : [];

        public static TypeGraph Of(CSharpCompilation compilation, IReadOnlyList<SyntaxTree> trees)
        {
            var found = new ConcurrentBag<(SyntaxTree Tree, List<INamedTypeSymbol> Declared, List<(INamedTypeSymbol User, INamedTypeSymbol Used)> Uses)>();
            Parallel.ForEach(trees, tree =>
            {
                var model = compilation.GetSemanticModel(tree);
                var root = tree.GetRoot();
                var declared = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
                    .Where(d => !d.Ancestors().OfType<BaseTypeDeclarationSyntax>().Any())
                    .Select(d => model.GetDeclaredSymbol(d)).OfType<INamedTypeSymbol>().ToList();
                var uses = new List<(INamedTypeSymbol, INamedTypeSymbol)>();
                foreach (var name in root.DescendantNodes().OfType<SimpleNameSyntax>())
                {
                    if (name.Ancestors().OfType<BaseTypeDeclarationSyntax>().LastOrDefault() is not { } declaration ||
                        model.GetDeclaredSymbol(declaration) is not { } user || Used(model.GetSymbolInfo(name).Symbol) is not { } used ||
                        !used.Locations.Any(l => l.IsInSource))
                        continue;
                    uses.Add((user, Outermost(used)));
                }

                found.Add((tree, declared, uses));
            });

            var graph = new Dictionary<INamedTypeSymbol, HashSet<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
            var byTree = new Dictionary<SyntaxTree, List<INamedTypeSymbol>>();
            foreach (var (tree, declared, uses) in found)
            {
                byTree[tree] = declared;
                foreach (var type in declared)
                    graph.TryAdd(type, new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default));
                foreach (var (user, used) in uses)
                {
                    if (!graph.TryGetValue(user, out var set))
                        graph[user] = set = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
                    if (!SymbolEqualityComparer.Default.Equals(user, used))
                        set.Add(used);
                }
            }

            return new TypeGraph(compilation, trees, graph, byTree);
        }

        /// <summary>
        /// Each way a type <paramref name="from"/> takes reaches one <paramref name="to"/> takes, following uses through
        /// every other type on the way: "A -> B -> C", the shortest way from each such type to each it reaches.
        /// </summary>
        public List<string> Reaching(Func<INamedTypeSymbol, bool> from, Func<INamedTypeSymbol, bool> to)
        {
            var ways = new List<string>();
            foreach (var start in _uses.Keys.Where(from))
            {
                var cameFrom = new Dictionary<INamedTypeSymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default);
                var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default) { start };
                var queue = new Queue<INamedTypeSymbol>([start]);
                while (queue.TryDequeue(out var type))
                {
                    if (!_uses.TryGetValue(type, out var used))
                        continue;
                    foreach (var next in used)
                    {
                        if (!seen.Add(next))
                            continue;
                        cameFrom[next] = type;
                        if (!to(next))
                        {
                            queue.Enqueue(next);
                            continue;
                        }

                        var way = new List<string> { Name(next) };
                        for (var at = type; !SymbolEqualityComparer.Default.Equals(at, start); at = cameFrom[at])
                            way.Add(Name(at));
                        way.Add(Name(start));
                        way.Reverse();
                        ways.Add(string.Join(" -> ", way));
                    }
                }
            }

            return [.. ways.Distinct().Order()];
        }

        private static INamedTypeSymbol Outermost(INamedTypeSymbol type)
        {
            while (type.ContainingType is { } outer)
                type = outer;
            return type.OriginalDefinition;
        }
    }

    /// <summary>The core's source (src/Griff), parsed and bound: for tests about what its code uses.</summary>
    internal static (CSharpCompilation Compilation, List<SyntaxTree> Trees) Core() => CoreSource.Value;

    private static readonly Lazy<(CSharpCompilation Compilation, List<SyntaxTree> Trees)> CoreSource = new(Bind);

    private static (CSharpCompilation Compilation, List<SyntaxTree> Trees) Bind()
    {
        var trees = Parse(Path.Combine(RepoRoot(), "src", "Griff")).Append(CSharpSyntaxTree.ParseText(ImplicitUsings)).ToList();
        var compilation = CSharpCompilation.Create("Griff", trees, PlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        // Anything unbound would hide a use; the core needs nothing beyond the platform (and NVorbis, the OGG decoder every
        // host shares).
        var unbound = compilation.GetDiagnostics().Where(d => d.Id is "CS0246" or "CS0234" or "CS0103").Take(5).ToList();
        Assert.True(unbound.Count == 0, string.Join("\n", unbound));
        return (compilation, trees);
    }

    /// <summary>A project's source files (leaving out its build output), parsed.</summary>
    internal static IEnumerable<SyntaxTree> Parse(string project) =>
        Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(project, path))
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest), path));

    // The type a name refers to: the type itself, or the type that declares the member.
    internal static INamedTypeSymbol? Used(ISymbol? symbol) => symbol switch
    {
        INamedTypeSymbol type => type.OriginalDefinition,
        IMethodSymbol { ReducedFrom: { } extension } => extension.ContainingType,
        { ContainingType: { } type } => type.OriginalDefinition,
        _ => null,
    };

    internal static bool InLayer(ISymbol symbol, string layer)
    {
        var ns = symbol.ContainingNamespace.ToDisplayString();
        return ns == layer || ns.StartsWith(layer + ".", StringComparison.Ordinal);
    }

    internal static string Name(ISymbol symbol) => symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.None));

    private static bool IsBuildOutput(string project, string path)
    {
        var relative = Path.GetRelativePath(project, path);
        return relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    // What the SDK's ImplicitUsings adds for a plain library (Griff.csproj).
    internal const string ImplicitUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Threading;
        global using System.Threading.Tasks;
        """;

    internal static IEnumerable<MetadataReference> PlatformReferences() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Where(path => Path.GetFileName(path).StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
                       Path.GetFileName(path) is "mscorlib.dll" or "netstandard.dll" or "NVorbis.dll")
        .Select(path => MetadataReference.CreateFromFile(path));

    internal static string RepoRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "griff.slnx")))
            folder = folder.Parent;
        return folder?.FullName ?? throw new DirectoryNotFoundException("The tests run from inside the Griff repository.");
    }
}
