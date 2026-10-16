using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Griff.Tests.Architecture;

/// <summary>
/// Guards the core's extension points: the renderer and the network schema know no genre by name. A component draws
/// itself (IDrawable, IDrawEffect, ILightSource) and says what it sends (NetLocal, PlayerMovement, Sent, NotSent,
/// INetState), and a game's systems draw what the game knows of (GameSystem.Draw), so script components draw and play
/// online the same way Griff's do. Only the components that make up a view (cameras and their viewports, what's pinned
/// to the screen, parallax, sprites, lights) and an entity's networking settings may be named here. Likewise triggers, wires and
/// switches know nothing of what they set off: a component names its actions ([Action]) and what a switch powers
/// ([Powered]), so the code that finds and does them names no component but the trigger and scope it runs. And counters,
/// bars, texts and wires know nothing of what they read: a component names its outputs ([Output]), so the code that
/// finds and reads them names none. The ideas every genre shares know no genre either: a team is whatever a component
/// says it's on (ITeamMember), items are whatever holds them (IHoldsItems), so the code that finds a team or pays a cost
/// names no component but the core's own Team.
/// </summary>
public sealed class ExtensionPointTests
{
    [Theory]
    [InlineData("Rendering/SceneRenderer.cs", new[] { "Transform", "Camera", "ScreenAnchor", "Parallax", "Sprite", "Light", "ScreenEffects", "Viewport", "MinigameRoot" })]
    [InlineData("Runtime/Views.cs", new[] { "Transform", "Camera", "Sprite", "Light", "Viewport", "MinigameRoot" })]
    [InlineData("Runtime/FrameDrawing.cs", new[] { "Viewport" })]
    [InlineData("Runtime/Net/NetSchema.cs", new[] { "Transform", "MissingComponent", "Networked" })]
    [InlineData("Runtime/TriggerSystem.cs", new[] { "Trigger", "MessageScope" })]
    [InlineData("Scenes/ComponentActions.cs", new string[0])]
    [InlineData("Editing/ActionNames.cs", new string[0])]
    [InlineData("Scenes/ComponentOutputs.cs", new string[0])]
    [InlineData("Scenes/OutputReader.cs", new string[0])]
    [InlineData("Scenes/Readout.cs", new string[0])]
    [InlineData("Editing/OutputNames.cs", new string[0])]

    [InlineData("Scenes/Teams.cs", new[] { "Team" })]
    [InlineData("Scenes/Items.cs", new string[0])]
    [InlineData("Scenes/Tilemap.Grid.cs", new[] { "Tilemap", "Transform" })]

    // A wire reads any output by name; only the wiring parts' own inputs are named.
    [InlineData("Modules/Contraptions/WirePorts.cs", new[] { "WireGate", "WireLamp", "Switch", "WireDisplay", "Light" })]
    public void Core_NamesNoComponentButItsOwn(string file, string[] allowed)
    {
        var (compilation, trees) = LayerTests.Core();
        var path = Path.GetFullPath(Path.Combine(LayerTests.RepoRoot(), "src", "Griff", file));
        var tree = trees.Single(t => t.FilePath.Length > 0 && string.Equals(Path.GetFullPath(t.FilePath), path, StringComparison.OrdinalIgnoreCase));
        var model = compilation.GetSemanticModel(tree);
        var component = compilation.GetTypeByMetadataName("Griff.Scenes.Component")!;

        var named = tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>()
            .Select(n => LayerTests.Used(model.GetSymbolInfo(n).Symbol))
            .OfType<INamedTypeSymbol>()
            .Where(t => !SymbolEqualityComparer.Default.Equals(t, component) && DerivesFrom(t, component))
            .Select(t => t.Name)
            .Distinct()
            .Where(name => !allowed.Contains(name))
            .Order()
            .ToList();

        Assert.True(named.Count == 0,
            $"{file} names components it should reach through an extension point (IDrawable, IDrawEffect, ILightSource, " +
            "GameSystem.Draw, the network attributes or INetState):\n" + string.Join("\n", named));
    }

    // Every one of the core's systems (each GameSystem outside the modules), and Game itself, knows no genre: none names a
    // component a module declares. A genre's work reaches the core through the extension points above, or is the module's.
    [Fact]
    public void CoreSystems_NameNoModulesComponent()
    {
        var (compilation, trees) = LayerTests.Core();
        var component = compilation.GetTypeByMetadataName("Griff.Scenes.Component")!;
        var system = compilation.GetTypeByMetadataName("Griff.Runtime.GameSystem")!;
        var game = compilation.GetTypeByMetadataName("Griff.Runtime.Game")!;
        var modules = Path.Combine(LayerTests.RepoRoot(), "src", "Griff", "Modules") + Path.DirectorySeparatorChar;
        bool InModules(ISymbol symbol) =>
            symbol.Locations.Any(l => l.SourceTree?.FilePath is { Length: > 0 } path && Path.GetFullPath(path).StartsWith(modules, StringComparison.OrdinalIgnoreCase));

        var named = new List<string>();
        var checkedSystems = 0;
        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            var core = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Select(c => model.GetDeclaredSymbol(c))
                .OfType<INamedTypeSymbol>()
                .Where(t => !InModules(t) && (SymbolEqualityComparer.Default.Equals(t, game) || (DerivesFrom(t, system) && !t.IsAbstract)))
                .ToList();
            if (core.Count == 0)
                continue;
            checkedSystems += core.Count(t => !SymbolEqualityComparer.Default.Equals(t, game));
            named.AddRange(tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>()
                .Select(n => LayerTests.Used(model.GetSymbolInfo(n).Symbol))
                .OfType<INamedTypeSymbol>()
                .Where(t => DerivesFrom(t, component) && InModules(t))
                .Select(t => $"{Path.GetFileName(tree.FilePath)}: {t.Name}"));
        }

        Assert.True(checkedSystems >= 36, $"Found {checkedSystems} core systems");
        Assert.True(named.Count == 0,
            "A core system names a module's component (reach it through an extension point, or move the work to the module):\n" +
            string.Join("\n", named.Distinct().Order()));
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
