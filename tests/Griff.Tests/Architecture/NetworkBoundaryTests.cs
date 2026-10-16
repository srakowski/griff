using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Griff.Tests.Architecture;

/// <summary>
/// Guards networking as a service. The network session (every part of NetSession) reaches the game only through the
/// network system that owns it, which asks the systems that own each part (the scene, spawning, messages, the scene the
/// host loads): it names neither the Game nor any other system. And a module asks the game's INetwork, whose questions
/// all have an answer offline, rather than reaching into a session that's there only while the game is networked.
/// </summary>
public sealed class NetworkBoundaryTests
{
    [Fact]
    public void Session_ReachesTheGameOnlyThroughTheNetworkSystem()
    {
        var (compilation, trees) = LayerTests.Core();
        var session = compilation.GetTypeByMetadataName("Griff.Runtime.NetSession")!;
        var game = compilation.GetTypeByMetadataName("Griff.Runtime.Game")!;
        var system = compilation.GetTypeByMetadataName("Griff.Runtime.GameSystem")!;
        var network = compilation.GetTypeByMetadataName("Griff.Runtime.NetworkSystem")!;
        var declaring = session.DeclaringSyntaxReferences.Select(r => r.SyntaxTree).Distinct().ToList();

        var named = Named(compilation, trees.Where(declaring.Contains))
            .Where(t => SymbolEqualityComparer.Default.Equals(t.Type, game) ||
                        (DerivesFrom(t.Type, system) && !SymbolEqualityComparer.Default.Equals(t.Type, network)))
            .Select(t => $"{t.File}: {t.Type.Name}")
            .Distinct()
            .Order()
            .ToList();

        Assert.True(declaring.Count >= 3, $"Found {declaring.Count} files of NetSession");
        Assert.True(named.Count == 0,
            "The network session reaches into the game (ask the network system, which asks the system that owns it):\n" +
            string.Join("\n", named));
    }

    [Fact]
    public void Modules_AskTheNetworkService_NotTheSession()
    {
        var (compilation, trees) = LayerTests.Core();
        var session = compilation.GetTypeByMetadataName("Griff.Runtime.NetSession")!;
        var modules = trees.Where(t => t.FilePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains("Modules")).ToList();

        var named = Named(compilation, modules)
            .Where(t => SymbolEqualityComparer.Default.Equals(t.Type, session))
            .Select(t => $"{t.File}: {t.Name}")
            .Distinct()
            .Order()
            .ToList();

        Assert.True(modules.Count >= 50, $"Found {modules.Count} files of modules");
        Assert.True(named.Count == 0,
            "A module reaches into the network session (ask Game.Network, the INetwork service, which answers offline too):\n" +
            string.Join("\n", named));
    }

    private static IEnumerable<(string File, string Name, INamedTypeSymbol Type)> Named(Compilation compilation, IEnumerable<SyntaxTree> trees)
    {
        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var name in tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
            {
                if (LayerTests.Used(model.GetSymbolInfo(name).Symbol) is INamedTypeSymbol type)
                    yield return (Path.GetFileName(tree.FilePath), name.Identifier.Text, type);
            }
        }
    }

    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var t = type.BaseType; t is not null; t = t.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(t, baseType))
                return true;
        }

        return false;
    }
}
