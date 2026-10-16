using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Griff.Tests.Architecture;

/// <summary>
/// Guards the core's namespaces against cycles, so each can become an assembly of its own: if one namespace uses another,
/// however far round, the other never uses it back. Each type's every use counts (a field, a call, a base class, a
/// <c>new</c>), and a namespace counts with those inside it (Griff.Scenes.Serialization is part of the scenes,
/// Griff.Editing.Presets of editing), but for Griff's own types (Color, Rand) and each module, which stand apart. So the
/// tables (Griff.Data) sit below the scenes and the simulations that run games (Griff.Simulations) above the runtime; the
/// project and its files (Griff.Projects) below the runtime. The editor's parts (Griff.Editing and its templates, the
/// agent surface) and each module are assemblies of their own above these, which the compiler keeps (ModuleTests).
/// </summary>
public sealed class NamespaceTests
{
    [Fact]
    public void Namespaces_FormNoCycles()
    {
        var cycles = Cycles(LayerTests.Graph());

        Assert.True(cycles.Count == 0,
            "These namespaces use each other, so they couldn't be split into assemblies (move the type to the namespace below, " +
            "or ask for what it needs through an interface the lower one declares):\n" + string.Join("\n\n", cycles));
    }

    // The core has the namespaces the cycles were found between, so the test looks at the real thing.
    [Fact]
    public void Graph_HasTheCoresNamespaces()
    {
        var areas = LayerTests.Graph().Uses.Select(u => Area(u.User)).ToHashSet();

        Assert.Superset(new HashSet<string> { "Griff", "Griff.Data", "Griff.Projects", "Griff.Runtime", "Griff.Scenes",
            "Griff.Simulations", "Griff.Modules.Fighting" }, areas);
    }

    // A cycle is caught however many namespaces it goes through, and named by the types that close it.
    [Fact]
    public void Cycles_AreFoundThroughOtherNamespaces()
    {
        var tree = CSharpSyntaxTree.ParseText("""
            namespace Griff.Scenes { public sealed class Health { public Griff.Data.Table? Stats; } }
            namespace Griff.Data { public sealed class Table { public void Run() => Griff.Runtime.Game.Step(); } }
            namespace Griff.Runtime { public static class Game { public static void Step() => new Griff.Scenes.Health(); } }
            namespace Griff.Scenes.Serialization { public sealed class Codec { public Griff.Scenes.Health? Of; } }
            namespace Griff.Scenes { public sealed class Model { public Griff.Scenes.Serialization.Codec? Codec; } }
            namespace Griff.Rendering { public sealed class Renderer { public Griff.Runtime.Game? Game; } }
            """);
        var graph = LayerTests.TypeGraph.Of(CSharpCompilation.Create("Cycle", [tree], LayerTests.PlatformReferences()), [tree]);

        var cycle = Assert.Single(Cycles(graph));
        Assert.Equal("""
            Griff.Data, Griff.Runtime, Griff.Scenes:
              Griff.Data -> Griff.Runtime: Griff.Data.Table uses Griff.Runtime.Game
              Griff.Runtime -> Griff.Scenes: Griff.Runtime.Game uses Griff.Scenes.Health
              Griff.Scenes -> Griff.Data: Griff.Scenes.Health uses Griff.Data.Table
            """.ReplaceLineEndings("\n"), cycle);
    }

    // Each group of namespaces that reach each other, with the uses between them.
    private static List<string> Cycles(LayerTests.TypeGraph graph)
    {
        var edges = new Dictionary<string, Dictionary<string, SortedSet<string>>>();
        foreach (var (user, used) in graph.Uses)
        {
            var from = Area(user);
            if (!edges.TryGetValue(from, out var targets))
                edges[from] = targets = [];
            foreach (var type in used)
            {
                var to = Area(type);
                if (to == from)
                    continue;
                if (!targets.TryGetValue(to, out var ways))
                    targets[to] = ways = new SortedSet<string>(StringComparer.Ordinal);
                ways.Add($"{LayerTests.Name(user)} uses {LayerTests.Name(type)}");
            }
        }

        return [.. Components(edges).Where(c => c.Count > 1).Select(group =>
        {
            var lines = group.Order(StringComparer.Ordinal).SelectMany(from => edges[from]
                .Where(e => group.Contains(e.Key))
                .OrderBy(e => e.Key, StringComparer.Ordinal)
                .Select(e => $"  {from} -> {e.Key}: {string.Join(", ", e.Value.Take(5))}{(e.Value.Count > 5 ? $" (and {e.Value.Count - 5} more)" : "")}"));
            return $"{string.Join(", ", group.Order(StringComparer.Ordinal))}:\n{string.Join("\n", lines)}";
        }).Order(StringComparer.Ordinal)];
    }

    // The strongly connected parts of the graph (Tarjan's): namespaces that each reach all the others.
    private static List<HashSet<string>> Components(Dictionary<string, Dictionary<string, SortedSet<string>>> edges)
    {
        var index = new Dictionary<string, int>();
        var low = new Dictionary<string, int>();
        var stack = new Stack<string>();
        var onStack = new HashSet<string>();
        var found = new List<HashSet<string>>();

        foreach (var node in edges.Keys.Order(StringComparer.Ordinal))
        {
            if (!index.ContainsKey(node))
                Visit(node);
        }

        return found;

        void Visit(string node)
        {
            index[node] = low[node] = index.Count;
            stack.Push(node);
            onStack.Add(node);
            foreach (var next in edges.TryGetValue(node, out var targets) ? targets.Keys.ToList() : [])
            {
                if (!index.TryGetValue(next, out var seen))
                {
                    Visit(next);
                    low[node] = Math.Min(low[node], low[next]);
                }
                else if (onStack.Contains(next))
                    low[node] = Math.Min(low[node], seen);
            }

            if (low[node] != index[node])
                return;
            var group = new HashSet<string>();
            string member;
            do
            {
                member = stack.Pop();
                onStack.Remove(member);
                group.Add(member);
            }
            while (member != node);
            found.Add(group);
        }
    }

    // Griff.Scenes.Serialization counts as Griff.Scenes, Griff.Modules.Fighting as itself, Griff's own types as Griff.
    private static string Area(INamedTypeSymbol type)
    {
        var parts = type.ContainingNamespace.ToDisplayString().Split('.');
        var length = parts.Length > 1 && parts[0] == "Griff" && parts[1] == "Modules" ? 3 : 2;
        return string.Join('.', parts.Take(length));
    }
}
