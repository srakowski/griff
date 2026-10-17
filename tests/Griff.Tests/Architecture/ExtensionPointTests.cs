using Griff.Scenes;
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
    [InlineData("Runtime/Views.cs", new[] { "Transform", "Camera", "Sprite", "Light", "Viewport", "MinigameRoot", "SortGroup" })]
    [InlineData("Runtime/FrameDrawing.cs", new[] { "Viewport" })]
    [InlineData("Runtime/Net/NetSchema.cs", new[] { "Transform", "MissingComponent", "Networked" })]
    [InlineData("Runtime/TriggerSystem.cs", new[] { "Trigger", "MessageScope" })]
    [InlineData("Scenes/ComponentActions.cs", new string[0])]
    [InlineData("Scenes/ActionNames.cs", new string[0])]
    [InlineData("Scenes/ComponentOutputs.cs", new string[0])]
    [InlineData("Scenes/OutputReader.cs", new string[0])]
    [InlineData("Scenes/Readout.cs", new string[0])]
    [InlineData("Scenes/OutputNames.cs", new string[0])]

    [InlineData("Scenes/Teams.cs", new[] { "Team" })]
    [InlineData("Scenes/Items.cs", new string[0])]
    [InlineData("Scenes/Tilemap.Grid.cs", new[] { "Tilemap", "Transform" })]

    // A wire reads any output by name; only the wiring parts' own inputs are named.
    [InlineData("Modules/Contraptions/WirePorts.cs", new[] { "WireGate", "WireLamp", "Switch", "WireDisplay", "Light" })]
    public void Core_NamesNoComponentButItsOwn(string file, string[] allowed)
    {
        var (compilation, trees) = LayerTests.Core();
        var path = Path.GetFullPath(Path.Combine(LayerTests.RepoRoot(), "src", file.StartsWith("Modules/", StringComparison.Ordinal) ? "" : "Griff", file));
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
        var modules = LayerTests.ModulesFolder() + Path.DirectorySeparatorChar;
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

    // An extension point of the core serves any component, so a script's does what a genre's does: each is public, says so in
    // its documentation, and has at least two users among Griff's components and the tests' own.
    [Theory]
    [InlineData("Griff.Scenes.IHearsMessages")]
    [InlineData("Griff.Scenes.ILightSource")]
    [InlineData("Griff.Scenes.INetSecret")]
    [InlineData("Griff.Scenes.IDialogueStarter")]
    [InlineData("Griff.Scenes.IDrawsTilemap")]
    [InlineData("Griff.Scenes.ClaimsKeysAttribute")]
    public void ExtensionPoints_ServeAnyComponent(string name)
    {
        var (compilation, _) = LayerTests.Core();
        var symbol = compilation.GetTypeByMetadataName(name)!;
        Assert.Equal(Accessibility.Public, symbol.DeclaredAccessibility);
        Assert.Contains("Any component", symbol.GetDocumentationCommentXml());

        var type = typeof(Component).Assembly.GetType(name)!;
        var users = ModuleAssemblies.All.Prepend(typeof(Component).Assembly).Append(typeof(ExtensionPointTests).Assembly).SelectMany(a => a.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract && typeof(Component).IsAssignableFrom(t) &&
                (type.IsInterface ? type.IsAssignableFrom(t) : t.GetCustomAttributes(type, inherit: true).Length > 0))
            .Select(t => t.Name)
            .ToList();
        Assert.True(users.Count >= 2, $"{name} is used by {string.Join(", ", users)}: one genre's extension point, not a general one");
    }

    // A counter counts a message, an item or an output: what a genre's components give is theirs to name.
    [Fact]
    public void ACounter_KnowsNoGenre()
    {
        var core = typeof(Component).Assembly;
        Assert.DoesNotContain(typeof(Counter).GetProperties(), p => p.Name.StartsWith("Count", StringComparison.Ordinal));
        Assert.Null(core.GetType("Griff.Scenes.ICounterSource"));
    }

    // The architecture guide says what is core and what is a module.
    [Fact]
    public void TheGuide_SaysWhatIsCoreAndWhatIsAModule()
    {
        var guide = File.ReadAllText(Path.Combine(LayerTests.RepoRoot(), "site", "src", "content", "docs", "docs", "architecture.md"));
        Assert.Contains("What is core and what is a module", guide);
        Assert.Contains("foundations several genres build on", guide);
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
